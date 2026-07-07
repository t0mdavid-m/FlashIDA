using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Flash;
using Flash.IDA;
using Thermo.Interfaces.FusionAccess_V1.Control.Scans;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;

namespace Flash.Tests.Mocks
{
    /// <summary>
    /// Test harness that wires up the full processor stack for continuity tests.
    /// Replicates the setup logic from Flash.cs but with mock components.
    ///
    /// Architecture decision D1: Calls processor.ProcessMS directly (bypass DataPipe).
    /// Architecture decision D2: Passes null for ScanFactory's IFusionScans parameter.
    /// </summary>
    public class ContinuityTestHarness : IDisposable
    {
        /// <summary>The mock scan factory that captures created scans</summary>
        public MockScanFactory Factory { get; }

        /// <summary>The loaded method parameters</summary>
        public MethodParameters MethodParams { get; }

        /// <summary>The scan processor (IDA, FAIMS, or Quant)</summary>
        public IScanProcessor Processor { get; }

        /// <summary>The FLASHIda wrapper (real C++ engine)</summary>
        public FLASHIdaWrapper Wrapper { get; }

        /// <summary>Whether FAIMS cycling mode is active (multiple CVs)</summary>
        public bool UseFaimsCycling { get; }

        /// <summary>Records captured directly from raw ScanCommand structs (includes scoring fields)</summary>
        public List<ScanCommandRecord> CapturedRecords { get; } = new List<ScanCommandRecord>();

        /// <summary>
        /// Create a test harness from a method XML configuration file.
        /// Replicates Flash.cs setup: creates FLASHIdaWrapper and UnifiedScanProcessor.
        /// Phase 6: C++ handles FAIMS CV cycling, no ScanScheduler needed.
        /// </summary>
        /// <param name="methodXmlPath">Absolute path to method XML configuration</param>
        /// <param name="forceFaims">Force FAIMS cycling mode regardless of CV count</param>
        /// <param name="forceQuant">Force quant processor mode</param>
        /// <param name="configure">
        /// Optional mutator applied to the loaded <see cref="MethodParameters"/> AFTER config-file
        /// load + relative-path resolution but BEFORE the FLASHIdaWrapper (C++ engine) is created.
        /// The log-golden suite uses it to inject absolute <c>Runtime.*Path</c> values so the engine
        /// writes its four log streams to a per-case temp directory. (Path resolution above only
        /// touches <c>Files.*</c>, never <c>Runtime.*</c>, so injected runtime paths are left as-is.)
        /// </param>
        public ContinuityTestHarness(string methodXmlPath, bool forceFaims = false, bool forceQuant = false,
            Action<MethodParameters> configure = null)
        {
            MethodParams = MethodParameters.Load(methodXmlPath);
            Factory = new MockScanFactory();

            double[] CVs = MethodParams.Config.Faims.CVValues;
            UseFaimsCycling = forceFaims && CVs.Length > 1;

            // Resolve relative file paths in config (relative to config directory)
            string configDir = Path.GetDirectoryName(methodXmlPath);
            ResolveRelativePath(configDir, () => MethodParams.Config.Files.InclusionList, v => MethodParams.Config.Files.InclusionList = v);
            ResolveRelativePath(configDir, () => MethodParams.Config.Files.FastaFile, v => MethodParams.Config.Files.FastaFile = v);
            ResolveRelativePath(configDir, () => MethodParams.Config.Files.PtmList, v => MethodParams.Config.Files.PtmList = v);

            // Resolve target log paths (List<string>)
            if (MethodParams.Config.Files.TargetLogs != null)
            {
                for (int i = 0; i < MethodParams.Config.Files.TargetLogs.Count; i++)
                {
                    string path = MethodParams.Config.Files.TargetLogs[i];
                    if (!string.IsNullOrEmpty(path) && !Path.IsPathRooted(path))
                    {
                        string resolved = Path.Combine(configDir, path);
                        if (File.Exists(resolved))
                        {
                            MethodParams.Config.Files.TargetLogs[i] = Path.GetFullPath(resolved);
                        }
                    }
                }
            }

            // Inject any test-specific config mutations (e.g. runtime log paths) before the
            // C++ engine is constructed, so the engine opens its log streams at the right paths.
            configure?.Invoke(MethodParams);

            // Create FLASHIdaWrapper (real C++ engine — handles FAIMS CV cycling in Phase 6+)
            Wrapper = new FLASHIdaWrapper(MethodParams);

            // Clear setup scans from capture list so only test-produced scans are tracked
            Factory.CreatedScans.Clear();

            // Phase 6: always use UnifiedScanProcessor — C++ handles FAIMS CV cycling
            Processor = new UnifiedScanProcessor(Wrapper);
        }

        /// <summary>
        /// Push a scan through the processor pipeline.
        /// Calls ProcessMS then drains commands via GetNextScanCommand.
        /// </summary>
        /// <returns>The list of custom scans produced by the C++ engine</returns>
        public List<IFusionCustomScan> PushScan(IMsScan msScan)
        {
            Processor.ProcessMS(msScan);

            var scanList = new List<IFusionCustomScan>();
            var cmd = new ScanCommand();
            while (Wrapper.GetNextScanCommand(ref cmd) == 1)
            {
                CapturedRecords.Add(ScanCommandRecord.FromScanCommand(cmd));
                scanList.Add(Factory.BuildFromCommand(cmd));
                // Idle cycle: AGC signals queue is empty — capture it then stop
                if (cmd.IsAgc == 1) break;
                cmd = new ScanCommand();
            }
            return scanList;
        }

        /// <summary>
        /// Interleaved full-acquisition drive: mirrors the real instrument round-trip far more
        /// closely than the staged PushScan helpers. Instead of pushing all MS1, then all MS2,
        /// then all MS3 in separate phases (each feeding back our OWN synthetic descriptions), this
        /// drains the engine command queue BY PRIORITY one command at a time and feeds each command
        /// back as a response scan stamped with the ENGINE-EMITTED ScanCommand.ScanDescription. That
        /// is exactly how parent/child join edges form on a real instrument: the engine's own
        /// tracking id round-trips on the "Scan Description" trailer, so an MS3's parent resolves to
        /// the MS2 scan id the engine actually emitted (not a harness-invented id).
        ///
        /// The MS1 ids are the engine's own survey-command ids: each survey MS1 command the engine emits
        /// is answered with the next TSV MS1 scan stamped with THAT command's ScanDescription, so the
        /// processScan desc_str.size() &lt; 3 guard and the MS1 gate are both cleared with genuine ids.
        /// Terminates on 3 consecutive idle ticks (AGC or an MS1 re-survey after all TSV scans are fed),
        /// exactly mirroring the C++ runFullAcquisition driver.
        /// </summary>
        /// <param name="ms1Path">TSV MS1 file; each scan is fed once, in order, one per survey command.</param>
        /// <param name="ms2Path">TSV MS2 spectrum file (fragment peaks for every MS2-level response).</param>
        /// <param name="ms3FixtureFor">Per-MS3-command fixture selector; null/empty result => skip that MS3
        /// (never fabricate). When null, no MS3 is fed.</param>
        /// <param name="maxIters">Hard upper bound on drain iterations, so idle AGC/MS1 cycling can never loop forever.</param>
        // [DRAIN-CONTRACT C#<->C++ — see docs/kb/test-harness] canonical C# driver; twin of C++ FLASHIda_TestHelpers::runInterleaved
        // [DRAIN-CONTRACT C#<->C++: this interleaved engine-id-echo drain MIRRORS the C++
        //  FLASHIda_TestHelpers.h runFullAcquisition. Keep the idle-termination (idle < 3) and per-level
        //  dispatch in lockstep with that function. See .claude/hooks/driver-sync-reminder.sh.]
        //
        // ms3FixtureFor: per-MS3-command fixture selector — decode the trailing ion from cmd.ScanDescription
        //   and look it up in the caller's manifest; return null/empty => SKIP that MS3 (never fabricate).
        // MS1 feed: each engine survey-MS1 command is answered with the NEXT TSV MS1 scan (one per command,
        //   in order — the same scan coverage the old DriveCycle had), stamped with that command's
        //   engine-emitted ScanDescription, so the scan_results MS1 tracking_id == the id the engine issued.
        //
        // maxMs2Responses (Phase 2): optional cap on the number of MS2 commands the harness RESPONDS to
        //   (feeds an MS2 spectrum back for). Once the cap is reached, further MS2 commands are still drained
        //   and recorded into CapturedRecords (so the ABI/queue is exhausted normally) but no response scan is
        //   pushed back -- exactly the bespoke `maxMS2Returns: 1` behaviour CT35/36/37 used (process at most one
        //   MS2 return so the data-dependent MS3 cascade is bounded). -1 (default) feeds every MS2 command back.
        //   C# twin of the C++ runInterleaved single_group_only bookkeeping: a knob to bound how far the
        //   MS2-return cascade runs without changing the core pull->classify->feed->idle<3 contract.
        //
        // onFirstMs2Response (Phase 2): optional mid-drive snapshot/callback fired EXACTLY ONCE, immediately
        //   BEFORE the first MS2 response is fed back to the engine. At that instant CapturedRecords holds only
        //   the commands the engine emitted from MS1 surveys (no tag-/return-triggered follow-ups yet), so a
        //   caller can snapshot the pre-return state (CT34: prove the initial batch is ETD-only, before any HCD
        //   follow-up the MS2 return triggers). The callback observes the harness; it must not drive the engine.
        //
        // ms2CeMap (Task E.2-4): optional CE-keyed MS2-spectrum source map for the MS2 exploration CE sweep. When
        //   non-null, an MS2 command's response spectrum is selected by the command's stage-0 collision energy
        //   (cmd.Stages[0].CollisionEnergy, rounded to int) — so each CE variant of the sweep gets its OWN
        //   energy-resolved fixture (large fragments strongest at low CE, small at high CE), exercising the
        //   per-fragment best-MS2 selection end-to-end. NO FALLBACK: an MS2 command whose rounded CE is not a key
        //   throws InvalidOperationException naming the CE + available keys (a silent single-spectrum fall-through
        //   would collapse the sweep and defeat the test). When null, the single ms2Path is fed for every MS2
        //   command (existing behaviour, all other modes unaffected). C# twin of the C++ runInterleaved ms2_ce_map.
        public void PushScanAndDrainFull(string ms1Path, string ms2Path,
            Func<ScanCommand, string> ms3FixtureFor = null, int maxIters = 600,
            int maxMs2Responses = -1, Action<ContinuityTestHarness> onFirstMs2Response = null,
            Dictionary<int, string> ms2CeMap = null)
        {
            // Feed each TSV MS1 scan exactly once (nMs1 = scan count); any further MS1 survey is an idle tick.
            int nMs1;
            { var probe = MockMsScan.FromTsvAllScans(ms1Path); nMs1 = probe.Count; foreach (var s in probe) s.Dispose(); }
            if (nMs1 == 0) return;

            int idle = 0, ms1Fed = 0, ms2Responded = 0;
            bool firstMs2HookFired = false;
            var cmd = new ScanCommand();
            for (int it = 0; it < maxIters && idle < 3; it++)
            {
                if (Wrapper.GetNextScanCommand(ref cmd) != 1) break;
                CapturedRecords.Add(ScanCommandRecord.FromScanCommand(cmd));
                Factory.BuildFromCommand(cmd);

                int level = cmd.MsnLevel;
                // Idle tick (mirror of C++ runFullAcquisition): an AGC, an empty-descriptor command, or an MS1
                // re-survey after we've fed all nMs1 scans. 3 consecutive idle ticks => the real queue is drained.
                if (cmd.IsAgc != 0 || string.IsNullOrEmpty(cmd.ScanDescription) || (level <= 1 && ms1Fed >= nMs1))
                {
                    idle++;
                    cmd = new ScanCommand();
                    continue;
                }
                idle = 0;

                MockMsScan response;
                if (level <= 1)
                {
                    var ms1 = MockMsScan.FromTsvAllScans(ms1Path);
                    int pick = ms1Fed;                       // feed TSV MS1 scans in order, one per survey command
                    if (pick >= ms1.Count) { foreach (var s in ms1) s.Dispose(); cmd = new ScanCommand(); continue; }
                    response = ms1[pick];
                    response.SetScanDescription(cmd.ScanDescription);   // echo the engine-emitted survey id
                    // F7: echo the command's FAIMS CV so FAIMS cycling re-binds (C++ runInterleaved passes
                    // cmd.faims_cv to processScan; here the wrapper reads it from this trailer). Only stamp a
                    // real (non-zero) CV — for non-FAIMS runs cmd.FaimsCv is 0, matching the C++ no-op and
                    // leaving the existing non-FAIMS goldens byte-identical.
                    if (cmd.FaimsCv != 0.0) response.SetFaimsCv(cmd.FaimsCv);
                    for (int i = 0; i < ms1.Count; i++) if (i != pick) ms1[i].Dispose();
                    ms1Fed++;
                }
                else if (level >= 3)
                {
                    // Per-command MS3 fixture (ion-keyed). null/empty => skip; never fabricate from MS2 peaks.
                    string src = ms3FixtureFor?.Invoke(cmd);
                    if (string.IsNullOrEmpty(src))
                    {
                        Console.WriteLine($"[MS3-SKIP] id={cmd.ScanDescription} status=no_fixture");
                        cmd = new ScanCommand();
                        continue;
                    }
                    double precMz = cmd.NumStages > 0 && cmd.Stages != null ? cmd.Stages[0].PrecursorMz : 0.0;
                    int z = cmd.NumStages > 0 && cmd.Stages != null ? cmd.Stages[0].ChargeState : 1;
                    // NOTE: MS2-exploration precursor depletion only (below). The MS3 remaining_ratio window is
                    // around the MS2 FRAGMENT (stage[last]), not stage[0], so we do not scale here -- the MS3
                    // depletion ladder (if wanted) needs the fragment-window center; deferred pending diff review.
                    response = MockMsScan.FromTsvAsMSn(src, level, cmd.ScanDescription, precMz, z);
                }
                else
                {
                    // MS2 command. Honour the response cap: once we have responded to maxMs2Responses MS2
                    // commands, keep draining/recording further MS2 commands but stop feeding spectra back
                    // (bounds the data-dependent MS2-return -> MS3 cascade exactly as maxMS2Returns:1 did).
                    if (maxMs2Responses >= 0 && ms2Responded >= maxMs2Responses)
                    {
                        cmd = new ScanCommand();
                        continue;
                    }
                    // Mid-drive snapshot: fire once, just before the FIRST MS2 response, so the caller sees the
                    // pre-return command set (no follow-ups triggered yet).
                    if (!firstMs2HookFired)
                    {
                        firstMs2HookFired = true;
                        onFirstMs2Response?.Invoke(this);
                    }
                    double precMz = cmd.NumStages > 0 && cmd.Stages != null ? cmd.Stages[0].PrecursorMz : 0.0;
                    int z = cmd.NumStages > 0 && cmd.Stages != null ? cmd.Stages[0].ChargeState : 1;
                    // CE-keyed MS2 spectrum (Task E.2-4): select the energy-resolved fixture for THIS variant's
                    // stage-0 collision energy. NO FALLBACK — an unmapped CE throws (mirrors C++ runInterleaved).
                    string ms2Src = ms2Path;
                    if (ms2CeMap != null)
                    {
                        int ce = (int)Math.Round(cmd.NumStages > 0 && cmd.Stages != null
                                                 ? cmd.Stages[0].CollisionEnergy : 0.0);
                        if (!ms2CeMap.TryGetValue(ce, out ms2Src))
                            throw new InvalidOperationException(
                                $"PushScanAndDrainFull: MS2 command collision energy {ce} has no CE-map fixture " +
                                $"(available keys: {string.Join(",", ms2CeMap.Keys.OrderBy(k => k))}).");
                    }
                    response = MockMsScan.FromTsvAsMSn(ms2Src, level, cmd.ScanDescription, precMz, z,
                                                      precursorScale: ExplorationPrecursorScale(cmd));
                    ms2Responded++;
                }

                Processor.ProcessMS(response);
                response.Dispose();
                cmd = new ScanCommand();
            }
        }

        /// <summary>
        /// remaining_ratio depletion factor for an exploration variant's precursor window. The CE-0 baseline
        /// (fragmentation dose 0) stays 1.0 (the un-fragmented reference); each real CE/RT variant depletes the
        /// surviving precursor monotonically with its fragmentation dose (collision energy for HCD, reaction time
        /// for ETD), so remaining_ratio forms a &lt; 1 ladder that decreases with fragmentation -- uniform across
        /// sweep types, no per-CE fixture files. Production (non-exploration) scans are never scaled.
        /// </summary>
        private static double ExplorationPrecursorScale(ScanCommand cmd)
        {
            if (cmd.NumStages <= 0 || cmd.Stages == null) return 1.0;
            string desc = cmd.ScanDescription ?? "";
            if (desc.Length < 4 || desc[3] != 'E') return 1.0;   // 'E' = exploration variant (id(3)+marker)
            var frag = cmd.Stages[cmd.NumStages - 1];            // the fragmentation stage (last)
            double dose = frag.CollisionEnergy + frag.ReactionTime;
            if (dose <= 0.0) return 1.0;                         // CE-0/RT-0 baseline: full surviving precursor
            return Math.Max(0.1, 1.0 - dose / 100.0);           // monotonic depletion, floored
        }

        /// <summary>
        /// Drain the engine's command queue until a real survey-MS1 command surfaces and return its
        /// engine-emitted ScanDescription (a genuine tracking id). Repeatable: each call advances the
        /// queue and returns the NEXT survey id. Behavioral feeders stamp their MS1 spectra with this so
        /// the spectra clear the processScan MS1 gate (an un-emitted id is rejected). Falls back to the
        /// mock sentinel only if the engine never surfaces an MS1 within 8 drains — the idle cycle always
        /// queues one, so the fallback is effectively unreachable (and a stamped sentinel would be rejected,
        /// failing loud rather than silently).
        /// </summary>
        public string NextSurveyMs1Description()
        {
            var cmd = new ScanCommand();
            for (int i = 0; i < 8 && Wrapper.GetNextScanCommand(ref cmd) == 1; i++)
            {
                if (cmd.IsAgc == 0 && cmd.MsnLevel <= 1 && !string.IsNullOrEmpty(cmd.ScanDescription))
                    return cmd.ScanDescription;
                cmd = new ScanCommand();
            }
            return MockMsScan.Ms1ScanDescription;
        }

        /// <summary>
        /// Feed an MS1 spectrum stamped with a real engine-emitted survey tracking id (see
        /// <see cref="NextSurveyMs1Description"/>) so it clears the processScan MS1 gate, then drain the
        /// resulting commands. Use this for every MS1 the behavioral tests push; MS2/MS3 responses keep
        /// using <see cref="PushScan"/> (they already carry the engine command's ScanDescription).
        /// </summary>
        public List<IFusionCustomScan> PushMs1(MockMsScan ms1)
        {
            ms1.SetScanDescription(NextSurveyMs1Description());
            return PushScan(ms1);
        }

        /// <summary>
        /// Collect all scan command records captured during test execution.
        /// Filters out null entries and Full-type (default/AGC) scans.
        /// </summary>
        public List<ScanCommandRecord> CollectResults()
        {
            return Factory.CreatedScans
                .Select(s => ScanCommandRecord.FromCustomScan(s))
                .Where(r => r.ScanType == "MSn") // Only MS2/MS3 scans, not defaults
                .ToList();
        }

        /// <summary>
        /// Collect ALL scan command records including AGC and default scans.
        /// </summary>
        public List<ScanCommandRecord> CollectAllResults()
        {
            return Factory.CreatedScans
                .Select(s => ScanCommandRecord.FromCustomScan(s))
                .ToList();
        }

        public void Dispose()
        {
            Wrapper?.Dispose();
        }

        private static bool IsActive(string val) =>
            val != null && val.Equals("True", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Resolve a relative file path to absolute, relative to the given base directory.
        /// </summary>
        private static void ResolveRelativePath(string baseDir, Func<string> getter, Action<string> setter)
        {
            string path = getter();
            if (!string.IsNullOrEmpty(path) && !Path.IsPathRooted(path))
            {
                string resolved = Path.Combine(baseDir, path);
                if (File.Exists(resolved))
                {
                    setter(Path.GetFullPath(resolved));
                }
            }
        }
    }
}

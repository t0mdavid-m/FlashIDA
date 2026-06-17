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
        public void PushScanAndDrainFull(string ms1Path, string ms2Path,
            Func<ScanCommand, string> ms3FixtureFor = null, int maxIters = 600)
        {
            // Feed each TSV MS1 scan exactly once (nMs1 = scan count); any further MS1 survey is an idle tick.
            int nMs1;
            { var probe = MockMsScan.FromTsvAllScans(ms1Path); nMs1 = probe.Count; foreach (var s in probe) s.Dispose(); }
            if (nMs1 == 0) return;

            int idle = 0, ms1Fed = 0;
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
                    response = MockMsScan.FromTsvAsMSn(src, level, cmd.ScanDescription, precMz, z);
                }
                else
                {
                    double precMz = cmd.NumStages > 0 && cmd.Stages != null ? cmd.Stages[0].PrecursorMz : 0.0;
                    int z = cmd.NumStages > 0 && cmd.Stages != null ? cmd.Stages[0].ChargeState : 1;
                    response = MockMsScan.FromTsvAsMSn(ms2Path, level, cmd.ScanDescription, precMz, z);
                }

                Processor.ProcessMS(response);
                response.Dispose();
                cmd = new ScanCommand();
            }
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

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
        /// Bootstrap: the engine's idle cycle (FLASHIda::getNextScanCommand step 5) emits an AGC
        /// command immediately and pushes an idle MS1 (priority 3) for the next dequeue. We harvest
        /// that idle MS1's description to stamp the first real MS1 spectrum, clearing the
        /// processScan desc_str.size() < 3 guard with a genuine engine id.
        /// </summary>
        /// <param name="ms1Path">TSV MS1 spectrum file (peaks reused for every MS1-level response).</param>
        /// <param name="ms2Path">TSV MS2 spectrum file (fragment peaks for every MS2-level response).</param>
        /// <param name="ms3Path">Optional TSV MS3 spectrum file; falls back to <paramref name="ms2Path"/> peaks when null.</param>
        /// <param name="maxScans">Hard upper bound on response scans fed back, so idle AGC/MS1
        /// cycling can never loop forever.</param>
        public void PushScanAndDrainFull(string ms1Path, string ms2Path, string ms3Path = null, int maxScans = 200)
        {
            string ms3File = ms3Path ?? ms2Path;

            // Bootstrap: harvest the engine's idle-MS1 description (step-5 fallback). The first
            // GetNextScanCommand returns an AGC and queues an idle MS1; the second returns that MS1.
            string ms1Desc = BootstrapMs1Description();

            // Queue of pending engine commands to respond to, seeded with the bootstrap MS1.
            var pending = new Queue<ScanCommand>();

            // Push the first real MS1 stamped with the engine's idle-MS1 description.
            // ms1[1] = scan 134 carries the cytC envelope; ms1[0] = scan 132 is a weak edge scan from
            // which the engine correctly selects 0 precursors (=> 0 MS2). Bootstrap from the strong scan.
            var firstMs1 = MockMsScan.FromTsvAllScans(ms1Path);
            if (firstMs1.Count < 2) return;
            firstMs1[1].SetScanDescription(ms1Desc);
            EnqueueDrained(Processor, firstMs1[1], pending);
            for (int i = 0; i < firstMs1.Count; i++) firstMs1[i].Dispose();

            int fed = 0;
            while (pending.Count > 0 && fed < maxScans)
            {
                var cmd = pending.Dequeue();

                // AGC commands carry no payload (engine resolves them and returns 0); skip feeding.
                if (cmd.IsAgc != 0) continue;
                if (string.IsNullOrEmpty(cmd.ScanDescription)) continue;

                int level = cmd.MsnLevel;
                MockMsScan response;
                if (level <= 1)
                {
                    var ms1 = MockMsScan.FromTsvAllScans(ms1Path);
                    if (ms1.Count < 2) continue;
                    response = ms1[1];  // strong cytC MS1 (scan 134), not the weak scan 132 at [0]
                    response.SetScanDescription(cmd.ScanDescription);
                    ms1[0].Dispose();
                    for (int i = 2; i < ms1.Count; i++) ms1[i].Dispose();
                }
                else
                {
                    string src = level >= 3 ? ms3File : ms2Path;
                    double precMz = cmd.NumStages > 0 && cmd.Stages != null ? cmd.Stages[0].PrecursorMz : 0.0;
                    int z = cmd.NumStages > 0 && cmd.Stages != null ? cmd.Stages[0].ChargeState : 1;
                    response = MockMsScan.FromTsvAsMSn(src, level, cmd.ScanDescription, precMz, z);
                }

                EnqueueDrained(Processor, response, pending);
                response.Dispose();
                fed++;
            }
        }

        /// <summary>
        /// Drain the engine's idle cycle to obtain a real MS1 tracking-id description. Returns the
        /// first MS1-level (non-AGC) idle command's ScanDescription, or the hardcoded mock default
        /// if the engine never surfaces one (defensive — the idle cycle always queues an MS1).
        /// </summary>
        private string BootstrapMs1Description()
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
        /// Push one response scan through the processor, then drain every command the engine emits
        /// in response, capturing each (records + factory scan) and enqueueing it for feed-back.
        /// Mirrors PushScan's drain semantics but does NOT stop at the first AGC — full acquisition
        /// keeps consuming the priority queue.
        /// </summary>
        private void EnqueueDrained(IScanProcessor processor, IMsScan scan, Queue<ScanCommand> pending)
        {
            processor.ProcessMS(scan);

            var cmd = new ScanCommand();
            int agcSeen = 0;
            while (Wrapper.GetNextScanCommand(ref cmd) == 1)
            {
                CapturedRecords.Add(ScanCommandRecord.FromScanCommand(cmd));
                Factory.BuildFromCommand(cmd);
                pending.Enqueue(cmd);

                // An AGC means the queue drained to the idle fallback. Allow one idle MS1 to follow
                // (it is queued at priority 3 by step 5b) then stop, so we don't spin on idle cycling.
                if (cmd.IsAgc != 0) { if (++agcSeen >= 1) break; }
                cmd = new ScanCommand();
            }
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

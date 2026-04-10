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
        public ContinuityTestHarness(string methodXmlPath, bool forceFaims = false, bool forceQuant = false)
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

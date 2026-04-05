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

        /// <summary>The scan scheduler</summary>
        public ScanScheduler Scheduler { get; }

        /// <summary>Whether FAIMS cycling mode is active (multiple CVs)</summary>
        public bool UseFaimsCycling { get; }

        /// <summary>Records captured directly from raw ScanCommand structs (includes scoring fields)</summary>
        public List<ScanCommandRecord> CapturedRecords { get; } = new List<ScanCommandRecord>();

        /// <summary>
        /// Create a test harness from a method XML configuration file.
        /// Replicates Flash.cs setup: creates default/AGC scans, ScanScheduler,
        /// FLASHIdaWrapper, and the appropriate processor.
        /// </summary>
        /// <param name="methodXmlPath">Absolute path to method XML configuration</param>
        /// <param name="forceFaims">Force FAIMS cycling mode regardless of CV count</param>
        /// <param name="forceQuant">Force quant processor mode</param>
        public ContinuityTestHarness(string methodXmlPath, bool forceFaims = false, bool forceQuant = false)
        {
            MethodParams = MethodParameters.Load(methodXmlPath);
            Factory = new MockScanFactory();

            double[] CVs = MethodParams.IDA.CVValues;
            // FAIMS mode is only active when forceFaims is set (simulates instrument detection).
            // In production, useFAIMS comes from instrument hardware detection (Flash.cs line 239).
            // The default CVValues=[-50] does NOT mean FAIMS is enabled — it's just a default.
            UseFaimsCycling = forceFaims && CVs.Length > 1;
            bool useStaticFaims = forceFaims && CVs.Length == 1;
            double? staticFaimsCV = useStaticFaims ? CVs[0] : (double?)null;

            // Create default and AGC scans (replicates Flash.cs lines 300-383)
            IFusionCustomScan agcScan = Factory.CreateFusionCustomScan(
                new ScanParameters
                {
                    Analyzer = "IonTrap",
                    FirstMass = new double[] { MethodParams.MS1.FirstMass },
                    LastMass = new double[] { MethodParams.MS1.LastMass },
                    ScanRate = "Turbo",
                    AGCTarget = 30000,
                    MaxIT = 1,
                    Microscans = 1,
                    SrcRFLens = new double[] { MethodParams.MS1.RFLens },
                    SourceCIDEnergy = MethodParams.MS1.SourceCID,
                    SourceCIDScalingFactor = MethodParams.MS1.SourceCIDScaling,
                    DataType = "Profile",
                    ScanType = "Full",
                    FAIMS_CV = staticFaimsCV,
                    FAIMS_Voltages = useStaticFaims ? "on" : "off"
                }, id: 41, IsAGC: true, delay: 3);

            IFusionCustomScan defaultScan = Factory.CreateFusionCustomScan(
                new ScanParameters
                {
                    Analyzer = MethodParams.MS1.Analyzer,
                    FirstMass = new double[] { MethodParams.MS1.FirstMass },
                    LastMass = new double[] { MethodParams.MS1.LastMass },
                    OrbitrapResolution = MethodParams.MS1.OrbitrapResolution,
                    AGCTarget = MethodParams.MS1.AGCTarget,
                    MaxIT = MethodParams.MS1.MaxIT,
                    Microscans = MethodParams.MS1.Microscans,
                    SrcRFLens = new double[] { MethodParams.MS1.RFLens },
                    SourceCIDEnergy = MethodParams.MS1.SourceCID,
                    SourceCIDScalingFactor = MethodParams.MS1.SourceCIDScaling,
                    DataType = MethodParams.MS1.DataType,
                    ScanType = "Full",
                    FAIMS_CV = staticFaimsCV,
                    FAIMS_Voltages = useStaticFaims ? "on" : "off",
                    ScanRangeMode = "DefineMZRange",
                }, delay: 3);

            // Create FAIMS per-CV scans
            IFusionCustomScan[] faimsAgcScans = new IFusionCustomScan[CVs.Length];
            IFusionCustomScan[] faimsDefaultScans = new IFusionCustomScan[CVs.Length];
            Dictionary<double, int> faimsPAGCGroups = new Dictionary<double, int>();

            for (int i = 0; i < CVs.Length; i++)
            {
                faimsAgcScans[i] = Factory.CreateFusionCustomScan(
                    new ScanParameters
                    {
                        Analyzer = "IonTrap",
                        FirstMass = new double[] { MethodParams.MS1.FirstMass },
                        LastMass = new double[] { MethodParams.MS1.LastMass },
                        ScanRate = "Turbo",
                        AGCTarget = 30000,
                        MaxIT = 1,
                        Microscans = 1,
                        SrcRFLens = new double[] { MethodParams.MS1.RFLens },
                        SourceCIDEnergy = MethodParams.MS1.SourceCID,
                        SourceCIDScalingFactor = MethodParams.MS1.SourceCIDScaling,
                        DataType = "Profile",
                        ScanType = "Full",
                        FAIMS_CV = CVs[i],
                        FAIMS_Voltages = "on",
                    }, id: 41, IsAGC: true, delay: 3, AGCgroup: i + 1);

                faimsDefaultScans[i] = Factory.CreateFusionCustomScan(
                    new ScanParameters
                    {
                        Analyzer = MethodParams.MS1.Analyzer,
                        FirstMass = new double[] { MethodParams.MS1.FirstMass },
                        LastMass = new double[] { MethodParams.MS1.LastMass },
                        OrbitrapResolution = MethodParams.MS1.OrbitrapResolution,
                        AGCTarget = MethodParams.MS1.AGCTarget,
                        MaxIT = MethodParams.MS1.MaxIT,
                        Microscans = MethodParams.MS1.Microscans,
                        SrcRFLens = new double[] { MethodParams.MS1.RFLens },
                        SourceCIDEnergy = MethodParams.MS1.SourceCID,
                        SourceCIDScalingFactor = MethodParams.MS1.SourceCIDScaling,
                        DataType = MethodParams.MS1.DataType,
                        ScanType = "Full",
                        FAIMS_CV = CVs[i],
                        FAIMS_Voltages = "on",
                        ScanRangeMode = "DefineMZRange",
                    }, delay: 3, AGCgroup: i + 1);

                faimsPAGCGroups[CVs[i]] = i + 1;
            }

            // Create ScanScheduler
            Scheduler = new ScanScheduler(defaultScan, agcScan, faimsDefaultScans,
                faimsAgcScans, faimsPAGCGroups, MethodParams, UseFaimsCycling);

            // Resolve relative file paths in IDA parameters (relative to config directory)
            string configDir = Path.GetDirectoryName(methodXmlPath);
            ResolveRelativePath(configDir, () => MethodParams.IDA.InclusionList, v => MethodParams.IDA.InclusionList = v);
            ResolveRelativePath(configDir, () => MethodParams.IDA.FastaFile, v => MethodParams.IDA.FastaFile = v);
            ResolveRelativePath(configDir, () => MethodParams.IDA.PtmList, v => MethodParams.IDA.PtmList = v);

            // Resolve target log paths (List<string>)
            if (MethodParams.IDA.TargetLogs != null)
            {
                for (int i = 0; i < MethodParams.IDA.TargetLogs.Count; i++)
                {
                    string path = MethodParams.IDA.TargetLogs[i];
                    if (!string.IsNullOrEmpty(path) && !Path.IsPathRooted(path))
                    {
                        string resolved = Path.Combine(configDir, path);
                        if (File.Exists(resolved))
                        {
                            MethodParams.IDA.TargetLogs[i] = Path.GetFullPath(resolved);
                        }
                    }
                }
            }

            // Create FLASHIdaWrapper (real C++ engine)
            Wrapper = new FLASHIdaWrapper(MethodParams);

            // Clear setup scans from capture list so only test-produced scans are tracked
            Factory.CreatedScans.Clear();

            // Create the appropriate processor
            var baseProcessor = new UnifiedScanProcessor(Wrapper);
            if (UseFaimsCycling)
                Processor = new FAIMSScanProcessor(MethodParams, Scheduler, baseProcessor, Wrapper);
            else
                Processor = baseProcessor;
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

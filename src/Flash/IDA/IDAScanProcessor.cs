using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Thermo.Interfaces.FusionAccess_V1.Control.Scans;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;
using log4net;

namespace Flash.IDA
{
    /// <summary>
    /// FLASHIda-enabled scan processor
    /// </summary>
    public class IDAScanProcessor : IScanProcessor
    {
        //loggers
        private ILog log;
        private ILog IDAlog;

        //active components
        private FLASHIdaWrapper flashIdaWrapper;
        private MethodParameters methodParams;
        private ScanFactory scanFactory;
        private ScanScheduler scanScheduler;
        private bool ms2TaggingEnabled;

        // Conditional MS2 mode fields
        private bool conditionalMS2Enabled;

        // MS3 mode fields
        private bool ms3Enabled;
        private int ms3Mode;
        private int maxMs3PerMs2;
        private string ms3ProteinSequence;

        // Static FAIMS CV mode (when FAIMS detected but only one CV configured)
        private double? staticFaimsCV;

        // MS2 tracking
        private ConcurrentDictionary<int, PendingMS2Info> pendingMS2s;
        private int ms2TrackingIdCounter = 0;

        /// <summary>
        /// Unified tracking for all MS2 scans - stores precursor info and mode flags.
        /// </summary>
        private class PendingMS2Info
        {
            // Core precursor info
            public double PrecursorMz { get; set; }
            public double IsolationWidth { get; set; }
            public int Charge { get; set; }
            public double MonoMass { get; set; }

            // Mode flags
            public bool IsConditional { get; set; }  // Needs tag check for follow-up MS2s
            public bool IsMS3Trigger { get; set; }   // Needs MS3 scheduling after deconv

            // MS2 scan parameters for MS3 targeting
            public string FragmentationMethod { get; set; }  // "HCD", "ETD", "UVPD"
            public int CollisionEnergy { get; set; }
        }

        /// <summary>
        /// Builds scan description metadata string from precursor data
        /// </summary>
        private static string BuildMS2Description(string prefix, PrecursorTarget precursor)
        {
            return String.Format("{0}|{1:F2}@{2}", prefix, precursor.MonoMass, precursor.Charge);
        }

        /// <summary>
        /// Builds MS3 scan description metadata string
        /// </summary>
        private static string BuildMS3Description(PendingMS2Info pending, FLASHIdaWrapper.MS3Target target)
        {
            string desc = String.Format("{0:F2}@{1}", target.Mass, target.Charge);
            if (target.IonName != null)
                desc += target.IonName;
            return desc;
        }

        /// <summary>
        /// Extracts tracking ID from scan description (handles both old and new formats)
        /// </summary>
        private static bool TryExtractTrackingId(string scanDesc, string prefix, out int trackingId)
        {
            trackingId = -1;
            if (string.IsNullOrEmpty(scanDesc) || !scanDesc.StartsWith(prefix))
                return false;

            string afterPrefix = scanDesc.Substring(prefix.Length);
            int pipeIndex = afterPrefix.IndexOf('|');
            string idPart = pipeIndex >= 0 ? afterPrefix.Substring(0, pipeIndex) : afterPrefix;

            return int.TryParse(idPart, out trackingId);
        }

        /// <summary>
        /// Create an instance of the scan processor using <paramref name="parameters"/>, connected to existing <see cref="ScanFactory"/> <paramref name="factory"/>
        /// and <see cref="ScanScheduler"/> <paramref name="scheduler"/>
        /// </summary>
        /// <param name="parameters">Parameters for scan processor</param>
        /// <param name="factory">An instance of <see cref="scanFactory"/></param>
        /// <param name="scheduler">An instance of <see cref="scanScheduler"/></param>
        /// <param name="staticCV">Optional static FAIMS CV to apply to all MS2/MS3 scans</param>
        public IDAScanProcessor(MethodParameters parameters, ScanFactory factory, ScanScheduler scheduler, double? staticCV = null)
        {
            //initialize loggers
            log = LogManager.GetLogger("General");
            IDAlog = LogManager.GetLogger("IDA");

            methodParams = parameters;
            scanScheduler = scheduler;
            scanFactory = factory;

            flashIdaWrapper = new FLASHIdaWrapper(methodParams.IDA);

            // Validate MS2Tagging requirements
            ms2TaggingEnabled = methodParams.IDA.MS2Tagging;
            if (ms2TaggingEnabled)
            {
                if (methodParams.IDA.TargetMode != 1)
                {
                    log.Warn("MS2Tagging requires TargetMode=1 (inclusion mode). MS2Tagging disabled.");
                    ms2TaggingEnabled = false;
                }
                else if (String.IsNullOrEmpty(methodParams.IDA.FastaFile))
                {
                    log.Warn("MS2Tagging requires FastaFile. MS2Tagging disabled.");
                    ms2TaggingEnabled = false;
                }
                else
                {
                    log.Info(String.Format("MS2Tagging ENABLED with FASTA: {0}", methodParams.IDA.FastaFile));
                }
            }

            // Initialize unified MS2 tracking (always needed for precursor info)
            pendingMS2s = new ConcurrentDictionary<int, PendingMS2Info>();

            // Initialize Conditional MS2 mode
            conditionalMS2Enabled = methodParams.IDA.ConditionalMS2;
            if (conditionalMS2Enabled)
            {
                if (methodParams.MS2.Count < 2)
                {
                    log.Warn("ConditionalMS2 enabled with only 1 MS2 type. Tag detection will work but no follow-up MS2s will be scheduled.");
                }

                log.Info(String.Format("ConditionalMS2 ENABLED with {0} MS2 types", methodParams.MS2.Count));
            }

            // Initialize MS3 mode
            ms3Enabled = methodParams.IDA.EnableMS3;
            ms3Mode = methodParams.IDA.MS3Mode;
            maxMs3PerMs2 = methodParams.IDA.MaxMs3PerMs2;
            ms3ProteinSequence = methodParams.IDA.MS3ProteinSequence;

            if (ms3Enabled)
            {
                if (methodParams.MS3 == null || methodParams.MS3.Count == 0)
                {
                    log.Warn("EnableMS3 is true but no MS3 parameters defined. MS3 disabled.");
                    ms3Enabled = false;
                }
                else
                {
                    string modeInfo = (ms3Mode == 1 || ms3Mode == 2 || ms3Mode == 3) && !string.IsNullOrEmpty(ms3ProteinSequence)
                        ? ", Protein: " + (ms3ProteinSequence.Length > 20 ? ms3ProteinSequence.Substring(0, 20) + "..." : ms3ProteinSequence)
                        : "";
                    log.Info(String.Format("MS3 mode {0} ENABLED - MaxMs3PerMs2: {1}, MS3 types: {2}{3}",
                        ms3Mode, maxMs3PerMs2, methodParams.MS3.Count, modeInfo));
                }
            }

            // Initialize static FAIMS CV mode
            staticFaimsCV = staticCV;
            if (staticFaimsCV.HasValue)
            {
                log.Info(String.Format("Static FAIMS CV mode ENABLED with CV={0}", staticFaimsCV.Value));
            }
        }

        /// <summary>
        /// Add new custom scan to a queue of scheduled scans,
        /// if the scan is not defined add the default ones
        /// </summary>
        /// <param name="scan">Definition of new custom scan <see cref="IFusionCustomScan"/></param>
        public void OutputMS(IFusionCustomScan scan)
        {
            if (scan != null)
            {
                scanScheduler.AddScan(scan, 2);
            }
            else
            {
                scanScheduler.AddDefault(); //add MS1 and AGC scans to the end of queue
            }
            
        }

        /// <summary>
        /// Process provided MSScan with FLASHIda
        /// </summary>
        /// <param name="msScan">An instance of scan object, as returned by the instrument API <see cref="IMsScan"/></param>
        /// <returns></returns>
        public IEnumerable<IFusionCustomScan> ProcessMS(IMsScan msScan)
        {

            List<IFusionCustomScan> scans = new List<IFusionCustomScan>();

            //for FTMS MS1 scans search for precursors (exclude IT scans)
            if (msScan.Header["MSOrder"] == "1" && msScan.Header["MassAnalyzer"] == "FTMS")
            {
                
                //get ScanID for logging purposes
                msScan.Trailer.TryGetValue("Access ID", out var scanId);

                try
                {
                    List<PrecursorTarget> targets = flashIdaWrapper.GetIsolationWindows(msScan);
                    List<double> monoMasses = flashIdaWrapper.GetAllMonoisotopicMasses();
                    //logging of targets
                    IDAlog.Info(String.Format("MS1 Scan# {0} RT {1:f04} (Access ID {2}) - {3} targets",
                        msScan.Header["Scan"], msScan.Header["StartTime"], scanId, targets.Count));

                     
                    //schedule TopN fragmentation scans with highest qScore
                    foreach (PrecursorTarget precursor in targets.OrderByDescending(t => t.Score).Take(methodParams.IDA.MaxMs2CountPerMs1))
                    {
                        double center = precursor.Window.Center;
                        double isolation = precursor.Window.Width;
                        int z = precursor.Charge;

                        if (conditionalMS2Enabled)
                        {
                            // CONDITIONAL MODE: Only send first MS2 type
                            MS2Parameters firstMS2Params = methodParams.MS2.First();

                            // Generate tracking ID for this MS2 scan
                            int trackingId = System.Threading.Interlocked.Increment(ref ms2TrackingIdCounter);

                            // Store precursor info with mode flags and scan parameters
                            pendingMS2s[trackingId] = new PendingMS2Info
                            {
                                PrecursorMz = center,
                                IsolationWidth = isolation,
                                Charge = z,
                                MonoMass = precursor.MonoMass,
                                IsConditional = true,
                                IsMS3Trigger = ms3Enabled,
                                FragmentationMethod = firstMS2Params.Activation,
                                CollisionEnergy = firstMS2Params.CollisionEnergy
                            };

                            string scanDesc = BuildMS2Description(String.Format("_{0}", trackingId), precursor);

                            IFusionCustomScan firstScan = scanFactory.CreateFusionCustomScan(
                                new ScanParameters
                                {
                                    Analyzer = firstMS2Params.Analyzer,
                                    IsolationMode = firstMS2Params.IsolationMode,
                                    FirstMass = new double[] { firstMS2Params.FirstMass },
                                    LastMass = new double[] { firstMS2Params.LastMass },
                                    OrbitrapResolution = firstMS2Params.OrbitrapResolution,
                                    MSXTargets = firstMS2Params.AGCTarget,
                                    PrecursorMass = new double[] { center },
                                    IsolationWidth = new double[] { isolation },
                                    ActivationType = new string[] { firstMS2Params.Activation },
                                    CollisionEnergy = new int[] { firstMS2Params.CollisionEnergy },
                                    ScanType = "MSn",
                                    Microscans = firstMS2Params.Microscans,
                                    ChargeStates = new int[] { Math.Min(z, 25) },
                                    MaxIT = firstMS2Params.MaxIT,
                                    ReactionTime = firstMS2Params.ReactionTime != 0 ? new double[] { firstMS2Params.ReactionTime } : null,
                                    ReagentMaxIT = firstMS2Params.ReagentMaxIT != 0 ? new double[] { firstMS2Params.ReagentMaxIT } : null,
                                    ReagentAGCTarget = firstMS2Params.ReagentAGCTarget != 0 ? new int[] { firstMS2Params.ReagentAGCTarget } : null,
                                    SrcRFLens = new double[] { methodParams.MS1.RFLens },
                                    SourceCIDEnergy = methodParams.MS1.SourceCID,
                                    SourceCIDScalingFactor = methodParams.MS1.SourceCIDScaling,
                                    DataType = firstMS2Params.DataType,
                                    ScanRangeMode = "DefineMZRange",
                                    ScanDescription = scanDesc,
                                    FAIMS_CV = staticFaimsCV,
                                    FAIMS_Voltages = staticFaimsCV.HasValue ? "on" : null
                                }, delay: 3);

                            scans.Add(firstScan);

                            log.Debug(String.Format("ADD CONDITIONAL m/z {0:f04}/{1:f02} ({2}+) qScore: {3:f04} trackingId: {4}",
                                center, isolation, z, precursor.Score, trackingId));
                            IDAlog.Debug(precursor.ToString());
                        }
                        else
                        {
                            // STANDARD MODE: Send all MS2 types - each gets its own tracking ID
                            foreach (MS2Parameters ms2_params in methodParams.MS2)
                            {
                                // Generate tracking ID for THIS specific MS2 scan
                                int trackingId = System.Threading.Interlocked.Increment(ref ms2TrackingIdCounter);

                                // Store precursor info with mode flags and this scan's parameters
                                pendingMS2s[trackingId] = new PendingMS2Info
                                {
                                    PrecursorMz = center,
                                    IsolationWidth = isolation,
                                    Charge = z,
                                    MonoMass = precursor.MonoMass,
                                    IsConditional = false,
                                    IsMS3Trigger = ms3Enabled,
                                    FragmentationMethod = ms2_params.Activation,
                                    CollisionEnergy = ms2_params.CollisionEnergy
                                };

                                string scanDesc = BuildMS2Description(String.Format("_{0}", trackingId), precursor);

                                Console.WriteLine(String.Format("MS2 Settings: PrecursorMass={0}, IsolationWidth={1}, ChargeStates={2}", center, isolation, z));
                                IFusionCustomScan repScan = scanFactory.CreateFusionCustomScan(
                                    new ScanParameters
                                    {
                                        Analyzer = ms2_params.Analyzer,
                                        IsolationMode = ms2_params.IsolationMode,
                                        FirstMass = new double[] { ms2_params.FirstMass },
                                        LastMass = new double[] { ms2_params.LastMass },
                                        OrbitrapResolution = ms2_params.OrbitrapResolution,
                                        MSXTargets = ms2_params.AGCTarget,
                                        PrecursorMass = new double[] { center },
                                        IsolationWidth = new double[] { isolation },
                                        ActivationType = new string[] { ms2_params.Activation },
                                        CollisionEnergy = new int[] { ms2_params.CollisionEnergy },
                                        ScanType = "MSn",
                                        Microscans = ms2_params.Microscans,
                                        ChargeStates = new int[] { Math.Min(z, 25) },
                                        MaxIT = ms2_params.MaxIT,
                                        ReactionTime = ms2_params.ReactionTime != 0 ? new double[] { ms2_params.ReactionTime } : null,
                                        ReagentMaxIT = ms2_params.ReagentMaxIT != 0 ? new double[] { ms2_params.ReagentMaxIT } : null,
                                        ReagentAGCTarget = ms2_params.ReagentAGCTarget != 0 ? new int[] { ms2_params.ReagentAGCTarget } : null,
                                        SrcRFLens = new double[] { methodParams.MS1.RFLens },
                                        SourceCIDEnergy = methodParams.MS1.SourceCID,
                                        SourceCIDScalingFactor = methodParams.MS1.SourceCIDScaling,
                                        DataType = ms2_params.DataType,
                                        ScanRangeMode = "DefineMZRange",
                                        ScanDescription = scanDesc,
                                        FAIMS_CV = staticFaimsCV,
                                        FAIMS_Voltages = staticFaimsCV.HasValue ? "on" : null
                                    }, delay: 3);

                                scans.Add(repScan);

                                log.Debug(String.Format("ADD m/z {0:f04}/{1:f02} ({2}+) qScore: {3:f04} hcd: {5} to Queue as #{4}",
                                    center, isolation, z, precursor.Score, scanScheduler.customScans.Count + scans.Count, ms2_params.CollisionEnergy));
                                IDAlog.Debug(precursor.ToString());
                            }
                        }
                    }
                    if (monoMasses.Count > 0)
                        IDAlog.Debug(String.Format("AllMass={0}", String.Join<double>(" ", monoMasses.ToArray())));
                }
                catch (Exception ex)
                {
                    IDAlog.Error(String.Format("ProcessMS failed while creating MS2 scans. {0}\n{1}", ex.Message, ex.StackTrace));
                }

                scans.Add(null); //will be replaced by default scan
            }
            // Process MS2 scans - unified handling for all tracked scans
            else if (msScan.Header["MSOrder"] == "2" && msScan.Header["MassAnalyzer"] == "FTMS")
            {
                msScan.Trailer.TryGetValue("Access ID", out var scanId);
                msScan.Trailer.TryGetValue("Scan Description", out var scanDesc);
                Console.WriteLine(String.Format("MS2 Scan with Scan ID={0}, Description={1}", scanId, scanDesc));
                double rt = double.Parse(msScan.Header["StartTime"]);

                // Unified handling for ALL tracked MS2 scans (prefix: _)
                if (scanDesc != null && scanDesc.StartsWith("_") &&
                    TryExtractTrackingId(scanDesc, "_", out int trackingId) &&
                    pendingMS2s.TryGetValue(trackingId, out var pending))
                {
                    try
                    {
                        // Always have precursor info now - use it for deconvolution
                        int peakGroups = flashIdaWrapper.DeconvolveMS2(msScan, pending.MonoMass, pending.Charge);

                        // Handle Conditional MS2 mode
                        if (pending.IsConditional)
                        {
                            bool tagsFound = peakGroups > 0 && flashIdaWrapper.ProcessMS2ForTagBasedTargeting(msScan);

                            if (tagsFound)
                            {
                                IDAlog.Info(String.Format("MS2 Scan# {0} RT {1:f02} - Tags found! Scheduling {2} additional MS2 types",
                                    msScan.Header["Scan"], rt, methodParams.MS2.Count - 1));

                                // Schedule all remaining MS2 types (skip first which was already sent)
                                foreach (MS2Parameters ms2_params in methodParams.MS2.Skip(1))
                                {
                                    IFusionCustomScan followUpScan = scanFactory.CreateFusionCustomScan(
                                        new ScanParameters
                                        {
                                            Analyzer = ms2_params.Analyzer,
                                            IsolationMode = ms2_params.IsolationMode,
                                            FirstMass = new double[] { ms2_params.FirstMass },
                                            LastMass = new double[] { ms2_params.LastMass },
                                            OrbitrapResolution = ms2_params.OrbitrapResolution,
                                            MSXTargets = ms2_params.AGCTarget,
                                            PrecursorMass = new double[] { pending.PrecursorMz },
                                            IsolationWidth = new double[] { pending.IsolationWidth },
                                            ActivationType = new string[] { ms2_params.Activation },
                                            CollisionEnergy = new int[] { ms2_params.CollisionEnergy },
                                            ScanType = "MSn",
                                            Microscans = ms2_params.Microscans,
                                            ChargeStates = new int[] { Math.Min(pending.Charge, 25) },
                                            MaxIT = ms2_params.MaxIT,
                                            ReactionTime = ms2_params.ReactionTime != 0 ? new double[] { ms2_params.ReactionTime } : null,
                                            ReagentMaxIT = ms2_params.ReagentMaxIT != 0 ? new double[] { ms2_params.ReagentMaxIT } : null,
                                            ReagentAGCTarget = ms2_params.ReagentAGCTarget != 0 ? new int[] { ms2_params.ReagentAGCTarget } : null,
                                            SrcRFLens = new double[] { methodParams.MS1.RFLens },
                                            SourceCIDEnergy = methodParams.MS1.SourceCID,
                                            SourceCIDScalingFactor = methodParams.MS1.SourceCIDScaling,
                                            DataType = ms2_params.DataType,
                                            ScanRangeMode = "DefineMZRange",
                                            FAIMS_CV = staticFaimsCV,
                                            FAIMS_Voltages = staticFaimsCV.HasValue ? "on" : null
                                        }, delay: 3);

                                    scans.Add(followUpScan);
                                    log.Debug(String.Format("ADD follow-up {0} m/z {1:f04}", ms2_params.Activation, pending.PrecursorMz));
                                }
                            }
                            else
                            {
                                IDAlog.Info(String.Format("MS2 Scan# {0} RT {1:f02} - No tags, skipping remaining MS2 types",
                                    msScan.Header["Scan"], rt));
                            }
                        }
                        // Handle standard MS2 tagging mode (not conditional)
                        else if (ms2TaggingEnabled)
                        {
                            bool detected = peakGroups > 0 && flashIdaWrapper.ProcessMS2ForTagBasedTargeting(msScan);

                            if (detected)
                            {
                                IDAlog.Info(String.Format("MS2 Scan# {0} RT {1:f02} - Protein family detected, inclusion list expanded",
                                    msScan.Header["Scan"], rt));
                            }
                        }

                        // Handle MS3 triggering (can be combined with conditional or tagging)
                        if (pending.IsMS3Trigger && peakGroups > 0)
                        {
                            if (ms3Mode == 0)
                            {
                                // Mode 0: Top N masses by qscore
                                List<FLASHIdaWrapper.MS3Target> ms3Targets =
                                    flashIdaWrapper.GetBestMS2Masses(maxMs3PerMs2);

                                IDAlog.Info(String.Format("MS2 Scan# {0} RT {1:f02} - Scheduling {2} MS3 scans",
                                    msScan.Header["Scan"], rt, ms3Targets.Count));

                                foreach (var ms3Target in ms3Targets)
                                {
                                    foreach (MS3Parameters ms3_params in methodParams.MS3)
                                    {
                                        // MS3 arrays: [0] = MS2 precursor info, [1] = MS3 fragment info
                                        IFusionCustomScan ms3Scan = scanFactory.CreateFusionCustomScan(
                                            new ScanParameters
                                            {
                                                Analyzer = ms3_params.Analyzer,
                                                IsolationMode = ms3_params.IsolationMode,
                                                FirstMass = new double[] { ms3_params.FirstMass },
                                                LastMass = new double[] { ms3_params.LastMass },
                                                OrbitrapResolution = ms3_params.OrbitrapResolution,
                                                MSXTargets = ms3_params.AGCTarget,
                                                // Two-stage isolation: MS2 precursor first, then MS3 fragment
                                                PrecursorMass = new double[] {
                                                    pending.PrecursorMz,    // MS2 precursor (from MS1)
                                                    ms3Target.IsolationMz         // MS3 fragment (from MS2)
                                                },
                                                IsolationWidth = new double[] {
                                                    pending.IsolationWidth, // MS2 isolation width
                                                    Math.Max(ms3Target.IsolationWidth, 2)       // MS3 isolation width
                                                },
                                                ActivationType = new string[] {
                                                    pending.FragmentationMethod, // MS2 activation
                                                    ms3_params.Activation                // MS3 activation
                                                },
                                                CollisionEnergy = new int[] {
                                                    pending.CollisionEnergy, // MS2 energy
                                                    ms3_params.CollisionEnergy                // MS3 energy
                                                },
                                                ScanType = "MSn",
                                                Microscans = ms3_params.Microscans,
                                                ChargeStates = new int[] {
                                                    Math.Min(pending.Charge, 25), // MS2 charge
                                                    Math.Min(ms3Target.Charge, 25)       // MS3 fragment charge
                                                },
                                                MaxIT = ms3_params.MaxIT,
                                                ReactionTime = ms3_params.ReactionTime != 0 ?
                                                    new double[] { 0, ms3_params.ReactionTime } : null,
                                                ReagentMaxIT = ms3_params.ReagentMaxIT != 0 ?
                                                    new double[] { 0, ms3_params.ReagentMaxIT } : null,
                                                ReagentAGCTarget = ms3_params.ReagentAGCTarget != 0 ?
                                                    new int[] { 0, ms3_params.ReagentAGCTarget } : null,
                                                SrcRFLens = new double[] { methodParams.MS1.RFLens },
                                                SourceCIDEnergy = methodParams.MS1.SourceCID,
                                                SourceCIDScalingFactor = methodParams.MS1.SourceCIDScaling,
                                                DataType = ms3_params.DataType,
                                                ScanRangeMode = "DefineMZRange",
                                                ScanDescription = BuildMS3Description(pending, ms3Target),
                                                FAIMS_CV = staticFaimsCV,
                                                FAIMS_Voltages = staticFaimsCV.HasValue ? "on" : null
                                            }, delay: 3);

                                        scans.Add(ms3Scan);

                                        log.Debug(String.Format("ADD MS3 MS2-precursor {0:f04} -> MS3-fragment {1:f04}/{2:f02}",
                                            pending.PrecursorMz, ms3Target.IsolationMz, ms3Target.IsolationWidth));
                                    }
                                }
                            }
                            else if (ms3Mode == 1)
                            {
                                // Mode 1: Top N fragments matching protein sequence
                                if (string.IsNullOrEmpty(ms3ProteinSequence))
                                {
                                    IDAlog.Warn("MS3 Mode 1 requires ProteinSequence - skipping");
                                }
                                else
                                {
                                    List<FLASHIdaWrapper.MS3Target> ms3Targets =
                                        flashIdaWrapper.GetTopFragmentMatches(ms3ProteinSequence, maxMs3PerMs2, pending.FragmentationMethod);

                                    IDAlog.Info(String.Format("MS2 Scan# {0} RT {1:f02} - Scheduling {2} MS3 scans (fragment matches)",
                                        msScan.Header["Scan"], rt, ms3Targets.Count));

                                    foreach (var ms3Target in ms3Targets)
                                    {
                                        foreach (MS3Parameters ms3_params in methodParams.MS3)
                                        {
                                            // MS3 arrays: [0] = MS2 precursor info, [1] = MS3 fragment info
                                            IFusionCustomScan ms3Scan = scanFactory.CreateFusionCustomScan(
                                                new ScanParameters
                                                {
                                                    Analyzer = ms3_params.Analyzer,
                                                    IsolationMode = ms3_params.IsolationMode,
                                                    FirstMass = new double[] { ms3_params.FirstMass },
                                                    LastMass = new double[] { ms3_params.LastMass },
                                                    OrbitrapResolution = ms3_params.OrbitrapResolution,
                                                    MSXTargets = ms3_params.AGCTarget,
                                                    // Two-stage isolation: MS2 precursor first, then MS3 fragment
                                                    PrecursorMass = new double[] {
                                                        pending.PrecursorMz,    // MS2 precursor (from MS1)
                                                        ms3Target.IsolationMz         // MS3 fragment (from MS2)
                                                    },
                                                    IsolationWidth = new double[] {
                                                        pending.IsolationWidth, // MS2 isolation width
                                                        Math.Max(ms3Target.IsolationWidth, 2)     // MS3 isolation width
                                                    },
                                                    ActivationType = new string[] {
                                                        pending.FragmentationMethod, // MS2 activation
                                                        ms3_params.Activation                // MS3 activation
                                                    },
                                                    CollisionEnergy = new int[] {
                                                        pending.CollisionEnergy, // MS2 energy
                                                        ms3_params.CollisionEnergy                // MS3 energy
                                                    },
                                                    ScanType = "MSn",
                                                    Microscans = ms3_params.Microscans,
                                                    ChargeStates = new int[] {
                                                        Math.Min(pending.Charge, 25), // MS2 charge
                                                        Math.Min(ms3Target.Charge, 25)       // MS3 fragment charge
                                                    },
                                                    MaxIT = ms3_params.MaxIT,
                                                    ReactionTime = ms3_params.ReactionTime != 0 ?
                                                        new double[] { 0, ms3_params.ReactionTime } : null,
                                                    ReagentMaxIT = ms3_params.ReagentMaxIT != 0 ?
                                                        new double[] { 0, ms3_params.ReagentMaxIT } : null,
                                                    ReagentAGCTarget = ms3_params.ReagentAGCTarget != 0 ?
                                                        new int[] { 0, ms3_params.ReagentAGCTarget } : null,
                                                    SrcRFLens = new double[] { methodParams.MS1.RFLens },
                                                    SourceCIDEnergy = methodParams.MS1.SourceCID,
                                                    SourceCIDScalingFactor = methodParams.MS1.SourceCIDScaling,
                                                    DataType = ms3_params.DataType,
                                                    ScanRangeMode = "DefineMZRange",
                                                    ScanDescription = BuildMS3Description(pending, ms3Target),
                                                    FAIMS_CV = staticFaimsCV,
                                                    FAIMS_Voltages = staticFaimsCV.HasValue ? "on" : null
                                                }, delay: 3);

                                            scans.Add(ms3Scan);

                                            string ionInfo = ms3Target.IonName ?? "fragment";
                                            log.Debug(String.Format("ADD MS3 MS2-precursor {0:f04} -> MS3-fragment {1:f04}/{2:f02} ({3})",
                                                pending.PrecursorMz, ms3Target.IsolationMz, ms3Target.IsolationWidth, ionInfo));
                                        }
                                    }
                                }
                            }
                            else if (ms3Mode == 2)
                            {
                                // Mode 2: Ions enclosing PTM ambiguity regions
                                if (string.IsNullOrEmpty(ms3ProteinSequence))
                                {
                                    IDAlog.Warn("MS3 Mode 2 requires ProteinSequence - skipping");
                                }
                                else
                                {
                                    List<FLASHIdaWrapper.MS3Target> ms3Targets =
                                        flashIdaWrapper.GetAmbiguityEnclosingIons(ms3ProteinSequence, maxMs3PerMs2, pending.FragmentationMethod);

                                    IDAlog.Info(String.Format("MS2 Scan# {0} RT {1:f02} - Scheduling {2} MS3 scans (ambiguity enclosing)",
                                        msScan.Header["Scan"], rt, ms3Targets.Count));

                                    foreach (var ms3Target in ms3Targets)
                                    {
                                        foreach (MS3Parameters ms3_params in methodParams.MS3)
                                        {
                                            // MS3 arrays: [0] = MS2 precursor info, [1] = MS3 fragment info
                                            IFusionCustomScan ms3Scan = scanFactory.CreateFusionCustomScan(
                                                new ScanParameters
                                                {
                                                    Analyzer = ms3_params.Analyzer,
                                                    IsolationMode = ms3_params.IsolationMode,
                                                    FirstMass = new double[] { ms3_params.FirstMass },
                                                    LastMass = new double[] { ms3_params.LastMass },
                                                    OrbitrapResolution = ms3_params.OrbitrapResolution,
                                                    MSXTargets = ms3_params.AGCTarget,
                                                    // Two-stage isolation: MS2 precursor first, then MS3 fragment
                                                    PrecursorMass = new double[] {
                                                        pending.PrecursorMz,    // MS2 precursor (from MS1)
                                                        ms3Target.IsolationMz         // MS3 fragment (from MS2)
                                                    },
                                                    IsolationWidth = new double[] {
                                                        pending.IsolationWidth, // MS2 isolation width
                                                        Math.Max(ms3Target.IsolationWidth, 2)       // MS3 isolation width
                                                    },
                                                    ActivationType = new string[] {
                                                        pending.FragmentationMethod, // MS2 activation
                                                        ms3_params.Activation                // MS3 activation
                                                    },
                                                    CollisionEnergy = new int[] {
                                                        pending.CollisionEnergy, // MS2 energy
                                                        ms3_params.CollisionEnergy                // MS3 energy
                                                    },
                                                    ScanType = "MSn",
                                                    Microscans = ms3_params.Microscans,
                                                    ChargeStates = new int[] {
                                                        Math.Min(pending.Charge, 25), // MS2 charge
                                                        Math.Min(ms3Target.Charge, 25)       // MS3 fragment charge
                                                    },
                                                    MaxIT = ms3_params.MaxIT,
                                                    ReactionTime = ms3_params.ReactionTime != 0 ?
                                                        new double[] { 0, ms3_params.ReactionTime } : null,
                                                    ReagentMaxIT = ms3_params.ReagentMaxIT != 0 ?
                                                        new double[] { 0, ms3_params.ReagentMaxIT } : null,
                                                    ReagentAGCTarget = ms3_params.ReagentAGCTarget != 0 ?
                                                        new int[] { 0, ms3_params.ReagentAGCTarget } : null,
                                                    SrcRFLens = new double[] { methodParams.MS1.RFLens },
                                                    SourceCIDEnergy = methodParams.MS1.SourceCID,
                                                    SourceCIDScalingFactor = methodParams.MS1.SourceCIDScaling,
                                                    DataType = ms3_params.DataType,
                                                    ScanRangeMode = "DefineMZRange",
                                                    ScanDescription = BuildMS3Description(pending, ms3Target),
                                                    FAIMS_CV = staticFaimsCV,
                                                    FAIMS_Voltages = staticFaimsCV.HasValue ? "on" : null
                                                }, delay: 3);

                                            scans.Add(ms3Scan);

                                            string ionInfo = ms3Target.IonName ?? "fragment";
                                            log.Debug(String.Format("ADD MS3 MS2-precursor {0:f04} -> MS3-fragment {1:f04}/{2:f02} ({3} ambig)",
                                                pending.PrecursorMz, ms3Target.IsolationMz, ms3Target.IsolationWidth, ionInfo));
                                        }
                                    }
                                }
                            }
                            else if (ms3Mode == 3)
                            {
                                // Mode 3: Terminal fragment ions (innermost b/y-ions)
                                if (string.IsNullOrEmpty(ms3ProteinSequence))
                                {
                                    IDAlog.Warn("MS3 Mode 3 requires ProteinSequence - skipping");
                                }
                                else
                                {
                                    List<FLASHIdaWrapper.MS3Target> ms3Targets =
                                        flashIdaWrapper.GetTerminalFragmentIons(ms3ProteinSequence, maxMs3PerMs2, pending.FragmentationMethod);

                                    IDAlog.Info(String.Format("MS2 Scan# {0} RT {1:f02} - Scheduling {2} MS3 scans (terminal fragments)",
                                        msScan.Header["Scan"], rt, ms3Targets.Count));

                                    foreach (var ms3Target in ms3Targets)
                                    {
                                        string ionInfo = ms3Target.IonName ?? (ms3Target.IonType.HasValue ? ms3Target.IonType.Value.ToString() : "?");

                                        foreach (MS3Parameters ms3_params in methodParams.MS3)
                                        {
                                            IFusionCustomScan ms3Scan = scanFactory.CreateFusionCustomScan(
                                                new ScanParameters
                                                {
                                                    Analyzer = ms3_params.Analyzer,
                                                    IsolationMode = ms3_params.IsolationMode,
                                                    FirstMass = new double[] { ms3_params.FirstMass },
                                                    LastMass = new double[] { ms3_params.LastMass },
                                                    OrbitrapResolution = ms3_params.OrbitrapResolution,
                                                    MSXTargets = ms3_params.AGCTarget,
                                                    PrecursorMass = new double[] {
                                                        pending.PrecursorMz,
                                                        ms3Target.IsolationMz
                                                    },
                                                    IsolationWidth = new double[] {
                                                        pending.IsolationWidth,
                                                        Math.Max(ms3Target.IsolationWidth, 2)
                                                    },
                                                    ActivationType = new string[] {
                                                        pending.FragmentationMethod,
                                                        ms3_params.Activation
                                                    },
                                                    CollisionEnergy = new int[] {
                                                        pending.CollisionEnergy,
                                                        ms3_params.CollisionEnergy
                                                    },
                                                    ScanType = "MSn",
                                                    Microscans = ms3_params.Microscans,
                                                    ChargeStates = new int[] {
                                                        Math.Min(pending.Charge, 25),
                                                        Math.Min(ms3Target.Charge, 25)
                                                    },
                                                    MaxIT = ms3_params.MaxIT,
                                                    ReactionTime = ms3_params.ReactionTime != 0 ?
                                                        new double[] { 0, ms3_params.ReactionTime } : null,
                                                    ReagentMaxIT = ms3_params.ReagentMaxIT != 0 ?
                                                        new double[] { 0, ms3_params.ReagentMaxIT } : null,
                                                    ReagentAGCTarget = ms3_params.ReagentAGCTarget != 0 ?
                                                        new int[] { 0, ms3_params.ReagentAGCTarget } : null,
                                                    SrcRFLens = new double[] { methodParams.MS1.RFLens },
                                                    SourceCIDEnergy = methodParams.MS1.SourceCID,
                                                    SourceCIDScalingFactor = methodParams.MS1.SourceCIDScaling,
                                                    DataType = ms3_params.DataType,
                                                    ScanRangeMode = "DefineMZRange",
                                                    ScanDescription = BuildMS3Description(pending, ms3Target),
                                                    FAIMS_CV = staticFaimsCV,
                                                    FAIMS_Voltages = staticFaimsCV.HasValue ? "on" : null
                                                }, delay: 3);

                                            scans.Add(ms3Scan);

                                            log.Debug(String.Format("ADD MS3 MS2-precursor {0:f04} -> MS3-fragment {1:f04}/{2:f02} ({3} terminal)",
                                                pending.PrecursorMz, ms3Target.IsolationMz, ms3Target.IsolationWidth, ionInfo));
                                        }
                                    }
                                }
                            }

                        }

                        // Always clear MS2 deconvolution state
                        flashIdaWrapper.ClearMS2DeconvolutionState();
                    }
                    catch (Exception ex)
                    {
                        IDAlog.Error(String.Format("MS2 processing failed: {0}", ex.Message));
                    }
                }
            }

            return scans;
        }
    }
}

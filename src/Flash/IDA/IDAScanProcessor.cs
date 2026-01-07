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
        private ConcurrentDictionary<int, PendingConditionalMS2> pendingConditionalMS2s;

        // MS3 mode fields
        private bool ms3Enabled;
        private int ms3Mode;
        private int maxMs3PerMs2;
        private string ms3ProteinSequence;
        private ConcurrentDictionary<int, PendingMS3Info> pendingMS3s;
        private int ms3TrackingIdCounter = 0;

        /// <summary>
        /// Stores pending conditional MS2 information for a precursor.
        /// MS2 parameters (including HCD energy) are accessed from methodParams.MS2 when needed.
        /// </summary>
        private class PendingConditionalMS2
        {
            public double PrecursorMz { get; set; }
            public double IsolationWidth { get; set; }
            public int Charge { get; set; }
            public double MonoMass { get; set; }
        }

        /// <summary>
        /// Stores pending MS3 information for tracking IDA-scheduled MS2 scans.
        /// </summary>
        private class PendingMS3Info
        {
            public double MS2PrecursorMz { get; set; }
            public double MS2IsolationWidth { get; set; }
            public int MS2Charge { get; set; }
            public double MS2PrecursorMass { get; set; }
        }

        /// <summary>
        /// Builds scan description metadata string from precursor data
        /// </summary>
        private static string BuildMS2Description(string prefix, PrecursorTarget precursor)
        {
            return String.Format("{0}|PM={1:F2}",
                prefix,
                precursor.MonoMass,
                precursor.Charge,
                precursor.Score,
                precursor.PrecursorIntensity,
                precursor.Window.Center,
                precursor.Window.Width);
        }

        /// <summary>
        /// Builds MS3 scan description metadata string
        /// </summary>
        private static string BuildMS3Description(PendingMS3Info pending, FLASHIdaWrapper.MS3Target target)
        {
            string desc = String.Format("PM={0:F2}",
                pending.MS2PrecursorMass,
                pending.MS2Charge,
                target.Mass,
                target.Charge,
                target.QScore);

            if (target.IonName != null)
                desc += "|" + target.IonName.ToUpper();  // e.g., "|B12" or "|Y5"
            else if (target.IsBIon.HasValue)
                desc += target.IsBIon.Value ? "|B" : "|Y";  // fallback

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
        public IDAScanProcessor(MethodParameters parameters, ScanFactory factory, ScanScheduler scheduler)
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

            // Initialize Conditional MS2 mode
            conditionalMS2Enabled = methodParams.IDA.ConditionalMS2;
            if (conditionalMS2Enabled)
            {
                pendingConditionalMS2s = new ConcurrentDictionary<int, PendingConditionalMS2>();

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
                pendingMS3s = new ConcurrentDictionary<int, PendingMS3Info>();

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
                            int trackingId = precursor.Id;

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
                                    ScanDescription = BuildMS2Description(String.Format("cond_{0}", trackingId), precursor)
                                }, delay: 3);

                            scans.Add(firstScan);

                            // Store precursor info for potential follow-up MS2s
                            if (methodParams.MS2.Count > 1)
                            {
                                pendingConditionalMS2s[trackingId] = new PendingConditionalMS2
                                {
                                    PrecursorMz = center,
                                    IsolationWidth = isolation,
                                    Charge = z,
                                    MonoMass = precursor.MonoMass
                                };
                            }

                            log.Debug(String.Format("ADD CONDITIONAL m/z {0:f04}/{1:f02} ({2}+) qScore: {3:f04} trackingId: {4}",
                                center, isolation, z, precursor.Score, trackingId));
                            IDAlog.Debug(precursor.ToString());
                        }
                        else
                        {
                            // STANDARD MODE: Existing behavior - send all MS2 types
                            // MS3 tracking: tag the first MS2 type for MS3 triggering
                            int ms3TrackingId = -1;
                            string ms3ScanDesc = null;

                            if (ms3Enabled)
                            {
                                ms3TrackingId = System.Threading.Interlocked.Increment(ref ms3TrackingIdCounter);
                                ms3ScanDesc = BuildMS2Description(String.Format("ms3_{0}", ms3TrackingId), precursor);

                                pendingMS3s[ms3TrackingId] = new PendingMS3Info
                                {
                                    MS2PrecursorMz = center,
                                    MS2IsolationWidth = isolation,
                                    MS2Charge = z,
                                    MS2PrecursorMass = precursor.MonoMass
                                };
                            }

                            foreach (MS2Parameters ms2_params in methodParams.MS2)
                            {
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
                                        ScanDescription = ms3ScanDesc
                                    }, delay: 3);

                                scans.Add(repScan);

                                // Only tag the first MS2 type for MS3 triggering
                                ms3ScanDesc = null;

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
            // Process MS2 scans for conditional MS2 mode or tag-based targeting
            else if (msScan.Header["MSOrder"] == "2" && msScan.Header["MassAnalyzer"] == "FTMS")
            {
                msScan.Trailer.TryGetValue("Access ID", out var scanId);
                msScan.Trailer.TryGetValue("Scan Description", out var scanDesc);
                Console.WriteLine(String.Format("MS2 Scan with Scan ID={0}, Description={1}", scanId, scanDesc));
                double rt = double.Parse(msScan.Header["StartTime"]);

                // Handle Conditional MS2 mode
                if (conditionalMS2Enabled && scanDesc != null && scanDesc.StartsWith("cond_"))
                {
                    try
                    {
                        if (TryExtractTrackingId(scanDesc, "cond_", out int trackingId))
                        {
                            // Get precursor mass and charge from pending info for MS2 deconvolution
                            double precursorMass = 0.0;
                            int precursorCharge = 0;
                            if (pendingConditionalMS2s.TryGetValue(trackingId, out var pendingPeek))
                            {
                                precursorMass = pendingPeek.MonoMass;
                                precursorCharge = pendingPeek.Charge;
                            }
                            int peakGroups = flashIdaWrapper.DeconvolveMS2(msScan, precursorMass, precursorCharge);
                            bool tagsFound = peakGroups > 0 && flashIdaWrapper.ProcessMS2ForTagBasedTargeting(msScan);
                            flashIdaWrapper.ClearMS2DeconvolutionState();

                            if (tagsFound && pendingConditionalMS2s.TryRemove(trackingId, out var pending))
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
                                        }, delay: 3);

                                    scans.Add(followUpScan);
                                    log.Debug(String.Format("ADD follow-up {0} m/z {1:f04}", ms2_params.Activation, pending.PrecursorMz));
                                }
                            }
                            else if (!tagsFound)
                            {
                                pendingConditionalMS2s.TryRemove(trackingId, out _);
                                IDAlog.Info(String.Format("MS2 Scan# {0} RT {1:f02} - No tags, skipping remaining MS2 types",
                                    msScan.Header["Scan"], rt));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        IDAlog.Error(String.Format("Conditional MS2 processing failed: {0}", ex.Message));
                    }
                }
                // Handle existing MS2 tagging mode (when ConditionalMS2 is off or scan is not conditional)
                else if (ms2TaggingEnabled)
                {
                    try
                    {
                        // Explicit MS2 deconvolution workflow (no tracked precursor info)
                        int peakGroups = flashIdaWrapper.DeconvolveMS2(msScan, 0.0, 0);
                        bool detected = peakGroups > 0 && flashIdaWrapper.ProcessMS2ForTagBasedTargeting(msScan);
                        flashIdaWrapper.ClearMS2DeconvolutionState();

                        if (detected)
                        {
                            IDAlog.Info(String.Format("MS2 Scan# {0} RT {1:f02} - Protein family detected, inclusion list expanded",
                                msScan.Header["Scan"], rt));
                        }
                    }
                    catch (Exception ex)
                    {
                        IDAlog.Error(String.Format("MS2 tag processing failed: {0}", ex.Message));
                    }
                }

                // Handle MS3 triggering from IDA-scheduled MS2 scans
                if (ms3Enabled && scanDesc != null && scanDesc.StartsWith("ms3_"))
                {
                    try
                    {
                        if (TryExtractTrackingId(scanDesc, "ms3_", out int ms3TrackingId) &&
                            pendingMS3s.TryRemove(ms3TrackingId, out var pendingMs3))
                        {
                            // Deconvolve MS2 to find fragment masses for MS3
                            int peakGroups = flashIdaWrapper.DeconvolveMS2(msScan, pendingMs3.MS2PrecursorMass, pendingMs3.MS2Charge);

                            if (peakGroups > 0 && ms3Mode == 0)
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
                                                    pendingMs3.MS2PrecursorMz,    // MS2 precursor (from MS1)
                                                    ms3Target.IsolationMz         // MS3 fragment (from MS2)
                                                },
                                                IsolationWidth = new double[] {
                                                    pendingMs3.MS2IsolationWidth, // MS2 isolation width
                                                    Math.Min(ms3Target.IsolationWidth, 2)       // MS3 isolation width
                                                },
                                                ActivationType = new string[] {
                                                    methodParams.MS2.First().Activation, // MS2 activation
                                                    ms3_params.Activation                // MS3 activation
                                                },
                                                CollisionEnergy = new int[] {
                                                    methodParams.MS2.First().CollisionEnergy, // MS2 energy
                                                    ms3_params.CollisionEnergy                // MS3 energy
                                                },
                                                ScanType = "MSn",
                                                Microscans = ms3_params.Microscans,
                                                ChargeStates = new int[] {
                                                    Math.Min(pendingMs3.MS2Charge, 25), // MS2 charge
                                                    Math.Max(1, ms3Target.Charge)       // MS3 fragment charge
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
                                                ScanDescription = BuildMS3Description(pendingMs3, ms3Target)
                                            }, delay: 3);

                                        scans.Add(ms3Scan);

                                        log.Debug(String.Format("ADD MS3 MS2-precursor {0:f04} -> MS3-fragment {1:f04}/{2:f02}",
                                            pendingMs3.MS2PrecursorMz, ms3Target.IsolationMz, ms3Target.IsolationWidth));
                                    }
                                }
                            }
                            else if (peakGroups > 0 && ms3Mode == 1)
                            {
                                // Mode 1: Top N fragments matching protein sequence
                                if (string.IsNullOrEmpty(ms3ProteinSequence))
                                {
                                    IDAlog.Warn("MS3 Mode 1 requires ProteinSequence - skipping");
                                }
                                else
                                {
                                    List<FLASHIdaWrapper.MS3Target> ms3Targets =
                                        flashIdaWrapper.GetTopFragmentMatches(ms3ProteinSequence, maxMs3PerMs2);

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
                                                        pendingMs3.MS2PrecursorMz,    // MS2 precursor (from MS1)
                                                        ms3Target.IsolationMz         // MS3 fragment (from MS2)
                                                    },
                                                    IsolationWidth = new double[] {
                                                        pendingMs3.MS2IsolationWidth, // MS2 isolation width
                                                        Math.Min(ms3Target.IsolationWidth, 2)     // MS3 isolation width
                                                    },
                                                    ActivationType = new string[] {
                                                        methodParams.MS2.First().Activation, // MS2 activation
                                                        ms3_params.Activation                // MS3 activation
                                                    },
                                                    CollisionEnergy = new int[] {
                                                        methodParams.MS2.First().CollisionEnergy, // MS2 energy
                                                        ms3_params.CollisionEnergy                // MS3 energy
                                                    },
                                                    ScanType = "MSn",
                                                    Microscans = ms3_params.Microscans,
                                                    ChargeStates = new int[] {
                                                        Math.Min(pendingMs3.MS2Charge, 25), // MS2 charge
                                                        Math.Max(1, ms3Target.Charge)       // MS3 fragment charge
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
                                                    ScanDescription = BuildMS3Description(pendingMs3, ms3Target)
                                                }, delay: 3);

                                            scans.Add(ms3Scan);

                                            string ionInfo = ms3Target.IonName ?? "fragment";
                                            log.Debug(String.Format("ADD MS3 MS2-precursor {0:f04} -> MS3-fragment {1:f04}/{2:f02} ({3})",
                                                pendingMs3.MS2PrecursorMz, ms3Target.IsolationMz, ms3Target.IsolationWidth, ionInfo));
                                        }
                                    }
                                }
                            }
                            else if (peakGroups > 0 && ms3Mode == 2)
                            {
                                // Mode 2: Ions enclosing PTM ambiguity regions
                                if (string.IsNullOrEmpty(ms3ProteinSequence))
                                {
                                    IDAlog.Warn("MS3 Mode 2 requires ProteinSequence - skipping");
                                }
                                else
                                {
                                    List<FLASHIdaWrapper.MS3Target> ms3Targets =
                                        flashIdaWrapper.GetAmbiguityEnclosingIons(ms3ProteinSequence, maxMs3PerMs2);

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
                                                        pendingMs3.MS2PrecursorMz,    // MS2 precursor (from MS1)
                                                        ms3Target.IsolationMz         // MS3 fragment (from MS2)
                                                    },
                                                    IsolationWidth = new double[] {
                                                        pendingMs3.MS2IsolationWidth, // MS2 isolation width
                                                        Math.Min(ms3Target.IsolationWidth, 2)       // MS3 isolation width
                                                    },
                                                    ActivationType = new string[] {
                                                        methodParams.MS2.First().Activation, // MS2 activation
                                                        ms3_params.Activation                // MS3 activation
                                                    },
                                                    CollisionEnergy = new int[] {
                                                        methodParams.MS2.First().CollisionEnergy, // MS2 energy
                                                        ms3_params.CollisionEnergy                // MS3 energy
                                                    },
                                                    ScanType = "MSn",
                                                    Microscans = ms3_params.Microscans,
                                                    ChargeStates = new int[] {
                                                        Math.Min(pendingMs3.MS2Charge, 25), // MS2 charge
                                                        Math.Max(1, ms3Target.Charge)       // MS3 fragment charge
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
                                                    ScanDescription = BuildMS3Description(pendingMs3, ms3Target)
                                                }, delay: 3);

                                            scans.Add(ms3Scan);

                                            string ionInfo = ms3Target.IonName ?? "fragment";
                                            log.Debug(String.Format("ADD MS3 MS2-precursor {0:f04} -> MS3-fragment {1:f04}/{2:f02} ({3} ambig)",
                                                pendingMs3.MS2PrecursorMz, ms3Target.IsolationMz, ms3Target.IsolationWidth, ionInfo));
                                        }
                                    }
                                }
                            }
                            else if (peakGroups > 0 && ms3Mode == 3)
                            {
                                // Mode 3: Terminal fragment ions (innermost b/y-ions)
                                if (string.IsNullOrEmpty(ms3ProteinSequence))
                                {
                                    IDAlog.Warn("MS3 Mode 3 requires ProteinSequence - skipping");
                                }
                                else
                                {
                                    List<FLASHIdaWrapper.MS3Target> ms3Targets =
                                        flashIdaWrapper.GetTerminalFragmentIons(ms3ProteinSequence, maxMs3PerMs2);

                                    IDAlog.Info(String.Format("MS2 Scan# {0} RT {1:f02} - Scheduling {2} MS3 scans (terminal fragments)",
                                        msScan.Header["Scan"], rt, ms3Targets.Count));

                                    foreach (var ms3Target in ms3Targets)
                                    {
                                        string ionInfo = ms3Target.IonName ?? (ms3Target.IsBIon == true ? "b" : "y");

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
                                                        pendingMs3.MS2PrecursorMz,
                                                        ms3Target.IsolationMz
                                                    },
                                                    IsolationWidth = new double[] {
                                                        pendingMs3.MS2IsolationWidth,
                                                        Math.Min(ms3Target.IsolationWidth, 2)
                                                    },
                                                    ActivationType = new string[] {
                                                        methodParams.MS2.First().Activation,
                                                        ms3_params.Activation
                                                    },
                                                    CollisionEnergy = new int[] {
                                                        methodParams.MS2.First().CollisionEnergy,
                                                        ms3_params.CollisionEnergy
                                                    },
                                                    ScanType = "MSn",
                                                    Microscans = ms3_params.Microscans,
                                                    ChargeStates = new int[] {
                                                        Math.Min(pendingMs3.MS2Charge, 25),
                                                        Math.Max(1, ms3Target.Charge)
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
                                                    ScanDescription = BuildMS3Description(pendingMs3, ms3Target)
                                                }, delay: 3);

                                            scans.Add(ms3Scan);

                                            log.Debug(String.Format("ADD MS3 MS2-precursor {0:f04} -> MS3-fragment {1:f04}/{2:f02} ({3} terminal)",
                                                pendingMs3.MS2PrecursorMz, ms3Target.IsolationMz, ms3Target.IsolationWidth, ionInfo));
                                        }
                                    }
                                }
                            }

                            flashIdaWrapper.ClearMS2DeconvolutionState();
                        }
                    }
                    catch (Exception ex)
                    {
                        IDAlog.Error(String.Format("MS3 processing failed: {0}", ex.Message));
                    }
                }
            }

            return scans;
        }
    }
}

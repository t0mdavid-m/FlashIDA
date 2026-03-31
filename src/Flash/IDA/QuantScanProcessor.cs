using System;
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
    public class QuantScanProcessor : IScanProcessor
    {
        //loggers
        private ILog log;
        private ILog IDAlog;

        //active components
        private FLASHIdaWrapper flashIdaWrapper;
        public FLASHIdaWrapper Wrapper => flashIdaWrapper;
        private MethodParameters methodParams;
        private ScanFactory scanFactory;
        private ScanScheduler scanScheduler;

        // Static FAIMS CV mode
        private double? staticFaimsCV;

        /// <summary>
        /// Create an instance of the scan processor using <paramref name="parameters"/>, connected to existing <see cref="ScanFactory"/> <paramref name="factory"/>
        /// and <see cref="ScanScheduler"/> <paramref name="scheduler"/>
        /// </summary>
        /// <param name="parameters">Parameters for scan processor</param>
        /// <param name="factory">An instance of <see cref="scanFactory"/></param>
        /// <param name="scheduler">An instance of <see cref="scanScheduler"/></param>
        /// <param name="staticCV">Optional static FAIMS CV to apply to all MS2 scans</param>
        public QuantScanProcessor(MethodParameters parameters, ScanFactory factory, ScanScheduler scheduler, double? staticCV = null, FLASHIdaWrapper wrapper = null)
        {
            //initialize loggers
            log = LogManager.GetLogger("General");
            IDAlog = LogManager.GetLogger("IDA");

            methodParams = parameters;
            scanScheduler = scheduler;
            scanFactory = factory;

            flashIdaWrapper = wrapper ?? new FLASHIdaWrapper(methodParams);

            if (methodParams.MS2.Count() != 2)
            {
                throw new ArgumentException("The MS2 parameter list must contain exactly two sets of MS2 parameters.");
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
            msScan.Trailer.TryGetValue("Scan Description", out var desc);

            List<IFusionCustomScan> scans = new List<IFusionCustomScan>();

            // Phase 4: Unified bridge path
            if (methodParams.UseUnifiedBridge)
            {
                double[] mzs = msScan.Centroids.Select(c => c.Mz).ToArray();
                double[] ints = msScan.Centroids.Select(c => c.Intensity).ToArray();
                double rt = double.Parse(msScan.Header["StartTime"]);
                int msLevel = int.Parse(msScan.Header["MSOrder"]);
                string scanDesc = "";
                if (msLevel >= 2)
                    msScan.Trailer.TryGetValue("Scan Description", out scanDesc);
                scanDesc = scanDesc ?? "";
                flashIdaWrapper.ProcessScan(mzs, ints, rt, msLevel, scanDesc);
                return scans;
            }

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
                        MS2Parameters ms2_params = methodParams.MS2.First();

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
                            CollisionEnergy = ms2_params.CollisionEnergy != 0 ? new int[] { ms2_params.CollisionEnergy } : null,
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
                            ScanDescription = "quant",
                            ScanRangeMode = "DefineMZRange",
                            FAIMS_CV = staticFaimsCV,
                            FAIMS_Voltages = staticFaimsCV.HasValue ? "on" : null
                        }, delay: 3);

                        scans.Add(repScan);

                        log.Debug(String.Format("ADD m/z {0:f04}/{1:f02} ({2}+) qScore: {3:f04} to Queue as #{4}",
                            center, isolation, z, precursor.Score, scanScheduler.customScans.Count + scans.Count));
                        IDAlog.Debug(precursor.ToString());
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
            else if (msScan.Header["MSOrder"] == "2" && desc.Trim() == "quant")
            {
                //get ScanID for logging purposes
                msScan.Trailer.TryGetValue("Access ID", out var scanId);

                try
                {
                    bool differentiallyAbundant = flashIdaWrapper.IsDifferentiallyAbundant(
                        msScan, methodParams.IDA.quantReporterMZTol, methodParams.IDA.quantFoldChangeThreshold, 
                        methodParams.IDA.quantOnlyOneCondition
                    );
                    IDAlog.Info(String.Format("MS2 Scan# {0} RT {1:f04} (Access ID {2}) - differential={3}",
                       msScan.Header["Scan"], msScan.Header["StartTime"], scanId, differentiallyAbundant));
                    if (!differentiallyAbundant)
                    {
                        return null;
                    }

                    MS2Parameters ms2_params = methodParams.MS2.Last();
                    double center = double.Parse(msScan.Header["PrecursorMass[0]"]);
                    double isolation = double.Parse(msScan.Header["IsolationWidth[0]"]);
                    msScan.Trailer.TryGetValue("Charge State", out var charge_string);
                    int charge_state = int.Parse(charge_string);

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
                        CollisionEnergy = ms2_params.CollisionEnergy != 0 ? new int[] { ms2_params.CollisionEnergy } : null,
                        ScanType = "MSn",
                        Microscans = ms2_params.Microscans,
                        ChargeStates = new int[] { charge_state },
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

                    scans.Add(repScan);

                    log.Debug(String.Format("ADD m/z {0:f04}/{1:f02} ({2}+) (differentially abundant) to Queue as #{3}",
                        center, isolation, charge_state, scanScheduler.customScans.Count + scans.Count));
                }
                catch (Exception ex)
                {
                    IDAlog.Error(String.Format("ProcessMS failed while creating MS2 scans. {0}\n{1}", ex.Message, ex.StackTrace));
                }

                scans.Add(null); //will be replaced by default scan

            }

            return scans;
        }
    }
}

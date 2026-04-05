using System;
using System.Collections.Generic;
using System.Linq;
using Thermo.Interfaces.FusionAccess_V1.Control.Scans;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;
using log4net;

namespace Flash.IDA
{
    /// <summary>
    /// FAIMS scan processor using legacy deconvolution pipeline.
    /// Creates MS2 scans with correct FAIMS CV via GetIsolationWindows + ScanFactory.
    /// Retained until Phase 6 migrates FAIMS to the unified bridge.
    /// </summary>
    public class FAIMSScanProcessor : IScanProcessor
    {
        private ILog log;
        private ILog IDAlog;

        private FLASHIdaWrapper flashIdaWrapper;
        private MethodParameters methodParams;
        private ScanFactory scanFactory;
        private ScanScheduler scanScheduler;

        public FAIMSScanProcessor(MethodParameters parameters, ScanFactory factory,
            ScanScheduler scheduler, FLASHIdaWrapper wrapper)
        {
            log = LogManager.GetLogger("General");
            IDAlog = LogManager.GetLogger("IDA");

            methodParams = parameters;
            scanScheduler = scheduler;
            scanFactory = factory;
            flashIdaWrapper = wrapper;
        }

        public void ProcessMS(IMsScan msScan)
        {
            log.Info("Scan Received - FAIMS");

            // Only process FTMS MS1 scans (skip ion trap and MS2+)
            if (msScan.Header["MSOrder"] != "1" || msScan.Header["MassAnalyzer"] != "FTMS")
                return;

            msScan.Trailer.TryGetValue("Access ID", out var scanId);
            msScan.Trailer.TryGetValue("FAIMS CV", out var CVString);
            msScan.Trailer.TryGetValue("FAIMS Voltage On", out var faimsStatus);

            try
            {
                double cv = double.Parse(CVString);

                // Ignore scans with CVs not in the configured set
                if (!methodParams.IDA.CVValues.Contains(cv))
                {
                    IDAlog.Info(String.Format("Got scan with CV={0}, which is not in {1} -> Ignore Scan",
                        cv, string.Join(" ", methodParams.IDA.CVValues)));
                    return;
                }

                // Deconvolve spectrum via legacy bridge
                List<PrecursorTarget> targets = flashIdaWrapper.GetIsolationWindows(msScan, CVString);
                List<double> monoMasses = flashIdaWrapper.GetAllMonoisotopicMasses();
                int precursors = flashIdaWrapper.GetAllPeakGroupSize();
                double parsedCV = flashIdaWrapper.GetRepresentativeMass();

                IDAlog.Info(String.Format(
                    "MS1 Scan# {0} RT {1:f04} CV={4} FAIMS Voltage On={5} (Access ID {2}) - {3} targets ({6} precursors) ScanCV={7} ParsedCV={8}",
                    msScan.Header["Scan"], msScan.Header["StartTime"], scanId,
                    targets.Count, CVString, faimsStatus, precursors, cv, parsedCV));

                bool accepted = false;

                // Schedule TopN fragmentation scans with highest qScore
                foreach (PrecursorTarget precursor in targets.OrderByDescending(t => t.Score)
                    .Take(methodParams.IDA.MaxMs2CountPerMs1))
                {
                    double center = precursor.Window.Center;
                    double isolation = precursor.Window.Width;
                    int z = precursor.Charge;

                    foreach (MS2Parameters ms2_params in methodParams.MS2)
                    {
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
                                CollisionEnergy = ms2_params.CollisionEnergy != 0
                                    ? new int[] { ms2_params.CollisionEnergy } : null,
                                ScanType = "MSn",
                                Microscans = ms2_params.Microscans,
                                ChargeStates = new int[] { Math.Min(z, 25) },
                                MaxIT = ms2_params.MaxIT,
                                ReactionTime = ms2_params.ReactionTime != 0
                                    ? new double[] { ms2_params.ReactionTime } : null,
                                ReagentMaxIT = ms2_params.ReagentMaxIT != 0
                                    ? new double[] { ms2_params.ReagentMaxIT } : null,
                                ReagentAGCTarget = ms2_params.ReagentAGCTarget != 0
                                    ? new int[] { ms2_params.ReagentAGCTarget } : null,
                                SrcRFLens = new double[] { methodParams.MS1.RFLens },
                                SourceCIDEnergy = methodParams.MS1.SourceCID,
                                SourceCIDScalingFactor = methodParams.MS1.SourceCIDScaling,
                                DataType = ms2_params.DataType,
                                FAIMS_CV = cv,
                                FAIMS_Voltages = "on",
                                ScanRangeMode = "DefineMZRange",
                            }, delay: 3, AGCgroup: scanScheduler.faimsPagcGroups[cv]);

                        int queue_pos = scanScheduler.AddScan(repScan, 2, accepted);

                        if (queue_pos == -1)
                        {
                            log.Debug(String.Format("IGNORE m/z {0:f04}/{1:f02} ({2}+) qScore: {3:f04}",
                                center, isolation, z, precursor.Score));
                            flashIdaWrapper.RemoveFromExclusionList(precursor.Id);
                        }
                        else
                        {
                            log.Debug(String.Format("ADD m/z {0:f04}/{1:f02} ({2}+) qScore: {3:f04} to Queue as #{4}",
                                center, isolation, z, precursor.Score, queue_pos));
                            IDAlog.Debug(precursor.ToString());
                            accepted = true;
                        }
                    }
                }

                if (monoMasses.Count > 0)
                    IDAlog.Debug(String.Format("AllMass={0}", String.Join<double>(" ", monoMasses.ToArray())));

                // Update CV scheduling
                scanScheduler.updateCV(cv, precursors);
                scanScheduler.getFAIMSMS1Scan(true);
            }
            catch (Exception ex)
            {
                IDAlog.Error(String.Format("ProcessMS failed while creating MS2 scans. {0}\n{1}",
                    ex.Message, ex.StackTrace));
            }
        }
    }
}

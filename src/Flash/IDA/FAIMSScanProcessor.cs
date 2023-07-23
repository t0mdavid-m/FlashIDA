﻿using System;
using System.Collections.Generic;
using System.Linq;
using Thermo.Interfaces.FusionAccess_V1.Control.Scans;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;
using log4net;
using log4net.Core;

namespace Flash.IDA
{
    /// <summary>
    /// FLASHIda-enabled scan processor
    /// </summary>
    public class FAIMSScanProcessor : IScanProcessor
    {
        //loggers
        private ILog log;
        private ILog IDAlog;

        //active components
        private FLASHIdaWrapper flashIdaWrapper;
        private MethodParameters methodParams;
        private ScanFactory scanFactory;
        private ScanScheduler scanScheduler;

        /// <summary>
        /// Create an instance of the scan processor using <paramref name="parameters"/>, connected to existing <see cref="ScanFactory"/> <paramref name="factory"/>
        /// and <see cref="ScanScheduler"/> <paramref name="scheduler"/>
        /// </summary>
        /// <param name="parameters">Parameters for scan processor</param>
        /// <param name="factory">An instance of <see cref="scanFactory"/></param>
        /// <param name="scheduler">An instance of <see cref="scanScheduler"/></param>
        public FAIMSScanProcessor(MethodParameters parameters, ScanFactory factory, ScanScheduler scheduler)
        {
            //initialize loggers
            log = LogManager.GetLogger("General");
            IDAlog = LogManager.GetLogger("IDA");

            methodParams = parameters;
            scanScheduler = scheduler;
            scanFactory = factory;

            flashIdaWrapper = new FLASHIdaWrapper(methodParams.IDA);
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
                // get ScanID and CV data
                msScan.Trailer.TryGetValue("Access ID", out var scanId);
                msScan.Trailer.TryGetValue("FAIMS CV", out var CVString);
                msScan.Trailer.TryGetValue("FAIMS Voltage On", out var faims_status);

                try
                {

                    // Get CV and position in the CV list
                    double cv = double.Parse(CVString);
                    int pos = Array.IndexOf(scanScheduler.CVs, cv);
                    // In the beginning scans with different CV values are scheduled, ignore those
                    if (pos == -1)
                    {
                        scans.Add(null);
                        return scans;
                    }

                    // TODO: Move and make thread safe
                    // Deconvolve spectrum and get relevant information
                    List<PrecursorTarget> targets = flashIdaWrapper.GetIsolationWindows(msScan);
                    List<double> monoMasses = flashIdaWrapper.GetAllMonoisotopicMasses();
                    int precursors = flashIdaWrapper.GetAllPeakGroupSize();

                    if (!scanScheduler.planned.All(a => a) && !scanScheduler.planned[pos]) // Planning is not complete and the plan scan for the current cv has not been recorded yet
                    {
                        // Get number of precursors and set variables to indicate complete planning
                        scanScheduler.noPrecursors[pos] = precursors;
                        scanScheduler.noPrecursorsTruncated[pos] = precursors - (precursors % methodParams.TopN);
                        scanScheduler.planned[pos] = true;

                        if (scanScheduler.planned.All(a => a)) // Planning was successfully completed
                        {
                            // Truncate number of such that all MS2 scans are used if enough precursors are available
                            if (scanScheduler.noPrecursorsTruncated.Sum() >= (scanScheduler.maxCVScans * methodParams.TopN))
                            {
                                scanScheduler.noPrecursors = scanScheduler.noPrecursorsTruncated;
                            }

                            // Assign maximum number of scans for each CV
                            for (int i = 0; i < scanScheduler.CVs.Length; i++)
                            {
                                scanScheduler.maxScansPerCV[i] = Convert.ToInt32(Convert.ToDouble((scanScheduler.noPrecursors[i]) / Convert.ToDouble(scanScheduler.noPrecursors.Sum())) * Convert.ToDouble(methodParams.TopN));
                            }

                        }                    
                    }

                    // Schedule MS2 scans
                    
                    //logging of targets
                    IDAlog.Info(String.Format("MS1 Scan# {0} RT {1:f04} CV={4} FAIMS Voltage On={5} (Access ID {2}) - {3} targets",
                            msScan.Header["Scan"], msScan.Header["StartTime"], scanId, targets.Count, CVString, faims_status));
                    if (targets.Count > 0) IDAlog.Debug(String.Join<PrecursorTarget>("\n", targets.ToArray()));
                    if (monoMasses.Count > 0)                   
                        IDAlog.Debug(String.Format("AllMass={0}", String.Join<double>(" ", monoMasses.ToArray())));

                    // Move to next CV value if no precursors are found
                    if ((targets.Count == 0) && (pos==scanScheduler.currentCV))
                    {
                        scanScheduler.maxScansPerCV[pos] = -1;
                    }

                    //schedule TopN fragmentation scans with highest qScore
                    foreach (PrecursorTarget precursor in targets.OrderByDescending(t => t.Score).Take(methodParams.TopN))
                    {
                        double center = precursor.Window.Center;
                        double isolation = precursor.Window.Width;
                        int z = precursor.Charge;

                        IFusionCustomScan repScan = scanFactory.CreateFusionCustomScan(
                            new ScanParameters
                            {
                                Analyzer = methodParams.MS2.Analyzer,
                                IsolationMode = methodParams.MS2.IsolationMode,
                                FirstMass = new double[] { methodParams.MS2.FirstMass },
                                LastMass = new double[] { Math.Min(z * center + 10, 2000) },
                                OrbitrapResolution = methodParams.MS2.OrbitrapResolution,
                                AGCTarget = methodParams.MS2.AGCTarget,
                                PrecursorMass = new double[] { center },
                                IsolationWidth = new double[] { isolation },
                                ActivationType = new string[] { methodParams.MS2.Activation },
                                CollisionEnergy = methodParams.MS2.CollisionEnergy != 0 ? new int[] { methodParams.MS2.CollisionEnergy } : null,
                                ScanType = "MSn",
                                Microscans = methodParams.MS2.Microscans,
                                ChargeStates = new int[] { Math.Min(z, 25) },
                                MaxIT = methodParams.MS2.MaxIT,
                                ReactionTime = methodParams.MS2.ReactionTime != 0 ? new double[] { methodParams.MS2.ReactionTime } : null,
                                ReagentMaxIT = methodParams.MS2.ReagentMaxIT != 0 ? new double[] { methodParams.MS2.ReagentMaxIT } : null,
                                ReagentAGCTarget = methodParams.MS2.ReagentAGCTarget != 0 ? new int[] { methodParams.MS2.ReagentAGCTarget } : null,
                                SrcRFLens = new double[] { methodParams.MS1.RFLens },
                                SourceCIDEnergy = methodParams.MS1.SourceCID,
                                DataType = methodParams.MS2.DataType
                            }, delay: 3);

                        scans.Add(repScan);

                        log.Debug(String.Format("ADD m/z {0:f04}/{1:f02} ({2}+) qScore: {3:f04} to Queue as #{4}",
                            center, isolation, z, precursor.Score, scanScheduler.customScans.Count + scans.Count));

                    }

                    if (scanScheduler.currentCV != pos) // If the CV is not currently scheduled (likely last CV in list) shelve MS2 scans
                    {
                        scanScheduler.shelvedMS2Scans[scanScheduler.currentCV] = scans;
                        List<IFusionCustomScan> blank = new List<IFusionCustomScan> { null };
                        return blank;
                    }
                }

                catch (Exception ex)
                {
                    IDAlog.Error(String.Format("ProcessMS failed while creating MS2 scans. {0}\n{1}", ex.Message, ex.StackTrace));
                }

                scans.Add(null); // If an exception occurrs do nothing
            }

            return scans;
        }
    }
}

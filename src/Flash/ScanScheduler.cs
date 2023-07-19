using System;
using System.Collections.Concurrent;
using log4net;
using Thermo.Interfaces.FusionAccess_V1.Control.Scans;
using System.Linq;
using Thermo.Interfaces.InstrumentAccess_V1.Control.Scans;
using System.Runtime.InteropServices;

namespace Flash
{
    /// <summary>
    /// Helper class to handle scheduling custom scans request to the instrument
    /// </summary>
    public class ScanScheduler
    {
        /// <summary>
        /// Custom scan request queue
        /// </summary>
        public ConcurrentQueue<IFusionCustomScan> customScans { get; set; }
        
        //debug only
        private int MS1Count;
        private int MS2Count;
        private int AGCCount;

        private IFusionCustomScan defaultScan; //type of scan that will be requested when nothing is in the queue
        private IFusionCustomScan agcScan;

        // Housekeeping for FAIMS
        public double[] cvs;
        public int currentCV;
        public int[] maxScansPerCV;
        public int[] scansPerCV;
        public int[] planScanIDs;
        public int[] noPrecursors;
        public int[] noPrecursorsTruncated;
        public bool planMode;
        public bool planningComplete;

        public int planScanCounter;

        // TODO : find nicer solution
        ScanFactory scanFactory;
        MethodParameters methodParams;


        private ILog log;

        /// <summary>
        /// Create the instance using provided definitions of default scan <paramref name="scan"/> and default AGC scan <paramref name="AGCScan"/>
        /// Default scans will be submitted every time the queue is empty
        /// </summary>
        /// <param name="scan">API definition of a default "regular" scan</param>
        /// <param name="AGCScan">API definition of a default "regular" AGC scan</param>
        public ScanScheduler(IFusionCustomScan scan, IFusionCustomScan AGCScan, ScanFactory factory, MethodParameters mparams)
        {
            scanFactory = factory;
            methodParams = mparams;
            
            defaultScan = scan;
            agcScan = AGCScan;
            customScans = new ConcurrentQueue<IFusionCustomScan>();
            log = LogManager.GetLogger("General");

            MS1Count = 0;
            MS2Count = 0;
            AGCCount = 0;

            // TODO : Move to methodParams
            cvs = new double[4];
            cvs[0] = 0.0;
            cvs[1] = -40.0;
            cvs[2] = -50.0;
            cvs[3] = -60.0;
            currentCV = cvs.Length - 1;

            maxScansPerCV = new int[cvs.Length];
            scansPerCV = new int[cvs.Length];
            noPrecursors = new int[cvs.Length];
            noPrecursorsTruncated = new int[cvs.Length];
            planScanIDs = new int[cvs.Length];
            planScanCounter = 100000;
            for (int i = 0; i < maxScansPerCV.Length; i++)
            {
                maxScansPerCV[i] = -1;
                scansPerCV[i] = 0;
                noPrecursors[i] = 0;
                noPrecursorsTruncated[i] = 0;
                planScanIDs[i] = planScanCounter;
                planScanCounter++;
                
            }

            planMode = true;
            planningComplete = false;

            
        }

        /// <summary>
        /// Adds single scan request to a queue
        /// </summary>
        /// <param name="scan">Scan to add</param>
        /// <param name="level">MS level of the scan (this parameter is used for internal "book-keeping")</param>
        public void AddScan(IFusionCustomScan scan, int level)
        {
            customScans.Enqueue(scan);
            switch (level)
            {
                case 0: AGCCount++; break;
                case 1: MS1Count++; break;
                case 2: MS2Count++; break;
                default: //currently we only use up to MS2, if, for example, MS3 will be necessary that should be updated
                    log.Warn(String.Format("MS Level is {0}", level));
                    break;
            }
        }

        /// <summary>
        /// Add a default scan(s) to a queue
        /// </summary>
        public void AddDefault()
        {
            if (AGCCount < 2) //Why 2? - scheduling works smoother, if we allow 0 or 1 full cycles in the request queue (due to processing delay)
            {
                customScans.Enqueue(agcScan);
                log.Debug(String.Format("ADD default AGC scan as #{0}", customScans.Count));
                AGCCount++;
            }

            if (MS1Count < 2) //same as above
            {
                customScans.Enqueue(defaultScan);
                log.Debug(String.Format("ADD default MS1 scan as #{0}", customScans.Count));
                MS1Count++;
            }

            log.Debug(String.Format("QUEUE is [{0}]",
                String.Join(" - ", customScans.ToArray().Select(scan => PrintScanInfo(scan)).ToArray())));
        }

        /// <summary>
        /// Receive next scan from the queue or fallback to a default scan
        /// </summary>
        /// <returns></returns>
        public IFusionCustomScan getNextScan()
        {
            log.Info(String.Format("Queue length: {0}", customScans.Count));
            try
            {
                if (customScans.IsEmpty) //No scans in the queue => send AGC scan and put default scan in the queue to be next
                {
                    log.Info("Empty queue - Handle Scans Appropiately");
                    if (planMode)
                    {
                        log.Info("Starting Plan Mode");

                        for (int i = 0; i < maxScansPerCV.Length; i++)
                        {
                            maxScansPerCV[i] = -1;
                            scansPerCV[i] = 0;
                            noPrecursors[i] = 0;
                            noPrecursorsTruncated[i] = 0;
                            planScanIDs[i] = planScanCounter;
                            planScanCounter++;
                            customScans.Enqueue(createAGCScan(cvs[i]));
                            customScans.Enqueue(createMS1Scan(cvs[i], planScanCounter));
                            MS1Count++;
                            AGCCount++;
                            log.Info(String.Format("ADD default MS1 scan with CV={0} as #{1}", cvs[i], customScans.Count));
                        }

                        planMode = false;
                        planningComplete = false;
                        currentCV = cvs.Length - 1;
                    }

                    else if (!planningComplete)
                    {
                        double cv = cvs[currentCV];
                        scansPerCV[currentCV]++;
                        customScans.Enqueue(createAGCScan(cv));
                        customScans.Enqueue(createMS1Scan(cv, 42));
                        MS1Count++;
                        AGCCount++;
                        log.Info(String.Format("Ran out of scans but planning is not complete - Send default MS1 scan with CV={0} as #{1}", cv, customScans.Count));
                    }
                    else
                    {
                        if (maxScansPerCV[currentCV] >= scansPerCV[currentCV])
                        {
                            if (currentCV == 0)
                            {
                                planMode = true;
                                Array.Sort(noPrecursors, cvs);
                                log.Info(String.Format("´Finished all CVs - order for next scan = {0}", string.Join(" ", cvs)));
                                return getNextScan();
                            }
                            log.Info(String.Format("´Finished with CV {0} - Next up {1}", cvs[currentCV], cvs[currentCV - 1]));
                            currentCV--;
                        }

                        while (maxScansPerCV[currentCV] <= 0)
                        {
                            if (currentCV == 0)
                            {
                                planMode = true;
                                Array.Sort(noPrecursors, cvs);
                                log.Info(String.Format("´Finished all CVs - order for next scan = {0}", string.Join(" ", cvs)));
                                return getNextScan();
                            }
                            log.Info(String.Format("´Finished with CV {0} - Next up {1}", cvs[currentCV], cvs[currentCV - 1]));
                            currentCV--;
                        }

                        double cv = cvs[currentCV];
                        scansPerCV[currentCV]++;
                        customScans.Enqueue(createAGCScan(cv));
                        customScans.Enqueue(createMS1Scan(cv, 42));
                        MS1Count++;
                        AGCCount++;

                    }

                }

                customScans.TryDequeue(out var nextScan);
                if (nextScan != null)
                {
                    if (nextScan.Values["ScanType"] == "Full")
                    {
                        //we assume that we never use IonTrap for anything except AGC, if it ever going to change more sofisticated check is necessary
                        if (nextScan.Values["Analyzer"] == "IonTrap") AGCCount--;
                        else MS1Count--;

                        log.Debug(String.Format("POP Full {0} scan [{1} - {2}] // AGC: {3}, MS1: {4}, MS2: {5}",
                            nextScan.Values["Analyzer"], nextScan.Values["FirstMass"], nextScan.Values["LastMass"],
                            AGCCount, MS1Count, MS2Count));
                    }
                    else if (nextScan.Values["ScanType"] == "MSn") //all MSn considered MS2 (i.e. no check for the actual MS level), should be added if necessary
                    {
                        MS2Count--;
                        log.Debug(String.Format("POP MSn scan MZ = {0} Z = {1} // AGC: {2}, MS1: {3}, MS2: {4}",
                            nextScan.Values["PrecursorMass"], nextScan.Values["ChargeStates"],
                            AGCCount, MS1Count, MS2Count));
                    }

                    return nextScan;
                }
                else //cannot get the scan out for some reason (hopefully never happens)
                {
                    log.Debug("Cannot receive next scan - gonna send AGC scan");
                    customScans.Enqueue(defaultScan);
                    MS1Count++;
                    log.Debug(String.Format("ADD default MS1 scan as #{0}", customScans.Count));
                    return agcScan;
                }
            }
            catch (Exception ex)
            {
                log.Error(String.Format("Schedule Error: {0}\n{1}", ex.Message, ex.StackTrace));
                return agcScan;
            }
        }

        /// <summary>
        /// Returns some basic scan parameters in a text form
        /// </summary>
        /// <remarks>
        /// Used for dubug logging
        /// </remarks>
        private string PrintScanInfo(IScanDefinition scan)
        {
            if (scan.Values["ScanType"] == "Full")
            {
                return String.Format("#{0} Full {1} [{2}:{3}]", scan.RunningNumber, scan.Values["Analyzer"], scan.Values["FirstMass"], scan.Values["LastMass"]);
            }
            else if (scan.Values["ScanType"] == "MSn")
            {
                return String.Format("#{0} MSn {1} [{2}, {3}+]", scan.RunningNumber, scan.Values["Analyzer"], scan.Values["PrecursorMass"], scan.Values["ChargeStates"]);
            }
            return "Unknown"; //sanity
        }


        public IFusionCustomScan createAGCScan(double cv)
        {
            IFusionCustomScan cvAgcScan;
            try
            {
                //default AGC scan, scan parameters match the vendor implementation
                cvAgcScan = scanFactory.CreateFusionCustomScan(
                    new ScanParameters
                    {
                        Analyzer = "IonTrap",
                        FirstMass = new double[] { methodParams.MS1.FirstMass },
                        LastMass = new double[] { methodParams.MS1.LastMass },
                        ScanRate = "Turbo",
                        AGCTarget = 30000,
                        MaxIT = 1,
                        Microscans = 1,
                        SrcRFLens = new double[] { methodParams.MS1.RFLens },
                        SourceCIDEnergy = methodParams.MS1.SourceCID,
                        DataType = "Profile",
                        ScanType = "Full",
                        FAIMS_CV = cv,
                        FAIMS_Voltages = "on"

                    }, id: 41, IsAGC: true, delay: 3); //41 is the magic scan identifier
                log.Info(String.Format("Created AGC scan with cv={0}", cv));
            }
            catch (Exception ex)
            {
                cvAgcScan = agcScan;
                log.Error(String.Format("Cannot create AGC scan: {0}\n{1}", ex.Message, ex.StackTrace));
            }
            return cvAgcScan;
        }

        public IFusionCustomScan createMS1Scan(double cv, int ms1Id)
        {
            IFusionCustomScan cvMS1Scan;
            try
            {
                //default MS1 scan
                cvMS1Scan = scanFactory.CreateFusionCustomScan(
                new ScanParameters
                {
                    Analyzer = methodParams.MS1.Analyzer,
                    FirstMass = new double[] { methodParams.MS1.FirstMass },
                    LastMass = new double[] { methodParams.MS1.LastMass },
                    OrbitrapResolution = methodParams.MS1.OrbitrapResolution,
                    AGCTarget = methodParams.MS1.AGCTarget,
                    MaxIT = methodParams.MS1.MaxIT,
                    Microscans = methodParams.MS1.Microscans,
                    SrcRFLens = new double[] { methodParams.MS1.RFLens },
                    SourceCIDEnergy = methodParams.MS1.SourceCID,
                    DataType = methodParams.MS1.DataType,
                    ScanType = "Full",
                    FAIMS_CV = cv,
                    FAIMS_Voltages = "on"
                }, id: ms1Id, delay: 3);
                log.Info(String.Format("Created MS1 scan with cv={0}", cv));
            }
            catch (Exception ex)
            {
                cvMS1Scan = defaultScan;
                log.Error(String.Format("Cannot create MS1 scan: {0}\n{1}", ex.Message, ex.StackTrace));
            }
            return cvMS1Scan;
        }
    }
}

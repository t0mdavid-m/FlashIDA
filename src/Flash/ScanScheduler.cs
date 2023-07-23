using System;
using System.Collections.Concurrent;
using log4net;
using Thermo.Interfaces.FusionAccess_V1.Control.Scans;
using System.Linq;
using Thermo.Interfaces.InstrumentAccess_V1.Control.Scans;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Policy;
using log4net.Core;

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
        private readonly object sync = new object();

        //debug only
        private int MS1Count;
        private int MS2Count;
        private int AGCCount;

        private IFusionCustomScan defaultScan; //type of scan that will be requested when nothing is in the queue
        private IFusionCustomScan agcScan;
        private IFusionCustomScan[] faimsDefaultScans; //type of scan that will be requested when nothing is in the queue
        private IFusionCustomScan[] faimsAgcScans;

        // Stores the cvs used for FAIMS
        private double[] CVs;
        // Index of the cv currently selected
        private int currentCV;
        // Number of scans allowed per CV
        private int[] maxScansPerCV;
        // Number of scans conducted for each CV in the current cycle
        private int[] scansPerCV;
        // Number of precursors for each CV
        private int[] noPrecursors;
        // Number of precursors for each CV, truncated to be divisible by MaxMs2CountPerMs1
        private int[] noPrecursorsTruncated;
        // Whether the planning phase has been completed for each CV
        private bool[] planned;
        // Whether the planning phase has been started
        private bool planMode;
        // Maximum number of scans per CV cycle (Discovery Phase with all CVs)
        private int maxCVScans;
        // MS2 scans that are shelved until the cv is analyzed
        private List<IFusionCustomScan>[] shelvedMS2Scans;
        // Number of MS1 scans that have been sent out while planning phase is not completed
        private int unplannedScans;


        private MethodParameters methodParams;


        private ILog log;

        /// <summary>
        /// Create the instance using provided definitions of default scan <paramref name="scan"/> and default AGC scan <paramref name="AGCScan"/>
        /// Default scans will be submitted every time the queue is empty
        /// </summary>
        /// <param name="scan">API definition of a default "regular" scan</param>
        /// <param name="AGCScan">API definition of a default "regular" AGC scan</param>
        public ScanScheduler(IFusionCustomScan scan, IFusionCustomScan AGCScan, IFusionCustomScan[] faimsScans, IFusionCustomScan[] faimsAGCScans, MethodParameters mparams)
        {
            methodParams = mparams;
            
            defaultScan = scan;
            agcScan = AGCScan;
            faimsDefaultScans = faimsScans;
            faimsAgcScans = faimsAGCScans;
            customScans = new ConcurrentQueue<IFusionCustomScan>();
            log = LogManager.GetLogger("General");

            MS1Count = 0;
            MS2Count = 0;
            AGCCount = 0;

            // Initialize FAIMS related variables
            CVs = methodParams.IDA.CVValues;
            maxCVScans = (int) ((methodParams.IDA.RTWindow - ((Convert.ToDouble(CVs.Length) - 1) * 0.3)) / 2.25);
            if (methodParams.IDA.UseFAIMS)
            {
                log.Debug(String.Format("Maximum # of scans per block={0}, CVValues={1]", maxCVScans ,string.Join(" ", CVs)));
            }
            currentCV = CVs.Length - 1;
            maxScansPerCV = new int[CVs.Length];
            scansPerCV = new int[CVs.Length];
            noPrecursors = new int[CVs.Length];
            noPrecursorsTruncated = new int[CVs.Length];
            planned = new bool[CVs.Length];
            shelvedMS2Scans = new List<IFusionCustomScan>[CVs.Length];
            for (int i = 0; i < maxScansPerCV.Length; i++)
            {
                maxScansPerCV[i] = -1;
                scansPerCV[i] = 0;
                noPrecursors[i] = 0;
                noPrecursorsTruncated[i] = 0;
                planned[i] = false;
            }
            planMode = true;
            unplannedScans = 0;
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
        /// Make planning calculations for the current CV if required.
        /// </summary>
        /// <returns></returns>
        public void planCV(double cv, int precursors)
        {
            lock (sync)
            {
                if (!planned.All(a => a)) // Planning is not complete 
                {
                    int pos = Array.IndexOf(CVs, cv);
                    if (!planned[pos]) // the plan scan for the current cv has not been recorded yet
                    {
                        // Get number of precursors and set variables to indicate complete planning
                        noPrecursors[pos] = precursors;
                        noPrecursorsTruncated[pos] = precursors - (precursors % methodParams.TopN);
                        planned[pos] = true;

                        log.Debug(String.Format("Received plan scan for CV={0} with {1} precursors ({2} truncated)", cv, noPrecursors[pos], noPrecursorsTruncated[pos]));
                        if (planned.All(a => a)) // Planning was successfully completed
                        {
                            // Truncate number of such that all MS2 scans are used if enough precursors are available
                            if (noPrecursorsTruncated.Sum() >= (maxCVScans * methodParams.TopN))
                            {
                                noPrecursors = noPrecursorsTruncated;
                            }

                            // Assign maximum number of scans for each CV
                            for (int i = 0; i < CVs.Length; i++)
                            {
                                maxScansPerCV[i] = Convert.ToInt32((Convert.ToDouble(noPrecursors[i]) / (Convert.ToDouble(noPrecursors.Sum())+0.0000001)) * Convert.ToDouble(methodParams.TopN));
                            }
                            log.Debug(String.Format("Planning complete! Came up with plan {0} for precursor distribution {1}", string.Join(" ", maxCVScans), string.Join(" ", noPrecursors)));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Handles the case that no targets are found for the current CV
        /// </summary>
        /// <returns></returns>
        public void handleNoTargets(double cv)
        {
            lock (sync)
            {
                if (cv == CVs[currentCV]) // Check if the CV is currently analyzed
                {
                    // Move to next CV value
                    maxScansPerCV[currentCV] = -1;
                    log.Debug(String.Format("Ran out of targets for CV={0}, jumping to next CV", cv));
                }
            }
        }

        /// <summary>
        /// Checks if MS2 scans should be shelved as their CV is not active and shelves the scans if required
        /// </summary>
        /// <returns></returns>
        public bool shelveMS2Scans(double cv, List<IFusionCustomScan> scans)
        {
            lock (sync)
            {
                if (CVs[currentCV] != cv) // If the CV is not currently scheduled shelve MS2 scans
                {
                    shelvedMS2Scans[currentCV] = scans;
                    log.Debug(String.Format("Found {0} targets at CV={1} but currently at CV={2}, shelving for later", scans.Count, cv, CVs[currentCV]));
                    return true;
                }
            }
            return false;
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
                if (customScans.IsEmpty) //No scans in the queue => Fill Queue
                {
                    log.Debug("Empty queue - handle appropiately");
                    if (!methodParams.IDA.UseFAIMS)
                    {
                        customScans.Enqueue(defaultScan);
                        MS1Count++;
                        log.Debug(String.Format("ADD default MS1 scan as #{0}", customScans.Count));
                        return agcScan;
                    }
                    lock (sync)
                    {
                        if (planMode) // Planning Mode: Scan over CVs and collect precursors
                        {
                            log.Debug(String.Format("Planning Started with CVs={0}", string.Join(" ", CVs)));
                            for (int i = 0; i < CVs.Length; i++)
                            {
                                // Set planning variables
                                maxScansPerCV[i] = -1;
                                scansPerCV[i] = 0;
                                noPrecursors[i] = 0;
                                noPrecursorsTruncated[i] = 0;
                                planned[i] = false;
                                shelvedMS2Scans[i] = null;
                                // Add planning scans
                                if (i > 0)
                                {
                                    customScans.Enqueue(faimsAgcScans[i]);
                                    AGCCount++;
                                }
                                customScans.Enqueue(faimsDefaultScans[i]);
                                MS1Count++;
                                log.Debug(String.Format("ADD default MS1 scan with CV={0} as #{1} (Plan Mode)", CVs[i], customScans.Count));
                            }
                            // Planning has been executed
                            planMode = false;
                            // Start MS2 acquisition at the last CV value in the queue
                            currentCV = CVs.Length - 1;
                            // No unplanned scans have been executed yet
                            unplannedScans = 0;
                            return faimsAgcScans[0];
                        }

                        else if (!planned.All(a => a)) // Planning is not yet complete => Acquire MS2 scans for last CV
                        {
                            log.Debug(String.Format("Planning still in progress CVs={0}; Planned={1}", string.Join(" ", CVs), string.Join(" ", planned)));
                            if (unplannedScans >= 5) // If 5 MS1 scans have been scheduled and planning has not yet completed, something went wrong
                            {
                                // Restart planning
                                planMode = true;
                                return getNextScan();
                            }
                            scansPerCV[currentCV]++;
                            customScans.Enqueue(faimsDefaultScans[currentCV]);
                            MS1Count++;
                            unplannedScans++;
                            log.Debug(String.Format("ADD default MS1 scan with CV={0} as #{1} (Unplanned #{2})", CVs[currentCV], customScans.Count,unplannedScans));
                            return faimsAgcScans[currentCV];
                        }
                        else // Planning is complete => Acquire MS2 scans as planned
                        {
                            // Whether or not the CV is changed now
                            bool CVChanged = false;

                            while (scansPerCV[currentCV] >= maxScansPerCV[currentCV]) // Maximum number of scans has been reached for current CV or no scans are scheduled
                            {
                                if (currentCV == 0) // This is the last CV => Set to plan mode
                                {
                                    // Schedule CVs such that the CV with the maximum number of precursors is run last => Schedule the scans while in plan mode
                                    if (!noPrecursors.All(a => (a <= 0))) // Only change order of CV values if precursors were found
                                    {
                                        Array.Sort(noPrecursors, CVs);
                                        Array.Sort(noPrecursors, faimsAgcScans);
                                        Array.Sort(noPrecursors, faimsDefaultScans);
                                    }
                                    planMode = true;
                                    return getNextScan();
                                }
                                currentCV--;
                                CVChanged = true;
                            }

                            if (CVChanged)
                            {
                                log.Debug(String.Format("Changed to CV={0} in CVs={1} with plan={2}", CVs[currentCV], string.Join(" ", CVs), string.Join(" ", maxScansPerCV)));
                            }

                            // Queue MS1 scan with appropiate CV
                            customScans.Enqueue(faimsDefaultScans[currentCV]);
                            scansPerCV[currentCV]++;
                            MS1Count++;

                            if (CVChanged && (shelvedMS2Scans[currentCV] != null)) // Add shelved MS2 scans
                            {
                                log.Debug(String.Format("Found {0} shelved MS2 scans}", shelvedMS2Scans[currentCV].Count));
                                foreach (var shelvedMS2 in shelvedMS2Scans[currentCV])
                                {
                                    if (shelvedMS2 != null)
                                    {
                                        AddScan(shelvedMS2, 2);
                                    }
                                }
                            }
                            log.Info(String.Format("ADD default MS1 scan with CV={0} as #{1} (Planned #{2}/{3})", CVs[currentCV], customScans.Count, scansPerCV[currentCV], maxScansPerCV[currentCV]));
                            return faimsAgcScans[currentCV];
                        }
                    }
                }
                else
                {
                    // Queue is not empty => If it was empty at beginning of method, it was just refilled
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
    }
}

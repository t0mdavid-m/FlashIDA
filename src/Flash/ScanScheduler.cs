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
        public Dictionary<double, int> faimsPagcGroups;

        // Stores the cvs used for FAIMS
        private double[] CVs;
        private double[] CVMedians;
        // Index of the cv currently selected
        private int currentCV;
        // No of MS2 scans that have been queued after last MS1 scan
        private int MS2AfterMS1;
        // Whether or not FAIMS is used
        private bool useFAIMS;

        private int lastSwitch;

        private MethodParameters methodParams;


        private ILog log;

        /// <summary>
        /// Create the instance using provided definitions of default scan <paramref name="scan"/> and default AGC scan <paramref name="AGCScan"/>
        /// Default scans will be submitted every time the queue is empty
        /// </summary>
        /// <param name="scan">API definition of a default "regular" scan</param>
        /// <param name="AGCScan">API definition of a default "regular" AGC scan</param>
        public ScanScheduler(IFusionCustomScan scan, IFusionCustomScan AGCScan, IFusionCustomScan[] faimsScans, IFusionCustomScan[] faimsAGCScans, Dictionary<double, int> faimsPAGCGroups, MethodParameters mparams, bool UseFAIMS, double[] CVMedians_)
        {
            lastSwitch = 0;
            methodParams = mparams;
            useFAIMS = UseFAIMS;
            defaultScan = scan;
            agcScan = AGCScan;
            faimsDefaultScans = faimsScans;
            faimsAgcScans = faimsAGCScans;
            faimsPagcGroups = faimsPAGCGroups;
            customScans = new ConcurrentQueue<IFusionCustomScan>();
            log = LogManager.GetLogger("General");

            MS1Count = 0;
            MS2Count = 0;
            AGCCount = 0;

            // Initialize FAIMS related variables
            CVs = methodParams.IDA.CVValues;
            CVMedians = CVMedians_;

            currentCV = 0;
        }

        public void updateCV(double moment)
        {
            lastSwitch++;
            if (lastSwitch < methodParams.IDA.switchEveryNCV)
            {
                return;
            }
            lastSwitch = 0;

            int new_value = 0;
            for (int i = 0; i < CVMedians.Length; i++)
            {
                if ( (i == 0) && (CVMedians[0] > moment) )
                {
                    new_value = 0;
                    break;
                }
                else if ( (i >= (CVMedians.Length - 1) ) )
                {
                    new_value = CVMedians.Length-1;
                    break;
                }
                else if ( (CVMedians[i] < moment) && (CVMedians[i+1] > moment) )
                {
                    if ((moment - CVMedians[i]) > (CVMedians[i+1] - moment)) {
                        new_value = i + 1;
                    }
                    else
                    {
                        new_value = i;
                    }
                        break;
                }
            }
            if (methodParams.IDA.constrainedSwitching)
            {
                if (currentCV < new_value)
                {
                    currentCV++;
                }
                else if (currentCV > new_value)
                {
                    currentCV--;
                }
            }
            else
            {
                currentCV = new_value;
            }
        }


        /// <summary>
        /// Adds single scan request to a queue
        /// </summary>
        /// <param name="scan">Scan to add</param>
        /// <param name="level">MS level of the scan (this parameter is used for internal "book-keeping")</param>
        public int AddScan(IFusionCustomScan scan, int level)
        {
            if (useFAIMS)
            {
                double cv = double.Parse(scan.Values["FAIMS CV"]);
                lock (sync)
                {
                    if (cv != CVs[currentCV])
                    {
                        log.Debug(String.Format("Received MS2 scan for CV={0} but CV changed to {1} -> Scrapping scan", cv, CVs[currentCV]));
                        return -1;
                    }
                    // 2 accounts for MS1 + AGC
                    if (customScans.Count > 9-2)
                    {
                        log.Debug(String.Format("Received MS2 scan for CV={0} but queue length is {1} -> Scrapping scan", cv, customScans.Count));
                        return -1;
                    }
                    if (MS2AfterMS1 >= methodParams.IDA.MaxMs2CountPerMs1)
                    {
                        getFAIMSMS1Scan(queue_agc: true);
                    }
                    MS2AfterMS1++;
                }
            }
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
            return customScans.Count;
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
        /// Returns the next AGC + MS1 scan as scheduled for FAIMS
        /// </summary>
        /// <returns></returns>
        public IFusionCustomScan getFAIMSMS1Scan(bool queue_agc=false)
        {
            lock (sync)
            {
                MS2AfterMS1 = 0;

                if (queue_agc)
                {
                    customScans.Enqueue(faimsAgcScans[currentCV]);
                    AGCCount++;
                }

                // Queue MS1 scan with appropiate CV
                customScans.Enqueue(faimsDefaultScans[currentCV]);
                MS1Count++;

                log.Info(String.Format("ADD default MS1 scan with CV={0} as #{1}", CVs[currentCV], customScans.Count));
                return faimsAgcScans[currentCV];
            }
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
                    if (!useFAIMS)
                    {
                        customScans.Enqueue(defaultScan);
                        MS1Count++;
                        log.Debug(String.Format("ADD default MS1 scan as #{0}", customScans.Count));
                        return agcScan;
                    }
                    else
                    {
                        return getFAIMSMS1Scan();
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

                            if (useFAIMS)
                            {
                                log.Debug(String.Format("POP Full {0} scan [{1} - {2}] CV={6} // AGC: {3}, MS1: {4}, MS2: {5}",
                                nextScan.Values["Analyzer"], nextScan.Values["FirstMass"], nextScan.Values["LastMass"],
                                AGCCount, MS1Count, MS2Count, nextScan.Values["FAIMS CV"]));

                            }
                            else
                            {
                                log.Debug(String.Format("POP Full {0} scan [{1} - {2}] // AGC: {3}, MS1: {4}, MS2: {5}",
                                nextScan.Values["Analyzer"], nextScan.Values["FirstMass"], nextScan.Values["LastMass"],
                                AGCCount, MS1Count, MS2Count));
                            }
                            
                        }
                        else if (nextScan.Values["ScanType"] == "MSn") //all MSn considered MS2 (i.e. no check for the actual MS level), should be added if necessary
                        {
                            MS2Count--;
                            if (useFAIMS)
                            {
                                log.Debug(String.Format("POP MSn scan MZ = {0} Z = {1} CV={5} // AGC: {2}, MS1: {3}, MS2: {4}",
                                nextScan.Values["PrecursorMass"], nextScan.Values["ChargeStates"],
                                AGCCount, MS1Count, MS2Count, nextScan.Values["FAIMS CV"]));
                            }
                            else
                            {
                                log.Debug(String.Format("POP MSn scan MZ = {0} Z = {1} // AGC: {2}, MS1: {3}, MS2: {4}",
                                nextScan.Values["PrecursorMass"], nextScan.Values["ChargeStates"],
                                AGCCount, MS1Count, MS2Count));
                            }
                            
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

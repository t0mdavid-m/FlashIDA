using System;
using System.Collections.Generic;
using Thermo.Interfaces.FusionAccess_V1.Control.Scans;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;
using log4net;

namespace Flash.IDA
{
    /// <summary>
    /// FLASHIda-enabled scan processor
    /// </summary>
    public class FAIMSTestScanProcessor : IScanProcessor
    {
        //loggers
        private ILog log;
        private ILog IDAlog;

        //active components
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
        public FAIMSTestScanProcessor(MethodParameters parameters, ScanFactory factory, ScanScheduler scheduler)
        {
            //initialize loggers
            log = LogManager.GetLogger("General");
            IDAlog = LogManager.GetLogger("IDA");

            methodParams = parameters;
            scanScheduler = scheduler;
            scanFactory = factory;
        }

        /// <summary>
        /// Add new custom scan to a queue of scheduled scans,
        /// if the scan is not defined add the default ones
        /// </summary>
        /// <param name="scan">Definition of new custom scan <see cref="IFusionCustomScan"/></param>
        public void OutputMS(IFusionCustomScan scan)
        {      
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
                    //logging of scan data
                    IDAlog.Info(String.Format("MS1 Scan# {0} RT {1:f04} (Access ID {2})",
                        msScan.Header["Scan"], msScan.Header["StartTime"], scanId));
                }
                catch (Exception ex)
                {
                    IDAlog.Error(String.Format("ProcessMS failed while logging scan overview data. {0}\n{1}", ex.Message, ex.StackTrace));
                }

                try
                {
                    foreach (var key in msScan.Header.Keys)
                    {
                        IDAlog.Info(String.Format("Header - {0} : {1}", key, msScan.Header[key]));
                    }
                }
                catch (Exception ex)
                {
                    IDAlog.Error(String.Format("ProcessMS failed while logging scan header data. {0}\n{1}", ex.Message, ex.StackTrace));

                }

                try
                {
                    foreach (var name in msScan.Trailer.ItemNames)
                    {
                        msScan.Trailer.TryGetValue(name, out var value);
                        IDAlog.Info(String.Format("Trailer - {0} : {1}", name, value));
                    }
                }
                catch (Exception ex)
                {
                    IDAlog.Error(String.Format("ProcessMS failed while logging scan trailer data. {0}\n{1}", ex.Message, ex.StackTrace));

                }

                scans.Add(null); //will be replaced by default scan
            }

            return scans;
        }
    }
}

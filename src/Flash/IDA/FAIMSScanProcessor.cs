using System;
using System.Linq;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;
using log4net;

namespace Flash.IDA
{
    public class FAIMSScanProcessor : IScanProcessor
    {
        private ILog log;
        private ILog IDAlog;

        private FLASHIdaWrapper wrapper;
        private MethodParameters methodParams;
        private ScanScheduler scanScheduler;
        private IScanProcessor innerProcessor;

        public FAIMSScanProcessor(MethodParameters parameters, ScanScheduler scheduler,
            IScanProcessor innerProcessor, FLASHIdaWrapper wrapper)
        {
            log = LogManager.GetLogger("General");
            IDAlog = LogManager.GetLogger("IDA");

            methodParams = parameters;
            scanScheduler = scheduler;
            this.innerProcessor = innerProcessor;
            this.wrapper = wrapper;
        }

        public void ProcessMS(IMsScan msScan)
        {
            log.Info("Scan Received - FAIMS");

            // Delegate deconvolution to inner processor (UnifiedScanProcessor)
            innerProcessor.ProcessMS(msScan);

            // FAIMS CV scheduling — only for MS1 FTMS scans
            if (msScan.Header["MSOrder"] == "1" && msScan.Header["MassAnalyzer"] == "FTMS")
            {
                msScan.Trailer.TryGetValue("FAIMS CV", out var CVString);
                try
                {
                    double cv = double.Parse(CVString);
                    if (methodParams.IDA.CVValues.Contains(cv))
                    {
                        int precursors = wrapper.GetAllPeakGroupSize();
                        scanScheduler.updateCV(cv, precursors);
                        scanScheduler.getFAIMSMS1Scan(true);
                    }
                }
                catch (Exception ex)
                {
                    IDAlog.Error(String.Format(
                        "FAIMS CV scheduling failed: {0}\n{1}", ex.Message, ex.StackTrace));
                }
            }
        }
    }
}

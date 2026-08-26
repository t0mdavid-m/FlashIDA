using log4net;

namespace Flash.IDA
{
    public class UnifiedScanProcessor : IScanProcessor
    {
        private static readonly ILog log = LogManager.GetLogger("General");

        private FLASHIdaWrapper wrapper;

        public UnifiedScanProcessor(FLASHIdaWrapper wrapper)
        {
            this.wrapper = wrapper;
        }

        public void ProcessMS(ScanData scan)
        {
            //The extraction that used to live here now lives in ScanData.From, which runs on the
            //arrival thread while the IMsScan handle is still readable. Same six values, same order;
            //what changed is WHEN they are read, not what they are.
            //
            //-1 is an ALREADY-HANDLED failure: FLASHIdaWrapper.ProcessScan catches the exception and
            //logs it with its stack trace before converting it. Surface it here so it is visible at
            //the call site rather than silently discarded - but do not escalate it to a run abort.
            //0 is a normal gate rejection (AGC scans, the handshake scan) and is not an error.
            int rc = wrapper.ProcessScan(scan.Mzs, scan.Intensities, scan.RetentionTime,
                                         scan.MsLevel, scan.ScanDescription, scan.FaimsCv,
                                         scan.InstrumentScanNumber);
            if (rc == -1) log.Error("ProcessScan was not successful (bridge returned -1)");
        }
    }
}

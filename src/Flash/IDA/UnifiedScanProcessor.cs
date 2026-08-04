using System.Linq;
using log4net;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;

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

        public void ProcessMS(IMsScan msScan)
        {
            double[] mzs = msScan.Centroids.Select(c => c.Mz).ToArray();
            double[] ints = msScan.Centroids.Select(c => c.Intensity).ToArray();
            double rt = double.Parse(msScan.Header["StartTime"]);
            int msLevel = int.Parse(msScan.Header["MSOrder"]);
            string scanDesc = "";
            msScan.Trailer.TryGetValue("Scan Description", out scanDesc);

            double faimsCv = 0.0;
            if (msScan.Trailer.TryGetValue("FAIMS CV", out var cvStr))
                double.TryParse(cvStr, out faimsCv);

            //-1 is an ALREADY-HANDLED failure: FLASHIdaWrapper.ProcessScan catches the exception and
            //logs it with its stack trace before converting it. Surface it here so it is visible at
            //the call site rather than silently discarded - but do not escalate it to a run abort.
            //0 is a normal gate rejection (AGC scans, the handshake scan) and is not an error.
            int rc = wrapper.ProcessScan(mzs, ints, rt, msLevel, scanDesc ?? "", faimsCv);
            if (rc == -1) log.Error("ProcessScan was not successful (bridge returned -1)");
        }
    }
}

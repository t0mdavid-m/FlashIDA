using System.Linq;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;

namespace Flash.IDA
{
    public class UnifiedScanProcessor : IScanProcessor
    {
        private FLASHIdaWrapper wrapper;

        public UnifiedScanProcessor(FLASHIdaWrapper wrapper)
        {
            this.wrapper = wrapper;
        }

        public void ProcessMS(IMsScan msScan)
        {
            // Guard: only FTMS scans (skip ion trap)
            if (msScan.Header["MassAnalyzer"] != "FTMS")
                return;

            double[] mzs = msScan.Centroids.Select(c => c.Mz).ToArray();
            double[] ints = msScan.Centroids.Select(c => c.Intensity).ToArray();
            double rt = double.Parse(msScan.Header["StartTime"]);
            int msLevel = int.Parse(msScan.Header["MSOrder"]);
            string scanDesc = "";
            if (msLevel >= 2)
                msScan.Trailer.TryGetValue("Scan Description", out scanDesc);

            wrapper.ProcessScan(mzs, ints, rt, msLevel, scanDesc ?? "");
        }
    }
}

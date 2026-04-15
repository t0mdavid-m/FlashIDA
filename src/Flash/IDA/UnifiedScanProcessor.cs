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
            double[] mzs = msScan.Centroids.Select(c => c.Mz).ToArray();
            double[] ints = msScan.Centroids.Select(c => c.Intensity).ToArray();
            double rt = double.Parse(msScan.Header["StartTime"]);
            int msLevel = int.Parse(msScan.Header["MSOrder"]);
            string scanDesc = "";
            if (msLevel >= 2)
                msScan.Trailer.TryGetValue("Scan Description", out scanDesc);

            double faimsCv = 0.0;
            if (msScan.Trailer.TryGetValue("FAIMS CV", out var cvStr))
                double.TryParse(cvStr, out faimsCv);

            wrapper.ProcessScan(mzs, ints, rt, msLevel, scanDesc ?? "", faimsCv);
        }
    }
}

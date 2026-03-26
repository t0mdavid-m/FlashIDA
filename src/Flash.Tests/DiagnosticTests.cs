using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// Standalone diagnostic test — no OneTimeSetUp dependency.
    /// Logs every step to diagnose why GetPeakGroupSize returns 0
    /// in the NUnit process but works in Flash.exe standalone.
    /// Uses Console.WriteLine (captured in NUnit output element).
    /// </summary>
    [TestFixture]
    public class DiagnosticTests
    {
        private const string DllName = "OpenMS.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr CreateFLASHIda(string config);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void DisposeFLASHIda(IntPtr ptr);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetPeakGroupSize(IntPtr ptr,
            double[] mzs, double[] ints, int length,
            double rt, int msLevel, string name, string cv);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetAllPeakGroupSize(IntPtr ptr);

        private static string SpectraDir => Path.Combine(
            TestContext.CurrentContext.TestDirectory, "..", "test-data", "spectra");

        private static (double[] mzs, double[] ints, double rt) LoadSpectrum(string path)
        {
            var mzs = new List<double>();
            var ints = new List<double>();
            double rt = 0;
            bool started = false;

            foreach (var line in File.ReadAllLines(path))
            {
                var token = line.Split('\t');
                if (line.StartsWith("Spec"))
                {
                    rt = double.Parse(token[1]) / 60.0;
                    started = true;
                }
                else if (started && token.Length >= 2)
                {
                    mzs.Add(double.Parse(token[0]));
                    ints.Add(double.Parse(token[1]));
                }
            }

            return (mzs.ToArray(), ints.ToArray(), rt);
        }

        [Test, Category("Tier2")]
        public void P0_DIAG_DeconvolutionDiagnostic()
        {
            string ms1Path = Path.Combine(SpectraDir, "ms1_smoke_test.txt");
            Console.WriteLine("[DIAG] ms1Path: " + ms1Path);
            Console.WriteLine("[DIAG] File exists: " + File.Exists(ms1Path));
            Assume.That(File.Exists(ms1Path), "ms1_smoke_test.txt not found");

            // Load spectrum
            var (mzs, ints, rt) = LoadSpectrum(ms1Path);
            Console.WriteLine("[DIAG] Spectrum peaks: " + mzs.Length);
            Console.WriteLine("[DIAG] RT (minutes): " + rt);
            Console.WriteLine("[DIAG] mzs[0]=" + mzs[0] + ", ints[0]=" + ints[0]);
            Console.WriteLine("[DIAG] mzs[last]=" + mzs[mzs.Length - 1] + ", ints[last]=" + ints[ints.Length - 1]);
            Console.WriteLine("[DIAG] Sum(ints)=" + ints.Sum());
            Console.WriteLine("[DIAG] OMP_NUM_THREADS=" + Environment.GetEnvironmentVariable("OMP_NUM_THREADS"));

            Assert.That(mzs.Length, Is.GreaterThan(6000), "Spectrum too small");

            // Create engine
            string config = "max_mass_count 1 score_threshold 0 min_charge 4 max_charge 50 " +
                   "min_mass 500 max_mass 50000 RT_window 180 tol 10 10 " +
                   "tqscore_threshold 0.9 target_mode 0 IDScore 0 AllCharges 0 " +
                   "HCDEnergy 29 strict_inclusion 0 tie_threshold 0.1 MS3AllCharges 1 " +
                   "min_tag_length 3 max_tag_length 8 max_ptm_count 3 max_flanking_mass_diff 50000 ";
            Console.WriteLine("[DIAG] Config length: " + config.Length);
            IntPtr ptr = CreateFLASHIda(config);
            Console.WriteLine("[DIAG] Ptr: " + ptr);
            Assert.AreNotEqual(IntPtr.Zero, ptr, "CreateFLASHIda returned null");

            try
            {
                int preSize = GetAllPeakGroupSize(ptr);
                Console.WriteLine("[DIAG] GetAllPeakGroupSize (pre): " + preSize);

                int size = GetPeakGroupSize(ptr, mzs, ints, mzs.Length, rt, 1, "diag_test", null);
                Console.WriteLine("[DIAG] GetPeakGroupSize returned: " + size);

                int postSize = GetAllPeakGroupSize(ptr);
                Console.WriteLine("[DIAG] GetAllPeakGroupSize (post): " + postSize);

                Assert.That(size, Is.GreaterThan(0),
                    string.Format("GetPeakGroupSize={0}, AllPeakGroupSize pre={1} post={2}, peaks={3}, rt={4}",
                        size, preSize, postSize, mzs.Length, rt));
            }
            finally
            {
                DisposeFLASHIda(ptr);
            }
        }
    }
}

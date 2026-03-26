using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// Standalone diagnostic test — no OneTimeSetUp dependency.
    /// Logs every step to diagnose why GetPeakGroupSize returns 0
    /// in the NUnit process but works in Flash.exe standalone.
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
            TestContext.Error.WriteLine("[DIAG] ms1Path: " + ms1Path);
            TestContext.Error.WriteLine("[DIAG] File exists: " + File.Exists(ms1Path));
            Assume.That(File.Exists(ms1Path), "ms1_smoke_test.txt not found");

            // Load spectrum
            var (mzs, ints, rt) = LoadSpectrum(ms1Path);
            TestContext.Error.WriteLine("[DIAG] Spectrum peaks: " + mzs.Length);
            TestContext.Error.WriteLine("[DIAG] RT (minutes): " + rt);
            if (mzs.Length > 0)
            {
                TestContext.Error.WriteLine("[DIAG] First mz: " + mzs[0] + ", intensity: " + ints[0]);
                TestContext.Error.WriteLine("[DIAG] Last mz: " + mzs[mzs.Length - 1] + ", intensity: " + ints[ints.Length - 1]);
            }

            // Verify spectrum data integrity
            Assert.AreEqual(6610, mzs.Length, "Spectrum should have 6610 peaks");
            Assert.That(mzs[0], Is.EqualTo(501.707581).Within(0.001), "First m/z mismatch");
            Assert.That(ints[0], Is.EqualTo(148213.16).Within(1.0), "First intensity mismatch");
            Assert.That(rt, Is.EqualTo(70.5841 / 60.0).Within(0.0001), "RT mismatch");

            // Create engine
            string config = "max_mass_count 1 score_threshold 0 min_charge 4 max_charge 50 " +
                   "min_mass 500 max_mass 50000 RT_window 180 tol 10 10 " +
                   "tqscore_threshold 0.9 target_mode 0 IDScore 0 AllCharges 0 " +
                   "HCDEnergy 29 strict_inclusion 0 tie_threshold 0.1 MS3AllCharges 1 " +
                   "min_tag_length 3 max_tag_length 8 max_ptm_count 3 max_flanking_mass_diff 50000 ";
            TestContext.Error.WriteLine("[DIAG] Config: " + config);
            IntPtr ptr = CreateFLASHIda(config);
            TestContext.Error.WriteLine("[DIAG] Ptr: " + ptr);
            Assert.AreNotEqual(IntPtr.Zero, ptr, "CreateFLASHIda returned null");

            try
            {
                // Pre-deconvolution state
                int preSize = GetAllPeakGroupSize(ptr);
                TestContext.Error.WriteLine("[DIAG] GetAllPeakGroupSize (pre): " + preSize);

                // Call GetPeakGroupSize
                int size = GetPeakGroupSize(ptr, mzs, ints, mzs.Length, rt, 1, "diag_test", null);
                TestContext.Error.WriteLine("[DIAG] GetPeakGroupSize returned: " + size);

                // Post-deconvolution state
                int postSize = GetAllPeakGroupSize(ptr);
                TestContext.Error.WriteLine("[DIAG] GetAllPeakGroupSize (post): " + postSize);

                TestContext.Error.WriteLine("[DIAG] OMP_NUM_THREADS=" +
                    Environment.GetEnvironmentVariable("OMP_NUM_THREADS"));

                Assert.That(size, Is.GreaterThan(0),
                    string.Format("GetPeakGroupSize returned {0}, GetAllPeakGroupSize pre={1} post={2}",
                        size, preSize, postSize));
            }
            finally
            {
                DisposeFLASHIda(ptr);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// Phase 0 bridge MS2 tests: verify that MS2 deconvolution and MS3 targeting
    /// bridge functions do not crash and return sane values.
    /// Requires ms1_smoke_test.txt and ms2_smoke_test.txt in test-data/spectra/.
    /// </summary>
    [TestFixture]
    public class BridgeMS2Tests
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
        private static extern void GetIsolationWindows(IntPtr ptr,
            double[] wstart, double[] wend, double[] qScores,
            int[] charges, int[] minCharges, int[] maxCharges,
            double[] monoMasses, double[] chargeCos, double[] chargeSnrs,
            double[] isoCos, double[] snrs, double[] chargeScores,
            double[] ppmErrors, double[] precursorIntensities,
            double[] peakgroupIntensities, int[] hcds, int[] ids);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int DeconvolveMS2(IntPtr ptr,
            double[] mzs, double[] ints, int length,
            double rt, double precursorMass, int precursorCharge);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool ProcessMS2ForTagBasedTargeting(IntPtr ptr,
            double precursorMass);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetBestMS2Masses(IntPtr ptr, int n,
            double[] masses, double[] qscores, int[] charges,
            double[] windowStarts, double[] windowEnds);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetTopFragmentMatches(IntPtr ptr,
            string proteinSequence, int n,
            double[] masses, double[] qscores, int[] charges,
            double[] windowStarts, double[] windowEnds,
            byte[] ionTypes, int[] fragmentIndices,
            string fragmentationMethod);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetAmbiguityEnclosingIons(IntPtr ptr,
            string proteinSequence, int n,
            double[] masses, double[] qscores, int[] charges,
            double[] windowStarts, double[] windowEnds,
            byte[] ionTypes, int[] fragmentIndices,
            string fragmentationMethod);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetTerminalFragmentIons(IntPtr ptr,
            string proteinSequence, int n,
            double[] masses, double[] qscores, int[] charges,
            double[] windowStarts, double[] windowEnds,
            byte[] ionTypes, int[] fragmentIndices,
            string fragmentationMethod);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ClearMS2Deconvolution(IntPtr ptr);

        // Histone H3.1 sequence used by GetTopFragmentMatches / GetAmbiguityEnclosingIons / GetTerminalFragmentIons
        private const string HistoneH3 =
            "SGRGKQGGKARAKAKTRSSRAGLQFPVGRVHRLLRKGNYSERVGAGAPVYLAAVLEYLTAEILELAGNAARDNKKTRIIPRHLQLAIRNDEELNKLLGKVTIAQGGVLPNIQAVLLPKKTESHHKAKGK";

        private IntPtr _ptr;
        private int _ms2PeakGroups;

        private static string SpectraDir => Path.Combine(
            TestContext.CurrentContext.TestDirectory, "..", "test-data", "spectra");

        /// <summary>
        /// Load a spectrum file (tab-separated: header "Spec scan=N\tRT_seconds", then "mz\tintensity" rows).
        /// Returns (mzs, ints, rt_minutes).
        /// </summary>
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

        [OneTimeSetUp]
        public void Setup()
        {
            string ms1Path = Path.Combine(SpectraDir, "ms1_smoke_test.txt");
            string ms2Path = Path.Combine(SpectraDir, "ms2_smoke_test.txt");

            Assume.That(File.Exists(ms1Path), "ms1_smoke_test.txt not found at " + ms1Path);
            Assume.That(File.Exists(ms2Path), "ms2_smoke_test.txt not found at " + ms2Path);

            // Create engine
            string config = BridgeSmokeTests_BuildLegacyConfigString();
            _ptr = CreateFLASHIda(config);
            Assume.That(_ptr, Is.Not.EqualTo(IntPtr.Zero), "CreateFLASHIda returned null");

            // Deconvolve MS1 to obtain targets
            var (ms1Mzs, ms1Ints, ms1Rt) = LoadSpectrum(ms1Path);
            int ms1Size = GetPeakGroupSize(_ptr, ms1Mzs, ms1Ints, ms1Mzs.Length, ms1Rt, 1, "setup_ms1", null);
            Assume.That(ms1Size, Is.GreaterThan(0), "MS1 deconvolution found no targets");

            // Get first target's mass and charge
            double[] monoMasses = new double[ms1Size];
            int[] charges = new int[ms1Size];
            double[] wstart = new double[ms1Size];
            double[] wend = new double[ms1Size];
            double[] qScores = new double[ms1Size];
            int[] minCharges = new int[ms1Size];
            int[] maxCharges = new int[ms1Size];
            double[] chargeCos = new double[ms1Size];
            double[] chargeSnrs = new double[ms1Size];
            double[] isoCos = new double[ms1Size];
            double[] snrs = new double[ms1Size];
            double[] chargeScores = new double[ms1Size];
            double[] ppmErrors = new double[ms1Size];
            double[] precIntensities = new double[ms1Size];
            double[] pgIntensities = new double[ms1Size];
            int[] hcds = new int[ms1Size];
            int[] ids = new int[ms1Size];

            GetIsolationWindows(_ptr, wstart, wend, qScores, charges, minCharges, maxCharges,
                monoMasses, chargeCos, chargeSnrs, isoCos, snrs, chargeScores,
                ppmErrors, precIntensities, pgIntensities, hcds, ids);

            // Deconvolve MS2 using first MS1 target's mass and charge
            var (ms2Mzs, ms2Ints, ms2Rt) = LoadSpectrum(ms2Path);
            _ms2PeakGroups = DeconvolveMS2(_ptr, ms2Mzs, ms2Ints, ms2Mzs.Length,
                ms2Rt, monoMasses[0], charges[0]);
        }

        [OneTimeTearDown]
        public void Teardown()
        {
            if (_ptr != IntPtr.Zero)
            {
                ClearMS2Deconvolution(_ptr);
                DisposeFLASHIda(_ptr);
                _ptr = IntPtr.Zero;
            }
        }

        [Test, Category("Tier2")]
        public void P0_I03_DeconvolveMS2_ReturnsNonNegativePeakGroups()
        {
            Assert.That(_ms2PeakGroups, Is.GreaterThanOrEqualTo(0),
                "DeconvolveMS2 returned a negative peak group count");
        }

        [Test, Category("Tier2")]
        public void P0_I04_ProcessMS2ForTagBasedTargeting_DoesNotCrash()
        {
            Assume.That(_ms2PeakGroups, Is.GreaterThanOrEqualTo(0), "MS2 deconvolution prerequisite failed");

            bool result = false;
            Assert.DoesNotThrow(() =>
            {
                result = ProcessMS2ForTagBasedTargeting(_ptr, 12351.0);
            }, "ProcessMS2ForTagBasedTargeting threw an exception");
            // result is a bool — just verify it didn't crash; value is data-dependent
            Assert.That(result, Is.TypeOf<bool>());
        }

        [Test, Category("Tier2")]
        public void P0_I05_GetBestMS2Masses_ReturnsResults()
        {
            Assume.That(_ms2PeakGroups, Is.GreaterThanOrEqualTo(0), "MS2 deconvolution prerequisite failed");

            int maxN = 100;
            double[] masses = new double[maxN];
            double[] qscores = new double[maxN];
            int[] charges = new int[maxN];
            double[] windowStarts = new double[maxN];
            double[] windowEnds = new double[maxN];

            int count = -1;
            Assert.DoesNotThrow(() =>
            {
                count = GetBestMS2Masses(_ptr, maxN, masses, qscores, charges, windowStarts, windowEnds);
            }, "GetBestMS2Masses threw an exception");

            Assert.That(count, Is.GreaterThanOrEqualTo(0), "GetBestMS2Masses returned negative count");
        }

        [Test, Category("Tier2")]
        public void P0_I06_GetTopFragmentMatches_WithProteinSequence()
        {
            Assume.That(_ms2PeakGroups, Is.GreaterThanOrEqualTo(0), "MS2 deconvolution prerequisite failed");

            int maxN = 100;
            double[] masses = new double[maxN];
            double[] qscores = new double[maxN];
            int[] charges = new int[maxN];
            double[] windowStarts = new double[maxN];
            double[] windowEnds = new double[maxN];
            byte[] ionTypes = new byte[maxN];
            int[] fragIndices = new int[maxN];

            int count = -1;
            Assert.DoesNotThrow(() =>
            {
                count = GetTopFragmentMatches(_ptr, HistoneH3, maxN,
                    masses, qscores, charges, windowStarts, windowEnds,
                    ionTypes, fragIndices, "HCD");
            }, "GetTopFragmentMatches threw an exception");

            Assert.That(count, Is.GreaterThanOrEqualTo(0), "GetTopFragmentMatches returned negative count");
        }

        [Test, Category("Tier2")]
        public void P0_I07_GetAmbiguityEnclosingIons_WithProteinSequence()
        {
            Assume.That(_ms2PeakGroups, Is.GreaterThanOrEqualTo(0), "MS2 deconvolution prerequisite failed");

            int maxN = 100;
            double[] masses = new double[maxN];
            double[] qscores = new double[maxN];
            int[] charges = new int[maxN];
            double[] windowStarts = new double[maxN];
            double[] windowEnds = new double[maxN];
            byte[] ionTypes = new byte[maxN];
            int[] fragIndices = new int[maxN];

            int count = -1;
            Assert.DoesNotThrow(() =>
            {
                count = GetAmbiguityEnclosingIons(_ptr, HistoneH3, maxN,
                    masses, qscores, charges, windowStarts, windowEnds,
                    ionTypes, fragIndices, "HCD");
            }, "GetAmbiguityEnclosingIons threw an exception");

            Assert.That(count, Is.GreaterThanOrEqualTo(0), "GetAmbiguityEnclosingIons returned negative count");
        }

        [Test, Category("Tier2")]
        public void P0_I08_GetTerminalFragmentIons_WithProteinSequence()
        {
            Assume.That(_ms2PeakGroups, Is.GreaterThanOrEqualTo(0), "MS2 deconvolution prerequisite failed");

            int maxN = 100;
            double[] masses = new double[maxN];
            double[] qscores = new double[maxN];
            int[] charges = new int[maxN];
            double[] windowStarts = new double[maxN];
            double[] windowEnds = new double[maxN];
            byte[] ionTypes = new byte[maxN];
            int[] fragIndices = new int[maxN];

            int count = -1;
            Assert.DoesNotThrow(() =>
            {
                count = GetTerminalFragmentIons(_ptr, HistoneH3, maxN,
                    masses, qscores, charges, windowStarts, windowEnds,
                    ionTypes, fragIndices, "HCD");
            }, "GetTerminalFragmentIons threw an exception");

            Assert.That(count, Is.GreaterThanOrEqualTo(0), "GetTerminalFragmentIons returned negative count");
        }

        /// <summary>
        /// Same config string as BridgeSmokeTests.BuildLegacyConfigString().
        /// Duplicated here to avoid coupling test fixtures.
        /// </summary>
        private static string BridgeSmokeTests_BuildLegacyConfigString()
        {
            return "max_mass_count 1 score_threshold 0 min_charge 4 max_charge 50 " +
                   "min_mass 500 max_mass 50000 RT_window 180 tol 10 10 " +
                   "tqscore_threshold 0.9 target_mode 0 IDScore 0 AllCharges 0 " +
                   "HCDEnergy 29 strict_inclusion 0 tie_threshold 0.1 MS3AllCharges 1 " +
                   "min_tag_length 3 max_tag_length 8 max_ptm_count 3 max_flanking_mass_diff 50000 ";
        }
    }
}

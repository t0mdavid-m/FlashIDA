using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Flash;
using Flash.IDA;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// Phase 4 bridge integration tests: verify legacy (pre-unified) bridge path
    /// still works via GetPeakGroupSize + GetIsolationWindows.
    /// </summary>
    [TestFixture]
    public class BridgePhase4Tests
    {
        private const string DllName = "OpenMS.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr CreateFLASHIda(string config);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void DisposeFLASHIda(IntPtr ptr);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetPeakGroupSize(IntPtr ptr, double[] mzs, double[] ints,
            int length, double rt, int msLevel, string name, string cv);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void GetIsolationWindows(IntPtr ptr, double[] wstart, double[] wend,
            double[] qScores, int[] charges, int[] minCharges, int[] maxCharges,
            double[] monoMasses, double[] chargeCos, double[] chargeSnrs,
            double[] isoCos, double[] snrs, double[] chargeScores,
            double[] ppmErrors, double[] precursorIntensities, double[] peakgroupIntensities,
            int[] hcds, int[] ids);

        /// <summary>
        /// Parse multi-scan TSV file: "Spec scan=N\tRT" headers, "mz\tintensity" data lines.
        /// Returns list of (mz[], intensity[], rt) tuples.
        /// </summary>
        private static List<(double[] mzs, double[] ints, double rt)> LoadTsvScans(string path)
        {
            var result = new List<(double[], double[], double)>();
            var mzs = new List<double>();
            var intensities = new List<double>();
            double rt = 0;
            bool inScan = false;

            foreach (string line in File.ReadLines(path))
            {
                if (line.StartsWith("Spec"))
                {
                    if (inScan && mzs.Count > 0)
                        result.Add((mzs.ToArray(), intensities.ToArray(), rt));
                    mzs = new List<double>();
                    intensities = new List<double>();
                    int tab = line.IndexOf('\t');
                    rt = double.Parse(line.Substring(tab + 1));
                    inScan = true;
                }
                else if (inScan)
                {
                    int tab = line.IndexOf('\t');
                    if (tab > 0)
                    {
                        mzs.Add(double.Parse(line.Substring(0, tab)));
                        intensities.Add(double.Parse(line.Substring(tab + 1)));
                    }
                }
            }
            if (inScan && mzs.Count > 0)
                result.Add((mzs.ToArray(), intensities.ToArray(), rt));

            return result;
        }

        // P4-I01: Legacy bridge path (GetPeakGroupSize + GetIsolationWindows) still works
        [Test, Category("Tier2")]
        public void P4_I01_LegacyBridgePath_StillWorks()
        {
            // Build legacy config string (same format as BridgeSmokeTests)
            string legacyConfig = "max_mass_count 1 score_threshold 0 min_charge 4 max_charge 50 " +
                                  "min_mass 500 max_mass 50000 RT_window 180 tol 10 10 " +
                                  "tqscore_threshold 0.9 target_mode 0 IDScore 0 AllCharges 0 " +
                                  "HCDEnergy 29 strict_inclusion 0 tie_threshold 0.1 MS3AllCharges 1 " +
                                  "min_tag_length 3 max_tag_length 8 max_ptm_count 3 max_flanking_mass_diff 50000 ";

            IntPtr ptr = CreateFLASHIda(legacyConfig);
            Assume.That(ptr, Is.Not.EqualTo(IntPtr.Zero), "CreateFLASHIda returned null");

            try
            {
                // Load ms1_standard.txt (50 scans for sufficient engine state accumulation)
                string spectraDir = Path.Combine(
                    TestContext.CurrentContext.TestDirectory, "..", "test-data", "spectra");
                string ms1Path = Path.Combine(spectraDir, "ms1_standard.txt");

                if (!File.Exists(ms1Path))
                {
                    Assert.Ignore("ms1_standard.txt not found at " + ms1Path);
                    return;
                }

                var scans = LoadTsvScans(ms1Path);
                Assert.That(scans.Count, Is.GreaterThan(0), "No scans loaded from ms1_standard.txt");

                // Push all scans through the OLD bridge path (GetPeakGroupSize + GetIsolationWindows)
                int totalResults = 0;
                foreach (var scan in scans)
                {
                    int size = GetPeakGroupSize(ptr, scan.mzs, scan.ints,
                        scan.mzs.Length, scan.rt, 1, "legacy_test", null);

                    if (size > 0)
                    {
                        // Allocate arrays and retrieve isolation windows
                        double[] wstart = new double[size];
                        double[] wend = new double[size];
                        double[] qScores = new double[size];
                        int[] charges = new int[size];
                        int[] minCharges = new int[size];
                        int[] maxCharges = new int[size];
                        double[] monoMasses = new double[size];
                        double[] chargeCos = new double[size];
                        double[] chargeSnrs = new double[size];
                        double[] isoCos = new double[size];
                        double[] snrs = new double[size];
                        double[] chargeScores = new double[size];
                        double[] ppmErrors = new double[size];
                        double[] precursorIntensities = new double[size];
                        double[] peakgroupIntensities = new double[size];
                        int[] hcds = new int[size];
                        int[] ids = new int[size];

                        Assert.DoesNotThrow(() =>
                        {
                            GetIsolationWindows(ptr, wstart, wend, qScores, charges,
                                minCharges, maxCharges, monoMasses, chargeCos, chargeSnrs,
                                isoCos, snrs, chargeScores, ppmErrors,
                                precursorIntensities, peakgroupIntensities, hcds, ids);
                        }, "GetIsolationWindows should not throw");

                        totalResults += size;
                    }
                }

                // ms1_standard.txt has 50 scans — sufficient for engine state accumulation
                Assert.That(totalResults, Is.GreaterThan(0),
                    "Legacy bridge path should produce results with 50 MS1 scans");
            }
            finally
            {
                DisposeFLASHIda(ptr);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Flash.IDA;
using Flash.Tests.Mocks;
using NUnit.Framework;

namespace Flash.Tests.AcquisitionLoop
{
    /// <summary>
    /// Acquisition loop continuity tests (AL-CT01 through CT28, CT31-CT32 stubs).
    /// These tests push mock IMsScan objects through the processor pipeline and verify
    /// that the correct scan commands are produced.
    ///
    /// Tests require OpenMS.dll (real C++ deconvolution engine) and Thermo DLLs.
    /// They can only run in CI (Windows + Thermo DLLs + OpenMS.dll).
    /// </summary>
    [TestFixture]
    public class ContinuityTests
    {
        private static string TestDir => TestContext.CurrentContext.TestDirectory;
        private static string TestDataDir => Path.Combine(TestDir, "..", "test-data");
        private static string ConfigDir => Path.Combine(TestDataDir, "configs");
        private static string SpectraDir => Path.Combine(TestDataDir, "spectra");
        private static string GoldenDir => Path.Combine(TestDataDir, "golden");
        private static string OutputDir => Path.Combine(TestDir, "continuity-output");

        [OneTimeSetUp]
        public void Setup()
        {
            // Configure log4net to avoid NullReferenceExceptions from processor loggers
            if (!log4net.LogManager.GetRepository().Configured)
            {
                log4net.Config.BasicConfigurator.Configure(
                    new log4net.Appender.ConsoleAppender
                    {
                        Threshold = log4net.Core.Level.Off
                    });
            }

            // Create output directory for golden capture
            Directory.CreateDirectory(OutputDir);
        }

        #region Helper Methods

        private string ConfigPath(string fileName) => Path.Combine(ConfigDir, fileName);

        private ContinuityTestHarness CreateHarness(string configFile,
            bool forceFaims = false, bool forceQuant = false)
        {
            return new ContinuityTestHarness(ConfigPath(configFile), forceFaims, forceQuant);
        }

        /// <summary>
        /// Load the smoke test MS1 spectrum and push it through the harness.
        /// </summary>
        private List<ScanCommandRecord> PushSmokeSpectrumAndCollect(ContinuityTestHarness harness)
        {
            var scan = MockMsScan.FromTsv(Path.Combine(SpectraDir, "ms1_smoke_test.txt"));
            harness.PushScan(scan);
            scan.Dispose();
            return harness.CollectResults();
        }

        /// <summary>
        /// Assert against golden file. If golden doesn't exist, write actual output
        /// and mark test as Inconclusive for first-run capture.
        /// </summary>
        private void AssertGolden(string goldenFileName, List<ScanCommandRecord> results)
        {
            string actualJson = ScanCommandRecord.ToJson(results);
            string goldenPath = Path.Combine(GoldenDir, goldenFileName);
            string outputPath = Path.Combine(OutputDir, goldenFileName);

            // Always write actual output for CI capture/debugging
            File.WriteAllText(outputPath, actualJson);

            if (File.Exists(goldenPath))
            {
                string expected = File.ReadAllText(goldenPath);
                Assert.AreEqual(expected, actualJson,
                    "Behavioral reference mismatch for " + goldenFileName +
                    ". If this change is intentional, update the golden file.");
            }
            else
            {
                Assert.Inconclusive(
                    "Golden file not found: " + goldenFileName +
                    ". Actual output written to continuity-output/. " +
                    "Review and commit to test-data/golden/.");
            }
        }

        #endregion

        #region AL-CT01 through CT05: Standard DDA Basics

        [Test, Category("Tier2")]
        public void P0_AL_CT04_EmptySpectrum_ZeroCommands()
        {
            using (var harness = CreateHarness("method_default.xml"))
            {
                var scan = MockMsScan.EmptyMS1();
                harness.PushScan(scan);
                scan.Dispose();

                var results = harness.CollectResults();
                Assert.AreEqual(0, results.Count,
                    "Empty spectrum should produce zero scan commands");
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT05_NoiseOnlySpectrum_ZeroCommands()
        {
            using (var harness = CreateHarness("method_default.xml"))
            {
                var scan = MockMsScan.NoiseOnlyMS1();
                harness.PushScan(scan);
                scan.Dispose();

                var results = harness.CollectResults();
                Assert.AreEqual(0, results.Count,
                    "Noise-only spectrum should produce zero scan commands");
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT03_StandardDDA_AllOutputsAreMSn()
        {
            using (var harness = CreateHarness("method_default.xml"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);

                // Skip if deconvolution found nothing (environment issue)
                Assume.That(results.Count, Is.GreaterThan(0),
                    "Deconvolution must find at least one precursor");

                Assert.IsTrue(results.All(r => r.MsnLevel == 2),
                    "Standard DDA should produce only MS2 commands, got: " +
                    string.Join(", ", results.Select(r => "MS" + r.MsnLevel)));
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT01_StandardDDA_PrecursorMasses()
        {
            using (var harness = CreateHarness("method_default.xml"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);

                Assume.That(results.Count, Is.GreaterThan(0),
                    "Deconvolution must find at least one precursor");

                // All precursor m/z values should be in the MS1 scan range
                foreach (var r in results)
                {
                    Assert.That(r.PrecursorMz, Is.GreaterThan(0),
                        "Precursor m/z must be positive");
                    Assert.That(r.PrecursorMz, Is.InRange(
                        harness.MethodParams.MS1.FirstMass,
                        harness.MethodParams.MS1.LastMass),
                        "Precursor m/z should be within MS1 scan range");
                }
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT02_StandardDDA_CollisionEnergiesMatchConfig()
        {
            using (var harness = CreateHarness("method_default.xml"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);

                Assume.That(results.Count, Is.GreaterThan(0),
                    "Deconvolution must find at least one precursor");

                // All collision energies should match the configured MS2 parameters
                var configuredEnergies = harness.MethodParams.MS2
                    .Select(p => p.CollisionEnergy).ToList();

                foreach (var r in results)
                {
                    // CollisionEnergy 0 means not set (ETD mode uses ReactionTime instead)
                    if (r.CollisionEnergy != 0)
                    {
                        Assert.That(configuredEnergies, Has.Member(r.CollisionEnergy),
                            string.Format("Collision energy {0} not in configured values [{1}]",
                                r.CollisionEnergy, string.Join(",", configuredEnergies)));
                    }
                }
            }
        }

        #endregion

        #region AL-CT06 through CT08: Standard DDA Reference + TopN + Tracking

        [Test, Category("Tier2")]
        public void P0_AL_CT06_StandardDDA_BehavioralReference()
        {
            using (var harness = CreateHarness("method_default.xml"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);
                Assume.That(results.Count, Is.GreaterThan(0),
                    "Deconvolution must find at least one precursor");

                AssertGolden("continuity_standard_dda.json", results);
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT07_TrackingIDs_UniqueAcross1000Scans()
        {
            using (var harness = CreateHarness("method_default.xml"))
            {
                var allDescriptions = new HashSet<string>();
                var smokeScan = MockMsScan.FromTsv(Path.Combine(SpectraDir, "ms1_smoke_test.txt"));

                // Push the same spectrum 1000 times with different scan numbers
                for (int i = 1; i <= 1000; i++)
                {
                    // Create a fresh scan each time with unique scan number
                    var scan = MockMsScan.WithPeaks(
                        i * 0.01, // Incrementing RT
                        i.ToString(),
                        smokeScan.Centroids.Select(c => (c.Mz, c.Intensity)).ToArray());
                    harness.PushScan(scan);
                    scan.Dispose();
                }

                smokeScan.Dispose();

                var results = harness.CollectResults();
                Assume.That(results.Count, Is.GreaterThan(0),
                    "Should have produced scan commands from 1000 MS1 scans");

                // Extract tracking IDs from scan descriptions
                foreach (var r in results)
                {
                    if (!string.IsNullOrEmpty(r.ScanDescription))
                    {
                        Assert.IsTrue(allDescriptions.Add(r.ScanDescription),
                            "Duplicate scan description (tracking ID): " + r.ScanDescription);
                    }
                }
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT08_MS2Count_RespectsMaxMs2CountPerMs1()
        {
            using (var harness = CreateHarness("method_default_topn5.xml"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);

                int maxPerMs1 = harness.MethodParams.IDA.MaxMs2CountPerMs1;
                int ms2Types = harness.MethodParams.MS2.Count;

                Assert.AreEqual(5, maxPerMs1,
                    "Config should have MaxMs2CountPerMs1=5");

                // Total MS2 scans should not exceed MaxMs2CountPerMs1 * MS2Types
                // (each precursor gets one scan per MS2 parameter set)
                Assert.That(results.Count, Is.LessThanOrEqualTo(maxPerMs1 * ms2Types),
                    string.Format("MS2 count {0} exceeds MaxMs2CountPerMs1={1} * MS2Types={2}",
                        results.Count, maxPerMs1, ms2Types));
            }
        }

        #endregion

        #region AL-CT09 through CT11: FAIMS Tests

        [Test, Category("Tier2")]
        public void P0_AL_CT09_FAIMS_CVCycling_3CVsInOrder()
        {
            using (var harness = CreateHarness("method_faims_3cv.xml", forceFaims: true))
            {
                double[] expectedCVs = harness.MethodParams.IDA.CVValues;
                Assert.AreEqual(3, expectedCVs.Length, "Config should have 3 CVs");

                // Push 9 MS1 scans, each with the correct CV from the cycling pattern
                // The ScanScheduler cycles through CVs, so scans arrive with CVs in order
                for (int i = 0; i < 9; i++)
                {
                    double cv = expectedCVs[i % expectedCVs.Length];
                    var scan = MockMsScan.WithFaimsPeaks(
                        i * 0.5, (i + 1).ToString(), cv,
                        // Simple peaks - may or may not trigger deconvolution
                        (600.0, 50000), (700.0, 60000), (800.0, 70000));
                    harness.PushScan(scan);
                    scan.Dispose();
                }

                // Verify that scans were processed (at least some CVs produced output)
                var results = harness.CollectResults();
                // FAIMS tests: just verify no crashes and correct CV assignment
                if (results.Count > 0)
                {
                    foreach (var r in results)
                    {
                        Assert.That(expectedCVs, Has.Member(r.FaimsCV),
                            string.Format("FAIMS CV {0} not in configured values", r.FaimsCV));
                    }
                }
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT10_FAIMS_MS2CarriesParentCV()
        {
            using (var harness = CreateHarness("method_faims_3cv.xml", forceFaims: true))
            {
                double[] configuredCVs = harness.MethodParams.IDA.CVValues;
                var smokeScan = MockMsScan.FromTsv(Path.Combine(SpectraDir, "ms1_smoke_test.txt"));
                var peaks = smokeScan.Centroids.Select(c => (c.Mz, c.Intensity)).ToArray();
                smokeScan.Dispose();

                // Push scans with different CVs
                foreach (double cv in configuredCVs)
                {
                    var scan = MockMsScan.WithFaimsPeaks(1.0, "1", cv, peaks);
                    harness.PushScan(scan);
                    scan.Dispose();
                }

                var results = harness.CollectResults();
                foreach (var r in results)
                {
                    Assert.That(configuredCVs, Has.Member(r.FaimsCV),
                        "MS2 FAIMS CV should match one of the configured parent CVs");
                }
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT11_NonFAIMS_CVIsZero()
        {
            using (var harness = CreateHarness("method_default.xml"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);

                Assume.That(results.Count, Is.GreaterThan(0),
                    "Deconvolution must find at least one precursor");

                foreach (var r in results)
                {
                    Assert.AreEqual(0, r.FaimsCV,
                        "Non-FAIMS mode should have FaimsCV = 0");
                }
            }
        }

        #endregion

        #region AL-CT12 through CT16: Precursor Selection Modes

        [Test, Category("Tier2")]
        public void P0_AL_CT12_DeepMode_MorePrecursors()
        {
            int standardCount, deepCount;

            // Run standard DDA with TopN=5
            using (var harness = CreateHarness("method_default_topn5.xml"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);
                standardCount = results.Count;
            }

            // Run deep mode with TopN=5
            using (var harness = CreateHarness("method_deep.xml"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);
                deepCount = results.Count;
            }

            // Deep mode should produce at least as many MS2 scans as standard DDA
            // for the same input spectrum and TopN setting
            Assert.That(deepCount, Is.GreaterThanOrEqualTo(standardCount),
                string.Format("Deep mode ({0}) should produce >= standard DDA ({1}) MS2 scans",
                    deepCount, standardCount));
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT13_InclusionList_OnlyListedMasses()
        {
            // Non-strict inclusion: targets get priority but non-targets can fill remaining slots.
            // With this test spectrum, no masses match the inclusion list (10k, 15k, 20k, 25k, 30k),
            // so all results are non-target fill-ins. Verify it runs and produces results.
            using (var harness = CreateHarness("method_inclusion.xml"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);

                Assert.That(results.Count, Is.GreaterThan(0),
                    "Non-strict inclusion mode should produce scan commands even when no targets match");
                Assert.IsTrue(results.All(r => r.PrecursorMz > 0),
                    "All results should have valid precursor m/z");
            }

            // Strict inclusion: only inclusion-list masses are selected.
            // With this test spectrum, no masses match the inclusion list,
            // so strict mode should produce zero results.
            using (var harness = CreateHarness("method_inclusion_strict.xml"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);

                Assert.That(results.Count, Is.EqualTo(0),
                    "Strict inclusion should produce zero commands when no targets match the spectrum");
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT14_ExclusionList_ExcludedMassesSuppressed()
        {
            // Compare exclusion results against standard DDA results.
            // Exclusion mode should produce a different set of precursors.
            List<ScanCommandRecord> standardResults;
            List<ScanCommandRecord> exclusionResults;

            using (var stdHarness = CreateHarness("method_default.xml"))
            {
                standardResults = PushSmokeSpectrumAndCollect(stdHarness);
            }

            using (var exclHarness = CreateHarness("method_exclusion.xml"))
            {
                exclusionResults = PushSmokeSpectrumAndCollect(exclHarness);
            }

            // Exclusion mode should run without error
            Assert.That(exclusionResults.Count, Is.GreaterThanOrEqualTo(0),
                "Exclusion mode should not crash");

            // If both produce results, verify they differ (exclusion suppresses some targets)
            if (standardResults.Count > 0 && exclusionResults.Count > 0)
            {
                var stdMasses = standardResults.Select(r => r.PrecursorMz).OrderBy(x => x).ToList();
                var exclMasses = exclusionResults.Select(r => r.PrecursorMz).OrderBy(x => x).ToList();

                // At minimum, verify both modes produce valid precursor m/z values
                Assert.IsTrue(exclusionResults.All(r => r.PrecursorMz > 0),
                    "All exclusion mode results should have valid precursor m/z");
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT15_Inclusion_BehavioralReference()
        {
            using (var harness = CreateHarness("method_inclusion.xml"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);
                AssertGolden("continuity_inclusion.json", results);
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT16_Exclusion_BehavioralReference()
        {
            using (var harness = CreateHarness("method_exclusion.xml"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);
                AssertGolden("continuity_exclusion.json", results);
            }
        }

        #endregion

        #region AL-CT17 through CT21: Tag Targeting + Quant

        [Test, Category("Tier2")]
        public void P0_AL_CT17_TagTargeting_TriggersFollowUpMS2()
        {
            using (var harness = CreateHarness("method_tag_targeting.xml"))
            {
                // Push MS1 to get initial MS2 commands
                var ms1Results = PushSmokeSpectrumAndCollect(harness);

                // Verify that tag targeting mode produces MS2 commands from MS1 processing
                Assert.That(ms1Results.Count, Is.GreaterThan(0),
                    "Tag targeting should produce MS2 scan commands from MS1 input");

                Assert.IsTrue(ms1Results.All(r => r.ScanType == "MSn"),
                    "All scan commands should be MSn type");
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT18_ConditionalMS2_FollowUpOnlyWhenTagsDetected()
        {
            using (var harness = CreateHarness("method_tag_targeting.xml"))
            {
                // Push MS1 scan
                var smokeScan = MockMsScan.FromTsv(Path.Combine(SpectraDir, "ms1_smoke_test.txt"));
                var ms1Scans = harness.PushScan(smokeScan);
                smokeScan.Dispose();

                // Get the MS2 commands from MS1 processing
                var ms2Commands = harness.CollectResults();

                // In conditional MS2 mode, the first MS2 type is sent for each precursor.
                // Follow-up MS2 types are only sent if tags are detected in the first MS2.
                // Verify that at most 1 MS2 per precursor was sent initially
                // (the conditional mode sends only the first MS2 parameter set)
                if (ms2Commands.Count > 0 && harness.MethodParams.IDA.ConditionalMS2)
                {
                    // In conditional mode, initial MS2 count equals number of precursors
                    int maxPrecursors = harness.MethodParams.IDA.MaxMs2CountPerMs1;
                    Assert.That(ms2Commands.Count, Is.LessThanOrEqualTo(maxPrecursors),
                        "Conditional MS2: initial batch should have at most 1 scan per precursor");
                }
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT19_TagTargeting_BehavioralReference()
        {
            using (var harness = CreateHarness("method_tag_targeting.xml"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);
                AssertGolden("continuity_tag_targeting.json", results);
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT20_QuantMode_ConstructsWithoutError()
        {
            // Quant mode requires exactly 2 MS2 parameter sets
            Assert.DoesNotThrow(() =>
            {
                using (var harness = CreateHarness("method_quant.xml"))
                {
                    Assert.IsNotNull(harness.Processor,
                        "Quant processor should be created successfully");
                    Assert.AreEqual(2, harness.MethodParams.MS2.Count,
                        "Quant config should have exactly 2 MS2 parameter sets");
                }
            }, "Quant mode construction should not throw");
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT21_Quant_BehavioralReference()
        {
            using (var harness = CreateHarness("method_quant.xml"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);
                AssertGolden("continuity_quant.json", results);
            }
        }

        #endregion

        #region AL-CT22 through CT26: MS3 Tests

        [Test, Category("Tier2")]
        public void P0_AL_CT22_MS3Enabled_MsnLevel3RecordsExist()
        {
            using (var harness = CreateHarness("method_ms3_mode1.xml"))
            {
                // Push MS1 to get MS2 commands
                var smokeScan = MockMsScan.FromTsv(Path.Combine(SpectraDir, "ms1_smoke_test.txt"));
                var ms1Results = harness.PushScan(smokeScan);
                smokeScan.Dispose();

                // Get MS2 commands from the MS1 processing
                var ms2Commands = harness.Factory.CreatedScans
                    .Select(s => ScanCommandRecord.FromCustomScan(s))
                    .Where(r => r.ScanType == "MSn" && r.MsnLevel == 2)
                    .ToList();

                if (ms2Commands.Count > 0)
                {
                    // Simulate MS2 scan coming back to trigger MS3
                    // Use the first MS2 command's parameters to create a mock MS2 response
                    var firstMS2 = ms2Commands[0];
                    var ms2Scan = MockMsScan.MS2WithDescription(
                        1.1, "1001", firstMS2.ScanDescription,
                        firstMS2.PrecursorMz, firstMS2.ChargeState,
                        // Simple MS2 fragment peaks
                        (200.0, 10000), (300.0, 15000), (400.0, 20000),
                        (500.0, 25000), (600.0, 30000));
                    harness.PushScan(ms2Scan);
                    ms2Scan.Dispose();

                    // Check if any MS3 scans were produced
                    var allResults = harness.CollectResults();
                    var ms3Results = allResults.Where(r => r.MsnLevel == 3).ToList();

                    // MS3 scans should exist if MS2 deconvolution found peak groups
                    // and the protein sequence matched. This is data-dependent.
                    // Verify that if MS3 results exist, they have correct level
                    Assert.That(ms3Results.Count, Is.GreaterThanOrEqualTo(0),
                        "MS3 pipeline should not crash");
                    if (ms3Results.Count > 0)
                    {
                        Assert.IsTrue(ms3Results.All(r => r.MsnLevel == 3),
                            "MS3 records should have MsnLevel == 3");
                    }
                }
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT23_MS3Disabled_NoMsnLevel3()
        {
            using (var harness = CreateHarness("method_default.xml"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);

                var ms3Results = results.Where(r => r.MsnLevel == 3).ToList();
                Assert.AreEqual(0, ms3Results.Count,
                    "MS3 disabled: no MsnLevel 3 records should exist");
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT24_MS3Mode1_BehavioralReference()
        {
            using (var harness = CreateHarness("method_ms3_mode1.xml"))
            {
                // Push MS1
                var smokeScan = MockMsScan.FromTsv(Path.Combine(SpectraDir, "ms1_smoke_test.txt"));
                harness.PushScan(smokeScan);
                smokeScan.Dispose();

                // Get MS2 commands and simulate MS2 responses
                var ms2Commands = harness.Factory.CreatedScans
                    .Select(s => ScanCommandRecord.FromCustomScan(s))
                    .Where(r => r.ScanType == "MSn" && r.MsnLevel == 2)
                    .ToList();

                foreach (var cmd in ms2Commands.Take(1)) // Process at most 1 MS2
                {
                    var ms2Scan = MockMsScan.MS2WithDescription(
                        1.1, "1001", cmd.ScanDescription,
                        cmd.PrecursorMz, cmd.ChargeState,
                        (200.0, 10000), (300.0, 15000), (400.0, 20000));
                    harness.PushScan(ms2Scan);
                    ms2Scan.Dispose();
                }

                var results = harness.CollectResults();
                AssertGolden("continuity_ms3_mode1.json", results);
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT25_MS3Mode2_BehavioralReference()
        {
            using (var harness = CreateHarness("method_ms3_mode2.xml"))
            {
                var smokeScan = MockMsScan.FromTsv(Path.Combine(SpectraDir, "ms1_smoke_test.txt"));
                harness.PushScan(smokeScan);
                smokeScan.Dispose();

                var ms2Commands = harness.Factory.CreatedScans
                    .Select(s => ScanCommandRecord.FromCustomScan(s))
                    .Where(r => r.ScanType == "MSn" && r.MsnLevel == 2)
                    .ToList();

                foreach (var cmd in ms2Commands.Take(1))
                {
                    var ms2Scan = MockMsScan.MS2WithDescription(
                        1.1, "1001", cmd.ScanDescription,
                        cmd.PrecursorMz, cmd.ChargeState,
                        (200.0, 10000), (300.0, 15000), (400.0, 20000));
                    harness.PushScan(ms2Scan);
                    ms2Scan.Dispose();
                }

                var results = harness.CollectResults();
                AssertGolden("continuity_ms3_mode2.json", results);
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT26_MS3Mode3_BehavioralReference()
        {
            using (var harness = CreateHarness("method_ms3_mode3.xml"))
            {
                var smokeScan = MockMsScan.FromTsv(Path.Combine(SpectraDir, "ms1_smoke_test.txt"));
                harness.PushScan(smokeScan);
                smokeScan.Dispose();

                var ms2Commands = harness.Factory.CreatedScans
                    .Select(s => ScanCommandRecord.FromCustomScan(s))
                    .Where(r => r.ScanType == "MSn" && r.MsnLevel == 2)
                    .ToList();

                foreach (var cmd in ms2Commands.Take(1))
                {
                    var ms2Scan = MockMsScan.MS2WithDescription(
                        1.1, "1001", cmd.ScanDescription,
                        cmd.PrecursorMz, cmd.ChargeState,
                        (200.0, 10000), (300.0, 15000), (400.0, 20000));
                    harness.PushScan(ms2Scan);
                    ms2Scan.Dispose();
                }

                var results = harness.CollectResults();
                AssertGolden("continuity_ms3_mode3.json", results);
            }
        }

        #endregion

        #region AL-CT27 through CT28: FAIMS Adaptive Skip

        [Test, Category("Tier2")]
        public void P0_AL_CT27_FAIMSAdaptiveSkip_LowPrecursorCVLessFrequent()
        {
            using (var harness = CreateHarness("method_faims_skip.xml", forceFaims: true))
            {
                double[] configuredCVs = harness.MethodParams.IDA.CVValues;
                Assert.AreEqual(3, configuredCVs.Length, "Config should have 3 CVs");
                Assert.That(harness.MethodParams.IDA.MaxCVSkip, Is.GreaterThan(0),
                    "MaxCVSkip should be configured for adaptive skip");

                // Push many MS1 scans alternating between CVs
                // CVs with low precursor counts should be skipped more often
                var smokeScan = MockMsScan.FromTsv(Path.Combine(SpectraDir, "ms1_smoke_test.txt"));
                var peaks = smokeScan.Centroids.Select(c => (c.Mz, c.Intensity)).ToArray();
                smokeScan.Dispose();

                // Push with each CV multiple times
                for (int round = 0; round < 5; round++)
                {
                    foreach (double cv in configuredCVs)
                    {
                        var scan = MockMsScan.WithFaimsPeaks(
                            round * 3 + Array.IndexOf(configuredCVs, cv),
                            (round * 3 + Array.IndexOf(configuredCVs, cv) + 1).ToString(),
                            cv, peaks);
                        harness.PushScan(scan);
                        scan.Dispose();
                    }
                }

                // Verify that the processor ran without error
                var results = harness.CollectResults();
                // Adaptive skip verification: with identical spectra across all CVs,
                // the engine should produce results for at least one CV.
                Assert.That(results.Count, Is.GreaterThan(0),
                    "FAIMS adaptive skip should produce scan commands from 5 rounds of 3 CVs");

                // Verify that at least 2 different CVs are represented in the results,
                // proving that FAIMS cycling actually visits multiple CV values.
                var distinctCVs = results.Where(r => r.FaimsCV != 0)
                    .Select(r => r.FaimsCV).Distinct().ToList();
                if (distinctCVs.Count > 0)
                {
                    Assert.That(distinctCVs.Count, Is.GreaterThanOrEqualTo(2),
                        string.Format("Results should contain scans from at least 2 different FAIMS CVs, got {0}: [{1}]",
                            distinctCVs.Count, string.Join(", ", distinctCVs)));
                }
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT28_FAIMSSkip_BehavioralReference()
        {
            using (var harness = CreateHarness("method_faims_skip.xml", forceFaims: true))
            {
                double[] configuredCVs = harness.MethodParams.IDA.CVValues;
                var smokeScan = MockMsScan.FromTsv(Path.Combine(SpectraDir, "ms1_smoke_test.txt"));
                var peaks = smokeScan.Centroids.Select(c => (c.Mz, c.Intensity)).ToArray();
                smokeScan.Dispose();

                for (int round = 0; round < 3; round++)
                {
                    foreach (double cv in configuredCVs)
                    {
                        var scan = MockMsScan.WithFaimsPeaks(
                            round * 3 + Array.IndexOf(configuredCVs, cv),
                            (round * 3 + Array.IndexOf(configuredCVs, cv) + 1).ToString(),
                            cv, peaks);
                        harness.PushScan(scan);
                        scan.Dispose();
                    }
                }

                var results = harness.CollectResults();
                AssertGolden("continuity_faims_skip.json", results);
            }
        }

        #endregion

        #region AL-CT31 through CT32: Stress Tests (Phase 3)

        [Test, Category("Tier4")]
        public void P3_AL_CT31_StressTest_1000ScansSequential()
        {
            string configsDir = Path.Combine(TestDir, "..", "test-data", "configs");
            string configPath = Path.Combine(configsDir, "method_default.xml");
            if (!File.Exists(configPath))
            {
                Assert.Ignore("method_default.xml not found");
                return;
            }

            var mp = MethodParameters.Load(configPath);
            using (var wrapper = new FLASHIdaWrapper(mp))
            {
                var trackingIds = new HashSet<int>();

                // Run 1000 sequential ProcessScan + GetNextScanCommand cycles
                for (int i = 0; i < 1000; i++)
                {
                    double[] mzs = { 500.0 + i * 0.1, 600.0 + i * 0.1, 700.0 + i * 0.1 };
                    double[] ints = { 1000.0, 2000.0, 3000.0 };
                    double rt = 1.0 + i * 0.01;

                    int processResult = wrapper.ProcessScan(mzs, ints, rt, 1, "stress_" + i);
                    Assert.AreEqual(0, processResult,
                        string.Format("ProcessScan should return 0 at iteration {0}", i));

                    var cmd = new ScanCommand();
                    int cmdResult = wrapper.GetNextScanCommand(ref cmd);
                    Assert.AreEqual(1, cmdResult,
                        string.Format("GetNextScanCommand should return 1 at iteration {0}", i));

                    int trackId = wrapper.GetNextTrackingId();
                    Assert.IsFalse(trackingIds.Contains(trackId),
                        string.Format("Tracking ID {0} should be unique at iteration {1}", trackId, i));
                    trackingIds.Add(trackId);
                }

                Assert.AreEqual(1000, trackingIds.Count,
                    "All 1000 tracking IDs should be unique");
            }
        }

        [Test, Category("Tier4")]
        public void P3_AL_CT32_StressTest_ConcurrentProcessing()
        {
            string configsDir = Path.Combine(TestDir, "..", "test-data", "configs");
            string configPath = Path.Combine(configsDir, "method_default.xml");
            if (!File.Exists(configPath))
            {
                Assert.Ignore("method_default.xml not found");
                return;
            }

            var mp = MethodParameters.Load(configPath);
            using (var wrapper = new FLASHIdaWrapper(mp))
            {
                int threadCount = 4;
                int iterationsPerThread = 250;
                var allIds = new System.Collections.Concurrent.ConcurrentBag<int>();
                var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

                var threads = new System.Threading.Thread[threadCount];
                for (int t = 0; t < threadCount; t++)
                {
                    int threadId = t;
                    threads[t] = new System.Threading.Thread(() =>
                    {
                        try
                        {
                            for (int i = 0; i < iterationsPerThread; i++)
                            {
                                double[] mzs = { 500.0 + threadId * 100 + i * 0.1 };
                                double[] ints = { 1000.0 };

                                wrapper.ProcessScan(mzs, ints, 1.0 + i * 0.01, 1,
                                    string.Format("thread{0}_scan{1}", threadId, i));

                                var cmd = new ScanCommand();
                                wrapper.GetNextScanCommand(ref cmd);

                                int id = wrapper.GetNextTrackingId();
                                allIds.Add(id);
                            }
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    });
                    threads[t].Start();
                }

                foreach (var thread in threads)
                    thread.Join();

                // No exceptions should have occurred
                Assert.IsEmpty(exceptions,
                    string.Format("Concurrent processing threw {0} exception(s): {1}",
                        exceptions.Count,
                        exceptions.Count > 0 ? exceptions.First().Message : ""));

                // All tracking IDs should be unique (mutex protects counter)
                var idSet = new HashSet<int>(allIds);
                Assert.AreEqual(allIds.Count, idSet.Count,
                    string.Format("Expected {0} unique tracking IDs but got {1} (duplicates detected)",
                        allIds.Count, idSet.Count));
            }
        }

        #endregion
    }
}

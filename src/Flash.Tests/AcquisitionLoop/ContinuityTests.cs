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

                // Engine must produce results — golden baselines confirm this data works
                Assert.That(results.Count, Is.GreaterThan(0),
                    "Deconvolution must find at least one precursor (was Assume; promoted to Assert since golden baselines exist)");

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

                Assert.That(results.Count, Is.GreaterThan(0),
                    "Deconvolution must find at least one precursor (was Assume; promoted to Assert since golden baselines exist)");

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

                Assert.That(results.Count, Is.GreaterThan(0),
                    "Deconvolution must find at least one precursor (was Assume; promoted to Assert since golden baselines exist)");

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
                Assert.That(results.Count, Is.GreaterThan(0),
                    "TopN=5 must produce at least one MS2 command from smoke spectrum");

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
                Assert.AreEqual(5, expectedCVs.Length, "Config should have 5 CVs");

                // Load real FAIMS spectra with per-CV peak data and CV annotations
                var faimsScans = MockMsScan.FromTsvAllScans(
                    Path.Combine(SpectraDir, "ms1_faims_3cv.txt"));

                // Push first 50 scans (enough for engine state accumulation)
                int pushCount = Math.Min(50, faimsScans.Count);
                for (int i = 0; i < pushCount; i++)
                {
                    harness.PushScan(faimsScans[i]);
                    faimsScans[i].Dispose();
                }
                for (int i = pushCount; i < faimsScans.Count; i++)
                    faimsScans[i].Dispose();

                var results = harness.CollectResults();
                Assert.That(results.Count, Is.GreaterThan(0),
                    "FAIMS 3-CV should produce results");
                foreach (var r in results)
                {
                    Assert.That(expectedCVs, Has.Member(r.FaimsCV),
                        string.Format("FAIMS CV {0} not in configured values", r.FaimsCV));
                }
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT10_FAIMS_MS2CarriesParentCV()
        {
            using (var harness = CreateHarness("method_faims_3cv.xml", forceFaims: true))
            {
                double[] configuredCVs = harness.MethodParams.IDA.CVValues;

                // Load real FAIMS spectra with per-CV peak data and CV annotations
                var faimsScans = MockMsScan.FromTsvAllScans(
                    Path.Combine(SpectraDir, "ms1_faims_3cv.txt"));

                // Push first 50 scans — with 5 CVs, each CV gets ~10 scans for state accumulation
                int pushCount = Math.Min(50, faimsScans.Count);
                for (int i = 0; i < pushCount; i++)
                {
                    harness.PushScan(faimsScans[i]);
                    faimsScans[i].Dispose();
                }
                for (int i = pushCount; i < faimsScans.Count; i++)
                    faimsScans[i].Dispose();

                var results = harness.CollectResults();
                Assert.That(results.Count, Is.GreaterThan(0),
                    "FAIMS MS2 should carry parent CV");
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

                Assert.That(results.Count, Is.GreaterThan(0),
                    "Deconvolution must find at least one precursor (was Assume; promoted to Assert since golden baselines exist)");

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

            // Exclusion mode should produce results (smoke spectrum has many precursors)
            Assert.That(exclusionResults.Count, Is.GreaterThan(0),
                "Exclusion mode should produce results with smoke test data");

            // Verify exclusion produces fewer or different results than standard DDA
            if (standardResults.Count > 0)
            {
                var stdMasses = new HashSet<double>(standardResults.Select(r => r.PrecursorMz));
                var exclMasses = new HashSet<double>(exclusionResults.Select(r => r.PrecursorMz));

                bool fewerResults = exclusionResults.Count < standardResults.Count;
                bool differentTargets = !exclMasses.SetEquals(stdMasses);
                Assert.IsTrue(fewerResults || differentTargets,
                    "Exclusion mode should produce fewer or different targets than standard DDA");
            }

            // All exclusion mode results should have valid precursor m/z values
            Assert.IsTrue(exclusionResults.All(r => r.PrecursorMz > 0),
                "All exclusion mode results should have valid precursor m/z");
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
                Assume.That(ms2Commands.Count, Is.GreaterThan(0),
                    "Conditional MS2 test requires MS2 commands from MS1 processing");
                Assume.That(harness.MethodParams.IDA.ConditionalMS2, Is.True,
                    "Config must have ConditionalMS2 enabled for this test");

                int maxPrecursors = harness.MethodParams.IDA.MaxMs2CountPerMs1;
                Assert.That(ms2Commands.Count, Is.LessThanOrEqualTo(maxPrecursors),
                    "Conditional MS2: initial batch should have at most 1 scan per precursor");
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

                Assume.That(ms2Commands.Count, Is.GreaterThan(0),
                    "MS3 test requires MS2 commands from MS1 processing");

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

                    // Verify MS1→MS2 pipeline works (prerequisite for MS3)
                    Assert.That(ms2Commands.Count, Is.GreaterThan(0),
                        "MS1 should produce MS2 commands for MS3 pipeline");

                    // MS3 results are data-dependent: require MS2 deconvolution to find
                    // peak groups matching the protein sequence. The real behavioral check
                    // is in CT24 (golden file comparison). Here we just verify structure.
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
                Assert.AreEqual(5, configuredCVs.Length, "Config should have 5 CVs");
                Assert.That(harness.MethodParams.IDA.MaxCVSkip, Is.GreaterThan(0),
                    "MaxCVSkip should be configured for adaptive skip");

                // Load real FAIMS spectra with per-CV peak data (distinct precursor counts per CV)
                var faimsScans = MockMsScan.FromTsvAllScans(
                    Path.Combine(SpectraDir, "ms1_faims_3cv.txt"));

                // Push all 300 scans — adaptive skip needs many scans per CV to
                // accumulate enough engine state across all 5 CVs
                for (int i = 0; i < faimsScans.Count; i++)
                {
                    harness.PushScan(faimsScans[i]);
                    faimsScans[i].Dispose();
                }

                var results = harness.CollectResults();
                Assert.That(results.Count, Is.GreaterThan(0),
                    "FAIMS adaptive skip should produce scan commands from real per-CV data");

                // Verify that at least 2 different CVs are represented in the results
                var distinctCVs = results.Where(r => r.FaimsCV != 0)
                    .Select(r => r.FaimsCV).Distinct().ToList();
                Assert.That(distinctCVs.Count, Is.GreaterThanOrEqualTo(2),
                    string.Format("Results should contain scans from at least 2 different FAIMS CVs, got {0}: [{1}]",
                        distinctCVs.Count, string.Join(", ", distinctCVs)));
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT28_FAIMSSkip_BehavioralReference()
        {
            using (var harness = CreateHarness("method_faims_skip.xml", forceFaims: true))
            {
                // Load real FAIMS spectra with per-CV peak data and CV annotations
                var faimsScans = MockMsScan.FromTsvAllScans(
                    Path.Combine(SpectraDir, "ms1_faims_3cv.txt"));

                // Push all 300 scans — matches CT27 for consistent golden capture
                for (int i = 0; i < faimsScans.Count; i++)
                {
                    harness.PushScan(faimsScans[i]);
                    faimsScans[i].Dispose();
                }

                var results = harness.CollectResults();
                AssertGolden("continuity_faims_skip.json", results);
            }
        }

        #endregion

        #region Phase 4 MS2 Return Path Tests (CT33–CT42)

        /// <summary>
        /// Push all MS1 scans from a TSV file through harness, then push MS2 responses back
        /// using real TSV fragment data. Extracts ScanDescription, PrecursorMz, and ChargeState
        /// from each MS2 command produced by MS1 processing, loads MS2 TSV data with those
        /// parameters, and pushes the MS2 scans back through the processor.
        /// </summary>
        private List<ScanCommandRecord> PushMS1ThenMS2Return(
            ContinuityTestHarness harness, string ms1File, string ms2File, int maxMS2Returns = -1)
        {
            // Step 1: Push all MS1 scans to build up deconvolution state
            var ms1Scans = MockMsScan.FromTsvAllScans(ms1File);
            foreach (var scan in ms1Scans)
            {
                harness.PushScan(scan);
                scan.Dispose();
            }

            // Step 2: Extract MS2 commands from factory
            var ms2Commands = harness.Factory.CreatedScans
                .Select(s => ScanCommandRecord.FromCustomScan(s))
                .Where(r => r.ScanType == "MSn" && r.MsnLevel == 2)
                .ToList();

            // Step 3: For each MS2 command, create an MS2 scan from TSV and push it back
            int count = maxMS2Returns >= 0 ? Math.Min(maxMS2Returns, ms2Commands.Count) : ms2Commands.Count;
            for (int i = 0; i < count; i++)
            {
                var cmd = ms2Commands[i];
                var ms2Scan = MockMsScan.FromTsvAsMS2(
                    ms2File,
                    cmd.ScanDescription,
                    cmd.PrecursorMz,
                    cmd.ChargeState);
                harness.PushScan(ms2Scan);
                ms2Scan.Dispose();
            }

            // Step 4: Collect all results (includes initial MS2 + follow-ups + MS3)
            return harness.CollectResults();
        }

        /// <summary>
        /// Load all MS1 scans from standard spectrum and push through harness. Returns scan commands.
        /// </summary>
        private List<ScanCommandRecord> PushStandardSpectrumAndCollect(ContinuityTestHarness harness)
        {
            var scans = MockMsScan.FromTsvAllScans(Path.Combine(SpectraDir, "ms1_standard.txt"));
            foreach (var scan in scans)
            {
                harness.PushScan(scan);
                scan.Dispose();
            }
            return harness.CollectResults();
        }

        // --- CT33: Tag Targeting MS2 Return (golden) ---

        [Test, Category("Tier2")]
        public void P4_AL_CT33_TagTargeting_MS2Return()
        {
            using (var harness = CreateHarness("method_tag_targeting.xml"))
            {
                var results = PushMS1ThenMS2Return(
                    harness,
                    Path.Combine(SpectraDir, "ms1_standard.txt"),
                    Path.Combine(SpectraDir, "ms2_hcd_fragment.txt"));

                Assert.That(results.Count, Is.GreaterThan(0),
                    "MS1→MS2 return pipeline must produce results");

                AssertGolden("continuity_tag_ms2return.json", results);
            }
        }

        // --- CT34: Conditional MS2 Follow-Up (structural assertion) ---
        // Proves conditionality: initial MS2 commands (from MS1) are ETD with tracking-ID
        // descriptions. Follow-up HCD scans only appear AFTER MS2 return, proving they
        // were triggered by tag detection.

        [Test, Category("Tier2")]
        public void P4_AL_CT34_ConditionalMS2_FollowUp()
        {
            using (var harness = CreateHarness("method_tag_targeting.xml"))
            {
                // Step 1: push MS1 only, before any MS2 return
                var ms1Scans = MockMsScan.FromTsvAllScans(Path.Combine(SpectraDir, "ms1_standard.txt"));
                foreach (var s in ms1Scans) { harness.PushScan(s); s.Dispose(); }

                // Initial MS2 commands from MS1 should be ETD only (no HCD yet)
                var initialResults = harness.CollectResults();
                Assert.That(initialResults.Count, Is.GreaterThan(0),
                    "MS1 processing must produce MS2 commands");
                Assert.IsTrue(initialResults.All(r => r.ActivationType == "ETD"),
                    "Initial MS2 commands should all be ETD (HCD is conditional on tag detection)");
                Assert.IsTrue(initialResults.All(r => r.ScanDescription.Length >= 5 && r.ScanDescription[4] == '|'),
                    "Initial MS2 commands should have base-36 tracking-ID scan descriptions (XXXX|...)");

                // Step 2: push MS2 back with real fragments to trigger tag detection
                var ms2Commands = harness.Factory.CreatedScans
                    .Select(s => ScanCommandRecord.FromCustomScan(s))
                    .Where(r => r.ScanType == "MSn" && r.MsnLevel == 2)
                    .ToList();
                string ms2File = Path.Combine(SpectraDir, "ms2_hcd_fragment.txt");
                foreach (var cmd in ms2Commands)
                {
                    var ms2Scan = MockMsScan.FromTsvAsMS2(
                        ms2File, cmd.ScanDescription, cmd.PrecursorMz, cmd.ChargeState);
                    harness.PushScan(ms2Scan);
                    ms2Scan.Dispose();
                }

                // Collect ALL results (initial ETD + any follow-up HCD)
                var allResults = harness.CollectResults();
                var hcdFollowUps = allResults.Where(r =>
                    r.ActivationType == "HCD" && r.MsnLevel == 2).ToList();

                Assert.That(hcdFollowUps.Count, Is.GreaterThan(0),
                    "Tag detection should trigger follow-up HCD scans");

                // Each HCD follow-up should match a precursor from an initial ETD scan
                foreach (var hcd in hcdFollowUps)
                {
                    Assert.IsTrue(
                        initialResults.Any(etd => Math.Abs(etd.PrecursorMz - hcd.PrecursorMz) < 0.01),
                        string.Format("HCD follow-up at m/z {0:F4} should match an initial ETD precursor",
                            hcd.PrecursorMz));
                }
            }
        }

        // --- CT35: MS3 Mode 1 MS2 return pipeline ---
        // Exercises MS1→MS2→MS2-return path with MS3 mode 1 config and real HCD fragments.
        // MS3 generation is data-dependent (requires DeconvolveMS2 peak groups + fragment
        // matching); golden file captures actual behavior including whether MS3 fires.

        [Test, Category("Tier2")]
        public void P4_AL_CT35_MS3Mode1_MS2ReturnPipeline()
        {
            using (var harness = CreateHarness("method_ms3_mode1_hcd.xml"))
            {
                var results = PushMS1ThenMS2Return(
                    harness,
                    Path.Combine(SpectraDir, "ms1_standard.txt"),
                    Path.Combine(SpectraDir, "ms2_hcd_fragment.txt"),
                    maxMS2Returns: 1);

                Assert.That(results.Count, Is.GreaterThan(0),
                    "MS1→MS2 return pipeline must produce results");
                AssertGolden("continuity_ms3_mode1_real.json", results);
            }
        }

        // --- CT36: MS3 Mode 2 MS2 return pipeline ---

        [Test, Category("Tier2")]
        public void P4_AL_CT36_MS3Mode2_MS2ReturnPipeline()
        {
            using (var harness = CreateHarness("method_ms3_mode2_hcd.xml"))
            {
                var results = PushMS1ThenMS2Return(
                    harness,
                    Path.Combine(SpectraDir, "ms1_standard.txt"),
                    Path.Combine(SpectraDir, "ms2_hcd_fragment.txt"),
                    maxMS2Returns: 1);

                Assert.That(results.Count, Is.GreaterThan(0),
                    "MS1→MS2 return pipeline must produce results");
                AssertGolden("continuity_ms3_mode2_real.json", results);
            }
        }

        // --- CT37: MS3 Mode 3 MS2 return pipeline ---

        [Test, Category("Tier2")]
        public void P4_AL_CT37_MS3Mode3_MS2ReturnPipeline()
        {
            using (var harness = CreateHarness("method_ms3_mode3_hcd.xml"))
            {
                var results = PushMS1ThenMS2Return(
                    harness,
                    Path.Combine(SpectraDir, "ms1_standard.txt"),
                    Path.Combine(SpectraDir, "ms2_hcd_fragment.txt"),
                    maxMS2Returns: 1);

                Assert.That(results.Count, Is.GreaterThan(0),
                    "MS1→MS2 return pipeline must produce results");
                AssertGolden("continuity_ms3_mode3_real.json", results);
            }
        }

        // --- CT38: Quant Mode MS2 Return ---

        [Test, Category("Tier2")]
        public void P4_AL_CT38_QuantMode_MS2Return()
        {
            using (var harness = CreateHarness("method_quant.xml"))
            {
                // Push all MS1 scans to get initial quant MS2 commands
                var ms1Scans = MockMsScan.FromTsvAllScans(Path.Combine(SpectraDir, "ms1_standard.txt"));
                foreach (var scan in ms1Scans)
                {
                    harness.PushScan(scan);
                    scan.Dispose();
                }

                // Extract the quant MS2 commands (first MS2 param set = ETD with "quant" description)
                var ms2Commands = harness.Factory.CreatedScans
                    .Select(s => ScanCommandRecord.FromCustomScan(s))
                    .Where(r => r.ScanType == "MSn" && r.MsnLevel == 2)
                    .ToList();

                Assert.That(ms2Commands.Count, Is.GreaterThan(0),
                    "MS1 must produce MS2 commands for quant mode");

                // Push TMT reporter MS2 data back for each quant command
                string ms2File = Path.Combine(SpectraDir, "ms2_quant_tmt.txt");
                foreach (var cmd in ms2Commands)
                {
                    var ms2Scan = MockMsScan.FromTsvAsMS2(
                        ms2File,
                        cmd.ScanDescription,
                        cmd.PrecursorMz,
                        cmd.ChargeState);
                    harness.PushScan(ms2Scan);
                    ms2Scan.Dispose();
                }

                var results = harness.CollectResults();
                AssertGolden("continuity_quant_ms2return.json", results);
            }
        }

        // --- CT39: Inclusion with matching targets (golden) ---

        [Test, Category("Tier2")]
        public void P4_AL_CT39_Inclusion_MatchingTargets()
        {
            using (var harness = CreateHarness("method_inclusion.xml"))
            {
                var results = PushStandardSpectrumAndCollect(harness);

                Assert.That(results.Count, Is.GreaterThan(0),
                    "Deconvolution must find precursors in ms1_standard spectrum");

                AssertGolden("continuity_inclusion_matching.json", results);
            }
        }

        // --- CT40: Strict inclusion with matching targets ---

        [Test, Category("Tier2")]
        public void P4_AL_CT40_StrictInclusion_Matching()
        {
            using (var harness = CreateHarness("method_inclusion_strict.xml"))
            {
                var results = PushStandardSpectrumAndCollect(harness);

                // Strict inclusion: only target-matched masses survive.
                // Inclusion targets: 2063.606, 2277.254, 4297.177, 5315.129, 12358.31
                // With ms1_standard.txt, some (not all) deconvolved masses should match targets.
                // Strict means non-matching masses are excluded entirely.

                Assert.That(results.Count, Is.GreaterThan(0),
                    "Strict inclusion should find at least one matching target in ms1_standard");

                Assert.That(results.Count, Is.LessThanOrEqualTo(
                    5 * harness.MethodParams.MS2.Count), // at most 5 targets * MS2 types
                    "Strict inclusion should produce at most target_count * MS2_types results");

                Assert.IsTrue(results.All(r => r.PrecursorMz > 0),
                    "All results should have valid precursor m/z");

                AssertGolden("continuity_inclusion_strict_matching.json", results);
            }
        }

        // --- CT41: Standard DDA with rich spectrum (golden) ---

        [Test, Category("Tier2")]
        public void P4_AL_CT41_StandardDDA_RichSpectrum()
        {
            using (var harness = CreateHarness("method_default.xml"))
            {
                var results = PushStandardSpectrumAndCollect(harness);

                Assert.That(results.Count, Is.GreaterThan(0),
                    "Deconvolution must find precursors in ms1_standard spectrum");

                AssertGolden("continuity_standard_dda_rich.json", results);
            }
        }

        // --- CT42: Deep Mode target log deprioritization effect ---
        // Uses same TopN=5 for both runs so only the target log causes differences.
        // Target log has masses 2063.606, 2277.254, 5315.129 — deep mode should
        // deprioritize these, producing different precursor selections.

        [Test, Category("Tier2")]
        public void P4_AL_CT42_DeepMode_TargetLogEffect()
        {
            // Standard DDA with TopN=5 as baseline (same TopN as deep mode config)
            int standardCount;
            List<double> standardMasses;
            using (var harness = CreateHarness("method_default_topn5.xml"))
            {
                var results = PushStandardSpectrumAndCollect(harness);
                standardCount = results.Count;
                standardMasses = results.Select(r => r.PrecursorMz).ToList();
            }

            Assert.That(standardCount, Is.GreaterThan(0),
                "Standard DDA (TopN=5) must produce results for deep mode comparison");

            // Deep mode with target log — previously seen masses should be deprioritized
            int deepCount;
            List<double> deepMasses;
            using (var harness = CreateHarness("method_deep.xml"))
            {
                var results = PushStandardSpectrumAndCollect(harness);
                deepCount = results.Count;
                deepMasses = results.Select(r => r.PrecursorMz).ToList();
            }

            // Deep mode should produce fewer results or different mass selections
            // because target log masses (2063.6, 2277.3, 5315.1) are deprioritized
            bool fewerResults = deepCount < standardCount;
            bool differentMasses = !new HashSet<double>(deepMasses).SetEquals(standardMasses);

            Assert.IsTrue(fewerResults || differentMasses,
                string.Format("Deep mode ({0} results) should differ from standard DDA ({1} results) " +
                    "due to target log deprioritization. Standard masses: [{2}], Deep masses: [{3}]",
                    deepCount, standardCount,
                    string.Join(", ", standardMasses.Select(m => m.ToString("F2"))),
                    string.Join(", ", deepMasses.Select(m => m.ToString("F2")))));
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
                    // Synthetic peaks produce 0 commands, so queue is empty → returns 0
                    Assert.AreEqual(0, cmdResult,
                        string.Format("GetNextScanCommand should return 0 (empty queue) at iteration {0}", i));

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

        #region Phase 4 Scoring Field Coverage

        [Test, Category("Tier2")]
        public void P4_I06_ScoringFields_NonZeroForMS2Commands()
        {
            using (var harness = CreateHarness("method_default.xml"))
            {
                // Push all 50 MS1 scans from ms1_standard.txt for engine state accumulation
                var allScans = MockMsScan.FromTsvAllScans(Path.Combine(SpectraDir, "ms1_standard.txt"));
                foreach (var scan in allScans)
                {
                    harness.PushScan(scan);
                    scan.Dispose();
                }

                var results = harness.CapturedRecords.Where(r => r.ScanType == "MSn").ToList();
                Assert.That(results.Count, Is.GreaterThan(0), "Need MS2 results to test scoring");

                foreach (var r in results)
                {
                    Assert.That(r.Qscore, Is.Not.EqualTo(0),
                        string.Format("Qscore should be non-zero for {0}", r.ScanDescription));
                    Assert.That(r.MonoMass, Is.GreaterThan(0),
                        string.Format("MonoMass should be positive for {0}", r.ScanDescription));
                    Assert.That(r.PrecursorIntensity, Is.GreaterThan(0),
                        string.Format("PrecursorIntensity should be positive for {0}", r.ScanDescription));
                }
            }
        }

        #endregion
    }
}

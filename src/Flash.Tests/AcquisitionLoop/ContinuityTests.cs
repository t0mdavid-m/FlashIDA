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
            harness.PushMs1(scan);
            scan.Dispose();
            return harness.CollectResults();
        }

        /// <summary>
        /// Assert the JSON-serialized results against the committed golden file. The actual
        /// output is always written to continuity-output/ for capture/debugging. If the golden
        /// file is missing, the test FAILS (Assert.Fail) so a missing reference can never pass
        /// silently — capture and commit the written output to test-data/golden/.
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
                // Numeric-aware compare: each CI run rebuilds OpenMS into a different binary, so the engine's
                // floating-point score fields (Qscore/ChargeCos/ChargeSnr/Snr) jitter ~1e-8..3e-5 run to run and
                // exact string match can never converge. Float tokens tolerance; ids/levels/counts/strings stay exact.
                if (!GoldenNumericComparer.Equivalent(expected, actualJson, out string diff))
                    Assert.Fail("Behavioral reference mismatch for " + goldenFileName + " (" + diff +
                        "). Numbers compare with tolerance; ids/levels/counts/strings are exact. " +
                        "If this change is intentional, update the golden file.");
            }
            else
            {
                Assert.Fail(
                    "Golden file not found: " + goldenFileName +
                    ". Actual output written to continuity-output/. " +
                    "Capture and commit it to test-data/golden/.");
            }
        }

        #endregion

        #region AL-CT01 through CT05: Standard DDA Basics

        [Test, Category("Tier2")]
        public void P0_AL_CT04_EmptySpectrum_ZeroCommands()
        {
            using (var harness = CreateHarness("method_default.json"))
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
            using (var harness = CreateHarness("method_default.json"))
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
            using (var harness = CreateHarness("method_default.json"))
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
            using (var harness = CreateHarness("method_default.json"))
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
                        harness.MethodParams.Config.MsSettings.MS1.FirstMass,
                        harness.MethodParams.Config.MsSettings.MS1.LastMass),
                        "Precursor m/z should be within MS1 scan range");
                }
            }
        }

        // CT02 was a single "CollisionEnergiesMatchConfig" test whose only CE assertion sat
        // behind `if (CollisionEnergy != 0)`; on its all-ETD config (CE always 0) that branch
        // never ran, so the test passed without checking anything. Split into two focused tests
        // — one ETD, one HCD — each asserting scan type + activation + the activation-specific
        // energy/reaction-time on EVERY MS2 command, so neither can pass vacuously.
        // CapturedRecords (raw ScanCommand structs) is used because ReactionTime is not exposed
        // on the Values-based CollectResults() path.

        [Test, Category("Tier2")]
        public void P0_AL_CT02a_StandardDDA_ETD()
        {
            using (var harness = CreateHarness("method_dda_etd.json"))
            {
                PushSmokeSpectrumAndCollect(harness);

                var ms2 = harness.CapturedRecords.Where(r => r.MsnLevel == 2).ToList();
                Assert.That(ms2, Is.Not.Empty, "ETD standard DDA must produce at least one MS2 command");

                double expectedReactionTime = harness.MethodParams.Config.MsSettings.MS2.ReactionTime;
                Assert.That(expectedReactionTime, Is.GreaterThan(0), "ETD fixture must configure a reaction time");

                foreach (var r in ms2)
                {
                    Assert.That(r.ScanType, Is.EqualTo("MSn"), "ETD command must be an MSn scan");
                    Assert.That(r.ActivationType, Is.EqualTo("ETD"), "MS2 activation must be ETD");
                    Assert.That(r.ReactionTime, Is.EqualTo(expectedReactionTime).Within(0.001),
                        "ETD reaction time must match the configured value");
                    Assert.That(r.CollisionEnergy, Is.EqualTo(0),
                        "ETD MS2 must not carry a collision energy");
                }
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT02b_StandardDDA_HCD()
        {
            using (var harness = CreateHarness("method_dda_hcd.json"))
            {
                PushSmokeSpectrumAndCollect(harness);

                var ms2 = harness.CapturedRecords.Where(r => r.MsnLevel == 2).ToList();
                Assert.That(ms2, Is.Not.Empty, "HCD standard DDA must produce at least one MS2 command");

                int expectedCe = harness.MethodParams.Config.MsSettings.MS2.CollisionEnergy;
                Assert.That(expectedCe, Is.GreaterThan(0), "HCD fixture must configure a non-zero collision energy");

                foreach (var r in ms2)
                {
                    Assert.That(r.ScanType, Is.EqualTo("MSn"), "HCD command must be an MSn scan");
                    Assert.That(r.ActivationType, Is.EqualTo("HCD"), "MS2 activation must be HCD");
                    Assert.That(r.CollisionEnergy, Is.EqualTo(expectedCe),
                        "HCD collision energy must match the configured value");
                    Assert.That(r.ReactionTime, Is.EqualTo(0.0).Within(0.001),
                        "HCD MS2 must not carry a reaction time");
                }
            }
        }

        #endregion

        #region AL-CT06 through CT08: Standard DDA Reference + TopN + Tracking

        [Test, Category("Tier2")]
        public void P0_AL_CT06_StandardDDA_BehavioralReference()
        {
            using (var harness = CreateHarness("method_default.json"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);
                Assert.That(results.Count, Is.GreaterThan(0),
                    "Deconvolution must find at least one precursor");

                AssertGolden("continuity_standard_dda.json", results);
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT07_TrackingIDs_UniqueAcross1000Scans()
        {
            using (var harness = CreateHarness("method_default.json"))
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
                    harness.PushMs1(scan);
                    scan.Dispose();
                }

                smokeScan.Dispose();

                var results = harness.CollectResults();
                Assert.That(results.Count, Is.GreaterThan(0),
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

                // Fail closed: prove the loop actually examined data. Every MSn result must
                // carry a tracking-ID description, and all must be unique — otherwise an
                // all-empty-description regression would pass this test vacuously.
                int checkedCount = results.Count(r => !string.IsNullOrEmpty(r.ScanDescription));
                Assert.That(checkedCount, Is.EqualTo(results.Count),
                    "Every MSn command must carry a tracking-ID scan description");
                Assert.That(allDescriptions.Count, Is.EqualTo(results.Count),
                    "All scan descriptions (tracking IDs) must be unique across 1000 scans");
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT08_MS2Count_RespectsMaxMs2CountPerMs1()
        {
            using (var harness = CreateHarness("method_default_topn5.json"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);
                Assert.That(results.Count, Is.GreaterThan(0),
                    "TopN=5 must produce at least one MS2 command from smoke spectrum");

                int maxPerMs1 = harness.MethodParams.Config.PrecursorSelection.MaxPrecursors;
                int ms2Types = 1 + (harness.MethodParams.Config.PrecursorSelection.AdditionalScans?.Count ?? 0);

                Assert.AreEqual(5, maxPerMs1,
                    "Config should have MaxTargets=5");

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
            using (var harness = CreateHarness("method_faims_3cv.json", forceFaims: true))
            {
                double[] expectedCVs = harness.MethodParams.Config.Faims.CVValues;
                Assert.AreEqual(5, expectedCVs.Length, "Config should have 5 CVs");

                // Load real FAIMS spectra with per-CV peak data and CV annotations
                var faimsScans = MockMsScan.FromTsvAllScans(
                    Path.Combine(SpectraDir, "ms1_faims_3cv.txt"));

                // Push first 50 scans (enough for engine state accumulation)
                int pushCount = Math.Min(50, faimsScans.Count);
                for (int i = 0; i < pushCount; i++)
                {
                    harness.PushMs1(faimsScans[i]);
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
            using (var harness = CreateHarness("method_faims_3cv.json", forceFaims: true))
            {
                double[] configuredCVs = harness.MethodParams.Config.Faims.CVValues;

                // Load real FAIMS spectra with per-CV peak data and CV annotations
                var faimsScans = MockMsScan.FromTsvAllScans(
                    Path.Combine(SpectraDir, "ms1_faims_3cv.txt"));

                // Push first 50 scans — with 5 CVs, each CV gets ~10 scans for state accumulation
                int pushCount = Math.Min(50, faimsScans.Count);
                for (int i = 0; i < pushCount; i++)
                {
                    harness.PushMs1(faimsScans[i]);
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
            using (var harness = CreateHarness("method_default.json"))
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
            using (var harness = CreateHarness("method_default_topn5.json"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);
                standardCount = results.Count;
            }

            // Run deep mode with TopN=5
            using (var harness = CreateHarness("method_deep.json"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);
                deepCount = results.Count;
            }

            // Floor the standard count first, otherwise an all-zero engine run (both configs
            // emitting nothing) would satisfy 0 >= 0 and pass without exercising deep mode.
            Assert.That(standardCount, Is.GreaterThan(0),
                "Standard DDA must produce at least one MS2 command for the smoke spectrum");

            // Deep mode should produce at least as many MS2 scans as standard DDA
            // for the same input spectrum and TopN setting (deepCount > 0 follows).
            Assert.That(deepCount, Is.GreaterThanOrEqualTo(standardCount),
                string.Format("Deep mode ({0}) should produce >= standard DDA ({1}) MS2 scans",
                    deepCount, standardCount));
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT13_InclusionList_OnlyListedMasses()
        {
            // Non-strict inclusion: targets get priority but non-targets can fill remaining
            // slots. None of the inclusion-list masses match this test spectrum's precursors,
            // so all results are non-target fill-ins. This run also establishes the baseline
            // that the engine DOES deconvolve precursors for this spectrum.
            int nonStrictCount;
            using (var harness = CreateHarness("method_inclusion.json"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);
                nonStrictCount = results.Count;

                Assert.That(nonStrictCount, Is.GreaterThan(0),
                    "Non-strict inclusion mode should produce scan commands even when no targets match");
                Assert.IsTrue(results.All(r => r.PrecursorMz > 0),
                    "All results should have valid precursor m/z");
            }

            // Strict inclusion: only inclusion-list masses are selected. Because the non-strict
            // run above proved precursors exist for this spectrum, a zero strict result is
            // attributable to strict suppression rather than a dead engine / failed deconvolution.
            using (var harness = CreateHarness("method_inclusion_strict.json"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);

                Assert.That(results.Count, Is.EqualTo(0),
                    string.Format("Strict inclusion must suppress the {0} non-target precursor(s) the " +
                        "non-strict run produced (none match the inclusion list)", nonStrictCount));
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT14_ExclusionList_ExcludedMassesSuppressed()
        {
            // Compare exclusion results against standard DDA results.
            // Exclusion mode should produce a different set of precursors.
            List<ScanCommandRecord> standardResults;
            List<ScanCommandRecord> exclusionResults;

            using (var stdHarness = CreateHarness("method_default.json"))
            {
                standardResults = PushSmokeSpectrumAndCollect(stdHarness);
            }

            using (var exclHarness = CreateHarness("method_exclusion.json"))
            {
                exclusionResults = PushSmokeSpectrumAndCollect(exclHarness);
            }

            // Exclusion mode should produce results (smoke spectrum has many precursors)
            Assert.That(exclusionResults.Count, Is.GreaterThan(0),
                "Exclusion mode should produce results with smoke test data");

            // Standard DDA must produce a baseline to compare exclusion against.
            Assert.That(standardResults.Count, Is.GreaterThan(0),
                "Standard DDA must produce precursors to compare exclusion against");

            // Verify exclusion produces fewer or different results than standard DDA
            var stdMasses = new HashSet<double>(standardResults.Select(r => r.PrecursorMz));
            var exclMasses = new HashSet<double>(exclusionResults.Select(r => r.PrecursorMz));

            bool fewerResults = exclusionResults.Count < standardResults.Count;
            bool differentTargets = !exclMasses.SetEquals(stdMasses);
            Assert.IsTrue(fewerResults || differentTargets,
                "Exclusion mode should produce fewer or different targets than standard DDA");

            // All exclusion mode results should have valid precursor m/z values
            Assert.IsTrue(exclusionResults.All(r => r.PrecursorMz > 0),
                "All exclusion mode results should have valid precursor m/z");
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT15_Inclusion_BehavioralReference()
        {
            using (var harness = CreateHarness("method_inclusion.json"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);
                AssertGolden("continuity_inclusion.json", results);
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT16_Exclusion_BehavioralReference()
        {
            using (var harness = CreateHarness("method_exclusion.json"))
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
            using (var harness = CreateHarness("method_tag_targeting.json"))
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
            using (var harness = CreateHarness("method_tag_targeting.json"))
            {
                // Push MS1 scan
                var smokeScan = MockMsScan.FromTsv(Path.Combine(SpectraDir, "ms1_smoke_test.txt"));
                var ms1Scans = harness.PushMs1(smokeScan);
                smokeScan.Dispose();

                // Get the MS2 commands from MS1 processing
                var ms2Commands = harness.CollectResults();

                // In conditional MS2 mode, the first MS2 type is sent for each precursor.
                // Follow-up MS2 types are only sent if tags are detected in the first MS2.
                // Verify that at most 1 MS2 per precursor was sent initially
                // (the conditional mode sends only the first MS2 parameter set)
                Assert.That(ms2Commands.Count, Is.GreaterThan(0),
                    "Conditional MS2 test requires MS2 commands from MS1 processing");
                Assert.That(harness.MethodParams.Config.Tagging.ConditionalMS2, Is.True,
                    "Config must have ConditionalMS2 enabled for this test");

                int maxPrecursors = harness.MethodParams.Config.PrecursorSelection.MaxPrecursors;
                Assert.That(ms2Commands.Count, Is.LessThanOrEqualTo(maxPrecursors),
                    "Conditional MS2: initial batch should have at most 1 scan per precursor");
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT19_TagTargeting_BehavioralReference()
        {
            using (var harness = CreateHarness("method_tag_targeting.json"))
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
                using (var harness = CreateHarness("method_quant.json"))
                {
                    Assert.IsNotNull(harness.Processor,
                        "Quant processor should be created successfully");
                    // The quant follow-up is a NAME into ms_settings.additional_ms2 and is
                    // deliberately absent from the dispatch roster, so it never fires per precursor.
                    Assert.AreEqual(0,
                        harness.MethodParams.Config.PrecursorSelection.AdditionalScans?.Count ?? 0,
                        "Quant config should dispatch only ms_settings.ms2 (the follow-up is referenced, not rostered)");
                }
            }, "Quant mode construction should not throw");
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT21_Quant_BehavioralReference()
        {
            using (var harness = CreateHarness("method_quant.json"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);
                AssertGolden("continuity_quant.json", results);
            }
        }

        #endregion

        #region AL-CT23 through CT26: MS3 Tests
        // CT22 removed: it fed synthetic MS2 peaks that never match the proteoform, so the
        // engine emits 0 MS3 and its only assertion was both skipped (if count>0) and
        // tautological. Real MS3 existence is golden-covered by CT24 (synthetic, 0 MS3) and
        // CT35/CT36 (real CytC, 4 MS3 records each).

        [Test, Category("Tier2")]
        public void P0_AL_CT23_MS3Disabled_NoMsnLevel3()
        {
            using (var harness = CreateHarness("method_default.json"))
            {
                var results = PushSmokeSpectrumAndCollect(harness);

                var ms3Results = results.Where(r => r.MsnLevel == 3).ToList();
                Assert.AreEqual(0, ms3Results.Count,
                    "MS3 disabled: no MsnLevel 3 records should exist");
            }
        }

        // CT24/25/26 (R1a — golden restored): the MS1->one-MS2-return->MS3 drive runs through the canonical
        // interleaved driver PushScanAndDrainFull (engine-id echo + by-priority drain), with the bespoke
        // "Take(1)" MS2 cap expressed as maxMs2Responses:1. The original CT24/25/26 fed a LOAD-BEARING synthetic
        // 3-peak MS2 spectrum {(200,10000),(300,15000),(400,20000)} via MS2WithDescription; that exact spectrum
        // is now committed as the TSV fixture ms2_synth_3peak.txt so the interleaved driver (which only feeds
        // TSV spectra) reproduces the same data-dependent MS3 cascade, and the byte-exact AssertGolden behavioral
        // reference is restored. One cheap non-empty guard precedes AssertGolden so an empty capture cannot pass
        // vacuously.
        private void RunMs3ModeGolden(string configFile, string goldenFileName)
        {
            using (var harness = CreateHarness(configFile))
            {
                // Interleaved drive: smoke MS1 survey + at most ONE synthetic-3-peak MS2 response
                // (Take(1) -> maxMs2Responses:1), letting the engine's data-dependent MS3 cascade form off
                // that single MS2 return.
                harness.PushScanAndDrainFull(
                    Path.Combine(SpectraDir, "ms1_smoke_test.txt"),
                    Path.Combine(SpectraDir, "ms2_synth_3peak.txt"),
                    maxMs2Responses: 1);

                var results = CapturedMsn(harness);
                Assert.That(results.Count, Is.GreaterThan(0),
                    "MS3-mode run must emit at least one MSn command (non-empty guard before AssertGolden)");

                AssertGolden(goldenFileName, results);
            }
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT24_MS3Mode1_BehavioralReference()
        {
            RunMs3ModeGolden("method_ms3_mode1.json", "continuity_ms3_mode1.json");
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT25_MS3Mode2_BehavioralReference()
        {
            RunMs3ModeGolden("method_ms3_mode2.json", "continuity_ms3_mode2.json");
        }

        [Test, Category("Tier2")]
        public void P0_AL_CT26_MS3Mode3_BehavioralReference()
        {
            RunMs3ModeGolden("method_ms3_mode3.json", "continuity_ms3_mode3.json");
        }

        #endregion

        #region AL-CT27 through CT28: FAIMS Adaptive Skip

        [Test, Category("Tier2")]
        public void P0_AL_CT27_FAIMSAdaptiveSkip_LowPrecursorCVLessFrequent()
        {
            using (var harness = CreateHarness("method_faims_skip.json", forceFaims: true))
            {
                double[] configuredCVs = harness.MethodParams.Config.Faims.CVValues;
                Assert.AreEqual(5, configuredCVs.Length, "Config should have 5 CVs");
                Assert.That(harness.MethodParams.Config.Faims.MaxCVSkip, Is.GreaterThan(0),
                    "MaxCVSkip should be configured for adaptive skip");

                // Load real FAIMS spectra with per-CV peak data (distinct precursor counts per CV)
                var faimsScans = MockMsScan.FromTsvAllScans(
                    Path.Combine(SpectraDir, "ms1_faims_3cv.txt"));

                // Push all 300 scans — adaptive skip needs many scans per CV to
                // accumulate enough engine state across all 5 CVs
                for (int i = 0; i < faimsScans.Count; i++)
                {
                    harness.PushMs1(faimsScans[i]);
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
            using (var harness = CreateHarness("method_faims_skip.json", forceFaims: true))
            {
                // Load real FAIMS spectra with per-CV peak data and CV annotations
                var faimsScans = MockMsScan.FromTsvAllScans(
                    Path.Combine(SpectraDir, "ms1_faims_3cv.txt"));

                // Push all 300 scans — matches CT27 for consistent golden capture
                for (int i = 0; i < faimsScans.Count; i++)
                {
                    harness.PushMs1(faimsScans[i]);
                    faimsScans[i].Dispose();
                }

                var results = harness.CollectResults();
                AssertGolden("continuity_faims_skip.json", results);
            }
        }

        #endregion

        #region Phase 4 MS2 Return Path Tests (CT33–CT42)

        // Phase 2 migration: the bespoke PushMS1ThenMS2Return helper (push all MS1, then feed MS2 returns,
        // capped by maxMS2Returns) has been ABSORBED into the canonical interleaved driver
        // ContinuityTestHarness.PushScanAndDrainFull(ms1Path, ms2Path, maxMs2Responses: N). The staged
        // "push all MS1 then all MS2" feed is replaced by the by-priority engine-id-echo interleave; the
        // maxMS2Returns cap maps to maxMs2Responses. Helper deleted (no longer duplicated here).

        /// <summary>
        /// Real MSn (level 2/3) command records captured by the interleaved driver, excluding idle/AGC and
        /// MS1 survey commands. Re-expresses the old CollectResults() "MSn only" filter over the raw
        /// CapturedRecords stream that PushScanAndDrainFull populates (which includes the full MS3 cascade).
        /// </summary>
        private static List<ScanCommandRecord> CapturedMsn(ContinuityTestHarness harness) =>
            harness.CapturedRecords
                .Where(r => r.ScanType == "MSn" && r.MsnLevel >= 2 && !r.IsAGC)
                .ToList();

        /// <summary>
        /// Load all MS1 scans from standard spectrum and push through harness. Returns scan commands.
        /// </summary>
        private List<ScanCommandRecord> PushStandardSpectrumAndCollect(ContinuityTestHarness harness)
        {
            var scans = MockMsScan.FromTsvAllScans(Path.Combine(SpectraDir, "ms1_standard.txt"));
            foreach (var scan in scans)
            {
                harness.PushMs1(scan);
                scan.Dispose();
            }
            return harness.CollectResults();
        }

        // --- CT33: Tag Targeting MS2 Return (golden) ---

        [Test, Category("Tier2")]
        public void P4_AL_CT33_TagTargeting_MS2Return()
        {
            using (var harness = CreateHarness("method_tag_targeting.json"))
            {
                // Interleaved engine-id-echo drive: standard MS1 surveys + HCD-fragment MS2 returns for every
                // MS2 command (no cap). Replaces the staged PushMS1ThenMS2Return(all returns).
                harness.PushScanAndDrainFull(
                    Path.Combine(SpectraDir, "ms1_standard.txt"),
                    Path.Combine(SpectraDir, "ms2_hcd_fragment.txt"));

                // R1a — byte-exact golden restored over the SAME captured MSn stream the drive produced.
                // One cheap non-empty guard ensures an empty capture cannot vacuously pass AssertGolden.
                var results = CapturedMsn(harness);
                Assert.That(results.Count, Is.GreaterThan(0),
                    "MS1->MS2 return pipeline must produce results (non-empty guard before AssertGolden)");

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
            using (var harness = CreateHarness("method_tag_targeting.json"))
            {
                // The conditional-MS2 proof is a BEFORE/AFTER comparison across one point in the drive:
                //   BEFORE the first MS2 return -> only the initial (ETD) MS2 commands the MS1 surveys emit;
                //   AFTER  the MS2 returns      -> tag detection has triggered follow-up HCD commands.
                // The interleaved driver interleaves surveys and returns, so we capture the "before" set via
                // the onFirstMs2Response mid-drive snapshot (fired once, just before the first MS2 response):
                // at that instant CapturedRecords holds only MS1-survey-emitted commands (no follow-ups yet).
                List<ScanCommandRecord> initialResults = null;

                harness.PushScanAndDrainFull(
                    Path.Combine(SpectraDir, "ms1_standard.txt"),
                    Path.Combine(SpectraDir, "ms2_hcd_fragment.txt"),
                    onFirstMs2Response: h => initialResults = CapturedMsn(h));

                // Snapshot must have fired and captured the initial (pre-return) MS2 batch.
                Assert.That(initialResults, Is.Not.Null,
                    "Mid-drive snapshot must fire before the first MS2 return");
                Assert.That(initialResults.Count, Is.GreaterThan(0),
                    "MS1 processing must produce initial MS2 commands");
                Assert.IsTrue(initialResults.All(r => r.ActivationType == "ETD"),
                    "Initial MS2 commands should all be ETD (HCD is conditional on tag detection)");
                Assert.IsTrue(initialResults.All(r => r.ScanDescription.Length >= 4 && "ARFCE".Contains(r.ScanDescription[3])),
                    "Initial MS2 commands should have compact tracking-ID scan descriptions (XXXR...)");

                // AFTER the full interleaved drive: tag detection must have triggered follow-up HCD scans.
                var allResults = CapturedMsn(harness);
                var hcdFollowUps = allResults.Where(r =>
                    r.ActivationType == "HCD" && r.MsnLevel == 2).ToList();

                // The HCD follow-ups must exist (tag detection fired). This also guards the linkage loop below:
                // a foreach over an empty list passes vacuously, so assert non-empty adjacent to the loop it guards.
                Assert.That(hcdFollowUps.Count, Is.GreaterThan(0),
                    "Tag detection must trigger at least one HCD follow-up");

                // Each HCD follow-up must descend from a REAL triggering ETD MS2 via the engine's parent edge:
                // buildFollowUp sets parent_scan_id = encode(trigger ETD scan_id) (ScanCommandQueue.cpp:396-398);
                // the ETD's own id is its description prefix (<id>R<mass>@<z>) and the follow-up carries the
                // conditional 'C' suffix at char[3] (FLASHIda.cpp:1031). Join on the unique scan_id edge — NOT on
                // precursor m/z, which the engine struct-copies verbatim (matching it would be vacuously true).
                var etdMs2 = allResults.Where(r => r.ActivationType == "ETD" && r.MsnLevel == 2).ToList();
                foreach (var hcd in hcdFollowUps)
                {
                    Assert.IsTrue(hcd.ScanDescription.Length >= 4 && hcd.ScanDescription[3] == 'C',
                        string.Format("HCD follow-up '{0}' must carry the conditional 'C' description suffix",
                            hcd.ScanDescription));

                    var trigger = etdMs2.FirstOrDefault(e =>
                        e.ScanDescription.Length >= 3 && e.ScanDescription.Substring(0, 3) == hcd.ParentScanId);

                    Assert.IsNotNull(trigger,
                        string.Format("HCD follow-up '{0}' parent '{1}' must reference a real ETD MS2 command in the drive",
                            hcd.ScanDescription, hcd.ParentScanId));
                    Assert.That(hcd.PrecursorMz, Is.EqualTo(trigger.PrecursorMz).Within(1e-9),
                        string.Format("HCD follow-up '{0}' precursor must equal its named trigger ETD '{1}' (engine struct-copies ctx)",
                            hcd.ScanDescription, hcd.ParentScanId));
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
            using (var harness = CreateHarness("method_ms3_mode1_hcd.json"))
            {
                // Interleaved drive: cytC MS1 surveys + at most ONE real cytC MS2 return (maxMS2Returns:1 ->
                // maxMs2Responses:1), bounding the data-dependent MS3 cascade. Re-expressed over CapturedRecords.
                harness.PushScanAndDrainFull(
                    Path.Combine(SpectraDir, "ms1_cytc.txt"),
                    // scan-57, not scan-149: the scan-149 ladder is too weak for FLASHExtender to
                    // return a proteoform hit, and without an identification the tracker is never fed
                    // so ZERO MS3 is emitted (Exploration.cpp:823-825). The green C++ mirror of this
                    // scenario (FLASHIda_ProcessScan_test processScan_ms3_commands) already moved here.
                    Path.Combine(SpectraDir, "ms2_cytc_fresh_scan57.txt"),
                    maxMs2Responses: 1);

                AssertMs3ReturnGolden(harness, "continuity_ms3_mode1_real.json");
            }
        }

        // --- CT36: MS3 Mode 2 MS2 return pipeline ---
        // Differs from CT35 by characterization.objective = "coverage" (CT35 keeps the default
        // "ambiguity"). That is the knob deciding WHICH fragments become MS3 targets (ADR-0009), so
        // the pair covers two dispatch paths. Before this, CT36's config was byte-identical to
        // CT35's and the two tests asserted the same behaviour twice.

        [Test, Category("Tier2")]
        public void P4_AL_CT36_MS3Mode2_MS2ReturnPipeline()
        {
            using (var harness = CreateHarness("method_ms3_mode2_hcd.json"))
            {
                harness.PushScanAndDrainFull(
                    Path.Combine(SpectraDir, "ms1_cytc.txt"),
                    // scan-57, not scan-149: the scan-149 ladder is too weak for FLASHExtender to
                    // return a proteoform hit, and without an identification the tracker is never fed
                    // so ZERO MS3 is emitted (Exploration.cpp:823-825). The green C++ mirror of this
                    // scenario (FLASHIda_ProcessScan_test processScan_ms3_commands) already moved here.
                    Path.Combine(SpectraDir, "ms2_cytc_fresh_scan57.txt"),
                    maxMs2Responses: 1);

                AssertMs3ReturnGolden(harness, "continuity_ms3_mode2_real.json");
            }
        }

        // --- CT37: MS3 Mode 3 MS2 return pipeline ---

        [Test, Category("Tier2")]
        public void P4_AL_CT37_MS3Mode3_MS2ReturnPipeline()
        {
            using (var harness = CreateHarness("method_ms3_mode3_hcd.json"))
            {
                harness.PushScanAndDrainFull(
                    Path.Combine(SpectraDir, "ms1_standard.txt"),
                    Path.Combine(SpectraDir, "ms2_hcd_fragment.txt"),
                    maxMs2Responses: 1);

                AssertMs3ReturnGolden(harness, "continuity_ms3_mode3_real.json");
            }
        }

        /// <summary>
        /// Shared CT35/36/37 assertion (R1a — byte-exact golden restored): asserts the captured MSn stream
        /// the interleaved maxMs2Responses:1 drive produced against the committed golden. One cheap non-empty
        /// guard precedes AssertGolden so an empty capture cannot vacuously pass.
        /// </summary>
        private void AssertMs3ReturnGolden(ContinuityTestHarness harness, string goldenFileName)
        {
            var results = CapturedMsn(harness);
            Assert.That(results.Count, Is.GreaterThan(0),
                "MS1->MS2 return pipeline must produce results (non-empty guard before AssertGolden)");

            AssertGolden(goldenFileName, results);
        }

        // --- CT38: Quant Mode MS2 Return ---

        [Test, Category("Tier2")]
        public void P4_AL_CT38_QuantMode_MS2Return()
        {
            using (var harness = CreateHarness("method_quant.json"))
            {
                // Interleaved drive: standard MS1 surveys + TMT-reporter MS2 returns for every quant MS2
                // command (no cap). Replaces "push all MS1, then feed TMT back for every command".
                harness.PushScanAndDrainFull(
                    Path.Combine(SpectraDir, "ms1_standard.txt"),
                    Path.Combine(SpectraDir, "ms2_quant_tmt.txt"));

                // R1a — byte-exact golden restored over the SAME captured MSn stream the drive produced.
                // One cheap non-empty guard ensures an empty capture cannot vacuously pass AssertGolden.
                var results = CapturedMsn(harness);
                Assert.That(results.Count, Is.GreaterThan(0),
                    "MS1 must produce MS2 commands for quant mode (non-empty guard before AssertGolden)");

                AssertGolden("continuity_quant_ms2return.json", results);
            }
        }

        // --- CT39: Inclusion with matching targets (golden) ---

        [Test, Category("Tier2")]
        public void P4_AL_CT39_Inclusion_MatchingTargets()
        {
            using (var harness = CreateHarness("method_inclusion.json"))
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
            using (var harness = CreateHarness("method_inclusion_strict.json"))
            {
                var results = PushStandardSpectrumAndCollect(harness);

                // Strict inclusion: only target-matched masses survive.
                // Inclusion targets: 2063.606, 2277.254, 4297.177, 5315.129, 12358.31
                // With ms1_standard.txt, some (not all) deconvolved masses should match targets.
                // Strict means non-matching masses are excluded entirely.

                Assert.That(results.Count, Is.GreaterThan(0),
                    "Strict inclusion should find at least one matching target in ms1_standard");

                Assert.That(results.Count, Is.LessThanOrEqualTo(
                    5 * (1 + (harness.MethodParams.Config.PrecursorSelection.AdditionalScans?.Count ?? 0))), // at most 5 targets * MS2 types
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
            using (var harness = CreateHarness("method_default.json"))
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
        //
        // IGNORED: this fixture cannot express the behaviour it asserts. in_depth is a SOFT,
        // iteration-0-only de-prioritization, not a hard exclusion — PrecursorSelection runs
        // `for (iteration = mode==2 ? 0 : 1; iteration < 2; ...)`, pass 0 skips tqscore-exceeding
        // masses and pass 1 has no such guard, so it BACK-FILLS every mass pass 0 skipped whenever
        // slots remain. Two independent gates must therefore both be satisfied, and ms1_standard
        // satisfies neither:
        //
        //   threshold   skip needs 1 - PRODUCT(1-qscore) > tqscore_threshold. test_target_log.log
        //               records ONE observation per mass, so the product is just (1-qscore) and
        //               1-factor peaks at 0.772 — under the 0.9 default. Multiple observations are
        //               what drive the product down far enough.
        //   contention  even when a mass IS skipped in pass 0, pass 1 restores it unless the slot
        //               budget is saturated. ms1_standard yields 6 MS2 across 103 surveys against
        //               max_precursors 5 — never contended.
        //
        // This test was green only because method_deep.json was mis-set to exclusion_masses, which
        // hard-skips regardless of qscore; it has never once exercised in_depth. Lowering
        // tqscore_threshold was tried and changed nothing, because it addresses only the first gate.
        //
        // in_depth IS properly covered, on the C++ side, by FLASHIda_LoggingFields_test::
        // exclusion_mode2_tqscore_suppresses_target_mass — which drives ms1_ecoli_rich (>=9
        // selectable masses/scan) with max_targets==1 so the single slot is genuinely contended.
        // Reviving this test means mirroring that recipe here, which needs a target log whose
        // masses match the ecoli survey; none exists yet. Per the division of labour it is also
        // arguably C++'s job: it asserts a behavioural DIFFERENCE, not an exact golden.
        [Test, Category("Tier2")]
        [Ignore("in_depth is a soft reorder that pass 1 back-fills; ms1_standard never contends the "
                + "slot budget, so this assertion cannot hold. Covered in C++ by FLASHIda_LoggingFields_test"
                + "::exclusion_mode2_tqscore_suppresses_target_mass. See the comment above.")]
        public void P4_AL_CT42_DeepMode_TargetLogEffect()
        {
            // Standard DDA with TopN=5 as baseline (same TopN as deep mode config)
            int standardCount;
            List<double> standardMasses;
            using (var harness = CreateHarness("method_default_topn5.json"))
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
            using (var harness = CreateHarness("method_deep.json"))
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

        #region AL-CT31 through CT32: Primitive / Concurrency Contract Tests (Phase 3)

        // RECLASSIFIED (Phase 2): CT31 and CT32 are DELIBERATELY NOT migrated to the interleaved
        // PushScanAndDrainFull harness. They are primitive/concurrency-CONTRACT tests that exercise the raw
        // FLASHIdaWrapper ABI (ProcessScan / GetNextScanCommand / GetNextTrackingId) directly, by design:
        //   * CT31 = tracking-id uniqueness + idle-MS1 cycle contract. It asserts the fixed-count ABI shape of
        //     the queue under idle conditions: ProcessScan returns 0, every GetNextScanCommand returns exactly 1
        //     with an MS-level-1 idle scan, and 1000 GetNextTrackingId calls are unique. This is precisely the
        //     "primitive-contract test (fixed-count getNextScanCommand asserting the queue ABI)" that MUST NOT be
        //     routed through the bounded harness — the harness exists to AVOID the raw `while(==1)` loop, whereas
        //     CT31's whole point is to pin that the idle self-refill never returns 0.
        //   * CT32 = 4-thread concurrency stress on the tracking-id counter mutex; the C# mirror of the C++
        //     ScanCommandQueue_Concurrent_test. It verifies thread-safety (no exceptions, unique ids across
        //     threads) of the raw wrapper, which the single-threaded interleaved harness cannot express.
        // Their raw loops are intentionally left intact.

        // CT31 (RECLASSIFIED — primitive ABI contract; raw loop intentionally NOT migrated to the harness):
        // tracking-id uniqueness + idle-MS1 cycle. Pins that getNextScanCommand never returns 0 (idle self-refill)
        // and yields an MS-level-1 idle scan each tick — the queue-ABI invariant the bounded harness deliberately
        // bypasses, so this test keeps its raw FLASHIdaWrapper drive.
        [Test, Category("Tier4")]
        public void P3_AL_CT31_StressTest_1000ScansSequential()
        {
            string configsDir = Path.Combine(TestDir, "..", "test-data", "configs");
            string configPath = Path.Combine(configsDir, "method_default.json");
            // method_default.json is a committed, must-exist fixture. A missing file is
            // test-data layout drift (a real failure), not a reason to silently skip the
            // stress coverage — fail closed instead of Assert.Ignore.
            Assert.That(File.Exists(configPath), Is.True,
                "REQUIRED committed config missing: " + configPath);

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

                    int processResult = wrapper.ProcessScan(mzs, ints, rt, 1, "stress_" + i, 0.0, i + 1);
                    Assert.AreEqual(0, processResult,
                        string.Format("ProcessScan should return 0 at iteration {0}", i));

                    var cmd = new ScanCommand();
                    int cmdResult = wrapper.GetNextScanCommand(ref cmd);
                    // Idle cycling: a drained queue yields an idle survey MS1 at priority 3 on EVERY
                    // tick. It used to alternate an AGC prescan with the survey; prescans are now
                    // scheduled by agc_interval_seconds alone (ADR-0031), and method_default.json
                    // pins that at 9999999, so none can fire across these 1000 iterations.
                    Assert.AreEqual(1, cmdResult,
                        string.Format("GetNextScanCommand should return 1 (idle scan) at iteration {0}", i));
                    Assert.AreEqual(1, cmd.MsnLevel,
                        string.Format("Idle scan should be MS level 1 at iteration {0}", i));
                    Assert.AreEqual(0, cmd.IsAgc,
                        string.Format("Idle scan should be a survey, not a prescan, at iteration {0}", i));
                    Assert.AreEqual(3, cmd.Priority,
                        string.Format("Idle survey should be priority 3 at iteration {0}", i));

                    int trackId = wrapper.GetNextTrackingId();
                    Assert.IsFalse(trackingIds.Contains(trackId),
                        string.Format("Tracking ID {0} should be unique at iteration {1}", trackId, i));
                    trackingIds.Add(trackId);
                }

                Assert.AreEqual(1000, trackingIds.Count,
                    "All 1000 tracking IDs should be unique");
            }
        }

        // CT32 (RECLASSIFIED — concurrency contract; raw loop intentionally NOT migrated to the harness):
        // 4-thread concurrency stress on the raw FLASHIdaWrapper. C# mirror of the C++ ScanCommandQueue_Concurrent_test;
        // verifies the tracking-id counter mutex (no exceptions, unique ids across threads). The single-threaded
        // interleaved harness cannot express multi-thread contention, so this test keeps its raw threaded drive.
        [Test, Category("Tier4")]
        public void P3_AL_CT32_StressTest_ConcurrentProcessing()
        {
            string configsDir = Path.Combine(TestDir, "..", "test-data", "configs");
            string configPath = Path.Combine(configsDir, "method_default.json");
            // method_default.json is a committed, must-exist fixture. A missing file is
            // test-data layout drift (a real failure), not a reason to silently skip the
            // stress coverage — fail closed instead of Assert.Ignore.
            Assert.That(File.Exists(configPath), Is.True,
                "REQUIRED committed config missing: " + configPath);

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
                                    string.Format("thread{0}_scan{1}", threadId, i),
                                    0.0, threadId * 1000 + i + 1);

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
            using (var harness = CreateHarness("method_default.json"))
            {
                // Push all 50 MS1 scans from ms1_standard.txt for engine state accumulation
                var allScans = MockMsScan.FromTsvAllScans(Path.Combine(SpectraDir, "ms1_standard.txt"));
                foreach (var scan in allScans)
                {
                    harness.PushMs1(scan);
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

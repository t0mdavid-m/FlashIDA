using System.Collections.Generic;
using System.IO;
using System.Linq;
using Flash.Tests.Mocks;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// The C# half of the ADR-0031 drain-sentinel contract.
    ///
    /// Three production drain loops terminate on <c>MsnLevel == 1 &amp;&amp; Priority == 3</c> —
    /// <c>FLASHIdaWrapper</c>'s offline harness (twice) and <c>ContinuityTestHarness.PushScan</c>.
    /// That predicate is only correct while the engine emits priority 3 for the idle survey MS1 and
    /// for nothing else. The emission lives in C++ and the predicate lives here, so the contract is
    /// pinned on both CI paths: <c>FLASHIda_ProcessScan_test::only_the_idle_survey_is_emitted_at_priority_3</c>
    /// asserts it against the engine directly, and this fixture asserts it across the P/Invoke
    /// boundary through the same struct marshalling the production loops use.
    ///
    /// If either half is deleted the other still passes, which is the point — a drift guard that
    /// only exists on the side that changed is not a guard.
    /// </summary>
    [TestFixture]
    public class IdleSurveySentinelTests
    {
        private static string TestDir => TestContext.CurrentContext.TestDirectory;
        private static string TestDataDir => Path.Combine(TestDir, "..", "test-data");
        private static string ConfigDir => Path.Combine(TestDataDir, "configs");
        private static string SpectraDir => Path.Combine(TestDataDir, "spectra");

        /// <summary>
        /// Drive one real MS1 through the engine and check every command it emits: a priority-3
        /// command must be a non-AGC MS1.
        ///
        /// Anti-vacuity is asserted twice. The drive must produce real MS2 workload (so the loop had
        /// non-priority-3 commands to reject rather than passing on an empty list), and it must
        /// produce at least one priority-3 command (so the per-command assertions actually ran).
        /// Without both, a regression that emitted nothing at all would pass this test.
        /// </summary>
        [Test]
        public void Priority3_IsOnlyEverAnIdleSurveyMs1()
        {
            string configPath = Path.Combine(ConfigDir, "method_dda_hcd.json");
            string ms1Path = Path.Combine(SpectraDir, "ms1_standard.txt");
            Assert.IsTrue(File.Exists(configPath), "method_dda_hcd.json not found at " + configPath);
            Assert.IsTrue(File.Exists(ms1Path), "ms1_standard.txt not found at " + ms1Path);

            using (var harness = new ContinuityTestHarness(configPath))
            {
                var scans = MockMsScan.FromTsvAllScans(ms1Path);
                Assert.IsNotEmpty(scans, "no MS1 scans loaded");

                // PushMs1 stamps the spectrum with a real engine-emitted survey id so it clears the
                // MS1 gate, then drains — CapturedRecords holds the RAW ScanCommand structs, which
                // is what carries Priority (CollectResults reads the built request, which does not).
                harness.PushMs1(scans[0]);
                foreach (var s in scans) s.Dispose();

                List<ScanCommandRecord> emitted = harness.CapturedRecords;
                Assert.IsNotEmpty(emitted, "engine emitted no commands at all");

                var priority3 = emitted.Where(r => r.Priority == 3).ToList();
                var ms2 = emitted.Where(r => r.MsnLevel == 2).ToList();

                foreach (var r in priority3)
                {
                    Assert.AreEqual(1, r.MsnLevel,
                        "priority 3 must be an MS1 — the three C# drain loops treat it as 'queue drained' "
                        + "and would stop early on anything else. Offending description: " + r.ScanDescription);
                    Assert.IsFalse(r.IsAGC,
                        "priority 3 must not be an AGC prescan — prescans are scheduled by "
                        + "agc_interval_seconds and bypass the queue entirely (ADR-0031)");
                }

                Assert.IsNotEmpty(ms2, "anti-vacuous: the drive produced no MS2 commands, so the "
                    + "priority filter above had nothing to reject");
                Assert.IsNotEmpty(priority3, "anti-vacuous: no priority-3 command was emitted, so the "
                    + "per-command assertions never ran");
            }
        }

        /// <summary>
        /// A drained queue yields an idle survey, never an AGC prescan.
        ///
        /// The committed test configs pin agc_interval_seconds at 9999999 precisely so golden capture
        /// cannot depend on wall clock, which means no scheduled prescan can fire during a test run.
        /// So under a committed config every command the engine emits has IsAGC == false — and before
        /// ADR-0031 that was flatly untrue: the drained-queue path fabricated one prescan per drain,
        /// and roughly half of every scan_commands golden was AGC rows.
        /// </summary>
        [Test]
        public void DrainedQueue_EmitsNoAgcPrescan_UnderAPinnedInterval()
        {
            string configPath = Path.Combine(ConfigDir, "method_dda_hcd.json");
            string ms1Path = Path.Combine(SpectraDir, "ms1_standard.txt");
            Assert.IsTrue(File.Exists(configPath), "method_dda_hcd.json not found at " + configPath);

            using (var harness = new ContinuityTestHarness(configPath))
            {
                var scans = MockMsScan.FromTsvAllScans(ms1Path);
                Assert.IsNotEmpty(scans, "no MS1 scans loaded");

                harness.PushMs1(scans[0]);
                foreach (var s in scans) s.Dispose();

                List<ScanCommandRecord> emitted = harness.CapturedRecords;
                Assert.IsNotEmpty(emitted, "anti-vacuous: engine emitted no commands at all");

                var prescans = emitted.Where(r => r.IsAGC).ToList();
                Assert.IsEmpty(prescans,
                    "a drained queue must not fabricate an AGC prescan; found " + prescans.Count
                    + ". If agc_interval_seconds has been un-pinned in this config, the log goldens "
                    + "have become wall-clock dependent too.");
            }
        }
    }
}

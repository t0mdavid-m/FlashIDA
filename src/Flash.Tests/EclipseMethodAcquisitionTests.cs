using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Flash;

namespace Flash.Tests
{
    /// <summary>
    /// End-to-end proof that method_eclipse_cytc_ambiguity.json ACQUIRES, not merely that it loads.
    ///
    /// This is the distinction the whole config reshape exists to make. A config that loads clean,
    /// runs green and fires zero MS3 is the failure mode being designed out; a test that only
    /// asserts "MethodParameters.Load did not throw" would pass on exactly that config and prove
    /// nothing. So every assertion here is about commands the engine actually emitted.
    ///
    /// Deliberately NOT a log-golden case. Adding an 18th mode would mean capturing a golden, which
    /// is a separate signed-off step; these assertions read the raw ScanCommand structs the harness
    /// captured, so the test is self-contained and has nothing to recapture.
    /// </summary>
    [TestFixture]
    public class EclipseMethodAcquisitionTests
    {
        private static string TestDataDir =>
            Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "test-data");

        private static string ConfigPath =>
            Path.Combine(TestDataDir, "configs", "method_eclipse_cytc_ambiguity.json");

        private static string Ms1Path =>
            Path.Combine(TestDataDir, "spectra", "ms1_cytc.txt");

        private static string Ms2Path =>
            Path.Combine(TestDataDir, "spectra", "ms2_cytc_fresh_scan57.txt");

        [Test, Category("Tier2")]
        public void EclipseCytcAmbiguity_LoadsWithTheExpectedEffectiveSettings()
        {
            Assert.That(File.Exists(ConfigPath), Is.True,
                "REQUIRED committed config missing: " + ConfigPath);

            var mp = MethodParameters.Load(ConfigPath);

            // The point of the reshape: these are STATED in the file, not inherited from a default
            // that lives in another language. Four committed configs used to state an MS3 budget of
            // 200 into a dead key and silently run 3; asserting the stated values here is what stops
            // that class of drift coming back through this method.
            Assert.AreEqual("ambiguity", mp.Config.Characterization.Mode,
                "mode is the single MS3 switch and must be stated, not defaulted");
            Assert.AreEqual(3, mp.Config.Characterization.MaxTargets,
                "the MS3 budget is authored in characterization");
            Assert.AreEqual(5, mp.Config.PrecursorSelection.MaxPrecursors,
                "max_precursors is the MS2 count per survey");
            Assert.AreEqual("none", mp.Config.PrecursorSelection.Targeting,
                "untargeted DDA -- 'all proteoforms', not an inclusion list");
            Assert.IsEmpty(mp.Config.Files.InclusionList ?? "",
                "an inclusion list would contradict targeting: none");
            Assert.AreEqual(105, (mp.Config.Characterization.ProteinSequence ?? "").Length,
                "the 105-residue equine cytC sequence MS3 fragments are matched against");

            // fragment_count is the metric that makes the CE sweep meaningful: it scores each energy
            // by how many fragments match the known ladder, which requires the sequence above.
            Assert.IsNotNull(mp.Config.PrecursorSelection.Exploration,
                "the MS2 CE sweep lives in precursor_selection, the section that dispatches MS2");
            Assert.AreEqual("fragment_count", mp.Config.PrecursorSelection.Exploration.Metric);
            Assert.AreEqual(15.0, mp.Config.PrecursorSelection.Exploration.CEMin, 1e-9);
            Assert.AreEqual(50.0, mp.Config.PrecursorSelection.Exploration.CEMax, 1e-9);
            Assert.AreEqual(1.0, mp.Config.PrecursorSelection.Exploration.CEStep, 1e-9);
        }

        [Test, Category("Tier2")]
        public void EclipseCytcAmbiguity_SweepsCollisionEnergyAndReachesMS3()
        {
            Assert.That(File.Exists(ConfigPath), Is.True, "REQUIRED config missing: " + ConfigPath);
            Assert.That(File.Exists(Ms1Path), Is.True, "REQUIRED spectrum missing: " + Ms1Path);
            Assert.That(File.Exists(Ms2Path), Is.True, "REQUIRED spectrum missing: " + Ms2Path);

            using (var harness = new Mocks.ContinuityTestHarness(ConfigPath))
            {
                harness.PushScanAndDrainFull(Ms1Path, Ms2Path);

                var cmds = harness.CapturedRecords.Where(r => !r.IsAGC).ToList();
                Assert.That(cmds.Count, Is.GreaterThan(0), "engine emitted no non-AGC commands at all");

                // 1. MS2 must actually sweep. A single CE would mean the exploration block was
                //    silently dropped -- which is exactly what an ordinal `metric != "none"` compare
                //    used to do to a config that wrote "None", and what a typo'd metric used to do
                //    by falling through to None.
                var ms2Ces = cmds.Where(r => r.MsnLevel == 2).Select(r => r.CollisionEnergy).Distinct().ToList();
                Assert.That(ms2Ces.Count, Is.GreaterThan(1),
                    "MS2 collision energy never varied -- the CE sweep did not run. Distinct CEs seen: "
                    + string.Join(",", ms2Ces));
                Assert.That(ms2Ces.Where(ce => ce > 0), Is.All.InRange(15, 50),
                    "every swept CE should fall inside the configured 15-50 range");

                // 2. MS3 must be reached. THE assertion: a config that loads, runs green and fires
                //    zero MS3 is the silent no-op this whole change exists to eliminate.
                int ms3 = cmds.Count(r => r.MsnLevel == 3);
                Assert.That(ms3, Is.GreaterThanOrEqualTo(1),
                    "no MS3 command was emitted. characterization.mode is 'ambiguity', so this config "
                    + "claims to characterize -- if it acquires no MS3 the claim is false. Commands by "
                    + "level: " + string.Join(", ", cmds.GroupBy(r => r.MsnLevel)
                        .OrderBy(g => g.Key).Select(g => "MS" + g.Key + "=" + g.Count())));

                // 3. Every MS3 must descend from a real MS2, not appear free-floating.
                foreach (var m3 in cmds.Where(r => r.MsnLevel == 3))
                    Assert.That(string.IsNullOrEmpty(m3.ParentScanId), Is.False,
                        "an MS3 command carried no parent scan id");
            }
        }
    }
}

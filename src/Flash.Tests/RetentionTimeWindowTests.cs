using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Flash.Tests.Mocks;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// ADR-0051: retention time reaches the engine in the instrument's MINUTES, and the engine decides
    /// in SECONDS -- the unit of <c>precursor_selection.rt_window</c> and of every file it loads. The
    /// one conversion lives in the C export, so only a test that crosses it the way the host does can
    /// see it. This fixture drives the production path: fixture header -> <c>MockMsScan</c> StartTime
    /// in minutes -> <c>ScanData.From</c> -> <c>FLASHIdaWrapper.ProcessScan</c> -> export x 60 ->
    /// <c>FLASHIda::processScan</c>.
    ///
    /// Until ADR-0051 nothing converted. On the instrument no exclusion bar ever expired and an
    /// inclusion row with a finite window was never active, while every C++ test passed: they call
    /// <c>processScan</c> directly, in seconds, which is the engine's own contract. No golden saw it
    /// either, because no golden's surveys span more than 64 s.
    ///
    /// Shared data: <c>method_inclusion_cytc_strict.json</c> -- strict inclusion of cytC 12351.3,
    /// <c>tqscore_threshold</c> 0.0 so every acquisition arms both bars -- over <c>ms1_cytc.txt</c>,
    /// whose 50 survey headers run 8.2-72.0. Both harnesses read those headers as SECONDS (see
    /// test-data/spectra/FIXTURES.md), so every time below is in harness seconds. cytC is resolved at
    /// 8.3 s and, after 38.3 s, first at 43.0 s.
    /// </summary>
    [TestFixture]
    public class RetentionTimeWindowTests
    {
        private static string TestDir => TestContext.CurrentContext.TestDirectory;
        private static string TestDataDir => Path.Combine(TestDir, "..", "test-data");
        private static string ConfigDir => Path.Combine(TestDataDir, "configs");
        private static string SpectraDir => Path.Combine(TestDataDir, "spectra");

        private const int CytcNominalMass = 12351;

        /// <summary>
        /// A 180 s window holds for the whole 64 s fixture: cytC is commanded once. The non-vacuity
        /// partner of <see cref="Bar_ExpiresOnceOlderThanRtWindowSeconds"/> -- a bar released early on
        /// the host path would show up here as a second command.
        /// </summary>
        [Test]
        public void Bar_HoldsForRtWindowSeconds()
        {
            var t = CytcSurveyTimes(180);
            Assert.That(t.Count, Is.EqualTo(1),
                "64 s of surveys inside a 180 s window: cytC must be commanded once; commanded at " + Show(t));
        }

        /// <summary>
        /// The regression test for the bridge defect. With a 30 s window the bar set at 8.32 s has
        /// expired by the 42.98 s survey, so cytC is commanded again. Before ADR-0051 the engine read
        /// the 30 as minutes against a fixture that lasts about one, so the bar outlived the run and
        /// cytC was commanded once. Both bars and the qscore ledger expire together, because the ledger
        /// is not re-stamped while the species is barred.
        /// </summary>
        [Test]
        public void Bar_ExpiresOnceOlderThanRtWindowSeconds()
        {
            var t = CytcSurveyTimes(30);
            Assert.That(t.Count, Is.GreaterThanOrEqualTo(2),
                "a 30 s window must release cytC and command it again; commanded at " + Show(t));
            for (int i = 1; i < t.Count; i++)
                Assert.That(t[i] - t[i - 1], Is.InRange(30.0, 40.0),
                    "not before the window ends, and at the first cytC survey after it; commanded at " + Show(t));
        }

        /// <summary>
        /// An inclusion row written for 0.5-0.8 min acts between 30 and 48 s. The file is in minutes and
        /// the loader converts it to the engine's seconds; before ADR-0051 the engine compared that
        /// [30, 48] with the minutes it was handed, so the row was never active and strict inclusion
        /// commanded nothing.
        /// </summary>
        [Test]
        public void InclusionRow_IsActiveOnlyInsideItsWindow()
        {
            var t = CytcSurveyTimes(180, "inclusion_cytc_window.txt");
            Assert.That(t.Count, Is.EqualTo(1),
                "the 0.5-0.8 min row must be active for its window and cytC commanded once; commanded at " + Show(t));
            Assert.That(t[0], Is.InRange(30.0, 48.0), "cytC must be commanded inside the row's window");
        }

        /// <summary>
        /// Survey retention time (seconds) of every MS2 the engine commanded at cytC's nominal mass, in
        /// command order: scan_commands.tsv's MS2 rows -> parent_tracking_id -> that survey's
        /// scan_results.tsv row -> rt, which the log writes in MINUTES.
        /// </summary>
        private static List<double> CytcSurveyTimes(double rtWindowSeconds, string inclusionList = null)
        {
            string configPath = Path.Combine(ConfigDir, "method_inclusion_cytc_strict.json");
            string ms1Path = Path.Combine(SpectraDir, "ms1_cytc.txt");
            string ms2Path = Path.Combine(SpectraDir, "ms2_cytc_fresh_scan57.txt");
            Assert.IsTrue(File.Exists(configPath), "method_inclusion_cytc_strict.json not found at " + configPath);
            Assert.IsTrue(File.Exists(ms1Path), "ms1_cytc.txt not found at " + ms1Path);
            Assert.IsTrue(File.Exists(ms2Path), "ms2_cytc_fresh_scan57.txt not found at " + ms2Path);

            // IdaLogger opens its streams in APPEND mode and never creates a directory, so every drive
            // gets its own fresh, empty folder.
            string logDir = Path.Combine(Path.GetTempPath(), "flashida-rt-window", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(logDir);
            try
            {
                using (var harness = new ContinuityTestHarness(configPath, configure: mp =>
                {
                    mp.Config.Runtime.LogDir = logDir;
                    mp.Config.PrecursorSelection.RTWindow = rtWindowSeconds;
                    // configure runs AFTER the harness resolves relative paths, so this one is absolute.
                    if (inclusionList != null)
                        mp.Config.Files.InclusionList = Path.GetFullPath(Path.Combine(ConfigDir, inclusionList));
                }))
                {
                    harness.PushScanAndDrainFull(ms1Path, ms2Path);
                } // Dispose closes the engine and its log streams

                var surveyRtMinutes = new Dictionary<string, double>();
                var results = ParseTsv(Path.Combine(logDir, "scan_results.tsv"), out var rh);
                int rId = Array.IndexOf(rh, "tracking_id"), rLevel = Array.IndexOf(rh, "ms_level"), rRt = Array.IndexOf(rh, "rt");
                Assert.That(new[] { rId, rLevel, rRt }, Has.All.GreaterThanOrEqualTo(0), "scan_results.tsv columns");
                foreach (var r in results)
                    if (r[rLevel] == "1") surveyRtMinutes[r[rId]] = double.Parse(r[rRt], CultureInfo.InvariantCulture);

                var commands = ParseTsv(Path.Combine(logDir, "scan_commands.tsv"), out var ch);
                int cLevel = Array.IndexOf(ch, "ms_level"), cMass = Array.IndexOf(ch, "mono_mass"), cParent = Array.IndexOf(ch, "parent_tracking_id");
                Assert.That(new[] { cLevel, cMass, cParent }, Has.All.GreaterThanOrEqualTo(0), "scan_commands.tsv columns");

                var times = new List<double>();
                foreach (var c in commands)
                {
                    if (c[cLevel] != "2") continue;
                    if ((int)Math.Round(double.Parse(c[cMass], CultureInfo.InvariantCulture)) != CytcNominalMass) continue;
                    Assert.IsTrue(surveyRtMinutes.TryGetValue(c[cParent], out double rtMinutes),
                        "MS2 parent " + c[cParent] + " has no survey row in scan_results.tsv");
                    times.Add(rtMinutes * 60.0);
                }
                return times;
            }
            finally
            {
                try { Directory.Delete(logDir, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static List<string[]> ParseTsv(string path, out string[] header)
        {
            Assert.IsTrue(File.Exists(path), "the engine wrote no " + Path.GetFileName(path));
            var lines = File.ReadAllLines(path);
            header = lines.Length > 0 ? lines[0].Split('\t') : new string[0];
            var rows = new List<string[]>();
            for (int i = 1; i < lines.Length; i++) rows.Add(lines[i].Split('\t'));
            return rows;
        }

        private static string Show(List<double> times) =>
            times.Count == 0
                ? "(none)"
                : string.Join(", ", times.Select(x => x.ToString("F2", CultureInfo.InvariantCulture))) + " s";
    }
}

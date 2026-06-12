using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Flash;
using Flash.IDA;
using Flash.Tests.Mocks;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// Golden tests for FLASHIda's four log streams across all execution paths. Where the C++
    /// FLASHIda_LoggingFields suite asserts drift-stable PLAUSIBILITY (ranges/sets/signs), this
    /// suite locks EXACT behaviour: it drives the real C++ engine in-process (via
    /// ContinuityTestHarness, NOT Flash.exe Main which never wires runtime paths or feeds MSn back),
    /// captures the four log files per case, normalizes only the non-deterministic content
    /// (timestamps/durations -> &lt;TS&gt;/&lt;DUR&gt;, tracking ids -> T&lt;n&gt; preserving joins),
    /// and compares every remaining column exactly against committed goldens.
    ///
    /// Capture/regen: run with env LOG_GOLDEN_CAPTURE=1 to (re)write the goldens under
    /// test-data/golden/logs/&lt;case&gt;/; review the normalized T&lt;n&gt;/&lt;TS&gt; diff and commit.
    /// Without a committed golden the test FAILS (a missing reference can never pass silently),
    /// mirroring the existing AssertGolden / regression-runner -captureMode flow. The normalized
    /// output is always written to bin/log-golden-output/&lt;case&gt;/ for the CI failure-diff artifact.
    /// </summary>
    [TestFixture]
    public class FLASHIdaLogGolden_test
    {
        private static string TestDir => TestContext.CurrentContext.TestDirectory;
        private static string TestDataDir => Path.Combine(TestDir, "..", "test-data");
        private static string ConfigDir => Path.Combine(TestDataDir, "configs");
        private static string SpectraDir => Path.Combine(TestDataDir, "spectra");
        private static string GoldenDir => Path.Combine(TestDataDir, "golden", "logs");
        private static string OutputDir => Path.Combine(TestDir, "log-golden-output");

        private static bool Capture =>
            Environment.GetEnvironmentVariable("LOG_GOLDEN_CAPTURE") == "1";

        [OneTimeSetUp]
        public void Setup()
        {
            if (!log4net.LogManager.GetRepository().Configured)
            {
                log4net.Config.BasicConfigurator.Configure(
                    new log4net.Appender.ConsoleAppender { Threshold = log4net.Core.Level.Off });
            }
            Directory.CreateDirectory(OutputDir);
        }

        // ---- cases (one per execution path) -------------------------------------------------

        [Test, Category("Tier2")]
        public void Golden_DDA_HCD() =>
            RunCase("dda_hcd", "method_dda_hcd.json", "ms1_standard.txt", "ms2_hcd_fragment.txt");

        [Test, Category("Tier2")]
        public void Golden_DDA_ETD() =>
            RunCase("dda_etd", "method_dda_etd.json", "ms1_standard.txt", "ms2_hcd_fragment.txt");

        [Test, Category("Tier2")]
        public void Golden_Exploration() =>
            RunCase("exploration", "method_exploration.json", "ms1_standard.txt", "ms2_hcd_fragment.txt");

        [Test, Category("Tier2")]
        public void Golden_Quant() =>
            RunCase("quant", "method_quant.json", "ms1_standard.txt", "ms2_hcd_fragment.txt");

        [Test, Category("Tier2")]
        public void Golden_TagTargeting() =>
            RunCase("tag", "method_tag_targeting.json", "ms1_standard.txt", "ms2_hcd_fragment.txt");

        [Test, Category("Tier2")]
        public void Golden_Inclusion() =>
            RunCase("inclusion", "method_inclusion.json", "ms1_cytc.txt", "ms2_cytc_fresh_scan57.txt");

        [Test, Category("Tier2")]
        public void Golden_Exclusion() =>
            RunCase("exclusion", "method_exclusion.json", "ms1_standard.txt", "ms2_hcd_fragment.txt");

        [Test, Category("Tier2")]
        public void Golden_Faims() =>
            RunCase("faims", "method_faims_3cv.json", "ms1_standard.txt", "ms2_hcd_fragment.txt",
                    forceFaims: true);

        [Test, Category("Tier2")]
        public void Golden_MS3_CytC() =>
            RunCase("ms3_cytc", "method_ms3_mode1_hcd.json", "ms1_cytc.txt", "ms2_cytc_scan149.txt",
                    feedMs3: true);

        // ---- engine driver + golden compare -------------------------------------------------

        private void RunCase(string caseName, string configFile, string ms1File, string ms2File,
            bool feedMs3 = false, bool forceFaims = false)
        {
            string caseDir = Path.Combine(OutputDir, caseName);
            Directory.CreateDirectory(caseDir);
            foreach (var f in LogGoldenComparer.FileNames)
            {
                string p = Path.Combine(caseDir, f);
                if (File.Exists(p)) File.Delete(p);
            }

            using (var harness = new ContinuityTestHarness(
                Path.Combine(ConfigDir, configFile), forceFaims, false,
                configure: mp =>
                {
                    mp.Config.Runtime.IdaLogPath = Path.Combine(caseDir, LogGoldenComparer.IdaLogName);
                    mp.Config.Runtime.ScanCommandsPath = Path.Combine(caseDir, LogGoldenComparer.CommandsName);
                    mp.Config.Runtime.ScanResultsPath = Path.Combine(caseDir, LogGoldenComparer.ResultsName);
                    mp.Config.Runtime.IdentificationLogPath = Path.Combine(caseDir, LogGoldenComparer.IdentificationName);
                }))
            {
                DriveCycle(harness,
                    Path.Combine(SpectraDir, ms1File),
                    Path.Combine(SpectraDir, ms2File),
                    feedMs3);
            } // Dispose() closes the C++ engine and flushes/closes the log streams

            // Fail-closed: a case that produced no scan commands is broken, never a valid golden.
            string commandsPath = Path.Combine(caseDir, LogGoldenComparer.CommandsName);
            int cmdRows = File.Exists(commandsPath) ? Math.Max(0, File.ReadAllLines(commandsPath).Length - 1) : 0;
            Assert.That(cmdRows, Is.GreaterThan(0),
                $"Case '{caseName}' produced no scan commands — cannot golden an empty run.");

            var idMap = LogGoldenComparer.BuildIdMap(caseDir);
            foreach (var fileName in LogGoldenComparer.FileNames)
                CompareOrCapture(caseName, fileName, LogGoldenComparer.Normalize(caseDir, fileName, idMap));

            if (Capture)
                Assert.Pass($"Captured goldens for '{caseName}'. Review the normalized diff and commit.");
        }

        private void DriveCycle(ContinuityTestHarness harness, string ms1Path, string ms2Path, bool feedMs3)
        {
            // MS1 -> MS2 commands
            foreach (var s in MockMsScan.FromTsvAllScans(ms1Path)) { harness.PushScan(s); s.Dispose(); }

            // Feed each MS2 command back with real fragment data -> MS2 results + (maybe) MS3 commands
            var ms2Cmds = harness.Factory.CreatedScans
                .Select(s => ScanCommandRecord.FromCustomScan(s))
                .Where(r => r.ScanType == "MSn" && r.MsnLevel == 2)
                .ToList();
            foreach (var cmd in ms2Cmds)
            {
                var ms2 = MockMsScan.FromTsvAsMSn(ms2Path, 2, cmd.ScanDescription, cmd.PrecursorMz, cmd.ChargeState);
                harness.PushScan(ms2);
                ms2.Dispose();
            }

            if (!feedMs3) return;

            // Feed each MS3 command back -> MS3 results + identification rows
            var ms3Cmds = harness.Factory.CreatedScans
                .Select(s => ScanCommandRecord.FromCustomScan(s))
                .Where(r => r.ScanType == "MSn" && r.MsnLevel == 3)
                .ToList();
            foreach (var cmd in ms3Cmds)
            {
                var ms3 = MockMsScan.FromTsvAsMSn(ms2Path, 3, cmd.ScanDescription, cmd.PrecursorMz, cmd.ChargeState);
                harness.PushScan(ms3);
                ms3.Dispose();
            }
        }

        private void CompareOrCapture(string caseName, string fileName, string normalized)
        {
            string outCaseDir = Path.Combine(OutputDir, caseName);
            Directory.CreateDirectory(outCaseDir);
            File.WriteAllText(Path.Combine(outCaseDir, fileName + ".normalized"), normalized);

            string goldenCaseDir = Path.Combine(GoldenDir, caseName);
            string goldenPath = Path.Combine(goldenCaseDir, fileName + ".golden.tsv");

            if (Capture)
            {
                Directory.CreateDirectory(goldenCaseDir);
                File.WriteAllText(goldenPath, normalized);
                return;
            }

            if (!File.Exists(goldenPath))
            {
                Assert.Fail(
                    $"Log golden missing: {caseName}/{fileName}. Normalized output written under " +
                    $"log-golden-output/{caseName}/. Re-run with LOG_GOLDEN_CAPTURE=1 to capture, review, and commit.");
            }

            // Normalize line endings on both sides so CRLF/LF differences never cause spurious diffs.
            string expected = File.ReadAllText(goldenPath).Replace("\r\n", "\n");
            string actual = normalized.Replace("\r\n", "\n");
            Assert.AreEqual(expected, actual,
                $"Log golden mismatch for {caseName}/{fileName}. If intentional, recapture with LOG_GOLDEN_CAPTURE=1.");
        }
    }
}

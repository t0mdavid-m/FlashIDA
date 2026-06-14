using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Flash;
using Flash.IDA;
using Flash.Tests.Mocks;
using NUnit.Framework;
using Thermo.Interfaces.FusionAccess_V1.Control.Scans;

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
        public void Golden_Exploration_HCD() =>
            RunCase("exploration_hcd", "method_exploration.json", "ms1_standard.txt", "ms2_hcd_fragment.txt");

        // ETD exploration sweep — the activation-type-parallel half of the HCD/ETD split (mirrors
        // the C++ FLASHIda_exploration ETD sections). Skips cleanly when the data-agent fixture
        // method_exploration_etd.json is not present, exactly as the C++ ETD fixture section does;
        // never fabricates a config.
        [Test, Category("Tier2")]
        public void Golden_Exploration_ETD()
        {
            string cfg = Path.Combine(ConfigDir, "method_exploration_etd.json");
            if (!File.Exists(cfg))
            {
                Assert.Pass("method_exploration_etd.json absent — ETD exploration golden skipped cleanly (no fabrication).");
                return;
            }
            RunCase("exploration_etd", "method_exploration_etd.json", "ms1_standard.txt", "ms2_hcd_fragment.txt");
        }

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

        // MS3 cytC golden. Feeds the REAL MS3 fragment spectrum for each MS3 command the engine
        // issues, selecting the fixture PER COMMAND by the precursor ion decoded from the command's
        // scan_description (see DecodeIonFromScanDescription / BuildMs3IonMap) — the C# golden-locked
        // equivalent of the C++ results_ms3_real_fragment_data section. When no ms3_cytc_*_scan*.txt
        // fixture exists on disk the case skips cleanly (never fabricates MS3 data by reusing MS2
        // peaks); the match-dependent path is then covered only when fixtures are committed.

        /// <summary>
        /// Decode the trailing precursor ion key (ion_type + ion_index, e.g. "b44") from an MS3
        /// scan_description of the form {id(3)}R{mass}k@{charge}{ion_type}{ion_index}. MIRRORS the
        /// C++ decode contract EXACTLY: take the LAST '@', skip the run of fragment-charge digits
        /// after it, the next char must be a valid ion type in {a,b,c,x,y,z}, and the remaining
        /// chars must all be digits forming an index &gt;= 1. Returns null on the no-ion form
        /// ({id}R{mass}k@{charge}) or any malformed descriptor (decode tolerated to fail).
        /// </summary>
        private static string DecodeIonFromScanDescription(string d)
        {
            if (string.IsNullOrEmpty(d)) return null;
            int at = d.LastIndexOf('@');
            if (at < 0) return null;

            // Skip the run of fragment-charge digits immediately after '@'.
            int pos = at + 1;
            while (pos < d.Length && d[pos] >= '0' && d[pos] <= '9') pos++;

            // Next char is the ion type; must be one of {a,b,c,x,y,z}.
            if (pos >= d.Length) return null;
            char ionType = d[pos];
            if (ionType != 'a' && ionType != 'b' && ionType != 'c' &&
                ionType != 'x' && ionType != 'y' && ionType != 'z') return null;
            pos++;

            // Remaining chars are the ion index: all digits, at least one (index >= 1).
            if (pos >= d.Length) return null;
            int idxStart = pos;
            while (pos < d.Length && d[pos] >= '0' && d[pos] <= '9') pos++;
            if (pos != d.Length) return null;              // trailing non-digit -> malformed
            string idxStr = d.Substring(idxStart);
            if (idxStr.Length == 0) return null;
            // Index >= 1 (reject all-zero indices such as "b0" / "b00").
            if (!idxStr.TrimStart('0').Any()) return null;

            return ionType + idxStr;
        }

        /// <summary>
        /// Build an ion-key -&gt; first matching fixture path map by globbing
        /// ms3_cytc_*_scan*.txt under <paramref name="spectraDir"/> and parsing the ion key as the
        /// substring between "ms3_cytc_" and "_scan" (the FIXTURE NAMING CONTRACT). The first file
        /// seen for a given ion wins. Returns an empty map (caller skips cleanly) when the directory
        /// is absent or holds no matching fixtures.
        /// </summary>
        private static Dictionary<string, string> BuildMs3IonMap(string spectraDir)
        {
            var map = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(spectraDir) || !Directory.Exists(spectraDir)) return map;

            const string prefix = "ms3_cytc_";
            const string marker = "_scan";
            foreach (var path in Directory.GetFiles(spectraDir, "ms3_cytc_*_scan*.txt"))
            {
                string name = Path.GetFileName(path);
                if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                int scanAt = name.IndexOf(marker, prefix.Length, StringComparison.Ordinal);
                if (scanAt <= prefix.Length) continue;     // need a non-empty ion between prefix and "_scan"
                string ion = name.Substring(prefix.Length, scanAt - prefix.Length);
                if (ion.Length == 0) continue;
                if (!map.ContainsKey(ion)) map[ion] = path;   // first file for this ion wins
            }
            return map;
        }

        [Test, Category("Tier2")]
        public void Golden_MS3_CytC()
        {
            var ms3Map = BuildMs3IonMap(SpectraDir);
            if (ms3Map.Count == 0)
            {
                Assert.Pass("No ms3_cytc_*_scan*.txt fixtures present — MS3 cytC golden skipped cleanly (no MS2-as-MS3 fabrication).");
                return;
            }
            RunCase("ms3_cytc", "method_ms3_cytc_real.json", "ms1_cytc.txt", "ms2_cytc_fresh_scan57.txt",
                    feedMs3: true, ms3Map: ms3Map);
        }

        // ---- H-cs: engine-chained full-acquisition lineage (structural, non-golden) ----------

        /// <summary>
        /// INCLUSION-mode cytC full-acquisition lineage check (C# equivalent of the C++
        /// FLASHIda_LoggingFields parent_tracking_id_resolution section). Drives the engine via the
        /// interleaved PushScanAndDrainFull loop — every fed-back scan carries the ENGINE-EMITTED
        /// ScanCommand.ScanDescription, so parent/child edges use the engine's own tracking ids.
        ///
        /// HARD assertions (data-independent for the pinned inclusion cytC recipe):
        ///   * >= 1 MS2 command is emitted on the pinned cytC precursor;
        ///   * EVERY non-empty parent_tracking_id in scan_commands.tsv resolves to an emitted
        ///     tracking_id (no orphan parents);
        ///   * MS2 parents are MS1-level ids; MS3 parents are MS2-level ids.
        /// MS3 existence itself is recipe/data-dependent under the MS2-as-MS3 feed-back, so it is
        /// asserted WHEN present (mirroring the C++ section's `(void)checked_ms3;`), and hard-locked
        /// only when a real ms3_cytc_*_scan*.txt fixture is committed (Golden_MS3_CytC covers that).
        /// </summary>
        [Test, Category("Tier2")]
        public void FullAcquisition_InclusionCytC_ParentLineageResolves()
        {
            string caseDir = Path.Combine(OutputDir, "fullacq_inclusion_cytc");
            Directory.CreateDirectory(caseDir);
            string commandsPath = Path.Combine(caseDir, LogGoldenComparer.CommandsName);
            if (File.Exists(commandsPath)) File.Delete(commandsPath);

            // Presence of a real MS3 fixture is determined from the ion manifest; when present, the
            // harness is fed a real MS3 fragment spectrum (never the MS2-as-MS3 shortcut). The
            // interleaved PushScanAndDrainFull loop takes a single MS3 source, so pass the first
            // mapped fixture — sufficient for the lineage/emission assertions below.
            var ms3Map = BuildMs3IonMap(SpectraDir);
            string ms3FixtureName = ms3Map.Count > 0 ? ms3Map.Values.First() : null;

            using (var harness = MakeHarness("method_ms3_cytc_real.json", caseDir))
            {
                harness.PushScanAndDrainFull(
                    Path.Combine(SpectraDir, "ms1_cytc.txt"),
                    Path.Combine(SpectraDir, "ms2_cytc_fresh_scan57.txt"),
                    ms3FixtureName);   // null -> MS2-as-MS3 shortcut; MS3 then asserted only when present
            }

            Assert.That(File.Exists(commandsPath), Is.True, "engine must have written scan_commands.tsv");
            var rows = ParseTsv(commandsPath, out var header);
            int msLevelCol = Array.IndexOf(header, "ms_level");
            int trackingCol = Array.IndexOf(header, "tracking_id");
            int parentCol = Array.IndexOf(header, "parent_tracking_id");
            Assert.That(msLevelCol, Is.GreaterThanOrEqualTo(0), "ms_level column present");
            Assert.That(trackingCol, Is.EqualTo(0), "tracking_id is the first column");
            Assert.That(parentCol, Is.GreaterThanOrEqualTo(0), "parent_tracking_id column present");

            // Build id -> level map from emitted commands.
            var level = new Dictionary<string, int>();
            int ms2Count = 0, ms3Count = 0;
            foreach (var r in rows)
            {
                if (trackingCol >= r.Length || msLevelCol >= r.Length) continue;
                int lvl = ParseIntSafe(r[msLevelCol]);
                level[r[trackingCol]] = lvl;
                if (lvl == 2) ms2Count++;
                if (lvl == 3) ms3Count++;
            }

            Assert.That(ms2Count, Is.GreaterThanOrEqualTo(1),
                "inclusion mode must trigger >= 1 MS2 command on the pinned cytC precursor");

            // Every non-empty parent resolves to an emitted id, with MS2->MS1 / MS3->MS2 lineage.
            bool checkedMs2 = false, checkedMs3 = false;
            foreach (var r in rows)
            {
                if (msLevelCol >= r.Length || parentCol >= r.Length) continue;
                int lvl = ParseIntSafe(r[msLevelCol]);
                string parent = r[parentCol];
                if (string.IsNullOrEmpty(parent)) continue;

                Assert.That(level.ContainsKey(parent), Is.True,
                    $"parent_tracking_id '{parent}' (level {lvl} row) must resolve to an emitted command id");
                if (lvl == 2)
                {
                    checkedMs2 = true;
                    Assert.That(level[parent], Is.EqualTo(1), "MS2 parent must be an MS1 survey scan");
                }
                else if (lvl == 3)
                {
                    checkedMs3 = true;
                    Assert.That(level[parent], Is.EqualTo(2), "MS3 parent must be an MS2 scan");
                }
            }
            Assert.That(checkedMs2, Is.True, "at least one MS2 command must carry a resolvable MS1 parent");

            // When a real MS3 fixture is committed, MS3 must actually fire and chain; otherwise MS3
            // lineage is asserted only when present (the MS2-as-MS3 shortcut is data-dependent).
            if (ms3FixtureName != null)
            {
                Assert.That(ms3Count, Is.GreaterThanOrEqualTo(1),
                    "with a real ms3_cytc fixture, the inclusion MS3 recipe must emit >= 1 MS3 command");
                Assert.That(checkedMs3, Is.True, "emitted MS3 commands must carry a resolvable MS2 parent");
            }
        }

        // ---- E6: scan_description three-way equivalence (struct == built-scan Values == TSV) ----

        /// <summary>
        /// E6 locks that the raw scan_description is identical across all three surfaces it crosses:
        ///   (1) the ScanCommand struct returned by the C++ engine (ScanCommandRecord.FromScanCommand),
        ///   (2) the IFusionCustomScan the ScanFactory builds from it (round-tripped through the
        ///       Values dictionary the instrument receives — read via ScanCommandRecord.FromCustomScan,
        ///       which reads the actual "ScanDescription" Values key set by FillParameters), and
        ///   (3) scan_commands.tsv column 28 (the LAST column, the raw descriptor the engine logged).
        /// If any pair drifts, the value sent to the instrument no longer equals the value logged —
        /// exactly the failure E6 exists to prevent.
        /// </summary>
        [Test, Category("Tier2")]
        public void Equivalence_ScanDescription_StructVsBuiltScanVsTsv()
        {
            string caseDir = Path.Combine(OutputDir, "equiv_scan_description");
            Directory.CreateDirectory(caseDir);
            string commandsPath = Path.Combine(caseDir, LogGoldenComparer.CommandsName);
            if (File.Exists(commandsPath)) File.Delete(commandsPath);

            List<IFusionCustomScan> builtScans;
            List<ScanCommandRecord> structRecords;
            using (var harness = MakeHarness("method_dda_hcd.json", caseDir))
            {
                foreach (var s in MockMsScan.FromTsvAllScans(Path.Combine(SpectraDir, "ms1_standard.txt")))
                { harness.PushScan(s); s.Dispose(); }

                structRecords = new List<ScanCommandRecord>(harness.CapturedRecords);
                builtScans = new List<IFusionCustomScan>(harness.Factory.CreatedScans);
            }

            // Per built scan: the Values descriptor (key "ScanDescription", set by FillParameters)
            // equals the struct's descriptor (captured in the same drain order).
            Assert.That(builtScans.Count, Is.EqualTo(structRecords.Count),
                "every captured struct corresponds to exactly one built scan (same drain order)");
            int comparedWithDesc = 0;
            for (int i = 0; i < builtScans.Count; i++)
            {
                string structDesc = structRecords[i].ScanDescription ?? "";
                string valuesDesc = ScanCommandRecord.FromCustomScan(builtScans[i]).ScanDescription ?? "";
                Assert.That(valuesDesc, Is.EqualTo(structDesc),
                    $"built-scan Values[\"ScanDescription\"] must equal the ScanCommand struct descriptor (scan #{i})");

                // The ScanFactory only writes a non-empty descriptor into Values; when present the raw
                // dictionary key must be exactly "ScanDescription" (no space — the struct field has no
                // underscore, so FillParameters' '_'->' ' rename is a no-op).
                if (!string.IsNullOrEmpty(structDesc))
                {
                    Assert.That(builtScans[i].Values.ContainsKey("ScanDescription"), Is.True,
                        "built scan must expose the descriptor under the 'ScanDescription' Values key");
                    Assert.That(builtScans[i].Values["ScanDescription"], Is.EqualTo(structDesc),
                        "raw Values[\"ScanDescription\"] must equal the struct descriptor");
                    comparedWithDesc++;
                }
            }
            Assert.That(comparedWithDesc, Is.GreaterThan(0),
                "fail-closed: at least one built MSn scan must carry a non-empty descriptor to compare");

            // (3) scan_commands.tsv column 28 == the struct descriptor, matched by tracking id.
            Assert.That(File.Exists(commandsPath), Is.True, "engine must have written scan_commands.tsv");
            var rows = ParseTsv(commandsPath, out var header);
            int descCol = Array.IndexOf(header, "scan_description");
            Assert.That(descCol, Is.EqualTo(28), "scan_description is the LAST scan_commands column (index 28)");
            var tsvDescByPrefix = new Dictionary<string, string>();
            foreach (var r in rows)
                if (r.Length > descCol && r.Length > 0 && !tsvDescByPrefix.ContainsKey(r[0]))
                    tsvDescByPrefix[r[0]] = r[descCol];

            int comparedTsv = 0;
            foreach (var rec in structRecords)
            {
                string d = rec.ScanDescription ?? "";
                if (d.Length < 3) continue;
                string prefix = d.Substring(0, 3); // tracking-id prefix == tracking_id col 0
                if (!tsvDescByPrefix.TryGetValue(prefix, out var tsvDesc)) continue;
                Assert.That(tsvDesc, Is.EqualTo(d),
                    $"scan_commands.tsv col[28] must equal the struct descriptor for id '{prefix}'");
                comparedTsv++;
            }
            Assert.That(comparedTsv, Is.GreaterThan(0),
                "fail-closed: at least one descriptor must be cross-checked against scan_commands.tsv col[28]");
        }

        // ---- shared harness + TSV plumbing --------------------------------------------------

        private ContinuityTestHarness MakeHarness(string configFile, string caseDir)
        {
            return new ContinuityTestHarness(
                Path.Combine(ConfigDir, configFile), false, false,
                configure: mp =>
                {
                    mp.Config.Runtime.IdaLogPath = Path.Combine(caseDir, LogGoldenComparer.IdaLogName);
                    mp.Config.Runtime.ScanCommandsPath = Path.Combine(caseDir, LogGoldenComparer.CommandsName);
                    mp.Config.Runtime.ScanResultsPath = Path.Combine(caseDir, LogGoldenComparer.ResultsName);
                    mp.Config.Runtime.IdentificationLogPath = Path.Combine(caseDir, LogGoldenComparer.IdentificationName);
                });
        }

        private static List<string[]> ParseTsv(string path, out string[] header)
        {
            var lines = File.ReadAllLines(path);
            header = lines.Length > 0 ? lines[0].Split('\t') : new string[0];
            var rows = new List<string[]>();
            for (int i = 1; i < lines.Length; i++) rows.Add(lines[i].Split('\t'));
            return rows;
        }

        private static int ParseIntSafe(string s)
        {
            return int.TryParse(s, out int v) ? v : -1;
        }

        // ---- engine driver + golden compare -------------------------------------------------

        private void RunCase(string caseName, string configFile, string ms1File, string ms2File,
            bool feedMs3 = false, bool forceFaims = false, Dictionary<string, string> ms3Map = null)
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
                    feedMs3,
                    ms3Map);
            } // Dispose() closes the C++ engine and flushes/closes the log streams

            // Fail-closed: a case that produced no scan commands is broken, never a valid golden.
            string commandsPath = Path.Combine(caseDir, LogGoldenComparer.CommandsName);
            int cmdRows = File.Exists(commandsPath) ? Math.Max(0, File.ReadAllLines(commandsPath).Length - 1) : 0;
            Assert.That(cmdRows, Is.GreaterThan(0),
                $"Case '{caseName}' produced no scan commands — cannot golden an empty run.");

            // C1: ALWAYS write every .normalized file BEFORE any compare/capture, so a missing
            // golden for one stream (e.g. ida.log) can never abort before the other three are
            // written for the CI failure-diff artifact. Failures are collected and reported in a
            // single Assert.Fail listing every offending stream.
            var idMap = LogGoldenComparer.BuildIdMap(caseDir);
            var normalized = new Dictionary<string, string>();
            foreach (var fileName in LogGoldenComparer.FileNames)
            {
                string norm = LogGoldenComparer.Normalize(caseDir, fileName, idMap);
                normalized[fileName] = norm;
                WriteNormalized(caseName, fileName, norm);
            }

            if (Capture)
            {
                foreach (var fileName in LogGoldenComparer.FileNames)
                    CaptureGolden(caseName, fileName, normalized[fileName]);
                Assert.Pass($"Captured goldens for '{caseName}'. Review the normalized diff and commit.");
                return;
            }

            var failures = new List<string>();
            foreach (var fileName in LogGoldenComparer.FileNames)
            {
                string err = CompareOne(caseName, fileName, normalized[fileName]);
                if (err != null) failures.Add(err);
            }
            if (failures.Count > 0)
                Assert.Fail($"Log golden failures for '{caseName}':\n  " + string.Join("\n  ", failures));
        }

        private void DriveCycle(ContinuityTestHarness harness, string ms1Path, string ms2Path,
            bool feedMs3, Dictionary<string, string> ms3Map)
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

            // Feed each MS3 command back -> MS3 results + identification rows. Pick the REAL MS3
            // fragment spectrum PER COMMAND by the precursor ion decoded from the command's
            // scan_description: decode the ion key (e.g. "b44"), look it up in the manifest, and feed
            // that fixture. If the ion is absent from the map (or the descriptor decodes to no ion),
            // SKIP feeding that MS3 command — never fabricate MS3 by reusing the MS2 peaks.
            var map = ms3Map ?? new Dictionary<string, string>();
            var ms3Cmds = harness.Factory.CreatedScans
                .Select(s => ScanCommandRecord.FromCustomScan(s))
                .Where(r => r.ScanType == "MSn" && r.MsnLevel == 3)
                .ToList();
            foreach (var cmd in ms3Cmds)
            {
                string ion = DecodeIonFromScanDescription(cmd.ScanDescription);
                if (ion == null || !map.TryGetValue(ion, out var src))
                    continue;   // no real fixture for this ion -> skip (do NOT fabricate)
                var ms3 = MockMsScan.FromTsvAsMSn(src, 3, cmd.ScanDescription, cmd.PrecursorMz, cmd.ChargeState);
                harness.PushScan(ms3);
                ms3.Dispose();
            }
        }

        private void WriteNormalized(string caseName, string fileName, string normalized)
        {
            string outCaseDir = Path.Combine(OutputDir, caseName);
            Directory.CreateDirectory(outCaseDir);
            File.WriteAllText(Path.Combine(outCaseDir, fileName + ".normalized"), normalized);
        }

        private void CaptureGolden(string caseName, string fileName, string normalized)
        {
            string goldenCaseDir = Path.Combine(GoldenDir, caseName);
            Directory.CreateDirectory(goldenCaseDir);
            File.WriteAllText(Path.Combine(goldenCaseDir, fileName + ".golden.tsv"), normalized);
        }

        /// <summary>
        /// Compare one normalized stream against its committed golden. Returns null on match, or a
        /// human-readable failure string (missing golden or mismatch) so the caller can aggregate
        /// every stream's verdict into a single Assert.Fail.
        /// </summary>
        private string CompareOne(string caseName, string fileName, string normalized)
        {
            string goldenPath = Path.Combine(GoldenDir, caseName, fileName + ".golden.tsv");
            if (!File.Exists(goldenPath))
            {
                return $"{fileName}: golden missing. Normalized output written under " +
                       $"log-golden-output/{caseName}/. Re-run with LOG_GOLDEN_CAPTURE=1 to capture, review, and commit.";
            }

            // Normalize line endings on both sides so CRLF/LF differences never cause spurious diffs.
            string expected = File.ReadAllText(goldenPath).Replace("\r\n", "\n");
            string actual = normalized.Replace("\r\n", "\n");
            if (expected != actual)
                return $"{fileName}: mismatch vs golden. If intentional, recapture with LOG_GOLDEN_CAPTURE=1.";
            return null;
        }
    }
}

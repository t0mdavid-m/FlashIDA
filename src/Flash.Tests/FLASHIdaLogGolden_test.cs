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

        // F8-quant: feed the REAL TMT-reporter MS2 (ms2_quant_tmt.txt) instead of the inert ms2_hcd_fragment.txt,
        // so isDifferentiallyAbundant fires and a quant follow-up ('F') is actually generated. minFollowUps:1
        // fail-closes: the old reporter-less fixture produced 0 'F' commands -> the golden proved nothing.
        [Test, Category("Tier2")]
        public void Golden_Quant() =>
            RunCase("quant", "method_quant.json", "ms1_standard.txt", "ms2_quant_tmt.txt", minFollowUps: 1);

        [Test, Category("Tier2")]
        public void Golden_TagTargeting() =>
            RunCase("tag", "method_tag_targeting.json", "ms1_standard.txt", "ms2_hcd_fragment.txt");

        // F8: inclusion goldens use the DEDICATED cytC configs (method_inclusion_cytc[_strict].json ->
        // inclusion_cytc.txt @ the engine's real monoisotopic cytC mass 12351.3). The shared
        // method_inclusion.json/_strict (-> test_inclusion_list.txt, the 12358.31 AVERAGE-mass decoy) is left
        // UNTOUCHED for the ContinuityTests (CT13 smoke-no-match / CT39-40 E.coli-match). minMs2Commands:1
        // fail-closes: on the old wrong target, strict inclusion matched nothing -> 0 MS2 -> no DDA-masked golden.
        [Test, Category("Tier2")]
        public void Golden_Inclusion() =>
            RunCase("inclusion", "method_inclusion_cytc.json", "ms1_cytc.txt", "ms2_cytc_fresh_scan57.txt",
                    minMs2Commands: 1);

        [Test, Category("Tier2")]
        public void Golden_Inclusion_Strict() =>
            RunCase("inclusion_strict", "method_inclusion_cytc_strict.json", "ms1_cytc.txt", "ms2_cytc_fresh_scan57.txt",
                    minMs2Commands: 1);

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
        // [ION-DECODE C#<->C++ — see docs/kb/test-harness] byte-for-byte twin of C++ decodeTrailingIonKey
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

        // ---- ion-decode parity (drift guard) ------------------------------------------------

        /// <summary>
        /// SHARED ion-decode parity vectors — the SINGLE cross-language table that pins
        /// DecodeIonFromScanDescription (C#) and decodeTrailingIonKey (C++, FLASHIda_TestHelpers.h:224)
        /// to byte-for-byte equivalence. The C++ FLASHIda parity test feeds the EXACT same desc->expected
        /// rows; if either decoder drifts, the two suites disagree on at least one row here. Edge cases
        /// covered: '@' INSIDE the 3-char tracking id (rfind/LastIndexOf takes the LAST '@' as the charge
        /// delimiter), multi-digit charge+index, the no-ion form, the MS1 survey descriptor (no '@'),
        /// an invalid ion type, a zero ion index, and the empty string. expected==null means "no ion".
        /// See docs/kb/test-harness/README.md (Ion-decode parity).
        /// </summary>
        private static readonly (string desc, string expected)[] IonDecodeParityVectors =
        {
            ("!#@R4.450k@5y38", "y38"),   // '@' INSIDE the 3-char id (!#@); LAST '@' is the charge delim
            ("!!!R1.000k@2b10", "b10"),
            ("AAAR12.351k@3y5", "y5"),
            ("JJJR2.0k@12c144", "c144"),  // multi-digit charge + index
            ("!!!R5.0k@4",      null),    // no-ion form (nothing after the charge digits)
            ("!!\"S",           null),    // MS1 survey descriptor, no '@'
            ("!!!R5.0k@2d10",   null),    // invalid ion type 'd'
            ("!!!R5.0k@2y0",    null),    // index 0 (<1) invalid
            ("",                null),    // empty
        };

        // [ION-DECODE C#<->C++ — see docs/kb/test-harness] byte-for-byte twin of C++ decodeTrailingIonKey
        // Asserts the C# decoder reproduces the SHARED parity table exactly; the C++ FLASHIda parity test
        // feeds the identical vectors so any divergence between the two decoders is caught on both sides.
        [Test, Category("Tier2")]
        public void IonDecode_Parity_MatchesSharedVectorTable()
        {
            foreach (var (desc, expected) in IonDecodeParityVectors)
            {
                string actual = DecodeIonFromScanDescription(desc);
                Assert.AreEqual(expected, actual,
                    $"ion-decode parity drift for descriptor \"{desc}\": expected " +
                    $"{(expected == null ? "<none>" : expected)} but got {(actual == null ? "<none>" : actual)}");
            }
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

        // I4: exploration FOLLOW-UP golden. An MS2 CE-sweep whose winner — via a non-tolerance override
        // (analyzer) in method_exploration_followup.json — re-acquires as a PRODUCTION MS2, which then
        // cascades to MS3 (mirrors C++ ms2_exploration_production_winner_then_ms3). Inclusion-pinned cytC;
        // each MS3 command is fed its REAL per-ion fragment fixture. Skips cleanly when no ms3_cytc_*_scan*.txt
        // fixtures exist (same no-fabrication contract as Golden_MS3_CytC), since the cascade reaches MS3.
        [Test, Category("Tier2")]
        public void Golden_Exploration_FollowUp_CytC()
        {
            var ms3Map = BuildMs3IonMap(SpectraDir);
            if (ms3Map.Count == 0)
            {
                Assert.Pass("No ms3_cytc_*_scan*.txt fixtures present — exploration follow-up golden skipped cleanly (no MS2-as-MS3 fabrication).");
                return;
            }
            RunCase("exploration_followup", "method_exploration_followup.json", "ms1_cytc.txt", "ms2_cytc_fresh_scan57.txt",
                    feedMs3: true, ms3Map: ms3Map);
        }

        // I4: MS3-level EXPLORATION golden. A two-level MS2->MS3 CE-sweep cascade — the MS2-exploration
        // winner triggers an MS3-exploration sweep over the selected fragment ions (mirrors C++
        // ms2_then_ms3_exploration_acquires_ms3) — driven by the repurposed inclusion-pinned
        // method_exploration_ms3.json. Each MS3 variant is fed its REAL per-ion fragment fixture; skips
        // cleanly when no fixtures exist.
        //
        // Task E.2-4: the MS2-exploration CE sweep ({20,25,30,35,40} HCD, from Exploration.cpp ce_min/max/step)
        // is now fed the 5 ENERGY-RESOLVED cytC MS2 fixtures (ms2_cytc_ce{20..40}.txt) via a CE-keyed map, so
        // per-fragment best-MS2 selection lands at DIFFERENT CE per fragment and the resulting MS3 stage-0 CE
        // varies by fragment — exercising the per-fragment CE optimization end-to-end (vs. the prior single
        // fixture that made every fragment's best-MS2 identical). An explicit anti-collapse assertion below the
        // golden compare pins the variation.
        [Test, Category("Tier2")]
        public void Golden_Exploration_MS3_CytC()
        {
            var ms3Map = BuildMs3IonMap(SpectraDir);
            if (ms3Map.Count == 0)
            {
                Assert.Pass("No ms3_cytc_*_scan*.txt fixtures present — MS3 exploration golden skipped cleanly (no MS2-as-MS3 fabrication).");
                return;
            }

            // CE-keyed MS2 fixtures for the MS2-exploration sweep. NO FALLBACK: every CE the engine sweeps must
            // have a fixture (the harness throws otherwise). Built from the committed Exploration sweep grid.
            var ms2CeMap = BuildMs2CeMap(SpectraDir);
            Assert.That(ms2CeMap.Count, Is.EqualTo(5),
                "Task E.2-4 requires all 5 CE-resolved cytC MS2 fixtures (ms2_cytc_ce{20,25,30,35,40}.txt).");

            RunCase("exploration_ms3", "method_exploration_ms3.json", "ms1_cytc.txt", "ms2_cytc_fresh_scan57.txt",
                    feedMs3: true, ms3Map: ms3Map, ms2CeMap: ms2CeMap,
                    postDriveAssert: AssertMs3Stage0CeNotCollapsed);
        }

        /// <summary>
        /// Part G: MS3-level EXPLORATION-WITH-OVERRIDES golden — the production-MS3 trajectory fold. Identical
        /// to Golden_Exploration_MS3_CytC except the MS3 exploration block carries a NON-TOLERANCE override
        /// (<c>"overrides": { "analyzer": "Orbitrap" }</c>, mirroring how method_exploration_followup.json's MS2
        /// block does it). That override makes the WINNING fragment of the MS3 CE sweep re-acquire as a real
        /// PRODUCTION MS3 that returns on the regular (non-exploration) acquisition path, rather than the result
        /// being consumed inside the exploration variant loop.
        ///
        /// The Part G engine fix (FLASHIda.cpp: the production-MS3 context-cache lookup) lets that returning
        /// production MS3 hit its cached parent-MS2 context, re-feed into the ProteoformTracker, and FOLD a
        /// trajectory row into the pooled identification log (previously it cache-missed and dropped the
        /// fragments). The post-drive assertion (AssertProductionMs3Folded) pins exactly that: an MS2 baseline
        /// row plus >= 1 ion-tagged fold row in pooled_identification.tsv. Non-winning CE-sweep variants are
        /// transient and do NOT fold — only the production re-acquisition of the winner produces a fold row.
        ///
        /// The MS2-exploration CE sweep still runs, so the 5-fixture CE map (and its Is.EqualTo(5) guard) is
        /// still required; the BuildMs3IonMap/Assert.Pass clean-skip guard keeps the no-fabrication contract.
        /// </summary>
        [Test, Category("Tier2")]
        public void Golden_Exploration_MS3_FollowUp_CytC()
        {
            var ms3Map = BuildMs3IonMap(SpectraDir);
            if (ms3Map.Count == 0)
            {
                Assert.Pass("No ms3_cytc_*_scan*.txt fixtures present — MS3 exploration-followup golden skipped cleanly (no MS2-as-MS3 fabrication).");
                return;
            }

            // CE-keyed MS2 fixtures for the MS2-exploration sweep. NO FALLBACK: every CE the engine sweeps must
            // have a fixture (the harness throws otherwise). Built from the committed Exploration sweep grid.
            var ms2CeMap = BuildMs2CeMap(SpectraDir);
            Assert.That(ms2CeMap.Count, Is.EqualTo(5),
                "Task E.2-4 requires all 5 CE-resolved cytC MS2 fixtures (ms2_cytc_ce{20,25,30,35,40}.txt).");

            RunCase("exploration_ms3_followup", "method_exploration_ms3_followup.json", "ms1_cytc.txt", "ms2_cytc_fresh_scan57.txt",
                    feedMs3: true, ms3Map: ms3Map, ms2CeMap: ms2CeMap,
                    postDriveAssert: AssertProductionMs3Folded);
        }

        /// <summary>
        /// Build the CE -> energy-resolved cytC MS2 fixture map for the MS2-exploration sweep. Keys are the
        /// integer collision energies the engine sweeps (Exploration.cpp: ce_min 20, ce_max 40, ce_step 5 =>
        /// {20,25,30,35,40}); each maps to ms2_cytc_ce&lt;CE&gt;.txt. Only fixtures present on disk are added,
        /// so a missing one surfaces as a count mismatch in the caller (loud), never a silent skip.
        /// </summary>
        private static Dictionary<int, string> BuildMs2CeMap(string spectraDir)
        {
            var map = new Dictionary<int, string>();
            foreach (int ce in new[] { 20, 25, 30, 35, 40 })
            {
                string path = Path.Combine(spectraDir, $"ms2_cytc_ce{ce}.txt");
                if (File.Exists(path)) map[ce] = path;
            }
            return map;
        }

        /// <summary>
        /// Task E.2-4 anti-collapse assertion. After the exploration_ms3 drive, read the produced (raw,
        /// un-normalized) scan_commands.tsv and pull every MS3 (ms_level==3) command row's STAGE-0 (MS2)
        /// collision energy — the FIRST ';'-token of the per-stage 'collision_energy' column (IdaLogger writes
        /// it as 'stage0_CE;stage1_CE' for MS3) — alongside the MS3 FRAGMENT mass (the SECOND ';'-token of the
        /// per-stage 'mono_mass' column, i.e. mono_mass_s1, the fragment PeakGroup mono mass). Then assert:
        ///   (a) the set of distinct stage-0 CE values has size &gt; 1 — the per-fragment CE optimization did
        ///       NOT collapse to a single value (a regression deleting the ScanCommandQueue per-fragment
        ///       stage-0 CE override, or feeding one MS2 spectrum for all CE variants, fails here); AND
        ///   (b) energy-resolved trend: the LARGEST-mass MS3 fragment's stage-0 CE &lt;= the SMALLEST-mass
        ///       fragment's, with at least one STRICT difference across the fragments — large fragments are
        ///       strongest at low CE, small fragments at high CE. Tolerant/directional (robust to ties), not
        ///       over-precise.
        /// Reads scan_commands.tsv (not scan_results.tsv): the per-stage CE/mono_mass tokens for the COMMANDED
        /// MS3 are written there (IdaLogger::writeScanCommandRow), and that column is compared verbatim by the
        /// golden comparer (neither masked nor relabeled), so the raw cell value is the engine's decision value.
        /// </summary>
        private static void AssertMs3Stage0CeNotCollapsed(string commandsPath)
        {
            Assert.That(File.Exists(commandsPath), Is.True,
                "exploration_ms3: engine must have written scan_commands.tsv for the anti-collapse check");
            var rows = ParseTsv(commandsPath, out var header);
            int msLevelCol = Array.IndexOf(header, "ms_level");
            int ceCol = Array.IndexOf(header, "collision_energy");
            int monoCol = Array.IndexOf(header, "mono_mass");
            Assert.That(msLevelCol, Is.GreaterThanOrEqualTo(0), "ms_level column present");
            Assert.That(ceCol, Is.GreaterThanOrEqualTo(0), "collision_energy column present");
            Assert.That(monoCol, Is.GreaterThanOrEqualTo(0), "mono_mass column present");

            var fragments = new List<(double stage0Ce, double fragMass)>();
            foreach (var r in rows)
            {
                if (msLevelCol >= r.Length || ceCol >= r.Length || monoCol >= r.Length) continue;
                if (ParseIntSafe(r[msLevelCol]) != 3) continue;          // MS3 commands only

                // collision_energy = 'stage0;stage1' for MS3 — stage-0 (MS2) CE is the first ';'-token.
                string[] ceTok = r[ceCol].Split(';');
                // mono_mass = 'ms2_mono;fragment_mono' for MS3 — fragment mass is the second ';'-token.
                string[] monoTok = r[monoCol].Split(';');
                if (ceTok.Length < 1 || monoTok.Length < 2) continue;    // not a 2-stage MS3 row -> skip
                if (!double.TryParse(ceTok[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double stage0Ce)) continue;
                if (!double.TryParse(monoTok[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double fragMass)) continue;
                fragments.Add((stage0Ce, fragMass));
            }

            Assert.That(fragments.Count, Is.GreaterThanOrEqualTo(2),
                "exploration_ms3 must emit >= 2 MS3 commands to exercise per-fragment CE optimization " +
                $"(got {fragments.Count}).");

            // (a) NOT collapsed to a single stage-0 CE across fragments.
            var distinctCe = new HashSet<double>(fragments.Select(f => f.stage0Ce));
            Assert.That(distinctCe.Count, Is.GreaterThan(1),
                "exploration_ms3 anti-collapse: MS3 stage-0 (MS2) collision energy collapsed to a single value " +
                $"({string.Join(",", distinctCe)}) across {fragments.Count} fragments — per-fragment CE " +
                "optimization is not being exercised (a regression collapsing the per-fragment stage-0 CE " +
                "override, or feeding one MS2 spectrum for all CE variants, would do this).");

            // (b) Energy-resolved trend: largest-mass fragment's stage-0 CE <= smallest-mass fragment's, with
            //     >= 1 strict difference. Tolerant/directional (robust to ties), not over-precise.
            var largest = fragments.OrderByDescending(f => f.fragMass).First();
            var smallest = fragments.OrderBy(f => f.fragMass).First();
            Assert.That(largest.stage0Ce, Is.LessThanOrEqualTo(smallest.stage0Ce),
                $"exploration_ms3 energy-resolved trend: largest MS3 fragment (mass {largest.fragMass}, " +
                $"stage-0 CE {largest.stage0Ce}) must have stage-0 CE <= smallest fragment (mass " +
                $"{smallest.fragMass}, stage-0 CE {smallest.stage0Ce}).");
            Assert.That(distinctCe.Count, Is.GreaterThan(1),
                "exploration_ms3 energy-resolved trend: expected at least one strict stage-0 CE difference " +
                "between fragments (already guaranteed by (a), restated for the trend contract).");
        }

        /// <summary>
        /// Part G production-MS3 trajectory-fold assertion for the exploration_ms3_followup mode. After the
        /// drive, read the produced pooled_identification.tsv (a per-precursor trajectory: one MS2 baseline row
        /// + one fold row per MS3-analyzed fragment) and assert the production-MS3 re-acquisition actually
        /// folded a row. The MS3 exploration's NON-TOLERANCE override re-acquires the winning fragment as a
        /// PRODUCTION MS3 that returns on the regular path; the Part G context-cache fix lets that MS3 identify
        /// and fold a trajectory row (a pre-fix cache miss dropped the fragments and emitted no fold row).
        ///
        /// Asserts:
        ///   * the pooled file exists and has &gt;= 2 data rows (an MS2 baseline + &gt;= 1 fold);
        ///   * &gt;= 1 row's trigger (col 12) == "MS2" — the MS2 baseline;
        ///   * &gt;= 1 row's trigger matches the fragment-ion pattern ^[abcxyz]\d+$ (e.g. "y6") — an MS3 fold,
        ///     which only the production-MS3 re-acquisition produces (non-winning CE-sweep variants never fold);
        ///   * each fold row's trigger_scan_id (col 13) is a non-empty 3-char tracking id (the driving scan).
        /// Column indices mirror IdaLogger's pooled header (trigger=12, trigger_scan_id=13) and
        /// LogGoldenComparer.PooledScanIdsCol=8 / PooledTriggerScanIdCol=13. The delegate receives the
        /// scan_commands.tsv path (RunCase passes commandsPath); the pooled file is its sibling in the same
        /// case dir.
        /// </summary>
        private static void AssertProductionMs3Folded(string commandsPath)
        {
            string caseDir = Path.GetDirectoryName(commandsPath);
            string pooledPath = Path.Combine(caseDir, LogGoldenComparer.PooledName);
            Assert.That(File.Exists(pooledPath), Is.True,
                "exploration_ms3_followup: engine must have written pooled_identification.tsv for the " +
                "production-MS3 fold check");

            var rows = ParseTsv(pooledPath, out var _);   // skips the header; splits rows on '\t'
            Assert.That(rows.Count, Is.GreaterThanOrEqualTo(2),
                $"exploration_ms3_followup: pooled trajectory has {rows.Count} data row(s) (< 2) — expected an " +
                "MS2 baseline plus >= 1 production-MS3 fold (Part G cache regression?).");

            const int triggerCol = 12;          // pooled column 12 = trigger (IdaLogger pooled header)
            const int triggerScanIdCol = 13;    // pooled column 13 = trigger_scan_id (== LogGoldenComparer.PooledTriggerScanIdCol)

            bool sawMs2Baseline = false;
            int foldRows = 0;
            foreach (var r in rows)
            {
                if (triggerCol >= r.Length) continue;
                string trigger = r[triggerCol];
                if (trigger == "MS2") sawMs2Baseline = true;
                else if (System.Text.RegularExpressions.Regex.IsMatch(trigger, @"^[abcxyz]\d+$"))
                {
                    foldRows++;
                    // The driving (production) MS3 scan's tracking id must be a real 3-char id, not empty.
                    Assert.That(triggerScanIdCol < r.Length, Is.True,
                        "exploration_ms3_followup: MS3-fold pooled row is missing the trigger_scan_id column " +
                        $"(trigger '{trigger}').");
                    Assert.That(r[triggerScanIdCol].Length, Is.EqualTo(3),
                        "exploration_ms3_followup: MS3-fold pooled row's trigger_scan_id " +
                        $"('{r[triggerScanIdCol]}', trigger '{trigger}') is not a non-empty 3-char tracking id.");
                }
            }

            Assert.That(sawMs2Baseline, Is.True,
                "exploration_ms3_followup: no MS2 baseline pooled row (trigger == \"MS2\") — the MS2 " +
                "identification did not establish the trajectory the production MS3 folds onto.");
            Assert.That(foldRows, Is.GreaterThanOrEqualTo(1),
                "exploration_ms3_followup: no MS3-fold pooled row — the production MS3 re-acquisition did not " +
                "fold (Part G cache regression?). Expected >= 1 row whose trigger is a fragment ion " +
                "(^[abcxyz]\\d+$, e.g. \"y6\").");
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

            // Presence of a real MS3 fixture is determined from the ion manifest; when present, each MS3
            // command is fed its REAL per-ion fragment spectrum (decode ion -> manifest), never fabricated.
            var ms3Map = BuildMs3IonMap(SpectraDir);
            string ms3FixtureName = ms3Map.Count > 0 ? ms3Map.Values.First() : null;   // indicator: fixtures present
            Func<ScanCommand, string> ms3Sel = ms3Map.Count > 0
                ? c => { string ion = DecodeIonFromScanDescription(c.ScanDescription);
                         return ion != null && ms3Map.TryGetValue(ion, out var p) ? p : null; }
                : (Func<ScanCommand, string>)null;

            using (var harness = MakeHarness("method_ms3_cytc_real.json", caseDir))
            {
                harness.PushScanAndDrainFull(
                    Path.Combine(SpectraDir, "ms1_cytc.txt"),
                    Path.Combine(SpectraDir, "ms2_cytc_fresh_scan57.txt"),
                    ms3Sel);   // per-ion MS3 fixture; null when no manifest (MS3 asserted only when present)
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
                    mp.Config.Runtime.PooledIdentificationLogPath = Path.Combine(caseDir, LogGoldenComparer.PooledName);
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

        /// <summary>
        /// ADDITIONAL no-~~~ guard: assert that no MS1 (ms_level == 1) row in the case's RAW
        /// scan_results.tsv carries tracking_id == "~~~" (the empty-survey-description sentinel). A
        /// leaked "~~~" means the engine logged a placeholder instead of a real engine-emitted survey
        /// id, which would silently break the MS1-anchored parent/child joins. Reads columns by header
        /// name (tracking_id, ms_level) so it stays correct regardless of column-shift refactors, and
        /// is a no-op (passes) when no scan_results.tsv was produced — the cmdRows fail-closed check
        /// already covers the empty-run case.
        /// </summary>
        private static void AssertNoTildeTrackingIdInMs1Results(string caseName, string caseDir)
        {
            string resultsPath = Path.Combine(caseDir, LogGoldenComparer.ResultsName);
            if (!File.Exists(resultsPath)) return;

            var rows = ParseTsv(resultsPath, out var header);
            int trackingCol = Array.IndexOf(header, "tracking_id");
            int msLevelCol = Array.IndexOf(header, "ms_level");
            Assert.That(trackingCol, Is.GreaterThanOrEqualTo(0),
                $"Case '{caseName}': scan_results.tsv must have a tracking_id column");
            Assert.That(msLevelCol, Is.GreaterThanOrEqualTo(0),
                $"Case '{caseName}': scan_results.tsv must have an ms_level column");

            foreach (var r in rows)
            {
                if (msLevelCol >= r.Length || trackingCol >= r.Length) continue;
                if (ParseIntSafe(r[msLevelCol]) != 1) continue;   // MS1 rows only
                Assert.That(r[trackingCol], Is.Not.EqualTo("~~~"),
                    $"Case '{caseName}': MS1 scan_results row carries the '~~~' placeholder tracking id " +
                    "instead of a real engine-emitted survey id (would break MS1-anchored joins)");
            }
        }

        // ---- engine driver + golden compare -------------------------------------------------

        private void RunCase(string caseName, string configFile, string ms1File, string ms2File,
            bool feedMs3 = false, bool forceFaims = false, Dictionary<string, string> ms3Map = null,
            int minMs2Commands = 0, int minFollowUps = 0,
            Dictionary<int, string> ms2CeMap = null, Action<string> postDriveAssert = null)
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
                    mp.Config.Runtime.PooledIdentificationLogPath = Path.Combine(caseDir, LogGoldenComparer.PooledName);
                }))
            {
                // Interleaved engine-id-echo drive (one drain, mirrors C++ runFullAcquisition). MS1 rows
                // carry the engine's real survey ids; MS3 is fed per-command by the decoded ion (skip if
                // no real fixture — never fabricate). Replaces the old staged DriveCycle.
                Func<ScanCommand, string> ms3Sel = null;
                if (feedMs3)
                {
                    var map = ms3Map ?? new Dictionary<string, string>();
                    ms3Sel = c =>
                    {
                        string ion = DecodeIonFromScanDescription(c.ScanDescription);
                        return ion != null && map.TryGetValue(ion, out var p) ? p : null;
                    };
                }
                harness.PushScanAndDrainFull(
                    Path.Combine(SpectraDir, ms1File),
                    Path.Combine(SpectraDir, ms2File),
                    ms3Sel,
                    ms2CeMap: ms2CeMap);
            } // Dispose() closes the C++ engine and flushes/closes the log streams

            // Fail-closed: a case that produced no scan commands is broken, never a valid golden.
            string commandsPath = Path.Combine(caseDir, LogGoldenComparer.CommandsName);

            // Task E.2-4 post-drive structural assertion (e.g. the exploration_ms3 anti-collapse check on
            // per-fragment stage-0 CE). Runs AFTER the engine has flushed/closed its streams (Dispose above)
            // and AFTER the fail-closed cmdRows guard below would catch an empty run — but BEFORE the golden
            // compare, so a regression that collapses the CE-resolved behaviour fails here on its own merits
            // (independent of, and in addition to, the byte-exact golden TSV comparison).
            int cmdRows = File.Exists(commandsPath) ? Math.Max(0, File.ReadAllLines(commandsPath).Length - 1) : 0;
            Assert.That(cmdRows, Is.GreaterThan(0),
                $"Case '{caseName}' produced no scan commands — cannot golden an empty run.");

            postDriveAssert?.Invoke(commandsPath);

            // F8-inclusion fail-closed gate: a case that REQUIRES precursor selection (e.g. strict inclusion
            // on the corrected cytC target) must emit >= minMs2Commands MS2-level scan_commands. On the
            // pre-fix wrong target (12358.31 average mass), strict inclusion matched nothing and produced 0
            // MS2 -> this guard refuses to capture/compare a meaningless DDA-masked or empty golden.
            if (minMs2Commands > 0)
            {
                var cmdRowsParsed = ParseTsv(commandsPath, out var cmdHeader);
                int msLevelCol = Array.IndexOf(cmdHeader, "ms_level");
                Assert.That(msLevelCol, Is.GreaterThanOrEqualTo(0), "ms_level column present in scan_commands");
                int ms2Count = cmdRowsParsed.Count(r => msLevelCol < r.Length && ParseIntSafe(r[msLevelCol]) == 2);
                Assert.That(ms2Count, Is.GreaterThanOrEqualTo(minMs2Commands),
                    $"Case '{caseName}' produced {ms2Count} MS2 command(s) (< {minMs2Commands}) — the inclusion " +
                    "target did not drive selection (wrong mass -> silent DDA fall-through / strict 0-match).");
            }

            // F8-quant fail-closed gate: a quant case must emit >= minFollowUps quant follow-up ('F') commands.
            // On the pre-fix inert fixture (an MS2 with no TMT reporter ions), isDifferentiallyAbundant was
            // never true and 0 'F' follow-ups fired -> this guard refuses to capture/compare a golden that
            // proves nothing about the quant follow-up path. 'F' is the scan_description suffix (index 3).
            if (minFollowUps > 0)
            {
                var cmdRowsParsed = ParseTsv(commandsPath, out var cmdHeader);
                int descCol = Array.IndexOf(cmdHeader, "scan_description");
                Assert.That(descCol, Is.GreaterThanOrEqualTo(0), "scan_description column present in scan_commands");
                int followUps = cmdRowsParsed.Count(r => descCol < r.Length &&
                                                         r[descCol].Length >= 4 && r[descCol][3] == 'F');
                Assert.That(followUps, Is.GreaterThanOrEqualTo(minFollowUps),
                    $"Case '{caseName}' produced {followUps} quant follow-up ('F') command(s) (< {minFollowUps}) — " +
                    "the quant fixture has no TMT reporter ions, so no differential-abundance follow-up fired.");
            }

            // ADDITIONAL fail-closed guard (does NOT alter the golden comparison below): no captured
            // MS1 scan_results row may carry the "~~~" placeholder tracking id. "~~~" is the empty-MS1
            // survey-description sentinel; if it ever leaks into a real MS1 scan_results row the engine
            // logged a placeholder instead of a genuine engine-emitted survey id, breaking every
            // parent/child join that anchors on the MS1 tracking id. Checks the RAW (un-normalized)
            // scan_results.tsv so it is independent of the T<n> relabeling.
            AssertNoTildeTrackingIdInMs1Results(caseName, caseDir);

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

            // Numeric-aware compare (CRLF-agnostic): cross-build OpenMS rebuilds jitter the logged FP scores
            // (ida.log) and score columns (scan_commands/scan_results .tsv) ~1e-8..3e-5 run to run, so exact match
            // can never converge. Float tokens tolerance; ids/counts/levels/sentinels/strings stay exact.
            if (!GoldenNumericComparer.Equivalent(File.ReadAllText(goldenPath), normalized, out string diff))
                return $"{fileName}: mismatch vs golden ({diff}). If intentional, recapture with LOG_GOLDEN_CAPTURE=1.";
            return null;
        }
    }
}

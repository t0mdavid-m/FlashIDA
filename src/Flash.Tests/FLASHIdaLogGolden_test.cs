using System;
using System.Collections.Generic;
using System.Globalization;
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
            RunCase("exploration_hcd", "method_exploration.json", "ms1_ca.txt", "ms2_ca_hcd25_scan181.txt");

        // Exploration x co-isolation — the combination that had NO coverage anywhere. Every charge-mode
        // golden (multiplexed_ms2/_ms3, separate_charges) runs WITHOUT exploration, and all six
        // exploration goldens leave precursor_charges/fragment_charges at "single", so the CE-sweep path
        // was never once driven with a notch present. That gap hid two things:
        //
        //   1. An MS3 built from an MS2-exploration WINNER takes stage 0 from an exploration VARIANT
        //      command (Exploration.cpp:652/:687 -> buildMS3), a different object than the regular
        //      path's resolved parent_ctx. Nothing proved that one carries the notch block.
        //   2. Exploration deconvolved returning variants against the group's ANCHOR charge rather than
        //      the isolated maximum, silently dropping fragments of the higher co-isolated members
        //      before matching -- and MS3 targets are chosen from that matched list.
        //
        // An exact two-key twin of exploration_ms3 below (+ precursor_charges and fragment_charges =
        // "multiplexed"), so a mode-to-mode diff isolates co-isolation and nothing else. That baseline
        // rather than exploration_hcd, on evidence: the cytC survey co-isolates 10 charges wide in the
        // multiplexed_ms2 golden, whereas the CA survey is unproven — and a golden mode that turns out
        // to co-isolate nothing would be a byte-copy of its twin dressed up as coverage. It is also
        // inclusion-pinned and fed REAL per-ion MS3 responses, so the identification streams can move
        // rather than the command stream alone.
        //
        // Both charge modes on, because the point is that an MS3's two cascade stages can be loaded
        // with notches at once without contending (ADR-0019's disjoint blocks) under real data.
        [Test, Category("Tier2")]
        public void Golden_Exploration_Multiplexed()
        {
            var ms3Map = BuildMs3IonMap(SpectraDir);
            if (ms3Map.Count == 0)
            {
                Assert.Pass("No ms3_cytc_*_scan*.txt fixtures present — exploration co-isolation golden skipped cleanly (no MS2-as-MS3 fabrication).");
                return;
            }

            // Same sweep grid as exploration_ms3 (ce 20-40 step 5 + the CE-0 baseline), so the same six
            // CE-resolved fixtures cover it. NO FALLBACK: a missing CE throws rather than collapsing the
            // sweep onto one spectrum.
            var ms2CeMap = BuildMs2CeMap(SpectraDir);
            Assert.That(ms2CeMap.Count, Is.EqualTo(6),
                "Requires the CE-0 baseline fixture (ms2_cytc_ce0.txt) + all 5 CE-resolved cytC MS2 fixtures (ms2_cytc_ce{20,25,30,35,40}.txt).");

            RunCase("exploration_multiplexed", "method_exploration_multiplexed.json",
                    "ms1_cytc.txt", "ms2_cytc_fresh_scan57.txt",
                    feedMs3: true, ms3Map: ms3Map, ms2CeMap: ms2CeMap);
        }

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

        // AUTHORED CHARGE SETs (ADR-0028): the inclusion row names 10;13;16 rather than -1, so the
        // acquisition is restricted to three of cytC's thirteen resolved charge states. Two modes, because
        // the authored set and precursor_charges are ORTHOGONAL axes and single-axis goldens have hidden
        // real defects at their product before: "single" walks the set one charge per survey on per-charge
        // exclusion, "multiplexed" co-isolates it in one command. Diffing either against the plain
        // inclusion golden above isolates exactly what the charge column contributes -- the same
        // isolate-one-variable construction the other paired modes use.
        //
        // minMs2Commands: 1 fail-closes: if the authored set ever selected nothing, this fails rather than
        // capturing an empty golden that would then look like agreement forever after.
        [Test, Category("Tier2")]
        public void Golden_Inclusion_ChargeSet() =>
            RunCase("inclusion_charge_set", "method_inclusion_charge_set.json", "ms1_cytc.txt", "ms2_cytc_fresh_scan57.txt",
                    minMs2Commands: 1);

        [Test, Category("Tier2")]
        public void Golden_Inclusion_ChargeSet_Multiplexed() =>
            RunCase("inclusion_charge_set_multiplexed", "method_inclusion_charge_set_multiplexed.json", "ms1_cytc.txt", "ms2_cytc_fresh_scan57.txt",
                    minMs2Commands: 1);

        // Identification WITHOUT MS3 -- the configuration that had no representation until identification
        // stopped being gated on characterization.mode. method_identify_only.json is a byte-identical twin
        // of method_ms3_cytc_real.json except for ONE key (mode ambiguity -> off), so diffing this golden
        // against ms3_cytc's isolates exactly what MS3 dispatch contributes and nothing else -- the same
        // isolate-one-variable construction the multiplexed and faims_single_cv modes use.
        //
        // It is the only golden covering mode: off WITH a real protein_sequence. Every other mode: off
        // config carries an empty one, so before this the engine path "identify, dispatch nothing" was
        // exercised by a single C++ section (§ID1) and no exact values anywhere. Expect a populated
        // identification.tsv, real tag_count/fragment_count/tic_coverage on its MS2 scan_results rows,
        // and ZERO ms_level==3 rows in scan_commands.
        //
        // No feedMs3/ms3Map: with MS3 off the engine issues no MS3 commands, so there is nothing to answer.
        [Test, Category("Tier2")]
        public void Golden_Identify_Only() =>
            RunCase("identify_only", "method_identify_only.json", "ms1_cytc.txt", "ms2_cytc_fresh_scan57.txt",
                    minMs2Commands: 1);

        [Test, Category("Tier2")]
        public void Golden_Exclusion() =>
            RunCase("exclusion", "method_exclusion.json", "ms1_standard.txt", "ms2_hcd_fragment.txt");

        [Test, Category("Tier2")]
        public void Golden_Faims() =>
            RunCase("faims", "method_faims_3cv.json", "ms1_standard.txt", "ms2_hcd_fragment.txt",
                    forceFaims: true);

        /// <summary>
        /// FAIMS at a single fixed CV — a case that could not be acquired until ADR-0012.
        ///
        /// faims_.enabled was `cv_values.size() > 1`, so one CV meant "no FAIMS" and the run
        /// silently used the instrument method's own FAIMS state. method_faims_single_cv.json is
        /// byte-identical to method_faims_3cv.json except for the CV list, so diffing this golden
        /// against the faims one isolates CYCLING from ENABLEMENT: both carry a configured CV on
        /// every command, only the 3cv run transitions between them.
        ///
        /// New mode, so its five streams are captured fresh rather than recaptured. Until the
        /// golden is committed this fails "golden missing" by design — promote the .normalized
        /// files from the CI log-golden-capture artifact after reviewing the diff.
        /// </summary>
        [Test, Category("Tier2")]
        public void Golden_FaimsSingleCv() =>
            RunCase("faims_single_cv", "method_faims_single_cv.json", "ms1_standard.txt",
                    "ms2_hcd_fragment.txt", forceFaims: true);

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
        private static Dictionary<string, string> BuildMs3IonMap(string spectraDir, string prefix = "ms3_cytc_")
        {
            var map = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(spectraDir) || !Directory.Exists(spectraDir)) return map;

            const string marker = "_scan";
            foreach (var path in Directory.GetFiles(spectraDir, prefix + "*_scan*.txt"))
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
                    feedMs3: true, ms3Map: ms3Map, postDriveAssert: AssertMs3CytcLeafMatchesGroundTruth);
        }

        // Charge-state co-isolation, end to end (ADR-0016). Both cases are byte-identical twins of
        // method_ms3_cytc_real.json except for ONE key, so diffing their goldens against ms3_cytc's
        // isolates the notch axis and nothing else -- the same isolate-one-variable construction
        // method_faims_single_cv.json uses for FAIMS enablement (ADR-0012).
        //
        // They run on ms1_cytc.txt deliberately: cytC is present at many charge states, whereas
        // ms1_standard.txt yields at most one selectable precursor per scan and so would produce ZERO
        // notches, making the golden prove nothing. Until these two modes existed, the whole engine
        // path -- peakGroupNotchCandidates -> selectNotches -> writeNotchesForStage -> wire -> log --
        // had never executed with a real spectrum; only hand-built structs in unit tests.

        [Test, Category("Tier2")]
        public void Golden_Multiplexed_MS2()
        {
            var ms3Map = BuildMs3IonMap(SpectraDir);
            if (ms3Map.Count == 0)
            {
                Assert.Pass("No ms3_cytc_*_scan*.txt fixtures present — multiplexed MS2 golden skipped cleanly.");
                return;
            }
            // MS3 stays ON here on purpose: with the MS2 co-isolating a charge set, this is also the
            // only end-to-end cover for buildMS3 INHERITING stage-0's notches into the MS3 replay.
            RunCase("multiplexed_ms2", "method_multiplexed_ms2.json", "ms1_cytc.txt", "ms2_cytc_fresh_scan57.txt",
                    feedMs3: true, ms3Map: ms3Map);
        }

        [Test, Category("Tier2")]
        public void Golden_Multiplexed_MS3()
        {
            var ms3Map = BuildMs3IonMap(SpectraDir);
            if (ms3Map.Count == 0)
            {
                Assert.Pass("No ms3_cytc_*_scan*.txt fixtures present — multiplexed MS3 golden skipped cleanly.");
                return;
            }
            // The fragment stage co-isolates its charge states, which is the half with the shared slot
            // budget and buildMS3's write ordering.
            RunCase("multiplexed_ms3", "method_multiplexed_ms3.json", "ms1_cytc.txt", "ms2_cytc_fresh_scan57.txt",
                    feedMs3: true, ms3Map: ms3Map);
        }

        /// <summary>
        /// The OTHER non-default charge mode: one scan PER charge state at both levels, rather than one
        /// scan co-isolating them. This is the end-to-end cover for precursor_charges: "separate" and
        /// characterization.fragment_charges: "separate".
        /// </summary>
        /// <remarks>
        /// It exists because "separate" shipped silently inert at the MS2 level: the value parsed, it was
        /// documented as the mode the old fan-out had become, and PrecursorSelection never branched on it
        /// — the break added for the charge-keyed-exclusion fix was unconditional. Nothing failed, because
        /// nothing asserted the behaviour the value promises. CBE-08 now catches that in C++; this pins the
        /// whole acquisition shape.
        ///
        /// Both budgets are raised from the base config on purpose, because "separate" cannot express
        /// itself otherwise: selected_peak_groups_ is bounded by max_precursors, so at 1 one-scan-per-charge
        /// can only ever emit one scan; and under "separate" the MS3 budget counts (fragment, charge) PAIRS,
        /// so at 3 a single fragment seen at three charges consumes all of it and one cleavage site gets
        /// characterised. That budget arithmetic is the mode's real cost and the golden should show it.
        /// </remarks>
        [Test, Category("Tier2")]
        public void Golden_Separate_Charges()
        {
            var ms3Map = BuildMs3IonMap(SpectraDir);
            if (ms3Map.Count == 0)
            {
                Assert.Pass("No ms3_cytc_*_scan*.txt fixtures present — separate-charges golden skipped cleanly.");
                return;
            }
            RunCase("separate_charges", "method_cytc_separate_charges.json", "ms1_cytc.txt",
                    "ms2_cytc_fresh_scan57.txt", feedMs3: true, ms3Map: ms3Map);
        }

        // Coverage-objective MS3 targeting on the real cytC example. This is the CONTRAST PARTNER to
        // Golden_MS3_CytC, which runs method_ms3_cytc_real.json with the DEFAULT characterization
        // objective ("ambiguity"). Setting characterization.objective="coverage" makes
        // ProteoformTracker::planNextScans pick fragments that CONTAIN the largest uncovered backbone
        // gaps instead of containers of the ambiguous mod ranges, so the emitted MS3 targets differ from
        // the ambiguity golden (ms3_cytc). NB: selection_strategy.ms3.selection does NOT drive targeting
        // here — characterization.objective does (the SelectionMetric is inert for acquisition on this
        // path). minMs2Commands:1 fail-closes if the inclusion pin does not select the cytC precursor.
        [Test, Category("Tier2")]
        public void Golden_MS3_CoverageObjective_CytC()
        {
            var ms3Map = BuildMs3IonMap(SpectraDir);
            if (ms3Map.Count == 0)
            {
                Assert.Pass("No ms3_cytc_*_scan*.txt fixtures present — coverage MS3 cytC golden skipped cleanly (no MS2-as-MS3 fabrication).");
                return;
            }
            RunCase("ms3_coverage_cytc", "method_ms3_cytc_coverage.json", "ms1_cytc.txt", "ms2_cytc_fresh_scan57.txt",
                    feedMs3: true, ms3Map: ms3Map, minMs2Commands: 1);
        }

        // The THIRD objective on the same cytC data (ADR-0023). ambiguity and coverage both select out of
        // the mapped-fragment table; exhaustive selects out of the winner MS2 scan's RAW deconvolved
        // masses, so masses that matched no theoretical fragment become MS3 targets too — in this
        // spectrum roughly two of every three by intensity. Those carry ion_type 'u' / ion_index 0
        // in-engine, take buildMS3's no-ion branch, and therefore log an EMPTY ion_type with index 0
        // (ADR-0023 5b): an ms_level-3 command row with no ion is the discriminator for an unassigned
        // target, since every mapped target carries a real ion and a positive index.
        //
        // The fixture is method_ms3_cytc_real.json with exactly two keys changed (mode, min_target_mass),
        // so this golden diffed against ms3_cytc isolates the objective and nothing else.
        // minMs2Commands:1 fail-closes if the inclusion pin does not select the cytC precursor.
        //
        // WHAT THIS GOLDEN PINS, AND WHAT IT DOES NOT. It is a TARGETING reference, not a return-path
        // one. 49 of its 51 MS3 commands are never fed back: BuildMs3IonMap keys fixtures by ION, and
        // an unassigned target has no ion to key on (every committed ms3_cytc_* fixture is a b-ion).
        // So it pins the pool, the intensity ranking, the budget, the identification gate, and the
        // unassigned wire contract (empty ion_type, ion_index 0, no-ion descriptor) -- 34 such rows
        // against ms3_cytc's zero, which is what would catch `exhaustive` decaying into an alias of
        // `ambiguity`.
        //
        // It does NOT pin: the unassigned RETURN path (ADR-0023 decision 6's second half), the
        // matcher's known-ion-class guard (never reached through processScan), the dispatch memory
        // (planExhaustive_ runs once per Precursor here, so the set is written and never read), either
        // pool filter (min_target_mass is 0 and min_fragment_charge unauthored), the decision-11 metric
        // override (no exploration block), or the ETD capability gate (all 25 winners are HCD).
        // identification/pooled/ida.log are strict subsets of ms3_cytc's -- a red in those three is
        // almost certainly NOT this feature.
        //
        // Adding one real ms3_cytc_y56_scan*.txt would convert 15 skipped commands into real returns
        // and close most of that gap. It needs ACQUIRED data: fabricating an MS3 from an MS2 is what
        // the Assert.Pass guards above exist to prevent.
        //
        // KNOWN, DELIBERATE: the rank-1 target in 8 of 17 precursors is 6175.65 @ z=7, which is the
        // surviving intact precursor mis-deconvolved at half mass and half charge (2x6175.65 = 12351.30
        // vs 12351.4; m/z 883.814 either way). It carries the file's lowest SNR (0.329) and qscore
        // (0.796) and its highest intensity (8.76e7), and intensity is what ranks. For precursors 6 and
        // 20 it displaces b51, which ms3_cytc acquired. ADR-0023 decision 9 declined a precursor skip,
        // and one keyed on precursor IDENTITY would not catch a half-mass harmonic anyway.
        [Test, Category("Tier2")]
        public void Golden_MS3_Exhaustive_CytC()
        {
            var ms3Map = BuildMs3IonMap(SpectraDir);
            if (ms3Map.Count == 0)
            {
                Assert.Pass("No ms3_cytc_*_scan*.txt fixtures present — exhaustive MS3 cytC golden skipped cleanly (no MS2-as-MS3 fabrication).");
                return;
            }
            RunCase("ms3_exhaustive_cytc", "method_ms3_cytc_exhaustive.json", "ms1_cytc.txt", "ms2_cytc_fresh_scan57.txt",
                    feedMs3: true, ms3Map: ms3Map, minMs2Commands: 1);
        }

        // Active guard that the two characterization objectives select DIFFERENT MS3 targets on the same
        // cytC data — the "ambiguity vs coverage" contrast that motivates the coverage golden. Ambiguity
        // (method_ms3_cytc_real.json, default objective) picks containers of the ambiguous mod ranges;
        // Coverage (method_ms3_cytc_coverage.json) picks containers of the largest uncovered backbone
        // gaps. If a future regression collapses the two objectives to the same targets, this fails.
        [Test, Category("Tier2")]
        public void Ms3Objective_AmbiguityVsCoverage_SelectDifferentTargets()
        {
            var ms3Map = BuildMs3IonMap(SpectraDir);
            if (ms3Map.Count == 0)
            {
                Assert.Pass("No ms3_cytc_*_scan*.txt fixtures present — objective contrast skipped cleanly (no MS2-as-MS3 fabrication).");
                return;
            }
            var ambiguity = DriveMs3TargetKeys("contrast_ambiguity", "method_ms3_cytc_real.json",
                                               "ms1_cytc.txt", "ms2_cytc_fresh_scan57.txt", ms3Map);
            var coverage = DriveMs3TargetKeys("contrast_coverage", "method_ms3_cytc_coverage.json",
                                              "ms1_cytc.txt", "ms2_cytc_fresh_scan57.txt", ms3Map);
            Assert.That(ambiguity.Count, Is.GreaterThan(0), "ambiguity objective emitted no MS3 targets");
            Assert.That(coverage.Count, Is.GreaterThan(0), "coverage objective emitted no MS3 targets");
            Assert.That(coverage.SetEquals(ambiguity), Is.False,
                "coverage and ambiguity objectives selected the SAME MS3 target ions [" +
                string.Join(",", ambiguity.OrderBy(x => x)) + "] — the objective contrast is vacuous for this data");
        }

        // Inclusion-pinned MS3 cytC golden built on the SECOND cytC fixture set (ms1_cytc2.txt /
        // ms2_cytc2_scan434.txt + the ms3_cytc2_*_scan*.txt fragment manifest) and method_ms3_cytc_new.json
        // (inclusion pin via inclusion_cytc_12307.txt). Mirrors Golden_MS3_CytC but fail-closes on the MS3
        // cascade: minMs2Commands:1 requires the pin to select, and AssertInclusionMs3Produced requires an
        // MS3 row in BOTH scan_commands.tsv and identification.tsv (no vacuous MS3 golden). Skips cleanly
        // when no ms3_cytc2_*_scan*.txt fixtures are present (same no-fabrication contract).
        [Test, Category("Tier2")]
        public void Golden_Inclusion_MS3_CytC()
        {
            var ms3Map = BuildMs3IonMap(SpectraDir, "ms3_cytc2_");
            if (ms3Map.Count == 0)
            {
                Assert.Pass("No ms3_cytc2_*_scan*.txt fixtures present — inclusion MS3 cytC golden skipped cleanly.");
                return;
            }
            RunCase("inclusion_ms3_cytc", "method_ms3_cytc_new.json", "ms1_cytc2.txt", "ms2_cytc2_scan434.txt",
                    feedMs3: true, ms3Map: ms3Map, minMs2Commands: 1, postDriveAssert: AssertInclusionMs3Produced);
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
            Assert.That(ms2CeMap.Count, Is.EqualTo(6),
                "Requires the CE-0 baseline fixture (ms2_cytc_ce0.txt) + all 5 CE-resolved cytC MS2 fixtures (ms2_cytc_ce{20,25,30,35,40}.txt).");

            RunCase("exploration_ms3", "method_exploration_ms3.json", "ms1_cytc.txt", "ms2_cytc_fresh_scan57.txt",
                    feedMs3: true, ms3Map: ms3Map, ms2CeMap: ms2CeMap,
                    postDriveAssert: p => { AssertMs3Stage0CeNotCollapsed(p); AssertExplorationMs3LeafMatchesPooled(p); });
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
            Assert.That(ms2CeMap.Count, Is.EqualTo(6),
                "Requires the CE-0 baseline fixture (ms2_cytc_ce0.txt) + all 5 CE-resolved cytC MS2 fixtures (ms2_cytc_ce{20,25,30,35,40}.txt).");

            RunCase("exploration_ms3_followup", "method_exploration_ms3_followup.json", "ms1_cytc.txt", "ms2_cytc_fresh_scan57.txt",
                    feedMs3: true, ms3Map: ms3Map, ms2CeMap: ms2CeMap,
                    postDriveAssert: p => { AssertProductionMs3Folded(p); AssertExplorationMs3LeafMatchesPooled(p); });
        }

        /// <summary>
        /// Build the CE -> energy-resolved cytC MS2 fixture map for the MS2-exploration sweep. Keys are the
        /// integer collision energies the engine sweeps (Exploration.cpp: ce_min 20, ce_max 40, ce_step 5 =>
        /// {20,25,30,35,40}) PLUS the CE-0 baseline variant that baseline-on-all (#18) now prepends to every
        /// exploration group. CE 0 = the un-fragmented reference: ms2_cytc_ce0.txt is the isolated cytC
        /// precursor's z=15 isotope cluster (isolation window [824.04,825.90] of ms1_cytc.txt), NO fragments,
        /// so the baseline contributes 0 fragments to the FragmentCount joint calibration (no ripple). Each CE
        /// maps to ms2_cytc_ce&lt;CE&gt;.txt. Only fixtures present on disk are added, so a missing one surfaces
        /// as a count mismatch in the caller (loud), never a silent skip.
        /// </summary>
        private static Dictionary<int, string> BuildMs2CeMap(string spectraDir)
        {
            var map = new Dictionary<int, string>();
            foreach (int ce in new[] { 0, 20, 25, 30, 35, 40 })
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
        /// End-to-end invariant for the exploration two-winner render-seed fix (Exploration.cpp: the MS3 render
        /// seed proto_ctx / group.proteoform_ctx is taken from tracker->buildWinnerProteoformContext -- the
        /// finalized model the pooled log emits -- not the exploration-metric winner's frag_result). The per-scan
        /// MS3 leaf in identification.tsv must carry the SAME modification decomposition (set of PTM masses) as
        /// the authoritative pooled_identification model for the same precursor. Pre-fix, an MS2 CE sweep whose
        /// mass_count winner decomposed a FUSED single blind mod (e.g. -89.0302 + 615.2512 = +526.2213, Met1
        /// unmodified) leaked into the MS3 render seed, so the leaf reported ONE +526.2213 mod while pooled kept
        /// the two split mods -- the streams disagreed. This asserts, for precursor 1, that every MS3 leaf row's
        /// PTM-mass set equals the pooled model's PTM-mass set (count AND values within 0.05 Da). Both streams are
        /// byte-compared by the golden comparer, so the check cannot pass vacuously; it FAILS the moment a fused
        /// metric-winner mod reappears in the leaf (count mismatch) or any leaf mass drifts off the pooled set.
        /// </summary>
        private static void AssertExplorationMs3LeafMatchesPooled(string commandsPath)
        {
            string caseDir = Path.GetDirectoryName(commandsPath);
            string mode = Path.GetFileName(caseDir);   // exploration_ms3 or exploration_ms3_followup
            string idPath = Path.Combine(caseDir, LogGoldenComparer.IdentificationName);
            string pooledPath = Path.Combine(caseDir, LogGoldenComparer.PooledName);
            Assert.That(File.Exists(idPath), Is.True,
                $"{mode}: engine must have written identification.tsv for the leaf==pooled check");
            Assert.That(File.Exists(pooledPath), Is.True,
                $"{mode}: engine must have written pooled_identification.tsv for the leaf==pooled check");

            // --- pooled model PTM-mass set for precursor 1 (from the FINAL update row) ---
            var pooledRows = ParseTsv(pooledPath, out var pooledHeader);
            int pPidCol = Array.IndexOf(pooledHeader, "precursor_id");
            int pLocCol = Array.IndexOf(pooledHeader, "localized_mods");
            int pAmbCol = Array.IndexOf(pooledHeader, "ambiguous_mods");
            int pUpdCol = Array.IndexOf(pooledHeader, "update_index");
            Assert.That(pPidCol, Is.GreaterThanOrEqualTo(0), "pooled: precursor_id column present");
            Assert.That(pLocCol, Is.GreaterThanOrEqualTo(0), "pooled: localized_mods column present");
            Assert.That(pAmbCol, Is.GreaterThanOrEqualTo(0), "pooled: ambiguous_mods column present");
            Assert.That(pUpdCol, Is.GreaterThanOrEqualTo(0), "pooled: update_index column present");

            string[] finalPooled = null;
            int bestUpd = int.MinValue;
            foreach (var r in pooledRows)
            {
                if (pPidCol >= r.Length || ParseIntSafe(r[pPidCol]) != 1) continue;
                int upd = pUpdCol < r.Length ? ParseIntSafe(r[pUpdCol]) : 0;
                if (upd >= bestUpd) { bestUpd = upd; finalPooled = r; }
            }
            Assert.That(finalPooled, Is.Not.Null,
                $"{mode}: no pooled_identification row for precursor 1 — the leaf==pooled invariant cannot be checked.");

            var pooledMasses = ExtractModMasses(
                (pLocCol < finalPooled.Length ? finalPooled[pLocCol] : "") + ";" +
                (pAmbCol < finalPooled.Length ? finalPooled[pAmbCol] : ""));
            Assert.That(pooledMasses.Count, Is.GreaterThan(0),
                $"{mode}: pooled model for precursor 1 carries no PTM masses — unexpected for the cytC recipe " +
                "(fixture drift or a broken pooled writer).");

            // --- every MS3 leaf row for precursor 1 must carry the SAME PTM-mass set ---
            var idRows = ParseTsv(idPath, out var idHeader);
            int iLvlCol = Array.IndexOf(idHeader, "ms_level");
            int iPfCol = Array.IndexOf(idHeader, "proteoform");
            int iPidCol = Array.IndexOf(idHeader, "precursor_id");
            Assert.That(iLvlCol, Is.GreaterThanOrEqualTo(0), "identification: ms_level column present");
            Assert.That(iPfCol, Is.GreaterThanOrEqualTo(0), "identification: proteoform column present");
            Assert.That(iPidCol, Is.GreaterThanOrEqualTo(0), "identification: precursor_id column present");

            int leafRows = 0;
            foreach (var r in idRows)
            {
                if (iLvlCol >= r.Length || ParseIntSafe(r[iLvlCol]) != 3) continue;
                if (iPidCol >= r.Length || ParseIntSafe(r[iPidCol]) != 1) continue;
                if (iPfCol >= r.Length) continue;
                leafRows++;
                var leafMasses = ExtractModMasses(r[iPfCol]);
                Assert.That(leafMasses.Count, Is.EqualTo(pooledMasses.Count),
                    $"{mode}: MS3 leaf proteoform '{r[iPfCol]}' has {leafMasses.Count} PTM mass(es) but the " +
                    $"pooled model for precursor 1 has {pooledMasses.Count} — the two-winner render-seed split is back " +
                    "(a fused exploration-metric-winner mod leaked into the MS3 leaf/command).");
                foreach (double lm in leafMasses)
                    Assert.That(pooledMasses.Any(pm => Math.Abs(pm - lm) < 0.05), Is.True,
                        $"{mode}: MS3 leaf PTM mass {lm:F4} (proteoform '{r[iPfCol]}') is not in the pooled " +
                        $"model's mass set [{string.Join(", ", pooledMasses.Select(m => m.ToString("F4")))}] — leaf != pooled.");
            }
            Assert.That(leafRows, Is.GreaterThanOrEqualTo(1),
                $"{mode}: no MS3 (ms_level==3) leaf row for precursor 1 — the leaf==pooled invariant is " +
                "vacuous (fixture/engine drift removed the MS3 leaves).");
        }

        /// <summary>
        /// Extract the multiset of modification masses (the numeric value of every [+/-NN.NNNN] bracket) from a
        /// ProForma proteoform string or a pooled localized/ambiguous mods cell. Both formats encode each mod as a
        /// [mass] bracket, so this yields a comparable, format-agnostic mass set for the leaf==pooled invariant.
        /// </summary>
        private static List<double> ExtractModMasses(string s)
        {
            var masses = new List<double>();
            if (string.IsNullOrEmpty(s)) return masses;
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(s, @"\[([+-]?\d+(?:\.\d+)?)\]"))
            {
                if (double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double v))
                    masses.Add(v);
            }
            return masses;
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
        ///   * &gt;= 1 row's trigger (col 17) == "MS2" — the MS2 baseline;
        ///   * &gt;= 1 row's trigger matches the fragment-ion pattern ^[abcxyz]\d+$ (e.g. "y6") — an MS3 fold,
        ///     which only the production-MS3 re-acquisition produces (non-winning CE-sweep variants never fold);
        ///   * each fold row's trigger_scan_id is a non-empty 3-char tracking id (the driving scan).
        /// The trigger / trigger_scan_id columns are resolved BY HEADER NAME (order-agnostic to the pooled
        /// column layout, so an engine column reorder needs no change here). The delegate receives the
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

            var rows = ParseTsv(pooledPath, out var header);   // header captured for name-based column resolution
            Assert.That(rows.Count, Is.GreaterThanOrEqualTo(2),
                $"exploration_ms3_followup: pooled trajectory has {rows.Count} data row(s) (< 2) — expected an " +
                "MS2 baseline plus >= 1 production-MS3 fold (Part G cache regression?).");

            // Resolve by header NAME — order-agnostic to the pooled column layout (post-reorder-safe).
            int triggerCol = Array.IndexOf(header, "trigger");
            int triggerScanIdCol = Array.IndexOf(header, "trigger_scan_id");
            Assert.That(triggerCol, Is.GreaterThanOrEqualTo(0),
                "exploration_ms3_followup: pooled header is missing the 'trigger' column.");
            Assert.That(triggerScanIdCol, Is.GreaterThanOrEqualTo(0),
                "exploration_ms3_followup: pooled header is missing the 'trigger_scan_id' column.");

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

        /// <summary>
        /// Fail-closed vacuity guard for Golden_Inclusion_MS3_CytC. RunCase passes the scan_commands.tsv
        /// PATH to the post-drive delegate; identification.tsv is its sibling in the same case dir. Asserts
        /// BOTH streams actually carry an MS3 row, so the inclusion-pinned MS3 golden can never pass vacuously:
        ///   (a) &gt;= 1 ms_level==3 row in scan_commands.tsv — the inclusion pin / MS2 cascaded to MS3; else
        ///       the pin selected but no MS3 was ever commanded;
        ///   (b) &gt;= 1 ms_level==3 row in identification.tsv — the requested MS3 ion(s) were not ALL silently
        ///       skipped (an empty MS3 identification would otherwise golden as a valid-but-vacuous run).
        /// </summary>
        private static void AssertInclusionMs3Produced(string commandsPath)
        {
            string caseDir = Path.GetDirectoryName(commandsPath);
            string idPath = Path.Combine(caseDir, LogGoldenComparer.IdentificationName);

            // (a) scan_commands.tsv must contain >= 1 MS3 command row.
            Assert.That(File.Exists(commandsPath), Is.True,
                "inclusion_ms3_cytc: engine must have written scan_commands.tsv for the MS3-produced check");
            var cmdRows = ParseTsv(commandsPath, out var cmdHeader);
            int cmdMsLevelCol = Array.IndexOf(cmdHeader, "ms_level");
            Assert.That(cmdMsLevelCol, Is.GreaterThanOrEqualTo(0), "ms_level column present in scan_commands.tsv");
            int ms3Cmds = cmdRows.Count(r => cmdMsLevelCol < r.Length && ParseIntSafe(r[cmdMsLevelCol]) == 3);
            Assert.That(ms3Cmds, Is.GreaterThanOrEqualTo(1),
                "inclusion_ms3_cytc: no ms_level==3 row in scan_commands.tsv — the inclusion pin / MS2 did not " +
                "cascade to MS3.");

            // (b) identification.tsv must contain >= 1 MS3 identification row (else every requested MS3 ion was
            //     silently skipped -> vacuous golden).
            Assert.That(File.Exists(idPath), Is.True,
                "inclusion_ms3_cytc: engine must have written identification.tsv for the MS3-produced check");
            var idRows = ParseTsv(idPath, out var idHeader);
            int idMsLevelCol = Array.IndexOf(idHeader, "ms_level");
            Assert.That(idMsLevelCol, Is.GreaterThanOrEqualTo(0), "ms_level column present in identification.tsv");
            int ms3Ids = idRows.Count(r => idMsLevelCol < r.Length && ParseIntSafe(r[idMsLevelCol]) == 3);
            Assert.That(ms3Ids, Is.GreaterThanOrEqualTo(1),
                "inclusion_ms3_cytc: no ms_level==3 row in identification.tsv — every requested MS3 ion was " +
                "silently skipped (vacuous MS3 golden).");
        }

        /// <summary>
        /// End-to-end anchor for the MS3 leaf flip/mislocalization regression (Change L). Reads the RAW
        /// identification.tsv the ms3_cytc drive produced and asserts precursor-1's per-event leaf `proteoform`
        /// EQUALS the externally-grounded ground truth in test-data/reference/ms3_leaf_expected.tsv (the
        /// owner-validated proformas for tracking ids !!& / !!'). This is deliberately NOT a byte-golden
        /// re-capture: a golden compares the engine to its own last output (self-referential) and a future
        /// blanket recapture could silently re-bless a regression; this compares the real pipeline to a fixed
        /// external oracle, so the flip bug (-89 dragged off M1, ambiguity mislocalized) cannot come back
        /// unnoticed. Only the deterministic localization is checked (not the jittering score columns).
        /// </summary>
        private static void AssertMs3CytcLeafMatchesGroundTruth(string commandsPath)
        {
            string caseDir = Path.GetDirectoryName(commandsPath);
            string idPath = Path.Combine(caseDir, LogGoldenComparer.IdentificationName);
            Assert.That(File.Exists(idPath), Is.True,
                "ms3_cytc: engine must have written identification.tsv for the ground-truth leaf check");
            var idRows = ParseTsv(idPath, out var idHeader);
            int tidCol = Array.IndexOf(idHeader, "tracking_id");
            int pfCol = Array.IndexOf(idHeader, "proteoform");
            int lvlCol = Array.IndexOf(idHeader, "ms_level");
            Assert.That(tidCol, Is.GreaterThanOrEqualTo(0), "ms3_cytc: tracking_id column present in identification.tsv");
            Assert.That(pfCol, Is.GreaterThanOrEqualTo(0), "ms3_cytc: proteoform column present in identification.tsv");
            Assert.That(lvlCol, Is.GreaterThanOrEqualTo(0), "ms3_cytc: ms_level column present in identification.tsv");

            string fixturePath = Path.Combine(TestDataDir, "reference", "ms3_leaf_expected.tsv");
            Assert.That(File.Exists(fixturePath), Is.True,
                $"ms3_cytc: ground-truth fixture missing: {fixturePath}");
            var expected = ParseTsv(fixturePath, out var fxHeader);
            int fxTid = Array.IndexOf(fxHeader, "tracking_id");
            int fxPf = Array.IndexOf(fxHeader, "expected_proforma");
            Assert.That(fxTid, Is.GreaterThanOrEqualTo(0), "ms3_cytc: tracking_id column present in the fixture");
            Assert.That(fxPf, Is.GreaterThanOrEqualTo(0), "ms3_cytc: expected_proforma column present in the fixture");

            foreach (var exp in expected)
            {
                if (fxTid >= exp.Length || fxPf >= exp.Length) continue;
                string tid = exp[fxTid];
                string wantPf = exp[fxPf];
                var row = idRows.FirstOrDefault(r =>
                    lvlCol < r.Length && ParseIntSafe(r[lvlCol]) == 3 &&
                    tidCol < r.Length && r[tidCol] == tid);
                Assert.That(row, Is.Not.Null,
                    $"ms3_cytc: no MS3 identification row for tracking_id '{tid}' -- the ground-truth leaf anchor " +
                    "cannot be checked (fixture/engine scan sequence drift).");
                Assert.That(row[pfCol], Is.EqualTo(wantPf),
                    $"ms3_cytc: per-event leaf proteoform for '{tid}' must equal the external ground truth " +
                    $"(MS3 flip-localization regression). Expected '{wantPf}', got '{row[pfCol]}'.");
            }
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
            Assert.That(descCol, Is.EqualTo(28), "scan_description is at scan_commands column index 28 (no longer last after ms3_proteoform was appended)");
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

        // ---- stage-parameter three-way equivalence (log == what was actually sent) -----------

        /// <summary>
        /// Generalises the E6 scan_description equivalence above from ONE field to every stage-bound
        /// instrument parameter: for each dequeued command, the value in scan_commands.tsv must equal
        /// the value in the IFusionCustomScan the ScanFactory actually built for the instrument.
        ///
        /// This is the invariant whose absence let a defect live in 522 committed golden rows.
        /// hcd_energy is a log-only mirror of stages[0].collision_energy; buildMS3 refreshed the stage
        /// from the tracker's per-ion stage0_params but kept the mirror from the MS2 context, so
        /// exploration MS3 rows logged e.g. collision_energy "40;35" beside hcd_energy "30;35". The
        /// instrument had the right energy all along -- only the record was wrong, and nothing
        /// compared the two.
        ///
        /// Driving exploration_ms3 is MANDATORY, not illustrative: the stage-0 override happens
        /// nowhere else, so a run without it would assert nothing about that defect.
        /// </summary>
        [Test, Category("Tier2")]
        public void Equivalence_StageParameters_StructVsBuiltScanVsTsv()
        {
            // Single-stage baseline: proves the comparison itself is meaningful on the simple path.
            AssertLogMatchesSentRequest("equiv_stageparams_dda", "method_dda_hcd.json",
                "ms1_standard.txt", "ms2_hcd_fragment.txt", null, null, requireTwoStage: false);

            // Two-stage path with the per-ion stage-0 override.
            var ms3Map = BuildMs3IonMap(SpectraDir);
            Assert.That(ms3Map.Count, Is.GreaterThan(0),
                "the ms3_cytc_*_scan*.txt fixtures are required — without a real MS3 fixture this test " +
                "cannot exercise the two-stage path and would silently stop guarding the stage-0 mirror");
            var ms2CeMap = BuildMs2CeMap(SpectraDir);
            Assert.That(ms2CeMap.Count, Is.EqualTo(6),
                "requires the CE-0 baseline fixture + all 5 CE-resolved cytC MS2 fixtures");

            AssertLogMatchesSentRequest("equiv_stageparams_expl_ms3", "method_exploration_ms3.json",
                "ms1_cytc.txt", "ms2_cytc_fresh_scan57.txt", ms3Map, ms2CeMap, requireTwoStage: true);
        }

        /// <summary>
        /// Drive one config through the ground-truth interleaved harness, then assert that every
        /// stage-bound column of scan_commands.tsv agrees with the built instrument request, joined by
        /// tracking id (the first 3 chars of the descriptor, exactly as the E6 test joins them).
        /// </summary>
        private void AssertLogMatchesSentRequest(string caseName, string configFile, string ms1File,
            string ms2File, Dictionary<string, string> ms3Map, Dictionary<int, string> ms2CeMap,
            bool requireTwoStage)
        {
            string caseDir = Path.Combine(OutputDir, caseName);
            Directory.CreateDirectory(caseDir);
            foreach (var f in LogGoldenComparer.FileNames)
            {
                string p = Path.Combine(caseDir, f);
                if (File.Exists(p)) File.Delete(p);
            }

            List<IFusionCustomScan> builtScans;
            using (var harness = MakeHarness(configFile, caseDir))
            {
                Func<ScanCommand, string> ms3Sel = null;
                if (ms3Map != null)
                {
                    ms3Sel = c =>
                    {
                        string ion = DecodeIonFromScanDescription(c.ScanDescription);
                        return ion != null && ms3Map.TryGetValue(ion, out var p) ? p : null;
                    };
                }
                harness.PushScanAndDrainFull(
                    Path.Combine(SpectraDir, ms1File),
                    Path.Combine(SpectraDir, ms2File),
                    ms3Sel,
                    ms2CeMap: ms2CeMap);
                builtScans = new List<IFusionCustomScan>(harness.Factory.CreatedScans);
            } // Dispose() flushes and closes the log streams

            string commandsPath = Path.Combine(caseDir, LogGoldenComparer.CommandsName);
            Assert.That(File.Exists(commandsPath), Is.True,
                $"Case '{caseName}': engine must have written scan_commands.tsv");
            var rows = ParseTsv(commandsPath, out var header);

            // Join surface: the descriptor the ScanFactory copied into the request carries the same
            // 3-char engine-minted tracking id that heads every scan_commands row.
            var sentById = new Dictionary<string, IDictionary<string, string>>();
            foreach (var s in builtScans)
            {
                string desc;
                if (!s.Values.TryGetValue("ScanDescription", out desc) || desc.Length < 3) continue;
                sentById[desc.Substring(0, 3)] = s.Values;
            }

            int idCol = Array.IndexOf(header, "tracking_id");
            int lvlCol = Array.IndexOf(header, "ms_level");
            Assert.That(idCol, Is.GreaterThanOrEqualTo(0));
            Assert.That(lvlCol, Is.GreaterThanOrEqualTo(0));

            int comparedMsn = 0, comparedTwoStage = 0;
            foreach (var row in rows)
            {
                if (lvlCol >= row.Length || idCol >= row.Length) continue;
                int level = ParseIntSafe(row[lvlCol]);
                if (level < 2) continue;                      // MS1/AGC rows are stage-less placeholders

                IDictionary<string, string> sent;
                if (!sentById.TryGetValue(row[idCol], out sent)) continue;

                int n = level == 3 ? 2 : 1;
                string where = $"Case '{caseName}', command '{row[idCol]}' (MS{level})";

                // Structural: always present in the request, one element per stage.
                CompareStages(sent, "PrecursorMass", row, header, "precursor_mz", n, where, Cmp.Approx);
                CompareStages(sent, "IsolationWidth", row, header, "isolation_width", n, where, Cmp.Approx);
                CompareStages(sent, "ActivationType", row, header, "activation", n, where, Cmp.Text);
                CompareStages(sent, "CollisionEnergy", row, header, "collision_energy", n, where, Cmp.Rounded);
                CompareStages(sent, "ChargeStates", row, header, "charge", n, where, Cmp.ChargeClamped);

                // Optional: zero-filled positionally, key absent when NO stage uses the parameter.
                CompareStages(sent, "ReactionTime", row, header, "reaction_time", n, where, Cmp.Approx);
                CompareStages(sent, "ReagentMaxIT", row, header, "reagent_max_it", n, where, Cmp.Approx);
                CompareStages(sent, "ReagentAGCTarget", row, header, "reagent_agc_target", n, where, Cmp.Rounded);

                // The defect-1 assertion: the logged energy mirror must equal the energy actually sent,
                // for BOTH stages. This is what fails on 258 exploration_ms3 rows without the fix.
                var hcd = Cell(row, header, "hcd_energy").Split(';');
                Assert.That(hcd.Length, Is.EqualTo(n), $"{where}: hcd_energy must carry one token per stage");
                string sentCe;
                Assert.That(sent.TryGetValue("CollisionEnergy", out sentCe), Is.True,
                    $"{where}: the request must carry a collision energy");
                var ceTok = sentCe.Split(';');
                Assert.That(ceTok.Length, Is.EqualTo(n), $"{where}: CollisionEnergy must carry one element per stage");
                for (int i = 0; i < n; i++)
                {
                    Assert.That(ParseLog(hcd[i]), Is.EqualTo(ParseSent(ceTok[i])).Within(0.5),
                        $"{where}: logged hcd_energy stage {i} must equal the collision energy actually " +
                        "sent to the instrument for that stage");
                }

                comparedMsn++;
                if (n == 2) comparedTwoStage++;
            }

            Assert.That(comparedMsn, Is.GreaterThan(0),
                $"Case '{caseName}': fail-closed — no MSn command was cross-checked against its built request");
            if (requireTwoStage)
            {
                Assert.That(comparedTwoStage, Is.GreaterThan(0),
                    $"Case '{caseName}': fail-closed — no two-stage (MS3) command was cross-checked, so the " +
                    "stage-0 override path this case exists to cover was never exercised");
            }
        }

        private enum Cmp { Approx, Rounded, Text, ChargeClamped }

        /// <summary>Cell lookup by header name, so column order stays irrelevant.</summary>
        private static string Cell(string[] row, string[] header, string col)
        {
            int i = Array.IndexOf(header, col);
            return (i >= 0 && i < row.Length) ? row[i] : "";
        }

        // Both sides are invariant: the TSV is written by C++ (default "C" locale) and the Values
        // dictionary by ScanFactory.Fmt, which pins InvariantCulture. ParseSent used to use
        // CurrentCulture to match a ToString() that followed the machine locale — on a comma-decimal
        // locale that made a sent "824,97" parse back to 824.97 and the mismatch cancel out, hiding
        // the fact that the instrument was being handed a two-notch isolation request.
        private static double ParseLog(string s) { return double.Parse(s, CultureInfo.InvariantCulture); }
        private static double ParseSent(string s) { return double.Parse(s, CultureInfo.InvariantCulture); }

        /// <summary>
        /// Compare one ';'-joined per-stage column against the corresponding request key. An absent key
        /// is only legal for the optional parameters and only when every stage logged zero.
        /// </summary>
        private static void CompareStages(IDictionary<string, string> sent, string key, string[] row,
            string[] header, string col, int n, string where, Cmp mode)
        {
            var logTok = Cell(row, header, col).Split(';');
            Assert.That(logTok.Length, Is.EqualTo(n),
                $"{where}: logged '{col}' must carry one token per stage");

            string sentRaw;
            if (!sent.TryGetValue(key, out sentRaw))
            {
                // Key omitted => the instrument applies its method default, which is only correct when
                // no stage used the parameter at all.
                for (int i = 0; i < n; i++)
                {
                    Assert.That(ParseLog(logTok[i]), Is.EqualTo(0.0).Within(1e-9),
                        $"{where}: '{key}' is absent from the request, so every stage's logged '{col}' " +
                        "must be 0 — otherwise a value the engine chose was silently dropped");
                }
                return;
            }

            var sentTok = sentRaw.Split(';');
            Assert.That(sentTok.Length, Is.EqualTo(n),
                $"{where}: request key '{key}' must carry exactly one element per stage " +
                $"(got '{sentRaw}' for {n} stage(s)) — otherwise position no longer identifies the stage");

            for (int i = 0; i < n; i++)
            {
                // Each ';'-group may itself be a ','-joined list of co-isolation notches (ADR-0016):
                // the anchor first, then this stage's notches. Compare group-wise so the notch axis is
                // checked rather than tripped over -- a whole group handed to double.Parse throws on
                // the first ',' and would take the wire contract's only cross-check down with it.
                // Keys with no notch axis (activation, collision energy, the reagent trio) have
                // single-element groups, so this degenerates to the previous element-wise compare.
                var logWin = logTok[i].Split(',');
                var sentWin = sentTok[i].Split(',');
                Assert.That(sentWin.Length, Is.EqualTo(logWin.Length),
                    $"{where}: '{col}' stage {i} isolation-window count disagrees — logged " +
                    $"'{logTok[i]}' ({logWin.Length}) vs sent '{sentTok[i]}' ({sentWin.Length}). " +
                    "The instrument acts on the sent count, so the log would be describing a " +
                    "different acquisition than the one performed.");

                for (int w = 0; w < logWin.Length; w++)
                {
                    string ctx = $"{where}: '{col}' stage {i}" + (logWin.Length > 1 ? $" window {w}" : "")
                               + $" (logged '{logWin[w]}', sent '{sentWin[w]}')";
                    if (mode == Cmp.Text)
                    {
                        Assert.That(sentWin[w], Is.EqualTo(logWin[w]), ctx);
                        continue;
                    }
                    double logged = ParseLog(logWin[w]);
                    double actual = ParseSent(sentWin[w]);
                    if (mode == Cmp.ChargeClamped) logged = Math.Min(logged, 25);   // ScanFactory clamps
                    if (mode == Cmp.Rounded)
                    {
                        Assert.That(actual, Is.EqualTo(Math.Round(logged)).Within(0.5), ctx);
                    }
                    else
                    {
                        // The TSV carries C++ ostringstream's 6 significant digits, so compare relatively.
                        Assert.That(actual, Is.EqualTo(logged).Within(Math.Max(1e-9, Math.Abs(logged) * 1e-5)), ctx);
                    }
                }
            }
        }

        // ---- shared harness + TSV plumbing --------------------------------------------------

        private ContinuityTestHarness MakeHarness(string configFile, string caseDir)
        {
            return new ContinuityTestHarness(
                Path.Combine(ConfigDir, configFile), false, false,
                configure: mp =>
                {
                    // caseDir is absolute and already created; the engine joins the five fixed
                    // basenames itself, and they are LogGoldenComparer.FileNames verbatim. No run
                    // folder is composed here -- LogPathResolver runs only in the two Main methods
                    // -- so the golden paths stay deterministic.
                    mp.Config.Runtime.LogDir = caseDir;
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
                    // caseDir is absolute and already created; the engine joins the five fixed
                    // basenames itself, and they are LogGoldenComparer.FileNames verbatim. No run
                    // folder is composed here -- LogPathResolver runs only in the two Main methods
                    // -- so the golden paths stay deterministic.
                    mp.Config.Runtime.LogDir = caseDir;
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
                // Fail-closed BEFORE writing anything. A capture run performs no comparison at all
                // (it writes and Assert.Passes), and LogGoldenComparer.Normalize returns "" for a
                // file that does not exist -- so a run whose streams landed somewhere unexpected
                // would overwrite good goldens with empty ones and report success. Every committed
                // golden is non-empty, so "the engine wrote all five streams" is a safe precondition.
                var missing = new List<string>();
                foreach (var fileName in LogGoldenComparer.FileNames)
                {
                    string path = Path.Combine(caseDir, fileName);
                    if (!File.Exists(path)) missing.Add(fileName + " (absent)");
                    else if (new FileInfo(path).Length == 0) missing.Add(fileName + " (empty)");
                }
                Assert.IsEmpty(missing,
                    $"Refusing to capture goldens for '{caseName}': the engine did not write every "
                    + $"stream into {caseDir}. Capturing now would blank the committed goldens and "
                    + "pass. Offending streams:\n  " + string.Join("\n  ", missing));

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


        // Drive one config through the same ground-truth interleaved harness as RunCase (engine-id-echo,
        // MS3 fed per decoded ion via the tolerant map) and return the SET of emitted MS3-target ion keys
        // ("<ion_type><ion_index>") read from scan_commands.tsv (ms_level==3). No golden compare — used by
        // the objective-contrast test to prove ambiguity vs coverage select different MS3 targets. Mirrors
        // the RunCase drive block; the MS3 target commands are emitted during MS2 processing regardless of
        // whether each target's fixture is present, so an unfed target still appears in scan_commands.
        private HashSet<string> DriveMs3TargetKeys(string caseName, string configFile, string ms1File, string ms2File,
            Dictionary<string, string> ms3Map)
        {
            string caseDir = Path.Combine(OutputDir, caseName);
            Directory.CreateDirectory(caseDir);
            foreach (var f in LogGoldenComparer.FileNames)
            {
                string p = Path.Combine(caseDir, f);
                if (File.Exists(p)) File.Delete(p);
            }

            using (var harness = new ContinuityTestHarness(
                Path.Combine(ConfigDir, configFile), false, false,
                configure: mp =>
                {
                    // caseDir is absolute and already created; the engine joins the five fixed
                    // basenames itself, and they are LogGoldenComparer.FileNames verbatim. No run
                    // folder is composed here -- LogPathResolver runs only in the two Main methods
                    // -- so the golden paths stay deterministic.
                    mp.Config.Runtime.LogDir = caseDir;
                }))
            {
                var map = ms3Map ?? new Dictionary<string, string>();
                Func<ScanCommand, string> ms3Sel = c =>
                {
                    string ion = DecodeIonFromScanDescription(c.ScanDescription);
                    return ion != null && map.TryGetValue(ion, out var p) ? p : null;
                };
                harness.PushScanAndDrainFull(
                    Path.Combine(SpectraDir, ms1File),
                    Path.Combine(SpectraDir, ms2File),
                    ms3Sel,
                    ms2CeMap: null);
            }

            string commandsPath = Path.Combine(caseDir, LogGoldenComparer.CommandsName);
            var keys = new HashSet<string>();
            var rows = ParseTsv(commandsPath, out var header);
            int lvlCol = Array.IndexOf(header, "ms_level");
            int ionTypeCol = Array.IndexOf(header, "ion_type");
            int ionIdxCol = Array.IndexOf(header, "ion_index");
            foreach (var r in rows)
            {
                if (lvlCol < 0 || lvlCol >= r.Length || ParseIntSafe(r[lvlCol]) != 3) continue;
                string it = ionTypeCol >= 0 && ionTypeCol < r.Length ? r[ionTypeCol] : "";
                string ii = ionIdxCol >= 0 && ionIdxCol < r.Length ? r[ionIdxCol] : "";
                keys.Add(it + ii);
            }
            return keys;
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
            //
            // First apply the SAME compare-time canonicalization to BOTH sides: the engine dumps the MS1
            // deconvolution (scan_results) and the MS2/MS3 fragment matches (identification) and ida.log's
            // AllMass line in INTENSITY order, so near-tied entries swap position between non-deterministic
            // CI builds. GoldenListCanonicalizer mass-sorts those parallel list-tuples symmetrically so a pure
            // reorder matches while any value/count/int change still fails. It ALSO permutes both sides into
            // the golden's column order BY NAME, so the frozen (old-order) golden matches the NEW-order live
            // output after an engine column reorder. It does NOT touch the stored golden bytes (no recapture)
            // — only this in-memory comparison. The golden's header row (line 0) is the canonical reference
            // order; ida.log has no header, so the reference is ignored for it.
            string goldenText = File.ReadAllText(goldenPath);
            string[] refHeader = goldenText.Replace("\r\n", "\n").Split('\n')[0].Split('\t');
            string goldenC = GoldenListCanonicalizer.Canonicalize(fileName, goldenText, refHeader);
            string freshC = GoldenListCanonicalizer.Canonicalize(fileName, normalized, refHeader);
            if (!GoldenNumericComparer.Equivalent(goldenC, freshC, out string diff))
                return $"{fileName}: mismatch vs golden ({diff}). If intentional, recapture with LOG_GOLDEN_CAPTURE=1.";
            return null;
        }
    }
}

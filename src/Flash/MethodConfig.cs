using System;
using System.Collections.Generic;
using System.ComponentModel;
using Flash.IDA;

namespace Flash
{
    // ====================================================================
    // User-facing JSON config schema — annotated for serialization and docs
    // ====================================================================

    [JsonKey("global")]
    public class GlobalConfig
    {
        [JsonKey("method_name")]
        [Description("Name of the acquisition method")]
        public string MethodName { get; set; } = "";

        [JsonKey("method_description")]
        [Description("Description of the acquisition method")]
        public string MethodDescription { get; set; } = "";

        [JsonKey("duration")]
        [Description("Acquisition duration in minutes")]
        public double Duration { get; set; } = 90;
    }

    [JsonKey("deconvolution")]
    public class DeconvolutionConfig
    {
        [JsonKey("score_threshold")]
        [Description("Quality score threshold for accepting deconvolved peaks (0.0-1.0)")]
        public double ScoreThreshold { get; set; } = -1;

        [JsonKey("tqscore_threshold")]
        [Description("Target quality score threshold for precursor filtering")]
        public double TQScoreThreshold { get; set; } = 0.9;

        [JsonKey("min_charge")]
        [Description("Minimum precursor charge state")]
        public int MinCharge { get; set; } = 4;

        [JsonKey("max_charge")]
        [Description("Maximum precursor charge state")]
        public int MaxCharge { get; set; } = 50;

        [JsonKey("min_mass")]
        [Description("Minimum precursor mass in Da")]
        public double MinMass { get; set; } = 500;

        [JsonKey("max_mass")]
        [Description("Maximum precursor mass in Da")]
        public double MaxMass { get; set; } = 50000;

        [JsonKey("tol")]
        [Description("Mass tolerance array [down, up] in ppm")]
        public double[] Tolerances { get; set; } = new double[] { 10, 10 };
    }

    /// <summary>
    /// Decision section 1: WHICH intact species do we fragment?
    ///
    /// Holds the MS1 selection policy that used to live in selection_strategy.ms1. The keys are named
    /// for what they PRODUCE, not for the level they are read at: selection_strategy.ms1.max_targets
    /// was the MS2 count, which is the misreading that put max_targets:200 into four committed
    /// configs believing it was the MS3 budget. See the characterization section for that.
    ///
    /// C# property names are deliberately unchanged where only the wire key moved (ADR-0006 froze the
    /// POCO names so the test suite does not ripple), so e.g. the key is rt_window and the property
    /// stays RTWindow. TargetMode is the one exception: its TYPE changed int -> string enum.
    /// </summary>
    [JsonKey("precursor_selection")]
    public class PrecursorSelectionConfig
    {
        [JsonKey("rt_window")]
        [Description("Retention time window in seconds for precursor tracking")]
        public double RTWindow { get; set; } = 180;

        // Values taken from the CODE, not the doc comments: PrecursorSelection.cpp:138-141 logs
        // mode 2 as "in-depth" and mode 3 as "exclusion". MethodConfig.cs, Config.h:155 and
        // PrecursorSelection.cpp:564 all had 2 and 3 the wrong way round for the old int form.
        [JsonKey("targeting")]
        [Description("Targeting mode: none, inclusion, in_depth, or exclusion_masses")]
        public string Targeting { get; set; } = "none";

        [JsonKey("strict_inclusion")]
        [Description("If true, only acquire targets from the inclusion list")]
        public bool StrictInclusion { get; set; }

        [JsonKey("tie_threshold")]
        [Description("Tie-breaking threshold for precursor ranking")]
        public double TieThreshold { get; set; } = 0.1;

        [JsonKey("consider_all_charges")]
        [Description("Rank SPECIES by their best per-charge qscore rather than the representative charge's. Orders species only; which charge a species is isolated at is charge_selection (ADR-0059).")]
        public bool ConsiderAllChargeStates { get; set; }

        [JsonKey("charge_selection")]
        [Description("The one ranking of a selected species' charge states: \"snr\" (default), \"qscore\", \"intensity\" or \"purity\" (the species' share of the raw MS1 signal inside the window it would be isolated in). The first-ranked charge is the anchor; the first max_charge_states form the acquisition charge set. \"purity\" is refused under precursor_charges \"multiplexed\" (ADR-0059).")]
        public string ChargeSelection { get; set; } = "snr";

        [JsonKey("precursor_charges")]
        [Description("How many charge states of a selected precursor ONE MS2 acquires: \"single\" (the representative charge), \"separate\" (one MS2 per charge state, each its own precursor), or \"multiplexed\" (one MS2 co-isolating the whole SNR-positive set as notches). This is the only thing that decides acquisition geometry; exclusion is mass-keyed.")]
        public string PrecursorCharges { get; set; } = "single";

        [JsonKey("snr_threshold")]
        [Description("Minimum signal-to-noise for a charge state to be admitted: the anchor's own bar and the co-isolation notch gate. Hardcoded at 1.0 and unauthorable before ADR-0040.")]
        public double SnrThreshold { get; set; } = 1.0;

        [JsonKey("max_charge_states")]
        [Description("Upper bound on the acquisition charge set (anchor + co-isolated notches) of one species; 0 = no bound. Under \"separate\" this bounds the SCAN COUNT per species. Cannot exceed the instrument's 10 windows per fragmentation stage.")]
        public int MaxChargeStates { get; set; } = 0;

        [JsonKey("min_charge_states")]
        [Description("Minimum acquisition charge set size for a species to be selected; 1 = no floor. Inert under precursor_charges \"single\", where the set is always size 1. A species below the floor costs no max_precursors slot.")]
        public int MinChargeStates { get; set; } = 1;

        [JsonKey("log_target_rt_window")]
        [Description("Inclusion from files.target_logs: true (default), a reference mass is a target only within rt_window of a reference acquisition of it; false, at every retention time (the behaviour from 2025-07-07 to 2026-10-21, e.g. for a reference whose gradient does not match the run).")]
        public bool LogTargetRtWindow { get; set; } = true;

        [JsonKey("log_target_charges")]
        [Description("Inclusion from files.target_logs: \"any\" (default), a reference target is a mass matched at any charge; \"reference\", matched only at a charge the reference acquired it at, and each acquisition uses that charge up for the rest of the run (the behaviour from 2025-07-04 to 2026-10-21).")]
        public string LogTargetCharges { get; set; } = "any";

        // --- moved here from selection_strategy.ms1 ---

        [JsonKey("rank_by")]
        [Description("How MS1 precursors are ranked: qscore or intensity. 'none' disables MS1 selection entirely.")]
        public string RankBy { get; set; } = "qscore";

        [JsonKey("max_precursors")]
        [Description("Maximum MS2 scans triggered per survey (was selection_strategy.ms1.max_targets)")]
        public int MaxPrecursors { get; set; } = 10;

        [JsonKey("min_precursor_charge")]
        [Description("Minimum precursor charge state for selection (0 = no filter)")]
        public int MinPrecursorCharge { get; set; } = 0;

        [JsonKey("additional_scans")]
        [Description("Names from ms_settings.additional_ms2 to acquire for every selected precursor, in addition to ms_settings.ms2")]
        public List<string> AdditionalScans { get; set; } = new List<string>();

        // The MS2 CE/RT sweep. Lives here because precursor_selection is what dispatches MS2.
        [JsonKey("exploration")]
        public ExplorationBlockConfig Exploration { get; set; }
    }

    // ADR-0061 d.13. Every key is null when unauthored and crosses the bridge as null, so C++ takes
    // FLASHTnT's own default (FlashTnTSettings; skip_precursor_inference defaults to true). The C#
    // side holds no default and validates nothing here: C++ builds and validates fixed_mod (ADR-0067) and
    // refuses a gapped_tags other than off/fallback.
    [JsonKey("flashtnt")]
    public class FlashTnTConfig
    {
        [JsonKey("min_length")]
        [Description("Minimum sequence tag length (tag:min_length). Unset: FLASHTnT's default, 3.")]
        public int? MinLength { get; set; }

        [JsonKey("max_length")]
        [Description("Maximum sequence tag length (tag:max_length). Unset: FLASHTnT's default, 8.")]
        public int? MaxLength { get; set; }

        [JsonKey("allow_gap")]
        [Description("Allow mass gaps in sequence tags (tag:allow_gap). Unset: FLASHTnT's default, false.")]
        public bool? AllowGap { get; set; }

        [JsonKey("max_aa_in_gap")]
        [Description("Maximum amino acids in a tag mass gap (tag:max_aa_in_gap). Unset: FLASHTnT's default, 2.")]
        public int? MaxAaInGap { get; set; }

        [JsonKey("fixed_mod")]
        [Description("Fixed modifications (ADR-0067), by name or Unimod accession with the residue, e.g. 'Carbamidomethyl (C)', 'iodoTMT6plex (C)': every occurrence of the residue carries the mass, for the whole search. Residue-anywhere only, one per residue. C++ refuses an unknown name and, under quantification.scan ms3/both, any on C.")]
        public List<string> FixedMod { get; set; }

        [JsonKey("max_blind_mod_count")]
        [Description("Maximum blind modifications per proteoform (max_blind_mod_count). Unset: FLASHTnT's default, 1.")]
        public int? MaxBlindModCount { get; set; }

        [JsonKey("max_mod_mass")]
        [Description("Maximum absolute mass of a blind modification in Da (max_mod_mass). Unset: FLASHTnT's default, 500.")]
        public double? MaxModMass { get; set; }

        [JsonKey("var_mod")]
        [Description("Variable modifications by Unimod/PSI-MOD name with site (var_mod). Unset: FLASHTnT's default, Acetyl (Protein N-term).")]
        public List<string> VarMod { get; set; }

        [JsonKey("max_var_mod_count")]
        [Description("Maximum variable modifications per proteoform (max_var_mod_count). Unset: FLASHTnT's default, 3.")]
        public int? MaxVarModCount { get; set; }

        [JsonKey("max_protein_hits")]
        [Description("Candidate proteins kept per spectrum (max_protein_hits). Unset: FLASHTnT's default, 50.")]
        public int? MaxProteinHits { get; set; }

        [JsonKey("gapped_tags")]
        [Description("Candidate proteins from gapped tags: off or fallback (gapped_tags). Unset: FLASHTnT's default, fallback.")]
        public string GappedTags { get; set; }

        [JsonKey("terminal_ladders")]
        [Description("Candidate proteins from terminal ladders (terminal_ladders). Unset: FLASHTnT's default, true.")]
        public bool? TerminalLadders { get; set; }

        [JsonKey("precursor_ladders")]
        [Description("Candidate proteins from precursor-anchored ladders (precursor_ladders). Unset: FLASHTnT's default, true.")]
        public bool? PrecursorLadders { get; set; }

        [JsonKey("skip_precursor_inference")]
        [Description("Skip precursor-mass inference from complementary fragment pairs (ex:skip_precursor_inference). Unset: true (ADR-0061).")]
        public bool? SkipPrecursorInference { get; set; }
    }

    // ADR-0069. One MS3 yield gate: identification.characterization.yield_gate and quantification.ms3_yield_gate share
    // this class (the class-level [JsonKey] only marks it as a nested config class for MethodConfigSerializer; each
    // property's own [JsonKey] names the key). Defaults mirror YieldGateSettings.h exactly: threshold 1 expected bond and
    // the core-5 coefficients. Declared ABOVE both of its parents for the CS0579 reason MonitorMs1Config gives.
    [JsonKey("yield_gate")]
    public class YieldGateConfig
    {
        [JsonKey("threshold")]
        [Description("Admit an MS3 only if its predicted bond yield E = p x (L - 1), the bonds inside its target fragment it is expected to witness, is at least this many (> 0). 1 aims at the bonds MS3 witnesses; about 6 at a run's total coverage (an MS3 must beat the MS2 its time displaces).")]
        public double Threshold { get; set; } = 1.0;

        [JsonKey("intercept")]
        [Description("Model intercept. With the five coefficients below: p = 1 / (1 + exp(-(intercept + fragment_intensity x log10(I_frag) + precursor_intensity x log10(I_prec) + precursor_charge x z_prec + mass_ratio x M_frag/M_prec + fragment_iso_cos x iso_cos))), each input clipped to its training range first. Default: the core-5 fit (two Caco-2 histone runs, CID 35, 240k, 4 microscans, AGC 5e5, max_it 502 ms); refit at least this for another sample or MS3 setting.")]
        public double Intercept { get; set; } = -31.632174;

        [JsonKey("fragment_intensity")]
        [Description("Coefficient of log10 of the fragment's intensity at its charge in the source MS2 (clipped to [372, 5e6]).")]
        public double FragmentIntensity { get; set; } = 1.529105;

        [JsonKey("precursor_intensity")]
        [Description("Coefficient of log10 of the source MS2 precursor's intensity (clipped to [6.8e3, 8.4e7]).")]
        public double PrecursorIntensity { get; set; } = 0.474888;

        [JsonKey("precursor_charge")]
        [Description("Coefficient of the source MS2 precursor's charge (clipped to [4, 49]).")]
        public double PrecursorCharge { get; set; } = -0.105683;

        [JsonKey("mass_ratio")]
        [Description("Coefficient of the fragment's monoisotopic mass over the precursor's (clipped to [0.011, 1]).")]
        public double MassRatio { get; set; } = -2.766660;

        [JsonKey("fragment_iso_cos")]
        [Description("Coefficient of the fragment's isotope cosine (clipped to [0.85, 1.0005]).")]
        public double FragmentIsoCos { get; set; } = 21.747494;
    }

    [JsonKey("quantification")]
    public class QuantificationConfig
    {
        [JsonKey("enabled")]
        [Description("Enable isobaric labeling quantification")]
        public bool Active { get; set; }

        // ADR-0064. Where the reporters are read. Always emitted as a real string (identify's reason, below).
        [JsonKey("scan")]
        [Description("Where the reporters are read: ms2 (an HCD MS2 screens every precursor and its verdict buys the "
            + "identification scan), ms3 (no screen; an identification that closes out a cysteine proteoform buys an "
            + "MS3 on one iodoTMT-labelled fragment), or both")]
        public string Scan { get; set; } = "ms2";

        [JsonKey("ms3_targets")]
        [Description("How many quantification MS3s one identification buys (the most intense eligible labelled "
            + "fragments). Read under scan ms3 and both")]
        public int Ms3Targets { get; set; } = 1;

        // ADR-0069. Null = absent = no yield gate on the quantification MS3.
        [JsonKey("ms3_yield_gate")]
        [Description("The quantification MS3's own yield gate (ADR-0069): present, a labelled fragment is bought only at a charge whose predicted bond yield reaches its threshold, and a refused fragment hands its place on. Its own coefficients, because these MS3 use ms_settings.ms3_quant. Absent: no gate. Read under scan ms3 and both")]
        public YieldGateConfig Ms3YieldGate { get; set; }

        // Defaults equal the engine's (Config.cpp). Without an initializer a config that omits the key
        // would emit 0.0 across the bridge -- every ratio differential -- because ToCppJson emits
        // every key and the C++ .value() fallback never fires.
        [JsonKey("reporter_mz_tol")]
        [Description("Reporter ion m/z tolerance in Da")]
        public double ReporterMZTol { get; set; } = 0.002;

        [JsonKey("fold_change_threshold")]
        [Description("Fold-change threshold: the geometric fold change (or its inverse) must exceed it. "
            + "Read by test fold_change and welch_and_fold_change")]
        public double FoldChangeThreshold { get; set; } = 1.4;

        // ADR-0056. Which test turns the reporter channels into a verdict. Both numbers -- the
        // geometric fold change and the two-sided Welch p on the log2 channels -- are computed and
        // logged under every test; the test decides only which of them the verdict reads.
        [JsonKey("test")]
        [Description("Which test decides differential abundance: fold_change (the default), welch "
            + "(two-sided Welch t-test on log2 reporter intensities), or welch_and_fold_change (both). "
            + "Unknown values are rejected")]
        public string Test { get; set; } = "fold_change";

        [JsonKey("p_value_threshold")]
        [Description("Welch p at or below this is a hit. Read by test welch and welch_and_fold_change")]
        public double PValueThreshold { get; set; } = 0.05;

        [JsonKey("empty_condition")]
        [Description("What a condition empty in every channel, against one measured in every channel, "
            + "means: differential (the default -- present in one condition, absent in the other) or "
            + "incomplete (cannot be tested, buys nothing but under identify: all)")]
        public string EmptyCondition { get; set; } = "differential";

        [JsonKey("scan_normalize")]
        [Description("Scale each charge-state scan of a quantification group to its share of its own "
            + "reporter signal before combining, so a bright charge state does not dominate")]
        public bool ScanNormalize { get; set; } = true;

        [JsonKey("aggregate")]
        [Description("How a quantification group's usable charge-state scans are combined per channel "
            + "before the one test: median (the default), mean or sum")]
        public string Aggregate { get; set; } = "median";

        [JsonKey("min_usable_scans")]
        [Description("A quantification group decides only when at least this many of its scans measured "
            + "every channel. Above 1 requires precursor_charges separate")]
        public int MinUsableScans { get; set; } = 1;

        [JsonKey("labelling")]
        [Description("Isobaric labelling scheme: itraq4plex, itraq8plex, tmt6plex, tmt10plex, tmt11plex, tmt16plex or tmt18plex")]
        public string Labelling { get; set; } = "tmt6plex";

        // ADR-0039. The OBJECTIVE: which verdicts buy the identification scan (ms_settings.ms2).
        // Until this key existed the answer was hardcoded to "differential" in the engine.
        [JsonKey("identify")]
        [Description("Which quantification verdicts buy the identification scan ms_settings.ms2: "
            + "differential (only differentially abundant species -- the default, and the behaviour "
            + "before this key existed), quantified (anything cleanly measured, differential or not), "
            + "all (every screened precursor, verdict irrelevant), or none (quantify only, never "
            + "identify). Unknown values are rejected, not defaulted.")]
        public string Identify { get; set; } = "differential";

        // Named by CONDITION, never as up/down: fold_change = gmean(conditions[0]) / gmean(conditions[1]),
        // so "up" would mean enriched in whichever condition happens to be listed first, and would
        // invert silently if someone reordered the array.
        [JsonKey("enriched_in")]
        [Description("Condition a species must be ENRICHED IN for a differential verdict to buy the "
            + "identification scan -- one of the two names in `conditions`, or \"either\" for either "
            + "direction (the default). Restricts only identify: \"differential\"; under the other "
            + "objectives it is inert and the engine says so at load.")]
        public string EnrichedIn { get; set; } = "either";

        // ADR-0038. Exactly two, and the ORDER IS THE RATIO DIRECTION:
        // fold_change = gmean(conditions[0]) / gmean(conditions[1]). A list rather than a dictionary
        // because a dictionary has no order, and the numerator must not depend on how the author
        // happened to name their groups.
        [JsonKey("conditions")]
        [Description("The two experimental conditions, by reporter channel name. Order is the ratio direction")]
        public List<QuantConditionConfig> Conditions { get; set; } = new List<QuantConditionConfig>();

        [JsonKey("correction_matrix")]
        [Description("Isotope-impurity correction matrix, one entry per channel; omit to use the labelling scheme's stock matrix")]
        public List<string> CorrectionMatrix { get; set; } = new List<string>();

        // ADR-0057. Keyed by channel NAME, so a dictionary's lack of order is harmless here (unlike
        // `conditions`, whose order is the ratio direction). Null = off. Not CorrectionMatrix: that is
        // the label lot's isotope impurity, this is the sample loading of one experiment.
        [JsonKey("channel_normalization")]
        [Description("Channel normalisation factors from a previous run: each reporter channel is divided "
            + "by its factor before the quantification test reads it (e.g. the offline pipeline's channel "
            + "medians, {\"126\": 0.171, ...}). Only their ratios matter. Once authored it must name every "
            + "channel the conditions assign. Logged channels stay raw (ADR-0057)")]
        public Dictionary<string, double> ChannelNormalization { get; set; }
    }

    /// One of the two conditions the fold change is taken over. Channels are named
    /// (e.g. "126", "127N"); the engine resolves them to ordinals at load, so a typo is a load
    /// error rather than a silently wrong intensity.
    ///
    /// The class-level [JsonKey] is REQUIRED, not decoration: ConvertValue recognises a nested
    /// config class by `targetType.GetCustomAttribute<JsonKeyAttribute>() != null` on the CLASS.
    /// Without it a QuantConditionConfig falls through to Convert.ChangeType and throws
    /// "Object must implement IConvertible" for every config that authors conditions.
    [JsonKey("conditions")]
    public class QuantConditionConfig
    {
        [JsonKey("name")]
        [Description("Condition label, e.g. control or treated")]
        public string Name { get; set; } = "";

        [JsonKey("channels")]
        [Description("Reporter channel names belonging to this condition")]
        public List<string> Channels { get; set; } = new List<string>();
    }

    [JsonKey("faims")]
    public class FaimsConfig
    {
        [JsonKey("cv_values")]
        [Description("FAIMS compensation voltage values to cycle through")]
        public double[] CVValues { get; set; } = new double[] { -50 };

        [JsonKey("max_cv_skip")]
        [Description("Maximum number of FAIMS CV cycles to skip")]
        public int MaxCVSkip { get; set; }

        [JsonKey("cv_precursor_threshold")]
        [Description("Mass threshold for FAIMS CV precursor grouping")]
        public int MassThreshold { get; set; } = 15;
    }

    /// <summary>
    /// Instrument scan parameters, and nothing else. No key in here decides WHETHER a scan happens —
    /// that is what the two decision sections are for.
    ///
    /// All three levels are bare objects, so the common case (38 of 41 committed configs have exactly
    /// one MS2; all 41 have exactly one MS3) needs no naming at all. Extra MS2 configs go in
    /// additional_ms2 under a name, and are reached by reference from
    /// precursor_selection.additional_scans, which fires them per precursor after ms_settings.ms2.
    /// (Two follow-up references used to name entries here as well: quantification.follow_up_scan,
    /// retired by ADR-0038, and tagging.follow_up_scan, retired with tag targeting by ADR-0061.)
    ///
    /// A block that is defined but referenced by nobody never fires — Config resolves the references
    /// into the dispatch roster at parse time, so an unreferenced definition simply is not in it.
    /// There is no additional_ms3: every level-3 consumer reads scans[0] (Exploration.cpp:799), so a
    /// second MS3 config would be unreachable.
    /// </summary>
    [JsonKey("ms_settings")]
    public class MsSettingsConfig
    {
        [JsonKey("ms1")]
        public MS1Parameters MS1 { get; set; }

        // Every MSn slot below is NULLABLE, deliberately: MS2Parameters and MS3Parameters are structs, so
        // a plain field could not express "unauthored" -- it would always emit, zero-filled, and the
        // engine's "required but not defined" throws could never fire from a method.json. ms2 and ms3
        // were plain fields until 2026-11-09, so a method that selected precursors with no ms2, or set
        // identification.characterization with no ms3, loaded clean and acquired on activation "",
        // CE 0, resolution 0. The reflective serializer unwraps Nullable<T> at
        // MethodConfigSerializer.cs:241/293/377, so this costs nothing.

        // Required unless precursor_selection.rank_by is none (Config.cpp, "rank_by is not none but
        // ms_settings.ms2 is not defined"), and as the identification scan when quantification is
        // enabled (ADR-0039). Permitted and inert otherwise.
        [JsonKey("ms2")]
        public MS2Parameters? MS2 { get; set; }

        // ADR-0038. A bare slot beside ms1/ms2/ms3 rather than a name reference, the same shape
        // characterization already uses with ms3: the decision section holds no scan config, and
        // this supplies the parameters with no key pointing at it. Required when
        // quantification.enabled, permitted and inert otherwise.
        [JsonKey("ms2_quant")]
        public MS2Parameters? MS2Quant { get; set; }

        // ADR-0064. Stage 1 of the quantification MS3: required when quantification.scan is ms3 or both, inert
        // otherwise.
        [JsonKey("ms3_quant")]
        public MS3Parameters? MS3Quant { get; set; }

        // ADR-0068. The rescue scan: an identification scan that falls short of identification.threshold buys it,
        // once per detection. Its presence is the switch; the C++ side refuses it without the threshold.
        [JsonKey("ms2_rescue")]
        public MS2Parameters? MS2Rescue { get; set; }

        // Required when identification.characterization is set (Config.cpp, "ms_settings.ms3 is not
        // defined"); permitted and inert without it, so deleting the block switches MS3 off (ADR-0061).
        [JsonKey("ms3")]
        public MS3Parameters? MS3 { get; set; }

        // Absent in 38 of 41 committed configs. Keys are user-authored, so they cannot be
        // allowlisted the way a fixed schema is; they are validated as identifiers instead
        // (^[a-z][a-z0-9_]{0,31}$, reserved words rejected) and their VALUES are validated against
        // the normal 17-key scan allowlist.
        [JsonKey("additional_ms2")]
        public Dictionary<string, MS2Parameters> AdditionalMS2 { get; set; }
    }

    [JsonKey("cycle_time")]
    public class CycleTimeConfig
    {
        [JsonKey("enabled")]
        [Description("Enable cycle time limit")]
        public bool Enabled { get; set; }

        [JsonKey("value_ms")]
        [Description("Maximum cycle time in milliseconds")]
        public double ValueMs { get; set; } = 60000;
    }

    [JsonKey("scan_timeout")]
    public class ScanTimeoutConfig
    {
        [JsonKey("enabled")]
        [Description("Enable scan timeout")]
        public bool Enabled { get; set; }

        [JsonKey("value_ms")]
        [Description("Scan timeout in milliseconds")]
        public double ValueMs { get; set; } = 30000;
    }

    [JsonKey("scheduling")]
    public class SchedulingConfig
    {
        [JsonKey("cycle_time")]
        public CycleTimeConfig CycleTime { get; set; } = new CycleTimeConfig();

        [JsonKey("scan_timeout")]
        public ScanTimeoutConfig ScanTimeout { get; set; } = new ScanTimeoutConfig();

        // The only trigger that emits an AGC prescan (ADR-0031). The drained-queue path used to
        // fabricate one as filler and reset this timer with it, so the authored value never
        // governed the real cadence. Committed test configs deliberately pin this at 9999999 so
        // golden capture stays independent of wall clock.
        [JsonKey("agc_interval_seconds")]
        [Description("Interval between scheduled AGC prescans, in seconds")]
        public double AgcIntervalSeconds { get; set; } = 1;

        // How many commands the instrument may hold at once. HOST-SIDE ONLY -- the engine parses
        // this key and does nothing with it; the queue it describes belongs to the instrument, and
        // Flash.cs:ProcessSpectrum is the only thing that can size it.
        //
        // 1 is ADR-0032's behaviour exactly: submit, wait for the echo, submit again. It is also
        // why the instrument idles between EVERY pair of scans -- on the 2026-08-25 Eclipse run
        // that gap was ~186 ms and the instrument filled all of it with its own method's scans,
        // 53% of the whole duty cycle. 2 keeps one command queued behind the one executing, so
        // there is no gap to fill. See docs/adr/0033.
        //
        // Bounded, and DERIVED rather than accumulated, which is the distinction ADR-0032 turns
        // on: the pathology it removed was a monotonic ratchet (2 -> 4 -> 9 -> 11 with no path
        // down), not a steady depth. Raise this only with instrument evidence -- the symptom to
        // watch for is MS1 injection time railing at its max_it ceiling.
        [JsonKey("target_depth")]
        [Description("Commands the instrument may hold at once; 1 = submit-and-wait (ADR-0032)")]
        public int TargetDepth { get; set; } = 2;
    }

    // ADR-0049. The seven knobs of the Refine annotation method (shift scan at an FDR -> levels ->
    // one DP per total). Defaults mirror RefineSettings.h exactly. Read ONLY under
    // identification.method "refine": the C++ loader refuses this block under "flashtnt" rather
    // than carrying it unread, so the property is null by default and emitted only when authored.
    // Declared ABOVE CharacterizationConfig for the CS0579 reason MonitorMs1Config gives: an
    // attribute binds to the next type declaration.
    [JsonKey("refine")]
    public class RefineBlockConfig
    {
        [JsonKey("fdr")]
        [Description("False-discovery rate a shift level must pass against shuffled sequences, in (0, 1]")]
        public double Fdr { get; set; } = 0.2;

        [JsonKey("shuffles")]
        [Description("Number of shuffled sequences the FDR is estimated from (>= 1)")]
        public int Shuffles { get; set; } = 300;

        [JsonKey("grid_da")]
        [Description("Step of the trial-shift grid (Da, > 0)")]
        public double GridDa { get; set; } = 0.01;

        [JsonKey("max_shift_da")]
        [Description("The trial-shift grid runs from -max_shift_da to +max_shift_da (Da, > 0)")]
        public double MaxShiftDa { get; set; } = 700.0;

        [JsonKey("min_ions")]
        [Description("Smallest matched-ion cut tried for a level (>= 1)")]
        public int MinIons { get; set; } = 3;

        [JsonKey("max_levels")]
        [Description("Cap on the number of shift levels kept (>= 1)")]
        public int MaxLevels { get; set; } = 20;

        [JsonKey("jump_penalty")]
        [Description("Ions one jump between levels costs in a path's score: S = ions - jump_penalty x jumps (>= 1)")]
        public int JumpPenalty { get; set; } = 2;
    }

    /// <summary>
    /// identification.characterization: HOW do we pin that proteoform down with MS3?
    ///
    /// This block holds decisions only -- no scan plumbing. The MS3 scan's instrument parameters
    /// stay in ms_settings.ms3 like every other scan config.
    ///
    /// Its PRESENCE is THE MS3 switch and it is the only one (ADR-0061): there is no mode "off", and
    /// `mode` names the objective. It replaces three scattered gates (selection_strategy.ms2.selection,
    /// selection_strategy.ms3.selection, and the presence of ms_settings.ms3) plus the old `objective`
    /// key, whose absence silently meant "ambiguity".
    /// </summary>
    [JsonKey("characterization")]
    public class CharacterizationConfig
    {
        // Null = unauthored; C++ refuses a characterization block without a mode, because the block's
        // presence turns MS3 on and must name an objective.
        [JsonKey("mode")]
        [Description("The MS3 objective; required, because the block's presence turns MS3 on (delete the block for no MS3): ambiguity (resolve PTM site ambiguity), coverage (every fragment whose interior holds a bond MS2 never witnessed, strongest first, up to max_targets -- ADR-0052), fragments (every mapped fragment with a bond inside it, strongest first, up to max_targets -- ADR-0052), exhaustive (fragment every deconvolved mass of the winner MS2 scan, mapped or not -- ADR-0023), or discriminating (fragment the MS2 masses the two best Refine paths explain differently; requires identification.method \"refine\" and no characterization.exploration -- ADR-0049). Unknown values are rejected, not defaulted.")]
        public string Mode { get; set; }

        [JsonKey("max_targets")]
        [Description("Maximum MS3 scans planned per identified precursor. This is the MS3 budget (was selection_strategy.ms2.max_targets). With per_proteoform it also caps the distinct cleavages one proteoform gets per run.")]
        public int MaxTargets { get; set; } = 3;

        [JsonKey("min_fragment_charge")]
        [Description("Minimum charge of an MS2 FRAGMENT for it to become an MS3 target (0 = no filter). Distinct from precursor_selection.min_precursor_charge.")]
        public int MinFragmentCharge { get; set; } = 0;

        [JsonKey("fragment_charges")]
        [Description("How many charge states of a target FRAGMENT one MS3 acquires: \"single\" (the fragment's best-MS2 charge), \"separate\" (one MS3 per observed charge state -- the budget then counts (fragment, charge) pairs), or \"multiplexed\" (one MS3 co-isolating them, so the budget counts fragments and the same slots buy more cleavage sites). Replaces the bool ms3_all_charges, whose two states are the first two values.")]
        public string FragmentCharges { get; set; } = "single";

        // ADR-0066. Off (the default) keeps every committed method's behaviour.
        [JsonKey("per_proteoform")]
        [Description("One live MS3 plan per proteoform (ADR-0066): a newer identification of the same proteoform (protein, start/end, mass) replaces its MS3s not yet sent; a sent fragment is acquired again only at 2x its sent intensity; at most max_targets distinct cleavages per proteoform per run; only fragments whose isolation window SNR exceeds 1.")]
        public bool PerProteoform { get; set; } = false;

        // ADR-0069. Null = absent = no yield gate: every MS3 plan exactly as before.
        [JsonKey("yield_gate")]
        [Description("The MS3 yield gate (ADR-0069): present, every characterization MS3 target, in every mode and with or without per_proteoform, is admitted only at a charge whose predicted bond yield reaches the threshold; a refused target hands its place on. A target with no ion index (a raw mass that matches no ion) is admitted untested. Absent: no gate.")]
        public YieldGateConfig YieldGate { get; set; }

        // The MS3 CE/RT sweep. Must stay separate from precursor_selection.exploration: a single
        // config legitimately sweeps different ranges at MS2 and MS3 (method_exploration_ms3_followup
        // sweeps HCD 20-40 at MS2 and CID 15-35 at MS3).
        [JsonKey("exploration")]
        public ExplorationBlockConfig Exploration { get; set; }

        // Not inheritable from deconvolution.min_mass: that floor is not applied to MSn output (the
        // reference config sets min_mass 500 / min_charge 4 and its MS2 spectra still carry 248 Da and
        // charge-1 species). A genuinely new floor, not a duplicate of an existing one. Default 0 =
        // off, deliberately -- exhaustive does exactly what its name says until told otherwise.
        [JsonKey("min_target_mass")]
        [Description("Exhaustive mode only: deconvolved masses below this (Da) are not MS3 targets. 0 = off.")]
        public double MinTargetMass { get; set; } = 0.0;
    }

    // ADR-0061. The protein every MS2 is matched against. Declared ABOVE IdentificationConfig for the
    // CS0579 reason MonitorMs1Config gives: an attribute binds to the next type declaration.
    [JsonKey("reference")]
    public class ReferenceConfig
    {
        // Exactly one of Fasta and Sequence, non-empty. Both null when unauthored and emitted as
        // authored, so C++ (which owns the refusal) sees which one was written: both, neither or an
        // empty value is refused there.
        [JsonKey("fasta")]
        [Description("Path of the FASTA reference every MS2 is searched against, read as authored (a relative path resolves against the process working directory). Exactly one of fasta and sequence.")]
        public string Fasta { get; set; }

        [JsonKey("sequence")]
        [Description("One inline protein sequence every MS2 is matched against. Exactly one of fasta and sequence.")]
        public string Sequence { get; set; }
    }

    /// <summary>
    /// The `identification` section (ADR-0061): WHICH protein every MS2 is matched against, and HOW.
    ///
    /// Its PRESENCE is the matching switch -- MethodConfig.Identification is null by default and an
    /// absent section matches nothing -- and the presence of the nested `characterization` block is
    /// the MS3 switch.
    /// </summary>
    [JsonKey("identification")]
    public class IdentificationConfig
    {
        [JsonKey("reference")]
        public ReferenceConfig Reference { get; set; } = new ReferenceConfig();

        // ADR-0049. Which annotation method reads the MS2 fragments. Emitted verbatim (not
        // lower-cased): the C++ loader is case-sensitive and hard-rejects anything but these two,
        // so a typo'd "Refine" fails loudly instead of silently running FLASHTnT.
        [JsonKey("method")]
        [Description("MS2 annotation method: flashtnt (tag-based search, the default) or refine (no-search shift scan + per-total DP -- ADR-0049). Case-sensitive.")]
        public string Method { get; set; } = "flashtnt";

        [JsonKey("flashtnt")]
        public FlashTnTConfig FlashTnT { get; set; } = new FlashTnTConfig();

        // Null = unauthored; C++ then uses RefineSettings' defaults. See RefineBlockConfig.
        [JsonKey("refine")]
        public RefineBlockConfig Refine { get; set; }

        // ADR-0062. The search pool's size. Null = unauthored: C++ uses the core count minus two (at
        // least 1). C++ refuses anything but an integer >= 1.
        [JsonKey("search_threads")]
        [Description("Search workers that match MS2s beside the analysis thread (ADR-0062). Unset: the core count minus two, at least 1.")]
        public int? SearchThreads { get; set; }

        // ADR-0061 d.6/d.7. The Identification score (FLASHTnT's scale, whatever the method) at which an MS2 closes
        // its species out of acquisition. Null = absent = identify and log, never close out. C++ refuses anything but
        // a finite number > 0.
        [JsonKey("threshold")]
        [Description("Identification score at which an identified species is closed out of acquisition (ADR-0061). Unset: identify and log, never close out.")]
        public double? Threshold { get; set; }

        // Null = absent = no MS3.
        [JsonKey("characterization")]
        public CharacterizationConfig Characterization { get; set; }
    }

    [JsonKey("files")]
    public class FilesConfig
    {
        [JsonKey("target_logs")]
        [Description("Log files containing target or excluded masses")]
        public List<string> TargetLogs { get; set; } = new List<string>();

        [JsonKey("inclusion_list")]
        [Description("Inclusion list file path")]
        public string InclusionList { get; set; } = "";
    }

    // ADR-0042. A periodic MS1 acquired WHILE THIS BLOCK'S LEVEL IS SWEEPING, so the operator can
    // watch the source -- spray stability, contamination, whether the sample is still eluting.
    // It is deconvolved and logged and it decides nothing: it selects no precursor, excludes no
    // mass, and no later acquisition decision reads anything it wrote.
    //
    // Nested under EACH exploration block rather than under `scheduling`, because the two blocks are
    // authored separately and their sweeps differ in length by an order of magnitude. An MS3 sweep
    // is max_targets x (points + 2) scans and is the one that leaves the operator blind; an MS2
    // sweep is often over in seconds. Enabling it on characterization.exploration alone is the
    // intended production shape.
    //
    // DECLARED ABOVE the [JsonKey("exploration")] below, not between it and its class: an attribute
    // binds to the next type declaration, so putting this class there gave it BOTH keys and the
    // build failed with CS0579 "duplicate attribute JsonKey".
    [JsonKey("monitor_ms1")]
    public class MonitorMs1Config
    {
        [JsonKey("enabled")]
        [Description("Acquire a periodic neutral MS1 while this level is sweeping, for source monitoring")]
        public bool Enabled { get; set; }

        // Wall clock, and the engine THROWS on <= 0 when enabled: a monitor scan would then be due on
        // every drain, minting a priority-0 MS1 ahead of the sweep indefinitely so the sweep could
        // never progress. At production ms_settings.ms1 (240k, 4 microscans) each reading costs ~2 s,
        // so this is a switch-it-on-while-investigating feature, not a leave-it-on one.
        [JsonKey("interval_ms")]
        [Description("Milliseconds between monitor MS1 scans while this level is sweeping")]
        public double IntervalMs { get; set; } = 30000;
    }

    // ADR-0047. The Kinetic Model's one authored input: k2, the overfragmentation rate per NCE, BY
    // ACTIVATION. There is no default -- k2 differs by an order of magnitude between activations and
    // is unmeasured at MS3 -- so the C++ loader throws when the swept activation has no entry, and
    // when this object is authored under any other metric (nothing would read it). Declared above
    // ExplorationBlockConfig for the same CS0579 reason MonitorMs1Config is.
    [JsonKey("kinetic_model")]
    public class KineticModelConfig
    {
        [JsonKey("k2")]
        [Description("Overfragmentation rate per NCE, by activation (e.g. {\"HCD\": 0.0388}). Required for every swept activation when metric is kinetic_model; never defaulted (ADR-0047).")]
        public Dictionary<string, double> K2 { get; set; }
    }

    [JsonKey("exploration")]
    public class ExplorationBlockConfig
    {
        [JsonKey("metric")]
        [Description("Exploration metric: none, mass_count, remaining_precursor, fragment_count, kinetic_model")]
        public string Metric { get; set; } = "none";

        [JsonKey("ce_min")]
        [Description("Minimum collision energy for exploration sweep")]
        public double CEMin { get; set; } = 20;

        [JsonKey("ce_max")]
        [Description("Maximum collision energy for exploration sweep")]
        public double CEMax { get; set; } = 40;

        [JsonKey("ce_step")]
        [Description("Collision energy step size")]
        public double CEStep { get; set; } = 5;

        [JsonKey("overrides")]
        [Description("Per-field scan config overrides for exploration variants (e.g. analyzer, resolution)")]
        public Dictionary<string, string> Overrides { get; set; }

        [JsonKey("remaining_precursor_target")]
        [Description("Target precursor share (anchor-window signal over the whole scan's, same scan) for the remaining_precursor metric; 0.1 = 10% of the signal still precursor (ADR-0048)")]
        public double RemainingPrecursorTarget { get; set; } = 0.1;

        // Renamed from rt_min/rt_max/rt_step. These are ion-ion REACTION time in ms; "rt" elsewhere
        // in this codebase means RETENTION time (precursor_selection.rt_window, in seconds, and the
        // rt_min argument of the ProcessScan bridge export). Two unrelated quantities under the same
        // prefix, now adjacent in one document, is a 1000x misread waiting to happen.
        [JsonKey("reaction_time_min")]
        [Description("Minimum ion-ion reaction time for an ETD-family exploration sweep (ms)")]
        public double ReactionTimeMin { get; set; }

        [JsonKey("reaction_time_max")]
        [Description("Maximum ion-ion reaction time for an ETD-family exploration sweep (ms)")]
        public double ReactionTimeMax { get; set; }

        [JsonKey("reaction_time_step")]
        [Description("Ion-ion reaction time step size (ms). Must be > 0 — a zero or negative step is an infinite loop.")]
        public double ReactionTimeStep { get; set; } = 1;

        // Promoted out of the overrides map. It was the one key applyOverrides had no branch for:
        // Config.cpp:473-481 extracted it and then ERASED it from the map, before Exploration.cpp:605
        // tested that same map for emptiness to decide whether to acquire the production scan. So an
        // overrides block containing only tolerance_ppm silently suppressed a scan.
        [JsonKey("tolerance_ppm")]
        [Description("Mass tolerance for scoring exploration variants (ppm). 0 = use deconvolution.tol for this level.")]
        public double TolerancePpm { get; set; } = 0;

        [JsonKey("activations")]
        [Description("Activation types to sweep (e.g. HCD, ETD, CID, EThcD)")]
        public List<string> Activations { get; set; }

        // ADR-0042. See MonitorMs1Config for why this is per exploration block rather than a single
        // key under `scheduling`.
        [JsonKey("monitor_ms1")]
        public MonitorMs1Config MonitorMs1 { get; set; } = new MonitorMs1Config();

        // ADR-0047. Null by default and emitted as JSON null when unauthored, which the C++ parser
        // treats as absent -- so every config that does not sweep by the Kinetic Model crosses the
        // bridge exactly as before, and a kinetic_model metric without this block is refused there.
        [JsonKey("kinetic_model")]
        public KineticModelConfig KineticModel { get; set; }
    }

    // selection_strategy and its ms1/ms2/ms3 sub-blocks are DELETED.
    //
    // The section named each key for the level it was READ at, while the value governed the level
    // BELOW -- a shift-by-one that made every key read exactly one level off its effect:
    //     ms1.max_targets = the MS2 count      -> precursor_selection.max_precursors
    //     ms2.max_targets = the MS3 budget     -> characterization.max_targets
    //     ms3.max_targets = an MS4 budget      -> deleted, zero read sites
    // Four committed configs set ms3.max_targets:200 believing it was the MS3 budget and silently
    // ran 3. Same story for min_charge.
    //
    // ms2.selection and ms3.selection were booleans in disguise -- only None-vs-non-None was ever
    // read (FLASHIda.cpp:366, Exploration.cpp:728/:730) -- and are now derived from
    // characterization.mode. ms1.selection is the ONE value-sensitive selection metric
    // (PrecursorSelection.cpp:246) and survives as precursor_selection.rank_by.
    // ms1.exploration was discarded on both sides and is simply gone.

    /// <summary>
    /// Root method configuration — user-facing JSON schema.
    /// </summary>
    public class MethodConfig
    {
        [JsonKey("global")]
        public GlobalConfig Global { get; set; } = new GlobalConfig();

        [JsonKey("deconvolution")]
        public DeconvolutionConfig Deconvolution { get; set; } = new DeconvolutionConfig();

        [JsonKey("precursor_selection")]
        public PrecursorSelectionConfig PrecursorSelection { get; set; } = new PrecursorSelectionConfig();

        // ADR-0061. Null by default: an absent identification section matches nothing.
        [JsonKey("identification")]
        public IdentificationConfig Identification { get; set; }

        [JsonKey("quantification")]
        public QuantificationConfig Quantification { get; set; } = new QuantificationConfig();

        [JsonKey("faims")]
        public FaimsConfig Faims { get; set; } = new FaimsConfig();

        [JsonKey("ms_settings")]
        public MsSettingsConfig MsSettings { get; set; } = new MsSettingsConfig();

        [JsonKey("scheduling")]
        public SchedulingConfig Scheduling { get; set; } = new SchedulingConfig();

        [JsonKey("files")]
        public FilesConfig Files { get; set; } = new FilesConfig();

        [JsonKey("runtime")]
        public RuntimeConfig Runtime { get; set; } = new RuntimeConfig();
    }

    [JsonKey("runtime")]
    public class RuntimeConfig
    {
        // The five per-stream path keys (ida_log_path, scan_commands_path, scan_results_path,
        // identification_log_path, pooled_identification_log_path) are DELETED. Naming five
        // absolute paths per method is why no committed config ever set any of them, so the
        // engine's five streams were dark on the instrument for the whole life of the feature.
        //
        // THIS KEY MEANS TWO DIFFERENT THINGS EITHER SIDE OF THE BRIDGE, deliberately (ADR-0015):
        //   authored (here)  ""  ->  "." , the process working directory
        //   emitted (C++)    ""  ->  open nothing
        // Flash.Main / FLASHIdaWrapper.Main resolve the authored value ONCE via LogPathResolver
        // -- absolutise, append the per-run folder, create it -- and write the result back here
        // before ToCppJson runs. So an empty value never crosses the bridge while logging is on,
        // and a C++ fixture with no runtime section still opens nothing.
        [JsonKey("log_dir")]
        [Description("Folder that receives ALL log files. Each run gets its own timestamped "
                   + "subfolder inside it, holding ida.log, scan_commands.tsv, scan_results.tsv, "
                   + "identification.tsv, pooled_identification.tsv, FlashLog.log and IDALog.log. "
                   + "Empty means the current working directory.")]
        public string LogDir { get; set; } = "";
    }

    // --- Phase 1: JSON serialization classes for C++ bridge ---

    public class JsonDeconvolutionConfig
    {
        public double score_threshold { get; set; }
        public double tqscore_threshold { get; set; }
        public int min_charge { get; set; }
        public int max_charge { get; set; }
        public double min_mass { get; set; }
        public double max_mass { get; set; }
        public double[] tol { get; set; }
    }

    // These FIELD NAMES are the wire format -- JavaScriptSerializer emits them verbatim and there is
    // no [JsonKey] indirection here. Renaming the loader's [JsonKey] without renaming these produces
    // a config the C# loader accepts and the emitter writes in the OLD spelling, which C++ then
    // hard-rejects. Both halves must always move together.
    public class JsonPrecursorSelectionConfig
    {
        public double rt_window { get; set; }
        public string targeting { get; set; }
        public bool consider_all_charges { get; set; }
        public bool strict_inclusion { get; set; }
        public double tie_threshold { get; set; }
        public string precursor_charges { get; set; }
        public double snr_threshold { get; set; }
        public int max_charge_states { get; set; }
        public int min_charge_states { get; set; }
        public string charge_selection { get; set; }
        public bool log_target_rt_window { get; set; }
        public string log_target_charges { get; set; }
        public string rank_by { get; set; }
        public int max_precursors { get; set; }
        public int min_precursor_charge { get; set; }
        public string[] additional_scans { get; set; }
        public JsonExplorationBlockConfig exploration { get; set; }
    }

    // ADR-0061 d.13. Every key null when unauthored; C++ reads null as unset (FLASHTnT's default).
    public class JsonFlashTnTConfig
    {
        public int? min_length { get; set; }
        public int? max_length { get; set; }
        public bool? allow_gap { get; set; }
        public int? max_aa_in_gap { get; set; }
        public string[] fixed_mod { get; set; }
        public int? max_blind_mod_count { get; set; }
        public double? max_mod_mass { get; set; }
        public string[] var_mod { get; set; }
        public int? max_var_mod_count { get; set; }
        public int? max_protein_hits { get; set; }
        public string gapped_tags { get; set; }
        public bool? terminal_ladders { get; set; }
        public bool? precursor_ladders { get; set; }
        public bool? skip_precursor_inference { get; set; }
    }

    public class JsonGlobalConfig
    {
        public string method_name { get; set; }
        public string method_description { get; set; }
        public double duration { get; set; }
    }

    // EVERY authorable field must appear here. A field on QuantificationConfig with no counterpart
    // in this DTO is unreachable from method.json no matter how completely C++ supports it -- that
    // is exactly how only_one_condition spent its whole life bound, documented and inert (ADR-0038).
    public class JsonQuantificationConfig
    {
        public bool enabled { get; set; }
        // ADR-0064. Always emitted as a real string, like identify.
        public string scan { get; set; }
        public int ms3_targets { get; set; }
        public JsonYieldGateConfig ms3_yield_gate { get; set; }   // ADR-0069; null when unauthored
        public string labelling { get; set; }
        public double reporter_mz_tol { get; set; }
        public double fold_change_threshold { get; set; }
        // ADR-0039. Emitted UNCONDITIONALLY as real strings, unlike `conditions` below which is
        // nulled when unauthored. The null-skip trick is right for a genuinely-absent list and wrong
        // for a string with a default: SerializeValue would emit `"identify": null`, and C++
        // .value() on a present-but-null string throws a type_error -- the exact failure `conditions`
        // caused for all 41 committed configs. Nothing compares the emitted JSON, so always-emit is free.
        public string identify { get; set; }
        public string enriched_in { get; set; }
        // ADR-0056. The three strings always-emit for the same reason as identify above.
        public string test { get; set; }
        public double p_value_threshold { get; set; }
        public string empty_condition { get; set; }
        public bool scan_normalize { get; set; }
        public string aggregate { get; set; }
        public int min_usable_scans { get; set; }
        public List<JsonQuantCondition> conditions { get; set; }
        public List<string> correction_matrix { get; set; }
        // ADR-0057. Null when unauthored, so SerializeValue skips the key, like conditions above.
        public Dictionary<string, double> channel_normalization { get; set; }
    }

    public class JsonQuantCondition
    {
        public string name { get; set; }
        public List<string> channels { get; set; }
    }

    public class JsonFaimsConfig
    {
        public double[] cv_values { get; set; }
        public int max_cv_skip { get; set; }
        public int cv_precursor_threshold { get; set; }
    }

    // Emitted keys mirror the MS1Parameters struct fields exactly (single source of truth).
    public class JsonMs1Config
    {
        public string analyzer { get; set; }
        public double first_mass { get; set; }
        public double last_mass { get; set; }
        public int resolution { get; set; }
        public int agc_target { get; set; }
        public double max_it { get; set; }
        public int microscans { get; set; }
        public double rf_lens { get; set; }
        public double source_cid { get; set; }
        public double source_cid_scaling { get; set; }
        public string data_type { get; set; }
        public string scan_rate { get; set; }
    }

    // Emitted keys mirror the MS2Parameters/MS3Parameters struct fields exactly, which in turn cover
    // every C++ kScanKeys entry (Config.cpp:65-68) that can reach an MSn scan. A key omitted here can
    // never cross the bridge and is unreachable from method.json -- that is how follow-up scans lost
    // their reaction_time, and how ms2/ms3 lost rf_lens/source_cid/source_cid_scaling/scan_rate
    // (commit 45c2cf9, reversed by ADR-0011). ConfigSchemaParity_test pins the set mechanically.
    //
    // This one class serves every MSn emit site: ms_settings.ms2, ms2_quant, ms2_rescue, ms3, ms3_quant
    // and each additional_ms2 entry.
    public class JsonMs2Config
    {
        public string analyzer { get; set; }
        public string activation { get; set; }
        public int collision_energy { get; set; }
        public int resolution { get; set; }
        public int agc_target { get; set; }
        public double max_it { get; set; }
        public double first_mass { get; set; }
        public double last_mass { get; set; }
        public int microscans { get; set; }
        public string data_type { get; set; }
        public string scan_rate { get; set; }
        public double rf_lens { get; set; }
        public double source_cid { get; set; }
        public double source_cid_scaling { get; set; }
        public double reaction_time { get; set; }
        public double reagent_max_it { get; set; }
        public int reagent_agc_target { get; set; }
    }

    public class JsonMsSettingsConfig
    {
        public JsonMs1Config ms1 { get; set; }
        public JsonMs2Config ms2 { get; set; }
        // Null when unset, and SerializeValue skips nulls -- so the 40 configs with no
        // quantification scan stay exactly as short as they are today.
        public JsonMs2Config ms2_quant { get; set; }
        public JsonMs2Config ms3 { get; set; }
        // ADR-0064. Null when unset, like ms2_quant.
        public JsonMs2Config ms3_quant { get; set; }
        // ADR-0068. Null when unset, like ms2_quant.
        public JsonMs2Config ms2_rescue { get; set; }
        // Omitted from the emitted JSON when there are no extras (SerializeValue skips nulls), so the
        // 30 configs with none stay exactly as short as they are today.
        public Dictionary<string, JsonMs2Config> additional_ms2 { get; set; }
    }

    public class JsonCycleTimeConfig
    {
        public bool enabled { get; set; }
        public double value_ms { get; set; }
    }

    public class JsonScanTimeoutConfig
    {
        public bool enabled { get; set; }
        public double value_ms { get; set; }
    }

    public class JsonSchedulingConfig
    {
        public JsonCycleTimeConfig cycle_time { get; set; }
        public JsonScanTimeoutConfig scan_timeout { get; set; }
        public double agc_interval_seconds { get; set; }

        //Emitted although the engine ignores it. ToCppJson is what generates
        //test-data/config_schema_reference.json, and ConfigSchemaParity_test parses that file
        //through the C++ Config, which hard-rejects unknown keys (ADR-0007) -- so a host-only key
        //that is not emitted here and accepted there breaks that ctest. Both sides, one commit.
        public int target_depth { get; set; }
    }

    public class JsonExplorationConfig
    {
        public bool enabled { get; set; }
        public int max_depth { get; set; }
        public int max_variants { get; set; }
    }

    // --- Phase 7: JSON serialization classes for selection_strategy ---

    // ADR-0042. Emit DTO for MonitorMs1Config. Both properties are emitted unconditionally, like
    // every other key in ToCppJson -- C++ .value(key, default) fallbacks are dead in production
    // precisely because this side always writes the key.
    public class JsonMonitorMs1Config
    {
        public bool enabled { get; set; }
        public double interval_ms { get; set; }
    }

    // ADR-0047. Emitted as null when unauthored (the C++ parser reads null as absent).
    public class JsonKineticModelConfig
    {
        public Dictionary<string, double> k2 { get; set; }
    }

    public class JsonExplorationBlockConfig
    {
        public string metric { get; set; }
        public double ce_min { get; set; }
        public double ce_max { get; set; }
        public double ce_step { get; set; }
        public Dictionary<string, string> overrides { get; set; }
        public double remaining_precursor_target { get; set; }
        public double reaction_time_min { get; set; }
        public double reaction_time_max { get; set; }
        public double reaction_time_step { get; set; }
        public List<string> activations { get; set; }
        public double tolerance_ppm { get; set; }
        public JsonMonitorMs1Config monitor_ms1 { get; set; }
        public JsonKineticModelConfig kinetic_model { get; set; }
    }

    // JsonMsLevelConfig / JsonSelectionStrategyConfig are DELETED along with the section they emitted.
    // BuildSelectionStrategy() went with them -- it was the largest remaining non-identity transform
    // in ToCppJson, synthesizing a whole section and sharing one defaultExpl instance across three
    // levels. precursor_selection and characterization are now authored, emitted and parsed in the
    // same shape, which is a step ADR-0006 no longer has to take.

    public class JsonFilesConfig
    {
        public string[] target_logs { get; set; }
        public string inclusion_list { get; set; }
    }

    public class JsonCharacterizationConfig
    {
        public string mode { get; set; }
        public int max_targets { get; set; }
        public int min_fragment_charge { get; set; }
        public double min_target_mass { get; set; }
        public string fragment_charges { get; set; }
        public bool per_proteoform { get; set; }
        public JsonYieldGateConfig yield_gate { get; set; }   // ADR-0069; null when unauthored
        public JsonExplorationBlockConfig exploration { get; set; }
    }

    // ADR-0069. Emit DTO for YieldGateConfig; all seven keys written whenever the block is.
    public class JsonYieldGateConfig
    {
        public double threshold { get; set; }
        public double intercept { get; set; }
        public double fragment_intensity { get; set; }
        public double precursor_intensity { get; set; }
        public double precursor_charge { get; set; }
        public double mass_ratio { get; set; }
        public double fragment_iso_cos { get; set; }
    }

    public class JsonReferenceConfig
    {
        public string fasta { get; set; }
        public string sequence { get; set; }
    }

    // ADR-0061. The whole section is emitted as null when unauthored, and so are `refine` and
    // `characterization` inside it (the C++ parser reads null as absent).
    public class JsonIdentificationConfig
    {
        public JsonReferenceConfig reference { get; set; }
        public string method { get; set; }
        public JsonFlashTnTConfig flashtnt { get; set; }
        public JsonRefineBlockConfig refine { get; set; }
        public int? search_threads { get; set; }
        public double? threshold { get; set; }
        public JsonCharacterizationConfig characterization { get; set; }
    }

    // ADR-0049. Emit DTO for RefineBlockConfig; all seven keys written whenever the block is.
    public class JsonRefineBlockConfig
    {
        public double fdr { get; set; }
        public int shuffles { get; set; }
        public double grid_da { get; set; }
        public double max_shift_da { get; set; }
        public int min_ions { get; set; }
        public int max_levels { get; set; }
        public int jump_penalty { get; set; }
    }

    public class JsonRuntimeConfig
    {
        // The RESOLVED absolute run folder, not the authored base directory -- see RuntimeConfig.
        // Empty here means "open nothing" on the C++ side (Config::RuntimeConfig).
        public string log_dir { get; set; }
    }

    public class JsonMethodConfig
    {
        public JsonGlobalConfig global { get; set; }
        public JsonDeconvolutionConfig deconvolution { get; set; }
        public JsonPrecursorSelectionConfig precursor_selection { get; set; }
        public JsonIdentificationConfig identification { get; set; }
        public JsonQuantificationConfig quantification { get; set; }
        public JsonFaimsConfig faims { get; set; }
        public JsonMsSettingsConfig ms_settings { get; set; }
        public JsonSchedulingConfig scheduling { get; set; }
        public JsonFilesConfig files { get; set; }
        public JsonRuntimeConfig runtime { get; set; }
    }
}

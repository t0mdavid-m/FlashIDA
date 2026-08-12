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
    /// Tag-based target expansion. These two keys used to sit in `flashtnt`, where their names
    /// implied a reach they never had: neither is a FLASHTagger/FLASHExtender Param.
    /// max_ptm_count is read only by PrecursorSelection::generatePTMCombinations_, and
    /// max_flanking_mass_diff is a call argument FLASHIda passes to a static tagger helper at its
    /// own call site. Both remain stored in the C++ TargetingConfig, so the move is a parse-path
    /// change only -- no read site changed and no value moved.
    /// </summary>
    [JsonKey("tag_expansion")]
    public class TagExpansionConfig
    {
        [JsonKey("max_ptm_count")]
        [Description("Maximum PTMs per enumerated target mass (tag-based target expansion)")]
        public int MaxPtmCount { get; set; } = 3;

        [JsonKey("max_flanking_mass_diff")]
        [Description("Maximum flanking mass difference when matching a tag to a FASTA protein, in Da")]
        public double MaxFlankingMassDiff { get; set; } = 50000;
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
        [Description("Consider all charge states for precursor selection")]
        public bool ConsiderAllChargeStates { get; set; }

        [JsonKey("precursor_charges")]
        [Description("How many charge states of a selected precursor ONE MS2 acquires: \"single\" (the representative charge), \"separate\" (one MS2 per charge state, each its own precursor), or \"multiplexed\" (one MS2 co-isolating the whole SNR-positive set as notches). This is the only thing that decides acquisition geometry; exclusion is mass-keyed.")]
        public string PrecursorCharges { get; set; } = "single";

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

        // INITIALISED, unlike Exploration: ToCppJson must always emit the block, so a config that
        // omits it keeps today's values (3 / 50000) instead of emitting zeros.
        [JsonKey("tag_expansion")]
        public TagExpansionConfig TagExpansion { get; set; } = new TagExpansionConfig();
    }

    [JsonKey("tagging")]
    public class TaggingConfig
    {
        [JsonKey("active")]
        [Description("Enable MS2 sequence tagging")]
        public bool Active { get; set; }

        [JsonKey("conditional_ms2")]
        [Description("Use conditional MS2 based on tag results")]
        public bool ConditionalMS2 { get; set; }

        // A follow-up is just another MS2, so it no longer carries its own inline 17-key block --
        // it NAMES one in ms_settings.additional_ms2. A name that does not resolve is a load error.
        [JsonKey("follow_up_scan")]
        [Description("Name of an ms_settings.additional_ms2 entry to acquire as the conditional ('C') follow-up MS2")]
        public string FollowUpScan { get; set; } = "";
    }

    [JsonKey("flashtnt")]
    public class FlashTnTConfig
    {
        [JsonKey("min_length")]
        [Description("Minimum sequence tag length (FLASHTagger)")]
        public int MinLength { get; set; } = 3;

        [JsonKey("max_length")]
        [Description("Maximum sequence tag length (FLASHTagger)")]
        public int MaxLength { get; set; } = 8;

        [JsonKey("allow_gap")]
        [Description("Allow mass gaps in sequence tags (FLASHTagger)")]
        public bool AllowGap { get; set; } = false;

        [JsonKey("max_aa_in_gap")]
        [Description("Maximum amino acids in a tag mass gap (FLASHTagger)")]
        public int MaxAaInGap { get; set; } = 2;

        [JsonKey("fixed_mod")]
        [Description("Fixed modifications applied by the tagger and extender")]
        public List<string> FixedMod { get; set; } = new List<string>();

        [JsonKey("max_blind_mod_count")]
        [Description("Maximum blind modifications per proteoform (FLASHExtender)")]
        public int MaxBlindModCount { get; set; } = 2;

        [JsonKey("max_mod_mass")]
        [Description("Maximum absolute mass of a blind modification in Da (FLASHExtender). 700 preserves prior behavior.")]
        public double MaxModMass { get; set; } = 700;
    }

    [JsonKey("quantification")]
    public class QuantificationConfig
    {
        [JsonKey("enabled")]
        [Description("Enable isobaric labeling quantification")]
        public bool Active { get; set; }

        [JsonKey("reporter_mz_tol")]
        [Description("Reporter ion m/z tolerance in Da")]
        public double ReporterMZTol { get; set; }

        [JsonKey("fold_change_threshold")]
        [Description("Fold-change threshold for differential quantification")]
        public double FoldChangeThreshold { get; set; }

        [JsonKey("only_one_condition")]
        [Description("Only quantify targets present in one condition")]
        public bool OnlyOneCondition { get; set; }

        [JsonKey("follow_up_scan")]
        [Description("Name of an ms_settings.additional_ms2 entry to acquire as the quantification ('F') follow-up MS2")]
        public string FollowUpScan { get; set; } = "";
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
    /// All three levels are bare objects, so the common case (31 of 33 committed configs have exactly
    /// one MS2; all 33 have exactly one MS3) needs no naming at all. Extra MS2 configs — a second
    /// unconditional MS2, or a block backing a tagging/quantification follow-up — go in
    /// additional_ms2 under a name, and are reached by reference:
    ///
    ///     precursor_selection.additional_scans : fire per precursor, after ms_settings.ms2
    ///     tagging.follow_up_scan               : the conditional ('C') follow-up
    ///     quantification.follow_up_scan        : the quant ('F') follow-up
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

        [JsonKey("ms2")]
        public MS2Parameters MS2 { get; set; }

        [JsonKey("ms3")]
        public MS3Parameters MS3 { get; set; }

        // Absent in 30 of 33 committed configs. Keys are user-authored, so they cannot be
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

        [JsonKey("agc_interval_seconds")]
        [Description("AGC recalculation interval in seconds")]
        public double AgcIntervalSeconds { get; set; } = 30;
    }

    /// <summary>
    /// Decision section 2: HOW do we pin that proteoform down?
    ///
    /// This section holds decisions only -- no scan plumbing. The MS3 scan's instrument parameters
    /// stay in ms_settings.ms3 like every other scan config.
    ///
    /// `mode` is THE MS3 switch and it is the only one. It replaces three scattered gates
    /// (selection_strategy.ms2.selection, selection_strategy.ms3.selection, and the presence of
    /// ms_settings.ms3) plus the old `objective` key, whose absence silently meant "ambiguity".
    /// Supersedes ADR-0004, which decided there would be no enable flag.
    /// </summary>
    [JsonKey("characterization")]
    public class CharacterizationConfig
    {
        [JsonKey("mode")]
        [Description("MS3 characterization: off (no MS3), ambiguity (resolve PTM site ambiguity), coverage (extend sequence coverage), or exhaustive (fragment every deconvolved mass of the winner MS2 scan, mapped or not -- ADR-0023). Unknown values are rejected, not defaulted.")]
        public string Mode { get; set; } = "off";

        [JsonKey("protein_sequence")]
        [Description("Protein sequence fragments are matched against. Required when mode is not off.")]
        public string ProteinSequence { get; set; } = "";

        [JsonKey("max_targets")]
        [Description("Maximum MS3 scans planned per identified precursor. This is the MS3 budget (was selection_strategy.ms2.max_targets).")]
        public int MaxTargets { get; set; } = 3;

        [JsonKey("min_fragment_charge")]
        [Description("Minimum charge of an MS2 FRAGMENT for it to become an MS3 target (0 = no filter). Distinct from precursor_selection.min_precursor_charge.")]
        public int MinFragmentCharge { get; set; } = 0;

        [JsonKey("fragment_charges")]
        [Description("How many charge states of a target FRAGMENT one MS3 acquires: \"single\" (the fragment's best-MS2 charge), \"separate\" (one MS3 per observed charge state -- the budget then counts (fragment, charge) pairs), or \"multiplexed\" (one MS3 co-isolating them, so the budget counts fragments and the same slots buy more cleavage sites). Replaces the bool ms3_all_charges, whose two states are the first two values.")]
        public string FragmentCharges { get; set; } = "single";

        // The MS3 CE/RT sweep. Must stay separate from precursor_selection.exploration: a single
        // config legitimately sweeps different ranges at MS2 and MS3 (method_exploration_ms3_followup
        // sweeps HCD 20-40 at MS2 and CID 15-35 at MS3).
        [JsonKey("exploration")]
        public ExplorationBlockConfig Exploration { get; set; }
    }
        // Not inheritable from deconvolution.min_mass: that floor is not applied to MSn output (the
        // reference config sets min_mass 500 / min_charge 4 and its MS2 spectra still carry 248 Da and
        // charge-1 species). A genuinely new floor, not a duplicate of an existing one. Default 0 =
        // off, deliberately -- exhaustive does exactly what its name says until told otherwise.
        [JsonKey("min_target_mass")]
        [Description("Exhaustive mode only: deconvolved masses below this (Da) are not MS3 targets. 0 = off.")]
        public double MinTargetMass { get; set; } = 0.0;


    [JsonKey("files")]
    public class FilesConfig
    {
        [JsonKey("target_logs")]
        [Description("Log files containing target or excluded masses")]
        public List<string> TargetLogs { get; set; } = new List<string>();

        [JsonKey("fasta")]
        [Description("FASTA file path for sequence tagging")]
        public string FastaFile { get; set; } = "";

        [JsonKey("inclusion_list")]
        [Description("Inclusion list file path")]
        public string InclusionList { get; set; } = "";

        [JsonKey("ptm_list")]
        [Description("PTM list file path")]
        public string PtmList { get; set; } = "";
    }

    [JsonKey("exploration")]
    public class ExplorationBlockConfig
    {
        [JsonKey("metric")]
        [Description("Exploration metric: none, qscore, or intensity")]
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
        [Description("Target remaining precursor ratio for exploration (0.1 = 10%)")]
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

        [JsonKey("tagging")]
        public TaggingConfig Tagging { get; set; } = new TaggingConfig();

        [JsonKey("flashtnt")]
        public FlashTnTConfig FlashTnT { get; set; } = new FlashTnTConfig();

        [JsonKey("quantification")]
        public QuantificationConfig Quantification { get; set; } = new QuantificationConfig();

        [JsonKey("faims")]
        public FaimsConfig Faims { get; set; } = new FaimsConfig();

        [JsonKey("ms_settings")]
        public MsSettingsConfig MsSettings { get; set; } = new MsSettingsConfig();

        [JsonKey("scheduling")]
        public SchedulingConfig Scheduling { get; set; } = new SchedulingConfig();

        [JsonKey("characterization")]
        public CharacterizationConfig Characterization { get; set; } = new CharacterizationConfig();

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
        public string rank_by { get; set; }
        public int max_precursors { get; set; }
        public int min_precursor_charge { get; set; }
        public string[] additional_scans { get; set; }
        public JsonExplorationBlockConfig exploration { get; set; }
        public JsonTagExpansionConfig tag_expansion { get; set; }
    }

    public class JsonTagExpansionConfig
    {
        public int max_ptm_count { get; set; }
        public double max_flanking_mass_diff { get; set; }
    }

    // The wire now carries the NAME; C++ resolves it against ms_settings.additional_ms2 at parse
    // time, so no downstream consumer ever learns that names exist.
    public class JsonTaggingConfig
    {
        public string follow_up_scan { get; set; }
    }

    public class JsonFlashTnTConfig
    {
        public int min_length { get; set; }
        public int max_length { get; set; }
        public bool allow_gap { get; set; }
        public int max_aa_in_gap { get; set; }
        public string[] fixed_mod { get; set; }
        public int max_blind_mod_count { get; set; }
        public double max_mod_mass { get; set; }
    }

    public class JsonGlobalConfig
    {
        public string method_name { get; set; }
        public string method_description { get; set; }
        public double duration { get; set; }
    }

    public class JsonQuantificationConfig
    {
        public bool enabled { get; set; }
        public double reporter_mz_tol { get; set; }
        public double fold_change_threshold { get; set; }
        public string follow_up_scan { get; set; }
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
    // This one class serves FOUR emit sites: ms_settings.ms2[], ms_settings.ms3[],
    // tagging.follow_up_scan and quantification.follow_up_scan.
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
        public JsonMs2Config ms3 { get; set; }
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
    }

    public class JsonExplorationConfig
    {
        public bool enabled { get; set; }
        public int max_depth { get; set; }
        public int max_variants { get; set; }
    }

    // --- Phase 7: JSON serialization classes for selection_strategy ---

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
    }

    // JsonMsLevelConfig / JsonSelectionStrategyConfig are DELETED along with the section they emitted.
    // BuildSelectionStrategy() went with them -- it was the largest remaining non-identity transform
    // in ToCppJson, synthesizing a whole section and sharing one defaultExpl instance across three
    // levels. precursor_selection and characterization are now authored, emitted and parsed in the
    // same shape, which is a step ADR-0006 no longer has to take.

    public class JsonFilesConfig
    {
        public string[] target_logs { get; set; }
        public string fasta { get; set; }
        public string inclusion_list { get; set; }
        public string ptm_list { get; set; }
    }

    public class JsonCharacterizationConfig
    {
        public string mode { get; set; }
        public string protein_sequence { get; set; }
        public int max_targets { get; set; }
        public int min_fragment_charge { get; set; }
        public string fragment_charges { get; set; }
        public JsonExplorationBlockConfig exploration { get; set; }
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
        public JsonFlashTnTConfig flashtnt { get; set; }
        public double min_target_mass { get; set; }
        public JsonTaggingConfig tagging { get; set; }
        public JsonQuantificationConfig quantification { get; set; }
        public JsonFaimsConfig faims { get; set; }
        public JsonMsSettingsConfig ms_settings { get; set; }
        public JsonSchedulingConfig scheduling { get; set; }
        public JsonFilesConfig files { get; set; }
        public JsonCharacterizationConfig characterization { get; set; }
        public bool conditional_ms2 { get; set; }
        public JsonRuntimeConfig runtime { get; set; }
    }
}

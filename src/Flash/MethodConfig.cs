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

    [JsonKey("precursor_selection")]
    public class PrecursorSelectionConfig
    {
        [JsonKey("RT_window")]
        [Description("Retention time window in seconds for precursor tracking")]
        public double RTWindow { get; set; } = 180;

        [JsonKey("target_mode")]
        [Description("Targeting mode: 0=none, 1=inclusion, 2=exclusion, 3=deep")]
        public int TargetMode { get; set; } = 0;

        [JsonKey("strict_inclusion")]
        [Description("If true, only acquire targets from the inclusion list")]
        public bool StrictInclusion { get; set; }

        [JsonKey("tie_threshold")]
        [Description("Tie-breaking threshold for precursor ranking")]
        public double TieThreshold { get; set; } = 0.1;

        [JsonKey("AllCharges")]
        [Description("Consider all charge states for precursor selection")]
        public bool ConsiderAllChargeStates { get; set; }

        [JsonKey("HCDEnergy")]
        [Description("HCD collision energy for charge-state determination")]
        public int HCDEnergy { get; set; } = 29;

        [JsonKey("ChargeBasedExclusion")]
        [Description("Treat each (mass, charge) as an independent acquisition target; the mass itself is never globally excluded.")]
        public bool ChargeBasedExclusion { get; set; }
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

        [JsonKey("follow_up_scan")]
        [Description("Follow-up scan config for conditional MS2")]
        public MS2Parameters? FollowUpScan { get; set; }
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

        [JsonKey("max_ptm_count")]
        [Description("Maximum number of PTMs per proteoform during expansion")]
        public int MaxPtmCount { get; set; } = 3;

        [JsonKey("max_flanking_mass_diff")]
        [Description("Maximum flanking mass difference in Da (FLASHTagger)")]
        public double MaxFlankingMassDiff { get; set; } = 50000;

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
        [Description("Follow-up scan config for quantification")]
        public MS2Parameters? FollowUpScan { get; set; }
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

    [JsonKey("ms_settings")]
    public class MsSettingsConfig
    {
        [JsonKey("ms1")]
        public MS1Parameters MS1 { get; set; }

        [JsonKey("ms2")]
        public List<MS2Parameters> MS2 { get; set; } = new List<MS2Parameters>();

        [JsonKey("ms3")]
        public List<MS3Parameters> MS3 { get; set; } = new List<MS3Parameters>();
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

    [JsonKey("characterization")]
    public class CharacterizationConfig
    {
        [JsonKey("objective")]
        [Description("Characterization objective: ambiguity (resolve PTM site ambiguity) or coverage (extend sequence coverage)")]
        public string Objective { get; set; } = "ambiguity";

        [JsonKey("protein_sequence")]
        [Description("Protein sequence for targeted MS3 characterization")]
        public string ProteinSequence { get; set; } = "";

        [JsonKey("ms3_all_charges")]
        [Description("MS3AllCharges: dispatch one MS3 per observed charge state of a target fragment (default: single best charge)")]
        public bool MS3AllCharges { get; set; } = false;
    }

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

        [JsonKey("rt_min")]
        [Description("Minimum reaction time for ETD exploration sweep (ms)")]
        public double RTMin { get; set; }

        [JsonKey("rt_max")]
        [Description("Maximum reaction time for ETD exploration sweep (ms)")]
        public double RTMax { get; set; }

        [JsonKey("rt_step")]
        [Description("Reaction time step size (ms)")]
        public double RTStep { get; set; } = 1;

        [JsonKey("activations")]
        [Description("Activation types to sweep (e.g. HCD, ETD, CID, EThcD)")]
        public List<string> Activations { get; set; }
    }

    [JsonKey("ms1")]
    public class MS1SelectionConfig
    {
        [JsonKey("selection")]
        [Description("MS1 precursor selection metric: qscore, intensity, or none")]
        public string Selection { get; set; } = "qscore";

        [JsonKey("max_targets")]
        [Description("Maximum number of targets to select per MS1 scan")]
        public int MaxTargets { get; set; } = 10;

        [JsonKey("min_charge")]
        [Description("Minimum charge state for target selection (0 = no filter)")]
        public int MinCharge { get; set; } = 0;

        // ToCppJson emits an (inert at MS1) exploration block for every level; model it so the
        // generated reference round-trips through the strict loader. C++ ignores exploration at MS1.
        [JsonKey("exploration")]
        public ExplorationBlockConfig Exploration { get; set; }
    }

    [JsonKey("ms2")]
    public class MS2SelectionConfig
    {
        [JsonKey("selection")]
        [Description("MS2 fragment selection metric: qscore, intensity, or none")]
        public string Selection { get; set; } = "intensity";

        [JsonKey("max_targets")]
        [Description("Maximum number of targets to select per MS2 scan")]
        public int MaxTargets { get; set; } = 3;

        [JsonKey("min_charge")]
        [Description("Minimum charge state for target selection (0 = no filter)")]
        public int MinCharge { get; set; } = 0;

        [JsonKey("exploration")]
        public ExplorationBlockConfig Exploration { get; set; }
    }

    [JsonKey("ms3")]
    public class MS3SelectionConfig
    {
        [JsonKey("selection")]
        [Description("MS3 fragment selection metric: qscore, intensity, or none")]
        public string Selection { get; set; } = "none";

        [JsonKey("max_targets")]
        [Description("Maximum number of targets to select per MS3 scan")]
        public int MaxTargets { get; set; } = 3;

        [JsonKey("min_charge")]
        [Description("Minimum charge state for target selection (0 = no filter)")]
        public int MinCharge { get; set; } = 0;

        [JsonKey("exploration")]
        public ExplorationBlockConfig Exploration { get; set; }
    }

    [JsonKey("selection_strategy")]
    public class SelectionStrategyConfig
    {
        [JsonKey("ms1")]
        public MS1SelectionConfig MS1 { get; set; } = new MS1SelectionConfig();

        [JsonKey("ms2")]
        public MS2SelectionConfig MS2 { get; set; } = new MS2SelectionConfig();

        [JsonKey("ms3")]
        public MS3SelectionConfig MS3 { get; set; } = new MS3SelectionConfig();
    }

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

        [JsonKey("selection_strategy")]
        public SelectionStrategyConfig SelectionStrategy { get; set; } = new SelectionStrategyConfig();

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
        [JsonKey("ida_log_path")]
        public string IdaLogPath { get; set; } = "";

        [JsonKey("scan_commands_path")]
        public string ScanCommandsPath { get; set; } = "";

        [JsonKey("scan_results_path")]
        public string ScanResultsPath { get; set; } = "";

        [JsonKey("identification_log_path")]
        public string IdentificationLogPath { get; set; } = "";

        [JsonKey("pooled_identification_log_path")]
        public string PooledIdentificationLogPath { get; set; } = "";
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

    public class JsonPrecursorSelectionConfig
    {
        public double RT_window { get; set; }
        public int target_mode { get; set; }
        public bool AllCharges { get; set; }
        public int HCDEnergy { get; set; }
        public bool strict_inclusion { get; set; }
        public double tie_threshold { get; set; }
        public bool ChargeBasedExclusion { get; set; }
    }

    public class JsonTaggingConfig
    {
        public JsonMs2Config follow_up_scan { get; set; }
    }

    public class JsonFlashTnTConfig
    {
        public int min_length { get; set; }
        public int max_length { get; set; }
        public int max_ptm_count { get; set; }
        public double max_flanking_mass_diff { get; set; }
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
        public JsonMs2Config follow_up_scan { get; set; }
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
        public JsonMs2Config[] ms2 { get; set; }
        public JsonMs2Config[] ms3 { get; set; }
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
        public double rt_min { get; set; }
        public double rt_max { get; set; }
        public double rt_step { get; set; }
        public List<string> activations { get; set; }
    }

    public class JsonMsLevelConfig
    {
        public string selection { get; set; }
        public int max_targets { get; set; }
        public int min_charge { get; set; }
        public JsonExplorationBlockConfig exploration { get; set; }
    }

    public class JsonSelectionStrategyConfig
    {
        public JsonMsLevelConfig ms1 { get; set; }
        public JsonMsLevelConfig ms2 { get; set; }
        public JsonMsLevelConfig ms3 { get; set; }
    }

    public class JsonFilesConfig
    {
        public string[] target_logs { get; set; }
        public string fasta { get; set; }
        public string inclusion_list { get; set; }
        public string ptm_list { get; set; }
    }

    public class JsonCharacterizationConfig
    {
        public string objective { get; set; }
        public string protein_sequence { get; set; }
        public bool ms3_all_charges { get; set; }
    }

    public class JsonRuntimeConfig
    {
        public string ida_log_path { get; set; }
        public string scan_commands_path { get; set; }
        public string scan_results_path { get; set; }
        public string identification_log_path { get; set; }
        public string pooled_identification_log_path { get; set; }
    }

    public class JsonMethodConfig
    {
        public JsonGlobalConfig global { get; set; }
        public JsonDeconvolutionConfig deconvolution { get; set; }
        public JsonPrecursorSelectionConfig precursor_selection { get; set; }
        public JsonFlashTnTConfig flashtnt { get; set; }
        public JsonTaggingConfig tagging { get; set; }
        public JsonQuantificationConfig quantification { get; set; }
        public JsonFaimsConfig faims { get; set; }
        public JsonMsSettingsConfig ms_settings { get; set; }
        public JsonSchedulingConfig scheduling { get; set; }
        public JsonFilesConfig files { get; set; }
        public JsonCharacterizationConfig characterization { get; set; }
        public bool conditional_ms2 { get; set; }
        public JsonSelectionStrategyConfig selection_strategy { get; set; }
        public JsonRuntimeConfig runtime { get; set; }
    }
}

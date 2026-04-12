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
        [JsonKey("rt_window")]
        [Description("Retention time window in seconds for precursor tracking")]
        public double RTWindow { get; set; } = 180;

        [JsonKey("targeting_mode")]
        [Description("Targeting mode: none, inclusion, exclusion, or deep")]
        public string TargetingMode { get; set; } = "none";

        [JsonKey("strict_inclusion")]
        [Description("If true, only acquire targets from the inclusion list")]
        public bool StrictInclusion { get; set; }

        [JsonKey("tie_threshold")]
        [Description("Tie-breaking threshold for precursor ranking")]
        public double TieThreshold { get; set; } = 0.1;

        [Developer]
        [JsonKey("use_id_score")]
        [Description("Use identification-based scoring instead of QScore")]
        public bool UseIDScore { get; set; }

        [Developer]
        [JsonKey("consider_all_charges")]
        [Description("Consider all charge states for precursor selection")]
        public bool ConsiderAllChargeStates { get; set; }

        [Developer]
        [JsonKey("hcd_energy")]
        [Description("HCD collision energy for charge-state determination")]
        public int HCDEnergy { get; set; } = 29;
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

        [JsonKey("min_tag_length")]
        [Description("Minimum sequence tag length")]
        public int MinTagLength { get; set; } = 3;

        [JsonKey("max_tag_length")]
        [Description("Maximum sequence tag length")]
        public int MaxTagLength { get; set; } = 8;

        [JsonKey("max_ptm_count")]
        [Description("Maximum number of PTMs to consider per tag")]
        public int MaxPtmCount { get; set; } = 3;

        [JsonKey("max_flanking_mass_diff")]
        [Description("Maximum flanking mass difference in Da")]
        public double MaxFlankingMassDiff { get; set; } = 50000;

        [JsonKey("follow_up_scan")]
        [Description("Follow-up scan config for conditional MS2")]
        public MS2Parameters? FollowUpScan { get; set; }
    }

    [JsonKey("quantification")]
    public class QuantificationConfig
    {
        [JsonKey("active")]
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

        [Developer]
        [JsonKey("max_cv_skip")]
        [Description("Maximum number of FAIMS CV cycles to skip")]
        public int MaxCVSkip { get; set; }

        [Developer]
        [JsonKey("mass_threshold")]
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

    [JsonKey("scheduling")]
    public class SchedulingConfig
    {
        [JsonKey("cycle_time_enabled")]
        [Description("Enable cycle time limit")]
        public bool CycleTimeEnabled { get; set; }

        [JsonKey("cycle_time_ms")]
        [Description("Maximum cycle time in milliseconds")]
        public double CycleTimeMs { get; set; } = 60000;

        [JsonKey("timeout_enabled")]
        [Description("Enable scan timeout")]
        public bool TimeoutEnabled { get; set; }

        [JsonKey("timeout_ms")]
        [Description("Scan timeout in milliseconds")]
        public double TimeoutMs { get; set; } = 30000;
    }

    [JsonKey("ms3")]
    public class Ms3Config
    {
        [JsonKey("active")]
        [Description("Enable MS3 characterization")]
        public bool Active { get; set; }

        [JsonKey("mode")]
        [Description("MS3 characterization mode (1, 2, or 3)")]
        public int Mode { get; set; }

        [JsonKey("max_per_ms2")]
        [Description("Maximum MS3 scans per MS2 scan")]
        public int MaxPerMs2 { get; set; } = 4;

        [JsonKey("all_charges")]
        [Description("Consider all charge states for MS3")]
        public bool AllCharges { get; set; }

        [JsonKey("protein_sequence")]
        [Description("Protein sequence for MS3 targeted characterization")]
        public string ProteinSequence { get; set; } = "";
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

        [JsonKey("activation")]
        [Description("Activation method for exploration (HCD or CID)")]
        public string Activation { get; set; } = "HCD";
    }

    [JsonKey("ms1")]
    public class MS1SelectionConfig
    {
        [JsonKey("selection")]
        [Description("MS1 precursor selection metric: qscore, intensity, or none")]
        public string Selection { get; set; } = "qscore";

        [JsonKey("max_precursors")]
        [Description("Maximum number of precursors to select per MS1 scan")]
        public int MaxPrecursors { get; set; } = 10;
    }

    [JsonKey("ms2")]
    public class MS2SelectionConfig
    {
        [JsonKey("selection")]
        [Description("MS2 fragment selection metric: qscore, intensity, or none")]
        public string Selection { get; set; } = "intensity";

        [JsonKey("max_fragments")]
        [Description("Maximum number of fragments to select per MS2 scan")]
        public int MaxFragments { get; set; } = 3;

        [JsonKey("exploration")]
        public ExplorationBlockConfig Exploration { get; set; }
    }

    [JsonKey("ms3")]
    public class MS3SelectionConfig
    {
        [JsonKey("selection")]
        [Description("MS3 fragment selection metric: qscore, intensity, or none")]
        public string Selection { get; set; } = "none";

        [JsonKey("max_fragments")]
        [Description("Maximum number of fragments to select per MS3 scan")]
        public int MaxFragments { get; set; } = 3;

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

        [JsonKey("ms3")]
        public Ms3Config Ms3 { get; set; } = new Ms3Config();

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
        public bool IDScore { get; set; }
        public bool AllCharges { get; set; }
        public bool MS3AllCharges { get; set; }
        public int HCDEnergy { get; set; }
        public bool strict_inclusion { get; set; }
        public double tie_threshold { get; set; }
    }

    public class JsonTaggingConfig
    {
        public int min_tag_length { get; set; }
        public int max_tag_length { get; set; }
        public int max_ptm_count { get; set; }
        public double max_flanking_mass_diff { get; set; }
        public JsonMs2Config follow_up_scan { get; set; }
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

    public class JsonMs1Config
    {
        public string analyzer { get; set; }
        public double first_mass { get; set; }
        public double last_mass { get; set; }
        public int resolution { get; set; }
        public int agc_target { get; set; }
        public double max_it { get; set; }
    }

    public class JsonMs2Config
    {
        public string analyzer { get; set; }
        public string activation { get; set; }
        public int collision_energy { get; set; }
        public int resolution { get; set; }
    }

    public class JsonMsSettingsConfig
    {
        public JsonMs1Config ms1 { get; set; }
        public JsonMs2Config[] ms2 { get; set; }
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
        public string activation { get; set; }
    }

    public class JsonMsLevelConfig
    {
        public string selection { get; set; }
        public int max_precursors { get; set; }
        public int max_fragments { get; set; }
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

    public class JsonMs3Config
    {
        public bool enabled { get; set; }
        public int mode { get; set; }
        public int max_per_ms2 { get; set; }
        public string protein_sequence { get; set; }
    }

    public class JsonRuntimeConfig
    {
        public string ida_log_path { get; set; }
        public string scan_commands_path { get; set; }
        public string scan_results_path { get; set; }
    }

    public class JsonMethodConfig
    {
        public JsonDeconvolutionConfig deconvolution { get; set; }
        public JsonPrecursorSelectionConfig precursor_selection { get; set; }
        public JsonTaggingConfig tagging { get; set; }
        public JsonQuantificationConfig quantification { get; set; }
        public JsonFaimsConfig faims { get; set; }
        public JsonMsSettingsConfig ms_settings { get; set; }
        public JsonSchedulingConfig scheduling { get; set; }
        public JsonExplorationConfig exploration { get; set; }
        public JsonFilesConfig files { get; set; }
        public JsonMs3Config ms3 { get; set; }
        public bool conditional_ms2 { get; set; }
        public JsonSelectionStrategyConfig selection_strategy { get; set; }
        public JsonRuntimeConfig runtime { get; set; }
    }
}

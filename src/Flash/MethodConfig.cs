using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace Flash
{
    public class GlobalParameters
    {
        public string MethodName;
        public string MethodDescription;
        public double Duration = 90;
    }

    public class PrecursorSelectionParameters
    {
        public double QScoreThreshold = -1;
        public double TQScoreThreshold = 0.9;
        public int MinCharge = 4;
        public int MaxCharge = 50;
        public double MinMass = 500;
        public double MaxMass = 50000;
        public double RTWindow = 180;
        public int HCDEnergy = 29;
        [XmlArray] public double[] Tolerances = new double[] { 10, 10 };
    }

    public class MS2TaggingConfig
    {
        public string Active = "False";
        public bool ConditionalMS2;
        public string FastaFile;
        public string PtmList;
        public int MaxPtmCount = 3;
        public int MinTagLength = 3;
        public int MaxTagLength = 8;
        public double MaxFlankingMassDiff = 50000;
    }

    public class TargetedInclusionConfig
    {
        public bool StrictInclusion;
        public double TieThreshold = 0.1;
        public string InclusionList;
        public MS2TaggingConfig MS2Tagging;
    }

    public class TargetedExclusionConfig { }

    public class DeepModeConfig { }

    public class LabelingQuantConfig
    {
        public string Active = "False";
        public double ReporterMZTol;
        public double FoldChangeThreshold;
        public bool OnlyOneCondition;
    }

    public class MS3CharacterizationConfig
    {
        public string Active = "False";
        public int MS3Mode;
        public int MaxMs3PerMs2 = 4;
        public bool MS3AllCharges;
        public string MS3ProteinSequence;
    }

    public class DeveloperFAIMSConfig
    {
        public int MaxCVSkip;
        public int MassThreshold = 15;
    }

    public class DeveloperPrecursorSelectionConfig
    {
        public bool UseIDScore;
        public bool ConsiderAllChargeStates;
    }

    public class DeveloperConfig
    {
        public DeveloperPrecursorSelectionConfig PrecursorSelection;
        public DeveloperFAIMSConfig FAIMS;
    }

    public class AcquisitionModesConfig
    {
        public string TargetingMode = "None";
        public List<string> TargetLogs;
        public TargetedInclusionConfig TargetedInclusion;
        public TargetedExclusionConfig TargetedExclusion;
        public DeepModeConfig DeepMode;
        public LabelingQuantConfig LabelingBasedQuantification;
        public MS3CharacterizationConfig MS3Characterization;
        public DeveloperConfig Developer;
    }

    public class FAIMSSettings
    {
        [XmlArray] public double[] CVValues;
    }

    public class MSSettingsConfig
    {
        public int MaxMs2CountPerMs1 = 4;
        public FAIMSSettings FAIMS;
        public MS1Parameters MS1;
        public List<MS2Parameters> MS2;
        public List<MS3Parameters> MS3;
    }

    // --- Phase 1 deferrals resolved in Phase 3 ---

    /// <summary>
    /// Scan scheduling configuration — cycle time and timeout settings.
    /// Phase 1 deferral: stored for future scan command construction.
    /// </summary>
    [Serializable]
    public class ScanSchedulingConfig
    {
        public bool CycleTimeEnabled;
        public double CycleTimeMs = 60000.0;
        public bool TimeoutEnabled;
        public double TimeoutMs = 30000.0;
    }

    /// <summary>
    /// Parameter optimization configuration — exploration and variant settings.
    /// Phase 1 deferral: stored for future parameter optimization.
    /// </summary>
    [Serializable]
    public class ParameterOptimizationConfig
    {
        public bool ExplorationEnabled;
        public int MaxDepth = 1;
        public int MaxVariants = 5;
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
        public int[] max_mass_count { get; set; }
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
    }

    public class JsonQuantificationConfig
    {
        public bool enabled { get; set; }
        public double reporter_mz_tol { get; set; }
        public double fold_change_threshold { get; set; }
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
    }
}

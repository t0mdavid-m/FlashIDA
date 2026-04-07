using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Web.Script.Serialization;
using System.Xml.Serialization;

namespace Flash.IDA
{
    /// <summary>
    /// Parameters for FLASHIda
    /// </summary>
    public class IDAParameters
    {        
        [Description("Maximum number of MS2 scans per MS1 cycle")]
        public int MaxMs2CountPerMs1 { set; get; }

        [Description("Targeting mode (0=normal, 1=inclusion, 2=deep, 3=exclusion)")]
        public int TargetMode { set; get; }
        [Description("Quality score threshold for precursor selection")]
        public double QScoreThreshold { set; get; }
        [Description("Tie-breaking threshold for precursor ranking")]
        public double TieThreshold { set; get; }
        public double TQScoreThreshold { set; get; }
        public double quantReporterMZTol { set; get; }
        public double quantFoldChangeThreshold { set; get; }
        public bool quantOnlyOneCondition { set; get; }

        [Description("Minimum precursor charge state")]
        public int MinCharge { set; get; }

        [Description("Maximum precursor charge state")]
        public int MaxCharge { set; get; }

        [Description("Minimum precursor mass in Da")]
        public double MinMass { set; get; }

        [Description("Maximum precursor mass in Da")]
        public double MaxMass { set; get; }

        public List<string> TargetLogs { set; get; }

        [XmlArray()]
        public double[] Tolerances { set; get; }

        [XmlArray()]
        public double[] CVValues { set; get; }
        
        public double RTWindow { set; get; }

        public int MaxCVSkip { set; get; }
        public int MassThreshold { set; get; }

        public bool UseIDScore { set; get; }
        public bool ConsiderAllChargeStates { set; get; }
        [Description("HCD collision energy")]
        public int HCDEnergy { set; get; }

        public bool StrictInclusion { set; get; }
        public string InclusionList { set; get; }
        public string PtmList { set; get; }

        public bool MS2Tagging { set; get; }
        public string FastaFile { set; get; }
        public int MaxPtmCount { set; get; }
        public int MinTagLength { set; get; }
        public int MaxTagLength { set; get; }
        public double MaxFlankingMassDiff { set; get; }
        public bool ConditionalMS2 { set; get; }

        // MS3 mode parameters
        public bool EnableMS3 { set; get; }
        public int MS3Mode { set; get; }
        public int MaxMs3PerMs2 { set; get; }
        public string MS3ProteinSequence { set; get; }
        public bool MS3AllCharges { set; get; }

        /// <summary>
        /// Complete constructor
        /// </summary>
        /// <param name="tolerances">Two member array for mass tolerances (down, up)</param>
        /// <param name="maxMs2CountPerMs1"></param>
        /// <param name="qScoreThreshold">Threshold for quality score</param>
        /// <param name="rtWindow">Retention time tolerance window</param>
        /// <param name="minCharge">Minimal precursor charge</param>
        /// <param name="maxCharge">Maximal precursor charge</param>
        /// <param name="minMass">Minimal precursor mass</param>
        /// <param name="maxMass">Maximal precursor mass</param> 
        /// <param name="targetLogs">log files containing target or excluded masses</param> 
        /// <param name="targetMode">If set to 1, inclusive targeted mode if 2, exclusive targeted mode. If 0, normal exclusion list mode</param> 
        /// <param name="cvvalues">contains the cvvalues to be scanned</param>
        public IDAParameters(double[] tolerances = null, int maxMs2CountPerMs1 = 5, double qScoreThreshold = -1, double tieThreshold = 0.1, double rtWindow = 5, int minCharge = 1, int maxCharge = 100,
                             double minMass = 50, double maxMass = 100000, List<string> targetLogs = null, int targetMode = 0, double[] cvvalues = null,
                             int MaxCVSkip_ = 0, int MassThreshold_ = 15, double tqScoreThreshold = 0.9, double quantReporterMZTol_ = 0, double quantFoldChangeThreshold_ = 0, bool quantOnlyOneCondition_ = false,
                             bool UseIDScore_ = false, bool ConsiderAllChargeStates_ = false, int HCDEnergy_ = 29,
                             bool strictInclusion = false, string inclusionList = null, string ptmList = null,
                             bool ms2Tagging = false, string fastaFile = null, int maxPtmCount = 3, int minTagLength = 3, int maxTagLength = 8, double maxFlankingMassDiff = 50000.0,
                             bool conditionalMS2 = false,
                             bool enableMS3 = false, int ms3Mode = 0, int maxMs3PerMs2 = 4, string ms3ProteinSequence = null, bool ms3AllCharges = false)
        {
            Tolerances = tolerances ?? new double[] { 10, 10 };
            CVValues = cvvalues ?? new double[] { 0.0, -40.0, -50.0, -60.0 };
            RTWindow = rtWindow;
            MaxMs2CountPerMs1 = maxMs2CountPerMs1;
            MinCharge = minCharge;
            MaxCharge = maxCharge;
            MinMass = minMass;
            MaxMass = maxMass;
            QScoreThreshold = qScoreThreshold;
            TieThreshold = tieThreshold;
            TQScoreThreshold = tqScoreThreshold;
            TargetLogs = targetLogs;
            TargetMode = targetMode;
            MaxCVSkip = MaxCVSkip_;
            MassThreshold = MassThreshold_;
            quantReporterMZTol = quantReporterMZTol_;
            quantFoldChangeThreshold = quantFoldChangeThreshold_;
            quantOnlyOneCondition = quantOnlyOneCondition_;
            UseIDScore = UseIDScore_;
            ConsiderAllChargeStates = ConsiderAllChargeStates_;
            HCDEnergy = HCDEnergy_;
            StrictInclusion = strictInclusion;
            InclusionList = inclusionList;
            PtmList = ptmList;
            MS2Tagging = ms2Tagging;
            FastaFile = fastaFile;
            MaxPtmCount = maxPtmCount;
            MinTagLength = minTagLength;
            MaxTagLength = maxTagLength;
            MaxFlankingMassDiff = maxFlankingMassDiff;
            ConditionalMS2 = conditionalMS2;
            EnableMS3 = enableMS3;
            MS3Mode = ms3Mode;
            MaxMs3PerMs2 = maxMs3PerMs2;
            MS3ProteinSequence = ms3ProteinSequence;
            MS3AllCharges = ms3AllCharges;
        }

        /// <summary>
        /// Parameterless constructor used only for serialization
        /// </summary>
        public IDAParameters()
        {
            Tolerances = new double[] { 0, 0 };
        }
        
        /// <summary>
        /// Serialize full method configuration as JSON for C++ engine.
        /// </summary>
        public string ToJSON(MethodParameters mp)
        {
            if (mp == null)
                throw new ArgumentNullException(nameof(mp));

            var ms2List = mp.MS2 ?? new System.Collections.Generic.List<MS2Parameters>();

            var config = new JsonMethodConfig
            {
                deconvolution = new JsonDeconvolutionConfig
                {
                    score_threshold = QScoreThreshold,
                    tqscore_threshold = TQScoreThreshold,
                    min_charge = MinCharge,
                    max_charge = MaxCharge,
                    min_mass = MinMass,
                    max_mass = MaxMass,
                    tol = Tolerances
                },
                precursor_selection = new JsonPrecursorSelectionConfig
                {
                    max_mass_count = new int[] { MaxMs2CountPerMs1 },
                    RT_window = RTWindow,
                    target_mode = TargetMode,
                    IDScore = UseIDScore,
                    AllCharges = ConsiderAllChargeStates,
                    MS3AllCharges = MS3AllCharges,
                    HCDEnergy = HCDEnergy,
                    strict_inclusion = StrictInclusion,
                    tie_threshold = TieThreshold
                },
                tagging = new JsonTaggingConfig
                {
                    min_tag_length = MinTagLength,
                    max_tag_length = MaxTagLength,
                    max_ptm_count = MaxPtmCount,
                    max_flanking_mass_diff = MaxFlankingMassDiff
                },
                quantification = new JsonQuantificationConfig
                {
                    enabled = mp.isobaricQuantification,
                    reporter_mz_tol = quantReporterMZTol,
                    fold_change_threshold = quantFoldChangeThreshold
                },
                faims = new JsonFaimsConfig
                {
                    cv_values = CVValues,
                    max_cv_skip = MaxCVSkip,
                    cv_precursor_threshold = MassThreshold
                },
                ms_settings = new JsonMsSettingsConfig
                {
                    ms1 = new JsonMs1Config
                    {
                        analyzer = mp.MS1.Analyzer ?? "",
                        first_mass = mp.MS1.FirstMass,
                        last_mass = mp.MS1.LastMass,
                        resolution = mp.MS1.OrbitrapResolution,
                        agc_target = mp.MS1.AGCTarget,
                        max_it = mp.MS1.MaxIT
                    },
                    ms2 = ms2List.Select(m => new JsonMs2Config
                    {
                        analyzer = m.Analyzer ?? "",
                        activation = m.Activation ?? "",
                        collision_energy = m.CollisionEnergy,
                        resolution = m.OrbitrapResolution
                    }).ToArray()
                },
                scheduling = new JsonSchedulingConfig
                {
                    cycle_time = new JsonCycleTimeConfig { enabled = false, value_ms = 60000 },
                    scan_timeout = new JsonScanTimeoutConfig { enabled = false, value_ms = 30000 },
                    agc_interval_seconds = 30
                },
                exploration = new JsonExplorationConfig
                {
                    enabled = false,
                    max_depth = 1,
                    max_variants = 5
                },
                selection_strategy = BuildSelectionStrategy(mp),
                ms3 = new JsonMs3Config
                {
                    enabled = EnableMS3,
                    mode = MS3Mode,
                    max_per_ms2 = MaxMs3PerMs2,
                    protein_sequence = MS3ProteinSequence ?? ""
                },
                conditional_ms2 = ConditionalMS2,
                files = new JsonFilesConfig
                {
                    target_logs = (TargetLogs ?? new List<string>()).ToArray(),
                    fasta = FastaFile ?? "",
                    inclusion_list = InclusionList ?? "",
                    ptm_list = PtmList ?? ""
                }
            };

            return new JavaScriptSerializer().Serialize(config);
        }

        /// <summary>
        /// Build selection_strategy JSON object from MethodParameters SelectionStrategy XML.
        /// Crashes if SelectionStrategy is absent (required in all configs).
        /// </summary>
        private JsonSelectionStrategyConfig BuildSelectionStrategy(MethodParameters mp)
        {
            var ss = mp.SelectionStrategy;
            if (ss == null)
                throw new InvalidOperationException(
                    "Method XML must contain <SelectionStrategy> block. " +
                    "All method configs must be updated for Phase 7.");

            int ms1MaxTargets = ss.MS1?.MaxPrecursors ?? MaxMs2CountPerMs1;
            int ms2MaxTargets = ss.MS2?.MaxFragments ?? 3;
            int ms3MaxTargets = ss.MS3?.MaxFragments ?? 3;

            var result = new JsonSelectionStrategyConfig
            {
                ms1 = new JsonMsLevelConfig
                {
                    selection = (ss.MS1?.Selection ?? "qscore").ToLower(),
                    max_precursors = ms1MaxTargets,
                    max_fragments = ms1MaxTargets
                },
                ms2 = new JsonMsLevelConfig
                {
                    selection = (ss.MS2?.Selection ?? "intensity").ToLower(),
                    max_precursors = ms2MaxTargets,
                    max_fragments = ms2MaxTargets
                },
                ms3 = new JsonMsLevelConfig
                {
                    selection = (ss.MS3?.Selection ?? "none").ToLower(),
                    max_precursors = ms3MaxTargets,
                    max_fragments = ms3MaxTargets
                }
            };

            // Exploration: always emit a block (JavaScriptSerializer writes null, which
            // crashes C++ nlohmann::json parser). Default to metric="none" when not configured.
            var defaultExpl = new JsonExplorationBlockConfig
            {
                metric = "none", ce_min = 20, ce_max = 40, ce_step = 5, activation = "HCD"
            };
            result.ms1.exploration = defaultExpl;
            result.ms2.exploration = defaultExpl;
            result.ms3.exploration = defaultExpl;

            // MS2 exploration (override if configured)
            if (ss.MS2?.Exploration != null && ss.MS2.Exploration.Metric != "none")
            {
                result.ms2.exploration = new JsonExplorationBlockConfig
                {
                    metric = ss.MS2.Exploration.Metric.ToLower(),
                    ce_min = ss.MS2.Exploration.CEMin,
                    ce_max = ss.MS2.Exploration.CEMax,
                    ce_step = ss.MS2.Exploration.CEStep,
                    activation = ss.MS2.Exploration.Activation ?? "HCD"
                };
            }

            // MS3 exploration (override if configured)
            if (ss.MS3?.Exploration != null && ss.MS3.Exploration.Metric != "none")
            {
                result.ms3.exploration = new JsonExplorationBlockConfig
                {
                    metric = ss.MS3.Exploration.Metric.ToLower(),
                    ce_min = ss.MS3.Exploration.CEMin,
                    ce_max = ss.MS3.Exploration.CEMax,
                    ce_step = ss.MS3.Exploration.CEStep,
                    activation = ss.MS3.Exploration.Activation ?? "CID"
                };
            }

            return result;
        }

    }
}

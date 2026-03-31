using System;
using System.Collections.Generic;
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
        public int MaxMs2CountPerMs1 { set; get; }

        public int TargetMode { set; get; } 
        public double QScoreThreshold { set; get; }
        public double TieThreshold { set; get; }
        public double TQScoreThreshold { set; get; }
        public double quantReporterMZTol { set; get; }
        public double quantFoldChangeThreshold { set; get; }
        public bool quantOnlyOneCondition { set; get; }

        public int MinCharge { set; get; }
        
        public int MaxCharge { set; get; }
        
        public double MinMass { set; get; }

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
        /// Convert <see cref="IDAParameters"/> instnace to string representation to transfer to C++ engine
        /// </summary>
        /// <returns></returns>
        public string ToFLASHDeconvInput()
        {
            var ret = String.Format("max_mass_count {0} score_threshold {1} min_charge {2} max_charge {3} min_mass {4} max_mass {5} RT_window {6} tol {7} tqscore_threshold {8} target_mode {9} IDScore {10} AllCharges {11} HCDEnergy {12} strict_inclusion {13} tie_threshold {14} MS3AllCharges {15} ",
                MaxMs2CountPerMs1, QScoreThreshold, MinCharge, MaxCharge, MinMass, MaxMass, RTWindow, String.Join(" ", Tolerances), TQScoreThreshold, TargetMode, UseIDScore ? 1 : 0, ConsiderAllChargeStates ? 1 : 0, HCDEnergy, StrictInclusion ? 1 : 0, TieThreshold, MS3AllCharges ? 1 : 0);

            // min_tag_length and max_ptm_count must come before file paths
            if (MinTagLength > 0)
            {
                ret += String.Format("min_tag_length {0} ", MinTagLength);
            }

            if (MaxTagLength > 0)
            {
                ret += String.Format("max_tag_length {0} ", MaxTagLength);
            }

            if (MaxPtmCount > 0)
            {
                ret += String.Format("max_ptm_count {0} ", MaxPtmCount);
            }

            if (MaxFlankingMassDiff > 0)
            {
                ret += String.Format("max_flanking_mass_diff {0} ", MaxFlankingMassDiff);
            }

            foreach(var f in TargetLogs)
            {
                ret += f + " ";
            }

            // PTM list must come before inclusion list (file extension detection order)
            if (!String.IsNullOrEmpty(PtmList))
            {
                ret += PtmList + " ";
            }

            if (!String.IsNullOrEmpty(InclusionList))
            {
                ret += InclusionList + " ";
            }

            // FASTA file must come last (file extension detection order: .fasta/.fa)
            if (!String.IsNullOrEmpty(FastaFile))
            {
                ret += FastaFile + " ";
            }

            return ret;
        }

        /// <summary>
        /// Serialize full method configuration as JSON for C++ engine (Phase 1).
        /// Falls back to <see cref="ToFLASHDeconvInput()"/> if mp is null.
        /// </summary>
        public string ToJSON(MethodParameters mp)
        {
            if (mp == null)
                return ToFLASHDeconvInput();

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
                    max_cv_skip = MaxCVSkip
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

    }
}

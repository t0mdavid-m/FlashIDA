using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Flash
{
    /// <summary>
    /// MS1 acquisition parameters
    /// </summary>
    /// <remarks>
    /// Naming of properties is alligned with Thermo instrument API and should not be changed
    /// </remarks>
    public struct MS1Parameters
    {
        public string Analyzer;
        public double FirstMass;
        public double LastMass;
        public int OrbitrapResolution;
        public int AGCTarget;
        public double MaxIT;
        public int Microscans;
        public string DataType;
        public double RFLens;
        public double SourceCID;
        // Should be zero
        public double SourceCIDScaling;
    }

    /// <summary>
    /// MS2 acquisition parameters
    /// </summary>
    /// <remarks>
    /// Naming of properties is alligned with Thermo instrument API and should not be changed
    /// </remarks>
    public struct MS2Parameters
    {
        public string Analyzer;
        public string IsolationMode;
        public double FirstMass;
        public double LastMass;
        public int OrbitrapResolution;
        public int AGCTarget;
        public double MaxIT;
        public int Microscans;
        public string DataType;
        public string Activation;
        public double ReactionTime;
        public double ReagentMaxIT;
        public int ReagentAGCTarget;
        public int CollisionEnergy;
    }

    /// <summary>
    /// MS3 acquisition parameters
    /// </summary>
    /// <remarks>
    /// Naming of properties is alligned with Thermo instrument API and should not be changed
    /// </remarks>
    public struct MS3Parameters
    {
        public string Analyzer;
        public string IsolationMode;
        public double FirstMass;
        public double LastMass;
        public int OrbitrapResolution;
        public int AGCTarget;
        public double MaxIT;
        public int Microscans;
        public string DataType;
        public string Activation;
        public double ReactionTime;
        public double ReagentMaxIT;
        public int ReagentAGCTarget;
        public int CollisionEnergy;
    }

    /// <summary>
    /// Complete set of acquisition parameters, loaded from JSON config.
    /// </summary>
    public class MethodParameters
    {
        public MethodConfig Config { get; set; }

        public MethodParameters()
        {
            Config = new MethodConfig();
        }

        public static MethodParameters Load(string path)
        {
            string json = File.ReadAllText(path);
            var mp = new MethodParameters();
            mp.Config = MethodConfigSerializer.Deserialize(json);
            return mp;
        }

        public string ToCppJson()
        {
            var c = Config;
            int targetMode;
            switch (c.PrecursorSelection.TargetingMode?.ToLower())
            {
                case "deep": targetMode = 3; break;
                case "exclusion": targetMode = 2; break;
                case "inclusion": targetMode = 1; break;
                default: targetMode = 0; break;
            }

            var ms2List = c.MsSettings.MS2 ?? new List<MS2Parameters>();

            var config = new JsonMethodConfig
            {
                deconvolution = new JsonDeconvolutionConfig
                {
                    score_threshold = c.Deconvolution.ScoreThreshold,
                    tqscore_threshold = c.Deconvolution.TQScoreThreshold,
                    min_charge = c.Deconvolution.MinCharge,
                    max_charge = c.Deconvolution.MaxCharge,
                    min_mass = c.Deconvolution.MinMass,
                    max_mass = c.Deconvolution.MaxMass,
                    tol = c.Deconvolution.Tolerances
                },
                precursor_selection = new JsonPrecursorSelectionConfig
                {
                    RT_window = c.PrecursorSelection.RTWindow,
                    target_mode = targetMode,
                    IDScore = c.PrecursorSelection.UseIDScore,
                    AllCharges = c.PrecursorSelection.ConsiderAllChargeStates,
                    MS3AllCharges = c.PrecursorSelection.MS3AllCharges,
                    HCDEnergy = c.PrecursorSelection.HCDEnergy,
                    strict_inclusion = c.PrecursorSelection.StrictInclusion,
                    tie_threshold = c.PrecursorSelection.TieThreshold
                },
                tagging = new JsonTaggingConfig
                {
                    min_tag_length = c.Tagging.MinTagLength,
                    max_tag_length = c.Tagging.MaxTagLength,
                    max_ptm_count = c.Tagging.MaxPtmCount,
                    max_flanking_mass_diff = c.Tagging.MaxFlankingMassDiff
                },
                quantification = new JsonQuantificationConfig
                {
                    enabled = c.Quantification.Active,
                    reporter_mz_tol = c.Quantification.ReporterMZTol,
                    fold_change_threshold = c.Quantification.FoldChangeThreshold
                },
                faims = new JsonFaimsConfig
                {
                    cv_values = c.Faims.CVValues,
                    max_cv_skip = c.Faims.MaxCVSkip,
                    cv_precursor_threshold = c.Faims.MassThreshold
                },
                ms_settings = new JsonMsSettingsConfig
                {
                    ms1 = new JsonMs1Config
                    {
                        analyzer = c.MsSettings.MS1.Analyzer ?? "",
                        first_mass = c.MsSettings.MS1.FirstMass,
                        last_mass = c.MsSettings.MS1.LastMass,
                        resolution = c.MsSettings.MS1.OrbitrapResolution,
                        agc_target = c.MsSettings.MS1.AGCTarget,
                        max_it = c.MsSettings.MS1.MaxIT
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
                    cycle_time = new JsonCycleTimeConfig
                    {
                        enabled = c.Scheduling.CycleTimeEnabled,
                        value_ms = c.Scheduling.CycleTimeMs
                    },
                    scan_timeout = new JsonScanTimeoutConfig
                    {
                        enabled = c.Scheduling.TimeoutEnabled,
                        value_ms = c.Scheduling.TimeoutMs
                    },
                    agc_interval_seconds = 30
                },
                exploration = new JsonExplorationConfig
                {
                    enabled = false,
                    max_depth = 1,
                    max_variants = 5
                },
                selection_strategy = BuildSelectionStrategy(),
                ms3 = new JsonMs3Config
                {
                    enabled = c.Ms3.Active,
                    mode = c.Ms3.Mode,
                    max_per_ms2 = c.Ms3.MaxPerMs2,
                    protein_sequence = c.Ms3.ProteinSequence ?? ""
                },
                conditional_ms2 = c.Tagging.ConditionalMS2,
                files = new JsonFilesConfig
                {
                    target_logs = (c.Files.TargetLogs ?? new List<string>()).ToArray(),
                    fasta = c.Files.FastaFile ?? "",
                    inclusion_list = c.Files.InclusionList ?? "",
                    ptm_list = c.Files.PtmList ?? ""
                }
            };

            return new JavaScriptSerializer().Serialize(config);
        }

        private JsonSelectionStrategyConfig BuildSelectionStrategy()
        {
            var ss = Config.SelectionStrategy;
            if (ss == null)
                throw new InvalidOperationException(
                    "Method config must contain selection_strategy block.");

            int ms1Max = ss.MS1?.MaxPrecursors ?? 10;
            int ms2Max = ss.MS2?.MaxFragments ?? 3;
            int ms3Max = ss.MS3?.MaxFragments ?? 3;

            var result = new JsonSelectionStrategyConfig
            {
                ms1 = new JsonMsLevelConfig
                {
                    selection = (ss.MS1?.Selection ?? "qscore").ToLower(),
                    max_precursors = ms1Max,
                    max_fragments = ms1Max
                },
                ms2 = new JsonMsLevelConfig
                {
                    selection = (ss.MS2?.Selection ?? "intensity").ToLower(),
                    max_precursors = ms2Max,
                    max_fragments = ms2Max
                },
                ms3 = new JsonMsLevelConfig
                {
                    selection = (ss.MS3?.Selection ?? "none").ToLower(),
                    max_precursors = ms3Max,
                    max_fragments = ms3Max
                }
            };

            var defaultExpl = new JsonExplorationBlockConfig
            {
                metric = "none", ce_min = 20, ce_max = 40, ce_step = 5, activation = "HCD"
            };
            result.ms1.exploration = defaultExpl;
            result.ms2.exploration = defaultExpl;
            result.ms3.exploration = defaultExpl;

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

        public string ToLogString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("--- Method Parameters ---");
            var c = Config;
            sb.AppendFormat("Global: Duration={0}min\n", c.Global.Duration);
            sb.AppendFormat("Deconv: QScore>={0}, TQScore>={1}, Charge=[{2},{3}], Mass=[{4},{5}], Tol=[{6}]\n",
                c.Deconvolution.ScoreThreshold, c.Deconvolution.TQScoreThreshold,
                c.Deconvolution.MinCharge, c.Deconvolution.MaxCharge,
                c.Deconvolution.MinMass, c.Deconvolution.MaxMass,
                String.Join(",", c.Deconvolution.Tolerances));
            sb.AppendFormat("Precursor: RTWindow={0}s, TargetMode={1}\n",
                c.PrecursorSelection.RTWindow, c.PrecursorSelection.TargetingMode);
            sb.AppendFormat("Inclusion: Strict={0}, TieThreshold={1}\n",
                c.PrecursorSelection.StrictInclusion, c.PrecursorSelection.TieThreshold);
            if (c.Tagging.Active)
                sb.AppendFormat("Tagging: ConditionalMS2={0}, Tags=[{1},{2}], MaxPtm={3}\n",
                    c.Tagging.ConditionalMS2, c.Tagging.MinTagLength, c.Tagging.MaxTagLength, c.Tagging.MaxPtmCount);
            else
                sb.AppendLine("Tagging: Off");
            if (c.Quantification.Active)
                sb.AppendFormat("Quant: MZTol={0}, FoldChange={1}\n",
                    c.Quantification.ReporterMZTol, c.Quantification.FoldChangeThreshold);
            else
                sb.AppendLine("Quant: Off");
            if (c.Ms3.Active)
                sb.AppendFormat("MS3: Mode={0}, MaxPerMS2={1}, AllCharges={2}\n",
                    c.Ms3.Mode, c.Ms3.MaxPerMs2, c.Ms3.AllCharges);
            else
                sb.AppendLine("MS3: Off");
            sb.AppendFormat("Developer: IDScore={0}, AllCharges={1}, HCDEnergy={2}, MaxCVSkip={3}\n",
                c.PrecursorSelection.UseIDScore, c.PrecursorSelection.ConsiderAllChargeStates,
                c.PrecursorSelection.HCDEnergy, c.Faims.MaxCVSkip);
            sb.AppendFormat("FAIMS: CV=[{0}]\n", String.Join(",", c.Faims.CVValues));
            var ms1 = c.MsSettings.MS1;
            sb.AppendFormat("MS1: {0} {1}k, mz=[{2},{3}], AGC={4}, MaxIT={5}ms\n",
                ms1.Analyzer, ms1.OrbitrapResolution / 1000, ms1.FirstMass, ms1.LastMass,
                ms1.AGCTarget, ms1.MaxIT);
            var ms2List = c.MsSettings.MS2 ?? new List<MS2Parameters>();
            for (int i = 0; i < ms2List.Count; i++)
            {
                var m = ms2List[i];
                var activation = m.Activation ?? "";
                if (activation.Equals("ETD", StringComparison.OrdinalIgnoreCase))
                    sb.AppendFormat("MS2[{0}]: {1} {2}k, {3} RT={4}ms\n",
                        i, m.Analyzer, m.OrbitrapResolution / 1000, activation, m.ReactionTime);
                else
                    sb.AppendFormat("MS2[{0}]: {1} {2}k, {3} CE={4}\n",
                        i, m.Analyzer, m.OrbitrapResolution / 1000, activation, m.CollisionEnergy);
            }
            return sb.ToString().TrimEnd();
        }
    }
}

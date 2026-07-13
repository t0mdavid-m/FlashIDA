using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Flash.IDA;

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
        [JsonKey("analyzer")] public string Analyzer;
        [JsonKey("first_mass")] public double FirstMass;
        [JsonKey("last_mass")] public double LastMass;
        [JsonKey("resolution")] public int OrbitrapResolution;
        [JsonKey("agc_target")] public int AGCTarget;
        [JsonKey("max_it")] public double MaxIT;
        [JsonKey("microscans")] public int Microscans;
        [JsonKey("data_type")] public string DataType;
        [JsonKey("rf_lens")] public double RFLens;
        [JsonKey("source_cid")] public double SourceCID;
        // Should be zero
        [JsonKey("source_cid_scaling")] public double SourceCIDScaling;
    }

    /// <summary>
    /// MS2 acquisition parameters
    /// </summary>
    /// <remarks>
    /// Naming of properties is alligned with Thermo instrument API and should not be changed
    /// </remarks>
    public struct MS2Parameters
    {
        [JsonKey("analyzer")] public string Analyzer;
        [JsonKey("first_mass")] public double FirstMass;
        [JsonKey("last_mass")] public double LastMass;
        [JsonKey("resolution")] public int OrbitrapResolution;
        [JsonKey("agc_target")] public int AGCTarget;
        [JsonKey("max_it")] public double MaxIT;
        [JsonKey("microscans")] public int Microscans;
        [JsonKey("data_type")] public string DataType;
        [JsonKey("activation")] public string Activation;
        [JsonKey("reaction_time")] public double ReactionTime;
        [JsonKey("reagent_max_it")] public double ReagentMaxIT;
        [JsonKey("reagent_agc_target")] public int ReagentAGCTarget;
        [JsonKey("collision_energy")] public int CollisionEnergy;
    }

    /// <summary>
    /// MS3 acquisition parameters
    /// </summary>
    /// <remarks>
    /// Naming of properties is alligned with Thermo instrument API and should not be changed
    /// </remarks>
    public struct MS3Parameters
    {
        [JsonKey("analyzer")] public string Analyzer;
        [JsonKey("first_mass")] public double FirstMass;
        [JsonKey("last_mass")] public double LastMass;
        [JsonKey("resolution")] public int OrbitrapResolution;
        [JsonKey("agc_target")] public int AGCTarget;
        [JsonKey("max_it")] public double MaxIT;
        [JsonKey("microscans")] public int Microscans;
        [JsonKey("data_type")] public string DataType;
        [JsonKey("activation")] public string Activation;
        [JsonKey("reaction_time")] public double ReactionTime;
        [JsonKey("reagent_max_it")] public double ReagentMaxIT;
        [JsonKey("reagent_agc_target")] public int ReagentAGCTarget;
        [JsonKey("collision_energy")] public int CollisionEnergy;
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
            var ms2List = c.MsSettings.MS2 ?? new List<MS2Parameters>();

            var config = new JsonMethodConfig
            {
                global = new JsonGlobalConfig
                {
                    method_name = c.Global.MethodName ?? "",
                    method_description = c.Global.MethodDescription ?? "",
                    duration = c.Global.Duration
                },
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
                    target_mode = c.PrecursorSelection.TargetMode,
                    AllCharges = c.PrecursorSelection.ConsiderAllChargeStates,
                    HCDEnergy = c.PrecursorSelection.HCDEnergy,
                    strict_inclusion = c.PrecursorSelection.StrictInclusion,
                    tie_threshold = c.PrecursorSelection.TieThreshold,
                    ChargeBasedExclusion = c.PrecursorSelection.ChargeBasedExclusion
                },
                flashtnt = new JsonFlashTnTConfig
                {
                    min_length = c.FlashTnT.MinLength,
                    max_length = c.FlashTnT.MaxLength,
                    max_ptm_count = c.FlashTnT.MaxPtmCount,
                    max_flanking_mass_diff = c.FlashTnT.MaxFlankingMassDiff,
                    allow_gap = c.FlashTnT.AllowGap,
                    max_aa_in_gap = c.FlashTnT.MaxAaInGap,
                    fixed_mod = (c.FlashTnT.FixedMod ?? new List<string>()).ToArray(),
                    max_blind_mod_count = c.FlashTnT.MaxBlindModCount,
                    max_mod_mass = c.FlashTnT.MaxModMass
                },
                tagging = new JsonTaggingConfig
                {
                    follow_up_scan = c.Tagging.FollowUpScan.HasValue ? new JsonMs2Config
                    {
                        analyzer = c.Tagging.FollowUpScan.Value.Analyzer ?? "",
                        activation = c.Tagging.FollowUpScan.Value.Activation ?? "",
                        collision_energy = c.Tagging.FollowUpScan.Value.CollisionEnergy,
                        resolution = c.Tagging.FollowUpScan.Value.OrbitrapResolution
                    } : null
                },
                quantification = new JsonQuantificationConfig
                {
                    enabled = c.Quantification.Active,
                    reporter_mz_tol = c.Quantification.ReporterMZTol,
                    fold_change_threshold = c.Quantification.FoldChangeThreshold,
                    follow_up_scan = c.Quantification.FollowUpScan.HasValue ? new JsonMs2Config
                    {
                        analyzer = c.Quantification.FollowUpScan.Value.Analyzer ?? "",
                        activation = c.Quantification.FollowUpScan.Value.Activation ?? "",
                        collision_energy = c.Quantification.FollowUpScan.Value.CollisionEnergy,
                        resolution = c.Quantification.FollowUpScan.Value.OrbitrapResolution
                    } : null
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
                        max_it = c.MsSettings.MS1.MaxIT,
                        microscans = c.MsSettings.MS1.Microscans,
                        rf_lens = c.MsSettings.MS1.RFLens,
                        source_cid = c.MsSettings.MS1.SourceCID,
                        source_cid_scaling = c.MsSettings.MS1.SourceCIDScaling,
                        data_type = c.MsSettings.MS1.DataType ?? ""
                    },
                    ms2 = ms2List.Select(m => new JsonMs2Config
                    {
                        analyzer = m.Analyzer ?? "",
                        activation = m.Activation ?? "",
                        collision_energy = m.CollisionEnergy,
                        resolution = m.OrbitrapResolution,
                        agc_target = m.AGCTarget,
                        max_it = m.MaxIT,
                        first_mass = m.FirstMass,
                        last_mass = m.LastMass,
                        microscans = m.Microscans,
                        data_type = m.DataType ?? "",
                        reaction_time = m.ReactionTime,
                        reagent_max_it = m.ReagentMaxIT,
                        reagent_agc_target = m.ReagentAGCTarget
                    }).ToArray(),
                    ms3 = c.MsSettings.MS3.Select(m => new JsonMs2Config
                    {
                        analyzer = m.Analyzer ?? "",
                        activation = m.Activation ?? "",
                        collision_energy = m.CollisionEnergy,
                        resolution = m.OrbitrapResolution,
                        agc_target = m.AGCTarget,
                        max_it = m.MaxIT,
                        first_mass = m.FirstMass,
                        last_mass = m.LastMass,
                        microscans = m.Microscans,
                        data_type = m.DataType ?? "",
                        reaction_time = m.ReactionTime,
                        reagent_max_it = m.ReagentMaxIT,
                        reagent_agc_target = m.ReagentAGCTarget
                    }).ToArray()
                },
                scheduling = new JsonSchedulingConfig
                {
                    cycle_time = new JsonCycleTimeConfig
                    {
                        enabled = c.Scheduling.CycleTime.Enabled,
                        value_ms = c.Scheduling.CycleTime.ValueMs
                    },
                    scan_timeout = new JsonScanTimeoutConfig
                    {
                        enabled = c.Scheduling.ScanTimeout.Enabled,
                        value_ms = c.Scheduling.ScanTimeout.ValueMs
                    },
                    agc_interval_seconds = c.Scheduling.AgcIntervalSeconds
                },
                selection_strategy = BuildSelectionStrategy(),
                characterization = new JsonCharacterizationConfig
                {
                    objective = (c.Characterization.Objective ?? "ambiguity").ToLower(),
                    protein_sequence = c.Characterization.ProteinSequence ?? "",
                    ms3_all_charges = c.Characterization.MS3AllCharges
                },
                conditional_ms2 = c.Tagging.ConditionalMS2,
                files = new JsonFilesConfig
                {
                    target_logs = (c.Files.TargetLogs ?? new List<string>()).ToArray(),
                    fasta = c.Files.FastaFile ?? "",
                    inclusion_list = c.Files.InclusionList ?? "",
                    ptm_list = c.Files.PtmList ?? ""
                },
                runtime = new JsonRuntimeConfig
                {
                    ida_log_path = c.Runtime.IdaLogPath ?? "",
                    scan_commands_path = c.Runtime.ScanCommandsPath ?? "",
                    scan_results_path = c.Runtime.ScanResultsPath ?? "",
                    identification_log_path = c.Runtime.IdentificationLogPath ?? "",
                    pooled_identification_log_path = c.Runtime.PooledIdentificationLogPath ?? ""
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

            int ms1Max = ss.MS1?.MaxTargets ?? 10;
            int ms2Max = ss.MS2?.MaxTargets ?? 3;
            int ms3Max = ss.MS3?.MaxTargets ?? 3;

            var result = new JsonSelectionStrategyConfig
            {
                ms1 = new JsonMsLevelConfig
                {
                    selection = (ss.MS1?.Selection ?? "qscore").ToLower(),
                    max_targets = ms1Max,
                    min_charge = ss.MS1?.MinCharge ?? 0
                },
                ms2 = new JsonMsLevelConfig
                {
                    selection = (ss.MS2?.Selection ?? "intensity").ToLower(),
                    max_targets = ms2Max,
                    min_charge = ss.MS2?.MinCharge ?? 0
                },
                ms3 = new JsonMsLevelConfig
                {
                    selection = (ss.MS3?.Selection ?? "none").ToLower(),
                    max_targets = ms3Max,
                    min_charge = ss.MS3?.MinCharge ?? 0
                }
            };

            var defaultExpl = new JsonExplorationBlockConfig
            {
                metric = "none", ce_min = 20, ce_max = 40, ce_step = 5,
                overrides = null, remaining_precursor_target = 0.1,
                rt_min = 0, rt_max = 0, rt_step = 1, activations = null
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
                    overrides = ss.MS2.Exploration.Overrides,
                    remaining_precursor_target = ss.MS2.Exploration.RemainingPrecursorTarget,
                    rt_min = ss.MS2.Exploration.RTMin,
                    rt_max = ss.MS2.Exploration.RTMax,
                    rt_step = ss.MS2.Exploration.RTStep,
                    activations = ss.MS2.Exploration.Activations
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
                    overrides = ss.MS3.Exploration.Overrides,
                    remaining_precursor_target = ss.MS3.Exploration.RemainingPrecursorTarget,
                    rt_min = ss.MS3.Exploration.RTMin,
                    rt_max = ss.MS3.Exploration.RTMax,
                    rt_step = ss.MS3.Exploration.RTStep,
                    activations = ss.MS3.Exploration.Activations
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
                c.PrecursorSelection.RTWindow, c.PrecursorSelection.TargetMode);
            sb.AppendFormat("Inclusion: Strict={0}, TieThreshold={1}\n",
                c.PrecursorSelection.StrictInclusion, c.PrecursorSelection.TieThreshold);
            if (c.Tagging.Active)
                sb.AppendFormat("Tagging: ConditionalMS2={0}, Tags=[{1},{2}], MaxPtm={3}\n",
                    c.Tagging.ConditionalMS2, c.FlashTnT.MinLength, c.FlashTnT.MaxLength, c.FlashTnT.MaxPtmCount);
            else
                sb.AppendLine("Tagging: Off");
            if (c.Quantification.Active)
                sb.AppendFormat("Quant: MZTol={0}, FoldChange={1}\n",
                    c.Quantification.ReporterMZTol, c.Quantification.FoldChangeThreshold);
            else
                sb.AppendLine("Quant: Off");
            if (!string.IsNullOrEmpty(c.Characterization.ProteinSequence))
                sb.AppendFormat("MS3: ProteinSequence={0}\n",
                    c.Characterization.ProteinSequence.Substring(0, Math.Min(20, c.Characterization.ProteinSequence.Length)) + "...");
            else
                sb.AppendLine("MS3: No protein sequence");
            sb.AppendFormat("Developer: AllCharges={0}, HCDEnergy={1}, MaxCVSkip={2}\n",
                c.PrecursorSelection.ConsiderAllChargeStates,
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

        // ----------------------------------------------------------------
        // Self-generating full-schema reference
        // ----------------------------------------------------------------

        /// <summary>
        /// Produce the complete bridge-schema JSON — every section and key present at a
        /// representative value — by running <see cref="ToCppJson"/> over a fully-populated config.
        /// This is the single source of truth committed at
        /// FlashIDA/test-data/config_schema_reference.json; a staleness test asserts the committed
        /// file equals this output so the schema reference can never go stale.
        /// </summary>
        public static string GenerateReferenceConfigJson()
        {
            var mp = new MethodParameters { Config = BuildFullReferenceConfig() };
            return PrettyPrintJson(mp.ToCppJson());
        }

        /// <summary>
        /// A fully-populated config whose emitted JSON exercises every key and passes C++ validate():
        /// tol covers MS3; MSn>=2 selection has a protein sequence; MS2 exploration has one scan and a
        /// valid CE sweep; conditional_ms2 is off.
        /// </summary>
        private static MethodConfig BuildFullReferenceConfig()
        {
            var c = new MethodConfig();

            c.Global.MethodName = "SchemaReference";
            c.Global.MethodDescription =
                "Full config: every key at a representative value; regenerated by GenerateReferenceConfigJson.";
            c.Global.Duration = 90;

            c.Deconvolution.ScoreThreshold = 0.11;
            c.Deconvolution.TQScoreThreshold = 0.93;
            c.Deconvolution.MinCharge = 4;
            c.Deconvolution.MaxCharge = 47;
            c.Deconvolution.MinMass = 511;
            c.Deconvolution.MaxMass = 49001;
            c.Deconvolution.Tolerances = new double[] { 11, 12, 13 };

            c.PrecursorSelection.RTWindow = 181;
            c.PrecursorSelection.TargetMode = 1;
            c.PrecursorSelection.ConsiderAllChargeStates = true;
            c.PrecursorSelection.HCDEnergy = 27;
            c.PrecursorSelection.StrictInclusion = true;
            c.PrecursorSelection.TieThreshold = 0.13;
            c.PrecursorSelection.ChargeBasedExclusion = true;

            c.FlashTnT.MinLength = 4;
            c.FlashTnT.MaxLength = 9;
            c.FlashTnT.MaxPtmCount = 5;
            c.FlashTnT.MaxFlankingMassDiff = 41001;
            c.FlashTnT.AllowGap = true;
            c.FlashTnT.MaxAaInGap = 3;
            c.FlashTnT.FixedMod = new List<string> { "Carbamidomethyl (C)" };
            c.FlashTnT.MaxBlindModCount = 1;
            c.FlashTnT.MaxModMass = 733;

            c.Tagging.ConditionalMS2 = false;
            c.Tagging.FollowUpScan = new MS2Parameters
            {
                Analyzer = "Orbitrap", Activation = "ETD", CollisionEnergy = 24, OrbitrapResolution = 15002
            };

            c.Quantification.Active = false;
            c.Quantification.ReporterMZTol = 0.0031;
            c.Quantification.FoldChangeThreshold = 1.7;
            c.Quantification.FollowUpScan = new MS2Parameters
            {
                Analyzer = "Orbitrap", Activation = "HCD", CollisionEnergy = 28, OrbitrapResolution = 15003
            };

            c.Faims.CVValues = new double[] { -41, -52, -63 };
            c.Faims.MaxCVSkip = 2;
            c.Faims.MassThreshold = 17;

            c.MsSettings.MS1 = new MS1Parameters
            {
                Analyzer = "Orbitrap", FirstMass = 501, LastMass = 2001, OrbitrapResolution = 120001,
                AGCTarget = 800001, MaxIT = 247, Microscans = 2, DataType = "Centroid",
                RFLens = 31, SourceCID = 16, SourceCIDScaling = 0
            };
            c.MsSettings.MS2 = new List<MS2Parameters>
            {
                new MS2Parameters
                {
                    Analyzer = "Orbitrap", Activation = "HCD", CollisionEnergy = 29, OrbitrapResolution = 120002,
                    AGCTarget = 500001, MaxIT = 101, FirstMass = 101, LastMass = 2002, Microscans = 3,
                    DataType = "Centroid", ReactionTime = 0, ReagentMaxIT = 0, ReagentAGCTarget = 0
                }
            };
            c.MsSettings.MS3 = new List<MS3Parameters>
            {
                new MS3Parameters
                {
                    Analyzer = "Orbitrap", Activation = "CID", CollisionEnergy = 26, OrbitrapResolution = 240001,
                    AGCTarget = 5000001, MaxIT = 501, FirstMass = 201, LastMass = 2003, Microscans = 8,
                    DataType = "Centroid", ReactionTime = 0, ReagentMaxIT = 0, ReagentAGCTarget = 0
                }
            };

            c.Scheduling.CycleTime.Enabled = true;
            c.Scheduling.CycleTime.ValueMs = 60001;
            c.Scheduling.ScanTimeout.Enabled = true;
            c.Scheduling.ScanTimeout.ValueMs = 30001;
            c.Scheduling.AgcIntervalSeconds = 29;

            c.SelectionStrategy.MS1 = new MS1SelectionConfig { Selection = "qscore", MaxTargets = 3, MinCharge = 2 };
            c.SelectionStrategy.MS2 = new MS2SelectionConfig
            {
                Selection = "intensity", MaxTargets = 4, MinCharge = 1,
                Exploration = new ExplorationBlockConfig
                {
                    Metric = "mass_count", CEMin = 21, CEMax = 39, CEStep = 5,
                    RemainingPrecursorTarget = 0.12, RTMin = 0, RTMax = 0, RTStep = 1
                }
            };
            c.SelectionStrategy.MS3 = new MS3SelectionConfig { Selection = "none", MaxTargets = 3, MinCharge = 0 };

            c.Characterization.Objective = "coverage";
            c.Characterization.ProteinSequence = "MSENTINELPEPTIDESEQ";
            c.Characterization.MS3AllCharges = true;

            return c;
        }

        /// <summary>Re-indent a compact JSON string by walking its object graph (no JSON-syntax parsing).</summary>
        private static string PrettyPrintJson(string compactJson)
        {
            object graph = new JavaScriptSerializer().DeserializeObject(compactJson);
            var sb = new StringBuilder();
            WriteJsonNode(graph, sb, 0);
            sb.Append("\n");
            return sb.ToString();
        }

        private static void WriteJsonNode(object node, StringBuilder sb, int indent)
        {
            string pad = new string(' ', indent * 2);
            string pad1 = new string(' ', (indent + 1) * 2);

            if (node is Dictionary<string, object> dict)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                int i = 0;
                foreach (var kv in dict)
                {
                    sb.Append(pad1).Append('"').Append(EscapeJson(kv.Key)).Append("\": ");
                    WriteJsonNode(kv.Value, sb, indent + 1);
                    sb.Append(++i < dict.Count ? ",\n" : "\n");
                }
                sb.Append(pad).Append("}");
                return;
            }
            if (node is object[] arr)
            {
                if (arr.Length == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (int j = 0; j < arr.Length; j++)
                {
                    sb.Append(pad1);
                    WriteJsonNode(arr[j], sb, indent + 1);
                    sb.Append(j + 1 < arr.Length ? ",\n" : "\n");
                }
                sb.Append(pad).Append("]");
                return;
            }
            if (node == null) { sb.Append("null"); return; }
            if (node is string s) { sb.Append('"').Append(EscapeJson(s)).Append('"'); return; }
            if (node is bool b) { sb.Append(b ? "true" : "false"); return; }
            sb.Append(Convert.ToString(node, System.Globalization.CultureInfo.InvariantCulture));
        }

        private static string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }
    }
}

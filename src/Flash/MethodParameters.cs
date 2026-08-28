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
        [JsonKey("scan_rate")] public string ScanRate;
        // Source-region parameters (ADR-0011): upstream of the analyzer, so they determine WHICH
        // ions arrive rather than how they are measured. Shared by every scan in the cycle -- MSn
        // inherits these from the survey when it does not state its own. Emitted unconditionally,
        // including 0, because 0 is a meaningful setting for source_cid_scaling rather than "unset".
        [JsonKey("rf_lens")] public double RFLens;
        [JsonKey("source_cid")] public double SourceCID;
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
        [JsonKey("scan_rate")] public string ScanRate;
        [JsonKey("activation")] public string Activation;
        [JsonKey("reaction_time")] public double ReactionTime;
        [JsonKey("reagent_max_it")] public double ReagentMaxIT;
        [JsonKey("reagent_agc_target")] public int ReagentAGCTarget;
        [JsonKey("collision_energy")] public int CollisionEnergy;
        // Source-region parameters (ADR-0011). 0 means "inherit the survey's value", resolved in
        // ToJsonScanConfig before the config crosses the bridge -- so the ScanConfig C++ receives is
        // still fully determined and ADR-0009 holds unchanged.
        [JsonKey("rf_lens")] public double RFLens;
        [JsonKey("source_cid")] public double SourceCID;
        [JsonKey("source_cid_scaling")] public double SourceCIDScaling;
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
        [JsonKey("scan_rate")] public string ScanRate;
        [JsonKey("activation")] public string Activation;
        [JsonKey("reaction_time")] public double ReactionTime;
        [JsonKey("reagent_max_it")] public double ReagentMaxIT;
        [JsonKey("reagent_agc_target")] public int ReagentAGCTarget;
        [JsonKey("collision_energy")] public int CollisionEnergy;
        // Source-region parameters (ADR-0011) -- identical to MS2Parameters by construction; both
        // structs funnel through the same JsonMs2Config, so they must not be allowed to drift.
        [JsonKey("rf_lens")] public double RFLens;
        [JsonKey("source_cid")] public double SourceCID;
        [JsonKey("source_cid_scaling")] public double SourceCIDScaling;
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
                    rt_window = c.PrecursorSelection.RTWindow,
                    targeting = (c.PrecursorSelection.Targeting ?? "none").ToLower(),
                    consider_all_charges = c.PrecursorSelection.ConsiderAllChargeStates,
                    strict_inclusion = c.PrecursorSelection.StrictInclusion,
                    tie_threshold = c.PrecursorSelection.TieThreshold,
                    precursor_charges = c.PrecursorSelection.PrecursorCharges,
                    rank_by = (c.PrecursorSelection.RankBy ?? "qscore").ToLower(),
                    max_precursors = c.PrecursorSelection.MaxPrecursors,
                    min_precursor_charge = c.PrecursorSelection.MinPrecursorCharge,
                    additional_scans = (c.PrecursorSelection.AdditionalScans ?? new List<string>()).ToArray(),
                    exploration = ToJsonExploration(c.PrecursorSelection.Exploration),
                    // Always emitted: TagExpansion is initialised on the model, so a config that omits
                    // the block still crosses the bridge carrying the defaults (3 / 50000).
                    tag_expansion = new JsonTagExpansionConfig
                    {
                        max_ptm_count = c.PrecursorSelection.TagExpansion.MaxPtmCount,
                        max_flanking_mass_diff = c.PrecursorSelection.TagExpansion.MaxFlankingMassDiff
                    }
                },
                flashtnt = new JsonFlashTnTConfig
                {
                    min_length = c.FlashTnT.MinLength,
                    max_length = c.FlashTnT.MaxLength,
                    allow_gap = c.FlashTnT.AllowGap,
                    max_aa_in_gap = c.FlashTnT.MaxAaInGap,
                    fixed_mod = (c.FlashTnT.FixedMod ?? new List<string>()).ToArray(),
                    max_blind_mod_count = c.FlashTnT.MaxBlindModCount,
                    max_mod_mass = c.FlashTnT.MaxModMass
                },
                tagging = new JsonTaggingConfig
                {
                    // A name, not a block. Empty means "no conditional follow-up configured", which
                    // is what Config::validate() now checks conditional_ms2 against.
                    follow_up_scan = string.IsNullOrEmpty(c.Tagging.FollowUpScan)
                        ? null : c.Tagging.FollowUpScan
                },
                quantification = new JsonQuantificationConfig
                {
                    enabled = c.Quantification.Active,
                    labelling = c.Quantification.Labelling,
                    reporter_mz_tol = c.Quantification.ReporterMZTol,
                    fold_change_threshold = c.Quantification.FoldChangeThreshold,
                    // Null (not an empty list) when unauthored, so SerializeValue skips the key and
                    // the 40 configs that never quantify emit exactly what they emit today. C++
                    // requires conditions only when enabled, so an absent key is legal there.
                    conditions = (c.Quantification.Conditions == null || c.Quantification.Conditions.Count == 0)
                        ? null
                        : c.Quantification.Conditions.ConvertAll(cond => new JsonQuantCondition
                          {
                              name = cond.Name,
                              channels = cond.Channels
                          }),
                    correction_matrix = (c.Quantification.CorrectionMatrix == null || c.Quantification.CorrectionMatrix.Count == 0)
                        ? null : c.Quantification.CorrectionMatrix
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
                        data_type = c.MsSettings.MS1.DataType ?? "",
                        scan_rate = c.MsSettings.MS1.ScanRate ?? ""
                    },
                    ms2 = ToJsonScanConfig(c.MsSettings.MS2, c.MsSettings.MS1),
                    // Null when unset -> key omitted, so nothing changes for the 40 configs that
                    // have no quantification scan. Source-region parameters inherit from the survey
                    // through the same ToJsonScanConfig path as every other scan (ADR-0011).
                    ms2_quant = c.MsSettings.MS2Quant.HasValue
                        ? ToJsonScanConfig(c.MsSettings.MS2Quant.Value, c.MsSettings.MS1) : null,
                    ms3 = ToJsonScanConfig(c.MsSettings.MS3, c.MsSettings.MS1),
                    // Emit nothing at all when there are no extras, so the 30 configs without any
                    // stay byte-for-byte the length they are today.
                    additional_ms2 = (c.MsSettings.AdditionalMS2 == null || c.MsSettings.AdditionalMS2.Count == 0)
                        ? null
                        : c.MsSettings.AdditionalMS2.ToDictionary(
                              kv => kv.Key,
                              kv => ToJsonScanConfig(kv.Value, c.MsSettings.MS1))
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
                    agc_interval_seconds = c.Scheduling.AgcIntervalSeconds,
                    target_depth = c.Scheduling.TargetDepth
                },
                characterization = new JsonCharacterizationConfig
                {
                    mode = (c.Characterization.Mode ?? "off").ToLower(),
                    protein_sequence = c.Characterization.ProteinSequence ?? "",
                    max_targets = c.Characterization.MaxTargets,
                    min_fragment_charge = c.Characterization.MinFragmentCharge,
                    min_target_mass = c.Characterization.MinTargetMass,
                    fragment_charges = c.Characterization.FragmentCharges,
                    exploration = ToJsonExploration(c.Characterization.Exploration)
                },
                conditional_ms2 = c.Tagging.ConditionalMS2,
                files = new JsonFilesConfig
                {
                    target_logs = (c.Files.TargetLogs ?? new List<string>()).ToArray(),
                    fasta = c.Files.FastaFile ?? "",
                    inclusion_list = c.Files.InclusionList ?? "",
                    ptm_list = c.Files.PtmList ?? ""
                },
                // PURE PASSTHROUGH -- do not resolve here. No Path.GetFullPath, no "" -> ".", no
                // timestamp. This method is the body of GenerateReferenceConfigJson (see below), so
                // anything clock- or CWD-derived would make config_schema_reference.json differ on
                // every run and ConfigSchemaParityTests.Reference_IsNeverStale would fail
                // permanently -- regenerating it would not help, because the regenerated file goes
                // stale the moment it is written. Resolution belongs to LogPathResolver, called
                // from the two Main methods and nowhere else.
                runtime = new JsonRuntimeConfig
                {
                    log_dir = c.Runtime.LogDir ?? ""
                }
            };

            return new JavaScriptSerializer().Serialize(config);
        }

        /// <summary>
        /// Convert one exploration block for the wire. Replaces BuildSelectionStrategy(), which
        /// synthesized a whole section; each of the two surviving blocks now converts itself.
        ///
        /// Value-preserving by construction, including the quirk that an absent block and a block
        /// with metric "none" both emit the same 20/40/5 placeholder — that is what the engine has
        /// always received, and C++ ignores every field of it once metric is None.
        ///
        /// Fixes one latent defect while preserving behaviour: the old code built ONE defaultExpl
        /// object and assigned the same reference to all three levels. Benign only because nothing
        /// mutated it; any future per-level fixup would have silently hit all three. This returns a
        /// fresh instance per call.
        /// </summary>
        private static JsonExplorationBlockConfig ToJsonExploration(ExplorationBlockConfig e)
        {
            // NOTE the comparison is deliberately ordinal, matching the old behaviour exactly:
            // "None" with a capital N took the OTHER branch and forwarded the user's sweep values.
            // Rejecting bad casing is the C++ validator's job now (it throws rather than guessing),
            // so this stays a pure value-preserving transform.
            if (e == null || e.Metric == null || e.Metric == "none")
            {
                return new JsonExplorationBlockConfig
                {
                    metric = "none", ce_min = 20, ce_max = 40, ce_step = 5,
                    overrides = null, remaining_precursor_target = 0.1,
                    reaction_time_min = 0, reaction_time_max = 0, reaction_time_step = 1,
                    activations = null, tolerance_ppm = 0
                };
            }

            return new JsonExplorationBlockConfig
            {
                metric = e.Metric.ToLower(),
                ce_min = e.CEMin,
                ce_max = e.CEMax,
                ce_step = e.CEStep,
                overrides = e.Overrides,
                remaining_precursor_target = e.RemainingPrecursorTarget,
                reaction_time_min = e.ReactionTimeMin,
                reaction_time_max = e.ReactionTimeMax,
                reaction_time_step = e.ReactionTimeStep,
                activations = e.Activations,
                tolerance_ppm = e.TolerancePpm
            };
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
            sb.AppendFormat("Precursor: RTWindow={0}s, Targeting={1}, MaxPrecursors={2}, RankBy={3}\n",
                c.PrecursorSelection.RTWindow, c.PrecursorSelection.Targeting,
                c.PrecursorSelection.MaxPrecursors, c.PrecursorSelection.RankBy);
            sb.AppendFormat("Inclusion: Strict={0}, TieThreshold={1}\n",
                c.PrecursorSelection.StrictInclusion, c.PrecursorSelection.TieThreshold);
            if (c.Tagging.Active)
                sb.AppendFormat("Tagging: ConditionalMS2={0}, Tags=[{1},{2}], MaxPtm={3}\n",
                    c.Tagging.ConditionalMS2, c.FlashTnT.MinLength, c.FlashTnT.MaxLength,
                    c.PrecursorSelection.TagExpansion.MaxPtmCount);
            else
                sb.AppendLine("Tagging: Off");
            if (c.Quantification.Active)
                sb.AppendFormat("Quant: MZTol={0}, FoldChange={1}\n",
                    c.Quantification.ReporterMZTol, c.Quantification.FoldChangeThreshold);
            else
                sb.AppendLine("Quant: Off");
            if (string.Equals(c.Characterization.Mode, "off", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine("MS3: Off");
            else
                sb.AppendFormat("MS3: mode={0}, budget={1}, ProteinSequence={2}\n",
                    c.Characterization.Mode, c.Characterization.MaxTargets,
                    string.IsNullOrEmpty(c.Characterization.ProteinSequence)
                        ? "(none)"
                        : c.Characterization.ProteinSequence.Substring(
                              0, Math.Min(20, c.Characterization.ProteinSequence.Length)) + "...");
            sb.AppendFormat("Developer: AllCharges={0}, MaxCVSkip={1}\n",
                c.PrecursorSelection.ConsiderAllChargeStates, c.Faims.MaxCVSkip);
            sb.AppendFormat("FAIMS: CV=[{0}]\n", String.Join(",", c.Faims.CVValues));
            //Both of these decide how much of the instrument's time FLASHIda actually gets, and
            //neither was reported anywhere before -- so the 2026-08-25 run's logs could not be read
            //back to the settings that produced them. Cheap to print, expensive to reconstruct.
            sb.AppendFormat("Scheduling: TargetDepth={0}, AGCInterval={1}s\n",
                c.Scheduling.TargetDepth, c.Scheduling.AgcIntervalSeconds);
            var ms1 = c.MsSettings.MS1;
            sb.AppendFormat("MS1: {0} {1}k, mz=[{2},{3}], AGC={4}, MaxIT={5}ms\n",
                ms1.Analyzer, ms1.OrbitrapResolution / 1000, ms1.FirstMass, ms1.LastMass,
                ms1.AGCTarget, ms1.MaxIT);
            AppendMs2Line(sb, "MS2", c.MsSettings.MS2);
            if (c.MsSettings.AdditionalMS2 != null)
                foreach (var kv in c.MsSettings.AdditionalMS2)
                    AppendMs2Line(sb, "MS2[" + kv.Key + "]", kv.Value);
            return sb.ToString().TrimEnd();
        }

        private static void AppendMs2Line(System.Text.StringBuilder sb, string label, MS2Parameters m)
        {
            var activation = m.Activation ?? "";
            if (activation.Equals("ETD", StringComparison.OrdinalIgnoreCase))
                sb.AppendFormat("{0}: {1} {2}k, {3} RT={4}ms\n",
                    label, m.Analyzer, m.OrbitrapResolution / 1000, activation, m.ReactionTime);
            else
                sb.AppendFormat("{0}: {1} {2}k, {3} CE={4}\n",
                    label, m.Analyzer, m.OrbitrapResolution / 1000, activation, m.CollisionEnergy);
        }

        // ----------------------------------------------------------------
        /// <summary>
        /// Map one scan config to its bridge-JSON form. A scan config means the same thing at every
        /// site it appears (ms_settings.ms2/ms3, tagging.follow_up_scan, quantification.follow_up_scan)
        /// and must therefore be emitted identically at all of them — ADR-0009.
        /// </summary>
        /// <remarks>
        /// Two overloads because MS2Parameters and MS3Parameters are distinct structs with identical
        /// field sets. Emitting a subset here is not a shortcut: a key this method omits never
        /// crosses the bridge and is unreachable from method.json, which is how follow-up scans
        /// became unable to carry their own reaction_time, and how every MSn scan lost
        /// rf_lens/source_cid/source_cid_scaling/scan_rate.
        ///
        /// <para><b>Source-region inheritance (ADR-0011).</b> rf_lens, source_cid and
        /// source_cid_scaling describe the ion source, not this scan's analyzer, so an MSn scan that
        /// does not state its own runs at the survey's — otherwise the MS1 that picked the precursor
        /// and the MSn that fragments it sample different ion populations. Zero means "inherit"
        /// (there is no separate absent state: ToCppJson emits every key unconditionally, so C++
        /// cannot distinguish absent from 0 and the resolution has to happen here).</para>
        ///
        /// <para>Resolving it at emit time is what keeps ADR-0009 intact: by the time the JSON
        /// crosses the bridge every ScanConfig carries a concrete value, so a scan config still
        /// fully determines its scan's instrument parameters and nothing downstream performs a
        /// cross-scan lookup. scan_rate is analyzer-side and deliberately does NOT inherit.</para>
        /// </remarks>
        private static JsonMs2Config ToJsonScanConfig(MS2Parameters m, MS1Parameters ms1)
        {
            return new JsonMs2Config
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
                scan_rate = m.ScanRate ?? "",
                rf_lens = m.RFLens != 0 ? m.RFLens : ms1.RFLens,
                source_cid = m.SourceCID != 0 ? m.SourceCID : ms1.SourceCID,
                source_cid_scaling = m.SourceCIDScaling != 0 ? m.SourceCIDScaling : ms1.SourceCIDScaling,
                reaction_time = m.ReactionTime,
                reagent_max_it = m.ReagentMaxIT,
                reagent_agc_target = m.ReagentAGCTarget
            };
        }

        private static JsonMs2Config ToJsonScanConfig(MS3Parameters m, MS1Parameters ms1)
        {
            return new JsonMs2Config
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
                scan_rate = m.ScanRate ?? "",
                rf_lens = m.RFLens != 0 ? m.RFLens : ms1.RFLens,
                source_cid = m.SourceCID != 0 ? m.SourceCID : ms1.SourceCID,
                source_cid_scaling = m.SourceCIDScaling != 0 ? m.SourceCIDScaling : ms1.SourceCIDScaling,
                reaction_time = m.ReactionTime,
                reagent_max_it = m.ReagentMaxIT,
                reagent_agc_target = m.ReagentAGCTarget
            };
        }

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
            c.PrecursorSelection.Targeting = "inclusion";
            c.PrecursorSelection.ConsiderAllChargeStates = true;
            c.PrecursorSelection.StrictInclusion = true;
            c.PrecursorSelection.TieThreshold = 0.13;
            c.PrecursorSelection.PrecursorCharges = "multiplexed";

            c.FlashTnT.MinLength = 4;
            c.FlashTnT.MaxLength = 9;
            c.FlashTnT.AllowGap = true;
            c.FlashTnT.MaxAaInGap = 3;
            c.FlashTnT.FixedMod = new List<string> { "Carbamidomethyl (C)" };
            c.FlashTnT.MaxBlindModCount = 1;
            c.FlashTnT.MaxModMass = 733;

            // The two follow-up scan BLOCKS now live in ms_settings.additional_ms2 (assigned below);
            // these sections carry only the NAME that references them.
            // ETD requires its activation-coupled reaction parameters or Config::validate() rejects
            // this reference (ADR-0009). Every key must also carry a distinct, non-default value so
            // the parity tests can tell a correctly-bound key from a coincidentally-equal default.
            c.Tagging.ConditionalMS2 = false;
            c.Tagging.FollowUpScan = "tagging_reference";

            // Active stays FALSE deliberately, and it is the one key in this section whose binding
            // the parity tests cannot verify (a binding hardwired to false would pass). Enabling it
            // here would invert the level-2 roster per ADR-0038, so cfg.level(2).scans[0] would
            // become ms2_quant and ConfigSchemaParity_test's ms_settings.ms2 block -- which asserts
            // all 17 keys against scans[0] -- would compare the wrong scan. The roster inversion is
            // behaviour, not schema, and is pinned in Config_SchemaProjection_test instead.
            c.Quantification.Active = false;
            c.Quantification.Labelling = "tmt10plex";   // non-default: the default is tmt6plex
            c.Quantification.ReporterMZTol = 0.0031;
            c.Quantification.FoldChangeThreshold = 1.7;
            // Channel names must be valid for the labelling above, or Config throws at load.
            // tmt10plex's N/C channels are used on purpose: they only exist in the 10-plex+ schemes,
            // so a reference that silently fell back to tmt6plex would fail here rather than pass.
            c.Quantification.Conditions = new List<QuantConditionConfig>
            {
                new QuantConditionConfig { Name = "reference_a", Channels = new List<string> { "126", "127N" } },
                new QuantConditionConfig { Name = "reference_b", Channels = new List<string> { "130C", "131" } }
            };
            c.Quantification.CorrectionMatrix = new List<string>
            {
                "0.0/0.0/1.1/0.0", "0.0/0.0/1.2/0.0", "0.0/0.3/1.3/0.0", "0.0/0.4/1.4/0.0",
                "0.5/0.0/1.5/0.0", "0.6/0.0/1.6/0.0", "0.7/0.0/1.7/0.0", "0.8/0.0/0.0/0.0",
                "0.9/0.0/0.0/0.0", "1.0/0.0/0.0/0.0"
            };

            c.Faims.CVValues = new double[] { -41, -52, -63 };
            c.Faims.MaxCVSkip = 2;
            c.Faims.MassThreshold = 17;

            c.MsSettings.MS1 = new MS1Parameters
            {
                Analyzer = "Orbitrap", FirstMass = 501, LastMass = 2001, OrbitrapResolution = 120001,
                AGCTarget = 800001, MaxIT = 247, Microscans = 2, DataType = "Centroid",
                ScanRate = "Turbo", RFLens = 31, SourceCID = 16, SourceCIDScaling = 0.11
            };
            c.MsSettings.MS2 = new MS2Parameters
            {
                Analyzer = "Orbitrap", Activation = "HCD", CollisionEnergy = 29, OrbitrapResolution = 120002,
                AGCTarget = 500001, MaxIT = 101, FirstMass = 101, LastMass = 2002, Microscans = 3,
                DataType = "Centroid", ReactionTime = 0, ReagentMaxIT = 0, ReagentAGCTarget = 0,
                ScanRate = "Rapid", RFLens = 32, SourceCID = 17, SourceCIDScaling = 0.12
            };
            c.MsSettings.MS3 = new MS3Parameters
            {
                Analyzer = "Orbitrap", Activation = "CID", CollisionEnergy = 26, OrbitrapResolution = 240001,
                AGCTarget = 5000001, MaxIT = 501, FirstMass = 201, LastMass = 2003, Microscans = 8,
                DataType = "Centroid", ReactionTime = 0, ReagentMaxIT = 0, ReagentAGCTarget = 0,
                ScanRate = "Normal", RFLens = 33, SourceCID = 18, SourceCIDScaling = 0.13
            };
            // ADR-0038: the quantification scan is a bare slot, so it belongs here beside ms2/ms3
            // rather than in additional_ms2. These are the values the retired "quant_reference"
            // entry carried, moved verbatim so the reference keeps asserting the same 17 keys.
            c.MsSettings.MS2Quant = new MS2Parameters
            {
                Analyzer = "Orbitrap", Activation = "HCD", CollisionEnergy = 28, OrbitrapResolution = 15003,
                AGCTarget = 400003, MaxIT = 103, FirstMass = 153, LastMass = 2003, Microscans = 3,
                DataType = "Profile", ReactionTime = 0, ReagentMaxIT = 0, ReagentAGCTarget = 0,
                ScanRate = "Enhanced", RFLens = 35, SourceCID = 20, SourceCIDScaling = 0.15
            };
            // Two named entries, one backing each follow-up reference, and BOTH referenced.
            //
            // There is deliberately no third "dispatched extra" here. It would have to be listed in
            // precursor_selection.additional_scans, and that is mutually exclusive with the live
            // precursor_selection.exploration below: a CE/RT sweep varies ONE base scan config, so
            // Config::validate() throws when an exploring level dispatches more than one. The
            // reference carried both for a while and was therefore an invalid config -- it emitted
            // fine and then failed to load in C++.
            //
            // The exploration keeps its distinctive values and additional_scans stays empty, rather
            // than the reverse, because the reference exists so a DROPPED key is detectable. With
            // metric "none" the emitter substitutes its own defaults, so an exploration block that
            // vanished entirely would re-emit byte-identical and Emit_And_Reload_PreserveEveryKey
            // would not notice. Populated additional_scans and roster order are covered instead by
            // Config_SchemaProjection_test::scan_name_resolution.
            c.MsSettings.AdditionalMS2 = new Dictionary<string, MS2Parameters>
            {
                { "tagging_reference", new MS2Parameters
                    {
                        Analyzer = "Orbitrap", Activation = "ETD", CollisionEnergy = 24, OrbitrapResolution = 15002,
                        AGCTarget = 400002, MaxIT = 102, FirstMass = 152, LastMass = 2002, Microscans = 2,
                        DataType = "Centroid", ReactionTime = 12, ReagentMaxIT = 202, ReagentAGCTarget = 700002,
                        ScanRate = "Zoom", RFLens = 34, SourceCID = 19, SourceCIDScaling = 0.14
                    } }
                // "quant_reference" is gone (ADR-0038). Quantification no longer references
                // additional_ms2 at all -- its two scans are the bare ms_settings.ms2_quant and
                // ms_settings.ms2 slots -- and leaving the entry here would make it an unreferenced
                // definition, which the engine warns about precisely because it never fires.
            };

            c.Scheduling.CycleTime.Enabled = true;
            c.Scheduling.CycleTime.ValueMs = 60001;
            c.Scheduling.ScanTimeout.Enabled = true;
            c.Scheduling.ScanTimeout.ValueMs = 30001;
            c.Scheduling.AgcIntervalSeconds = 29;
            c.Scheduling.TargetDepth = 3;   //deliberately NOT the default, so the round-trip proves the key survives

            c.PrecursorSelection.RankBy = "qscore";
            c.PrecursorSelection.MaxPrecursors = 3;
            c.PrecursorSelection.MinPrecursorCharge = 2;
            c.PrecursorSelection.AdditionalScans = new List<string>();   // see AdditionalMS2 above
            c.PrecursorSelection.Exploration = new ExplorationBlockConfig
            {
                Metric = "mass_count", CEMin = 21, CEMax = 39, CEStep = 5,
                RemainingPrecursorTarget = 0.12,
                ReactionTimeMin = 0, ReactionTimeMax = 0, ReactionTimeStep = 1,
                TolerancePpm = 14
            };
            // Distinctive, deliberately non-default (defaults are 3 / 50000): the reference exists so
            // that a DROPPED key is detectable, which a defaulted value would hide. These are the same
            // two values that used to be asserted on c.FlashTnT.
            c.PrecursorSelection.TagExpansion.MaxPtmCount = 5;
            c.PrecursorSelection.TagExpansion.MaxFlankingMassDiff = 41001;

            c.Characterization.Mode = "coverage";
            c.Characterization.ProteinSequence = "MSENTINELPEPTIDESEQ";
            c.Characterization.MaxTargets = 4;
            c.Characterization.MinFragmentCharge = 1;
            // Distinctive, per this builder's own rule: every key carries a non-default value so a
            // DROPPED key is detectable. Leaving this at 0.0 would make the regenerated reference
            // vacuous for exactly the key it is being regenerated for.
            c.Characterization.MinTargetMass = 617.5;
            c.Characterization.FragmentCharges = "separate";
            c.Characterization.Exploration = new ExplorationBlockConfig
            {
                Metric = "fragment_count", CEMin = 16, CEMax = 34, CEStep = 2,
                RemainingPrecursorTarget = 0.13,
                ReactionTimeMin = 0, ReactionTimeMax = 0, ReactionTimeStep = 1,
                TolerancePpm = 15
            };

            // runtime was the ONE section this builder never touched, so the reference carried ""
            // on both sides -- which an emitter that simply hardcoded "" would have satisfied
            // vacuously. A fixed RELATIVE literal keeps the generated file byte-deterministic
            // (no clock, no absolute path, no CWD dependence) while still proving the key is read
            // from the model. No path separator, no '%', no '${'.
            c.Runtime.LogDir = "schema_reference_logs";

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

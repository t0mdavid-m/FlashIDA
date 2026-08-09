using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using Flash;
using NUnit.Framework;

namespace Flash.Tests
{
    [TestFixture]
    public class JsonConfigTests
    {
        private static readonly string TestDataDir = Path.Combine(
            TestContext.CurrentContext.TestDirectory, "..", "test-data");
        private static readonly string ConfigsDir = Path.Combine(TestDataDir, "configs");

        private MethodParameters LoadJsonMethod(string jsonName)
        {
            string path = Path.Combine(ConfigsDir, jsonName);
            Assert.IsTrue(File.Exists(path), "Test config not found: " + path);
            return MethodParameters.Load(path);
        }

        [Test, Category("Tier1")]
        public void ToCppJson_ProducesValidJson()
        {
            var mp = LoadJsonMethod("method_default.json");
            string json = mp.ToCppJson();
            Assert.IsNotNull(json);
            Assert.IsNotEmpty(json);
            Assert.IsTrue(json.StartsWith("{"), "JSON must start with '{'");
            var serializer = new JavaScriptSerializer();
            var parsed = serializer.Deserialize<Dictionary<string, object>>(json);
            Assert.IsNotNull(parsed);
        }

        [Test, Category("Tier1")]
        public void ToCppJson_ContainsAllTopLevelKeys()
        {
            var mp = LoadJsonMethod("method_default.json");
            string json = mp.ToCppJson();
            var serializer = new JavaScriptSerializer();
            var parsed = serializer.Deserialize<Dictionary<string, object>>(json);
            string[] requiredKeys = new[] {
                "global", "deconvolution", "precursor_selection", "flashtnt", "tagging",
                "quantification", "faims", "ms_settings",
                "scheduling", "characterization", "files", "runtime"
            };
            foreach (var key in requiredKeys)
                Assert.IsTrue(parsed.ContainsKey(key), "Missing key: " + key);
        }

        [Test, Category("Tier1")]
        public void FlashTnT_Defaults_PreserveCurrentBehavior()
        {
            // A migrated config with a default flashtnt block must reproduce today's behavior.
            var mp = LoadJsonMethod("method_default.json");
            Assert.AreEqual(3, mp.Config.FlashTnT.MinLength);
            Assert.AreEqual(8, mp.Config.FlashTnT.MaxLength);
            Assert.AreEqual(3, mp.Config.PrecursorSelection.TagExpansion.MaxPtmCount);
            Assert.AreEqual(2, mp.Config.FlashTnT.MaxAaInGap);
            Assert.IsFalse(mp.Config.FlashTnT.AllowGap);
            Assert.AreEqual(2, mp.Config.FlashTnT.MaxBlindModCount);
            // Load-bearing: 700 (prior hardcoded MS2 value), NOT the extender's own 500 default.
            Assert.AreEqual(700.0, mp.Config.FlashTnT.MaxModMass, 0.001);
            var cpp = mp.ToCppJson();
            Assert.IsTrue(cpp.Contains("\"max_mod_mass\":700") || cpp.Contains("\"max_mod_mass\": 700"),
                "ToCppJson must emit flashtnt.max_mod_mass = 700");
        }

        [Test, Category("Tier1")]
        public void FlashTnT_Deserialize_ReadsAllParams()
        {
            string json = @"{
                ""precursor_selection"": {
                    ""tag_expansion"": {
                        ""max_ptm_count"": 6, ""max_flanking_mass_diff"": 42000
                    }
                },
                ""flashtnt"": {
                    ""min_length"": 5, ""max_length"": 12,
                    ""allow_gap"": true, ""max_aa_in_gap"": 3,
                    ""fixed_mod"": [""Carbamidomethyl (C)""], ""max_blind_mod_count"": 4, ""max_mod_mass"": 650
                }
            }";
            var config = MethodConfigSerializer.Deserialize(json);
            Assert.AreEqual(5, config.FlashTnT.MinLength);
            Assert.AreEqual(12, config.FlashTnT.MaxLength);
            Assert.AreEqual(6, config.PrecursorSelection.TagExpansion.MaxPtmCount);
            Assert.AreEqual(42000.0, config.PrecursorSelection.TagExpansion.MaxFlankingMassDiff, 0.001);
            Assert.IsTrue(config.FlashTnT.AllowGap);
            Assert.AreEqual(3, config.FlashTnT.MaxAaInGap);
            Assert.AreEqual(1, config.FlashTnT.FixedMod.Count);
            Assert.AreEqual("Carbamidomethyl (C)", config.FlashTnT.FixedMod[0]);
            Assert.AreEqual(4, config.FlashTnT.MaxBlindModCount);
            Assert.AreEqual(650.0, config.FlashTnT.MaxModMass, 0.001);
        }

        [Test, Category("Tier1")]
        public void FlashTnT_ToCppJson_EmitsFlashtntBlock_NotUnderTagging()
        {
            var mp = LoadJsonMethod("method_default.json");
            var parsed = new JavaScriptSerializer()
                .Deserialize<Dictionary<string, object>>(mp.ToCppJson());
            Assert.IsTrue(parsed.ContainsKey("flashtnt"), "ToCppJson must emit a flashtnt block");
            var ft = (Dictionary<string, object>)parsed["flashtnt"];
            Assert.IsTrue(ft.ContainsKey("min_length"));
            Assert.IsTrue(ft.ContainsKey("max_mod_mass"));
            // The four moved keys must NOT remain under tagging.
            if (parsed.ContainsKey("tagging") && parsed["tagging"] is Dictionary<string, object> tagging)
                Assert.IsFalse(tagging.ContainsKey("min_tag_length"),
                    "min_tag_length must have moved out of tagging into flashtnt");
        }

        [Test, Category("Tier1")]
        public void Deserialize_DeveloperRouting()
        {
            var mp = LoadJsonMethod("method_json_roundtrip.json");
            // HCDEnergy was the other [Developer]-routed probe here; it is deleted (no reachable
            // consumer), so MaxCVSkip now carries this test on its own.
            Assert.AreEqual(2, mp.Config.Faims.MaxCVSkip);
        }

        [Test, Category("Tier1")]
        public void Deserialize_ChargeBasedExclusion_RoundTrip()
        {
            var mp = LoadJsonMethod("method_charge_based_exclusion.json");
            Assert.IsTrue(mp.Config.PrecursorSelection.ChargeBasedExclusion);

            // Roundtrip preserves the flag.
            string serialized = MethodConfigSerializer.Serialize(mp.Config);
            var config2 = MethodConfigSerializer.Deserialize(serialized);
            Assert.IsTrue(config2.PrecursorSelection.ChargeBasedExclusion);

            // ToCppJson surfaces the flag on the wire-JSON.
            var cpp = mp.ToCppJson();
            // Matches the RAW emitted text, so it breaks on the emit-DTO field rename alone --
            // independently of the [JsonKey]. That is deliberate: it is the tripwire for a
            // half-rename, where the loader accepts the new key and the emitter writes the old one.
            Assert.IsTrue(cpp.Contains("\"charge_based_exclusion\":true") ||
                          cpp.Contains("\"charge_based_exclusion\": true"));
        }

        [Test, Category("Tier1")]
        public void Deserialize_ChargeBasedExclusion_DefaultsFalse()
        {
            var mp = LoadJsonMethod("method_default.json");
            Assert.IsFalse(mp.Config.PrecursorSelection.ChargeBasedExclusion);

            var cpp = mp.ToCppJson();
            Assert.IsTrue(cpp.Contains("\"charge_based_exclusion\":false") ||
                          cpp.Contains("\"charge_based_exclusion\": false"));
        }

        [Test, Category("Tier1")]
        public void Deserialize_RoundTrip()
        {
            var mp = LoadJsonMethod("method_default.json");
            string serialized = MethodConfigSerializer.Serialize(mp.Config);

            // Serialize is now symmetric with the strict loader: ms_settings uses snake_case keys
            // (bound by [JsonKey]), never the PascalCase struct field names.
            StringAssert.Contains("\"first_mass\"", serialized);
            Assert.IsFalse(serialized.Contains("\"FirstMass\""),
                "Serialize must emit snake_case ms_settings keys, not PascalCase field names.");

            var config2 = MethodConfigSerializer.Deserialize(serialized);
            Assert.AreEqual(mp.Config.Deconvolution.MinCharge, config2.Deconvolution.MinCharge);
            Assert.AreEqual(mp.Config.Deconvolution.MaxCharge, config2.Deconvolution.MaxCharge);
            Assert.AreEqual(mp.Config.PrecursorSelection.RTWindow, config2.PrecursorSelection.RTWindow);
            // HCDEnergy deleted; round-trip the keys that moved here out of selection_strategy.ms1
            // instead, which exercises the same path and covers the new surface.
            Assert.AreEqual(mp.Config.PrecursorSelection.RankBy, config2.PrecursorSelection.RankBy);
            Assert.AreEqual(mp.Config.PrecursorSelection.MaxPrecursors, config2.PrecursorSelection.MaxPrecursors);
            Assert.AreEqual(mp.Config.Characterization.Mode, config2.Characterization.Mode);
            Assert.AreEqual(mp.Config.Characterization.MaxTargets, config2.Characterization.MaxTargets);
            Assert.AreEqual(mp.Config.Faims.CVValues.Length, config2.Faims.CVValues.Length);
        }

        [Test, Category("Tier1")]
        public void ToCppJson_ContainsRuntimeSection()
        {
            var mp = new MethodParameters();
            mp.Config.Runtime.LogDir = @"C:\runs\case_2026-01-01-00-00-00";

            string json = mp.ToCppJson();
            var parsed = new JavaScriptSerializer()
                .Deserialize<Dictionary<string, object>>(json);

            Assert.IsTrue(parsed.ContainsKey("runtime"), "JSON should contain runtime section");
            var runtime = parsed["runtime"] as Dictionary<string, object>;
            Assert.IsNotNull(runtime, "runtime should be a dictionary");
            Assert.AreEqual(@"C:\runs\case_2026-01-01-00-00-00", runtime["log_dir"]);
        }

        /// <summary>
        /// ToCppJson must hand log_dir across UNCHANGED.
        ///
        /// This is the tripwire for the one refactor that would quietly break everything: moving
        /// run-folder composition (absolutise / stamp / mkdir) into ToCppJson or MethodParameters.Load
        /// instead of leaving it in LogPathResolver. That would (a) make the generated
        /// config_schema_reference.json differ on every run, failing Reference_IsNeverStale
        /// unfixably, and (b) make every NUnit suite that merely calls Load start writing five log
        /// files into bin/. Both failures point away from the actual cause.
        /// </summary>
        [Test, Category("Tier1")]
        public void ToCppJson_EmitsLogDirVerbatim()
        {
            var mp = new MethodParameters();
            mp.Config.Runtime.LogDir = "relative/unresolved";

            var parsed = new JavaScriptSerializer()
                .Deserialize<Dictionary<string, object>>(mp.ToCppJson());
            var runtime = (Dictionary<string, object>)parsed["runtime"];

            Assert.AreEqual("relative/unresolved", runtime["log_dir"],
                "ToCppJson resolved log_dir -- composition belongs in LogPathResolver, not the emitter");
            Assert.AreEqual(1, runtime.Count,
                "the runtime section carries exactly one key; a straggler here means the five "
                + "deleted per-stream paths are still being emitted");
        }

        [Test, Category("Tier1")]
        public void RuntimeConfig_UserOverridePreserved()
        {
            string methodJson = @"{
                ""global"": { ""duration"": 90 },
                ""runtime"": { ""log_dir"": ""logs/run"" }
            }";

            var config = MethodConfigSerializer.Deserialize(methodJson);
            // The AUTHORED value survives unresolved: the loader never absolutises and never
            // composes a run folder. Only the two Main methods do that, via LogPathResolver.
            Assert.AreEqual("logs/run", config.Runtime.LogDir);
        }

        /// <summary>The five per-stream path keys are gone; a config carrying one must not load.</summary>
        [Test, Category("Tier1")]
        public void RuntimeConfig_LegacyPerStreamKeys_Rejected()
        {
            foreach (string dead in new[] { "ida_log_path", "scan_commands_path", "scan_results_path",
                                            "identification_log_path", "pooled_identification_log_path" })
            {
                string methodJson = "{ \"runtime\": { \"" + dead + "\": \"x.log\" } }";
                var ex = Assert.Throws<ArgumentException>(
                    () => MethodConfigSerializer.Deserialize(methodJson),
                    "loader accepted the deleted runtime key " + dead);
                StringAssert.Contains("runtime." + dead, ex.Message);
            }
        }
    }
}

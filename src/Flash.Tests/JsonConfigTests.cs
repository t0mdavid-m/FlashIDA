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
                "scheduling", "selection_strategy", "characterization", "files", "runtime"
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
            Assert.AreEqual(3, mp.Config.FlashTnT.MaxPtmCount);
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
                ""flashtnt"": {
                    ""min_length"": 5, ""max_length"": 12, ""max_ptm_count"": 6,
                    ""max_flanking_mass_diff"": 42000, ""allow_gap"": true, ""max_aa_in_gap"": 3,
                    ""fixed_mod"": [""Carbamidomethyl (C)""], ""max_blind_mod_count"": 4, ""max_mod_mass"": 650
                }
            }";
            var config = MethodConfigSerializer.Deserialize(json);
            Assert.AreEqual(5, config.FlashTnT.MinLength);
            Assert.AreEqual(12, config.FlashTnT.MaxLength);
            Assert.AreEqual(6, config.FlashTnT.MaxPtmCount);
            Assert.AreEqual(42000.0, config.FlashTnT.MaxFlankingMassDiff, 0.001);
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
            Assert.AreEqual(35, mp.Config.PrecursorSelection.HCDEnergy);
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
            Assert.IsTrue(cpp.Contains("\"ChargeBasedExclusion\":true") ||
                          cpp.Contains("\"ChargeBasedExclusion\": true"));
        }

        [Test, Category("Tier1")]
        public void Deserialize_ChargeBasedExclusion_DefaultsFalse()
        {
            var mp = LoadJsonMethod("method_default.json");
            Assert.IsFalse(mp.Config.PrecursorSelection.ChargeBasedExclusion);

            var cpp = mp.ToCppJson();
            Assert.IsTrue(cpp.Contains("\"ChargeBasedExclusion\":false") ||
                          cpp.Contains("\"ChargeBasedExclusion\": false"));
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
            Assert.AreEqual(mp.Config.PrecursorSelection.HCDEnergy, config2.PrecursorSelection.HCDEnergy);
            Assert.AreEqual(mp.Config.Faims.CVValues.Length, config2.Faims.CVValues.Length);
        }

        [Test, Category("Tier1")]
        public void ToCppJson_ContainsRuntimeSection()
        {
            var mp = new MethodParameters();
            mp.Config.Runtime.IdaLogPath = "IDALog_test.log";
            mp.Config.Runtime.ScanCommandsPath = "ScanCommands_test.tsv";
            mp.Config.Runtime.ScanResultsPath = "ScanResults_test.tsv";

            string json = mp.ToCppJson();
            var parsed = new JavaScriptSerializer()
                .Deserialize<Dictionary<string, object>>(json);

            Assert.IsTrue(parsed.ContainsKey("runtime"), "JSON should contain runtime section");
            var runtime = parsed["runtime"] as Dictionary<string, object>;
            Assert.IsNotNull(runtime, "runtime should be a dictionary");
            Assert.AreEqual("IDALog_test.log", runtime["ida_log_path"]);
            Assert.AreEqual("ScanCommands_test.tsv", runtime["scan_commands_path"]);
            Assert.AreEqual("ScanResults_test.tsv", runtime["scan_results_path"]);
        }

        [Test, Category("Tier1")]
        public void RuntimeConfig_UserOverridePreserved()
        {
            string methodJson = @"{
                ""global"": { ""duration"": 90 },
                ""runtime"": {
                    ""ida_log_path"": ""user_ida.log"",
                    ""scan_commands_path"": ""user_commands.tsv"",
                    ""scan_results_path"": ""user_results.tsv""
                }
            }";

            var config = MethodConfigSerializer.Deserialize(methodJson);
            Assert.AreEqual("user_ida.log", config.Runtime.IdaLogPath);
            Assert.AreEqual("user_commands.tsv", config.Runtime.ScanCommandsPath);
            Assert.AreEqual("user_results.tsv", config.Runtime.ScanResultsPath);
        }
    }
}

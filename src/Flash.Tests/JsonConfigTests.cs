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
                "deconvolution", "precursor_selection", "tagging",
                "quantification", "faims", "ms_settings",
                "scheduling", "exploration", "files"
            };
            foreach (var key in requiredKeys)
                Assert.IsTrue(parsed.ContainsKey(key), "Missing key: " + key);
        }

        [Test, Category("Tier1")]
        public void ToCppJson_DefaultMatchesGoldenFile()
        {
            var mp = LoadJsonMethod("method_default.json");
            string json = mp.ToCppJson();
            string goldenPath = Path.Combine(TestDataDir, "json", "config_default.json");
            Assert.IsTrue(File.Exists(goldenPath), "Golden file not found: " + goldenPath);
            string goldenJson = File.ReadAllText(goldenPath);
            var serializer = new JavaScriptSerializer();
            var actual = serializer.Deserialize<Dictionary<string, object>>(json);
            var expected = serializer.Deserialize<Dictionary<string, object>>(goldenJson);
            CompareJsonSection(actual, expected, "deconvolution",
                "score_threshold", "tqscore_threshold", "min_charge", "max_charge",
                "min_mass", "max_mass", "tol");
            CompareJsonSection(actual, expected, "precursor_selection",
                "RT_window", "target_mode", "IDScore", "AllCharges",
                "HCDEnergy", "strict_inclusion", "tie_threshold");
            CompareJsonSection(actual, expected, "tagging",
                "min_tag_length", "max_tag_length", "max_ptm_count", "max_flanking_mass_diff");
        }

        [Test, Category("Tier1")]
        public void ToCppJson_FullMatchesGoldenFile()
        {
            var mp = LoadJsonMethod("method_json_roundtrip.json");
            string json = mp.ToCppJson();
            string goldenPath = Path.Combine(TestDataDir, "json", "config_full.json");
            Assert.IsTrue(File.Exists(goldenPath), "Golden file not found: " + goldenPath);
            string goldenJson = File.ReadAllText(goldenPath);
            var serializer = new JavaScriptSerializer();
            var actual = serializer.Deserialize<Dictionary<string, object>>(json);
            var expected = serializer.Deserialize<Dictionary<string, object>>(goldenJson);
            CompareJsonSection(actual, expected, "deconvolution",
                "score_threshold", "tqscore_threshold", "min_charge", "max_charge",
                "min_mass", "max_mass", "tol");
            CompareJsonSection(actual, expected, "precursor_selection",
                "RT_window", "target_mode", "IDScore", "AllCharges",
                "HCDEnergy", "strict_inclusion", "tie_threshold");
            var msSettings = (Dictionary<string, object>)actual["ms_settings"];
            var ms2Array = (System.Collections.ArrayList)msSettings["ms2"];
            Assert.AreEqual(2, ms2Array.Count, "Should have 2 MS2 entries");
            var faims = (Dictionary<string, object>)actual["faims"];
            var cvValues = (System.Collections.ArrayList)faims["cv_values"];
            Assert.AreEqual(3, cvValues.Count, "Should have 3 FAIMS CVs");
        }

        [Test, Category("Tier1")]
        public void Deserialize_DeveloperRouting()
        {
            var mp = LoadJsonMethod("method_json_roundtrip.json");
            Assert.IsTrue(mp.Config.PrecursorSelection.UseIDScore);
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
            var config2 = MethodConfigSerializer.Deserialize(serialized);
            Assert.AreEqual(mp.Config.Deconvolution.MinCharge, config2.Deconvolution.MinCharge);
            Assert.AreEqual(mp.Config.Deconvolution.MaxCharge, config2.Deconvolution.MaxCharge);
            Assert.AreEqual(mp.Config.PrecursorSelection.RTWindow, config2.PrecursorSelection.RTWindow);
            Assert.AreEqual(mp.Config.PrecursorSelection.HCDEnergy, config2.PrecursorSelection.HCDEnergy);
            Assert.AreEqual(mp.Config.Faims.CVValues.Length, config2.Faims.CVValues.Length);
        }

        private static void CompareJsonSection(
            Dictionary<string, object> actual,
            Dictionary<string, object> expected,
            string section, params string[] fields)
        {
            var actSection = (Dictionary<string, object>)actual[section];
            var expSection = (Dictionary<string, object>)expected[section];
            foreach (var field in fields)
            {
                var exp = expSection[field];
                var act = actSection[field];
                if (exp is bool)
                    Assert.AreEqual((bool)exp, (bool)act, string.Format("{0}.{1} mismatch", section, field));
                else if (exp is System.Collections.ArrayList)
                    CompareJsonArray((System.Collections.ArrayList)exp, (System.Collections.ArrayList)act,
                        string.Format("{0}.{1}", section, field));
                else
                    Assert.AreEqual(Convert.ToDouble(exp), Convert.ToDouble(act), 0.001,
                        string.Format("{0}.{1} mismatch", section, field));
            }
        }

        private static void CompareJsonArray(System.Collections.ArrayList expected,
            System.Collections.ArrayList actual, string path)
        {
            Assert.AreEqual(expected.Count, actual.Count, path + " length mismatch");
            for (int i = 0; i < expected.Count; i++)
                Assert.AreEqual(Convert.ToDouble(expected[i]), Convert.ToDouble(actual[i]), 0.001,
                    string.Format("{0}[{1}] mismatch", path, i));
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

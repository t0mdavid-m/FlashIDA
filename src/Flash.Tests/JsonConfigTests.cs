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
                "global", "deconvolution", "precursor_selection", "identification",
                "quantification", "faims", "ms_settings",
                "scheduling", "files", "runtime"
            };
            foreach (var key in requiredKeys)
                Assert.IsTrue(parsed.ContainsKey(key), "Missing key: " + key);
        }

        [Test, Category("Tier1")]
        public void FlashTnT_Defaults_PreserveCurrentBehavior()
        {
            // A migrated config with a default flashtnt block must reproduce today's behavior. The
            // block lives in identification (ADR-0061), so the fixture is one that identifies.
            var mp = LoadJsonMethod("method_identify_only.json");
            var ft = mp.Config.Identification.FlashTnT;
            Assert.AreEqual(3, ft.MinLength);
            Assert.AreEqual(8, ft.MaxLength);
            Assert.AreEqual(2, ft.MaxAaInGap);
            Assert.IsFalse(ft.AllowGap);
            Assert.AreEqual(2, ft.MaxBlindModCount);
            // Load-bearing: 700 (prior hardcoded MS2 value), NOT the extender's own 500 default.
            Assert.AreEqual(700.0, ft.MaxModMass, 0.001);
            var cpp = mp.ToCppJson();
            Assert.IsTrue(cpp.Contains("\"max_mod_mass\":700") || cpp.Contains("\"max_mod_mass\": 700"),
                "ToCppJson must emit flashtnt.max_mod_mass = 700");
        }

        [Test, Category("Tier1")]
        public void FlashTnT_Deserialize_ReadsAllParams()
        {
            string json = @"{
                ""identification"": {
                    ""reference"": { ""sequence"": ""PEPTIDE"" },
                    ""flashtnt"": {
                        ""min_length"": 5, ""max_length"": 12,
                        ""allow_gap"": true, ""max_aa_in_gap"": 3,
                        ""fixed_mod"": [""Carbamidomethyl (C)""], ""max_blind_mod_count"": 4, ""max_mod_mass"": 650
                    }
                }
            }";
            var config = MethodConfigSerializer.Deserialize(json);
            var ft = config.Identification.FlashTnT;
            Assert.AreEqual(5, ft.MinLength);
            Assert.AreEqual(12, ft.MaxLength);
            Assert.IsTrue(ft.AllowGap);
            Assert.AreEqual(3, ft.MaxAaInGap);
            Assert.AreEqual(1, ft.FixedMod.Count);
            Assert.AreEqual("Carbamidomethyl (C)", ft.FixedMod[0]);
            Assert.AreEqual(4, ft.MaxBlindModCount);
            Assert.AreEqual(650.0, ft.MaxModMass, 0.001);
        }

        [Test, Category("Tier1")]
        public void FlashTnT_ToCppJson_EmitsFlashtntBlock_AndNoTagging()
        {
            var mp = LoadJsonMethod("method_identify_only.json");
            var parsed = new JavaScriptSerializer()
                .Deserialize<Dictionary<string, object>>(mp.ToCppJson());
            var ident = (Dictionary<string, object>)parsed["identification"];
            Assert.IsTrue(ident.ContainsKey("flashtnt"), "ToCppJson must emit identification.flashtnt");
            Assert.IsFalse(parsed.ContainsKey("flashtnt"), "flashtnt moved into identification (ADR-0061)");
            var ft = (Dictionary<string, object>)ident["flashtnt"];
            Assert.IsTrue(ft.ContainsKey("min_length"));
            Assert.IsTrue(ft.ContainsKey("max_mod_mass"));
            // tagging is retired (ADR-0061); it must not cross the bridge in any shape.
            Assert.IsFalse(parsed.ContainsKey("tagging"), "ToCppJson must not emit a tagging block");
            Assert.IsFalse(parsed.ContainsKey("conditional_ms2"), "ToCppJson must not emit conditional_ms2");
        }

        [Test, Category("Tier1")]
        public void Deserialize_DeveloperRouting()
        {
            var mp = LoadJsonMethod("method_json_roundtrip.json");
            // HCDEnergy was the other [Developer]-routed probe here; it is deleted (no reachable
            // consumer), so MaxCVSkip now carries this test on its own.
            Assert.AreEqual(2, mp.Config.Faims.MaxCVSkip);
        }

        // charge_based_exclusion is retired (ADR-0021). The two tests that used to live here asserted
        // it round-tripped and defaulted to false; both are meaningless now. What replaces them is the
        // migration path, and it is asserted HERE rather than only in C++ because C# validates
        // method.json first -- a user with the retired key never reaches the C++ loader, so a message
        // that exists only there is unreachable.
        [Test, Category("Tier1")]
        public void Deserialize_ChargeBasedExclusion_IsRejectedWithMigrationHint()
        {
            string path = Path.Combine(ConfigsDir, "method_default.json");
            Assert.IsTrue(File.Exists(path), "Test config not found: " + path);
            // Re-introduce the retired key into an otherwise-valid config. `false` on purpose: that
            // was its default, so a stale config carrying it is the common case, not the exotic one.
            string json = File.ReadAllText(path)
                .Replace("\"consider_all_charges\"", "\"charge_based_exclusion\": false, \"consider_all_charges\"");

            var ex = Assert.Throws<ArgumentException>(() => MethodConfigSerializer.Deserialize(json));

            // The dotted path, so the reader knows WHICH key -- and the replacement, so they do not
            // simply delete it and silently lose multi-charge acquisition.
            StringAssert.Contains("precursor_selection.charge_based_exclusion", ex.Message);
            StringAssert.Contains("precursor_charges", ex.Message);
        }

        // Tag-based targeting and the conditional MS2 are retired (ADR-0061). Every method the
        // serializer ever wrote carries all five keys at their EMPTY defaults, so the default value is
        // the case that matters: it proves an untouched old method is refused with a migration hint,
        // not merely one that used the feature.
        [Test, Category("Tier1")]
        public void Deserialize_RetiredTagTargetingKeys_AreRejectedWithMigrationHint()
        {
            string path = Path.Combine(ConfigsDir, "method_default.json");
            Assert.IsTrue(File.Exists(path), "Test config not found: " + path);
            var cases = new[]
            {
                new { Section = (string)null, Key = "conditional_ms2", Value = (object)false },
                new { Section = (string)null, Key = "tagging", Value = (object)new Dictionary<string, object>() },
                new { Section = "files", Key = "fasta", Value = (object)"" },
                new { Section = "files", Key = "ptm_list", Value = (object)"" },
                new { Section = "precursor_selection", Key = "tag_expansion", Value = (object)new Dictionary<string, object>() },
            };
            var js = new JavaScriptSerializer();
            foreach (var c in cases)
            {
                var raw = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                if (c.Section == null) raw[c.Key] = c.Value;
                else ((Dictionary<string, object>)raw[c.Section])[c.Key] = c.Value;
                string reported = c.Section == null ? c.Key : c.Section + "." + c.Key;

                var ex = Assert.Throws<ArgumentException>(() => MethodConfigSerializer.Deserialize(js.Serialize(raw)),
                    reported + " at its default must be refused");
                StringAssert.Contains(reported, ex.Message);
                StringAssert.Contains("ADR-0061", ex.Message);
            }
        }

        // The second push of ADR-0061: characterization and flashtnt moved into identification. Every
        // method the serializer ever wrote carries a top-level characterization and flashtnt, so the
        // DEFAULT shapes are the ones that matter; the three keys that moved up out of
        // identification.characterization are refused with a pointer to their new home.
        [Test, Category("Tier1")]
        public void Deserialize_PreIdentificationSections_AreRejectedWithMigrationHint()
        {
            string path = Path.Combine(ConfigsDir, "method_default.json");
            Assert.IsTrue(File.Exists(path), "Test config not found: " + path);
            var js = new JavaScriptSerializer();
            Func<Dictionary<string, object>> identWith = () => new Dictionary<string, object>
            {
                { "reference", new Dictionary<string, object> { { "sequence", "PEPTIDE" } } },
                { "characterization", new Dictionary<string, object> { { "mode", "ambiguity" } } }
            };
            var cases = new List<KeyValuePair<string, Action<Dictionary<string, object>>>>
            {
                new KeyValuePair<string, Action<Dictionary<string, object>>>("characterization",
                    raw => raw["characterization"] = new Dictionary<string, object> { { "mode", "off" } }),
                new KeyValuePair<string, Action<Dictionary<string, object>>>("flashtnt",
                    raw => raw["flashtnt"] = new Dictionary<string, object>()),
            };
            foreach (var key in new[] { "protein_sequence", "method", "refine" })
            {
                string k = key;
                cases.Add(new KeyValuePair<string, Action<Dictionary<string, object>>>(
                    "identification.characterization." + k, raw =>
                    {
                        var ident = identWith();
                        ((Dictionary<string, object>)ident["characterization"])[k] =
                            k == "refine" ? (object)new Dictionary<string, object>() : "flashtnt";
                        raw["identification"] = ident;
                    }));
            }
            foreach (var c in cases)
            {
                var raw = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                c.Value(raw);
                var ex = Assert.Throws<ArgumentException>(() => MethodConfigSerializer.Deserialize(js.Serialize(raw)),
                    c.Key + " must be refused");
                StringAssert.Contains(c.Key, ex.Message);
                StringAssert.Contains("ADR-0061", ex.Message);
            }
        }

        // ADR-0061. The section's ABSENCE is a setting (no MS2 is matched), so it must survive the
        // C# side as null rather than be filled with a default instance, and cross the bridge as null.
        [Test, Category("Tier1")]
        public void Identification_AbsentStaysNull_PresentRoundTrips()
        {
            var none = LoadJsonMethod("method_default.json");
            Assert.IsNull(none.Config.Identification, "method_default.json authors no identification");
            var parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(none.ToCppJson());
            Assert.IsTrue(parsed.ContainsKey("identification"));
            Assert.IsNull(parsed["identification"], "an absent section must cross the bridge as null");

            var idOnly = LoadJsonMethod("method_identify_only.json");
            Assert.IsNotNull(idOnly.Config.Identification);
            Assert.IsNull(idOnly.Config.Identification.Characterization,
                "method_identify_only.json identifies without MS3, so it has no characterization block");

            var ms3 = LoadJsonMethod("method_ms3_cytc_real.json");
            var back = MethodConfigSerializer.Deserialize(MethodConfigSerializer.Serialize(ms3.Config));
            Assert.AreEqual(ms3.Config.Identification.Reference.Sequence, back.Identification.Reference.Sequence);
            Assert.IsFalse(string.IsNullOrEmpty(back.Identification.Reference.Sequence));
            Assert.AreEqual(ms3.Config.Identification.Method, back.Identification.Method);
            Assert.AreEqual(ms3.Config.Identification.Characterization.Mode, back.Identification.Characterization.Mode);
            Assert.IsNotNull(back.Identification.Characterization.Mode);
            Assert.AreEqual(ms3.Config.Identification.Characterization.MaxTargets,
                back.Identification.Characterization.MaxTargets);
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
            // method_default.json authors no identification section (ADR-0061); it must stay absent.
            Assert.IsNull(mp.Config.Identification);
            Assert.IsNull(config2.Identification);
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

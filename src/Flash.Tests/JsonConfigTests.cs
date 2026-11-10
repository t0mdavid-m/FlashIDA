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
        public void FlashTnT_CorpusBlock_AuthorsTodaysValues()
        {
            // The C# model holds no flashtnt defaults any more: an unauthored key is null and C++ takes
            // FLASHTnT's own (ADR-0061 push 4c; Identification_Flashtnt_UnauthoredStaysNull_AuthoredRoundTrips).
            // FLASHTnT's max_blind_mod_count is 1 and max_mod_mass 500, so the corpus keeps today's
            // behaviour by AUTHORING the old values; this pins that an identifying fixture still does.
            var mp = LoadJsonMethod("method_identify_only.json");
            var ft = mp.Config.Identification.FlashTnT;
            Assert.AreEqual(3, ft.MinLength);
            Assert.AreEqual(8, ft.MaxLength);
            Assert.AreEqual(2, ft.MaxAaInGap);
            Assert.IsFalse(ft.AllowGap);
            Assert.AreEqual(2, ft.MaxBlindModCount, "authored, against FLASHTnT's default of 1");
            // Load-bearing: 700 authored, NOT FLASHTnT's default of 500 (cytochrome c's heme is +616 Da).
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

        // ADR-0062: identification.search_threads. No corpus config authors it, and unauthored it reaches C++ as
        // null or not at all, so the engine sizes the pool from the core count; authored, it survives the
        // round trip and crosses the bridge as the number.
        [Test, Category("Tier1")]
        public void Identification_SearchThreads_UnauthoredStaysNull_AuthoredRoundTrips()
        {
            var mp = LoadJsonMethod("method_ms3_cytc_real.json");
            Assert.IsNotNull(mp.Config.Identification);
            Assert.IsNull(mp.Config.Identification.SearchThreads, "no corpus config authors search_threads");
            var parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(mp.ToCppJson());
            var ident = (Dictionary<string, object>)parsed["identification"];
            Assert.IsTrue(!ident.ContainsKey("search_threads") || ident["search_threads"] == null,
                "an unauthored search_threads must not reach C++ as a number");

            mp.Config.Identification.SearchThreads = 5;
            var back = MethodConfigSerializer.Deserialize(MethodConfigSerializer.Serialize(mp.Config));
            Assert.AreEqual(5, back.Identification.SearchThreads);
            parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(mp.ToCppJson());
            ident = (Dictionary<string, object>)parsed["identification"];
            Assert.AreEqual(5, Convert.ToInt32(ident["search_threads"]));
        }

        // ADR-0061 push 5: identification.threshold. Unauthored it never reaches C++ as a number (identify and log,
        // never close out); authored, it survives the round trip and crosses the bridge as the number.
        [Test, Category("Tier1")]
        public void Identification_Threshold_UnauthoredStaysNull_AuthoredRoundTrips()
        {
            var mp = LoadJsonMethod("method_ms3_cytc_real.json");
            Assert.IsNotNull(mp.Config.Identification);
            Assert.IsNull(mp.Config.Identification.Threshold, "method_ms3_cytc_real.json authors no threshold");
            var parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(mp.ToCppJson());
            var ident = (Dictionary<string, object>)parsed["identification"];
            Assert.IsTrue(!ident.ContainsKey("threshold") || ident["threshold"] == null,
                "an unauthored threshold must not reach C++ as a number");

            mp.Config.Identification.Threshold = 612.5;
            var back = MethodConfigSerializer.Deserialize(MethodConfigSerializer.Serialize(mp.Config));
            Assert.AreEqual(612.5, back.Identification.Threshold);
            parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(mp.ToCppJson());
            ident = (Dictionary<string, object>)parsed["identification"];
            Assert.AreEqual(612.5, Convert.ToDouble(ident["threshold"], System.Globalization.CultureInfo.InvariantCulture));
        }

        // ADR-0064: quantification.scan / ms3_targets / ms_settings.ms3_quant. Unauthored they are ms2, 1 and absent (the
        // key not emitted); authored, each survives the round trip and crosses the bridge.
        [Test, Category("Tier1")]
        public void Quantification_Ms3Keys_DefaultsAndAuthoredRoundTrip()
        {
            var mp = LoadJsonMethod("method_quant.json");
            Assert.AreEqual("ms2", mp.Config.Quantification.Scan);
            Assert.AreEqual(1, mp.Config.Quantification.Ms3Targets);
            Assert.IsFalse(mp.Config.MsSettings.MS3Quant.HasValue, "method_quant.json authors no ms3_quant");
            var parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(mp.ToCppJson());
            var quant = (Dictionary<string, object>)parsed["quantification"];
            Assert.AreEqual("ms2", quant["scan"]);
            Assert.AreEqual(1, Convert.ToInt32(quant["ms3_targets"]));
            var ms = (Dictionary<string, object>)parsed["ms_settings"];
            Assert.IsTrue(!ms.ContainsKey("ms3_quant") || ms["ms3_quant"] == null, "an unauthored ms3_quant must not be emitted");

            var authored = LoadJsonMethod("method_quant_ms3.json");
            Assert.AreEqual("ms3", authored.Config.Quantification.Scan);
            Assert.IsTrue(authored.Config.MsSettings.MS3Quant.HasValue);
            Assert.AreEqual("HCD", authored.Config.MsSettings.MS3Quant.Value.Activation);
            Assert.AreEqual(80, authored.Config.MsSettings.MS3Quant.Value.CollisionEnergy);

            authored.Config.Quantification.Scan = "both";
            authored.Config.Quantification.Ms3Targets = 3;
            var back = MethodConfigSerializer.Deserialize(MethodConfigSerializer.Serialize(authored.Config));
            Assert.AreEqual("both", back.Quantification.Scan);
            Assert.AreEqual(3, back.Quantification.Ms3Targets);
            Assert.AreEqual(80, back.MsSettings.MS3Quant.Value.CollisionEnergy);
            parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(authored.ToCppJson());
            quant = (Dictionary<string, object>)parsed["quantification"];
            Assert.AreEqual("both", quant["scan"]);
            Assert.AreEqual(3, Convert.ToInt32(quant["ms3_targets"]));
            var q3 = (Dictionary<string, object>)((Dictionary<string, object>)parsed["ms_settings"])["ms3_quant"];
            Assert.AreEqual("HCD", q3["activation"]);
            Assert.AreEqual(80, Convert.ToInt32(q3["collision_energy"]));
        }

        // ms_settings.ms2 and ms3 were plain structs, so an unauthored one crossed the bridge zero-filled and the
        // engine's "ms_settings.msN is not defined" refusals could never fire from a method.json. Unauthored, each
        // must now stay unset and cross as absent or null. The control (the fixture as committed) proves the block
        // does cross when authored, so the stripped assertion is not passing on a block that never crossed at all.
        [Test, Category("Tier1")]
        public void MsSettings_UnauthoredMs3_IsNotEmitted()
        {
            AssertUnauthoredScanSlotIsNotEmitted("ms3", c => c.MsSettings.MS3.HasValue);
        }

        [Test, Category("Tier1")]
        public void MsSettings_UnauthoredMs2_IsNotEmitted()
        {
            AssertUnauthoredScanSlotIsNotEmitted("ms2", c => c.MsSettings.MS2.HasValue);
        }

        private void AssertUnauthoredScanSlotIsNotEmitted(string slot, Func<MethodConfig, bool> hasValue)
        {
            // Characterization plus both ms2 and ms3, as committed.
            string path = Path.Combine(ConfigsDir, "method_ms3_cytc_fragments.json");
            Assert.IsTrue(File.Exists(path), "Test config not found: " + path);
            var js = new JavaScriptSerializer();

            var authored = new MethodParameters { Config = MethodConfigSerializer.Deserialize(File.ReadAllText(path)) };
            Assert.IsTrue(hasValue(authored.Config), "control: the fixture authors ms_settings." + slot);
            var authoredMs = (Dictionary<string, object>)js.Deserialize<Dictionary<string, object>>(authored.ToCppJson())["ms_settings"];
            Assert.IsTrue(authoredMs.ContainsKey(slot) && authoredMs[slot] is Dictionary<string, object>,
                "control: an authored ms_settings." + slot + " must cross the bridge as an object");

            var raw = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            Assert.IsTrue(((Dictionary<string, object>)raw["ms_settings"]).Remove(slot));
            var stripped = new MethodParameters { Config = MethodConfigSerializer.Deserialize(js.Serialize(raw)) };
            Assert.IsFalse(hasValue(stripped.Config), "an unauthored ms_settings." + slot + " must stay unset, not default-construct");
            var strippedMs = (Dictionary<string, object>)js.Deserialize<Dictionary<string, object>>(stripped.ToCppJson())["ms_settings"];
            Assert.IsTrue(!strippedMs.ContainsKey(slot) || strippedMs[slot] == null,
                "an unauthored ms_settings." + slot + " must not cross the bridge zero-filled; the engine refuses it only when absent");
        }

        // ADR-0068: ms_settings.ms2_rescue. Unauthored it is absent (the key not emitted, so no rescue); authored, it
        // survives the round trip and crosses the bridge. method_quant_ms3.json carries identification.threshold, which
        // the C++ side requires beside it.
        [Test, Category("Tier1")]
        public void MsSettings_Ms2Rescue_DefaultsAndAuthoredRoundTrip()
        {
            var mp = LoadJsonMethod("method_quant_ms3.json");
            Assert.IsFalse(mp.Config.MsSettings.MS2Rescue.HasValue, "method_quant_ms3.json authors no ms2_rescue");
            var parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(mp.ToCppJson());
            var ms = (Dictionary<string, object>)parsed["ms_settings"];
            Assert.IsTrue(!ms.ContainsKey("ms2_rescue") || ms["ms2_rescue"] == null, "an unauthored ms2_rescue must not be emitted");

            mp.Config.MsSettings.MS2Rescue = new MS2Parameters
            {
                Analyzer = "Orbitrap", Activation = "ETD", ReactionTime = 12, OrbitrapResolution = 120000, DataType = "Centroid"
            };
            var back = MethodConfigSerializer.Deserialize(MethodConfigSerializer.Serialize(mp.Config));
            Assert.IsTrue(back.MsSettings.MS2Rescue.HasValue);
            Assert.AreEqual("ETD", back.MsSettings.MS2Rescue.Value.Activation);
            Assert.AreEqual(12.0, back.MsSettings.MS2Rescue.Value.ReactionTime);
            parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(mp.ToCppJson());
            var rs = (Dictionary<string, object>)((Dictionary<string, object>)parsed["ms_settings"])["ms2_rescue"];
            Assert.AreEqual("ETD", rs["activation"]);
            Assert.AreEqual(12.0, Convert.ToDouble(rs["reaction_time"], System.Globalization.CultureInfo.InvariantCulture));
            Assert.AreEqual(120000, Convert.ToInt32(rs["resolution"]));
        }

        // ADR-0066: identification.characterization.per_proteoform. No corpus config authors it; the default false
        // crosses the bridge as false (C++ treats it as absent), and an authored true round-trips and crosses as true.
        [Test, Category("Tier1")]
        public void Characterization_PerProteoform_DefaultFalse_AuthoredRoundTrips()
        {
            var mp = LoadJsonMethod("method_ms3_cytc_fragments.json");
            Assert.IsNotNull(mp.Config.Identification?.Characterization);
            Assert.IsFalse(mp.Config.Identification.Characterization.PerProteoform, "no corpus config authors per_proteoform");
            var parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(mp.ToCppJson());
            var cz = (Dictionary<string, object>)((Dictionary<string, object>)parsed["identification"])["characterization"];
            Assert.AreEqual(false, cz["per_proteoform"]);

            mp.Config.Identification.Characterization.PerProteoform = true;
            var back = MethodConfigSerializer.Deserialize(MethodConfigSerializer.Serialize(mp.Config));
            Assert.IsTrue(back.Identification.Characterization.PerProteoform);
            parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(mp.ToCppJson());
            cz = (Dictionary<string, object>)((Dictionary<string, object>)parsed["identification"])["characterization"];
            Assert.AreEqual(true, cz["per_proteoform"]);
        }

        // ADR-0069: identification.characterization.yield_gate. Absent: null, and it crosses the bridge as null (no gate).
        // An empty block takes the core-5 defaults on both sides of the bridge; an authored one round-trips and crosses.
        [Test, Category("Tier1")]
        public void Characterization_YieldGate_AbsentIsNull_EmptyTakesDefaults_AuthoredRoundTrips()
        {
            var mp = LoadJsonMethod("method_ms3_cytc_fragments.json");
            Assert.IsNull(mp.Config.Identification.Characterization.YieldGate, "no committed config authors yield_gate");
            var parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(mp.ToCppJson());
            var cz = (Dictionary<string, object>)((Dictionary<string, object>)parsed["identification"])["characterization"];
            Assert.IsTrue(!cz.ContainsKey("yield_gate") || cz["yield_gate"] == null, "an unauthored yield_gate must cross as absent");

            var empty = MethodConfigSerializer.Deserialize(
                "{ \"identification\": { \"characterization\": { \"mode\": \"fragments\", \"yield_gate\": {} } } }");
            AssertCore5(empty.Identification.Characterization.YieldGate);

            mp.Config.Identification.Characterization.YieldGate = new YieldGateConfig { Threshold = 6, Intercept = -30.0 };
            var back = MethodConfigSerializer.Deserialize(MethodConfigSerializer.Serialize(mp.Config));
            Assert.AreEqual(6.0, back.Identification.Characterization.YieldGate.Threshold);
            Assert.AreEqual(-30.0, back.Identification.Characterization.YieldGate.Intercept);
            Assert.AreEqual(21.747494, back.Identification.Characterization.YieldGate.FragmentIsoCos);
            parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(mp.ToCppJson());
            cz = (Dictionary<string, object>)((Dictionary<string, object>)parsed["identification"])["characterization"];
            var yg = (Dictionary<string, object>)cz["yield_gate"];
            Assert.AreEqual(6.0, Convert.ToDouble(yg["threshold"]));
            Assert.AreEqual(-30.0, Convert.ToDouble(yg["intercept"]));
            Assert.AreEqual(1.529105, Convert.ToDouble(yg["fragment_intensity"]), 1e-12);
            Assert.AreEqual(7, yg.Count, "all seven keys cross whenever the block does");
        }

        // ADR-0069: quantification.ms3_yield_gate, the quantification MS3's own gate: the same block under its own key.
        [Test, Category("Tier1")]
        public void Quantification_Ms3YieldGate_AbsentIsNull_EmptyTakesDefaults_AuthoredRoundTrips()
        {
            var mp = LoadJsonMethod("method_quant_ms3.json");
            Assert.IsNull(mp.Config.Quantification.Ms3YieldGate, "no committed config authors ms3_yield_gate");
            var parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(mp.ToCppJson());
            var quant = (Dictionary<string, object>)parsed["quantification"];
            Assert.IsTrue(!quant.ContainsKey("ms3_yield_gate") || quant["ms3_yield_gate"] == null);

            var empty = MethodConfigSerializer.Deserialize("{ \"quantification\": { \"ms3_yield_gate\": {} } }");
            AssertCore5(empty.Quantification.Ms3YieldGate);

            mp.Config.Quantification.Ms3YieldGate = new YieldGateConfig { Threshold = 0.5, MassRatio = -2.0 };
            var back = MethodConfigSerializer.Deserialize(MethodConfigSerializer.Serialize(mp.Config));
            Assert.AreEqual(0.5, back.Quantification.Ms3YieldGate.Threshold);
            Assert.AreEqual(-2.0, back.Quantification.Ms3YieldGate.MassRatio);
            Assert.IsNull(back.Identification?.Characterization?.YieldGate, "one gate does not set the other");
            parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(mp.ToCppJson());
            var yg = (Dictionary<string, object>)((Dictionary<string, object>)parsed["quantification"])["ms3_yield_gate"];
            Assert.AreEqual(0.5, Convert.ToDouble(yg["threshold"]));
            Assert.AreEqual(-2.0, Convert.ToDouble(yg["mass_ratio"]));
        }

        // The core-5 fit (MS3_gate_spec.md, YieldGateSettings.h): the C# defaults pinned to the same literals the C++
        // Config_SchemaProjection_test pins, so the two sides cannot default differently.
        private static void AssertCore5(YieldGateConfig g)
        {
            Assert.IsNotNull(g, "an empty block is present: the gate is on");
            Assert.AreEqual(1.0, g.Threshold);
            Assert.AreEqual(-31.632174, g.Intercept);
            Assert.AreEqual(1.529105, g.FragmentIntensity);
            Assert.AreEqual(0.474888, g.PrecursorIntensity);
            Assert.AreEqual(-0.105683, g.PrecursorCharge);
            Assert.AreEqual(-2.766660, g.MassRatio);
            Assert.AreEqual(21.747494, g.FragmentIsoCos);
        }

        // ADR-0061 push 4c. Every identification.flashtnt key, its model value and the value an authored
        // config carries. Every authored value differs from FLASHTnT's default (and from ADR-0061's
        // skip_precursor_inference true); fixed_mod's default is [] (ADR-0067).
        private static readonly (string Key, Func<FlashTnTConfig, object> Get, object Authored)[] FlashtntKnobs =
        {
            ("min_length", f => f.MinLength, 4),
            ("max_length", f => f.MaxLength, 9),
            ("allow_gap", f => f.AllowGap, true),
            ("max_aa_in_gap", f => f.MaxAaInGap, 3),
            ("fixed_mod", f => f.FixedMod == null ? null : f.FixedMod.ToArray(), new[] { "iodoTMT6plex (C)" }),
            ("max_blind_mod_count", f => f.MaxBlindModCount, 2),
            ("max_mod_mass", f => f.MaxModMass, 616.5),
            ("var_mod", f => f.VarMod == null ? null : f.VarMod.ToArray(), new[] { "Oxidation (M)", "Acetyl (N-term)" }),
            ("max_var_mod_count", f => f.MaxVarModCount, 1),
            ("max_protein_hits", f => f.MaxProteinHits, 7),
            ("gapped_tags", f => f.GappedTags, "off"),
            ("terminal_ladders", f => f.TerminalLadders, false),
            ("precursor_ladders", f => f.PrecursorLadders, false),
            ("skip_precursor_inference", f => f.SkipPrecursorInference, false),
        };

        private const string AuthoredFlashtnt = @"{
                ""min_length"": 4, ""max_length"": 9, ""allow_gap"": true, ""max_aa_in_gap"": 3, ""fixed_mod"": [""iodoTMT6plex (C)""],
                ""max_blind_mod_count"": 2, ""max_mod_mass"": 616.5,
                ""var_mod"": [""Oxidation (M)"", ""Acetyl (N-term)""], ""max_var_mod_count"": 1, ""max_protein_hits"": 7,
                ""gapped_tags"": ""off"", ""terminal_ladders"": false, ""precursor_ladders"": false,
                ""skip_precursor_inference"": false }";

        private static MethodParameters WithIdentification(string identificationJson)
        {
            return new MethodParameters
            {
                Config = MethodConfigSerializer.Deserialize(@"{ ""identification"": " + identificationJson + " }")
            };
        }

        private static Dictionary<string, object> CppIdentification(MethodParameters mp)
        {
            var parsed = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(mp.ToCppJson());
            return (Dictionary<string, object>)parsed["identification"];
        }

        // A model value or a JavaScriptSerializer-parsed bridge value against the expected one.
        private static bool SameValue(object expected, object actual)
        {
            if (expected == null || actual == null) return expected == null && actual == null;
            var list = expected as string[];
            if (list != null)
            {
                var got = actual as System.Collections.IList;
                if (got == null || got.Count != list.Length) return false;
                for (int i = 0; i < list.Length; i++)
                    if (got[i] as string != list[i]) return false;
                return true;
            }
            if (expected is bool) return actual is bool && (bool)actual == (bool)expected;
            if (expected is string) return actual as string == (string)expected;
            return !(actual is string) && !(actual is bool) && Convert.ToDouble(actual) == Convert.ToDouble(expected);
        }

        // ADR-0061 push 4c: the flashtnt keys follow search_threads. Unauthored, every key stays null in
        // the model and crosses the bridge as null (or not at all), so C++ takes FLASHTnT's own default;
        // authored, each survives the round trip and crosses the bridge as written.
        [Test, Category("Tier1")]
        public void Identification_Flashtnt_UnauthoredStaysNull_AuthoredRoundTrips()
        {
            Assert.AreEqual(14, FlashtntKnobs.Length, "the 14 identification.flashtnt keys");
            const string reference = @"""reference"": { ""sequence"": ""PEPTIDEK"" }";
            foreach (var unset in new[] { "{ " + reference + " }", "{ " + reference + @", ""flashtnt"": {} }" })
            {
                var mp = WithIdentification(unset);
                var ft = mp.Config.Identification.FlashTnT;
                Assert.IsNotNull(ft);
                var cpp = (Dictionary<string, object>)CppIdentification(mp)["flashtnt"];
                foreach (var k in FlashtntKnobs)
                {
                    Assert.IsNull(k.Get(ft), k.Key + " unauthored must stay null in the model: " + unset);
                    Assert.IsTrue(!cpp.ContainsKey(k.Key) || cpp[k.Key] == null,
                        k.Key + " unauthored must not reach C++ as a value, or C++ cannot take FLASHTnT's default");
                }
            }

            var authored = WithIdentification("{ " + reference + @", ""flashtnt"": " + AuthoredFlashtnt + " }");
            var back = MethodConfigSerializer.Deserialize(MethodConfigSerializer.Serialize(authored.Config));
            var emitted = (Dictionary<string, object>)CppIdentification(authored)["flashtnt"];
            foreach (var k in FlashtntKnobs)
            {
                Assert.IsTrue(SameValue(k.Authored, k.Get(authored.Config.Identification.FlashTnT)), k.Key + " read");
                Assert.IsTrue(SameValue(k.Authored, k.Get(back.Identification.FlashTnT)), k.Key + " round trip");
                Assert.IsTrue(emitted.ContainsKey(k.Key), k.Key + " emitted");
                Assert.IsTrue(SameValue(k.Authored, emitted[k.Key]), k.Key + " crosses the bridge as authored");
            }
        }

        // ADR-0061 d.13: reference is { fasta } XOR { sequence }. The refusal of both, neither or an empty
        // value lives in C++ (FLASHIda_LegacyConfig_test reference_is_fasta_xor_sequence): C# validates no
        // reference value, so what it must do is carry each key across exactly as authored (null =
        // unauthored), or C++ would see a different reference than the method file names.
        [Test, Category("Tier1")]
        public void Identification_Reference_FastaXorSequence()
        {
            var fasta = WithIdentification(@"{ ""reference"": { ""fasta"": ""C:/db/ecoli.fasta"" } }");
            Assert.AreEqual("C:/db/ecoli.fasta", fasta.Config.Identification.Reference.Fasta);
            Assert.IsNull(fasta.Config.Identification.Reference.Sequence, "an unauthored sequence stays null, not \"\"");
            var back = MethodConfigSerializer.Deserialize(MethodConfigSerializer.Serialize(fasta.Config));
            Assert.AreEqual("C:/db/ecoli.fasta", back.Identification.Reference.Fasta);
            Assert.IsNull(back.Identification.Reference.Sequence);
            var refJson = (Dictionary<string, object>)CppIdentification(fasta)["reference"];
            Assert.AreEqual("C:/db/ecoli.fasta", refJson["fasta"]);
            Assert.IsTrue(!refJson.ContainsKey("sequence") || refJson["sequence"] == null,
                "a FASTA reference must not reach C++ with a sequence, or C++ refuses it as both");

            var seq = WithIdentification(@"{ ""reference"": { ""sequence"": ""PEPTIDEK"" } }");
            Assert.IsNull(seq.Config.Identification.Reference.Fasta);
            refJson = (Dictionary<string, object>)CppIdentification(seq)["reference"];
            Assert.AreEqual("PEPTIDEK", refJson["sequence"]);
            Assert.IsTrue(!refJson.ContainsKey("fasta") || refJson["fasta"] == null);

            // Both, neither and empty are carried unchanged for C++ to refuse with its message.
            var both = WithIdentification(@"{ ""reference"": { ""fasta"": ""a.fasta"", ""sequence"": ""PEPTIDEK"" } }");
            refJson = (Dictionary<string, object>)CppIdentification(both)["reference"];
            Assert.AreEqual("a.fasta", refJson["fasta"]);
            Assert.AreEqual("PEPTIDEK", refJson["sequence"]);

            var neither = WithIdentification(@"{ ""reference"": {} }");
            refJson = (Dictionary<string, object>)CppIdentification(neither)["reference"];
            Assert.IsTrue(refJson["fasta"] == null && refJson["sequence"] == null);

            var empty = WithIdentification(@"{ ""reference"": { ""sequence"": """" } }");
            refJson = (Dictionary<string, object>)CppIdentification(empty)["reference"];
            Assert.AreEqual("", refJson["sequence"], "an authored empty sequence stays \"\", so C++ names it empty");
        }

        // ADR-0067: C++ builds fixed_mod into its table and owns every check (FLASHIda_LegacyConfig_test
        // fixed_mod_builds_the_table); C# validates no flashtnt value, so this pins that a non-empty list
        // reaches C++ unchanged rather than being dropped here, and [] stays [].
        [Test, Category("Tier1")]
        public void Identification_FixedMod_NonEmptyReachesCpp()
        {
            const string reference = @"""reference"": { ""sequence"": ""PEPTIDEK"" }";
            var mod = WithIdentification("{ " + reference + @", ""flashtnt"": { ""fixed_mod"": [""Carbamidomethyl (C)""] } }");
            var ft = (Dictionary<string, object>)CppIdentification(mod)["flashtnt"];
            Assert.IsTrue(SameValue(new[] { "Carbamidomethyl (C)" }, ft["fixed_mod"]),
                "a non-empty fixed_mod must reach C++, which builds its table");

            var none = WithIdentification("{ " + reference + @", ""flashtnt"": { ""fixed_mod"": [] } }");
            ft = (Dictionary<string, object>)CppIdentification(none)["flashtnt"];
            Assert.IsTrue(SameValue(new string[0], ft["fixed_mod"]), "[] crosses as []");
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

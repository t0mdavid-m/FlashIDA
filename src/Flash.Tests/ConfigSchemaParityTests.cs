using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using Flash;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// Schema drift guard (C# side). The committed reference config_schema_reference.json is GENERATED
    /// by MethodParameters.GenerateReferenceConfigJson() (the single full-schema source of truth):
    ///   - Reference_IsNeverStale proves the committed file still equals the generator output.
    ///   - Emit_And_Reload_PreserveEveryKey proves ToCppJson round-trips it and the strict loader accepts it.
    ///   - Reject_UnknownKey_Throws proves the loader hard-rejects any key with no home in the schema.
    /// The C++ side (ConfigSchemaParity_test) proves the C++ reader binds every on-disk key. Together the
    /// C# emitter and the C++ reader cannot silently diverge from the single bridge schema.
    ///
    /// To intentionally change the schema: update the model + ToCppJson + BuildFullReferenceConfig, then
    /// regenerate the committed file by running this suite with REGEN_CONFIG_REFERENCE=1.
    /// </summary>
    [TestFixture]
    public class ConfigSchemaParityTests
    {
        private static readonly string ReferencePath = Path.Combine(
            TestContext.CurrentContext.TestDirectory, "..", "test-data", "config_schema_reference.json");

        [Test, Category("Tier1")]
        public void Reference_IsNeverStale()
        {
            string generated = MethodParameters.GenerateReferenceConfigJson();

            if (Environment.GetEnvironmentVariable("REGEN_CONFIG_REFERENCE") == "1")
            {
                File.WriteAllText(ReferencePath, generated);
                Assert.Pass("Regenerated config_schema_reference.json from the generator.");
                return;
            }

            Assert.IsTrue(File.Exists(ReferencePath), "Reference fixture not found: " + ReferencePath);
            var serializer = new JavaScriptSerializer();
            var committed = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(ReferencePath));
            var fresh = serializer.Deserialize<Dictionary<string, object>>(generated);

            var problems = new List<string>();
            DiffLeaves(committed, fresh, problems);
            Assert.IsEmpty(problems,
                "Committed config_schema_reference.json diverged from GenerateReferenceConfigJson(). "
                + "Regenerate it (run with REGEN_CONFIG_REFERENCE=1) after an intentional schema change:\n  "
                + string.Join("\n  ", problems));
        }

        [Test, Category("Tier1")]
        public void Emit_And_Reload_PreserveEveryKey()
        {
            Assert.IsTrue(File.Exists(ReferencePath), "Reference fixture not found: " + ReferencePath);
            var serializer = new JavaScriptSerializer();
            var reference = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(ReferencePath));

            // The strict loader must ACCEPT the generated reference (every key has a schema home).
            MethodParameters mp = null;
            Assert.DoesNotThrow(() => mp = MethodParameters.Load(ReferencePath),
                "The strict loader rejected the generated reference — a key emitted by ToCppJson has no model home.");

            // ToCppJson(Load(reference)) must re-emit every reference key/value (no silent default).
            var emitted = serializer.Deserialize<Dictionary<string, object>>(mp.ToCppJson());
            var refLeaves = new Dictionary<string, object>();
            Flatten(reference, "", refLeaves);
            var emitLeaves = new Dictionary<string, object>();
            Flatten(emitted, "", emitLeaves);

            var problems = new List<string>();
            foreach (var kv in refLeaves)
            {
                object emitVal;
                if (!emitLeaves.TryGetValue(kv.Key, out emitVal))
                    problems.Add(kv.Key + "  (absent from ToCppJson output)");
                else if (!ValuesEqual(kv.Value, emitVal))
                    problems.Add(string.Format("{0}  (reference={1}, emitted={2})", kv.Key, kv.Value, emitVal));
            }
            Assert.IsEmpty(problems,
                "ToCppJson dropped or changed reference keys — the C# emitter and the bridge schema diverged:\n  "
                + string.Join("\n  ", problems));
        }

        /// <summary>
        /// Every scan-config site must emit the source-region keys and scan_rate.
        ///
        /// A key absent from JsonMs2Config never crosses the bridge and is unreachable from
        /// method.json, no matter how completely C++ supports it -- which is exactly what happened:
        /// commit 45c2cf9 trimmed rf_lens/source_cid/source_cid_scaling/scan_rate as "always-default
        /// emit-only keys" while C++ kScanKeys admitted all four and every ScanCommand builder
        /// copied them. Nothing failed, because nothing asserted the emitted key SET.
        ///
        /// ms_settings.ms2, ms_settings.ms3 and every ms_settings.additional_ms2 entry share one
        /// DTO, so all of them are checked -- a regression in any one is a regression in all.
        /// The C++ side asserts the mirror image against kScanKeys (ConfigSchemaParity_test), so
        /// neither side can drop a key the other still expects.
        ///
        /// The additional_ms2 sites are DISCOVERED from the emitted JSON rather than named here.
        /// Both follow-up blocks live there now (tagging.follow_up_scan is a name string, not an
        /// object), and hard-coding the generator's chosen names would make this test quietly stop
        /// covering a site the day someone renames one.
        /// </summary>
        [Test, Category("Tier1")]
        public void Emit_SourceRegion_AtEveryScanLevel()
        {
            var serializer = new JavaScriptSerializer();
            var mp = MethodParameters.Load(ReferencePath);
            var emitted = serializer.Deserialize<Dictionary<string, object>>(mp.ToCppJson());

            var leaves = new Dictionary<string, object>();
            Flatten(emitted, "", leaves);

            string[] sourceRegion = { "rf_lens", "source_cid", "source_cid_scaling" };
            var msnSites = new List<string> { "ms_settings.ms2", "ms_settings.ms3" };

            var msSettings = (Dictionary<string, object>)emitted["ms_settings"];
            object addObj;
            if (msSettings.TryGetValue("additional_ms2", out addObj) && addObj != null)
                foreach (string name in ((Dictionary<string, object>)addObj).Keys)
                    msnSites.Add("ms_settings.additional_ms2." + name);

            Assert.GreaterOrEqual(msnSites.Count, 3,
                "the reference config must define at least one additional_ms2 entry, or this test "
                + "silently stops covering the follow-up scan sites");

            var missing = new List<string>();
            foreach (string site in msnSites)
                foreach (string key in sourceRegion)
                    if (!leaves.ContainsKey(site + "." + key)) missing.Add(site + "." + key);

            // scan_rate is analyzer-side rather than source-region, but it had the same defect at
            // EVERY level including ms1 -- no [JsonKey] anywhere in C# at all.
            foreach (string site in msnSites)
                if (!leaves.ContainsKey(site + ".scan_rate")) missing.Add(site + ".scan_rate");
            if (!leaves.ContainsKey("ms_settings.ms1.scan_rate")) missing.Add("ms_settings.ms1.scan_rate");

            Assert.IsEmpty(missing,
                "ToCppJson omitted scan keys that C++ parses and ScanFactory sends, so they are "
                + "unreachable from method.json:\n  " + string.Join("\n  ", missing));

            // ms1 must NOT gain the five stage-carried keys. kScanKeys is a lenient union that
            // would accept them under ms1, but makeMS1 sets num_stages = 0, so they could never
            // reach an MS1 scan; the C# schema stays deliberately stricter.
            foreach (string key in new[] { "activation", "collision_energy", "reaction_time",
                                           "reagent_max_it", "reagent_agc_target" })
                Assert.IsFalse(leaves.ContainsKey("ms_settings.ms1." + key),
                    "ms_settings.ms1 must not emit the stage-carried key '" + key
                    + "' -- an MS1 command has no isolation stage to carry it.");
        }

        /// <summary>
        /// Source-region parameters inherit from the survey when an MSn scan does not state its own
        /// (ADR-0011), and only then.
        ///
        /// This is the rule most at risk of being "fixed" back out: ADR-0009 says a scan config
        /// fully determines its scan and never inherits, so a future reader who finds
        /// ToJsonScanConfig copying ms1.source_cid into ms2 has an ADR to cite. The reconciliation
        /// is that inheritance is resolved HERE, at emit time -- by the time the JSON crosses the
        /// bridge every ScanConfig carries a concrete value and ADR-0009 holds verbatim. Deleting
        /// this behaviour would put MSn scans back on the instrument method's source settings while
        /// the MS1 that selected the precursor ran on FLASHIda's.
        ///
        /// Zero means inherit: ToCppJson emits every key unconditionally, so there is no "absent"
        /// state on the wire for C++ or anyone else to distinguish.
        /// </summary>
        [Test, Category("Tier1")]
        public void SourceRegion_InheritsFromMs1_UnlessOverridden()
        {
            var mp = MethodParameters.Load(ReferencePath);

            var ms1 = mp.Config.MsSettings.MS1;
            ms1.RFLens = 60; ms1.SourceCID = 15; ms1.SourceCIDScaling = 0.5; ms1.ScanRate = "Turbo";
            mp.Config.MsSettings.MS1 = ms1;

            // ms2: states nothing -> inherits all three.
            var ms2 = mp.Config.MsSettings.MS2;
            ms2.RFLens = 0; ms2.SourceCID = 0; ms2.SourceCIDScaling = 0; ms2.ScanRate = "";
            mp.Config.MsSettings.MS2 = ms2;

            // ms3: states its own source_cid -> keeps it, inherits the rest.
            var ms3 = mp.Config.MsSettings.MS3;
            ms3.RFLens = 0; ms3.SourceCID = 25; ms3.SourceCIDScaling = 0; ms3.ScanRate = "";
            mp.Config.MsSettings.MS3 = ms3;

            var leaves = new Dictionary<string, object>();
            Flatten(new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(mp.ToCppJson()),
                    "", leaves);

            Assert.IsTrue(ValuesEqual(60, leaves["ms_settings.ms2.rf_lens"]),
                "ms2 stated no rf_lens, so it must run at the survey's 60");
            Assert.IsTrue(ValuesEqual(15, leaves["ms_settings.ms2.source_cid"]),
                "ms2 stated no source_cid, so it must run at the survey's 15");
            Assert.IsTrue(ValuesEqual(0.5, leaves["ms_settings.ms2.source_cid_scaling"]),
                "ms2 stated no source_cid_scaling, so it must run at the survey's 0.5");

            Assert.IsTrue(ValuesEqual(25, leaves["ms_settings.ms3.source_cid"]),
                "ms3 stated its own source_cid, which must win over the survey's");
            Assert.IsTrue(ValuesEqual(60, leaves["ms_settings.ms3.rf_lens"]),
                "stating one source-region key must not suppress inheritance of the others");

            // scan_rate is analyzer-side: it describes how this scan measures, not which ions
            // arrive, so it must NOT inherit even though it sits in the same struct.
            Assert.AreEqual("", leaves["ms_settings.ms2.scan_rate"],
                "scan_rate is analyzer-side and must not inherit from ms1");
        }

        [Test, Category("Tier1")]
        public void Reject_UnknownKey_Throws()
        {
            // Each minimal config carries exactly one key with no schema home; the loader must throw
            // (before populating) and name the offending dotted path.
            AssertRejects("{ \"developer\": {} }", "developer");
            AssertRejects("{ \"precursor_selection\": { \"bogus\": 1 } }", "precursor_selection.bogus");
            AssertRejects("{ \"ms_settings\": { \"ms1\": { \"FirstMass\": 1 } } }", "ms_settings.ms1.FirstMass");
            AssertRejects("{ \"ms_settings\": { \"ms3\": { \"IsolationMode\": \"Quadrupole\" } } }",
                "ms_settings.ms3.IsolationMode");
            AssertRejects("{ \"flashtnt\": { \"typo\": 1 } }", "flashtnt.typo");
            AssertRejects("{ \"ms3\": { \"active\": true } }", "ms3");   // legacy top-level section
        }

        private static void AssertRejects(string json, string expectedPathInMessage)
        {
            var ex = Assert.Throws<ArgumentException>(() => MethodConfigSerializer.Deserialize(json),
                "Loader accepted an unknown key it should reject: " + expectedPathInMessage);
            StringAssert.Contains(expectedPathInMessage, ex.Message);
        }

        // Compare two object graphs leaf-by-leaf (both directions).
        private static void DiffLeaves(object committed, object fresh, List<string> problems)
        {
            var a = new Dictionary<string, object>();
            Flatten(committed, "", a);
            var b = new Dictionary<string, object>();
            Flatten(fresh, "", b);
            foreach (var kv in a)
            {
                object bv;
                if (!b.TryGetValue(kv.Key, out bv))
                    problems.Add(kv.Key + "  (in committed, missing from generator)");
                else if (!ValuesEqual(kv.Value, bv))
                    problems.Add(string.Format("{0}  (committed={1}, generator={2})", kv.Key, kv.Value, bv));
            }
            foreach (var kv in b)
                if (!a.ContainsKey(kv.Key))
                    problems.Add(kv.Key + "  (in generator, missing from committed)");
        }

        private static void Flatten(object node, string prefix, Dictionary<string, object> outLeaves)
        {
            var dict = node as Dictionary<string, object>;
            if (dict != null)
            {
                foreach (var kv in dict)
                    Flatten(kv.Value, prefix.Length == 0 ? kv.Key : prefix + "." + kv.Key, outLeaves);
                return;
            }
            var list = node as ArrayList;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                    Flatten(list[i], prefix + "[" + i + "]", outLeaves);
                return;
            }
            outLeaves[prefix] = node;
        }

        private static bool ValuesEqual(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a is bool || b is bool) return a.Equals(b);
            if (a is string && b is string) return (string)a == (string)b;
            double da, db;
            if (TryToDouble(a, out da) && TryToDouble(b, out db))
                return Math.Abs(da - db) <= 1e-6 + 1e-9 * Math.Max(Math.Abs(da), Math.Abs(db));
            return a.ToString() == b.ToString();
        }

        private static bool TryToDouble(object o, out double d)
        {
            try { d = Convert.ToDouble(o); return true; }
            catch { d = 0; return false; }
        }
    }
}

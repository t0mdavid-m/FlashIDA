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

        [Test, Category("Tier1")]
        public void Reject_UnknownKey_Throws()
        {
            // Each minimal config carries exactly one key with no schema home; the loader must throw
            // (before populating) and name the offending dotted path.
            AssertRejects("{ \"developer\": {} }", "developer");
            AssertRejects("{ \"precursor_selection\": { \"bogus\": 1 } }", "precursor_selection.bogus");
            AssertRejects("{ \"ms_settings\": { \"ms1\": { \"FirstMass\": 1 } } }", "ms_settings.ms1.FirstMass");
            AssertRejects("{ \"ms_settings\": { \"ms3\": [ { \"IsolationMode\": \"Quadrupole\" } ] } }",
                "ms_settings.ms3[0].IsolationMode");
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

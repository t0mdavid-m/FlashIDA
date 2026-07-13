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
    /// Schema drift guard (C# side). The shared reference fixture config_schema_reference.json gives
    /// every bridge-schema key a UNIQUE sentinel value; this proves ToCppJson re-emits every reference
    /// key with the value C# loaded. The C++ side (ConfigSchemaParity_test) proves the C++ Config
    /// reader binds every key to the right field. Together with config-schema-drift-reminder.sh, the
    /// C# emitter and the C++ reader cannot silently diverge from the single bridge schema.
    ///
    /// If you add/rename/move a config key: add its sentinel to the fixture and wire both sides.
    /// </summary>
    [TestFixture]
    public class ConfigSchemaParityTests
    {
        private static readonly string ReferencePath = Path.Combine(
            TestContext.CurrentContext.TestDirectory, "..", "test-data", "config_schema_reference.json");

        [Test, Category("Tier1")]
        public void Reference_LoadsWithoutError()
        {
            Assert.IsTrue(File.Exists(ReferencePath), "Reference fixture not found: " + ReferencePath);
            var mp = MethodParameters.Load(ReferencePath);
            Assert.IsNotNull(mp.Config);
            Assert.IsNotEmpty(mp.ToCppJson());
        }

        [Test, Category("Tier1")]
        public void Emit_PreservesEveryReferenceValue()
        {
            var serializer = new JavaScriptSerializer();
            var reference = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(ReferencePath));

            var mp = MethodParameters.Load(ReferencePath);
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
                "ToCppJson dropped or changed reference keys — the C# emitter and the bridge schema diverged. "
                + "Update the fixture + both parity sides in lockstep:\n  " + string.Join("\n  ", problems));
        }

        // Flatten a JavaScriptSerializer object graph into dotted-path leaves; descends objects and
        // arrays (by index). Matches the granularity the C++ per-field assertions cover.
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

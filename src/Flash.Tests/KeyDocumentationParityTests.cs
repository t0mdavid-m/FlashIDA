using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// Key documentation drift guard (ADR-0058). docs/site/method.schema.json is hand-written; this
    /// fixture holds its key set equal to config_schema_reference.json, which Reference_IsNeverStale
    /// in turn holds equal to the C# model. Adding a key to the model therefore needs a schema entry
    /// with a description, or CI fails here.
    /// </summary>
    [TestFixture]
    public class KeyDocumentationParityTests
    {
        // Assembly location, not TestContext, so the fixture also runs under a plain reflection runner.
        private static readonly string Root = Path.Combine(
            Path.GetDirectoryName(typeof(KeyDocumentationParityTests).Assembly.Location), "..");
        private static readonly string SchemaPath = Path.Combine(Root, "docs", "site", "method.schema.json");
        private static readonly string ReferencePath = Path.Combine(Root, "test-data", "config_schema_reference.json");

        private static Dictionary<string, object> Parse(string json)
        {
            var s = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return s.Deserialize<Dictionary<string, object>>(json);
        }

        private static Dictionary<string, object> Load(string path)
        {
            Assert.IsTrue(File.Exists(path), "File not found: " + path);
            return Parse(File.ReadAllText(path));
        }

        private static void AssertNone(List<string> problems, string what)
        {
            Assert.IsEmpty(problems, what + ":\n  " + string.Join("\n  ", problems));
        }

        [Test, Category("Tier1")]
        public void FromSchema_RefWithPropertyNamesFiltersKeys()
        {
            var schema = Parse(@"{""properties"":{
                ""ms1"":{""$ref"":""#/$defs/scan"",""propertyNames"":{""enum"":[""a""]}},
                ""ms2"":{""$ref"":""#/$defs/scan""}},
              ""$defs"":{""scan"":{""type"":""object"",""properties"":{""a"":{},""b"":{}}}}}");
            CollectionAssert.AreEquivalent(new[] { "ms1", "ms1.a", "ms2", "ms2.a", "ms2.b" },
                KeyPaths.FromSchema(schema).Keys);
        }

        [Test, Category("Tier1")]
        public void FromSchema_MapsAndObjectArrays()
        {
            var schema = Parse(@"{""properties"":{
                ""extra"":{""type"":""object"",""additionalProperties"":{""$ref"":""#/$defs/scan""}},
                ""norm"":{""type"":""object"",""additionalProperties"":{""type"":""number""}},
                ""conds"":{""type"":""array"",""items"":{""type"":""object"",""properties"":{""name"":{},""channels"":{}}}}},
              ""$defs"":{""scan"":{""type"":""object"",""properties"":{""a"":{},""b"":{}}}}}");
            CollectionAssert.AreEquivalent(
                new[] { "extra", "extra.*", "extra.*.a", "extra.*.b", "norm", "conds", "conds[]", "conds[].name", "conds[].channels" },
                KeyPaths.FromSchema(schema).Keys);
        }

        [Test, Category("Tier1")]
        public void FromSchema_DanglingRefThrows()
        {
            var schema = Parse(@"{""properties"":{""x"":{""$ref"":""#/$defs/missing""}}}");
            var ex = Assert.Throws<InvalidOperationException>(() => KeyPaths.FromSchema(schema));
            StringAssert.Contains("missing", ex.Message);
        }

        [Test, Category("Tier1")]
        public void Compare_NullAndEmptyReferenceNodesExemptTheirSubtree()
        {
            var schema = Parse(@"{""properties"":{""e1"":{""$ref"":""#/$defs/expl""},""e2"":{""$ref"":""#/$defs/expl""}},
              ""$defs"":{""expl"":{""type"":""object"",""properties"":{
                ""km"":{""type"":""object"",""properties"":{""k2"":{""type"":""object"",""additionalProperties"":{""type"":""number""}}}}}}}}");
            var schemaPaths = KeyPaths.FromSchema(schema).Keys;

            var r = KeyPaths.FromReference(Parse(@"{""e1"":{""km"":null},""e2"":{""km"":{""k2"":{""CID"":0.01}}}}"), schema);
            AssertNone(KeyPaths.Undocumented(r, schemaPaths), "undocumented");
            AssertNone(KeyPaths.Absent(r, schemaPaths), "absent");

            var r2 = KeyPaths.FromReference(Parse(@"{""e1"":{""km"":null},""e2"":{""km"":{}}}"), schema);
            AssertNone(KeyPaths.Absent(r2, schemaPaths), "absent with both instances uncovered");
        }

        [Test, Category("Tier1")]
        public void Compare_ReportsBothDirections()
        {
            var schema = Parse(@"{""properties"":{""a"":{},""b"":{}}}");
            var r = KeyPaths.FromReference(Parse(@"{""a"":1,""c"":2}"), schema);
            var schemaPaths = KeyPaths.FromSchema(schema).Keys;
            CollectionAssert.AreEqual(new[] { "c" }, KeyPaths.Undocumented(r, schemaPaths));
            CollectionAssert.AreEqual(new[] { "b" }, KeyPaths.Absent(r, schemaPaths));
        }

        [Test, Category("Tier1")]
        public void Schema_DocumentsEveryKeyOfTheReference()
        {
            var schema = Load(SchemaPath);
            var r = KeyPaths.FromReference(Load(ReferencePath), schema);
            AssertNone(KeyPaths.Undocumented(r, KeyPaths.FromSchema(schema).Keys),
                "Keys in config_schema_reference.json with no entry in docs/site/method.schema.json");
        }

        [Test, Category("Tier1")]
        public void Schema_DocumentsNoKeyTheModelLacks()
        {
            var schema = Load(SchemaPath);
            var r = KeyPaths.FromReference(Load(ReferencePath), schema);
            AssertNone(KeyPaths.Absent(r, KeyPaths.FromSchema(schema).Keys),
                "Keys documented in docs/site/method.schema.json that config_schema_reference.json does not have");
        }

        [Test, Category("Tier1")]
        public void Schema_EveryDocumentedKeyHasADescription()
        {
            var missing = new List<string>();
            foreach (var kv in KeyPaths.FromSchema(Load(SchemaPath)))
            {
                if (kv.Value == null) continue; // synthetic "*" / "[]" node
                object d;
                if (!kv.Value.TryGetValue("description", out d) || string.IsNullOrWhiteSpace(d as string))
                    missing.Add(kv.Key);
            }
            AssertNone(missing, "Documented keys with no description");
        }
    }
}

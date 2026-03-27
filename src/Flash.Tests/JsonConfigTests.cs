using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using Flash;
using Flash.IDA;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// Phase 1 unit tests: verify JSON serialization of method configuration.
    /// </summary>
    [TestFixture]
    public class JsonConfigTests
    {
        private static readonly string TestDataDir = Path.Combine(
            TestContext.CurrentContext.TestDirectory, "..", "..", "test-data");

        private static readonly string ConfigsDir = Path.Combine(TestDataDir, "configs");

        /// <summary>
        /// Load MethodParameters from a test config XML file.
        /// </summary>
        private MethodParameters LoadMethod(string xmlName)
        {
            string path = Path.Combine(ConfigsDir, xmlName);
            Assert.IsTrue(File.Exists(path), "Test config not found: " + path);
            return MethodParameters.Load(path);
        }

        // P1-U01: ToJSON() produces valid JSON (parseable by JavaScriptSerializer)
        [Test]
        [Category("Tier1")]
        public void P1_U01_ToJSON_ProducesValidJson()
        {
            var mp = LoadMethod("method_default.xml");
            string json = mp.IDA.ToJSON(mp);

            Assert.IsNotNull(json);
            Assert.IsNotEmpty(json);

            // Must start with '{' (this is what the C++ auto-detect checks)
            Assert.IsTrue(json.StartsWith("{"), "JSON must start with '{'");

            // Must be parseable
            var serializer = new JavaScriptSerializer();
            var parsed = serializer.Deserialize<Dictionary<string, object>>(json);
            Assert.IsNotNull(parsed, "JSON could not be deserialized");
        }

        // P1-U02: JSON contains all 9 required top-level keys
        [Test]
        [Category("Tier1")]
        public void P1_U02_ToJSON_ContainsAllTopLevelKeys()
        {
            var mp = LoadMethod("method_default.xml");
            string json = mp.IDA.ToJSON(mp);

            var serializer = new JavaScriptSerializer();
            var parsed = serializer.Deserialize<Dictionary<string, object>>(json);

            string[] requiredKeys = new[]
            {
                "deconvolution", "precursor_selection", "tagging",
                "quantification", "faims", "ms_settings",
                "scheduling", "exploration", "files"
            };

            foreach (var key in requiredKeys)
            {
                Assert.IsTrue(parsed.ContainsKey(key),
                    "Missing required top-level key: " + key);
            }
        }

        // P1-U03: Field values from method_default.xml match expected values
        [Test]
        [Category("Tier1")]
        public void P1_U03_ToJSON_FieldValuesMatchXml()
        {
            var mp = LoadMethod("method_default.xml");
            string json = mp.IDA.ToJSON(mp);

            var serializer = new JavaScriptSerializer();
            var parsed = serializer.Deserialize<Dictionary<string, object>>(json);

            // Check deconvolution values match method_default.xml
            var deconv = (Dictionary<string, object>)parsed["deconvolution"];
            Assert.AreEqual(4, deconv["min_charge"]);
            Assert.AreEqual(50, deconv["max_charge"]);
            Assert.AreEqual(500.0, Convert.ToDouble(deconv["min_mass"]), 0.001);
            Assert.AreEqual(50000.0, Convert.ToDouble(deconv["max_mass"]), 0.001);
            Assert.AreEqual(0.9, Convert.ToDouble(deconv["tqscore_threshold"]), 0.001);

            // Check precursor_selection
            var ps = (Dictionary<string, object>)parsed["precursor_selection"];
            Assert.AreEqual(180.0, Convert.ToDouble(ps["RT_window"]), 0.001);
            Assert.AreEqual(0, ps["target_mode"]);
            Assert.AreEqual(29, ps["HCDEnergy"]);

            // Check tagging defaults
            var tagging = (Dictionary<string, object>)parsed["tagging"];
            Assert.AreEqual(3, tagging["min_tag_length"]);
            Assert.AreEqual(8, tagging["max_tag_length"]);
            Assert.AreEqual(3, tagging["max_ptm_count"]);
        }

        // P1-U04: ms_settings.ms2 is an array matching XML MS2 count
        [Test]
        [Category("Tier1")]
        public void P1_U04_ToJSON_Ms2IsArrayMatchingXml()
        {
            var mp = LoadMethod("method_default.xml");
            string json = mp.IDA.ToJSON(mp);

            var serializer = new JavaScriptSerializer();
            var parsed = serializer.Deserialize<Dictionary<string, object>>(json);

            var msSettings = (Dictionary<string, object>)parsed["ms_settings"];
            var ms2Array = (System.Collections.ArrayList)msSettings["ms2"];

            // method_default.xml has 1 MS2 entry
            Assert.AreEqual(mp.MS2.Count, ms2Array.Count,
                "ms2 array length should match XML MS2 count");
            Assert.AreEqual(1, ms2Array.Count);

            // Check first MS2 entry has activation
            var firstMs2 = (Dictionary<string, object>)ms2Array[0];
            Assert.AreEqual("ETD", firstMs2["activation"]);
        }

        // P1-U05: Round-trip: FAIMS cv_values array and scheduling keys survive
        [Test]
        [Category("Tier1")]
        public void P1_U05_ToJSON_RoundTripArraysAndNested()
        {
            var mp = LoadMethod("method_default.xml");
            string json = mp.IDA.ToJSON(mp);

            var serializer = new JavaScriptSerializer();
            var parsed = serializer.Deserialize<Dictionary<string, object>>(json);

            // FAIMS cv_values should be an array
            var faims = (Dictionary<string, object>)parsed["faims"];
            var cvValues = (System.Collections.ArrayList)faims["cv_values"];
            Assert.IsNotNull(cvValues, "faims.cv_values should be an array");
            Assert.IsTrue(cvValues.Count > 0, "faims.cv_values should not be empty");

            // scheduling should have nested cycle_time and scan_timeout
            var scheduling = (Dictionary<string, object>)parsed["scheduling"];
            Assert.IsTrue(scheduling.ContainsKey("cycle_time"), "scheduling must have cycle_time");
            Assert.IsTrue(scheduling.ContainsKey("scan_timeout"), "scheduling must have scan_timeout");

            var cycleTime = (Dictionary<string, object>)scheduling["cycle_time"];
            Assert.IsTrue(cycleTime.ContainsKey("enabled"), "cycle_time must have enabled");
            Assert.IsTrue(cycleTime.ContainsKey("value_ms"), "cycle_time must have value_ms");
        }

        // P1-U05b: Round-trip with multi-MS2 config (method_json_roundtrip.xml)
        [Test]
        [Category("Tier1")]
        public void P1_U05b_ToJSON_MultiMs2RoundTrip()
        {
            string roundtripPath = Path.Combine(ConfigsDir, "method_json_roundtrip.xml");
            if (!File.Exists(roundtripPath))
            {
                Assert.Ignore("method_json_roundtrip.xml not yet created");
                return;
            }

            var mp = LoadMethod("method_json_roundtrip.xml");
            string json = mp.IDA.ToJSON(mp);

            var serializer = new JavaScriptSerializer();
            var parsed = serializer.Deserialize<Dictionary<string, object>>(json);

            // Should have multiple MS2 entries
            var msSettings = (Dictionary<string, object>)parsed["ms_settings"];
            var ms2Array = (System.Collections.ArrayList)msSettings["ms2"];
            Assert.AreEqual(mp.MS2.Count, ms2Array.Count);
            Assert.GreaterOrEqual(ms2Array.Count, 2,
                "method_json_roundtrip.xml should have at least 2 MS2 entries");

            // Non-default FAIMS CVs
            var faims = (Dictionary<string, object>)parsed["faims"];
            var cvValues = (System.Collections.ArrayList)faims["cv_values"];
            Assert.AreEqual(3, cvValues.Count, "Should have 3 FAIMS CVs");

            // Non-default charge range
            var deconv = (Dictionary<string, object>)parsed["deconvolution"];
            Assert.AreEqual(5, deconv["min_charge"]);
            Assert.AreEqual(40, deconv["max_charge"]);
        }
    }
}

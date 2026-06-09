using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using Flash;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// Capture utilities that write ToCppJson() output to disk during CI.
    /// Always pass — the CI workflow uploads the output as an artifact.
    /// </summary>
    [TestFixture]
    [Category("GoldenCapture")]
    public class GoldenCaptureTests
    {
        private static readonly string TestDataDir = Path.Combine(
            TestContext.CurrentContext.TestDirectory, "..", "test-data");
        private static readonly string OutputDir = Path.Combine(
            TestContext.CurrentContext.TestDirectory, "..", "test-output", "json");

        [OneTimeSetUp]
        public void EnsureOutputDir() => Directory.CreateDirectory(OutputDir);

        [Test]
        public void CaptureConfigDefault()
        {
            var mp = MethodParameters.Load(Path.Combine(TestDataDir, "configs", "method_default.json"));
            string json = mp.ToCppJson();
            var serializer = new JavaScriptSerializer();
            var parsed = serializer.Deserialize<Dictionary<string, object>>(json);
            Assert.That(parsed, Does.ContainKey("deconvolution"),
                "Config JSON must contain 'deconvolution' section");
            Assert.That(parsed, Does.ContainKey("precursor_selection"),
                "Config JSON must contain 'precursor_selection' section");
            File.WriteAllText(Path.Combine(OutputDir, "config_default.json"), json);
        }

        [Test]
        public void CaptureConfigFull()
        {
            string jsonPath = Path.Combine(TestDataDir, "configs", "method_json_roundtrip.json");
            Assert.That(File.Exists(jsonPath), Is.True,
                "method_json_roundtrip.json not found at " + jsonPath);
            var mp = MethodParameters.Load(jsonPath);
            string json = mp.ToCppJson();
            var serializer = new JavaScriptSerializer();
            var parsed = serializer.Deserialize<Dictionary<string, object>>(json);
            Assert.That(parsed, Does.ContainKey("deconvolution"),
                "Config JSON must contain 'deconvolution' section");
            Assert.That(parsed, Does.ContainKey("precursor_selection"),
                "Config JSON must contain 'precursor_selection' section");
            File.WriteAllText(Path.Combine(OutputDir, "config_full.json"), json);
        }
    }
}

using System.IO;
using Flash;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// Capture utilities that write ToJSON() output to disk during CI.
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
            var mp = MethodParameters.Load(Path.Combine(TestDataDir, "configs", "method_default.xml"));
            string json = mp.IDA.ToJSON(mp);
            Assert.IsTrue(json.StartsWith("{"), "JSON must start with '{'");
            File.WriteAllText(Path.Combine(OutputDir, "config_default.json"), json);
        }

        [Test]
        public void CaptureConfigFull()
        {
            string xmlPath = Path.Combine(TestDataDir, "configs", "method_json_roundtrip.xml");
            if (!File.Exists(xmlPath)) { Assert.Ignore("method_json_roundtrip.xml not present"); return; }
            var mp = MethodParameters.Load(xmlPath);
            string json = mp.IDA.ToJSON(mp);
            Assert.IsTrue(json.StartsWith("{"), "JSON must start with '{'");
            File.WriteAllText(Path.Combine(OutputDir, "config_full.json"), json);
        }
    }
}

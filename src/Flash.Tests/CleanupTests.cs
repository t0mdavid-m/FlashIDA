using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Flash.IDA;
using NUnit.Framework;

namespace Flash.Tests
{
    [TestFixture]
    public class CleanupTests
    {
        /// <summary>
        /// P8-U01: Exactly 5 DllImport declarations remain in FLASHIdaWrapper.
        /// Uses reflection to count methods decorated with DllImportAttribute.
        /// </summary>
        [Test]
        public void P8_U01_ExactlyFiveDllImports()
        {
            var wrapperType = typeof(FLASHIdaWrapper);
            var dllImportMethods = wrapperType
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(m => m.GetCustomAttribute<DllImportAttribute>() != null)
                .ToList();

            Assert.AreEqual(5, dllImportMethods.Count,
                "Expected exactly 5 [DllImport] declarations. Found: " +
                string.Join(", ", dllImportMethods.Select(m => m.Name)));
        }

        /// <summary>
        /// P8-U02: No reference to ToFLASHDeconvInput outside test files.
        /// Scans source files for the legacy method name.
        /// </summary>
        [Test]
        public void P8_U02_NoToFLASHDeconvInputReferences()
        {
            // Navigate from bin/ up to src/Flash/
            string testDir = TestContext.CurrentContext.TestDirectory;
            string srcDir = Path.Combine(testDir, "..", "src", "Flash");

            // If running from a different layout, try alternative path
            if (!Directory.Exists(srcDir))
            {
                srcDir = Path.Combine(testDir, "..", "..", "..", "src", "Flash");
            }

            if (!Directory.Exists(srcDir))
            {
                Assert.Inconclusive("Could not locate src/Flash directory from " + testDir);
            }

            var csFiles = Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith("CleanupTests.cs", StringComparison.OrdinalIgnoreCase));

            var hits = csFiles
                .Where(f => File.ReadAllText(f).Contains("ToFLASHDeconvInput"))
                .Select(f => Path.GetFileName(f))
                .ToList();

            Assert.AreEqual(0, hits.Count,
                "ToFLASHDeconvInput still referenced in: " + string.Join(", ", hits));
        }

        /// <summary>
        /// P8-U03: MethodDocGenerator produces correct output for IDAParameters.
        /// Verifies the reflection utility reads [Description] attributes.
        /// </summary>
        [Test]
        public void P8_U03_MethodDocGeneratorProducesOutput()
        {
            string output = MethodDocGenerator.Generate(typeof(IDAParameters));

            Assert.IsNotEmpty(output, "MethodDocGenerator returned empty string");
            Assert.That(output, Does.Contain("QScoreThreshold"),
                "Output should contain QScoreThreshold");
            Assert.That(output, Does.Contain("MinCharge"),
                "Output should contain MinCharge");
            Assert.That(output, Does.Contain("HCDEnergy"),
                "Output should contain HCDEnergy");
        }
    }
}

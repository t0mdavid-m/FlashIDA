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
        /// P8-U03: MethodDocGenerator produces correct output for MethodConfig.
        /// Verifies the reflection utility reads [Description] attributes.
        /// </summary>
        [Test]
        public void P8_U03_MethodDocGeneratorProducesOutput()
        {
            string output = MethodDocGenerator.Generate(typeof(Flash.MethodConfig));

            Assert.IsNotEmpty(output, "MethodDocGenerator returned empty string");
            Assert.That(output, Does.Contain("score_threshold"),
                "Output should contain score_threshold");
            Assert.That(output, Does.Contain("min_charge"),
                "Output should contain min_charge");
            Assert.That(output, Does.Contain("HCDEnergy"),
                "Output should contain HCDEnergy (bridge key)");
        }
    }
}

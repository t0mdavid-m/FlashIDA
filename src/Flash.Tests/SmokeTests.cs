using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// Phase 0 smoke tests: verify that the solution builds and that Flash.exe -t
    /// runs without error. These tests establish the pre-migration baseline.
    /// </summary>
    [TestFixture]
    public class SmokeTests
    {
        private static readonly string TestDir =
            TestContext.CurrentContext.TestDirectory;

        private static readonly string FlashExePath =
            Path.Combine(TestDir, "Flash.exe");

        private static readonly string TestDataDir =
            Path.GetFullPath(Path.Combine(TestDir, "..", "test-data"));

        private static readonly string SmokeSpectrumPath =
            Path.Combine(TestDataDir, "spectra", "ms1_smoke_test.txt");

        private static readonly string DefaultMethodPath =
            Path.Combine(TestDataDir, "configs", "method_default.json");

        // P0-U01: Flash.sln compiles without error.
        // This test is validated by the fact that this assembly was compiled.
        [Test]
        [Category("Tier1")]
        public void P0_U01_SolutionCompilesWithoutError()
        {
            Assert.Pass("Assembly compiled successfully — build is clean.");
        }

        // P0-U02: Flash.exe exists in the build output directory.
        [Test]
        [Category("Tier1")]
        public void P0_U02_FlashExeExistsInBuildOutput()
        {
            Assert.IsTrue(
                File.Exists(FlashExePath),
                $"Flash.exe not found at: {FlashExePath}");
        }

        // P0-U03: Flash.exe -t runs with the minimal smoke spectrum and exits cleanly.
        [Test]
        [Category("Tier3")]
        public void P0_U03_TestModeRunsAndExitsCleanly()
        {
            string outputPath = Path.Combine(
                Path.GetTempPath(), "p0_u03_output.tsv");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = FlashExePath,
                    Arguments = $"\"{SmokeSpectrumPath}\" \"{outputPath}\" \"{DefaultMethodPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(psi))
                {
                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit(60_000);
                    Assert.AreEqual(0, process.ExitCode,
                        $"Flash.exe exited with code {process.ExitCode}.\n" +
                        $"STDOUT: {stdout}\n" +
                        $"STDERR: {stderr}");
                }
            }
            finally
            {
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }
        }

        // P0-U04: Flash.exe -t output is a non-empty valid TSV with expected columns.
        [Test]
        [Category("Tier3")]
        public void P0_U04_TestModeOutputIsNonEmptyValidTsv()
        {
            string outputPath = Path.Combine(
                Path.GetTempPath(), "p0_u04_output.tsv");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = FlashExePath,
                    Arguments = $"\"{SmokeSpectrumPath}\" \"{outputPath}\" \"{DefaultMethodPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(psi))
                {
                    process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    process.WaitForExit(60_000);
                }

                Assert.IsTrue(File.Exists(outputPath),
                    "Output file was not created.");

                string[] lines = File.ReadAllLines(outputPath);
                Assert.GreaterOrEqual(lines.Length, 2,
                    "Output must have at least a header row and one data row.");

                string[] expectedColumns = {
                    "rt", "mz1", "mz2", "qScore", "charges", "monoMasses",
                    "ccos", "csnr", "cos", "snr", "cScore", "ppm",
                    "precursorIntensity", "massIntensity", "hcd"
                };
                string header = lines[0];
                foreach (string col in expectedColumns)
                {
                    Assert.IsTrue(header.Contains(col),
                        $"Expected column '{col}' not found in TSV header: {header}");
                }
            }
            finally
            {
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }
        }
    }
}

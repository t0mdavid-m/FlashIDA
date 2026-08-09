using System.Globalization;
using Flash.IDA;
using Flash.Tests.Mocks;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// Pins that every number reaching the instrument is formatted with InvariantCulture,
    /// regardless of the machine's locale.
    /// </summary>
    /// <remarks>
    /// This is not cosmetic. In the iAPI scan-parameter grammar a ',' separates parallel
    /// co-isolation windows within one MS stage, while ';' descends an MSn stage
    /// (docs/kb/scan-pipeline/multi-notch-wire-grammar.md). So on a comma-decimal locale a
    /// precursor m/z of 1000.5 rendered by a culture-sensitive ToString() becomes "1000,5" — a
    /// well-formed request for TWO isolation windows, at m/z 1000 and m/z 5. The instrument cannot
    /// distinguish that from a deliberate multiplexed isolation, so it fails silently: the scan
    /// still happens, at the wrong geometry, with the ion budget split across a junk second window.
    ///
    /// Why this needs [SetCulture] rather than just running: GitHub's windows-2022 runners are
    /// en-US, where "." is already the decimal separator and the defect is invisible. The culture
    /// must be IMPOSED by the test, not inherited from the host, or CI can never observe it.
    ///
    /// Why it is not vacuous: MockScanFactory calls the base class's own protected FillParameters.
    /// It previously carried a hand-copied FillParametersMock, and a test written against that copy
    /// would have passed while production still emitted "1000,5".
    /// </remarks>
    [TestFixture]
    public class ScanFactoryCultureTests
    {
        /// <summary>An MS2 command whose m/z, width and injection time all have fractional parts.</summary>
        private static ScanCommand FractionalMs2Command()
        {
            var cmd = new ScanCommand();
            cmd.MsnLevel = 2;
            cmd.NumStages = 1;
            cmd.Analyzer = "Orbitrap";
            cmd.MaxIt = 246.5;              // exercises the scalar (non-array) branch
            var stages = new IsolationStage[10];
            stages[0].PrecursorMz = 1000.5;
            stages[0].IsolationWidth = 3.25;
            stages[0].ChargeState = 17;
            stages[0].CollisionEnergy = 30;
            stages[0].ActivationType = "HCD";
            cmd.Stages = stages;
            return cmd;
        }

        [Test, Category("Tier1")]
        [SetCulture("de-DE")]
        public void EmitsInvariantNumbers_UnderCommaDecimalCulture()
        {
            // Guard the premise: if this ever stops holding, the test below proves nothing.
            Assert.That(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator,
                Is.EqualTo(","),
                "[SetCulture(\"de-DE\")] did not take effect, so this test cannot detect the defect");

            var scan = new MockScanFactory().BuildFromCommand(FractionalMs2Command());

            // Array branch. A ',' here would be read by the instrument as a second isolation notch.
            Assert.That(scan.Values["PrecursorMass"], Is.EqualTo("1000.5"),
                "a comma-decimal m/z is a well-formed TWO-notch isolation request, not a bad number");
            Assert.That(scan.Values["IsolationWidth"], Is.EqualTo("3.25"));

            // Scalar branch.
            Assert.That(scan.Values["MaxIT"], Is.EqualTo("246.5"));

            // Nothing that crosses to the instrument may carry a decimal comma.
            foreach (var kv in scan.Values)
            {
                Assert.That(kv.Value, Does.Not.Match(@"\d,\d"),
                    "scan parameter '" + kv.Key + "' = '" + kv.Value + "' contains a decimal comma, " +
                    "which the iAPI grammar reads as a co-isolation notch separator");
            }
        }

        [Test, Category("Tier1")]
        public void EmitsIdenticalNumbers_UnderInvariantAndCommaDecimalCulture()
        {
            var prev = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                var invariant = new MockScanFactory().BuildFromCommand(FractionalMs2Command()).Values;

                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                var german = new MockScanFactory().BuildFromCommand(FractionalMs2Command()).Values;

                Assert.That(german.Count, Is.EqualTo(invariant.Count),
                    "the emitted key set must not depend on the machine locale");
                foreach (var kv in invariant)
                {
                    Assert.That(german[kv.Key], Is.EqualTo(kv.Value),
                        "scan parameter '" + kv.Key + "' differs between locales");
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = prev;
            }
        }
    }
}

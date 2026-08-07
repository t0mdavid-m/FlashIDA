using System.Collections.Generic;
using Flash;
using Flash.IDA;
using Flash.Tests.Mocks;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// ScanCommand -> Thermo scan-request binding.
    ///
    /// This is the last hop before the instrument and, until now, the least covered: grepping the
    /// suite for SourceCid / SrcRFLens / Microscans returned only byte-offset assertions, so every
    /// scalar could be dropped on the floor without a single test noticing. Two defects lived here
    /// undetected -- SourceCIDScalingFactor was never sent on any scan at any level, and FLASHIda
    /// could command FAIMS "on" but never "off".
    ///
    /// Values keys are ScanParameters field names with '_' replaced by ' '
    /// (ScanFactory.FillParameters); the names below have no underscores, so they ship verbatim.
    /// </summary>
    [TestFixture]
    public class ScanFactoryTests
    {
        /// <summary>An MS1-level command: num_stages 0, so BuildFromCommand skips the stage block
        /// entirely and the scalar bindings under test are the only thing exercised.</summary>
        private static ScanCommand Ms1Command()
        {
            return new ScanCommand
            {
                ScanId = 1,
                MsnLevel = 1,
                NumStages = 0,
                Stages = new IsolationStage[10],
                Analyzer = "Orbitrap",
                ScanDescription = "!!!S",
                FirstMass = 500,
                LastMass = 2000,
                OrbitrapResolution = 120000,
                AgcTarget = 800000,
                MaxIt = 246,
            };
        }

        private static IDictionary<string, string> Build(ScanCommand cmd)
        {
            return new MockScanFactory().BuildFromCommand(cmd).Values;
        }

        // ------------------------------------------------------------------
        // Source region (ADR-0011)
        // ------------------------------------------------------------------

        /// <summary>
        /// The source-region group travels as a unit and is always sent, including a scaling factor
        /// of 0.
        ///
        /// source_cid_scaling is the case that matters: 0 is its documented correct value (the
        /// shipped etc/method.json sets it, MethodParameters says it should be zero), so the old
        /// `if (cmd.SourceCidScaling > 0)` guard discarded the only value anyone configures.
        /// SourceCIDScalingFactor was therefore never sent on any scan, at any MS level, and the
        /// instrument silently applied whatever scaling its own method carried.
        ///
        /// Under the old guards this test fails on the scaling assertion with a KeyNotFoundException
        /// -- the key is absent, not zero.
        /// </summary>
        [Test, Category("Tier1")]
        public void SourceRegion_IsAlwaysSent_IncludingZeroScaling()
        {
            var cmd = Ms1Command();
            cmd.RfLens = 60;
            cmd.SourceCid = 15;
            cmd.SourceCidScaling = 0;

            var v = Build(cmd);

            Assert.IsTrue(v.ContainsKey("SrcRFLens"), "SrcRFLens must be sent");
            Assert.IsTrue(v.ContainsKey("SourceCIDEnergy"), "SourceCIDEnergy must be sent");
            Assert.IsTrue(v.ContainsKey("SourceCIDScalingFactor"),
                "SourceCIDScalingFactor must be sent even when 0 -- 0 is a real setting for the "
                + "source region, not the 'leave it to the instrument method' sentinel that the "
                + "analyzer-side scalars use.");

            Assert.AreEqual("60", v["SrcRFLens"]);
            Assert.AreEqual("15", v["SourceCIDEnergy"]);
            Assert.AreEqual("0", v["SourceCIDScalingFactor"]);
        }

        /// <summary>
        /// The whole group is sent even when every member is 0, so an unconfigured source region is
        /// commanded explicitly rather than left to the instrument. This is what makes makeAGC's
        /// source region load-bearing: an AGC command that left these at 0 now actively commands
        /// RF lens 0 instead of omitting the key.
        /// </summary>
        [Test, Category("Tier1")]
        public void SourceRegion_AllZero_StillSendsEveryKey()
        {
            var v = Build(Ms1Command());

            Assert.AreEqual("0", v["SrcRFLens"]);
            Assert.AreEqual("0", v["SourceCIDEnergy"]);
            Assert.AreEqual("0", v["SourceCIDScalingFactor"]);
        }

        /// <summary>
        /// Analyzer-side scalars keep absence semantics: 0 / "" means "use the instrument method
        /// default" for them, so they must still be omitted. Guards against a fix that unguards
        /// everything -- microscans 0 is not a scan anyone can acquire.
        /// </summary>
        [Test, Category("Tier1")]
        public void AnalyzerSideScalars_StillOmittedWhenUnset()
        {
            var cmd = Ms1Command();
            cmd.Microscans = 0;
            cmd.DataType = "";
            cmd.ScanRate = "";

            var v = Build(cmd);

            Assert.IsFalse(v.ContainsKey("Microscans"), "microscans 0 means 'method default'");
            Assert.IsFalse(v.ContainsKey("DataType"), "empty data_type means 'method default'");
            Assert.IsFalse(v.ContainsKey("ScanRate"), "empty scan_rate means 'method default'");
        }

        // ------------------------------------------------------------------
        // FAIMS (ADR-0012)
        // ------------------------------------------------------------------

        /// <summary>
        /// FAIMS off is an instruction, not an omission.
        ///
        /// The old code was `if (Math.Abs(cmd.FaimsCv) > 0.001) { ...on... }` with no else, so a
        /// non-FAIMS run sent neither FAIMS key and the instrument stayed on whatever FAIMS state
        /// its own method carried. Pre-port sent FAIMS_Voltages = "off" for exactly this reason.
        /// </summary>
        [Test, Category("Tier1")]
        public void Faims_Disabled_CommandsVoltagesOff()
        {
            var cmd = Ms1Command();
            cmd.FaimsEnabled = 0;
            cmd.FaimsCv = 0;

            var v = Build(cmd);

            Assert.AreEqual("off", v["FAIMS Voltages"],
                "a run with no FAIMS must command it off, not stay silent");
            Assert.IsFalse(v.ContainsKey("FAIMS CV"), "no CV to command when FAIMS is off");
        }

        [Test, Category("Tier1")]
        public void Faims_Enabled_CommandsCvAndOn()
        {
            var cmd = Ms1Command();
            cmd.FaimsEnabled = 1;
            cmd.FaimsCv = -45;

            var v = Build(cmd);

            Assert.AreEqual("on", v["FAIMS Voltages"]);
            Assert.AreEqual("-45", v["FAIMS CV"]);
        }

        /// <summary>
        /// A compensation voltage of exactly 0 is a legitimate FAIMS setting and is now
        /// expressible. Under the old |cv| > 0.001 test it was indistinguishable from "no FAIMS",
        /// so it was silently dropped -- the same guard-family defect as source_cid_scaling.
        ///
        /// This is the test that fails if anyone reintroduces a magnitude test in place of the
        /// explicit flag.
        /// </summary>
        [Test, Category("Tier1")]
        public void Faims_EnabledAtCvZero_StillCommandsOn()
        {
            var cmd = Ms1Command();
            cmd.FaimsEnabled = 1;
            cmd.FaimsCv = 0;

            var v = Build(cmd);

            Assert.AreEqual("on", v["FAIMS Voltages"],
                "CV 0 with FAIMS enabled is a real setting, not an absent one");
            Assert.AreEqual("0", v["FAIMS CV"]);
        }
    }
}

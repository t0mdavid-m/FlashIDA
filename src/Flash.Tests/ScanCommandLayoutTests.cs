using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Flash.IDA;
using Flash.Tests.Mocks;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// Phase 3 layout tests: verify that C# struct marshalling matches C++ layout.
    /// Hard-coded offsets verified by C++ static_assert and ScanCommandLayout_test binary.
    /// </summary>
    [TestFixture]
    public class ScanCommandLayoutTests
    {
        // P3-U01: ScanCommand is exactly 2048 bytes (scan parameter expansion + reserved block)
        [Test, Category("Tier1")]
        public void P3_U01_ScanCommand_SizeMatchesCpp()
        {
            Assert.AreEqual(2048, Marshal.SizeOf<ScanCommand>(),
                "ScanCommand must be 2048 bytes to match C++ layout");
        }

        // P3-U02: IsolationStage is exactly 80 bytes
        [Test, Category("Tier1")]
        public void P3_U02_IsolationStage_SizeMatchesCpp()
        {
            Assert.AreEqual(80, Marshal.SizeOf<IsolationStage>(),
                "IsolationStage must be 80 bytes to match C++ layout");
        }

        // P3-U03: Field offsets match C++ offsetof values
        [Test, Category("Tier1")]
        public void P3_U03_ScanCommand_FieldOffsetsMatchCpp()
        {
            // ScanCommand field offsets (from C++ layout)
            Assert.AreEqual(0, (int)Marshal.OffsetOf<ScanCommand>("ScanId"), "ScanId offset");
            Assert.AreEqual(4, (int)Marshal.OffsetOf<ScanCommand>("MsnLevel"), "MsnLevel offset");
            Assert.AreEqual(8, (int)Marshal.OffsetOf<ScanCommand>("Priority"), "Priority offset");
            Assert.AreEqual(12, (int)Marshal.OffsetOf<ScanCommand>("IsAgc"), "IsAgc offset");
            Assert.AreEqual(16, (int)Marshal.OffsetOf<ScanCommand>("NumStages"), "NumStages offset");
            Assert.AreEqual(20, (int)Marshal.OffsetOf<ScanCommand>("OrbitrapResolution"), "OrbitrapResolution offset");
            Assert.AreEqual(24, (int)Marshal.OffsetOf<ScanCommand>("AgcTarget"), "AgcTarget offset");
            Assert.AreEqual(28, (int)Marshal.OffsetOf<ScanCommand>("Pad1"), "Pad1 offset");
            Assert.AreEqual(32, (int)Marshal.OffsetOf<ScanCommand>("FirstMass"), "FirstMass offset");
            Assert.AreEqual(40, (int)Marshal.OffsetOf<ScanCommand>("LastMass"), "LastMass offset");
            Assert.AreEqual(48, (int)Marshal.OffsetOf<ScanCommand>("MaxIt"), "MaxIt offset");
            Assert.AreEqual(56, (int)Marshal.OffsetOf<ScanCommand>("Analyzer"), "Analyzer offset");
            Assert.AreEqual(88, (int)Marshal.OffsetOf<ScanCommand>("ScanDescription"), "ScanDescription offset");
            Assert.AreEqual(344, (int)Marshal.OffsetOf<ScanCommand>("Stages"), "Stages offset");
            Assert.AreEqual(1144, (int)Marshal.OffsetOf<ScanCommand>("EnqueueTimestampMs"), "EnqueueTimestampMs offset");
            Assert.AreEqual(1152, (int)Marshal.OffsetOf<ScanCommand>("DequeueTimestampMs"), "DequeueTimestampMs offset");

            // Scoring fields (after DequeueTimestampMs at 1152 + 8 = 1160)
            Assert.AreEqual(1160, (int)Marshal.OffsetOf<ScanCommand>("Qscore"), "Qscore offset");
            Assert.AreEqual(1168, (int)Marshal.OffsetOf<ScanCommand>("MonoMass"), "MonoMass offset");
            Assert.AreEqual(1176, (int)Marshal.OffsetOf<ScanCommand>("ChargeCos"), "ChargeCos offset");
            Assert.AreEqual(1184, (int)Marshal.OffsetOf<ScanCommand>("ChargeSnr"), "ChargeSnr offset");
            Assert.AreEqual(1192, (int)Marshal.OffsetOf<ScanCommand>("IsoCos"), "IsoCos offset");
            Assert.AreEqual(1200, (int)Marshal.OffsetOf<ScanCommand>("Snr"), "Snr offset");
            Assert.AreEqual(1208, (int)Marshal.OffsetOf<ScanCommand>("ChargeScore"), "ChargeScore offset");
            Assert.AreEqual(1216, (int)Marshal.OffsetOf<ScanCommand>("PpmError"), "PpmError offset");
            Assert.AreEqual(1224, (int)Marshal.OffsetOf<ScanCommand>("PrecursorIntensity"), "PrecursorIntensity offset");
            Assert.AreEqual(1232, (int)Marshal.OffsetOf<ScanCommand>("PeakgroupIntensity"), "PeakgroupIntensity offset");
            Assert.AreEqual(1240, (int)Marshal.OffsetOf<ScanCommand>("HcdEnergy"), "HcdEnergy offset");
            Assert.AreEqual(1244, (int)Marshal.OffsetOf<ScanCommand>("Pad2"), "Pad2 offset");
            Assert.AreEqual(1248, (int)Marshal.OffsetOf<ScanCommand>("FaimsCv"), "FaimsCv offset");

            // New scan parameter fields (after FaimsCv at 1248 + 8 = 1256)
            Assert.AreEqual(1256, (int)Marshal.OffsetOf<ScanCommand>("Microscans"), "Microscans offset");
            Assert.AreEqual(1260, (int)Marshal.OffsetOf<ScanCommand>("Pad3"), "Pad3 offset");
            Assert.AreEqual(1264, (int)Marshal.OffsetOf<ScanCommand>("RfLens"), "RfLens offset");
            Assert.AreEqual(1272, (int)Marshal.OffsetOf<ScanCommand>("SourceCid"), "SourceCid offset");
            Assert.AreEqual(1280, (int)Marshal.OffsetOf<ScanCommand>("SourceCidScaling"), "SourceCidScaling offset");
            Assert.AreEqual(1288, (int)Marshal.OffsetOf<ScanCommand>("DataType"), "DataType offset");
            Assert.AreEqual(1320, (int)Marshal.OffsetOf<ScanCommand>("ScanRate"), "ScanRate offset");
            Assert.AreEqual(1352, (int)Marshal.OffsetOf<ScanCommand>("ParentScanId"), "ParentScanId offset");
            Assert.AreEqual(1356, (int)Marshal.OffsetOf<ScanCommand>("Reserved"), "Reserved offset");

            // IsolationStage field offsets
            Assert.AreEqual(0, (int)Marshal.OffsetOf<IsolationStage>("PrecursorMz"), "PrecursorMz offset");
            Assert.AreEqual(8, (int)Marshal.OffsetOf<IsolationStage>("IsolationWidth"), "IsolationWidth offset");
            Assert.AreEqual(16, (int)Marshal.OffsetOf<IsolationStage>("CollisionEnergy"), "CollisionEnergy offset");
            Assert.AreEqual(24, (int)Marshal.OffsetOf<IsolationStage>("ReactionTime"), "ReactionTime offset");
            Assert.AreEqual(32, (int)Marshal.OffsetOf<IsolationStage>("ReagentMaxIt"), "ReagentMaxIt offset");
            Assert.AreEqual(40, (int)Marshal.OffsetOf<IsolationStage>("ReagentAgcTarget"), "ReagentAgcTarget offset");
            Assert.AreEqual(44, (int)Marshal.OffsetOf<IsolationStage>("ChargeState"), "ChargeState offset");
            Assert.AreEqual(48, (int)Marshal.OffsetOf<IsolationStage>("ActivationType"), "ActivationType offset");
        }

        // P3-U04: MarshalAs SizeConst values are correct for char fields
        [Test, Category("Tier1")]
        public void P3_U04_ScanCommand_CharFieldSizesAreCorrect()
        {
            // ScanCommand.Analyzer should be SizeConst=32
            var analyzerAttr = typeof(ScanCommand).GetField("Analyzer")
                .GetCustomAttribute<MarshalAsAttribute>();
            Assert.IsNotNull(analyzerAttr, "Analyzer should have MarshalAs attribute");
            Assert.AreEqual(32, analyzerAttr.SizeConst, "Analyzer SizeConst");

            // ScanCommand.ScanDescription should be SizeConst=256
            var descAttr = typeof(ScanCommand).GetField("ScanDescription")
                .GetCustomAttribute<MarshalAsAttribute>();
            Assert.IsNotNull(descAttr, "ScanDescription should have MarshalAs attribute");
            Assert.AreEqual(256, descAttr.SizeConst, "ScanDescription SizeConst");

            // IsolationStage.ActivationType should be SizeConst=32
            var actAttr = typeof(IsolationStage).GetField("ActivationType")
                .GetCustomAttribute<MarshalAsAttribute>();
            Assert.IsNotNull(actAttr, "ActivationType should have MarshalAs attribute");
            Assert.AreEqual(32, actAttr.SizeConst, "ActivationType SizeConst");

            // ScanCommand.DataType should be SizeConst=32
            var dataTypeAttr = typeof(ScanCommand).GetField("DataType")
                .GetCustomAttribute<MarshalAsAttribute>();
            Assert.IsNotNull(dataTypeAttr, "DataType should have MarshalAs attribute");
            Assert.AreEqual(32, dataTypeAttr.SizeConst, "DataType SizeConst");

            // ScanCommand.ScanRate should be SizeConst=32
            var scanRateAttr = typeof(ScanCommand).GetField("ScanRate")
                .GetCustomAttribute<MarshalAsAttribute>();
            Assert.IsNotNull(scanRateAttr, "ScanRate should have MarshalAs attribute");
            Assert.AreEqual(32, scanRateAttr.SizeConst, "ScanRate SizeConst");

            // ScanCommand.ParentScanId should be SizeConst=4
            var parentScanIdAttr = typeof(ScanCommand).GetField("ParentScanId")
                .GetCustomAttribute<MarshalAsAttribute>();
            Assert.IsNotNull(parentScanIdAttr, "ParentScanId should have MarshalAs attribute");
            Assert.AreEqual(4, parentScanIdAttr.SizeConst, "ParentScanId SizeConst");

            // ScanCommand.Reserved should be SizeConst=692
            var reservedAttr = typeof(ScanCommand).GetField("Reserved")
                .GetCustomAttribute<MarshalAsAttribute>();
            Assert.IsNotNull(reservedAttr, "Reserved should have MarshalAs attribute");
            Assert.AreEqual(692, reservedAttr.SizeConst, "Reserved SizeConst");
        }

        // P4-I02: CollisionEnergy rounds correctly (D5 fix)
        [Test, Category("Tier2")]
        public void P4_I02_BuildFromCommand_CollisionEnergyRoundsCorrectly()
        {
            var factory = new MockScanFactory();
            var cmd = new ScanCommand();
            cmd.MsnLevel = 2;
            cmd.NumStages = 1;
            cmd.Analyzer = "Orbitrap";
            // Stages array is null in pure C# struct creation — must initialize
            var stages = new IsolationStage[10];
            stages[0].PrecursorMz = 500.0;
            stages[0].IsolationWidth = 2.0;
            stages[0].CollisionEnergy = 29.5;  // Fractional CE
            stages[0].ActivationType = "HCD";
            stages[0].ChargeState = 4;
            cmd.Stages = stages;

            var scan = factory.BuildFromCommand(cmd);
            // CE should round to 30, not truncate to 29
            Assert.That(scan.Values["CollisionEnergy"], Is.EqualTo("30"),
                "CollisionEnergy 29.5 should round to 30, not truncate to 29");
        }

        // P4-I04: ScanCommandRecord scoring fields round-trip through JSON
        [Test, Category("Tier1")]
        public void P4_I04_ScanCommandRecord_ScoringFieldsRoundTrip()
        {
            var record = new ScanCommandRecord
            {
                MsnLevel = 2, PrecursorMz = 500.5, IsolationWidth = 2.0,
                CollisionEnergy = 29, Analyzer = "Orbitrap",
                ScanDescription = "0000|500.50@4", IsAGC = false,
                FaimsCV = 0, ActivationType = "HCD", ScanType = "MSn",
                ChargeState = 4,
                Qscore = 0.85, MonoMass = 1999.5, ChargeCos = 0.92,
                ChargeSnr = 15.3, IsoCos = 0.88, Snr = 12.1,
                ChargeScore = 0.76, PpmError = 3.2,
                PrecursorIntensity = 5e6, PeakgroupIntensity = 2e7,
                HcdEnergy = 29
            };

            string json = record.ToJsonObject();
            var parsed = ScanCommandRecord.ParseJsonObject(json);

            Assert.That(parsed.Qscore, Is.EqualTo(0.85).Within(1e-10));
            Assert.That(parsed.MonoMass, Is.EqualTo(1999.5).Within(1e-10));
            Assert.That(parsed.ChargeCos, Is.EqualTo(0.92).Within(1e-10));
            Assert.That(parsed.ChargeSnr, Is.EqualTo(15.3).Within(1e-10));
            Assert.That(parsed.IsoCos, Is.EqualTo(0.88).Within(1e-10));
            Assert.That(parsed.Snr, Is.EqualTo(12.1).Within(1e-10));
            Assert.That(parsed.ChargeScore, Is.EqualTo(0.76).Within(1e-10));
            Assert.That(parsed.PpmError, Is.EqualTo(3.2).Within(1e-10));
            Assert.That(parsed.PrecursorIntensity, Is.EqualTo(5e6).Within(1));
            Assert.That(parsed.PeakgroupIntensity, Is.EqualTo(2e7).Within(1));
            Assert.That(parsed.HcdEnergy, Is.EqualTo(29));
        }

        // P4-I05: Old-format JSON without scoring fields parses with zero defaults
        [Test, Category("Tier1")]
        public void P4_I05_ScanCommandRecord_ParseOldFormatWithoutScoringFields()
        {
            // Old-format JSON without scoring fields — should parse with 0 defaults
            string oldJson = "{\"MsnLevel\":2,\"PrecursorMz\":500.5,\"IsolationWidth\":2," +
                "\"CollisionEnergy\":29,\"Analyzer\":\"Orbitrap\",\"ScanDescription\":\"_0|500.50@4\"," +
                "\"IsAGC\":false,\"FaimsCV\":0,\"ActivationType\":\"HCD\"," +
                "\"ScanType\":\"MSn\",\"ChargeState\":4}";
            var parsed = ScanCommandRecord.ParseJsonObject(oldJson);
            Assert.That(parsed.MsnLevel, Is.EqualTo(2));
            Assert.That(parsed.Qscore, Is.EqualTo(0));
            Assert.That(parsed.MonoMass, Is.EqualTo(0));
            Assert.That(parsed.HcdEnergy, Is.EqualTo(0));
        }
    }
}

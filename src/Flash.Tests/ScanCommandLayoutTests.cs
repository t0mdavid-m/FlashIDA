using System;
using System.Globalization;
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
            Assert.AreEqual(1356, (int)Marshal.OffsetOf<ScanCommand>("HcdEnergyS1"), "HcdEnergyS1 offset");
            Assert.AreEqual(1360, (int)Marshal.OffsetOf<ScanCommand>("MonoMassS1"), "MonoMassS1 offset");
            Assert.AreEqual(1368, (int)Marshal.OffsetOf<ScanCommand>("QscoreS1"), "QscoreS1 offset");
            Assert.AreEqual(1376, (int)Marshal.OffsetOf<ScanCommand>("ChargeCosS1"), "ChargeCosS1 offset");
            Assert.AreEqual(1384, (int)Marshal.OffsetOf<ScanCommand>("ChargeSnrS1"), "ChargeSnrS1 offset");
            Assert.AreEqual(1392, (int)Marshal.OffsetOf<ScanCommand>("IsoCosS1"), "IsoCosS1 offset");
            Assert.AreEqual(1400, (int)Marshal.OffsetOf<ScanCommand>("SnrS1"), "SnrS1 offset");
            Assert.AreEqual(1408, (int)Marshal.OffsetOf<ScanCommand>("ChargeScoreS1"), "ChargeScoreS1 offset");
            Assert.AreEqual(1416, (int)Marshal.OffsetOf<ScanCommand>("PpmErrorS1"), "PpmErrorS1 offset");
            Assert.AreEqual(1424, (int)Marshal.OffsetOf<ScanCommand>("PrecursorIntensityS1"), "PrecursorIntensityS1 offset");
            Assert.AreEqual(1432, (int)Marshal.OffsetOf<ScanCommand>("PeakgroupIntensityS1"), "PeakgroupIntensityS1 offset");
            Assert.AreEqual(1440, (int)Marshal.OffsetOf<ScanCommand>("WindowSnr"), "WindowSnr offset");
            // Carved out of Reserved, which has now moved 1448 -> 1896 and shrunk 600 -> 152 across
            // three changes (FaimsEnabled, ADR-0012; the two notch counts, ADR-0017; the notch array,
            // ADR-0019). Every offset above is unchanged and the struct stays 2048 bytes -- that is
            // why new bridge fields are consumed from the tail rather than appended.
            Assert.AreEqual(1448, (int)Marshal.OffsetOf<ScanCommand>("FaimsEnabled"), "FaimsEnabled offset");
            Assert.AreEqual(1452, (int)Marshal.OffsetOf<ScanCommand>("Stage0NotchCount"), "Stage0NotchCount offset");
            Assert.AreEqual(1456, (int)Marshal.OffsetOf<ScanCommand>("Stage1NotchCount"), "Stage1NotchCount offset");
            // Pad4 is explicit on both sides: Notches is 8-aligned and 1460 is 4-mod-8, so leaving it
            // implicit would put the C++ array at 1464 and give the mirror nothing to line up against.
            Assert.AreEqual(1460, (int)Marshal.OffsetOf<ScanCommand>("Pad4"), "Pad4 offset");
            Assert.AreEqual(1464, (int)Marshal.OffsetOf<ScanCommand>("Notches"), "Notches offset");
            Assert.AreEqual(1896, (int)Marshal.OffsetOf<ScanCommand>("Reserved"), "Reserved offset");
            Assert.AreEqual(24, Marshal.SizeOf<Notch>(), "Notch must be 24 bytes");
            Assert.AreEqual(432, 18 * Marshal.SizeOf<Notch>(), "Notches block must be 432 bytes");

            // Notch field offsets -- geometry only; no per-notch collision energy or activation,
            // because every notch of a stage fires into the same fragmentation event.
            Assert.AreEqual(0, (int)Marshal.OffsetOf<Notch>("PrecursorMz"), "Notch.PrecursorMz offset");
            Assert.AreEqual(8, (int)Marshal.OffsetOf<Notch>("IsolationWidth"), "Notch.IsolationWidth offset");
            Assert.AreEqual(16, (int)Marshal.OffsetOf<Notch>("ChargeState"), "Notch.ChargeState offset");
            Assert.AreEqual(20, (int)Marshal.OffsetOf<Notch>("Pad"), "Notch.Pad offset");

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

            // ScanCommand.Reserved should be SizeConst=152 (after carving 84 B stage-1 scoring
            // + 8 B window_snr + 4 B faims_enabled + 8 B the two notch counts + 4 B pad4
            // + 432 B notches[18]). Every carve shrinks Reserved by exactly the bytes it takes,
            // which is what keeps the struct at 2048 and every prior offset fixed.
            var reservedAttr = typeof(ScanCommand).GetField("Reserved")
                .GetCustomAttribute<MarshalAsAttribute>();
            Assert.IsNotNull(reservedAttr, "Reserved should have MarshalAs attribute");
            Assert.AreEqual(152, reservedAttr.SizeConst, "Reserved SizeConst");

            // ScanCommand.Notches should be SizeConst=18 (two per-stage blocks of 9).
            var notchesAttr = typeof(ScanCommand).GetField("Notches")
                .GetCustomAttribute<MarshalAsAttribute>();
            Assert.IsNotNull(notchesAttr, "Notches should have MarshalAs attribute");
            Assert.AreEqual(ScanFactory.MaxNotches, notchesAttr.SizeConst, "Notches SizeConst");

            // The carve arithmetic, asserted rather than only described: whatever Reserved and the
            // notch block are, together with everything before them they must still total 2048.
            Assert.AreEqual(2048,
                (int)Marshal.OffsetOf<ScanCommand>("Reserved") + reservedAttr.SizeConst,
                "Reserved must end exactly at the 2048-byte ABI boundary");
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

        /// <summary>
        /// Two-stage (MS3) requests must be POSITIONAL: element i of every emitted per-stage array
        /// belongs to stage i. BuildFromCommand used to append a stage only if its value passed a
        /// `> 0` filter, so a stage whose value was 0 was SKIPPED rather than zero-filled and every
        /// later stage shifted one slot forward onto the wrong stage.
        ///
        /// The trigger is one config value away and legal today: Config.cpp forces reaction_time > 0
        /// for an ETD/EThcD scan config and applies that at every level, while an HCD MS2 legally has
        /// reaction_time 0. That yields stage0 = 0 / stage1 = 10, which the old filter emitted as the
        /// single-element "10" -- binding the MS3's reaction time to the MS2 replay stage.
        /// No committed fixture produces the mixed pattern, so it is built by hand here.
        /// </summary>
        [Test, Category("Tier2")]
        public void BuildFromCommand_TwoStage_ArraysArePositional()
        {
            var factory = new MockScanFactory();
            var cmd = new ScanCommand();
            cmd.MsnLevel = 3;
            cmd.NumStages = 2;
            cmd.Analyzer = "Orbitrap";
            var stages = new IsolationStage[10];
            // stage 0 -- the MS2 replay: HCD, so no reaction/reagent parameters apply
            stages[0].PrecursorMz = 824.97; stages[0].IsolationWidth = 1.86;
            stages[0].ChargeState = 15; stages[0].CollisionEnergy = 30;
            stages[0].ActivationType = "HCD";
            stages[0].ReactionTime = 0; stages[0].ReagentMaxIt = 0; stages[0].ReagentAgcTarget = 0;
            // stage 1 -- the MS3 fragment step: ETD, so it DOES carry them
            stages[1].PrecursorMz = 1050.9; stages[1].IsolationWidth = 2.0;
            stages[1].ChargeState = 9; stages[1].CollisionEnergy = 25;
            stages[1].ActivationType = "ETD";
            stages[1].ReactionTime = 10; stages[1].ReagentMaxIt = 150; stages[1].ReagentAgcTarget = 500000;
            cmd.Stages = stages;

            var scan = factory.BuildFromCommand(cmd);

            Assert.That(scan.Values["ActivationType"], Is.EqualTo("HCD;ETD"),
                "activation is positional -- stage 0 HCD, stage 1 ETD");
            Assert.That(scan.Values["CollisionEnergy"], Is.EqualTo("30;25"));
            Assert.That(scan.Values["ChargeStates"], Is.EqualTo("15;9"));
            Assert.That(scan.Values["ReagentAGCTarget"], Is.EqualTo("0;500000"),
                "stage 0 does not use a reagent AGC target, but must still occupy slot 0");

            // Doubles are compared after parsing so the assertion is culture-independent
            // (the Values dictionary is built with the same culture via ToString()).
            AssertStageDoubles(scan.Values["PrecursorMass"], 824.97, 1050.9);
            AssertStageDoubles(scan.Values["IsolationWidth"], 1.86, 2.0);
            AssertStageDoubles(scan.Values["ReactionTime"], 0.0, 10.0);   // was "10" -> bound to stage 0
            AssertStageDoubles(scan.Values["ReagentMaxIT"], 0.0, 150.0);  // was "150" -> bound to stage 0

            // Arity invariant: an emitted per-stage array is either absent or exactly NumStages long.
            // This is what fails if the loop is ever widened past Math.Min(NumStages, 10).
            foreach (var key in new[] { "PrecursorMass", "IsolationWidth", "CollisionEnergy",
                                        "ActivationType", "ChargeStates", "ReactionTime",
                                        "ReagentMaxIT", "ReagentAGCTarget" })
            {
                string raw;
                if (!scan.Values.TryGetValue(key, out raw)) continue;
                Assert.That(raw.Split(';').Length, Is.EqualTo(cmd.NumStages),
                    "per-stage array '" + key + "' must carry exactly one element per stage");
            }

            // All-or-nothing half of the rule: when NO stage uses an optional parameter the key stays
            // ABSENT, so the instrument applies its own method default rather than a literal 0. This
            // is the shape every committed MS3 fixture has (HCD MS2 + CID MS3, reaction_time "0;0"),
            // which is why this change moves no golden.
            stages[1].ActivationType = "CID";
            stages[1].ReactionTime = 0; stages[1].ReagentMaxIt = 0; stages[1].ReagentAgcTarget = 0;
            cmd.Stages = stages;
            var shipped = factory.BuildFromCommand(cmd);

            Assert.That(shipped.Values.ContainsKey("ReactionTime"), Is.False,
                "no stage uses a reaction time -> key omitted, not sent as \"0;0\"");
            Assert.That(shipped.Values.ContainsKey("ReagentMaxIT"), Is.False);
            Assert.That(shipped.Values.ContainsKey("ReagentAGCTarget"), Is.False);
            Assert.That(shipped.Values["ActivationType"], Is.EqualTo("HCD;CID"),
                "structural fields are still emitted for every stage");
        }

        /// <summary>
        /// A stage without isolation geometry is not a stage. Zero-filling it would command an
        /// isolation at m/z 0; the old filter instead collapsed the arrays to one element and
        /// produced a RAGGED request -- PrecursorMass with one element beside CollisionEnergy with
        /// two, because CollisionEnergy alone is filtered on `>= 0` -- while ScanType stayed "MSn".
        /// Neither is a meaningful instruction, so BuildFromCommand refuses to build it.
        /// </summary>
        [Test, Category("Tier2")]
        public void BuildFromCommand_StageMissingGeometry_Throws()
        {
            var factory = new MockScanFactory();
            var cmd = new ScanCommand();
            cmd.MsnLevel = 3;
            cmd.NumStages = 2;
            cmd.Analyzer = "Orbitrap";
            var stages = new IsolationStage[10];
            // stage 0 left entirely zeroed; stage 1 fully populated
            stages[1].PrecursorMz = 1050.9; stages[1].IsolationWidth = 2.0;
            stages[1].ChargeState = 9; stages[1].CollisionEnergy = 25;
            stages[1].ActivationType = "CID";
            cmd.Stages = stages;

            var ex = Assert.Throws<InvalidOperationException>(() => factory.BuildFromCommand(cmd));
            Assert.That(ex.Message, Does.Contain("stage 0"),
                "the refusal must name the offending stage");
            Assert.That(ex.Message, Does.Contain("isolation geometry"));
        }

        /// <summary>Parse a ';'-joined two-stage numeric cell and assert both stages' values.</summary>
        /// <summary>
        /// The two wire axes: ';' descends an MSn cascade stage, ',' widens one into parallel
        /// co-isolation notches (ADR-0016, docs/kb/scan-pipeline/multi-notch-wire-grammar.md).
        /// </summary>
        /// <remarks>
        /// Matches Thermo's own tribrid example, which sends PrecursorMass "524.3;104,271,453" with
        /// ActivationType "HCD;CID" and CollisionEnergy "35;0" -- three simultaneous windows at the MS3
        /// stage, but ONE activation and ONE energy per stage, because all notches of a stage fire into
        /// the same fragmentation event.
        /// </remarks>
        [Test, Category("Tier2")]
        public void BuildFromCommand_Notches_AreCommaJoinedWithinTheStageGroup()
        {
            var factory = new MockScanFactory();
            var cmd = new ScanCommand();
            cmd.MsnLevel = 3;
            cmd.NumStages = 2;
            cmd.Analyzer = "Orbitrap";
            cmd.Stage0NotchCount = 2;
            cmd.Stage1NotchCount = 1;
            var stages = new IsolationStage[10];
            stages[0].PrecursorMz = 1000.5; stages[0].IsolationWidth = 3.2;
            stages[0].ChargeState = 17; stages[0].CollisionEnergy = 30; stages[0].ActivationType = "HCD";
            stages[1].PrecursorMz = 1251.3; stages[1].IsolationWidth = 2.0;
            stages[1].ChargeState = 4; stages[1].CollisionEnergy = 25; stages[1].ActivationType = "CID";
            cmd.Stages = stages;
            // Notches live in their own array now, in fixed per-stage blocks: stage 0 at [0..9),
            // stage 1 at [9..18). No CollisionEnergy or ActivationType per notch -- the Notch struct
            // has no such field, which is what makes "one fragmentation event per stage" structural.
            var notches = new Notch[18];
            notches[0].PrecursorMz = 938.2; notches[0].IsolationWidth = 3.0; notches[0].ChargeState = 16;
            notches[1].PrecursorMz = 883.9; notches[1].IsolationWidth = 2.9; notches[1].ChargeState = 15;
            notches[ScanFactory.MaxNotchesPerStage + 0].PrecursorMz = 1001.2;
            notches[ScanFactory.MaxNotchesPerStage + 0].IsolationWidth = 2.0;
            notches[ScanFactory.MaxNotchesPerStage + 0].ChargeState = 5;
            cmd.Notches = notches;

            var scan = factory.BuildFromCommand(cmd);

            Assert.That(scan.Values["PrecursorMass"], Is.EqualTo("1000.5,938.2,883.9;1251.3,1001.2"),
                "notches join with ',' inside their stage's ';' group");
            Assert.That(scan.Values["IsolationWidth"], Is.EqualTo("3.2,3,2.9;2,2"));
            Assert.That(scan.Values["ChargeStates"], Is.EqualTo("17,16,15;4,5"));

            // Per cascade stage only -- one group each, no ',' axis.
            Assert.That(scan.Values["ActivationType"], Is.EqualTo("HCD;CID"));
            Assert.That(scan.Values["CollisionEnergy"], Is.EqualTo("30;25"));

            // Group count is the cascade depth, whatever the notch count.
            foreach (var key in new[] { "PrecursorMass", "IsolationWidth", "ChargeStates",
                                        "ActivationType", "CollisionEnergy" })
            {
                Assert.That(scan.Values[key].Split(';').Length, Is.EqualTo(cmd.NumStages),
                    "'" + key + "' must carry exactly one ';'-group per cascade stage");
            }
        }

        /// <summary>
        /// With no notches the emitted strings are byte-identical to the pre-notch format: no ',' is
        /// produced anywhere. This is the acceptance criterion for shipping the feature defaulted off.
        /// </summary>
        [Test, Category("Tier2")]
        public void BuildFromCommand_NoNotches_EmitsNoCommaAxis()
        {
            var factory = new MockScanFactory();
            var cmd = new ScanCommand();
            cmd.MsnLevel = 2;
            cmd.NumStages = 1;
            cmd.Analyzer = "Orbitrap";
            var stages = new IsolationStage[10];
            stages[0].PrecursorMz = 1000.5; stages[0].IsolationWidth = 3.2;
            stages[0].ChargeState = 17; stages[0].CollisionEnergy = 30; stages[0].ActivationType = "HCD";
            cmd.Stages = stages;
            cmd.Notches = new Notch[ScanFactory.MaxNotches];   // present but all counts zero

            var scan = factory.BuildFromCommand(cmd);

            Assert.That(scan.Values["PrecursorMass"], Is.EqualTo("1000.5"));
            Assert.That(scan.Values["ChargeStates"], Is.EqualTo("17"));
            foreach (var kv in scan.Values)
            {
                Assert.That(kv.Value, Does.Not.Contain(","),
                    "scan parameter '" + kv.Key + "' = '" + kv.Value + "' emitted a ',' with no notches, "
                    + "which the instrument would read as an extra isolation window");
            }
        }

        /// <summary>
        /// A 10-plex at BOTH cascade stages of one MS3 — 20 isolation windows in a single command.
        /// </summary>
        /// <remarks>
        /// This is the ceiling the instrument documents: every notch-bearing key accepts "a maximum of
        /// 10 values", and MSXTargets caps MSX windows per fragmentation stage at 10. It was previously
        /// unreachable — notches shared the unused tail of Stages[], so an MS3's two stages had 8 slots
        /// BETWEEN them and a fully multiplexed parent left the fragment stage with none.
        /// </remarks>
        [Test, Category("Tier2")]
        public void BuildFromCommand_TenPlexAtBothStages_EmitsTwentyWindows()
        {
            var factory = new MockScanFactory();
            var cmd = new ScanCommand();
            cmd.MsnLevel = 3;
            cmd.NumStages = 2;
            cmd.Analyzer = "Orbitrap";
            cmd.Stage0NotchCount = ScanFactory.MaxNotchesPerStage;
            cmd.Stage1NotchCount = ScanFactory.MaxNotchesPerStage;
            var stages = new IsolationStage[10];
            stages[0].PrecursorMz = 1000.0; stages[0].IsolationWidth = 3.0;
            stages[0].ChargeState = 20; stages[0].CollisionEnergy = 30; stages[0].ActivationType = "HCD";
            stages[1].PrecursorMz = 500.0; stages[1].IsolationWidth = 2.0;
            stages[1].ChargeState = 10; stages[1].CollisionEnergy = 25; stages[1].ActivationType = "CID";
            cmd.Stages = stages;
            var notches = new Notch[ScanFactory.MaxNotches];
            for (int i = 0; i < ScanFactory.MaxNotchesPerStage; i++)
            {
                notches[i].PrecursorMz = 1010.0 + i;
                notches[i].IsolationWidth = 3.0;
                notches[i].ChargeState = 19 - i;
                int j = ScanFactory.MaxNotchesPerStage + i;
                notches[j].PrecursorMz = 510.0 + i;
                notches[j].IsolationWidth = 2.0;
                notches[j].ChargeState = 9 - i;
            }
            cmd.Notches = notches;

            var scan = factory.BuildFromCommand(cmd);

            foreach (var key in new[] { "PrecursorMass", "IsolationWidth", "ChargeStates" })
            {
                var groups = scan.Values[key].Split(';');
                Assert.That(groups.Length, Is.EqualTo(2), "'" + key + "' group count");
                Assert.That(groups[0].Split(',').Length, Is.EqualTo(10),
                    "'" + key + "' stage 0 must be a full 10-plex, got '" + groups[0] + "'");
                Assert.That(groups[1].Split(',').Length, Is.EqualTo(10),
                    "'" + key + "' stage 1 must be a full 10-plex, got '" + groups[1] + "'");
            }
            // Anchor first in each group, then notches in the order the engine ranked them.
            Assert.That(scan.Values["ChargeStates"],
                Is.EqualTo("20,19,18,17,16,15,14,13,12,11;10,9,8,7,6,5,4,3,2,1"));
            // Still one activation and one energy per stage, however many windows the stage carries.
            Assert.That(scan.Values["ActivationType"], Is.EqualTo("HCD;CID"));
            Assert.That(scan.Values["CollisionEnergy"], Is.EqualTo("30;25"));
        }

        private static void AssertStageDoubles(string raw, double stage0, double stage1)
        {
            var parts = raw.Split(';');
            Assert.That(parts.Length, Is.EqualTo(2), "expected one element per stage, got '" + raw + "'");
            // InvariantCulture, matching ScanFactory.Fmt. A bare double.Parse follows the machine
            // locale, so on a comma-decimal one it round-tripped the old CurrentCulture ToString()
            // and passed either way -- which is what let "824,97" reach the instrument unnoticed.
            Assert.That(double.Parse(parts[0], CultureInfo.InvariantCulture),
                Is.EqualTo(stage0).Within(1e-9), "stage 0 of '" + raw + "'");
            Assert.That(double.Parse(parts[1], CultureInfo.InvariantCulture),
                Is.EqualTo(stage1).Within(1e-9), "stage 1 of '" + raw + "'");
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

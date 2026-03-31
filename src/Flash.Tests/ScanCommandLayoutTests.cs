using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Flash.IDA;
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
        // P3-U01: ScanCommand is exactly 1152 bytes (updated Phase 4: added EnqueueTimestampMs)
        [Test, Category("Tier1")]
        public void P3_U01_ScanCommand_SizeMatchesCpp()
        {
            Assert.AreEqual(1152, Marshal.SizeOf<ScanCommand>(),
                "ScanCommand must be 1152 bytes to match C++ layout");
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
        }
    }
}

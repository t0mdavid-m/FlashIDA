using System;
using System.IO;
using System.Runtime.InteropServices;
using Flash;
using Flash.IDA;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// Phase 3 bridge integration tests: verify ProcessScan, GetNextScanCommand,
    /// GetNextTrackingId across the P/Invoke boundary.
    /// </summary>
    [TestFixture]
    public class BridgePhase3Tests
    {
        private const string DllName = "OpenMS.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr CreateFLASHIda(string config);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void DisposeFLASHIda(IntPtr ptr);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int ProcessScan(IntPtr obj, double[] mzs, double[] ints,
            int length, double rt_min, int ms_level, string scan_description);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetNextScanCommand(IntPtr obj, ref ScanCommand output);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetNextTrackingId(IntPtr obj);

        private IntPtr nativePtr;

        [OneTimeSetUp]
        public void Setup()
        {
            // Use MethodParameters to build JSON config matching method_default.json
            string configsDir = Path.Combine(
                TestContext.CurrentContext.TestDirectory, "..", "test-data", "configs");
            string configPath = Path.Combine(configsDir, "method_default.json");

            Assert.IsTrue(File.Exists(configPath),
                "method_default.json not found at " + configPath);

            var mp = MethodParameters.Load(configPath);
            string jsonConfig = mp.ToCppJson();
            nativePtr = CreateFLASHIda(jsonConfig);
            Assert.That(nativePtr, Is.Not.EqualTo(IntPtr.Zero), "CreateFLASHIda returned null");
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            if (nativePtr != IntPtr.Zero)
            {
                DisposeFLASHIda(nativePtr);
                nativePtr = IntPtr.Zero;
            }
        }

        // P3-I01: GetNextScanCommand returns struct with correct MS1 fallback fields
        [Test, Category("Tier2")]
        public void P3_I01_ScanCommand_MarshalingRoundTrip()
        {
            var cmd = new ScanCommand();
            int result = GetNextScanCommand(nativePtr, ref cmd);
            // Idle cycling: returns AGC when queue is empty (never returns 0)
            Assert.AreEqual(1, result, "GetNextScanCommand should return 1 (idle AGC)");

            // Verify AGC struct fields survived marshaling
            Assert.AreEqual(1, cmd.MsnLevel, "MsnLevel should be 1 for AGC");
            Assert.AreEqual(1, cmd.IsAgc, "IsAgc should be 1 for AGC");
            Assert.AreEqual(0, cmd.NumStages, "NumStages should be 0 for AGC");
        }

        // P3-I02: ProcessScan returns 0 with insufficient/synthetic peaks (no real charge envelopes)
        [Test, Category("Tier2")]
        public void P3_I02_ProcessScan_ReturnsZeroForSyntheticPeaks()
        {
            double[] mzs = { 500.0, 600.0, 700.0, 800.0, 900.0 };
            double[] ints = { 1000.0, 2000.0, 3000.0, 4000.0, 5000.0 };
            int result = ProcessScan(nativePtr, mzs, ints, mzs.Length, 1.5, 1, "test_scan");
            Assert.AreEqual(0, result, "ProcessScan should return 0 for synthetic peaks with no charge envelopes");
        }

        // P3-I03: GetNextScanCommand returns idle scan when queue is empty
        [Test, Category("Tier2")]
        public void P3_I03_GetNextScanCommand_ReturnsIdleScanWhenQueueEmpty()
        {
            var cmd = new ScanCommand();
            int result = GetNextScanCommand(nativePtr, ref cmd);
            // Idle cycling: never returns 0 — returns AGC or MS1
            Assert.AreEqual(1, result, "Should return 1 (idle scan) when queue is empty");

            // Verify returned scan is a valid idle scan (AGC or MS1)
            Assert.AreEqual(1, cmd.MsnLevel, "MsnLevel should be 1 for idle scan");
        }

        // P3-I04: GetNextTrackingId is monotonically increasing
        [Test, Category("Tier2")]
        public void P3_I04_GetNextTrackingId_IsMonotonicallyIncreasing()
        {
            int prev = GetNextTrackingId(nativePtr);
            Assert.That(prev, Is.GreaterThanOrEqualTo(0), "First tracking ID should be >= 0");

            for (int i = 0; i < 100; i++)
            {
                int current = GetNextTrackingId(nativePtr);
                Assert.That(current, Is.GreaterThan(prev),
                    String.Format("Tracking ID {0} should be > previous {1}", current, prev));
                prev = current;
            }
        }

        // P3-I05: the 3 new bridge exports resolve AND behave (not just "don't throw").
        [Test, Category("Tier2")]
        public void P3_I05_DllExports_IncludeNewFunctions()
        {
            // Enqueue an MS1 scan: ProcessScan must return 0 (enqueue success).
            double[] mzs = { 500.0 };
            double[] ints = { 1000.0 };
            int processResult = ProcessScan(nativePtr, mzs, ints, 1, 1.0, 1, "export_test");
            Assert.That(processResult, Is.EqualTo(0), "ProcessScan should return 0 on enqueue");

            // Drain a command: GetNextScanCommand must report one was filled (1 — even when the
            // real queue is empty the engine fills an idle scan) with a valid MS level.
            var cmd = new ScanCommand();
            int cmdResult = GetNextScanCommand(nativePtr, ref cmd);
            Assert.That(cmdResult, Is.EqualTo(1), "GetNextScanCommand should return 1 (a command was filled)");
            Assert.That(cmd.MsnLevel, Is.GreaterThanOrEqualTo(1), "Drained command must have a valid MS level");

            // Tracking IDs must advance on each call.
            int firstId = GetNextTrackingId(nativePtr);
            int secondId = GetNextTrackingId(nativePtr);
            Assert.That(secondId, Is.GreaterThan(firstId), "GetNextTrackingId must increase");
        }
    }
}

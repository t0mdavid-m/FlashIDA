using System.Collections.Generic;
using Flash;
using Thermo.Interfaces.FusionAccess_V1.Control.Scans;

namespace Flash.Tests.Mocks
{
    /// <summary>
    /// ScanFactory subclass that captures all created scans for test verification.
    /// Overrides the virtual CreateFusionCustomScan to produce MockCustomScan instances
    /// instead of Thermo FusionCustomScan (which requires instrument DLLs).
    /// </summary>
    public class MockScanFactory : ScanFactory
    {
        /// <summary>
        /// All scans created via CreateFusionCustomScan, in creation order.
        /// Clear after setup to isolate test-produced scans from infrastructure scans.
        /// </summary>
        public List<IFusionCustomScan> CreatedScans { get; } = new List<IFusionCustomScan>();

        /// <summary>
        /// Create a MockScanFactory. Passes null to base ScanFactory constructor.
        /// This is safe because we override CreateFusionCustomScan (the only method
        /// used in tests), and the other methods that use the controller field
        /// (CreateCustomScan, CreateRepeatingScan) are not called.
        /// </summary>
        public MockScanFactory() : base(null) { }

        /// <summary>
        /// Create a MockCustomScan instead of a Thermo FusionCustomScan, populating its Values
        /// through the base class's own <c>FillParameters</c> — so a test asserting on Values is
        /// asserting on production behaviour.
        /// </summary>
        public override IFusionCustomScan CreateFusionCustomScan(
            ScanParameters parameters, int id = 0, double delay = 0,
            bool IsAGC = false, int AGCgroup = 1)
        {
            var scan = new MockCustomScan();
            // The REAL ScanFactory.FillParameters (protected). This used to be a hand-copied
            // FillParametersMock, so tests asserting on Values were checking the copy, not production —
            // and the copy formatted numbers with the current culture after production moved to
            // InvariantCulture. MockCustomScan is an IFusionCustomScan, hence an IScanDefinition.
            FillParameters(scan, parameters);
            scan.RunningNumber = id;
            scan.SingleProcessingDelay = delay;
            scan.IsPAGCScan = IsAGC;
            scan.PAGCGroupIndex = AGCgroup;

            CreatedScans.Add(scan);
            return scan;
        }

    }
}

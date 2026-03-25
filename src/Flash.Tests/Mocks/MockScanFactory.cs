using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        /// Create a MockCustomScan instead of a Thermo FusionCustomScan.
        /// Populates Values via reflection mirroring ScanFactory.FillParameters.
        /// </summary>
        public override IFusionCustomScan CreateFusionCustomScan(
            ScanParameters parameters, int id = 0, double delay = 0,
            bool IsAGC = false, int AGCgroup = 1)
        {
            var scan = new MockCustomScan();
            FillParametersMock(scan, parameters);
            scan.RunningNumber = id;
            scan.SingleProcessingDelay = delay;
            scan.IsPAGCScan = IsAGC;
            scan.PAGCGroupIndex = AGCgroup;

            CreatedScans.Add(scan);
            return scan;
        }

        /// <summary>
        /// Reimplementation of ScanFactory.FillParameters (private, cannot be called from subclass).
        /// Uses reflection to populate the scan Values dictionary from ScanParameters fields.
        /// Field names have underscores replaced with spaces (e.g. FAIMS_CV -> "FAIMS CV").
        /// Array fields are joined with semicolons (e.g. [100, 2000] -> "100;2000").
        /// </summary>
        private static void FillParametersMock(MockCustomScan scan, ScanParameters parameters)
        {
            foreach (FieldInfo field in typeof(ScanParameters).GetFields())
            {
                object value = field.GetValue(parameters);
                if (value != null)
                {
                    string key = field.Name.Replace("_", " ");
                    if (field.FieldType.IsArray)
                    {
                        scan.Values[key] = String.Join(";",
                            ((IEnumerable)value).Cast<object>().Select(o => o.ToString()).ToArray());
                    }
                    else
                    {
                        scan.Values[key] = value.ToString();
                    }
                }
            }
        }
    }
}

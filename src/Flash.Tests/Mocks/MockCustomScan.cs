using System.Collections.Generic;
using Thermo.Interfaces.FusionAccess_V1.Control.Scans;

namespace Flash.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of IFusionCustomScan for continuity tests.
    /// Replaces the Thermo FusionCustomScan concrete class.
    ///
    /// IScanDefinition.RunningNumber and IFusionCustomScan.PAGCGroupIndex are long (not int).
    /// </summary>
    public class MockCustomScan : IFusionCustomScan
    {
        /// <summary>
        /// Scan parameter values dictionary (matches IScanDefinition.Values)
        /// </summary>
        public IDictionary<string, string> Values { get; } = new Dictionary<string, string>();

        /// <summary>
        /// Running number identifier for this scan (IScanDefinition uses long)
        /// </summary>
        public long RunningNumber { get; set; }

        /// <summary>
        /// Processing delay in seconds
        /// </summary>
        public double SingleProcessingDelay { get; set; }

        /// <summary>
        /// Whether this scan is a PAGC (predictive AGC) scan
        /// </summary>
        public bool IsPAGCScan { get; set; }

        /// <summary>
        /// PAGC group index for grouping AGC measurements (IFusionCustomScan uses long)
        /// </summary>
        public long PAGCGroupIndex { get; set; }
    }
}

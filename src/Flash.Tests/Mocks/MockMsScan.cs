using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Flash.DataObjects;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;
using Thermo.Interfaces.SpectrumFormat_V1;

namespace Flash.Tests.Mocks
{
    /// <summary>
    /// Mock implementation of IMsScan for continuity tests.
    ///
    /// CI FIXUP NOTE: IMsScan may have additional abstract members beyond Header, Trailer,
    /// Centroids, and Dispose. Compilation errors will list missing members to implement.
    /// The Header and Trailer properties may need to return a specific Thermo interface type
    /// (e.g. IInfoContainer); if so, update MockInfoContainer to implement that interface
    /// and adjust return types here.
    /// </summary>
    public class MockMsScan : IMsScan
    {
        private readonly MockInfoContainer _header;
        private readonly MockInfoContainer _trailer;
        private readonly List<ICentroid> _centroids;

        /// <summary>
        /// Header dictionary for scan metadata (MSOrder, MassAnalyzer, StartTime, Scan, etc.)
        /// </summary>
        /// <remarks>
        /// Return type may need to be changed to match IMsScan.Header declaration.
        /// If IMsScan.Header returns IInfoContainer, MockInfoContainer must implement it.
        /// </remarks>
        public MockInfoContainer Header => _header;

        /// <summary>
        /// Trailer dictionary for scan-level metadata (Access ID, Charge State, FAIMS CV, etc.)
        /// </summary>
        public MockInfoContainer Trailer => _trailer;

        /// <summary>
        /// Centroid peak list
        /// </summary>
        /// <remarks>
        /// Return type may need to be ICentroid[] or IReadOnlyCollection&lt;ICentroid&gt;
        /// depending on IMsScan.Centroids declaration.
        /// </remarks>
        public IList<ICentroid> Centroids => _centroids;

        /// <summary>
        /// Detector name (not used by Flash code but may be required by IMsScan interface)
        /// </summary>
        public string DetectorName => "MockDetector";

        public MockMsScan()
        {
            _header = new MockInfoContainer();
            _trailer = new MockInfoContainer();
            _centroids = new List<ICentroid>();
        }

        public void Dispose()
        {
            // No resources to release in mock
        }

        /// <summary>
        /// Create a minimal MS1 scan with the given peaks
        /// </summary>
        public static MockMsScan WithPeaks(double rt, string scanNumber, params (double mz, double intensity)[] peaks)
        {
            var scan = new MockMsScan();
            scan._header.Set("MSOrder", "1");
            scan._header.Set("MassAnalyzer", "FTMS");
            scan._header.Set("StartTime", rt.ToString());
            scan._header.Set("Scan", scanNumber);

            scan._trailer.Set("Access ID", scanNumber);

            foreach (var peak in peaks)
            {
                scan._centroids.Add(new Centroid(peak.mz, peak.intensity, 0, 120000));
            }

            return scan;
        }

        /// <summary>
        /// Create a MS1 scan for FAIMS mode with the given CV value
        /// </summary>
        public static MockMsScan WithFaimsPeaks(double rt, string scanNumber, double faimsCV,
            params (double mz, double intensity)[] peaks)
        {
            var scan = WithPeaks(rt, scanNumber, peaks);
            scan._trailer.Set("FAIMS CV", faimsCV.ToString());
            scan._trailer.Set("FAIMS Voltage On", "True");
            return scan;
        }

        /// <summary>
        /// Create an MS2 scan with tracking ID in scan description
        /// </summary>
        public static MockMsScan MS2WithDescription(double rt, string scanNumber, string scanDescription,
            double precursorMz, int chargeState, params (double mz, double intensity)[] peaks)
        {
            var scan = new MockMsScan();
            scan._header.Set("MSOrder", "2");
            scan._header.Set("MassAnalyzer", "FTMS");
            scan._header.Set("StartTime", rt.ToString());
            scan._header.Set("Scan", scanNumber);
            scan._header.Set("PrecursorMass[0]", precursorMz.ToString());

            scan._trailer.Set("Access ID", scanNumber);
            scan._trailer.Set("Scan Description", scanDescription);
            scan._trailer.Set("Charge State", chargeState.ToString());

            foreach (var peak in peaks)
            {
                scan._centroids.Add(new Centroid(peak.mz, peak.intensity, 0, 120000));
            }

            return scan;
        }

        /// <summary>
        /// Create an empty MS1 scan (no centroids)
        /// </summary>
        public static MockMsScan EmptyMS1(double rt = 1.0, string scanNumber = "1")
        {
            var scan = new MockMsScan();
            scan._header.Set("MSOrder", "1");
            scan._header.Set("MassAnalyzer", "FTMS");
            scan._header.Set("StartTime", rt.ToString());
            scan._header.Set("Scan", scanNumber);
            scan._trailer.Set("Access ID", scanNumber);
            return scan;
        }

        /// <summary>
        /// Create a noise-only MS1 scan with very low intensity peaks
        /// </summary>
        public static MockMsScan NoiseOnlyMS1(double rt = 1.0, string scanNumber = "1")
        {
            var scan = new MockMsScan();
            scan._header.Set("MSOrder", "1");
            scan._header.Set("MassAnalyzer", "FTMS");
            scan._header.Set("StartTime", rt.ToString());
            scan._header.Set("Scan", scanNumber);
            scan._trailer.Set("Access ID", scanNumber);

            // Add noise peaks - very low intensity, no isotope patterns
            var rng = new Random(42);
            for (int i = 0; i < 50; i++)
            {
                double mz = 500 + rng.NextDouble() * 1500;
                double intensity = rng.NextDouble() * 100; // Very low
                scan._centroids.Add(new Centroid(mz, intensity, 0, 120000));
            }

            return scan;
        }

        /// <summary>
        /// Load an MS1 scan from a TSV spectrum file (same format as ms1_smoke_test.txt)
        /// </summary>
        public static MockMsScan FromTsv(string filePath)
        {
            var scan = new MockMsScan();
            scan._header.Set("MSOrder", "1");
            scan._header.Set("MassAnalyzer", "FTMS");

            bool started = false;
            foreach (var line in File.ReadAllLines(filePath))
            {
                var tokens = line.Split('\t');
                if (line.StartsWith("Spec"))
                {
                    double rtSeconds = double.Parse(tokens[1]);
                    scan._header.Set("StartTime", (rtSeconds / 60.0).ToString());
                    string scanNum = tokens[0].Replace("Spec scan=", "");
                    scan._header.Set("Scan", scanNum);
                    scan._trailer.Set("Access ID", scanNum);
                    started = true;
                }
                else if (started && tokens.Length >= 2)
                {
                    double mz = double.Parse(tokens[0]);
                    double intensity = double.Parse(tokens[1]);
                    scan._centroids.Add(new Centroid(mz, intensity, 0, 120000));
                }
            }

            return scan;
        }
    }
}

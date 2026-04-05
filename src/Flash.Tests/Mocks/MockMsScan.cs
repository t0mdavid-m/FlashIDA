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
    /// IMsScan extends ISpectrum (from SpectrumFormat_V1) and IDisposable.
    ///
    /// Interface member types discovered from CI build errors:
    /// - Header: IDictionary&lt;string, string&gt;
    /// - Trailer: IInformationSourceAccess
    /// - Centroids: IEnumerable&lt;ICentroid&gt;
    /// - TuneData, StatusLog: guessed as IDictionary (may need fixup)
    /// - NoiseCount, CentroidCount: guessed as int?
    /// - NoiseBand: guessed as INoise[]
    /// - ChargeEnvelopes: guessed as IChargeEnvelope[]
    /// </summary>
    public class MockMsScan : IMsScan
    {
        private readonly Dictionary<string, string> _headerDict;
        private readonly MockTrailerAccess _trailerAccess;
        private readonly List<ICentroid> _centroids;

        // === IMsScan members ===

        /// <summary>Header: scan metadata (MSOrder, MassAnalyzer, StartTime, Scan, etc.)</summary>
        public IDictionary<string, string> Header => _headerDict;

        /// <summary>Trailer: scan-level metadata (Access ID, Charge State, FAIMS CV, etc.)</summary>
        public IInformationSourceAccess Trailer => _trailerAccess;

        /// <summary>Tune data (not used by Flash code)</summary>
        public IInformationSourceAccess TuneData => null;

        /// <summary>Status log (not used by Flash code)</summary>
        public IInformationSourceAccess StatusLog => null;

        /// <summary>Detector name</summary>
        public string DetectorName => "MockDetector";

        // === ISpectrum members ===

        /// <summary>Centroid peak list</summary>
        public IEnumerable<ICentroid> Centroids => _centroids;

        /// <summary>Number of centroids</summary>
        public int? CentroidCount => _centroids.Count;

        /// <summary>Noise count (not used by Flash code)</summary>
        public int? NoiseCount => null;

        /// <summary>Noise band data (not used by Flash code)</summary>
        public IEnumerable<INoiseNode> NoiseBand => null;

        /// <summary>Charge envelopes (not used by Flash code)</summary>
        public IChargeEnvelope[] ChargeEnvelopes => null;

        // === Constructor ===

        public MockMsScan()
        {
            _headerDict = new Dictionary<string, string>();
            _trailerAccess = new MockTrailerAccess();
            _centroids = new List<ICentroid>();
        }

        public void Dispose()
        {
            // No resources to release in mock
        }

        // === Factory methods ===

        /// <summary>Create a minimal MS1 scan with the given peaks</summary>
        public static MockMsScan WithPeaks(double rt, string scanNumber, params (double mz, double intensity)[] peaks)
        {
            var scan = new MockMsScan();
            scan._headerDict["MSOrder"] = "1";
            scan._headerDict["MassAnalyzer"] = "FTMS";
            scan._headerDict["StartTime"] = rt.ToString();
            scan._headerDict["Scan"] = scanNumber;

            scan._trailerAccess.Set("Access ID", scanNumber);

            foreach (var peak in peaks)
            {
                scan._centroids.Add(new Centroid(peak.mz, peak.intensity, 0, 120000));
            }

            return scan;
        }

        /// <summary>Create a MS1 scan for FAIMS mode with the given CV value</summary>
        public static MockMsScan WithFaimsPeaks(double rt, string scanNumber, double faimsCV,
            params (double mz, double intensity)[] peaks)
        {
            var scan = WithPeaks(rt, scanNumber, peaks);
            scan._trailerAccess.Set("FAIMS CV", faimsCV.ToString());
            scan._trailerAccess.Set("FAIMS Voltage On", "True");
            return scan;
        }

        /// <summary>Create an MS2 scan with tracking ID in scan description</summary>
        public static MockMsScan MS2WithDescription(double rt, string scanNumber, string scanDescription,
            double precursorMz, int chargeState, params (double mz, double intensity)[] peaks)
        {
            var scan = new MockMsScan();
            scan._headerDict["MSOrder"] = "2";
            scan._headerDict["MassAnalyzer"] = "FTMS";
            scan._headerDict["StartTime"] = rt.ToString();
            scan._headerDict["Scan"] = scanNumber;
            scan._headerDict["PrecursorMass[0]"] = precursorMz.ToString();
            scan._headerDict["IsolationWidth[0]"] = "2";

            scan._trailerAccess.Set("Access ID", scanNumber);
            scan._trailerAccess.Set("Scan Description", scanDescription);
            scan._trailerAccess.Set("Charge State", chargeState.ToString());

            foreach (var peak in peaks)
            {
                scan._centroids.Add(new Centroid(peak.mz, peak.intensity, 0, 120000));
            }

            return scan;
        }

        /// <summary>
        /// Load an MS2 scan from a TSV spectrum file with the given precursor metadata.
        /// Used for MS2 return path tests where real fragment peak data (thousands of peaks)
        /// is too large to pass as inline tuples.
        /// </summary>
        public static MockMsScan FromTsvAsMS2(string filePath, string scanDescription,
            double precursorMz, int chargeState, double isolationWidth = 2.0)
        {
            var scan = new MockMsScan();
            scan._headerDict["MSOrder"] = "2";
            scan._headerDict["MassAnalyzer"] = "FTMS";
            scan._headerDict["PrecursorMass[0]"] = precursorMz.ToString();
            scan._headerDict["IsolationWidth[0]"] = isolationWidth.ToString();

            scan._trailerAccess.Set("Scan Description", scanDescription);
            scan._trailerAccess.Set("Charge State", chargeState.ToString());

            bool started = false;
            foreach (var line in File.ReadAllLines(filePath))
            {
                var tokens = line.Split('\t');
                if (line.StartsWith("Spec"))
                {
                    if (started) break; // Only read the first scan
                    double rtSeconds = double.Parse(tokens[1]);
                    scan._headerDict["StartTime"] = (rtSeconds / 60.0).ToString();
                    string scanNum = tokens[0].Replace("Spec scan=", "");
                    scan._headerDict["Scan"] = scanNum;
                    scan._trailerAccess.Set("Access ID", scanNum);
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

        /// <summary>Create an empty MS1 scan (no centroids)</summary>
        public static MockMsScan EmptyMS1(double rt = 1.0, string scanNumber = "1")
        {
            var scan = new MockMsScan();
            scan._headerDict["MSOrder"] = "1";
            scan._headerDict["MassAnalyzer"] = "FTMS";
            scan._headerDict["StartTime"] = rt.ToString();
            scan._headerDict["Scan"] = scanNumber;
            scan._trailerAccess.Set("Access ID", scanNumber);
            return scan;
        }

        /// <summary>Create a noise-only MS1 scan with very low intensity peaks</summary>
        public static MockMsScan NoiseOnlyMS1(double rt = 1.0, string scanNumber = "1")
        {
            var scan = new MockMsScan();
            scan._headerDict["MSOrder"] = "1";
            scan._headerDict["MassAnalyzer"] = "FTMS";
            scan._headerDict["StartTime"] = rt.ToString();
            scan._headerDict["Scan"] = scanNumber;
            scan._trailerAccess.Set("Access ID", scanNumber);

            var rng = new Random(42);
            for (int i = 0; i < 50; i++)
            {
                double mz = 500 + rng.NextDouble() * 1500;
                double intensity = rng.NextDouble() * 100;
                scan._centroids.Add(new Centroid(mz, intensity, 0, 120000));
            }

            return scan;
        }

        /// <summary>Load the first MS1 scan from a TSV spectrum file (same format as ms1_smoke_test.txt)</summary>
        public static MockMsScan FromTsv(string filePath)
        {
            return FromTsvAllScans(filePath)[0];
        }

        /// <summary>Load all MS1 scans from a TSV spectrum file. Each scan becomes a separate MockMsScan.</summary>
        public static List<MockMsScan> FromTsvAllScans(string filePath)
        {
            var scans = new List<MockMsScan>();
            MockMsScan current = null;

            foreach (var line in File.ReadAllLines(filePath))
            {
                var tokens = line.Split('\t');
                if (line.StartsWith("Spec"))
                {
                    current = new MockMsScan();
                    current._headerDict["MSOrder"] = "1";
                    current._headerDict["MassAnalyzer"] = "FTMS";
                    double rtSeconds = double.Parse(tokens[1]);
                    current._headerDict["StartTime"] = (rtSeconds / 60.0).ToString();
                    string scanNum = tokens[0].Replace("Spec scan=", "");
                    current._headerDict["Scan"] = scanNum;
                    current._trailerAccess.Set("Access ID", scanNum);
                    if (tokens.Length >= 3 && tokens[2].StartsWith("cv="))
                    {
                        string cvStr = tokens[2].Substring(3);
                        current._trailerAccess.Set("FAIMS CV", cvStr);
                        current._trailerAccess.Set("FAIMS Voltage On", "True");
                    }
                    scans.Add(current);
                }
                else if (current != null && tokens.Length >= 2)
                {
                    double mz = double.Parse(tokens[0]);
                    double intensity = double.Parse(tokens[1]);
                    current._centroids.Add(new Centroid(mz, intensity, 0, 120000));
                }
            }

            return scans;
        }
    }
}

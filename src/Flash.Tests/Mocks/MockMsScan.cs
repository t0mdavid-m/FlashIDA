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

        /// <summary>
        /// Default synthetic MS1 "Scan Description" trailer value.
        ///
        /// FLASHIda::processScan rejects any scan whose description is shorter than 3 chars
        /// (FLASHIda.cpp: <c>if (desc_str.size() &lt; 3) return 0;</c>) — a guard for
        /// instrument method/AGC scans. On a real instrument every MS1 echoes back the
        /// "&lt;3-char id&gt;S" description FLASHIda stamps via makeMS1, so the guard never
        /// fires in production. The mock MS1 factories must mirror that shape or the engine
        /// returns zero precursors for every test spectrum.
        ///
        /// This synthetic default is used by the behavioral-reference continuity suite, where the
        /// emitted command ScanDescription uses each MS2 command's OWN counter-based tracking id
        /// (encode(nextTrackingId)), not the parent MS1's, so the constant value is inert.
        /// "~~~" decodes to 830583 (far above any engine-generated id, so it never collides in the
        /// pending map) and the trailing 'S' (not 'A') keeps it from being treated as an AGC scan.
        ///
        /// The log-golden full-acquisition path (H-cs) instead feeds back the ENGINE-EMITTED MS1
        /// description (bootstrapped from the first GetNextScanCommand idle MS1), so parent/child
        /// join edges in scan_results/identification resolve to real engine tracking ids. Every MS1
        /// factory therefore accepts an optional description that overrides this default.
        /// </summary>
        public const string Ms1ScanDescription = "~~~S";

        /// <summary>Create a minimal MS1 scan with the given peaks</summary>
        public static MockMsScan WithPeaks(double rt, string scanNumber, params (double mz, double intensity)[] peaks)
        {
            return WithPeaks(rt, scanNumber, Ms1ScanDescription, peaks);
        }

        /// <summary>
        /// Create a minimal MS1 scan with the given peaks and an explicit "Scan Description"
        /// trailer. The log-golden full-acquisition harness supplies the engine-emitted MS1
        /// description here so the engine chains real tracking ids; other callers use the
        /// <see cref="Ms1ScanDescription"/> default via the overload above.
        /// </summary>
        public static MockMsScan WithPeaks(double rt, string scanNumber, string scanDescription,
            params (double mz, double intensity)[] peaks)
        {
            var scan = new MockMsScan();
            scan._headerDict["MSOrder"] = "1";
            scan._headerDict["MassAnalyzer"] = "FTMS";
            scan._headerDict["StartTime"] = rt.ToString();
            scan._headerDict["Scan"] = scanNumber;

            scan._trailerAccess.Set("Access ID", scanNumber);
            scan._trailerAccess.Set("Scan Description", scanDescription);

            foreach (var peak in peaks)
            {
                scan._centroids.Add(new Centroid(peak.mz, peak.intensity, 0, 120000));
            }

            return scan;
        }

        /// <summary>
        /// Overwrite the "Scan Description" trailer of an already-built scan. Used by the
        /// full-acquisition harness to re-stamp a TSV-loaded MS1 with the engine-emitted
        /// idle-MS1 description before feeding it back through the engine.
        /// </summary>
        public void SetScanDescription(string scanDescription)
        {
            _trailerAccess.Set("Scan Description", scanDescription);
        }

        /// <summary>
        /// F7: stamp the FAIMS CV trailer on a re-fed scan so FLASHIdaWrapper.ProcessScan reads it and
        /// passes it to the native processScan faims_cv argument (the C# channel for the CV; the C++ twin
        /// passes cmd.faims_cv directly in runInterleaved). Mirrors WithFaimsPeaks' trailer keys.
        /// </summary>
        public void SetFaimsCv(double faimsCV)
        {
            _trailerAccess.Set("FAIMS CV", faimsCV.ToString());
            _trailerAccess.Set("FAIMS Voltage On", "True");
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
            return FromTsvAsMSn(filePath, 2, scanDescription, precursorMz, chargeState, isolationWidth);
        }

        /// <summary>
        /// Load an MSn (n=2 or 3) scan from a TSV spectrum file with the given precursor metadata.
        /// Generalizes <see cref="FromTsvAsMS2"/> with an explicit MS order so the log-golden suite
        /// can feed MS3 responses (MSOrder=3) back through the engine to populate MS3 result /
        /// identification rows.
        /// </summary>
        public static MockMsScan FromTsvAsMSn(string filePath, int msOrder, string scanDescription,
            double precursorMz, int chargeState, double isolationWidth = 2.0)
        {
            var scan = new MockMsScan();
            scan._headerDict["MSOrder"] = msOrder.ToString();
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
            scan._trailerAccess.Set("Scan Description", Ms1ScanDescription);
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
            scan._trailerAccess.Set("Scan Description", Ms1ScanDescription);

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
                    current._trailerAccess.Set("Scan Description", Ms1ScanDescription);
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

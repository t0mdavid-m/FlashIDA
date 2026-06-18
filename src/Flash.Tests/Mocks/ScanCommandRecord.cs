using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Flash.IDA;
using Thermo.Interfaces.FusionAccess_V1.Control.Scans;

namespace Flash.Tests.Mocks
{
    /// <summary>
    /// Plain data class capturing key properties of a custom scan command.
    /// Used for golden file comparison in continuity tests.
    /// </summary>
    public class ScanCommandRecord
    {
        /// <summary>MSn level: 2 for MS2, 3 for MS3 (inferred from PrecursorMass count)</summary>
        public int MsnLevel { get; set; }

        /// <summary>First precursor m/z value</summary>
        public double PrecursorMz { get; set; }

        /// <summary>First isolation width</summary>
        public double IsolationWidth { get; set; }

        /// <summary>First collision energy (0 if not set)</summary>
        public int CollisionEnergy { get; set; }

        /// <summary>First reaction time for ETD (0 if not set). In-memory only — intentionally NOT serialized to golden JSON.</summary>
        public double ReactionTime { get; set; }

        /// <summary>Mass analyzer (e.g. "Orbitrap")</summary>
        public string Analyzer { get; set; }

        /// <summary>Scan description metadata string</summary>
        public string ScanDescription { get; set; }

        /// <summary>
        /// Encoded tracking id (3-char) of this command's logical parent. For a conditional follow-up the engine
        /// sets this to the triggering MS2's own scan_id (ScanCommandQueue.cpp:396-398). Captured for provenance
        /// assertions (e.g. CT34); intentionally NOT serialized to golden JSON (see ToJsonObject).
        /// </summary>
        public string ParentScanId { get; set; }

        /// <summary>Whether this is a PAGC scan</summary>
        public bool IsAGC { get; set; }

        /// <summary>FAIMS CV value (0 if not set)</summary>
        public double FaimsCV { get; set; }

        /// <summary>First activation type (e.g. "HCD", "ETD")</summary>
        public string ActivationType { get; set; }

        /// <summary>Scan type from Values (e.g. "MSn", "Full")</summary>
        public string ScanType { get; set; }

        /// <summary>First charge state</summary>
        public int ChargeState { get; set; }

        // --- Scoring fields (from ScanCommand struct, Phase 4) ---
        public double Qscore { get; set; }
        public double MonoMass { get; set; }
        public double ChargeCos { get; set; }
        public double ChargeSnr { get; set; }
        public double IsoCos { get; set; }
        public double Snr { get; set; }
        public double ChargeScore { get; set; }
        public double PpmError { get; set; }
        public double PrecursorIntensity { get; set; }
        public double PeakgroupIntensity { get; set; }
        public int HcdEnergy { get; set; }

        /// <summary>
        /// Create a ScanCommandRecord directly from a raw ScanCommand struct.
        /// Captures all scoring fields that IFusionCustomScan.Values cannot carry.
        /// </summary>
        public static ScanCommandRecord FromScanCommand(ScanCommand cmd)
        {
            var record = new ScanCommandRecord();

            record.MsnLevel = cmd.MsnLevel;
            record.ScanType = cmd.MsnLevel > 1 ? "MSn" : "Full";
            record.Analyzer = cmd.Analyzer ?? "";
            record.ScanDescription = cmd.ScanDescription ?? "";
            record.ParentScanId = cmd.ParentScanId ?? "";
            record.IsAGC = cmd.IsAgc != 0;

            if (cmd.NumStages > 0 && cmd.Stages != null)
            {
                var stage = cmd.Stages[0];
                record.PrecursorMz = stage.PrecursorMz;
                record.IsolationWidth = stage.IsolationWidth;
                record.CollisionEnergy = (int)Math.Round(stage.CollisionEnergy);
                record.ReactionTime = stage.ReactionTime;
                record.ActivationType = stage.ActivationType ?? "";
                record.ChargeState = stage.ChargeState;
            }

            // Scoring fields
            record.Qscore = cmd.Qscore;
            record.MonoMass = cmd.MonoMass;
            record.ChargeCos = cmd.ChargeCos;
            record.ChargeSnr = cmd.ChargeSnr;
            record.IsoCos = cmd.IsoCos;
            record.Snr = cmd.Snr;
            record.ChargeScore = cmd.ChargeScore;
            record.PpmError = cmd.PpmError;
            record.PrecursorIntensity = cmd.PrecursorIntensity;
            record.PeakgroupIntensity = cmd.PeakgroupIntensity;
            record.HcdEnergy = cmd.HcdEnergy;
            record.FaimsCV = cmd.FaimsCv;

            return record;
        }

        /// <summary>
        /// Extract a ScanCommandRecord from an IFusionCustomScan's Values dictionary.
        /// </summary>
        public static ScanCommandRecord FromCustomScan(IFusionCustomScan scan)
        {
            var record = new ScanCommandRecord();
            var values = scan.Values;

            record.ScanType = GetValueOrDefault(values, "ScanType", "");
            record.Analyzer = GetValueOrDefault(values, "Analyzer", "");
            record.ScanDescription = GetValueOrDefault(values, "ScanDescription", "");
            record.IsAGC = scan.IsPAGCScan;

            // Determine MsnLevel from precursor mass array size
            // MS2 has 1 precursor mass, MS3 has 2 (semicolon-separated)
            string precursorStr = GetValueOrDefault(values, "PrecursorMass", "");
            if (!string.IsNullOrEmpty(precursorStr))
            {
                string[] precursors = precursorStr.Split(';');
                record.MsnLevel = precursors.Length + 1; // 1 precursor = MS2, 2 = MS3
                record.PrecursorMz = ParseDouble(precursors[0]);
            }
            else
            {
                record.MsnLevel = record.ScanType == "Full" ? 1 : 0;
            }

            string isoStr = GetValueOrDefault(values, "IsolationWidth", "");
            if (!string.IsNullOrEmpty(isoStr))
            {
                record.IsolationWidth = ParseDouble(isoStr.Split(';')[0]);
            }

            string ceStr = GetValueOrDefault(values, "CollisionEnergy", "");
            if (!string.IsNullOrEmpty(ceStr))
            {
                int.TryParse(ceStr.Split(';')[0], out int ce);
                record.CollisionEnergy = ce;
            }

            string cvStr = GetValueOrDefault(values, "FAIMS CV", "");
            if (!string.IsNullOrEmpty(cvStr))
            {
                record.FaimsCV = ParseDouble(cvStr);
            }

            string actStr = GetValueOrDefault(values, "ActivationType", "");
            if (!string.IsNullOrEmpty(actStr))
            {
                record.ActivationType = actStr.Split(';')[0];
            }

            string chargeStr = GetValueOrDefault(values, "ChargeStates", "");
            if (!string.IsNullOrEmpty(chargeStr))
            {
                int.TryParse(chargeStr.Split(';')[0], out int z);
                record.ChargeState = z;
            }

            return record;
        }

        /// <summary>
        /// Serialize a list of records to deterministic JSON for golden file comparison.
        /// </summary>
        public static string ToJson(List<ScanCommandRecord> records)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[");
            for (int i = 0; i < records.Count; i++)
            {
                sb.Append("  ");
                sb.Append(records[i].ToJsonObject());
                if (i < records.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }
            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// Parse a list of records from JSON.
        /// </summary>
        public static List<ScanCommandRecord> FromJson(string json)
        {
            var records = new List<ScanCommandRecord>();
            // Simple JSON array parsing - each object is on one line after indentation
            var lines = json.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("{"))
                .Select(l => l.TrimEnd(','));

            foreach (var line in lines)
            {
                records.Add(ParseJsonObject(line));
            }

            return records;
        }

        internal string ToJsonObject()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"MsnLevel\":{0},\"PrecursorMz\":{1:G17},\"IsolationWidth\":{2:G17}," +
                "\"CollisionEnergy\":{3},\"Analyzer\":\"{4}\",\"ScanDescription\":\"{5}\"," +
                "\"IsAGC\":{6},\"FaimsCV\":{7:G17},\"ActivationType\":\"{8}\"," +
                "\"ScanType\":\"{9}\",\"ChargeState\":{10}," +
                "\"Qscore\":{11:G17},\"MonoMass\":{12:G17},\"ChargeCos\":{13:G17}," +
                "\"ChargeSnr\":{14:G17},\"IsoCos\":{15:G17},\"Snr\":{16:G17}," +
                "\"ChargeScore\":{17:G17},\"PpmError\":{18:G17}," +
                "\"PrecursorIntensity\":{19:G17},\"PeakgroupIntensity\":{20:G17}," +
                "\"HcdEnergy\":{21}}}",
                MsnLevel, PrecursorMz, IsolationWidth,
                CollisionEnergy, EscapeJson(Analyzer), EscapeJson(ScanDescription),
                IsAGC ? "true" : "false", FaimsCV, EscapeJson(ActivationType),
                EscapeJson(ScanType), ChargeState,
                Qscore, MonoMass, ChargeCos, ChargeSnr, IsoCos, Snr,
                ChargeScore, PpmError, PrecursorIntensity, PeakgroupIntensity,
                HcdEnergy);
        }

        internal static ScanCommandRecord ParseJsonObject(string json)
        {
            var record = new ScanCommandRecord();
            // Simple key-value extraction from JSON object string
            record.MsnLevel = ExtractInt(json, "MsnLevel");
            record.PrecursorMz = ExtractDouble(json, "PrecursorMz");
            record.IsolationWidth = ExtractDouble(json, "IsolationWidth");
            record.CollisionEnergy = ExtractInt(json, "CollisionEnergy");
            record.Analyzer = ExtractString(json, "Analyzer");
            record.ScanDescription = ExtractString(json, "ScanDescription");
            record.IsAGC = ExtractBool(json, "IsAGC");
            record.FaimsCV = ExtractDouble(json, "FaimsCV");
            record.ActivationType = ExtractString(json, "ActivationType");
            record.ScanType = ExtractString(json, "ScanType");
            record.ChargeState = ExtractInt(json, "ChargeState");

            // Scoring fields (backward-compatible: default 0 if missing)
            record.Qscore = ExtractDouble(json, "Qscore");
            record.MonoMass = ExtractDouble(json, "MonoMass");
            record.ChargeCos = ExtractDouble(json, "ChargeCos");
            record.ChargeSnr = ExtractDouble(json, "ChargeSnr");
            record.IsoCos = ExtractDouble(json, "IsoCos");
            record.Snr = ExtractDouble(json, "Snr");
            record.ChargeScore = ExtractDouble(json, "ChargeScore");
            record.PpmError = ExtractDouble(json, "PpmError");
            record.PrecursorIntensity = ExtractDouble(json, "PrecursorIntensity");
            record.PeakgroupIntensity = ExtractDouble(json, "PeakgroupIntensity");
            record.HcdEnergy = ExtractInt(json, "HcdEnergy");
            return record;
        }

        private static string GetValueOrDefault(IDictionary<string, string> dict, string key, string defaultValue)
        {
            return dict.ContainsKey(key) ? dict[key] : defaultValue;
        }

        private static double ParseDouble(string s)
        {
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double result);
            return result;
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static int ExtractInt(string json, string key)
        {
            string pattern = "\"" + key + "\":";
            int idx = json.IndexOf(pattern);
            if (idx < 0) return 0;
            idx += pattern.Length;
            int end = json.IndexOfAny(new[] { ',', '}' }, idx);
            int.TryParse(json.Substring(idx, end - idx).Trim(), out int result);
            return result;
        }

        private static double ExtractDouble(string json, string key)
        {
            string pattern = "\"" + key + "\":";
            int idx = json.IndexOf(pattern);
            if (idx < 0) return 0;
            idx += pattern.Length;
            int end = json.IndexOfAny(new[] { ',', '}' }, idx);
            double.TryParse(json.Substring(idx, end - idx).Trim(),
                NumberStyles.Float, CultureInfo.InvariantCulture, out double result);
            return result;
        }

        private static bool ExtractBool(string json, string key)
        {
            string pattern = "\"" + key + "\":";
            int idx = json.IndexOf(pattern);
            if (idx < 0) return false;
            idx += pattern.Length;
            return json.Substring(idx).TrimStart().StartsWith("true");
        }

        private static string ExtractString(string json, string key)
        {
            string pattern = "\"" + key + "\":\"";
            int idx = json.IndexOf(pattern);
            if (idx < 0) return "";
            idx += pattern.Length;
            int end = json.IndexOf("\"", idx);
            if (end < 0) return "";
            return json.Substring(idx, end - idx)
                .Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }
}

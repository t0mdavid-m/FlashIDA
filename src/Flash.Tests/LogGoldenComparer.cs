using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Flash.Tests
{
    /// <summary>
    /// Normalizes FLASHIda's four log streams (ida_log, scan_commands.tsv, scan_results.tsv,
    /// identification.tsv) for exact golden comparison.
    ///
    /// Two transforms remove the only non-deterministic content while leaving every chemistry /
    /// score / structural column to be compared EXACTLY:
    ///   * volatile wall-clock columns (timestamps, durations) are masked to &lt;TS&gt; / &lt;DUR&gt;;
    ///   * tracking ids are relabeled to T0, T1, ... by first-appearance order, shared across all
    ///     files so every parent/child join edge is preserved (a flat per-file mask would lose them).
    ///
    /// Column indices below are 0-based and pinned to the header order written by the FLASHIda
    /// constructor (scan_commands 28 cols, scan_results 32 cols, identification 19 cols). They are
    /// asserted by the C++ FLASHIda_LoggingFields schema_column_counts section.
    /// </summary>
    public static class LogGoldenComparer
    {
        public const string IdaLogName = "ida.log";
        public const string CommandsName = "scan_commands.tsv";
        public const string ResultsName = "scan_results.tsv";
        public const string IdentificationName = "identification.tsv";

        public static readonly string[] FileNames =
            { IdaLogName, CommandsName, ResultsName, IdentificationName };

        // ID-bearing columns per TSV. results child_ids (col 8) is space-split and handled separately.
        private static readonly int[] CmdIdCols = { 0, 22 };  // tracking_id, parent_tracking_id
        private static readonly int[] ResIdCols = { 0, 27 };  // tracking_id, parent_tracking_id
        private static readonly int[] IdfIdCols = { 2 };      // tracking_id
        private const int ResChildCol = 8;                    // child_ids (space-separated)

        // Volatile wall-clock columns -> placeholder.
        private static readonly Dictionary<int, string> CmdMask =
            new Dictionary<int, string> { { 3, "<TS>" } };    // enqueue_ts
        private static readonly Dictionary<int, string> ResMask = new Dictionary<int, string>
        {
            { 1, "<TS>" },  // resolve_ts
            { 2, "<DUR>" }, // duration_ms
            { 3, "<TS>" },  // received_ts
            { 4, "<DUR>" }, // duration_received_ms
            { 28, "<TS>" }, // dequeue_ts
            { 29, "<DUR>" },// queue_duration_ms
            { 30, "<DUR>" },// instrument_duration_ms
            { 31, "<DUR>" } // processing_duration_ms
        };
        private static readonly Dictionary<int, string> NoMask = new Dictionary<int, string>();

        /// <summary>
        /// Build tracking_id -> T&lt;n&gt; by first appearance across the three TSVs in fixed order
        /// (commands, results, identification) so labels are stable run-to-run.
        /// </summary>
        public static Dictionary<string, string> BuildIdMap(string caseDir)
        {
            var map = new Dictionary<string, string>();
            void Add(string id)
            {
                if (string.IsNullOrEmpty(id)) return;
                if (!map.ContainsKey(id)) map[id] = "T" + map.Count;
            }

            foreach (var row in DataRows(Path.Combine(caseDir, CommandsName)))
                foreach (var c in CmdIdCols) if (c < row.Length) Add(row[c]);

            foreach (var row in DataRows(Path.Combine(caseDir, ResultsName)))
            {
                foreach (var c in ResIdCols) if (c < row.Length) Add(row[c]);
                if (ResChildCol < row.Length && row[ResChildCol].Length > 0)
                    foreach (var k in row[ResChildCol].Split(' ')) Add(k);
            }

            foreach (var row in DataRows(Path.Combine(caseDir, IdentificationName)))
                foreach (var c in IdfIdCols) if (c < row.Length) Add(row[c]);

            return map;
        }

        /// <summary>Normalize one log file (dispatched by name) into golden-comparable text.</summary>
        public static string Normalize(string caseDir, string fileName, Dictionary<string, string> ids)
        {
            string path = Path.Combine(caseDir, fileName);
            if (fileName == CommandsName) return NormalizeTsv(path, ids, CmdIdCols, -1, CmdMask);
            if (fileName == ResultsName) return NormalizeTsv(path, ids, ResIdCols, ResChildCol, ResMask);
            if (fileName == IdentificationName) return NormalizeTsv(path, ids, IdfIdCols, -1, NoMask);
            if (fileName == IdaLogName) return NormalizeIdaLog(path);
            throw new ArgumentException("unknown log file: " + fileName);
        }

        private static string NormalizeTsv(string path, Dictionary<string, string> ids,
            int[] idCols, int childCol, Dictionary<int, string> mask)
        {
            if (!File.Exists(path)) return "";
            var lines = File.ReadAllLines(path);
            var sb = new StringBuilder();
            for (int li = 0; li < lines.Length; li++)
            {
                if (li == 0) { sb.Append(lines[li]).Append('\n'); continue; } // header verbatim
                var cols = lines[li].Split('\t');
                foreach (var c in idCols) if (c < cols.Length) cols[c] = Relabel(cols[c], ids);
                if (childCol >= 0 && childCol < cols.Length && cols[childCol].Length > 0)
                    cols[childCol] = string.Join(" ", cols[childCol].Split(' ').Select(k => Relabel(k, ids)));
                foreach (var kv in mask) if (kv.Key < cols.Length) cols[kv.Key] = kv.Value;
                sb.Append(string.Join("\t", cols)).Append('\n');
            }
            return sb.ToString();
        }

        // ida_log is free text, not TSV: the masses / scores / features are deterministic and kept;
        // only the per-scan number (decoded tracking id) and Access ID are masked (id-base dependent).
        private static string NormalizeIdaLog(string path)
        {
            if (!File.Exists(path)) return "";
            string text = File.ReadAllText(path).Replace("\r\n", "\n");
            text = Regex.Replace(text, @"Scan# \d+", "Scan# <SCAN>");
            text = Regex.Replace(text, @"Access ID [^)]+\)", "Access ID <ID>)");
            return text;
        }

        private static string Relabel(string id, Dictionary<string, string> ids)
        {
            if (string.IsNullOrEmpty(id)) return id;
            return ids.TryGetValue(id, out var v) ? v : id;
        }

        private static IEnumerable<string[]> DataRows(string path)
        {
            if (!File.Exists(path)) yield break;
            var lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++) yield return lines[i].Split('\t');
        }
    }
}

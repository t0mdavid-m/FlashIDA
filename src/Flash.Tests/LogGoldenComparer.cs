using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Flash.Tests
{
    /// <summary>
    /// Normalizes FLASHIda's five log streams (ida_log, scan_commands.tsv, scan_results.tsv,
    /// identification.tsv, pooled_identification.tsv) for exact golden comparison.
    ///
    /// Two transforms remove the only non-deterministic content while leaving every chemistry /
    /// score / structural column to be compared EXACTLY:
    ///   * volatile wall-clock columns (timestamps, durations) are masked to &lt;TS&gt; / &lt;DUR&gt;;
    ///   * tracking ids are relabeled to T0, T1, ... by first-appearance order, shared across all
    ///     files so every parent/child join edge is preserved (a flat per-file mask would lose them).
    ///
    /// Column indices below are 0-based and pinned to the header order written by the FLASHIda
    /// constructor (scan_commands 29 cols, scan_results 34 cols, identification 25 cols). They are
    /// asserted by the C++ FLASHIda_LoggingFields schema_column_counts section.
    ///
    /// F5 appended winner_tracking_id as the LAST scan_results column (index 33): the encoded id of an
    /// exploration group's winning variant (empty on every non-completing / non-exploration row). It is an
    /// id-bearing column, so it is relabeled via the shared id map (ResIdCols) and joins run-to-run.
    ///
    /// E5 inserted ms_level at scan_results column index 1 (int, unmasked), shifting every
    /// downstream scan_results column by +1: child_ids 8->9, parent_tracking_id 27->28, and the
    /// eight volatile timestamp/duration columns by +1. E6 appended the raw scan_description as the
    /// LAST scan_commands column (index 28); its leading 3-char tracking-id prefix is relabeled here
    /// via the shared id map so descriptors join run-to-run while the deterministic E2 mass/charge
    /// remainder is compared verbatim.
    /// </summary>
    public static class LogGoldenComparer
    {
        public const string IdaLogName = "ida.log";
        public const string CommandsName = "scan_commands.tsv";
        public const string ResultsName = "scan_results.tsv";
        public const string IdentificationName = "identification.tsv";
        // 5th runtime stream: the pooled per-precursor proteoform model (IdaLogger pooled_stream_),
        // written only when runtime.pooled_identification_log_path is non-empty (the harness sets it
        // to caseDir/PooledName, so this basename is what the engine writes).
        public const string PooledName = "pooled_identification.tsv";

        public static readonly string[] FileNames =
            { IdaLogName, CommandsName, ResultsName, IdentificationName, PooledName };

        // ID-bearing columns per TSV. results child_ids (col 9) is space-split and handled separately.
        private static readonly int[] CmdIdCols = { 0, 22 };  // tracking_id, parent_tracking_id (unchanged by E6)
        private static readonly int[] ResIdCols = { 0, 28, 33 };  // tracking_id, parent_tracking_id (+1 from E5 ms_level@1), winner_tracking_id (F5, last)
        private static readonly int[] IdfIdCols = { 2 };      // tracking_id (identification still leads ms_level,scan_mode,tracking_id)
        private const int ResChildCol = 9;                    // child_ids (space-separated; +1 from E5 ms_level@1)
        // scan_commands raw descriptor (E6), appended LAST. Its first 3 chars are the encoded
        // tracking id (== col 0); relabel just that prefix, keep the marker + mass remainder intact.
        private const int CmdDescriptionCol = 28;

        // pooled_identification.tsv contributing_scan_ids (engine pooled_stream_ header order, 14 cols):
        // nominal_mass[0] mono_mass[1] proteoform[2] flash_extender_score[3] coverage_pct[4]
        // n_fragments[5] localized_mods[6] ambiguous_mods[7] contributing_scan_ids[8]
        // combined_ms2_frame_masses[9] update_index[10] precursor_id[11] trigger[12]
        // trigger_scan_id[13]. Volatile id columns: col 8 (decoded ints) + col 13 (encoded 3-char).
        // UNLIKE scan_results child_ids (encoded 3-char base-94 strings), the engine writes col 8
        // as the DECODED integer scan ids (ProteoformTracker source_scan_id/winner_scan_id == the int
        // queue_.decode() of the tracking id; written via std::to_string). So each token is RE-ENCODED to
        // its 3-char base-94 string (mirroring ScanCommandQueue::encode) before the shared id-map relabel,
        // so pooled ids carry the SAME T<n> labels as scan_results child_ids and join run-to-run.
        // trigger_scan_id[13] is already an encoded 3-char base-94 tracking id (like col 0 on other
        // streams) and is relabeled directly via the shared id map (no re-encoding step needed).
        private const int PooledScanIdsCol = 8;
        private const int PooledTriggerScanIdCol = 13;

        // The base-94 tracking-id alphabet (all printable ASCII 0x21-0x7E), byte-for-byte the C++
        // ScanCommandQueue::tracking_alphabet_. encode() is the inverse of the engine's decode().
        private const string TrackingAlphabet =
            "!\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

        // Volatile wall-clock columns -> placeholder.
        private static readonly Dictionary<int, string> CmdMask =
            new Dictionary<int, string> { { 3, "<TS>" } };    // enqueue_ts (unchanged by E6)
        // E5 shifted every scan_results column after index 0 by +1 (ms_level@1 is an int, left UNMASKED).
        private static readonly Dictionary<int, string> ResMask = new Dictionary<int, string>
        {
            { 2, "<TS>" },  // resolve_ts
            { 3, "<DUR>" }, // duration_ms
            { 4, "<TS>" },  // received_ts
            { 5, "<DUR>" }, // duration_received_ms
            { 29, "<TS>" }, // dequeue_ts
            { 30, "<DUR>" },// queue_duration_ms
            { 31, "<DUR>" },// instrument_duration_ms
            { 32, "<DUR>" } // processing_duration_ms
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

            // pooled contributing_scan_ids: space-separated DECODED int ids — re-encode each to its
            // 3-char base-94 string (== the encoded form the other three streams use) before Add, so it
            // maps to the SAME T<n> label. Mirrors the scan_results child_ids split-on-space path above.
            // trigger_scan_id (col 13) is already an encoded 3-char base-94 string; Add directly.
            foreach (var row in DataRows(Path.Combine(caseDir, PooledName)))
            {
                if (PooledScanIdsCol < row.Length && row[PooledScanIdsCol].Length > 0)
                    foreach (var k in row[PooledScanIdsCol].Split(' ')) Add(EncodeTrackingId(k));
                if (PooledTriggerScanIdCol < row.Length && row[PooledTriggerScanIdCol].Length > 0)
                    Add(row[PooledTriggerScanIdCol]);
            }

            return map;
        }

        /// <summary>Normalize one log file (dispatched by name) into golden-comparable text.</summary>
        public static string Normalize(string caseDir, string fileName, Dictionary<string, string> ids)
        {
            string path = Path.Combine(caseDir, fileName);
            if (fileName == CommandsName) return NormalizeTsv(path, ids, CmdIdCols, -1, CmdMask, CmdDescriptionCol);
            if (fileName == ResultsName) return NormalizeTsv(path, ids, ResIdCols, ResChildCol, ResMask, -1);
            if (fileName == IdentificationName) return NormalizeTsv(path, ids, IdfIdCols, -1, NoMask, -1);
            if (fileName == PooledName) return NormalizePooled(path, ids);
            if (fileName == IdaLogName) return NormalizeIdaLog(path);
            throw new ArgumentException("unknown log file: " + fileName);
        }

        private static string NormalizeTsv(string path, Dictionary<string, string> ids,
            int[] idCols, int childCol, Dictionary<int, string> mask, int descCol)
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
                // E6 scan_description: relabel the leading 3-char tracking-id prefix only, leaving
                // the deterministic marker + adaptive-precision mass/charge remainder verbatim.
                if (descCol >= 0 && descCol < cols.Length)
                    cols[descCol] = RelabelDescriptionPrefix(cols[descCol], ids);
                foreach (var kv in mask) if (kv.Key < cols.Length) cols[kv.Key] = kv.Value;
                sb.Append(string.Join("\t", cols)).Append('\n');
            }
            return sb.ToString();
        }

        // pooled_identification.tsv: two volatile id columns — contributing_scan_ids (col 8) and
        // trigger_scan_id (col 13).  Col 8 holds DECODED int scan ids (space-separated), re-encoded to
        // 3-char base-94 form and relabeled via the SHARED id map to T<n> labels — the same labels the
        // other streams use, so pooled rows join run-to-run.  Col 13 is already an encoded 3-char
        // base-94 tracking id and is relabeled directly (no re-encoding step).  All other pooled columns
        // are deterministic and compared verbatim; the pooled stream has NO timestamp/duration columns,
        // so NO masking applies.
        private static string NormalizePooled(string path, Dictionary<string, string> ids)
        {
            if (!File.Exists(path)) return "";
            var lines = File.ReadAllLines(path);
            var sb = new StringBuilder();
            for (int li = 0; li < lines.Length; li++)
            {
                if (li == 0) { sb.Append(lines[li]).Append('\n'); continue; } // header verbatim
                var cols = lines[li].Split('\t');
                if (PooledScanIdsCol < cols.Length && cols[PooledScanIdsCol].Length > 0)
                    cols[PooledScanIdsCol] = string.Join(" ",
                        cols[PooledScanIdsCol].Split(' ').Select(k => Relabel(EncodeTrackingId(k), ids)));
                if (PooledTriggerScanIdCol < cols.Length && cols[PooledTriggerScanIdCol].Length > 0)
                    cols[PooledTriggerScanIdCol] = Relabel(cols[PooledTriggerScanIdCol], ids);
                sb.Append(string.Join("\t", cols)).Append('\n');
            }
            return sb.ToString();
        }

        // Encode a decoded int tracking id back to its 3-char base-94 string, the inverse of the engine's
        // ScanCommandQueue::decode (== how the other three streams store the id). Non-integer tokens (none
        // expected) pass through unchanged so they can still be looked up / surfaced verbatim.
        private static string EncodeTrackingId(string token)
        {
            if (!int.TryParse(token, out int value) || value < 0) return token;
            int b = TrackingAlphabet.Length;
            var buf = new char[3];
            for (int i = 2; i >= 0; --i) { buf[i] = TrackingAlphabet[value % b]; value /= b; }
            return new string(buf);
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

        // Relabel ONLY the leading 3-char base-94 tracking-id prefix of a raw scan_description cell
        // (E6, scan_commands col 28) to its T&lt;n&gt; label, leaving the marker (S/A/R/F/C/E) and the
        // deterministic adaptive-precision mass/charge/ion remainder intact. The prefix equals the
        // row's tracking_id (col 0), already in the id map, so this never introduces new labels and
        // keeps the descriptor stable run-to-run. Shorter cells (none expected) pass through unchanged.
        private static string RelabelDescriptionPrefix(string desc, Dictionary<string, string> ids)
        {
            if (string.IsNullOrEmpty(desc) || desc.Length < 3) return desc;
            string prefix = desc.Substring(0, 3);
            if (!ids.TryGetValue(prefix, out var label)) return desc;
            return label + desc.Substring(3);
        }

        private static IEnumerable<string[]> DataRows(string path)
        {
            if (!File.Exists(path)) yield break;
            var lines = File.ReadAllLines(path);
            for (int i = 1; i < lines.Length; i++) yield return lines[i].Split('\t');
        }
    }
}

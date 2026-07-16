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
    /// Columns are resolved BY HEADER NAME (read from each file's own line-0 header), so this
    /// normalizer is AGNOSTIC to the live column order — a pure column reorder in the engine writer
    /// needs no change here and no golden recapture. The header is emitted verbatim; the compare-time
    /// GoldenListCanonicalizer permutes both sides into the golden's column order by name before the
    /// positional comparison.
    ///
    /// winner_tracking_id (scan_results) is the encoded id of an exploration group's winning variant
    /// (empty on non-completing rows); it is an id-bearing column relabeled via the shared id map so it
    /// joins run-to-run. child_ids / contributing_scan_ids are space-separated encoded-id lists.
    /// scan_description's leading 3-char tracking-id prefix is relabeled via the shared id map so
    /// descriptors join run-to-run while the deterministic mass/charge remainder is compared verbatim.
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

        // ID-bearing columns per TSV, resolved BY HEADER NAME (order-agnostic to the live column layout).
        // The NAME LISTS are visited in a FIXED logical order so BuildIdMap assigns T<n> labels in the same
        // first-appearance sequence regardless of physical column position — keeping labels byte-identical to
        // the stored (old-order-capture) goldens. child_ids / contributing_scan_ids are space-split.
        private static readonly string[] CmdIdColNames = { "tracking_id", "parent_tracking_id" };
        private static readonly string[] ResIdColNames = { "tracking_id", "parent_tracking_id", "winner_tracking_id" };
        private static readonly string[] IdfIdColNames = { "tracking_id" };
        private const string ResChildColName = "child_ids";            // space-separated encoded ids
        // scan_commands raw descriptor: its first 3 chars are the encoded tracking id (== tracking_id);
        // relabel just that prefix, keep the marker + mass remainder intact.
        private const string CmdDescriptionColName = "scan_description";

        // pooled_identification.tsv volatile id columns: contributing_scan_ids (space-separated) and
        // trigger_scan_id. Both hold base-94 encoded 3-char tracking ids (ScanCommandQueue::encode), so both
        // are relabeled DIRECTLY via the shared id map — pooled ids carry the SAME T<n> labels as every other
        // stream and join run-to-run. The grouped fragment-mass table is numeric masses + string ion labels
        // (no ids) → compared verbatim/toleranced, no relabel.
        private const string PooledScanIdsColName = "contributing_scan_ids";
        private const string PooledTriggerScanIdColName = "trigger_scan_id";

        // Volatile wall-clock columns -> placeholder, keyed BY NAME.
        private static readonly Dictionary<string, string> CmdMaskNames =
            new Dictionary<string, string> { { "enqueue_ts", "<TS>" } };
        private static readonly Dictionary<string, string> ResMaskNames = new Dictionary<string, string>
        {
            { "resolve_ts", "<TS>" },
            { "duration_ms", "<DUR>" },
            { "received_ts", "<TS>" },
            { "duration_received_ms", "<DUR>" },
            { "dequeue_ts", "<TS>" },
            { "queue_duration_ms", "<DUR>" },
            { "instrument_duration_ms", "<DUR>" },
            { "processing_duration_ms", "<DUR>" }
        };
        private static readonly Dictionary<string, string> NoMaskNames = new Dictionary<string, string>();

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

            // Visit id cells in the SAME logical order as before (commands -> results -> identification ->
            // pooled; within each row the name-list order, then the space-split child/scan-id column) so the
            // first-appearance T<n> numbering is byte-identical to the stored (old-order) goldens.
            AddIdsFromTsv(Path.Combine(caseDir, CommandsName), CmdIdColNames, null, Add);
            AddIdsFromTsv(Path.Combine(caseDir, ResultsName), ResIdColNames, ResChildColName, Add);
            AddIdsFromTsv(Path.Combine(caseDir, IdentificationName), IdfIdColNames, null, Add);
            AddIdsFromPooled(Path.Combine(caseDir, PooledName), Add);

            return map;
        }

        // Visit id cells of one TSV in header-name order: for each data row, each name in @idColNames
        // (in list order), then the space-split @childColName column (or none). Missing names are skipped.
        private static void AddIdsFromTsv(string path, string[] idColNames, string childColName, Action<string> add)
        {
            if (!File.Exists(path)) return;
            var lines = File.ReadAllLines(path);
            if (lines.Length == 0) return;
            var hdr = HeaderIndex(lines[0]);
            var idCols = idColNames.Select(n => hdr.TryGetValue(n, out var i) ? i : -1).ToArray();
            int childCol = (childColName != null && hdr.TryGetValue(childColName, out var cc)) ? cc : -1;
            for (int li = 1; li < lines.Length; li++)
            {
                var row = lines[li].Split('\t');
                foreach (var c in idCols) if (c >= 0 && c < row.Length) add(row[c]);
                if (childCol >= 0 && childCol < row.Length && row[childCol].Length > 0)
                    foreach (var k in row[childCol].Split(' ')) add(k);
            }
        }

        // pooled visit order: contributing_scan_ids (space-split) THEN trigger_scan_id, per data row.
        private static void AddIdsFromPooled(string path, Action<string> add)
        {
            if (!File.Exists(path)) return;
            var lines = File.ReadAllLines(path);
            if (lines.Length == 0) return;
            var hdr = HeaderIndex(lines[0]);
            int scanIdsCol = hdr.TryGetValue(PooledScanIdsColName, out var si) ? si : -1;
            int triggerCol = hdr.TryGetValue(PooledTriggerScanIdColName, out var ti) ? ti : -1;
            for (int li = 1; li < lines.Length; li++)
            {
                var row = lines[li].Split('\t');
                if (scanIdsCol >= 0 && scanIdsCol < row.Length && row[scanIdsCol].Length > 0)
                    foreach (var k in row[scanIdsCol].Split(' ')) add(k);
                if (triggerCol >= 0 && triggerCol < row.Length && row[triggerCol].Length > 0)
                    add(row[triggerCol]);
            }
        }

        /// <summary>Normalize one log file (dispatched by name) into golden-comparable text.</summary>
        public static string Normalize(string caseDir, string fileName, Dictionary<string, string> ids)
        {
            string path = Path.Combine(caseDir, fileName);
            if (fileName == CommandsName) return NormalizeTsv(path, ids, CmdIdColNames, null, CmdMaskNames, CmdDescriptionColName);
            if (fileName == ResultsName) return NormalizeTsv(path, ids, ResIdColNames, ResChildColName, ResMaskNames, null);
            if (fileName == IdentificationName) return NormalizeTsv(path, ids, IdfIdColNames, null, NoMaskNames, null);
            if (fileName == PooledName) return NormalizePooled(path, ids);
            if (fileName == IdaLogName) return NormalizeIdaLog(path);
            throw new ArgumentException("unknown log file: " + fileName);
        }

        private static string NormalizeTsv(string path, Dictionary<string, string> ids,
            string[] idColNames, string childColName, Dictionary<string, string> maskNames, string descColName)
        {
            if (!File.Exists(path)) return "";
            var lines = File.ReadAllLines(path);
            if (lines.Length == 0) return "";
            // Resolve every column role from THIS file's own header (order-agnostic).
            var hdr = HeaderIndex(lines[0]);
            var idCols = idColNames.Select(n => hdr.TryGetValue(n, out var i) ? i : -1).ToArray();
            int childCol = (childColName != null && hdr.TryGetValue(childColName, out var cc)) ? cc : -1;
            int descCol = (descColName != null && hdr.TryGetValue(descColName, out var dc)) ? dc : -1;
            var mask = new Dictionary<int, string>();
            foreach (var kv in maskNames) if (hdr.TryGetValue(kv.Key, out var mi)) mask[mi] = kv.Value;

            var sb = new StringBuilder();
            for (int li = 0; li < lines.Length; li++)
            {
                if (li == 0) { sb.Append(lines[li]).Append('\n'); continue; } // header verbatim (permuted at canonicalize time)
                var cols = lines[li].Split('\t');
                foreach (var c in idCols) if (c >= 0 && c < cols.Length) cols[c] = Relabel(cols[c], ids);
                if (childCol >= 0 && childCol < cols.Length && cols[childCol].Length > 0)
                    cols[childCol] = string.Join(" ", cols[childCol].Split(' ').Select(k => Relabel(k, ids)));
                // scan_description: relabel the leading 3-char tracking-id prefix only, leaving
                // the deterministic marker + adaptive-precision mass/charge remainder verbatim.
                if (descCol >= 0 && descCol < cols.Length)
                    cols[descCol] = RelabelDescriptionPrefix(cols[descCol], ids);
                foreach (var kv in mask) if (kv.Key < cols.Length) cols[kv.Key] = kv.Value;
                sb.Append(string.Join("\t", cols)).Append('\n');
            }
            return sb.ToString();
        }

        // pooled_identification.tsv: two volatile id columns — contributing_scan_ids (col 8) and
        // trigger_scan_id (col 18).  Both hold base-94 encoded 3-char tracking ids (col 8 is
        // space-separated); each is relabeled DIRECTLY via the SHARED id map to T<n> labels — the same
        // labels the other streams use, so pooled rows join run-to-run.  All other pooled columns are
        // deterministic and compared verbatim; the pooled stream has NO timestamp/duration columns, so
        // NO masking applies.
        private static string NormalizePooled(string path, Dictionary<string, string> ids)
        {
            if (!File.Exists(path)) return "";
            var lines = File.ReadAllLines(path);
            if (lines.Length == 0) return "";
            var hdr = HeaderIndex(lines[0]);
            int scanIdsCol = hdr.TryGetValue(PooledScanIdsColName, out var si) ? si : -1;
            int triggerCol = hdr.TryGetValue(PooledTriggerScanIdColName, out var ti) ? ti : -1;
            var sb = new StringBuilder();
            for (int li = 0; li < lines.Length; li++)
            {
                if (li == 0) { sb.Append(lines[li]).Append('\n'); continue; } // header verbatim
                var cols = lines[li].Split('\t');
                if (scanIdsCol >= 0 && scanIdsCol < cols.Length && cols[scanIdsCol].Length > 0)
                    cols[scanIdsCol] = string.Join(" ",
                        cols[scanIdsCol].Split(' ').Select(k => Relabel(k, ids)));
                if (triggerCol >= 0 && triggerCol < cols.Length && cols[triggerCol].Length > 0)
                    cols[triggerCol] = Relabel(cols[triggerCol], ids);
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
            // Mask the base-94 Access ID. Anchor on the trailing " - <n> targets" (base-94 ids never
            // contain a space) so a ')' INSIDE the id is consumed too — [^)]+ used to stop at the id's
            // own ')', leaving the format paren as a run-dependent stray "<ID>))".
            text = Regex.Replace(text, @"Access ID .+?\) - ", "Access ID <ID>) - ");
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

        // Build name -> 0-based column index from a header line. First occurrence wins (headers are unique).
        private static Dictionary<string, int> HeaderIndex(string headerLine)
        {
            var map = new Dictionary<string, int>();
            var cols = headerLine.Split('\t');
            for (int i = 0; i < cols.Length; i++) if (!map.ContainsKey(cols[i])) map[cols[i]] = i;
            return map;
        }
    }
}

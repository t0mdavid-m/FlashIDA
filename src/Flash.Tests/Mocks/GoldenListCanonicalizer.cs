using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Flash.Tests.Mocks
{
    /// <summary>
    /// Compare-time, symmetric canonicalization of the reorderable ';'-joined list columns in
    /// FLASHIda's normalized log streams. Applied to BOTH the stored golden and the fresh capture right
    /// before <see cref="GoldenNumericComparer"/> runs — never at capture time — so no golden is ever
    /// re-recaptured (the stored bytes stay untouched; <c>LogGoldenComparer.Normalize</c> is not touched).
    ///
    /// Why: the engine dumps the MS1 deconvolution (scan_results deconv_masses/intensities/charges) and
    /// the MS2/MS3 fragment matches (identification) in INTENSITY order, and ida.log's AllMass line in the
    /// same order. Near-tied entries swap position between the non-deterministic CI OpenMS rebuilds, so the
    /// positional <see cref="GoldenNumericComparer"/> flags a pure reorder as a diff (exploration_hcd flaps
    /// on the 501-mass deconv dump). Sorting each parallel record-tuple by its STABLE numeric key (the mass,
    /// not the jittering intensity) puts both sides in one canonical order, so pure reorders match while any
    /// value / count / integer change still fails downstream.
    ///
    /// The reorderable tuple columns and the ms_level dispatch column are resolved BY NAME from the
    /// caller-supplied reference (golden) header — the canonicalizer is agnostic to the live column order.
    /// Before the list-sort, every row (header + data) is permuted into the reference column order by name
    /// (fail closed if the fresh header is not a permutation of the reference), so a pure engine-writer
    /// column reorder needs no change here and no golden recapture.
    /// </summary>
    internal static class GoldenListCanonicalizer
    {
        // scan_results reorderable deconv 4-tuple and the identification MS2/MS3 fragment tuples are resolved
        // BY NAME (see ResolveColumns) against the reference header, not by hardcoded index.

        private const string AllMassPrefix = "AllMass=";

        // Record-field separator for the deterministic tiebreak string; never occurs in the logged data.
        private const char RecordSep = '|';

        /// <summary>
        /// Return <paramref name="normalizedText"/> permuted into <paramref name="referenceHeader"/> column
        /// order by name and with the reorderable list fields mass-sorted. For the TSV streams every row
        /// (header + data) is first permuted into the reference order (fail closed if the text's header is not
        /// a permutation of the reference), so both sides land in one canonical column order before the
        /// positional compare — no golden recapture on a pure column reorder. ida.log is free text (no
        /// permute). Line count and trailing-newline shape are preserved.
        /// </summary>
        public static string Canonicalize(string fileName, string normalizedText, string[] referenceHeader)
        {
            if (normalizedText == null) return null;

            // Match GoldenNumericComparer's own CRLF handling: fold "\r\n" to "\n" up front so that
            // reordering a list cell (or an AllMass token) can never strand a lone '\r' mid-line, and both
            // sides are canonicalized on an identical line-ending shape. This does not change the line count
            // and GoldenNumericComparer folds "\r\n" again downstream (a no-op on the result).
            string text = normalizedText.Replace("\r\n", "\n");

            if (fileName == LogGoldenComparer.IdaLogName)
                return CanonicalizeIdaLog(text);   // free text: no columns to permute

            if (referenceHeader == null) throw new ArgumentNullException(nameof(referenceHeader));

            // Permute every row into the reference (golden) column order BY NAME; both the golden (identity
            // permute) and the fresh (NEW->golden permute) end up in one order for the positional compare.
            text = PermuteColumnsToReference(fileName, text, referenceHeader);

            if (fileName == LogGoldenComparer.ResultsName)
            {
                int[] deconv = ResolveColumns(referenceHeader,
                    "deconv_masses", "deconv_intensities", "deconv_min_charge", "deconv_max_charge");
                return CanonicalizeTsv(text, cols => ReorderParallelColumns(cols, deconv, deconv[0]));
            }
            if (fileName == LogGoldenComparer.IdentificationName)
            {
                int msLevelCol = ColumnIndex(referenceHeader, "ms_level");
                int[] ms2 = ResolveColumns(referenceHeader,
                    "ms2_fragments", "ms2_fragment_masses", "theoretical_masses", "diff_da", "diff_ppm");
                int ms2Key = ColumnIndex(referenceHeader, "ms2_fragment_masses");
                int[] ms3 = ResolveColumns(referenceHeader,
                    "ms2_fragments", "ms2_fragment_masses", "ms3_fragments", "ms3_fragment_masses",
                    "theoretical_masses", "diff_da", "diff_ppm");
                int ms3Key = ColumnIndex(referenceHeader, "ms3_fragment_masses");
                return CanonicalizeTsv(text, cols =>
                {
                    if (msLevelCol >= cols.Length) return false;
                    string level = cols[msLevelCol];
                    if (level == "2") return ReorderParallelColumns(cols, ms2, ms2Key);
                    if (level == "3") return ReorderParallelColumns(cols, ms3, ms3Key);
                    return false;
                });
            }

            // CommandsName, PooledName: permuted into reference order above; no reorderable list columns.
            return text;
        }

        // Permute every line (header + data) of a normalized TSV stream into referenceHeader column order,
        // matching columns BY NAME. Fail closed if the text's header is not an exact permutation of the
        // reference (a rename/add/drop is a schema change, not a permutation). The identity case (golden vs
        // its own header) leaves the bytes unchanged.
        private static string PermuteColumnsToReference(string fileName, string text, string[] referenceHeader)
        {
            var lines = text.Split('\n');
            if (lines.Length == 0 || lines[0].Length == 0) return text;

            var srcHeader = lines[0].Split('\t');
            if (srcHeader.Length != referenceHeader.Length)
                throw new InvalidOperationException(
                    $"{fileName}: header width {srcHeader.Length} != reference {referenceHeader.Length} (not a permutation)");

            var srcIndex = new Dictionary<string, int>();
            for (int i = 0; i < srcHeader.Length; i++)
                if (!srcIndex.ContainsKey(srcHeader[i])) srcIndex[srcHeader[i]] = i;

            var srcOf = new int[referenceHeader.Length];
            for (int r = 0; r < referenceHeader.Length; r++)
                if (!srcIndex.TryGetValue(referenceHeader[r], out srcOf[r]))
                    throw new InvalidOperationException(
                        $"{fileName}: column '{referenceHeader[r]}' missing from fresh header (not a permutation)");

            for (int li = 0; li < lines.Length; li++)
            {
                if (lines[li].Length == 0) continue; // preserved trailing empty line
                var cols = lines[li].Split('\t');
                var outCols = new string[referenceHeader.Length];
                bool full = true;
                for (int r = 0; r < referenceHeader.Length; r++)
                {
                    int s = srcOf[r];
                    if (s < cols.Length) outCols[r] = cols[s];
                    else { full = false; break; }
                }
                if (full) lines[li] = string.Join("\t", outCols);
                // ragged rows (should never happen for full TSV output) pass through verbatim
            }
            return string.Join("\n", lines);
        }

        // Resolve each name to its 0-based index in header; throw if any is missing (schema drift).
        private static int[] ResolveColumns(string[] header, params string[] names)
        {
            var idx = new int[names.Length];
            for (int i = 0; i < names.Length; i++) idx[i] = ColumnIndex(header, names[i]);
            return idx;
        }

        private static int ColumnIndex(string[] header, string name)
        {
            int i = Array.IndexOf(header, name);
            if (i < 0) throw new InvalidOperationException($"canonicalizer: column '{name}' not found in reference header");
            return i;
        }

        // Rewrite every data row (line 0 header stays verbatim) via rowRewriter, which mutates cols in place
        // and returns true when it changed something. Blank trailing line (from the final '\n') passes through.
        private static string CanonicalizeTsv(string text, Func<string[], bool> rowRewriter)
        {
            var lines = text.Split('\n');
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue; // preserved trailing empty line
                var cols = lines[i].Split('\t');
                if (rowRewriter(cols))
                    lines[i] = string.Join("\t", cols);
            }
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Treat the named columns as PARALLEL ';'-joined lists forming one record per element. Stable-sort
        /// the records by the numeric <paramref name="keyColumn"/> value (with the full record string as a
        /// deterministic tiebreak so identical multisets sort identically on both sides), then reorder every
        /// column by that permutation. Ragged / empty / missing / non-numeric-key rows are left verbatim so a
        /// real desync still fails downstream. Returns true iff the row was rewritten.
        /// </summary>
        private static bool ReorderParallelColumns(string[] cols, int[] columnIndices, int keyColumn)
        {
            foreach (int c in columnIndices)
                if (c < 0 || c >= cols.Length) return false;

            var parts = new string[columnIndices.Length][];
            int n = -1;
            for (int j = 0; j < columnIndices.Length; j++)
            {
                string cell = cols[columnIndices[j]];
                if (cell.Length == 0) return false;              // empty list -> skip verbatim
                parts[j] = cell.Split(';');
                if (n < 0) n = parts[j].Length;
                else if (parts[j].Length != n) return false;     // ragged -> skip verbatim
            }
            if (n <= 1) return false;                            // 0/1 element: nothing to reorder

            int keyPos = Array.IndexOf(columnIndices, keyColumn);
            if (keyPos < 0) return false;

            var keys = new double[n];
            var records = new string[n];
            for (int i = 0; i < n; i++)
            {
                if (!double.TryParse(parts[keyPos][i], NumberStyles.Float, CultureInfo.InvariantCulture, out keys[i]))
                    return false;                                // non-numeric key -> skip verbatim
                var rec = new StringBuilder();
                for (int j = 0; j < parts.Length; j++)
                {
                    if (j > 0) rec.Append(RecordSep);
                    rec.Append(parts[j][i]);
                }
                records[i] = rec.ToString();
            }

            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, (x, y) =>
            {
                int c = keys[x].CompareTo(keys[y]);
                if (c != 0) return c;
                c = string.CompareOrdinal(records[x], records[y]); // deterministic tiebreak
                if (c != 0) return c;
                return x.CompareTo(y);                              // final tiebreak -> stable
            });

            for (int j = 0; j < columnIndices.Length; j++)
            {
                var reordered = new string[n];
                for (int i = 0; i < n; i++) reordered[i] = parts[j][order[i]];
                cols[columnIndices[j]] = string.Join(";", reordered);
            }
            return true;
        }

        // ida.log: sort only the AllMass=<space-separated masses> lines by numeric value (stable, with the
        // token string as tiebreak), preserving the exact token text so the numeric comparer still tolerances
        // the low-digit jitter. Every other line (Scan#, Mass=, Features, Window, ...) is left verbatim.
        private static string CanonicalizeIdaLog(string text)
        {
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith(AllMassPrefix, StringComparison.Ordinal)) continue;
                string rhs = lines[i].Substring(AllMassPrefix.Length);
                if (rhs.Length == 0) continue;

                var toks = rhs.Split(' ');
                var vals = new double[toks.Length];
                bool ok = true;
                for (int k = 0; k < toks.Length; k++)
                    if (!double.TryParse(toks[k], NumberStyles.Float, CultureInfo.InvariantCulture, out vals[k]))
                    {
                        ok = false;
                        break;
                    }
                if (!ok) continue; // malformed -> leave verbatim

                var order = new int[toks.Length];
                for (int k = 0; k < toks.Length; k++) order[k] = k;
                Array.Sort(order, (x, y) =>
                {
                    int c = vals[x].CompareTo(vals[y]);
                    if (c != 0) return c;
                    c = string.CompareOrdinal(toks[x], toks[y]);
                    if (c != 0) return c;
                    return x.CompareTo(y);
                });

                var sorted = new string[toks.Length];
                for (int k = 0; k < toks.Length; k++) sorted[k] = toks[order[k]];
                lines[i] = AllMassPrefix + string.Join(" ", sorted);
            }
            return string.Join("\n", lines);
        }
    }
}

using System;
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
    /// Column indices below are 0-based and verified against the committed golden headers
    /// (test-data/golden/logs/exploration_hcd/scan_results.tsv.golden.tsv and
    /// test-data/golden/logs/ms3_cytc/identification.tsv.golden.tsv).
    /// </summary>
    internal static class GoldenListCanonicalizer
    {
        // scan_results.tsv reorderable deconv columns (verified: header order tracking_id..winner_tracking_id).
        private const int ResDeconvMasses = 19;
        private const int ResDeconvIntensities = 20;
        private const int ResDeconvMinCharge = 21;
        private const int ResDeconvMaxCharge = 22;

        // identification.tsv columns (verified: ms_level leads; fragment/theoretical/diff blocks parallel).
        private const int IdMsLevel = 0;
        private const int IdMs2Fragments = 15;
        private const int IdMs2FragmentMasses = 16;
        private const int IdMs3Fragments = 17;
        private const int IdMs3FragmentMasses = 18;
        private const int IdTheoreticalMasses = 26;
        private const int IdDiffDa = 27;
        private const int IdDiffPpm = 28;

        private const string AllMassPrefix = "AllMass=";

        // Record-field separator for the deterministic tiebreak string; never occurs in the logged data.
        private const char RecordSep = '|';

        /// <summary>
        /// Return <paramref name="normalizedText"/> with the reorderable list fields of the given stream
        /// canonicalized (mass-sorted). Header (line 0 of the TSV streams) is left verbatim, line count and
        /// trailing-newline shape are preserved. CommandsName / PooledName / any unknown stream: unchanged.
        /// </summary>
        public static string Canonicalize(string fileName, string normalizedText)
        {
            if (normalizedText == null) return null;

            // Match GoldenNumericComparer's own CRLF handling: fold "\r\n" to "\n" up front so that
            // reordering a list cell (or an AllMass token) can never strand a lone '\r' mid-line, and both
            // sides are canonicalized on an identical line-ending shape. This does not change the line count
            // and GoldenNumericComparer folds "\r\n" again downstream (a no-op on the result).
            string text = normalizedText.Replace("\r\n", "\n");

            if (fileName == LogGoldenComparer.ResultsName)
                return CanonicalizeTsv(text, CanonicalizeResultsRow);
            if (fileName == LogGoldenComparer.IdentificationName)
                return CanonicalizeTsv(text, CanonicalizeIdentificationRow);
            if (fileName == LogGoldenComparer.IdaLogName)
                return CanonicalizeIdaLog(text);

            // CommandsName, PooledName (combined_* lists / id columns), unknown streams: leave untouched.
            return text;
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

        // scan_results data row: reorder the deconv 4-tuple by the (stable, printed-precision) mass key.
        private static bool CanonicalizeResultsRow(string[] cols)
        {
            return ReorderParallelColumns(cols,
                new[] { ResDeconvMasses, ResDeconvIntensities, ResDeconvMinCharge, ResDeconvMaxCharge },
                ResDeconvMasses);
        }

        // identification data row: dispatch by ms_level (col 0). MS1 / other / empty fragment lists: skip.
        private static bool CanonicalizeIdentificationRow(string[] cols)
        {
            if (IdMsLevel >= cols.Length) return false;
            string level = cols[IdMsLevel];
            if (level == "2")
                return ReorderParallelColumns(cols,
                    new[] { IdMs2Fragments, IdMs2FragmentMasses, IdTheoreticalMasses, IdDiffDa, IdDiffPpm },
                    IdMs2FragmentMasses);
            if (level == "3")
                return ReorderParallelColumns(cols,
                    new[]
                    {
                        IdMs2Fragments, IdMs2FragmentMasses, IdMs3Fragments, IdMs3FragmentMasses,
                        IdTheoreticalMasses, IdDiffDa, IdDiffPpm
                    },
                    IdMs3FragmentMasses);
            return false;
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

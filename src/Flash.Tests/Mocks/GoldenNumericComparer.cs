using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Flash.Tests.Mocks
{
    /// <summary>
    /// Numeric-aware golden comparison. Walks two strings token by token: floating-point numbers
    /// (tokens containing '.', 'e' or 'E') compare with tolerance; integer-looking numbers and all
    /// non-numeric text compare exactly.
    ///
    /// Rationale: each CI run rebuilds OpenMS into a different binary (verified: differing OpenMS.dll
    /// SHAs), so the engine's floating-point score fields (Qscore, ChargeCos, ChargeSnr, Snr and the
    /// logged scores) jitter ~1e-8..3e-5 run to run. Exact-string golden comparison can therefore never
    /// converge. Tolerancing the FLOAT tokens absorbs that jitter while integer tokens (scan ids, counts,
    /// MS levels, charge states, -1 sentinels) and all structural text stay strict, so real regressions
    /// (precursor/charge/count/activation/sequence/id changes) are still caught.
    ///
    /// Float compare uses the symmetric math.isclose form: |x - y| &lt;= max(AbsTol, RelTol*max(|x|,|y|)).
    /// AbsTol=1e-5 matches compare_golden.py's ABS_TOL (the small-value floor). RelTol=1e-3 is calibrated
    /// empirically: the worst observed cross-build jitter is 3.79e-5 (SNR fields), so 1e-3 gives ~26x
    /// headroom against flakiness; goldens store only 5-6 sig figs so 1e-3 still catches real regressions.
    /// </summary>
    internal static class GoldenNumericComparer
    {
        private const double AbsTol = 1e-5;
        private const double RelTol = 1e-3;

        // optional sign, digits, optional fraction, optional exponent
        private static readonly Regex NumberRx = new Regex(@"-?\d+\.?\d*(?:[eE][+-]?\d+)?");

        /// <summary>
        /// True if expected and actual are equivalent under the rules above. Line-ending agnostic.
        /// On mismatch, <paramref name="diff"/> describes the first difference for the failure message.
        /// </summary>
        public static bool Equivalent(string expected, string actual, out string diff)
        {
            var e = (expected ?? "").Replace("\r\n", "\n").Split('\n');
            var a = (actual ?? "").Replace("\r\n", "\n").Split('\n');
            if (e.Length != a.Length)
            {
                diff = string.Format("line count {0} vs {1}", e.Length, a.Length);
                return false;
            }
            for (int i = 0; i < e.Length; i++)
            {
                if (!LineEquivalent(e[i], a[i], out string d))
                {
                    diff = string.Format("line {0}: {1}", i + 1, d);
                    return false;
                }
            }
            diff = null;
            return true;
        }

        private static bool LineEquivalent(string e, string a, out string diff)
        {
            // The literal text between numbers (and, implicitly, the number count/positions) must match
            // exactly: blank every number to a single sentinel and compare the skeletons.
            if (NumberRx.Replace(e, "\0") != NumberRx.Replace(a, "\0"))
            {
                diff = string.Format("text differs: <{0}> vs <{1}>", e, a);
                return false;
            }

            var en = NumberRx.Matches(e);
            var an = NumberRx.Matches(a);
            if (en.Count != an.Count)
            {
                diff = "number count differs";
                return false;
            }

            for (int k = 0; k < en.Count; k++)
            {
                string eTok = en[k].Value;
                string aTok = an[k].Value;
                if (eTok == aTok) continue;

                bool floaty = eTok.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0
                           || aTok.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0;
                if (!floaty)
                {
                    // integer token (ids, counts, levels, sentinels) -> exact
                    diff = string.Format("int {0} vs {1}", eTok, aTok);
                    return false;
                }

                double ev = double.Parse(eTok, CultureInfo.InvariantCulture);
                double av = double.Parse(aTok, CultureInfo.InvariantCulture);
                double tol = Math.Max(AbsTol, RelTol * Math.Max(Math.Abs(ev), Math.Abs(av)));
                if (Math.Abs(ev - av) > tol)
                {
                    diff = string.Format("float {0} vs {1} out of tol", eTok, aTok);
                    return false;
                }
            }

            diff = null;
            return true;
        }
    }
}

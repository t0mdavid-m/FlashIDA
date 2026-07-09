using Flash.Tests.Mocks;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// Unit tests for the numeric-aware golden comparison primitive. Float tokens tolerate cross-build
    /// jitter; integer tokens, strings, and structure stay exact so real regressions are still caught.
    /// </summary>
    [TestFixture]
    public class GoldenNumericComparerTests
    {
        private static bool Eq(string a, string b)
        {
            return GoldenNumericComparer.Equivalent(a, b, out _);
        }

        [Test] // 1
        public void Identical_Equivalent()
        {
            Assert.IsTrue(Eq("{\"Qscore\":0.5,\"ChargeState\":14}", "{\"Qscore\":0.5,\"ChargeState\":14}"));
        }

        [Test] // 2 — real observed Qscore jitter (~5.6e-8)
        public void FloatWithinTol_Equivalent()
        {
            Assert.IsTrue(Eq("\"Qscore\":0.96776742787238634", "\"Qscore\":0.96776748349528086"));
        }

        [Test] // 3 — a real score change must fail
        public void FloatBeyondTol_NotEquivalent()
        {
            Assert.IsFalse(Eq("\"Qscore\":0.5281", "\"Qscore\":0.6000"));
        }

        [Test] // 4 — §G 5-decimal last-place jitter (Δ≈1e-5), covered by the relative term
        public void FloatLastPlaceJitter_Equivalent()
        {
            Assert.IsTrue(Eq("score\t0.67697\tend", "score\t0.67698\tend"));
        }

        [Test] // 5 — integer differences (scan id, charge state) stay strict
        public void IntegerDiffers_NotEquivalent()
        {
            Assert.IsFalse(Eq("T87\tHCD", "T88\tHCD"));
            Assert.IsFalse(Eq("\"ChargeState\":14", "\"ChargeState\":15"));
        }

        [Test] // 6 — -1 sentinel is an integer -> exact
        public void Sentinel_ExactInteger()
        {
            Assert.IsTrue(Eq("frag_count\t-1", "frag_count\t-1"));
            Assert.IsFalse(Eq("frag_count\t-1", "frag_count\t-2"));
        }

        [Test] // 7 — non-numeric text must match exactly
        public void StringDiffers_NotEquivalent()
        {
            Assert.IsFalse(Eq("\"ActivationType\":\"HCD\"", "\"ActivationType\":\"ETD\""));
        }

        [Test] // 8 — structural changes are caught
        public void Structural_NotEquivalent()
        {
            Assert.IsFalse(Eq("1 2 3", "1 2"));        // different number count
            Assert.IsFalse(Eq("a\nb", "a\nb\nc"));     // different line count
        }

        [Test] // 9 — exponent notation handled
        public void Exponent_WithinTol_Equivalent()
        {
            Assert.IsTrue(Eq("x=5E-08", "x=5.1E-08"));
        }

        [Test] // 10 — mass token: fractional part tolerates jitter, integer id stays strict
        public void MassToken_FloatTolerant_IntStrict()
        {
            Assert.IsTrue(Eq("T87R2.256179k@4", "T87R2.256180k@4"));   // mass fraction within tol
            Assert.IsFalse(Eq("T87R2.256179k@4", "T88R2.256179k@4"));  // id 87 vs 88 -> exact fail
        }

        // -------------------------------------------------------------------------------------------------
        // GoldenListCanonicalizer: compare-time SYMMETRIC mass-sort of the reorderable ';'-joined list
        // columns. The engine dumps the MS1 deconvolution (scan_results), the MS2/MS3 fragment matches
        // (identification), and ida.log's AllMass line in INTENSITY order, so near-tied entries swap
        // position between non-deterministic CI builds. Canonicalizing BOTH sides (as CompareOne does) must
        // make a pure REORDER Equivalent while any value / count / integer change still FAILS.
        //
        // Each input is a header line + ONE data row with the REAL column count (scan_results 29,
        // identification 31); every column outside the tuple is the placeholder "px" (identical on both
        // sides so GoldenNumericComparer's skeleton check passes).
        // -------------------------------------------------------------------------------------------------

        // Canonicalize both sides (like FLASHIdaLogGolden_test.CompareOne) then run the numeric comparer.
        private static bool EqCanon(string fileName, string golden, string fresh)
        {
            string g = GoldenListCanonicalizer.Canonicalize(fileName, golden);
            string f = GoldenListCanonicalizer.Canonicalize(fileName, fresh);
            return GoldenNumericComparer.Equivalent(g, f, out _);
        }

        private static string[] Blank(int n)
        {
            var c = new string[n];
            for (int i = 0; i < n; i++) c[i] = "px";
            return c;
        }

        private static string Doc(params string[] lines)
        {
            return string.Join("\n", lines) + "\n"; // mirror Normalize's trailing-newline shape
        }

        private static readonly string ResultsHeader = string.Join("\t", Blank(29));
        private static readonly string IdHeader = string.Join("\t", Blank(31));

        // scan_results data row: deconv 4-tuple at cols 19-22, everything else placeholder.
        private static string ResultsRow(string masses, string ints, string minC, string maxC)
        {
            var c = Blank(29);
            c[0] = "T1";
            c[19] = masses;
            c[20] = ints;
            c[21] = minC;
            c[22] = maxC;
            return string.Join("\t", c);
        }

        // identification ms_level==2 data row: fragment 5-tuple at cols 15,16,26,27,28.
        private static string Ms2Row(string frags, string masses, string theo, string dda, string dppm)
        {
            var c = Blank(31);
            c[0] = "2";   // ms_level
            c[2] = "T4";  // tracking_id
            c[15] = frags;
            c[16] = masses;
            c[26] = theo;
            c[27] = dda;
            c[28] = dppm;
            return string.Join("\t", c);
        }

        [Test] // 11a — a reordered scan_results deconv 4-tuple (same multiset) -> Equivalent after canonicalize
        public void Canon_Results_Reorder_Equivalent()
        {
            string golden = Doc(ResultsHeader,
                ResultsRow("100.5;200.5;300.5", "1000.0;2000.0;3000.0", "1;2;3", "4;5;6"));
            string fresh = Doc(ResultsHeader,
                ResultsRow("300.5;100.5;200.5", "3000.0;1000.0;2000.0", "3;1;2", "6;4;5"));
            Assert.IsFalse(Eq(golden, fresh), "pre-canonicalize the reorder must differ (guards vacuity)");
            Assert.IsTrue(EqCanon(LogGoldenComparer.ResultsName, golden, fresh));
        }

        [Test] // 11b — a DROPPED deconv entry (count change) still FAILS
        public void Canon_Results_DroppedEntry_NotEquivalent()
        {
            string golden = Doc(ResultsHeader,
                ResultsRow("100.5;200.5;300.5", "1000.0;2000.0;3000.0", "1;2;3", "4;5;6"));
            string fresh = Doc(ResultsHeader,
                ResultsRow("100.5;200.5", "1000.0;2000.0", "1;2", "4;5"));
            Assert.IsFalse(EqCanon(LogGoldenComparer.ResultsName, golden, fresh));
        }

        [Test] // 11c — a deconv MASS changed beyond tol still FAILS (order preserved -> only the value differs)
        public void Canon_Results_MassChanged_NotEquivalent()
        {
            string golden = Doc(ResultsHeader,
                ResultsRow("100.5;200.5;300.5", "1000.0;2000.0;3000.0", "1;2;3", "4;5;6"));
            string fresh = Doc(ResultsHeader,
                ResultsRow("150.5;200.5;300.5", "1000.0;2000.0;3000.0", "1;2;3", "4;5;6"));
            Assert.IsFalse(EqCanon(LogGoldenComparer.ResultsName, golden, fresh));
        }

        [Test] // 11d — a changed deconv_min_charge INT (reordered otherwise) still FAILS: ints stay exact
        public void Canon_Results_MinChargeInt_NotEquivalent()
        {
            string golden = Doc(ResultsHeader,
                ResultsRow("100.5;200.5;300.5", "1000.0;2000.0;3000.0", "1;2;3", "4;5;6"));
            // same multiset reordered C,A,B but record B's min_charge 2 -> 9 (record still sorts by its mass)
            string fresh = Doc(ResultsHeader,
                ResultsRow("300.5;100.5;200.5", "3000.0;1000.0;2000.0", "3;1;9", "6;4;5"));
            Assert.IsFalse(EqCanon(LogGoldenComparer.ResultsName, golden, fresh));
        }

        [Test] // 11e — identification ms_level==2 fragment 5-tuple reordered -> Equivalent after canonicalize
        public void Canon_Identification_Ms2_Reorder_Equivalent()
        {
            string golden = Doc(IdHeader,
                Ms2Row("y5;b3;y2", "500.5;300.3;200.2", "500.6;300.4;200.3", "-0.1;-0.2;-0.3", "-1.0;-1.5;-2.0"));
            string fresh = Doc(IdHeader,
                Ms2Row("y2;y5;b3", "200.2;500.5;300.3", "200.3;500.6;300.4", "-0.3;-0.1;-0.2", "-2.0;-1.0;-1.5"));
            Assert.IsFalse(Eq(golden, fresh), "pre-canonicalize the reorder must differ (guards vacuity)");
            Assert.IsTrue(EqCanon(LogGoldenComparer.IdentificationName, golden, fresh));
        }

        [Test] // 11e' — a changed fragment ion-index INT still FAILS
        public void Canon_Identification_Ms2_IonIndexInt_NotEquivalent()
        {
            string golden = Doc(IdHeader,
                Ms2Row("y5;b3;y2", "500.5;300.3;200.2", "500.6;300.4;200.3", "-0.1;-0.2;-0.3", "-1.0;-1.5;-2.0"));
            // identical masses so records align after canonicalize; only ion label y5 -> y6 changes
            string fresh = Doc(IdHeader,
                Ms2Row("y6;b3;y2", "500.5;300.3;200.2", "500.6;300.4;200.3", "-0.1;-0.2;-0.3", "-1.0;-1.5;-2.0"));
            Assert.IsFalse(EqCanon(LogGoldenComparer.IdentificationName, golden, fresh));
        }

        [Test] // 11f — ida.log AllMass reordered -> Equivalent after canonicalize
        public void Canon_IdaLog_AllMass_Reorder_Equivalent()
        {
            string golden = Doc("Scan# <SCAN>", "AllMass=100.5 200.5 300.5");
            string fresh = Doc("Scan# <SCAN>", "AllMass=300.5 100.5 200.5");
            Assert.IsFalse(Eq(golden, fresh), "pre-canonicalize the reorder must differ (guards vacuity)");
            Assert.IsTrue(EqCanon(LogGoldenComparer.IdaLogName, golden, fresh));
        }

        [Test] // 11f' — an AllMass value change OR a dropped mass still FAILS
        public void Canon_IdaLog_AllMass_ValueOrCount_NotEquivalent()
        {
            string golden = Doc("Scan# <SCAN>", "AllMass=100.5 200.5 300.5");
            string changed = Doc("Scan# <SCAN>", "AllMass=100.5 250.5 300.5"); // one mass moved beyond tol
            string dropped = Doc("Scan# <SCAN>", "AllMass=100.5 300.5");        // a mass removed (count change)
            Assert.IsFalse(EqCanon(LogGoldenComparer.IdaLogName, golden, changed));
            Assert.IsFalse(EqCanon(LogGoldenComparer.IdaLogName, golden, dropped));
        }
    }
}

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
        // identification 32). The header carries the REAL column NAMES at the tuple positions so the
        // name-based canonicalizer resolves them; every other column is a unique placeholder name with a
        // "px" data value (identical on both sides so GoldenNumericComparer's skeleton check passes). The
        // same header is passed as the canonicalizer's reference (an identity permute for these rows).
        // -------------------------------------------------------------------------------------------------

        // Canonicalize both sides (like FLASHIdaLogGolden_test.CompareOne) then run the numeric comparer.
        // referenceHeader is the golden's header row (the canonical column order); ida.log ignores it.
        private static bool EqCanon(string fileName, string golden, string fresh, string[] referenceHeader)
        {
            string g = GoldenListCanonicalizer.Canonicalize(fileName, golden, referenceHeader);
            string f = GoldenListCanonicalizer.Canonicalize(fileName, fresh, referenceHeader);
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

        // Header with UNIQUE placeholder names ("c0","c1",...) except the tuple positions, which carry the
        // REAL names the name-based canonicalizer resolves. Unique names keep the permute a clean 1:1 map.
        private static string[] MakeHeader(int n)
        {
            var h = new string[n];
            for (int i = 0; i < n; i++) h[i] = "c" + i;
            return h;
        }

        // The four parallel deconv lists the canonicalizer keys on, in row order: deconv_masses,
        // deconv_qscores (one PeakGroup qscore per deconvolved mass, index-aligned 1:1 with
        // deconv_masses), then deconv_charges / deconv_intensities — the per-charge pair that replaced
        // deconv_min_charge / deconv_max_charge and the summed intensity. Only the NAMES matter here:
        // the canonicalizer resolves the tuple by name, so these synthetic indices need not match the
        // live writer's, but the width (29) is the real scan_results column count.
        private static string[] BuildResultsHeader()
        {
            var h = MakeHeader(32);
            h[19] = "deconv_masses"; h[20] = "deconv_qscores";
            h[21] = "deconv_charges"; h[22] = "deconv_intensities";
            return h;
        }

        private static string[] BuildIdHeader()
        {
            var h = MakeHeader(34);
            h[0] = "ms_level";
            h[15] = "ms2_fragments"; h[16] = "ms2_fragment_masses";
            h[17] = "ms3_fragments"; h[18] = "ms3_fragment_masses";
            h[26] = "theoretical_masses"; h[27] = "diff_da"; h[28] = "diff_ppm";
            // Sixth member of the reorderable fragment tuple. It needs a REAL name, not a "cN"
            // placeholder: ColumnIndex throws on a missing name, and a placeholder cell would trip
            // ReorderParallelColumns' ragged-length guard instead of exercising the permute.
            h[29] = "fragment_qscores";
            return h;
        }

        private static readonly string[] ResultsHeaderCols = BuildResultsHeader();
        private static readonly string[] IdHeaderCols = BuildIdHeader();
        private static readonly string ResultsHeader = string.Join("\t", ResultsHeaderCols);
        private static readonly string IdHeader = string.Join("\t", IdHeaderCols);

        // scan_results data row: the deconv 4-tuple at cols 19-22, everything else placeholder.
        // One row of the four parallel deconv lists: ';' separates PeakGroups in all four, ',' separates
        // the charge states within one PeakGroup's envelope. masses and qscores are ONE value per
        // PeakGroup, so they never carry a ',' — they are still ';'-aligned with charges/intensities.
        private static string ResultsRow(string masses, string qscores, string charges, string ints)
        {
            var c = Blank(32);
            c[0] = "T1";
            c[19] = masses;
            c[20] = qscores;
            c[21] = charges;
            c[22] = ints;
            return string.Join("\t", c);
        }

        // identification ms_level==2 data row: fragment 5-tuple at cols 15,16,26,27,28.
        private static string Ms2Row(string frags, string masses, string theo, string dda, string dppm)
        {
            var c = Blank(34);
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
            // records A=(100.5,0.91) B=(200.5,0.82) C=(300.5,0.73); golden order A,B,C, fresh order C,A,B.
            // The qscores ride the permutation exactly as the charges and intensities do.
            string golden = Doc(ResultsHeader,
                ResultsRow("100.5;200.5;300.5", "0.91;0.82;0.73", "1,2;3,4;5", "10.0,11.0;20.0,21.0;30.0"));
            string fresh = Doc(ResultsHeader,
                ResultsRow("300.5;100.5;200.5", "0.73;0.91;0.82", "5;1,2;3,4", "30.0;10.0,11.0;20.0,21.0"));
            Assert.IsFalse(Eq(golden, fresh), "pre-canonicalize the reorder must differ (guards vacuity)");
            Assert.IsTrue(EqCanon(LogGoldenComparer.ResultsName, golden, fresh, ResultsHeaderCols));
        }

        [Test] // 11b — a DROPPED deconv entry (count change) still FAILS
        public void Canon_Results_DroppedEntry_NotEquivalent()
        {
            string golden = Doc(ResultsHeader,
                ResultsRow("100.5;200.5;300.5", "0.91;0.82;0.73", "1,2;3,4;5", "10.0,11.0;20.0,21.0;30.0"));
            string fresh = Doc(ResultsHeader,
                ResultsRow("100.5;200.5", "0.91;0.82", "1,2;3,4", "10.0,11.0;20.0,21.0"));
            Assert.IsFalse(EqCanon(LogGoldenComparer.ResultsName, golden, fresh, ResultsHeaderCols));
        }

        [Test] // 11c — a deconv MASS changed beyond tol still FAILS (order preserved -> only the value differs)
        public void Canon_Results_MassChanged_NotEquivalent()
        {
            string golden = Doc(ResultsHeader,
                ResultsRow("100.5;200.5;300.5", "0.91;0.82;0.73", "1,2;3,4;5", "10.0,11.0;20.0,21.0;30.0"));
            string fresh = Doc(ResultsHeader,
                ResultsRow("150.5;200.5;300.5", "0.91;0.82;0.73", "1,2;3,4;5", "10.0,11.0;20.0,21.0;30.0"));
            Assert.IsFalse(EqCanon(LogGoldenComparer.ResultsName, golden, fresh, ResultsHeaderCols));
        }

        [Test] // 11d — a changed deconv_charges INT (reordered otherwise) still FAILS: ints stay exact
        public void Canon_Results_ChargeListInt_NotEquivalent()
        {
            string golden = Doc(ResultsHeader,
                ResultsRow("100.5;200.5;300.5", "0.91;0.82;0.73", "1,2;3,4;5", "10.0,11.0;20.0,21.0;30.0"));
            // same multiset reordered C,A,B (qscores permuted with it) but record B's second charge 4 -> 9,
            // so the records still sort by mass and ONLY the charge int differs after canonicalize
            string fresh = Doc(ResultsHeader,
                ResultsRow("300.5;100.5;200.5", "0.73;0.91;0.82", "5;1,2;3,9", "30.0;10.0,11.0;20.0,21.0"));
            Assert.IsFalse(EqCanon(LogGoldenComparer.ResultsName, golden, fresh, ResultsHeaderCols));
        }

        [Test] // 11d' — a DROPPED charge state within one envelope still FAILS: the ',' arity is data
        public void Canon_Results_ChargeDroppedWithinEnvelope_NotEquivalent()
        {
            string golden = Doc(ResultsHeader,
                ResultsRow("100.5;200.5;300.5", "0.91;0.82;0.73", "1,2;3,4;5", "10.0,11.0;20.0,21.0;30.0"));
            // record A loses charge 2: same PeakGroup count, same masses, same qscores (one per PeakGroup,
            // so the drop is invisible there), one fewer isolated charge. Summed intensities could not
            // express this at all -- it is exactly what the per-charge schema exists to make visible.
            string fresh = Doc(ResultsHeader,
                ResultsRow("100.5;200.5;300.5", "0.91;0.82;0.73", "1;3,4;5", "10.0;20.0,21.0;30.0"));
            Assert.IsFalse(EqCanon(LogGoldenComparer.ResultsName, golden, fresh, ResultsHeaderCols));
        }

        [Test] // 11e — identification ms_level==2 fragment 5-tuple reordered -> Equivalent after canonicalize
        public void Canon_Identification_Ms2_Reorder_Equivalent()
        {
            string golden = Doc(IdHeader,
                Ms2Row("y5;b3;y2", "500.5;300.3;200.2", "500.6;300.4;200.3", "-0.1;-0.2;-0.3", "-1.0;-1.5;-2.0"));
            string fresh = Doc(IdHeader,
                Ms2Row("y2;y5;b3", "200.2;500.5;300.3", "200.3;500.6;300.4", "-0.3;-0.1;-0.2", "-2.0;-1.0;-1.5"));
            Assert.IsFalse(Eq(golden, fresh), "pre-canonicalize the reorder must differ (guards vacuity)");
            Assert.IsTrue(EqCanon(LogGoldenComparer.IdentificationName, golden, fresh, IdHeaderCols));
        }

        [Test] // 11e' — a changed fragment ion-index INT still FAILS
        public void Canon_Identification_Ms2_IonIndexInt_NotEquivalent()
        {
            string golden = Doc(IdHeader,
                Ms2Row("y5;b3;y2", "500.5;300.3;200.2", "500.6;300.4;200.3", "-0.1;-0.2;-0.3", "-1.0;-1.5;-2.0"));
            // identical masses so records align after canonicalize; only ion label y5 -> y6 changes
            string fresh = Doc(IdHeader,
                Ms2Row("y6;b3;y2", "500.5;300.3;200.2", "500.6;300.4;200.3", "-0.1;-0.2;-0.3", "-1.0;-1.5;-2.0"));
            Assert.IsFalse(EqCanon(LogGoldenComparer.IdentificationName, golden, fresh, IdHeaderCols));
        }

        [Test] // 11f — ida.log AllMass reordered -> Equivalent after canonicalize
        public void Canon_IdaLog_AllMass_Reorder_Equivalent()
        {
            string golden = Doc("Scan# <SCAN>", "AllMass=100.5 200.5 300.5");
            string fresh = Doc("Scan# <SCAN>", "AllMass=300.5 100.5 200.5");
            Assert.IsFalse(Eq(golden, fresh), "pre-canonicalize the reorder must differ (guards vacuity)");
            Assert.IsTrue(EqCanon(LogGoldenComparer.IdaLogName, golden, fresh, null));
        }

        [Test] // 11f' — an AllMass value change OR a dropped mass still FAILS
        public void Canon_IdaLog_AllMass_ValueOrCount_NotEquivalent()
        {
            string golden = Doc("Scan# <SCAN>", "AllMass=100.5 200.5 300.5");
            string changed = Doc("Scan# <SCAN>", "AllMass=100.5 250.5 300.5"); // one mass moved beyond tol
            string dropped = Doc("Scan# <SCAN>", "AllMass=100.5 300.5");        // a mass removed (count change)
            Assert.IsFalse(EqCanon(LogGoldenComparer.IdaLogName, golden, changed, null));
            Assert.IsFalse(EqCanon(LogGoldenComparer.IdaLogName, golden, dropped, null));
        }
    }
}

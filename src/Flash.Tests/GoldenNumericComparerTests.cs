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
    }
}

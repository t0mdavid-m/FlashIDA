using System;
using System.IO;
using System.Linq;
using Flash.Tests.Mocks;
using NUnit.Framework;

namespace Flash.Tests
{
    /// <summary>
    /// Guards the two order-agnostic primitives introduced with the golden-log column reorder:
    ///   * <see cref="LogGoldenComparer"/> resolves id / mask / description columns BY HEADER NAME, so a
    ///     column moving to a new physical position is masked / relabeled at its NEW position, not the old
    ///     index;
    ///   * <see cref="GoldenListCanonicalizer.Canonicalize(string,string,string[])"/> permutes a row into a
    ///     reference header's column order BY NAME (values preserved), and fails closed when the input header
    ///     is not a permutation of the reference (a rename/add/drop is a schema change, not a reorder).
    /// These make the frozen (old-order) goldens match the NEW-order live output with no recapture.
    /// </summary>
    [TestFixture]
    public class LogColumnReorderTests
    {
        // The scan_commands header in the NEW (reordered) live-writer order — enqueue_ts is now LAST (index
        // 31), parent_tracking_id is index 3 (the OLD masked index), scan_description index 28.
        // These tests are synthetic and self-consistent, so they pass whatever this array contains; it is
        // kept in step with IdaLogger.cpp's header emit anyway, because a stale schema mirror is exactly
        // the kind of thing a later reader trusts. ADR-0012 added faims_enabled at index 30.
        private static readonly string[] NewCommandsHeader =
        {
            "tracking_id", "scan_type", "ms_level", "parent_tracking_id", "precursor_id",
            "priority", "mono_mass", "charge", "precursor_mz", "isolation_width", "qscore", "charge_cos",
            "charge_snr", "iso_cos", "snr", "charge_score", "activation", "collision_energy", "hcd_energy",
            "reaction_time", "reagent_max_it", "reagent_agc_target", "ppm_error", "precursor_intensity",
            "peakgroup_intensity", "ion_type", "ion_index", "ms3_proteoform", "scan_description", "faims_cv",
            "faims_enabled", "enqueue_ts"
        };

        // (a) Name-based Normalize masks enqueue_ts and relabels ids at their NEW positions, NOT the old
        // fixed indices. Under a bug where masking stayed positional (old index 3), parent_tracking_id (now
        // index 3) would be masked and enqueue_ts (now index 31) would survive — both asserts would fail.
        [Test]
        public void NameBasedNormalize_MasksAndRelabelsByName_UnderNewColumnOrder()
        {
            var row = Enumerable.Repeat("px", NewCommandsHeader.Length).ToArray();
            row[0] = "abc";     // tracking_id
            row[3] = "abc";     // parent_tracking_id (same id -> same T<n>; must be RELABELED, never masked)
            row[28] = "abcS1";  // scan_description (3-char id prefix + marker)
            row[31] = "999";    // enqueue_ts (must be masked to <TS> BY NAME at its new last position)

            string dir = Path.Combine(Path.GetTempPath(), "logreorder_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, LogGoldenComparer.CommandsName),
                    string.Join("\t", NewCommandsHeader) + "\n" + string.Join("\t", row) + "\n");

                var ids = LogGoldenComparer.BuildIdMap(dir);
                string norm = LogGoldenComparer.Normalize(dir, LogGoldenComparer.CommandsName, ids);

                var lines = norm.Replace("\r\n", "\n").Split('\n');
                Assert.AreEqual(string.Join("\t", NewCommandsHeader), lines[0], "header emitted verbatim");

                var outCols = lines[1].Split('\t');
                int enq = Array.IndexOf(NewCommandsHeader, "enqueue_ts");
                int parent = Array.IndexOf(NewCommandsHeader, "parent_tracking_id");
                int track = Array.IndexOf(NewCommandsHeader, "tracking_id");
                int desc = Array.IndexOf(NewCommandsHeader, "scan_description");

                Assert.AreEqual("<TS>", outCols[enq], "enqueue_ts masked BY NAME at its new (last) position");
                Assert.AreEqual("T0", outCols[track], "tracking_id relabeled");
                Assert.AreEqual("T0", outCols[parent],
                    "parent_tracking_id relabeled, NOT masked (proves name-based, not the old positional index-3 mask)");
                Assert.AreEqual("T0S1", outCols[desc], "scan_description id-prefix relabeled by name");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        // (b) The compare-time permute aligns a NEW-order row to a golden(OLD) reference header by name,
        // preserving every value. CommandsName has no reorderable list columns, so this exercises the pure
        // column permute.
        [Test]
        public void CanonicalizePermute_AlignsToReferenceHeaderByName_ValuesPreserved()
        {
            string fresh = "b\ta\n2\t1\n";                        // header b,a ; row 2,1
            string outp = GoldenListCanonicalizer.Canonicalize(
                LogGoldenComparer.CommandsName, fresh, new[] { "a", "b" });   // reference order a,b
            Assert.AreEqual("a\tb\n1\t2\n", outp);
        }

        // (c) Fail closed: if the fresh header is not a permutation of the reference (a column renamed /
        // added / dropped), the permute throws rather than silently papering over a schema change.
        [Test]
        public void CanonicalizePermute_NonPermutationHeader_FailsClosed()
        {
            string fresh = "a\tc\n1\t3\n";                        // header a,c is NOT a permutation of a,b
            Assert.Throws<InvalidOperationException>(() =>
                GoldenListCanonicalizer.Canonicalize(LogGoldenComparer.CommandsName, fresh, new[] { "a", "b" }));
        }

        // (d) ida.log is free text (no columns): Canonicalize passes a non-AllMass body through unchanged
        // and ignores the reference header.
        [Test]
        public void CanonicalizeIdaLog_PassesNonAllMassThrough_IgnoringReference()
        {
            string text = "Scan# <SCAN>\nMass=100.0 charge=5\n";
            string outp = GoldenListCanonicalizer.Canonicalize(LogGoldenComparer.IdaLogName, text, null);
            Assert.AreEqual(text, outp);
        }
    }
}

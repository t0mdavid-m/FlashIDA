# -*- coding: utf-8 -*-
"""Cuts the Refine test tables (ADR-0049) out of their sources. The suite never runs this; it reads what it wrote.

  --july   FlashIDA/test-data/refine/july_ms3_pieces.tsv: the MS3 scans of the run of 2026-07-08 (the engine's logs in
           C:/FLASHIda/flashida-fresh, scan_results_path.tsv and scan_commands_path.tsv) of the parents b47, b48 and b51,
           selected as the prototype's refute.py selects them: every scan_results row with ms_level 3, its parent the
           scan_commands row of the same tracking id, report numbering = the engine's ion_index + 1. One row per logged
           deconvolved mass of every such scan (pooled over the parent's scans, duplicates kept): target_ion, mass and
           intensity exactly as logged (six significant digits).

Task 3 adds --corpus and --cytc to this file. Run from the parent repo root:

    uv run --with numpy python -I FlashIDA/test-scripts/refine/make_refine_tables.py --july
"""
import argparse
import csv
import io
import os

HERE = os.path.dirname(os.path.abspath(__file__))
TEST_DATA = os.path.normpath(os.path.join(HERE, "..", "..", "test-data", "refine"))
FRESH = "C:/FLASHIda/flashida-fresh"
JULY_PARENTS = (47, 48, 51)          # report numbering (Met = 1): b47, b48, b51


def july():
    commands = {r["tracking_id"]: r for r in csv.DictReader(
        io.open(os.path.join(FRESH, "scan_commands_path.tsv"), encoding="utf-8"), delimiter="\t")}
    rows, scans = [], dict((p, 0) for p in JULY_PARENTS)
    for r in csv.DictReader(io.open(os.path.join(FRESH, "scan_results_path.tsv"), encoding="utf-8"), delimiter="\t"):
        if r["ms_level"] != "3":
            continue
        cmd = commands[r["tracking_id"]]
        parent = int(cmd["ion_index"]) + 1
        if parent not in JULY_PARENTS:
            continue
        assert cmd["ion_type"] == "b", cmd["ion_type"]
        masses = [x for x in r["deconv_masses"].split(";") if x]
        intensities = [x for x in r["deconv_intensities"].split(";") if x]
        assert len(masses) == len(intensities), r["tracking_id"]
        scans[parent] += 1
        rows += [("b%d" % parent, m, i) for m, i in zip(masses, intensities)]
    assert all(n == 14 for n in scans.values()), scans      # 14 MS3 scans per parent, as refute.py and ms3_targets_v78.py
    rows.sort(key=lambda x: (int(x[0][1:]), float(x[1])))
    out = os.path.join(TEST_DATA, "july_ms3_pieces.tsv")
    with io.open(out, "w", encoding="utf-8", newline="\r\n") as fh:
        fh.write("# source: run of 2026-07-08, C:/FLASHIda/flashida-fresh/scan_results_path.tsv + scan_commands_path.tsv"
                 " (MS3 scans, selected as fi_refine_publication_notes/evidence/lenient_cut/refute.py)\n")
        fh.write("# parents (report numbering = engine ion_index + 1): %s; MS3 scans per parent: %s\n"
                 % (", ".join("b%d" % p for p in JULY_PARENTS), ", ".join("%d" % scans[p] for p in JULY_PARENTS)))
        fh.write("# one row per logged deconvolved mass, pooled over the parent's MS3 scans; masses and intensities as logged\n")
        fh.write("target_ion\tmass\tintensity\n")
        for row in rows:
            fh.write("\t".join(row) + "\n")
    print("wrote %s: %d rows (%s)" % (out, len(rows), ", ".join("b%d %d" % (p, sum(1 for r in rows if r[0] == "b%d" % p))
                                                               for p in JULY_PARENTS)))


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--july", action="store_true", help="write july_ms3_pieces.tsv")
    a = ap.parse_args()
    if not a.july:
        ap.error("name a table: --july")
    july()


if __name__ == "__main__":
    main()

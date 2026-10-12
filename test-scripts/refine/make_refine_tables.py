# -*- coding: utf-8 -*-
"""Cuts the Refine test tables (ADR-0049) out of their sources. The suite never runs this; it reads what it wrote.

  --july   FlashIDA/test-data/refine/july_ms3_pieces.tsv: the MS3 scans of the run of 2026-07-08 (the engine's logs in
           C:/FLASHIda/flashida-fresh, scan_results_path.tsv and scan_commands_path.tsv) of the parents b47, b48 and b51,
           selected as the prototype's refute.py selects them: every scan_results row with ms_level 3, its parent the
           scan_commands row of the same tracking id, report numbering = the engine's ion_index + 1. One row per logged
           deconvolved mass of every such scan (pooled over the parent's scans, duplicates kept): target_ion, mass and
           intensity exactly as logged (six significant digits).

  --corpus FlashIDA/test-data/refine/corpus/<key>.tsv (7) and refine_expected_corpus.tsv: seven of the fourteen
           characterized proteoforms of C:/FLASHIda/Measurements/MS2_Characterization/proteoforms.tsv (beta-casein run B,
           which is NOT_CHARACTERIZED in dataset_proteoform_map.tsv, has no row there), the KEEP list: ca, cytc, myoglobin,
           tia_red, ubiquitin, lactalbumin_red, betacasein. The other seven (KEYS minus KEEP) are not written: intact
           disulfides are outside the step-function model (ADR-0049 decision 11); lysozyme_red's corpus row carries four
           bridges and the oxidized mass. Per row: the run folder is the dataset cell
           up to " (", the scan is the scan_results.tsv row whose tracking_id is the first token of best_scan matching
           ![\\x21-\\x7e]{2}; one fixture row per logged deconvolved mass (mass as logged; intensity = the sum of the logged
           per-charge intensities; #charges = the number of logged charge states), masses ascending. The tolerance is the
           run's method.json deconvolution.tol[1] (ppm). Backbone = the ProForma without its bracket groups, the "-" after
           the N-terminal group and the ?/^n tokens; expected total = theoretical_mass_Da - backbone mass (refine_core's
           MONO and WATER). Expected shifts: [Acetyl]- +42.0106@1-1; heme +615.1695@c1-c2; each disulfide -2.0157@c2-c2
           (its second cysteine); [Phospho]^5? +399.8315@-; [Deamidated]? +0.9840@-. proto_* are the vendored prototype
           (refine_core.py) on the fixture's masses AT THE ENGINE'S DEFAULTS, through the same code path as --cytc
           (refine_at_defaults: alpha 0.2, both exclusions, cap 20, penalty 2), with the row's backbone in place of the
           report's sequence and the run's ppm in place of 10; proto_second_* is the second-best candidate, for the record
           (the test does not assert it). pass_total: |proto_total -
           expected_total| <= 2 tol_da; pass_sites: every sited expected shift has a jump of the best path within tol_da
           whose window holds [lo, hi], and every site-less one a jump within tol_da or pass_total (tol_da = ppm 1e-6 x the
           observed precursor mass).

  --cytc   FlashIDA/test-data/refine/refine_expected_cytc.tsv: the numbers Refine_test's cytochrome c sections read, from the
           prototype on cytc_grp1_prec1.tsv (10 ppm, Met 1 + the 104 residues, b/y) AT THE ENGINE'S DEFAULTS, i.e. ADR-0049 as
           amended on 2026-10-12: alpha 0.2 (K 300, seed 12345, grid 0.01 Da over +-700 Da, cut >= 3), the neutral-loss AND the
           neutron-satellite exclusion, the level cap 20 and the jump penalty 2. These are NOT the report's settings (alpha 0.5,
           penalty 1, no satellite rule, no cap), so the numbers differ from Versions 85/88. The amendments are new helpers here
           (is_neutron_satellite, cap_levels, solve_penalized) layered over the verbatim prototype functions; refine_core.py's
           vendored blocks are not edited. Written: Detect (the FDR up to the cut, every level with its exclusion), the five
           best totals (S = ions - 2 x jumps), the levels and windows of the best two, the discriminating masses of the best two
           (targets_v88.py's rule), and the recount against the second with the July MS3 confirmations b47, b48, b51.

Run from the parent repo root:

    uv run --with numpy python -I FlashIDA/test-scripts/refine/make_refine_tables.py --july
    uv run --with numpy python -I FlashIDA/test-scripts/refine/make_refine_tables.py --corpus
    uv run --with numpy python -I FlashIDA/test-scripts/refine/make_refine_tables.py --cytc
"""
import argparse
import contextlib
import csv
import importlib.util
import io
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TEST_DATA = os.path.normpath(os.path.join(HERE, "..", "..", "test-data", "refine"))
FRESH = "C:/FLASHIda/flashida-fresh"
JULY_PARENTS = (47, 48, 51)          # report numbering (Met = 1): b47, b48, b51
CORPUS = "C:/FLASHIda/Measurements/MS2_Characterization"
CYTC_FIXTURE = os.path.join(TEST_DATA, "cytc_grp1_prec1.tsv")
# the fourteen rows of proteoforms.tsv, in its order, and a piece of each dataset name that must match
KEYS = [("ca", "DirectInfusionCA_"), ("cytc", "DirectInfusionCytc_"), ("lysozyme", "DirectInfusionLysozym_90min_150nlmin_12ul_2026"),
        ("lysozyme_red", "DirectInfusionLysozym_90min_150nlmin_12ul_red_"), ("myoglobin", "DirectInfusionMyoglobin_"),
        ("rnase", "DirectInfusionRNAse_"), ("tia_red", "DirectInfusionTrypsinInhibitorRed_"),
        ("tia", "DirectInfusionTrypsinInhibitor_"), ("trypsinogen", "DirectInfusionTrypsinogen_"),
        ("ubiquitin", "DirectInfusionUbiquitin_"), ("lactalbumin_red", "DirectInfusionalphaLactalbuminRed_"),
        ("lactalbumin", "DirectInfusionalphaLactalbumin_"), ("betacasein", "DirectInfusionbetaCasein_"),
        ("betalac", "DirectInfusionbetaLac_")]
# layer A: the seven rows the step-function model covers; the other seven carry intact disulfides (ADR-0049 decision 11)
KEEP = ("ca", "cytc", "myoglobin", "tia_red", "ubiquitin", "lactalbumin_red", "betacasein")
TRACKING_ID = re.compile(r"![\x21-\x7e]{2}")


def write_crlf(path, lines):
    with io.open(path, "w", encoding="utf-8", newline="\r\n") as fh:
        for line in lines:
            fh.write(line + "\n")


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


# ------------------------------------------------------------------------------------------------------------ the prototype
def load_prototype():
    """refine_core.py, imported once on the report's scan (its own pins run and print to a buffer); its functions read their
    data from module globals, which refine_at_defaults() rebinds per scan."""
    argv = sys.argv
    sys.argv = ["refine_core.py", "--cytc", CYTC_FIXTURE]
    spec = importlib.util.spec_from_file_location("refine_core", os.path.join(HERE, "refine_core.py"))
    rc = importlib.util.module_from_spec(spec)
    sys.modules["refine_core"] = rc                  # refine_core binds tc = pc = lc = sys.modules[__name__]
    buf = io.StringIO()
    try:
        with contextlib.redirect_stdout(buf):
            spec.loader.exec_module(rc)
    finally:
        sys.argv = argv
    # column_dp_v85.py rebinds the name `cells` to a count of DP cells after it has built its matrices, shadowing
    # lenient_core.cells, which lenient_core.matrices calls; restore the function from its own vendored text
    src = io.open(os.path.join(HERE, "refine_core.py"), encoding="utf-8").read()
    a = src.index("def cells(lk, lam):")
    exec(compile(src[a:src.index("\n\n\n", a)], "refine_core.py (lenient_core.cells)", "exec"), rc.__dict__)
    assert callable(rc.cells)
    for lk in rc.totals:                                  # the greedy walk is tied_orders(...)[0] on every matrix of the report
        D, C, A = rc.mats[lk]
        Am = rc.amat(D, A, scale=1000)
        F, G, S, i0, ik = rc.fwd_bwd(lk, D, Am, cost=1001)
        assert first_tied_order(rc, D, Am, F, G, S, i0, ik, 1001) == rc.tied_orders(D, Am, F, G, S, i0, ik, cost=1001)[0], lk
    return rc, buf.getvalue()


def first_tied_order(rc, D, Am, F, G, S, i0, ik, cost):
    """tied_orders(...)[0] -- the smallest order of levels (a tuple of column indices) among the paths at the best score --
    without enumerating them: the same opt states and the same tight transitions as refine_core.tied_orders, walked greedily
    (the next column visited is the smallest any tied path can visit next). tied_orders builds the whole set, which on a
    disulfide-rich scan grows past memory (lysozyme: > 4.6 GB, unfinished after 10 min); the walk is checked against it on
    every matrix of the report's scan (load_prototype)."""
    NS, NEG, d = rc.NS, rc.NEG, len(D)
    opt = (F + G == S) & (F > NEG // 2) & (G > NEG // 2)

    def stay(n, lam):
        return opt[n + 1, lam] and F[n, lam] + Am[lam, n + 1] + G[n + 1, lam] == S

    def jump(n, lam, mu):
        return (mu != lam and opt[n + 1, mu] and lam != ik and mu != i0
                and F[n, lam] - cost + Am[mu, n + 1] + G[n + 1, mu] == S)

    alive = set([(NS, ik)])                              # states from which a tight path reaches (NS, ik)
    for n in range(NS - 1, -1, -1):
        for lam in range(d):
            if opt[n, lam] and (((n + 1, lam) in alive and stay(n, lam)) or
                                any((n + 1, mu) in alive and jump(n, lam, mu) for mu in range(d))):
                alive.add((n, lam))
    assert (0, i0) in alive
    order, frontier = [i0], set([(0, i0)])
    while True:
        lam = order[-1]
        seen, todo, nxt = set(frontier), list(frontier), {}
        while todo:                                      # the stays of the current level, and every jump out of them
            n, l = todo.pop()
            if n == NS:
                continue
            if (n + 1, l) in alive and stay(n, l) and (n + 1, l) not in seen:
                seen.add((n + 1, l))
                todo.append((n + 1, l))
            for mu in range(d):
                if (n + 1, mu) in alive and jump(n, l, mu):
                    nxt.setdefault(mu, set()).add((n + 1, mu))
        if lam == ik:                                    # no jump out of lambda_k: the order ends here
            assert (NS, ik) in seen and not nxt
            return tuple(order)
        mu = min(nxt)
        order.append(mu)
        frontier = nxt[mu]


# ------------------------------------------------------------------------------------------------------------ the ProForma
SHIFT_ACETYL, SHIFT_HEME, SHIFT_SS, SHIFT_PHOSPHO5, SHIFT_DEAMID = "+42.0106", "+615.1695", "-2.0157", "+399.8315", "+0.9840"


def parse_proforma(pf):
    """(backbone, [(shift text, lo, hi)]; lo = hi = None for a site-less shift)"""
    shifts, seq, xl = [], [], {}
    i = 0
    while i < len(pf):
        c = pf[i]
        if c == "[":
            j = pf.index("]", i)
            tag = pf[i + 1:j]
            i = j + 1
            if not seq:                                      # a group before the first residue
                if pf.startswith("^5?", i) and tag == "Phospho":
                    shifts.append((SHIFT_PHOSPHO5, None, None))
                    i += 3
                elif pf.startswith("?", i) and tag == "Deamidated":
                    shifts.append((SHIFT_DEAMID, None, None))
                    i += 1
                elif pf.startswith("-", i) and tag == "Acetyl":
                    shifts.append((SHIFT_ACETYL, 1, 1))
                    i += 1
                else:
                    raise ValueError("unknown leading group [%s] in %s" % (tag, pf[:60]))
                continue
            m = re.fullmatch(r"(Formula:C34H31N4O4Fe|MOD:00034)?#(XL\d+)", tag)
            if not m:
                raise ValueError("unknown group [%s]" % tag)
            assert seq[-1] == "C", tag
            xl.setdefault(m.group(2), []).append((len(seq), m.group(1)))
            continue
        assert c in "ACDEFGHIKLMNPQRSTVWY", c
        seq.append(c)
        i += 1
    for name in sorted(xl, key=lambda n: int(n[2:])):
        (a, ka), (b, kb) = sorted(xl[name])
        kind = ka or kb
        assert (ka is None) != (kb is None), xl[name]
        if kind.startswith("Formula:"):
            shifts.append((SHIFT_HEME, a, b))
        else:
            shifts.append((SHIFT_SS, b, b))
    return "".join(seq), shifts


def shift_text(s):
    txt, lo, hi = s
    return "%s@%s" % (txt, "-" if lo is None else "%d-%d" % (lo, hi))


# ------------------------------------------------------------------------------------------------------------ the corpus
def corpus():
    rc, _ = load_prototype()
    rows = list(csv.DictReader(io.open(os.path.join(CORPUS, "proteoforms.tsv"), encoding="utf-8"), delimiter="\t"))
    assert len(rows) == len(KEYS), len(rows)
    os.makedirs(os.path.join(TEST_DATA, "corpus"), exist_ok=True)
    table, verdicts = [], []
    for (key, probe), r in zip(KEYS, rows):
        folder = r["dataset"].split(" (")[0].strip()
        assert probe in folder, (key, folder)
        if key not in KEEP:
            continue
        run = os.path.join(CORPUS, "Full_Scans", folder)
        tid = TRACKING_ID.search(r["best_scan"]).group(0)
        scan = [s for s in csv.DictReader(io.open(os.path.join(run, "scan_results.tsv"), encoding="utf-8"), delimiter="\t")
                if s["tracking_id"] == tid]
        assert len(scan) == 1, (key, tid, len(scan))
        scan = scan[0]
        assert scan["ms_level"] == "2", (key, scan["ms_level"])
        ppm = float(json.load(io.open(os.path.join(run, "method.json"), encoding="utf-8"))["deconvolution"]["tol"][1])
        ms = [x for x in scan["deconv_masses"].split(";") if x]
        zs = [x for x in scan["deconv_charges"].split(";") if x]
        ins = [x for x in scan["deconv_intensities"].split(";") if x]
        assert len(ms) == len(zs) == len(ins), key
        fx = []
        for m, z, it in zip(ms, zs, ins):
            zl, il = z.split(","), it.split(",")
            assert len(zl) == len(il), (key, m)
            fx.append((float(m), m, sum(float(x) for x in il), len(zl)))
        fx.sort(key=lambda x: x[0])
        precursor = float(r["precursor_mass_observed_Da"])
        theo = float(r["theoretical_mass_Da"])
        activation = scan["activation_type"]
        assert activation in ("HCD", "CID"), activation          # b/y, the prototype's only ladder
        write_crlf(os.path.join(TEST_DATA, "corpus", key + ".tsv"), [
            "# source: C:/FLASHIda/Measurements/MS2_Characterization/Full_Scans/%s/scan_results.tsv" % folder,
            "# tracking_id %s, RT %s min, %s, CE %s; precursor %s Da observed (proteoforms.tsv), theoretical %s Da"
            % (tid, scan["rt"], activation, scan["collision_energy"], r["precursor_mass_observed_Da"], r["theoretical_mass_Da"]),
            "# proteoform: %s" % r["proforma"],
            "# one row per logged deconvolved mass: mass as logged; intensity = sum of the logged per-charge intensities;"
            " #charges = number of logged charge states; masses ascending",
            "mass\tintensity\t#charges"] + ["%s\t%.10g\t%d" % (s, i, n) for _, s, i, n in fx])
        backbone, shifts = parse_proforma(r["proforma"])
        backbone_mass = float(rc.residues(backbone).sum() + rc.WATER)
        expected_total = theo - backbone_mass
        d = refine_at_defaults(rc, backbone, [x[0] for x in fx], [x[2] for x in fx], ppm)
        best = d["sols"][0]
        second = d["sols"][1] if len(d["sols"]) > 1 else None
        p, w = best["p"], best["windows"]
        jumps = [(p[j] - p[j - 1], w[j - 1][0], w[j - 1][-1]) for j in range(1, len(p))]
        assert len(jumps) == best["J"], key
        ties = [t for t in d["sols"] if (t["S"], t["J"]) == (best["S"], best["J"])]
        tol_da = ppm * 1e-6 * precursor
        pass_total = abs(best["lk"] - expected_total) <= 2 * tol_da
        site_ok = []
        for txt, lo, hi in shifts:
            mass = float(txt)
            near = [j for j in jumps if abs(j[0] - mass) <= tol_da]
            if lo is None:
                site_ok.append(bool(near) or pass_total)      # a site-less shift counts through the total
            else:
                site_ok.append(any(j[1] <= lo and j[2] >= hi for j in near))
        pass_sites = all(site_ok)
        jump_text = ";".join("%+.3f@%d-%d" % j for j in jumps) or "-"
        table.append("\t".join([key, backbone, activation, "%g" % ppm, "%.4f" % expected_total, "%.4f" % precursor,
                                ";".join(shift_text(s) for s in shifts) or "-", "%.4f" % best["lk"], "%d" % best["S"],
                                jump_text, "%d" % pass_total, "%d" % pass_sites,
                                "%.4f" % second["lk"] if second else "-", "%d" % second["S"] if second else "-"]))
        verdicts.append((key, pass_total, pass_sites))
        print("%-16s %-4s %s %4g ppm %4d masses  cut %d levels %d (kept %d: %d nl, %d sat, %d capped; b %d, y %d) totals %d |"
              " expected %+.4f %s | proto %+.4f S %d shifts %d N %d jumps %s ties %d | second %s | total %d sites %d %s"
              % (key, tid, activation, ppm, len(fx), d["cut"], len(d["levels"]), len(d["kept"]),
                 sum(1 for l in d["levels"] if l["excluded"] == "neutral_loss"),
                 sum(1 for l in d["levels"] if l["excluded"] == "neutron_satellite"),
                 sum(1 for l in d["levels"] if l["excluded"] == "capped"), len(d["LB2"]), len(d["LY2"]), len(d["sols"]),
                 expected_total, ";".join(shift_text(s) for s in shifts) or "-", best["lk"], best["S"], best["J"], best["N"],
                 jump_text, len(ties), "%+.4f S %d shifts %d" % (second["lk"], second["S"], second["J"]) if second else "-",
                 pass_total, pass_sites, site_ok))
        sys.stdout.flush()
    assert [v[0] for v in verdicts] == [k for k, _ in KEYS if k in KEEP], verdicts
    out = os.path.join(TEST_DATA, "refine_expected_corpus.tsv")
    write_crlf(out, [
        "# generated by FlashIDA/test-scripts/refine/make_refine_tables.py --corpus from"
        " C:/FLASHIda/Measurements/MS2_Characterization/proteoforms.tsv and each run's scan_results.tsv + method.json",
        "# settings = the engine's defaults (ADR-0049 as amended 2026-10-12): fdr 0.2, shuffles 300 (seed 12345), grid 0.01 Da,"
        " +-700 Da, min_ions 3, the neutral-loss and neutron-satellite exclusions, max_levels 20, jump_penalty 2; the tolerance"
        " is the run's tolerance_ppm",
        "# not here: %s -- intact disulfides are outside the step-function model (ADR-0049 decision 11); lysozyme_red's corpus"
        " row carries four bridges and the oxidized mass" % ", ".join(k for k, _ in KEYS if k not in KEEP),
        "# expected_shifts: mass@lo-hi (the residues the jump's window must hold) or mass@- (no site); proto_*: refine_core.py"
        " on corpus/<key>.tsv at those settings (proto_shifts = the best path's jumps, delta@window); proto_second_*: the"
        " second-best candidate, for the record; pass_*: criterion a (tolerance in Da at precursor_mass_observed)",
        "key\tbackbone\tactivation\ttolerance_ppm\texpected_total\tprecursor_mass_observed\texpected_shifts\tproto_total\t"
        "proto_score\tproto_shifts\tpass_total\tpass_sites\tproto_second_total\tproto_second_score"] + table)
    print("wrote %s: %d rows; %d pass both" % (out, len(table), sum(1 for v in verdicts if v[1] and v[2])))
    return verdicts


# ------------------------------------------------------------------------------------------------------------ the amendments
# ADR-0049 decisions 3, 4, 9, 11 as amended on 2026-10-12 (the corpus finding), as helpers over the prototype's functions.
NEUTRON = 1.003355                   # Da, one 13C - 12C spacing
DEFAULTS = dict(fdr=0.2, shuffles=300, grid_da=0.01, max_shift_da=700.0, min_ions=3, max_levels=20, jump_penalty=2)
SLACK = 0.005                        # Da, the region test's slack on either side (column_dp_v85.py's neutral-loss rule)


def references(level, levels):
    """{0} u {the shift of every level with a strictly higher signal} -- excluded or not"""
    return [0.0] + [k["dm"] for k in levels if k["signal"] > level["signal"]]


def is_neutral_loss(rc, level, levels):
    """column_dp_v85.py's rule: the region holds r - v for a reference r and v in {water, ammonia}"""
    return any(level["lo"] - SLACK <= x - v <= level["hi"] + SLACK for x in references(level, levels) for _, v in rc.NLOSS)


def is_neutron_satellite(level, levels):
    """the amendment: the region holds r + s n 1.003355 for a reference r, n in {1, 2, 3}, s in {+1, -1}"""
    return any(level["lo"] - SLACK <= x + s * n * NEUTRON <= level["hi"] + SLACK
               for x in references(level, levels) for n in (1, 2, 3) for s in (1, -1))


def matched_intensity(rc, scan, intensity, dm):
    """the cap's tie-break: summed intensity of the observed masses the level's prefix and suffix ions match at dm (the
    neighbour rule of Scan.matches), each mass counted once even when two ions match it"""
    np = rc.np
    hit = set()
    for theo in (scan.b, scan.y):
        x = theo + dm
        i = np.searchsorted(scan.M, x)
        for j in (i - 1, i):
            for k in range(len(x)):
                if 0 <= j[k] < len(scan.M) and abs(scan.M[j[k]] - x[k]) <= scan.tol[j[k]]:
                    hit.add(int(j[k]))
    return float(sum(intensity[k] for k in hit))


def cap_levels(levels, max_levels):
    """the amendment: of the levels not excluded keep the max_levels with the highest signal, ties by the matched intensity
    (desc), then the smaller |shift|; the rest are marked 'capped' and stay in the list"""
    kept = [l for l in levels if l["excluded"] == "none"]
    if len(kept) > max_levels:
        for l in sorted(kept, key=lambda l: (-l["signal"], -l["tie"], abs(l["dm"])))[max_levels:]:
            l["excluded"] = "capped"


def exclude(rc, scan, intensity, levels, max_levels):
    """neutral loss first (a level that is both is a neutral loss), then the neutron satellite, then the cap"""
    for l in levels:
        l["excluded"] = ("neutral_loss" if is_neutral_loss(rc, l, levels) else
                         "neutron_satellite" if is_neutron_satellite(l, levels) else "none")
        l["tie"] = matched_intensity(rc, scan, intensity, l["dm"])
    cap_levels(levels, max_levels)
    return [l for l in levels if l["excluded"] == "none"]


def solve_penalized(rc, lk, D, A, jump_penalty):
    """column_dp_v85.py's column DP with a jump costing 1000 x penalty + 1 on the scaled matrix (an ion = 1000): the quotient
    is ions - penalty x jumps, the remainder the jump count (cost 1001 is penalty 1). The order is tied_orders(...)[0], walked
    (first_tied_order) and checked against the enumeration."""
    cost = 1000 * jump_penalty + 1
    Am = rc.amat(D, A, scale=1000)
    F, G, Sx, i0, ik = rc.fwd_bwd(lk, D, Am, cost=cost)
    first = first_tied_order(rc, D, Am, F, G, Sx, i0, ik, cost)
    assert first == rc.tied_orders(D, Am, F, G, Sx, i0, ik, cost=cost)[0], lk
    return int(-((-Sx) // 1000)), int((-Sx) % 1000), tuple(D[i] for i in first)


def refine_at_defaults(rc, seq, masses, intensities, ppm, s=DEFAULTS):
    """the one code path --cytc and --corpus share: Detect (detect_fdr.py's module code at alpha s["fdr"]), the exclusions and
    the cap (exclude), then Assemble (column_dp_v85.py's column DP at s["jump_penalty"], solve_penalized) on one mass list,
    with seq in place of the report's sequence and ppm in place of 10. Binds lenient_core's globals for the scan (M, tol,
    res, INTENSITY, L, NS, t, m_seq; totals and mats for rc.path). Candidates ranked as the prototype's rows.sort (S desc,
    jumps asc, stable over construction order), each with its jumps' windows. rc.path (ion labels, which asserts that no
    explained ion depends on the jumps' placement) is left to the caller: --cytc runs it on every candidate, --corpus needs
    only the windows."""
    np = rc.np
    order = sorted(range(len(masses)), key=lambda i: masses[i])
    M, INT = np.array([masses[i] for i in order]), np.array([intensities[i] for i in order])
    tol, res = rc.tolerance(M, ppm=ppm), rc.residues(seq)
    rc.M, rc.tol, rc.res, rc.INTENSITY = M, tol, res, INT
    rc.L = len(res)
    rc.NS = rc.L - 1
    rc.t = np.cumsum(res)[:-1]
    rc.m_seq = float(res.sum() + rc.WATER)
    rc._segs.clear()
    scan = rc.Scan(res, rc.WATER, M, tol)
    # Detect
    grid = rc.Grid(-s["max_shift_da"], s["max_shift_da"], s["grid_da"])
    T = rc.ladder_from_residues(res)
    ex = rc.explained(T, M, tol)
    S = rc.curve(T, M, tol, grid) - rc.curve(T, M[ex], tol[ex], grid)
    U, tolU = M[~ex], tol[~ex]
    K, HEIGHTS = s["shuffles"], rc.HEIGHTS
    tot = dict((h, 0) for h in HEIGHTS)
    for r in rc.shuffles(seq, K, 12345):
        n = rc.regions(rc.curve(rc.ladder_from_residues(r), U, tolU, grid), HEIGHTS)
        for h in HEIGHTS:
            tot[h] += n[h]
    nS = rc.regions(S, HEIGHTS)
    fdr = dict((h, tot[h] / K / nS[h]) for h in HEIGHTS if nS[h])
    cut = next((h for h in HEIGHTS if h >= s["min_ions"] and nS[h] > 0 and fdr[h] <= s["fdr"]), 0)   # 0: no cut passes
    levels = []
    if cut:
        for dm, top, lo, hi in rc.region_list(S, cut, grid):
            levels.append(dict(dm=round(dm, 3), signal=int(top), b=int(scan.matches(scan.b, dm).sum()),
                               y=int(scan.matches(scan.y, dm).sum()), lo=round(lo, 3), hi=round(hi, 3)))
    kept = exclude(rc, scan, INT, levels, s["max_levels"])
    LB2 = sorted(l["dm"] for l in kept if l["b"] > 0)
    LY2 = sorted(l["dm"] for l in kept if l["y"] > 0)
    # Assemble
    totals, mats = rc.matrices(LB2, LY2)
    rc.totals, rc.mats = totals, mats
    pen = s["jump_penalty"]
    sols = []
    for lk in totals:
        D, C, A = mats[lk]
        Sp, J, p = solve_penalized(rc, lk, D, A, pen)
        sols.append(dict(key=lk, lk=float(lk), S=Sp, J=J, N=Sp + pen * J, p=p))
    sols.sort(key=lambda x: (-x["S"], x["J"]))                    # the prototype's rows.sort, stable over construction order
    for t in sols:                                                # each jump's window (column_dp_v85.py's path_dp + windows)
        D, C, A = mats[t["key"]]
        Aj = [np.concatenate([[0], A[x]]) for x in t["p"]]
        Fp, Gp, N = rc.path_dp(Aj)
        assert N == t["N"], t["lk"]
        t["windows"] = rc.windows(Aj, Fp, Gp, N)
    return dict(M=M, INT=INT, ex=ex, fdr=fdr, cut=cut, levels=levels, kept=kept, LB2=LB2, LY2=LY2, sols=sols)


# ------------------------------------------------------------------------------------------------------------ cytochrome c
def cytc():
    rc, _ = load_prototype()
    s = DEFAULTS
    # the report's scan, read again (later vendored blocks rebind lenient_core's names, e.g. t): masses ascending, their
    # intensities, 10 ppm, the 105-residue sequence; refine_at_defaults binds lenient_core's globals for it
    fx = sorted((float(r[0]), float(r[1])) for r in (l.rstrip("\r\n").split("\t") for l in io.open(CYTC_FIXTURE, encoding="utf-8")
                                                     if not l.startswith("#")) if r[0] != "mass")
    assert len(fx) == 150 and len(rc.residues(rc.CYTC)) == 105
    d = refine_at_defaults(rc, rc.CYTC, [m for m, _ in fx], [i for _, i in fx], 10.0, s)
    cut, fdr, levels, kept, LB2, LY2, ex, sols = (d[k] for k in ("cut", "fdr", "levels", "kept", "LB2", "LY2", "ex", "sols"))
    assert cut, "no cut passes"
    for t in sols:                                                # rc.path reads the totals and mats refine_at_defaults bound
        t["info"] = rc.path(t["key"], t["p"])
        assert t["info"]["N"] == t["N"] and [list(x) for x in t["info"]["windows"]] == [list(x) for x in t["windows"]], t["lk"]
    rows = [("cut", "%d" % cut)]
    rows += [("fdr_h%d" % h, "%.4f" % fdr[h]) for h in range(s["min_ions"], cut + 1) if h in fdr]
    rows += [("levels", "%d" % len(levels)), ("levels_kept", "%d" % len(kept)),
             ("excluded_neutral_loss", "%d" % sum(1 for l in levels if l["excluded"] == "neutral_loss")),
             ("excluded_neutron_satellite", "%d" % sum(1 for l in levels if l["excluded"] == "neutron_satellite")),
             ("excluded_capped", "%d" % sum(1 for l in levels if l["excluded"] == "capped")),
             ("lambda_b", "%d" % len(LB2)), ("lambda_y", "%d" % len(LY2)), ("explained_unshifted", "%d" % int(ex.sum()))]
    for i, l in enumerate(levels, 1):
        rows.append(("level%d" % i, "%.2f %d %d %d %s" % (l["dm"], l["signal"], l["b"], l["y"], l["excluded"])))
    rows.append(("totals", "%d" % len(sols)))
    for i, t in enumerate(sols[:5], 1):
        rows.append(("best%d" % i, "total %.2f S %d shifts %d N %d" % (t["lk"], t["S"], t["J"], t["N"])))
    rows.append(("ties_with_best3", "%d" % sum(1 for t in sols if (t["S"], t["J"]) == (sols[2]["S"], sols[2]["J"]))))
    for i, t in enumerate(sols[:2], 1):
        w = t["info"]["windows"]
        rows.append(("best%d_levels" % i, " ".join("%.2f" % x for x in t["p"])))
        rows.append(("best%d_windows" % i, " ".join("%d-%d" % (x[0], x[-1]) for x in w) or "-"))
        labels = [lab for v in t["info"]["ions"].values() for lab, _ in v]
        rows.append(("best%d_ions_b_y" % i, "%d %d" % (sum(1 for x in labels if x[0] == "b"), sum(1 for x in labels if x[0] == "y"))))
    # targets_v88.py's loop for the best two
    A, B = sols[0]["info"], sols[1]["info"]
    tg = []
    for m in sorted(set(A["ions"]) | set(B["ions"])):
        ia, ib = sorted(A["ions"].get(m, [])), sorted(B["ions"].get(m, []))
        if [x for x in ia if x in ib]:
            continue                                          # shared, not a target

        def one(own, other):
            if len(own) < 2:
                return own
            k = [x for x in own if x[0][0] in set(y[0][0] for y in other)]
            if len(k) == 1:
                return k
            return [x for x in own if x[0][0] == "b"][:1] or own[:1]   # else the prefix ion (Version 88)
        sa, sb = one(ia, ib), one(ib, ia)
        tg.append(dict(mass=float(rc.M[m]), intensity=float(rc.INTENSITY[m]), a=sa and sa[0][0], b=sb and sb[0][0]))
    tg.sort(key=lambda t: -t["intensity"])
    rows.append(("targets", "%d" % len(tg)))
    rows.append(("targets_only_a", "%d" % sum(1 for t in tg if t["a"] and not t["b"])))
    rows.append(("targets_only_b", "%d" % sum(1 for t in tg if t["b"] and not t["a"])))
    rows.append(("targets_both", "%d" % sum(1 for t in tg if t["a"] and t["b"])))
    for i, t in enumerate(tg, 1):
        rows.append(("target%d" % i, "%.2f %s %s" % (t["mass"], t["a"] or "-", t["b"] or "-")))
    # the recount (ms3_recount_v79.py's rule, the penalty of the amendment) against the second: masses, not ions (Version 88:
    # one ion per mass and path); S after MS3 = shared + confirmed - penalty x jumps
    masses_a, masses_b = len(A["ions"]), len(B["ions"])
    only_a = sum(1 for t in tg if t["a"])
    only_b = sum(1 for t in tg if t["b"])
    shared = masses_a - only_a
    assert shared == masses_b - only_b, (masses_a, only_a, masses_b, only_b)
    confirmed = sum(1 for t in tg if t["a"] in ("b47", "b48", "b51"))
    ja, jb = len(A["levels"]) - 1, len(B["levels"]) - 1
    pen = s["jump_penalty"]
    rows += [("recount_shared", "%d" % shared), ("recount_confirmed_a", "%d" % confirmed),
             ("recount_a", "%d" % (shared + confirmed - pen * ja)), ("recount_b", "%d" % (shared - pen * jb))]
    out = os.path.join(TEST_DATA, "refine_expected_cytc.tsv")
    write_crlf(out, ["# generated by FlashIDA/test-scripts/refine/make_refine_tables.py --cytc: refine_core.py on cytc_grp1_prec1.tsv"
                     " (10 ppm, Met 1 + the 104 residues, b/y); Refine_test's cytochrome c sections read these values",
                     "# settings = the engine's defaults (ADR-0049 as amended 2026-10-12): fdr 0.2, shuffles 300 (seed 12345), grid 0.01 Da,"
                     " +-700 Da, min_ions 3, the neutral-loss and neutron-satellite exclusions, max_levels 20, jump_penalty 2 --"
                     " NOT the report's (alpha 0.5, penalty 1, no satellite rule), so the numbers differ from Versions 85/88",
                     "# level<i>: shift signal n_prefix n_suffix exclusion (none|neutral_loss|neutron_satellite|capped), ascending shift;"
                     " best<i>: S = ions - 2 x jumps, ranked by S then jumps (construction order on a tie; the engine breaks it by"
                     " intensity_sum); target<i>: mass, best's ion, second's ion, most intense first",
                     "# recount: S after MS3 = shared masses + confirmed targets - 2 x jumps, best against second, with the July MS3"
                     " confirmations b47, b48, b51",
                     "quantity\tvalue"] + ["%s\t%s" % x for x in rows])
    for k, v in rows:
        print("%-20s %s" % (k, v))
    print("wrote %s: %d rows" % (out, len(rows)))


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--july", action="store_true", help="write july_ms3_pieces.tsv")
    ap.add_argument("--corpus", action="store_true", help="write corpus/<key>.tsv and refine_expected_corpus.tsv")
    ap.add_argument("--cytc", action="store_true", help="write refine_expected_cytc.tsv")
    a = ap.parse_args()
    if not (a.july or a.corpus or a.cytc):
        ap.error("name a table: --july, --corpus or --cytc")
    if a.july:
        july()
    if a.corpus:
        corpus()
    if a.cytc:
        cytc()


if __name__ == "__main__":
    main()

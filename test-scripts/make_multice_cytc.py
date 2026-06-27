#!/usr/bin/env python3
"""Generate 5 collision-energy-specific cytochrome-C MS2 fixtures from one base spectrum.

PURPOSE
-------
Produce an *energy-resolved* family of cytC MS2 spectra (CE 20/25/30/35/40) by
re-scaling the intensities of a single measured cytC MS2 spectrum so that
DIFFERENT fragment ions dominate at DIFFERENT collision energies. The peak m/z
values and the peak ordering/count are kept identical to the base spectrum across
all five fixtures -- only intensities differ per CE. These fixtures later drive a
per-fragment CE-optimization test (separate task).

PHYSICAL MODEL
--------------
Higher collision energy -> more fragmentation -> small fragments dominate; large
(less-fragmented) fragments dominate at low CE. We encode this with two steps:

1. Optimal CE per fragment (mass -> CE, monotonically DECREASING):
   We map each deconvolved fragment's MonoisotopicMass linearly across the
   observed scan-57 mass range [m_min, m_max] onto the CE sweep grid
   {20, 25, 30, 35, 40}, with the LARGEST mass -> 20 (low CE) and the SMALLEST
   mass -> 40 (high CE), then snap to the nearest grid CE:

       frac    = (mass - m_min) / (m_max - m_min)        # 0 (small) .. 1 (large)
       ce_cont = CE_MAX - frac * (CE_MAX - CE_MIN)        # large -> CE_MIN, small -> CE_MAX
       optCE   = nearest grid value in {20,25,30,35,40} to ce_cont

2. Energy-resolved intensity factor (triangular bell over the 5-point CE grid):
   For a fixture at collision energy CE, the raw peaks INSIDE fragment f's m/z
   envelope [RepresentativeMzStart, RepresentativeMzEnd] are scaled by

       factor(CE, optCE) = max(FLOOR, 1 - |CE - optCE| / SPREAD)

   which PEAKS (=1.0) at f's optimal CE and declines linearly away from it.
   SPREAD = 20 (full grid width) so even the farthest CE keeps a small response;
   FLOOR = 0.05 guarantees a strictly positive, non-zero tail.

   A raw peak that falls inside SEVERAL fragment envelopes takes the MAX factor
   over those fragments (the dominant assignment). Peaks not inside ANY fragment
   envelope are treated as noise/unassigned and left UNCHANGED across all CE
   levels (constant), so the CE response is carried purely by assigned fragments.

INPUTS
------
  base   : FlashIDA/test-data/spectra/ms2_cytc_fresh_scan57.txt
           TSV. Line 1 = header "Spec scan=57\\t<RT>". Following lines =
           "<m/z>\\t<intensity>" (m/z 6 decimals, intensity 2 decimals).
  frags  : C:/FLASHIda/TestData/20250121_CytC_MS2HCD_MS3HCDCID_Mode2_MS2CE40_MS3CID27_spec3.tsv
           Deconvolved fragments. Columns used (1-based): ScanNum=3,
           MonoisotopicMass=8, RepresentativeMzStart=29, RepresentativeMzEnd=30.
           Filtered to ScanNum == 57.

OUTPUTS
-------
  FlashIDA/test-data/spectra/ms2_cytc_ce20.txt .. ms2_cytc_ce40.txt
  Same header, same m/z values, same peak count/order as the base; only the
  intensity column is rescaled per CE (kept at 2 decimals).

Run (this environment): python/py are MS Store stubs; use uv:
  uv run python FlashIDA/test-scripts/make_multice_cytc.py
(numpy not required.)
"""

import os

# --- CE sweep grid ---------------------------------------------------------
CE_GRID = [20, 25, 30, 35, 40]
CE_MIN = CE_GRID[0]
CE_MAX = CE_GRID[-1]
SPREAD = float(CE_MAX - CE_MIN)   # 20.0; triangular bell half-width-ish
FLOOR = 0.05                      # minimum factor (non-zero tail)

# --- 1-based column indices in spec3.tsv -----------------------------------
COL_SCANNUM = 3
COL_MONOMASS = 8
COL_MZSTART = 29
COL_MZEND = 30
TARGET_SCAN = 57

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BASE = os.path.join(REPO, "FlashIDA", "test-data", "spectra", "ms2_cytc_fresh_scan57.txt")
FRAGS = r"C:\FLASHIda\TestData\20250121_CytC_MS2HCD_MS3HCDCID_Mode2_MS2CE40_MS3CID27_spec3.tsv"
OUTDIR = os.path.join(REPO, "FlashIDA", "test-data", "spectra")


def load_base(path):
    """Return (header_line, [(mz_str, mz_float, intensity_float), ...])."""
    with open(path, "r") as fh:
        lines = fh.read().splitlines()
    header = lines[0]
    peaks = []
    for ln in lines[1:]:
        if not ln.strip():
            continue
        mz_str, int_str = ln.split("\t")
        peaks.append((mz_str, float(mz_str), float(int_str)))
    return header, peaks


def load_fragments(path):
    """Return list of (mono_mass, mz_start, mz_end) for ScanNum == TARGET_SCAN."""
    out = []
    with open(path, "r") as fh:
        for i, ln in enumerate(fh):
            if i == 0:
                continue
            cols = ln.rstrip("\n").split("\t")
            if len(cols) <= COL_MZEND - 1:
                continue
            try:
                scan = int(float(cols[COL_SCANNUM - 1]))
            except ValueError:
                continue
            if scan != TARGET_SCAN:
                continue
            mono = float(cols[COL_MONOMASS - 1])
            lo = float(cols[COL_MZSTART - 1])
            hi = float(cols[COL_MZEND - 1])
            if hi < lo:
                lo, hi = hi, lo
            out.append((mono, lo, hi))
    return out


def optimal_ce(mass, m_min, m_max):
    """Map mass -> nearest grid CE; large mass -> CE_MIN, small mass -> CE_MAX."""
    if m_max > m_min:
        frac = (mass - m_min) / (m_max - m_min)
    else:
        frac = 0.5
    ce_cont = CE_MAX - frac * (CE_MAX - CE_MIN)
    return min(CE_GRID, key=lambda g: abs(g - ce_cont))


def factor(ce, opt_ce):
    """Triangular bell over the CE grid, peaking at opt_ce, floored at FLOOR."""
    return max(FLOOR, 1.0 - abs(ce - opt_ce) / SPREAD)


def main():
    header, peaks = load_base(BASE)
    frags = load_fragments(FRAGS)
    if not frags:
        raise SystemExit("NEEDS_CONTEXT: no scan-57 fragments parsed from spec3.tsv")

    masses = [m for (m, _, _) in frags]
    m_min, m_max = min(masses), max(masses)

    # Per-fragment optimal CE.
    frag_opt = [(m, lo, hi, optimal_ce(m, m_min, m_max)) for (m, lo, hi) in frags]

    # For each peak, find the MAX factor over enveloping fragments (per CE).
    # Precompute, for each peak, the set of optimal-CEs of fragments whose
    # envelope contains it (a peak unassigned -> factor 1.0 for every CE).
    n_peaks = len(peaks)
    peak_opt_ces = [[] for _ in range(n_peaks)]
    for (m, lo, hi, opt) in frag_opt:
        for idx, (_, mz, _) in enumerate(peaks):
            if lo <= mz <= hi:
                peak_opt_ces[idx].append(opt)

    # Write one fixture per CE.
    written = {}
    for ce in CE_GRID:
        out_path = os.path.join(OUTDIR, "ms2_cytc_ce%d.txt" % ce)
        out_lines = [header]
        for idx, (mz_str, mz, base_int) in enumerate(peaks):
            opts = peak_opt_ces[idx]
            if opts:
                fac = max(factor(ce, o) for o in opts)
            else:
                fac = 1.0  # unassigned noise -> constant
            new_int = base_int * fac
            out_lines.append("%s\t%.2f" % (mz_str, new_int))
        with open(out_path, "w", newline="\n") as fh:
            fh.write("\n".join(out_lines) + "\n")
        written[ce] = (out_path, len(out_lines) - 1)

    # ---- Verification: per-fragment summed-envelope intensity vs CE ----
    # Build per-CE intensity arrays in memory for the envelope sums.
    ce_intensity = {}
    for ce in CE_GRID:
        arr = []
        for idx, (_, mz, base_int) in enumerate(peaks):
            opts = peak_opt_ces[idx]
            fac = max((factor(ce, o) for o in opts), default=1.0) if opts else 1.0
            arr.append(base_int * fac)
        ce_intensity[ce] = arr

    # Precompute peak indices per fragment envelope.
    frag_peak_idx = []
    for (m, lo, hi, opt) in frag_opt:
        idxs = [i for i, (_, mz, _) in enumerate(peaks) if lo <= mz <= hi]
        frag_peak_idx.append((m, opt, idxs))

    # Pick representatives spanning the mass range: sort by mass, sample ~8.
    frag_peak_idx_sorted = sorted(frag_peak_idx, key=lambda t: t[0])
    sample_count = 8
    step = max(1, len(frag_peak_idx_sorted) // sample_count)
    samples = frag_peak_idx_sorted[::step][:sample_count]

    print("=== Per-fragment best-CE verification (representatives) ===")
    print("mass(Da)   optCE  | summed envelope intensity per CE"
          "                              | argmax  match")
    header_ces = "  ".join("ce%d" % c for c in CE_GRID)
    print("                  | %s" % header_ces)
    ok_all = True
    for (m, opt, idxs) in samples:
        sums = {}
        for ce in CE_GRID:
            arr = ce_intensity[ce]
            sums[ce] = sum(arr[i] for i in idxs)
        argmax_ce = max(CE_GRID, key=lambda c: sums[c])
        match = (argmax_ce == opt)
        ok_all = ok_all and match
        sums_str = " ".join("%10.0f" % sums[c] for c in CE_GRID)
        print("%9.2f  %4d   | %s | ce%-4d %s"
              % (m, opt, sums_str, argmax_ce, "OK" if match else "MISMATCH"))

    print("")
    print("Fragments parsed (scan 57): %d ; mass range %.3f .. %.3f Da"
          % (len(frags), m_min, m_max))
    print("CE grid: %s ; SPREAD=%g FLOOR=%g" % (CE_GRID, SPREAD, FLOOR))
    print("")
    print("=== Written fixtures (peaks = base peak count) ===")
    for ce in CE_GRID:
        p, n = written[ce]
        print("  %s  peaks=%d" % (os.path.basename(p), n))
    print("")
    print("All sampled representatives argmax==optCE: %s" % ("YES" if ok_all else "NO"))


if __name__ == "__main__":
    main()

# -*- coding: utf-8 -*-
"""refine_core.py -- the owner's Refine prototype consolidated into one file (ADR-0049), the development oracle of Refine_test.

Every section below is a source file of C:/FLASHIda/fi_refine_publication_notes/evidence copied verbatim under a banner naming
its path, date and sha256 (VENDORED.md lists them). The only edits are the glue lines marked "VENDOR glue": the modules the
sources import from one another are this one module, the mass list is the --cytc argument (its '#' provenance lines skipped),
detect_fdr.json is kept in memory instead of written, and no output file is written. Run:

    uv run --with numpy python -I FlashIDA/test-scripts/refine/refine_core.py --cytc FlashIDA/test-data/refine/cytc_grp1_prec1.tsv

It prints Detect (cut, levels), the five best lambda_k of Version 85, the 21 targets of Version 88 and the recount of Version 79.
The suite never imports this file.
"""
import sys
import argparse
_ap = argparse.ArgumentParser(description="the Refine prototype (Detect, Version 85, Version 88, Version 79) on one mass list")
_ap.add_argument("--cytc", required=True, help="mass list: mass<TAB>intensity<TAB>#charges, '#' lines and one header line")
CYTC_TSV = _ap.parse_args().cytc
tc = pc = lc = sys.modules[__name__]  # VENDOR glue (refine_core): the modules trunc_core, paths_core_f3, lenient_core are this one


# ==============================================================================================================================
# VENDORED: evidence/truncation_scan/trunc_core.py  (dated 2026-09-21, sha256 a16d1ed9d2f5bb369d64d0d2f1c1369d2536c54b8ffe092bb0e3791b90af72f9)
# whole file, verbatim
# ==============================================================================================================================
# -*- coding: utf-8 -*-
"""The shift scan of the part "Detect mass shifts", written out on the lab machine for the truncation experiment of 2026-09-21.

Rules (from `evidence/shift_scan_project.md`, the counting rules relayed 2026-09-20, and the rows of the report):
  T          theoretical b and y ions of the supplied sequence, neutral and monoisotopic; b = sum of the residues,
             y = sum plus water; b1..b(L-1) and y1..y(L-1).
  match      |(t + dm) - m| <= tol(m); tol(m) = ppm * m, or a fixed width in Da.
  Observed   O(dm) = number of DISTINCT t in T with at least one match in M.
  Expected   E(dm) = the same count over M0 = the observed masses that the unshifted ladder explains (M n T).
  Signal     S = O - E.
  Shuffle    D_k(dm) = the same count for the k-th shuffled sequence over U = M without M0 (the matched null).
  Peaks      n(f, h) = number of separate regions of f with at least h ions: maximal runs of consecutive grid points
             with f >= h, no merging of neighbouring runs.
  FDR        FDR(h) = mean_k n(D_k, h) / n(S, h); cut c = the smallest h with FDR(h) <= alpha.
A curve is evaluated ON the grid points: a grid point dm counts a fragment when the match rule holds at that dm.
Read-only on everything but its own folder. No format strings in file names; LF only."""
import sys
sys.dont_write_bytecode = True
import numpy as np

MONO = {"G": 57.021464, "A": 71.037114, "S": 87.032028, "P": 97.052764, "V": 99.068414, "T": 101.047679, "C": 103.009185,
        "L": 113.084064, "I": 113.084064, "N": 114.042927, "D": 115.026943, "Q": 128.058578, "K": 128.094963, "E": 129.042593,
        "M": 131.040485, "H": 137.058912, "F": 147.068414, "R": 156.101111, "Y": 163.063329, "W": 186.079313}
AMINO = "ACDEFGHIKLMNPQRSTVWY"
WATER = 18.010565
HORSE = "GDVEKGKKIFVQKCAQCHTVEKGGKHKTGPNLHGLFGRKTGQAPGFTYTDANKNKGITWKEETLMEYLENPKKYIPGTKMIFAGIKKKTEREDLIAYLKKATNE"
CYTC = "M" + HORSE                      # the supplied sequence of the report: Met 1 plus the 104 residues
assert len(CYTC) == 105


def residues(seq):
    return np.array([MONO[a] for a in seq], dtype=float)


def ladder_from_residues(res):
    """b1..b(L-1) and y1..y(L-1), neutral monoisotopic."""
    b = np.cumsum(res)[:-1]
    y = np.cumsum(res[::-1])[:-1] + WATER
    return np.concatenate([b, y])


def ladder(seq):
    return ladder_from_residues(residues(seq))


class Grid(object):
    def __init__(self, lo, hi, step):
        self.lo, self.hi, self.step = float(lo), float(hi), float(step)
        self.n = int(round((self.hi - self.lo) / self.step)) + 1
        self.x = self.lo + self.step * np.arange(self.n)


def tolerance(masses, ppm=None, da=None):
    assert (ppm is None) != (da is None)
    return masses * (ppm * 1e-6) if ppm is not None else np.full(len(masses), float(da))


def curve(T, M, tol, grid):
    """Number of distinct t with a match, at every grid point. M must be sorted ascending, tol non-decreasing in M."""
    if len(M) == 0 or len(T) == 0:
        return np.zeros(grid.n, dtype=np.int32)
    a = (M[None, :] - tol[None, :] - T[:, None] - grid.lo) / grid.step      # first dm that matches, in grid units
    z = (M[None, :] + tol[None, :] - T[:, None] - grid.lo) / grid.step      # last dm that matches
    s = np.ceil(a - 1e-9).astype(np.int64)
    e = np.floor(z + 1e-9).astype(np.int64)
    s = np.clip(s, 0, grid.n)               # grid.n = past the end
    e = np.clip(e, -1, grid.n - 1)
    # one fragment counts once: along a row the ends ascend, so an interval starts behind the end of the one before it
    s[:, 1:] = np.maximum(s[:, 1:], e[:, :-1] + 1)
    ok = s <= e
    diff = np.bincount(s[ok], minlength=grid.n + 1).astype(np.int64)
    diff -= np.bincount(e[ok] + 1, minlength=grid.n + 1)
    return np.cumsum(diff[:grid.n]).astype(np.int32)


def explained(T, M, tol):
    """For every observed mass: does an unshifted theoretical ion match it?"""
    Ts = np.sort(T)
    i = np.searchsorted(Ts, M)
    out = np.zeros(len(M), dtype=bool)
    for j in (i - 1, i):
        ok = (j >= 0) & (j < len(Ts))
        jj = np.clip(j, 0, len(Ts) - 1)
        out |= ok & (np.abs(M - Ts[jj]) <= tol)
    return out


def regions(f, heights):
    """n(f, h) for every h in heights: maximal runs of consecutive grid points with f >= h."""
    hmin = min(heights)
    idx = np.flatnonzero(f >= hmin)
    val = f[idx]
    out = {}
    for h in heights:
        sub = idx[val >= h]
        out[h] = 0 if len(sub) == 0 else 1 + int(np.count_nonzero(np.diff(sub) > 1))
    return out


def region_list(f, h, grid):
    """The regions of f at or above h: (dm at the highest point, the height, dm of the first and the last grid point)."""
    idx = np.flatnonzero(f >= h)
    if len(idx) == 0:
        return []
    cuts = np.flatnonzero(np.diff(idx) > 1)
    starts = np.concatenate([[0], cuts + 1])
    ends = np.concatenate([cuts, [len(idx) - 1]])
    out = []
    for s0, e0 in zip(starts, ends):
        seg = idx[s0:e0 + 1]
        top = int(f[seg].max())
        best = seg[f[seg] == top]
        mid = best[len(best) // 2]
        out.append((float(grid.x[mid]), top, float(grid.x[seg[0]]), float(grid.x[seg[-1]])))
    return out


def shuffles(seq, K, seed):
    """K permutations of the residues of seq (composition, length and fragment count kept; only the ladder goes)."""
    rng = np.random.default_rng(seed)
    res = residues(seq)
    for _ in range(K):
        yield rng.permutation(res)


# ==============================================================================================================================
# VENDORED: evidence/assemble_all_paths_f3/paths_core_f3.py  (dated 2026-09-21, sha256 91423d4228b519dcb5463e9880fb185aa6867bf4b6c06311e847af932cb1f496)
# lines up to class Scan (what detect_fdr.py uses), verbatim
# ==============================================================================================================================
# -*- coding: utf-8 -*-
"""Core of the enumeration of ALL paths of allowed levels (forked chat [758e18], 2026-09-21). See all_paths_f3.py for the rules.
A path = prefix levels v_0 = 0, ..., v_k = D; cleavage site n (1..L-1) sits on v_j with j = number of step residues <= n; on the
level v the b ion of the site is explained at the trial shift v and the y ion of the site at D - v. Read-only, no file output."""
import sys
sys.dont_write_bytecode = True
import itertools
import numpy as np

NEG = -10 ** 9


class Scan(object):
    def __init__(self, res, water, M, tol):
        self.L = len(res)
        self.NS = self.L - 1
        self.b = np.cumsum(res)[:-1]
        self.y = (np.cumsum(res[::-1])[:-1] + water)[::-1]      # the y ion of cleavage site n at index n-1
        self.M, self.tol = M, tol

    def matches(self, theo, v):
        x = theo + v
        i = np.searchsorted(self.M, x)
        out = np.zeros(len(theo), dtype=bool)
        for j in (i - 1, i):
            ok = (j >= 0) & (j < len(self.M))
            jj = np.clip(j, 0, len(self.M) - 1)
            out |= ok & (np.abs(self.M[jj] - x) <= self.tol[jj])
        return out

    def gains(self, V, D):
        return dict((v, self.matches(self.b, v).astype(np.int64) + self.matches(self.y, D - v).astype(np.int64)) for v in V)




# ==============================================================================================================================
# VENDORED: evidence/lenient_cut/detect_fdr.py  (dated 2026-09-29, sha256 41ed9b8f3ba21c010d60d00be14aa3daa50ddd53d0bd2f24d6999415323b8925)
# whole file, verbatim but for the glue lines (input path, K, outputs)
# ==============================================================================================================================
# -*- coding: utf-8 -*-
"""Detect on the report's cytochrome c scan at a lenient FDR: FDR(h) for every cut h, the cut at 10, 20 and 50 %, and the levels
that pass with their b and y ions and their regions. Made for the owner's request of 2026-09-28 (chat round CUT-50-1):
"Assumption: We record a MS2 scan and detect levels Then run Level finding & DP to identify potential proteoforms (What happens if
we use 50% FDR?) Which fragment ions should we then select to confirm if these levels are real? If we find out a level is not real
we would be able to discard proteoform identification with that level."

Rules: those of ../truncation_scan/trunc_core.py, as ../assemble_all_paths_f3/check_delta_internal_f3.py runs them, on the true
sequence (Met 1 plus the 104 residues): the 150 masses of grp1_prec1.tsv, 10 ppm, the grid -700 to +700 Da in steps of 0.01 Da,
Signal S = Observed - Expected, K shuffled sequences (seed 12345; K = 300 unless given), FDR(h) = mean peaks of the shuffles with at
least h ions over the peaks of S with at least h ions, cut = the smallest h >= 3 with FDR(h) <= alpha. A level is a region of S at
or above the cut: its dm (the middle of its highest grid points), its height, its first and last grid point (lo, hi); its b and y
ions are the b and y ions of the supplied sequence that match at dm.
Read-only on everything but its own folder. Writes detect_fdr.json (read by lenient_core.py) and detect_fdr.txt next to this file.
LF only.
Run:  C:/FLASHIda/Measurements/qc/.venv/Scripts/python.exe -B detect_fdr.py [K]
"""
import sys
sys.dont_write_bytecode = True
import io, os, json
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
EV = os.path.dirname(HERE)
#  # VENDOR glue (refine_core): trunc_core and paths_core_f3 are this module (see the top)

K = 300  # VENDOR glue (refine_core): the report's K (argv carries --cytc)
HEIGHTS = list(range(1, 61))
OUT = []


def say(line):
    OUT.append(line)
    print(line)


rows = [l.rstrip("\r\n").split("\t") for l in io.open(CYTC_TSV, encoding="utf-8") if not l.startswith("#")][1:]  # VENDOR glue (refine_core): the --cytc mass list
M = np.array(sorted(float(r[0]) for r in rows))
tol = tc.tolerance(M, ppm=10.0)
res = tc.residues(tc.CYTC)
scan = pc.Scan(res, tc.WATER, M, tol)
grid = tc.Grid(-700.0, 700.0, 0.01)
T = tc.ladder_from_residues(res)
ex = tc.explained(T, M, tol)
S = tc.curve(T, M, tol, grid) - tc.curve(T, M[ex], tol[ex], grid)
U, tolU = M[~ex], tol[~ex]
tot = dict((h, 0) for h in HEIGHTS)
for r in tc.shuffles(tc.CYTC, K, 12345):
    n = tc.regions(tc.curve(tc.ladder_from_residues(r), U, tolU, grid), HEIGHTS)
    for h in HEIGHTS:
        tot[h] += n[h]
nS = tc.regions(S, HEIGHTS)
say("masses %d, explained unshifted %d, shuffles %d" % (len(M), int(ex.sum()), K))
say("h: peaks of the Signal, mean peaks per shuffle, FDR")
for h in range(1, 13):
    say("  %2d: %4d  %7.2f  %s" % (h, nS[h], tot[h] / K, ("%.3f" % (tot[h] / K / nS[h])) if nS[h] else "-"))
out = {}
for a in (0.10, 0.20, 0.50):
    cut = next(h for h in HEIGHTS if h >= 3 and nS[h] > 0 and tot[h] / K / nS[h] <= a)
    lv = []
    for dm, top, lo, hi in tc.region_list(S, cut, grid):
        nb, ny = int(scan.matches(scan.b, dm).sum()), int(scan.matches(scan.y, dm).sum())
        lv.append(dict(dm=round(dm, 3), signal=top, b=nb, y=ny, lo=round(lo, 3), hi=round(hi, 3)))
    out[str(a)] = dict(cut=cut, levels=lv)
    say("alpha %.2f: cut %d, %d levels (%d with b ions, %d with y ions):" % (a, cut, len(lv), sum(l["b"] > 0 for l in lv),
                                                                            sum(l["y"] > 0 for l in lv)))
    for l in lv:
        say("   %+8.2f  S %2d  b %2d  y %2d  region %+9.2f to %+9.2f" % (l["dm"], l["signal"], l["b"], l["y"], l["lo"], l["hi"]))
DETECT_FDR = json.loads(json.dumps(out))  # VENDOR glue (refine_core): detect_fdr.json kept in memory (json round trip as written); no file written


# ==============================================================================================================================
# VENDORED: evidence/lenient_cut/lenient_core.py  (dated 2026-09-29, sha256 62961d94f95148d4c150484b0cb86a1febb9ad48e28674666c7a4969ae31d0d1)
# whole file, verbatim but for the glue lines
# ==============================================================================================================================
# -*- coding: utf-8 -*-
"""Core of the lenient-cut study (chat rounds CUT-50-1 of 2026-09-28 and REFUTATION-1 of 2026-09-29): the report's cytochrome c
scan, its levels at a stated FDR (from detect_fdr.json, written by detect_fdr.py), and the paths by the rules of the report since
Version 73. The code is that of the chat scripts of 2026-09-28 (the session's scratchpad, folder fdr50/: paths_fdr.py with the fix
of the same day, int(b) + int(y) per cleavage site, since numpy booleans add as OR), moved here so that every script of this folder
uses one copy.

Rules (the report since Version 73, as example_current_rules_f3.py, variant "strict"):
  - one DP matrix per lambda_k = lambda_b + lambda_y, lambda_b in Lambda_b u {0}, lambda_y in Lambda_y u {0} (two sums within SAME
    are one matrix);
  - the diagonals D = {0, lambda_k} u Lambda_b u (lambda_k - Lambda_y); a path runs from 0 to lambda_k and visits each diagonal at
    most once; at most KMAX mass shifts per path (KMAX - 1 diagonals between 0 and lambda_k); in the matrix lambda_k = 0 only the
    path without a mass shift;
  - a(n, lambda) = [b ion of site n at t_n + lambda observed] + [y ion of site n at m_seq - t_n + lambda_k - lambda observed],
    10 ppm of the observed mass;
  - F(n, i) = max{F(n - 1, i), F(n - 1, i - 1)} + a(n, lambda_i); every new diagonal matches at least one ion; N = F(L - 1, k).
N0 is the same DP without the rule "matches at least one ion" (an upper bound of N, vectorised over the paths); the rule is applied
to the paths in descending N0 until N0 falls more than WINDOW below the best N.
Levels: Lambda_b = the levels of Detect with at least one b ion, Lambda_y = those with at least one y ion (MIN = 1, the report).
Read-only on everything but its own folder. LF only. No output of its own.
"""
import sys
sys.dont_write_bytecode = True
import io, os, json, itertools, time
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
EV = os.path.dirname(HERE)
#  # VENDOR glue (refine_core): trunc_core is this module

rows = [l.rstrip("\r\n").split("\t") for l in io.open(CYTC_TSV, encoding="utf-8") if not l.startswith("#")][1:]  # VENDOR glue (refine_core): the --cytc mass list
M = np.array(sorted(float(r[0]) for r in rows))
INTENSITY = np.array([float(r[1]) for r in sorted(rows, key=lambda r: float(r[0]))])
CHARGES = [r[2] for r in sorted(rows, key=lambda r: float(r[0]))]
tol = tc.tolerance(M, ppm=10.0)
res = tc.residues(tc.CYTC)
L = len(res)
NS = L - 1
t = np.cumsum(res)[:-1]
m_seq = float(res.sum() + tc.WATER)
SAME = 0.004
NEG = -10 ** 9
REPORTED = (0.0, -89.03, 526.14)                  # the reported path, in the matrix lambda_k = +526.14 Da


def same(x, y):
    return abs(x - y) <= SAME


def isin(x, S):
    return any(same(x, s) for s in S)


def detect_levels(alpha):
    """cut, Lambda_b, Lambda_y and the level records of detect_fdr.json at one FDR ("0.1", "0.2" or "0.5")"""
    J = DETECT_FDR[alpha]  # VENDOR glue (refine_core): detect_fdr.json from memory
    return J["cut"], [l["dm"] for l in J["levels"] if l["b"] > 0], [l["dm"] for l in J["levels"] if l["y"] > 0], J["levels"]


def idx_of(x):
    """the index of the observed mass within 10 ppm of x, or -1 (the higher of two neighbours when both match)"""
    i = np.searchsorted(M, x)
    best = -1
    for j in (i - 1, i):
        if 0 <= j < len(M) and abs(M[j] - x) <= tol[j]:
            best = j
    return best


def cells(lk, lam):
    bi = [idx_of(t[n] + lam) for n in range(NS)]
    yi = [idx_of(m_seq - t[n] + lk - lam) for n in range(NS)]
    return bi, yi


def dp(path, A):
    """N of the path with the rule 'every new diagonal matches at least one ion' (strict), and one best placement of the jumps
    (the cleavage sites at which the path enters diagonals 1..k)."""
    k = len(path) - 1
    a = [A[l] for l in path]
    F = [[[NEG, NEG] for _ in range(k + 1)] for _ in range(NS + 1)]
    P = [[[None, None] for _ in range(k + 1)] for _ in range(NS + 1)]
    F[1][0][1] = a[0][0]
    if k >= 1:
        s = 1 if a[1][0] > 0 else 0
        F[1][1][s] = a[1][0]
        P[1][1][s] = ("jump", 0, 1)
    for n in range(2, NS + 1):
        for i in range(k + 1):
            for sp in (0, 1):
                v = F[n - 1][i][sp]
                if v > NEG // 2:
                    s = 1 if (sp == 1 or a[i][n - 1] > 0) else 0
                    if v + a[i][n - 1] > F[n][i][s]:
                        F[n][i][s] = v + a[i][n - 1]
                        P[n][i][s] = ("stay", i, sp)
            if i >= 1:
                v = F[n - 1][i - 1][1]
                if v > NEG // 2:
                    s = 1 if a[i][n - 1] > 0 else 0
                    if v + a[i][n - 1] > F[n][i][s]:
                        F[n][i][s] = v + a[i][n - 1]
                        P[n][i][s] = ("jump", i - 1, 1)
    N = F[NS][k][1]
    if N <= NEG // 2:
        return None, None
    jumps = []
    n, i, s = NS, k, 1
    while n >= 1:
        p = P[n][i][s]
        if p is None:
            break
        if p[0] == "jump":
            jumps.append(n)
        n, i, s = n - 1, p[1], p[2]
    return int(N), sorted(jumps)


def n0_batch(paths, A):
    """N without the rule, vectorised over paths of one length."""
    g = np.array([[A[l] for l in p] for p in paths])          # (P, k+1, NS)
    k = g.shape[1] - 1
    f = np.full((len(paths), k + 1), NEG, dtype=np.int64)
    f[:, 0] = g[:, 0, 0]
    if k >= 1:
        f[:, 1] = g[:, 1, 0]
    for n in range(1, NS):
        sh = np.concatenate([np.full((len(paths), 1), NEG), f[:, :-1]], axis=1)
        f = np.maximum(f, sh)
        f = np.where(f > NEG // 2, f + g[:, :, n], NEG)
    return f[:, k]                                         # the report's strict rule: the path ends on lambda_k


def matrices(LB, LY, lk_filter=None):
    totals = []
    for u in [0.0] + LB:
        for w in [0.0] + LY:
            if not isin(u + w, totals):
                totals.append(u + w)
    if lk_filter is not None:
        totals = [x for x in totals if lk_filter(x)]
    mats = {}
    for lk in totals:
        D = [0.0]
        for x in [lk] + LB + [lk - y for y in LY]:
            if not isin(x, D):
                D.append(x)
        C = dict((d, cells(lk, d)) for d in D)
        A = dict((d, np.array([int(bi >= 0) + int(yi >= 0) for bi, yi in zip(*C[d])], dtype=np.int64)) for d in D)
        mats[lk] = (D, C, A)
    return totals, mats


def candidates(totals, mats, kmax):
    """every path with at most kmax mass shifts, with its N0, in descending N0; and the number of paths"""
    cand = []
    npaths = 0
    for lk in totals:
        D, C, A = mats[lk]
        end = [d for d in D if same(d, lk)][0]
        mids = [d for d in D if not same(d, 0.0) and not same(d, lk)]
        if same(lk, 0.0):
            cand.append((int(A[0.0].sum()), lk, (0.0,)))
            npaths += 1
            continue
        for r in range(0, kmax):
            ps = [(0.0,) + p + (end,) for p in itertools.permutations(mids, r)]
            npaths += len(ps)
            for i0 in range(0, len(ps), 20000):
                chunk = ps[i0:i0 + 20000]
                for N0, p in zip(n0_batch(chunk, A), chunk):
                    cand.append((int(N0), lk, p))
    cand.sort(key=lambda c: -c[0])
    return cand, npaths


def strict_best(cand, mats, window):
    """the paths with the rule applied, in descending N0 until N0 < best N - window; sorted by N"""
    best = []
    nbest = None
    for N0, lk, p in cand:
        if nbest is not None and N0 < nbest - window:
            break
        D, C, A = mats[lk]
        N, jumps = dp(p, A)
        if N is None:
            continue
        if nbest is None or N > nbest:
            nbest = N
        best.append((N, lk, p, jumps))
    best.sort(key=lambda b: -b[0])
    return best


def run(alpha, kmax, lk_filter=None, window=8, LB=None, LY=None):
    t0 = time.time()
    cut, lb, ly, levels = detect_levels(alpha)
    LB = lb if LB is None else LB
    LY = ly if LY is None else LY
    totals, mats = matrices(LB, LY, lk_filter)
    cand, npaths = candidates(totals, mats, kmax)
    best = strict_best(cand, mats, window)
    return dict(alpha=alpha, kmax=kmax, cut=cut, LB=LB, LY=LY, levels=levels, totals=totals, mats=mats, cand=cand, npaths=npaths,
                best=best, secs=time.time() - t0)


def fmt(p):
    return ", ".join(("%+.2f" % v if not same(v, 0.0) else "0") for v in p)


def used(mats, lk, p, jumps):
    """the ions a placement explains: (ion, diagonal, index of the observed mass)"""
    D, C, A = mats[lk]
    out = []
    for n in range(1, NS + 1):
        i = sum(1 for r in jumps if r <= n)
        bi, yi = C[p[i]]
        if bi[n - 1] >= 0:
            out.append(("b%d" % n, p[i], bi[n - 1]))
        if yi[n - 1] >= 0:
            out.append(("y%d" % (L - n), p[i], yi[n - 1]))
    return out


def is_reported(lk, p):
    return abs(lk - 526.14) < 0.02 and len(p) == 3 and all(same(x, y) or abs(x - y) < 0.02 for x, y in zip(p, REPORTED))


def rank_of_reported(best):
    """(N of the reported path, 1 + the paths with a larger N, the other paths with the same N), or None"""
    rep = [b for b in best if is_reported(b[1], b[2])]
    if not rep:
        return None
    n = rep[0][0]
    return n, 1 + sum(1 for b in best if b[0] > n), sum(1 for b in best if b[0] == n) - 1


# ==============================================================================================================================
# VENDORED: evidence/lenient_cut/ms2_rules.py  (dated 2026-09-29, sha256 cd5415a77e2eeb1c8271a8fcfd755162739d45ecb1f0b0f43fac12771f33df44)
# up to its main loop (what column_dp_v85.py executes), verbatim but for the glue line
# ==============================================================================================================================
# -*- coding: utf-8 -*-
"""The two MS2 rules proposed in chat round CUT-50-1 (2026-09-28), applied before any MS3, and the paths again at 10, 20 and 50 % FDR
(at most 3 mass shifts per path). Filed on 2026-09-29, when the question of that round counted as option 1 ("Rerun the example at
50 % with the two MS2 rules and the July MS3 pieces, and file it as an evidence note"). The owner's request of 2026-09-28:
"Assumption: We record a MS2 scan and detect levels Then run Level finding & DP to identify potential proteoforms (What happens if
we use 50% FDR?) Which fragment ions should we then select to confirm if these levels are real? If we find out a level is not real
we would be able to discard proteoform identification with that level."

Rule 1, one observed mass explains at most one ion of a path. For one placement of the jumps of a path (sites 1 <= r_1 < ... <
r_k <= L - 1; level 0 on sites 1..r_1 - 1, level i on r_i..r_(i+1) - 1, level k on r_k..L - 1), every ion of a site on its
diagonal points to one observed mass (lenient_core.idx_of). The path explains N1 = the number of DISTINCT observed masses. The rule
"every new diagonal matches at least one ion" becomes: every new diagonal is credited with an ion of its own, each mass credited
once. That is Hall's condition over the new diagonals: for every set S of them, their segments hold at least |S| distinct masses.
N1 of a path = the largest N1 over the placements that meet it, found exactly by enumerating every placement (k <= 3, at most
182,104 placements, masses as bit masks). N1 <= N <= N0, so the paths are asked in descending N0, down to N0 = N1 of the reported
path. Every path with N1 at or above the reported path's is then found.
Rule 2, fold a satellite level. A level is dropped from Lambda_b and Lambda_y when a stronger level, or 0, plus or minus one offset
lies within its tolerance. A stronger level is a kept level of Detect with a larger Signal; 0 counts as stronger than every level
(it carries the unshifted ions). The offsets are water 18.010565, ammonia 17.026549 and one neutron 1.003355 Da, and the 19
distinct residue masses of trunc_core.MONO (L and I one).
  Levels are taken in descending Signal. A level is compared with 0 and with the stronger levels kept so far, never with a folded
  one.
  The tolerance of a level: its region in Detect at the cut of that FDR (the grid points with at least c ions, detect_fdr.json lo to
  hi), widened by half a grid step, 0.005 Da. For comparison two fixed tolerances are run as well: 0.004 Da (SAME, with which the
  scripts tell two levels or diagonals apart) and 0.03 Da (the table of satellites.py).
Outputs per FDR: rule 1 alone, rule 2 alone, both. Each gives the levels left, the matrices, the paths, the best N and its paths,
and the rank of the reported path (1 + the paths with a larger N; the paths with the same N apart).
Needs detect_fdr.json (run detect_fdr.py first).
Read-only on everything but its own folder. Writes ms2_rules.txt next to this file. LF only.
Run:  C:/FLASHIda/Measurements/qc/.venv/Scripts/python.exe -B ms2_rules.py
"""
import sys
sys.dont_write_bytecode = True
import io, os, itertools, time
import numpy as np
#  # VENDOR glue (refine_core): lenient_core and trunc_core are this module

HERE = os.path.dirname(os.path.abspath(__file__))
NS = lc.NS
W = (len(lc.M) + 63) // 64
OUT = []


def say(line):
    OUT.append(line)
    print(line)


# ------------------------------------------------------------------------------------------------ rule 2: fold satellite levels
RESIDUES = sorted(set(round(v, 6) for v in tc.MONO.values()))
assert len(RESIDUES) == 19
OFFSETS = [("water", 18.010565), ("ammonia", 17.026549), ("one neutron", 1.003355)] + \
          [("residue " + "/".join(sorted(a for a, v in tc.MONO.items() if round(v, 6) == r)), r) for r in RESIDUES]


def fold(levels, mode):
    """(kept levels, [(folded level, reference, offset)]) with mode 'region' or a fixed tolerance in Da"""
    order = sorted(levels, key=lambda l: -l["signal"])
    kept, folded = [], []
    for l in order:
        refs = [("0", 0.0)] + [("%+.2f" % k["dm"], k["dm"]) for k in kept if k["signal"] > l["signal"]]
        hit = None
        for rname, ref in refs:
            for oname, ov in OFFSETS:
                for sign in (1, -1):
                    x = ref + sign * ov
                    if mode == "region":
                        ok = l["lo"] - 0.005 <= x <= l["hi"] + 0.005
                    else:
                        ok = abs(x - l["dm"]) <= mode
                    if ok and hit is None:
                        hit = (rname, ("+ " if sign > 0 else "- ") + oname)
        if hit:
            folded.append((l, hit[0], hit[1]))
        else:
            kept.append(l)
    kept.sort(key=lambda l: l["dm"])
    return kept, folded


# ------------------------------------------------------------------------------------------------ rule 1: one mass, one ion
COMBOS = dict((k, np.array(list(itertools.combinations(range(1, NS + 1), k)), dtype=np.int64)) for k in (1, 2, 3))
_segs = {}


def seg_table(lk, d, C):
    """T[a, b] = the masses (bit mask) of the ions of sites a..b on diagonal d of matrix lk; 0 for b < a"""
    key = (round(lk, 4), round(d, 4))
    if key not in _segs:
        if len(_segs) > 400:
            _segs.clear()
        rows = np.zeros((NS + 1, W), dtype=np.uint64)
        bi, yi = C[d]
        for n in range(1, NS + 1):
            for j in (bi[n - 1], yi[n - 1]):
                if j >= 0:
                    rows[n, j >> 6] |= np.uint64(1) << np.uint64(j & 63)
        T = np.zeros((NS + 2, NS + 1, W), dtype=np.uint64)
        for a in range(1, NS + 1):
            T[a, a:NS + 1] = np.bitwise_or.accumulate(rows[a:NS + 1], axis=0)
        _segs[key] = T
    return _segs[key]


def count(m):
    return np.bitwise_count(m).sum(axis=-1).astype(np.int64)


def rule1(lk, p, mats):
    """(N1, jumps of one best placement) or (None, None)"""
    D, C, A = mats[lk]
    k = len(p) - 1
    T = [seg_table(lk, d, C) for d in p]
    if k == 0:
        return int(count(T[0][1, NS])), []
    R = COMBOS[k]
    ends = np.concatenate([R[:, 1:] - 1, np.full((len(R), 1), NS)], axis=1)       # last site of levels 1..k
    masks = [T[0][1, R[:, 0] - 1]] + [T[i][R[:, i - 1], ends[:, i - 1]] for i in range(1, k + 1)]
    union = masks[0].copy()
    for m in masks[1:]:
        union |= m
    n1 = count(union)
    ok = np.ones(len(R), dtype=bool)
    for size in range(1, k + 1):
        for S in itertools.combinations(range(1, k + 1), size):
            u = masks[S[0]].copy()
            for i in S[1:]:
                u |= masks[i]
            ok &= count(u) >= size
    if not ok.any():
        return None, None
    n1 = np.where(ok, n1, -1)
    b = int(np.argmax(n1))
    return int(n1[b]), [int(x) for x in R[b]]


def rank_rule1(R):
    """paths with N1 at or above that of the reported path, and the reported path's N1, rank and ties"""
    mats, cand = R["mats"], R["cand"]
    rep = [c for c in cand if lc.is_reported(c[1], c[2])]
    n_rep = rule1(rep[0][1], rep[0][2], mats)[0] if rep else None
    stop = n_rep if n_rep is not None else cand[0][0] - 8
    out, asked = [], 0
    for N0, lk, p in cand:
        if N0 < stop:
            break
        asked += 1
        n1, jumps = rule1(lk, p, mats)
        if n1 is not None and n1 >= stop:
            out.append((n1, lk, p, jumps))
    out.sort(key=lambda x: -x[0])
    rk = None
    if n_rep is not None:
        rk = (n_rep, 1 + sum(1 for o in out if o[0] > n_rep), sum(1 for o in out if o[0] == n_rep) - 1)
    return out, rk, asked


def show(label, R, best, rk, asked=None):
    say("  %s: levels %d (b %d, y %d), matrices %d, paths %d%s" % (label, len(set(R["LB"] + R["LY"])), len(R["LB"]), len(R["LY"]),
                                                                  len(R["totals"]), R["npaths"],
                                                                  (", asked exactly %d (N0 at or above the reported path's N1)" % asked)
                                                                  if asked is not None else ""))
    if best:
        top = best[0][0]
        for N, lk, p, jumps in [b for b in best if b[0] == top][:8]:
            say("     best N %d: lambda_k %+.2f, levels %s, jumps at %s" % (N, lk, lc.fmt(p), jumps))
        more = sum(1 for b in best if b[0] == top) - 8
        if more > 0:
            say("     and %d more paths with N %d" % (more, top))
    say("     the reported path: " + ("N %d, rank %d, %d other path(s) with the same N" % rk if rk else "not found"))
    if rk and rk[2] > 0:
        for N, lk, p, jumps in [b for b in best if b[0] == rk[0] and not lc.is_reported(b[1], b[2])]:
            say("     same N: lambda_k %+.2f, levels %s, jumps at %s" % (lk, lc.fmt(p), jumps))




# ==============================================================================================================================
# VENDORED: evidence/lenient_cut/column_dp_v85.py  (dated 2026-09-30, sha256 8c4034b06d589aabb4b7866ad6ab58dddf462707d144f3ce0d059140121056b2)
# whole file, verbatim but for the glue lines (the exec of ms2_rules.py, the outputs)
# ==============================================================================================================================
# -*- coding: utf-8 -*-
"""Version 85 of the report (the owner, 2026-09-30, session flashida-3a: "Exclude neutral losses from lambda b and lambda y",
"1)", "proton loss" then, on the dangers, "1" (no proton loss), "stronger -> higher intensity.", "update artifact" and, on
the interrupted build, "then specify higher S(deltaM)"; grilling log DRAFT-NLOSS-1 and BUILD-V85): column_dp_v82.py (no fold)
without the neutral losses: a level whose region holds 0, or a level of higher S(Delta m), minus water or ammonia is not a
level of Lambda_b or Lambda_y. Excluded: -18.02, -17.02 (0 minus water, ammonia), +508.12 (+526.14 minus water), +637.18
(+655.19 minus water). Ranked by the summed intensity of their matched masses instead, the same four would go. Writes column_dp_v85.txt and column_dp_v85.json next to this file.
Run:  C:/FLASHIda/Measurements/qc/.venv/Scripts/python.exe -B column_dp_v85.py
. The numbers of the part Assemble in Version 76 of the report: the column DP (grilling log DRAFT-DP-COL-1 to -17 and
DRAFT-EX-COL-1, 2026-09-29; the owner: "We go back to the faster and simplified programming algorithm draft with me a new
dotpoint after Spectral Alignment [citation] and subdotpoints", "We apply a penalty of 1 per jump. each matched fragment ion gains
1", "We compute one path per lambda k and then try to distinguish the different lambda k", then "1)" and "update artifact").

At 50 % FDR with the folded levels of Version 75 (ms2_rules.py, fold "region"), one DP per lambda_k:
  F(0, 0) = 0, F(0, lambda) = -infinity for lambda != 0;
  F(n, lambda) = max{F(n - 1, lambda), max over lambda' of F(n - 1, lambda') - 1} + a(n, lambda),
  lambda' any level but lambda and lambda_k (no jump out of lambda_k), no jump into 0, no limit on the jumps;
  S = F(L - 1, lambda_k), N = S + jumps; on a tie the path with fewer mass shifts (a' = 1000 a, a jump 1001).
A forward and a backward DP give every path at the best score, so the ties are counted as well, with the penalty of 1 alone
and with the rule on ties. For the best path of the five best lambda_k: the windows of the jumps (every placement with the
same N), N and N_1. N_1 is exact over every admissible placement (Hall's condition of ms2_rules.rule1): only a placement with
N >= N_1 can reach N_1, so the placements with N >= T are enumerated, T lowered until the best N_1 found reaches T; for up to
3 mass shifts it is checked against rule1. With the sites the MS3 pieces of the run of 2026-07-08 fix (refute.py), the best
path is computed again. Cells: (L) |D| per matrix, at a time F of the largest matrix plus its a(n, lambda), as Version 75.
The definitions of ms2_rules.py are executed up to its main loop, so its output file is not written again.
Read-only on everything but its own folder. Writes column_dp_v82.txt and column_dp_v82.json (the matrix lambda_k = +526.13 Da
for the figure of the report, figures/cytc_assemble_col_v76_f3.py) next to this file. LF only.
Run:  C:/FLASHIda/Measurements/qc/.venv/Scripts/python.exe -B column_dp_v76.py
"""
import sys
sys.dont_write_bytecode = True
import io, os, json, itertools
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
OUTD = HERE
sys.path.insert(0, HERE)
#  # VENDOR glue (refine_core): the definitions of ms2_rules.py are the section above
NEG = -10 ** 9
FORCED = {14: -89.03, 19: 526.14, 20: 526.14, 21: 526.14}       # the sites the MS3 pieces fix (refute.py, example_v75.py)
OUT = []


def say(line):
    OUT.append(line)
    print(line)


cut, LB, LY, levels = lc.detect_levels("0.5")
# Version 85: no fold, but the neutral losses go (the owner: "Exclude neutral losses from lambda b and lambda y", "1)",
# "then specify higher S(deltaM)"): a level whose region holds 0 or a level of higher S(Delta m) minus water or ammonia
NLOSS = (("water", 18.010565), ("ammonia", 17.026549))
kept, folded = [], []
for l in levels:
    refs = [("0", 0.0)] + [("%+.2f" % k["dm"], k["dm"]) for k in levels if k["signal"] > l["signal"]]
    hit = [(r, n) for r, x in refs for n, v in NLOSS if l["lo"] - 0.005 <= x - v <= l["hi"] + 0.005]
    (folded.append((l, hit[0])) if hit else kept.append(l))
for l, (r, n) in folded:
    print("neutral loss: %+.2f = %s - %s" % (l["dm"], r, n))
assert sorted(round(l["dm"], 2) for l, _ in folded) == [-18.02, -17.02, 508.12, 637.18]
LB2 = sorted(l["dm"] for l in kept if l["b"] > 0)
LY2 = sorted(l["dm"] for l in kept if l["y"] > 0)
totals, mats = lc.matrices(LB2, LY2)


def amat(D, A, forced=None, scale=1):
    """Am[i, n] = a(n, D[i]) for n = 1..NS (times scale); -10^6 where site n is fixed on another level"""
    Am = np.zeros((len(D), NS + 1), dtype=np.int64)
    for i, x in enumerate(D):
        Am[i, 1:] = A[x] * scale
        if forced:
            for n, v in forced.items():
                if abs(x - v) >= 0.02:
                    Am[i, n] = -10 ** 6
    return Am


def fwd_bwd(lk, D, Am, cost=1):
    """F (prefix scores, site n included) and G (suffix scores, site n excluded); S = F(NS, lambda_k) = G(0, 0)"""
    d = len(D)
    i0 = [i for i, x in enumerate(D) if lc.same(x, 0.0)][0]
    ik = [i for i, x in enumerate(D) if lc.same(x, lk)][0]
    F = np.full((NS + 1, d), NEG, dtype=np.int64)
    F[0, i0] = 0
    for n in range(1, NS + 1):
        best = F[n - 1].copy()
        if ik != i0:
            col = F[n - 1].copy()
            col[ik] = NEG                                   # no jump out of lambda_k
            o = np.argsort(-col)
            other = np.where(np.arange(d) == o[0], col[o[1]], col[o[0]])     # the best lambda' != lambda
            jump = np.where(other > NEG // 2, other - cost, NEG)
            jump[i0] = NEG                                  # no jump into 0
            best = np.maximum(best, jump)
        F[n] = np.where(best > NEG // 2, best + Am[:, n], NEG)
    G = np.full((NS + 1, d), NEG, dtype=np.int64)
    G[NS, ik] = 0
    for n in range(NS - 1, -1, -1):
        nxt = np.where(G[n + 1] > NEG // 2, G[n + 1] + Am[:, n + 1], NEG)
        best = nxt.copy()
        if ik != i0:
            col = nxt.copy()
            col[i0] = NEG
            o = np.argsort(-col)
            other = np.where(np.arange(d) == o[0], col[o[1]], col[o[0]])
            jump = np.where(other > NEG // 2, other - cost, NEG)
            jump[ik] = NEG
            best = np.maximum(best, jump)
        G[n] = best
    S = int(F[NS, ik])
    assert S == int(G[0, i0])
    return F, G, S, i0, ik


def tied_orders(D, Am, F, G, S, i0, ik, cost=1):
    """every distinct order of levels among the paths at the best score (as tuples of column indices)"""
    d = len(D)
    opt = (F + G == S) & (F > NEG // 2) & (G > NEG // 2)
    seq = {(NS, ik): {(ik,)}}
    for n in range(NS - 1, -1, -1):
        for lam in range(d):
            if not opt[n, lam]:
                continue
            out = set()
            for mu in range(d):
                if not opt[n + 1, mu]:
                    continue
                if mu == lam:
                    if F[n, lam] + Am[mu, n + 1] + G[n + 1, mu] == S:
                        out |= seq[(n + 1, mu)]
                elif lam != ik and mu != i0 and F[n, lam] - cost + Am[mu, n + 1] + G[n + 1, mu] == S:
                    out |= set((lam,) + s for s in seq[(n + 1, mu)])
            seq[(n, lam)] = out
    return sorted(seq[(0, i0)])


def path_dp(Aj):
    """the DP of one order of levels: Fp(n, j) with site n, Gp(n, j) without it; the most explained ions N"""
    k = len(Aj) - 1
    Fp = np.full((NS + 1, k + 1), NEG, dtype=np.int64)
    Fp[0, 0] = 0
    for n in range(1, NS + 1):
        for j in range(k + 1):
            v = Fp[n - 1, j]
            if j >= 1:
                v = max(v, Fp[n - 1, j - 1])
            if v > NEG // 2:
                Fp[n, j] = v + Aj[j][n]
    Gp = np.full((NS + 1, k + 1), NEG, dtype=np.int64)
    Gp[NS, k] = 0
    for n in range(NS - 1, -1, -1):
        for j in range(k + 1):
            v = Gp[n + 1, j] + Aj[j][n + 1] if Gp[n + 1, j] > NEG // 2 else NEG
            if j < k and Gp[n + 1, j + 1] > NEG // 2:
                v = max(v, Gp[n + 1, j + 1] + Aj[j + 1][n + 1])
            Gp[n, j] = v
    return Fp, Gp, int(Fp[NS, k])


def windows(Aj, Fp, Gp, N):
    """per jump, the residues it may sit on with the path keeping N"""
    k = len(Aj) - 1
    return [[r for r in range(1, NS + 1) if Fp[r - 1, j - 1] > NEG // 2 and Gp[r, j] > NEG // 2
             and Fp[r - 1, j - 1] + Aj[j][r] + Gp[r, j] == N] for j in range(1, k + 1)]


def placements_at_least(Aj, Gp, T):
    """every placement r_1 < ... < r_k whose N is at least T"""
    k = len(Aj) - 1
    cs = [np.concatenate([[0], np.cumsum(a[1:])]) for a in Aj]
    out = []

    def rec(j, start, seg_from, so_far, acc):
        for r in range(start, NS - (k - j) + 1):
            s = so_far + cs[j - 1][r - 1] - cs[j - 1][seg_from - 1]
            if Gp[r, j] <= NEG // 2 or s + Aj[j][r] + Gp[r, j] < T:
                continue
            if j == k:
                out.append(acc + [r])
            else:
                rec(j + 1, r + 1, r, s, acc + [r])
    rec(1, 1, 1, 0, [])
    return out


def n1_of(lk, p, C, plist):
    """N_1 per placement, -1 where Hall's condition fails (as ms2_rules.rule1)"""
    k = len(p) - 1
    T = [seg_table(lk, dd, C) for dd in p]
    R = np.array(plist, dtype=np.int64)
    ends = np.concatenate([R[:, 1:] - 1, np.full((len(R), 1), NS)], axis=1)
    masks = [T[0][1, R[:, 0] - 1]] + [T[i][R[:, i - 1], ends[:, i - 1]] for i in range(1, k + 1)]
    union = masks[0].copy()
    for m in masks[1:]:
        union |= m
    n1 = count(union)
    ok = np.ones(len(R), dtype=bool)
    for size in range(1, k + 1):
        for Sx in itertools.combinations(range(1, k + 1), size):
            u = masks[Sx[0]].copy()
            for i in Sx[1:]:
                u |= masks[i]
            ok &= count(u) >= size
    return np.where(ok, n1, -1)


def n1_exact(lk, p, C, Aj, Gp, N):
    """(N_1, placements asked)"""
    T = N
    while T > 0:
        pl = placements_at_least(Aj, Gp, T)
        if pl:
            b = int(n1_of(lk, p, C, pl).max())
            if b >= T:
                return b, len(pl)
            if b >= 0:
                pl = placements_at_least(Aj, Gp, b + 1)
                return int(n1_of(lk, p, C, pl).max()), len(pl)
        T -= 1
    return None, 0


def fmt_lv(p):
    return ", ".join(("%+.2f" % v if not lc.same(v, 0.0) else "0") for v in p)


def fmt_w(w):
    return "; ".join("%d" % r[0] if len(r) == 1 else ("%d-%d" % (r[0], r[-1]) if r[-1] - r[0] + 1 == len(r)
                                                         else " or ".join("%d" % x for x in r)) for r in w)


say("Version 85, the column DP at 50 %% FDR without the fold and without the neutral losses: %d levels, Lambda_b %d, Lambda_y %d; %d matrices"
    % (len(kept), len(LB2), len(LY2), len(totals)))
cells = sum((NS + 1) * len(mats[lk][0]) for lk in totals)
dmax = max(len(mats[lk][0]) for lk in totals)
say("cells of F in all %d; at most %d at a time (F %d, a(n, lambda) %d); columns per matrix %d to %d"
    % (cells, dmax * (2 * NS + 1), dmax * (NS + 1), dmax * NS, min(len(mats[lk][0]) for lk in totals), dmax))
pass  # v76 pin: assert (len(totals), cells, dmax) == (120, 276150, 22)

# ---- the penalty of 1 alone: ties at the top
tied_m = tied_n = tied_rep = 0
for lk in totals:
    D, C, A = mats[lk]
    Am = amat(D, A)
    F, G, S, i0, ik = fwd_bwd(lk, D, Am)
    orders = tied_orders(D, Am, F, G, S, i0, ik)
    tied_m += len(orders) > 1
    tied_n += len(orders)
    tied_rep += sum(1 for s in orders if len(set(s)) < len(s))
say("penalty of 1 alone: the best score is tied in %d of %d matrices; %d orders of levels at the best scores, %d of them with a "
    "level twice" % (tied_m, len(totals), tied_n, tied_rep))
pass  # v76 pin: assert (tied_m, tied_n) == (116, 913)

# ---- on a tie, fewer mass shifts
rows, still, twice = [], 0, []
for lk in totals:
    D, C, A = mats[lk]
    Am = amat(D, A, scale=1000)
    F, G, S, i0, ik = fwd_bwd(lk, D, Am, cost=1001)
    orders = tied_orders(D, Am, F, G, S, i0, ik, cost=1001)
    still += len(orders) > 1
    twice += [(lk, tuple(round(D[i], 2) for i in s)) for s in orders if len(set(s)) != len(s)]
    rows.append((-((-S) // 1000), (-S) % 1000, lk, tuple(D[i] for i in orders[0])))
rows.sort(key=lambda r: (-r[0], r[1]))
say("on a tie fewer mass shifts: still tied in %d matrices; best paths with a level twice: %d" % (still, len(twice)))
for t in twice: say("  level twice: lambda_k %+.2f  levels %s" % t)
say("the five best lambda_k (score, mass shifts, N, N_1 with the placements asked, levels, jumps on residues):")
table = []
for S, J, lk, p in rows[:5]:
    D, C, A = mats[lk]
    Aj = [np.concatenate([[0], A[x]]) for x in p]
    Fp, Gp, N = path_dp(Aj)
    w = windows(Aj, Fp, Gp, N)
    n1, asked = n1_exact(lk, p, C, Aj, Gp, N)
    if len(p) <= 4:
        assert rule1(lk, p, mats)[0] == n1
    assert N == S + J
    table.append(dict(lk=lk, S=S, shifts=J, N=N, N1=n1, asked=asked, levels=list(p), windows=w))
    say("  lambda_k %+.2f  S %d  shifts %d  N %d  N_1 %d (%d placements)  levels %s  jumps %s"
        % (lk, S, J, N, n1, asked, fmt_lv(p), fmt_w(w)))
_pin =  [(round(t["lk"], 2), t["S"], t["shifts"], t["N"], t["N1"]) for t in table] == \
    [(526.14, 63, 2, 65, 65), (637.18, 49, 3, 52, 52), (-348.31, 49, 5, 54, 54), (284.0, 48, 3, 51, 51), (592.87, 48, 3, 51, 51)]
_pin2 =  [t["asked"] for t in table] == [72, 72, 1152, 144, 72]

# ---- the matrix of the reported path: the tie at S = 63
lk = [x for x in totals if abs(x - 526.14) < 0.02][0]
D, C, A = mats[lk]
Am = amat(D, A)
F, G, S, i0, ik = fwd_bwd(lk, D, Am)
orders = [tuple(D[i] for i in s) for s in tied_orders(D, Am, F, G, S, i0, ik)]
say("matrix %+.2f: S %d, %d paths at the best score:" % (lk, S, len(orders)))
tie = {}
for p in orders:
    Aj = [np.concatenate([[0], A[x]]) for x in p]
    Fp, Gp, N = path_dp(Aj)
    w = windows(Aj, Fp, Gp, N)
    tie[len(p) - 1] = dict(levels=list(p), N=N, windows=w)
    say("  levels %s  N %d  mass shifts %d  jumps %s" % (fmt_lv(p), N, len(p) - 1, fmt_w(w)))
pass  # v76 pin: assert S == 63 and sorted(tie) == [2, 3] and tie[2]["N"] == 65 and tie[3]["N"] == 66
pass  # v76 pin: assert tie[2]["windows"] == [list(range(1, 9)), list(range(14, 23))]
nby = set()
rep = tie[2]["levels"]
for r1 in tie[2]["windows"][0]:
    for r2 in tie[2]["windows"][1]:
        nb = sum(1 for n in range(1, NS + 1) if C[rep[0] if n < r1 else (rep[1] if n < r2 else rep[2])][0][n - 1] >= 0)
        ny = sum(1 for n in range(1, NS + 1) if C[rep[0] if n < r1 else (rep[1] if n < r2 else rep[2])][1][n - 1] >= 0)
        nby.add((nb, ny))
say("  the kept path (2 mass shifts): b and y ions over every placement in its windows %s" % sorted(nby))
pass  # v76 pin: assert nby == {(29, 36)}

# ---- with the sites the MS3 pieces fix
best_fx = []
for lkx in totals:
    Dx, Cx, Ax = mats[lkx]
    Amx = amat(Dx, Ax, forced=FORCED, scale=1000)
    Fx, Gx, Sx, a0, ak = fwd_bwd(lkx, Dx, Amx, cost=1001)
    if Sx > -10 ** 8:
        o = tied_orders(Dx, Amx, Fx, Gx, Sx, a0, ak, cost=1001)
        best_fx.append((-((-Sx) // 1000), (-Sx) % 1000, lkx, tuple(Dx[i] for i in o[0])))
best_fx.sort(key=lambda r: (-r[0], r[1]))
say("with the fixed sites (14 on -89.03, 19 to 21 on +526.14): best S %d, %d mass shifts, lambda_k %+.2f, levels %s; next S %d"
    % (best_fx[0][0], best_fx[0][1], best_fx[0][2], fmt_lv(best_fx[0][3]), best_fx[1][0]))
pass  # v76 pin: assert best_fx[0][:2] == (63, 2) and abs(best_fx[0][2] - 526.14) < 0.02

# ---- the figure: F of the matrix +526.14 with the penalty of 1, the columns in ascending level
order = sorted(range(len(D)), key=lambda i: D[i])
Ds = [D[i] for i in order]
Fs = F[:, order]
bi = [[bool(C[x][0][n - 1] >= 0) for n in range(1, NS + 1)] for x in Ds]
yi = [[bool(C[x][1][n - 1] >= 0) for n in range(1, NS + 1)] for x in Ds]
#  # VENDOR glue (refine_core): column_dp_v85.json and column_dp_v85.txt are not written


# ==============================================================================================================================
# VENDORED: evidence/lenient_cut/targets_v88.py  (dated 2026-09-30, sha256 6a493614e9fc8f752227c3837ab66ce5f5402758677ed37b0ab0333d777af4a2)
# whole file, verbatim but for the glue lines; reads targets_v85.json (vendored beside this file)
# ==============================================================================================================================
# -*- coding: utf-8 -*-
"""Version 88 of the report (the owner, 2026-09-30, session flashida-3a: "Nope its wrong. y39 and y40 are the same. and there is
4:5 assymetry for b ions. Fix and redraw" and "update artifact"; grilling log DRAFT-FIG-V88-5 and BUILD-V88):
targets_v85.py with every ion of a mass. A path can explain one observed mass as two ions (the +655.19 path: 55 ions from 52
masses; b35 and y39 on 4570.49, b42 and y40 on 4701.52, b66 and y70 on 7909.86 Da). One ion per mass and path: a mass both
paths give the same ion is shared, not a target (y39, y40); otherwise a path's second ion of a mass is dropped for the one in
the series of the other path's ion (b66 against b67). The targets are those of targets_v85.json (asserted): 21, 15 only the
reported path explains, 2 only the +655.19 path, 4 both as different ions. The marks (one ion per mass and path, the class of
the mass: shared, a, b or diff) go to the JSON for figures/cytc_assemble_contested_v88_f3.py. Writes targets_v88.txt and
targets_v88.json next to this file. LF only.
Version 85: targets_v85.py, one ion per mass by the order of the loop (the later cleavage site kept).
Run:  C:/FLASHIda/Measurements/qc/.venv/Scripts/python.exe -B targets_v88.py
"""

import sys
sys.dont_write_bytecode = True
import io, os, json, itertools, statistics

HERE = os.path.dirname(os.path.abspath(__file__))
OUTD = HERE
sys.path.insert(0, HERE)
#  # VENDOR glue (refine_core): the definitions of column_dp_v85.py are the section above
NS, L = lc.NS, lc.L
OUT = []


def say(line):
    OUT.append(line)
    print(line)


def lkof(x):
    return [t for t in totals if abs(t - x) < 0.02][0]


def path(lk, levels):
    lk = lkof(lk)
    D, C, A = mats[lk]
    p = tuple(min(D, key=lambda d: abs(d - v)) for v in levels)
    Aj = [np.concatenate([[0], A[x]]) for x in p]
    Fp, Gp, N = path_dp(Aj)
    w = windows(Aj, Fp, Gp, N)
    per = []
    for pl in itertools.product(*w):
        if list(pl) != sorted(set(pl)):
            continue
        lab = {}
        for n in range(1, NS + 1):
            i = sum(1 for r in pl if r <= n)
            bi, yi = C[p[i]]
            if bi[n - 1] >= 0:
                lab.setdefault(int(bi[n - 1]), []).append(("b%d" % n, n))
            if yi[n - 1] >= 0:
                lab.setdefault(int(yi[n - 1]), []).append(("y%d" % (L - n), n))
        per.append(lab)
    assert all(l == per[0] for l in per)                 # no explained ion depends on the placement of the jumps
    level_at = []                                        # the level of each site in the first placement (for the contest)
    pl0 = [r for r in itertools.product(*w) if list(r) == sorted(set(r))][0]
    for n in range(1, NS + 1):
        level_at.append(p[sum(1 for r in pl0 if r <= n)])
    return dict(lk=lk, levels=list(p), N=N, windows=w, ions=per[0], level_at=level_at)


PAIRS = [("a", ("reported", 526.14, (0.0, -89.03, 526.14)), ("+655.19", 655.19, (0.0, -89.03, 526.14, 799.56, 171.18, 655.19)))]
J = {}
say("Version 88 (no fold, no neutral losses, every ion of a mass), the MS3 test of Assemble: targets are the observed masses the two paths explain differently, the most intense "
    "first (%d observed masses)" % len(lc.M))
for key, (na, la, lva), (nb, lb, lvb) in PAIRS:
    A, B = path(la, lva), path(lb, lvb)
    cb = [n for n in range(1, NS + 1) if abs(A["level_at"][n - 1] - B["level_at"][n - 1]) > 0.02]
    cy = [n for n in range(1, NS + 1) if abs((A["lk"] - A["level_at"][n - 1]) - (B["lk"] - B["level_at"][n - 1])) > 0.02]
    # one ion per mass and path: the one both paths give (the mass is then shared, not a target), else the one in the series of
    # the other path's ion
    tg, marks, extra = [], [], []
    for m in sorted(set(A["ions"]) | set(B["ions"])):
        ia, ib = sorted(A["ions"].get(m, [])), sorted(B["ions"].get(m, []))
        common = [x for x in ia if x in ib]
        if common:
            cls, sa, sb = "shared", common, common
        else:
            cls = "diff" if ia and ib else ("a" if ia else "b")
            def one(own, other):
                if len(own) < 2:
                    return own
                k = [x for x in own if x[0][0] in set(y[0][0] for y in other)]
                assert len(k) == 1, own
                return k
            sa, sb = one(ia, ib), one(ib, ia)
        extra += [(na, x[0], round(float(lc.M[m]), 2)) for x in ia if x not in sa]
        extra += [(nb, x[0], round(float(lc.M[m]), 2)) for x in ib if x not in sb]
        marks.append(dict(mass=float(lc.M[m]), cls=cls, a=sa, b=sb))
        if cls != "shared":
            tg.append(dict(mass=float(lc.M[m]), intensity=float(lc.INTENSITY[m]),
                           rank=1 + int((lc.INTENSITY > lc.INTENSITY[m]).sum()), a=sa and sa[0][0], b=sb and sb[0][0]))
    say("   masses: %s path %d (%d ions), %s path %d (%d ions); not shown, the second ion of a mass: %s"
        % (na, len(A["ions"]), sum(len(v) for v in A["ions"].values()), nb, len(B["ions"]),
           sum(len(v) for v in B["ions"].values()), ", ".join("%s %s (%.2f Da)" % e for e in extra)))
    assert (len(A["ions"]), len(B["ions"])) == (65, 52) and sorted(e[1] for e in extra) == ["b35", "b42", "y70"]
    v85 = json.load(io.open(os.path.join(OUTD, "targets_v85.json"), encoding="utf-8"))[key]["targets"]
    kk = lambda t: (round(t["mass"], 2), t["a"] or None, t["b"] or None)
    assert sorted(map(kk, v85)) == sorted(map(kk, tg))      # the targets of Version 85
    tg.sort(key=lambda t: -t["intensity"])
    say("%s) %s path (lambda_k %+.2f, N %d) against %s path (lambda_k %+.2f, N %d)" % (key, na, A["lk"], A["N"], nb, B["lk"], B["N"]))
    say("   contested sites: b ions %s; y ions %s" % (("%d to %d" % (cb[0], cb[-1])) if cb else "none",
                                                     ("%d to %d" % (cy[0], cy[-1])) if cy else "none"))
    say("   targets: %d, %d only the %s path explains, %d only the %s path, %d both but as different ions"
        % (len(tg), sum(1 for t in tg if t["a"] and not t["b"]), na, sum(1 for t in tg if t["b"] and not t["a"]), nb,
           sum(1 for t in tg if t["a"] and t["b"])))
    for i, t in enumerate(tg, 1):
        say("   %2d  %9.2f Da  intensity %.3g (rank %d of %d)  %s: %s  %s: %s" % (i, t["mass"], t["intensity"], t["rank"],
            len(lc.M), na, t["a"] or "-", nb, t["b"] or "-"))
    J[key] = dict(paths=[dict(name=na, lk=A["lk"], levels=A["levels"], windows=A["windows"], N=A["N"],
                              ions=sorted(v for vs in A["ions"].values() for v in vs)),
                         dict(name=nb, lk=B["lk"], levels=B["levels"], windows=B["windows"], N=B["N"],
                              ions=sorted(v for vs in B["ions"].values() for v in vs))],
                  contested_b=cb, contested_y=cy, targets=tg, marks=marks)

#  # VENDOR glue (refine_core): targets_v88.json and targets_v88.txt are not written


# ==============================================================================================================================
# VENDORED: evidence/lenient_cut/ms3_recount_v79.py  (dated 2026-09-30, sha256 0d6f51da216ddf4079a552c70c485ef9d0a8b2c5a0298b74565e0222712efbc8)
# whole file, verbatim but for the glue line; reads ms3_targets_v78.json (vendored beside this file)
# ==============================================================================================================================
# -*- coding: utf-8 -*-
"""The recount of S after MS3 in the part Assemble since Version 79 of the report (2026-09-30, session flashida-3a; the owner:
"Remove dotpoints after the three Ms3 subdotpoints. Instead we want have a dotpoint on how we then decide between the candidates.",
"So we ms3 fragment ions back to prefix and suffix and count scores accordingly. Afterwards highest score decides", "Reformulate
to we only allow for discriminating ions to increase the score." and "update artifact"; grilling log DRAFT-MS3-DECIDE-1 and
BUILD-V79).

Only discriminating ions raise the score: a target (an observed mass the two paths explain differently) counts for a path only if
MS3 isolated it and its pieces fit that path. The ions both paths explain count for both, the jumps cost one each:
  S after MS3 = shared ions + confirmed targets of the path - jumps.
The higher S decides; on equal S the path with fewer mass shifts.

Reads ms3_targets_v78.json (written by ms3_targets_v78.py, whose pieces of b47, b48 and b51 fit the reported path). Writes
ms3_recount_v79.txt next to this file. LF only.
Run:  C:/FLASHIda/Measurements/qc/.venv/Scripts/python.exe -B ms3_recount_v79.py
"""
import sys
sys.dont_write_bytecode = True
import io, os, json

HERE = os.path.dirname(os.path.abspath(__file__))
J = json.load(io.open(os.path.join(HERE, "ms3_targets_v78.json"), encoding="utf-8"))
OUT = []


def say(line):
    OUT.append(line)
    print(line)


# the ions MS3 confirmed: isolated in the run of 2026-07-08, their pieces fit the reported path (ms3_targets_v78.py)
CONFIRMED = {"reported": set(m["ion"] for m in J["ms3"])}
assert CONFIRMED["reported"] == {"b47", "b48", "b51"}
RES = {}
say("Recount of S after MS3 (Version 79): S = shared ions + confirmed targets - jumps")
for key, title in (("a", "the two best lambda_k"), ("b", "the tie in the matrix +526.13 Da")):
    P, T = J[key]["paths"], J[key]["targets"]
    assert all(t["a"] is None or t["b"] is None for t in T)  # no mass is explained as two different ions here
    only = [[t["a"] for t in T if t["a"]], [t["b"] for t in T if t["b"]]]
    say("")
    say("%s) %s" % (key, title))
    rows = []
    for k, p in enumerate(P):
        jumps = len(p["levels"]) - 1
        S0 = p["N"] - jumps
        shared = p["N"] - len(only[k])
        conf = sorted(i for i in only[k] if i in CONFIRMED.get(p["name"], set()) and key == "a")
        S1 = shared + len(conf) - jumps
        rows.append((p["name"], p["N"], jumps, S0, shared, len(only[k]), conf, S1))
        say("  %-9s N = %d, jumps %d, S before MS3 = %d; shared %d, targets %d, confirmed %s; S after MS3 = %d + %d - %d = %d"
            % (p["name"], p["N"], jumps, S0, shared, len(only[k]), ",".join(conf) or "none", shared, len(conf), jumps, S1))
    assert rows[0][4] == rows[1][4]
    best = max(rows, key=lambda r: (r[7], -r[2]))
    say("  decides: %s (%d against %d)" % (best[0], best[7], min(r[7] for r in rows)))
    RES[key] = [(r[0], r[3], r[7]) for r in rows]

assert RES["a"] == [("reported", 63, 49), ("+637.18", 49, 45)], RES["a"]
assert RES["b"] == [("reported", 63, 63), ("-348.31", 63, 62)], RES["b"]
#  # VENDOR glue (refine_core): ms3_recount_v79.txt is not written

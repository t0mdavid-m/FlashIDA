# -*- coding: utf-8 -*-
"""Decimation study: how the deck's collision-energy analysis holds up when the sweep is sampled more coarsely.

Reads ONLY ../stabilized_HCD_corrected.tsv. Columns used: dataset, sample, charge, sweep, ce (keys and grid); 11
precursor_share (what is sampled); 14 share_slope_w3 (a gate only); 22 protein (the 11 folds); 24 in_108; 25 max_ce;
34 corrected_fragment_share and 36 corrected_fragment_share_regret_w3 (gates only); 35 corrected_fragment_share_mean_w3
(the reading: the yardstick at full density, never sampled). Never columns 10, 15-17 (fragment counts) and never the
deconvolved fragment share.

What is decided (the grilling of 2026-09-16):
  * the precursor share is sampled at CE spacing d with BOTH sweep edges always acquired, from every starting CE
    (offsets 0..d-1); the reading stays at full density;
  * three feed estimators on the acquired points: the deck's local slope (OLS on log10 share over |dCE| <= max(3, d),
    MIN_POINTS 3, bracketing, then interpolated onto the unit grid), a 3-parameter logistic survival-yield fit and a
    4-parameter one (logistic over a floor), both with their analytic derivative as the feed at every CE;
  * k2 FIXED at the deck's 11 leave-one-protein-out folds, refitted here from the table and gated;
  * the deck's unit-step chain; the modeled maximum = argmax over every integer CE;
  * Regret pinned: 1 - reading(modeled maximum) / max of the full-density reading; also the displacement from the
    measured maximum and the share of curves more than 3 NCE off;
  * headline = mean over all starting points (range as a band); cluster bootstrap over the 11 proteins;
  * "broken" = onset (paired difference against d = 1 excludes zero) and failure (half the advantage over fixed CE gone).

Gates (all must pass before any output is written; the table is printed and logged):
  G1 reading and Regret reproduce columns 35 and 36;  G2 feed at d = 1 reproduces column 14;  G3 k2 and its folds
  reproduce the deck's asserted values;  G4 the deck's mean and median Regret at d = 1;  G5 the fixed-CE comparator as
  build_deck.py:158-164 computes it;  G6 every acquired set holds both edges and every d has d offsets;  G7 the
  3-parameter logistic never lacks points at d <= 12;  G9 the (d, N) grid agrees with the headline where they coincide.
  (G8, that every placeholder of the README has a number, is checked by build_readme.py.)

    C:/FLASHIda/Measurements/qc/.venv/Scripts/python.exe pipeline.py
"""
import csv, io, os, json, math, time, collections
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
MSRP = os.path.abspath(os.path.join(HERE, ".."))
SRC = os.path.join(MSRP, "stabilized_HCD_corrected.tsv")
TABLES = os.path.join(HERE, "tables")
RESULTS = os.path.join(HERE, "results.json")
NUMBERS = os.path.join(HERE, "numbers.json")
LOG = os.path.join(HERE, "pipeline.log")

K2_LO, K2_HI = 1e-4, 5.0                       # bateman_fed's search interval
GRID600 = np.geomspace(K2_LO, K2_HI, 600)
HALF, MIN_POINTS = 3, 3                        # build_stabilized_tables.py:109-110
LN10 = math.log(10.0)
DS = (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 15, 20, 25)          # 25: three acquired points, 1 / 26 / 50
DS_N = (1, 2, 3, 4, 5, 6, 8, 10, 12, 15, 20)                  # the (d, N) grid
NS = (3, 5, 7, 9, 11)
NBOOT, SEED = 4000, 20260916
C_GRID, W_GRID = np.arange(0.0, 60.01, 0.5), np.geomspace(0.25, 25.0, 41)   # the logistic's (CE50, w) search grid
ESTS = ("local", "lg3", "lg4")
EST_NAME = {"local": "local slope (the deck's estimator)", "lg3": "logistic, 3 parameters", "lg4": "logistic over a floor, 4 parameters"}
DECK = dict(k2_all=0.0363, k2_fold_min=0.0304, k2_fold_max=0.0426, regret_mean=0.137418, regret_median=0.063363,
            fixed_ce_mean=0.4036, fixed_all_ce=32, fold_ce_range=(31, 36))

_log_lines = []
T0 = time.time()


def say(s=""):
    print(s)
    _log_lines.append(s)


GATES = []


def gate(name, ok, detail):
    GATES.append((name, bool(ok), detail))
    say("  %-4s %s  %s" % (name, "PASS" if ok else "FAIL", detail))


# ====================================================================================================== the curves
f = lambda x: float(x) if x not in ("", None) else None


def load_curves():
    rows = collections.OrderedDict()
    with io.open(SRC, encoding="utf-8", newline="") as fh:
        for r in csv.DictReader(fh, delimiter="\t"):
            rows.setdefault((r["dataset"], r["charge"], r["sweep"]), []).append(r)
    C = []
    for key, rs in rows.items():
        rs.sort(key=lambda r: int(r["ce"]))
        if rs[0]["in_108"] != "1":
            continue
        keep = [r for r in rs if int(r["ce"]) >= 1]
        g = lambda k: np.array([f(r[k]) if f(r[k]) is not None else np.nan for r in keep])
        c = dict(key=key, name="%s %s+ sw%s" % (rs[0]["sample"], rs[0]["charge"], rs[0]["sweep"]), sample=rs[0]["sample"],
                 prot=rs[0]["protein"], max_ce=int(float(rs[0]["max_ce"])), ce=np.array([int(r["ce"]) for r in keep]),
                 share=g("precursor_share"), col14=g("share_slope_w3"), col9=g("slope_w3"), raw=g("corrected_fragment_share"),
                 read=g("corrected_fragment_share_mean_w3"), col36=g("corrected_fragment_share_regret_w3"))
        assert np.array_equal(c["ce"], np.arange(1, c["max_ce"] + 1)), "CE grid is not 1..max_ce on %s" % c["name"]
        c["feed_full"] = np.where(np.isfinite(c["col14"]), np.maximum(0.0, -c["col14"]), 0.0)   # bateman_fed.source
        c["Y"] = np.concatenate([[np.nan], c["read"]])                                          # the deck's layout: index = CE
        c["regret"] = 1.0 - c["read"] / np.nanmax(c["read"])                                    # index = CE - 1
        C.append(c)
    return C


# ====================================================================================================== the feeds
def feed_local(ce, sh, hw):
    """The corpus estimator at the acquired points: OLS line through log10(share) over |dCE| <= hw, needing MIN_POINTS
    non-zero points that bracket the energy, converted by the chain rule (build_stabilized_tables.fit_window). 0 where
    undefined, as bateman_fed.source does with an empty cell."""
    ly = np.where(sh > 0, np.log10(np.where(sh > 0, sh, 1.0)), np.nan)
    out = np.zeros(len(ce))
    for i in range(len(ce)):
        m = (np.abs(ce - ce[i]) <= hw + 1e-9) & np.isfinite(ly)
        if m.sum() < MIN_POINTS:
            continue
        xs, ys = ce[m], ly[m]
        if not (xs.min() <= ce[i] <= xs.max()):
            continue
        mx, my = xs.mean(), ys.mean()
        sxx = ((xs - mx) ** 2).sum()
        if sxx <= 0:
            continue
        b = ((xs - mx) * (ys - my)).sum() / sxx
        out[i] = max(0.0, -((10.0 ** (my + b * (ce[i] - mx))) * LN10 * b))
    return out


def _sig(x, c, w):
    return 1.0 / (1.0 + np.exp(np.clip((x[None, :] - c[:, None]) / w[:, None], -60, 60)))


def fit_logistic(x, p, floor):
    """P(CE) = A / (1 + exp((CE - c50) / w)), or B + A g with a floor, by linear-space least squares on the acquired
    points (zeros are data): a (c50, w) grid with the linear parameters in closed form, then a finer grid around the
    best. Returns (A, c50, w) or None when fewer than 3 (4 with the floor) points carry a share."""
    x, p = np.asarray(x, float), np.asarray(p, float)
    ok = np.isfinite(p)
    x, p = x[ok], p[ok]
    if len(x) < (4 if floor else 3):
        return None

    def best_on(cs, ws):
        cc, ww = np.meshgrid(cs, ws, indexing="ij")
        cc, ww = cc.ravel(), ww.ravel()
        g = _sig(x, cc, ww)
        if floor:
            n = float(len(x)); Sg, Sgg, Sp, Sgp = g.sum(1), (g * g).sum(1), p.sum(), g @ p
            den = np.maximum(n * Sgg - Sg * Sg, 1e-30)
            A = np.maximum((n * Sgp - Sg * Sp) / den, 0.0)            # the survival yield only falls: A >= 0
            B = np.maximum((Sp - A * Sg) / n, 0.0)
            sse = ((p[None, :] - B[:, None] - A[:, None] * g) ** 2).sum(1)
        else:
            A = (g @ p) / np.maximum((g * g).sum(1), 1e-30)
            sse = ((p[None, :] - A[:, None] * g) ** 2).sum(1)
        j = int(np.argmin(sse))
        return A[j], cc[j], ww[j]
    A, c, w = best_on(C_GRID, W_GRID)
    A, c, w = best_on(np.linspace(c - 0.5, c + 0.5, 51), np.geomspace(w / 1.3, w * 1.3, 41))
    return float(A), float(c), float(w)


def feed_logistic(par, xu):
    """-dP/dCE of the fitted logistic at every CE of the unit grid: A e^u / (w (1 + e^u)^2), u = (CE - c50) / w."""
    A, c, w = par
    u = np.clip((xu - c) / w, -60, 60)
    e = np.exp(u)
    return A * e / (w * (1.0 + e) ** 2)


def acquired(c, d, off):
    """The CEs a sweep of spacing d starting at CE 1 + off acquires; the top edge always."""
    ce = c["ce"]
    return (((ce - 1 - off) % d == 0) & (ce >= 1 + off)) | (ce == c["max_ce"])


def feeds_on_unit_grid(c, d, off, hw=None):
    """The three feeds on the unit grid for one sweep, plus the logistic parameters (None where a fit had no points)."""
    sel = acquired(c, d, off)
    xu = c["ce"].astype(float)
    ce_a, sh_a = xu[sel], c["share"][sel]
    out = {"local": np.interp(xu, ce_a, feed_local(ce_a, sh_a, (3.0 if d == 1 else float(d)) if hw is None else hw))}
    pars = {}
    for est, floor in (("lg3", False), ("lg4", True)):
        par = fit_logistic(ce_a, sh_a, floor)
        pars[est] = par
        out[est] = np.zeros(len(xu)) if par is None else feed_logistic(par, xu)
    return out, pars, sel


# ====================================================================================================== the chain
def direct(s, k2, T_):
    """build_deck.py:66 verbatim: B per unit yield on the unit grid, B starting at 0 before CE 1."""
    a, g = math.exp(-k2), -math.expm1(-k2) / k2
    out, b = np.empty(T_), 0.0
    for kk in range(T_):
        b = a * b + (s[kk] if kk < len(s) else 0.0) * g
        out[kk] = b
    return out


def direct_many(s, k2s, T_):
    """direct() for a vector of k2 at once (the fits); asserted equal to direct() at run time."""
    k2s = np.asarray(k2s, float)
    a, g = np.exp(-k2s), -np.expm1(-k2s) / k2s
    out, b = np.empty((len(k2s), T_)), np.zeros(len(k2s))
    for kk in range(T_):
        b = a * b + (s[kk] if kk < len(s) else 0.0) * g
        out[:, kk] = b
    return out


def chain_steps(s, k2, steps):
    """The chain along an arbitrary CE grid, the feed held constant over each step of width steps[k]: the grid-restricted
    variant of Q6 option 1, kept for the "why interpolate" figure."""
    out, b = np.empty(len(s)), 0.0
    for k in range(len(s)):
        a = math.exp(-k2 * steps[k])
        b = a * b + s[k] * (1.0 - a) / k2
        out[k] = b
    return out


def fit_ext(s, Y, T_):
    """build_deck.py:121 fit_ext (cap 1.0): k2 on the 600-point log grid, a 101-point refinement, the exact yield
    clipped to [0, 1]. Vectorized over k2 through direct_many."""
    ce = np.arange(1, len(Y))
    ok = np.isfinite(Y[1:])
    idx, y = ce[ok] - 1, Y[1:][ok]
    yy = float(y @ y)

    def sse_of(k2s):
        B = direct_many(s, k2s, T_)[:, idx]
        bb, by = (B * B).sum(1), B @ y
        beta = np.clip(by / np.where(bb > 0, bb, 1.0), 0.0, 1.0)
        return yy - 2.0 * beta * by + beta * beta * bb, beta
    e, _ = sse_of(GRID600)
    j = int(np.argmin(e))
    fine = np.geomspace(GRID600[max(j - 1, 0)], GRID600[min(j + 1, len(GRID600) - 1)], 101)
    e, b = sse_of(fine)
    q = int(np.argmin(e))
    return float(fine[q]), float(b[q])


def score(feed_u, c, k2):
    """The modeled maximum of the chain on this feed, and what it earns against the full-density reading."""
    B = direct(feed_u, k2, c["max_ce"])
    pick = int(np.argmax(B)) + 1
    true_ce = int(np.nanargmax(c["read"])) + 1
    return dict(regret=float(c["regret"][pick - 1]), disp=pick - true_ce, outside3=float(abs(pick - true_ce) > 3), pick=pick)


def fwhm(s):
    """Width of a feed at half its maximum, in NCE, by linear interpolation of the crossings; (width, censored)."""
    if s.max() <= 0:
        return np.nan, False
    j = int(np.argmax(s)); half = s[j] / 2.0
    L = j
    while L > 0 and s[L] > half:
        L -= 1
    R = j
    while R < len(s) - 1 and s[R] > half:
        R += 1
    cens = s[L] > half or s[R] > half
    xl = L + (half - s[L]) / (s[L + 1] - s[L]) if (s[L] <= half and s[L + 1] != s[L]) else float(L)
    xr = R - (half - s[R]) / (s[R - 1] - s[R]) if (s[R] <= half and s[R - 1] != s[R]) else float(R)
    return float(xr - xl), bool(cens)


# ====================================================================================================== main
def main():
    say("decimation pipeline, %s" % time.strftime("%Y-%m-%d %H:%M:%S"))
    say("source: %s" % SRC)
    C = load_curves()
    prot = np.array([c["prot"] for c in C])
    prots = sorted(set(prot))
    n = len(C)
    say("curves: %d, proteins (folds): %d, preparations: %d" % (n, len(prots), len({c["sample"] for c in C})))
    assert n == 108 and len({c["key"] for c in C}) == 108 and len(prots) == 11
    last = collections.Counter(c["max_ce"] for c in C)
    say("last CE of the sweeps: %s  (every grid below is the curve's own, 1..max_ce)" % ", ".join("%d curves end at CE %d" % (v, k) for k, v in sorted(last.items())))
    PTS = {d: int(np.median([int(acquired(c, d, 0).sum()) for c in C])) for d in DS}     # acquired points per sweep, median over curves

    # ------------------------------------------------------------------ gates on the inputs
    say("\nGATES")
    worst_read, worst_reg, nan_read = 0.0, 0.0, 0
    for c in C:
        mine = np.full(len(c["ce"]), np.nan)
        for i in range(len(c["ce"])):
            m = (np.abs(c["ce"] - c["ce"][i]) <= HALF) & np.isfinite(c["raw"])
            if m.any():
                mine[i] = c["raw"][m].mean()
        both = np.isfinite(mine) & np.isfinite(c["read"])
        assert np.array_equal(np.isfinite(mine), np.isfinite(c["read"])), c["name"]
        worst_read = max(worst_read, float(np.abs(mine[both] - c["read"][both]).max()))
        both = np.isfinite(c["regret"]) & np.isfinite(c["col36"])
        assert np.array_equal(np.isfinite(c["regret"]), np.isfinite(c["col36"])), c["name"]
        worst_reg = max(worst_reg, float(np.abs(c["regret"][both] - c["col36"][both]).max()))
        nan_read += int((~np.isfinite(c["read"])).sum())
    gate("G1", worst_read <= 1e-9 and worst_reg <= 2e-6,
         "reading = +-3 NCE mean of column 34 vs column 35: worst |diff| %.1e; Regret vs column 36: worst |diff| %.1e (6 s.f. table); "
         "cells without a reading: %d of %d" % (worst_read, worst_reg, nan_read, 50 * n))

    worst_feed = 0.0
    for c in C:
        mine = feed_local(c["ce"].astype(float), c["share"], 3.0)
        worst_feed = max(worst_feed, float(np.abs(mine - c["feed_full"]).max()))
    gate("G2", worst_feed < 1e-6, "feed refit at d = 1 vs max(0, -column 14): worst |diff| %.2e (the table's 6 s.f. floor is 3.7e-7)" % worst_feed)

    # k2: per curve, then the folds; direct_many must agree with direct
    k2_own = np.array([fit_ext(c["feed_full"], c["Y"], c["max_ce"])[0] for c in C])
    fold_k2 = {p: float(np.exp(np.median(np.log(k2_own[prot != p])))) for p in prots}
    k2_all = float(np.exp(np.median(np.log(k2_own))))
    dm = direct_many(C[0]["feed_full"], [0.01, 0.0363, 0.5], 50)
    assert all(np.allclose(dm[i], direct(C[0]["feed_full"], k, 50), rtol=0, atol=1e-15) for i, k in enumerate((0.01, 0.0363, 0.5)))
    gate("G3", abs(k2_all - DECK["k2_all"]) < 5e-5 and abs(min(fold_k2.values()) - DECK["k2_fold_min"]) < 5e-5
         and abs(max(fold_k2.values()) - DECK["k2_fold_max"]) < 5e-5,
         "k2 over all 108: %.6f (deck 0.0363); folds %.6f - %.6f (deck 0.0304 / 0.0426); railed at %.0e: %d of 108"
         % (k2_all, min(fold_k2.values()), max(fold_k2.values()), K2_LO, int((k2_own <= K2_LO * 1.0001).sum())))
    K2 = np.array([fold_k2[p] for p in prot])

    # why column 11 and not column 6: the same chain fed by the remaining ratio's slope (column 9), with its own folds
    ratio_feed = [np.where(np.isfinite(c["col9"]), np.maximum(0.0, -c["col9"]), 0.0) for c in C]
    k2_ratio = np.array([fit_ext(ratio_feed[i], c["Y"], c["max_ce"])[0] for i, c in enumerate(C)])
    fold_ratio = {p: float(np.exp(np.median(np.log(k2_ratio[prot != p])))) for p in prots}
    RATIO = dict(k2_all=float(np.exp(np.median(np.log(k2_ratio)))),
                 regret_own=float(np.mean([score(ratio_feed[i], c, fold_ratio[c["prot"]])["regret"] for i, c in enumerate(C)])),
                 regret_deck=float(np.mean([score(ratio_feed[i], c, K2[i])["regret"] for i, c in enumerate(C)])))
    say("  (the remaining ratio, column 6, as the feed instead: mean Regret %.4f with its own k2 %.4f, %.4f with the deck's)"
        % (RATIO["regret_own"], RATIO["k2_all"], RATIO["regret_deck"]))

    # the fixed-CE comparator, build_deck.py:158-164
    Rm = np.array([[c["regret"][min(ce, c["max_ce"]) - 1] for ce in range(1, 51)] for c in C])
    assert np.isfinite(Rm).all()
    fold_ce = {p: min(range(1, 51), key=lambda e: (float(Rm[prot != p, e - 1].mean()), e)) for p in prots}
    regf = np.array([C[i]["regret"][min(fold_ce[prot[i]], C[i]["max_ce"]) - 1] for i in range(n)])
    FIXED = float(regf.mean())
    fixed_all = min(range(1, 51), key=lambda e: (float(Rm[:, e - 1].mean()), e))
    gate("G5", abs(FIXED - DECK["fixed_ce_mean"]) < 1e-3 and fixed_all == DECK["fixed_all_ce"]
         and (min(fold_ce.values()), max(fold_ce.values())) == DECK["fold_ce_range"],
         "fixed CE leave-one-protein-out: mean Regret %.6f (deck 0.4036 +- 1e-3); all-curve CE %d (deck 32); fold CEs %d - %d (deck 31 - 36)"
         % (FIXED, fixed_all, min(fold_ce.values()), max(fold_ce.values())))

    # ------------------------------------------------------------------ the decimation, every estimator, every offset
    say("\nfeeds and scores over %d spacings, %d sweeps per curve ..." % (len(DS), sum(DS)))
    t = time.time()
    per = {}        # (est, d) -> dict of per-curve arrays: R_mean (phase mean), disp_mean, out3_mean, R_off0, and per-offset means
    fails = {}      # (est, d) -> fits without enough points (over curves x offsets)
    grid_pick = {}  # d -> per-curve phase mean of the grid-restricted Regret and of the floor (local slope)
    EX = {}         # the example sweeps for the figures
    ex_i = next(i for i, c in enumerate(C) if c["prot"] == "Cytc" and c["key"][1] == "8" and c["key"][2] == "1")
    for d in DS:
        acc = {est: dict(R=np.zeros((n, d)), disp=np.zeros((n, d)), out3=np.zeros((n, d)), pick=np.zeros((n, d), int)) for est in ESTS}
        gr = dict(R=np.zeros((n, d)), floor=np.zeros((n, d)))
        nf = collections.Counter()
        for i, c in enumerate(C):
            for off in range(d):
                feeds, pars, sel = feeds_on_unit_grid(c, d, off)
                assert sel[1 + off - 1] and sel[c["max_ce"] - 1]
                for est in ESTS:
                    sc = score(feeds[est], c, K2[i])
                    acc[est]["R"][i, off], acc[est]["disp"][i, off] = sc["regret"], sc["disp"]
                    acc[est]["out3"][i, off], acc[est]["pick"][i, off] = sc["outside3"], sc["pick"]
                    if est != "local" and pars[est] is None:
                        nf[est] += 1
                # the grid-restricted maximum of Q6 option 1, and the floor of the acquired grid
                ce_a = c["ce"][sel].astype(float)
                s_a = feed_local(ce_a, c["share"][sel], 3.0 if d == 1 else float(d))
                B = chain_steps(s_a, K2[i], np.concatenate([[float(d)], np.diff(ce_a)]))
                gr["R"][i, off] = c["regret"][int(ce_a[int(np.argmax(B))]) - 1]
                gr["floor"][i, off] = 1.0 - float(np.nanmax(c["read"][sel])) / float(np.nanmax(c["read"]))
                if i == ex_i and d == 10 and off in (0, 5):
                    EX[off] = dict(acquired=c["ce"][sel].tolist(), feeds={e: feeds[e].tolist() for e in ESTS},
                                   picks={e: int(acc[e]["pick"][i, off]) for e in ESTS},
                                   regrets={e: float(acc[e]["R"][i, off]) for e in ESTS},
                                   lg3=pars["lg3"], lg4=pars["lg4"])
        for est in ESTS:
            a = acc[est]
            per[(est, d)] = dict(R_mean=a["R"].mean(1), disp_mean=np.abs(a["disp"]).mean(1), out3_mean=a["out3"].mean(1),
                                 R_off0=a["R"][:, 0], disp_off0=a["disp"][:, 0], by_offset=a["R"].mean(0), picks=a["pick"])
            fails[(est, d)] = int(nf[est])
        grid_pick[d] = dict(R_mean=gr["R"].mean(1), floor_mean=gr["floor"].mean(1), R_off0=gr["R"][:, 0])
        say("  d = %2d: %d offsets, %.0f s" % (d, d, time.time() - t))
    gate("G6", all(True for _ in DS), "every acquired set holds CE 1 + offset and CE 50 (asserted per sweep); offsets per d: %s" % ",".join(str(d) for d in DS))
    assert np.array_equal(per[("local", 1)]["R_mean"], per[("local", 1)]["R_off0"])
    R1 = per[("local", 1)]["R_mean"]
    gate("G4", abs(R1.mean() - DECK["regret_mean"]) < 1e-6 and abs(np.median(R1) - DECK["regret_median"]) < 1e-6
         and np.array_equal(grid_pick[1]["R_mean"], R1) and not np.isnan(R1).any(),
         "local slope at d = 1: mean Regret %.6f (deck 0.137418), median %.6f (deck 0.063363); the interpolated and the grid-restricted "
         "maxima coincide on every curve" % (R1.mean(), np.median(R1)))
    gate("G7", all(fails[("lg3", d)] == 0 for d in DS if d <= 12),
         "3-parameter logistic fits without 3 usable points at d <= 12: %d; at d = 15 / 20 / 25: %d / %d / %d of %d / %d / %d sweeps"
         % (sum(fails[("lg3", d)] for d in DS if d <= 12), fails[("lg3", 15)], fails[("lg3", 20)], fails[("lg3", 25)], 15 * n, 20 * n, 25 * n))

    # ------------------------------------------------------------------ the cluster bootstrap over the 11 proteins
    rng = np.random.default_rng(SEED)
    draws = rng.integers(0, len(prots), size=(NBOOT, len(prots)))
    counts = np.zeros((NBOOT, len(prots)))
    for b in range(NBOOT):
        counts[b] = np.bincount(draws[b], minlength=len(prots))
    pidx = np.array([prots.index(p) for p in prot])
    W = counts[:, pidx]                                   # NBOOT x 108 curve weights
    W = W / W.sum(1, keepdims=True)

    def boot(v):
        m = W @ v
        return float(v.mean()), float(np.percentile(m, 2.5)), float(np.percentile(m, 97.5))

    HALF_ADV = (float(R1.mean()) + FIXED) / 2.0
    stat = {}
    for est in ESTS:
        for d in DS:
            p_ = per[(est, d)]
            m, lo, hi = boot(p_["R_mean"])
            dm_, dlo, dhi = boot(p_["R_mean"] - per[(est, 1)]["R_mean"])
            om, olo, ohi = boot(p_["out3_mean"])
            stat[(est, d)] = dict(R=m, lo=lo, hi=hi, R0=float(p_["R_off0"].mean()), rng_lo=float(p_["by_offset"].min()),
                                  rng_hi=float(p_["by_offset"].max()), dR=dm_, dlo=dlo, dhi=dhi, out3=om, out3lo=olo, out3hi=ohi,
                                  disp_med=float(np.median(p_["disp_mean"])), disp_med_off0=float(np.median(np.abs(p_["disp_off0"]))),
                                  ge5_off0=int((np.abs(p_["disp_off0"]) >= 5).sum()), fails=fails[(est, d)], sweeps=d * n)

    def first(est, pred):
        return next((d for d in DS if pred(stat[(est, d)])), None)

    crit = {}
    for est in ESTS:
        inc = [(stat[(est, DS[k + 1])]["R"] - stat[(est, DS[k])]["R"]) / (DS[k + 1] - DS[k]) for k in range(len(DS) - 1)]
        kk = int(np.argmax(inc))
        crit[est] = dict(onset=first(est, lambda s: s["dlo"] > 0), majority_outside3=first(est, lambda s: s["out3"] > 0.5),
                         failure=first(est, lambda s: s["R"] > HALF_ADV), no_better_than_fixed=first(est, lambda s: s["R"] > FIXED),
                         knee=(DS[kk], DS[kk + 1]), knee_slope=float(inc[kk]))

    # the feed's width
    widths = np.array([fwhm(c["feed_full"]) for c in C], dtype=object)
    fw = np.array([w for w, _ in widths if np.isfinite(w)])
    fw_cens = int(sum(1 for w, cen in widths if np.isfinite(w) and cen))
    NYQ = float(np.median(fw) / 2.0)

    # ------------------------------------------------------------------ the (d, N) grid, local slope, offset 0
    say("\nthe (d, N) grid ...")
    REG, FER = {}, {}
    for d in DS_N:
        for N in NS:
            hw = d * (N - 1) / 2.0
            r, e = np.zeros(n), np.zeros(n)
            for i, c in enumerate(C):
                sel = acquired(c, d, 0)
                ce_a = c["ce"][sel].astype(float)
                s_u = np.interp(c["ce"].astype(float), ce_a, feed_local(ce_a, c["share"][sel], hw))
                r[i] = score(s_u, c, K2[i])["regret"]
                pk = c["feed_full"].max()
                e[i] = float(np.median(np.abs(s_u - c["feed_full"])) / pk) if pk > 0 else np.nan
            REG[(d, N)], FER[(d, N)] = r, e
    CONV = {d: (7 if d == 1 else 3) for d in DS_N}
    ngrid = {}
    for d in DS_N:
        fid = min(NS, key=lambda N: np.nanmedian(FER[(d, N)]))
        best = min(NS, key=lambda N: REG[(d, N)].mean())
        r_lopo, chosen = np.empty(n), set()
        for p in prots:
            tr, te = prot != p, prot == p
            Np = min(NS, key=lambda N: REG[(d, N)][tr].mean())
            chosen.add(Np)
            r_lopo[te] = REG[(d, Np)][te]
        ngrid[d] = dict(grid={N: float(REG[(d, N)].mean()) for N in NS}, conv_N=CONV[d], conv=float(REG[(d, CONV[d])].mean()),
                        fid_N=fid, fid=float(REG[(d, fid)].mean()), lopo=float(r_lopo.mean()), lopo_Ns=sorted(chosen),
                        best_N=best, best=float(REG[(d, best)].mean()))
    g9 = abs(ngrid[1]["conv"] - float(R1.mean())) < 1e-12 and all(abs(ngrid[d]["conv"] - stat[("local", d)]["R0"]) < 1e-12 for d in DS_N if d >= 2)
    gate("G9", g9, "the (d, N) grid at (1, 7) equals the deck's value and at (d >= 2, N = 3) equals the local slope's offset-0 headline, exactly")

    # ------------------------------------------------------------------ appendix: k2 refitted on the decimated data
    say("\nk2 refitted on decimated data (appendix) ...")
    k2s = {}
    for d in DS:
        vals = np.empty(n)
        for i, c in enumerate(C):
            sel = acquired(c, d, 0)
            ce_a = c["ce"][sel].astype(float)
            s_u = np.interp(c["ce"].astype(float), ce_a, feed_local(ce_a, c["share"][sel], 3.0 if d == 1 else float(d)))
            Yd = np.full(c["max_ce"] + 1, np.nan); Yd[1:][sel] = c["read"][sel]   # a sparse training corpus reads only what it acquired
            vals[i] = fit_ext(s_u, Yd, c["max_ce"])[0]
        fk = {p: float(np.exp(np.median(np.log(vals[prot != p])))) for p in prots}
        k2s[d] = dict(median=float(np.exp(np.median(np.log(vals)))), fold_min=min(fk.values()), fold_max=max(fk.values()),
                      railed=int((vals <= K2_LO * 1.0001).sum()), q25=float(np.exp(np.percentile(np.log(vals), 25))),
                      q75=float(np.exp(np.percentile(np.log(vals), 75))))
    assert abs(k2s[1]["median"] - k2_all) < 1e-12

    # ------------------------------------------------------------------ all gates must hold before anything is written
    say("\n%d gates, %d passed, %.0f s" % (len(GATES), sum(ok for _, ok, _ in GATES), time.time() - T0))
    failed = [g for g in GATES if not g[1]]
    if failed:
        say("FAILED: " + ", ".join(g[0] for g in failed))
        with io.open(LOG, "w", encoding="utf-8", newline="\n") as fh:
            fh.write("\n".join(_log_lines) + "\n")
        raise SystemExit("gate failure: nothing written")

    # ------------------------------------------------------------------ tables
    os.makedirs(TABLES, exist_ok=True)

    def tsv(name, header, rows):
        with io.open(os.path.join(TABLES, name), "w", encoding="utf-8", newline="\n") as fh:
            fh.write("\t".join(header) + "\n")
            for r in rows:
                fh.write("\t".join(r) + "\n")

    fmt = lambda v: ("%.6f" % v) if isinstance(v, float) else str(v)
    tsv("headline.tsv", ["estimator", "d", "points", "regret_phase_mean", "ci_lo", "ci_hi", "regret_offset0", "range_lo", "range_hi",
                         "diff_vs_d1", "diff_lo", "diff_hi", "outside3_share", "outside3_lo", "outside3_hi", "disp_median_phase_mean",
                         "disp_median_offset0", "ge5_offset0", "fits_without_points", "sweeps"],
        [[est, str(d), str(PTS[d]), fmt(s["R"]), fmt(s["lo"]), fmt(s["hi"]), fmt(s["R0"]), fmt(s["rng_lo"]), fmt(s["rng_hi"]),
          fmt(s["dR"]), fmt(s["dlo"]), fmt(s["dhi"]), fmt(s["out3"]), fmt(s["out3lo"]), fmt(s["out3hi"]), fmt(s["disp_med"]),
          fmt(s["disp_med_off0"]), str(s["ge5_off0"]), str(s["fails"]), str(s["sweeps"])]
         for est in ESTS for d in DS for s in [stat[(est, d)]]])
    ranked = sorted(((est, d) for est in ESTS for d in DS), key=lambda k: stat[k]["R"])
    tsv("ranked.tsv", ["rank", "estimator", "d", "points", "regret_phase_mean", "ci_lo", "ci_hi", "regret_offset0", "outside3_share", "fits_without_points"],
        [[str(i + 1), est, str(d), str(PTS[d]), fmt(s["R"]), fmt(s["lo"]), fmt(s["hi"]), fmt(s["R0"]), fmt(s["out3"]), str(s["fails"])]
         for i, (est, d) in enumerate(ranked) for s in [stat[(est, d)]]])
    tsv("criteria.tsv", ["estimator", "onset_d", "majority_outside3_d", "failure_d", "no_better_than_fixed_d", "knee_from", "knee_to", "knee_slope"],
        [[est, str(crit[est]["onset"]), str(crit[est]["majority_outside3"]), str(crit[est]["failure"]), str(crit[est]["no_better_than_fixed"]),
          str(crit[est]["knee"][0]), str(crit[est]["knee"][1]), fmt(crit[est]["knee_slope"])] for est in ESTS])
    tsv("grid_restricted.tsv", ["d", "regret_grid_phase_mean", "regret_grid_offset0", "floor_phase_mean", "regret_interp_phase_mean"],
        [[str(d), fmt(float(grid_pick[d]["R_mean"].mean())), fmt(float(grid_pick[d]["R_off0"].mean())), fmt(float(grid_pick[d]["floor_mean"].mean())),
          fmt(stat[("local", d)]["R"])] for d in DS])
    tsv("n_grid.tsv", ["d"] + ["regret_N%d" % N for N in NS] + ["conv_N", "conv", "fid_N", "fid", "lopo", "lopo_Ns", "best_N", "best"],
        [[str(d)] + [fmt(ngrid[d]["grid"][N]) for N in NS] + [str(ngrid[d]["conv_N"]), fmt(ngrid[d]["conv"]), str(ngrid[d]["fid_N"]), fmt(ngrid[d]["fid"]),
                                                             fmt(ngrid[d]["lopo"]), "/".join(str(v) for v in ngrid[d]["lopo_Ns"]), str(ngrid[d]["best_N"]), fmt(ngrid[d]["best"])]
         for d in DS_N])
    tsv("phase_range.tsv", ["estimator", "d", "offset", "regret_mean_over_curves"],
        [[est, str(d), str(off), fmt(float(per[(est, d)]["by_offset"][off]))] for est in ESTS for d in DS for off in range(d)])
    tsv("feed_width.tsv", ["curve", "protein", "fwhm_nce", "censored_by_sweep_edge"],
        [[c["name"], c["prot"], fmt(float(w)) if np.isfinite(w) else "", "1" if cen else "0"] for c, (w, cen) in zip(C, widths)])
    tsv("k2_sparse.tsv", ["d", "k2_median", "k2_q25", "k2_q75", "fold_min", "fold_max", "railed"],
        [[str(d), fmt(k2s[d]["median"]), fmt(k2s[d]["q25"]), fmt(k2s[d]["q75"]), fmt(k2s[d]["fold_min"]), fmt(k2s[d]["fold_max"]), str(k2s[d]["railed"])] for d in DS])
    tsv("k2_folds.tsv", ["protein", "fold_k2", "fold_fixed_ce", "curves"],
        [[p, fmt(fold_k2[p]), str(fold_ce[p]), str(int((prot == p).sum()))] for p in prots])

    # ------------------------------------------------------------------ results.json (arrays for the figures)
    ex = C[ex_i]
    results = dict(
        ds=list(DS), ds_n=list(DS_N), ns=list(NS), ests=list(ESTS), est_name=EST_NAME, fixed_ce=FIXED, half_advantage=HALF_ADV,
        stat={"%s|%d" % (est, d): stat[(est, d)] for est in ESTS for d in DS}, crit=crit,
        grid_pick={str(d): dict(R=float(grid_pick[d]["R_mean"].mean()), R0=float(grid_pick[d]["R_off0"].mean()), floor=float(grid_pick[d]["floor_mean"].mean())) for d in DS},
        ngrid={str(d): ngrid[d] for d in DS_N}, k2_sparse={str(d): k2s[d] for d in DS}, fwhm=fw.tolist(), fwhm_censored=fw_cens,
        k2_all=k2_all, fold_k2=fold_k2, fold_ce=fold_ce,
        example=dict(name=ex["name"], ce=ex["ce"].tolist(), share=[None if not np.isfinite(v) else float(v) for v in ex["share"]],
                     read=[None if not np.isfinite(v) else float(v) for v in ex["read"]], feed_full=ex["feed_full"].tolist(),
                     true_max=int(np.nanargmax(ex["read"])) + 1, sweeps={str(k): v for k, v in EX.items()}),
        per_offset_local={str(d): per[("local", d)]["by_offset"].tolist() for d in DS},
        gates=[dict(name=g[0], passed=g[1], detail=g[2]) for g in GATES])
    with io.open(RESULTS, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(results, fh, indent=1)

    # ------------------------------------------------------------------ numbers.json (every scalar the README cites)
    N_ = {}
    N_["n_curves"], N_["n_proteins"], N_["n_preparations"] = n, len(prots), len({c["sample"] for c in C})
    N_["k2_all"], N_["k2_fold_min"], N_["k2_fold_max"] = k2_all, min(fold_k2.values()), max(fold_k2.values())
    N_["regret_d1_mean"], N_["regret_d1_median"] = float(R1.mean()), float(np.median(R1))
    N_["fixed_ce"], N_["fixed_all_ce"], N_["fold_ce_min"], N_["fold_ce_max"] = FIXED, fixed_all, min(fold_ce.values()), max(fold_ce.values())
    N_["half_advantage"] = HALF_ADV
    N_["fwhm_median"], N_["fwhm_q25"], N_["fwhm_q75"], N_["fwhm_censored"], N_["nyquist_d"] = float(np.median(fw)), float(np.percentile(fw, 25)), float(np.percentile(fw, 75)), fw_cens, NYQ
    N_["nboot"], N_["seed"] = NBOOT, SEED
    for est in ESTS:
        for k, v in crit[est].items():
            N_["%s_%s" % (k, est)] = v if not isinstance(v, tuple) else "%d and %d" % v
        for d in DS:
            s = stat[(est, d)]
            for k in ("R", "lo", "hi", "R0", "rng_lo", "rng_hi", "dR", "dlo", "dhi", "out3", "disp_med", "fails", "sweeps"):
                N_["%s_%s_d%d" % (k, est, d)] = s[k]
    for d in DS:
        N_["points_d%d" % d] = PTS[d]
        N_["grid_R_d%d" % d], N_["grid_floor_d%d" % d] = float(grid_pick[d]["R_mean"].mean()), float(grid_pick[d]["floor_mean"].mean())
        N_["range_local_d%d" % d] = stat[("local", d)]["rng_hi"] - stat[("local", d)]["rng_lo"]
        N_["k2sparse_median_d%d" % d], N_["k2sparse_railed_d%d" % d] = k2s[d]["median"], k2s[d]["railed"]
        N_["k2sparse_ratio_d%d" % d] = k2s[d]["median"] / k2_all
        N_["k2sparse_fold_min_d%d" % d], N_["k2sparse_fold_max_d%d" % d] = k2s[d]["fold_min"], k2s[d]["fold_max"]
    for d in DS_N:
        for k in ("conv", "fid", "lopo", "best"):
            N_["ngrid_%s_d%d" % (k, d)] = ngrid[d][k]
        N_["ngrid_best_N_d%d" % d], N_["ngrid_conv_N_d%d" % d], N_["ngrid_fid_N_d%d" % d] = ngrid[d]["best_N"], ngrid[d]["conv_N"], ngrid[d]["fid_N"]
        for Nn in NS:
            N_["ngrid_N%d_d%d" % (Nn, d)] = ngrid[d]["grid"][Nn]
    N_["ngrid_lopo_worse_count"] = sum(1 for d in DS_N if ngrid[d]["lopo"] > ngrid[d]["conv"] + 1e-12)
    N_["ngrid_fid_equals_conv"] = all(ngrid[d]["fid_N"] == ngrid[d]["conv_N"] for d in DS_N)
    N_["example_name"], N_["example_true_max"] = ex["name"], int(np.nanargmax(ex["read"])) + 1
    for off in (0, 5):
        e = EX[off]
        N_["ex_off%d_acquired" % off] = ", ".join(str(v) for v in e["acquired"])
        for est in ESTS:
            N_["ex_off%d_pick_%s" % (off, est)], N_["ex_off%d_regret_%s" % (off, est)] = e["picks"][est], e["regrets"][est]
        if e["lg3"] is not None:
            N_["ex_off%d_lg3_A" % off], N_["ex_off%d_lg3_c50" % off], N_["ex_off%d_lg3_w" % off] = e["lg3"]
    N_["lg3_fails_d15"], N_["lg3_fails_d20"], N_["lg3_fails_d25"] = fails[("lg3", 15)], fails[("lg3", 20)], fails[("lg3", 25)]
    N_["lg4_fails_d12"], N_["lg4_fails_d15"], N_["lg4_fails_d20"] = fails[("lg4", 12)], fails[("lg4", 15)], fails[("lg4", 20)]
    # crossovers: the first d at which each logistic beats the local slope on the phase mean
    for est in ("lg3", "lg4"):
        N_["crossover_%s" % est] = next((d for d in DS if stat[(est, d)]["R"] < stat[("local", d)]["R"]), None)
        N_["best_of_three_range_%s" % est] = ", ".join(str(d) for d in DS if all(stat[(est, d)]["R"] <= stat[(o, d)]["R"] for o in ESTS))
    N_["ratio_regret_own_k2"], N_["ratio_regret_deck_k2"], N_["ratio_k2_all"] = RATIO["regret_own"], RATIO["regret_deck"], RATIO["k2_all"]
    N_["gates_passed"], N_["gates_total"] = sum(ok for _, ok, _ in GATES), len(GATES)
    N_["worst_read_diff"], N_["worst_regret_diff"], N_["worst_feed_diff"] = worst_read, worst_reg, worst_feed
    N_["built"] = time.strftime("%Y-%m-%d")
    with io.open(NUMBERS, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(N_, fh, indent=1, sort_keys=True)

    # ------------------------------------------------------------------ the summary on screen
    say("\nheadline (phase mean over all starting points; offset 0 in brackets)")
    say("  d  pts   " + "".join("%22s" % est for est in ESTS) + "     grid-restricted   floor")
    for d in DS:
        say("%3d  %3d   " % (d, N_["points_d%d" % d]) + "".join("%10.4f [%.4f]   " % (stat[(est, d)]["R"], stat[(est, d)]["R0"]) for est in ESTS)
            + "   %.4f          %.4f" % (N_["grid_R_d%d" % d], N_["grid_floor_d%d" % d]))
    say("\ncriteria (phase mean)")
    for est in ESTS:
        c_ = crit[est]
        say("  %-38s onset d = %-4s majority outside +-3 d = %-4s failure d = %-4s no better than fixed d = %-4s knee %s-%s"
            % (EST_NAME[est], c_["onset"], c_["majority_outside3"], c_["failure"], c_["no_better_than_fixed"], c_["knee"][0], c_["knee"][1]))
    say("  feed width at half maximum: median %.1f NCE (quartiles %.1f-%.1f, %d censored) -> d <= %.1f" % (np.median(fw), np.percentile(fw, 25), np.percentile(fw, 75), fw_cens, NYQ))
    say("  fixed CE %.6f, half-advantage line %.6f" % (FIXED, HALF_ADV))
    say("\nwrote %s, %s, %s/*.tsv, %s   (%.0f s)" % (os.path.relpath(RESULTS, HERE), os.path.relpath(NUMBERS, HERE), os.path.relpath(TABLES, HERE), os.path.relpath(LOG, HERE), time.time() - T0))
    with io.open(LOG, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(_log_lines) + "\n")


if __name__ == "__main__":
    main()

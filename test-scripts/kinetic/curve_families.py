# -*- coding: utf-8 -*-
"""Is any three-parameter survival shape better suited than the logistic as the chain's feed?

Every family is P(CE) = A g(CE; p1, p2) with g falling from 1 to 0, fitted to the acquired shares by least squares in share
units (A in closed form on a (p1, p2) grid, then a finer grid around the best), the feed = -dP/dCE analytic. Scored on the
study's pipeline (pipeline.py: all spacings, all starting points, the deck's fixed k2, pinned Regret). Families:

    logistic        g = 1 / (1 + e^u)               u = (x - c) / w     symmetric feed, exponential tail in the share
    erfc (probit)   g = erfc(u / sqrt 2) / 2                            symmetric feed, Gaussian tail: thresholds normally distributed
    Gompertz        g = exp(-e^u)                                       feed skewed: slow onset, sharp end
    Gumbel tail     g = 1 - exp(-e^{-u})                                feed skewed the other way: sharp onset, long tail
    Weibull         g = exp(-(x / lam)^k)                                power law in log share: a plateau, then an accelerating fall
    log-logistic    g = 1 / (1 + (x / c)^k)                              logistic in log CE: long high-CE tail

    C:/FLASHIda/Measurements/qc/.venv/Scripts/python.exe curve_families.py
"""
import io, json, os, time
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import pipeline as P

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_TSV = os.path.join(HERE, "tables", "curve_families.tsv")
OUT_PNG = os.path.join(HERE, "figures", "x1_curve_families.png")
DS = P.DS
SQ2, SQ2PI = np.sqrt(2.0), np.sqrt(2.0 * np.pi)


def erfc(z):
    """Abramowitz & Stegun 7.1.26, |error| < 1.5e-7, vectorized; enough for a fit whose data carry 6 figures."""
    s = np.sign(z); a = np.abs(z)
    t = 1.0 / (1.0 + 0.3275911 * a)
    y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * np.exp(-a * a)
    return 1.0 - s * y          # erfc = 1 - erf


def U(x, c, w):
    return np.clip((x[None, :] - c[:, None]) / w[:, None], -60, 60)


FAMILIES = {
    # name: (g(x, p1, p2), -dg/dx(x, p1, p2), grid p1, grid p2, refine p1 additive half-range, refine p2 multiplicative)
    "logistic": (lambda x, c, w: 1.0 / (1.0 + np.exp(U(x, c, w))),
                 lambda x, c, w: (lambda e: e / (w[:, None] * (1.0 + e) ** 2))(np.exp(U(x, c, w))),
                 np.arange(0.0, 60.01, 0.5), np.geomspace(0.25, 25.0, 41), 0.5, 1.3),
    "erfc": (lambda x, c, w: 0.5 * erfc(U(x, c, w) / SQ2),
             lambda x, c, w: np.exp(-0.5 * U(x, c, w) ** 2) / (w[:, None] * SQ2PI),
             np.arange(0.0, 60.01, 0.5), np.geomspace(0.25, 25.0, 41), 0.5, 1.3),
    "Gompertz": (lambda x, c, w: np.exp(-np.exp(U(x, c, w))),
                 lambda x, c, w: (lambda e: e * np.exp(-e) / w[:, None])(np.exp(U(x, c, w))),
                 np.arange(0.0, 60.01, 0.5), np.geomspace(0.25, 25.0, 41), 0.5, 1.3),
    "Gumbel tail": (lambda x, c, w: 1.0 - np.exp(-np.exp(-U(x, c, w))),
                    lambda x, c, w: (lambda e: e * np.exp(-e) / w[:, None])(np.exp(-U(x, c, w))),
                    np.arange(0.0, 60.01, 0.5), np.geomspace(0.25, 25.0, 41), 0.5, 1.3),
    "Weibull": (lambda x, lam, k: np.exp(-(x[None, :] / lam[:, None]) ** k[:, None]),
                lambda x, lam, k: (lambda r: (k[:, None] / lam[:, None]) * r ** (k[:, None] - 1.0) * np.exp(-r ** k[:, None]))(x[None, :] / lam[:, None]),
                np.arange(1.0, 80.01, 0.5), np.geomspace(1.0, 60.0, 41), 0.5, 1.3),
    "log-logistic": (lambda x, c, k: 1.0 / (1.0 + (x[None, :] / c[:, None]) ** k[:, None]),
                     lambda x, c, k: (lambda r: (k[:, None] / c[:, None]) * r ** (k[:, None] - 1.0) / (1.0 + r ** k[:, None]) ** 2)(x[None, :] / c[:, None]),
                     np.arange(1.0, 80.01, 0.5), np.geomspace(1.0, 60.0, 41), 0.5, 1.3),
}


PLATEAU_FACTOR = 1.1     # the bounded fit: the plateau A may exceed the highest acquired share by this factor at most


def plateau_bound(p):
    """The bound on A for a sweep's acquired shares: a share cannot exceed the TIC, and the plateau cannot lie far above the
    highest share actually seen. Removes the fits that run to a plateau above 1 placed before the sweep."""
    return PLATEAU_FACTOR * float(np.nanmax(p))


def fit(name, x, p, A_max=None):
    """Least squares in share units on a (p1, p2) grid, A in closed form; A clipped to [0, A_max] when a bound is given
    (None: the plateau is free, the behaviour of every result before the bound was measured)."""
    g_of, _, G1, G2, r1, r2 = FAMILIES[name]
    x, p = np.asarray(x, float), np.asarray(p, float)
    ok = np.isfinite(p)
    x, p = x[ok], p[ok]
    if len(x) < 3:
        return None

    def best_on(P1, P2):
        a, b = np.meshgrid(P1, P2, indexing="ij")
        a, b = a.ravel(), b.ravel()
        g = g_of(x, a, b)
        A = (g @ p) / np.maximum((g * g).sum(1), 1e-30)
        if A_max is not None:
            A = np.clip(A, 0.0, A_max)
        sse = ((p[None, :] - A[:, None] * g) ** 2).sum(1)
        j = int(np.argmin(sse))
        return float(A[j]), float(a[j]), float(b[j]), float(sse[j])
    A, p1, p2, _ = best_on(G1, G2)
    A, p1, p2, sse = best_on(np.linspace(max(p1 - r1, G1[0]), p1 + r1, 51), np.geomspace(max(p2 / r2, G2[0] / 2), p2 * r2, 41))
    return A, p1, p2, sse


def feed_of(name, par, xu):
    _, dg, _, _, _, _ = FAMILIES[name]
    A, p1, p2 = par[0], par[1], par[2]
    return np.maximum(0.0, A * dg(xu, np.array([p1]), np.array([p2]))[0])


def main():
    t0 = time.time()
    C = P.load_curves()
    prot = np.array([c["prot"] for c in C]); prots = sorted(set(prot)); n = len(C)
    k2_own = np.array([P.fit_ext(c["feed_full"], c["Y"], c["max_ce"])[0] for c in C])
    fold = {p: float(np.exp(np.median(np.log(k2_own[prot != p])))) for p in prots}
    K2 = np.array([fold[p] for p in prot])
    assert abs(float(np.exp(np.median(np.log(k2_own)))) - 0.0363) < 5e-5
    with io.open(os.path.join(HERE, "numbers.json"), encoding="utf-8") as fh:
        N = json.load(fh)
    HALF_ADV, FIXED = N["half_advantage"], N["fixed_ce"]
    with io.open(os.path.join(HERE, "results.json"), encoding="utf-8") as fh:
        R = json.load(fh)
    local = {d: R["stat"]["local|%d" % d]["R"] for d in DS}
    print("curves %d, k2 folds %.4f-%.4f, failure line %.4f" % (n, min(fold.values()), max(fold.values()), HALF_ADV))

    res = {}          # (family, d) -> phase-mean Regret over curves
    sse_full = {}     # family -> median over curves of the full-density residual, in share units, relative to A
    own_k2 = {}       # family -> (Regret at full density with the family's own LOPO k2, its k2 over all)
    ex_i = next(i for i, c in enumerate(C) if c["prot"] == "Cytc" and c["key"][1] == "8" and c["key"][2] == "1")
    EX = {}
    for name in FAMILIES:
        t = time.time()
        # full density: the fit quality, and the family's own k2
        feeds1, rel = [], []
        for c in C:
            xu = c["ce"].astype(float)
            par = fit(name, xu, c["share"])
            feeds1.append(feed_of(name, par, xu))
            rel.append(np.sqrt(par[3] / np.isfinite(c["share"]).sum()) / par[0])
        sse_full[name] = float(np.median(rel))
        k2f = np.array([P.fit_ext(feeds1[i], c["Y"], c["max_ce"])[0] for i, c in enumerate(C)])
        ff = {p: float(np.exp(np.median(np.log(k2f[prot != p])))) for p in prots}
        own_k2[name] = (float(np.mean([P.score(feeds1[i], c, ff[c["prot"]])["regret"] for i, c in enumerate(C)])), float(np.exp(np.median(np.log(k2f)))))
        for d in DS:
            acc = np.zeros((n, d))
            for i, c in enumerate(C):
                xu = c["ce"].astype(float)
                for off in range(d):
                    sel = P.acquired(c, d, off)
                    par = fit(name, xu[sel], c["share"][sel])
                    fu = np.zeros(len(xu)) if par is None else feed_of(name, par, xu)
                    acc[i, off] = P.score(fu, c, K2[i])["regret"]
                    if i == ex_i and d == 10 and off == 5:
                        EX[name] = dict(par=par, feed=fu.tolist(), pick=P.score(fu, c, K2[i])["pick"], regret=float(acc[i, off]))
            res[(name, d)] = float(acc.mean(1).mean())
        print("  %-13s full density %.4f (own k2 %.4f: %.4f)   d=6 %.4f  10 %.4f  15 %.4f  20 %.4f  25 %.4f   fit rms/A %.3f   %.0f s"
              % (name, res[(name, 1)], own_k2[name][1], own_k2[name][0], res[(name, 6)], res[(name, 10)], res[(name, 15)], res[(name, 20)], res[(name, 25)], sse_full[name], time.time() - t))

    # ---------------------------------------------------------------- table
    names = list(FAMILIES)
    with io.open(OUT_TSV, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("family\tfit_rms_over_A_full_density\tregret_full_density_own_k2\town_k2_all\t" + "\t".join("regret_d%d" % d for d in DS) + "\tfailure_d\tcrossover_vs_local_d\n")
        for name in names:
            fail = next((d for d in DS if res[(name, d)] > HALF_ADV), None)
            cross = next((d for d in DS if res[(name, d)] < local[d]), None)
            fh.write("%s\t%.6f\t%.6f\t%.6f\t" % (name, sse_full[name], own_k2[name][0], own_k2[name][1]) + "\t".join("%.6f" % res[(name, d)] for d in DS) + "\t%s\t%s\n" % (fail, cross))
    print("\nfamily          " + "".join("%8s" % ("d=%d" % d) for d in DS) + "   failure  beats local from")
    print("local slope     " + "".join("%8.3f" % local[d] for d in DS) + "   %-8s -" % N["failure_local"])
    for name in names:
        fail = next((d for d in DS if res[(name, d)] > HALF_ADV), None)
        cross = next((d for d in DS if res[(name, d)] < local[d]), None)
        print("%-15s " % name + "".join("%8.3f" % res[(name, d)] for d in DS) + "   %-8s %s" % (fail, cross))
    best = {d: min(names, key=lambda nm: res[(nm, d)]) for d in DS}
    print("\nbest family per spacing: " + ", ".join("%d: %s" % (d, best[d]) for d in DS))

    # ---------------------------------------------------------------- figure
    ex = C[ex_i]; xu = ex["ce"].astype(float)
    COL = {"logistic": "#B5442A", "erfc": "#2E7D8E", "Gompertz": "#7B3F8C", "Gumbel tail": "#C2762A", "Weibull": "#2E7D5B", "log-logistic": "#8A6D2F"}
    fig, ax = plt.subplots(1, 3, figsize=(16.5, 4.9))
    a = ax[0]
    sel = P.acquired(ex, 10, 5)
    a.semilogy(xu, np.where(ex["share"] > 0, ex["share"], np.nan), color="#9AA0A6", lw=1.2, marker="o", ms=3, label="precursor share, every scan")
    a.semilogy(xu[sel], np.where(ex["share"][sel] > 0, ex["share"][sel], np.nan), color="#222222", lw=0, marker="D", ms=9, mfc="none", mew=1.8, label="acquired: from CE 6, spacing 10")
    xx = np.linspace(1, ex["max_ce"], 400)
    for name in names:
        par = EX[name]["par"]
        g = FAMILIES[name][0](xx, np.array([par[1]]), np.array([par[2]]))[0]
        a.semilogy(xx, par[0] * g, color=COL[name], lw=1.5, label=name)
    a.set_ylim(1e-3, 3); a.set_xlabel("NCE"); a.set_ylabel("precursor share  (log scale)")
    a.legend(fontsize=7.6, frameon=False, loc="lower left")
    a.set_title("(a) %s: six survival shapes through the same six scans" % ex["name"], fontsize=10)
    a = ax[1]
    a.plot(xu, ex["feed_full"], color="#9AA0A6", lw=1.3, label="feed at full density (local slope)")
    for name in names:
        a.plot(xu, EX[name]["feed"], color=COL[name], lw=1.5, label="%s: maximum CE %d, Regret %.2f" % (name, EX[name]["pick"], EX[name]["regret"]))
    a.axvline(int(np.nanargmax(ex["read"])) + 1, color="#222222", lw=1.0, ls=":")
    a.set_xlabel("NCE"); a.set_ylabel("feed  (share lost per NCE)")
    a.legend(fontsize=7.4, frameon=False, loc="upper left")
    a.set_title("(b) the feeds they imply (dotted: the measured maximum)", fontsize=10)
    a = ax[2]
    a.plot(DS, [local[d] for d in DS], color="#1F3F66", lw=2.2, marker="o", ms=4.5, label="local slope (the deck's estimator)")
    for name in names:
        a.plot(DS, [res[(name, d)] for d in DS], color=COL[name], lw=1.5, marker="s", ms=3.6, label=name)
    a.axhline(FIXED, color="#C2762A", lw=1.0, ls="-."); a.text(DS[0], FIXED, "fixed CE %.3f" % FIXED, fontsize=8, color="#C2762A", va="bottom")
    a.axhline(HALF_ADV, color="#222222", lw=0.9, ls="--"); a.text(DS[0], HALF_ADV, "failure line %.3f" % HALF_ADV, fontsize=8, va="bottom")
    a.set_xticks(DS); a.set_ylim(0, 0.6)
    a.set_xlabel("CE spacing d  (NCE; both sweep edges always acquired)"); a.set_ylabel("mean Regret, phase mean over starting points")
    a.legend(fontsize=7.6, frameon=False, loc="upper left")
    a.set_title("(c) Regret against spacing, deck k2, all starting points", fontsize=10)
    for aa in ax:
        aa.grid(alpha=0.18, lw=0.6)
        for sp in ("top", "right"):
            aa.spines[sp].set_visible(False)
    fig.tight_layout(w_pad=2.0)
    fig.savefig(OUT_PNG, dpi=170)
    print("\nwrote %s and %s  (%.0f s)" % (os.path.relpath(OUT_TSV, HERE), os.path.relpath(OUT_PNG, HERE), time.time() - t0))


if __name__ == "__main__":
    main()

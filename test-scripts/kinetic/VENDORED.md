# Vendored reference pipeline (ADR-0047 decision 10)

`pipeline.py` and `curve_families.py` are **byte-for-byte copies** of

    C:/FLASHIda/Measurements/MS2_Characterization/analysis/matched-share-regret-prediction/decimation/{pipeline.py,curve_families.py}

at corpus commit `eed88d7b79f7944cee2ab35d0cf2e9b65eb00c63` (2026-09-17, "decimation: where the
early-reading explanation fails, with two more diagnostics"). Content SHA-256 (first 16 hex, LF content):
`pipeline.py` = `6afd51b4d6142f25`, `curve_families.py` = `b13c0d20c7b01a8a`.

They are the study's own math behind the progress report of 2026-09-30 ("Precursor Tuned
Fragmentation"): the Gompertz grid-search fit with the bounded plateau (`curve_families.fit`,
`plateau_bound`), the sparse-acquisition rule (`pipeline.acquired`: spacing d from CE 1 + offset,
the top edge always), the pool recursion (`pipeline.direct`), the k2 fitter (`pipeline.fit_ext`) and
the regret score (`pipeline.score`). Nothing in them is edited; `make_kinetic_tables.py` imports them.

**Why a copy.** The corpus analysis repository has no remote, so a reference that lives only there is
not reproducible from a clone of this workspace. This copy is the reference until the corpus repo is
public. A change to the corpus pipeline is carried here together with regenerated tables under
`test-data/kinetic/` and the expectations in `KineticModel_test` — or not at all.

**Dependencies.** `curve_families.py` imports matplotlib at module level (it also draws figures the
generator never calls), so the generator runs with both installed:

    uv run --with numpy --with matplotlib python -I FlashIDA/test-scripts/kinetic/make_kinetic_tables.py \
        "C:/FLASHIda/Measurements/MS2_Characterization/analysis/matched-share-regret-prediction/stabilized_HCD_corrected.tsv" \
        FlashIDA/test-data/kinetic

On this host `python`/`py` are Microsoft Store stubs; use `uv` as above.

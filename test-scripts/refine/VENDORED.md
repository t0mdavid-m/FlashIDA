# Vendored Refine prototype (ADR-0049)

`refine_core.py` is the owner's Refine prototype consolidated into one file: each source below is
copied **verbatim** under a banner naming its path, date and sha256, in this order. It is the
development oracle of `Refine_test` (OpenMS `src/tests/class_tests/openms/source/Refine_test.cpp`);
the suite never imports it.

Sources, under `C:/FLASHIda/fi_refine_publication_notes/evidence/` (dates are file dates; sha256 of
the file as it is there):

| Source | Date | Taken | sha256 |
|---|---|---|---|
| `truncation_scan/trunc_core.py` | 2026-09-21 | whole file | `a16d1ed9d2f5bb369d64d0d2f1c1369d2536c54b8ffe092bb0e3791b90af72f9` |
| `assemble_all_paths_f3/paths_core_f3.py` | 2026-09-21 | up to `class Scan` (what `detect_fdr.py` uses) | `91423d4228b519dcb5463e9880fb185aa6867bf4b6c06311e847af932cb1f496` |
| `lenient_cut/detect_fdr.py` | 2026-09-29 | whole file | `41ed9b8f3ba21c010d60d00be14aa3daa50ddd53d0bd2f24d6999415323b8925` |
| `lenient_cut/lenient_core.py` | 2026-09-29 | whole file | `62961d94f95148d4c150484b0cb86a1febb9ad48e28674666c7a4969ae31d0d1` |
| `lenient_cut/ms2_rules.py` | 2026-09-29 | up to `t0 = time.time()` (what `column_dp_v85.py` executes) | `cd5415a77e2eeb1c8271a8fcfd755162739d45ecb1f0b0f43fac12771f33df44` |
| `lenient_cut/column_dp_v85.py` | 2026-09-30 | whole file | `8c4034b06d589aabb4b7866ad6ab58dddf462707d144f3ce0d059140121056b2` |
| `lenient_cut/targets_v88.py` | 2026-09-30 | whole file | `6a493614e9fc8f752227c3837ab66ce5f5402758677ed37b0ab0333d777af4a2` |
| `lenient_cut/ms3_recount_v79.py` | 2026-09-30 | whole file | `0d6f51da216ddf4079a552c70c485ef9d0a8b2c5a0298b74565e0222712efbc8` |

Two data files are copied byte for byte beside it, because two of the scripts read them from their
own folder: `targets_v85.json` (2026-09-30, `bd2717c1697a3b50d60e68b0b039969b568a886fb93f5bf9ff6711edae03a7eb`;
`targets_v88.py` asserts its 21 targets equal these) and `ms3_targets_v78.json` (2026-09-30,
`5520171c7f58f767835094cafefea8f972ea4444933c6bfcf25e3b1cf63f64f7`; the input of `ms3_recount_v79.py`).

**The only edits** are lines marked `# VENDOR glue (refine_core)`:

- the modules the sources import from one another (`trunc_core`, `paths_core_f3`, `lenient_core`, and
  the `exec` of `ms2_rules.py` / `column_dp_v85.py` definitions) are this one module;
- the mass list is the `--cytc` argument, its `#` provenance lines skipped and CRLF stripped
  (`detect_fdr.py`, `lenient_core.py`);
- `detect_fdr.py` runs at the report's K = 300 (argv carries `--cytc`) and keeps `detect_fdr.json` in
  memory, round-tripped through `json` exactly as it would be written; `lenient_core.detect_levels`
  reads it from there;
- no output file is written (`detect_fdr`, `column_dp_v85`, `targets_v88`, `ms3_recount_v79`).

Run from the parent repo root (`python`/`py` on this host are Store stubs; numpy >= 2 for
`np.bitwise_count`):

    uv run --with numpy python -I FlashIDA/test-scripts/refine/refine_core.py --cytc FlashIDA/test-data/refine/cytc_grp1_prec1.tsv

It prints Detect (36 explained, cut 4, the 26 levels), the five best lambda_k of Version 85
(+526.14 S 63, +655.19 S 50, +412.04 / +525.12 / +655.22 S 49), the 21 targets of Version 88 and the
recount of Version 79. Note that the recount is Version 79's, against the **+637.18 Da** path of
`ms3_targets_v78.json` (Version 78, before Version 85 excluded +637.18 as +655.19 minus water) — its
49 / 45 are not the recount of the Version 88 pair.

**Why a copy.** The publication notes live outside every repository; a reference that exists only
there is not reproducible from a clone of this workspace. A change to the prototype is carried here
together with regenerated fixtures under `test-data/refine/` and the expectations in `Refine_test`
— or not at all.

`make_refine_tables.py --july` cuts `test-data/refine/july_ms3_pieces.tsv` from the run of
2026-07-08 (`C:/FLASHIda/flashida-fresh`), selecting the MS3 scans as `lenient_cut/refute.py` does.

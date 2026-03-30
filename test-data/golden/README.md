# Golden Files

This directory contains reference output files for regression testing.
Each file captures the output of `Flash.exe -t` for a specific
(spectrum file, method config) combination.

## Provenance

Each golden file is generated from real experimental data (top-down
proteomics `.mzML` files). The source `.mzML` file and the scan number
used to produce the input spectrum should be documented alongside each
golden file entry. Golden files are created by capturing CI output
(see "How to Update" below) and must not be constructed synthetically.

## How to Update

When an intentional behavioral change is made (e.g., a scoring change
in Phase 4), update golden files as follows:

1. Trigger CI on the branch containing the code change.
2. When the `windows-tests` job completes, download the `regression-output`
   artifact from the Actions UI.
3. Inspect the diffs between the artifact output and the current golden files.
4. If the diffs are expected, copy the updated files to `test-data/golden/`
   and commit them.
5. In the PR description, list each changed golden file and explain
   why the output changed.

## Review Expectations

PR reviewers must verify:
- Golden file changes are accompanied by a code change that explains them.
- The diff is in the expected direction (e.g., different scores if
  scoring logic changed, same scores if only refactoring occurred).
- No golden file changes occur in phases that claim zero behavioral change
  (e.g., Phase 0, Phase 2, Phase 3).

## File Index

| File | Phase | Source | Notes |
|------|-------|--------|-------|
| `baseline_phase0.tsv` | 0 | `ms1_smoke_test.txt` + `method_default.xml` | Initial baseline |
| `baseline_phase3.tsv` | 3 | `ms1_smoke_test.txt` + `method_default.xml` | Identical to Phase 0 (shadow-only, no behavioral change) |
| `phase4_standard_dda.tsv` | 4 | `ms1_standard.txt` + `method_default.xml` | Pre-switch baseline: standard DDA (old bridge path) |
| `phase4_deep_mode.tsv` | 4 | `ms1_standard.txt` + `method_deep.xml` | Pre-switch baseline: deep mode |
| `phase4_inclusion.tsv` | 4 | `ms1_standard.txt` + `method_inclusion.xml` | Pre-switch baseline: inclusion mode (non-strict, TSV targets) |
| `phase4_exclusion.tsv` | 4 | `ms1_standard.txt` + `method_exclusion.xml` | Pre-switch baseline: exclusion mode |
| `phase4_tag_targeting.tsv` | 4 | `ms1_standard.txt` + `ms2_hcd_fragment.txt` + `method_tag_targeting.xml` | Pre-switch baseline: tag-based targeting |
| `phase4_quant.tsv` | 4 | `ms1_standard.txt` + `ms2_quant_tmt.txt` + `method_quant.xml` | Pre-switch baseline: isobaric quant |
| `phase4_ms3_mode1.tsv` | 4 | `ms1_standard.txt` + `ms2_hcd_fragment.txt` + `method_ms3_mode1.xml` | Pre-switch baseline: MS3 Source CID |
| `phase4_ms3_mode2.tsv` | 4 | `ms1_standard.txt` + `ms2_hcd_fragment.txt` + `method_ms3_mode2.xml` | Pre-switch baseline: MS3 SPS |
| `phase4_ms3_mode3.tsv` | 4 | `ms1_standard.txt` + `ms2_hcd_fragment.txt` + `method_ms3_mode3.xml` | Pre-switch baseline: MS3 HCD-triggered |

### Phase 4 Golden File Provenance

- **Branch:** `flashida-v9-migration` at commit `1e35287`
- **CI run:** `23738550840` on `phase-4` branch
- **OpenMS commit:** `10e7950c61` (`flashida-v9-bridge`)
- **Spectrum sources:**
  - `ms1_standard.txt`: 50 MS1 scans from `Eclipse_20251016_Original_EcoliRedAlkMCWFA_60min_2ul_R1.mzML`
  - `ms2_hcd_fragment.txt`: CytC HCD MS2 from `20250121_CytC_MS2HCD_MS3HCDCID_Mode2_MS2CE40_MS3CID27.mzML`
  - `ms2_quant_tmt.txt`: iodoTMT MS2 from `FLASHIda_methodQuant_Ecoli_Glucose_vs_Acetat_iodoTMT_FC0_only1Cond_1ul.mzML`
- **Note:** Captured from old bridge path (pre-switch) to serve as behavioral equivalence baseline for the unified bridge switch-over.
- **Note:** `phase4_inclusion.tsv` captured with malformed TSV inclusion list (bare mass values). Will be re-captured after fixing inclusion list format to 5-column TSV.

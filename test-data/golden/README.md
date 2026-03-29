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

# Design: MS2 Identification Logging + ProForma Notation + Unified Fragment Structs

**Date:** 2026-04-16
**Scope:** C++ (`FragmentAnalysis.h/.cpp`, `MS3FragmentMatcher.h/.cpp`, `FLASHIda.h/.cpp`, `Exploration.h/.cpp`)

## Problem

1. **identification.tsv is MS3-only.** The file only logs fragment matches from MS3 scans. MS2 scans that identify a proteoform (via tag-based fragment matching) produce no identification record, even though the engine has the proteoform sequence, PTM sites, and matched fragment ions.

2. **Proteoform notation is a plain sequence.** The `proteoform` column writes the raw amino acid string with no PTM information. PTM sites are available in the data but not represented in the output.

3. **No scan mode distinction.** Exploration variant scans and production scans are not distinguished in identification.tsv. The results.tsv has exploration metadata, but identification.tsv does not.

4. **Two separate fragment match structs.** `MS3FragmentMatcher::FragmentMatch` (MS3FragmentMatcher.h:66-74) and the per-fragment data from `TagBasedFragmentMatch` (FragmentAnalysis.h:266-276) use different representations for the same concept (a matched fragment ion). This prevents a unified identification writer.

5. **MS2 per-fragment detail is discarded.** `runTagBasedFragmentMatching_()` populates `FragmentMatchResult.total_match_count` (line 744) but the individual `TagBasedFragmentMatch` entries (ion type, index, mass) are only used for top-N selection into output arrays — the full list is not preserved.

## Current State

The FragmentMatchResult unification spec is **already implemented**. `FragmentAnalysis::FragmentMatchResult` exists at FragmentAnalysis.h:69-77 with fields: `total_match_count`, `region_start`, `region_end`, `ptm_sites`, `matched_protein`, `proteoform_sequence`. All three matching functions (`getTopFragmentMatches`, `getTerminalFragmentIons`, `getAmbiguityEnclosingIons`) already accept `FragmentMatchResult& result` as a parameter.

`MS3FragmentMatcher::MatchResult` and `MS3FragmentMatcher::FragmentMatch` still exist as separate structs, used by:
- `ExplorationVariant.identification_result` (Exploration.h:79)
- `FeedResultInfo.identification_result` (Exploration.h:160)
- `writeIdentificationRow_()` (FLASHIda.h:285-287)
- `calibrateAndScore()` (MS3FragmentMatcher.h:134-142)

## Design

Four sub-problems, implemented in dependency order.

### Sub-problem 1: Rename `FragmentMatchResult` to `ProteoformMatch` and add `FragmentMatch`

Rename the existing `FragmentAnalysis::FragmentMatchResult` (FragmentAnalysis.h:69-77) to `ProteoformMatch` and nest a `FragmentMatch` struct inside it that covers both MS2 and MS3 fragment detail.

**`FragmentAnalysis.h` — renamed and extended struct (replaces lines 68-77):**

```cpp
/// Complete result from a fragment matching operation (MS2 or MS3)
struct ProteoformMatch
{
  // -- existing fields (renamed from FragmentMatchResult) --
  int total_match_count = 0;   ///< Total fragments matched (uncapped)
  int region_start = -1;       ///< 0-based proteoform start (-1 = full sequence)
  int region_end = -1;         ///< 0-based exclusive proteoform end (-1 = full sequence)
  std::vector<PTMSite> ptm_sites;        ///< PTM sites from FLASHExtender
  std::string matched_protein;           ///< Protein file/DB name
  std::string proteoform_sequence;       ///< Matched protein sequence

  // -- new: per-fragment detail --
  struct FragmentMatch
  {
    std::string ion_type;       ///< "b", "y", "a" (MS2); "b", "y", "yb", "ya", "a" (MS3 local)
    int ion_index = 0;          ///< 1-based, proteoform-space (MS2) or subsequence-space (MS3)
    double observed_mass = 0.0; ///< Deconvolved mass (calibrated for MS3)
    std::string equiv_type;     ///< MS3 only: full-protein equivalent ion type ("b"/"y")
    int equiv_index = 0;        ///< MS3 only: full-protein equivalent ion index
    double adjusted_mass = 0.0; ///< MS3 only: offset-adjusted to full-protein
  };
  std::vector<FragmentMatch> fragments;  ///< All matched fragments with detail

  // -- new: calibration (MS3 only, defaults for MS2) --
  double ppm_offset = 0.0;        ///< Median PPM error from calibration pass
  double correction_factor = 1.0;  ///< 1/(1 + ppm_offset * 1e-6)
};
```

**What this replaces:**

| Old | Location | New |
|-----|----------|-----|
| `FragmentAnalysis::FragmentMatchResult` | FragmentAnalysis.h:69-77 | `FragmentAnalysis::ProteoformMatch` |
| `MS3FragmentMatcher::FragmentMatch` | MS3FragmentMatcher.h:66-74 | `FragmentAnalysis::ProteoformMatch::FragmentMatch` |
| `MS3FragmentMatcher::MatchResult` | MS3FragmentMatcher.h:77-82 | `ProteoformMatch` fields: `fragments` + `ppm_offset` + `correction_factor` |

**Rename propagation — all `FragmentMatchResult` references become `ProteoformMatch`:**

| File | Lines | Change |
|------|-------|--------|
| `FragmentAnalysis.h` | 133-145, 169-181, 203-215, 294-299 | Parameter type in matching functions + `runTagBasedFragmentMatching_()` |
| `FragmentAnalysis.cpp` | 379-384, 748-760 and others | Local variables, parameter types |
| `Exploration.h` | 235, 247 | `computeExplorationScore_()` and `computeFragmentMatch_()` signatures |
| `Exploration.cpp` | 287, 511 | Local variable declarations |

**`MS3FragmentMatcher.h` changes:**

Remove `FragmentMatch` struct (lines 66-74) and `MatchResult` struct (lines 77-82). Update `calibrateAndScore()` signature (line 134):

```cpp
static std::vector<double> calibrateAndScore(
    const std::vector<const DeconvolvedSpectrum*>& variant_spectra,
    const std::string& protein_sequence,
    const ProteoformContext& ctx,
    char fragment_ion_type,
    int fragment_ion_index,
    double loose_tolerance_ppm,
    double tight_tolerance_ppm,
    std::vector<FragmentAnalysis::ProteoformMatch>* detailed_results = nullptr);
```

**`MS3FragmentMatcher.cpp` changes:**

`calibrateAndScore()` populates `ProteoformMatch` instead of `MatchResult`:
- `result.ppm_offset`, `result.correction_factor` — same data, same field names
- `result.fragments` — vector of `ProteoformMatch::FragmentMatch` instead of `MS3FragmentMatcher::FragmentMatch`
- Field mapping: `ms3_ion_type` → `ion_type`, `ms3_ion_index` → `ion_index`, `observed_mass` → `observed_mass`, `ms2_equiv_type` → `equiv_type`, `ms2_equiv_index` → `equiv_index`, `adjusted_mass` → `adjusted_mass`

**`Exploration.h` changes:**

- `ExplorationVariant.identification_result` (line 79): type changes from `MS3FragmentMatcher::MatchResult` to `FragmentAnalysis::ProteoformMatch`
- `FeedResultInfo.identification_result` (line 160): same type change
- `computeExplorationScore_()` (line 235): `FragmentAnalysis::FragmentMatchResult*` → `FragmentAnalysis::ProteoformMatch*`
- `computeFragmentMatch_()` (line 247): return type `FragmentAnalysis::FragmentMatchResult` → `FragmentAnalysis::ProteoformMatch`

**`Exploration.cpp` changes:**

- `feedResultImpl_()` line 287: `FragmentAnalysis::FragmentMatchResult frag{}` → `FragmentAnalysis::ProteoformMatch frag{}`
- `feedResultImpl_()` line 392: `std::vector<MS3FragmentMatcher::MatchResult>` → `std::vector<FragmentAnalysis::ProteoformMatch>`
- `initiateNextLevel()` line 511: `FragmentAnalysis::FragmentMatchResult frag_result` → `FragmentAnalysis::ProteoformMatch frag_result`

### Sub-problem 2: Populate `FragmentMatch` vector during MS2 and MS3 matching

#### MS2: `FragmentAnalysis.cpp` — `runTagBasedFragmentMatching_()`

After matching completes and `result.total_match_count` is set (line 744), populate `result.fragments` from the `matches` vector. The `TagBasedFragmentMatch` struct (FragmentAnalysis.h:266-276) has these fields: `ion_type` (char), `fragment_index` (int, 1-based), `observed_mass` (double), plus `qscore`, `charge`, `peak_index`, `theoretical_mass`, `ppm_error`.

```cpp
// After line 744: result.total_match_count = static_cast<int>(matches.size());
result.fragments.clear();
result.fragments.reserve(matches.size());
for (const auto& m : matches)
{
  ProteoformMatch::FragmentMatch fm;
  fm.ion_type = std::string(1, m.ion_type);  // char → string
  fm.ion_index = m.fragment_index;            // 1-based
  fm.observed_mass = m.observed_mass;
  // equiv fields left at defaults (MS2 — no equivalent mapping needed)
  result.fragments.push_back(std::move(fm));
}
```

All matched fragments are captured, not just the top-N written to the output arrays.

#### MS3: `MS3FragmentMatcher.cpp` — `calibrateAndScore()`

When populating `detailed_results`, build `ProteoformMatch::FragmentMatch` entries instead of `MS3FragmentMatcher::FragmentMatch` entries. Same data, different struct. The `computeEquivalentIon()` results map to `equiv_type`, `equiv_index`, and `adjusted_mass` on the new struct.

### Sub-problem 3: ProForma Formatting

A static utility function on `FragmentAnalysis`:

```cpp
static std::string toProForma(
    const std::string& sequence,
    const std::vector<PTMSite>& ptm_sites);
```

**Algorithm:**

1. Sort PTM sites by `start_position` descending (right-to-left processing so insertions don't shift earlier indices).
2. Start with the plain sequence string.
3. For each PTM site:
   - Format mass shift as `[+19.0523]` or `[-18.0106]` (sign always shown, 4 decimal places).
   - If `start_position == end_position` (localized): insert mass shift after residue at position `start_position` (1-based → 0-based index adjustment).
   - If `start_position != end_position` (ambiguous): insert `)` + mass shift after residue at `end_position`, insert `(` before residue at `start_position`.
4. Return the modified string.

**Position semantics:** PTMSite positions are 1-based relative to the proteoform (verified: FragmentAnalysis.cpp lines 610-611 set `mod_starts` and `mod_ends` with `+= 1`). For a proteoform `PROTEOSFORMSISK`:
- Localized PTM at position 5: `PROTE[+79.9663]OSFORMSISK`
- Ambiguous PTM at positions 3-8: `PR(OTEOSF)[+19.0523]ORMSISK`

**Location:** `FragmentAnalysis.h` (declaration), `FragmentAnalysis.cpp` (definition). Static method — no instance state needed.

### Sub-problem 4: identification.tsv Schema and Writer Changes

#### Updated Header

Two columns prepended (`ms_level`, `scan_mode`), rest unchanged. In `FLASHIda.cpp` lines 125-130, update the header string:

```
ms_level	scan_mode	tracking_id	proteoform	start_pos	end_pos	ppm_offset	correction_factor	ms1_precursor_mass	ms1_precursor_mz	ms1_precursor_charge	ms2_precursor_ion	ms2_precursor_mass	ms2_precursor_mz	ms2_precursor_charge	ms2_fragments	ms2_fragment_masses	ms3_fragments	ms3_fragment_masses
```

#### Column semantics by level

| Column | MS2 row | MS3 row |
|--------|---------|---------|
| `ms_level` | `2` | `3` |
| `scan_mode` | `E` or `R` | `E` or `R` |
| `proteoform` | ProForma notation | ProForma notation |
| `start_pos` / `end_pos` | proteoform region | proteoform region |
| `ppm_offset` | `0.00` | calibration result |
| `correction_factor` | `1.00000000` | calibration result |
| `ms1_precursor_*` | from originating ScanCommand | from MS2Context |
| `ms2_precursor_ion` | empty | targeted MS2 fragment (e.g. `b20`) |
| `ms2_precursor_mass/mz/charge` | empty | MS2 fragment targeted for MS3 |
| `ms2_fragments` | all matched ions: `b3;y5;b7` | full-protein equiv ions (existing behavior) |
| `ms2_fragment_masses` | observed masses | offset-adjusted masses (existing behavior) |
| `ms3_fragments` | empty | MS3 local ions (existing behavior) |
| `ms3_fragment_masses` | empty | observed masses (existing behavior) |

#### Updated writer signature

Current signature (FLASHIda.h:285-287):
```cpp
void writeIdentificationRow_(const std::string& tracking_id,
                              const Exploration::MS2Context& ctx,
                              const MS3FragmentMatcher::MatchResult& result);
```

New signature:
```cpp
void writeIdentificationRow_(const std::string& tracking_id,
                              int ms_level,
                              char scan_mode,
                              const Exploration::MS2Context& ctx,
                              const FragmentAnalysis::ProteoformMatch& match);
```

#### Writer logic (replaces FLASHIda.cpp:454-508)

```cpp
void FLASHIda::writeIdentificationRow_(
    const std::string& tracking_id,
    int ms_level,
    char scan_mode,
    const Exploration::MS2Context& ctx,
    const FragmentAnalysis::ProteoformMatch& match)
{
  if (!identification_tsv_stream_.is_open()) return;
  if (match.fragments.empty()) return;

  std::string proforma = FragmentAnalysis::toProForma(
      match.proteoform_sequence, match.ptm_sites);

  // ms2_fragments / ms2_fragment_masses
  std::ostringstream ms2_frags, ms2_masses;
  ms2_frags << std::fixed << std::setprecision(4);
  ms2_masses << std::fixed << std::setprecision(4);

  if (ms_level == 2)
  {
    // Direct: ion_type + ion_index, observed_mass
    for (size_t i = 0; i < match.fragments.size(); ++i)
    {
      if (i > 0) { ms2_frags << ";"; ms2_masses << ";"; }
      ms2_frags << match.fragments[i].ion_type << match.fragments[i].ion_index;
      ms2_masses << match.fragments[i].observed_mass;
    }
  }
  else
  {
    // Equivalent: equiv_type + equiv_index, adjusted_mass
    for (size_t i = 0; i < match.fragments.size(); ++i)
    {
      if (i > 0) { ms2_frags << ";"; ms2_masses << ";"; }
      ms2_frags << match.fragments[i].equiv_type << match.fragments[i].equiv_index;
      ms2_masses << match.fragments[i].adjusted_mass;
    }
  }

  // ms3_fragments / ms3_fragment_masses (MS3 only)
  std::ostringstream ms3_frags, ms3_masses;
  ms3_frags << std::fixed << std::setprecision(4);
  ms3_masses << std::fixed << std::setprecision(4);

  if (ms_level == 3)
  {
    for (size_t i = 0; i < match.fragments.size(); ++i)
    {
      if (i > 0) { ms3_frags << ";"; ms3_masses << ";"; }
      ms3_frags << match.fragments[i].ion_type << match.fragments[i].ion_index;
      ms3_masses << match.fragments[i].observed_mass;
    }
  }

  // ms2_precursor_ion (MS3 only)
  std::string precursor_ion;
  if (ms_level == 3 && ctx.fragment_ion_type != '\0')
    precursor_ion = std::string(1, ctx.fragment_ion_type)
                    + std::to_string(ctx.fragment_ion_index);

  identification_tsv_stream_
    << ms_level << "\t"
    << scan_mode << "\t"
    << tracking_id << "\t"
    << proforma << "\t"
    << match.region_start << "\t"
    << match.region_end << "\t"
    << std::fixed << std::setprecision(2) << match.ppm_offset << "\t"
    << std::setprecision(8) << match.correction_factor << "\t"
    << std::setprecision(4) << ctx.ms1_precursor_mass << "\t"
    << ctx.ms1_precursor_mz << "\t"
    << ctx.ms1_precursor_charge << "\t"
    << precursor_ion << "\t"
    << (ms_level == 3 ? ctx.fragment_mass : 0.0) << "\t"
    << (ms_level == 3 ? ctx.fragment_mz : 0.0) << "\t"
    << (ms_level == 3 ? ctx.fragment_charge : 0) << "\t"
    << ms2_frags.str() << "\t"
    << ms2_masses.str() << "\t"
    << ms3_frags.str() << "\t"
    << ms3_masses.str() << "\n";
  identification_tsv_stream_.flush();
}
```

#### `NextLevelResult` changes (Exploration.h:132-140)

Add `ProteoformMatch` field so the caller (`processScan`) can access fragment detail for MS2 identification logging. The existing `fragment_count`, `matched_protein`, `proteoform_sequence` fields become redundant but are kept for backward compatibility with `writeScanResultRow_()`:

```cpp
struct NextLevelResult
{
  std::vector<ScanCommand> commands;
  std::vector<MS2Context> ms3_contexts;
  std::string matched_protein;
  std::string proteoform_sequence;
  float tic_coverage = 0.0f;
  int fragment_count = 0;
  FragmentAnalysis::ProteoformMatch proteoform_match;  // NEW: full fragment detail for identification
};
```

In `initiateNextLevel()` (Exploration.cpp:511-553), the local `frag_result` is already populated. Add one line after line 553:
```cpp
nlr.proteoform_match = frag_result;
```

#### Call sites in `FLASHIda.cpp`

**Non-exploration MS2** (after `initiateNextLevel()`, around line 908):

The `nlr` (NextLevelResult) now carries `proteoform_match`. The originating `ScanCommand ctx` (resolved at line 837) provides MS1 precursor info:

```cpp
if (!nlr.proteoform_sequence.empty())
{
  Exploration::MS2Context ms2_ctx;
  ms2_ctx.proteoform_sequence = nlr.proteoform_match.proteoform_sequence;
  ms2_ctx.start_pos = nlr.proteoform_match.region_start;
  ms2_ctx.end_pos = nlr.proteoform_match.region_end;
  ms2_ctx.ptm_sites = nlr.proteoform_match.ptm_sites;
  ms2_ctx.ms1_precursor_mass = ctx.mono_mass;
  ms2_ctx.ms1_precursor_mz = ctx.stages[0].precursor_mz;
  ms2_ctx.ms1_precursor_charge = ctx.stages[0].charge_state;
  writeIdentificationRow_(id_str, 2, 'R', ms2_ctx, nlr.proteoform_match);
}
```

Insert this block before `writeScanResultRow_()` (line 915).

**Exploration MS2** (after `feedResult()`, around line 826):

`FeedResultInfo.identification_result` (now `ProteoformMatch`) carries fragment detail when the exploration metric is `FragmentCount`. For other metrics, fragments may be empty — only write if non-empty:

```cpp
if (!info.proteoform_sequence.empty() && !info.identification_result.fragments.empty())
{
  writeIdentificationRow_(id_str, 2, 'E', info.ms2_context, info.identification_result);
}
```

Note: For MS2 exploration, `info.ms2_context` (Exploration.cpp:337-358) contains the MS1 precursor info from `group.originating_cmd`. The `fragment_ion_type`/`fragment_ion_index` fields will be '\0'/0 (no MS3 precursor context), which is correct — the writer skips `ms2_precursor_ion` when `ms_level == 2`.

**Non-exploration MS3** (around line 1012-1014, existing):

Change from:
```cpp
writeIdentificationRow_(id_str, mc, detailed[0]);
```
To:
```cpp
writeIdentificationRow_(id_str, 3, 'R', mc, detailed[0]);
```

Where `detailed` changes type from `std::vector<MS3FragmentMatcher::MatchResult>` to `std::vector<FragmentAnalysis::ProteoformMatch>` (line 1001).

**Exploration MS3** (around line 958-960, existing):

Change from:
```cpp
writeIdentificationRow_(id_str, info.ms2_context, info.identification_result);
```
To:
```cpp
writeIdentificationRow_(id_str, 3, 'E', info.ms2_context, info.identification_result);
```

#### Exploration MS2 `identification_result` population

Currently, `FeedResultInfo.identification_result` is only populated for MS3 `FragmentCount` metric (Exploration.cpp:385-413). For MS2 exploration variants, the `frag` variable (line 287) contains the fragment match data but is not copied to `info.identification_result`.

Add after line 330 (`info.proteoform_sequence = frag.proteoform_sequence;`):
```cpp
info.identification_result = frag;
```

This ensures MS2 exploration variants carry their fragment detail to the identification writer.

## Files Changed

### C++ (OpenMS)

| File | Change |
|------|--------|
| `FragmentAnalysis.h` | Rename `FragmentMatchResult` → `ProteoformMatch` (line 69), add nested `FragmentMatch` struct, add `fragments`/`ppm_offset`/`correction_factor` fields, add `toProForma()` static method declaration |
| `FragmentAnalysis.cpp` | Populate `result.fragments` in `runTagBasedFragmentMatching_()` (after line 744), implement `toProForma()`, rename all `FragmentMatchResult` refs |
| `MS3FragmentMatcher.h` | Remove `FragmentMatch` (lines 66-74) and `MatchResult` (lines 77-82) structs, update `calibrateAndScore()` to use `ProteoformMatch` (line 134-142) |
| `MS3FragmentMatcher.cpp` | Populate `ProteoformMatch` in `calibrateAndScore()`, use `ProteoformMatch::FragmentMatch` |
| `Exploration.h` | `ExplorationVariant.identification_result` (line 79) and `FeedResultInfo.identification_result` (line 160): type → `ProteoformMatch`. Add `ProteoformMatch proteoform_match` to `NextLevelResult` (line 132). Update `computeExplorationScore_`/`computeFragmentMatch_` signatures (lines 235, 247) |
| `Exploration.cpp` | Rename local `FragmentMatchResult` vars. Copy `frag_result` to `nlr.proteoform_match` in `initiateNextLevel()`. Copy `frag` to `info.identification_result` in `feedResultImpl_()` for MS2 variants |
| `FLASHIda.h` | Update `writeIdentificationRow_()` signature (lines 285-287) |
| `FLASHIda.cpp` | Updated header (lines 125-130), rewritten level-aware writer (lines 454-508), add MS2 identification call sites (~line 908 and ~line 826), update MS3 call sites (~line 960, ~line 1014), update `detailed` type (~line 1001) |

### Not changed

- `ScanCommand.h` / `IsolationStage` — no struct layout changes
- C# / P/Invoke — no changes (identification.tsv is written entirely in C++)
- `MS3FragmentMatcher::ProteoformContext` — unchanged (still used for subsequence extraction)
- `MS3FragmentMatcher::TheoreticalMass`, `MatchDetail` — unchanged (internal matching structs)
- `MS2Context` struct — unchanged (still used to carry MS1/MS2 precursor context)

## Tests

### Existing tests — backward compatibility

| Test | Impact | Action |
|------|--------|--------|
| `FLASHIda_ProcessScan_test` | identification.tsv gains `ms_level`/`scan_mode` columns, proteoform becomes ProForma, MS2 rows may appear | Update expected output for new columns |
| `FLASHIda_exploration_test` | Same column additions, exploration MS2 rows may appear if `FragmentCount` metric produces matches | Update expected output |
| `MS3FragmentMatcher_identification_test` | `MatchResult`/`FragmentMatch` types removed | Update to use `ProteoformMatch`/`ProteoformMatch::FragmentMatch` |
| `FLASHIdaFAIMS_test` | No identification.tsv assertions | Unaffected |
| `FLASHIdaQueueTracking_test` | Queue ordering only | Unaffected |
| `ScanCommandLayout_test` | Struct layout only | Unaffected |
| `DeconvolvedSpectrum_OptimizationMetadata_test` | No fragment matching | Unaffected |

### New tests

**`toProForma()` unit tests:**
- Unmodified sequence → plain sequence (no PTMs)
- Single localized PTM (start == end) → `PEPTK[+79.9663]IDE`
- Single ambiguous PTM (start != end) → `PEP(TKI)[+79.9663]DE`
- Multiple PTMs (localized + ambiguous, mixed) → correct right-to-left insertion
- Negative mass shift → `PEPTK[-18.0106]IDE`
- Empty PTM sites vector → plain sequence unchanged
- PTM at first residue (position 1)
- PTM at last residue (position == sequence length)

**MS2 identification row test:**
- Process MS2 scan with known proteoform match (tag-based matching succeeds)
- Verify identification.tsv row: `ms_level=2`, `scan_mode=R`
- Verify `proteoform` column is ProForma notation
- Verify `ms2_fragments`/`ms2_fragment_masses` populated with all matched fragments (not just top-N)
- Verify `ms3_fragments`/`ms3_fragment_masses` empty
- Verify `ms2_precursor_ion/mass/mz/charge` empty (no MS3 precursor context for MS2)
- Verify `ppm_offset=0.00`, `correction_factor=1.00000000`

**MS3 identification row backward compatibility test:**
- Verify existing MS3 rows now have `ms_level=3`, `scan_mode=R` or `E`
- Verify `proteoform` is ProForma (not plain sequence)
- Verify ms2/ms3 fragment columns unchanged in content

**Exploration MS2 identification test:**
- Process exploration MS2 variant with `FragmentCount` metric where proteoform matches
- Verify row with `ms_level=2`, `scan_mode=E`

**Mixed MS2 + MS3 identification test:**
- Process MS2 then corresponding MS3 for same precursor
- Verify both rows appear in order, same proteoform (ProForma), consistent MS1 precursor data
- MS2 row has fragments in `ms2_fragments`, MS3 row has fragments in both `ms2_fragments` (equiv) and `ms3_fragments` (local)

### Future test considerations

- **ETD parameter wiring** (from ETD spec): once ETD fields flow through, identification tests should verify ETD scans produce correct rows with appropriate `scan_mode`
- **Multi-activation exploration**: rows for different activation types (HCD vs ETD) distinguishable via `scan_mode` column; identification.tsv captures the proteoform result regardless of activation type
- **ProForma round-trip**: if downstream tools parse ProForma, a round-trip test (PTMSites → ProForma → parse → PTMSites) would catch formatting bugs — only needed if a parser is added

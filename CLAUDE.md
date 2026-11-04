# CLAUDE.md

Guidance for Claude Code in the FlashIDA submodule. **Scope: the C# side only.** The parent
`../CLAUDE.md` owns the working rules, the bridge/ABI contract, CI and landing, build commands,
goldens and config flow, and wins on any conflict; `../OpenMS/CLAUDE.md` owns the C++ engine.
Each rule below is one line; the mechanism, measurements and history behind it are in
`docs/csharp-reference.md`, under the same heading.

FlashIDA is real-time intelligent data acquisition for top-down proteomics on Thermo tribrid
instruments: it decides which spectra the instrument acquires, in which order. C# 7.3 / .NET
Framework 4.8, x64, Windows only. Solution `src/Flash.sln`, output `bin/`.

## `Flash.Flash` is production

Two classes define `Main`; `src/Flash/Flash.csproj:38` picks one. Git pins
`Flash.IDA.FLASHIdaWrapper.Main`, the offline harness that CI builds and tests. **What ships is
`Flash.Flash.Main`** (`Flash.cs`): the owner flips `StartupObject` at deploy time, outside version
control (21 flips in history).

- A bug in `Flash.cs`, `OnContactClosure`, `ProcessSpectrum` or `DataPipe` is a **live production
  bug** that wastes instrument runs. Nothing tests it; it is found by inspection.
- log4net **is** configured in production (inside the instrument `Main`).
- "Flip it back" is not a fix: the regression runner and CI's capture steps drive the offline
  interface, `Flash.exe <input_spectrum> <output.tsv> <method.json> [ms2_spectrum]`, positional
  only (`-t` becomes the input filename).

## Acquisition (the instrument path)

```
MsScanArrived ─► ProcessSpectrum(IMsScan)                     [instrument event thread]
                  ├─ Access ID == HandshakeJobNumber ─► inCustom = true; run clock restarts
                  └─ if inCustom:
                       DataPipe.Push(scan) ─► ScanData.From (7 owned values, read HERE)
                         ─► BufferBlock ─► ActionBlock ─► UnifiedScanProcessor.ProcessMS ─► ProcessScan
                       top up to scheduling.target_depth (default 2):
                         GetNextScanCommand ─► ScanFactory.BuildFromCommand ─► SendCustomScan
```

- **There is no acquisition loop**: it is event-driven. `Main`'s busy-wait ends in the teardown
  (unsubscribe, `CancelCustomScan`), on the Main thread, once (ADR-0041).
- **Nothing happens until the handshake echoes.** Both startup paths send the same
  `BuildHandshakeScan()`; latching at send time is a defect (ADR-0008).
- **The drain tops up to `target_depth` in a bounded loop**; one send per arrival can never reach 2
  (ADR-0033). "Success" means `SetFusionCustomScan` returned `true`; a refusal `break`s and the
  outstanding counter is clamped at 0.
- **An uncommanded arrival (`Access ID == "0"`) is neither ingested nor answered.** An AGC prescan is
  pushed with no centroids.
- **Nobody disposes the `IMsScan`**: the iAPI frees it when the next scan replaces it. Both
  disposal attempts cost instrument runs. Pinned by `DataPipe_DoesNotDisposeScan`.
- **`processScan` and `getNextScanCommand` run on different threads** (pool vs event thread); the
  engine's mutexes are load-bearing.
- **Scan identity is the first 3 characters of `Scan Description`**, a base-94 id the engine minted;
  not `Access ID`, not `RunningNumber`. A description the engine did not mint is rejected, so tests
  cannot fabricate ids.
- **`GetNextScanCommand` never returns 0**; an empty queue yields an idle survey (MS1, priority 3).
  Every drain loop breaks on `MsnLevel == 1 && Priority == 3` or caps iterations. `IsAgc` is
  **not** a stop condition (ADR-0031). A `0` from the wrapper means an exception was caught.
- **The run clock starts at the handshake echo**, not at `AcquisitionStreamOpening` (ADR-0043).

## Components

- **`IDA/FLASHIdaWrapper.cs`**: 5 `[DllImport]`s, the mirrored 2048-byte `ScanCommand`, the offline
  `Main`. Its static constructor **overwrites `OPENMS_DATA_PATH`** with `<assembly dir>/share/OpenMS`.
  Error sentinels: `ProcessScan` −1, `GetNextScanCommand` 0, `GetNextTrackingId` −1.
- **`IDA/UnifiedScanProcessor.cs`**: the only `IScanProcessor`; every MS level goes through
  `ProcessScan`. (`ScanScheduler`, `FAIMSScanProcessor`, `IDAScanProcessor`, `QuantScanProcessor`
  do not exist.)
- **`ScanFactory.cs`** builds the custom scan from the struct's fields.
  - Every number is formatted with **`InvariantCulture`**: under `de-DE`, m/z 1000.5 became two
    isolation windows.
  - `PrecursorMass`/`IsolationWidth`/`ChargeStates` are `string[]` of `','`-joined notch groups per
    `';'` stage; `NotchesForStage` reads stage k's fixed block of `Notches[18]` and mirrors C++
    (ADR-0016/0019). `MaxNotchesPerStage + 1` and `MaxIsolationStages` are different tens.
  - **`ReactionTime` is gated on the activation, never on the value** (ADR-0030); reagent keys keep
    their `> 0` gate. `NeedsReactionTime` mirrors the engine's predicate as an exact set.
  - This hop is **invisible to every golden**; only `ScanFactoryTests`-style assertions see it.
    `FillParameters` is `protected` so the mock calls production code.
- **`DataPipe.cs`**: `MaxDegreeOfParallelism = 1` (stated, because `processScan` relies on it).
  - The queue holds a **`ScanData` snapshot**; anything new the engine needs is added to
    `ScanData.From`, never read lazily. `RetentionTime` is minutes and crosses unconverted (ADR-0051).
  - Unbounded and never drops: a dropped exploration variant wedges its group.
  - **The `ActionBlock` must never fault**: no statement outside its `try/catch`, and the catch body
    is itself guarded. A fault silently ends acquisition.
  - Shutdown is `RequestStop(reason)`, one-shot; production never drains the pipeline or disposes the
    wrapper, deliberately (every stream flushes per row).

## Config (C# side)

Twelve `[JsonKey]` properties of `MethodConfig` plus the root bool `conditional_ms2`: `global`,
`deconvolution`, `precursor_selection`, `tagging`, `flashtnt`, `quantification`, `faims`,
`ms_settings`, `scheduling`, `characterization`, `files`, `runtime`. `selection_strategy` is a
migration error. Do not find sections by grepping class-level `[JsonKey]` (nested classes have one).
`ms1`/`ms2`/`ms3` are structs under `ms_settings` only; extra MS2 configs in `additional_ms2`.
Structs validate against fields, classes against properties. A dictionary's keys are free but its
values are validated.

- **`deconvolution.tol` needs ≥ 3 entries**; the C# default of two is unloadable.
- **C++ `.value(key, default)` fallbacks are dead in production** (`ToCppJson` emits every key);
  hand-written C++ fixtures do get them, so their defaults differ (MS3 budget 10 vs 3).
- No level-1 exploration exists. `metric: "none"` rewrites the sweep values.
- `exploration.kinetic_model` and `characterization.refine` are forwarded verbatim or `null`, never
  synthesised; C++ decides whether they are required or refused.
- Unknown keys are rejected here and in C++. `test-data/config_schema_reference.json` is generated;
  regenerate, never hand-edit. Every key is also documented by hand in `docs/site/method.schema.json`
  (ADR-0058, `KeyDocumentationParityTests`); a push to `phase-containers` republishes the site.

## Tests (`src/Flash.Tests/`)

- **Drive acquisitions only through `ContinuityTestHarness.PushScanAndDrainFull`**, the mirror of
  C++ `runInterleaved`: one command → one response stamped with that command's description; idle
  predicate `IsAgc != 0 || empty description || (level <= 1 && ms1Fed >= nMs1)`; stop at
  `idle >= 3` or `maxIters`. The two sides change together (`driver-sync-reminder.sh`).
- **Vacuity trap**: `MockMsScan`'s default description `"~~~S"` always fails the engine's gate, so a
  raw `PushScan` MS1 is rejected regardless of content. Use `PushMs1` or `PushScanAndDrainFull`.
- **The test host must be `en-US`**: `MockMsScan` parses fixtures with a culture-sensitive
  `double.Parse` (under `de-DE`, `674.6919` reads as `6746919`). The runners' account is en-US.
- **The CE map feeds every MS2 command, the follow-up included**, with no fallback. MS3 fixtures can
  be selected by ion and CE (`BuildMs3IonCeSelector`); a miss is logged `[MS3-SKIP]`, never
  fabricated, except in `Golden_Refine_Discriminating_CytC`, which answers with an empty MS3.
- **`test-scripts/refine/` and `test-scripts/kinetic/` are generators, not oracles**; a change ships
  with regenerated tables or not at all. Fixture provenance: `test-data/spectra/FIXTURES.md`.
- **A capture run proves nothing**: with `LOG_GOLDEN_CAPTURE=1` the suite always passes.
- Goldens compare floats with tolerance, ints/strings/structure exactly, columns by name. The log
  comparer relabels ids to `T<n>` and masks `ida.log`'s `ID=` and `Superseded=` ids, so the C++
  `FLASHIda_LoggingFields_test` is their only gate. Three cytC MS3 assertions anchor on 12321.28's
  `b49` (FDmerge moved 12351.39's ions out of tolerance).
- `CapturedRecords` sees the raw struct; `CollectResults()` sees only the built scan's `Values`.

## Dependencies and logging

- **Thermo iAPI** (`dependencies/`, proprietary, never committed or pushed; this repo is public).
  `Thermo.TNG.Client.API` is deliberately `<Private>False</Private>` in `Flash.csproj`: licensed,
  never shipped. Do not "fix" the asymmetry with `Flash.Tests.csproj`.
- **OpenMS DLLs** (`dll/`, tracked): CI overwrites 4 of 5 with fresh builds (only `zlib.dll` stays).
- **`share/OpenMS` exists twice**: `FlashIDA/share/OpenMS` (148 files) is what every C# process
  reads; `OpenMS/share/OpenMS` (254) is for ctest.
- **log4net**: `General` (console + `FlashLog.log`) carries everything; the `IDA` logger is never
  written to, so `IDALog.log` is always empty. `Flash.Main` overwrites both `<file>` paths to the
  run folder; keep `appendToFile=false` explicit and no `PatternString` type.

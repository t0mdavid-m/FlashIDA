# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

FLASHIda is a real-time intelligent data acquisition (IDA) system for top-down proteomics on Thermo Scientific tribrid instruments. It controls which mass spectra the instrument acquires and in which order, using real-time deconvolution and machine learning-based quality assessment for precursor selection.

- **Language**: C# (.NET Framework 4.8, C# 7.3)
- **IDE**: Visual Studio 2019
- **Platform**: Windows x64 only (requires physical Thermo instrument or test mode)
- **Solution file**: `src/Flash/Flash.sln`

## Build

Open `src/Flash/Flash.sln` in Visual Studio 2019 and build, or use MSBuild:

```
msbuild src/Flash/Flash.sln /p:Configuration=Debug /p:Platform="Any CPU"
```

Debug output goes to `bin/`. The Thermo iAPI DLLs must be placed in `dependencies/` before building (not checked in; see Installation.md).

There are no automated tests. The `-t` (test mode) flag runs `FLASHIdaWrapper.Main()` for offline deconvolution against text file input without an instrument connection.

## Architecture

### Data Flow

```
Instrument → IMsScan → DataPipe (BufferBlock → TransformManyBlock → ActionBlock)
  → IScanProcessor.ProcessMS() → FLASHIdaWrapper (P/Invoke to OpenMS.dll)
  → Precursor targets → ScanFactory.CreateCustomScan()
  → ScanScheduler.enqueue() → Instrument
```

### Key Components

- **Flash.cs** — Entry point. Connects to instrument via Thermo Fusion API, manages instrument state, loads XML method config, creates the scan processor, and runs the main acquisition loop.

- **IScanProcessor.cs** — Interface for scan processors. `ProcessMS(IMsScan)` returns custom scans to submit; `OutputMS(IFusionCustomScan)` sends them to the instrument.

- **IDA/UnifiedScanProcessor.cs** — The sole scan processor (since Phase 5). Routes all MS levels through `FLASHIdaWrapper.ProcessScan()` → C++ `processScan()`, then drains commands via `GetNextScanCommand()`.

- **IDA/QuantScanProcessor.cs** — Isobaric labeling quantification mode with reporter ion detection and fold-change thresholds.

- **IDA/FLASHIdaWrapper.cs** (~1130 lines) — P/Invoke bridge to the C++ OpenMS.dll deconvolution engine. Manages ~20 exported C functions for MS1/MS2 deconvolution, MS3 targeting, and exclusion lists. Also contains `Main()` for standalone testing.

- **IDA/Parameter.cs** — Serializes method XML config to JSON for the C++ engine via `ToJSON()`. Phase 7 adds `<SelectionStrategy>` serialization.

- **DataPipe.cs** — Three-stage TPL Dataflow async pipeline (buffer → process → output) for concurrent scan processing.

- **ScanFactory.cs** — Creates Thermo API custom scan requests. Uses reflection to map string parameter dictionaries to API properties.

- **MethodParameters.cs** / **MethodConfig.cs** — Loads and structures the XML method file. Hierarchical config: GlobalParameters, PrecursorSelection, AcquisitionModes (targeting, quantification, MS3), MSSettings (MS1/MS2/MS3 parameters, FAIMS CVs).

**Deleted in Phase 6:** `ScanScheduler.cs`, `IDA/FAIMSScanProcessor.cs`, `IDA/IDAScanProcessor.cs`. All scan processing now routes through `UnifiedScanProcessor` → C++ engine. Phase 8 removes 17 more `[DllImport]` declarations from `FLASHIdaWrapper.cs` and deletes `ToFLASHDeconvInput()` from `Parameter.cs`.

### Acquisition Modes

Configured via `TargetingMode` in the method XML: None (standard DDA), Inclusion, Exclusion, Deep. Additional modes: MS2 Tagging (protein-family detection), Conditional MS2 (tag-based method routing), Isobaric Quantification, MS3 Characterization (3 sub-modes).

### Phase 8: Cleanup + Documentation (Current)

Phase 8 removes all legacy bridge infrastructure. After this phase: exactly 5 `[DllImport]` declarations remain in `FLASHIdaWrapper.cs`, `ToFLASHDeconvInput()` is deleted, `MethodDocGenerator.cs` is added, and `msbuild /warnaserror` passes with zero warnings. Key files:
- `IDA/FLASHIdaWrapper.cs` — Remove 17 `[DllImport]` declarations (leave 5: `CreateFLASHIda`, `DisposeFLASHIda`, `ProcessScan`, `GetNextScanCommand`, `GetNextTrackingId`)
- `IDA/Parameter.cs` — Remove `ToFLASHDeconvInput()` method and any sole-support helpers
- `IDA/MethodDocGenerator.cs` — New ~30-line reflection utility for `[Description]` attributes
- `Flash.Tests/CleanupTests.cs` — New NUnit tests: P8-U01 (5 DllImport count), P8-U02 (dead code scan), P8-U03 (MethodDocGenerator output)
- `Flash.Tests/Flash.Tests.csproj` — Add `<Compile Include="CleanupTests.cs" />` (explicit includes, NOT wildcard)

**Phase 7 context (complete):** `<SelectionStrategy>` XML is in all method XMLs, serialized to `selection_strategy` JSON by `Parameter.ToJSON()`. Missing blocks cause a crash. `method_exploration.xml` and `method_exploration_ms3.xml` exist in `test-data/configs/`.

### External Dependencies

- **Thermo iAPI DLLs** (proprietary, in `dependencies/`): `API-2.0.dll`, `Fusion.API-1.0.dll`, `Spectrum-1.0.dll`, `Thermo.TNG.Factory.dll`, `Thermo.TNG.Client.API.dll`
- **OpenMS C++ engine** (in `dll/`): `OpenMS.dll` plus Qt6, OpenSwathAlgo, zlib
- **NuGet**: log4net, Mono.Options, System.Threading.Tasks.Dataflow

### Logging

Two log4net loggers: general logger (console + FlashLog file) and IDA logger (IDALog file only, detailed precursor analysis). Configured in `App.config`.

### Method Configuration

XML-based (`src/Flash/etc/method.xml`). Sections: GlobalParameters, PrecursorSelection, AcquisitionModes, MSSettings (MS1/MS2/MS3Parameters, FAIMSParameters).

# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

FLASHIda is a real-time intelligent data acquisition (IDA) system for top-down proteomics on Thermo Scientific tribrid instruments. It controls which mass spectra the instrument acquires and in which order, using real-time deconvolution and machine learning-based quality assessment for precursor selection.

- **Language**: C# (.NET Framework 4.8, C# 7.3)
- **IDE**: Visual Studio 2019
- **Platform**: Windows x64 only (requires physical Thermo instrument or test mode)
- **Solution file**: `src/Flash.sln`

## Build

Open `src/Flash.sln` in Visual Studio 2019 and build, or use MSBuild (run `nuget restore src/Flash.sln` first):

```
msbuild src/Flash.sln /p:Configuration=Debug /p:Platform="Any CPU" /m
```

Debug output goes to `bin/` (both `Flash.exe` and `Flash.Tests.dll`). The Thermo iAPI DLLs must be placed in `dependencies/` before building (not checked in; see Installation.md). OpenMS runtime DLLs are committed in `dll/` and copied to `bin/` by MSBuild.

Automated tests exist: a C# NUnit suite in `src/Flash.Tests/` (run in CI via `nunit3-console.exe` against `bin/Flash.Tests.dll`), plus a PowerShell regression/golden harness (`test-scripts/regression-runner.ps1`) and the C++ ctest suite in the OpenMS submodule. The `-t/--test` flag runs `FLASHIdaWrapper.Main()` for offline deconvolution against text-file input without an instrument: `Flash.exe <input> <output> <method.json> [ms2_file]`.

## Architecture

### Data Flow

```
Instrument → IMsScan → DataPipe (BufferBlock → ActionBlock)
  → IScanProcessor.ProcessMS() → FLASHIdaWrapper.ProcessScan() (P/Invoke to OpenMS.dll; enqueues)
  ── then, separately, the acquisition loop in Flash.cs drains queued commands ──
  → FLASHIdaWrapper.GetNextScanCommand() → ScanFactory.BuildFromCommand() → Instrument
```

### Key Components

- **Flash.cs** — Entry point. Connects to instrument via Thermo Fusion API, manages instrument state, loads JSON method config (`method.json`), creates the scan processor, and runs the main acquisition loop.

- **IScanProcessor.cs** — Interface for scan processors: a single `void ProcessMS(IMsScan)`. In the unified model the processor enqueues work into the C++ engine; instrument commands are drained separately via `FLASHIdaWrapper.GetNextScanCommand()` in `Flash.cs` (they are *not* returned from `ProcessMS`).

- **IDA/UnifiedScanProcessor.cs** — The sole scan processor. Routes all MS levels through `FLASHIdaWrapper.ProcessScan()` → C++ `processScan()`, then commands are drained via `GetNextScanCommand()`.

- **IDA/FLASHIdaWrapper.cs** — P/Invoke bridge to the C++ OpenMS.dll deconvolution engine. Declares the 5 `[DllImport("OpenMS.dll")]` bridge functions and the mirrored `ScanCommand` struct. Also contains `Main()` for standalone testing. Isobaric quantification is *not* a separate processor — it is a config-driven mode (`quantification.active` in `method.json`) handled through `UnifiedScanProcessor` → the C++ engine (`TOPDOWN/FLASHIda/Quantification.cpp`).

- **DataPipe.cs** — Two-stage TPL Dataflow pipeline (`BufferBlock` → `ActionBlock`): buffers incoming scans, then invokes `IScanProcessor.ProcessMS` on each.

- **ScanFactory.cs** — Creates Thermo API custom scan requests. Uses reflection to map string parameter dictionaries to API properties.

- **MethodParameters.cs** / **MethodConfig.cs** / **IDA/MethodConfigSerializer.cs** — Load and structure the JSON method file (`method.json`). Reflection-driven via `[JsonKey]` + `[Developer]` attributes. Sections: `global`, `deconvolution`, `precursor_selection`, `tagging`, `quantification`, `faims`, `ms_settings`, `scheduling`, `selection_strategy`, `ms3`, `files`, `runtime`, plus a synthetic `developer` section into which `[Developer]`-marked fields are routed. `MethodParameters.ToCppJson()` re-serializes into a *different* C++-facing schema before crossing the bridge. See `docs/kb/config-flow/` for the end-to-end flow.

**No-longer-present:** `ScanScheduler.cs`, `IDA/FAIMSScanProcessor.cs`, `IDA/IDAScanProcessor.cs` were removed when scan processing was unified through `UnifiedScanProcessor` → C++ engine.

### Acquisition Modes

Configured via `precursor_selection.targeting_mode` in `method.json`: None (standard DDA), Inclusion, Exclusion, Deep. Additional modes: MS2 Tagging (protein-family detection), Conditional MS2 (tag-based method routing), Isobaric Quantification, MS3 Characterization (3 sub-modes).

### External Dependencies

- **Thermo iAPI DLLs** (proprietary, in `dependencies/`): `API-2.0.dll`, `Fusion.API-1.0.dll`, `Spectrum-1.0.dll`, `Thermo.TNG.Factory.dll`, `Thermo.TNG.Client.API.dll`
- **OpenMS C++ engine** (in `dll/`): `OpenMS.dll` plus Qt6, OpenSwathAlgo, zlib
- **NuGet**: log4net, Mono.Options, System.Threading.Tasks.Dataflow

### Logging

Two log4net loggers: general logger (console + FlashLog file) and IDA logger (IDALog file only, detailed precursor analysis). Configured in `App.config`.

### Method Configuration

JSON-based (`src/Flash/etc/method.json`). See `docs/kb/config-flow/` for the end-to-end flow from `method.json` to the C++ engine's `Config` structs.

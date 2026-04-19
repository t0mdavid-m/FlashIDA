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

- **Flash.cs** — Entry point. Connects to instrument via Thermo Fusion API, manages instrument state, loads JSON method config (`method.json`), creates the scan processor, and runs the main acquisition loop.

- **IScanProcessor.cs** — Interface for scan processors. `ProcessMS(IMsScan)` returns custom scans to submit; `OutputMS(IFusionCustomScan)` sends them to the instrument.

- **IDA/UnifiedScanProcessor.cs** — The sole scan processor. Routes all MS levels through `FLASHIdaWrapper.ProcessScan()` → C++ `processScan()`, then drains commands via `GetNextScanCommand()`.

- **IDA/QuantScanProcessor.cs** — Isobaric labeling quantification mode with reporter ion detection and fold-change thresholds.

- **IDA/FLASHIdaWrapper.cs** — P/Invoke bridge to the C++ OpenMS.dll deconvolution engine. Exports the C bridge functions for MS1/MS2 deconvolution, MS3 targeting, and exclusion lists. Also contains `Main()` for standalone testing.

- **DataPipe.cs** — Three-stage TPL Dataflow async pipeline (buffer → process → output) for concurrent scan processing.

- **ScanFactory.cs** — Creates Thermo API custom scan requests. Uses reflection to map string parameter dictionaries to API properties.

- **MethodParameters.cs** / **MethodConfig.cs** / **IDA/MethodConfigSerializer.cs** — Load and structure the JSON method file (`method.json`). Reflection-driven via `[JsonKey]` + `[Developer]` attributes. Sections: `global`, `deconvolution`, `precursor_selection`, `tagging`, `quantification`, `faims`, `ms_settings`, `scheduling`, `selection_strategy`, `ms3`, `files`, `runtime`. See `docs/kb/config-flow/` for the end-to-end flow.

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

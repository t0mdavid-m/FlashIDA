# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

**Scope: the C# side only.** This is a git submodule of the `flashida-development` workspace.
The parent `../CLAUDE.md` owns the bridge/ABI contract, CI, the local container system, build
commands, goldens and config flow, and wins on any conflict; `../OpenMS/CLAUDE.md` owns the C++
engine. Don't restate them here.

## Project Overview

FLASHIda is a real-time intelligent data acquisition (IDA) system for top-down proteomics on
Thermo Scientific tribrid instruments. It decides which spectra the instrument acquires and in
which order, using real-time deconvolution and quality scoring for precursor selection.

C# 7.3 / .NET Framework 4.8, x64, Windows only. Solution: `src/Flash.sln`. Output: `bin/`.

## `Flash.Flash` IS production. The `StartupObject` in git says otherwise, and it is lying.

Two classes define a `Main`, and `src/Flash/Flash.csproj:38` picks between them:

| `Main` | Contains | Built by the checked-in csproj? |
|---|---|---|
| `Flash.IDA.FLASHIdaWrapper.Main` (`IDA/FLASHIdaWrapper.cs:438`) | Offline deconvolution over a text spectrum file | **Yes — this is what CI and the Windows container build and test** |
| `Flash.Flash.Main` (`Flash.cs:154`) | Thermo instrument connection, method load + run-folder composition + log4net wiring, the whole Mono.Options CLI (`-t/--test`, `-m`, `-o`, `-r`, …) | No — but **this is what ships** |

**Do not read the second row as "dead code".** Owner-confirmed: the instrument path is what runs on
the hardware. The `StartupObject` is a developer toggle that gets flipped at deploy time and back
again, and the flip happens **outside version control** —
`git log -L 37,39:src/Flash/Flash.csproj` shows the file created at `Flash.Flash` (`9ed3653`,
2021-07-01) and **21 flips** since, the last to the offline harness (`46f0fc8`, 2025-07-07). So no
commit in over a year has produced an instrument-entry-point binary, and the deployed artifact is
built from a working tree that differs from `HEAD` in exactly this line.

It sits in an **unconditional** `PropertyGroup`, so it is not a Debug/Release or platform switch,
and nothing asserts its value.

Consequences that are easy to get backwards:

- **A bug in `Flash.cs` / `OnContactClosure` / `ProcessSpectrum` / `DataPipe` is a live production
  bug**, and wastes real instrument runs and samples. Do not deprioritise defects there.
- **log4net *is* configured in production** — `XmlConfigurator.Configure` has exactly one call site
  and it is inside the instrument `Main`. "log4net is never configured" is false for real runs.
- **`Flash.Flash` has zero automated coverage.** It needs a live Thermo
  `IFusionInstrumentAccess`/`IFusionScans`, so **neither a CI job nor a container** can execute it
  — the Windows container reproduces CI's suite, not the instrument — and defects there are found
  by inspection only.
- **"Just flip it back" is not a fix.** `regression-runner.ps1` and the CI *and container*
  golden-capture steps all drive `Flash.exe` through the offline positional interface; flipping the
  StartupObject breaks every one of them.

When the offline harness *is* the entry point, `Flash.exe` takes **positional args only**:
`<input_spectrum> <output.tsv> <method.json> [ms2_spectrum]`. There is no `-t/--test` flag — passing
`-t` makes it `args[0]` and the run dies with `Cannot open input file: -t`. Fewer than 3 args prints
usage and exits 1.

Everything in *Architecture* below describes `Flash.cs`. Treat it as what runs on the instrument,
because it is.

## Architecture

### Data flow (production / instrument path)

```
Thermo MsScanArrived  ──► ProcessSpectrum(IMsScan)          [instrument event thread]
                            │
                            ├─ if Trailer["Access ID"] == HandshakeJobNumber ──► inCustom = true  (latch)
                            │                                                     └─ ArmRunClock() restarts
                            │                                                        the run clock (ADR-0043)
                            │
                            └─ if (inCustom):
                                 dataPipe.Push(scan)
                                 │   └─ ScanData.From(scan)  ◄── the handle is read HERE, on this
                                 │        (7 owned values)        thread, while it is still live
                                 │   ─────────► BufferBlock<ScanData> ─► ActionBlock  [pool thread]
                                 │   (false ⇒ DROPPED, logged)       └─► UnifiedScanProcessor.ProcessMS
                                 │                                        └─► FLASHIdaWrapper.ProcessScan
                                 │                                   (nobody disposes the scan)
                                 └─ TOP UP to scheduling.target_depth (default 2)
                                      └─ GetNextScanCommand ─► ScanFactory.BuildFromCommand ─► SendCustomScan
```

⚠️ **An uncommanded arrival (`Access ID == "0"`) is neither ingested nor answered.** Both halves
key on the same fail-open predicate. The ingest half was added after the 2026-08-25 Eclipse run,
where the instrument method's own scans came back with a **three-blank-character** description —
which clears `processScan`'s `size() < 3` gate — so each one crossed the bridge, took
`analysis_mutex_`, and flushed `[TRACK-RESOLVE] id=<blank> status=not_found` to stdout ~13.7×/s
against log4net's `ConsoleAppender` on the arrival thread. **An AGC prescan is still pushed** (only
`resolvePending` erases its pending-map entry, and only `processScan` reaches it) but pushed
**empty**: `ScanData.From` skips the centroid enumeration when `description[3] == 'A'`, because the
engine discards those peaks unread and there are ~12 000 of them per prescan.

Five things this diagram exists to correct:

- **`Flash.cs` has no acquisition loop.** Its only loop is a do-nothing busy-wait
  `while (!stopRequest) { }` on the Main thread (`Flash.cs:287-290`) that burns a core and does
  no work. Acquisition is entirely event-driven — the "loop" is the chain of `MsScanArrived`
  callbacks, each doing one ingest plus one drain.
  **What follows that loop is the run's teardown** (ADR-0041), and it is the whole reason the loop
  is worth finding: `Main`'s tail unsubscribes `MsScanArrived`, calls `IScans.CancelCustomScan`, and
  logs the depth it stopped at. It runs on the Main thread, once, in written order — not inside
  `RequestStop`, which is called from four different threads and would race this.
- **Nothing happens until the handshake latch fires.** `inCustom` is set only when a scan comes
  *back* with `Trailer["Access ID"] == HandshakeJobNumber` — the echo is what proves the instrument
  entered custom control, so it can never be latched at send time. **Both** startup paths must send
  the same `BuildHandshakeScan()`; a path that leaves the job number to chance is a defect by
  construction. (The contact-closure path once built the handshake from `GetNextScanCommand`, which
  stamped the engine's first tracking id — `0` — so the latch never fired and the run acquired
  nothing. See ADR-0008.)
- **The drain tops the instrument up to `scheduling.target_depth`** (default **2**), in a loop
  bounded by that target — *not* one command per arriving scan, which is what it was until
  ADR-0033. One send per arrival can only oscillate the count between 0 and 1; it can never
  **reach** 2, so a single `if` there reads like the fix and changes nothing. At depth 1 the
  instrument's queue is empty after every scan and a Tribrid does not wait — it acquires its own
  method. Measured on hardware: **53 % of the duty cycle**, 144 method scans against our 47 in
  17 s. `target_depth: 1` restores ADR-0032's behaviour with no rebuild.
  The loop carries a **second, independent bound on attempts**: `outstanding` is incremented only
  inside the success path, so a throwing `BuildFromCommand` would otherwise spin it forever on the
  instrument event thread. `GetNextScanCommand` is no bound at all — it never returns 0 (see below).
  A burst of commands from one `processScan` still drains across subsequent arrivals.
  It also reads `!stopRequest`, which is the **latch** half of latch-then-cancel (ADR-0041): a
  `CancelCustomScan` with no latch in front of it is undone by the very next arrival, and the iAPI
  guarantees arrivals continue after an acquisition closes.
  ⚠️ **"Success" means the instrument ACCEPTED it, not that nothing threw.** `SetFusionCustomScan`
  returns a `bool` and that return was discarded for years, so a declined command was counted as
  outstanding while nothing would ever arrive to discharge it — two of those and the real queue sits
  at 0 while the counter reads `target_depth`, which is **absorbing**: no queued command, no
  commanded arrival, no decrement, and the loop never fires again for the rest of the run. A refusal
  now `break`s (one attempt per arrival, so a return value that *lies* parks the real queue at depth
  1 instead of ratcheting), and the decrement is clamped at 0.
- **Nobody disposes the `IMsScan`.** It is a handle to *framework-owned* shared memory that the
  iAPI releases by itself once the next scan replaces it as the container's `LastScan`
  (`dependencies/API-2.0.xml`, `IMsScan`). This has now been got wrong in both directions, each
  time costing instrument runs: disposing on the **producer** side freed memory the pool thread was
  still lazily reading (`Centroids`/`Header`/`Trailer`, a window the whole queue deep), and moving
  the call to the **pool** side instead disposed late, out of arrival order, from a thread that
  never acquired the handle — which threw out of a `finally` sitting *outside* the delegate's
  `try/catch` and faulted the `ActionBlock` on the first scan of the run. There is no ownership
  protocol to get right any more, because there is no disposal.
  **The pool thread no longer reads the handle at all** — `Push` snapshots it into a `ScanData`
  first — so the *lifetime* hazard that made both mistakes so expensive is gone as well. That is a
  reason to be more suspicious of a new `Dispose`, not less: "we've copied what we need, release
  it" is the shape the producer-side mistake takes now. Pinned by `DataPipe_DoesNotDisposeScan`.
- **`processScan` and `getNextScanCommand` genuinely run on different threads** — the pool
  thread and the instrument event thread respectively. The engine's mutexes are load-bearing.

### Scan identity round-trips through `Scan Description`, not `Access ID`

The engine recognizes a returning scan by the **first 3 characters of the `Scan Description`
trailer** — a base-94 tracking id it minted itself. `ScanFactory` copies `cmd.ScanDescription`
into the scan parameters (`ScanFactory.cs:257-258`), the instrument echoes it back, and
`UnifiedScanProcessor` passes `msScan.Trailer["Scan Description"]` across the bridge as the only
identity token (`UnifiedScanProcessor.cs:21-28`).

`cmd.ScanId` *is* written to `RunningNumber` by `BuildFromCommand`, but `SendCustomScan`
immediately overwrites it with `++currentNumber` (`Flash.cs:397`) — harmless, because it is not
the round-trip key. **A scan whose description the engine did not mint is rejected before
deconvolution.** That is the always-on MS1 gate, and it is why tests cannot fabricate ids.

### `GetNextScanCommand` never returns 0 — bound every drain loop

The engine returns `1` on every path; an empty queue produces a fabricated **idle survey** — an MS1
at priority 3 — not a `0`. A bare `while (GetNextScanCommand(ref cmd) == 1)` **spins forever**. Each
sanctioned driver carries its own stop condition: `PushScan` and the two offline-harness loops in
`FLASHIdaWrapper` break on `cmd.MsnLevel == 1 && cmd.Priority == 3`; `PushScanAndDrainFull` bounds on
`idle >= 3` plus `maxIters`. A `0` from the C# wrapper means an exception was caught, never
"queue empty".

⚠️ **`IsAgc` is not a stop condition** (ADR-0031). The idle path used to fabricate an AGC prescan,
which is what those three loops broke on; it no longer does, and a loop still testing `IsAgc` hangs.
Prescans now come only from `scheduling.agc_interval_seconds` — production default **1 s**, and all
43 committed test configs pin it at `9999999` so golden capture cannot depend on wall clock. Because
a *scheduled* prescan can arrive mid-drain, breaking on `IsAgc` would also **truncate** the drain and
drop the MS2 commands behind it; a prescan falls through the `MsnLevel == 2` guard and costs one
harmless iteration instead. Priority 3 works as the sentinel because `makeMS1()` sets it and every
other caller overrides to 0 (cycle-time, CV-transition), while MS2 is 2 and MS3 is 1. Pinned on both
test paths — `IdleSurveySentinelTests` (C#) ∥
`FLASHIda_ProcessScan_test::only_the_idle_survey_is_emitted_at_priority_3` (C++).

### Key components

- **`IDA/FLASHIdaWrapper.cs`** — the P/Invoke bridge: 5 `[DllImport]`s, the mirrored 2048-byte
  `ScanCommand` struct, and the offline `Main`. Its **static constructor unconditionally sets
  `OPENMS_DATA_PATH` to `<assembly dir>/share/OpenMS`**, so any externally exported value is
  discarded for every C# process. Constructor serializes via `mp.ToCppJson()` and throws
  `InvalidOperationException` if `CreateFLASHIda` returns null. Standard Dispose pattern; the
  null-pointer guard makes double-dispose safe.
  - Data-path error sentinels (all swallow exceptions into log4net): `ProcessScan` → `-1`,
    `GetNextScanCommand` → `0`, `GetNextTrackingId` → `-1`. The native null-argument guards use
    the same values, so the two error sources are indistinguishable to the caller.
  - **Offline `Main` bootstraps a tracking id before feeding MS1**: it drains up to 16 commands
    hunting the first non-AGC MS1 with a non-empty description, uses it, and discards the rest
    (`:377-398`). Without this the MS1 gate would reject every scan and the run would silently
    deconvolve nothing. Note every scan in the input file is fed as **ms_level 1** (`:488`), and
    the 15-column TSV header is written unconditionally — a run that selects nothing yields a
    header-only file.
- **`IDA/UnifiedScanProcessor.cs`** — the *sole* `IScanProcessor`. One `void ProcessMS(IMsScan)`;
  all MS levels go through `ProcessScan`. Commands are drained separately, not returned from here.
- **`ScanFactory.cs`** — builds Thermo custom scan requests. The reflection goes **struct →
  dictionary**, not dictionary → API properties: it enumerates `ScanParameters`' *fields*, skips
  nulls, joins arrays with `';'`, and replaces `'_'` with `' '` in the key (`FAIMS_CV` → `"FAIMS CV"`)
  (`ScanFactory.cs:133-145`).
  - **Every number is formatted with `InvariantCulture`** (`Fmt`, `ScanFactory.cs`). Not cosmetic: a
    plain `ToString()` follows the machine locale, and on a comma-decimal one (this workspace is
    `de-DE`) an m/z of 1000.5 became `"1000,5"` — which the iAPI grammar reads as **two** isolation
    windows, at m/z 1000 and m/z 5. `ScanFactoryCultureTests` *imposes* `de-DE` via `[SetCulture]`
    to catch it, and asserts that its own `[SetCulture]` took effect
    (`ScanFactoryCultureTests.cs:55-57`) — so it is **not** a canary for the host's own locale.
  - **The test host must be `en-US`, and the Windows container must pin it and ASSERT the pin.** CI
    runners are `en-US`; this workspace is `de-DE`. `Mocks/MockMsScan.cs` parses every spectrum
    fixture value with a bare, culture-sensitive `double.Parse` (`:265`, `:274`, `:275`, `:338`,
    `:354`, `:355`), and `FromTsv` / `FromTsvAsMS2` / `FromTsvAsMSn` are what the whole golden and
    continuity suite feeds through — under `de-DE`, `double.Parse("674.6919")` returns **6746919**.
    A wrong host locale therefore surfaces as fabricated fixture values, never as a locale error, so
    the entrypoint's `en-US` assertion is the only thing that names the real cause.
  - **`PrecursorMass` / `IsolationWidth` / `ChargeStates` are `string[]`, not numeric arrays** — each
    element is a pre-formatted `','`-joined **group** for one cascade stage, because the wire carries
    two axes: `';'` descends an MSⁿ stage, `','` widens one into co-isolation notches (ADR‑0016;
    `docs/kb/scan-pipeline/multi-notch-wire-grammar.md`). `CollisionEnergy` / `ActivationType` /
    `ReactionTime` / `Reagent*` stay one value per stage — all notches of a stage share one
    fragmentation event. `NotchesForStage` mirrors the C++ accessor and must stay in lockstep — it
    reads stage `k`'s **fixed block** of the `Notches[18]` array (`[k * MaxNotchesPerStage, +9)`), not
    the tail of `Stages[]`, so either cascade stage can carry a full 10-plex and neither can consume
    the other's slots (ADR‑0019). `Notch` is 24 bytes and deliberately has **no** CollisionEnergy or
    ActivationType field. The three caps are named consts on `ScanFactory`, mirroring the C++ ones;
    `MaxNotchesPerStage + 1` and `MaxIsolationStages` are **different tens** on different axes.
  - **`ReactionTime` is gated on the stage's ACTIVATION, never on its value** (ADR‑0030).
    `reaction_time == 0` means two different things — "not applicable" on an HCD/CID stage, and a
    literal value on an ETD-family one. The old `if (reactionTimes.Any(v => v > 0))` conflated them
    and dropped the whole key, so an ETD scan at reaction time 0 silently inherited whatever default
    the instrument method carried while the engine logged 0 — a logged-vs-commanded disagreement with
    nothing to notice it.
    ⚠️ **The instrument rejects a reaction time of 0**, so the engine floors a swept ETD baseline to
    `MIN_REACTION_TIME_MS` (0.03) and never asks for 0 down that path. What the activation gate buys
    is therefore the *authored* path: an `ms_settings` ETD block at `reaction_time: 0` still loads, and
    its 0 now reaches the device and is refused **loudly** instead of vanishing into the method
    default. Do not "simplify" the gate back to a value test on the grounds that nothing emits 0.
    A pure HCD/CID scan still omits the key, so ADR‑0009's defer-to-the-method rule survives for every
    activation with no ion-ion reaction. The **Reagent keys deliberately keep their `> 0` gate**: a
    zero reagent AGC target or max IT has no useful meaning. `ScanFactory.NeedsReactionTime` is the C#
    half of a mirrored pair with the engine's `needsReactionTime`; both are pinned as **exact sets**
    (`ScanFactoryTests` ∥ `Config_SchemaProjection_test`), because an over-broad predicate starts
    commanding a reaction time on scans that have none and no other assertion would see it.
    ⚠️ This hop is **invisible to every golden**: the five log streams are written by the C++
    `IdaLogger`, so a defect between the `ScanCommand` struct and the `Values` dictionary can only be
    caught by a `ScanFactoryTests`-style assertion on the built scan.
  - **`FillParameters` is `protected`, and `MockScanFactory` calls it.** It used to be `private` with a
    hand-copied twin in the mock, so every test asserting on `Values` was checking the copy rather
    than production — and the two had drifted on exactly the number formatting above.
- **`DataPipe.cs`** — `BufferBlock` → `ActionBlock` with an explicit `MaxDegreeOfParallelism = 1`,
  so `ProcessMS` calls are serialized. It was always 1, but as a TPL *default*; it is now stated,
  because the engine leans on it — `processScan` is serialized against itself by this block and by
  nothing else, which is why `analysis_mutex_` only has to defend against the drain.
  - **The queue holds a `ScanData` snapshot, not the `IMsScan`.** `Push` takes the handle and copies
    the seven values the engine needs (`ScanData.From`) **on the caller's thread**, i.e. while the
    handle is still live. An `IMsScan` is a window onto framework-owned memory the iAPI releases as
    soon as the next scan replaces it as `LastScan`, so a queued *handle* is only safe while the
    queue is ~1 deep — which it was, by accident, because the command drain blocked behind the
    deconvolution. Removing that stall is what made a deep queue reachable.
    ⚠️ **Anything the engine needs is added to `ScanData.From`, never read at the consumer.** A field
    fetched lazily from the handle later reintroduces exactly this defect.
  - **The queue is deliberately unbounded and nothing is ever dropped.** Bounding it was considered
    and rejected: a dropped exploration variant wedges its group for the rest of the run
    (`Exploration::active_groups_.erase` is reachable only past the `all_received` gate, no timeout)
    and leaks its pending-map entry. If `D > T` persistently the backlog grows — watch RSS, and fix
    the throughput rather than start discarding data.
  - **`Push` is not an ownership transfer** — nothing here disposes the scan (see the diagram
    bullet above). Its `bool` says only whether the pipeline accepted it, and `false` means the scan
    was **dropped and never processed**, so `ProcessSpectrum` logs it rather than ignoring it. There
    are two ways to get one: the block refusing it (completed only by tests, so not a production
    path), and a scan that could not be *read* — a malformed header, or a handle already released.
    The second takes the same FATAL + `onFailure` route a processing failure takes, because it used
    to be one: the parsing ran inside the consumer's `try/catch` before it moved to `Push`. It is
    caught rather than thrown because `Push`'s caller is the instrument event thread.
  - **The `ActionBlock` must never fault, and no statement may sit outside its `try/catch`.** An
    escaping exception faults it *permanently*, severing the link while `GetNextScanCommand` keeps
    handing the instrument idle survey MS1s — a silent, total loss of acquisition. It is invisible
    from every side: `Post` still returns `true`, so the producer never learns, and `Completion` is
    otherwise awaited only by tests. The delegate therefore catches, logs FATAL and invokes the
    required `onFailure` callback (which ends the run) — **and that catch body is itself guarded**,
    because a throwing `onFailure` or logger would kill acquisition in the act of reporting that
    acquisition had a problem. `Completion` also gets an explicit fault observer, for the
    exceptions `catch (Exception)` cannot hold.
    > The `finally { scan.Dispose(); }` that used to live here is precisely how this happened for
    > real. Adding *anything* outside the `try/catch` re-opens it.
  - Only `DataPipeTests` exercises the async path — the NUnit continuity harness calls `ProcessMS`
    directly. `Complete()`/`WaitForCompletion()` are called only from tests: production still never
    drains the pipeline and never disposes the wrapper (`DisposeFLASHIda` runs on the finalizer, if
    ever). **Both omissions are now deliberate rather than incidental** (ADR-0041): every one of the
    engine's five streams `.flush()`es per row and `FLASHIda::~FLASHIda()` is `= default`, so there
    is nothing to lose by exiting and nothing for a join to wait for — and because teardown disposes
    nothing, there is no use-after-free for a pipeline join to prevent either.
    Shutdown is `RequestStop(reason)`, one-shot, returning whether **this** call latched. It records
    the reason and *then* publishes the `volatile` `stopRequest` in a `finally`, because that flag
    releases `Main` into a teardown that returns from the process — publish it first and the line
    saying why the run stopped can lose the race. `Main`'s tail then **does** unsubscribe
    `MsScanArrived` and cancel the instrument's outstanding custom scans; `AcquisitionStreamClosing`
    (armed on `inCustom`) and `Console.CancelKeyPress` join the **run clock** as triggers.
    ⚠️ **The run clock is armed at startup but does not START until the handshake echoes**
    (ADR-0043). `ArmRunClock` serves three sites with two meanings, told apart by `duration == null`:
    both startup paths arm it *before* the handshake goes out — so a handshake that never echoes
    still bounds the run, which is load-bearing because the send is wrapped in a `catch` that logs
    and carries on and `AcquisitionStreamClosing` is armed on `inCustom`, leaving this timer as the
    only stop trigger besides `^C` — and the latch restarts it, so `global.duration` is measured
    from the echo. It is deliberately **not** keyed to `AcquisitionStreamOpening`, the event
    actually named for "the acquisition started": a scan executes and echoes with no acquisition
    open at all, and `InstrumentConnected` commands exactly that state via `SetMode(CreateOnMode())`
    a few lines earlier. Worst-case process lifetime is `duration + (send → echo)`.
- **`MethodConfig.cs` / `MethodParameters.cs` / `IDA/MethodConfigSerializer.cs`** — see *Config*.

`ScanScheduler.cs`, `IDA/FAIMSScanProcessor.cs`, `IDA/IDAScanProcessor.cs` and
`QuantScanProcessor.cs` do **not** exist — scan processing was unified.

## Config (C# side)

The 12 top-level sections are the `[JsonKey]`-annotated **properties of the `MethodConfig` class**,
plus the special-cased root bool `conditional_ms2`:
`global`, `deconvolution`, `precursor_selection`, `tagging`, `flashtnt`, `quantification`,
`faims`, `ms_settings`, `scheduling`, `characterization`, `files`, `runtime`.
`selection_strategy` was the thirteenth and is **deleted** (ADR-0014); both loaders throw a
migration message on it.

> **Do not enumerate sections by grepping for class-level `[JsonKey]`.** Nested classes carry one
> too (`ms1`/`ms2`/`ms3`/`cycle_time`/`scan_timeout`/`exploration`), which is exactly how a phantom
> top-level `ms3` entered the old version of this file. C++ now throws on a top-level `ms3` with a
> migration message.

`ms1`/`ms2`/`ms3` now appear **only** under `ms_settings`, and all three are bare **structs**
(`MS1Parameters` / `MS2Parameters` / `MS3Parameters`) — `ms2` and `ms3` used to be `List<>`.
Extra MS2 configs live in `ms_settings.additional_ms2`, a
`Dictionary<string, MS2Parameters>` whose keys are user-authored.

`BuildAllowedKeyMap` dispatches on struct-vs-class at runtime (structs validate against **fields**,
classes against **properties**), so adding a scan key means a `[JsonKey]` on a struct field and
adding a decision key means one on a class property.

> **A `Dictionary<,>` is validated key-free but value-checked.** `CollectUnknownKeys` used to return
> outright for any dictionary ("keys are dynamic, allow anything"), which is right for
> `exploration.overrides` (string values) and wrong for `additional_ms2` (full scan objects) — it
> would let `{"etd": {"IsolationMode": "Quad"}}` load clean and silently drop the key. Keys stay
> free; values recurse.

### Config gotchas that cost real debugging time

- **`deconvolution.tol` needs ≥ 3 entries, always.** C++ materializes levels {1,2,3}
  unconditionally and requires `tol.size() >= 3`. The C# model default is `{10,10}` — two entries —
  which produces an **unloadable config**. Every committed test config carries exactly 3.
- **The C++ `.value(key, default)` fallbacks are dead in production.** `ToCppJson` emits every key
  unconditionally, so the *effective* defaults are the C# property initializers — and several
  disagree with the C++ literal a reader would find: `score_threshold` −1 vs 0.0,
  `reporter_mz_tol` 0.0 vs 0.002, `fold_change_threshold` 0.0 vs 1.4.
  **This bites hardest in hand-written C++ test fixtures**, which bypass the emitter entirely and so
  DO get the C++ literals: the MS3 budget defaulted to 10 there and to 3 here, so a fixture that
  omits `characterization.max_targets` silently changes budget.
- **There is no level-1 exploration.** It was modelled and emitted but discarded on both sides; only
  `precursor_selection.exploration` (MS2) and `characterization.exploration` (MS3) exist now.
- **An exploration block with `metric: "none"` has its sweep values silently rewritten** to
  ce 20/40/5. The forwarding guard is an ordinal, case-sensitive `!= "none"`, so `"None"` takes
  the *other* branch. Each block now gets a **fresh** instance — the old code shared one
  `defaultExpl` object by reference across all three levels.

Unknown keys are hard-rejected here and again in C++. `test-data/config_schema_reference.json` is
generated from the schema — regenerate it, never hand-edit.

## Tests (`src/Flash.Tests/`)

### One canonical acquisition drive — never hand-roll one

`ContinuityTestHarness.PushScanAndDrainFull` (C#) and `FLASHIda_TestHelpers.h::runInterleaved`
(C++) are two mirrors of one contract: pull one command → classify idle vs workload → feed exactly
one response scan stamped with **that command's own engine-emitted `ScanDescription`** → repeat.
Idle predicate, identical both sides:
`IsAgc != 0 || string.IsNullOrEmpty(ScanDescription) || (level <= 1 && ms1Fed >= nMs1)`.
Terminate on `idle >= 3` or `maxIters` (C# default 600).

You cannot invent a scan id — the engine's MS1 gate rejects any description it did not mint. That
gate is what forces interleaved driving, and it is pinned by
`FLASHIda_ProcessScan_test.cpp` `processScan_ms1_gate_rejects_unrequested_id`.

> **Vacuity trap.** `MockMsScan`'s default description is the sentinel `"~~~S"`, chosen to decode
> above any engine id so it never collides — which means it always **fails** the pending-map gate.
> An MS1 fed via raw `PushScan` is rejected before deconvolution regardless of its contents. Use
> `PushMs1` (drains a real survey id and re-stamps) or `PushScanAndDrainFull`. CT04/CT05
> (`EmptySpectrum_ZeroCommands`, `NoiseOnlySpectrum_ZeroCommands`) currently pass **by the gate,
> not by deconvolution behaviour**. CT31 is deliberately left as a raw loop to pin the
> never-returns-0 ABI invariant.

### Two capture channels, different fidelity

- `harness.CapturedRecords` — `ScanCommandRecord.FromScanCommand`, the **raw struct** including
  every scoring field, `ReactionTime` and `ParentScanId`.
- `harness.CollectResults()` — re-reads the **built** `IFusionCustomScan` `Values` dictionary and
  filters `ScanType == "MSn"`; sees no scoring fields and infers `MsnLevel` from the `';'`-count of
  `"PrecursorMass"`.

Continuity goldens serialize via `ToJsonObject`, which deliberately omits `ReactionTime` and
`ParentScanId` and formats doubles `G17`.

> **A capture run proves nothing.** With `LOG_GOLDEN_CAPTURE=1`, `RunCase` writes the goldens and
> calls `Assert.Pass(...)` then returns — **no comparison happens**, so the suite is always green.
> Never conclude "tests pass" from a capture run.

Golden comparison tolerates float drift (ints/strings/structure stay exact) because a fresh
`OpenMS.dll` is linked on every CI run and is not bit-reproducible. **A ccache-warm container can
relink an identical DLL, so zero local jitter is not evidence the tolerance is unnecessary** — and a
container DLL gone stale against the OpenMS SHA reintroduces exactly the bridge/ABI drift the
fresh-DLL swap exists to detect. The container's DLL is also configured `WITH_GUI=OFF` where CI's is
`ON`. **A golden captured locally must still survive a CI run before it is trusted.** Golden
locations and recapture paths: see `../CLAUDE.md`.

## Dependencies, logging, data

- **Thermo iAPI (proprietary, in `dependencies/`)** — `API-2.0.dll`, `Fusion.API-1.0.dll`,
  `Spectrum-1.0.dll`, `Thermo.TNG.Factory.dll` (all from the iAPI GitHub release) and
  `Thermo.TNG.Client.API.dll` (copied from a local Tune install, see `Installation.md`).
  `Thermo.TNG.Client.API` carries `<Private>False</Private>` in `Flash.csproj`, so **the Flash
  project deliberately does not copy it to `bin/`** — it is a *licensed, Tune-version-specific* DLL
  that must not be redistributed, and on the instrument it resolves against the local Tune install
  (hence `<SpecificVersion>False</SpecificVersion>`). This is original design, not drift: it dates
  to the initial public commit, and `Installation.md`'s deployed-folder listing pointedly omits it
  while listing the other four. `Flash.Tests.csproj` references it *without* that flag, so a
  full-solution build incidentally drops a copy into `bin/`; CI depends on neither, copying
  `dependencies\*.dll` explicitly after msbuild. **Do not "fix" the asymmetry** — it encodes
  "the app must not ship this DLL; the test host may have a local copy."
- **OpenMS runtime (in `dll/`)** — the 5 DLLs are `<None Include>` items with `<Link>` +
  `CopyToOutputDirectory=PreserveNewest`, so they land flat in `bin/`. CI **and the Windows
  container** overwrite 4 of the 5 with a freshly built engine before the C# build; `zlib.dll` stays
  committed. **The container must reproduce that swap, not skip it** — it is the bridge/ABI drift
  detector, and CI gets it for free from a fresh checkout while a container does not. Two local-only
  hazards CI cannot hit: `dll/` is a **tracked** directory, so the swap leaves 4 modified tracked
  files that a `finally` must restore (there is no opt-out flag); and `Copy-Item -Force` preserves
  the **source** mtime, so a freshly built DLL can be *older* than a stale `bin/` copy and
  `PreserveNewest` then silently skips it — delete `bin/` before every build.
- **`share/OpenMS` exists twice and has drifted** — `FlashIDA/share/OpenMS` (148 files, pruned) is
  copied to `bin/share/OpenMS` and is what every C# process actually reads;
  `OpenMS/share/OpenMS` (254 files) is what `OPENMS_DATA_PATH` points at for ctest.
- **log4net** (`App.config`) — 2 loggers, 4 appenders. `General` → colored console (threshold INFO)
  + `FlashLog.log`. `IDA` → `IDALog.log` (bare `%message`, machine-parseable) + `IDAInfoForward`,
  whose `LevelRangeFilter` is
  `levelMin=INFO` **and `levelMax=INFO`**, so IDA WARN/ERROR could never reach the console.
  That is inert rather than a defect: `LogManager.GetLogger("IDA")` (`FLASHIdaWrapper.cs:111`) is a
  write-only field — **nothing logs to the IDA logger at all**, and every real warning/error goes to
  `General`, which passes WARN/ERROR through. Worth knowing only if you ever start using it —
  `IDALog.log` therefore ships as a 0-byte file every run.
  **Both appender `<file>` values are overwritten at startup** by `Flash.Main` with absolute paths
  inside the run folder that `LogPathResolver.Compose` built, so these two files sit alongside the
  engine's five streams under one shared timestamp. Two details that were live defects and are now
  load-bearing: the `type="log4net.Util.PatternString"` attribute is **gone** (a `%` in an injected
  literal path is a conversion specifier, dropped silently), and `appendToFile=false` is now
  **explicit on both** — it was absent on `IDAFile`, i.e. log4net's default of `true`, so IDALog
  appended while its sibling truncated.
- **NuGet** — log4net, Mono.Options, System.Threading.Tasks.Dataflow.

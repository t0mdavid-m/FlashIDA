param (
    [string]$FlashExe = "FlashIDA\bin\Flash.exe",
    [string]$TestDataDir = "FlashIDA\test-data",
    [string]$OutputDir = "FlashIDA\test-output",
    [switch]$captureMode
)

# Non-capture mode: wipe the output dir first so a stale TSV from a prior run can never be
# compared against a golden (two cases can share a golden). Capture mode writes to its own
# dir and keeps existing contents.
if (-not $captureMode -and (Test-Path $OutputDir)) {
    Remove-Item -Recurse -Force $OutputDir
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

if (-not (Test-Path $FlashExe)) {
    Write-Host "FAIL: Flash.exe not found at $FlashExe"
    exit 1
}

# Copy supporting config files (inclusion lists, FASTA) to working directory
# so C++ engine can resolve bare filenames in method config
$configDir = Join-Path $TestDataDir "configs"
foreach ($supportFile in @("test_inclusion_list.txt", "test_fasta.fasta", "test_target_log.log")) {
    $src = Join-Path $configDir $supportFile
    if (-not (Test-Path $src)) {
        Write-Host "FAIL: required support file missing: $src"
        exit 1
    }
    Copy-Item $src . -Force -ErrorAction Stop
}
Write-Host "Copied supporting files from $configDir to $(Get-Location)"

$configs = @(
    @{
        name   = "baseline_phase0"
        method = "method_default.json"
        ms1    = "ms1_smoke_test.txt"
        ms2    = $null
        golden = "baseline_phase0.tsv"
    }
    # Phase 1: JSON config path (same golden as Phase 0 — must produce identical output)
    @{
        name   = "p1_json"
        method = "method_default.json"
        ms1    = "ms1_smoke_test.txt"
        ms2    = $null
        golden = "baseline_phase0.tsv"
    }
    # Phase 4: Legacy bridge path regression (UseUnifiedBridge=false)
    @{
        name   = "p4_legacy_path"
        method = "method_default_legacy.json"
        ms1    = "ms1_smoke_test.txt"
        ms2    = $null
        golden = "baseline_phase3.tsv"
    }
    # Phase 4: Pre-switch golden baselines (old bridge path, ms1_standard.txt)
    @{
        name   = "phase4_standard_dda"
        method = "method_default.json"
        ms1    = "ms1_standard.txt"
        ms2    = $null
        golden = "phase4_standard_dda.tsv"
    }
    @{
        name   = "phase4_deep_mode"
        method = "method_deep.json"
        ms1    = "ms1_standard.txt"
        ms2    = $null
        golden = "phase4_deep_mode.tsv"
    }
    @{
        name   = "phase4_inclusion"
        method = "method_inclusion.json"
        ms1    = "ms1_standard.txt"
        ms2    = $null
        golden = "phase4_inclusion.tsv"
    }
    @{
        name   = "phase4_inclusion_strict"
        method = "method_inclusion_strict.json"
        ms1    = "ms1_standard.txt"
        ms2    = $null
        golden = "phase4_inclusion_strict.tsv"
    }
    @{
        name   = "phase4_exclusion"
        method = "method_exclusion.json"
        ms1    = "ms1_standard.txt"
        ms2    = $null
        golden = "phase4_exclusion.tsv"
    }
    @{
        name   = "phase4_tag_targeting"
        method = "method_tag_targeting.json"
        ms1    = "ms1_standard.txt"
        ms2    = "ms2_hcd_fragment.txt"
        golden = "phase4_tag_targeting.tsv"
    }
    @{
        name   = "phase4_quant"
        method = "method_quant.json"
        ms1    = "ms1_standard.txt"
        ms2    = "ms2_quant_tmt.txt"
        golden = "phase4_quant.tsv"
    }
    @{
        name   = "phase4_ms3_mode1"
        method = "method_ms3_mode1.json"
        ms1    = "ms1_standard.txt"
        ms2    = "ms2_hcd_fragment.txt"
        golden = "phase4_ms3_mode1.tsv"
    }
    @{
        name   = "phase4_ms3_mode2"
        method = "method_ms3_mode2.json"
        ms1    = "ms1_standard.txt"
        ms2    = "ms2_hcd_fragment.txt"
        golden = "phase4_ms3_mode2.tsv"
    }
    @{
        name   = "phase4_ms3_mode3"
        method = "method_ms3_mode3.json"
        ms1    = "ms1_standard.txt"
        ms2    = "ms2_hcd_fragment.txt"
        golden = "phase4_ms3_mode3.tsv"
    }
    # Phase 7: Exploration enabled (CE sweep, mass_count metric)
    @{
        name   = "p7_exploration"
        method = "method_exploration.json"
        ms1    = "ms1_standard.txt"
        ms2    = $null
        golden = "phase7_exploration.tsv"
    }
)

$failures = 0

foreach ($cfg in $configs) {
    $outputFile = Join-Path $OutputDir "$($cfg.name).tsv"
    $ms1File    = Join-Path $TestDataDir "spectra\$($cfg.ms1)"
    $methodFile = Join-Path $TestDataDir "configs\$($cfg.method)"
    $goldenFile = Join-Path $TestDataDir "golden\$($cfg.golden)"

    Write-Host "Running: $($cfg.name) ..."

    $flashArgs = @($ms1File, $outputFile, $methodFile)
    if ($cfg.ms2) {
        $ms2File = Join-Path $TestDataDir "spectra\$($cfg.ms2)"
        $flashArgs += $ms2File
    }

    Write-Host "  Args: $($flashArgs -join ' ')"
    Write-Host "  CWD: $(Get-Location)"
    $global:LASTEXITCODE = $null
    try {
        & $FlashExe @flashArgs 2>&1 | ForEach-Object { Write-Host "  [Flash] $_" }
    } catch {
        Write-Host "FAIL: Flash.exe failed to launch for $($cfg.name): $_"
        $failures++
        continue
    }

    if ($null -eq $LASTEXITCODE -or $LASTEXITCODE -ne 0) {
        Write-Host "FAIL: Flash.exe exited with code $LASTEXITCODE for $($cfg.name)"
        $failures++
        continue
    }

    if ($captureMode) {
        if (-not (Test-Path $outputFile) -or ((Get-Item $outputFile).Length -eq 0)) {
            Write-Host "FAIL: capture produced no/empty output for $($cfg.name): $outputFile"
            $failures++
            continue
        }
        Write-Host "CAPTURE: $($cfg.name) -> $outputFile"
        continue
    }

    # The output dir was cleaned at startup, so a missing/empty file here means Flash.exe
    # exited 0 without producing output — fail closed instead of relying on
    # compare_golden.py's FileNotFoundError to catch it.
    if (-not (Test-Path $outputFile)) {
        Write-Host "FAIL: no output produced for $($cfg.name): $outputFile"
        $failures++
        continue
    }
    if ((Get-Item $outputFile).Length -eq 0) {
        Write-Host "FAIL: empty output for $($cfg.name): $outputFile"
        $failures++
        continue
    }

    if (-not (Test-Path $goldenFile)) {
        Write-Host "FAIL: Golden file not found for $($cfg.name): $goldenFile"
        $failures++
        continue
    }

    python FlashIDA\test-scripts\compare_golden.py $goldenFile $outputFile
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL: Golden comparison failed for $($cfg.name)"
        $failures++
    } else {
        Write-Host "PASS: $($cfg.name)"
    }
}

if ($failures -gt 0) {
    Write-Host "$failures test(s) failed."
    exit 1
}
Write-Host "All regression tests passed."
exit 0

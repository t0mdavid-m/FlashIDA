param (
    [string]$FlashExe = "FlashIDA\bin\Flash.exe",
    [string]$TestDataDir = "FlashIDA\test-data",
    [string]$OutputDir = "FlashIDA\test-output",
    [switch]$captureMode
)

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$configs = @(
    @{
        name   = "baseline_phase0"
        method = "method_default.xml"
        ms1    = "ms1_smoke_test.txt"
        ms2    = $null
        golden = "baseline_phase0.tsv"
    }
    # Phase 1: JSON config path (same golden as Phase 0 — must produce identical output)
    @{
        name   = "p1_json"
        method = "method_default.xml"
        ms1    = "ms1_smoke_test.txt"
        ms2    = $null
        golden = "baseline_phase0.tsv"
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

    & $FlashExe @flashArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL: Flash.exe exited with code $LASTEXITCODE for $($cfg.name)"
        $failures++
        continue
    }

    if ($captureMode) {
        Write-Host "CAPTURE: $($cfg.name) -> $outputFile"
        continue
    }

    if (-not (Test-Path $goldenFile)) {
        Write-Host "SKIP: Golden file not found for $($cfg.name): $goldenFile"
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

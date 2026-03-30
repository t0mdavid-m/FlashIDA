param (
    [string]$FlashExe = "FlashIDA\bin\Flash.exe",
    [string]$TestDataDir = "FlashIDA\test-data",
    [string]$OutputDir = "FlashIDA\test-output",
    [switch]$captureMode
)

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# Copy supporting config files (inclusion lists, FASTA) to working directory
# so C++ engine can resolve bare filenames in method XML
$configDir = Join-Path $TestDataDir "configs"
Copy-Item (Join-Path $configDir "test_inclusion_list.txt") . -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $configDir "test_fasta.fasta") . -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $configDir "test_target_log.log") . -Force -ErrorAction SilentlyContinue
Write-Host "Copied supporting files from $configDir to $(Get-Location)"

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
    # Phase 4: Pre-switch golden baselines (old bridge path, ms1_standard.txt)
    @{
        name   = "phase4_standard_dda"
        method = "method_default.xml"
        ms1    = "ms1_standard.txt"
        ms2    = $null
        golden = "phase4_standard_dda.tsv"
    }
    @{
        name   = "phase4_deep_mode"
        method = "method_deep.xml"
        ms1    = "ms1_standard.txt"
        ms2    = $null
        golden = "phase4_deep_mode.tsv"
    }
    @{
        name   = "phase4_inclusion"
        method = "method_inclusion.xml"
        ms1    = "ms1_standard.txt"
        ms2    = $null
        golden = "phase4_inclusion.tsv"
    }
    @{
        name   = "phase4_inclusion_strict"
        method = "method_inclusion_strict.xml"
        ms1    = "ms1_standard.txt"
        ms2    = $null
        golden = "phase4_inclusion_strict.tsv"
    }
    @{
        name   = "phase4_exclusion"
        method = "method_exclusion.xml"
        ms1    = "ms1_standard.txt"
        ms2    = $null
        golden = "phase4_exclusion.tsv"
    }
    @{
        name   = "phase4_tag_targeting"
        method = "method_tag_targeting.xml"
        ms1    = "ms1_standard.txt"
        ms2    = "ms2_hcd_fragment.txt"
        golden = "phase4_tag_targeting.tsv"
    }
    @{
        name   = "phase4_quant"
        method = "method_quant.xml"
        ms1    = "ms1_standard.txt"
        ms2    = "ms2_quant_tmt.txt"
        golden = "phase4_quant.tsv"
    }
    @{
        name   = "phase4_ms3_mode1"
        method = "method_ms3_mode1.xml"
        ms1    = "ms1_standard.txt"
        ms2    = "ms2_hcd_fragment.txt"
        golden = "phase4_ms3_mode1.tsv"
    }
    @{
        name   = "phase4_ms3_mode2"
        method = "method_ms3_mode2.xml"
        ms1    = "ms1_standard.txt"
        ms2    = "ms2_hcd_fragment.txt"
        golden = "phase4_ms3_mode2.tsv"
    }
    @{
        name   = "phase4_ms3_mode3"
        method = "method_ms3_mode3.xml"
        ms1    = "ms1_standard.txt"
        ms2    = "ms2_hcd_fragment.txt"
        golden = "phase4_ms3_mode3.tsv"
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
    & $FlashExe @flashArgs 2>&1 | ForEach-Object { Write-Host "  [Flash] $_" }

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

# Fixture provenance

Fixtures cut from instrument data with `test-scripts/cut_mzml_fixture.py` record their origin here, one
line per file, exactly as the script printed it. Fixtures generated synthetically (e.g. by
`make_multice_cytc.py`) are documented in their generator.

## `ms1_cytc_z17.txt`, `ms2_cytc_z17_ce{0,20,25,30,35,40}.txt` — 2026-09-22, ADR-0045

Source: `FLASHIda_methodDirectInfusionCytc_90min_150nlmin_12ul_20260906032456.mzML`, a direct-infusion
cytochrome c CE sweep acquired 2026-09-06 (exploration group 4: z = 17 at m/z 727.904, mono 12349.2, HCD
0–50 step 1). All seven were cut with `--centroid` (local maxima of the Orbitrap profile). The precursor
window sums (727.03–728.78 Th) reproduce the run's own `remaining_ratio` curve — 1.20 / 0.71 / 0.28 /
0.056 / 0.009 at NCE 20/25/30/35/40 — so the sweep crosses the 0.1 target between NCE 30 and 35 and the
winner is a real decision. These feed the `exploration_iontrap` log golden.

```
scan 168  (ms1, rt 5.8877 min,  FTMS + p NSI sid=15.00 Full ms [500.0000-2000.0000])                desc='!"SS'             -> ms1_cytc_z17.txt:      166281 points -> 12516 peaks
scan 990  (ms2, rt 39.3947 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd0.00 [150.0000-2000.0000])  desc='!%pE12.3492k@17'  -> ms2_cytc_z17_ce0.txt:  31014 points -> 2525 peaks
scan 1040 (ms2, rt 41.5289 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd20.00 [150.0000-2000.0000]) desc='!&&E12.3492k@17'  -> ms2_cytc_z17_ce20.txt: 57715 points -> 4824 peaks
scan 1055 (ms2, rt 42.0971 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd25.00 [150.0000-2000.0000]) desc='!&+E12.3492k@17'  -> ms2_cytc_z17_ce25.txt: 65400 points -> 5052 peaks
scan 1070 (ms2, rt 42.6661 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd30.00 [150.0000-2000.0000]) desc='!&0E12.3492k@17'  -> ms2_cytc_z17_ce30.txt: 78709 points -> 5828 peaks
scan 1080 (ms2, rt 43.1646 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd35.00 [150.0000-2000.0000]) desc='!&5E12.3492k@17'  -> ms2_cytc_z17_ce35.txt: 94652 points -> 6773 peaks
scan 1095 (ms2, rt 43.7328 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd40.00 [150.0000-2000.0000]) desc='!&:E12.3492k@17'  -> ms2_cytc_z17_ce40.txt: 87650 points -> 6243 peaks
```

The fixture header carries the retention time in **seconds** (`MockMsScan.FromTsv*` divides by 60); the
older hand-cut fixtures wrote minutes there, which the harness reads as a fraction of a minute — harmless
for every test that exists, and not worth normalising.

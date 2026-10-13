# Fixture provenance

Fixtures cut from instrument data with `test-scripts/cut_mzml_fixture.py` record their origin here, one
line per file, exactly as the script printed it. Fixtures generated synthetically (e.g. by
`make_multice_cytc.py`) are documented in their generator.

Deconvolved-mass fixtures for the Refine method (ADR-0049) live in `test-data/refine/`, each with its
provenance header, all written by `test-scripts/refine/make_refine_tables.py`. The corpus layer,
`test-data/refine/corpus/`, holds seven proteoforms of the MS2 characterization corpus: `ca`, `cytc`,
`myoglobin`, `tia_red`, `ubiquitin`, `lactalbumin_red` and `betacasein`. The corpus's other seven rows
(lysozyme, lysozyme_red, rnase, tia, trypsinogen, lactalbumin, betalac) are not there because intact
disulfides are outside Refine's step-function model.

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
for every test that exists, and not worth normalising. The rt_window tests (ADR-0051:
`RetentionTimeWindowTests` and two `FLASHIda_ProcessScan_test` sections) rely on exactly this reading of
`ms1_cytc.txt`: both harnesses treat its 8.2–72.0 as seconds, which gives them a 64 s run in which a 30 s
window expires once and a 180 s window never does.

## Full HCD sweeps for the Kinetic Model (ADR-0047 d.10, layer B) — 2026-10-11

Three corpus sweeps cut at **every** energy 1..50 so any authored grid and any modeled maximum resolves to a
real spectrum (the harness feeds MS2 by rounded CE with no fallback). Each sweep's MS1 is the **parent survey
the engine decided it from** — located by the `rt` of the parent `tracking_id` in the run's `scan_results.tsv`,
not the survey nearest the sweep. All cut with `--centroid`. Window sums of these centroided reads are the
engine's `precursor_share`; they are **not** column 11 of the corpus table, which was read from profile arrays
(`test-data/kinetic/cut_sweep_expected.tsv` carries both modeled maxima side by side).

Selection: `cut_mzml_fixture.py <run.mzML> --list-ms2=<m/z>` lists one sweep's MS2 scans by energy; each run holds
an HCD and a CID sweep at the same m/z, and only the `hcd` rows were cut.

### `ms2_cytc_z17_ce{1..50}.txt` (the 44 energies missing above; CE 0/20/25/30/35/40 are the 2026-09-22 cuts)

Source as above (`…Cytc…20260906032456.mzML`, m/z 727.904, HCD rows of `--list-ms2=727.904`).

```
scan 991 (ms2, rt 39.4800 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd1.00 [150.0000-2000.0000]) desc='!%qE12.3492k@17' -> ms2_cytc_z17_ce1.txt: 49924 points -> 4439 peaks (centroided)
scan 992 (ms2, rt 39.5661 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd2.00 [150.0000-2000.0000]) desc='!%rE12.3492k@17' -> ms2_cytc_z17_ce2.txt: 45361 points -> 4019 peaks (centroided)
scan 998 (ms2, rt 39.7218 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd3.00 [150.0000-2000.0000]) desc='!%sE12.3492k@17' -> ms2_cytc_z17_ce3.txt: 36672 points -> 3133 peaks (centroided)
scan 999 (ms2, rt 39.8071 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd4.00 [150.0000-2000.0000]) desc='!%tE12.3492k@17' -> ms2_cytc_z17_ce4.txt: 44062 points -> 3877 peaks (centroided)
scan 1000 (ms2, rt 39.8932 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd5.00 [150.0000-2000.0000]) desc='!%uE12.3492k@17' -> ms2_cytc_z17_ce5.txt: 42178 points -> 3702 peaks (centroided)
scan 1006 (ms2, rt 40.0490 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd6.00 [150.0000-2000.0000]) desc='!%vE12.3492k@17' -> ms2_cytc_z17_ce6.txt: 38468 points -> 3298 peaks (centroided)
scan 1007 (ms2, rt 40.1342 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd7.00 [150.0000-2000.0000]) desc='!%wE12.3492k@17' -> ms2_cytc_z17_ce7.txt: 41341 points -> 3595 peaks (centroided)
scan 1008 (ms2, rt 40.2203 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd8.00 [150.0000-2000.0000]) desc='!%xE12.3492k@17' -> ms2_cytc_z17_ce8.txt: 40722 points -> 3552 peaks (centroided)
scan 1014 (ms2, rt 40.3761 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd9.00 [150.0000-2000.0000]) desc='!%yE12.3492k@17' -> ms2_cytc_z17_ce9.txt: 31684 points -> 2587 peaks (centroided)
scan 1015 (ms2, rt 40.4614 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd10.00 [150.0000-2000.0000]) desc='!%zE12.3492k@17' -> ms2_cytc_z17_ce10.txt: 44694 points -> 3920 peaks (centroided)
scan 1016 (ms2, rt 40.5475 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd11.00 [150.0000-2000.0000]) desc='!%{E12.3492k@17' -> ms2_cytc_z17_ce11.txt: 46098 points -> 4097 peaks (centroided)
scan 1022 (ms2, rt 40.7033 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd12.00 [150.0000-2000.0000]) desc='!%|E12.3492k@17' -> ms2_cytc_z17_ce12.txt: 41603 points -> 3534 peaks (centroided)
scan 1023 (ms2, rt 40.7885 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd13.00 [150.0000-2000.0000]) desc='!%}E12.3492k@17' -> ms2_cytc_z17_ce13.txt: 34954 points -> 2865 peaks (centroided)
scan 1024 (ms2, rt 40.8746 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd14.00 [150.0000-2000.0000]) desc='!%~E12.3492k@17' -> ms2_cytc_z17_ce14.txt: 37276 points -> 3073 peaks (centroided)
scan 1030 (ms2, rt 41.0304 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd15.00 [150.0000-2000.0000]) desc='!&!E12.3492k@17' -> ms2_cytc_z17_ce15.txt: 39890 points -> 3303 peaks (centroided)
scan 1031 (ms2, rt 41.1157 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd16.00 [150.0000-2000.0000]) desc='!&"E12.3492k@17' -> ms2_cytc_z17_ce16.txt: 43298 points -> 3566 peaks (centroided)
scan 1032 (ms2, rt 41.2018 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd17.00 [150.0000-2000.0000]) desc='!&#E12.3492k@17' -> ms2_cytc_z17_ce17.txt: 40297 points -> 3256 peaks (centroided)
scan 1038 (ms2, rt 41.3575 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd18.00 [150.0000-2000.0000]) desc='!&$E12.3492k@17' -> ms2_cytc_z17_ce18.txt: 44806 points -> 3755 peaks (centroided)
scan 1039 (ms2, rt 41.4428 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd19.00 [150.0000-2000.0000]) desc='!&%E12.3492k@17' -> ms2_cytc_z17_ce19.txt: 56986 points -> 4860 peaks (centroided)
scan 1046 (ms2, rt 41.6847 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd21.00 [150.0000-2000.0000]) desc="!&'E12.3492k@17" -> ms2_cytc_z17_ce21.txt: 50080 points -> 4021 peaks (centroided)
scan 1047 (ms2, rt 41.7699 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd22.00 [150.0000-2000.0000]) desc='!&(E12.3492k@17' -> ms2_cytc_z17_ce22.txt: 55393 points -> 4382 peaks (centroided)
scan 1048 (ms2, rt 41.8560 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd23.00 [150.0000-2000.0000]) desc='!&)E12.3492k@17' -> ms2_cytc_z17_ce23.txt: 60514 points -> 4772 peaks (centroided)
scan 1054 (ms2, rt 42.0118 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd24.00 [150.0000-2000.0000]) desc='!&*E12.3492k@17' -> ms2_cytc_z17_ce24.txt: 61555 points -> 4838 peaks (centroided)
scan 1056 (ms2, rt 42.1832 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd26.00 [150.0000-2000.0000]) desc='!&,E12.3492k@17' -> ms2_cytc_z17_ce26.txt: 70647 points -> 5450 peaks (centroided)
scan 1062 (ms2, rt 42.3390 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd27.00 [150.0000-2000.0000]) desc='!&-E12.3492k@17' -> ms2_cytc_z17_ce27.txt: 73517 points -> 5596 peaks (centroided)
scan 1063 (ms2, rt 42.4242 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd28.00 [150.0000-2000.0000]) desc='!&.E12.3492k@17' -> ms2_cytc_z17_ce28.txt: 74111 points -> 5563 peaks (centroided)
scan 1064 (ms2, rt 42.5103 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd29.00 [150.0000-2000.0000]) desc='!&/E12.3492k@17' -> ms2_cytc_z17_ce29.txt: 77094 points -> 5709 peaks (centroided)
scan 1071 (ms2, rt 42.7513 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd31.00 [150.0000-2000.0000]) desc='!&1E12.3492k@17' -> ms2_cytc_z17_ce31.txt: 100033 points -> 7383 peaks (centroided)
scan 1072 (ms2, rt 42.8375 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd32.00 [150.0000-2000.0000]) desc='!&2E12.3492k@17' -> ms2_cytc_z17_ce32.txt: 105900 points -> 7748 peaks (centroided)
scan 1078 (ms2, rt 42.9932 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd33.00 [150.0000-2000.0000]) desc='!&3E12.3492k@17' -> ms2_cytc_z17_ce33.txt: 84040 points -> 6047 peaks (centroided)
scan 1079 (ms2, rt 43.0785 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd34.00 [150.0000-2000.0000]) desc='!&4E12.3492k@17' -> ms2_cytc_z17_ce34.txt: 90308 points -> 6484 peaks (centroided)
scan 1086 (ms2, rt 43.3204 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd36.00 [150.0000-2000.0000]) desc='!&6E12.3492k@17' -> ms2_cytc_z17_ce36.txt: 87635 points -> 6245 peaks (centroided)
scan 1087 (ms2, rt 43.4056 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd37.00 [150.0000-2000.0000]) desc='!&7E12.3492k@17' -> ms2_cytc_z17_ce37.txt: 103010 points -> 7412 peaks (centroided)
scan 1088 (ms2, rt 43.4917 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd38.00 [150.0000-2000.0000]) desc='!&8E12.3492k@17' -> ms2_cytc_z17_ce38.txt: 103729 points -> 7455 peaks (centroided)
scan 1094 (ms2, rt 43.6475 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd39.00 [150.0000-2000.0000]) desc='!&9E12.3492k@17' -> ms2_cytc_z17_ce39.txt: 95573 points -> 6857 peaks (centroided)
scan 1096 (ms2, rt 43.8189 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd41.00 [150.0000-2000.0000]) desc='!&;E12.3492k@17' -> ms2_cytc_z17_ce41.txt: 84623 points -> 5997 peaks (centroided)
scan 1102 (ms2, rt 43.9748 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd42.00 [150.0000-2000.0000]) desc='!&<E12.3492k@17' -> ms2_cytc_z17_ce42.txt: 84370 points -> 6006 peaks (centroided)
scan 1103 (ms2, rt 44.0599 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd43.00 [150.0000-2000.0000]) desc='!&=E12.3492k@17' -> ms2_cytc_z17_ce43.txt: 89839 points -> 6400 peaks (centroided)
scan 1104 (ms2, rt 44.1460 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd44.00 [150.0000-2000.0000]) desc='!&>E12.3492k@17' -> ms2_cytc_z17_ce44.txt: 87006 points -> 6195 peaks (centroided)
scan 1110 (ms2, rt 44.3019 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd45.00 [150.0000-2000.0000]) desc='!&?E12.3492k@17' -> ms2_cytc_z17_ce45.txt: 68464 points -> 4847 peaks (centroided)
scan 1111 (ms2, rt 44.3870 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd46.00 [150.0000-2000.0000]) desc='!&@E12.3492k@17' -> ms2_cytc_z17_ce46.txt: 98459 points -> 7050 peaks (centroided)
scan 1112 (ms2, rt 44.4732 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd47.00 [150.0000-2000.0000]) desc='!&AE12.3492k@17' -> ms2_cytc_z17_ce47.txt: 101735 points -> 7276 peaks (centroided)
scan 1118 (ms2, rt 44.6289 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd48.00 [150.0000-2000.0000]) desc='!&BE12.3492k@17' -> ms2_cytc_z17_ce48.txt: 70240 points -> 5022 peaks (centroided)
scan 1119 (ms2, rt 44.7142 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd49.00 [150.0000-2000.0000]) desc='!&CE12.3492k@17' -> ms2_cytc_z17_ce49.txt: 66088 points -> 4694 peaks (centroided)
scan 1120 (ms2, rt 44.8003 min, FTMS + p NSI sid=15.00 Full ms2 727.9039@hcd50.00 [150.0000-2000.0000]) desc='!&DE12.3492k@17' -> ms2_cytc_z17_ce50.txt: 62071 points -> 4412 peaks (centroided)
```

### `ms1_ubq_z9.txt`, `ms2_ubq_z9_ce{1..50}.txt` — ubiquitin 9+, the report's worked example

Source: `FLASHIda_methodDirectInfusionUbiquitin_90min_150nlmin_12ul.mzML` (run dir `…_2026-09-04-18-23-55`),
exploration rows of charge 9: m/z 952.793, isolation width 2.24672, mono 8559.56, parent survey `!!c`
(rt 2.76896 min → scan 79). Inclusion list `configs/inclusion_ubq_z9.txt`.

```
scan 79 (ms1, rt 2.7690 min, FTMS + p NSI sid=15.00 Full ms [500.0000-2000.0000]) desc='!!cS' -> ms1_ubq_z9.txt: 97994 points -> 7800 peaks (centroided)
scan 631 (ms2, rt 25.3072 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd1.00 [150.0000-2000.0000]) desc='!#yE8.559562k@9' -> ms2_ubq_z9_ce1.txt: 22186 points -> 1800 peaks (centroided)
scan 632 (ms2, rt 25.3933 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd2.00 [150.0000-2000.0000]) desc='!#zE8.559562k@9' -> ms2_ubq_z9_ce2.txt: 20656 points -> 1699 peaks (centroided)
scan 638 (ms2, rt 25.5491 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd3.00 [150.0000-2000.0000]) desc='!#{E8.559562k@9' -> ms2_ubq_z9_ce3.txt: 22709 points -> 1935 peaks (centroided)
scan 639 (ms2, rt 25.6343 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd4.00 [150.0000-2000.0000]) desc='!#|E8.559562k@9' -> ms2_ubq_z9_ce4.txt: 26665 points -> 2203 peaks (centroided)
scan 640 (ms2, rt 25.7204 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd5.00 [150.0000-2000.0000]) desc='!#}E8.559562k@9' -> ms2_ubq_z9_ce5.txt: 21639 points -> 1822 peaks (centroided)
scan 646 (ms2, rt 25.8762 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd6.00 [150.0000-2000.0000]) desc='!#~E8.559562k@9' -> ms2_ubq_z9_ce6.txt: 17391 points -> 1447 peaks (centroided)
scan 647 (ms2, rt 25.9615 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd7.00 [150.0000-2000.0000]) desc='!$!E8.559562k@9' -> ms2_ubq_z9_ce7.txt: 32386 points -> 2836 peaks (centroided)
scan 648 (ms2, rt 26.0476 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd8.00 [150.0000-2000.0000]) desc='!$"E8.559562k@9' -> ms2_ubq_z9_ce8.txt: 36432 points -> 3444 peaks (centroided)
scan 654 (ms2, rt 26.2033 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd9.00 [150.0000-2000.0000]) desc='!$#E8.559562k@9' -> ms2_ubq_z9_ce9.txt: 21880 points -> 1799 peaks (centroided)
scan 655 (ms2, rt 26.2886 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd10.00 [150.0000-2000.0000]) desc='!$$E8.559562k@9' -> ms2_ubq_z9_ce10.txt: 27260 points -> 2270 peaks (centroided)
scan 656 (ms2, rt 26.3747 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd11.00 [150.0000-2000.0000]) desc='!$%E8.559562k@9' -> ms2_ubq_z9_ce11.txt: 25604 points -> 2124 peaks (centroided)
scan 662 (ms2, rt 26.5306 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd12.00 [150.0000-2000.0000]) desc='!$&E8.559562k@9' -> ms2_ubq_z9_ce12.txt: 23382 points -> 2043 peaks (centroided)
scan 663 (ms2, rt 26.6158 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd13.00 [150.0000-2000.0000]) desc="!$'E8.559562k@9" -> ms2_ubq_z9_ce13.txt: 29529 points -> 2525 peaks (centroided)
scan 664 (ms2, rt 26.7018 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd14.00 [150.0000-2000.0000]) desc='!$(E8.559562k@9' -> ms2_ubq_z9_ce14.txt: 28894 points -> 2448 peaks (centroided)
scan 670 (ms2, rt 26.8577 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd15.00 [150.0000-2000.0000]) desc='!$)E8.559562k@9' -> ms2_ubq_z9_ce15.txt: 21951 points -> 1888 peaks (centroided)
scan 671 (ms2, rt 26.9429 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd16.00 [150.0000-2000.0000]) desc='!$*E8.559562k@9' -> ms2_ubq_z9_ce16.txt: 29907 points -> 2562 peaks (centroided)
scan 672 (ms2, rt 27.0290 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd17.00 [150.0000-2000.0000]) desc='!$+E8.559562k@9' -> ms2_ubq_z9_ce17.txt: 34855 points -> 2958 peaks (centroided)
scan 678 (ms2, rt 27.1848 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd18.00 [150.0000-2000.0000]) desc='!$,E8.559562k@9' -> ms2_ubq_z9_ce18.txt: 27009 points -> 2503 peaks (centroided)
scan 679 (ms2, rt 27.2700 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd19.00 [150.0000-2000.0000]) desc='!$-E8.559562k@9' -> ms2_ubq_z9_ce19.txt: 47398 points -> 5895 peaks (centroided)
scan 680 (ms2, rt 27.3561 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd20.00 [150.0000-2000.0000]) desc='!$.E8.559562k@9' -> ms2_ubq_z9_ce20.txt: 39639 points -> 3605 peaks (centroided)
scan 686 (ms2, rt 27.5119 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd21.00 [150.0000-2000.0000]) desc='!$/E8.559562k@9' -> ms2_ubq_z9_ce21.txt: 35231 points -> 2873 peaks (centroided)
scan 687 (ms2, rt 27.5972 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd22.00 [150.0000-2000.0000]) desc='!$0E8.559562k@9' -> ms2_ubq_z9_ce22.txt: 33411 points -> 2641 peaks (centroided)
scan 688 (ms2, rt 27.6833 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd23.00 [150.0000-2000.0000]) desc='!$1E8.559562k@9' -> ms2_ubq_z9_ce23.txt: 37933 points -> 2869 peaks (centroided)
scan 694 (ms2, rt 27.8390 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd24.00 [150.0000-2000.0000]) desc='!$2E8.559562k@9' -> ms2_ubq_z9_ce24.txt: 33027 points -> 2483 peaks (centroided)
scan 695 (ms2, rt 27.9243 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd25.00 [150.0000-2000.0000]) desc='!$3E8.559562k@9' -> ms2_ubq_z9_ce25.txt: 47295 points -> 3754 peaks (centroided)
scan 696 (ms2, rt 28.0104 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd26.00 [150.0000-2000.0000]) desc='!$4E8.559562k@9' -> ms2_ubq_z9_ce26.txt: 44716 points -> 3296 peaks (centroided)
scan 702 (ms2, rt 28.1662 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd27.00 [150.0000-2000.0000]) desc='!$5E8.559562k@9' -> ms2_ubq_z9_ce27.txt: 46304 points -> 3349 peaks (centroided)
scan 703 (ms2, rt 28.2515 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd28.00 [150.0000-2000.0000]) desc='!$6E8.559562k@9' -> ms2_ubq_z9_ce28.txt: 51330 points -> 3607 peaks (centroided)
scan 704 (ms2, rt 28.3375 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd29.00 [150.0000-2000.0000]) desc='!$7E8.559562k@9' -> ms2_ubq_z9_ce29.txt: 55859 points -> 3955 peaks (centroided)
scan 710 (ms2, rt 28.4934 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd30.00 [150.0000-2000.0000]) desc='!$8E8.559562k@9' -> ms2_ubq_z9_ce30.txt: 63727 points -> 4505 peaks (centroided)
scan 711 (ms2, rt 28.5786 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd31.00 [150.0000-2000.0000]) desc='!$9E8.559562k@9' -> ms2_ubq_z9_ce31.txt: 71885 points -> 5031 peaks (centroided)
scan 712 (ms2, rt 28.6647 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd32.00 [150.0000-2000.0000]) desc='!$:E8.559562k@9' -> ms2_ubq_z9_ce32.txt: 78873 points -> 5447 peaks (centroided)
scan 718 (ms2, rt 28.8205 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd33.00 [150.0000-2000.0000]) desc='!$;E8.559562k@9' -> ms2_ubq_z9_ce33.txt: 89996 points -> 6225 peaks (centroided)
scan 719 (ms2, rt 28.9057 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd34.00 [150.0000-2000.0000]) desc='!$<E8.559562k@9' -> ms2_ubq_z9_ce34.txt: 96722 points -> 6682 peaks (centroided)
scan 720 (ms2, rt 28.9918 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd35.00 [150.0000-2000.0000]) desc='!$=E8.559562k@9' -> ms2_ubq_z9_ce35.txt: 101776 points -> 7026 peaks (centroided)
scan 726 (ms2, rt 29.1476 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd36.00 [150.0000-2000.0000]) desc='!$>E8.559562k@9' -> ms2_ubq_z9_ce36.txt: 110712 points -> 7668 peaks (centroided)
scan 727 (ms2, rt 29.2329 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd37.00 [150.0000-2000.0000]) desc='!$?E8.559562k@9' -> ms2_ubq_z9_ce37.txt: 119929 points -> 8387 peaks (centroided)
scan 728 (ms2, rt 29.3190 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd38.00 [150.0000-2000.0000]) desc='!$@E8.559562k@9' -> ms2_ubq_z9_ce38.txt: 121181 points -> 8457 peaks (centroided)
scan 734 (ms2, rt 29.4747 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd39.00 [150.0000-2000.0000]) desc='!$AE8.559562k@9' -> ms2_ubq_z9_ce39.txt: 94492 points -> 6498 peaks (centroided)
scan 735 (ms2, rt 29.5600 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd40.00 [150.0000-2000.0000]) desc='!$BE8.559562k@9' -> ms2_ubq_z9_ce40.txt: 111397 points -> 7747 peaks (centroided)
scan 736 (ms2, rt 29.6461 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd41.00 [150.0000-2000.0000]) desc='!$CE8.559562k@9' -> ms2_ubq_z9_ce41.txt: 112192 points -> 7849 peaks (centroided)
scan 742 (ms2, rt 29.8019 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd42.00 [150.0000-2000.0000]) desc='!$DE8.559562k@9' -> ms2_ubq_z9_ce42.txt: 97313 points -> 6732 peaks (centroided)
scan 743 (ms2, rt 29.8871 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd43.00 [150.0000-2000.0000]) desc='!$EE8.559562k@9' -> ms2_ubq_z9_ce43.txt: 116402 points -> 8192 peaks (centroided)
scan 744 (ms2, rt 29.9732 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd44.00 [150.0000-2000.0000]) desc='!$FE8.559562k@9' -> ms2_ubq_z9_ce44.txt: 118547 points -> 8403 peaks (centroided)
scan 750 (ms2, rt 30.1291 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd45.00 [150.0000-2000.0000]) desc='!$GE8.559562k@9' -> ms2_ubq_z9_ce45.txt: 98825 points -> 6956 peaks (centroided)
scan 751 (ms2, rt 30.2143 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd46.00 [150.0000-2000.0000]) desc='!$HE8.559562k@9' -> ms2_ubq_z9_ce46.txt: 108958 points -> 7716 peaks (centroided)
scan 752 (ms2, rt 30.3004 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd47.00 [150.0000-2000.0000]) desc='!$IE8.559562k@9' -> ms2_ubq_z9_ce47.txt: 106507 points -> 7534 peaks (centroided)
scan 758 (ms2, rt 30.4562 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd48.00 [150.0000-2000.0000]) desc='!$JE8.559562k@9' -> ms2_ubq_z9_ce48.txt: 97371 points -> 6879 peaks (centroided)
scan 759 (ms2, rt 30.5414 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd49.00 [150.0000-2000.0000]) desc='!$KE8.559562k@9' -> ms2_ubq_z9_ce49.txt: 106767 points -> 7566 peaks (centroided)
scan 760 (ms2, rt 30.6275 min, FTMS + p NSI sid=15.00 Full ms2 952.7927@hcd50.00 [150.0000-2000.0000]) desc='!$LE8.559562k@9' -> ms2_ubq_z9_ce50.txt: 103201 points -> 7288 peaks (centroided)
```

### `ms1_tryg_z12.txt`, `ms2_tryg_z12_ce{1..50}.txt` — trypsinogen 12+, a disulfide protein (the model's weak class)

Source: `FLASHIda_methodDirectInfusionTrypsinogen_90min_150nlmin_12ul.mzML` (run dir `…_2026-09-04-14-28-54`),
exploration rows of charge 12: m/z 1999.2, isolation width 2.3011, mono 23965.3, parent survey `!)D`
(rt 36.4621 min → scan 903). Inclusion list `configs/inclusion_tryg_z12.txt`.

```
scan 903 (ms1, rt 36.4621 min, FTMS + p NSI sid=15.00 Full ms [500.0000-2000.0000]) desc='!)DS' -> ms1_tryg_z12.txt: 145023 points -> 10208 peaks (centroided)
scan 1183 (ms2, rt 47.8776 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd1.00 [150.0000-2000.0000]) desc='!*RE23.9653k@12' -> ms2_tryg_z12_ce1.txt: 37279 points -> 3482 peaks (centroided)
scan 1184 (ms2, rt 47.9637 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd2.00 [150.0000-2000.0000]) desc='!*SE23.9653k@12' -> ms2_tryg_z12_ce2.txt: 28874 points -> 2592 peaks (centroided)
scan 1190 (ms2, rt 48.1194 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd3.00 [150.0000-2000.0000]) desc='!*TE23.9653k@12' -> ms2_tryg_z12_ce3.txt: 23342 points -> 2057 peaks (centroided)
scan 1191 (ms2, rt 48.2047 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd4.00 [150.0000-2000.0000]) desc='!*UE23.9653k@12' -> ms2_tryg_z12_ce4.txt: 16598 points -> 1407 peaks (centroided)
scan 1192 (ms2, rt 48.2908 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd5.00 [150.0000-2000.0000]) desc='!*VE23.9653k@12' -> ms2_tryg_z12_ce5.txt: 13553 points -> 1156 peaks (centroided)
scan 1198 (ms2, rt 48.4466 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd6.00 [150.0000-2000.0000]) desc='!*WE23.9653k@12' -> ms2_tryg_z12_ce6.txt: 21735 points -> 1923 peaks (centroided)
scan 1199 (ms2, rt 48.5319 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd7.00 [150.0000-2000.0000]) desc='!*XE23.9653k@12' -> ms2_tryg_z12_ce7.txt: 24511 points -> 2146 peaks (centroided)
scan 1200 (ms2, rt 48.6179 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd8.00 [150.0000-2000.0000]) desc='!*YE23.9653k@12' -> ms2_tryg_z12_ce8.txt: 29244 points -> 2684 peaks (centroided)
scan 1206 (ms2, rt 48.7739 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd9.00 [150.0000-2000.0000]) desc='!*ZE23.9653k@12' -> ms2_tryg_z12_ce9.txt: 22304 points -> 1987 peaks (centroided)
scan 1207 (ms2, rt 48.8590 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd10.00 [150.0000-2000.0000]) desc='!*[E23.9653k@12' -> ms2_tryg_z12_ce10.txt: 23333 points -> 2084 peaks (centroided)
scan 1208 (ms2, rt 48.9451 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd11.00 [150.0000-2000.0000]) desc='!*\\E23.9653k@12' -> ms2_tryg_z12_ce11.txt: 25337 points -> 2251 peaks (centroided)
scan 1214 (ms2, rt 49.1010 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd12.00 [150.0000-2000.0000]) desc='!*]E23.9653k@12' -> ms2_tryg_z12_ce12.txt: 18888 points -> 1661 peaks (centroided)
scan 1215 (ms2, rt 49.1861 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd13.00 [150.0000-2000.0000]) desc='!*^E23.9653k@12' -> ms2_tryg_z12_ce13.txt: 14213 points -> 1186 peaks (centroided)
scan 1216 (ms2, rt 49.2723 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd14.00 [150.0000-2000.0000]) desc='!*_E23.9653k@12' -> ms2_tryg_z12_ce14.txt: 14567 points -> 1238 peaks (centroided)
scan 1222 (ms2, rt 49.4280 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd15.00 [150.0000-2000.0000]) desc='!*`E23.9653k@12' -> ms2_tryg_z12_ce15.txt: 11003 points -> 913 peaks (centroided)
scan 1223 (ms2, rt 49.5133 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd16.00 [150.0000-2000.0000]) desc='!*aE23.9653k@12' -> ms2_tryg_z12_ce16.txt: 16565 points -> 1411 peaks (centroided)
scan 1224 (ms2, rt 49.5994 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd17.00 [150.0000-2000.0000]) desc='!*bE23.9653k@12' -> ms2_tryg_z12_ce17.txt: 12908 points -> 1068 peaks (centroided)
scan 1230 (ms2, rt 49.7551 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd18.00 [150.0000-2000.0000]) desc='!*cE23.9653k@12' -> ms2_tryg_z12_ce18.txt: 6814 points -> 542 peaks (centroided)
scan 1231 (ms2, rt 49.8404 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd19.00 [150.0000-2000.0000]) desc='!*dE23.9653k@12' -> ms2_tryg_z12_ce19.txt: 9511 points -> 782 peaks (centroided)
scan 1232 (ms2, rt 49.9265 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd20.00 [150.0000-2000.0000]) desc='!*eE23.9653k@12' -> ms2_tryg_z12_ce20.txt: 8898 points -> 703 peaks (centroided)
scan 1238 (ms2, rt 50.0823 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd21.00 [150.0000-2000.0000]) desc='!*fE23.9653k@12' -> ms2_tryg_z12_ce21.txt: 7923 points -> 638 peaks (centroided)
scan 1239 (ms2, rt 50.1676 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd22.00 [150.0000-2000.0000]) desc='!*gE23.9653k@12' -> ms2_tryg_z12_ce22.txt: 7152 points -> 578 peaks (centroided)
scan 1240 (ms2, rt 50.2536 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd23.00 [150.0000-2000.0000]) desc='!*hE23.9653k@12' -> ms2_tryg_z12_ce23.txt: 8177 points -> 665 peaks (centroided)
scan 1246 (ms2, rt 50.4094 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd24.00 [150.0000-2000.0000]) desc='!*iE23.9653k@12' -> ms2_tryg_z12_ce24.txt: 5943 points -> 476 peaks (centroided)
scan 1247 (ms2, rt 50.4947 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd25.00 [150.0000-2000.0000]) desc='!*jE23.9653k@12' -> ms2_tryg_z12_ce25.txt: 6571 points -> 522 peaks (centroided)
scan 1248 (ms2, rt 50.5808 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd26.00 [150.0000-2000.0000]) desc='!*kE23.9653k@12' -> ms2_tryg_z12_ce26.txt: 5477 points -> 437 peaks (centroided)
scan 1254 (ms2, rt 50.7367 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd27.00 [150.0000-2000.0000]) desc='!*lE23.9653k@12' -> ms2_tryg_z12_ce27.txt: 3557 points -> 274 peaks (centroided)
scan 1255 (ms2, rt 50.8218 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd28.00 [150.0000-2000.0000]) desc='!*mE23.9653k@12' -> ms2_tryg_z12_ce28.txt: 4836 points -> 375 peaks (centroided)
scan 1256 (ms2, rt 50.9079 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd29.00 [150.0000-2000.0000]) desc='!*nE23.9653k@12' -> ms2_tryg_z12_ce29.txt: 4491 points -> 338 peaks (centroided)
scan 1262 (ms2, rt 51.0637 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd30.00 [150.0000-2000.0000]) desc='!*oE23.9653k@12' -> ms2_tryg_z12_ce30.txt: 3840 points -> 287 peaks (centroided)
scan 1263 (ms2, rt 51.1490 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd31.00 [150.0000-2000.0000]) desc='!*pE23.9653k@12' -> ms2_tryg_z12_ce31.txt: 5833 points -> 443 peaks (centroided)
scan 1264 (ms2, rt 51.2351 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd32.00 [150.0000-2000.0000]) desc='!*qE23.9653k@12' -> ms2_tryg_z12_ce32.txt: 5267 points -> 401 peaks (centroided)
scan 1270 (ms2, rt 51.3909 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd33.00 [150.0000-2000.0000]) desc='!*rE23.9653k@12' -> ms2_tryg_z12_ce33.txt: 3513 points -> 265 peaks (centroided)
scan 1271 (ms2, rt 51.4761 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd34.00 [150.0000-2000.0000]) desc='!*sE23.9653k@12' -> ms2_tryg_z12_ce34.txt: 5788 points -> 435 peaks (centroided)
scan 1272 (ms2, rt 51.5622 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd35.00 [150.0000-2000.0000]) desc='!*tE23.9653k@12' -> ms2_tryg_z12_ce35.txt: 5242 points -> 392 peaks (centroided)
scan 1278 (ms2, rt 51.7180 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd36.00 [150.0000-2000.0000]) desc='!*uE23.9653k@12' -> ms2_tryg_z12_ce36.txt: 3060 points -> 234 peaks (centroided)
scan 1279 (ms2, rt 51.8033 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd37.00 [150.0000-2000.0000]) desc='!*vE23.9653k@12' -> ms2_tryg_z12_ce37.txt: 3544 points -> 260 peaks (centroided)
scan 1280 (ms2, rt 51.8893 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd38.00 [150.0000-2000.0000]) desc='!*wE23.9653k@12' -> ms2_tryg_z12_ce38.txt: 3765 points -> 277 peaks (centroided)
scan 1286 (ms2, rt 52.0451 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd39.00 [150.0000-2000.0000]) desc='!*xE23.9653k@12' -> ms2_tryg_z12_ce39.txt: 4125 points -> 306 peaks (centroided)
scan 1287 (ms2, rt 52.1304 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd40.00 [150.0000-2000.0000]) desc='!*yE23.9653k@12' -> ms2_tryg_z12_ce40.txt: 6056 points -> 443 peaks (centroided)
scan 1288 (ms2, rt 52.2165 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd41.00 [150.0000-2000.0000]) desc='!*zE23.9653k@12' -> ms2_tryg_z12_ce41.txt: 5535 points -> 404 peaks (centroided)
scan 1294 (ms2, rt 52.3724 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd42.00 [150.0000-2000.0000]) desc='!*{E23.9653k@12' -> ms2_tryg_z12_ce42.txt: 3272 points -> 243 peaks (centroided)
scan 1295 (ms2, rt 52.4575 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd43.00 [150.0000-2000.0000]) desc='!*|E23.9653k@12' -> ms2_tryg_z12_ce43.txt: 5774 points -> 423 peaks (centroided)
scan 1296 (ms2, rt 52.5436 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd44.00 [150.0000-2000.0000]) desc='!*}E23.9653k@12' -> ms2_tryg_z12_ce44.txt: 4865 points -> 357 peaks (centroided)
scan 1302 (ms2, rt 52.6995 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd45.00 [150.0000-2000.0000]) desc='!*~E23.9653k@12' -> ms2_tryg_z12_ce45.txt: 2538 points -> 190 peaks (centroided)
scan 1303 (ms2, rt 52.7847 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd46.00 [150.0000-2000.0000]) desc='!+!E23.9653k@12' -> ms2_tryg_z12_ce46.txt: 3076 points -> 224 peaks (centroided)
scan 1304 (ms2, rt 52.8708 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd47.00 [150.0000-2000.0000]) desc='!+"E23.9653k@12' -> ms2_tryg_z12_ce47.txt: 3186 points -> 232 peaks (centroided)
scan 1310 (ms2, rt 53.0266 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd48.00 [150.0000-2000.0000]) desc='!+#E23.9653k@12' -> ms2_tryg_z12_ce48.txt: 2281 points -> 171 peaks (centroided)
scan 1311 (ms2, rt 53.1118 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd49.00 [150.0000-2000.0000]) desc='!+$E23.9653k@12' -> ms2_tryg_z12_ce49.txt: 4017 points -> 292 peaks (centroided)
scan 1312 (ms2, rt 53.1979 min, FTMS + p NSI sid=15.00 Full ms2 1999.1989@hcd50.00 [150.0000-2000.0000]) desc='!+%E23.9653k@12' -> ms2_tryg_z12_ce50.txt: 3625 points -> 262 peaks (centroided)
```

### `ms3_cytc3_b32_ce{0,15,17,…,39}_scan<N>.txt` — one MS3 CID sweep of the 2026-07-08 cytochrome c run

Source: `C:/FLASHIda/flashida-fresh/20260708_FLASHIda_CytC_MS3all_final.mzML`, the only MS3 CE sweep on disk:
parent fragment b32 (4+, stage 1 at m/z 1027.6311, width 2) under a stage-0 HCD 42 of cytC 12+ (m/z 1027.3131),
CID 0 and 15..39 in steps of 2 — the MS3 scans whose filter string carries `1027.6311@cid`. The parent MS2 of this
group is the `ms2_cytc2_scan434.txt` / `ms1_cytc2.txt` pair already used by the `inclusion_ms3_cytc` golden; the
older `ms3_cytc2_b32_scan455.txt` is the CE-23 member of this same sweep. Odd energies only: a modeled maximum at
an even CE has no fixture and the harness logs `[MS3-SKIP]` for the follow-up (never fabricates).

```
scan 449 (ms3, rt 4.9573 min, FTMS + p ESI Full ms3 1027.3131@hcd42.00 1027.6311@cid0.00 [200.0000-2000.0000]) desc='!#xE4.111k@4b32' -> ms3_cytc3_b32_ce0_scan449.txt: 2239 points -> 182 peaks (centroided)
scan 450 (ms3, rt 5.0394 min, FTMS + p ESI Full ms3 1027.3131@hcd42.00 1027.6311@cid15.00 [200.0000-2000.0000]) desc='!#yE4.111k@4b32' -> ms3_cytc3_b32_ce15_scan450.txt: 2772 points -> 227 peaks (centroided)
scan 451 (ms3, rt 5.1213 min, FTMS + p ESI Full ms3 1027.3131@hcd42.00 1027.6311@cid17.00 [200.0000-2000.0000]) desc='!#zE4.111k@4b32' -> ms3_cytc3_b32_ce17_scan451.txt: 2472 points -> 206 peaks (centroided)
scan 452 (ms3, rt 5.2034 min, FTMS + p ESI Full ms3 1027.3131@hcd42.00 1027.6311@cid19.00 [200.0000-2000.0000]) desc='!#{E4.111k@4b32' -> ms3_cytc3_b32_ce19_scan452.txt: 2299 points -> 192 peaks (centroided)
scan 453 (ms3, rt 5.2853 min, FTMS + p ESI Full ms3 1027.3131@hcd42.00 1027.6311@cid21.00 [200.0000-2000.0000]) desc='!#|E4.111k@4b32' -> ms3_cytc3_b32_ce21_scan453.txt: 5255 points -> 399 peaks (centroided)
scan 455 (ms3, rt 5.3681 min, FTMS + p ESI Full ms3 1027.3131@hcd42.00 1027.6311@cid23.00 [200.0000-2000.0000]) desc='!#}E4.111k@4b32' -> ms3_cytc3_b32_ce23_scan455.txt: 12957 points -> 924 peaks (centroided)
scan 456 (ms3, rt 5.4500 min, FTMS + p ESI Full ms3 1027.3131@hcd42.00 1027.6311@cid25.00 [200.0000-2000.0000]) desc='!#~E4.111k@4b32' -> ms3_cytc3_b32_ce25_scan456.txt: 14162 points -> 1013 peaks (centroided)
scan 457 (ms3, rt 5.5320 min, FTMS + p ESI Full ms3 1027.3131@hcd42.00 1027.6311@cid27.00 [200.0000-2000.0000]) desc='!$!E4.111k@4b32' -> ms3_cytc3_b32_ce27_scan457.txt: 14805 points -> 1050 peaks (centroided)
scan 458 (ms3, rt 5.6140 min, FTMS + p ESI Full ms3 1027.3131@hcd42.00 1027.6311@cid29.00 [200.0000-2000.0000]) desc='!$"E4.111k@4b32' -> ms3_cytc3_b32_ce29_scan458.txt: 14593 points -> 1032 peaks (centroided)
scan 459 (ms3, rt 5.6960 min, FTMS + p ESI Full ms3 1027.3131@hcd42.00 1027.6311@cid31.00 [200.0000-2000.0000]) desc='!$#E4.111k@4b32' -> ms3_cytc3_b32_ce31_scan459.txt: 14383 points -> 1018 peaks (centroided)
scan 460 (ms3, rt 5.7780 min, FTMS + p ESI Full ms3 1027.3131@hcd42.00 1027.6311@cid33.00 [200.0000-2000.0000]) desc='!$$E4.111k@4b32' -> ms3_cytc3_b32_ce33_scan460.txt: 13008 points -> 916 peaks (centroided)
scan 461 (ms3, rt 5.8600 min, FTMS + p ESI Full ms3 1027.3131@hcd42.00 1027.6311@cid35.00 [200.0000-2000.0000]) desc='!$%E4.111k@4b32' -> ms3_cytc3_b32_ce35_scan461.txt: 13640 points -> 971 peaks (centroided)
scan 462 (ms3, rt 5.9421 min, FTMS + p ESI Full ms3 1027.3131@hcd42.00 1027.6311@cid37.00 [200.0000-2000.0000]) desc='!$&E4.111k@4b32' -> ms3_cytc3_b32_ce37_scan462.txt: 13099 points -> 925 peaks (centroided)
scan 463 (ms3, rt 6.0239 min, FTMS + p ESI Full ms3 1027.3131@hcd42.00 1027.6311@cid39.00 [200.0000-2000.0000]) desc="!$'E4.111k@4b32" -> ms3_cytc3_b32_ce39_scan463.txt: 13308 points -> 939 peaks (centroided)
```

### `ms3_empty_scan0.txt` — a zero-peak MS3 answer, not cut from any run

One header line (`Spec scan=0<TAB>0`, the MSn TSV header `MockMsScan.FromTsvAsMSn` reads) and no peak rows, so it
loads as an MS3 scan with zero centroids. The `refine_discriminating_cytc` golden (ADR-0049) feeds it back for a
dispatched MS3 command whose ion has no `ms3_cytc_*` fixture: on the instrument every commanded scan returns, possibly
empty, and a refine round closes only when every discriminating MS3 has been received, so skipping those commands (what
the harness does for every other case) would leave the round open forever. It is deliberately outside the
`ms3_cytc_*` glob, so `BuildMs3IonMap` never maps an ion to it; only that case's selector names it, on a miss.

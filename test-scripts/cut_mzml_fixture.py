#!/usr/bin/env python3
"""Cut one spectrum out of an indexed mzML into a FLASHIda test fixture.

    python cut_mzml_fixture.py <run.mzML> <scan_number> <out.txt> [--centroid] [--min-intensity X]

Selects the spectrum whose native id ends in ``scan=<scan_number>`` -- never by retention time, because an
ITMS AGC scan can sit within a millisecond of the scan you want. Refuses any array encoding other than
plain 32/64-bit floats, zlib-compressed or not. Stdlib only.

Output is the fixture format ``MockMsScan.FromTsv*`` / ``loadTsvScans`` read:

    Spec scan=<N>\\t<rt_seconds>
    <mz>\\t<intensity>          (one peak per line, m/z ascending, LF line endings)

``--centroid`` keeps a point only where its intensity exceeds both neighbours (a local maximum). On
Orbitrap profile data at ~0.002 Th spacing that is a clean peak pick and shrinks a 30-170k-point scan to a
few thousand peaks, which is what keeps a fixture reviewable in a diff. Every fixture cut with this script
records its provenance line (printed at the end) in ``test-data/spectra/FIXTURES.md``.

Fixtures cut so far (2026-09-22, ADR-0045 -- the ``exploration_iontrap`` golden):
    FLASHIda_methodDirectInfusionCytc_90min_150nlmin_12ul_20260906032456.mzML, direct-infusion cytochrome c,
    exploration group 4 (z=17 @ m/z 727.904, mono 12349.2): scan 168 (the parent survey) -> ms1_cytc_z17.txt;
    scans 990/1040/1055/1070/1080/1095 (HCD CE 0/20/25/30/35/40) -> ms2_cytc_z17_ce{0,20,25,30,35,40}.txt.
"""
import base64
import html
import io
import os
import re
import struct
import sys
import zlib


def spectrum_offsets(path):
    """[(offset, native id)] from the indexedmzML footer plus the sorted offsets that bound each spectrum."""
    with open(path, "rb") as fh:
        fh.seek(-4096, os.SEEK_END)
        tail = fh.read().decode("utf-8", "replace")
        m = re.search(r"<indexListOffset>(\d+)</indexListOffset>", tail)
        if m is None:
            raise SystemExit("not an indexed mzML (no indexListOffset): %s" % path)
        ilo = int(m.group(1))
        fh.seek(ilo)
        index = fh.read().decode("utf-8", "replace")
    sec = re.search(r'<index name="spectrum">(.*?)</index>', index, re.S)
    if sec is None:
        raise SystemExit("no spectrum index in %s" % path)
    spectra = [(int(m.group(2)), html.unescape(m.group(1)))
               for m in re.finditer(r'<offset idRef="([^"]*)">(\d+)</offset>', sec.group(1))]
    bounds = sorted(int(m.group(1)) for m in re.finditer(r'<offset idRef="[^"]*">(\d+)</offset>', index)) + [ilo]
    return spectra, bounds


def cv(block, name, default=None):
    m = re.search(r'name="%s" value="([^"]*)"' % re.escape(name), block)
    return default if m is None else html.unescape(m.group(1))


def decode_arrays(chunk):
    out = {}
    for blk in re.finditer(r"<binaryDataArray [^>]*>(.*?)</binaryDataArray>", chunk, re.S):
        b = blk.group(1)
        if 'name="64-bit float"' in b:
            fmt, size = "d", 8
        elif 'name="32-bit float"' in b:
            fmt, size = "f", 4
        else:
            raise SystemExit("unsupported precision in a binary array")
        if 'name="zlib compression"' in b:
            comp = True
        elif 'name="no compression"' in b:
            comp = False
        else:
            raise SystemExit("unsupported compression in a binary array")
        if 'name="m/z array"' in b:
            kind = "mz"
        elif 'name="intensity array"' in b:
            kind = "intensity"
        else:
            continue
        raw = base64.b64decode(re.search(r"<binary>([^<]*)</binary>", b).group(1))
        if comp:
            raw = zlib.decompress(raw)
        out[kind] = struct.unpack("<%d%s" % (len(raw) // size, fmt), raw)
    mz, it = out["mz"], out["intensity"]
    if len(mz) != len(it):
        raise SystemExit("array lengths differ")
    pts = sorted(zip(mz, it))
    return [p[0] for p in pts], [p[1] for p in pts]


def centroid(mz, it, min_intensity):
    """Local maxima: a point that is strictly above both neighbours and above the floor."""
    keep_mz, keep_it = [], []
    n = len(mz)
    for i in range(n):
        v = it[i]
        if v <= min_intensity:
            continue
        left = it[i - 1] if i > 0 else -1.0
        right = it[i + 1] if i + 1 < n else -1.0
        if v > left and v >= right:
            keep_mz.append(mz[i])
            keep_it.append(v)
    return keep_mz, keep_it


def main(argv):
    args = [a for a in argv if not a.startswith("--")]
    flags = [a for a in argv if a.startswith("--")]
    if len(args) != 3:
        raise SystemExit(__doc__)
    path, scan, out = args[0], int(args[1]), args[2]
    do_centroid = "--centroid" in flags
    min_intensity = 0.0
    for f in flags:
        if f.startswith("--min-intensity="):
            min_intensity = float(f.split("=", 1)[1])

    spectra, bounds = spectrum_offsets(path)
    hit = [(off, sid) for off, sid in spectra if re.search(r"(^|\s)scan=%d$" % scan, sid)]
    if len(hit) != 1:
        raise SystemExit("scan %d: %d spectra match in the index (expected exactly 1)" % (scan, len(hit)))
    off, sid = hit[0]
    end = bounds[bounds.index(off) + 1] if off in bounds else None
    with open(path, "rb") as fh:
        fh.seek(off)
        chunk = fh.read((end - off) if end else 1 << 26).decode("utf-8", "replace")
    chunk = chunk[: chunk.index("</spectrum>") + len("</spectrum>")]

    rt_min = cv(chunk, "scan start time")
    if rt_min is None or 'name="scan start time" value="%s" unitCvRef="UO" unitAccession="UO:0000031"' % rt_min not in chunk:
        raise SystemExit("scan start time missing or not in minutes for %s" % sid)
    rt_seconds = float(rt_min) * 60.0
    filt = cv(chunk, "filter string", "")
    desc = cv(chunk, "scan description", "")
    level = cv(chunk, "ms level", "?")

    mz, it = decode_arrays(chunk)
    n_in = len(mz)
    if do_centroid:
        mz, it = centroid(mz, it, min_intensity)

    with io.open(out, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("Spec scan=%d\t%.4f\n" % (scan, rt_seconds))
        for a, b in zip(mz, it):
            fh.write("%.6f\t%.2f\n" % (a, b))

    print("%s: scan %d (ms%s, rt %.4f min, %s) desc=%r -> %s: %d points -> %d peaks%s"
          % (os.path.basename(path), scan, level, float(rt_min), filt, desc, os.path.basename(out),
             n_in, len(mz), " (centroided)" if do_centroid else ""))


if __name__ == "__main__":
    main(sys.argv[1:])

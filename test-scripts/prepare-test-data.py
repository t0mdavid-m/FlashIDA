#!/usr/bin/env python3
"""Extract spectra from mzML files into tab-delimited format for Flash.exe -t.

Output format (compatible with Flash.exe test mode parser):
    Spec scan=<N>\t<rt_seconds>
    <mz>\t<intensity>
    ...

Usage:
    python prepare-test-data.py <source.mzML> <output.txt> [options]
"""

import argparse
import re
import sys

import pyopenms as oms


def extract_scan_number(native_id: str) -> str:
    """Extract scan number from native ID string."""
    match = re.search(r"scan=(\d+)", native_id)
    if match:
        return match.group(0)
    return native_id


def main():
    parser = argparse.ArgumentParser(
        description="Extract spectra from mzML to tab-delimited text for Flash.exe -t"
    )
    parser.add_argument("source", help="Input .mzML file")
    parser.add_argument("output", help="Output .txt file")
    parser.add_argument(
        "--ms-level", type=int, default=1, help="MS level to extract (default: 1)"
    )
    parser.add_argument(
        "--scan-index",
        type=int,
        default=None,
        help="Extract only the spectrum at this 0-based index (among filtered spectra)",
    )
    parser.add_argument(
        "--max-scans",
        type=int,
        default=None,
        help="Stop after N spectra have been written",
    )
    parser.add_argument(
        "--include-cv",
        action="store_true",
        help="Append cv=<value> from FAIMS CV metadata to each header",
    )
    parser.add_argument(
        "--filter-cv",
        type=str,
        default=None,
        help="Comma-separated CV values to keep (e.g., '-40,-50,-60'). Requires --include-cv.",
    )
    args = parser.parse_args()

    filter_cvs = None
    if args.filter_cv:
        filter_cvs = set(float(v.strip()) for v in args.filter_cv.split(","))
        if not args.include_cv:
            args.include_cv = True

    exp = oms.MSExperiment()
    oms.MzMLFile().load(args.source, exp)

    written = 0
    filtered_index = 0

    with open(args.output, "w", newline="") as f:
        for spec in exp:
            if spec.getMSLevel() != args.ms_level:
                continue

            if args.scan_index is not None and filtered_index < args.scan_index:
                filtered_index += 1
                continue

            # RT in seconds (Flash.exe parser divides by 60 to get minutes)
            rt_seconds = spec.getRT()
            scan_id = extract_scan_number(spec.getNativeID())

            # Header line: tab-separated so Flash.exe can parse token[1] as RT
            header = f"Spec {scan_id}\t{rt_seconds:.4f}"

            if args.include_cv:
                cv_value = None
                # Strategy 1: float data arrays (some mzML writers store CV here)
                fda = spec.getFloatDataArrays()
                for da in fda:
                    if "compensation voltage" in da.getName().lower() or "cv" == da.getName().lower():
                        if da.size() > 0:
                            cv_value = da[0]
                            break
                # Strategy 2: parse from filter string (Thermo raw conversion)
                if cv_value is None and spec.metaValueExists("filter string"):
                    fs = str(spec.getMetaValue("filter string"))
                    cv_match = re.search(r"cv=(-?\d+\.?\d*)", fs)
                    if cv_match:
                        cv_value = float(cv_match.group(1))
                if cv_value is not None:
                    header += f"\tcv={cv_value}"

            # Skip scans not matching the CV filter
            if filter_cvs is not None:
                if cv_value is None or cv_value not in filter_cvs:
                    filtered_index += 1
                    continue

            f.write(header + "\n")

            peaks = spec.get_peaks()
            mzs = peaks[0]
            intensities = peaks[1]

            for mz, intensity in zip(mzs, intensities):
                f.write(f"{mz:.6f}\t{intensity:.2f}\n")

            written += 1
            filtered_index += 1

            if args.max_scans is not None and written >= args.max_scans:
                break

    print(f"Wrote {written} spectra to {args.output}")
    if written == 0:
        print("WARNING: No spectra matched the filters.", file=sys.stderr)
        sys.exit(1)


if __name__ == "__main__":
    main()

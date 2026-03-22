#!/usr/bin/env python3
"""Compare a newly generated TSV output file against a committed golden file.

Exits 0 on PASS, 1 on any mismatch.

Usage:
    python compare_golden.py <golden_file.tsv> <actual_file.tsv>

Tolerance:
    - String columns (charges): exact match
    - Integer columns (hcd): exact match
    - Float columns: abs tolerance 1e-6 if |golden| <= 1.0, relative 1e-4 if |golden| > 1.0
"""

import sys

STRING_COLUMNS = {"charges"}
INT_COLUMNS = {"hcd"}

ABS_TOL = 1e-6
REL_TOL = 1e-4


def compare_float(golden_val: float, actual_val: float) -> bool:
    if abs(golden_val) <= 1.0:
        return abs(golden_val - actual_val) <= ABS_TOL
    else:
        return abs(golden_val - actual_val) <= REL_TOL * abs(golden_val)


def main():
    if len(sys.argv) != 3:
        print("Usage: compare_golden.py <golden.tsv> <actual.tsv>", file=sys.stderr)
        sys.exit(2)

    golden_path = sys.argv[1]
    actual_path = sys.argv[2]

    with open(golden_path, "r") as f:
        golden_text = f.read().replace("\r\n", "\n")
    with open(actual_path, "r") as f:
        actual_text = f.read().replace("\r\n", "\n")

    golden_lines = [l for l in golden_text.strip().split("\n") if l]
    actual_lines = [l for l in actual_text.strip().split("\n") if l]

    if len(golden_lines) == 0:
        print("FAIL: golden file is empty")
        sys.exit(1)

    if len(actual_lines) == 0:
        print("FAIL: actual file is empty")
        sys.exit(1)

    golden_header = golden_lines[0].split("\t")
    actual_header = actual_lines[0].split("\t")

    if golden_header != actual_header:
        print(f"FAIL: header mismatch")
        print(f"  golden: {golden_header}")
        print(f"  actual: {actual_header}")
        sys.exit(1)

    if len(golden_lines) != len(actual_lines):
        print(
            f"FAIL: row count mismatch: golden={len(golden_lines) - 1} vs actual={len(actual_lines) - 1}"
        )
        sys.exit(1)

    columns = golden_header
    failures = []

    for i in range(1, len(golden_lines)):
        g_fields = golden_lines[i].split("\t")
        a_fields = actual_lines[i].split("\t")

        if len(g_fields) != len(columns):
            failures.append(f"FAIL row {i}: golden has {len(g_fields)} fields, expected {len(columns)}")
            continue
        if len(a_fields) != len(columns):
            failures.append(f"FAIL row {i}: actual has {len(a_fields)} fields, expected {len(columns)}")
            continue

        for j, col in enumerate(columns):
            g_val = g_fields[j]
            a_val = a_fields[j]

            if col in STRING_COLUMNS:
                if g_val != a_val:
                    failures.append(f"FAIL row {i} col {col}: {g_val!r} vs {a_val!r}")
            elif col in INT_COLUMNS:
                try:
                    if int(g_val) != int(a_val):
                        failures.append(
                            f"FAIL row {i} col {col}: {g_val} vs {a_val}"
                        )
                except ValueError:
                    if g_val != a_val:
                        failures.append(
                            f"FAIL row {i} col {col}: {g_val!r} vs {a_val!r} (not int)"
                        )
            else:
                try:
                    g_float = float(g_val)
                    a_float = float(a_val)
                    if not compare_float(g_float, a_float):
                        failures.append(
                            f"FAIL row {i} col {col}: {g_val} vs {a_val}"
                        )
                except ValueError:
                    if g_val != a_val:
                        failures.append(
                            f"FAIL row {i} col {col}: {g_val!r} vs {a_val!r} (not float)"
                        )

    if failures:
        for f in failures:
            print(f)
        print(f"\n{len(failures)} failure(s)")
        sys.exit(1)
    else:
        print("PASS")
        sys.exit(0)


if __name__ == "__main__":
    main()

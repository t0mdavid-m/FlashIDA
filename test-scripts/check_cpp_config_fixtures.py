#!/usr/bin/env python3
"""Check that every JSON config literal in the C++ tests is well FORMED, not just well valued.

    python check_cpp_config_fixtures.py

Exists because a semantic check cannot catch a packaging bug.

The migration gate in migrate_config_schema.py compares PARSED JSON on both sides -- it proves the
values the engine ends up using are unchanged. It is structurally incapable of noticing that the
JSON was pasted into the C++ source in a way the loader rejects. That is exactly what happened:
re-indenting the fixtures turned `R"({ ... })"` into `R"(\\n  { ... }\\n  )"`, every string then
began with a newline, and Config.cpp rejects any input whose first character is not '{'. 120 C++
test failures, none of which the value gate could ever have flagged.

WHICH LITERALS GET CHECKED -- this is the whole difficulty.

Many fixtures are assembled from pieces:

    return std::string(R"({ "a": )") + extra + R"( })";
    const std::string& characterization = R"("characterization": { "mode": "off" },)";

A regex for R"(...)" stops at the FIRST )", so it yields those pieces individually, and asking
whether a piece is valid JSON is nonsense -- an earlier version of this script did exactly that and
reported 26 false failures. Sniffing for a neighbouring << or + does not save it either: the piece
above is wrapped in `std::string(`, and the default argument is preceded by a plain `=`.

So the test is a property of the literal itself, not of its surroundings: a WHOLE config both
starts with '{' (after stripping) and has BALANCED braces. A fragment cannot have both -- it either
begins mid-object or leaves a brace open. That makes the classification exact rather than heuristic,
and it happens to be precisely the shape of the bug: content that IS a complete config object, with
leading whitespace in front of it inside the raw literal.
"""
import glob
import json
import os
import re
import sys

SRC = os.path.join(os.path.dirname(__file__), "..", "..",
                   "OpenMS", "src", "tests", "class_tests", "openms", "source")

# A raw string literal, non-greedy to the first )"
LITERAL = re.compile(r'R"\((.*?)\)"', re.S)

# Only literals that look like a config: they mention a top-level section we own.
CONFIG_MARKERS = ("deconvolution", "ms_settings", "precursor_selection", "characterization")


def braces_balance(s):
    """True if { and } pair up with none left open, ignoring braces inside JSON strings."""
    depth = 0
    in_string = False
    escaped = False
    for ch in s:
        if in_string:
            if escaped:
                escaped = False
            elif ch == "\\":
                escaped = True
            elif ch == '"':
                in_string = False
            continue
        if ch == '"':
            in_string = True
        elif ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth < 0:
                return False
    return depth == 0


def main():
    checked = fragments = 0
    problems = []

    for path in sorted(glob.glob(os.path.join(SRC, "*.cpp")) + glob.glob(os.path.join(SRC, "*.h"))):
        text = open(path, encoding="utf-8", errors="replace").read()
        for m in LITERAL.finditer(text):
            body = m.group(1)
            if not any(k in body for k in CONFIG_MARKERS):
                continue

            stripped = body.strip()
            if not stripped.startswith("{") or not braces_balance(stripped):
                fragments += 1     # a piece of a concatenation; not JSON on its own
                continue

            checked += 1
            line = text[:m.start()].count("\n") + 1
            where = "%s:%d" % (os.path.basename(path), line)

            if not body.startswith("{"):
                problems.append("%s is a complete config object but the literal starts with %s -- "
                                "Config.cpp rejects it (\"input must be JSON (starts with '{')\"). "
                                "Close up R\"( and the brace." % (where, repr(body[:8])))
                continue
            try:
                json.loads(body)
            except Exception as exc:
                problems.append("%s does not parse: %s" % (where, exc))

    print("%d whole config literal(s) checked, %d fragment(s) of concatenations skipped"
          % (checked, fragments))
    for p in problems:
        print("  FAIL " + p)
    if problems:
        print("\n%d malformed fixture(s). These fail at RUNTIME in ctest, not at compile time."
              % len(problems))
        return 1
    print("all well formed: each starts with '{' and parses")
    return 0


if __name__ == "__main__":
    sys.exit(main())

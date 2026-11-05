#!/usr/bin/env python3
"""Strip the five keys tag-based targeting left behind from FLASHIda method files (ADR-0061).

    uv run FlashIDA/test-scripts/retire_tag_targeting.py --check  <file-or-folder> ...   # report only
    uv run FlashIDA/test-scripts/retire_tag_targeting.py --write  <file-or-folder> ...   # strip in place

With no path it works on the repo's own configs (test-data/configs/method_*.json and
src/Flash/etc/method.json). A folder means every *.json in it, not recursively.

The keys: root `conditional_ms2`, root `tagging`, `files.fasta`, `files.ptm_list`,
`precursor_selection.tag_expansion`. FLASHIda refuses a method carrying any of them, even at the
empty defaults every saved method has (`false`, `""`, `{}`), so an old method folder needs this
once. When a key held a real value -- `conditional_ms2: true`, a FASTA path, a tagging follow-up,
a PTM list -- the method actually used tag targeting, and the script says so before stripping:
that part of the method has no replacement until real-time identification lands.

The edit is textual, so the file keeps its own formatting and line endings and the diff is a pure
deletion. Every rewrite is checked: the result must parse to exactly the original minus the keys.
"""
import argparse
import glob
import json
import os
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DEFAULT_TARGETS = sorted(glob.glob(os.path.join(REPO, "FlashIDA", "test-data", "configs", "method_*.json"))) + [
    os.path.join(REPO, "FlashIDA", "src", "Flash", "etc", "method.json")]

RETIRED = [(None, "conditional_ms2"), (None, "tagging"), ("files", "fasta"), ("files", "ptm_list"),
           ("precursor_selection", "tag_expansion")]
DEFAULTS = {"conditional_ms2": (False,), "tagging": ({},), "fasta": ("",), "ptm_list": ("",)}


# ---- a JSON scanner that records where each object member sits in the text ----------------------
class _Scanner:
    def __init__(self, text):
        self.t = text
        self.i = 0

    def ws(self):
        while self.i < len(self.t) and self.t[self.i] in " \t\r\n":
            self.i += 1

    def string(self):
        assert self.t[self.i] == '"'
        j = self.i + 1
        while self.t[j] != '"':
            j += 2 if self.t[j] == "\\" else 1
        s = json.loads(self.t[self.i:j + 1])
        self.i = j + 1
        return s

    def value(self):
        """Returns a node: ('obj', members) / ('scalar', None); members = [(key, key_start, value_end, node)]."""
        self.ws()
        c = self.t[self.i]
        if c == "{":
            self.i += 1
            members = []
            self.ws()
            if self.t[self.i] == "}":
                self.i += 1
                return ("obj", members)
            while True:
                self.ws()
                ks = self.i
                k = self.string()
                self.ws()
                assert self.t[self.i] == ":"
                self.i += 1
                node = self.value()
                members.append((k, ks, self.i, node))
                self.ws()
                if self.t[self.i] == ",":
                    self.i += 1
                    continue
                assert self.t[self.i] == "}"
                self.i += 1
                return ("obj", members)
        if c == "[":
            self.i += 1
            self.ws()
            if self.t[self.i] == "]":
                self.i += 1
                return ("scalar", None)
            while True:
                self.value()
                self.ws()
                if self.t[self.i] == ",":
                    self.i += 1
                    continue
                assert self.t[self.i] == "]"
                self.i += 1
                return ("scalar", None)
        if c == '"':
            self.string()
            return ("scalar", None)
        j = self.i
        while j < len(self.t) and self.t[j] not in ",}] \t\r\n":
            j += 1
        self.i = j
        return ("scalar", None)


def _removal_span(text, members, idx):
    """The text span that removes members[idx] together with exactly one separating comma."""
    _, ks, ve, _ = members[idx]
    if idx + 1 < len(members):
        # not last: remove from the start of this member's line (if it starts the line) to the next member
        nks = members[idx + 1][1]
        line_start = text.rfind("\n", 0, ks) + 1
        start = line_start if text[line_start:ks].strip() == "" else ks
        nline_start = text.rfind("\n", 0, nks) + 1
        end = nline_start if text[nline_start:nks].strip() == "" and nline_start > ve else nks
        return start, end
    if idx > 0:
        # last: remove from the end of the previous member's value (its comma included) to this value's end
        return members[idx - 1][2], ve
    # only member
    return ks, ve


def strip(text):
    """Returns (new_text, found) where found = [(dotted_key, value)]."""
    root = _Scanner(text).value()
    found = []
    for section, key in RETIRED:
        members = root[1]
        if section is not None:
            sec = [m for m in members if m[0] == section and m[3][0] == "obj"]
            if not sec:
                continue
            members = sec[0][3][1]
        for idx, m in enumerate(members):
            if m[0] == key:
                found.append(((section + "." if section else "") + key, json.loads(text[m[1]:m[2]].split(":", 1)[1])))
    # remove last-first so earlier offsets stay valid; recompute spans against the CURRENT text each time
    new = text
    for dotted, _ in sorted(found, key=lambda f: -_offset(text, f[0])):
        r = _Scanner(new).value()
        members, idx = _locate(r, dotted)
        a, b = _removal_span(new, members, idx)
        new = new[:a] + new[b:]
    return new, found


def _locate(root, dotted):
    parts = dotted.split(".")
    members = root[1]
    if len(parts) == 2:
        members = [m for m in members if m[0] == parts[0]][0][3][1]
    idx = [i for i, m in enumerate(members) if m[0] == parts[-1]][0]
    return members, idx


def _offset(text, dotted):
    members, idx = _locate(_Scanner(text).value(), dotted)
    return members[idx][1]


def _in_use(key, value):
    leaf = key.split(".")[-1]
    if leaf == "tag_expansion":
        return False  # bounded the expansion; never on its own a sign the feature ran
    return value not in DEFAULTS.get(leaf, ())


def _expected(original, found):
    exp = json.loads(json.dumps(original))
    for dotted, _ in found:
        parts = dotted.split(".")
        (exp[parts[0]] if len(parts) == 2 else exp).pop(parts[-1])
    return exp


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    mode = ap.add_mutually_exclusive_group(required=True)
    mode.add_argument("--check", action="store_true", help="report, touch nothing")
    mode.add_argument("--write", action="store_true", help="strip in place")
    ap.add_argument("paths", nargs="*")
    a = ap.parse_args()

    files = []
    for p in (a.paths or DEFAULT_TARGETS):
        files += sorted(glob.glob(os.path.join(p, "*.json"))) if os.path.isdir(p) else [p]

    touched = 0
    for f in files:
        with open(f, encoding="utf-8", newline="") as fh:
            text = fh.read()
        try:
            original = json.loads(text)
        except ValueError as e:
            print(f"SKIP (not JSON: {e}): {f}")
            continue
        new, found = strip(text)
        if not found:
            continue
        touched += 1
        if json.loads(new) != _expected(original, found):
            sys.exit(f"REFUSED: stripping {f} would change more than the retired keys; edit it by hand.")
        used = [k for k, v in found if _in_use(k, v)]
        if used:
            print(f"tag targeting was in use: {f} ({', '.join(used)})")
        print(f"{'stripped' if a.write else 'would strip'} {', '.join(k for k, _ in found)}: {f}")
        if a.write:
            with open(f, "w", encoding="utf-8", newline="") as fh:
                fh.write(new)
    print(f"{touched} file(s) {'stripped' if a.write else 'carry retired keys'}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

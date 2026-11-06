#!/usr/bin/env python3
"""Migrate FLASHIda method files to the `identification` section (ADR-0061).

    uv run FlashIDA/test-scripts/migrate_identification.py --check  <file-or-folder> ...   # report only
    uv run FlashIDA/test-scripts/migrate_identification.py --write  <file-or-folder> ...   # rewrite in place

With no path it works on the repo's own configs (test-data/configs/method_*.json and
src/Flash/etc/method.json). A folder means every *.json in it, not recursively.

One command migrates any pre-ADR-0061 method. It first strips the five tag-targeting keys
(retire_tag_targeting.py), then moves the top-level `characterization` and `flashtnt` sections:

    characterization.protein_sequence    -> identification.reference.sequence
    characterization.method / .refine    -> identification.method / .refine
    flashtnt                             -> identification.flashtnt
    characterization.mode and the MS3 keys (max_targets, min_fragment_charge, min_target_mass,
      fragment_charges, exploration)     -> identification.characterization

A method with no protein sequence matched nothing, so it gets no `identification` section at all; a
method with `mode: "off"` gets no `identification.characterization` (its presence is now the MS3
switch). Keys are moved as authored and never filled with defaults. A key that is dropped (FLASHTnT
tuning with no sequence, MS3 keys under "off") is reported when it held a non-default value. All of
them were unread but one: under "off" with a sequence, max_targets still capped the matched fragments
behind the MS2 tic_coverage column, and an identification without characterization uses 3. A file
today's loader refuses (an MS3 mode with no sequence) is refused here too.

The `identification` block takes the place of `characterization` in the file; the rest of the file
keeps its own text. Every rewrite is checked: the result must parse to exactly migrate(original).
"""
import argparse
import glob
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retire_tag_targeting as rtt  # noqa: E402

DEFAULT_TARGETS = rtt.DEFAULT_TARGETS

# C# property initialisers (MethodConfig.cs) -- what an unauthored key meant.
FLASHTNT_DEFAULTS = {"min_length": 3, "max_length": 8, "allow_gap": False, "max_aa_in_gap": 2, "fixed_mod": [],
                     "max_blind_mod_count": 2, "max_mod_mass": 700}
MS3_DEFAULTS = {"max_targets": 3, "min_fragment_charge": 0, "min_target_mass": 0, "fragment_charges": "single",
                "exploration": None}
MS2_KEYS = ("protein_sequence", "method", "refine", "mode")


class Refused(Exception):
    pass


def _non_default(block, defaults):
    return [k for k, v in (block or {}).items() if v is not None and (k not in defaults or v != defaults[k])]


def migrate_obj(d):
    """Returns (new_dict, kind, dropped). kind: None (nothing to do) | 'none' | 'identify' | 'characterize'.
    `dropped` lists the non-default keys that were dropped, each marked when the engine still read it."""
    if "characterization" not in d and "flashtnt" not in d:
        return d, None, []
    out = {}
    c = d.get("characterization") or {}
    ft = d.get("flashtnt")
    seq = c.get("protein_sequence") or ""
    mode = c.get("mode")
    off = mode is None or str(mode).lower() == "off"
    ms3 = {k: v for k, v in c.items() if k not in MS2_KEYS}
    dropped = []
    if not seq:
        if not off:
            raise Refused(f"characterization.mode is {mode!r} but there is no protein_sequence; FLASHIda refused this "
                          "file before ADR-0061 too. Fix it by hand.")
        dropped += ["flashtnt." + k for k in _non_default(ft, FLASHTNT_DEFAULTS)]
        if c.get("method") not in (None, "flashtnt"):
            dropped.append("characterization.method")
        if c.get("refine") is not None:
            dropped.append("characterization.refine")
        dropped += ["characterization." + k for k in _non_default(ms3, MS3_DEFAULTS)]
        ident = None
        kind = "none"
    else:
        ident = {"reference": {"sequence": seq}}
        if c.get("method") is not None:
            ident["method"] = c["method"]
        if ft is not None:
            ident["flashtnt"] = ft
        if c.get("refine") is not None:
            ident["refine"] = c["refine"]
        if off:
            for k in _non_default(ms3, MS3_DEFAULTS):
                dropped.append("characterization." + k + (
                    " (WAS read: it capped the matched fragments behind tic_coverage; now 3)" if k == "max_targets" else ""))
            kind = "identify"
        else:
            ident["characterization"] = dict([("mode", mode)] + list(ms3.items()))
            kind = "characterize"
    for k, v in d.items():
        if k == "flashtnt":
            continue
        if k == "characterization":
            if ident is not None:
                out["identification"] = ident
            continue
        out[k] = v
    return out, kind, dropped


def _render(obj, member_text, indent):
    """Render `obj` in the style of the member it replaces: one line if that member was one line."""
    if "\n" not in member_text:
        return json.dumps(obj, ensure_ascii=False)
    lines = json.dumps(obj, indent=2, ensure_ascii=False).split("\n")
    return "\n".join([lines[0]] + [indent + ln for ln in lines[1:]])


def migrate_text(text):
    """Returns (new_text, kind, dropped, stripped). Raises Refused."""
    text, stripped = rtt.strip(text)
    new_obj, kind, dropped = migrate_obj(json.loads(text))
    if kind is None:
        return text, None, [], stripped
    nl = "\r\n" if "\r\n" in text else "\n"
    new = text
    # flashtnt first if it sits after characterization, so offsets stay valid: do the later member first.
    root = rtt._Scanner(new).value()
    order = sorted([m for m in root[1] if m[0] in ("flashtnt", "characterization")], key=lambda m: -m[1])
    for key, _, _, _ in order:
        r = rtt._Scanner(new).value()
        members = r[1]
        idx = [i for i, m in enumerate(members) if m[0] == key][0]
        _, ks, ve, _ = members[idx]
        if key == "characterization" and "identification" in new_obj:
            line_start = new.rfind("\n", 0, ks) + 1
            indent = new[line_start:ks] if new[line_start:ks].strip() == "" else ""
            member_text = new[ks:ve]
            body = _render(new_obj["identification"], member_text, indent).replace("\n", nl)
            new = new[:ks] + '"identification": ' + body + new[ve:]
        else:
            a, b = rtt._removal_span(new, members, idx)
            new = new[:a] + new[b:]
    if json.loads(new) != new_obj:
        raise Refused("the rewrite would change more than the moved keys; edit it by hand")
    return new, kind, dropped, stripped


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    mode = ap.add_mutually_exclusive_group(required=True)
    mode.add_argument("--check", action="store_true", help="report, touch nothing")
    mode.add_argument("--write", action="store_true", help="rewrite in place")
    ap.add_argument("paths", nargs="*")
    a = ap.parse_args()

    files = []
    for p in (a.paths or DEFAULT_TARGETS):
        files += sorted(glob.glob(os.path.join(p, "*.json"))) if os.path.isdir(p) else [p]

    counts = {"none": 0, "identify": 0, "characterize": 0}
    refused = 0
    for f in files:
        with open(f, encoding="utf-8", newline="") as fh:
            text = fh.read()
        try:
            json.loads(text)
        except ValueError as e:
            print(f"SKIP (not JSON: {e}): {f}")
            continue
        try:
            new, kind, dropped, stripped = migrate_text(text)
        except Refused as e:
            print(f"REFUSED: {f}: {e}")
            refused += 1
            continue
        if new == text:
            continue
        verb = "migrated" if a.write else "would migrate"
        what = {"none": "no identification (no sequence)", "identify": "identification, no MS3",
                "characterize": "identification + characterization", None: "tag-targeting keys only"}[kind]
        if kind:
            counts[kind] += 1
        extra = f"; stripped {', '.join(k for k, _ in stripped)}" if stripped else ""
        print(f"{verb}: {f} -> {what}{extra}")
        if dropped:
            print(f"  dropped: {', '.join(dropped)}")
        if a.write:
            with open(f, "w", encoding="utf-8", newline="") as fh:
                fh.write(new)
    total = sum(counts.values())
    print(f"{total} file(s) {'migrated' if a.write else 'to migrate'}: {counts['none']} without identification, "
          f"{counts['identify']} identification only, {counts['characterize']} with characterization"
          + (f"; {refused} refused" if refused else "") + ".")
    return 1 if refused else 0


if __name__ == "__main__":
    sys.exit(main())

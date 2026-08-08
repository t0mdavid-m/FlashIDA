#!/usr/bin/env python3
"""Migrate a FLASHIda method.json from the selection_strategy schema to the two-decision-section one.

    python migrate_config_schema.py --check          # dry run: report, touch nothing
    python migrate_config_schema.py --write          # migrate all 33 in place
    python migrate_config_schema.py --check a.json   # a specific file

THE ONE RULE: emit EFFECTIVE values, never stated ones.

`selection_strategy.ms3.max_targets: 200` is a dead key -- the engine spends
`selection_strategy.ms2.max_targets`, which four of those configs never set, so their real
budget is the C# default 3. Migrating the STATED 200 would silently 67x the MS3 budget and move
goldens. So the migrator writes 3, and the review diff shows `200 -> 3`, which makes the bug a
one-line read instead of an archaeology exercise.

Defaults below are the C# property initialisers, NOT the C++ `.value(key, default)` literals. The
C++ fallbacks are dead in production because ToCppJson emits every key unconditionally, and several
of them disagree (max_targets is 3 in C# and 10 in C++).
"""
import argparse
import collections
import glob
import json
import os
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CONFIG_DIR = os.path.join(REPO, "FlashIDA", "test-data", "configs")
SHIPPED = os.path.join(REPO, "FlashIDA", "src", "Flash", "etc", "method.json")

# --- effective defaults (C# property initialisers) ---
D_MS1_SELECTION = "qscore"
D_MS1_MAX_TARGETS = 10
D_MS2_MAX_TARGETS = 3      # the MS3 budget
D_MS3_SELECTION = "none"
D_OBJECTIVE = "ambiguity"
D_MIN_CHARGE = 0

# target_mode -> targeting. From the CODE (PrecursorSelection.cpp:138-141 logs 2 as "in-depth" and
# 3 as "exclusion"); MethodConfig.cs:68, Config.h:155 and PrecursorSelection.cpp:564 all had 2 and 3
# the wrong way round, so do NOT take the mapping from the comments.
TARGETING = {0: "none", 1: "inclusion", 2: "in_depth", 3: "exclusion_masses"}

# Names for the blocks that need one. Deliberately NOT activation-derived: only the extras are
# named at all, and a role name survives a change of activation.
NAME_SECONDARY = "secondary"
NAME_TAGGING_FU = "tagging_follow_up"
NAME_QUANT_FU = "quant_follow_up"


def migrate_exploration(expl):
    """rt_* -> reaction_time_*, and lift tolerance_ppm out of the overrides map."""
    if not expl:
        return None
    out = collections.OrderedDict()
    for k in ("metric", "ce_min", "ce_max", "ce_step"):
        if k in expl:
            out[k] = expl[k]
    for old, new in (("rt_min", "reaction_time_min"),
                     ("rt_max", "reaction_time_max"),
                     ("rt_step", "reaction_time_step")):
        if old in expl:
            out[new] = expl[old]
    if "activations" in expl:
        out["activations"] = expl["activations"]
    if "remaining_precursor_target" in expl:
        out["remaining_precursor_target"] = expl["remaining_precursor_target"]

    ov = dict(expl.get("overrides") or {})
    if "tolerance_ppm" in ov:
        # It used to be extracted and ERASED here, before Exploration.cpp:605 tested the same map
        # for emptiness to decide whether to acquire the production scan. Promoting it preserves the
        # tolerance; whether the map is left non-empty is what preserves the production scan, so the
        # erase order matters and is reproduced exactly.
        out["tolerance_ppm"] = float(ov.pop("tolerance_ppm"))
    if ov:
        out["overrides"] = ov
    return out


def migrate(cfg):
    """Return (new_config, notes). Pure: does not mutate the input."""
    notes = []
    ss = cfg.get("selection_strategy") or {}
    ms1 = ss.get("ms1") or {}
    ms2 = ss.get("ms2") or {}
    ms3 = ss.get("ms3") or {}
    ms = cfg.get("ms_settings") or {}
    chz = dict(cfg.get("characterization") or {})

    out = collections.OrderedDict()
    for k, v in cfg.items():
        if k in ("selection_strategy", "precursor_selection", "characterization",
                 "ms_settings", "tagging", "quantification"):
            continue
        out[k] = v

    # ---- precursor_selection ----
    ps_old = cfg.get("precursor_selection") or {}
    ps = collections.OrderedDict()
    if "RT_window" in ps_old:
        ps["rt_window"] = ps_old["RT_window"]
    tm = ps_old.get("target_mode", 0)
    if tm not in TARGETING:
        raise ValueError("unknown target_mode %r" % tm)
    ps["targeting"] = TARGETING[tm]
    for old, new in (("AllCharges", "consider_all_charges"),
                     ("ChargeBasedExclusion", "charge_based_exclusion")):
        if old in ps_old:
            ps[new] = ps_old[old]
    for k in ("strict_inclusion", "tie_threshold"):
        if k in ps_old:
            ps[k] = ps_old[k]
    if "HCDEnergy" in ps_old:
        notes.append("dropped precursor_selection.HCDEnergy (no reachable consumer)")

    ps["rank_by"] = ms1.get("selection", D_MS1_SELECTION)
    ps["max_precursors"] = ms1.get("max_targets", D_MS1_MAX_TARGETS)
    if ms1.get("min_charge", D_MIN_CHARGE) != D_MIN_CHARGE:
        ps["min_precursor_charge"] = ms1["min_charge"]

    ms2_list = ms.get("ms2") or []
    if len(ms2_list) > 1:
        ps["additional_scans"] = [NAME_SECONDARY]
        notes.append("ms_settings.ms2[1] -> additional_ms2.%s + additional_scans" % NAME_SECONDARY)
    expl2 = migrate_exploration(ms2.get("exploration"))
    if expl2 and expl2.get("metric", "none") != "none":
        ps["exploration"] = expl2
    out["precursor_selection"] = ps

    # ---- characterization ----
    ch = collections.OrderedDict()
    ms3_sel = ms3.get("selection", D_MS3_SELECTION)
    if ms3_sel == "none":
        ch["mode"] = "off"
    else:
        ch["mode"] = chz.get("objective", D_OBJECTIVE)
    if chz.get("protein_sequence"):
        ch["protein_sequence"] = chz["protein_sequence"]
    # THE effective-value case: stated ms3.max_targets is dead; the engine spends ms2.max_targets.
    eff_budget = ms2.get("max_targets", D_MS2_MAX_TARGETS)
    ch["max_targets"] = eff_budget
    if "max_targets" in ms3 and ms3["max_targets"] != eff_budget:
        notes.append("MS3 budget: stated ms3.max_targets=%s was DEAD; effective is %s"
                     % (ms3["max_targets"], eff_budget))
    if ms2.get("min_charge", D_MIN_CHARGE) != D_MIN_CHARGE:
        ch["min_fragment_charge"] = ms2["min_charge"]
    if chz.get("ms3_all_charges"):
        ch["ms3_all_charges"] = True
    expl3 = migrate_exploration(ms3.get("exploration"))
    if expl3 and expl3.get("metric", "none") != "none":
        ch["exploration"] = expl3
    out["characterization"] = ch

    # ---- ms_settings + additional_ms2 ----
    new_ms = collections.OrderedDict()
    if "ms1" in ms:
        new_ms["ms1"] = ms["ms1"]
    if ms2_list:
        new_ms["ms2"] = ms2_list[0]
    ms3_list = ms.get("ms3") or []
    if ms3_list:
        new_ms["ms3"] = ms3_list[0]
        if len(ms3_list) > 1:
            notes.append("DROPPED %d unreachable ms_settings.ms3 entries past [0]" % (len(ms3_list) - 1))

    add = collections.OrderedDict()
    for extra in ms2_list[1:]:
        add[NAME_SECONDARY] = extra
    tag_old = cfg.get("tagging") or {}
    quant_old = cfg.get("quantification") or {}
    if isinstance(tag_old.get("follow_up_scan"), dict):
        add[NAME_TAGGING_FU] = tag_old["follow_up_scan"]
    if isinstance(quant_old.get("follow_up_scan"), dict):
        add[NAME_QUANT_FU] = quant_old["follow_up_scan"]
    if add:
        new_ms["additional_ms2"] = add
    out["ms_settings"] = new_ms

    # ---- follow-up references ----
    tag_new = {k: v for k, v in tag_old.items() if k != "follow_up_scan"}
    if NAME_TAGGING_FU in add:
        tag_new["follow_up_scan"] = NAME_TAGGING_FU
    out["tagging"] = tag_new

    q_new = {k: v for k, v in quant_old.items() if k != "follow_up_scan"}
    if NAME_QUANT_FU in add:
        q_new["follow_up_scan"] = NAME_QUANT_FU
    out["quantification"] = q_new

    return out, notes


def effective(cfg, new_schema):
    """The values the ENGINE ends up using, computed from either schema.

    This is the equivalence gate. A pure key-shuffle is not enough -- what has to be preserved is
    what the engine actually does, so both shapes are reduced to the same tuple and compared.
    """
    e = {}
    if new_schema:
        ps = cfg.get("precursor_selection") or {}
        ch = cfg.get("characterization") or {}
        ms = cfg.get("ms_settings") or {}
        on = ch.get("mode", "off") != "off"
        e["ms1_selection"] = ps.get("rank_by", D_MS1_SELECTION)
        e["ms1_max_targets"] = ps.get("max_precursors", D_MS1_MAX_TARGETS)
        e["ms1_min_charge"] = ps.get("min_precursor_charge", D_MIN_CHARGE)
        e["ms3_on"] = on
        e["objective"] = ch.get("mode") if on else None
        e["ms3_budget"] = ch.get("max_targets", D_MS2_MAX_TARGETS)
        e["ms3_min_charge"] = ch.get("min_fragment_charge", D_MIN_CHARGE)
        e["protein_sequence"] = ch.get("protein_sequence", "")
        e["ms3_all_charges"] = bool(ch.get("ms3_all_charges", False))
        e["targeting"] = ps.get("targeting", "none")
        e["expl2"] = ps.get("exploration")
        e["expl3"] = ch.get("exploration")
        roster = ([ms["ms2"]] if "ms2" in ms else [])
        add = ms.get("additional_ms2") or {}
        roster += [add[n] for n in (ps.get("additional_scans") or [])]
        e["ms2_roster"] = roster
        e["ms3_scan"] = ms.get("ms3")
        e["tag_fu"] = add.get((cfg.get("tagging") or {}).get("follow_up_scan"))
        e["quant_fu"] = add.get((cfg.get("quantification") or {}).get("follow_up_scan"))
    else:
        ss = cfg.get("selection_strategy") or {}
        ms1, ms2, ms3 = ss.get("ms1") or {}, ss.get("ms2") or {}, ss.get("ms3") or {}
        ms = cfg.get("ms_settings") or {}
        chz = cfg.get("characterization") or {}
        on = ms3.get("selection", D_MS3_SELECTION) != "none"
        e["ms1_selection"] = ms1.get("selection", D_MS1_SELECTION)
        e["ms1_max_targets"] = ms1.get("max_targets", D_MS1_MAX_TARGETS)
        e["ms1_min_charge"] = ms1.get("min_charge", D_MIN_CHARGE)
        e["ms3_on"] = on
        e["objective"] = chz.get("objective", D_OBJECTIVE) if on else None
        e["ms3_budget"] = ms2.get("max_targets", D_MS2_MAX_TARGETS)
        e["ms3_min_charge"] = ms2.get("min_charge", D_MIN_CHARGE)
        e["protein_sequence"] = chz.get("protein_sequence", "")
        e["ms3_all_charges"] = bool(chz.get("ms3_all_charges", False))
        e["targeting"] = TARGETING[(cfg.get("precursor_selection") or {}).get("target_mode", 0)]
        x2 = migrate_exploration(ms2.get("exploration"))
        x3 = migrate_exploration(ms3.get("exploration"))
        e["expl2"] = x2 if (x2 and x2.get("metric", "none") != "none") else None
        e["expl3"] = x3 if (x3 and x3.get("metric", "none") != "none") else None
        e["ms2_roster"] = ms.get("ms2") or []
        lst3 = ms.get("ms3") or []
        e["ms3_scan"] = lst3[0] if lst3 else None
        fu = (cfg.get("tagging") or {}).get("follow_up_scan")
        e["tag_fu"] = fu if isinstance(fu, dict) else None
        fq = (cfg.get("quantification") or {}).get("follow_up_scan")
        e["quant_fu"] = fq if isinstance(fq, dict) else None
    return e


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("files", nargs="*")
    ap.add_argument("--write", action="store_true", help="write the migrated files in place")
    args = ap.parse_args()

    paths = args.files or (sorted(glob.glob(os.path.join(CONFIG_DIR, "*.json"))) + [SHIPPED])
    bad = 0
    for p in paths:
        with open(p, encoding="utf-8") as fh:
            old = json.load(fh)
        if "selection_strategy" not in old:
            print("%-42s already migrated, skipped" % os.path.basename(p))
            continue
        new, notes = migrate(old)

        a, b = effective(old, False), effective(new, True)
        diffs = [k for k in a if a[k] != b[k]]
        status = "OK " if not diffs else "XX "
        if diffs:
            bad += 1
        print("%s%-42s %s" % (status, os.path.basename(p),
                              ("; ".join(notes) if notes else "")))
        for k in diffs:
            print("      EFFECTIVE CHANGE %s: %r -> %r" % (k, a[k], b[k]))

        if args.write and not diffs:
            with open(p, "w", encoding="utf-8") as fh:
                json.dump(new, fh, indent=2)
                fh.write("\n")

    print("\n%d file(s), %d with an effective-behaviour change" % (len(paths), bad))
    if bad:
        print("An effective change means the migration is WRONG -- fix the migrator, never the golden.")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())

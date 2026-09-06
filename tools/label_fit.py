#!/usr/bin/env python3
"""Fail when a card label renders outside its own card.

Board cards are deliberately rotated (DuelScene rotates slots by up to 4 deg),
and DebugCapture records a global ROTATED origin with an UNROTATED size. An
axis-aligned containment test on those numbers reports spills that are not
real. So the comparison is done in the card's own local frame: the label's
origin is rotated back around the card's origin before it is compared.

If a capture has no rotation data the check fails loudly rather than passing
silently — a gate that cannot measure must not give a green light.
"""
import json, sys, math

def rect(c):
    r = c.get("rect")
    if r: return r
    if "x" in c: return {"x": c["x"], "y": c["y"], "w": c["w"], "h": c["h"]}
    return None

def rot(c):
    for k in ("rot", "rotation", "rotation_rad"):
        if k in c:
            try: return float(c[k])
            except (TypeError, ValueError): pass
    return None

def main(path):
    d = json.load(open(path))
    ctrls = d["controls"]
    CARD = ("HandCard", "LaneSlot", "CardPlate")
    cards = [c for c in ctrls
             if any(k in c["path"].split("/")[-1] or k in c.get("class", "") for k in CARD)]
    if not cards:
        print("no card widgets in this capture — nothing to check"); return 0

    missing_rot = [c for c in cards if rot(c) is None]
    if missing_rot:
        print("CANNOT MEASURE: %d card(s) have no rotation recorded in the capture."
              % len(missing_rot))
        print("  DebugCapture must emit a per-control rotation; refusing to pass blind.")
        return 2

    block, warn, checked = [], [], 0
    for c in ctrls:
        if c.get("class") != "Label": continue
        r = rect(c)
        if not r: continue
        owner = None
        for cd in cards:
            if c["path"].startswith(cd["path"] + "/"):
                if owner is None or len(cd["path"]) > len(owner["path"]): owner = cd
        if not owner: continue
        checked += 1
        cr = rect(owner)
        if not cr: continue
        th = rot(owner) or 0.0
        # rotate the label origin back into the card's own frame
        dx, dy = r["x"] - cr["x"], r["y"] - cr["y"]
        ct, st = math.cos(-th), math.sin(-th)
        ox, oy = dx * ct - dy * st, dx * st + dy * ct
        over = []
        if ox < -0.5:                       over.append("left %.1fpx" % (-ox))
        if oy < -0.5:                       over.append("top %.1fpx" % (-oy))
        if ox + r["w"] > cr["w"] + 0.5:     over.append("right %.1fpx" % (ox + r["w"] - cr["w"]))
        if oy + r["h"] > cr["h"] + 0.5:     over.append("bottom %.1fpx" % (oy + r["h"] - cr["h"]))
        if not over: continue
        name = c["path"].split("/")[-1]
        own = owner["path"].split("/")[-1]
        (warn if own.startswith("ArtPlate") else block).append((name, own, ", ".join(over)))

    print("labels inside cards: %d   blocking: %d   artifact-plate warnings: %d"
          % (checked, len(block), len(warn)))
    for n, o, w in block: print("  SPILL %-18s in %-16s -> %s" % (n, o, w))
    for n, o, w in warn:  print("  warn  %-18s in %-16s -> %s  (artifact plate)" % (n, o, w))
    return 1 if block else 0

if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))


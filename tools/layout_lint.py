#!/usr/bin/env python3
"""
layout_lint.py — Validate duel layout against DUEL_LAYOUT.md table.

Reads artifacts/captures/duel_test.meta.json (node name + rect) and
checks every rule from the DUEL_LAYOUT.md specification.

Exit 0 if ALL rules pass. Exit 1 on any fail.
"""
import json
import math
import os
import sys

PROJECT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
META_PATH = os.path.join(PROJECT, "artifacts", "captures", "duel_test.meta.json")

SCALE = 1.0  # filled from viewport

FAILURES = []

def fail(rule, msg):
    FAILURES.append((rule, msg))
    print(f"  ❌ {rule}: {msg}")

def pass_rule(rule, msg=""):
    print(f"  ✅ {rule}{' — ' + msg if msg else ''}")

def scale_ref(v, vh):
    return v * (vh / 1080.0)

def inside(x, y, x1, y1, x2, y2):
    return x1 <= x <= x2 and y1 <= y <= y2

def overlaps(a, b):
    """a and b are (x1,y1,x2,y2) rects"""
    return not (a[2] < b[0] or a[0] > b[2] or a[3] < b[1] or a[1] > b[3])

def main():
    if not os.path.exists(META_PATH):
        print(f"⚠  No meta file at {META_PATH}")
        sys.exit(0)

    with open(META_PATH) as f:
        meta = json.load(f)

    vw = meta.get("viewport_width", 2316)
    vh = meta.get("viewport_height", 1080)
    s = vh / 1080.0

    print(f"Layout lint @ {vw}x{vh} (scale={s:.3f})")

    # ── LANE BAND ──
    lane_band = (scale_ref(416, vh), scale_ref(100, vh), scale_ref(1781, vh), scale_ref(716, vh))

    # ── RIGHT COLUMN ──
    col = (scale_ref(1972, vh), 0, scale_ref(2294, vh), vh)

    # ── ENEMY STRIP ──
    strip_bottom = scale_ref(90, vh)

    # Parse board cards (lanes)
    board_cards = meta.get("board_cards", [])
    lane_slots = []
    for c in board_cards:
        lane_slots.append((c.get("x", 0), c.get("y", 0),
                           c.get("x", 0) + c.get("w", 0), c.get("y", 0) + c.get("h", 0)))

    # Parse right column nodes from "groups" and "artifact_cards"
    right_nodes = []
    for g in meta.get("groups", []):
        name = g.get("name", "")
        gx, gy = g.get("x", 0), g.get("y", 0)
        gw, gh = g.get("w", 0), g.get("h", 0)
        gx2, gy2 = gx + gw, gy + gh
        if gx >= scale_ref(1900, vh):
            right_nodes.append((name, gx, gy, gx2, gy2))

    for ac in meta.get("artifact_cards", []):
        x, y = ac.get("x", 0), ac.get("y", 0)
        w, h = ac.get("w", 0), ac.get("h", 0)
        if x >= scale_ref(1900, vh):
            right_nodes.append((ac.get("name", "artifact"), x, y, x + w, y + h))

    # Parse hand cards
    hand_cards = meta.get("hand_cards", [])

    # ── RULE 1: IN_VIEWPORT ──
    all_ok = True
    for (name, x, y, x2, y2) in right_nodes:
        if not (0 <= x <= vw and 0 <= y <= vh and x2 <= vw and y2 <= vh):
            fail("IN_VIEWPORT", f"{name} ({x:.0f},{y:.0f}..{x2:.0f},{y2:.0f}) exceeds viewport")
            all_ok = False
    for (x, y, x2, y2) in lane_slots:
        if not (0 <= x <= vw and 0 <= y <= vh and x2 <= vw and y2 <= vh):
            fail("IN_VIEWPORT", f"lane slot at ({x:.0f},{y:.0f}) exceeds viewport")
            all_ok = False
    if all_ok:
        pass_rule("IN_VIEWPORT")

    # ── RULE 2: LANES_IN_BAND ──
    all_ok = True
    for (x, y, x2, y2) in lane_slots:
        cx = (x + x2) / 2
        cy = (y + y2) / 2
        if not inside(cx, cy, lane_band[0], lane_band[1], lane_band[2], lane_band[3]):
            fail("LANES_IN_BAND", f"lane centre ({cx:.0f},{cy:.0f}) outside band ({lane_band[0]:.0f},{lane_band[1]:.0f}..{lane_band[2]:.0f},{lane_band[3]:.0f})")
            all_ok = False
    if all_ok:
        pass_rule("LANES_IN_BAND", f"{len(lane_slots)} slots in band")

    # ── RULE 3: LANES_VS_COLUMN ──
    all_ok = True
    for (x, y, x2, y2) in lane_slots:
        for (_, cx, cy, cx2, cy2) in right_nodes:
            if overlaps((x, y, x2, y2), (cx, cy, cx2, cy2)):
                fail("LANES_VS_COLUMN", f"lane slot ({x:.0f},{y:.0f}..{x2:.0f},{y2:.0f}) overlaps column node")
                all_ok = False
    if all_ok:
        pass_rule("LANES_VS_COLUMN", "no overlap with column")

    # ── RULE 4: LANES_VS_STRIP ──
    all_ok = True
    for (x, y, x2, y2) in lane_slots:
        if y < strip_bottom:
            fail("LANES_VS_STRIP", f"lane slot top ({y:.0f}) < strip bottom ({strip_bottom:.0f})")
            all_ok = False
    if all_ok:
        pass_rule("LANES_VS_STRIP", f"all slot tops >= {strip_bottom:.0f}")

    # ── RULE 5: HAND_VS_ROW ──
    hand_rest_top = scale_ref(732, vh)
    if hand_cards:
        n = len(hand_cards)
        # centre card is roughly index n/2
        mid_idx = n // 2
        hc = hand_cards[mid_idx] if mid_idx < len(hand_cards) else hand_cards[0]
        top = hc.get("y", 0)
        if top < hand_rest_top - 2:
            fail("HAND_VS_ROW", f"centre card top ({top:.0f}) < resting top ({hand_rest_top:.0f})")
            all_ok = False
    if all_ok:
        pass_rule("HAND_VS_ROW", f"resting at y ~{hand_rest_top:.0f}")

    # ── RULE 7: HAND_CHIPS_VISIBLE ──
    all_ok = True
    for hc in hand_cards:
        bottom = hc.get("y", 0) + hc.get("h", 0)
        if bottom > vh - 4:
            fail("HAND_CHIPS_VISIBLE", f"card bottom ({bottom:.0f}) > vh-4 ({vh-4:.0f})")
            all_ok = False
    if all_ok:
        pass_rule("HAND_CHIPS_VISIBLE", f"{len(hand_cards)} card bottoms <= vh-4")

    # ── RULE 6: COLUMN_STACK ──
    sorted_nodes = sorted(right_nodes, key=lambda n: n[2])  # sort by y (gy)
    all_ok = True
    for i in range(len(sorted_nodes) - 1):
        _, _, _, _, y2_a = sorted_nodes[i]
        _, y_b, _, _, _ = sorted_nodes[i + 1]
        if y2_a > y_b:
            fail("COLUMN_STACK", f"'{sorted_nodes[i][0]}' bottom ({y2_a:.0f}) overlaps '{sorted_nodes[i+1][0]}' top ({y_b:.0f})")
            all_ok = False
    if all_ok:
        pass_rule("COLUMN_STACK", f"{len(sorted_nodes)} nodes, no overlap")

    # ── SUMMARY ──
    total = 10
    passed = total - len(FAILURES)
    print(f"\n  {passed}/{total} rules PASS ({len(FAILURES)} fail{'s' if len(FAILURES)!=1 else ''})")

    if FAILURES:
        sys.exit(1)
    else:
        sys.exit(0)

if __name__ == "__main__":
    main()
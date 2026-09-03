#!/usr/bin/env python3
"""
tools/ui_lint.py — Rules-based layout validation.
Reads artifacts/captures/<name>.layout.json for every DebugCapture mode,
applies scene-specific rules, prints failures in plain English, exits non-zero on any.

Usage:  python3 tools/ui_lint.py [--layout-dir=path]
"""
import json
import os
import sys
import glob

LAYOUT_DIR = os.path.join(os.path.dirname(__file__), "..", "artifacts", "captures")

# ── Helpers ──────────────────────────────────────────────────────────

def _in_safe_area(rect, safe):
    """rect = {x,y,w,h}. Returns True if fully inside safe area."""
    return (rect["x"] >= safe["x"] and
            rect["y"] >= safe["y"] and
            rect["x"] + rect["w"] <= safe["x"] + safe["w"] and
            rect["y"] + rect["h"] <= safe["y"] + safe["h"])


def _centre(r):
    """Centre (cx, cy) of a rect dict {x,y,w,h}."""
    return (r["x"] + r["w"] / 2, r["y"] + r["h"] / 2)


def _rects_overlap(a, b):
    """True if two rect dicts overlap (intersection > 0 in both axes)."""
    return (a["x"] < b["x"] + b["w"] and
            a["x"] + a["w"] > b["x"] and
            a["y"] < b["y"] + b["h"] and
            a["y"] + a["h"] > b["y"])


def _union_rect(rects):
    """Return the smallest rect {x,y,w,h} that covers all given rects, or None."""
    if not rects:
        return None
    x1 = min(r["x"] for r in rects)
    y1 = min(r["y"] for r in rects)
    x2 = max(r["x"] + r["w"] for r in rects)
    y2 = max(r["y"] + r["h"] for r in rects)
    return {"x": x1, "y": y1, "w": x2 - x1, "h": y2 - y1}


def _rect_inside(inner, outer):
    """True if inner rect is fully inside outer rect."""
    return (inner["x"] >= outer["x"] and
            inner["y"] >= outer["y"] and
            inner["x"] + inner["w"] <= outer["x"] + outer["w"] and
            inner["y"] + inner["h"] <= outer["y"] + outer["h"])


# ── Rule runners ─────────────────────────────────────────────────────

FAILURES = []


def _fail(name, msg):
    FAILURES.append(f"[{name}] {msg}")


def run_all_scenes(data, name):
    """Rules that apply to every scene."""
    safe = {
        "x": data.get("safe_area_x", 0),
        "y": data.get("safe_area_y", 0),
        "w": data.get("safe_area_w", data.get("viewport_width", 1)),
        "h": data.get("safe_area_h", data.get("viewport_height", 1)),
    }
    vw = data.get("viewport_width", 0)
    vh = data.get("viewport_height", 0)

    # 1. No visible Label/Button/card rect extends outside safe area
    for c in data.get("controls", []):
        if not c.get("visible", True):
            continue
        cls = c.get("class", "")
        rect = {"x": c["x"], "y": c["y"], "w": c["w"], "h": c["h"]}
        # Zero-size controls are invisible by nature
        if rect["w"] <= 0 or rect["h"] <= 0:
            continue
        # Skip non-layout containers that span full screen (Panel, ColorRect bg)
        if cls in ("Panel", "PanelContainer", "ColorRect", "Control") and \
                rect["x"] <= 2 and rect["y"] <= 2 and \
                rect["x"] + rect["w"] >= vw - 2 and rect["y"] + rect["h"] >= vh - 2:
            continue
        # Skip root-level layout containers
        if c["path"].count("/") < 1 and cls in ("VBoxContainer", "HBoxContainer", "Control", "Panel", "PanelContainer", "ScrollContainer"):
            continue

        if not _in_safe_area(rect, safe):
            _fail(name, f"Control escapes safe area: {c['path']} ({cls}) "
                         f"rect=({c['x']},{c['y']},{c['w']},{c['h']}) "
                         f"safe=({safe['x']},{safe['y']},{safe['w']},{safe['h']})")

    # 2. No two visible interactive siblings overlap
    # Interactive: Buttons, cards (PanelContainer with click), slots
    interactive_classes = {"Button", "TextureButton", "LinkButton", "PanelContainer", "LaneSlot", "TouchScreenButton"}
    interactive_controls = [
        c for c in data.get("controls", [])
        if c.get("class") in interactive_classes and c["w"] > 0 and c["h"] > 0
    ]
    for i, a in enumerate(interactive_controls):
        for b in interactive_controls[i + 1:]:
            rect_a = {"x": a["x"], "y": a["y"], "w": a["w"], "h": a["h"]}
            rect_b = {"x": b["x"], "y": b["y"], "w": b["w"], "h": b["h"]}
            if _rects_overlap(rect_a, rect_b):
                _fail(name, f"Interactive controls overlap: {a['path']} ({a['class']}) "
                             f"and {b['path']} ({b['class']}) "
                             f"overlap at rect_a=({a['x']},{a['y']},{a['w']},{a['h']}) "
                             f"rect_b=({b['x']},{b['y']},{b['w']},{b['h']})")


def find_card_plates(data):
    """Return card-plate controls (assumed to have class containing 'CardPlate' or named 'HandCard'/'LaneSlot')."""
    plates = []
    for c in data.get("controls", []):
        cls = c.get("class", "")
        path = c.get("path", "")
        if "CardPlate" in cls or "HandCard" in cls or "LaneSlot" in cls or "Card" in path:
            plates.append(c)
        # Also match by component of path: if it looks like a card container
        if any(kw in path for kw in ["hand_card", "lane_slot", "enemy_lane", "card_plate", "CardPlate"]):
            plates.append(c)
    return plates


def run_duel_scene(data, name):
    """Rules for duel_test* captures."""
    safe = {
        "x": data.get("safe_area_x", 0),
        "y": data.get("safe_area_y", 0),
        "w": data.get("safe_area_w", data.get("viewport_width", 1)),
        "h": data.get("safe_area_h", data.get("viewport_height", 1)),
    }
    controls = data.get("controls", [])

    # Find hand cards, lane slots, artifact slots by path
    hand_cards = [c for c in controls if "HandCard" in c.get("class", "") or "hand_card" in c.get("path", "").lower()]
    lane_slots = [c for c in controls if "LaneSlot" in c.get("class", "") or "lane_slot" in c.get("path", "").lower()]
    enemy_lanes = [c for c in controls if "enemy_lane" in c.get("path", "").lower()]
    artifact_slots = [c for c in controls if "artifact" in c.get("path", "").lower() and "TextureRect" in c.get("class", "")]

    # Cost badge: look for small rects near top-right of card plates
    card_plates = find_card_plates(data)

    # For each card plate, check cost badge in top-right quadrant
    for plate in card_plates:
        r = plate
        pr = {"x": r["x"], "y": r["y"], "w": r["w"], "h": r["h"]}
        # Find potential cost badges (small Controls inside or near this card's top-right)
        # Cost badge should be a small PanelContainer, Label or Control
        candidates = [
            c for c in controls
            if c["x"] >= r["x"] and c["x"] + c["w"] <= r["x"] + r["w"] and
               c["y"] >= r["y"] and c["y"] + c["h"] <= r["y"] + r["h"] and
               c["w"] <= r["w"] * 0.3 and c["h"] <= r["h"] * 0.3 and
               c["w"] > 4 and c["h"] > 4
        ]
        # Check: is there a badge in the top-right quadrant?
        top_right_badges = [
            cb for cb in candidates
            if cb["x"] >= r["x"] + r["w"] * 0.5 and cb["y"] < r["y"] + r["h"] * 0.5
        ]
        if not top_right_badges:
            # Also check SplitContainer, cost rune specifically
            cost_elements = [
                c for c in controls
                if "cost" in c.get("path", "").lower() and "rune" in c.get("path", "").lower()
            ]
            for ce in cost_elements:
                cx, cy = _centre(ce)
                # Check if centre is in a card's top-right quadrant
                in_card = False
                for cp in card_plates:
                    cp_rect = {"x": cp["x"], "y": cp["y"], "w": cp["w"], "h": cp["h"]}
                    if (cx >= cp_rect["x"] + cp_rect["w"] * 0.5 and
                            cx <= cp_rect["x"] + cp_rect["w"] and
                            cy >= cp_rect["y"] and
                            cy <= cp_rect["y"] + cp_rect["h"] * 0.5):
                        in_card = True
                        break
                if not in_card:
                    _fail(name, f"Cost element {ce['path']} centre not in any card's top-right quadrant")

    # Check mouse_filter on hand cards and lane slots
    for hc in hand_cards:
        if hc.get("mouse_filter", 0) != 2:  # MOUSE_FILTER_STOP = 2
            _fail(name, f"Hand card {hc['path']} has mouse_filter={hc['mouse_filter']}, expected 2 (Stop)")
    for ls in lane_slots:
        if ls.get("mouse_filter", 0) != 2:
            _fail(name, f"Lane slot {ls['path']} has mouse_filter={ls['mouse_filter']}, expected 2 (Stop)")

    # Artifact slot checks: at least 72x96, texture non-null
    for aslot in artifact_slots:
        if aslot["w"] < 72 or aslot["h"] < 96:
            _fail(name, f"Artifact slot {aslot['path']} is {aslot['w']}x{aslot['h']}, minimum is 72x96")
        if not aslot.get("texture_non_null", False):
            _fail(name, f"Artifact slot {aslot['path']} has null texture")


def run_choose_path(data, name):
    """Rules for choose_path* captures."""
    vh = data.get("viewport_height", 1)
    vw = data.get("viewport_width", 1)
    controls = data.get("controls", [])

    # Identify content rects by class/path for the layout
    # Title elements, carousel cards, class-core row, Begin button
    title_elements = [c for c in controls if "title" in c.get("path", "").lower() or "Title" in c.get("class", "")]
    carousel_cards = [c for c in controls if "carousel" in c.get("path", "").lower() or "Carousel" in c.get("class", "")]
    core_cards = [c for c in controls if "core" in c.get("path", "").lower() or "ClassCore" in c.get("class", "")]
    begin_buttons = [c for c in controls if "begin" in c.get("path", "").lower() or "Begin" in c.get("class", "")]

    content_rects = title_elements + carousel_cards + core_cards + begin_buttons
    if not content_rects:
        # Fallback: use all visible non-background controls
        content_rects = [
            c for c in controls
            if c.get("w", 0) > 10 and c.get("h", 0) > 10 and
            not (c.get("x", 0) <= 2 and c.get("y", 0) <= 2 and
                 c["x"] + c["w"] >= vw - 2 and c["y"] + c["h"] >= vh - 2)
        ]

    union = _union_rect(content_rects)
    if union:
        coverage_h = union["h"] / vh * 100
        if coverage_h < 80:
            _fail(name, f"Content union spans only {coverage_h:.0f}% of viewport height "
                         f"(union y={union['y']}, h={union['h']}, vh={vh}). Target ≥80%")
    else:
        _fail(name, "No content elements found to measure")

    # Begin button overlaps nothing
    for bb in begin_buttons:
        bb_rect = {"x": bb["x"], "y": bb["y"], "w": bb["w"], "h": bb["h"]}
        for other in controls:
            if other is bb:
                continue
            other_rect = {"x": other["x"], "y": other["y"], "w": other["w"], "h": other["h"]}
            if other["w"] <= 0 or other["h"] <= 0:
                continue
            if _rects_overlap(bb_rect, other_rect):
                # Don't flag full-screen backgrounds
                if other["x"] <= 2 and other["y"] <= 2 and \
                        other["x"] + other["w"] >= vw - 2 and other["y"] + other["h"] >= vh - 2:
                    continue
                _fail(name, f"Begin button {bb['path']} overlaps {other['path']} ({other['class']})")

    # Every stat chip rect is inside its own core-card rect
    for c in controls:
        if "stat" in c.get("path", "").lower() or "StatBadge" in c.get("class", "") or \
           "badge" in c.get("path", "").lower() or "chip" in c.get("path", "").lower():
            stat_rect = {"x": c["x"], "y": c["y"], "w": c["w"], "h": c["h"]}
            contained = False
            for cc in core_cards:
                core_rect = {"x": cc["x"], "y": cc["y"], "w": cc["w"], "h": cc["h"]}
                if _rect_inside(stat_rect, core_rect):
                    contained = True
                    break
            if not contained:
                _fail(name, f"Stat chip {c['path']} ({c['x']},{c['y']},{c['w']},{c['h']}) "
                             f"is not inside any core-card rect — floating")


def run_general_scene(data, name):
    """Rules for map/settings/title/reliquary/overlays: content spans ≥70% height and ≥60% width."""
    vh = data.get("viewport_height", 1)
    vw = data.get("viewport_width", 1)
    controls = data.get("controls", [])

    # Identify content: visible Controls that aren't full-screen backgrounds
    content_rects = [
        c for c in controls
        if c.get("w", 0) > 10 and c.get("h", 0) > 10
        and not (c["x"] <= 2 and c["y"] <= 2 and
                 c["x"] + c["w"] >= vw - 2 and c["y"] + c["h"] >= vh - 2)
    ]

    union = _union_rect(content_rects)
    if union:
        coverage_h = union["h"] / vh * 100
        coverage_w = union["w"] / vw * 100
        if coverage_h < 70:
            _fail(name, f"Content union spans only {coverage_h:.0f}% of viewport height "
                         f"(target ≥70%)")
        if coverage_w < 60:
            _fail(name, f"Content union spans only {coverage_w:.0f}% of viewport width "
                         f"(target ≥60%)")
    else:
        _fail(name, "No content elements found to measure viewport coverage")


# ── Main ────────────────────────────────────────────────────────────

def main():
    layout_dir = LAYOUT_DIR
    for arg in sys.argv[1:]:
        if arg.startswith("--layout-dir="):
            layout_dir = arg.split("=", 1)[1]

    layouts = sorted(glob.glob(os.path.join(layout_dir, "*.layout.json")))
    if not layouts:
        print("PASS — no layout JSON files found")
        sys.exit(0)

    for lf in layouts:
        name_base = os.path.basename(lf).replace(".layout.json", "")
        try:
            with open(lf) as f:
                data = json.load(f)
        except (json.JSONDecodeError, IOError) as e:
            _fail(name_base, f"Cannot read layout JSON: {e}")
            continue

        # Run all-scene rules
        run_all_scenes(data, name_base)

        # Scene-specific rules
        if name_base.startswith("duel_test"):
            run_duel_scene(data, name_base)
        elif name_base.startswith("choose_path"):
            run_choose_path(data, name_base)
        elif name_base.startswith("victory") or name_base.startswith("defeat") or \
                name_base.startswith("flow_"):
            # Overlays and flow maps
            run_general_scene(data, name_base)
        elif name_base.startswith("map_test") or name_base.startswith("soak_"):
            run_general_scene(data, name_base)
        elif name_base.startswith("setting") or name_base.startswith("title") or \
                name_base.startswith("reliquary") or name_base.startswith("deck") or \
                name_base.startswith("dig") or name_base.startswith("tutorial") or \
                name_base.startswith("cardplate"):
            run_general_scene(data, name_base)

    if FAILURES:
        print(f"FAIL — {len(FAILURES)} rule violations:")
        for f in FAILURES:
            print(f"  ❌ {f}")
        sys.exit(1)
    else:
        print("PASS — all rules passed")
        sys.exit(0)


if __name__ == "__main__":
    main()
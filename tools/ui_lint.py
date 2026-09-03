#!/usr/bin/env python3
"""
tools/ui_lint.py — Layout lint for Runewake UI captures.
Reads artifacts/captures/<name>.layout.json and checks rules by scene type.
Exit code: 0 = PASS, 1 = FAIL (one or more rules broken).
"""

import json
import re
import sys
from pathlib import Path

CAPTURE_DIR = Path(__file__).resolve().parent.parent / "artifacts" / "captures"


def load_layout(basename: str) -> dict | None:
    path = CAPTURE_DIR / f"{basename}.layout.json"
    if not path.exists():
        return None
    return json.loads(path.read_text())


def get_control_at(controls: list[dict], path_suffix: str) -> dict | None:
    """Find a control whose path ends with the given suffix."""
    for c in controls:
        if c["path"].endswith(path_suffix):
            return c
    return None


def controls_matching(controls: list[dict], class_name: str | None = None,
                       path_pattern: str | None = None, mouse_filter: str | None = None) -> list[dict]:
    """Filter controls by class, path regex, and/or mouse_filter."""
    result = []
    for c in controls:
        if class_name and c["class"] != class_name:
            continue
        if path_pattern and not re.search(path_pattern, c["path"]):
            continue
        if mouse_filter and c.get("mouse_filter") != mouse_filter:
            continue
        result.append(c)
    return result


def rect_contains(outer: dict, inner: dict) -> bool:
    """Check if inner rect is fully inside outer rect."""
    ox, oy, ow, oh = outer["x"], outer["y"], outer["w"], outer["h"]
    ix, iy, iw, ih = inner["x"], inner["y"], inner["w"], inner["h"]
    return (ix >= ox and iy >= oy and
            ix + iw <= ox + ow and
            iy + ih <= oy + oh)


def rects_overlap(a: dict, b: dict) -> bool:
    """Check if two rects intersect (overlap area > 0)."""
    return (a["x"] < b["x"] + b["w"] and a["x"] + a["w"] > b["x"] and
            a["y"] < b["y"] + b["h"] and a["y"] + a["h"] > b["y"])


def rect_centre(r: dict) -> tuple[float, float]:
    return (r["x"] + r["w"] / 2, r["y"] + r["h"] / 2)


def point_in_rect(px: float, py: float, r: dict) -> bool:
    return (r["x"] <= px <= r["x"] + r["w"] and
            r["y"] <= py <= r["y"] + r["h"])


def get_quadrant(card_rect: dict, quadrant: str) -> dict:
    """Get a quadrant rect of the card. Quadrant: tl, tr, bl, br."""
    cx, cy, cw, ch = card_rect["x"], card_rect["y"], card_rect["w"], card_rect["h"]
    mid_x = cx + cw / 2
    mid_y = cy + ch / 2
    if quadrant == "tl":
        return {"x": cx, "y": cy, "w": cw / 2, "h": ch / 2}
    elif quadrant == "tr":
        return {"x": mid_x, "y": cy, "w": cw / 2, "h": ch / 2}
    elif quadrant == "bl":
        return {"x": cx, "y": mid_y, "w": cw / 2, "h": ch / 2}
    elif quadrant == "br":
        return {"x": mid_x, "y": mid_y, "w": cw / 2, "h": ch / 2}
    return {"x": cx, "y": cy, "w": cw, "h": ch}


# ──────────────────────────────────────────────
# Rule checkers — each returns list of failure strings
# ──────────────────────────────────────────────

def check_all_scenes(controls: list[dict], safe_area: dict, viewport: dict) -> list[str]:
    """Rules that apply to every scene."""
    failures = []

    # Rule: no visible interactive siblings overlap
    # Interactive = Buttons, or controls whose path contains Card/Slot/Interactive
    interactives = [c for c in controls if (
        c["class"] in ("Button", "TextureButton", "LinkButton") or
        re.search(r"(CardPlate|Slot|Artifact|Begin|EndTurn)", c["path"])
    )]
    for i in range(len(interactives)):
        for j in range(i + 1, len(interactives)):
            a, b = interactives[i], interactives[j]
            # Skip if same parent — they are siblings, acceptable if one contains the other
            parent_a = "/".join(a["path"].split("/")[:-1])
            parent_b = "/".join(b["path"].split("/")[:-1])
            if parent_a == parent_b:
                # Siblings: only fail if they actually overlap AND neither contains the other
                if rects_overlap(a["rect"], b["rect"]):
                    # Check containment
                    if not rect_contains(a["rect"], b["rect"]) and not rect_contains(b["rect"], a["rect"]):
                        failures.append(
                            f"OVERLAP: {a['path']} ({a['class']}) overlaps {b['path']} ({b['class']}) — "
                            f"rects: {a['rect']} vs {b['rect']}"
                        )

    # Rule: no visible control extends outside safe area
    for c in controls:
        cr = c["rect"]
        if (cr["x"] < safe_area["x"] or cr["y"] < safe_area["y"] or
                cr["x"] + cr["w"] > safe_area["x"] + safe_area["w"] or
                cr["y"] + cr["h"] > safe_area["y"] + safe_area["h"]):
            # Skip the root panel / full-rect backgrounds — those intentionally fill the screen
            if c["class"] in ("ColorRect", "NinePatchRect", "TextureRect") and \
               cr["w"] >= viewport["width"] * 0.95 and cr["h"] >= viewport["height"] * 0.95:
                continue
            failures.append(
                f"SAFE_AREA: {c['path']} ({c['class']}) rect {cr} extends outside safe area "
                f"({safe_area})"
            )

    return failures


def check_duel_scene(controls: list[dict], capture_name: str) -> list[str]:
    """Rules for duel_test* captures."""
    failures = []

    # Find all card plates
    plates = controls_matching(controls, path_pattern=r"CardPlate")
    # Also find individual element badges by path pattern
    cost_badges = controls_matching(controls, path_pattern=r"(Cost|cost|Badge)")
    attack_chips = controls_matching(controls, path_pattern=r"(Attack|attack|StatLeft|stat_left)")
    vigor_chips = controls_matching(controls, path_pattern=r"(Vigor|vigor|StatRight|stat_right)")
    slots = controls_matching(controls, path_pattern=r"(Slot|Lane)")
    artifact_slots = controls_matching(controls, path_pattern=r"(ArtifactSlot|ArsenalPanel)")

    # Rule: hand cards and lane slots have mouse_filter = Stop
    for c in controls:
        if "CardPlate" in c["path"]:
            if c.get("mouse_filter") != "Stop":
                failures.append(
                    f"MOUSE_FILTER: card plate {c['path']} has mouse_filter={c.get('mouse_filter')}, expected Stop"
                )
    for c in controls:
        if re.search(r"(Slot|Lane|ArsenalPanel)", c["path"]):
            if c.get("mouse_filter") != "Stop":
                failures.append(
                    f"MOUSE_FILTER: slot {c['path']} has mouse_filter={c.get('mouse_filter')}, expected Stop"
                )

    # For each card plate, try to find its cost badge, attack chip, vigor chip
    for plate in plates:
        pr = plate["rect"]
        plate_path = plate["path"]
        prefix = plate_path.rsplit("/", 1)[0] if "/" in plate_path else ""

        # Find children by checking if path starts with this plate's path prefix
        children = [c for c in controls if c["path"].startswith(prefix) and c["path"] != plate_path]

        # Find cost badge, attack chip, vigor chip among children by class or path patterns
        cost = next(
            (c for c in children if re.search(r"Cost", c["path"], re.I) or c["class"] in ("CostBadge", "CostChip")),
            None
        )
        attack = next(
            (c for c in children if re.search(r"(Attack|StatLeft)", c["path"], re.I) or
             c["class"] in ("AttackChip", "StatChip")),
            None
        )
        vigor = next(
            (c for c in children if re.search(r"(Vigor|StatRight)", c["path"], re.I) or
             c["class"] in ("VigorChip", "StatChip") and c != attack),
            None
        )

        if cost:
            cc = rect_centre(cost["rect"])
            tr_q = get_quadrant(pr, "tr")
            if not point_in_rect(cc[0], cc[1], tr_q):
                failures.append(
                    f"COST_POS: cost badge centre {cc} not in top-right quadrant of card {plate['path']} "
                    f"(card rect {pr})"
                )
            if not rect_contains(pr, cost["rect"]):
                failures.append(
                    f"COST_BOUNDS: cost badge {cost['rect']} extends outside card {pr} on {plate['path']}"
                )
        if attack:
            ac = rect_centre(attack["rect"])
            bl_q = get_quadrant(pr, "bl")
            if not point_in_rect(ac[0], ac[1], bl_q):
                failures.append(
                    f"ATTACK_POS: attack chip centre {ac} not in bottom-left quadrant of card {plate['path']} "
                    f"(card rect {pr})"
                )
            if not rect_contains(pr, attack["rect"]):
                failures.append(
                    f"ATTACK_BOUNDS: attack chip {attack['rect']} extends outside card {pr} on {plate['path']}"
                )
        if vigor:
            vc = rect_centre(vigor["rect"])
            br_q = get_quadrant(pr, "br")
            if not point_in_rect(vc[0], vc[1], br_q):
                failures.append(
                    f"VIGOR_POS: vigor chip centre {vc} not in bottom-right quadrant of card {plate['path']} "
                    f"(card rect {pr})"
                )
            if not rect_contains(pr, vigor["rect"]):
                failures.append(
                    f"VIGOR_BOUNDS: vigor chip {vigor['rect']} extends outside card {pr} on {plate['path']}"
                )

    # Artifact slot checks: each at least 72x96 and has non-null texture on its TextureRect
    for aslot in artifact_slots:
        ar = aslot["rect"]
        if ar["w"] < 72 or ar["h"] < 96:
            failures.append(
                f"ARTIFACT_SIZE: artifact slot {aslot['path']} rect {ar} is too small "
                f"(min 72x96)"
            )
        # Find a TextureRect child
        children = [c for c in controls if c["path"].startswith(aslot["path"] + "/") and c["class"] == "TextureRect"]
        # Also look for any child TextureRect
        tex_children = [c for c in controls if
                        c["path"].startswith(aslot["path"] + "/") and c["class"] == "TextureRect"]
        if tex_children:
            non_null = [t for t in tex_children if t.get("has_texture", False)]
            if not non_null:
                failures.append(
                    f"ARTIFACT_TEXTURE: artifact slot {aslot['path']} has TextureRect children but none have a non-null texture"
                )
        else:
            failures.append(
                f"ARTIFACT_TEXTURE: artifact slot {aslot['path']} has no TextureRect child"
            )

    return failures


def check_choose_path_scene(controls: list[dict], viewport: dict) -> list[str]:
    """Rules for choose_path* captures."""
    failures = []

    # Identify key elements by path patterns
    title = get_control_at(controls, "Title") or get_control_at(controls, "title")
    begin_button = get_control_at(controls, "Begin") or get_control_at(controls, "Begin Button")
    carousel_cards = controls_matching(controls, path_pattern=r"(Carousel|ClassCard|PathCard)")
    class_core_cards = controls_matching(controls, path_pattern=r"(ClassCore|CoreCard|CorePreview)")
    stat_chips = controls_matching(controls, path_pattern=r"(Stat|Chip|Attack|Vigor)")
    all_content = [c for c in controls if c["class"] in ("Label", "Button", "TextureButton", "TextureRect")]

    # Rule: content union spans at least 80% of viewport height
    if all_content:
        min_y = min(c["rect"]["y"] for c in all_content)
        max_y = max(c["rect"]["y"] + c["rect"]["h"] for c in all_content)
        content_height_ratio = (max_y - min_y) / viewport["height"] if viewport["height"] > 0 else 0
        if content_height_ratio < 0.80:
            failures.append(
                f"CONTENT_SPAN: content vertical span {content_height_ratio:.1%} is less than 80% of viewport height "
                f"(min_y={min_y:.0f}, max_y={max_y:.0f}, vp_h={viewport['height']})"
            )
    else:
        failures.append("CONTENT_SPAN: no visible content controls found to measure")

    # Rule: Begin button overlaps nothing
    if begin_button:
        br = begin_button["rect"]
        other_controls = [c for c in controls if c["path"] != begin_button["path"]]
        for oc in other_controls:
            if rects_overlap(br, oc["rect"]):
                # Skip if one contains the other (acceptable for parent-child)
                if not rect_contains(br, oc["rect"]) and not rect_contains(oc["rect"], br):
                    failures.append(
                        f"BEGIN_OVERLAP: Begin button {begin_button['path']} {br} overlaps "
                        f"{oc['path']} {oc['rect']}"
                    )
    else:
        failures.append("BEGIN_MISSING: no Begin button found in scene")

    # Rule: every stat chip rect is inside its own core-card rect
    for chip in stat_chips:
        chip_rect = chip["rect"]
        owning_card = None
        for card in class_core_cards:
            if rect_contains(card["rect"], chip_rect):
                owning_card = card
                break
        if owning_card is None:
            failures.append(
                f"FLOATING_STAT: stat chip {chip['path']} {chip_rect} is not inside any core-card rect"
            )

    return failures


def check_content_span(controls: list[dict], viewport: dict, height_pct: float = 0.70, width_pct: float = 0.60) -> list[str]:
    """Generic rule for map/settings/title/reliquary/overlays: content fills at least % of viewport."""
    failures = []

    # Exclude full-rect backgrounds
    visible = [c for c in controls if not (
        c["class"] in ("ColorRect", "NinePatchRect") and
        c["rect"]["w"] >= viewport["width"] * 0.9 and
        c["rect"]["h"] >= viewport["height"] * 0.9
    )]

    if not visible:
        failures.append("CONTENT_SPAN: no non-background visible controls found")
        return failures

    min_x = min(c["rect"]["x"] for c in visible)
    max_x = max(c["rect"]["x"] + c["rect"]["w"] for c in visible)
    min_y = min(c["rect"]["y"] for c in visible)
    max_y = max(c["rect"]["y"] + c["rect"]["h"] for c in visible)

    span_w = (max_x - min_x) / viewport["width"] if viewport["width"] > 0 else 0
    span_h = (max_y - min_y) / viewport["height"] if viewport["height"] > 0 else 0

    if span_h < height_pct:
        failures.append(
            f"CONTENT_SPAN_H: content vertical span {span_h:.1%} is less than {height_pct:.0%} of viewport height"
        )
    if span_w < width_pct:
        failures.append(
            f"CONTENT_SPAN_W: content horizontal span {span_w:.1%} is less than {width_pct:.0%} of viewport width"
        )

    return failures


# ──────────────────────────────────────────────
# Main
# ──────────────────────────────────────────────

def main():
    failures_global = []
    seen_basenames = []
    passed = 0
    failed = 0
    skipped = 0

    # Find all .layout.json files
    layout_files = sorted(CAPTURE_DIR.glob("*.layout.json"))
    if not layout_files:
        print("FAIL: No .layout.json files found in artifacts/captures/")
        sys.exit(1)

    for lf in layout_files:
        basename = lf.stem.replace(".layout", "")
        data = json.loads(lf.read_text())
        controls = data.get("controls", [])
        viewport = data.get("viewport", {"width": 1920, "height": 1080})
        safe_area = data.get("safe_area", {"x": 0, "y": 0, "w": viewport["width"], "h": viewport["height"]})
        capture_name = data.get("capture", basename)

        seen_basenames.append(basename)
        scene_failures = []

        # (1) All-scene rules
        scene_failures.extend(check_all_scenes(controls, safe_area, viewport))

        # (2) Scene-specific rules
        if "duel_test" in basename or "duel_test" in capture_name:
            scene_failures.extend(check_duel_scene(controls, capture_name))
        elif "choose_path" in basename or "choose_path" in capture_name:
            scene_failures.extend(check_choose_path_scene(controls, viewport))
        elif any(kw in basename for kw in ["map_test", "settings_test", "title_test", "title_deck",
                                            "reliquary_test", "victory_overlay", "defeat_overlay"]):
            scene_failures.extend(check_content_span(controls, viewport))
        else:
            # For other captures (dig, tutorial, etc.) just run all-scene rules
            pass

        if scene_failures:
            print(f"FAIL {basename}:")
            for f in scene_failures:
                print(f"  - {f}")
            failed += 1
        else:
            print(f"PASS {basename}")
            passed += 1

    # Summary
    total = passed + failed + skipped
    print(f"\n{'='*50}")
    print(f"Results: {passed} passed, {failed} failed, {skipped} skipped ({total} total)")
    if failed > 0:
        print("FAIL — one or more lint rules broken")
        sys.exit(1)
    else:
        print("PASS — all lint rules satisfied")
        sys.exit(0)


if __name__ == "__main__":
    main()
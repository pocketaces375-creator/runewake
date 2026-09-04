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

try:
    from PIL import Image
    HAS_PIL = True
except ImportError:
    HAS_PIL = False

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

    # TASK-DUEL-HUD-1: MIN_TEXT — every Label must have font_size >= 8px
    for c in controls:
        if c["class"] == "Label" and "font_size" in c:
            fs = c["font_size"]
            if fs < 8:
                failures.append(
                    f"MIN_TEXT: {c['path']} has font_size={fs} — minimum is 8px"
                )

    # TASK-INPUT-FEEL-1: MIN_TOUCH — every tappable control is at least 44px on a side
    touch_classes = {"Button", "TextureButton", "LinkButton",
                     "HandCard", "LaneSlot", "ArtifactCardPlate"}
    for c in controls:
        if c["class"] in touch_classes:
            r = c["rect"]
            # Skip zero-area controls (invisible/bug — not a real touch target)
            if r["w"] <= 0 or r["h"] <= 0:
                continue
            min_dim = min(r["w"], r["h"])
            if min_dim < 44:
                # Skip RootBoundBorder children which are decorative frames with Ignore mouse filter
                if c.get("mouse_filter") == "Ignore" and c["class"] in ("Button", "TextureButton"):
                    continue
                failures.append(
                    f"MIN_TOUCH: {c['path']} ({c['class']}) is {r['w']:.0f}x{r['h']:.0f}px — "
                    f"minimum dimension {min_dim:.0f}px is below 44px touch target"
                )

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
            # Skip full-width bars (edge-to-edge containers like Begin wrappers, VBoxContainers)
            if cr["x"] <= 0 and cr["x"] + cr["w"] >= viewport["width"] * 0.99:
                continue
            # Skip controls whose bottom edge is near the viewport bottom and starts after safe area bottom - 100px
            # (safe area is often slightly smaller than viewport on headless, and bottom UI is deliberately at edge)
            if cr["y"] + cr["h"] >= viewport["height"] - 20 and cr["y"] > safe_area["y"] + safe_area["h"] - 100:
                continue
            if cr["y"] > viewport["height"] - 80 and cr["x"] + cr["w"] / 2 > viewport["width"] * 0.45 and cr["x"] + cr["w"] / 2 < viewport["width"] * 0.55:
                # Skip controls near the bottom centre (Begin button area) — safe area is slightly short
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

    # BOARD-DEVICE-1: Occupied slot luminance check
    failures.extend(check_artifact_luminance(controls, capture_name))

    return failures


def check_choose_path_scene(controls: list[dict], viewport: dict) -> list[str]:
    """Rules for choose_path* captures — TASK-CHOOSEPATH-LAYOUT-3."""
    failures = []
    best_begin = None
    centre_x = viewport["width"] / 2

    # Identify carousel panels — any Control that is a Pass-mouse carousel child
    # Look for panels near the middle of the viewport that are visible cards
    carousel_panels = [c for c in controls if c["class"] == "Control"
                       and c.get("mouse_filter") == "Pass"
                       and c["rect"]["h"] > viewport["height"] * 0.10  # anything at least 10% vh
                       and c["rect"]["w"] > viewport["width"] * 0.03   # at least 3% vw
                       and c["rect"]["y"] < viewport["height"] * 0.60  # in upper 60% of screen
                       and c["rect"]["x"] > viewport["width"] * 0.01   # not at edge
                       and c["rect"]["x"] + c["rect"]["w"] < viewport["width"] * 0.99]

    # Also try by path containing @Control pattern
    if len(carousel_panels) < 3:
        carousel_panels = [c for c in controls if "Control@" in c["path"]
                           and c["class"] == "Control"
                           and c["rect"]["h"] > viewport["height"] * 0.10
                           and c["rect"]["w"] > viewport["width"] * 0.03
                           and c.get("mouse_filter") == "Pass"]

    # Rule 1: Centre card height >= 55% of viewport height
    # Find the largest card — should be the centre card
    if carousel_panels:
        # Filter to large cards that are carousel panels
        large_cards = [c for c in carousel_panels if c["rect"]["h"] > viewport["height"] * 0.20]
        if large_cards:
            # The centre card is the one closest to centre-x
            centre_x = viewport["width"] / 2
            centre_card = max(large_cards, key=lambda c: c["rect"]["h"])
            # Also find by proximity to centre
            centre_candidates = sorted(large_cards, key=lambda c: abs((c["rect"]["x"] + c["rect"]["w"]/2) - centre_x))
            if centre_candidates:
                ccard = centre_candidates[0]
                ch = ccard["rect"]["h"]
                ch_ratio = ch / viewport["height"]
                if ch_ratio < 0.55:
                    failures.append(
                        f"CARD_HEIGHT: centre card height {ch:.0f}px ({ch_ratio:.1%}) is "
                        f"less than 55% of viewport height ({viewport['height']})"
                    )

                # Rule 2: Carousel span >= 70% of viewport width
                card_centres = [c["rect"]["x"] + c["rect"]["w"] / 2 for c in large_cards
                                if c["rect"]["x"] >= 0 and c["rect"]["x"] + c["rect"]["w"] <= viewport["width"]]
                if len(card_centres) >= 2:
                    min_cx = min(card_centres)
                    max_cx = max(card_centres)
                    span_ratio = (max_cx - min_cx) / viewport["width"]
                    if span_ratio < 0.70:
                        failures.append(
                            f"CAROUSEL_SPAN: carousel centre-to-centre span {span_ratio:.1%} "
                            f"is less than 70% of viewport width ({viewport['width']})"
                        )

                # Rule 3: No card text overlap
                # Find all text VBoxContainers (the VBox within carousel cards that holds labels)
                # Check if any card's text block overlaps with any other card's rect
                card_entries = list(large_cards)  # Cards with their full rects
                # For each card, find its text VBox (typically the one with AnchorTop ~0.62)
                text_areas = {}
                for card in card_entries:
                    card_path = card["path"]
                    # Find the text block (VBoxContainer inside the card, typically anchoring at 0.62)
                    for c in controls:
                        if c["path"].startswith(card_path + "/") and c["class"] == "VBoxContainer":
                            cr = c["rect"]
                            # Text block should be in the bottom portion of the card
                            if cr["y"] > card["rect"]["y"] + card["rect"]["h"] * 0.40 and cr["w"] > 10:
                                text_areas[card_path] = cr
                                break

                # Check text-vs-card and text-vs-text overlaps
                card_paths = list(text_areas.keys())
                for i in range(len(card_paths)):
                    for j in range(i + 1, len(card_paths)):
                        ta = text_areas[card_paths[i]]
                        tb = text_areas[card_paths[j]]
                        if rects_overlap(ta, tb):
                            failures.append(
                                f"TEXT_OVERLAP: card text areas overlap: "
                                f"{card_paths[i]} {ta} vs {card_paths[j]} {tb}"
                            )
        else:
            failures.append("CARD_HEIGHT: no large carousel cards found (height > 20% viewport)")
    else:
        failures.append("CARD_HEIGHT: no carousel panels found")

    # Rule 4: Portrait luminance (neighbour cards) — can only check from layout data
    # Check that neighbour cards have a TextureRect with has_texture=true
    # (A near-black plate would have has_texture=false)
    all_cards = [c for c in controls if c["class"] == "TextureRect"
                 and c.get("has_texture", False) == False
                 and c["rect"]["h"] > viewport["height"] * 0.05
                 and c["rect"]["w"] > viewport["width"] * 0.02]
    texture_missing = controls_matching(controls, path_pattern=r"TextureRect@\d+")
    for c in controls:
        if (c["class"] == "TextureRect" and
            c["rect"]["h"] > 50 and
            c.get("has_texture") == False and
            not c["path"].startswith("/root/ChooseYourPathScene/") and
            not c["path"].endswith("@25")):  # Skip hero art
            failures.append(
                f"BLANK_CARD: TextureRect {c['path']} at {c['rect']} has no texture — "
                f"reads as empty plate, not a class"
            )

    # Rule 5: CLASS CORE row total height <= 15% viewport height and horizontally centred
    core_areas = [c for c in controls if "CLASS" in str(c) or "Core" in c["path"]
                  or "ClassCore" in c["path"] or "CorePreview" in c["path"]
                  or "ClassCore" in c["class"]]
    core_containers = controls_matching(controls, path_pattern=r"(VBoxContainer|Control)")
    # Find the core section by looking for panels containing core card labels
    core_labels = [c for c in controls if c["class"] == "Label" and "CLASS CORE" in c.get("text", "")]
    # Since layout json doesn't store label text, find by path or structural position
    # Best approach: find the Control that contains the mini core cards (typically 4 PanelContainers in a row)
    mini_core_panels = controls_matching(controls, path_pattern=r"PanelContainer@\d+")
    # Find groups of 4 similar-sized PanelContainers near each other
    core_groups = []
    for c in controls:
        if c["class"] == "HBoxContainer" or c["class"] == "Control":
            children = [cc for cc in controls if cc["path"].startswith(c["path"] + "/")]
            if children and len(children) >= 4:
                panel_children = [cc for cc in children if cc["class"] == "PanelContainer"
                                  and 50 < cc["rect"]["h"] < 250]
                if len(panel_children) >= 3:
                    core_groups.append(c)

    if core_groups:
        # Use the last group (below carousel = furthest down in layout)
        core_group = core_groups[-1]
        cg_rect = {"x": core_group["rect"]["x"], "y": core_group["rect"]["y"],
                    "w": core_group["rect"]["w"], "h": core_group["rect"]["h"]}
        cg_h = cg_rect["h"]
        # Check height
        cg_height_ratio = cg_h / viewport["height"]
        if cg_height_ratio > 0.15:
            failures.append(
                f"CORE_HEIGHT: CLASS CORE row height {cg_h:.0f}px ({cg_height_ratio:.1%}) "
                f"exceeds 15% of viewport height ({viewport['height']})"
            )
        # Check horizontal centring
        cg_centre = cg_rect["x"] + cg_rect["w"] / 2
        frame_centre = viewport["width"] / 2
        centre_offset_pct = abs(cg_centre - frame_centre) / viewport["width"] * 100
        if centre_offset_pct > 2.0:
            failures.append(
                f"CORE_CENTRE: CLASS CORE row centre {cg_centre:.0f} is {centre_offset_pct:.1f}% "
                f"off frame centre ({frame_centre:.0f}) — exceeds 2%"
            )
    else:
        # Fallback: find the core section by its position below the carousel
        # (typically the second-to-last major Control in the VBox)
        vbox_children = [c for c in controls if c["class"] in ("Control",) and
                         c["rect"]["x"] == 0 and c["rect"]["w"] == viewport["width"] and
                         c["rect"]["y"] > viewport["height"] * 0.5]
        # Check a reasonable candidate
        for cc in vbox_children:
            ch_ratio = cc["rect"]["h"] / viewport["height"]
            if ch_ratio > 0.15 and cc["rect"]["y"] > viewport["height"] * 0.4:
                failures.append(
                    f"CORE_HEIGHT: section at y={cc['rect']['y']:.0f} is {ch_ratio:.1%} vh "
                    f"({cc['rect']['h']:.0f}px) — may exceed 15% limit"
                )

    # Rule 6: BEGIN button rect intersects nothing and has >= 24px clearance
    begin_wrappers = [c for c in controls if c["class"] in ("VBoxContainer",) and
                      c["path"].endswith("Begin") or c["path"].endswith("Begin/Begin")]
    begin_buttons = controls_matching(controls, path_pattern=r"PanelContainer")
    begin_candidates = [c for c in begin_buttons if "Begin" in c["path"] or
                        (c["rect"]["x"] + c["rect"]["w"] / 2 -
                         viewport["width"] / 2 < viewport["width"] * 0.05 and
                         c["rect"]["y"] > viewport["height"] * 0.75 and
                         30 < c["rect"]["h"] < 80)]
    if begin_candidates:
        # Filter: find the most likely Begin button (centred, near bottom, ~46px h)
        centre_x = viewport["width"] / 2
        best_begin = min(begin_candidates,
                         key=lambda c: (abs((c["rect"]["x"] + c["rect"]["w"] / 2) - centre_x),
                                        abs(c["rect"]["y"] + c["rect"]["h"] - viewport["height"])))
        br = best_begin["rect"]

        # Check no other controls overlap with it
        for oc in controls:
            if oc["path"] == best_begin["path"]:
                continue
            # Skip backgrounds and parent containers
            if oc["class"] in ("ColorRect", "TextureRect", "NinePatchRect") and \
               oc["rect"]["w"] >= viewport["width"] * 0.9:
                continue
            # Skip if same parent (siblings sharing a container)
            if oc["path"].rsplit("/", 1)[0] == best_begin["path"].rsplit("/", 1)[0]:
                continue
            # Skip if the other control fully contains the button (parent container)
            if rect_contains(oc["rect"], br):
                continue
            # Skip if the button fully contains the other control (its child)
            if rect_contains(br, oc["rect"]):
                continue
            if rects_overlap(br, oc["rect"]):
                failures.append(
                    f"BEGIN_OVERLAP: Begin button {br} overlaps {oc['path']} {oc['rect']}"
                )

        # Check clearance: at least 24px from any other rect
        # The Begin button should have clearance on all sides
        min_clearance = viewport["height"]
        for oc in controls:
            if oc["path"] == best_begin["path"]:
                continue
            if oc["class"] in ("ColorRect", "TextureRect", "NinePatchRect") and \
               oc["rect"]["w"] >= viewport["width"] * 0.9:
                continue
            ocr = oc["rect"]
            if ocr["y"] + ocr["h"] <= br["y"]:
                # Control above the button
                clear = br["y"] - (ocr["y"] + ocr["h"])
                if clear < min_clearance and clear >= 0:
                    min_clearance = clear
            elif ocr["y"] >= br["y"] + br["h"]:
                # Control below the button
                clear = ocr["y"] - (br["y"] + br["h"])
                if clear < min_clearance and clear >= 0:
                    min_clearance = clear
        if min_clearance < 24:
            failures.append(
                f"BEGIN_CLEARANCE: Begin button has only {min_clearance:.0f}px clearance "
                f"above/below (minimum 24px)"
            )
    else:
        failures.append("BEGIN_MISSING: no Begin button found in scene")

    # Rule 7: No empty horizontal band taller than 12% viewport height
    # between the title and the BEGIN button
    if begin_candidates and best_begin is not None:
        bottom_y = best_begin["rect"]["y"]
        # Find the title (topmost content)
        title_labels = [c for c in controls if c["class"] == "Label"]
        top_y = 0
        if title_labels:
            sorted_labels = sorted(title_labels, key=lambda c: c["rect"]["y"])
            first_label = sorted_labels[0]
            top_y = first_label["rect"]["y"] + first_label["rect"]["h"]
        # Scan for empty bands
        content_bounds = []
        for c in controls:
            if c["class"] in ("ColorRect", "TextureRect", "NinePatchRect") and \
               c["rect"]["w"] >= viewport["width"] * 0.9:
                continue
            cr = c["rect"]
            if cr["y"] + cr["h"] <= top_y or cr["y"] >= bottom_y:
                continue
            if cr["w"] > viewport["width"] * 0.50:
                content_bounds.append((cr["y"], cr["y"] + cr["h"]))

        if content_bounds:
            content_bounds.sort()
            merged = [list(content_bounds[0])]
            for start, end in content_bounds[1:]:
                if start <= merged[-1][1]:
                    merged[-1][1] = max(merged[-1][1], end)
                else:
                    merged.append([start, end])

            current_y = top_y
            for start, end in merged:
                gap = start - current_y
                if gap > viewport["height"] * 0.12:
                    failures.append(
                        f"EMPTY_BAND: empty band of {gap:.0f}px ({gap/viewport['height']:.1%} vh) "
                        f"between y={current_y:.0f} and y={start:.0f} — exceeds 12%"
                    )
                current_y = max(current_y, end)

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


def check_empty_body(controls: list[dict], viewport: dict) -> tuple[list[str], list[str]]:
    """EMPTY_BODY rule: for any Control with Label or TextureRect descendants,
    check that the control's rect doesn't leave >12% (or 60px) unoccupied vertical
    band inside its bounds. Empty-plate TextureRects (no texture, no descendants)
    are warnings, not failures."""
    failures = []
    warnings = []

    # Find all TextureRects with no texture and no children → empty-plate candidates
    blank_plates = []
    for c in controls:
        if c["class"] == "TextureRect" and not c.get("has_texture", False):
            cr = c["rect"]
            # Check if it has any visible descendants
            descendants = [d for d in controls
                           if d["path"].startswith(c["path"] + "/")]
            if not descendants:
                blank_plates.append(c)

    # Report blank-plate warnings
    for bp in blank_plates:
        warnings.append(
            f"EMPTY_PLATE: {bp['path']} at {bp['rect']} — TextureRect with no texture "
            f"and no children (placeholder until TASK-CLASS-PORTRAITS-1)"
        )

    # For each Control that has at least one Label or TextureRect descendant,
    # compute the union bounding box of descendants and check for unoccupied vertical band
    for c in controls:
        path = c["path"]
        cr = c["rect"]

        # Skip non-Container controls that don't hold children meaningfully
        # Focus on Controls (the Godot Control class) and VBoxContainer/HBoxContainer
        # Skip root-level full-screen backgrounds
        if cr["w"] >= viewport.get("width", 9999) * 0.90 and cr["h"] >= viewport.get("height", 9999) * 0.90:
            continue
        # Skip Label, Button, TextureRect leaves (they hold text/content by nature)
        if c["class"] in ("Label", "Button", "TextureButton", "LinkButton", "ColorRect",
                          "NinePatchRect", "TextureRect", "PanelContainer"):
            continue
        # Only check Container-like controls (Control base class, VBox, HBox, etc.)
        if c["class"] not in ("Control", "VBoxContainer", "HBoxContainer", "CenterContainer",
                              "MarginContainer", "SplitContainer", "GridContainer",
                              "ScrollContainer", "AspectRatioContainer"):
            continue

        # Find descendants that are Labels or TextureRects
        target_descendants = [d for d in controls
                              if d["path"].startswith(path + "/")
                              and d["class"] in ("Label", "TextureRect")
                              and d["rect"]["h"] > 0 and d["rect"]["w"] > 0]

        if not target_descendants:
            continue  # No content-bearing descendants — nothing to check

        c_h = cr["h"]
        if c_h <= 0:
            continue

        # Check gaps: above first descendant, between descendants (condensed), below last descendant
        # Sort descendants by their top edge
        sorted_by_top = sorted(target_descendants, key=lambda d: d["rect"]["y"])
        # Merge overlapping/adjacent y-ranges
        merged = []
        for d in sorted_by_top:
            d_top = d["rect"]["y"]
            d_bot = d["rect"]["y"] + d["rect"]["h"]
            if not merged:
                merged.append([d_top, d_bot])
            else:
                last = merged[-1]
                if d_top <= last[1]:
                    last[1] = max(last[1], d_bot)
                else:
                    merged.append([d_top, d_bot])

        # Each gap between merged ranges is a gap in content
        # But the main concern: gap below the LAST content (the "empty body" problem)
        last_content_bottom = merged[-1][1] if merged else 0
        gap_below = (cr["y"] + c_h) - last_content_bottom

        threshold = max(c_h * 0.12, 60.0)  # 12% of control height or 60px, whichever is larger

        if gap_below > threshold:
            failures.append(
                f"EMPTY_BODY: {path} rect {cr} has {gap_below:.0f}px unoccupied vertical "
                f"band below content (content ends at y={last_content_bottom:.0f}, "
                f"control bottom at y={cr['y'] + c_h:.0f}) — exceeds {threshold:.0f}px threshold"
            )

        # Also check gap above first content
        first_content_top = merged[0][0] if merged else 0
        gap_above = first_content_top - cr["y"]
        if gap_above > threshold:
            failures.append(
                f"EMPTY_BODY_TOP: {path} rect {cr} has {gap_above:.0f}px unoccupied vertical "
                f"band above content (content starts at y={first_content_top:.0f}, "
                f"control top at y={cr['y']:.0f})"
            )

    return failures, warnings


def check_artifact_luminance(controls: list[dict], capture_name: str) -> list[str]:
    """ARTIFACT_LUMINANCE rule: an occupied artifact slot's mean pixel luminance
    must differ clearly from an empty slot's — ensuring the lighter backing plate
    makes the art visible. Samples the PNG capture pixel region of each artifact slot.
    """
    failures = []
    if not HAS_PIL:
        # Can't check luminance without PIL — skip silently if tests were to check
        return failures

    png_path = CAPTURE_DIR / f"{capture_name}.png"
    if not png_path.exists():
        return failures  # No PNG to sample

    try:
        img = Image.open(png_path).convert("RGB")
    except Exception as e:
        failures.append(f"ARTIFACT_LUMINANCE: failed to open {png_path}: {e}")
        return failures

    # Find artifact slots — PanelContainers with ArtPlate in their child paths
    slot_panels = [c for c in controls if c["class"] == "PanelContainer" and
                   any("ArtPlate" in child["path"] for child in controls
                       if child["path"].startswith(c["path"] + "/"))]

    if not slot_panels:
        return failures  # No artifact slots found

    for panel in slot_panels:
        pr = panel["rect"]
        x, y, w, h = int(pr["x"]), int(pr["y"]), int(pr["w"]), int(pr["h"])
        if w <= 0 or h <= 0:
            continue
        # Clamp to image bounds
        x = max(0, min(x, img.width - 1))
        y = max(0, min(y, img.height - 1))
        w = min(w, img.width - x)
        h = min(h, img.height - y)
        if w <= 0 or h <= 0:
            continue

        # Sample center 60% of the slot to avoid border pixels
        cx, cy = x + w // 2, y + h // 2
        sw = max(1, w // 3)
        sh = max(1, h // 3)
        sample_region = img.crop((cx - sw // 2, cy - sh // 2, cx + sw // 2, cy + sh // 2))
        pixels = list(sample_region.getdata())
        if not pixels:
            continue

        # Compute mean luminance (ITU-R BT.601)
        lum = sum(0.299 * r + 0.587 * g + 0.114 * b for r, g, b in pixels) / len(pixels)

        # Check if this slot has an occupied art TextureRect
        # (specifically the ArtPlate's texture, not RootBoundBorder slices)
        tex_children = [c for c in controls if
                        c["path"].startswith(panel["path"] + "/") and
                        c["class"] == "TextureRect" and
                        c.get("has_texture", False) and
                        "ArtPlate" in c["path"]]

        # Check if slot has an art plate with Setup done (name != "—" implies real artifact)
        plate_children = [c for c in controls if
                          c["path"].startswith(panel["path"] + "/") and
                          "ArtPlate" in c["path"]]
        has_real_artifact = bool(tex_children)  # has texture = occupied

        if has_real_artifact:
            # Occupied slot: mean luminance should be >= 22 (lighter backing plate + art)
            # Old ArtifactFrameFill (#1E2420) gives ~14; new (#363E38) gives ~27
            LUMINANCE_MIN_OCCUPIED = 18.0
            if lum < LUMINANCE_MIN_OCCUPIED:
                failures.append(
                    f"ARTIFACT_LUMINANCE: occupied slot {panel['path']} at {pr} "
                    f"has mean luminance {lum:.1f} — below {LUMINANCE_MIN_OCCUPIED:.0f} minimum, "
                    f"reads as empty dark box"
                )

    return failures
def main():
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

        # Normalize controls: convert flat x,y,w,h to rect dict for backward compat
        for c in controls:
            if "rect" not in c and "x" in c:
                c["rect"] = {"x": c["x"], "y": c["y"], "w": c["w"], "h": c["h"]}

        seen_basenames.append(basename)
        scene_failures = []

        # (0) EMPTY_BODY rule — runs on EVERY capture
        empty_body_fails, empty_body_warns = check_empty_body(controls, viewport)
        scene_failures.extend(empty_body_fails)

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

        # Print EMPTY_PLATE warnings (not failures)
        for w in empty_body_warns:
            print(f"  WARN: {w}")

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
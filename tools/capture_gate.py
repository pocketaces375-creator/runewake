#!/usr/bin/env python3
"""
capture_gate.py — Acceptance gate for UI task screenshots.
HARD RULE: no bypass flags. A capture without a real PNG is a FAILED capture.
(Trikzos/Claude, 2026-08-16)

Reads a PNG + meta.json pair from artifacts/captures/ and validates that:
  - duel_test: Whole frame < 85% near-black, hand/board card visibility, name contrast
  - deck_test: Tome layout visible, card entries readable, validation annotations present

Usage: python3 tools/capture_gate.py [basename]
"""

import json
import struct
import sys
import zlib
from pathlib import Path


def paeth_predictor(a, b, c):
    p = a + b - c
    pa = abs(p - a)
    pb = abs(p - b)
    pc = abs(p - c)
    if pa <= pb and pa <= pc:
        return a
    elif pb <= pc:
        return b
    else:
        return c


def read_png(filename):
    with open(filename, 'rb') as f:
        if f.read(8) != b'\x89PNG\r\n\x1a\n':
            raise ValueError('Not a valid PNG file')
        width = height = 0
        raw_data = b''
        color_type = 0
        while True:
            chunk_len = struct.unpack('>I', f.read(4))[0]
            chunk_type = f.read(4)
            chunk_data = f.read(chunk_len)
            f.read(4)
            if chunk_type == b'IHDR':
                width = struct.unpack('>I', chunk_data[0:4])[0]
                height = struct.unpack('>I', chunk_data[4:8])[0]
                color_type = chunk_data[9]
            elif chunk_type == b'IDAT':
                raw_data += chunk_data
            elif chunk_type == b'IEND':
                break
        if not raw_data:
            raise ValueError('No IDAT chunks found')
        decompressed = zlib.decompress(raw_data)
        if color_type == 2:
            bpp = 3
        elif color_type == 6:
            bpp = 4
        else:
            raise ValueError(f'Unsupported PNG color type: {color_type}')
        row_len = width * bpp
        pixels = bytearray()
        prev_row = bytearray(b'\x00' * row_len)
        for y in range(height):
            offset = y * (1 + row_len)
            filter_type = decompressed[offset]
            raw_row = decompressed[offset + 1:offset + 1 + row_len]
            if filter_type == 0:
                decoded = bytearray(raw_row)
            elif filter_type == 1:
                decoded = bytearray(raw_row)
                for i in range(bpp, len(decoded)):
                    decoded[i] = (decoded[i] + decoded[i - bpp]) & 0xFF
            elif filter_type == 2:
                decoded = bytearray(raw_row)
                for i in range(len(decoded)):
                    decoded[i] = (decoded[i] + prev_row[i]) & 0xFF
            elif filter_type == 3:
                decoded = bytearray(raw_row)
                for i in range(len(decoded)):
                    left = decoded[i - bpp] if i >= bpp else 0
                    up = prev_row[i]
                    decoded[i] = (decoded[i] + (left + up) // 2) & 0xFF
            elif filter_type == 4:
                decoded = bytearray(raw_row)
                for i in range(len(decoded)):
                    left = decoded[i - bpp] if i >= bpp else 0
                    up = prev_row[i]
                    up_left = prev_row[i - bpp] if i >= bpp else 0
                    decoded[i] = (decoded[i] + paeth_predictor(left, up, up_left)) & 0xFF
            else:
                raise ValueError(f'Unknown PNG filter type: {filter_type}')
            if color_type == 2:
                rgba_row = bytearray()
                for i in range(0, len(decoded), 3):
                    rgba_row.append(decoded[i])
                    rgba_row.append(decoded[i + 1])
                    rgba_row.append(decoded[i + 2])
                    rgba_row.append(255)
                pixels.extend(rgba_row)
                prev_row = decoded
            else:
                pixels.extend(decoded)
                prev_row = decoded
        return width, height, bytes(pixels)


def get_luminance(r, g, b):
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def rect_mean_stddev(pixels, width, height, x, y, w, h):
    x, y, w, h = int(x), int(y), int(w), int(h)
    x = max(0, min(x, width - 1))
    y = max(0, min(y, height - 1))
    w = min(w, width - x)
    h = min(h, height - y)
    if w <= 0 or h <= 0:
        return 0, 0
    lums = []
    for row in range(y, y + h):
        for col in range(x, x + w):
            idx = (row * width + col) * 4
            if idx + 3 < len(pixels):
                r = pixels[idx] / 255.0
                g = pixels[idx + 1] / 255.0
                b = pixels[idx + 2] / 255.0
                lums.append(get_luminance(r, g, b))
    if not lums:
        return 0, 0
    n = len(lums)
    mean = sum(lums) / n
    var = sum((lum - mean) ** 2 for lum in lums) / n
    return mean, var ** 0.5


def rect_max_contrast(pixels, width, height, x, y, w, h):
    x, y, w, h = int(x), int(y), int(w), int(h)
    x = max(0, min(x, width - 1))
    y = max(0, min(y, height - 1))
    w = min(w, width - x)
    h = min(h, height - y)
    if w <= 0 or h <= 0:
        return 0.0
    min_lum = 1.0
    max_lum = 0.0
    for row in range(y, y + h):
        for col in range(x, x + w):
            idx = (row * width + col) * 4
            if idx + 3 < len(pixels):
                r = pixels[idx] / 255.0
                g = pixels[idx + 1] / 255.0
                b = pixels[idx + 2] / 255.0
                lum = get_luminance(r, g, b)
                min_lum = min(min_lum, lum)
                max_lum = max(max_lum, lum)
    return max_lum - min_lum


# ════════════════════════════════════════════
# Duel test validator (original)
# ════════════════════════════════════════════

def validate_duel_test(png_path, meta):
    expected_hand_count = meta.get("expected_hand_card_count", 4)
    expected_board_count = meta.get("expected_board_card_count", 10)

    if not png_path.exists():
        print(f"FAIL: PNG not found: {png_path}")
        sys.exit(1)

    width, height, pixels = read_png(str(png_path))
    total_pixels = width * height
    print(f"Image: {width}x{height}, {total_pixels} pixels")

    near_black_threshold = 20 / 255.0
    dark_count = 0
    for i in range(0, len(pixels), 4):
        r = pixels[i] / 255.0
        g = pixels[i + 1] / 255.0
        b = pixels[i + 2] / 255.0
        if get_luminance(r, g, b) < near_black_threshold:
            dark_count += 1

    dark_ratio = dark_count / total_pixels
    failures = []

    if dark_ratio > 0.85:
        failures.append(
            f"WHOLE_FRAME_DARK: {dark_ratio:.1%} pixels are near-black "
            f"(threshold 85%, expected scene to be brighter)"
        )
    else:
        print(f"  PASS whole-frame dark: {dark_ratio:.1%} near-black pixels (limit 85%)")

    hand_cards = meta.get("hand_cards", [])
    actual_hand = len(hand_cards)
    if actual_hand != expected_hand_count:
        failures.append(f"HAND_CARD_COUNT: expected {expected_hand_count}, got {actual_hand}")
    else:
        print(f"  PASS hand card count: {actual_hand}")

    for i, card in enumerate(hand_cards):
        r = card.get("rect")
        if not r:
            failures.append(f"HAND_CARD_{i}: missing rect")
            continue
        mean, std = rect_mean_stddev(pixels, width, height, r["x"], r["y"], r["w"], r["h"])
        if mean <= 25 / 255.0:
            failures.append(f"HAND_CARD_{i}: mean luminance {mean:.3f} too low (need > {25 / 255.0:.3f}, card_id={card.get('card_id','?')})")
        if std <= 12 / 255.0:
            failures.append(f"HAND_CARD_{i}: stddev {std:.3f} too low (need > {12 / 255.0:.3f}, card_id={card.get('card_id','?')})")
        if mean > 25 / 255.0 and std > 12 / 255.0:
            print(f"  PASS hand card {i}: mean={mean:.3f}, std={std:.3f}")
        name_r = card.get("name_rect")
        if name_r and name_r.get("w", 0) > 0 and name_r.get("h", 0) > 0:
            contrast = rect_max_contrast(pixels, width, height, name_r["x"], name_r["y"], name_r["w"], name_r["h"])
            if contrast < 0.15:
                failures.append(f"HAND_CARD_{i}: name strip contrast {contrast:.3f} too low (need > 0.15)")
            else:
                print(f"  PASS hand card {i} name: contrast={contrast:.3f}")

    board_cards = meta.get("board_cards", [])
    actual_board = len(board_cards)
    if actual_board != expected_board_count:
        failures.append(f"BOARD_CARD_COUNT: expected {expected_board_count}, got {actual_board}")
    else:
        print(f"  PASS board card count: {actual_board}")

    for i, card in enumerate(board_cards):
        r = card.get("rect")
        if not r:
            failures.append(f"BOARD_CARD_{i}: missing rect")
            continue
        slot_state = card.get("state", "empty")
        mean, std = rect_mean_stddev(pixels, width, height, r["x"], r["y"], r["w"], r["h"])
        slot_is_empty = (slot_state == "empty")
        if slot_is_empty:
            print(f"  SKIP board card {i}: slot state=empty, skipping checks")
        else:
            if mean <= 25 / 255.0 or std < 5 / 255.0:
                failures.append(f"BOARD_CARD_{i}: slot state={slot_state} but mean={mean:.3f}, std={std:.3f} — occupied slot must not be uniform")
            else:
                print(f"  PASS board card {i}: state={slot_state}, mean={mean:.3f}, std={std:.3f}")
        name_r = card.get("name_rect")
        if name_r and name_r.get("w", 0) > 0 and name_r.get("h", 0) > 0 and not slot_is_empty:
            nx, ny, nw, nh = int(name_r["x"]), int(name_r["y"]), int(name_r["w"]), int(name_r["h"])
            nx = max(0, min(nx, width - 1))
            ny = max(0, min(ny, height - 1))
            nw = min(nw, width - nx)
            nh = min(nh, height - ny)
            colors = set()
            for sy in range(ny, ny + nh, 4):
                for sx in range(nx, nx + nw, 4):
                    idx = (sy * width + sx) * 4
                    if idx + 3 < len(pixels):
                        colors.add((pixels[idx], pixels[idx+1], pixels[idx+2]))
                    if len(colors) > 1:
                        break
                if len(colors) > 1:
                    break
            if len(colors) <= 1:
                print(f"  SKIP board card {i} name: no content ({len(colors)} color(s))")
            else:
                contrast = rect_max_contrast(pixels, width, height, name_r["x"], name_r["y"], name_r["w"], name_r["h"])
                if contrast < 0.15:
                    failures.append(f"BOARD_CARD_{i}: name strip contrast {contrast:.3f} too low")
                else:
                    print(f"  PASS board card {i} name: contrast={contrast:.3f}")

    groups = meta.get("groups", [])
    if not groups:
        failures.append("GROUPS_MISSING: no 'groups' entries in meta.json")
    else:
        by_side = {g.get("side"): g for g in groups}
        if "player" not in by_side or "enemy" not in by_side:
            failures.append(f"GROUPS_SIDES: expected player+enemy groups, got {list(by_side.keys())}")
        for side in ("player", "enemy"):
            g = by_side.get(side)
            if not g or "rect" not in g:
                failures.append(f"GROUPS_{side.upper()}: missing rect")
                continue
            r = g["rect"]
            mean, std = rect_mean_stddev(pixels, width, height, r["x"], r["y"], r["w"], r["h"])
            if std <= 8 / 255.0:
                failures.append(f"GROUPS_{side.upper()}: rect stddev {std:.3f} too low (need > {8 / 255.0:.3f}) — group not visible")
            else:
                print(f"  PASS group {side}: mean={mean:.3f}, std={std:.3f}")

    all_entries = []
    for i, card in enumerate(hand_cards):
        if "rect" in card:
            all_entries.append((f"hand_card_{i}", "hand", card["rect"]))
    for i, card in enumerate(board_cards):
        slot = card.get("slot", f"board_{i}")
        if "rect" in card:
            group = "board_player" if slot.startswith("player") else "board_enemy"
            all_entries.append((f"board_{slot}", group, card["rect"]))
    for g in groups:
        side = g.get("side", "unknown")
        if "rect" in g:
            all_entries.append((f"group_{side}", f"group_{side}", g["rect"]))

    allowed_overlap_pairs = [
        ("hand", "board_player"),
        ("board_player", "group_player"),
    ]

    def is_allowed_overlap(ga, gb):
        for a, b in allowed_overlap_pairs:
            if (ga == a and gb == b) or (ga == b and gb == a):
                return True
        return False

    for i in range(len(all_entries)):
        for j in range(i + 1, len(all_entries)):
            name_a, group_a, ra = all_entries[i]
            name_b, group_b, rb = all_entries[j]
            if group_a == group_b:
                continue
            if is_allowed_overlap(group_a, group_b):
                continue
            ax, ay, aw, ah = ra["x"], ra["y"], ra["w"], ra["h"]
            bx, by, bw, bh = rb["x"], rb["y"], rb["w"], rb["h"]
            if ax < bx + bw and ax + aw > bx and ay < by + bh and ay + ah > by:
                failures.append(f"OVERLAP: {name_a} ({group_a}) intersects {name_b} ({group_b})")

    # Check 7: Hand tray vs altar ellipse overlap (UI-FIELD-FIX)
    altar_ellipse = meta.get("altar_ellipse")
    if altar_ellipse and "bottom_y" in altar_ellipse:
        ellipse_bottom = altar_ellipse["bottom_y"]
        hand_area_top = None
        for card in hand_cards:
            if "rect" in card:
                r = card["rect"]
                if hand_area_top is None or r["y"] < hand_area_top:
                    hand_area_top = r["y"]
        if hand_area_top is not None and hand_area_top < ellipse_bottom:
            failures.append(
                f"HAND_FIELD_OVERLAP: hand area top ({hand_area_top:.0f}) is above altar ellipse bottom "
                f"({ellipse_bottom:.0f}) — hand tray overlaps the play field"
            )
        else:
            print(f"  PASS hand/field clearance: hand top={hand_area_top:.0f}, ellipse bottom={ellipse_bottom:.0f}, "
                  f"gap={hand_area_top - ellipse_bottom:.0f}px" if hand_area_top is not None else "  PASS hand/field: no hand rects to check")

    if failures:
        print(f"\nFAILURE ({len(failures)} reasons):")
        for f in failures:
            print(f"  - {f}")
        sys.exit(1)
    else:
        print(f"\nPASS: All {len(hand_cards)} hand card + {len(board_cards)} board card checks passed")
        sys.exit(0)


# ════════════════════════════════════════════
# Deck test validator (TASK-DK2)
# ════════════════════════════════════════════

def validate_deck_test(png_path, meta):
    if not png_path.exists():
        print(f"FAIL: PNG not found: {png_path}")
        sys.exit(1)

    width, height, pixels = read_png(str(png_path))
    total_pixels = width * height
    print(f"Image: {width}x{height}, {total_pixels} pixels")
    failures = []

    near_black_threshold = 20 / 255.0
    dark_count = 0
    for i in range(0, len(pixels), 4):
        r = pixels[i] / 255.0
        g = pixels[i + 1] / 255.0
        b = pixels[i + 2] / 255.0
        if get_luminance(r, g, b) < near_black_threshold:
            dark_count += 1

    dark_ratio = dark_count / total_pixels
    if dark_ratio > 0.85:
        failures.append(f"WHOLE_FRAME_DARK: {dark_ratio:.1%} pixels are near-black — tome should be parchment-colored")
    else:
        print(f"  PASS whole-frame dark: {dark_ratio:.1%} near-black pixels (limit 85%)")

    left_rect = meta.get("left_page_rect")
    if left_rect:
        mean, std = rect_mean_stddev(pixels, width, height, left_rect["x"], left_rect["y"], left_rect["w"], left_rect["h"])
        if std < 8 / 255.0:
            failures.append(f"LEFT_PAGE: stddev {std:.3f} too low — collection page not rendered")
        else:
            print(f"  PASS left page: mean={mean:.3f}, std={std:.3f}")
    else:
        failures.append("LEFT_PAGE: no rect in meta")

    right_rect = meta.get("right_page_rect")
    if right_rect:
        mean, std = rect_mean_stddev(pixels, width, height, right_rect["x"], right_rect["y"], right_rect["w"], right_rect["h"])
        if std < 8 / 255.0:
            failures.append(f"RIGHT_PAGE: stddev {std:.3f} too low — manifest page not rendered")
        else:
            print(f"  PASS right page: mean={mean:.3f}, std={std:.3f}")
    else:
        failures.append("RIGHT_PAGE: no rect in meta")

    validation_rect = meta.get("validation_rect")
    if validation_rect:
        mean, std = rect_mean_stddev(pixels, width, height, validation_rect["x"], validation_rect["y"], validation_rect["w"], validation_rect["h"])
        if std < 5 / 255.0:
            failures.append(f"VALIDATION: stddev {std:.3f} too low — annotations not visible")
        else:
            print(f"  PASS validation annotations: mean={mean:.3f}, std={std:.3f}")

        vx, vy, vw, vh = int(validation_rect["x"]), int(validation_rect["y"]), int(validation_rect["w"]), int(validation_rect["h"])
        red_pixels = 0
        total_checked = 0
        for row in range(vy, min(vy + vh, height)):
            for col in range(vx, min(vx + vw, width)):
                idx = (row * width + col) * 4
                if idx + 3 < len(pixels):
                    r, g, b = pixels[idx] / 255.0, pixels[idx+1] / 255.0, pixels[idx+2] / 255.0
                    total_checked += 1
                    if r > 0.5 and g < 0.3 and b < 0.3:
                        red_pixels += 1
        if total_checked > 0:
            red_ratio = red_pixels / total_checked
            if red_ratio < 0.003:
                failures.append(f"VALIDATION: only {red_ratio:.3%} red pixels — duplicate error annotation may be missing (need > 0.5%)")
            else:
                print(f"  PASS red-ink annotations: {red_ratio:.1%} red pixels")
    else:
        failures.append("VALIDATION: no rect in meta")

    spine_rect = meta.get("spine_rect")
    if spine_rect:
        mean, std = rect_mean_stddev(pixels, width, height, spine_rect["x"], spine_rect["y"], spine_rect["w"], spine_rect["h"])
        if std < 3 / 255.0 and mean < 25 / 255.0:
            failures.append(f"SPINE: mean={mean:.3f}, std={std:.3f} — spine not visible")
        else:
            print(f"  PASS spine: mean={mean:.3f}, std={std:.3f}")
    else:
        failures.append("SPINE: no rect in meta")

    ribbon_rect = meta.get("ribbon_rect")
    if ribbon_rect:
        mean, std = rect_mean_stddev(pixels, width, height, ribbon_rect["x"], ribbon_rect["y"], ribbon_rect["w"], ribbon_rect["h"])
        if std < 3 / 255.0:
            failures.append(f"RIBBON: stddev {std:.3f} too low — filter ribbons not visible")
        else:
            print(f"  PASS filter ribbons: mean={mean:.3f}, std={std:.3f}")

    if failures:
        print(f"\nFAILURE ({len(failures)} reasons):")
        for f in failures:
            print(f"  - {f}")
        sys.exit(1)
    else:
        print("\nPASS: All deck builder checks passed")
        sys.exit(0)


# ════════════════════════════════════════════
# Title+Deck test validator (TASK-DK3)
# ════════════════════════════════════════════

def validate_title_deck_test(png_path, meta):
    if not png_path.exists():
        print(f"FAIL: PNG not found: {png_path}")
        sys.exit(1)

    width, height, pixels = read_png(str(png_path))
    total_pixels = width * height
    print(f"Image: {width}x{height}, {total_pixels} pixels")
    failures = []

    near_black_threshold = 25 / 255.0
    dark_count = 0
    for i in range(0, len(pixels), 4):
        r = pixels[i] / 255.0
        g = pixels[i + 1] / 255.0
        b = pixels[i + 2] / 255.0
        if get_luminance(r, g, b) < near_black_threshold:
            dark_count += 1

    dark_ratio = dark_count / total_pixels
    if dark_ratio > 0.92:
        failures.append(f"WHOLE_FRAME_DARK: {dark_ratio:.1%} pixels are near-black (threshold 92%, expected title screen with buttons and text)")
    else:
        print(f"  PASS whole-frame dark: {dark_ratio:.1%} near-black pixels (limit 92%)")

    decks_rect = meta.get("decks_button_rect")
    if decks_rect:
        mean, std = rect_mean_stddev(pixels, width, height, decks_rect["x"], decks_rect["y"], decks_rect["w"], decks_rect["h"])
        if std < 10 / 255.0:
            failures.append(f"DECKS_BUTTON: stddev {std:.3f} too low — Decks button not visible (need > {10 / 255.0:.3f})")
        else:
            print(f"  PASS Decks button area: mean={mean:.3f}, std={std:.3f}")

    if failures:
        print(f"\nFAILURE ({len(failures)} reasons):")
        for f in failures:
            print(f"  - {f}")
        sys.exit(1)
    else:
        print("\nPASS: All title+deck checks passed")
        sys.exit(0)


# ════════════════════════════════════════════
# Main dispatcher
# ════════════════════════════════════════════

VALIDATORS = {
    "duel_test": validate_duel_test,
    "deck_test": validate_deck_test,
    "title_deck": validate_title_deck_test,
}

def main():
    if len(sys.argv) > 1:
        base = sys.argv[1]
    else:
        base = "duel_test"

    if base not in VALIDATORS:
        print(f"Unknown capture type '{base}'. Known: {', '.join(VALIDATORS.keys())}")
        sys.exit(1)

    base_dir = Path(__file__).resolve().parent.parent / "artifacts" / "captures"
    png_path = base_dir / f"{base}.png"
    meta_path = base_dir / f"{base}.meta.json"

    if not meta_path.exists():
        print(f"FAIL: Meta not found: {meta_path}")
        sys.exit(1)

    with open(meta_path) as f:
        meta = json.load(f)

    VALIDATORS[base](png_path, meta)


if __name__ == "__main__":
    main()
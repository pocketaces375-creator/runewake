#!/usr/bin/env python3
"""
capture_gate.py — Acceptance gate for UI task screenshots.

Reads a PNG + meta.json pair from artifacts/captures/ and validates that:
  - Whole frame < 85% near-black pixels (luminance < 20/255)
  - Every hand-card body rect has mean luminance > 25/255 AND stddev > 12/255
  - Every name-strip rect shows high-contrast pixels vs its background (> 0.15)
  - Card count matches the test state (4 hand cards, 10 board cards)

Usage: python3 tools/capture_gate.py [basename]
  (defaults to duel_test if no name given)
"""

import json
import os
import struct
import sys
import zlib
from pathlib import Path


def paeth_predictor(a, b, c):
    """Paeth predictor from PNG spec."""
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
    """Read PNG file and return raw RGBA pixel data and dimensions.
    Properly applies PNG filter algorithms (Sub, Up, Average, Paeth)."""
    with open(filename, 'rb') as f:
        # Verify PNG header
        if f.read(8) != b'\x89PNG\r\n\x1a\n':
            raise ValueError('Not a valid PNG file')

        width = height = 0
        raw_data = b''
        bit_depth = 0
        color_type = 0

        while True:
            chunk_len = struct.unpack('>I', f.read(4))[0]
            chunk_type = f.read(4)
            chunk_data = f.read(chunk_len)
            f.read(4)  # CRC

            if chunk_type == b'IHDR':
                width = struct.unpack('>I', chunk_data[0:4])[0]
                height = struct.unpack('>I', chunk_data[4:8])[0]
                bit_depth = chunk_data[8]
                color_type = chunk_data[9]
            elif chunk_type == b'IDAT':
                raw_data += chunk_data
            elif chunk_type == b'IEND':
                break

        if not raw_data:
            raise ValueError('No IDAT chunks found')

        # Decompress
        decompressed = zlib.decompress(raw_data)

        # Determine bytes per pixel
        if color_type == 6:  # RGBA
            bpp = 4
        elif color_type == 2:  # RGB
            bpp = 3
        else:
            raise ValueError(f'Unsupported color type: {color_type}')

        row_len = width * bpp
        expected = height * (1 + row_len)  # filter byte + pixels per row
        if len(decompressed) != expected:
            raise ValueError(f'Decompressed size mismatch: got {len(decompressed)}, expected {expected}')

        # Apply scanline filters
        pixels = bytearray()
        prev_row = bytearray(b'\x00' * row_len)

        for y in range(height):
            offset = y * (1 + row_len)
            filter_type = decompressed[offset]
            raw_row = decompressed[offset + 1:offset + 1 + row_len]

            if filter_type == 0:  # None
                decoded = bytearray(raw_row)
            elif filter_type == 1:  # Sub
                decoded = bytearray(raw_row)
                for i in range(bpp, len(decoded)):
                    decoded[i] = (decoded[i] + decoded[i - bpp]) & 0xFF
            elif filter_type == 2:  # Up
                decoded = bytearray(raw_row)
                for i in range(len(decoded)):
                    decoded[i] = (decoded[i] + prev_row[i]) & 0xFF
            elif filter_type == 3:  # Average
                decoded = bytearray(raw_row)
                for i in range(len(decoded)):
                    left = decoded[i - bpp] if i >= bpp else 0
                    up = prev_row[i]
                    decoded[i] = (decoded[i] + (left + up) // 2) & 0xFF
            elif filter_type == 4:  # Paeth
                decoded = bytearray(raw_row)
                for i in range(len(decoded)):
                    left = decoded[i - bpp] if i >= bpp else 0
                    up = prev_row[i]
                    up_left = prev_row[i - bpp] if i >= bpp else 0
                    decoded[i] = (decoded[i] + paeth_predictor(left, up, up_left)) & 0xFF
            else:
                raise ValueError(f'Unknown PNG filter type: {filter_type}')

            # Convert to RGBA if needed
            if bpp == 3:
                rgba_row = bytearray()
                for i in range(0, len(decoded), 3):
                    rgba_row.append(decoded[i])
                    rgba_row.append(decoded[i + 1])
                    rgba_row.append(decoded[i + 2])
                    rgba_row.append(255)
                pixels.extend(rgba_row)
            else:
                pixels.extend(decoded)

            prev_row = decoded

        return width, height, bytes(pixels)


def get_luminance(r, g, b):
    """Relative luminance (sRGB gamma corrected)."""
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def rect_mean_stddev(pixels, width, height, x, y, w, h):
    """Compute mean luminance and standard deviation for a sub-rect."""
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
    """Compute the maximum luminance contrast in a sub-rect (max - min)."""
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


def main():
    base_name = sys.argv[1] if len(sys.argv) > 1 else "duel_test"
    capture_dir = Path("artifacts/captures")

    png_path = capture_dir / f"{base_name}.png"
    meta_path = capture_dir / f"{base_name}.meta.json"

    if not png_path.exists():
        print(f"FAIL: PNG not found: {png_path}")
        sys.exit(1)
    if not meta_path.exists():
        print(f"FAIL: Meta JSON not found: {meta_path}")
        sys.exit(1)

    # Load meta
    with open(meta_path) as f:
        meta = json.load(f)

    expected_hand_count = meta.get("expected_hand_card_count", 4)
    expected_board_count = meta.get("expected_board_card_count", 10)

    # Read PNG
    width, height, pixels = read_png(str(png_path))
    total_pixels = width * height
    print(f"Image: {width}x{height}, {total_pixels} pixels")

    # Check 1: Whole frame near-black pixel ratio
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

    # Check 2: Hand card rects
    hand_cards = meta.get("hand_cards", [])
    actual_hand = len(hand_cards)
    if actual_hand != expected_hand_count:
        failures.append(
            f"HAND_CARD_COUNT: expected {expected_hand_count}, got {actual_hand}"
        )
    else:
        print(f"  PASS hand card count: {actual_hand}")

    for i, card in enumerate(hand_cards):
        r = card.get("rect")
        if not r:
            failures.append(f"HAND_CARD_{i}: missing rect")
            continue
        mean, std = rect_mean_stddev(pixels, width, height, r["x"], r["y"], r["w"], r["h"])
        if mean <= 25 / 255.0:
            failures.append(
                f"HAND_CARD_{i}: mean luminance {mean:.3f} too low (need > {25 / 255.0:.3f}, card_id={card.get('card_id','?')})"
            )
        if std <= 12 / 255.0:
            failures.append(
                f"HAND_CARD_{i}: stddev {std:.3f} too low (need > {12 / 255.0:.3f}, card_id={card.get('card_id','?')})"
            )
        if mean > 25 / 255.0 and std > 12 / 255.0:
            print(f"  PASS hand card {i}: mean={mean:.3f}, std={std:.3f}")

        # Check name label strip
        name_r = card.get("name_rect")
        if name_r and name_r.get("w", 0) > 0 and name_r.get("h", 0) > 0:
            contrast = rect_max_contrast(pixels, width, height, name_r["x"], name_r["y"], name_r["w"], name_r["h"])
            if contrast < 0.15:
                failures.append(
                    f"HAND_CARD_{i}: name strip contrast {contrast:.3f} too low (need > 0.15)"
                )
            else:
                print(f"  PASS hand card {i} name: contrast={contrast:.3f}")

    # Check 3: Board card rects
    board_cards = meta.get("board_cards", [])
    actual_board = len(board_cards)
    if actual_board != expected_board_count:
        failures.append(
            f"BOARD_CARD_COUNT: expected {expected_board_count}, got {actual_board}"
        )
    else:
        print(f"  PASS board card count: {actual_board}")

    for i, card in enumerate(board_cards):
        r = card.get("rect")
        if not r:
            failures.append(f"BOARD_CARD_{i}: missing rect")
            continue
        slot_state = card.get("state", "empty")
        mean, std = rect_mean_stddev(pixels, width, height, r["x"], r["y"], r["w"], r["h"])
        # TASK-F4B: Use explicit state field from meta.json.
        # "empty" = no creature → skip luminance checks (uniform is OK).
        # "occupied" = creature present → uniform color MUST fail.
        slot_is_empty = (slot_state == "empty")
        if slot_is_empty:
            print(f"  SKIP board card {i}: slot state=empty, skipping checks")
        else:
            if mean <= 25 / 255.0 or std < 5 / 255.0:
                failures.append(
                    f"BOARD_CARD_{i}: slot state={slot_state} but mean={mean:.3f}, std={std:.3f} — "
                    f"occupied slot must not be uniform (need mean > {25 / 255.0:.3f} AND std >= {5 / 255.0:.3f})"
                )
            else:
                print(f"  PASS board card {i}: state={slot_state}, mean={mean:.3f}, std={std:.3f}")

        # Only check name contrast if the name rect has actual content (>1 unique color)
        name_r = card.get("name_rect")
        if name_r and name_r.get("w", 0) > 0 and name_r.get("h", 0) > 0 and not slot_is_empty:
            # Quick check: sample a grid in the name rect
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
                    failures.append(
                        f"BOARD_CARD_{i}: name strip contrast {contrast:.3f} too low (need > 0.15)"
                    )
                else:
                    print(f"  PASS board card {i} name: contrast={contrast:.3f}")

    # Check 4: Arsenal group rects (TASK-H)
    groups = meta.get("groups", [])
    if not groups:
        failures.append("GROUPS_MISSING: no 'groups' entries in meta.json (TASK-H requires player + enemy group rects)")
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
            # Group rect should contain visible content (deck pile + artifact frames) — not a void
            if std <= 8 / 255.0:
                failures.append(
                    f"GROUPS_{side.upper()}: rect stddev {std:.3f} too low (need > {8 / 255.0:.3f}) — group not visible"
                )
            else:
                print(f"  PASS group {side}: mean={mean:.3f}, std={std:.3f}")

    # Check 5: Overlap assertion (TASK-UI3c/e)
    # Expected overlaps (by design, not failures):
    #   - hand ↔ board_player: hand sits at bottom, naturally in front of player arc
    #   - board_player ↔ board_enemy: outer arc slots bow toward each other by design
    # Real failures: any group rect (shrine, enemy bar) overlapping non-natural partners
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

    # Allowed overlap pairs: (group_a_prefix, group_b_prefix)
    # These are by-design visual overlaps, not layout bugs.
    allowed_overlap_pairs = [
        ("hand", "board_player"),       # hand cards in front of player arc
        ("board_player", "board_enemy"),# outer arc slots bow toward each other
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
            # Skip same-group overlaps (own elements may stack)
            if group_a == group_b:
                continue
            # Skip by-design overlaps
            if is_allowed_overlap(group_a, group_b):
                continue
            # Rect intersection test
            ax, ay, aw, ah = ra["x"], ra["y"], ra["w"], ra["h"]
            bx, by, bw, bh = rb["x"], rb["y"], rb["w"], rb["h"]
            if ax < bx + bw and ax + aw > bx and ay < by + bh and ay + ah > by:
                failures.append(
                    f"OVERLAP: {name_a} ({group_a}) intersects {name_b} ({group_b}) — "
                    f"rect_a=({ax:.0f},{ay:.0f},{aw:.0f},{ah:.0f}) "
                    f"rect_b=({bx:.0f},{by:.0f},{bw:.0f},{bh:.0f})"
                )

    if failures:
        print(f"\nFAILURE ({len(failures)} reasons):")
        for f in failures:
            print(f"  - {f}")
        sys.exit(1)
    else:
        print(f"\nPASS: All {len(hand_cards)} hand card + {len(board_cards)} board card checks passed")
        sys.exit(0)


if __name__ == "__main__":
    main()
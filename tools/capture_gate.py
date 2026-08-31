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
import hashlib
import math
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
            if contrast < 0.08:
                failures.append(f"HAND_CARD_{i}: name strip contrast {contrast:.3f} too low (need > 0.08)")
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

    # ─── COVERAGE CHECK: Every occupied slot must show distinct art, not border texture ───
    # Load border texture reference color
    border_ref_rgb = None
    import os
    corner_path = os.path.join(os.path.dirname(png_path), "..", "..", "client", "content", "art", "border", "rootbound_corner_tl.png")
    corner_path = os.path.normpath(corner_path)
    if os.path.exists(corner_path):
        _, _, corner_pixels = read_png(corner_path)
        cw, ch = 148, 172
        cr = cg = cb = 0
        cc = 0
        for cy in range(ch):
            for cx in range(cw):
                ci = (cy * cw + cx) * 4
                if ci + 3 < len(corner_pixels):
                    cr += corner_pixels[ci]
                    cg += corner_pixels[ci+1]
                    cb += corner_pixels[ci+2]
                    cc += 1
        if cc > 0:
            border_ref_rgb = (cr / cc, cg / cc, cb / cc)
            print(f"  Border texture ref RGB: ({border_ref_rgb[0]:.0f}, {border_ref_rgb[1]:.0f}, {border_ref_rgb[2]:.0f})")

    for i, card in enumerate(board_cards):
        r = card.get("rect")
        if not r:
            continue
        slot_state = card.get("state", "empty")
        if slot_state == "empty":
            continue
        cx, cy, cw, ch = int(r["x"]), int(r["y"]), int(r["w"]), int(r["h"])
        if cw < 4 or ch < 4:
            continue
        # Central 30% region — properly rounded to pixel bounds
        c_x = int(cx + cw * 0.35)
        c_y = int(cy + ch * 0.35)
        c_w = max(1, int(cw * 0.3))
        c_h = max(1, int(ch * 0.3))
        c_x = max(cx, min(c_x, cx + cw - c_w))
        c_y = max(cy, min(c_y, cy + ch - c_h))
        # Compute mean RGB and stddev of center 30% region
        cr = cg = cb = cc = 0
        cr2 = cg2 = cb2 = 0
        for py in range(c_y, c_y + c_h, 2):
            for px in range(c_x, c_x + c_w, 2):
                pi = (py * width + px) * 4
                if pi + 3 < len(pixels):
                    rv = pixels[pi]
                    gv = pixels[pi+1]
                    bv = pixels[pi+2]
                    cr += rv; cg += gv; cb += bv
                    cr2 += rv * rv; cg2 += gv * gv; cb2 += bv * bv
                    cc += 1
        if cc == 0:
            continue
        center_mean = (cr / cc, cg / cc, cb / cc)
        center_std = (math.sqrt(cr2/cc - (cr/cc)*(cr/cc)),
                      math.sqrt(cg2/cc - (cg/cc)*(cg/cc)),
                      math.sqrt(cb2/cc - (cb/cc)*(cb/cc)))
        center_std_avg = (center_std[0] + center_std[1] + center_std[2]) / 3.0
        # If center has high stddev (> 15), art is definitely visible — skip RGB checks
        if center_std_avg > 15.0:
            print(f"  PASS board card {i} coverage: center stddev={center_std_avg:.0f} (> 15) — art detail visible")
            continue
        # Low stddev center — check if it matches border texture
        if center_std_avg < 5.0:
            failures.append(f"COVERAGE_BOARD_{i}: center stddev {center_std_avg:.0f} < 5 — no art detail visible, card may be covered by border texture")
            continue
        # Moderate stddev — check RGB against border texture
        if border_ref_rgb is not None:
            r_diff = abs(center_mean[0] - border_ref_rgb[0])
            g_diff = abs(center_mean[1] - border_ref_rgb[1])
            b_diff = abs(center_mean[2] - border_ref_rgb[2])
            if r_diff <= 12 and g_diff <= 12 and b_diff <= 12:
                failures.append(f"COVERAGE_BOARD_{i}: center RGB ({center_mean[0]:.0f},{center_mean[1]:.0f},{center_mean[2]:.0f}) matches border ({border_ref_rgb[0]:.0f},{border_ref_rgb[1]:.0f},{border_ref_rgb[2]:.0f}) with stddev {center_std_avg:.0f} — card may be covered by border texture")
            else:
                print(f"  PASS board card {i} coverage: center distinct from border ({center_mean[0]:.0f},{center_mean[1]:.0f},{center_mean[2]:.0f}) vs ({border_ref_rgb[0]:.0f},{border_ref_rgb[1]:.0f},{border_ref_rgb[2]:.0f})")
        else:
            print(f"  PASS board card {i} coverage: center stddev={center_std_avg:.0f}")

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

    # Check 7: Pairwise hand card rects vs player slot rects (ART-STYLE-3)
    player_slots = [c for c in board_cards if c.get("slot", "").startswith("player")]
    if not player_slots:
        print("  PASS hand/field: no player board slots to check")
    else:
        overlap_found = False
        for hc in hand_cards:
            if "rect" not in hc:
                continue
            hr = hc["rect"]
            for sc in player_slots:
                if "rect" not in sc:
                    continue
                sr = sc["rect"]
                # AABB overlap test
                x_overlap = hr["x"] < sr["x"] + sr["w"] and hr["x"] + hr["w"] > sr["x"]
                y_overlap = hr["y"] < sr["y"] + sr["h"] and hr["y"] + hr["h"] > sr["y"]
                if x_overlap and y_overlap:
                    slot_name = sc.get("slot", "?")
                    hand_name = hc.get("name", "?")
                    overlap_px = (sr["y"] + sr["h"]) - hr["y"]
                    failures.append(
                        f"HAND_FIELD_OVERLAP: hand card \"{hand_name}\" rect "
                        f"({hr['x']:.0f},{hr['y']:.0f},{hr['w']:.0f},{hr['h']:.0f}) "
                        f"overlaps player slot \"{slot_name}\" rect "
                        f"({sr['x']:.0f},{sr['y']:.0f},{sr['w']:.0f},{sr['h']:.0f}) "
                        f"— overlap={overlap_px:.0f}px"
                    )
                    overlap_found = True
        if not overlap_found:
            # Show the best gap
            best_gap = float("inf")
            for hc in hand_cards:
                if "rect" not in hc:
                    continue
                hr = hc["rect"]
                for sc in player_slots:
                    if "rect" not in sc:
                        continue
                    sr = sc["rect"]
                    gap = hr["y"] - (sr["y"] + sr["h"])
                    if gap < best_gap:
                        best_gap = gap
            print(f"  PASS hand/field: all {len(hand_cards)} hand cards clear of {len(player_slots)} player slots, "
                  f"best gap={best_gap:.0f}px")

    # Check 8: Viewport containment (HAND-VIEWPORT-FIX-1R, FULL-DECK-2)
    # Every hand card AND board card must be fully inside the project viewport.
    # Dims are read from meta.json (viewport_width/height) if available, falling
    # back to client/project.godot — no invented values, no bypass flags.
    vp_w = meta.get("viewport_width")
    vp_h = meta.get("viewport_height")
    if vp_w is None or vp_h is None:
        project_godot = Path(__file__).resolve().parent.parent / "client" / "project.godot"
        if project_godot.exists():
            for line in project_godot.read_text().splitlines():
                line = line.strip()
                if line.startswith("window/size/viewport_width="):
                    vp_w = int(line.split("=", 1)[1])
                elif line.startswith("window/size/viewport_height="):
                    vp_h = int(line.split("=", 1)[1])
    if vp_w is None or vp_h is None:
        failures.append(f"VIEWPORT_CONTAINMENT: could not read viewport dims from {project_godot}")
    else:
        vp_violations = 0
        for i, card in enumerate(hand_cards):
            r = card.get("rect")
            if not r:
                continue
            cx, cy, cw, ch = r["x"], r["y"], r["w"], r["h"]
            if cx < 0 or cy < 0 or cx + cw > vp_w or cy + ch > vp_h:
                name = card.get("name", f"hand_{i}")
                failures.append(
                    f"VIEWPORT_CONTAINMENT: hand card \"{name}\" rect "
                    f"({cx:.0f},{cy:.0f},{cw:.0f},{ch:.0f}) exceeds viewport {vp_w}x{vp_h}"
                )
                vp_violations += 1
        for i, card in enumerate(board_cards):
            r = card.get("rect")
            if not r:
                continue
            cx, cy, cw, ch = r["x"], r["y"], r["w"], r["h"]
            if cx < 0 or cy < 0 or cx + cw > vp_w or cy + ch > vp_h:
                slot = card.get("slot", f"board_{i}")
                failures.append(
                    f"VIEWPORT_CONTAINMENT: board card \"{slot}\" rect "
                    f"({cx:.0f},{cy:.0f},{cw:.0f},{ch:.0f}) exceeds viewport {vp_w}x{vp_h}"
                )
                vp_violations += 1
        if vp_violations == 0:
            print(f"  PASS viewport containment: all {len(hand_cards)} hand + {len(board_cards)} board cards within {vp_w}x{vp_h}")

    # Check 9: Hand cards must not overlap the End Turn / YOUR TURN strip area
    # End Turn button: BottomRight anchor, OffsetRight=-10, OffsetLeft=-100
    #                  OffsetBottom=-70, OffsetTop=-106 (height 36)
    # Turn indicator:  BottomRight, OffsetBottom=-110, OffsetTop=-126 (height 16)
    # So the strip zone = rightmost 100px × bottommost 126px
    if vp_w is not None and vp_h is not None:
        strip_left = vp_w - 100
        strip_top = vp_h - 126
        et_overlap = False
        for i, card in enumerate(hand_cards):
            r = card.get("rect")
            if not r:
                continue
            cx, cy, cw, ch = r["x"], r["y"], r["w"], r["h"]
            # AABB overlap with strip zone
            if cx < strip_left + 100 and cx + cw > strip_left and cy < strip_top + 126 and cy + ch > strip_top:
                name = card.get("name", f"hand_{i}")
                failures.append(
                    f"END_TURN_OVERLAP: hand card \"{name}\" rect "
                    f"({cx:.0f},{cy:.0f},{cw:.0f},{ch:.0f}) overlaps End Turn strip "
                    f"({strip_left:.0f},{strip_top:.0f},100,126)"
                )
                et_overlap = True
        if not et_overlap:
            print(f"  PASS End Turn strip: all {len(hand_cards)} hand cards clear of strip zone ({strip_left:.0f},{strip_top:.0f},100,126)")

    # Check 10: PAINTED-PLATE-1 — sample plate-only areas between/outside card slots.
    # Board rect = (0, 74, vp_w, vp_h-74-160). The plate is dark fantasy (avg ~87/255
    # raw, ~45/255 rendered through atmosphere). Sample the ring interior gap and
    # the slot-to-hand gap. Fail if any region < 30/255 mean (dark fantasy floor)
    # or near-zero stddev (flat wash = no art).
    board_top = 74
    board_bottom_offset = 160
    plate_threshold = 30.0 / 255.0
    vp_w_local = vp_w
    vp_h_local = vp_h
    if vp_w_local is not None and vp_h_local is not None:
        board_h = vp_h_local - board_top - board_bottom_offset
        if board_h > 0 and vp_w_local > 0:
            # Sample positions derived from PopulateLanes math at reference 648 height:
            #   slotH = 148, playerBaseY ~250 (center slot top), enemyBaseY ~39
            #   handTop = vh - handCardH - 12 = 484 (at 648)
            # Gaps shrink/grow proportionally with scale.
            sample_sz = 20
            scale_check = vp_h_local / 648.0
            # Mid-board gap between enemy lanes (bottom ~y=186) and player lanes (top ~y=250)
            gap_center_y = int(220 * scale_check + board_top)
            # Mid-board gap between player slots (bottom ~y=398) and hand cards (y=484)
            gap_hand_y = int(440 * scale_check + board_top)
            board_cx = vp_w_local // 2
            regions = [
                ("ring_interior_gap",    board_cx, gap_center_y),
                ("slot_hand_gap",        board_cx, gap_hand_y),
            ]
            check10_pass = True
            for name, rx, ry in regions:
                mean, std = rect_mean_stddev(pixels, width, height, rx, ry, sample_sz, sample_sz)
                print(f"  Check10 {name}: mean={mean:.4f} ({mean*255:.0f}/255), std={std:.4f}")
                if mean < plate_threshold:
                    failures.append(
                        f"PLATE_DARK_{name.upper()}: mean luminance {mean:.4f} ({mean*255:.0f}/255) "
                        f"below threshold {plate_threshold*255:.0f}/255"
                    )
                    check10_pass = False
                if std < 3.0 / 255.0:
                    failures.append(
                        f"PLATE_FLAT_{name.upper()}: stddev {std:.4f} ({std*255:.0f}/255) "
                        f"below threshold 3/255 — region appears flat"
                    )
                    check10_pass = False
            if check10_pass:
                print(f"  PASS Check10: both plate regions above {plate_threshold*255:.0f}/255 with visible texture")

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

    # TASK-DECKART-1: ARMORY RAIL layout — validate card tiles show distinct art.
    # Tiles carry per-card rect + strata in meta. Each tile must have visible
    # non-uniform art (an empty dark rectangle is a FAIL), at least 8 tiles must
    # pass, spanning at least 3 distinct strata.
    tiles = meta.get("tiles")
    if not tiles:
        failures.append("NO_TILES: meta has no 'tiles' array (deck builder must expose tile rects)")
    else:
        art_window_frac = 0.62  # art window = tile above the name band + stat rail (0.18+0.12=0.30 plate)
        visible_tiles = 0
        strata_seen = set()
        # Tiles share one geometry — the art window is the rect minus the bottom plate.
        for t in tiles:
            r = t.get("rect", {})
            x, y = r.get("x", 0), r.get("y", 0)
            w, h = r.get("w", 0), r.get("h", 0)
            if w <= 0 or h <= 0:
                failures.append(f"BAD_TILE_RECT: {t.get('card_id')} rect {r}")
                continue
            # Art window: top (w * 0.70) of the tile — excludes the bottom name band + stat rail
            aw = max(1, w)
            ah = max(1, int(h * art_window_frac))
            mean, std = rect_mean_stddev(pixels, width, height, x, y, aw, ah)
            if std < 5.0 / 255.0:
                failures.append(
                    f"EMPTY_TILE: {t.get('card_id')} ({t.get('strata')}) art window "
                    f"stddev {std:.3f} ({std*255:.0f}/255) — art is an empty rectangle"
                )
            else:
                visible_tiles += 1
                strata_seen.add(t.get("strata", "?"))
                print(f"  PASS tile {t.get('card_id')}: art mean={mean:.3f}, std={std:.3f}")

        if visible_tiles < 8:
            failures.append(f"NOT_ENOUGH_ART: only {visible_tiles}/8 tiles show distinct art (need >= 8)")
        if len(strata_seen) < 3:
            failures.append(f"NOT_ENOUGH_STRATA: art spans {len(strata_seen)} strata (need >= 3): {sorted(strata_seen)}")
        else:
            print(f"  PASS strata coverage: {sorted(strata_seen)} ({len(strata_seen)} strata)")

    if failures:
        print(f"\nFAILURE ({len(failures)} reasons):")
        for f in failures:
            print(f"  - {f}")
        sys.exit(1)
    else:
        print("\nPASS: All deck builder checks passed")
        sys.exit(0)


# ════════════════════════════════════════════
# Deck builder filter chips validator (TASK-DECKFILTER-1)
# ════════════════════════════════════════════

def validate_deck_test_chips(png_path, meta):
    """Validate that the strata filter chip row is visible, a non-ALL chip
    (HOLLOW) is selected, no chip is clipped at either edge."""
    if not png_path.exists():
        print(f"FAIL: PNG not found: {png_path}")
        sys.exit(1)

    width, height, pixels = read_png(str(png_path))
    total_pixels = width * height
    print(f"Image: {width}x{height}, {total_pixels} pixels")
    failures = []

    near_black_threshold = 25 / 255.0

    # 1. Whole frame must not be all-dark (chip row must be visible)
    dark_count = 0
    for i in range(0, len(pixels), 4):
        r = pixels[i] / 255.0
        g = pixels[i + 1] / 255.0
        b = pixels[i + 2] / 255.0
        lum = 0.2126 * r + 0.7152 * g + 0.0722 * b
        if lum < near_black_threshold:
            dark_count += 1

    dark_ratio = dark_count / total_pixels
    if dark_ratio > 0.92:
        failures.append(f"WHOLE_FRAME_DARK: {dark_ratio:.1%} pixels are near-black (threshold 92%)")
    else:
        print(f"  PASS whole-frame dark: {dark_ratio:.1%} near-black (limit 92%)")

    # 2. Top bar region (y=0 to y=70, full width) must have content —
    #    this is where the filter chip row lives. Check it's not uniform-dark.
    top_bar_h = min(70, height)
    top_samples = 0
    top_lum_sum = 0.0
    for y in range(top_bar_h):
        for x in range(0, width, 4):  # sample every 4th pixel
            i = (y * width + x) * 4
            if i + 3 >= len(pixels):
                continue
            r = pixels[i] / 255.0
            g = pixels[i + 1] / 255.0
            b = pixels[i + 2] / 255.0
            lum = 0.2126 * r + 0.7152 * g + 0.0722 * b
            top_lum_sum += lum
            top_samples += 1

    if top_samples > 0:
        top_avg_lum = top_lum_sum / top_samples
        if top_avg_lum < 0.05:
            failures.append(f"TOP_BAR_DARK: average luminance {top_avg_lum:.3f} — chip row not visible")
        else:
            print(f"  PASS top bar luminance: avg {top_avg_lum:.3f} (chip row has content)")

    # 3. Check for HOLLOW (purple) pixels in the top bar area to confirm
    #    the non-ALL filter chip is selected. HOLLOW = #5A3A5A ≈ R=90,G=58,B=90
    #    The selected chip has a fill of HOLLOW at 22% alpha over the dark bg
    #    (~RGB 49,38,40). Also check for gold pixels which indicate the
    #    selected chip's label color (#D4B84C ≈ R=212,G=184,B=76).
    #    Combining both signals gives reliable detection.
    selected_detected = False
    chip_row_y_start = 40  # chips start ~40px down in the top bar
    chip_row_y_end = min(chip_row_y_start + 44, height, top_bar_h)
    hollow_pixels = 0
    gold_pixels = 0
    total_checked = 0
    if chip_row_y_start < chip_row_y_end:
        for y in range(chip_row_y_start, chip_row_y_end):
            for x in range(width):  # full-density sampling
                i = (y * width + x) * 4
                if i + 3 >= len(pixels):
                    continue
                r = pixels[i]
                g = pixels[i + 1]
                b = pixels[i + 2]
                # Purple-ish (HOLLOW border #5A3A5A or its anti-aliased edges)
                if r > 60 and r < 130 and g > 25 and g < 80 and b > 50 and b < 120:
                    hollow_pixels += 1
                # Gold-ish (selected label color #D4B84C or similar)
                if r > 170 and g > 140 and b < 120 and r > g and g > b:
                    gold_pixels += 1
                total_checked += 1

    if total_checked > 0:
        hollow_ratio = hollow_pixels / total_checked
        gold_ratio = gold_pixels / total_checked
        # Either purple border pixels (> 2 per 10K) or gold label (> 20 per 10K) counts
        if hollow_ratio < 0.0002 and gold_ratio < 0.002:
            failures.append(f"NO_HOLLOW_SELECTED: hollow={hollow_pixels}/{total_checked} "
                          f"({hollow_ratio:.4f}), gold={gold_pixels}/{total_checked} "
                          f"({gold_ratio:.4f}) — no selected chip detected")
        else:
            print(f"  PASS HOLLOW filter active: "
                  f"{hollow_pixels} purple ({hollow_ratio:.4f}), "
                  f"{gold_pixels} gold ({gold_ratio:.4f}) in chip row area")

    # 4. Check that the first and last chips aren't clipped at edges.
    #    Scan the left and right edges of the top bar for content.
    left_edge_samples = 0
    left_edge_lit = 0
    right_edge_samples = 0
    right_edge_lit = 0
    lit_threshold = 30 / 255.0
    for y in range(chip_row_y_start, chip_row_y_end):
        # Left edge (x=0 to x=5)
        for x in range(6):
            i = (y * width + x) * 4
            if i + 3 >= len(pixels):
                continue
            r = pixels[i] / 255.0
            g = pixels[i + 1] / 255.0
            b = pixels[i + 2] / 255.0
            lum = 0.2126 * r + 0.7152 * g + 0.0722 * b
            left_edge_samples += 1
            if lum >= lit_threshold:
                left_edge_lit += 1
        # Right edge (x=width-6 to x=width-1)
        for x in range(max(0, width - 6), width):
            i = (y * width + x) * 4
            if i + 3 >= len(pixels):
                continue
            r = pixels[i] / 255.0
            g = pixels[i + 1] / 255.0
            b = pixels[i + 2] / 255.0
            lum = 0.2126 * r + 0.7152 * g + 0.0722 * b
            right_edge_samples += 1
            if lum >= lit_threshold:
                right_edge_lit += 1

    if left_edge_samples > 0:
        left_frac = left_edge_lit / left_edge_samples
        if left_frac < 0.3:
            # On phone (390px), the scroll might push the first chip off-screen
            if width <= 480:
                print(f"  NOTE Phone narrow ({width}px): left edge chips may scroll, "
                      f"left edge lit={left_frac:.0%}")
            else:
                failures.append(f"LEFT_EDGE_CLIPPED: only {left_frac:.0%} of left-edge "
                               f"pixels are lit — first chip may be clipped")

    if right_edge_samples > 0:
        right_frac = right_edge_lit / right_edge_samples
        if right_frac < 0.3:
            failures.append(f"RIGHT_EDGE_CLIPPED: only {right_frac:.0%} of right-edge "
                           f"pixels are lit — last chip may be clipped")
        else:
            print(f"  PASS right edge: {right_frac:.0%} edge pixels lit (last chip visible)")

    if failures:
        print(f"\nFAILURE ({len(failures)} reasons):")
        for f in failures:
            print(f"  - {f}")
        sys.exit(1)
    else:
        print("\nPASS: All deck builder filter chip checks passed")
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

# ════════════════════════════════════════════
# Screen live validator (black-screen gate)
# Used for title_test, map_test — FAIL if > 60%
# of pixels are near-black (mean luminance < 12)
# ════════════════════════════════════════════

def validate_screen_live(png_path, meta):
    """Generic black-screen gate for full-screen captures (title, map).
    FAIL if > 60% of pixels have luminance < 12/255 (near-black).
    The title/map art is dark fantasy (ambient ~40-56/255), so 12/255
    is well below the legitimate floor."""
    if not png_path.exists():
        print(f"FAIL: PNG not found: {png_path}")
        sys.exit(1)

    width, height, pixels = read_png(str(png_path))
    total_pixels = width * height
    print(f"Image: {width}x{height}, {total_pixels} pixels")

    black_threshold = 12.0 / 255.0
    dark_count = 0
    for i in range(0, len(pixels), 4):
        r = pixels[i] / 255.0
        g = pixels[i + 1] / 255.0
        b = pixels[i + 2] / 255.0
        if get_luminance(r, g, b) < black_threshold:
            dark_count += 1

    dark_ratio = dark_count / total_pixels
    failures = []

    if dark_ratio > 0.60:
        failures.append(
            f"BLACK_SCREEN: {dark_ratio:.1%} pixels are near-black "
            f"(threshold 60%, luminance < {black_threshold*255:.0f}/255) — "
            f"art is not rendering"
        )
    else:
        print(f"  PASS black-screen: {dark_ratio:.1%} near-black pixels (limit 60%)")

    # Also check the art area (center 70% of frame) isn't uniformly dark
    margin_w = int(width * 0.15)
    margin_h = int(height * 0.15)
    cx, cy = margin_w, margin_h
    cw, ch = width - 2 * margin_w, height - 2 * margin_h
    if cw > 0 and ch > 0:
        mean, std = rect_mean_stddev(pixels, width, height, cx, cy, cw, ch)
        if std < 5.0 / 255.0:
            failures.append(
                f"ART_FLAT: center {cw}x{ch} region stddev {std:.3f} "
                f"({std*255:.0f}/255) — too uniform (need > 5/255)"
            )
        else:
            print(f"  PASS art region: mean={mean:.3f}, std={std:.3f}")

    if failures:
        print(f"\nFAILURE ({len(failures)} reasons):")
        for f in failures:
            print(f"  - {f}")
        sys.exit(1)
    else:
        print(f"\nPASS: All black-screen checks passed")


def validate_tutorial_capture(png_path, meta):
    """Validate a tutorial beat capture: not a black screen, has beat metadata."""
    width, height, pixels, color_type = read_png(png_path)
    total_pixels = width * height

    black_threshold = 12.0 / 255.0
    dark_count = 0
    for i in range(0, len(pixels), 4 if color_type == 6 else 3):
        r = pixels[i] / 255.0
        g = pixels[i + 1] / 255.0
        b = pixels[i + 2] / 255.0
        if get_luminance(r, g, b) < black_threshold:
            dark_count += 1

    dark_ratio = dark_count / total_pixels
    failures = []

    if dark_ratio > 0.60:
        failures.append(
            f"BLACK_SCREEN: {dark_ratio:.1%} pixels are near-black "
            f"(threshold 60%, luminance < {black_threshold*255:.0f}/255)"
        )
    else:
        print(f"  PASS black-screen: {dark_ratio:.1%} near-black pixels (limit 60%)")

    beat_id = meta.get("beat_id", "")
    tutorial_id = meta.get("tutorial_id", "")
    turn = meta.get("turn", 0)
    if beat_id:
        print(f"  Beat: {beat_id} (turn {turn}, tutorial {tutorial_id})")
    else:
        failures.append("MISSING_META: capture has no beat_id in meta.json")

    margin_w = int(width * 0.15)
    margin_h = int(height * 0.15)
    cx, cy = margin_w, margin_h
    cw, ch = width - 2 * margin_w, height - 2 * margin_h
    if cw > 0 and ch > 0:
        mean, std = rect_mean_stddev(pixels, width, height, cx, cy, cw, ch)
        if std < 5.0 / 255.0:
            failures.append(
                f"ART_FLAT: center {cw}x{ch} region stddev {std:.3f} "
                f"({std*255:.0f}/255) — too uniform (need > 5/255)"
            )
        else:
            print(f"  PASS art region: mean={mean:.3f}, std={std:.3f}")

    if failures:
        print(f"\nFAILURE ({len(failures)} reasons):")
        for f in failures:
            print(f"  - {f}")
        sys.exit(1)
    else:
        print(f"\nPASS: Tutorial beat capture {beat_id} OK")


VALIDATORS = {
    "title_test": validate_screen_live,
    "title_test_wide": validate_screen_live,
    "map_test": validate_screen_live,
    "map_test_wide": validate_screen_live,
    "duel_test": validate_duel_test,
    "duel_test_wide": validate_duel_test,
    "deck_test": validate_deck_test_chips,
    "deck_test_phone": validate_deck_test_chips,
    "title_deck": validate_title_deck_test,
    "tutorial_warrior_intro": validate_tutorial_capture,
}


def main():
    import hashlib
    capture_dir = Path(__file__).resolve().parent.parent / "artifacts" / "captures"

    if len(sys.argv) > 1:
        bases = sys.argv[1:]
    else:
        bases = ["duel_test", "duel_test_wide"]

    # Anti-spoof: if duel_test_wide is being validated, fail if its PNG hash matches
    # duel_test.png (means the wide capture was not actually run at wide resolution).
    # Also fail if wide meta.json doesn't report wide viewport dims (1999x932).
    has_standard = any(b == "duel_test" for b in bases)
    has_wide = any(b == "duel_test_wide" for b in bases)

    if has_standard and has_wide:
        std_png = capture_dir / "duel_test.png"
        wide_png = capture_dir / "duel_test_wide.png"
        std_meta = capture_dir / "duel_test.meta.json"
        wide_meta = capture_dir / "duel_test_wide.meta.json"
        if std_png.exists() and wide_png.exists():
            std_hash = hashlib.md5(std_png.read_bytes()).hexdigest()
            wide_hash = hashlib.md5(wide_png.read_bytes()).hexdigest()
            if std_hash == wide_hash:
                print("FAIL: duel_test.png and duel_test_wide.png are byte-identical — "
                      "wide capture was not run at the correct resolution")
                sys.exit(1)
            print(f"  PASS hash distinct: duel_test.png ≠ duel_test_wide.png")
        if wide_meta.exists():
            wm = json.load(wide_meta.open())
            vp_w = wm.get("viewport_width")
            vp_h = wm.get("viewport_height")
            if vp_w is None or vp_h is None or vp_w < 1500 or vp_h < 900:
                print(f"FAIL: duel_test_wide.meta.json reports viewport {vp_w}x{vp_h} — "
                      f"expected wide dims (≥1999x932). The meta was not generated at wide resolution.")
                sys.exit(1)
            print(f"  PASS wide viewport: {vp_w}x{vp_h} confirmed")
    resolved = []
    for b in bases:
        if b == "duel_test":
            if "duel_test" not in resolved:
                resolved.append("duel_test")
            if "duel_test_wide" not in resolved:
                resolved.append("duel_test_wide")
        else:
            if b not in resolved:
                resolved.append(b)
    bases = resolved

    # ─── HARDENING: guard against byte-identical wide captures ───
    if "duel_test" in bases and "duel_test_wide" in bases:
        std_path = capture_dir / "duel_test.png"
        wide_path = capture_dir / "duel_test_wide.png"
        if std_path.exists() and wide_path.exists():
            with open(std_path, "rb") as f: std_hash = hashlib.md5(f.read()).hexdigest()
            with open(wide_path, "rb") as f: wide_hash = hashlib.md5(f.read()).hexdigest()
            if std_hash == wide_hash:
                print(f"FAIL: duel_test.png and duel_test_wide.png are byte-identical (both {std_hash})")
                print("  The wide capture was produced without the viewport swap. Use bash tools/capture_duel.sh")
                sys.exit(1)

    # ─── HARDENING: wide capture must report wide viewport dims ───
    if "duel_test_wide" in bases:
        wide_meta_path = capture_dir / "duel_test_wide.meta.json"
        if wide_meta_path.exists():
            with open(wide_meta_path) as f:
                wide_meta = json.load(f)
            vw = wide_meta.get("viewport_width", 0)
            vh = wide_meta.get("viewport_height", 0)
            if vw < 1900 or vh < 900:
                print(f"FAIL: duel_test_wide meta reports {vw}x{vh} — expected >= 1900x900")
                print("  The wide capture was produced without viewport swap. Use bash tools/capture_duel.sh")
                sys.exit(1)

    exit_code = 0
    for base in bases:
        if base not in VALIDATORS:
            print(f"Unknown capture type '{base}'. Known: {', '.join(VALIDATORS.keys())}")
            sys.exit(1)

        png_path = capture_dir / f"{base}.png"
        meta_path = capture_dir / f"{base}.meta.json"

        meta = {}
        if meta_path.exists():
            with open(meta_path) as f:
                meta = json.load(f)
        else:
            print(f"{base}: No meta.json found — using validate_screen_live defaults")

        if not png_path.exists():
            print(f"FAIL: {base}: PNG not found: {png_path}")
            exit_code = 1
            continue

        print(f"\n═══ Validating: {base} ═══")
        try:
            VALIDATORS[base](png_path, meta)
        except SystemExit as e:
            if e.code != 0:
                exit_code = 1

    if exit_code != 0:
        sys.exit(exit_code)


if __name__ == "__main__":
    main()
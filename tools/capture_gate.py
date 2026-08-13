#!/usr/bin/env python3
"""
capture_gate.py — Acceptance gate for UI task screenshots.

Reads a PNG + meta.json pair from artifacts/captures/ and validates that:
  - Whole frame < 85% near-black pixels (luminance < 20)
  - Every hand-card body rect has mean luminance > 25 AND stddev > 12
  - Every name-strip rect shows high-contrast pixels vs its background
  - Card count matches the test state (4 hand cards, 10 board cards)

Usage: python3 tools/capture_gate.py artifacts/captures/duel_test
(defaults to duel_test if no name given)
"""

import json
import os
import struct
import sys
import zlib
from pathlib import Path


def read_png(filename):
    """Read PNG file and return raw RGBA pixel data and dimensions."""
    with open(filename, 'rb') as f:
        # Verify PNG header
        if f.read(8) != b'\x89PNG\r\n\x1a\n':
            raise ValueError('Not a valid PNG file')

        # Read chunks until we find IHDR and IDAT
        width = height = 0
        raw_data = b''
        bit_depth = color_type = 0

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

        # Decompress
        decompressed = zlib.decompress(raw_data)

        # Calculate row size (PNG has filter byte per row)
        if color_type == 6:  # RGBA
            bytes_per_pixel = 4
        elif color_type == 2:  # RGB
            bytes_per_pixel = 3
        else:
            raise ValueError(f'Unsupported color type: {color_type}')

        row_size = 1 + width * bytes_per_pixel  # filter byte + pixel data
        if len(decompressed) != row_size * height:
            # Might be interlaced or other; try anyway
            pass

        # Remove filter bytes and extract RGBA
        pixels = bytearray()
        for y in range(height):
            offset = 1 + y * row_size
            row = decompressed[offset:offset + width * bytes_per_pixel]
            if bytes_per_pixel == 3:
                for i in range(0, len(row), 3):
                    pixels.append(row[i])     # R
                    pixels.append(row[i+1])   # G
                    pixels.append(row[i+2])   # B
                    pixels.append(255)         # A
            else:
                pixels.extend(row)

        return width, height, bytes(pixels)


def get_luminance(r, g, b):
    """Relative luminance (sRGB gamma corrected)."""
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def rect_mean_stddev(pixels, width, height, x, y, w, h):
    """Compute mean luminance and standard deviation for a sub-rect."""
    x, y, w, h = int(x), int(y), int(w), int(h)
    # Clamp to image bounds
    x = max(0, min(x, width - 1))
    y = max(0, min(y, height - 1))
    w = min(w, width - x)
    h = min(h, height - y)

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
                f"HAND_CARD_{i}: mean luminance {mean:.3f} too low (need > {25 / 255.0:.3f})"
            )
        if std <= 12 / 255.0:
            failures.append(
                f"HAND_CARD_{i}: stddev {std:.3f} too low (need > {12 / 255.0:.3f})"
            )
        if mean > 25 / 255.0 and std > 12 / 255.0:
            print(f"  PASS hand card {i}: mean={mean:.3f}, std={std:.3f}")

        # Check name label strip
        name_r = card.get("name_rect")
        if name_r:
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
        mean, std = rect_mean_stddev(pixels, width, height, r["x"], r["y"], r["w"], r["h"])
        if mean <= 25 / 255.0:
            failures.append(
                f"BOARD_CARD_{i}: mean luminance {mean:.3f} too low (need > {25 / 255.0:.3f})"
            )
        if mean > 25 / 255.0:
            print(f"  PASS board card {i}: mean={mean:.3f}, std={std:.3f}")

        name_r = card.get("name_rect")
        if name_r:
            contrast = rect_max_contrast(pixels, width, height, name_r["x"], name_r["y"], name_r["w"], name_r["h"])
            if contrast < 0.15:
                failures.append(
                    f"BOARD_CARD_{i}: name strip contrast {contrast:.3f} too low (need > 0.15)"
                )
            else:
                print(f"  PASS board card {i} name: contrast={contrast:.3f}")

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
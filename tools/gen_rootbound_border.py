#!/usr/bin/env python3
"""
Generate Root-Bound Stone card border — Pale warm limestone with gold keyline.
Stone band: RGB (158,149,118) — mean brightness ~141.
Gold keyline: RGB (161,139,79) — 1-2px at inner edge where band meets art.
Card bezel: RGB (13,12,10) — near-black.
Texture: vary +/-25 around base, never below 110.

Output: full 832x1216 border PNG, slices to client/content/art/border/
"""
import math
import os
import random
import sys
from PIL import Image, ImageDraw

W, H = 832, 1216
BAND = max(1, round(W * 0.07))  # 58px
INNER_W = W - 2 * BAND   # 716
INNER_H = H - 2 * BAND   # 1100

# ── Pale warm limestone palette ──
STONE_BASE = (158, 149, 118)      # pale warm limestone, mean brightness ~141
STONE_DARK = (133, 125, 98)       # darker veining (base -25)
STONE_LIGHT = (183, 173, 143)     # lighter highlight (base +25)
STONE_SHADOW = (110, 104, 82)     # deepest shadow, never below 110
# Gold keyline at inner edge where band meets art
GOLD_KEY = (161, 139, 79)
# Chipped inner lip: between stone and gold, slightly lighter
CHIPPED_INNER = (168, 158, 128)
CANVAS_COLOR = (13, 12, 10)       # near-black card bezel

# Art window boundaries (source image coords)
WIN_L = 148
WIN_T = 172
WIN_R = 675
WIN_B = 1019


def draw_veining(draw, x0, y0, x1, y1):
    """Add subtle darker veining lines typical of limestone."""
    rng = random.Random(42)
    num_veins = (x1 - x0) * (y1 - y0) // 6000 + 2
    for _ in range(num_veins):
        sx = rng.randint(x0 + 2, x1 - 10)
        sy = rng.randint(y0 + 2, y1 - 2)
        length = rng.randint(6, 20)
        for lx in range(length):
            ly = rng.randint(-1, 1)
            px = sx + lx
            py = sy + ly
            if x0 <= px < x1 and y0 <= py < y1:
                fade = 1.0 - (lx / length) * 0.6
                c = tuple(int(round(STONE_BASE[i] - 20 * fade)) for i in range(3))
                c = tuple(max(STONE_SHADOW[i], min(STONE_LIGHT[i], c[i])) for i in range(3))
                draw.point((px, py), fill=c)


def draw_chipped_chipping(draw, x0, y0, x1, y1):
    """Add small chipped/eroded texture spots on the stone surface."""
    rng = random.Random(137)
    for _ in range(max(1, (x1 - x0) * (y1 - y0) // 5000)):
        px = rng.randint(x0 + 1, x1 - 2)
        py = rng.randint(y0 + 1, y1 - 2)
        size = rng.randint(1, 2)
        for dy in range(-size, size + 1):
            for dx in range(-size, size + 1):
                sx, sy = px + dx, py + dy
                if x0 <= sx < x1 and y0 <= sy < y1:
                    brightness_var = rng.randint(-15, 15)
                    c = tuple(int(round(STONE_BASE[i] + brightness_var)) for i in range(3))
                    c = tuple(max(STONE_SHADOW[i], min(STONE_LIGHT[i], c[i])) for i in range(3))
                    draw.point((sx, sy), fill=c)


def generate_border(output_path):
    img = Image.new('RGB', (W, H), CANVAS_COLOR)
    draw = ImageDraw.Draw(img)
    rng = random.Random(137)

    # ── 1. Base stone frame ──
    draw.rectangle([(0, 0), (W - 1, BAND - 1)], fill=STONE_BASE)          # top band
    draw.rectangle([(0, H - BAND), (W - 1, H - 1)], fill=STONE_BASE)      # bottom band
    draw.rectangle([(0, 0), (BAND - 1, H - 1)], fill=STONE_BASE)          # left band
    draw.rectangle([(W - BAND, 0), (W - 1, H - 1)], fill=STONE_BASE)      # right band

    # ── 2. Subtle corner shadow for depth ──
    for cx, cy in [(BAND // 2, BAND // 2),
                    (W - BAND // 2, BAND // 2),
                    (BAND // 2, H - BAND // 2),
                    (W - BAND // 2, H - BAND // 2)]:
        for dy in range(-BAND // 2, BAND // 2):
            for dx in range(-BAND // 2, BAND // 2):
                dist = math.sqrt(dx * dx + dy * dy)
                if dist < BAND * 0.4:
                    factor = 1 - (dist / (BAND * 0.4)) * 0.10  # subtle 10% dark
                    px = cx + dx
                    py = cy + dy
                    if 0 <= px < W and 0 <= py < H:
                        orig = img.getpixel((px, py))
                        darkened = tuple(min(255, max(0, int(c * factor))) for c in orig)
                        draw.point((px, py), fill=darkened)

    # ── 3. Texture: darker veining lines ──
    draw_veining(draw, 0, 0, W, BAND)                   # top
    draw_veining(draw, 0, H - BAND, W, H)               # bottom
    draw_veining(draw, 0, 0, BAND, H)                   # left
    draw_veining(draw, W - BAND, 0, W, H)               # right

    # ── 4. Texture: chipped/eroded spots ──
    draw_chipped_chipping(draw, 0, 0, W, BAND)
    draw_chipped_chipping(draw, 0, H - BAND, W, H)
    draw_chipped_chipping(draw, 0, 0, BAND, H)
    draw_chipped_chipping(draw, W - BAND, 0, W, H)

    # ── 5. Gold keyline (1-2px) at the inner edge where band meets art ──
    # Top edge
    for ex in range(WIN_L, WIN_R + 1):
        draw.point((ex, WIN_T), fill=GOLD_KEY)
        if ex % 2 == 0:
            draw.point((ex, WIN_T + 1), fill=GOLD_KEY)
    # Bottom edge
    for ex in range(WIN_L, WIN_R + 1):
        draw.point((ex, WIN_B), fill=GOLD_KEY)
        if ex % 2 == 0:
            draw.point((ex, WIN_B - 1), fill=GOLD_KEY)
    # Left edge
    for ey in range(WIN_T, WIN_B + 1):
        draw.point((WIN_L, ey), fill=GOLD_KEY)
        if ey % 2 == 0:
            draw.point((WIN_L + 1, ey), fill=GOLD_KEY)
    # Right edge
    for ey in range(WIN_T, WIN_B + 1):
        draw.point((WIN_R, ey), fill=GOLD_KEY)
        if ey % 2 == 0:
            draw.point((WIN_R - 1, ey), fill=GOLD_KEY)

    # ── 6. Chipped inner lip (overlapping the gold keyline slightly) ──
    # Add some chips/breaks to the gold to keep it looking natural
    rng_chip = random.Random(73)
    for edge_name, x_range, y_range, is_horizontal in [
        ("top", (WIN_L, WIN_R), (WIN_T, WIN_T + 2), True),
        ("bottom", (WIN_L, WIN_R), (WIN_B - 2, WIN_B), True),
        ("left", (WIN_L, WIN_L + 2), (WIN_T, WIN_B), False),
        ("right", (WIN_R - 2, WIN_R), (WIN_T, WIN_B), False),
    ]:
        for cx in range(x_range[0], x_range[1] + 1):
            for cy in range(y_range[0], y_range[1] + 1):
                if rng_chip.random() < 0.15:
                    var = rng_chip.randint(-6, 6)
                    chip_color = tuple(min(255, max(0, CHIPPED_INNER[i] + var)) for i in range(3))
                    draw.point((cx, cy), fill=chip_color)

    # ── 7. Fine grain texture along the stone band ──
    rng_fine = random.Random(99)
    for y in range(0, H):
        for x in range(0, W):
            # Skip the art window area
            if WIN_L <= x <= WIN_R and WIN_T <= y <= WIN_B:
                continue
            if rng_fine.random() < 0.025:
                var_g = rng_fine.randint(-8, 8)
                c = tuple(int(round(STONE_BASE[i] + var_g)) for i in range(3))
                c = tuple(max(STONE_SHADOW[i], min(STONE_LIGHT[i], c[i])) for i in range(3))
                draw.point((x, y), fill=c)

    # ── 8. Fill art window with black ──
    draw.rectangle([(WIN_L, WIN_T), (WIN_R, WIN_B)], fill=(0, 0, 0))

    img.save(output_path, 'PNG')
    print(f"Generated border: {output_path} ({os.path.getsize(output_path)} bytes)")
    return img


def generate_stone_grain_texture(output_path, size=64):
    """Generate a small limestone-grain noise texture for the name-band background."""
    rng = random.Random(88)
    img = Image.new('RGB', (size, size), STONE_BASE)
    draw = ImageDraw.Draw(img)
    for y in range(size):
        for x in range(size):
            if rng.random() < 0.06:
                b = rng.randint(-12, 12)
                c = tuple(int(round(STONE_BASE[i] + b)) for i in range(3))
                c = tuple(max(STONE_SHADOW[i], min(STONE_LIGHT[i], c[i])) for i in range(3))
                draw.point((x, y), fill=c)
    # Veining lines
    for _ in range(6):
        sx = rng.randint(0, size - 10)
        sy = rng.randint(0, size - 2)
        for lx in range(rng.randint(4, 14)):
            ly = rng.randint(-1, 1)
            px = sx + lx
            py = sy + ly
            if 0 <= px < size and 0 <= py < size:
                sc = tuple(int(round(STONE_BASE[i] - 18)) for i in range(3))
                draw.point((px, py), fill=sc)
    img.save(output_path, 'PNG')
    print(f"Generated stone grain texture: {output_path}")


def slice_border(img, output_dir):
    """Slice the full border into 8 individual PNGs matching the 9-slice spec."""
    os.makedirs(output_dir, exist_ok=True)

    slices = {
        'corner_tl': (0, 0, WIN_L, WIN_T),
        'corner_tr': (WIN_R, 0, W, WIN_T),
        'corner_bl': (0, WIN_B, WIN_L, H),
        'corner_br': (WIN_R, WIN_B, W, H),
        'edge_top': (WIN_L, 0, WIN_R, WIN_T),
        'edge_bottom': (WIN_L, WIN_B, WIN_R, H),
        'edge_left': (0, WIN_T, WIN_L, WIN_B),
        'edge_right': (WIN_R, WIN_T, W, WIN_B),
    }

    for name, box in slices.items():
        outpath = os.path.join(output_dir, f'rootbound_{name}.png')
        crop = img.crop(box)
        crop.save(outpath, 'PNG')
        print(f"Saved: {outpath} ({crop.size})")

    # Save full image too
    full_path = os.path.join(output_dir, 'rootbound_full.png')
    img.save(full_path, 'PNG')
    print(f"Saved: {full_path}")


def generate_downscale_previews(img, out_dir):
    """Generate 16x16 upscaled previews for visual verification."""
    os.makedirs(out_dir, exist_ok=True)

    corners = {
        'corner_tl': (0, 0, WIN_L, WIN_T),
        'corner_tr': (WIN_R, 0, W, WIN_T),
        'corner_bl': (0, WIN_B, WIN_L, H),
        'corner_br': (WIN_R, WIN_B, W, H),
    }

    for name, box in corners.items():
        c = img.crop(box)
        # 16x16 at 6x upscale as specified
        c_scaled = c.resize((16, 16), Image.LANCZOS)
        c_upscaled = c_scaled.resize((96, 96), Image.NEAREST)
        outpath = os.path.join(out_dir, f'{name}_16px_6x.png')
        c_upscaled.save(outpath, 'PNG')
        print(f"Preview: {outpath}")


if __name__ == '__main__':
    output_path = '/tmp/gen_rootbound_border.png'
    img = generate_border(output_path)

    # Slice to the game's border directory
    border_dir = 'client/content/art/border'
    slice_border(img, border_dir)

    # Generate stone grain texture for the name-band background
    grain_path = os.path.join(border_dir, 'stone_grain.png')
    generate_stone_grain_texture(grain_path)

    # Generate 16x16 upscaled 6x previews in /tmp/
    preview_dir = '/tmp/border_previews'
    generate_downscale_previews(img, preview_dir)

    print("\nDone. Verify the previews in /tmp/border_previews/")
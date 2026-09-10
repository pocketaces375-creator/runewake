#!/usr/bin/env python3
"""
Generate Root-Bound Stone card border — Pale warm limestone with gold keyline.
Stone band: RGBA (158,149,118,255) — mean brightness ~141.
Gold keyline: RGBA (161,139,79,255) — 4px at inner edge of band.
Chipped inner lip: RGBA (168,158,128,255) — 10px at inner edge.

The interior (58..773, 58..1157) is fully transparent (0,0,0,0) so card art
shows through. All drawing stays within the 58px outer band.

Output: full 832x1216 RGBA border PNG, 8 RGBA slice PNGs to client/content/art/border/
"""
import math
import os
import random
import sys
from PIL import Image, ImageDraw

W, H = 832, 1216
BAND = max(1, round(W * 0.07))  # 58

# Band-derived geometry — interior must stay fully transparent
INNER_L = BAND                    # 58
INNER_T = BAND                    # 58
INNER_R = W - BAND - 1            # 773
INNER_B = H - BAND - 1            # 1157
INNER_W = W - 2 * BAND            # 716
INNER_H = H - 2 * BAND            # 1100

# ── Pale warm limestone palette (RGBA) ──
STONE_BASE = (158, 149, 118, 255)      # pale warm limestone, mean brightness ~141
STONE_DARK = (133, 125, 98, 255)       # darker veining (base -25)
STONE_LIGHT = (183, 173, 143, 255)     # lighter highlight (base +25)
STONE_SHADOW = (110, 104, 82, 255)     # deepest shadow, never below 110
# Gold keyline at inner edge where band meets art
GOLD_KEY = (161, 139, 79, 255)
# Chipped inner lip: between stone and gold, slightly lighter
CHIPPED_INNER = (168, 158, 128, 255)


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
                draw.point((px, py), fill=c + (255,))


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
                    draw.point((sx, sy), fill=c + (255,))


def generate_border(output_path):
    img = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    rng = random.Random(137)

    # ── 1. Base stone frame — only the 58px outer band ──
    draw.rectangle([(0, 0), (W - 1, BAND - 1)], fill=STONE_BASE)          # top band
    draw.rectangle([(0, H - BAND), (W - 1, H - 1)], fill=STONE_BASE)      # bottom band
    draw.rectangle([(0, 0), (BAND - 1, H - 1)], fill=STONE_BASE)          # left band
    draw.rectangle([(W - BAND, 0), (W - 1, H - 1)], fill=STONE_BASE)      # right band

    # ── 2. Texture: darker veining lines ──
    draw_veining(draw, 0, 0, W, BAND)                   # top
    draw_veining(draw, 0, H - BAND, W, H)               # bottom
    draw_veining(draw, 0, 0, BAND, H)                   # left
    draw_veining(draw, W - BAND, 0, W, H)               # right

    # ── 3. Texture: chipped/eroded spots ──
    draw_chipped_chipping(draw, 0, 0, W, BAND)
    draw_chipped_chipping(draw, 0, H - BAND, W, H)
    draw_chipped_chipping(draw, 0, 0, BAND, H)
    draw_chipped_chipping(draw, W - BAND, 0, W, H)

    # ── 4. Fine grain texture along the stone band ──
    rng_fine = random.Random(99)
    for y in range(0, H):
        for x in range(0, W):
            # Skip the transparent interior
            if INNER_L <= x <= INNER_R and INNER_T <= y <= INNER_B:
                continue
            if rng_fine.random() < 0.025:
                var_g = rng_fine.randint(-8, 8)
                c = tuple(int(round(STONE_BASE[i] + var_g)) for i in range(3))
                c = tuple(max(STONE_SHADOW[i], min(STONE_LIGHT[i], c[i])) for i in range(3))
                draw.point((x, y), fill=c + (255,))

    # ── 5. Chipped inner lip (10px wide) — drawn before gold keyline ──
    draw.rectangle([(INNER_L - 10, INNER_T - 10), (INNER_R + 10, INNER_B + 10)],
                   outline=CHIPPED_INNER, width=10)

    # ── 6. Gold keyline (4px wide) — drawn LAST, nothing after it ──
    draw.rectangle([(INNER_L - 4, INNER_T - 4), (INNER_R + 4, INNER_B + 4)],
                   outline=GOLD_KEY, width=4)

    img.save(output_path, 'PNG')
    print(f"Generated border: {output_path} ({os.path.getsize(output_path)} bytes)")
    return img


def generate_stone_grain_texture(output_path, size=64):
    """Generate a small limestone-grain noise texture for the name-band background."""
    rng = random.Random(88)
    img = Image.new('RGB', (size, size), STONE_BASE[:3])
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
    """Slice the full border into 8 individual RGBA PNGs matching the 9-slice spec.
    
    Slices are cut at BAND (58px) from each edge:
      corners: 58x58
      edge_top / edge_bottom: 716x58
      edge_left / edge_right: 58x1100
    """
    os.makedirs(output_dir, exist_ok=True)

    slices = {
        'corner_tl': (0, 0, BAND, BAND),
        'corner_tr': (W - BAND, 0, W, BAND),
        'corner_bl': (0, H - BAND, BAND, H),
        'corner_br': (W - BAND, H - BAND, W, H),
        'edge_top': (BAND, 0, W - BAND, BAND),
        'edge_bottom': (BAND, H - BAND, W - BAND, H),
        'edge_left': (0, BAND, BAND, H - BAND),
        'edge_right': (W - BAND, BAND, W, H - BAND),
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
    """Generate 16x16 upscaled previews for visual verification — committed to repo."""
    os.makedirs(out_dir, exist_ok=True)

    corners = {
        'corner_tl': (0, 0, BAND, BAND),
        'corner_tr': (W - BAND, 0, W, BAND),
        'corner_bl': (0, H - BAND, BAND, H),
        'corner_br': (W - BAND, H - BAND, W, H),
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

    # Generate 16x16 upscaled 6x corner previews to artifacts/border_previews/ (committed)
    preview_dir = 'artifacts/border_previews'
    generate_downscale_previews(img, preview_dir)

    print("\nDone. Verify the previews in artifacts/border_previews/")
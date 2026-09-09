#!/usr/bin/env python3
"""
Generate Root-Bound Stone card border — Option 5: plain carved dark stone.
Dark charcoal/grey stone frame with irregular chipped inner lip and subtle grain.
No gold, no vines, no rune motifs, no plaque.

Output: full 832x1216 border PNG, slices to client/content/art/border/
"""
import math
import os
import random
import sys
from PIL import Image, ImageDraw

W, H = 832, 1216
# 7% band width per the 9slice spec
BAND = max(1, round(W * 0.07))  # 58px
INNER_W = W - 2 * BAND   # 716
INNER_H = H - 2 * BAND   # 1100

# ── Stone palette — NO gold, NO vines, NO motifs ──
# Base frame stone in RGB 40-60 range (dark charcoal)
STONE_BASE = (50, 47, 43)
STONE_DARK = (30, 28, 25)      # very dark charcoal for outer-shadow feel
STONE_MID = (55, 52, 48)       # mid-grey stone
STONE_LIGHT = (80, 76, 70)     # lighter stone for texture highlights
# Chipped inner lip: lighter stone edge (RGB 90-120) at the art-window seam
CHIPPED_INNER = (105, 100, 95)
CANVAS_COLOR = (35, 33, 30)    # base canvas (matches darkest frame shadow)


def draw_stone_texture(draw, x0, y0, x1, y1):
    """Add subtle stone grain marks to the border area — short fine scratches."""
    rng = random.Random(42)
    for y in range(y0, y1, 2):
        for x in range(x0, x1, 3):
            if rng.random() < 0.04:
                b = rng.randint(-4, 4)
                c = STONE_MID[0] + b
                c = max(25, min(80, c))
                draw.point((x + rng.randint(-1, 1), y), fill=(c, c - 2, c - 4))
    # Occasional longer scratch lines
    rng2 = random.Random(73)
    for _ in range(max(1, (x1 - x0) * (y1 - y0) // 8000)):
        sx = rng2.randint(x0, x1 - 8)
        sy = rng2.randint(y0, y1 - 2)
        length = rng2.randint(4, 12)
        sc = rng2.randint(40, 70)
        for lx in range(length):
            ly = rng2.randint(-1, 1)
            px = sx + lx
            py = sy + ly
            if x0 <= px < x1 and y0 <= py < y1:
                draw.point((px, py), fill=(sc, sc - 2, sc - 4))


def generate_border(output_path):
    img = Image.new('RGB', (W, H), CANVAS_COLOR)
    draw = ImageDraw.Draw(img)
    rng_edge = random.Random(137)

    # ── 1. Base stone frame ──
    # Fill the full border band with mid-dark stone
    # Top band
    draw.rectangle([(0, 0), (W - 1, BAND - 1)], fill=STONE_MID)
    # Bottom band
    draw.rectangle([(0, H - BAND), (W - 1, H - 1)], fill=STONE_MID)
    # Left band
    draw.rectangle([(0, 0), (BAND - 1, H - 1)], fill=STONE_MID)
    # Right band
    draw.rectangle([(W - BAND, 0), (W - 1, H - 1)], fill=STONE_MID)

    # Darken the four corner squares slightly for depth
    # (they are already filled by the band rects; this is a subtle overlay)
    for cx, cy in [(BAND // 2, BAND // 2),
                    (W - BAND // 2, BAND // 2),
                    (BAND // 2, H - BAND // 2),
                    (W - BAND // 2, H - BAND // 2)]:
        for dy in range(-BAND // 2, BAND // 2):
            for dx in range(-BAND // 2, BAND // 2):
                dist = math.sqrt(dx * dx + dy * dy)
                if dist < BAND * 0.35:
                    factor = 1 - (dist / (BAND * 0.35)) * 0.15
                    px = cx + dx
                    py = cy + dy
                    if 0 <= px < W and 0 <= py < H:
                        orig = img.getpixel((px, py))
                        darkened = tuple(int(c * factor) for c in orig)
                        draw.point((px, py), fill=darkened)

    # ── 2. Chipped inner lip (1-2px at source scale) ──
    # The inner lip runs along the art-window boundary (148, 172, 675, 1019).
    # Draw it as a slightly irregular line with a chipped / broken appearance:
    # some pixels present, some missing, 1-2px thickness, color CHIPPED_INNER.
    inner_l = 148
    inner_t = 172
    inner_r = 675
    inner_b = 1019

    # Helper: draw one pixel of the lip with jitter perpendicular to the edge
    def lip_segment(x, y, is_horizontal, flip_jitter=False):
        """Draw 1-2px of chipped lip at (x,y) with ±1px perpendicular jitter."""
        if is_horizontal:
            jy = rng_edge.randint(-1, 1)
            if flip_jitter:
                jy = -jy
            for thick in range(2):
                py = y + jy + thick
                if 0 <= py < H:
                    draw.point((x, py), fill=CHIPPED_INNER)
                    # Slight colour variation
                    if rng_edge.random() < 0.2:
                        var = rng_edge.randint(-8, 8)
                        dv = (var, var - 2, var - 4)
                        alt = tuple(max(50, min(130, CHIPPED_INNER[i] + dv[i])) for i in range(3))
                        draw.point((x, py), fill=alt)
        else:
            jx = rng_edge.randint(-1, 1)
            if flip_jitter:
                jx = -jx
            for thick in range(2):
                px = x + jx + thick
                if 0 <= px < W:
                    draw.point((px, y), fill=CHIPPED_INNER)
                    if rng_edge.random() < 0.2:
                        var = rng_edge.randint(-8, 8)
                        dv = (var, var - 2, var - 4)
                        alt = tuple(max(50, min(130, CHIPPED_INNER[i] + dv[i])) for i in range(3))
                        draw.point((px, y), fill=alt)

    # Top edge: inner_l → inner_r at y=inner_t
    for ex in range(inner_l, inner_r):
        if rng_edge.random() < 0.12:
            continue  # chip missing
        lip_segment(ex, inner_t, is_horizontal=True, flip_jitter=False)
        # Second pass for the 2nd pixel width (the lip is 2px deep)
        # Offset perpendicular outward (into border area) = y+2
        if ex % 3 == 0:  # every 3rd pixel gets a 2nd row
            lip_segment(ex, inner_t + 2, is_horizontal=True, flip_jitter=True)

    # Bottom edge: inner_l → inner_r at y=inner_b
    for ex in range(inner_l, inner_r):
        if rng_edge.random() < 0.12:
            continue
        lip_segment(ex, inner_b, is_horizontal=True, flip_jitter=False)
        if ex % 3 == 0:
            lip_segment(ex, inner_b - 2, is_horizontal=True, flip_jitter=True)

    # Left edge: inner_t → inner_b at x=inner_l
    for ey in range(inner_t, inner_b):
        if rng_edge.random() < 0.12:
            continue
        lip_segment(inner_l, ey, is_horizontal=False, flip_jitter=False)
        if ey % 3 == 0:
            lip_segment(inner_l + 2, ey, is_horizontal=False, flip_jitter=True)

    # Right edge: inner_t → inner_b at x=inner_r
    for ey in range(inner_t, inner_b):
        if rng_edge.random() < 0.12:
            continue
        lip_segment(inner_r, ey, is_horizontal=False, flip_jitter=False)
        if ey % 3 == 0:
            lip_segment(inner_r - 2, ey, is_horizontal=False, flip_jitter=True)

    # ── 3. Stone grain texture over the frame ──
    draw_stone_texture(draw, 0, 0, W, BAND)       # top band
    draw_stone_texture(draw, 0, H - BAND, W, H)   # bottom band
    draw_stone_texture(draw, 0, 0, BAND, H)       # left band
    draw_stone_texture(draw, W - BAND, 0, W, H)   # right band

    # Additional fine grain along the inner edge of the frame
    for y in range(inner_t - 3, inner_t + BAND):
        for x in range(inner_l, inner_r):
            if rng_edge.random() < 0.015:
                shade = rng_edge.randint(35, 75)
                draw.point((x, y), fill=(shade, shade - 2, shade - 4))

    # ── 4. Fill the center (art window) solid black ──
    draw.rectangle([(inner_l, inner_t), (inner_r - 1, inner_b - 1)], fill=(0, 0, 0))

    img.save(output_path, 'PNG')
    print(f"Generated border: {output_path} ({os.path.getsize(output_path)} bytes)")
    return img


def generate_stone_grain_texture(output_path, size=64):
    """Generate a small stone-grain noise texture for the name-band background."""
    rng = random.Random(88)
    img = Image.new('RGB', (size, size), (50, 47, 43))
    draw = ImageDraw.Draw(img)
    for y in range(size):
        for x in range(size):
            if rng.random() < 0.06:
                b = rng.randint(-6, 6)
                c = 50 + b
                c = max(30, min(80, c))
                draw.point((x, y), fill=(c, c - 2, c - 4))
    # A few scratch lines
    for _ in range(8):
        sx = rng.randint(0, size - 10)
        sy = rng.randint(0, size - 2)
        for lx in range(rng.randint(4, 14)):
            ly = rng.randint(-1, 1)
            px = sx + lx
            py = sy + ly
            if 0 <= px < size and 0 <= py < size:
                sc = rng.randint(60, 85)
                draw.point((px, py), fill=(sc, sc - 2, sc - 4))
    img.save(output_path, 'PNG')
    print(f"Generated stone grain texture: {output_path}")


def slice_border(img, output_dir):
    """Slice the full border into 8 individual PNGs matching the 9-slice spec."""
    os.makedirs(output_dir, exist_ok=True)

    # Exact same window coordinates as the original
    window_left = 148
    window_top = 172
    window_right = 675
    window_bottom = 1019

    slices = {
        'corner_tl': (0, 0, window_left, window_top),
        'corner_tr': (window_right, 0, W, window_top),
        'corner_bl': (0, window_bottom, window_left, H),
        'corner_br': (window_right, window_bottom, W, H),
        'edge_top': (window_left, 0, window_right, window_top),
        'edge_bottom': (window_left, window_bottom, window_right, H),
        'edge_left': (0, window_top, window_left, window_bottom),
        'edge_right': (window_right, window_top, W, window_bottom),
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
    """Generate 16px and 24px downscale previews for the taste check."""
    os.makedirs(out_dir, exist_ok=True)

    window_left, window_top = 148, 172
    window_right, window_bottom = 675, 1019

    corners = {
        'corner_tl': (0, 0, window_left, window_top),
        'corner_tr': (window_right, 0, W, window_top),
        'corner_bl': (0, window_bottom, window_left, H),
        'corner_br': (window_right, window_bottom, W, H),
    }

    for sz in [16, 24]:
        for name, box in corners.items():
            c = img.crop(box)
            c_scaled = c.resize((sz, sz), Image.LANCZOS)
            outpath = os.path.join(out_dir, f'{name}_{sz}px.png')
            c_scaled.save(outpath, 'PNG')
            print(f"Preview: {outpath}")

        # Edge strips
        edge = img.crop((window_left, 0, window_right, window_top))
        edge_scaled = edge.resize((200, sz), Image.LANCZOS)
        edge_scaled.save(os.path.join(out_dir, f'edge_top_{sz}px.png'), 'PNG')

    print(f"All previews in {out_dir}")


if __name__ == '__main__':
    output_path = '/tmp/gen_rootbound_border.png'
    img = generate_border(output_path)

    # Slice to the game's border directory
    border_dir = 'client/content/art/border'
    slice_border(img, border_dir)

    # Generate stone grain texture for the name-band background
    grain_path = os.path.join(border_dir, 'stone_grain.png')
    generate_stone_grain_texture(grain_path)

    # Generate 16px/24px previews in /tmp/
    preview_dir = '/tmp/border_previews'
    generate_downscale_previews(img, preview_dir)

    print("\nDone. Verify the previews in /tmp/border_previews/")
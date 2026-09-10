#!/usr/bin/env python3
"""
Generate Root-Bound Stone card border — Pale weathered limestone
with visible streaked texture and gold keyline, designed to READ at
render size (~16px band on a 233px hand card).

KEY DESIGN CHOICE: Coarse features beat fine grain. At ~3.6x downscale
(58px source to 16px render), fine 1px detail vanishes into flat tan.
This generator uses large noise patches (3-8px blocks), wide streaks,
and bold value separation so the texture survives the downscale.

Output: full 832x1216 RGBA border PNG, 8 RGBA slice PNGs.
"""
import os
import random
from PIL import Image, ImageDraw, ImageStat

W, H = 832, 1216
BAND = max(1, round(W * 0.07))  # 58

INNER_L = BAND
INNER_T = BAND
INNER_R = W - BAND - 1
INNER_B = H - BAND - 1

# RGBA palette (all include alpha=255 explicitly)
def rgba(r, g, b): return (r, g, b, 255)

STONE_BASE     = rgba(165, 155, 125)
STONE_DARK     = rgba(125, 117,  93)
STONE_MID      = rgba(148, 139, 112)
STONE_LIGHT    = rgba(192, 182, 152)
STONE_SHADOW   = rgba(105,  98,  80)

GOLD_KEY       = rgba(171, 148,  82)
GOLD_HIGHLIGHT = rgba(190, 168, 100)
GOLD_SHADOW    = rgba(142, 120,  60)

CHIPPED_INNER  = rgba(178, 168, 136)


def render_stone_streaks(draw, x0, y0, x1, y1, rng_seed=42):
    """Vertical streaking typical of weathered limestone — coarse bands readable at 16px."""
    rng = random.Random(rng_seed)
    width, height = x1 - x0, y1 - y0
    if width <= 0 or height <= 0:
        return

    col_width = rng.randint(10, 18)
    cols = max(1, width // col_width)

    for ci in range(cols):
        cx0 = x0 + ci * col_width
        cx1 = min(x0 + (ci + 1) * col_width, x1)
        if cx1 - cx0 <= 0:
            continue

        shade = rng.choice([STONE_BASE, STONE_MID, STONE_DARK])
        draw.rectangle([(cx0, y0), (cx1 - 1, y1 - 1)], fill=shade)

        for _ in range(rng.randint(1, 3)):
            sy = rng.randint(y0 + 2, y1 - 4)
            sh = rng.randint(2, 4)
            sc = rng.choice([STONE_LIGHT, STONE_SHADOW, STONE_DARK])
            draw.rectangle([(cx0, sy), (cx1 - 1, sy + sh - 1)], fill=sc)

    for _ in range(rng.randint(2, 5)):
        vx = rng.randint(x0 + 2, x1 - 4)
        vlen = y1 - y0 - 1
        if vlen <= 0:
            continue
        for dy in range(vlen):
            ox = rng.randint(-1, 1)
            px = vx + ox
            if x0 <= px < x1:
                f = 1.0 - (dy / max(vlen, 1)) * 0.4
                r = int(STONE_LIGHT[0] * f + STONE_BASE[0] * (1 - f))
                g = int(STONE_LIGHT[1] * f + STONE_BASE[1] * (1 - f))
                b = int(STONE_LIGHT[2] * f + STONE_BASE[2] * (1 - f))
                draw.point((px, y0 + dy), fill=(r, g, b, 255))


def render_keyline(draw, x0, y0, x1, y1):
    """12px gold keyline so ~3px survives at 16px render size."""
    for w in range(9, 12):
        draw.rectangle([(x0 - w, y0 - w), (x1 + w, y1 + w)], outline=GOLD_SHADOW, width=1)
    draw.rectangle([(x0 - 7, y0 - 7), (x1 + 7, y1 + 7)], outline=GOLD_KEY, width=4)
    draw.rectangle([(x0 - 1, y0 - 1), (x1 + 1, y1 + 1)], outline=GOLD_HIGHLIGHT, width=1)


def render_coarse_grain(draw, x0, y0, x1, y1, rng_seed=99):
    """3-4px grain blocks that survive as stone pitting at render size."""
    rng = random.Random(rng_seed)
    for py in range(y0, y1, 2):
        for px in range(x0, x1, 2):
            if INNER_L - 8 <= px <= INNER_R + 8 and INNER_T - 8 <= py <= INNER_B + 8:
                continue
            if rng.random() < 0.08:
                sz = rng.randint(2, 4)
                br = rng.randint(-25, 15)
                col = rgba(
                    max(80, min(200, STONE_BASE[0] + br)),
                    max(72, min(190, STONE_BASE[1] + br)),
                    max(55, min(160, STONE_BASE[2] + br))
                )
                draw.rectangle([(px, py), (min(px + sz, x1 - 1), min(py + sz, y1 - 1))], fill=col)


def generate_border(output_path):
    img = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # 1. Base stone bands
    for rect in [(0, 0, W, BAND), (0, H - BAND, W, H),
                 (0, 0, BAND, H), (W - BAND, 0, W, H)]:
        draw.rectangle([(rect[0], rect[1]), (rect[2] - 1, rect[3] - 1)], fill=STONE_BASE)

    # 2. Stone streaks
    render_stone_streaks(draw, 0, 0, W, BAND, rng_seed=42)
    render_stone_streaks(draw, 0, H - BAND, W, H, rng_seed=43)
    render_stone_streaks(draw, 0, 0, BAND, H, rng_seed=44)
    render_stone_streaks(draw, W - BAND, 0, W, H, rng_seed=45)

    # 3. Coarse grain blocks
    render_coarse_grain(draw, 0, 0, W, BAND, rng_seed=99)
    render_coarse_grain(draw, 0, H - BAND, W, H, rng_seed=100)
    render_coarse_grain(draw, 0, 0, BAND, H, rng_seed=101)
    render_coarse_grain(draw, W - BAND, 0, W, H, rng_seed=102)

    # 4. Chipped inner lip
    draw.rectangle([(INNER_L - 8, INNER_T - 8), (INNER_R + 8, INNER_B + 8)],
                   outline=CHIPPED_INNER, width=8)

    # 5. Gold keyline
    render_keyline(draw, INNER_L, INNER_T, INNER_R, INNER_B)

    img.save(output_path, 'PNG')
    print(f"Generated border: {output_path} ({os.path.getsize(output_path)} bytes)")
    return img


def generate_stone_grain_texture(output_path, size=64):
    rng = random.Random(88)
    img = Image.new('RGB', (size, size), STONE_BASE[:3])
    draw = ImageDraw.Draw(img)
    for y in range(0, size, 2):
        for x in range(0, size, 2):
            if rng.random() < 0.12:
                b = rng.randint(-25, 20)
                col = tuple(
                    max(STONE_SHADOW[i], min(STONE_LIGHT[i], STONE_BASE[i] + b))
                    for i in range(3)
                )
                w = rng.randint(2, 4)
                draw.rectangle([(x, y), (min(x + w, size - 1), min(y + w, size - 1))], fill=col)
    for _ in range(8):
        sx = rng.randint(0, size - 8)
        sy = rng.randint(0, size - 2)
        for lx in range(rng.randint(4, 12)):
            ly = rng.randint(-1, 1)
            px, py = sx + lx, sy + ly
            if 0 <= px < size and 0 <= py < size:
                sc = tuple(max(STONE_SHADOW[i], STONE_BASE[i] - 22) for i in range(3))
                draw.point((px, py), fill=sc)
    img.save(output_path, 'PNG')
    print(f"Generated stone grain: {output_path}")


def slice_border(img, output_dir):
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
        img.crop(box).save(outpath, 'PNG')
        print(f"Saved: {outpath}")
    img.save(os.path.join(output_dir, 'rootbound_full.png'), 'PNG')

    # Colour analysis
    for name, box in slices.items():
        crop = img.crop(box)
        colors = crop.getcolors(maxcolors=100000)
        stat = ImageStat.Stat(crop)
        print(f"  {name}: {len(colors or [])} colours, size {crop.size}, "
              f"mean RGB=({stat.mean[0]:.0f},{stat.mean[1]:.0f},{stat.mean[2]:.0f})")


def generate_previews(img, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    for name, box in [('corner_tl', (0, 0, BAND, BAND)),
                      ('corner_tr', (W - BAND, 0, W, BAND)),
                      ('corner_bl', (0, H - BAND, BAND, H)),
                      ('corner_br', (W - BAND, H - BAND, W, H))]:
        c = img.crop(box)
        c16 = c.resize((16, 16), Image.LANCZOS)
        c16.save(os.path.join(out_dir, f'{name}_16px_raw.png'))
        c16.resize((96, 96), Image.NEAREST).save(os.path.join(out_dir, f'{name}_16px_6x.png'))
        print(f"Preview: {name}")


if __name__ == '__main__':
    img = generate_border('/tmp/gen_rootbound_border.png')
    slice_border(img, 'client/content/art/border')
    generate_stone_grain_texture(os.path.join('client/content/art/border', 'stone_grain.png'))
    generate_previews(img, 'artifacts/border_previews')
    print("\nDone.")
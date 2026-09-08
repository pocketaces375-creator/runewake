#!/usr/bin/env python3
"""
Generate Root-Bound Stone card border — built for readability at 15-25px band width.
Programmatic design with:
  - Dark charcoal stone frame
  - Clean continuous bright gold inlay line along inner edge (15px wide)
  - Simplified bold stylized vine/leaf silhouette (not painterly)
  - One bold angular rune motif per corner
Output: full 832x1216 border PNG, slices to client/content/art/border/
"""
import math
import os
import sys
from PIL import Image, ImageDraw

W, H = 832, 1216
# 7% band width per the 9slice spec
BAND = max(1, round(W * 0.07))  # 58px
INNER_W = W - 2 * BAND   # 716
INNER_H = H - 2 * BAND   # 1100
GOLD_LINE_W = 15
VINE_COLOR = (22, 50, 28)     # dark green
GOLD_COLOR = (230, 190, 50)   # bright gold
STONE_DARK = (30, 28, 25)     # dark charcoal
STONE_MID = (55, 52, 48)      # mid stone
STONE_LIGHT = (80, 76, 70)    # lighter stone for texture
CANVAS_COLOR = (35, 33, 30)   # base canvas

def draw_vine_pattern(draw, x0, y0, x1, y1, thickness=4):
    """Draw a simplified bold vine with broad leaves along a vertical or horizontal edge band."""
    is_horizontal = (y1 - y0) < (x1 - x0)
    band_center = (y0 + y1) // 2 if is_horizontal else (x0 + x1) // 2
    half_band = (y1 - y0) // 4 if is_horizontal else (x1 - x0) // 4
    
    if is_horizontal:
        # Horizontal vine runs centered in the band
        cy = band_center - 8
        step = 40
        for tx in range(x0, x1, step):
            draw.line([(tx, cy), (tx+step//2, cy-12), (tx+step, cy)], fill=VINE_COLOR, width=thickness)
            # Broad leaf at each node
            draw.ellipse([(tx-10, cy-18), (tx+10, cy-2)], fill=VINE_COLOR if tx % 80 < 60 else STONE_LIGHT)
            draw.ellipse([(tx-8, cy+2), (tx+12, cy+18)], fill=VINE_COLOR)
        # Second vine strand slightly offset
        cy2 = band_center + 12
        step2 = 50
        for tx in range(x0, x1, step2):
            draw.line([(tx, cy2), (tx+step2//3, cy2-8), (tx+step2, cy2)], fill=VINE_COLOR, width=thickness-1)
            draw.ellipse([(tx-8, cy2-18), (tx+8, cy2+2)], fill=VINE_COLOR)
    else:
        # Vertical vine runs centered in the band
        cx = band_center - 8
        step = 45
        for ty in range(y0, y1, step):
            draw.line([(cx, ty), (cx-12, ty+step//2), (cx, ty+step)], fill=VINE_COLOR, width=thickness)
            # Broad leaf
            draw.ellipse([(cx-18, ty-10), (cx-2, ty+10)], fill=VINE_COLOR)
            draw.ellipse([(cx+2, ty-8), (cx+18, ty+12)], fill=VINE_COLOR)
        # Second strand
        cx2 = band_center + 12
        step2 = 55
        for ty in range(y0, y1, step2):
            draw.line([(cx2, ty), (cx2-10, ty+step2//3), (cx2, ty+step2)], fill=VINE_COLOR, width=thickness-1)
            draw.ellipse([(cx2-18, ty-8), (cx2, ty+8)], fill=VINE_COLOR)


def draw_corner_rune(draw, cx, cy, style=0, size=28):
    """Draw a bold angular rune motif at corner position cx,cy."""
    half = size // 2
    pts = half  # inset from center
    if style == 0:  # Diamond-chevron (TL)
        draw.polygon([(cx, cy-pts), (cx+pts, cy), (cx, cy+pts), (cx-pts, cy)], fill=GOLD_COLOR)
        draw.polygon([(cx, cy-pts+5), (cx+pts-5, cy), (cx, cy+pts-5), (cx-pts+5, cy)], fill=STONE_DARK)
        # Inner dot
        draw.ellipse([(cx-4, cy-4), (cx+4, cy+4)], fill=GOLD_COLOR)
    elif style == 1:  # Angular sun-burst (TR)
        draw.polygon([(cx, cy-pts-4), (cx+pts+4, cy-pts), (cx+pts+6, cy), (cx+pts+4, cy+pts), (cx, cy+pts+4),
                       (cx-pts-4, cy+pts), (cx-pts-6, cy), (cx-pts-4, cy-pts)], fill=GOLD_COLOR)
        draw.ellipse([(cx-6, cy-6), (cx+6, cy+6)], fill=STONE_DARK)
        draw.ellipse([(cx-3, cy-3), (cx+3, cy+3)], fill=GOLD_COLOR)
    elif style == 2:  # Vertical zigzag (BL)
        draw.polygon([(cx-pts-2, cy+pts+4), (cx-pts, cy-4), (cx-2, cy+4), (cx+2, cy-4), (cx+pts, cy-4), (cx+pts+2, cy+pts+4)],
                     fill=GOLD_COLOR)
        # Two horizontal bars
        draw.rectangle([(cx-pts-4, cy-pts+2), (cx+pts+4, cy-pts+6)], fill=GOLD_COLOR)
        draw.rectangle([(cx-pts-4, cy-pts+12), (cx+pts+4, cy-pts+16)], fill=GOLD_COLOR)
    elif style == 3:  # Circle-triangle (BR)
        draw.ellipse([(cx-pts, cy-pts), (cx+pts, cy+pts)], outline=GOLD_COLOR, width=4)
        draw.ellipse([(cx-pts+8, cy-pts+8), (cx+pts-8, cy+pts-8)], fill=GOLD_COLOR)
        draw.polygon([(cx, cy-pts-8), (cx+pts+6, cy+pts), (cx-pts-6, cy+pts)], fill=GOLD_COLOR)


def draw_stone_texture(draw, x0, y0, x1, y1):
    """Add subtle stone grain marks to the border area."""
    import random
    # Seed for reproducibility
    rng = random.Random(42)
    for _ in range(y1 - y0):
        y = y0 + _ * 4
        for sx in range(x0, x1, 3):
            if rng.random() < 0.03:
                b = rng.randint(-5, 5)
                c = STONE_LIGHT[0] + b
                c = max(20, min(100, c))
                draw.point((sx + rng.randint(-1, 1), y), fill=(c, c-3, c-5))


def generate_border(output_path):
    img = Image.new('RGB', (W, H), CANVAS_COLOR)
    draw = ImageDraw.Draw(img)
    
    # 1. Base stone frame - fill the entire border band area
    # Top band
    draw.rectangle([(0, 0), (W-1, BAND-1)], fill=STONE_MID)
    # Bottom band
    draw.rectangle([(0, H-BAND), (W-1, H-1)], fill=STONE_MID)
    # Left band
    draw.rectangle([(0, 0), (BAND-1, H-1)], fill=STONE_MID)
    # Right band
    draw.rectangle([(W-BAND, 0), (W-1, H-1)], fill=STONE_MID)
    
    # Darken corners slightly
    # TL
    draw.rectangle([(0, 0), (BAND-1, BAND-1)], fill=STONE_MID)
    # TR
    draw.rectangle([(W-BAND, 0), (W-1, BAND-1)], fill=STONE_MID)
    # BL
    draw.rectangle([(0, H-BAND), (BAND-1, H-1)], fill=STONE_MID)
    # BR
    draw.rectangle([(W-BAND, H-BAND), (W-1, H-1)], fill=STONE_MID)
    
    # Add stone texture
    draw_stone_texture(draw, 0, 0, BAND, H)
    draw_stone_texture(draw, W-BAND, 0, W, H)
    for x in range(BAND, W-BAND):
        draw.point((x, 0), fill=STONE_DARK if (x % 11 == 0) else STONE_MID)
        draw.point((x, BAND-1), fill=STONE_DARK if (x % 13 == 0) else STONE_MID)
        draw.point((x, H-BAND), fill=STONE_DARK if (x % 7 == 0) else STONE_MID)
        draw.point((x, H-1), fill=STONE_DARK if (x % 17 == 0) else STONE_MID)
    
    # 2. Gold inlay line along the inner edge (continuous, clean)
    # Inner edge rect = the rectangle where art window begins
    inner_x = BAND
    inner_y = BAND
    inner_x2 = W - BAND
    inner_y2 = H - BAND
    
    # Gold line on top inner edge
    draw.rectangle([(inner_x, inner_y-GOLD_LINE_W//2), (inner_x2-1, inner_y+GOLD_LINE_W//2)], fill=GOLD_COLOR)
    # Bottom inner edge
    draw.rectangle([(inner_x, inner_y2-GOLD_LINE_W//2-1), (inner_x2-1, inner_y2+GOLD_LINE_W//2)], fill=GOLD_COLOR)
    # Left inner edge
    draw.rectangle([(inner_x-GOLD_LINE_W//2, inner_y), (inner_x+GOLD_LINE_W//2, inner_y2-1)], fill=GOLD_COLOR)
    # Right inner edge
    draw.rectangle([(inner_x2-GOLD_LINE_W//2-1, inner_y), (inner_x2+GOLD_LINE_W//2, inner_y2-1)], fill=GOLD_COLOR)
    
    # Gold corner squares where gold lines meet
    gold_corner_size = GOLD_LINE_W + 4
    draw.rectangle([(inner_x-gold_corner_size//2, inner_y-gold_corner_size//2),
                    (inner_x+gold_corner_size//2, inner_y+gold_corner_size//2)], fill=GOLD_COLOR)
    draw.rectangle([(inner_x2-gold_corner_size//2, inner_y-gold_corner_size//2),
                    (inner_x2+gold_corner_size//2, inner_y+gold_corner_size//2)], fill=GOLD_COLOR)
    draw.rectangle([(inner_x-gold_corner_size//2, inner_y2-gold_corner_size//2),
                    (inner_x+gold_corner_size//2, inner_y2+gold_corner_size//2)], fill=GOLD_COLOR)
    draw.rectangle([(inner_x2-gold_corner_size//2, inner_y2-gold_corner_size//2),
                    (inner_x2+gold_corner_size//2, inner_y2+gold_corner_size//2)], fill=GOLD_COLOR)
    
    # 3. Simplified vine/leaf silhouette on the border
    # Vertical veins on left band, between gold line and outer edge
    vine_left_x = BAND // 2 - 10
    draw_vine_pattern(draw, 6, BAND+GOLD_LINE_W, BAND-6, H-BAND-GOLD_LINE_W)
    
    # Right band
    draw_vine_pattern(draw, W-BAND+6, BAND+GOLD_LINE_W, W-6, H-BAND-GOLD_LINE_W)
    
    # Top band - horizontal vine
    draw_vine_pattern(draw, BAND+GOLD_LINE_W, 6, W-BAND-GOLD_LINE_W, BAND-6)
    
    # Bottom band - horizontal vine
    draw_vine_pattern(draw, BAND+GOLD_LINE_W, H-BAND+6, W-BAND-GOLD_LINE_W, H-6)
    
    # 4. Bold corner runes
    # TL corner - rune positioned in the corner area, inset
    cr_size = 32
    draw_corner_rune(draw, BAND//2, BAND//2, style=0, size=cr_size)
    draw_corner_rune(draw, W-BAND//2, BAND//2, style=1, size=cr_size)
    draw_corner_rune(draw, BAND//2, H-BAND//2, style=2, size=cr_size)
    draw_corner_rune(draw, W-BAND//2, H-BAND//2, style=3, size=cr_size)
    
    # 5. Fill the center window solid black
    draw.rectangle([(inner_x, inner_y), (inner_x2-1, inner_y2-1)], fill=(0, 0, 0))
    
    img.save(output_path, 'PNG')
    print(f"Generated border: {output_path} ({os.path.getsize(output_path)} bytes)")
    return img


def slice_border(img, output_dir):
    """Slice the full border into 8 individual PNGs matching the 9-slice spec."""
    os.makedirs(output_dir, exist_ok=True)
    
    # Corner sizes vary slightly in the original assets
    # TL=148x172, TR=157x172, BL=148x197, BR=157x197
    # Our BAND=58, so corners at full source are 58px square on 832w card
    # But the actual source has wider corners due to the window position
    # Let's use the same window as the original: (148, 172, 675, 1019)
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
    
    # Also generate a name-band crop showing the gradient effect
    if CARD_PLATE_FAKE:
        # Simulate what the name band will look like
        # The name band sits at bottom of card, ~7% height
        band_h = int(H * 0.07)
        name_band_crop = img.crop((0, H - band_h - 120, W, H - 60))
        name_band_crop.save(os.path.join(out_dir, 'name_band_reference.png'), 'PNG')

    print(f"All previews in {out_dir}")


CARD_PLATE_FAKE = False  # Set True to also generate a fake name-band preview


if __name__ == '__main__':
    if len(sys.argv) > 1 and sys.argv[1] == '--card-plate-fake':
        CARD_PLATE_FAKE = True
    
    output_path = '/tmp/gen_rootbound_border.png'
    img = generate_border(output_path)
    
    # Slice to the game's border directory
    border_dir = 'client/content/art/border'
    slice_border(img, border_dir)
    
    # Generate 16px/24px previews in /tmp/
    preview_dir = '/tmp/border_previews'
    generate_downscale_previews(img, preview_dir)
    
    print("\nDone. Verify the previews in /tmp/border_previews/")
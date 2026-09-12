#!/usr/bin/env python3
"""
tools/compose_frame_options.py — drop real card art into AI-generated border
frames and produce a comparison sheet.

Uses 9-slice reconstruction (nineslice.py) so band thickness is controlled
independently of the generated image. All frames get the same band width.
Any frame whose window cannot be detected is rejected entirely.

Usage:  python3 tools/compose_frame_options.py
"""
from __future__ import annotations

import glob
import os
import sys

from PIL import Image, ImageDraw, ImageFont

from nineslice import nineslice_frame, BAND_FRAC

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ART = os.path.join(ROOT, "client", "content", "art")
CINZEL = os.path.join(ROOT, "client", "assets", "fonts", "Cinzel.ttf")
WORK = os.path.join(ROOT, "pipeline", "work", "card_frames")

CARDS = [
    ("vrd_r_bloomweaver", "Bloomweaver", 5, 4, 4),
    ("emb_c_ember_hound", "Ember Hound", 1, 2, 1),
]


def fit_cover(img, size, bias=0.28):
    w, h = size
    sw, sh = img.size
    sc = max(w / sw, h / sh)
    img = img.resize((int(sw * sc) + 1, int(sh * sc) + 1), Image.LANCZOS)
    x = (img.width - w) // 2
    y = max(0, int((img.height - h) * bias))
    return img.crop((x, y, x + w, y + h))


def cinzel(sz):
    return ImageFont.truetype(CINZEL, sz)


def compose(frame_path, card_id, name, cost, atk, hp, out_size=(440, 643)):
    """Build a card mockup at out_size using 9-slice border reconstruction."""
    bp = int(out_size[0] * BAND_FRAC)
    result = nineslice_frame(frame_path, out_size, band_px=bp)
    if result is None:
        print(f"  REJECT: {frame_path} — window not found", file=sys.stderr)
        return None
    border, (x0, y0, x1, y1) = result
    W, H = out_size
    aw, ah = x1 - x0, y1 - y0

    # Report the art window coverage
    print(f"  {name}: window {aw*100//W}%x{ah*100//H}% — OK")

    # Art layer
    art = fit_cover(
        Image.open(os.path.join(ART, card_id + ".webp")).convert("RGB"),
        (aw, ah),
    )
    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    canvas.paste(art, (x0, y0))
    canvas.alpha_composite(border)

    d = ImageDraw.Draw(canvas, "RGBA")

    # Cost disc top-right (on the frame border)
    r = 52
    ccx, ccy = W - 96, 100
    d.ellipse([ccx - r, ccy - r, ccx + r, ccy + r],
              fill=(30, 25, 18), outline=(232, 205, 120), width=6)
    d.text((ccx, ccy + 2), str(cost), font=cinzel(58),
           fill=(232, 205, 120), anchor="mm")

    # Two bands: parchment name band + dark stat strip
    ph_total = int(ah * 0.17)
    py = y1 - ph_total

    # —— Name band (tight to text) ——
    name_text = name.upper()
    fs = 46
    font = cinzel(fs)
    bb = d.textbbox((0, 0), name_text, font=font)
    while (bb[2] - bb[0]) > (aw - 24) and fs > 20:
        fs -= 1
        font = cinzel(fs)
        bb = d.textbbox((0, 0), name_text, font=font)
    text_h = bb[3] - bb[1]
    name_h = text_h + 16                               # 8px margin top + 8px bottom
    name_by = py
    stat_sy = py + name_h

    # Parchment band
    d.rectangle([x0, name_by, x1, name_by + name_h], fill=(200, 184, 152))
    # Dark rules top and bottom (3px, colour 40,34,26)
    rule_col = (40, 34, 26)
    d.rectangle([x0, name_by, x1, name_by + 3], fill=rule_col)
    d.rectangle([x0, name_by + name_h - 3, x1, name_by + name_h], fill=rule_col)

    # Name (centred in the tight band)
    d.text(((x0 + x1) // 2, name_by + name_h // 2), name_text, font=font,
           fill=(58, 40, 22), anchor="mm")

    # —— Stat strip (dark) ——
    d.rectangle([x0, stat_sy, x1, y1], fill=(32, 30, 26))

    # Stat chips — pale fill, coloured border, coloured numeral
    strip_cx = stat_sy + (y1 - stat_sy) // 2
    for sx, (fill_c, border_c, txt_c), val in [
        (x0 + 78, ((214, 201, 176), (150, 45, 38), (150, 45, 38)), atk),
        (x1 - 78, ((214, 201, 176), (58, 105, 58), (58, 105, 58)), hp)]:
        d.rounded_rectangle([sx - 56, strip_cx - 32, sx + 56, strip_cx + 32],
                            radius=14, fill=fill_c,
                            outline=border_c, width=3)
        d.text((sx, strip_cx), str(val), font=cinzel(46),
               fill=txt_c, anchor="mm")

    return canvas


def main():
    frames = sorted(glob.glob(os.path.join(WORK, "frame_[0-9]*.png")))
    if not frames:
        print(f"no frames in {WORK}", file=sys.stderr)
        sys.exit(1)

    cols = []
    for fp in frames:
        big = compose(fp, *CARDS[0], out_size=(832, 1216))
        if big is None:
            continue
        big.save(fp.replace("frame_", "preview_"))
        col = [big.resize((440, 643), Image.LANCZOS)]
        small = compose(fp, *CARDS[1], out_size=(300, 438))
        if small is not None:
            col.append(small)
        else:
            # fallback: rescale the big one
            col.append(big.resize((300, 438), Image.LANCZOS))
        cols.append((os.path.basename(fp), col))

    if not cols:
        print("no usable frames", file=sys.stderr)
        sys.exit(1)

    CW, PAD, TOP = 520, 40, 120
    sheet = Image.new("RGB", (PAD + len(cols) * CW, 1330), (14, 12, 10))
    d = ImageDraw.Draw(sheet)
    d.text((sheet.width // 2, 50), "BORDER OPTIONS — 9-SLICE RECONSTRUCTED",
           font=cinzel(48), fill=(212, 184, 76), anchor="mm")

    for i, (label, col) in enumerate(cols):
        x = PAD + i * CW
        sheet.paste(col[0], (x, TOP))
        sheet.paste(col[1], (x + 110, TOP + 680))
        d.text((x + 220, TOP + 680 + 470),
               label.replace(".png", "").upper(),
               font=cinzel(30), fill=(184, 168, 138), anchor="mm")

    out = os.path.join(WORK, "frame_options_sheet.png")
    sheet.save(out)
    print(f"wrote {out} and per-frame previews")


if __name__ == "__main__":
    main()
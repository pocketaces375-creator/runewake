#!/usr/bin/env python3
"""tools/frame_portraits.py — forged border on every class portrait (2:3), class-tinted.

Raw masters: client/content/art/classes/raw/<name>.png (created on first run from the live file).
Framed output: client/content/art/classes/<name>.png. Re-running re-frames from raw, never a frame on a frame.
Usage: python3 tools/frame_portraits.py [name ...]   (default: every *_m.png / *_f.png and the 7 defaults)
"""
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

DIR = "client/content/art/classes"
RAW = os.path.join(DIR, "raw")
BORDER = 26
ACCENT = {
    "warrior": (196, 80, 27), "battlemage": (31, 111, 114), "necromancer": (110, 127, 74),
    "paladin": (200, 160, 74), "druid": (47, 107, 58), "rogue": (130, 96, 160), "astrologist": (31, 111, 114),
}
IRON_DARK, IRON_MID, IRON_LIGHT = (26, 23, 22), (62, 56, 51), (116, 105, 94)


def accent_for(name):
    for cls, col in ACCENT.items():
        if name == cls or name.startswith(cls + "_"):
            return col
    return (140, 130, 115)


def frame(img, accent):
    img = img.convert("RGB")
    W, H = img.width + 2 * BORDER, img.height + 2 * BORDER
    out = Image.new("RGB", (W, H), IRON_DARK)
    d = ImageDraw.Draw(out)
    for y in range(H):
        t = (y / H) * 0.75
        d.line([(0, y), (W, y)], fill=tuple(int(IRON_MID[i] + (IRON_DARK[i] - IRON_MID[i]) * t) for i in range(3)))
    sheen = Image.new("RGB", (W, H), (0, 0, 0))
    ImageDraw.Draw(sheen).polygon([(0, 0), (W, 0), (0, H)], fill=(26, 23, 20))
    out = ImageChops.add(out, sheen.filter(ImageFilter.GaussianBlur(40)))
    d = ImageDraw.Draw(out)
    d.rectangle([0, 0, W - 1, H - 1], outline=(12, 11, 10), width=3)
    d.line([(3, 3), (W - 4, 3)], fill=IRON_LIGHT, width=2)
    d.line([(3, 3), (3, H - 4)], fill=IRON_LIGHT, width=2)
    d.line([(4, H - 4), (W - 4, H - 4)], fill=(14, 13, 12), width=3)
    d.line([(W - 4, 4), (W - 4, H - 4)], fill=(14, 13, 12), width=3)
    d.rectangle([BORDER - 12, BORDER - 12, W - BORDER + 11, H - BORDER + 11], outline=(92, 82, 71), width=1)
    d.rectangle([BORDER - 9, BORDER - 9, W - BORDER + 8, H - BORDER + 8], outline=(18, 16, 15), width=1)
    glow = Image.new("RGB", (W, H), (0, 0, 0))
    ImageDraw.Draw(glow).rectangle([BORDER - 5, BORDER - 5, W - BORDER + 4, H - BORDER + 4], outline=accent, width=5)
    out = ImageChops.add(out, glow.filter(ImageFilter.GaussianBlur(7)))
    d = ImageDraw.Draw(out)
    L, T, Wd = 8, 26, 3
    for (cx, cy, dx, dy) in [(L, L, 1, 1), (W - L, L, -1, 1), (L, H - L, 1, -1), (W - L, H - L, -1, -1)]:
        d.line([(cx, cy), (cx + dx * T, cy)], fill=accent, width=Wd)
        d.line([(cx, cy), (cx, cy + dy * T)], fill=accent, width=Wd)
        d.ellipse([cx - 4, cy - 4, cx + 4, cy + 4], fill=(78, 70, 62), outline=(16, 15, 14))
        d.ellipse([cx - 2, cy - 2, cx, cy], fill=(168, 155, 138))
    out.paste(img, (BORDER, BORDER))
    d = ImageDraw.Draw(out)
    d.rectangle([BORDER - 1, BORDER - 1, W - BORDER, H - BORDER], outline=(10, 9, 8), width=2)
    d.rectangle([BORDER - 3, BORDER - 3, W - BORDER + 2, H - BORDER + 2], outline=accent, width=1)
    return out


def main():
    os.makedirs(RAW, exist_ok=True)
    names = sys.argv[1:] or sorted(f[:-4] for f in os.listdir(DIR) if f.endswith(".png") and (f.endswith("_m.png") or f.endswith("_f.png")))
    for n in names:
        live = os.path.join(DIR, n + ".png"); raw = os.path.join(RAW, n + ".png")
        if not os.path.exists(raw):
            Image.open(live).convert("RGB").save(raw, "PNG")
        frame(Image.open(raw), accent_for(n)).save(live, "PNG")
        print("framed", n)
    # defaults follow their variant
    default = {"warrior": "m", "battlemage": "m", "necromancer": "m", "paladin": "m", "druid": "m", "rogue": "f", "astrologist": "f"}
    for cls, g in default.items():
        src = os.path.join(DIR, f"{cls}_{g}.png")
        if os.path.exists(src):
            Image.open(src).save(os.path.join(DIR, cls + ".png"), "PNG")
            print(f"{cls}.png <- {cls}_{g}")


if __name__ == "__main__":
    main()

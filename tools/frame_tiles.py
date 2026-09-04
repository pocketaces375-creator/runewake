#!/usr/bin/env python3
"""tools/frame_tiles.py — put a forged metal border on every relic/weapon tile.

The raw generated art is kept in client/content/art/artifacts/raw/<id>.webp.
The framed tile that the game shows is written to client/content/art/artifacts/<id>.webp.
Re-running is safe: it always re-frames from raw/, never frames a frame.

Usage:
  python3 tools/frame_tiles.py                 # frame every tile
  python3 tools/frame_tiles.py artf_warrior_sword   # one tile
"""
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ART = "client/content/art/artifacts"
RAW = os.path.join(ART, "raw")
OUT_SIZE = 320          # framed tile
BORDER = 26             # frame thickness
INNER = OUT_SIZE - 2 * BORDER

# stratum accent per class — the thin inner rim light
ACCENT = {
    "warrior": (196, 80, 27),
    "battlemage": (31, 111, 114),
    "necromancer": (110, 127, 74),
    "paladin": (200, 160, 74),
    "druid": (47, 107, 58),
    "rogue": (130, 96, 160),
    "astrologist": (31, 111, 114),
}
IRON_DARK = (26, 23, 22)
IRON_MID = (62, 56, 51)
IRON_LIGHT = (116, 105, 94)


def accent_for(tile_id):
    for cls, col in ACCENT.items():
        if tile_id.startswith(f"artf_{cls}_"):
            return col
    return (140, 130, 115)


def frame(img, accent):
    img = img.convert("RGB").resize((INNER, INNER), Image.LANCZOS)
    out = Image.new("RGB", (OUT_SIZE, OUT_SIZE), IRON_DARK)
    d = ImageDraw.Draw(out)

    # forged bronze body: diagonal sheen, dark at the lower right
    for y in range(OUT_SIZE):
        for band, (x0, x1) in [(0, (0, OUT_SIZE))]:
            t = (y / OUT_SIZE) * 0.75
            c = tuple(int(IRON_MID[i] + (IRON_DARK[i] - IRON_MID[i]) * t) for i in range(3))
            d.line([(x0, y), (x1, y)], fill=c)
    sheen = Image.new("RGB", (OUT_SIZE, OUT_SIZE), (0, 0, 0))
    sd = ImageDraw.Draw(sheen)
    sd.polygon([(0, 0), (OUT_SIZE, 0), (0, OUT_SIZE)], fill=(26, 23, 20))
    out = ImageChops.add(out, sheen.filter(ImageFilter.GaussianBlur(28)))

    # hard outer keyline + bevel highlights
    d = ImageDraw.Draw(out)
    d.rectangle([0, 0, OUT_SIZE - 1, OUT_SIZE - 1], outline=(12, 11, 10), width=3)
    d.line([(3, 3), (OUT_SIZE - 4, 3)], fill=IRON_LIGHT, width=2)
    d.line([(3, 3), (3, OUT_SIZE - 4)], fill=IRON_LIGHT, width=2)
    d.line([(4, OUT_SIZE - 4), (OUT_SIZE - 4, OUT_SIZE - 4)], fill=(14, 13, 12), width=3)
    d.line([(OUT_SIZE - 4, 4), (OUT_SIZE - 4, OUT_SIZE - 4)], fill=(14, 13, 12), width=3)

    # engraved channel around the aperture
    d.rectangle([BORDER - 12, BORDER - 12, OUT_SIZE - BORDER + 11, OUT_SIZE - BORDER + 11],
                outline=(92, 82, 71), width=1)
    d.rectangle([BORDER - 9, BORDER - 9, OUT_SIZE - BORDER + 8, OUT_SIZE - BORDER + 8],
                outline=(18, 16, 15), width=1)

    # accent glow behind the aperture rim
    glow = Image.new("RGB", (OUT_SIZE, OUT_SIZE), (0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.rectangle([BORDER - 5, BORDER - 5, OUT_SIZE - BORDER + 4, OUT_SIZE - BORDER + 4],
                 outline=accent, width=5)
    out = ImageChops.add(out, glow.filter(ImageFilter.GaussianBlur(7)))

    # corner brackets in the class accent
    d = ImageDraw.Draw(out)
    L, T, W = 8, 22, 3
    for (cx, cy, dx, dy) in [(L, L, 1, 1), (OUT_SIZE - L, L, -1, 1),
                             (L, OUT_SIZE - L, 1, -1), (OUT_SIZE - L, OUT_SIZE - L, -1, -1)]:
        d.line([(cx, cy), (cx + dx * T, cy)], fill=accent, width=W)
        d.line([(cx, cy), (cx, cy + dy * T)], fill=accent, width=W)
        d.ellipse([cx - 4, cy - 4, cx + 4, cy + 4], fill=(78, 70, 62), outline=(16, 15, 14))
        d.ellipse([cx - 2, cy - 2, cx, cy], fill=(168, 155, 138))

    # aperture
    out.paste(img, (BORDER, BORDER))
    d = ImageDraw.Draw(out)
    d.rectangle([BORDER - 1, BORDER - 1, OUT_SIZE - BORDER, OUT_SIZE - BORDER], outline=(10, 9, 8), width=2)
    d.rectangle([BORDER - 3, BORDER - 3, OUT_SIZE - BORDER + 2, OUT_SIZE - BORDER + 2], outline=accent, width=1)
    return out



def main():
    os.makedirs(RAW, exist_ok=True)
    only = sys.argv[1] if len(sys.argv) > 1 else None
    ids = []
    for f in sorted(os.listdir(ART)):
        if not f.endswith(".webp"):
            continue
        ids.append(f[:-5])
    for tid in ids:
        if only and tid != only:
            continue
        raw_p = os.path.join(RAW, tid + ".webp")
        live_p = os.path.join(ART, tid + ".webp")
        if not os.path.exists(raw_p):          # first run: current art becomes the raw master
            Image.open(live_p).convert("RGB").save(raw_p, "WEBP", quality=95)
        framed = frame(Image.open(raw_p), accent_for(tid))
        framed.save(live_p, "WEBP", quality=92)
        print(f"framed {tid} -> {framed.size}")


if __name__ == "__main__":
    main()

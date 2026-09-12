#!/usr/bin/env python3
"""pipeline/bake_cards.py — bake every card face using compose() approach.
Output: client/content/art/cards_baked/<id>.webp at 416×608.
Stat badges EMPTY; cost baked. Also writes layout.json."""
from __future__ import annotations

import json, os, sys
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
from nineslice import nineslice_frame, BAND_FRAC

CARD_DIR = os.path.join(ROOT, "client", "content", "cards")
ART_DIR = os.path.join(ROOT, "client", "content", "art")
OUT_DIR = os.path.join(ART_DIR, "cards_baked")
CINZEL = os.path.join(ROOT, "client", "assets", "fonts", "Cinzel.ttf")
CINZEL_DEC = os.path.join(ROOT, "client", "assets", "fonts", "CinzelDecorative-Bold.ttf")
CHOSEN_FRAME = os.path.join(ROOT, "pipeline", "work", "card_frames", "frame_8.png")
OUT_W, OUT_H = 416, 608
BAND_PX = int(OUT_W * BAND_FRAC)

def cinzel(sz):
    return ImageFont.truetype(CINZEL, sz)


def cinzel_decorative(sz):
    return ImageFont.truetype(CINZEL_DEC, sz)


def fit_cover(img, size, bias=0.28):
    w, h = size
    sw, sh = img.size
    sc = max(w / sw, h / sh)
    img = img.resize((int(sw * sc) + 1, int(sh * sc) + 1), Image.Resampling.LANCZOS)
    x = (img.width - w) // 2
    y = max(0, int((img.height - h) * bias))
    return img.crop((x, y, x + w, y + h))

def bake_card(card_id, name, cost, atk, vig):
    result = nineslice_frame(CHOSEN_FRAME, (OUT_W, OUT_H), band_px=BAND_PX)
    if result is None:
        return None
    border, (x0, y0, x1, y1) = result
    aw, ah = x1 - x0, y1 - y0

    canvas = Image.new("RGBA", (OUT_W, OUT_H), (0, 0, 0, 0))

    art_path = os.path.join(ART_DIR, card_id + ".webp")
    if os.path.exists(art_path):
        art = fit_cover(Image.open(art_path).convert("RGB"), (aw, ah))
    else:
        art = Image.new("RGB", (aw, ah), (25, 22, 18))
    canvas.paste(art, (x0, y0))
    canvas.alpha_composite(border)

    d = ImageDraw.Draw(canvas, "RGBA")

    # Cost disc
    r = 26
    ccx, ccy = OUT_W - 48, 50
    d.ellipse([ccx - r, ccy - r, ccx + r, ccy + r], fill=(30, 25, 18), outline=(232, 205, 120), width=3)
    d.text((ccx, ccy + 1), str(cost), font=cinzel(29), fill=(232, 205, 120), anchor="mm")

    # Bottom scrim inside the art window
    sh = int(ah * 0.26)
    scrim = Image.new("RGBA", (aw, sh), (0, 0, 0, 0))
    ds = ImageDraw.Draw(scrim)
    for yy in range(sh):
        ds.line([(0, yy), (aw, yy)], fill=(5, 4, 3, int(215 * (yy / sh) ** 0.8)))
    canvas.alpha_composite(scrim, (x0, y1 - sh))

    # Name centred at bottom of art window — autofit
    name_text = name.upper()
    fs = 26
    font = cinzel_decorative(fs)
    bb = d.textbbox((0, 0), name_text, font=font)
    while (bb[2] - bb[0]) > (aw - 16) and fs > 10:
        fs -= 1
        font = cinzel_decorative(fs)
        bb = d.textbbox((0, 0), name_text, font=font)
    ny = y1 - int(ah * 0.085)
    cx = OUT_W // 2
    d.text((cx + 1, ny - 28 + 1), name_text, font=font,
           fill=(0, 0, 0, 230), anchor="mm")
    d.text((cx, ny - 28), name_text, font=font,
           fill=(232, 220, 200), anchor="mm")

    # Stat badges — solid colour, black outline, white numeral
    has_badges = atk is not None and vig is not None
    atk_b = vig_b = None
    if has_badges:
        for sx, col, isAtk in [
            (x0 + 39, (176, 58, 48), True),
            (x1 - 39, (76, 138, 76), False)]:
            d.rounded_rectangle([sx - 28, ny - 10, sx + 28, ny + 22],
                                radius=7, fill=col,
                                outline=(0, 0, 0, 200), width=2)
            b = (sx - 28, ny - 10, sx + 28, ny + 22)
            if isAtk: atk_b = b
            else: vig_b = b

    def norm(r):
        if r is None: return [0, 0, 0, 0]
        return [r[0]/OUT_W, r[1]/OUT_H, (r[2]-r[0])/OUT_W, (r[3]-r[1])/OUT_H]

    rects = {
        "attack_badge": norm(atk_b),
        "vigor_badge": norm(vig_b),
        "name_plate": [x0/OUT_W, (y1-sh)/OUT_H, aw/OUT_W, sh/OUT_H],
        "cost_badge": [(ccx-r)/OUT_W, (ccy-r)/OUT_H, (2*r)/OUT_W, (2*r)/OUT_H],
    }
    return canvas, rects

def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    cards = []
    for f in sorted(os.listdir(CARD_DIR)):
        if not f.endswith(".json"): continue
        with open(os.path.join(CARD_DIR, f)) as fh:
            cards.extend(json.load(fh))
    seen = set()
    cards = [c for c in cards if not (c["id"] in seen or seen.add(c["id"]))]
    print(f"Cards to bake: {len(cards)}")

    rects_by_card = {}
    ok = fail = 0
    for c in cards:
        cid = c["id"]
        result = bake_card(cid, c.get("name",""), c.get("cost",0), c.get("attack"), c.get("vigor"))
        if result is None: fail += 1; continue
        img, rects = result
        img.save(os.path.join(OUT_DIR, f"{cid}.webp"), "WEBP", quality=85)
        rects_by_card[cid] = rects
        ok += 1

    layout = {
        "format": "TASK-CARD-BAKE-1",
        "source_frame": "frame_8.png",
        "bake_size": [OUT_W, OUT_H],
        "band_frac": BAND_FRAC,
        "bake_count": ok,
        "cards": rects_by_card,
    }
    with open(os.path.join(OUT_DIR, "layout.json"), "w") as f:
        json.dump(layout, f, indent=2)
    print(f"Baked: {ok}  Failed: {fail}")

if __name__ == "__main__":
    main()
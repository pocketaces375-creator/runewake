#!/usr/bin/env python3
"""pipeline/bake_cards.py — bake every card face in the RUNESTONE layout (FABLE-037).

Trikzos chose this layout over five rounds of mock-ups ("E2/F2", the final candidate):

  - the art fills the card inside the rootbound stone frame;
  - a dark carved-stone NAME BAND lies over the frame's bottom border, capped by an aged-gold
    rule with a diamond stud at each end. It holds only the name: cream capitals, dark outline,
    centred, never closer than 40 px (at 832 wide) to the studs; long names narrow their letters
    (to 76 %) before they shrink;
  - STRENGTH and VIGOR are two slim heater shields in aged gold with matte enamel faces
    (oxblood + sword, moss + heart), standing above the gold rule with a clear gap;
  - the COST sits in a gold diamond in the top-left corner whose face is the card TYPE's colour:
    creature green, ritual blue, relic violet.

What is baked and what is live: everything above is baked EXCEPT the three numerals. The game
draws those (CardPlate), so buffs, damage and discounts show on the card itself. layout.json tells
CardPlate where each numeral goes: its visual centre and its size, in card fractions.

Needs only numpy and Pillow. Renders each card at 2x (832x1216) and downsamples to 416x608.
Output: client/content/art/cards_baked/<id>.webp + layout.json.
Usage:  python3 pipeline/bake_cards.py            # every card
        python3 pipeline/bake_cards.py <id> ...   # just these (layout.json is merged, not replaced)
"""
from __future__ import annotations

import json, math, os, sys
from concurrent.futures import ProcessPoolExecutor

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
from nineslice import nineslice_frame  # noqa: E402

CARD_DIR = os.path.join(ROOT, "client", "content", "cards")
ART_DIR = os.path.join(ROOT, "client", "content", "art")
OUT_DIR = os.path.join(ART_DIR, "cards_baked")
FONT_DIR = os.path.join(ROOT, "client", "assets", "fonts")
FRAME = os.path.join(ROOT, "pipeline", "work", "card_frames", "frame_8.png")

OUT_W, OUT_H = 416, 608
W, H = 832, 1216            # working canvas (2x the output)
BAND = int(832 * 0.08)      # the frame's band, as in-game since TASK-CARD-BAKE-1

INK = (244, 236, 218)
OXBLOOD = (132, 40, 30)
MOSS = (62, 108, 52)
TYPE_COLOUR = {"CREATURE": (66, 122, 56), "TOKEN": (66, 122, 56), "RITUAL": (44, 96, 152), "RELIC": (112, 72, 150)}
AGED_GOLD = [(0, (222, 192, 120)), (0.42, (176, 138, 66)), (0.68, (122, 90, 40)), (1, (184, 148, 78))]

# layout, in 832-wide card pixels
SW, SH, SGAP, RULE_GAP = 116, 158, 10, 14     # shields
DIAMOND_R = 116                                # cost diamond half-diagonal (fully inside the card)
NAME_SIZE = 56


# ── small image toolkit (numpy + Pillow only) ─────────────────────────────────
def blur(a, sigma):
    """Gaussian-ish blur of a float array: three box passes (no scipy needed)."""
    if sigma <= 0.3: return a.astype(np.float32)
    r = max(1, int(round(sigma * 0.93)))
    out = a.astype(np.float32)
    for _ in range(3):
        for ax in (0, 1):
            pad = [(0, 0), (0, 0)]; pad[ax] = (r + 1, r)
            p = np.pad(out, pad, mode="edge")
            c = np.cumsum(p, axis=ax, dtype=np.float64)
            if ax == 0: out = ((c[2 * r + 1:] - c[:-2 * r - 1]) / (2 * r + 1)).astype(np.float32)
            else: out = ((c[:, 2 * r + 1:] - c[:, :-2 * r - 1]) / (2 * r + 1)).astype(np.float32)
    return out


def font(name, size, weight=None):
    f = ImageFont.truetype(os.path.join(FONT_DIR, name), int(size))
    if weight:
        try: f.set_variation_by_name(weight)
        except Exception: pass
    return f


def cinzel(size, weight="Bold"): return font("Cinzel.ttf", size, weight)


def mask(draw_fn):
    m = Image.new("L", (W, H), 0); draw_fn(ImageDraw.Draw(m)); return m


def paint(canvas, m, color, opacity=1.0):
    img = Image.new("RGBA", m.size, tuple(color) + (0,))
    img.putalpha(m.point(lambda v: int(v * opacity)))
    canvas.alpha_composite(img)


def shadow(m, offset=(0, 10), sigma=12, opacity=0.8, color=(0, 0, 0)):
    a = blur(np.asarray(m, np.float32), sigma)
    a = np.roll(a, offset[1], axis=0); a = np.roll(a, offset[0], axis=1)
    img = Image.new("RGBA", m.size, color + (0,))
    img.putalpha(Image.fromarray(np.clip(a * opacity, 0, 255).astype(np.uint8)))
    return img


def bevel(fill, m, depth=6, strength=0.7, emboss=True, hl=0.16, light=(-0.6, -0.8)):
    mm = np.asarray(m, np.float32) / 255.0
    hgt = blur(mm, depth)
    gy, gx = np.gradient(hgt)
    shade = np.clip(-(gx * light[0] + gy * light[1]) * depth * 6.0 * strength, -1.0, 1.0)
    if not emboss: shade = -shade
    f = np.asarray(fill.convert("RGB"), np.float32)
    lit = np.where(shade[..., None] > 0, f + (255 - f) * shade[..., None] * hl, f * (1 + shade[..., None] * 0.75))
    return Image.fromarray(np.dstack([np.clip(lit, 0, 255), mm * 255]).astype(np.uint8), "RGBA")


def vgrad(size, stops):
    w, h = size
    t = np.linspace(0, 1, max(1, h))
    ts = [s[0] for s in stops]; cs = np.array([s[1] for s in stops], np.float32)
    rows = np.stack([np.interp(t, ts, cs[:, k]) for k in range(3)], axis=1)
    return Image.fromarray(np.repeat(rows[:, None, :], max(1, w), axis=1).astype(np.uint8))


def rgrad(center, radius, inner, outer, power=1.0):
    yy, xx = np.mgrid[0:H, 0:W]
    d = np.clip(np.hypot(xx - center[0], yy - center[1]) / radius, 0, 1) ** power
    a = np.array(inner, np.float32) * (1 - d[..., None]) + np.array(outer, np.float32) * d[..., None]
    return Image.fromarray(a.astype(np.uint8))


def grain(canvas, m, amount, sigma, seed):
    rng = np.random.default_rng(seed)
    n = blur(rng.normal(0, 1, (H, W)).astype(np.float32), sigma)
    n = n / (np.abs(n).max() + 1e-6) * amount
    a = np.asarray(canvas).astype(np.float32)
    k = np.asarray(m, np.float32)[..., None] / 255
    a[..., :3] = np.clip(a[..., :3] + n[..., None] * k, 0, 255)
    return Image.fromarray(a.astype(np.uint8), "RGBA")


def metal(canvas, m, stops=AGED_GOLD, depth=5, sh_sigma=10, sh_off=(0, 8)):
    x0, y0, x1, y1 = m.getbbox()
    canvas.alpha_composite(shadow(m, sh_off, sh_sigma, 0.8))
    g = Image.new("RGB", (W, H)); g.paste(vgrad((x1 - x0, y1 - y0), stops), (x0, y0))
    canvas.alpha_composite(bevel(g, m, depth=depth, strength=1.0, hl=0.18))


def slate(size, seed, base):
    w, h = size
    rng = np.random.default_rng(seed)
    n = np.zeros((h, w), np.float32)
    for sc, amp in ((20, 1.0), (7, 0.55), (2, 0.3), (0.6, 0.18)):
        n += blur(rng.normal(0, 1, (h, w)).astype(np.float32), sc) * amp * (sc * 2) ** 0.5
    n = n / (np.abs(n).max() + 1e-6)
    yy = np.mgrid[0:h, 0:w][0]
    strata = np.sin(yy / (h / 5.3) + n * 2.2) * 0.12
    a = np.array(base, np.float32)[None, None, :] + (n * 22 + strata * 40)[..., None]
    img = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))
    d = ImageDraw.Draw(img)
    for _ in range(5):
        x = rng.uniform(0, w); y = rng.uniform(0, h); pts = [(x, y)]
        for _ in range(8):
            x += rng.uniform(-30, 30); y += rng.uniform(-8, 8); pts.append((x, y))
        d.line(pts, fill=(28, 28, 30), width=1)
    return img


def matte(color, m, vignette=0.45):
    x0, y0, x1, y1 = m.getbbox()
    cx, cy = (x0 + x1) / 2, (y0 + y1) * 0.46
    r = max(x1 - x0, y1 - y0) * 0.62
    c = np.array(color, np.float32)
    body = rgrad((cx, cy), r, tuple(c.astype(int)), tuple((c * (1 - vignette)).astype(int)), 1.4)
    return bevel(body, m, depth=4, strength=0.5, hl=0.12, emboss=False)


def fit_cover(img, size, bias=0.28):
    w, h = size; sw, sh = img.size
    sc = max(w / sw, h / sh)
    img = img.resize((int(sw * sc) + 1, int(sh * sc) + 1), Image.Resampling.LANCZOS)
    x = (img.width - w) // 2; y = max(0, int((img.height - h) * bias))
    return img.crop((x, y, x + w, y + h))


# ── pieces ────────────────────────────────────────────────────────────────────
def heater(x, y, w, h):
    def draw(d):
        n = w * 0.07
        pts = [(x + n, y), (x + w - n, y), (x + w, y + n), (x + w, y + h * 0.42)]
        for i in range(1, 25):
            t = i / 24
            pts.append((x + w - (w / 2) * t, y + h * 0.42 + (h * 0.58) * math.sin(t * math.pi / 2) ** 1.1))
        for i in range(0, 25):
            t = 1 - i / 24
            pts.append((x + (w / 2) * t, y + h * 0.42 + (h * 0.58) * math.sin(t * math.pi / 2) ** 1.1))
        pts.append((x, y + n))
        d.polygon(pts, fill=255)
    return mask(draw)


def sword(cx, cy, h):
    def draw(d):
        w = h * 0.13
        d.polygon([(cx, cy - h * 0.5), (cx + w, cy - h * 0.34), (cx + w, cy + h * 0.16), (cx - w, cy + h * 0.16), (cx - w, cy - h * 0.34)], fill=255)
        d.rectangle([cx - h * 0.3, cy + h * 0.16, cx + h * 0.3, cy + h * 0.24], fill=255)
        d.rectangle([cx - w * 0.6, cy + h * 0.24, cx + w * 0.6, cy + h * 0.44], fill=255)
        d.ellipse([cx - w, cy + h * 0.42, cx + w, cy + h * 0.56], fill=255)
    return mask(draw)


def heart(cx, cy, h):
    def draw(d):
        r = h * 0.27
        d.ellipse([cx - 2 * r, cy - h * 0.42, cx, cy - h * 0.42 + 2 * r], fill=255)
        d.ellipse([cx, cy - h * 0.42, cx + 2 * r, cy - h * 0.42 + 2 * r], fill=255)
        d.polygon([(cx - 2 * r * 0.98, cy - h * 0.42 + r * 1.25), (cx + 2 * r * 0.98, cy - h * 0.42 + r * 1.25), (cx, cy + h * 0.5)], fill=255)
    return mask(draw)


def shield(canvas, x, y, color, icon, seed):
    """A slim heater shield — WITHOUT its numeral, which the game draws. Returns (canvas, numeral)."""
    w, h = SW, SH
    outer = heater(x, y, w, h)
    canvas.alpha_composite(shadow(outer, (0, 8), 12, 0.8))
    g = Image.new("RGB", (W, H)); g.paste(vgrad((w + 2, h + 2), AGED_GOLD), (int(x), int(y)))
    canvas.alpha_composite(bevel(g, outer, depth=3, strength=0.7, hl=0.18))
    rim = 9
    canvas.alpha_composite(matte(color, heater(x + rim, y + rim, w - 2 * rim, h - 2 * rim * 1.1)))
    canvas = grain(canvas, outer, 12, 0.8, seed)
    ic = icon(x + w / 2, y + h * 0.2, h * 0.2)
    paint(canvas, ImageChops.offset(ic, 0, -1), (0, 0, 0), 0.55)
    paint(canvas, ic, (226, 198, 140), 0.9)
    return canvas, (x + w / 2, y + h * 0.567, h * 0.56)    # numeral: visual centre x, y, em


def stone_band(canvas, x0, x1, y0, y1, seed=17):
    band = mask(lambda d: d.rectangle([x0, y0, x1, y1], fill=255))
    canvas.alpha_composite(shadow(band, (0, -8), 14, 0.7))
    tex = Image.new("RGB", (W, H)); tex.paste(slate((x1 - x0, y1 - y0), seed, (30, 31, 33)), (x0, y0))
    canvas.alpha_composite(bevel(tex, band, depth=3, strength=0.6, hl=0.14))
    return grain(canvas, band, 8, 0.8, seed)


def gold_rule(canvas, x0, x1, y, thick=6, studs=()):
    r = mask(lambda d: d.rectangle([x0, y - thick // 2, x1, y + thick // 2], fill=255))
    metal(canvas, r, depth=2, sh_sigma=4, sh_off=(0, 3))
    for dx, s in studs:
        dm = mask(lambda d, dx=dx, s=s: d.polygon([(dx, y - s), (dx + s, y), (dx, y + s), (dx - s, y)], fill=255))
        metal(canvas, dm, depth=3, sh_sigma=5, sh_off=(0, 4))


def name_text(canvas, center, txt, max_w, size, min_squeeze=0.76):
    """Cream capitals with a dark outline; narrowed (to 76 %) before shrunk."""
    d0 = ImageDraw.Draw(Image.new("L", (8, 8)))
    f = cinzel(size); w = d0.textlength(txt, font=f)
    while w * min_squeeze > max_w and size > 12:
        size -= 1; f = cinzel(size); w = d0.textlength(txt, font=f)
    squeeze = min(1.0, max_w / max(w, 1))
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    dl = ImageDraw.Draw(layer)
    x, y = center
    dl.text((x, y + 2), txt, font=f, fill=(0, 0, 0, 220), anchor="mm", stroke_width=3, stroke_fill=(0, 0, 0, 220))
    dl.text((x, y), txt, font=f, fill=INK, anchor="mm", stroke_width=3, stroke_fill=(8, 8, 8))
    if squeeze < 0.999:
        bb = layer.getbbox()
        part = layer.crop(bb)
        nw = max(1, int(part.width * squeeze))
        part = part.resize((nw, part.height), Image.LANCZOS)
        layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        layer.alpha_composite(part, (int(x - nw / 2 + ((bb[0] + bb[2]) / 2 - x) * squeeze), bb[1]))
    canvas.alpha_composite(layer)


def type_diamond(canvas, cx, cy, R, card_type, seed=22):
    """The gold diamond with a matte face in the card type's colour — WITHOUT the numeral."""
    col = TYPE_COLOUR.get((card_type or "").upper(), (90, 90, 90))
    outer = mask(lambda d: d.polygon([(cx, cy - R), (cx + R, cy), (cx, cy + R), (cx - R, cy)], fill=255))
    metal(canvas, outer, depth=5, sh_sigma=16, sh_off=(0, 12))
    r2 = R - 20
    face = mask(lambda d: d.polygon([(cx, cy - r2), (cx + r2, cy), (cx, cy + r2), (cx - r2, cy)], fill=255))
    canvas.alpha_composite(matte(col, face, 0.5))
    canvas = grain(canvas, outer, 11, 0.8, seed)
    r3 = r2 - 12
    line = mask(lambda d: d.polygon([(cx, cy - r3), (cx + r3, cy), (cx, cy + r3), (cx - r3, cy)], outline=255, width=2))
    paint(canvas, line, (200, 166, 96), 0.55)
    return canvas, (cx, cy - 0.02 * R, R * 0.98)


# ── one card ──────────────────────────────────────────────────────────────────
_border = None


def bake_card(card):
    global _border
    if _border is None:
        _border = nineslice_frame(FRAME, (W, H), band_px=BAND)
    border, (x0, y0, x1, y1) = _border
    cid = card["id"]
    canvas = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ap = os.path.join(ART_DIR, cid + ".webp")
    art = fit_cover(Image.open(ap).convert("RGB"), (x1 - x0, y1 - y0)) if os.path.exists(ap) \
        else Image.new("RGB", (x1 - x0, y1 - y0), (25, 22, 18))
    canvas.paste(art, (x0, y0))
    canvas.alpha_composite(border)

    has_stats = card.get("attack") is not None and card.get("vigor") is not None
    by0, by1 = y1 - 52, H - 16
    bx0, bx1 = x0 - 26, x1 + 26

    # the art settles into shadow above the band, so the shields stand on dark ground
    fade = 110
    a = np.zeros((H, W), np.float32)
    a[by0 - fade:by0, x0:x1] = (np.linspace(0, 1, fade) ** 1.6)[:, None] * 0.72
    canvas.alpha_composite(Image.fromarray(np.dstack([np.zeros((H, W, 3), np.uint8), (a * 255).astype(np.uint8)]), "RGBA"))

    canvas = stone_band(canvas, bx0, bx1, by0, by1)
    stud = 13
    gold_rule(canvas, bx0, bx1, by0, 6, studs=[(bx0 + 22, stud), (bx1 - 22, stud)])
    inner0, inner1 = bx0 + 22 + stud + 40, bx1 - 22 - stud - 40
    name_text(canvas, ((inner0 + inner1) / 2, (by0 + by1) / 2 + 2), card.get("name", "").upper(), inner1 - inner0, NAME_SIZE)

    atk_box = vig_box = atk_num = vig_num = None
    if has_stats:
        sy = by0 - RULE_GAP - SH
        sx = x0 + 14
        canvas, atk_num = shield(canvas, sx, sy, OXBLOOD, sword, 3)
        canvas, vig_num = shield(canvas, sx + SW + SGAP, sy, MOSS, heart, 5)
        atk_box = (sx, sy, SW, SH); vig_box = (sx + SW + SGAP, sy, SW, SH)

    dcx = dcy = DIAMOND_R + 8          # the diamond's points stay 8 px inside the card edge
    canvas, cost_num = type_diamond(canvas, dcx, dcy, DIAMOND_R, card.get("type"))

    out = canvas.resize((OUT_W, OUT_H), Image.Resampling.LANCZOS)

    def rect(b): return [0, 0, 0, 0] if b is None else [round(b[0] / W, 5), round(b[1] / H, 5), round(b[2] / W, 5), round(b[3] / H, 5)]
    def num(n): return [0, 0, 0] if n is None else [round(n[0] / W, 5), round(n[1] / H, 5), round(n[2] / H, 5)]
    rects = {
        "type": (card.get("type") or "").upper(),
        "attack_badge": rect(atk_box), "vigor_badge": rect(vig_box),
        "cost_badge": rect((dcx - DIAMOND_R, dcy - DIAMOND_R, 2 * DIAMOND_R, 2 * DIAMOND_R)),
        "name_plate": rect((bx0, by0, bx1 - bx0, by1 - by0)),
        # where the game draws each numeral: visual centre (x, y) and em size, in card fractions
        "attack_num": num(atk_num), "vigor_num": num(vig_num), "cost_num": num(cost_num),
    }
    return out, rects


def numeral_mid():
    """How far above the baseline the middle of Cinzel Black's digits sits, as a share of the em.
    CardPlate puts the baseline this far below each numeral's visual centre."""
    f = cinzel(1000, "Black")
    b = ImageDraw.Draw(Image.new("L", (8, 8))).textbbox((0, 0), "0123456789", font=f, anchor="ls")
    return round(-(b[1] + b[3]) / 2 / 1000, 4)


def _job(card):
    img, rects = bake_card(card)
    img.save(os.path.join(OUT_DIR, f"{card['id']}.webp"), "WEBP", quality=88, method=6)
    return card["id"], rects


def load_cards():
    cards = []
    for f in sorted(os.listdir(CARD_DIR)):
        if not f.endswith(".json"): continue
        with open(os.path.join(CARD_DIR, f)) as fh:
            d = json.load(fh)
        cards.extend(d["cards"] if isinstance(d, dict) and "cards" in d else d)
    seen = set()
    return [c for c in cards if not (c["id"] in seen or seen.add(c["id"]))]


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    cards = load_cards()
    only = set(sys.argv[1:])
    todo = [c for c in cards if not only or c["id"] in only]
    print(f"Cards to bake: {len(todo)} of {len(cards)}")
    lay_path = os.path.join(OUT_DIR, "layout.json")
    rects_by_card = {}
    if only and os.path.exists(lay_path):
        with open(lay_path) as f:
            old = json.load(f)
        if old.get("format") == "FABLE-037-RUNESTONE":
            rects_by_card = old.get("cards", {})
    with ProcessPoolExecutor(max(1, min(8, (os.cpu_count() or 2) - 1))) as ex:
        for i, (cid, rects) in enumerate(ex.map(_job, todo), 1):
            rects_by_card[cid] = rects
            if i % 20 == 0 or i == len(todo): print(f"  {i}/{len(todo)}", flush=True)
    layout = {
        "format": "FABLE-037-RUNESTONE",
        "source_frame": "frame_8.png",
        "bake_size": [OUT_W, OUT_H],
        "numeral_font": "res://assets/fonts/Cinzel.ttf (wght 900)",
        "numeral_mid": numeral_mid(),
        "bake_count": len(rects_by_card),
        "cards": rects_by_card,
    }
    with open(lay_path, "w") as f:
        json.dump(layout, f, indent=1)
    print(f"Baked {len(todo)} — layout.json has {len(rects_by_card)} cards")


if __name__ == "__main__":
    main()

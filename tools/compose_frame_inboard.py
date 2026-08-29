#!/usr/bin/env python3
"""
tools/compose_frame_inboard.py — show each generated card border IN GAME:
builds a full phone-resolution duel screen (2316x1080) on the real board
painting, with every card wearing that border.

Reads pipeline/work/card_frames/frame_[0-9]*.png (from gen_card_frames.sh)
and writes pipeline/work/card_frames/inboard_<n>.png — one full screen per
border option, plus inboard_sheet.png stacking them for comparison.

No client code is touched: this composites real assets exactly as the game
will lay them out, so it previews the finished screen.

Run from repo root:  python3 tools/compose_frame_inboard.py
"""
from __future__ import annotations

import glob
import os
import sys
from collections import deque

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ART = os.path.join(ROOT, "client", "content", "art")
CINZEL = os.path.join(ROOT, "client", "assets", "fonts", "Cinzel.ttf")
WORK = os.path.join(ROOT, "pipeline", "work", "card_frames")
BOARD_BG = os.path.join(ART, "board", "default.png")

W, H = 2316, 1080
GOLD = (201, 168, 76)
GOLD_HI = (232, 205, 120)
BRONZE = (74, 61, 32)
CREAM = (232, 220, 200)
TEAL = (76, 156, 146)
TEAL_HI = (140, 214, 202)

# ── Card assignments ────────────────────────────────────────────────
ENEMY = [
    ("emb_c_ember_hound",    "Ember Hound",     1, 2, 1),
    ("hol_c_skeletal_reaver", "Skeletal Reaver", 1, 2, 1),
    ("emb_u_wildfire_adept",  "Wildfire Adept",  2, 2, 1),
]
MINE = [
    ("vrd_u_elder_treant",   "Elder Treant",     6, 5, 7),
]
HAND = [
    ("hol_c_gravewrit_thrall", "Gravewrit Thrall", 3, 4, 2),
    ("dwn_c_sunblade_recruit", "Sunblade Recruit", 3, 3, 3),
    ("vrd_r_bloomweaver",      "Bloomweaver",      5, 4, 4),
    ("hol_u_crypt_crawler",    "Crypt Crawler",    3, 2, 3),
    ("tid_c_silt_reader",      "Silt Reader",      4, 2, 5),
]


# ── Font helper ─────────────────────────────────────────────────────
def cz(sz: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(CINZEL, sz)


# ── Imports from compose_frame_options ──────────────────────────────
def fit_cover(img: Image.Image, size: tuple[int, int], bias=0.28) -> Image.Image:
    w, h = size
    sw, sh = img.size
    sc = max(w / sw, h / sh)
    img = img.resize((int(sw * sc) + 1, int(sh * sc) + 1), Image.LANCZOS)
    x = (img.width - w) // 2
    y = max(0, int((img.height - h) * bias))
    return img.crop((x, y, x + w, y + h))


def detect_window(frame: Image.Image, thresh=48, scale=4):
    small = frame.convert("L").resize((frame.width // scale, frame.height // scale))
    px = small.load()
    SW, SH = small.size
    cx, cy = SW // 2, SH // 2
    if px[cx, cy] > thresh:
        return None
    seen = [[False] * SH for _ in range(SW)]
    q = deque([(cx, cy)])
    seen[cx][cy] = True
    pts = []
    while q:
        x, y = q.popleft()
        pts.append((x, y))
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if 0 <= nx < SW and 0 <= ny < SH and not seen[nx][ny] and px[nx, ny] <= thresh:
                seen[nx][ny] = True
                q.append((nx, ny))
    if len(pts) < (SW * SH) * 0.15:
        return None
    m = Image.new("L", (SW, SH), 0)
    mp = m.load()
    for x, y in pts:
        mp[x, y] = 255
    m = m.resize(frame.size, Image.LANCZOS)
    m = m.filter(ImageFilter.MinFilter(5))
    m = m.filter(ImageFilter.GaussianBlur(2))
    xs = [p[0] * scale for p in pts]
    ys = [p[1] * scale for p in pts]
    return m, (min(xs), min(ys), max(xs) + scale, max(ys) + scale)


# ── Card rendering cache ────────────────────────────────────────────
_cache: dict = {}


def make_card(frame_img: Image.Image, win: tuple[Image.Image, tuple], card_id: str,
              name: str, cost: int | None, atk: int | None, hp: int | None,
              size: tuple[int, int],
              weapon=False, suppressed=False, pips: tuple[int, int] | None = None
              ) -> Image.Image:
    """Render one card at full res in the given border with game UI, then downscale.

    weapon=True: teal colour overlay, "WEAPON" label, pip circles instead of ATK/HP.
    suppressed=True: desaturated art, dimmer name, "SUPPRESSED" overlay.
    pips=(lit, total): weapon activation pips.
    """
    key = (id(frame_img), card_id, cost, atk, hp, size, weapon, suppressed, pips)
    if key in _cache:
        return _cache[key]

    mask, (x0, y0, x1, y1) = win
    base = frame_img.copy().convert("RGBA")
    art = fit_cover(Image.open(os.path.join(ART, card_id + ".webp")).convert("RGB"),
                    (x1 - x0, y1 - y0))
    if suppressed:
        art = ImageEnhance.Brightness(ImageEnhance.Color(art).enhance(0.12)).enhance(0.72)
    art_layer = Image.new("RGBA", base.size, (0, 0, 0, 0))
    art_layer.paste(art, (x0, y0))
    # Composite art into the detected window via the mask
    base_rgb = Image.composite(art_layer.convert("RGB"), base.convert("RGB"), mask)
    base = base_rgb.convert("RGBA")

    if weapon:
        # weapons read teal, not gold
        tint = Image.new("RGB", base.size, (40, 120, 118))
        band = Image.new("L", base.size, 255)
        ImageDraw.Draw(band).rectangle([x0, y0, x1, y1], fill=0)
        base = Image.composite(Image.blend(base.convert("RGB"), tint, 0.4).convert("RGBA"),
                               base, band)

    BW, BH = base.size
    d = ImageDraw.Draw(base, "RGBA")

    # scrim across the bottom of the art window
    sh = int((y1 - y0) * 0.26)
    scrim = Image.new("RGBA", (x1 - x0, sh), (0, 0, 0, 0))
    ds = ImageDraw.Draw(scrim)
    for yy in range(sh):
        ds.line([(0, yy), (x1 - x0, yy)], fill=(5, 4, 3, int(215 * (yy / sh) ** 0.8)))
    base.alpha_composite(scrim, (x0, y1 - sh))
    d = ImageDraw.Draw(base, "RGBA")

    ny = y1 - int((y1 - y0) * 0.085)

    if cost is not None:
        r = 54
        cx, cy = BW - 98, 102
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(30, 25, 18),
                  outline=GOLD_HI, width=7)
        d.text((cx, cy + 2), str(cost), font=cz(60), fill=GOLD_HI, anchor="mm")

    if weapon:
        d.text((BW // 2, y0 + 46), "WEAPON", font=cz(34), fill=TEAL_HI, anchor="mm")

    nm = name.upper()
    d.text((BW // 2 + 3, ny - 58 + 3), nm, font=cz(48), fill=(0, 0, 0, 235), anchor="mm")
    d.text((BW // 2, ny - 58), nm, font=cz(48),
           fill=(160, 165, 162) if suppressed else CREAM, anchor="mm")

    if weapon and pips:
        lit, tot = pips
        gap, pr = 74, 20
        sx = BW // 2 - gap * (tot - 1) // 2
        for i in range(tot):
            col = TEAL_HI if (i < lit and not suppressed) else (52, 65, 63)
            d.ellipse([sx + i * gap - pr, ny + 6 - pr, sx + i * gap + pr, ny + 6 + pr],
                      fill=col, outline=(15, 20, 19), width=3)
    elif atk is not None:
        for sx, col, val in [(x0 + 82, (176, 58, 48), atk), (x1 - 82, (76, 138, 76), hp)]:
            d.rounded_rectangle([sx - 58, ny - 22, sx + 58, ny + 46], radius=15,
                                fill=col, outline=(0, 0, 0, 210), width=4)
            d.text((sx, ny + 12), str(val), font=cz(48), fill=(255, 255, 255), anchor="mm")

    if suppressed:
        d.text((BW // 2, BH // 2), "SUPPRESSED", font=cz(44), fill=(190, 195, 192), anchor="mm")

    out = base.resize(size, Image.LANCZOS)
    _cache[key] = out
    return out


def shadow(card: Image.Image, blur=16, dy=8, a=170) -> tuple[Image.Image, int]:
    """Return (card_with_shadow, padding)."""
    w, h = card.size
    p = blur * 2
    o = Image.new("RGBA", (w + p * 2, h + p * 2), (0, 0, 0, 0))
    s = Image.new("RGBA", o.size, (0, 0, 0, 0))
    # Build shadow mask from non-transparent card pixels
    alpha = card.split()[3].point(lambda v: a if v > 10 else 0)
    s.paste((0, 0, 0, a), (p, p + dy), alpha)
    o.alpha_composite(s.filter(ImageFilter.GaussianBlur(blur / 2)))
    o.alpha_composite(card, (p, p))
    return o, p


def plate(img: Image.Image, x, y, w, h, name, cur, mx, ca, cb):
    """Draw an HP/path progress plate."""
    d = ImageDraw.Draw(img, "RGBA")
    r = h // 2
    d.rounded_rectangle([x, y, x + w, y + h], radius=r, fill=(20, 18, 15, 235),
                        outline=BRONZE, width=2)
    fw = int((w - 6) * cur / mx)
    if fw > 0:
        bar = Image.new("RGBA", (fw, h - 6), (0, 0, 0, 0))
        ImageDraw.Draw(bar).rounded_rectangle([0, 0, fw - 1, h - 7], radius=r - 3, fill=cb)
        img.alpha_composite(bar, (x + 3, y + 3))
    d = ImageDraw.Draw(img, "RGBA")
    d.rounded_rectangle([x, y, x + w, y + h], radius=r, outline=GOLD, width=2)
    f = cz(int(h * 0.52))
    d.text((x + w // 2 + 2, y + h // 2 + 2), name.upper(), font=f, fill=(0, 0, 0, 230), anchor="mm")
    d.text((x + w // 2, y + h // 2), name.upper(), font=f, fill=CREAM, anchor="mm")
    d.text((x + w - int(h * 0.55), y + h // 2), str(cur), font=cz(int(h * 0.44)),
           fill=(255, 246, 228), anchor="rm")


# ── Main scene builder ───────────────────────────────────────────────
def build(frame_path: str, out_path: str, label: str):
    frame = Image.open(frame_path).convert("RGB").resize((832, 1216), Image.LANCZOS)
    win = detect_window(frame)
    if win is None:
        print(f"WARN: no clean window in {frame_path} — skipped", file=sys.stderr)
        return None

    # Board backdrop
    board = Image.open(BOARD_BG).convert("RGB")
    sc = max(W / board.width, H / board.height)
    board = board.resize((int(board.width * sc) + 1, int(board.height * sc) + 1), Image.LANCZOS)
    img = board.crop(((board.width - W) // 2, (board.height - H) // 2,
                      (board.width - W) // 2 + W, (board.height - H) // 2 + H)).convert("RGBA")
    # Vignette darken
    v = Image.new("L", (W, H), 0)
    ImageDraw.Draw(v).ellipse([-W * .25, -H * .35, W * 1.25, H * 1.35], fill=255)
    img = Image.composite(img, Image.new("RGBA", (W, H), (8, 7, 5, 255)),
                          v.filter(ImageFilter.GaussianBlur(160)).point(
                              lambda p: 55 + p * 200 // 255))
    d = ImageDraw.Draw(img, "RGBA")

    # Board lane strips (5 lane slots each side)
    CW, CH, GAP = 200, 292, 46
    x0 = (W - (5 * CW + 4 * GAP)) // 2
    ey, py = 64, 424
    for i in range(5):
        rx = x0 + i * (CW + GAP) - 10
        rib = Image.new("RGBA", (CW + 20, py + CH - ey + 20), (0, 0, 0, 0))
        ImageDraw.Draw(rib).rounded_rectangle([0, 0, CW + 19, py + CH - ey + 19], radius=18,
                                              fill=(201, 168, 76, 32), outline=(201, 168, 76, 85),
                                              width=2)
        img.alpha_composite(rib, (rx, ey - 10))

    # Empty lane wells
    def well(x, y):
        L = Image.new("RGBA", (CW + 2, CH + 2), (0, 0, 0, 0))
        dl = ImageDraw.Draw(L)
        dl.rounded_rectangle([0, 0, CW, CH], radius=14, fill=(12, 11, 9, 70),
                             outline=(201, 168, 76, 150), width=3)
        img.alpha_composite(L, (x, y))

    for i in range(5):
        x = x0 + i * (CW + GAP)
        if i < len(ENEMY):
            c, p = shadow(make_card(frame, win, *ENEMY[i], (CW, CH)))
            img.alpha_composite(c, (x - p, ey - p))
        else:
            well(x, ey)
    for i in range(5):
        x = x0 + i * (CW + GAP)
        if i == 2:
            c, p = shadow(make_card(frame, win, *MINE[0], (CW, CH)))
            img.alpha_composite(c, (x - p, py - p))
        else:
            well(x, py)

    d = ImageDraw.Draw(img, "RGBA")
    d.text((W // 2, 34), "TURN 3", font=cz(34), fill=(168, 82, 74), anchor="mm")

    # Arsenal panels
    def arsenal(ax, ay, ids, nms, deck, barrow, pips, sup):
        dd = ImageDraw.Draw(img, "RGBA")
        dd.rounded_rectangle([ax, ay, ax + 356, ay + 176], radius=14, fill=(18, 16, 13, 205),
                             outline=BRONZE, width=3)
        wx = ax + 14
        for i, wid in enumerate(ids):
            w_card = make_card(frame, win, wid, nms[i], None, None, None,
                               (96, 140), weapon=True, suppressed=sup[i], pips=pips[i])
            img.alpha_composite(w_card, (wx, ay + 18))
            wx += 108
        for j, (val, lab) in enumerate([(deck, "DECK"), (barrow, "BARROW")]):
            cy = ay + 18 + j * 72
            dd.rounded_rectangle([ax + 244, cy, ax + 342, cy + 62], radius=8,
                                 fill=(30, 25, 18, 240), outline=BRONZE, width=2)
            dd.text((ax + 293, cy + 22), str(val), font=cz(30), fill=CREAM, anchor="mm")
            dd.text((ax + 293, cy + 48), lab, font=cz(15), fill=(184, 168, 138), anchor="mm")

    # Player plates
    plate(img, W - 500, 34, 380, 44, "The Wayfarer", 22, 30, (110, 36, 28), (168, 53, 42))
    arsenal(W - 380, 96, ["hol_x_the_black_barrow", "emb_x_the_last_ember"],
            ["Black Barrow", "Last Ember"], 22, 0, [(2, 3), (0, 3)], [False, True])
    arsenal(24, H - 286, ["vrd_x_heartwood_relic", "emb_x_the_last_ember"],
            ["Heartwood", "Last Ember"], 24, 0, [(1, 3), (0, 3)], [False, False])
    plate(img, 120, H - 84, 380, 44, "Trikzos", 21, 30, (46, 90, 46), (76, 138, 76))
    d = ImageDraw.Draw(img, "RGBA")
    for i in range(5):
        cx, cy = 132 + i * 34, H - 24
        d.ellipse([cx - 11, cy - 11, cx + 11, cy + 11],
                  fill=(106, 139, 196) if i < 2 else (42, 46, 54),
                  outline=(20, 24, 30), width=2)

    # Hand fan (curved, 5 cards)
    HW, HH = 236, 344
    n = len(HAND)
    for i, card in enumerate(HAND):
        t = i - (n - 1) / 2
        c, p = shadow(make_card(frame, win, *card, (HW, HH)), blur=18, dy=10)
        rot = c.rotate(-t * 3.5, expand=True, resample=Image.BICUBIC)
        cx = W // 2 + 40 + int(t * HW * 0.94)
        cy = H - HH // 2 - 16 - int(abs(t) * 14)
        img.alpha_composite(rot, (cx - rot.width // 2, cy - rot.height // 2))

    # End turn button
    d = ImageDraw.Draw(img, "RGBA")
    d.rounded_rectangle([W - 260, H - 160, W - 40, H - 96], radius=10,
                        fill=(40, 36, 30, 245), outline=GOLD, width=3)
    d.text((W - 150, H - 128), "END TURN", font=cz(28), fill=CREAM, anchor="mm")

    # Frame label
    d.rounded_rectangle([18, 14, 620, 62], radius=8, fill=(10, 9, 7, 210))
    d.text((32, 38), label, font=cz(30), fill=(212, 184, 76), anchor="lm")

    img.convert("RGB").save(out_path, quality=92)
    print(f"wrote {out_path}")
    return out_path


NAMES = {1: "OPTION 1 — MOSSGROWN KNOTWORK", 2: "OPTION 2 — ROOT-BOUND STONE",
         3: "OPTION 3 — GILDED RELIQUARY", 4: "OPTION 4 — TIDEWASHED MONOLITH"}


def main():
    frames = sorted(glob.glob(os.path.join(WORK, "frame_[0-9]*.png")))
    if not frames:
        print(f"no frames in {WORK}", file=sys.stderr)
        sys.exit(1)
    made = []
    for fp in frames:
        n = int(os.path.basename(fp).split("_")[1].split(".")[0])
        out = os.path.join(WORK, f"inboard_{n}.png")
        if build(fp, out, NAMES.get(n, f"OPTION {n}")):
            made.append(out)
    if not made:
        sys.exit(1)

    # Build comparison sheet
    from math import ceil
    n_cols = 2
    n_rows = ceil(len(made) / n_cols)
    thumb_w, thumb_h = W // 2, H // 2
    sheet = Image.new("RGB", (n_cols * thumb_w, n_rows * thumb_h), (14, 12, 10))
    for i, p in enumerate(made):
        col = i % n_cols
        row = i // n_cols
        thumb = Image.open(p).resize((thumb_w, thumb_h), Image.LANCZOS)
        sheet.paste(thumb, (col * thumb_w, row * thumb_h))
    sheet_path = os.path.join(WORK, "inboard_sheet.png")
    sheet.save(sheet_path)
    print(f"wrote {sheet_path}")


if __name__ == "__main__":
    main()
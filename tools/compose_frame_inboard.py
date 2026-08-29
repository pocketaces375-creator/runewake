#!/usr/bin/env python3
"""
tools/compose_frame_inboard.py — show each generated card border IN GAME:
builds a full phone-resolution duel screen mockup on a painted board,
with every card wearing that border.

Reads pipeline/work/card_frames/frame_[0-9]*.png (from gen_card_frames.sh)
and writes pipeline/work/card_frames/inboard_<n>.png — one full screen per
border option, plus inboard_sheet.png stacking them for comparison.

No client code is touched: this composites real assets in the game layout
so it previews the finished screen.

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

# ── Scene dimensions ────────────────────────────────────────────────
W, H = 2316, 1080

# ── Card assignments ────────────────────────────────────────────────
ENEMY_CARDS = [
    ("emb_c_ember_hound",    "Ember Hound",     1, 2, 1),
    ("hol_c_skeletal_reaver", "Skeletal Reaver", 1, 2, 1),
    ("emb_u_wildfire_adept",  "Wildfire Adept",  2, 2, 1),
]
MINE_CARDS = [
    ("vrd_u_elder_treant",   "Elder Treant",     6, 5, 7),
]
HAND_CARDS = [
    ("hol_c_gravewrit_thrall", "Gravewrit Thrall", 3, 4, 2),
    ("dwn_c_sunblade_recruit", "Sunblade Recruit", 3, 3, 3),
    ("vrd_r_bloomweaver",      "Bloomweaver",      5, 4, 4),
    ("hol_u_crypt_crawler",    "Crypt Crawler",    3, 2, 3),
    ("tid_c_silt_reader",      "Silt Reader",      4, 2, 5),
]


# ── Font helper ─────────────────────────────────────────────────────
def cz(sz: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(CINZEL, sz)


# ── Card renderer (copied pattern from compose_frame_options)  ───────
def fit_cover(img: Image.Image, size, bias=0.28) -> Image.Image:
    w, h = size
    sw, sh = img.size
    sc = max(w / sw, h / sh)
    img = img.resize((int(sw * sc) + 1, int(sh * sc) + 1), Image.LANCZOS)
    x = (img.width - w) // 2
    y = max(0, int((img.height - h) * bias))
    return img.crop((x, y, x + w, y + h))


def detect_window(frame: Image.Image, thresh=48, scale=4):
    """Flood-fill dark central window; return (mask, bbox) at full res."""
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


def render_card(frame: Image.Image, card_id: str, name: str,
                cost: int, atk: int, hp: int,
                out_size, frame_res=(832, 1216),
                suppressed=False, weapon=False) -> Image.Image | None:
    """Render one card at the given output size using the border frame."""
    frame = frame.convert("RGB").resize(frame_res, Image.LANCZOS)
    det = detect_window(frame)
    if det is None:
        return None
    mask, (x0, y0, x1, y1) = det
    art = fit_cover(Image.open(os.path.join(ART, card_id + ".webp")).convert("RGB"),
                    (x1 - x0, y1 - y0))
    if suppressed:
        art = ImageEnhance.Brightness(ImageEnhance.Color(art).enhance(0.12)).enhance(0.72)
    base = frame.copy()
    art_layer = Image.new("RGB", frame.size, (0, 0, 0))
    art_layer.paste(art, (x0, y0))
    base = Image.composite(art_layer, base, mask)
    base = base.convert("RGBA")

    d = ImageDraw.Draw(base, "RGBA")
    FW, FH = base.size
    # bottom scrim
    scr_h = int((y1 - y0) * 0.26)
    scrim = Image.new("RGBA", (x1 - x0, scr_h), (0, 0, 0, 0))
    ds = ImageDraw.Draw(scrim)
    for yy in range(scr_h):
        a = int(215 * (yy / scr_h) ** 0.8)
        ds.line([(0, yy), (scrim.width - 1, yy)], fill=(5, 4, 3, a))
    base.paste(scrim, (x0, y1 - scr_h), scrim)
    d = ImageDraw.Draw(base, "RGBA")

    # cost disc (top-right)
    r = 52
    ccx, ccy = FW - 96, 100
    d.ellipse([ccx - r, ccy - r, ccx + r, ccy + r],
              fill=(30, 25, 18), outline=(232, 205, 120), width=6)
    d.text((ccx, ccy + 2), str(cost), font=cz(58), fill=(232, 205, 120), anchor="mm")

    # name
    ny = y1 - int((y1 - y0) * 0.085)
    d.text((FW // 2 + 2, ny - 56 + 2), name.upper(),
           font=cz(46), fill=(0, 0, 0, 230), anchor="mm")
    d.text((FW // 2, ny - 56), name.upper(),
           font=cz(46), fill=(232, 220, 200), anchor="mm")

    # stat chips
    for sx, col, val in [(x0 + 78, (176, 58, 48), atk), (x1 - 78, (76, 138, 76), hp)]:
        d.rounded_rectangle([sx - 56, ny - 20, sx + 56, ny + 44],
                            radius=14, fill=col, outline=(0, 0, 0, 200), width=3)
        d.text((sx, ny + 12), str(val), font=cz(46), fill=(255, 255, 255), anchor="mm")

    if weapon:
        # small weapon icon indicator
        wx, wy = x0 + 60, y0 + 60
        d.polygon([(wx, wy - 8), (wx + 8, wy), (wx, wy + 8), (wx - 8, wy)],
                  fill=(210, 190, 140))

    return base.resize(out_size, Image.LANCZOS)


# ── Board backdrop painter ───────────────────────────────────────────
def paint_board(w: int, h: int) -> Image.Image:
    """Draw a War Altar board background."""
    bg = Image.new("RGB", (w, h), (18, 14, 11))
    d = ImageDraw.Draw(bg, "RGBA")

    # floor sweep
    for y in range(h):
        t = y / h
        r = int(18 + 30 * (1 - abs(t - 0.5) * 2))
        g = int(14 + 26 * (1 - abs(t - 0.5) * 2))
        b = int(11 + 22 * (1 - abs(t - 0.5) * 2))
        d.line([(0, y), (w, y)], fill=(r, g, b))

    # fade edges darker
    for x in range(200):
        a = int(120 * (1 - x / 200) ** 0.7)
        d.line([(x, 0), (x, h)], fill=(0, 0, 0, a))
        d.line([(w - x - 1, 0), (w - x - 1, h)], fill=(0, 0, 0, a))

    # lane glow strips - enemy side
    for ly in (80, 200, 320):
        for yy in range(3):
            a = 18 - yy * 6
            d.line([(200, ly + yy), (w - 200, ly + yy)], fill=(62, 42, 24, a))

    # my side
    for ly in (480, 600, 720):
        for yy in range(3):
            a = 18 - yy * 6
            d.line([(200, ly + yy), (w - 200, ly + yy)], fill=(68, 58, 38, a))

    return bg


# ── Main  ────────────────────────────────────────────────────────────
def build_inboard(frame_path: str, label: str, out_dir: str) -> Image.Image | None:
    """Build one full-screen duel mockup with frame_path's border."""
    frame_src = Image.open(frame_path).convert("RGB")

    scene = paint_board(W, H)
    d = ImageDraw.Draw(scene, "RGBA")

    # ── Enemy board (3 cards, ~206×302 each) ─────────────────────────
    e_w, e_h = 206, 302
    e_y = 100
    e_gap = 160
    e_total = 3 * e_w + 2 * e_gap
    e_left = (W - e_total) // 2
    for i, card in enumerate(ENEMY_CARDS):
        cx = e_left + i * (e_w + e_gap)
        card_img = render_card(frame_src, *card, out_size=(e_w, e_h))
        if card_img:
            scene.paste(card_img, (cx, e_y), card_img)

    # spawn circle markers above enemy cards
    for i in range(len(ENEMY_CARDS)):
        cx = e_left + i * (e_w + e_gap) + e_w // 2
        d.ellipse([cx - 18, e_y - 30, cx + 18, e_y + 6],
                  fill=(20, 16, 12), outline=(140, 110, 60), width=3)

    # dividing line
    div_y = 395
    for yy in range(2):
        a = 40 if yy == 0 else 14
        d.line([(60, div_y + yy), (W - 60, div_y + yy)], fill=(90, 76, 52, a))

    # ── My board (1 large card, ~292×427)  ───────────────────────────
    m_w, m_h = 292, 427
    m_y = 420
    m_card = render_card(frame_src, *MINE_CARDS[0], out_size=(m_w, m_h))
    if m_card:
        mx = (W - m_w) // 2
        scene.paste(m_card, (mx, m_y), m_card)

    # ── Hand (bottom, 5 cards ~174×254) ──────────────────────────────
    h_w, h_h = 174, 254
    h_y = 800
    h_gap = 36
    h_total = len(HAND_CARDS) * h_w + (len(HAND_CARDS) - 1) * h_gap
    h_left = (W - h_total) // 2

    for i, card in enumerate(HAND_CARDS):
        cx = h_left + i * (h_w + h_gap)
        card_img = render_card(frame_src, *card, out_size=(h_w, h_h))
        if card_img:
            scene.paste(card_img, (cx, h_y), card_img)

    # ── Turn indicator ───────────────────────────────────────────────
    d.text((45, div_y + 12), "YOUR TURN", font=cz(28), fill=(180, 156, 106))
    d.text((W - 320, div_y + 12), "Shrine: 3", font=cz(24), fill=(156, 130, 90))

    # ── Frame label ──────────────────────────────────────────────────
    lb = label.replace(".png", "").replace("frame_", "BORDER ").upper()
    d.rounded_rectangle([30, H - 64, 380, H - 16], radius=12,
                        fill=(10, 8, 6, 200), outline=(100, 80, 40), width=2)
    d.text((205, H - 41), lb, font=cz(30), fill=(212, 184, 76), anchor="mm")

    out_path = os.path.join(out_dir, label.replace("frame_", "inboard_"))
    scene.save(out_path)
    print(f"  → {out_path}")
    return scene


def main():
    frames = sorted(glob.glob(os.path.join(WORK, "frame_[0-9]*.png")))
    if not frames:
        print(f"no frames in {WORK}", file=sys.stderr)
        sys.exit(1)

    out_dir = os.path.join(WORK, "inboard")
    os.makedirs(out_dir, exist_ok=True)

    scenes: list[tuple[str, Image.Image]] = []
    for fp in frames:
        label = os.path.basename(fp)
        print(f"Building {label} ...")
        scene = build_inboard(fp, label, out_dir)
        if scene:
            scenes.append((label, scene))

    if not scenes:
        print("no inboard scenes produced")
        sys.exit(1)

    # ── Build comparison sheet ───────────────────────────────────────
    from math import ceil
    n = len(scenes)
    cols = 2
    rows = ceil(n / cols)
    thumb_w, thumb_h = W // 2, H // 2
    sheet = Image.new("RGB", (cols * thumb_w, rows * thumb_h + 120), (14, 12, 10))
    sd = ImageDraw.Draw(sheet)
    sd.text((sheet.width // 2, 40), "INBOARD COMPARISON",
            font=cz(48), fill=(212, 184, 76), anchor="mm")

    for i, (label, scene) in enumerate(scenes):
        col = i % cols
        row = i // cols
        tx = col * thumb_w
        ty = row * thumb_h + 120
        thumb = scene.resize((thumb_w, thumb_h), Image.LANCZOS)
        sheet.paste(thumb, (tx, ty + 30))
        name = label.replace(".png", "").replace("frame_", "BORDER ").upper()
        sd.text((tx + thumb_w // 2, ty), name, font=cz(26), fill=(184, 168, 138), anchor="mm")

    sheet_path = os.path.join(WORK, "inboard_sheet.png")
    sheet.save(sheet_path)
    print(f"wrote {sheet_path}")

    # Also write inboard_*.png to the card_frames root for easy posting
    for label, scene in scenes:
        dest = os.path.join(WORK, label.replace("frame_", "inboard_"))
        scene.save(dest)
        print(f"  (mirror) {dest}")


if __name__ == "__main__":
    main()
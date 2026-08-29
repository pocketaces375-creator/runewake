#!/usr/bin/env python3
"""
tools/compose_frame_options.py — drop real card art into AI-generated border
frames and produce a comparison sheet.

Expects generated frames in pipeline/work/card_frames/frame_*.png
(portrait, ornate border, solid near-black empty window in the center).

For each frame: detects the central dark window by flood fill, pastes real
card art under it, overlays minimal card UI (cost, name, stats), and writes
pipeline/work/card_frames/preview_<n>.png plus one combined
frame_options_sheet.png for Telegram.

Run from repo root:  python3 tools/compose_frame_options.py
"""
from __future__ import annotations

import glob
import os
import sys
from collections import deque

from PIL import Image, ImageDraw, ImageFilter, ImageFont

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


def detect_window(frame, thresh=48, scale=4):
    """Flood-fill the dark central window; return (mask, bbox) at full res.
    Returns None if the centre is not dark or the window is tiny."""
    small = frame.convert("L").resize((frame.width // scale, frame.height // scale))
    px = small.load()
    W, H = small.size
    cx, cy = W // 2, H // 2
    if px[cx, cy] > thresh:
        print(f"  centre pixel {px[cx, cy]} > {thresh} — not dark enough")
        return None
    seen = [[False] * H for _ in range(W)]
    q = deque([(cx, cy)])
    seen[cx][cy] = True
    pts = []
    while q:
        x, y = q.popleft()
        pts.append((x, y))
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if 0 <= nx < W and 0 <= ny < H and not seen[nx][ny] and px[nx, ny] <= thresh:
                seen[nx][ny] = True
                q.append((nx, ny))
    if len(pts) < (W * H) * 0.15:
        print(f"  flood fill only {len(pts)}/{W*H} pixels — window too small")
        return None
    m = Image.new("L", (W, H), 0)
    mp = m.load()
    for x, y in pts:
        mp[x, y] = 255
    m = m.resize(frame.size, Image.LANCZOS)
    m = m.filter(ImageFilter.MinFilter(5))      # shrink: keep frame edge crisp
    m = m.filter(ImageFilter.GaussianBlur(2))
    xs = [p[0] * scale for p in pts]
    ys = [p[1] * scale for p in pts]
    return m, (min(xs), min(ys), max(xs) + scale, max(ys) + scale)


def cinzel(sz):
    return ImageFont.truetype(CINZEL, sz)


def compose(frame_path, card_id, name, cost, atk, hp, out_size=(440, 643)):
    frame = Image.open(frame_path).convert("RGB").resize((832, 1216), Image.LANCZOS)
    det = detect_window(frame)
    if det is None:
        print(f"WARN: no clean dark window found in {frame_path} — skipping", file=sys.stderr)
        return None
    mask, (x0, y0, x1, y1) = det
    art = fit_cover(Image.open(os.path.join(ART, card_id + ".webp")).convert("RGB"),
                    (x1 - x0, y1 - y0))
    base = frame.copy()
    art_layer = Image.new("RGB", frame.size, (0, 0, 0))
    art_layer.paste(art, (x0, y0))
    base = Image.composite(art_layer, base, mask)

    d = ImageDraw.Draw(base, "RGBA")
    W, H = base.size
    # bottom scrim inside the window for name/stats
    scrim = Image.new("RGBA", (x1 - x0, int((y1 - y0) * 0.26)), (0, 0, 0, 0))
    ds = ImageDraw.Draw(scrim)
    for yy in range(scrim.height):
        a = int(215 * (yy / scrim.height) ** 0.8)
        ds.line([(0, yy), (scrim.width, yy)], fill=(5, 4, 3, a))
    base.paste(scrim, (x0, y1 - scrim.height), scrim)
    d = ImageDraw.Draw(base, "RGBA")
    # cost disc top-right, on the frame corner
    r = 52
    ccx, ccy = W - 96, 100
    d.ellipse([ccx - r, ccy - r, ccx + r, ccy + r], fill=(30, 25, 18), outline=(232, 205, 120), width=6)
    d.text((ccx, ccy + 2), str(cost), font=cinzel(58), fill=(232, 205, 120), anchor="mm")
    # name
    ny = y1 - int((y1 - y0) * 0.085)
    d.text((W // 2 + 2, ny - 56 + 2), name.upper(), font=cinzel(46), fill=(0, 0, 0, 230), anchor="mm")
    d.text((W // 2, ny - 56), name.upper(), font=cinzel(46), fill=(232, 220, 200), anchor="mm")
    # stat chips
    for sx, col, val in [(x0 + 78, (176, 58, 48), atk), (x1 - 78, (76, 138, 76), hp)]:
        d.rounded_rectangle([sx - 56, ny - 20, sx + 56, ny + 44], radius=14, fill=col,
                            outline=(0, 0, 0, 200), width=3)
        d.text((sx, ny + 12), str(val), font=cinzel(46), fill=(255, 255, 255), anchor="mm")
    return base.resize(out_size, Image.LANCZOS)


def main():
    frames = sorted(glob.glob(os.path.join(WORK, "frame_[0-9]*.png")))
    if not frames:
        print(f"no frames in {WORK}", file=sys.stderr)
        sys.exit(1)
    cols = []
    for fp in frames:
        col = [compose(fp, *c, out_size=(440, 643) if i == 0 else (300, 438))
               for i, c in enumerate(CARDS)]
        if any(c is None for c in col):
            continue
        cols.append((os.path.basename(fp), col))
        big = compose(fp, *CARDS[0], out_size=(832, 1216))
        big.save(fp.replace("frame_", "preview_"))
    if not cols:
        print("no usable frames", file=sys.stderr)
        sys.exit(1)
    CW, PAD, TOP = 520, 40, 120
    sheet = Image.new("RGB", (PAD + len(cols) * CW, 1330), (14, 12, 10))
    d = ImageDraw.Draw(sheet)
    d.text((sheet.width // 2, 50), "BORDER OPTIONS — GENERATED",
           font=cinzel(48), fill=(212, 184, 76), anchor="mm")
    for i, (label, col) in enumerate(cols):
        x = PAD + i * CW
        sheet.paste(col[0], (x, TOP))
        sheet.paste(col[1], (x + 110, TOP + 680))
        d.text((x + 220, TOP + 680 + 470), label.replace(".png", "").upper(),
               font=cinzel(30), fill=(184, 168, 138), anchor="mm")
    out = os.path.join(WORK, "frame_options_sheet.png")
    sheet.save(out)
    print("wrote", out, "and per-frame previews")


if __name__ == "__main__":
    main()
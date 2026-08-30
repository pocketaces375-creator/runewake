"""Robust window detection: largest centered rectangle of near-black that
does NOT touch the image edge. Immune to dark painted frames."""
import numpy as np
from PIL import Image, ImageFilter


def detect_window_v2(frame, thresh=14, scale=4, min_frac=0.35, margin_frac=0.02):
    """Return (mask, (x0,y0,x1,y1)) at full res, or None.

    Strategy: from the exact center, grow a rectangle outward one edge at a
    time while that edge's line stays >=92% near-black. A painted frame is
    never uniformly black across a full span, so growth halts at the real
    window boundary even when the frame itself is dark. A window touching
    the image edge is rejected (a border frame always has a band).
    """
    W0, H0 = frame.size
    small = frame.convert("L").resize((W0 // scale, H0 // scale))
    a = np.asarray(small, dtype=np.int16)
    H, W = a.shape
    dark = a <= thresh
    cy, cx = H // 2, W // 2
    if not dark[cy, cx]:
        return None

    x0 = x1 = cx
    y0 = y1 = cy
    grew = True
    while grew:
        grew = False
        if x0 > 0 and dark[y0:y1 + 1, x0 - 1].mean() >= 0.92:
            x0 -= 1
            grew = True
        if x1 < W - 1 and dark[y0:y1 + 1, x1 + 1].mean() >= 0.92:
            x1 += 1
            grew = True
        if y0 > 0 and dark[y0 - 1, x0:x1 + 1].mean() >= 0.92:
            y0 -= 1
            grew = True
        if y1 < H - 1 and dark[y1 + 1, x0:x1 + 1].mean() >= 0.92:
            y1 += 1
            grew = True

    wf = (x1 - x0 + 1) / W
    hf = (y1 - y0 + 1) / H
    if wf < min_frac or hf < min_frac:
        return None                     # window too small / not found
    m = max(1, int(margin_frac * min(W, H)))
    if x0 < m or y0 < m or x1 > W - 1 - m or y1 > H - 1 - m:
        return None                     # touches edge: no real border band

    mask = Image.new("L", (W, H), 0)
    mask.paste(255, (x0, y0, x1 + 1, y1 + 1))
    mask = mask.resize(frame.size, Image.NEAREST).filter(ImageFilter.GaussianBlur(1.5))
    return mask, (x0 * scale, y0 * scale, (x1 + 1) * scale - 1, (y1 + 1) * scale - 1)
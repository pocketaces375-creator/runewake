# FABLE-103: Daily Rewards - the red wax seals become the painting's own bronze Fehu medallion (rune of wealth),
# and the rune-cyan glow is eased back a little. Input: the installed RewardsScene_words plate.
import numpy as np, cv2, sys
from PIL import Image, ImageFilter
src, dst, k = sys.argv[1], sys.argv[2], float(sys.argv[3])
im = Image.open(src).convert('RGB'); a = np.array(im).astype(np.float32)
# 1. ease the cyan: cyan pixels move part-way toward a dimmer, cooler grey of themselves
h = np.array(im.convert('HSV')).astype(np.float32)
hw = np.clip(1 - np.abs(h[...,0] - 125) / 30, 0, 1)
w = hw * np.clip((h[...,1] / 255 - 0.20) / 0.35, 0, 1) * np.clip((h[...,2] / 255 - 0.15) / 0.25, 0, 1)
w = np.array(Image.fromarray((w * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.2))).astype(np.float32) / 255
lum = (a * [0.3, 0.59, 0.11]).sum(2, keepdims=True)
target = np.concatenate([lum * 0.80, lum * 0.92, lum * 0.98], 2) * 0.85
a = a * (1 - k * w[..., None]) + target * (k * w[..., None])
img = np.clip(a, 0, 255).astype(np.uint8)
# 2. the medallion: the top-left corner's Fehu medallion, cut on a feathered circle
MX, MY, MR = 49, 48, 43
def medallion(r):
    m = Image.fromarray(img).crop((MX - MR, MY - MR, MX + MR, MY + MR)).resize((2 * r, 2 * r), Image.LANCZOS)
    yy, xx = np.mgrid[0:2 * r, 0:2 * r]; d = np.sqrt((xx - r + 0.5) ** 2 + (yy - r + 0.5) ** 2)
    alpha = np.clip((r - d) / 1.6, 0, 1)
    return np.array(m).astype(np.float32), alpha
seals = [(1243, 230), (853, 421), (853, 485), (853, 548), (853, 611), (1243, 613)]
out = img.copy()
for (sx, sy) in seals:
    x0, y0, x1, y1 = sx - 48, sy - 48, sx + 48, sy + 48
    win = out[y0:y1, x0:x1].astype(np.int32)
    R, G, B = win[..., 0], win[..., 1], win[..., 2]
    red = ((R > G * 1.45) & (R > B * 1.3) & (R > 55)).astype(np.uint8)
    n, lab, stats, cent = cv2.connectedComponentsWithStats(red, 8)
    if n < 2: print('no seal at', sx, sy); continue
    i = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
    blob = (lab == i).astype(np.uint8)
    cx, cy = cent[i]; area = stats[i, cv2.CC_STAT_AREA]
    r = int(round(np.sqrt(area / np.pi) * 1.06))
    mask = np.zeros(out.shape[:2], np.uint8); mask[y0:y1, x0:x1] = cv2.dilate(blob * 255, np.ones((7, 7), np.uint8))
    out = cv2.inpaint(np.ascontiguousarray(out), mask, 7, cv2.INPAINT_TELEA)
    gx, gy = int(round(x0 + cx)), int(round(y0 + cy))
    med, al = medallion(r)
    # a soft shadow under it, then the medallion
    sh = np.zeros(out.shape[:2], np.float32); cv2.circle(sh, (gx + 2, gy + 3), r, 1.0, -1)
    sh = cv2.GaussianBlur(sh, (0, 0), 3) * 0.55
    o = out.astype(np.float32) * (1 - sh[..., None])
    ys, xs = slice(gy - r, gy + r), slice(gx - r, gx + r)
    o[ys, xs] = o[ys, xs] * (1 - al[..., None]) + med * al[..., None]
    out = np.clip(o, 0, 255).astype(np.uint8)
    print('seal', sx, sy, '->', gx, gy, 'r', r)
Image.fromarray(out).save(dst)

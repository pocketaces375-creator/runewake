# FABLE-096: Deck Forge's four silver Roman coins become the rune medallions of the Settings painting (the same ones Daily Rewards wears)
import sys, numpy as np
from PIL import Image
src, settings, out = sys.argv[1:4]
D = np.array(Image.open(src).convert('RGB')).astype(np.float32)
S = Image.open(settings).convert('RGB')
def medallion(cx, cy, r):
    return S.crop((cx - r, cy - r, cx + r, cy + r))
# the Settings medallions (centre, outer radius) and the coins they replace (centre, radius)
meds = [medallion(70, 61, 56), medallion(1309, 60, 56), medallion(79, 703, 56), medallion(1297, 691, 56)]
coins = [((32, 90), 28), ((1337, 88), 28), ((294, 719), 36), ((1046, 728), 28)]
for m, ((cx, cy), r) in zip(meds, coins):
    R = r + 2
    patch = np.array(m.resize((2 * R, 2 * R), Image.LANCZOS)).astype(np.float32)
    yy, xx = np.mgrid[-R:R, -R:R]
    d = np.sqrt(xx ** 2 + yy ** 2)
    a = np.clip((R - 1.5 - d) / 2.5, 0, 1)[..., None]
    y0, x0 = cy - R, cx - R
    reg = D[y0:y0 + 2 * R, x0:x0 + 2 * R]
    D[y0:y0 + 2 * R, x0:x0 + 2 * R] = reg * (1 - a) + patch * a
Image.fromarray(np.clip(D, 0, 255).astype(np.uint8)).save(out)

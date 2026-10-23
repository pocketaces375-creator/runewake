# FABLE-098: the finishing touches on the 097 paintings - the few painted words and marks that change in play (or doubled
# what the game draws) are painted out with the painting's own neighbouring surface. Deterministic; no generator.
import sys
import numpy as np
from PIL import Image
import cv2
screens = sys.argv[1]
def load(n): return np.array(Image.open(f"{screens}/{n}.webp").convert("RGB")).astype(np.float32)
def save(a, n): Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).save(f"{screens}/{n}.webp", quality=93)
def copy(a, x0, y0, x1, y1, dx, dy, feather=3):
    """paint the box with the same-sized patch dx,dy away, feathered at the edges"""
    patch = a[y0 + dy:y1 + dy, x0 + dx:x1 + dx].copy()
    m = np.zeros(patch.shape[:2], np.float32); f = feather
    m[f:-f or None, f:-f or None] = 1
    m = cv2.GaussianBlur(m, (0, 0), max(1, f / 1.5))[..., None]
    a[y0:y1, x0:x1] = a[y0:y1, x0:x1] * (1 - m) + patch * m
def smooth(a, x0, y0, x1, y1, sx=10, sy=3, grain=3.0, feather=5, seed=98):
    pad = 24; H, W = a.shape[:2]
    X0, Y0, X1, Y1 = max(0, x0 - pad), max(0, y0 - pad), min(W, x1 + pad), min(H, y1 + pad)
    reg = a[Y0:Y1, X0:X1]
    # fill the box from the rows just above and below it (wood grain runs across), then soften
    fill = reg.copy()
    top, bot = reg[y0 - Y0 - 6:y0 - Y0 - 2].mean(0), reg[y1 - Y0 + 2:y1 - Y0 + 6].mean(0)
    for i, y in enumerate(range(y0 - Y0, y1 - Y0)):
        t = (i + 0.5) / max(1, (y1 - y0))
        fill[y] = top * (1 - t) + bot * t
    fill = cv2.GaussianBlur(fill, (0, 0), sigmaX=sx / 3, sigmaY=sy / 3)
    fill += np.random.default_rng(seed).normal(0, grain, fill.shape[:2])[..., None]
    m = np.zeros(reg.shape[:2], np.float32); m[y0 - Y0:y1 - Y0, x0 - X0:x1 - X0] = 1
    m = cv2.GaussianBlur(m, (0, 0), feather)[..., None]
    a[Y0:Y1, X0:X1] = reg * (1 - m) + fill * m

# Settings: the four arrows where the sliders' percentages go, and the arrow under the Reduce-motion switch
s = load("SettingsScene_words")
for y in (296, 353, 410, 467): copy(s, 632, y - 13, 654, y + 13, -24, 0)
copy(s, 1084, 288, 1110, 312, -30, 0)
save(s, "SettingsScene_words")

# Daily Rewards: today's tally and the chest's "claim everything" line are the game's to write (they change)
r = load("RewardsScene_words")
smooth(r, 100, 357, 424, 389, feather=2)
smooth(r, 976, 533, 1276, 559, sy=2, feather=2)
save(r, "RewardsScene_words")

# Deck Forge: "✓ Playing" (the game writes Play this / Playing), and the gold ring painted on the first card slot
d = load("DeckBuilderScene_words")
smooth(d, 1080, 709, 1148, 735, sx=6, sy=2, grain=2, feather=2)
copy(d, 366, 116, 492, 294, 104, 0, feather=3)
# the last sliver of that ring, left of the box: inpainted from the felt around it
box = d[110:300, 356:386].astype(np.uint8)
lum = box.mean(2); mask = ((lum > np.median(lum) + 22)).astype(np.uint8) * 255
mask = cv2.dilate(mask, np.ones((3, 3), np.uint8))
d[110:300, 356:386] = cv2.inpaint(np.ascontiguousarray(box), mask, 4, cv2.INPAINT_TELEA).astype(np.float32)
save(d, "DeckBuilderScene_words")
print("098 plates finished")

# Deck Forge tray: the painting has 27 empty deck cells (9 x 3) but a deck holds 30 - the game lays its own 30, so the
# painted cells become plain tray wood (the empty strip of the same tray, mirrored across)
d = load("DeckBuilderScene_words")
src = d[570:648, 338:438].copy()                                  # clean tray wood left of the artifacts (under YOUR DECK)
H, W = 737 - 543, 1032 - 618
tile = np.concatenate([src, src[:, ::-1]], axis=1)
tile = np.concatenate([tile, tile[::-1]], axis=0)
reps = (int(np.ceil(H / tile.shape[0])) + 1, int(np.ceil(W / tile.shape[1])) + 1, 1)
wood = np.tile(tile, reps)[:H, :W]
wood = cv2.GaussianBlur(wood, (0, 0), 0.6)
m = np.zeros((H, W), np.float32); m[5:-5, 5:-5] = 1; m = cv2.GaussianBlur(m, (0, 0), 3)[..., None]
d[543:737, 618:1032] = d[543:737, 618:1032] * (1 - m) + wood * m
save(d, "DeckBuilderScene_words")
print("098 tray cells painted out")

# Daily Rewards: the painting kept "Open Chest" on its plaque - the game writes it (it becomes "Opened")
r = load("RewardsScene_words")
smooth(r, 1066, 597, 1190, 628, sx=8, sy=2, grain=2, feather=2)
save(r, "RewardsScene_words")
print("098 open-chest word painted out")

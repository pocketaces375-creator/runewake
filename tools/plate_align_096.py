# FABLE-096: does an edited painting still line up with the one it was edited from? Edge maps of a region are compared
# at small shifts; the edit passes when the best shift is tiny and most edges agree. numpy + PIL only.
import sys
import numpy as np
from PIL import Image
def edges(path, box):
    im = Image.open(path).convert('L').resize((1376, 768), Image.LANCZOS)
    a = np.asarray(im, dtype=np.float32)[box[1]:box[3], box[0]:box[2]]
    gx = np.abs(np.diff(a, axis=1))[:-1, :]; gy = np.abs(np.diff(a, axis=0))[:, :-1]
    g = gx + gy
    return g > max(18.0, np.percentile(g, 90))
def check(orig, new, box, name):
    A, B = edges(orig, box), edges(new, box)
    self_score = (A & A).sum()
    best, bs = (0, 0), -1
    for dy in range(-8, 9):
        for dx in range(-8, 9):
            S = np.roll(np.roll(B, dy, axis=0), dx, axis=1)
            sc = (A & S).sum()
            if sc > bs: bs, best = sc, (dx, dy)
    ratio = bs / max(1, self_score)
    ok = abs(best[0]) <= 2 and abs(best[1]) <= 2 and ratio >= 0.72   # a real word-erasing edit scores ~0.85; a different painting ~0.58
    print(f"{name}: shift={best} agreement={ratio:.2f} -> {'OK' if ok else 'MISALIGNED'}")
    return ok
if __name__ == "__main__":
    orig, new, kind = sys.argv[1:4]
    boxes = {"settings": [(718, 190, 1110, 620, "right column"), (240, 190, 680, 610, "left column")],
             "deckforge": [(320, 70, 1050, 520, "card slots"), (1060, 90, 1350, 750, "right rail")]}[kind]
    sys.exit(0 if all(check(orig, new, b[:4], b[4]) for b in boxes) else 1)

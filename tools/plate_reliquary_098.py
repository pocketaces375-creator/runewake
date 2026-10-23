# FABLE-098: the Reliquary painting with its own lettering - the approved codex (v2) with its cards, numbers and counts
# taken from the 087 clean page (same painting, cards erased), the shards count painted out, and the two lit tabs
# (Cards, Everything) turned to the dark style of the rest so the game's gold ring alone shows the choice.
import sys
import numpy as np
from PIL import Image
import cv2
v2, clean, out = sys.argv[1:4]
A = np.array(Image.open(v2).convert("RGB").resize((1376, 768), Image.LANCZOS)).astype(np.float32)
C = np.array(Image.open(clean).convert("RGB").resize((1376, 768), Image.LANCZOS)).astype(np.float32)
def paste(x0, y0, x1, y1, f=6):
    m = np.zeros((y1 - y0, x1 - x0), np.float32); m[f:-f, f:-f] = 1
    m = cv2.GaussianBlur(m, (0, 0), f / 2)[..., None]
    A[y0:y1, x0:x1] = A[y0:y1, x0:x1] * (1 - m) + C[y0:y1, x0:x1] * m
paste(250, 203, 1128, 590)          # the two pages' card rows: cards, numbers, NEW tags, captions
paste(915, 160, 1125, 212, f=4)     # the ribbon's count and bar
# the shards plaque: its words painted out with the plaque's own dark interior
y0, y1, x0, x1 = 707, 733, 628, 760
top, bot = A[y0 - 4:y0 - 1, x0:x1].mean(0), A[y1 + 1:y1 + 4, x0:x1].mean(0)
fill = np.stack([top * (1 - (i + .5) / (y1 - y0)) + bot * ((i + .5) / (y1 - y0)) for i in range(y1 - y0)])
fill = cv2.GaussianBlur(fill, (0, 0), 2) + np.random.default_rng(98).normal(0, 2, fill.shape[:2])[..., None]
m = np.zeros(fill.shape[:2], np.float32); m[2:-2, 3:-3] = 1; m = cv2.GaussianBlur(m, (0, 0), 1.5)[..., None]
A[y0:y1, x0:x1] = A[y0:y1, x0:x1] * (1 - m) + fill * m
# the two lit tabs -> dark wood with cream words, like their neighbours
dark = np.array([58, 50, 40], np.float32); cream = np.array([226, 214, 182], np.float32)
for (x0, y0, x1, y1) in [(320, 89, 393, 127), (824, 97, 892, 122)]:
    reg = A[y0:y1, x0:x1]; L = reg.mean(2)
    bg = np.median(L); dev = np.abs(L - bg)
    t = np.clip(dev / max(12.0, np.percentile(dev, 99)), 0, 1)[..., None]
    t = np.clip((t - 0.15) / 0.7, 0, 1)
    new = dark * (1 - t) + cream * t
    m = np.zeros(L.shape, np.float32); m[2:-2, 2:-2] = 1; m = cv2.GaussianBlur(m, (0, 0), 1.2)[..., None]
    A[y0:y1, x0:x1] = reg * (1 - m) + new * m
Image.fromarray(np.clip(A, 0, 255).astype(np.uint8)).save(out, quality=93)
print("reliquary words plate", out)

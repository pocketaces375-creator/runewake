#!/usr/bin/env python3
"""
art_similarity.py — measure how alike the finished card paintings LOOK.

card_art_prompt.py makes the prompts different; this checks the pictures that come
back. It compares composition, not content: where the light and dark masses sit and
where the busy detail (the subject) is. Two paintings of different monsters, both a
lone figure dead centre against a dark wall, score as near-twins here — which is
exactly the sameness players notice in a hand of cards.

  art_similarity.py [--dir client/content/art] [--prefix emb_] [--threshold 0.90] [--top 10]

Per stratum it prints:
  centred      share of paintings whose subject sits in the middle fifth of the width
  layout       mean correlation of each painting with its closest sibling (1.0 = identical layout)
  twins        pairs above --threshold
Exit code 1 if any stratum has more than --max-twins pairs above the threshold (usable as a gate
on a freshly generated wave).
"""
import argparse
import collections
import glob
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

PREFIX_STRATA = {"emb": "EMBER", "tid": "TIDE", "hol": "HOLLOW", "vrd": "VERDANT", "dwn": "DAWN"}


def features(path):
    im = Image.open(path).convert("L").resize((64, 94))
    a = np.asarray(im, dtype=np.float32) / 255.0
    # 1. layout: 8x12 value map, mean-removed and normalised
    small = np.asarray(im.resize((8, 12)), dtype=np.float32)
    lay = small.flatten() - small.mean()
    lay /= (np.linalg.norm(lay) + 1e-6)
    # 2. where the detail is: edge energy centre of mass
    edges = np.asarray(im.filter(ImageFilter.FIND_EDGES), dtype=np.float32) ** 2   # squared: the subject's hard edges outweigh background texture
    edges[:3, :] = edges[-3:, :] = 0
    edges[:, :3] = edges[:, -3:] = 0
    w = edges.sum() + 1e-6
    ys, xs = np.mgrid[0:edges.shape[0], 0:edges.shape[1]]
    cx = float((edges * xs).sum() / w) / edges.shape[1]
    cy = float((edges * ys).sum() / w) / edges.shape[0]
    return lay, cx, cy, float(a.mean())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", default="client/content/art")
    ap.add_argument("--prefix", default="")
    ap.add_argument("--threshold", type=float, default=0.90)
    ap.add_argument("--max-twins", type=int, default=10**9)
    ap.add_argument("--top", type=int, default=6)
    a = ap.parse_args()

    files = sorted(f for f in glob.glob(os.path.join(a.dir, f"{a.prefix}*.webp")) + glob.glob(os.path.join(a.dir, f"{a.prefix}*.png"))
                   if os.path.basename(f)[:3] in PREFIX_STRATA and "_x_" not in f)
    groups = collections.defaultdict(list)
    for f in files:
        groups[PREFIX_STRATA[os.path.basename(f)[:3]]].append((os.path.basename(f).rsplit(".", 1)[0], *features(f)))

    bad = False
    for strata, items in sorted(groups.items()):
        if len(items) < 2:
            continue
        L = np.stack([x[1] for x in items])
        C = L @ L.T
        np.fill_diagonal(C, -1)
        nearest = C.max(axis=1)
        centred = sum(1 for x in items if 0.4 <= x[2] <= 0.6) / len(items)
        twins = [(C[i, j], items[i][0], items[j][0]) for i in range(len(items)) for j in range(i + 1, len(items)) if C[i, j] >= a.threshold]
        twins.sort(reverse=True)
        print(f"{strata:<8} {len(items):>3} paintings   centred {centred:4.0%}   layout vs closest sibling {nearest.mean():.2f}   "
              f"twins(>={a.threshold:.2f}) {len(twins)}")
        for s, x, y in twins[:a.top]:
            print(f"           {s:.2f}  {x}  ~  {y}")
        if len(twins) > a.max_twins:
            bad = True
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())

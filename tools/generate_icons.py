"""
TASK-ART-ICONS-1: Generate matching icon set for 11 keywords + 5 strata.
Style: Runewake archaeological high-fantasy — carved stone runes on transparent,
golden-amber hues matching FrameHexBorder (#C9A84C).
All icons: 48x48 RGBA, transparent bg, gold (#C9A84C) lines/shapes.
"""

from PIL import Image, ImageDraw
import os, math

SIZE = 48
OUT = "client/content/art/icons"
GOLD = (201, 168, 76, 255)  # #C9A84C
TRANS = (0, 0, 0, 0)

def make_icon(name, draw_fn):
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    draw_fn(draw, SIZE)
    path = os.path.join(OUT, f"{name}.webp")
    img.save(path, "WEBP", lossless=True, quality=100)
    # Verify
    verify = Image.open(path)
    assert verify.mode == "RGBA", f"{name}: mode is {verify.mode}, expected RGBA"
    print(f"  ok {name}.webp ({verify.size}, {verify.mode})")

# ─── Helpers ───

def line(d, pts, w=2):
    """Draw a polyline with gold color."""
    for i in range(len(pts) - 1):
        d.line([pts[i], pts[i+1]], fill=GOLD, width=w)

def polygon(d, pts, w=2):
    """Draw an outlined polygon (no fill) with gold."""
    d.polygon(pts, outline=GOLD, fill=None, width=w)

def filled_polygon(d, pts):
    """Draw a filled gold polygon."""
    d.polygon(pts, fill=GOLD)

def ellipse(d, bbox, w=2):
    """Draw an outlined ellipse."""
    d.ellipse(bbox, outline=GOLD, fill=None, width=w)

def filled_ellipse(d, bbox):
    """Draw a filled gold ellipse."""
    d.ellipse(bbox, fill=GOLD)

def gold_pt(d, cx, cy, r=2):
    """Draw a filled gold dot."""
    d.ellipse([cx-r, cy-r, cx+r, cy+r], fill=GOLD)

# ──────────────────────────────────────────────
# Keywords (11)
# ──────────────────────────────────────────────

# GUARD — shield shape (hollow)
def guard(d, s):
    cx, cy = s//2, s//2
    pts = [(cx, 4), (s-6, cy//2), (s-6, cy+6), (cx, s-4), (6, cy+6), (6, cy//2)]
    polygon(d, pts, 3)

# SWIFT — lightning bolt (filled)
def swift(d, s):
    pts = [(s//2+2, 4), (12, s//2+2), (s//2+1, s//2), (s//2-2, s-4),
           (s//2+4, s//2+4), (s//2, s//2)]
    filled_polygon(d, pts)

# PIERCE — downward arrowhead (hollow)
def pierce(d, s):
    pts = [(s//2, s-4), (6, 6), (s//2, 14), (s-6, 6)]
    polygon(d, pts, 3)

# WARD — circle with inner dot
def ward(d, s):
    ellipse(d, [6, 6, s-6, s-6], 3)
    filled_ellipse(d, [s//2-3, s//2-3, s//2+3, s//2+3])

# VENOM — droplet (hollow)
def venom(d, s):
    pts = [(s//2, 4), (s-6, s-6), (s//2, s-3), (6, s-6)]
    polygon(d, pts, 3)

# REACH — two connected chain links
def reach(d, s):
    ellipse(d, [4, s//3-4, s//2, s//3+10], 3)
    ellipse(d, [s//2, s//3-4, s-4, s//3+10], 3)

# ROOTED — three roots spreading downward (lines)
def rooted(d, s):
    cx = s//2
    line(d, [(cx, 4), (cx, s//2)], 3)
    line(d, [(cx, s//2-2), (8, s-6)], 3)
    line(d, [(cx, s//2-2), (s-8, s-6)], 3)
    line(d, [(cx, s//2-2), (cx, s-6)], 3)

# UNEARTH — upward arrow breaking ground (filled)
def unearth(d, s):
    pts = [(s//2, 4), (s-6, s//2-2), (s//2+4, s//2-2), (s//2+4, s-4),
           (s//2-4, s-4), (s//2-4, s//2-2), (6, s//2-2)]
    filled_polygon(d, pts)

# ECHO — concentric arcs
def echo(d, s):
    cx, cy = s//2, s//2
    for r in [12, 18, 24]:
        bbox = [cx-r, cy-r, cx+r, cy+r]
        d.arc(bbox, -60, 60, fill=GOLD, width=3)

# FRAGILE — cracked line
def fragile(d, s):
    cx, cy = s//2, s//2
    line(d, [(cx-10, 4), (cx+2, cy-4), (cx-4, cy+2), (cx+10, s-4)], 3)
    line(d, [(cx+2, cy-4), (cx+12, cy-2)], 2)
    line(d, [(cx-4, cy+2), (cx-12, cy+6)], 2)

# SEALED — lock/latch (hollow)
def sealed(d, s):
    d.arc([s//2-8, 6, s//2+8, 20], 180, 360, fill=GOLD, width=4)
    d.rectangle([s//2-9, 18, s//2+9, s-8], outline=GOLD, width=3)
    filled_ellipse(d, [s//2-3, s//2-1, s//2+3, s//2+5])
    d.rectangle([s//2-1, s//2+2, s//2+1, s//2+8], fill=GOLD)

# ──────────────────────────────────────────────
# Strata (5)
# ──────────────────────────────────────────────

# VERDANT — leaf (hollow + center vein)
def verdant(d, s):
    pts = [(s//2, 4), (s-6, s//2-2), (s-6, s-6), (s//2, s-3), (6, s-6), (6, s//2-2)]
    polygon(d, pts, 3)
    line(d, [(s//2, 4), (s//2, s-3)], 2)

# EMBER — flame (hollow + inner flicker)
def ember(d, s):
    pts = [(s//2, 4), (s-4, s//2+2), (s-4, s-6), (s//2, s-3), (4, s-6), (4, s//2+2)]
    polygon(d, pts, 3)
    filled_polygon(d, [(s//2, 12), (s//2+6, s//2), (s//2, s//2+6), (s//2-6, s//2)])

# TIDE — three wave arcs
def tide(d, s):
    d.arc([-10, s//2-6, 20, s//2+8], -30, 210, fill=GOLD, width=3)
    d.arc([14, s//2-8, 34, s//2+6], -30, 210, fill=GOLD, width=3)
    d.arc([28, s//2-8, 56, s//2+6], -30, 210, fill=GOLD, width=3)

# HOLLOW — crescent moon (two opposing arcs)
def hollow(d, s):
    d.pieslice([4, 4, s-4, s-4], -135, 135, outline=GOLD, width=3)
    d.pieslice([4, 4, s-4, s-4], 45, 315, outline=GOLD, width=3)

# DAWN — sun/star rays
def dawn(d, s):
    cx, cy = s//2, s//2
    for i in range(8):
        angle = i * 45
        rad = math.radians(angle)
        x1 = cx + 4 * math.cos(rad)
        y1 = cy + 4 * math.sin(rad)
        x2 = cx + 18 * math.cos(rad)
        y2 = cy + 18 * math.sin(rad)
        d.line([(x1, y1), (x2, y2)], fill=GOLD, width=3)
    filled_ellipse(d, [cx-5, cy-5, cx+5, cy+5])


# ─── Register all ───
keywords = {
    "kw_guard": guard,
    "kw_swift": swift,
    "kw_pierce": pierce,
    "kw_ward": ward,
    "kw_venom": venom,
    "kw_reach": reach,
    "kw_rooted": rooted,
    "kw_unearth": unearth,
    "kw_echo": echo,
    "kw_fragile": fragile,
    "kw_sealed": sealed,
}

strata = {
    "str_verdant": verdant,
    "str_ember": ember,
    "str_tide": tide,
    "str_hollow": hollow,
    "str_dawn": dawn,
}

os.makedirs(OUT, exist_ok=True)

for name, fn in {**keywords, **strata}.items():
    make_icon(name, fn)

print(f"\nDone: {len(keywords) + len(strata)} icons in {OUT}/")
#!/usr/bin/env python3
"""
tools/build_intro_splash.py — build the skippable intro page (intro_splash.png).

Takes the chosen title art, darkens it, overlays the canon intro copy
as centered Cinzel text, and adds faint carved glyph columns at margins.

Output: client/content/art/title/intro_splash.png (2316x1080)
Run from repo root:  python3 tools/build_intro_splash.py
"""
from __future__ import annotations

import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ART = os.path.join(ROOT, "client", "content", "art")
TITLE_ART = os.path.join(ART, "title", "hero_art.png")
CINZEL = os.path.join(ROOT, "client", "assets", "fonts", "Cinzel.ttf")
OUT = os.path.join(ART, "title", "intro_splash.png")

W, H = 2316, 1080

CANON = (
    "Before the maps had edges, the Old Age sang its last.",
    "Mountains knelt. Seas traded places with the sky.",
    "And the world, grown weary of its own wonders, buried them —",
    "its weapons, its wards, its wandering gods —",
    "and lay down over them like a stone upon a grave.",
    "",
    "Ages passed. The grave grew fields. The fields grew kingdoms.",
    "And the kingdoms forgot what slept beneath their feet.",
    "",
    "Now the runes are waking.",
    "The seals thin. The barrows hum at night.",
    "And what the Old Age buried is digging its way back.",
    "",
    "THE BURIED AGE — tap to continue",
)


def cz(sz: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(CINZEL, sz)


def main():
    # Load title art, crop/scale to 16:9
    bg = Image.open(TITLE_ART).convert("RGB")
    # Target aspect 2316:1080 ≈ 2.144
    target_ar = W / H
    img_ar = bg.width / bg.height

    if img_ar > target_ar:
        # Wider than target — crop sides
        new_w = int(bg.height * target_ar)
        x = (bg.width - new_w) // 2
        bg = bg.crop((x, 0, x + new_w, bg.height))
    elif img_ar < target_ar:
        # Taller than target — crop top/bottom
        new_h = int(bg.width / target_ar)
        y = (bg.height - new_h) // 2
        bg = bg.crop((0, y, bg.width, y + new_h))

    bg = bg.resize((W, H), Image.LANCZOS)

    # Darken to ~60%
    bg = bg.point(lambda p: int(p * 0.50))

    # Fade edges to black
    base = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    base.paste(bg, (0, 0))
    d = ImageDraw.Draw(base, "RGBA")

    # Vignette
    for y in range(H):
        t = abs(y - H / 2) / (H / 2)
        a = int(min(255, max(0, t * t * 200)))
        d.line([(0, y), (W, y)], fill=(0, 0, 0, a))
    for x in range(W):
        t = abs(x - W / 2) / (W / 2)
        a = int(min(255, max(0, t * t * 160)))
        d.line([(x, 0), (x, H)], fill=(0, 0, 0, a))

    # Faint carved glyph columns at margins
    gd = ImageDraw.Draw(base, "RGBA")
    gfont = cz(72)
    glyphs = "☿ ♀ ♁ ♂ ♃ ♄ ⚷ ⚸ ⚹ ⚺ ⚻ ⚼"
    g_cols = glyphs.split()
    # Left column
    gx = 60
    for i, g in enumerate(g_cols):
        gy = 80 + i * 120
        gd.text((gx, gy), g, font=gfont, fill=(100, 80, 40, 40), anchor="mm")
    # Right column
    gx = W - 60
    for i, g in enumerate(g_cols):
        gy = 80 + i * 120
        gd.text((gx, gy), g, font=gfont, fill=(100, 80, 40, 40), anchor="mm")

    # Text rendering
    d = ImageDraw.Draw(base, "RGBA")

    # Title line — large
    title_font = cz(52)
    d.text((W // 2, 120), "THE BURIED AGE", font=title_font,
           fill=(212, 184, 76, 255), anchor="mm")

    # Body text — centered, slightly smaller
    body_font = cz(40)
    line_h = 66
    start_y = 240

    for i, line in enumerate(CANON):
        y = start_y + i * line_h
        if not line:
            continue
        if line == "THE BURIED AGE — tap to continue":
            continue  # Skip, handled below as the closing line

        # Soft shadow
        d.text((W // 2 + 2, y + 2), line, font=body_font, fill=(0, 0, 0, 200), anchor="mm")
        d.text((W // 2, y), line, font=body_font, fill=(200, 190, 170, 240), anchor="mm")

    # Closing line — smaller, faded
    close_font = cz(32)
    close_y = start_y + len(CANON) * line_h + 40
    d.text((W // 2 + 2, close_y + 2), "THE BURIED AGE — tap to continue",
           font=close_font, fill=(0, 0, 0, 200), anchor="mm")
    d.text((W // 2, close_y), "THE BURIED AGE — tap to continue",
           font=close_font, fill=(201, 168, 76, 220), anchor="mm")

    base.convert("RGB").save(OUT, quality=92)
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
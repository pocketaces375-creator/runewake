#!/usr/bin/env python3
"""FABLE-070: painted art for the Ferryman's Toll (the Card Packs page and the pack-opening rite).

Trikzos: "epic god, sacrificial coin to the deity in the river Styx / cliffs of the ancient kind of
energy … always use image generation for visual design, we need everything looking peak."

  generate   paint candidates for each piece through OpenRouter (FLUX.2 Pro and Gemini 3 Pro Image,
             the same generators as the card art), write one numbered contact sheet per piece, and print
             the sheet paths. NOTHING is installed — Trikzos picks.
  install    install the picked candidates into client/content/art/styx/ (the game uses them the moment
             they exist and falls back to the drawn scene when they don't):
               backdrop.webp  the whole scene, cover-cropped to the screen
               coin.png       the drachma, background removed
               stele.png      the offer tablet, background removed
               rune.png       the rune stone that rises with each card, background removed

Run on the Hermes box (OPENROUTER_API_KEY from the environment or ~/.hermes/.env):
  python3 tools/styx_art.py generate [--only backdrop,coin] [--n 3]
  python3 tools/styx_art.py install --backdrop 2 --coin 1 --stele 3 --rune 2
Candidates live in ~/runewake_art_archive/styx/ (outside the repo, so a reset can't delete them).
"""
import argparse
import os
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

REPO = Path(__file__).resolve().parent.parent
WORK = Path(os.environ.get("STYX_ART_WORK", str(Path.home() / "runewake_art_archive" / "styx")))
OUT = REPO / "client" / "content" / "art" / "styx"
GEN = REPO / "pipeline" / "gen_image_any.py"

STYLE = ("epic dark-fantasy painterly oil painting, hand-painted textured brushwork, dramatic chiaroscuro, "
         "deep shadow with teal spirit-light and burning ember-gold accents, ancient Greek grandeur with a touch of "
         "Norse rune-carving, cinematic, masterpiece detail, no text, no letters, no numbers, no watermark, no signature")
ISOLATE = "isolated and centred on a pure flat black background, nothing else in the frame, front-on view"

PIECES = {
    # name: (aspect for chat models, (w, h) for FLUX, [prompts])
    "backdrop": ("21:9", (1792, 832), [
        "the river Styx at night, seen from a high ledge: a colossal ancient gorge of sheer black cliffs, "
        "titanic weathered statues of hooded gods carved into both cliff walls, Greek colonnades and temple "
        "facades cut into the rock, braziers burning on distant ledges, far below a broad slow river glowing "
        "with ghostly teal light and drifting spirit-mist, a lone ferryman's boat with a single lantern in the "
        "middle distance; in the LOWER-LEFT FOREGROUND a broken marble sacrificial altar at the very edge of the "
        "cliff with an empty offering bowl and a carved Norse runestone beside it; the TOP THIRD is open night "
        "sky with a few stars and drifting cloud, left empty for a title; the RIGHT HALF is the open chasm over "
        "the river. " + STYLE,
        "a vast underworld canyon where the river Styx winds between cliffs of black basalt: on the far cliff a "
        "colossal statue of a stern bearded god of the dead, seated on a throne, one enormous open palm held out "
        "over the river as if waiting for payment; the river shimmers teal with souls; ruined Greek temples and "
        "cliff-tombs glow with ember light; in the LOWER-LEFT FOREGROUND a cracked marble altar on the cliff edge, "
        "two fluted columns and a leaning runestone with faintly glowing runes; open starry sky across the top; "
        "the right half falls away into the gorge. " + STYLE,
        "twilight over the gates of the underworld: twin cliffs of dark stone flank the river Styx like a "
        "cathedral, carved with a giant hooded ferryman figure holding an oar on one wall and a giant mourning "
        "goddess on the other, waterfalls of pale spirit-light pouring into the gorge, teal mist rising, a tiny "
        "ferry boat with a lantern far below; in the LOWER-LEFT FOREGROUND a weathered marble offering altar on "
        "the edge with a bronze bowl, laurel and a runestone; dark open sky in the top third with a pale moon "
        "half-hidden by cloud. " + STYLE,
    ]),
    "coin": ("1:1", (1024, 1024), [
        "a single ancient gold drachma coin seen face-on, struck with the stern bearded profile of the god of the "
        "underworld wearing a laurel crown, worn raised rim with a Greek key border and tiny rune marks, rich "
        "burnished gold catching candlelight, highly detailed, " + ISOLATE + ", " + STYLE,
        "a single ancient gold coin for the ferryman, face-on: a hooded ferryman with an oar on a tiny boat struck "
        "in relief, a ring of small Elder Futhark runes around the rim, burnished gold with a glow of teal light "
        "in the recesses, highly detailed, " + ISOLATE + ", " + STYLE,
        "a single ancient gold drachma seen face-on, struck with a great owl with spread wings in relief, laurel "
        "sprig, worn Greek key rim, burnished antique gold with deep shadows, highly detailed, " + ISOLATE + ", " + STYLE,
    ]),
    "stele": ("9:16", (832, 1408), [
        "a tall ancient carved marble funerary stele with a triangular Greek pediment on top, a large blank smooth "
        "central face left empty for an inscription, weathered dark grey-white marble, a carved Greek key border "
        "around the face, faintly glowing teal Norse runes running down both edges, thin gold-leaf trim, "
        + ISOLATE + ", " + STYLE,
        "a tall dark basalt votive stele with a Greek temple pediment top and a small carved owl in the pediment, "
        "a large plain empty central panel, gold inlay lines framing the panel, rune-carved pilasters on both "
        "sides glowing faint teal, chipped ancient edges, " + ISOLATE + ", " + STYLE,
        "a tall ancient stone tablet shaped like a temple front, triangular pediment with laurel carving, a large "
        "smooth blank face for writing, bronze-and-gold edging, carved knotwork mixed with Greek meander along the "
        "sides, softly lit by ember light from below, " + ISOLATE + ", " + STYLE,
    ]),
    "rune": ("2:3", (832, 1216), [
        "a rectangular slab of dark slate, a rune stone with chipped edges, one deep-carved Elder Futhark rune in "
        "the centre glowing white-hot from within, a faint carved knotwork border, wisps of light rising from the "
        "carving, " + ISOLATE + ", " + STYLE,
        "an ancient rune tablet of black volcanic stone, rough rectangular, a single large rune carved in the centre "
        "filled with molten pale light, thin Greek key border cut into the stone, ember sparks, " + ISOLATE + ", " + STYLE,
    ]),
}
MODELS = ["black-forest-labs/flux.2-pro", "google/gemini-3-pro-image"]


def env():
    e = dict(os.environ)
    if not e.get("OPENROUTER_API_KEY"):
        f = Path(os.environ.get("ART_ENV_FILE", str(Path.home() / ".hermes" / ".env")))
        if f.exists():
            for line in f.read_text().splitlines():
                if line.startswith("OPENROUTER_API_KEY="):
                    e["OPENROUTER_API_KEY"] = line.split("=", 1)[1].strip().strip('"').strip("'")
    return e


def generate(only, n):
    e = env()
    if not e.get("OPENROUTER_API_KEY"):
        sys.exit("OPENROUTER_API_KEY not found (environment or ~/.hermes/.env)")
    sheets = []
    for name, (aspect, (w, h), prompts) in PIECES.items():
        if only and name not in only:
            continue
        d = WORK / name
        d.mkdir(parents=True, exist_ok=True)
        made = []
        for i in range(n):
            prompt = prompts[i % len(prompts)]
            model = MODELS[i % len(MODELS)]
            out = d / f"{name}_{i + 1}.png"
            cmd = [sys.executable, str(GEN), prompt, str(out), "--model", model, "--width", str(w), "--height", str(h), "--aspect", aspect]
            try:
                r = subprocess.run(cmd, env=e, capture_output=True, text=True, timeout=420)
                ok = r.returncode == 0 and out.exists()
            except subprocess.TimeoutExpired:
                ok = False
                r = None
            print(f"{name} {i + 1} via {model}: {'ok' if ok else 'FAILED ' + ((r.stderr or '')[-200:] if r else 'timeout')}", flush=True)
            if ok:
                made.append(out)
        if made:
            sheets.append(sheet(name, made))
    for s in sheets:
        print(f"SHEET {s}")


def sheet(name, paths):
    """One image with every candidate, numbered, on a dark field — what Trikzos picks from."""
    thumbs = []
    for p in paths:
        im = Image.open(p).convert("RGB")
        im.thumbnail((900, 520) if name == "backdrop" else (420, 560))
        thumbs.append(im)
    cols = 1 if name == "backdrop" else len(thumbs)
    tw = max(t.width for t in thumbs)
    th = max(t.height for t in thumbs)
    rows = (len(thumbs) + cols - 1) // cols
    W, H = cols * (tw + 30) + 30, rows * (th + 70) + 80
    S = Image.new("RGB", (W, H), (18, 14, 10))
    d = ImageDraw.Draw(S)
    try:
        f = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 34)
    except OSError:
        f = ImageFont.load_default()
    d.text((30, 20), f"{name.upper()} — reply with the number you like", fill=(240, 205, 120), font=f)
    for k, t in enumerate(thumbs):
        x = 30 + (k % cols) * (tw + 30)
        y = 80 + (k // cols) * (th + 70)
        S.paste(t, (x, y + 44))
        d.text((x, y), f"#{k + 1}", fill=(255, 230, 160), font=f)
    out = WORK / f"sheet_{name}.jpg"
    S.save(out, quality=88)
    return out


def cut_out(src, dst, max_side):
    """Remove the flat black background: flood-fill from the border through near-black, then feather.
    (A flood fill, not a colour key — dark stone inside the object stays solid.)"""
    im = Image.open(src).convert("RGBA")
    im.thumbnail((max_side, max_side), Image.LANCZOS)
    gray = im.convert("L")
    mask = Image.new("L", im.size, 0)
    lum = gray.load()
    w, h = im.size
    seen = bytearray(w * h)
    stack = [(x, 0) for x in range(w)] + [(x, h - 1) for x in range(w)] + [(0, y) for y in range(h)] + [(w - 1, y) for y in range(h)]
    mp = mask.load()
    thr = 26
    while stack:
        x, y = stack.pop()
        i = y * w + x
        if seen[i]:
            continue
        seen[i] = 1
        if lum[x, y] > thr:
            continue
        mp[x, y] = 255
        if x > 0: stack.append((x - 1, y))
        if x < w - 1: stack.append((x + 1, y))
        if y > 0: stack.append((x, y - 1))
        if y < h - 1: stack.append((x, y + 1))
    bg = mask.filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.GaussianBlur(2))
    alpha = Image.eval(bg, lambda v: 255 - v)
    im.putalpha(alpha)
    bbox = alpha.point(lambda v: 255 if v > 8 else 0).getbbox()
    if bbox:
        im = im.crop(bbox)
    im.save(dst, "PNG", optimize=True)


def install(picks):
    OUT.mkdir(parents=True, exist_ok=True)
    for name, num in picks.items():
        if not num:
            continue
        src = WORK / name / f"{name}_{num}.png"
        if not src.exists():
            sys.exit(f"missing candidate {src}")
        if name == "backdrop":
            im = Image.open(src).convert("RGB")
            im.thumbnail((2400, 1200), Image.LANCZOS)
            im.save(OUT / "backdrop.webp", "WEBP", quality=88)
        else:
            cut_out(src, OUT / f"{name}.png", {"coin": 512, "stele": 900, "rune": 700}[name])
        print(f"installed {name} #{num}")


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    g = sub.add_parser("generate")
    g.add_argument("--only", default="")
    g.add_argument("--n", type=int, default=3)
    i = sub.add_parser("install")
    for k in PIECES:
        i.add_argument(f"--{k}", type=int, default=0)
    a = ap.parse_args()
    if a.cmd == "generate":
        generate([x for x in a.only.split(",") if x], a.n)
    else:
        install({k: getattr(a, k) for k in PIECES})


if __name__ == "__main__":
    main()

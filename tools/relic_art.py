#!/usr/bin/env python3
"""
relic_art.py — artifact (relic) paintings, each framed differently (FABLE-044).

Every artifact tile so far was painted from one camera clause ("the object alone at the centre of
the frame, resting on a bare slab of dark stone … centred composition, eye-level"), so every
weapon in the game is a weapon on a rock. Here each candidate painting draws a DIFFERENT framing
from the twelve relic compositions in composition_bank.py (in the grip, mid-strike, embedded, on
the trophy wall, awakening, left on the field, at the forge, reflected, against the sky, half
revealed, sunken, old-master still life) — random, weighted toward the least-used — and the
object description, class rules, palette and style stay fixed. So three candidates for one
artifact are three genuinely different pictures to choose from.

Portrait 832x1216 (the tile on the board is portrait, ~146x238); engine = Nano Banana Pro, which
obeys "no frame, no border, no lettering" (the reliquary frame is drawn by the game).

The OpenRouter key comes from the environment or ~/.hermes/.env. Candidates go to
~/runewake_art_archive/relics/<run>/ (outside the repo); nothing is installed until `approve`.

  relic_art.py plan   [ids… | --all | --class rogue]   show the prompts it would paint (no spend)
  relic_art.py render [ids… | --all | --class rogue] [--candidates 3]   paint (≈ $0.15 per image)
  relic_art.py sheet  [--run RUN]                      contact sheet of the last run → artifacts/art_review/
  relic_art.py approve <artifact_id> <candidate#> [--run RUN]   install as client/content/art/artifacts/<id>.webp
"""
import argparse
import glob
import json
import os
import random
import subprocess
import sys
import time
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
REPO = TOOLS.parent
sys.path.insert(0, str(TOOLS))
import composition_bank as cb  # noqa: E402

ART = REPO / "client" / "content" / "art" / "artifacts"
CONTENT = REPO / "client" / "content" / "artifacts"
ARCHIVE = Path(os.environ.get("RELIC_ART_WORK", str(Path.home() / "runewake_art_archive" / "relics")))
LEDGER = ARCHIVE / "relic_ledger.json"
MODEL = os.environ.get("RELIC_ART_MODEL", "google/gemini-3-pro-image")

STYLE = ("Classical storybook oil painting, dramatic chiaroscuro, swirling expressive impasto brushwork, "
         "restrained palette with selective vivid accents, painted by hand, unsigned")
CLEAN = "A single full-bleed painting edge to edge, with no frame, no border and no lettering"

# What each launch artifact IS — the one part that never changes between framings. Standing art
# rules live here: the Battlemage's wand is SHORT (never a staff or sceptre), the Warrior's shield
# is ornate, the Necromancer's skull is a human skull.
OBJECT = {
    "artf_warrior_sword": "a battle-worn longsword with a wire-bound grip and a notched, gleaming blade",
    "artf_warrior_shield": "an ornate round war-shield of riveted steel and oak, embossed with a snarling lion, dented from a hundred charges",
    "artf_battlemage_wand": "a SHORT wand of black ironwood capped with a crackling crystal, about the length of a forearm",
    "artf_battlemage_aura": "a ring of living arcane light, runes circling within a shimmering sphere of protective force",
    "artf_necromancer_skull": "a human skull inscribed with fine silver runes, a faint green witch-light behind its eye sockets",
    "artf_necromancer_ritual_piece": "a ritual fetish of carved bone, twine and black feathers, humming with whispers",
    "artf_paladin_hammer": "a gilded warhammer with a sunburst head, prayer-strips tied around its haft",
    "artf_paladin_banner": "the white-and-gold banner of Sunspire on an iron pole, a blazing sun stitched at its heart",
    "artf_druid_book_of_familiar": "a weathered tome bound in moss-grown leather, small green shoots growing from between its pages",
    "artf_druid_elemental_bond": "a braided torc of living root, stone and water, a pulse of green light running along it",
    "artf_rogue_dagger_dusk": "Duskfang, a curved black dagger trailing violet smoke, a swept guard of dark steel",
    "artf_rogue_dagger_whisper": "Whisperfang, a slender verdigris-green leaf-bladed dagger, its edge pale as moonlight",
    "artf_astrologist_orb": "a crystal orb holding a swirling fragment of the night sky, set in a silver claw stand",
    "artf_astrologist_constellation_starlight": "a stoppered glass vial of captured starlight, spilling tiny constellations",
}
PALETTE = {
    "warrior": "iron greys and oxblood with a warm torch-gold accent",
    "battlemage": "storm blues and silver with a crackling cyan accent",
    "necromancer": "bone white and grave-moss green with a violet accent",
    "paladin": "sun gold and white stone with a sky-blue accent",
    "druid": "deep forest greens and loam brown with a firefly-gold accent",
    "rogue": "dusk violet and charcoal with a poison-green accent",
    "astrologist": "midnight blue and silver with a pale starlight accent",
}


def load_artifacts():
    arts = []
    for f in [CONTENT / "launch_artifacts.json", *sorted(glob.glob(str(CONTENT / "variants" / "*.json")))]:
        d = json.loads(Path(f).read_text())
        arts += d if isinstance(d, list) else d.get("artifacts", [d])
    return {a["id"]: a for a in arts}


def describe(a):
    if a["id"] in OBJECT:
        return OBJECT[a["id"]]
    flavor = (a.get("flavor") or "").strip().rstrip(".")
    return f"{a.get('name', a['id'])}, a {a.get('slot_pool', 'relic')}" + (f" — {flavor}" if flavor else "")


def load_ledger():
    try:
        return json.loads(LEDGER.read_text())
    except (OSError, ValueError):
        return {"cards": {}}


def save_ledger(led):
    LEDGER.parent.mkdir(parents=True, exist_ok=True)
    LEDGER.write_text(json.dumps(led, indent=1))


def build_prompt(a, comp):
    obj = describe(a)
    pal = PALETTE.get(a.get("class", ""), "")
    return (f"{obj[0].upper()}{obj[1:]}. {comp[2]} The {a.get('slot_pool', 'relic')} is the unmistakable hero of the "
            f"picture: its whole silhouette clearly readable even at the size of a thumbnail. "
            f"Colour: {pal}. {CLEAN}. {STYLE}.")


def select(args, arts):
    if args.ids:
        bad = [i for i in args.ids if i not in arts]
        if bad:
            sys.exit(f"unknown artifact(s): {', '.join(bad)}")
        return [arts[i] for i in args.ids]
    if getattr(args, "klass", None):
        return [a for a in arts.values() if a.get("class") == args.klass]
    if args.all:
        return list(arts.values())
    sys.exit("say which: artifact ids, --class NAME or --all")


def draws(led, n, rng):
    used, recent = cb.usage_from_ledger(led, "relic")
    out = []
    while len(out) < n:
        c = cb.pick("relic", used, recent + [x[0] for x in out], rng, avoid_last=3 + len(out))
        if c not in out:
            out.append(c)
            used[c[0]] = used.get(c[0], 0) + 1
    return out


def cmd_plan(a):
    arts, led, rng = load_artifacts(), load_ledger(), random.Random()
    for art in select(a, arts):
        for i, comp in enumerate(draws(led, a.candidates, rng), 1):
            print(f"{art['id']}  #{i}  [{comp[1]}]\n    {build_prompt(art, comp)}\n")
    return 0


def cmd_render(a):
    arts, led, rng = load_artifacts(), load_ledger(), random.Random()
    run = time.strftime("%Y%m%d-%H%M%S")
    out_dir = ARCHIVE / run
    out_dir.mkdir(parents=True, exist_ok=True)
    manifest = []
    for art in select(a, arts):
        for i, comp in enumerate(draws(led, a.candidates, rng), 1):
            prompt = build_prompt(art, comp)
            out = out_dir / f"{art['id']}__{i}.png"
            print(f"{art['id']} #{i} [{comp[1]}] …", flush=True)
            r = subprocess.run([sys.executable, str(REPO / "pipeline" / "gen_image_any.py"), prompt, str(out),
                                "--model", MODEL, "--width", "832", "--height", "1216", "--aspect", "2:3"],
                               capture_output=True, text=True)
            ok = out.exists() and out.stat().st_size > 10_000
            print(f"    {'ok' if ok else 'FAILED: ' + (r.stderr or r.stdout)[-300:]}")
            manifest.append({"id": art["id"], "n": i, "composition": comp[0], "prompt": prompt, "file": out.name, "ok": ok})
            if ok:
                led["cards"][f"{art['id']}#{run}#{i}"] = {"concept": {"composition": comp[0]}, "planned_at": time.time()}
                save_ledger(led)
    (out_dir / "manifest.json").write_text(json.dumps(manifest, indent=1))
    print(f"\nrun {run}: {sum(m['ok'] for m in manifest)}/{len(manifest)} painted → {out_dir}")
    print(f"next: tools/relic_art.py sheet --run {run}")
    return 0


def last_run():
    runs = sorted(p.name for p in ARCHIVE.glob("2*") if (p / "manifest.json").exists())
    if not runs:
        sys.exit(f"no runs in {ARCHIVE}")
    return runs[-1]


def cmd_sheet(a):
    from PIL import Image, ImageDraw, ImageFont
    run = a.run or last_run()
    d = ARCHIVE / run
    rows = [m for m in json.loads((d / "manifest.json").read_text()) if m["ok"]]
    ids = list(dict.fromkeys(m["id"] for m in rows))
    cols = max((sum(1 for m in rows if m["id"] == i) for i in ids), default=1)
    tw, th, pad, lab = 300, 438, 16, 44
    sheet = Image.new("RGB", (pad + cols * (tw + pad), pad + len(ids) * (th + lab + pad)), (18, 15, 12))
    draw = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype(str(REPO / "client" / "assets" / "fonts" / "Cinzel.ttf"), 15)
    except OSError:
        font = ImageFont.load_default()
    for r, i in enumerate(ids):
        for c, m in enumerate([m for m in rows if m["id"] == i]):
            x, y = pad + c * (tw + pad), pad + r * (th + lab + pad)
            sheet.paste(Image.open(d / m["file"]).convert("RGB").resize((tw, th)), (x, y))
            comp = cb.by_id("relic")[m["composition"]][1]
            draw.text((x, y + th + 4), f"{i.replace('artf_', '')} #{m['n']}", fill=(232, 212, 140), font=font)
            draw.text((x, y + th + 22), comp, fill=(200, 190, 170), font=font)
    out = REPO / "artifacts" / "art_review" / f"relics_{run}.jpg"
    out.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out, quality=88)
    print(out)
    return 0


def cmd_approve(a):
    from PIL import Image
    run = a.run or last_run()
    src = ARCHIVE / run / f"{a.id}__{a.n}.png"
    if not src.exists():
        sys.exit(f"no candidate {src}")
    dst = ART / f"{a.id}.webp"
    if dst.exists():
        keep = ARCHIVE / "replaced" / f"{a.id}.{int(time.time())}.webp"
        keep.parent.mkdir(parents=True, exist_ok=True)
        keep.write_bytes(dst.read_bytes())
    # real WebP bytes under a .webp name (Godot picks the importer by extension — see art_check)
    Image.open(src).convert("RGB").save(dst, "WEBP", quality=92)
    print(f"installed {dst.relative_to(REPO)}  (old copy kept under {ARCHIVE / 'replaced'})")
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    for name in ("plan", "render"):
        p = sub.add_parser(name)
        p.add_argument("ids", nargs="*")
        p.add_argument("--all", action="store_true")
        p.add_argument("--class", dest="klass")
        p.add_argument("--candidates", type=int, default=3)
    p = sub.add_parser("sheet"); p.add_argument("--run")
    p = sub.add_parser("approve"); p.add_argument("id"); p.add_argument("n", type=int); p.add_argument("--run")
    a = ap.parse_args(argv)
    return {"plan": cmd_plan, "render": cmd_render, "sheet": cmd_sheet, "approve": cmd_approve}[a.cmd](a)


if __name__ == "__main__":
    sys.exit(main())

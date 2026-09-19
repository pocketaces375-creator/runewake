#!/usr/bin/env python3
"""
art_prompt.py — build a DIFFERENT prompt for every piece of art, on purpose.

WHY THIS EXISTS
---------------
docs/ART_PROMPT_PLAYBOOK.md tells a human how to write a good prompt. It works —
and it is also why every artifact tile looks like every other artifact tile. One
author writing 47 prompts by hand reaches for the same nouns, the same light and
the same camera every time, and one generator adds its own house style on top.
The result is technically on-brief and visually monotonous.

So variety stops being a thing someone remembers to do and becomes a thing the
tool cannot avoid doing:

  * every asset draws from SEVEN orthogonal axes (subject, material, age, light,
    camera, ground, treatment) instead of one template;
  * the draw is seeded from the asset id, so it is random-looking but FIXED —
    the same id always produces the same prompt, in any process, on any machine;
  * the GENERATOR rotates too, and each one gets the prompt in its own dialect,
    because a FLUX prose paragraph and an SDXL tag list are not the same request;
  * a neighbour check forbids an asset from sharing a combination with another
    asset in the same slot, so sibling items cannot collide.

Style stays locked. The style spine, the isolation clause, the stratum palettes
and the two hard rules from the playbook are applied to every prompt and are not
part of the randomisation. Variety is in the subject and the craft, never in the
brand.

USAGE
  art_prompt.py plan                 write artifacts/art_plan.json for every asset
  art_prompt.py show <asset_id>      print one prompt exactly as the generator gets it
  art_prompt.py variety              measure how different the prompts actually are
  art_prompt.py verify               fail if any two siblings collide (a gate)
"""

import argparse
import collections
import glob
import json
import os
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CONTENT = REPO / "content"
OUT = REPO / "artifacts" / "art_plan.json"

M64 = (1 << 64) - 1


# ─────────────────────────── deterministic randomness ───────────────────────
def stable_hash(s: str) -> int:
    """FNV-1a. Matches ArtifactRegistry.StableHash in the engine.

    Not Python's hash(): that is salted per process, so the "random" choice
    would change on every run and the art would stop being reproducible.
    """
    h = 14695981039346656037
    for ch in s:
        h = ((h ^ ord(ch)) * 1099511628211) & M64
    return h


class Draw:
    """A seeded picker. Each axis advances the stream, so two assets that agree
    on one axis almost never agree on the next."""

    def __init__(self, seed: int):
        self._s = seed & M64

    def _next(self) -> int:
        self._s = (self._s + 0x9E3779B97F4A7C15) & M64
        z = self._s
        z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & M64
        z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & M64
        return (z ^ (z >> 31)) & M64

    def pick(self, options):
        return options[self._next() % len(options)]

    def pick_n(self, options, n):
        pool = list(options)
        out = []
        for _ in range(min(n, len(pool))):
            out.append(pool.pop(self._next() % len(pool)))
        return out


# ─────────────────────────────── the locked part ────────────────────────────
# The spine carries BRAND only — world, light, lineage, edge quality. It used to
# also say "oil painting, heavy impasto brushwork", which is a CRAFT choice, and
# that put it in a fight with the treatment axis: a prompt would ask for egg
# tempera with fine crosshatching and then immediately demand heavy impasto oil
# in the next sentence. The medium now belongs to the treatment axis alone, and
# the spine says only what must never vary.
STYLE_SPINE = ("dark fantasy, dramatic rim light, medieval woodcut influence, "
               "painterly edges")

ISOLATION = ("the object alone at the centre of the frame, empty unlit background "
             "falling to black")

PALETTES = {
    "EMBER":   "soot black #14100E with molten orange #C4501B and ash grey #6E655C",
    "TIDE":    "abyssal blue #0E2436 with teal #1F6F72 and pale foam #C9DCD8",
    "HOLLOW":  "bone white #D8CEBB with murky violet #3A2B45 and sickly green #6E7F4A",
    "VERDANT": "deep moss #1E2E1C with emerald #2F6B3A and wet bark brown #4A3626",
    "DAWN":    "warm gold #C8A04A with pale cream #EFE3C6 and amber #8A5A1E",
}

CLASS_PALETTE = {
    "battlemage": "EMBER", "warrior": "DAWN", "rogue": "HOLLOW",
    "paladin": "DAWN", "necromancer": "HOLLOW", "astrologist": "TIDE", "druid": "VERDANT",
}

# Words that must never appear: FLUX has no negative channel, so naming a thing
# to exclude statistically summons it. The playbook's rule, enforced.
BANNED = re.compile(r"\b(no|without|not|avoid|never|free of|lacking)\s+\w", re.I)

# Rule 1 of the playbook: an artifact tile is the object ALONE. This looks for a
# wielder, not for the word "face" — a shield has a face and light falls on the
# face of a blade, and banning the word outright flagged seven innocent prompts
# the first time it ran.
HUMAN = re.compile(r"\b(person|people|human|humanoid|man|woman|girl|boy|figure|silhouette|"
                   r"hand|hands|arm|arms|finger|fingers|holding|wielding|wearing|worn by)\b", re.I)


# ─────────────────────────────── the varied part ────────────────────────────
# Subjects per slot pool, from the playbook's variation banks. The slot name is a
# CATEGORY; each entry is a visibly different object of that category.
SUBJECTS = {
    "sword": ["a broken-tipped longsword", "a ritual falchion with a hooked quillon",
              "a leaf-bladed bronze shortsword", "an executioner's greatsword with a squared tip",
              "a slender duelling estoc", "a notched sabre with a basket hilt"],
    "shield": ["a round wooden targe bound in iron", "a battered kite shield",
               "a spiked buckler the size of a dinner plate", "a tower shield of riveted planks",
               "a bronze-fronted hoplon with a painted device"],
    "hammer": ["a smith's sledge with a burnt haft", "a long-handled war-maul",
               "a ceremonial mace with a flanged head", "a stonemason's pick worn to a nub",
               "a two-headed judgement hammer"],
    "banner": ["a torn cavalry pennant", "a processional standard topped with a sun-disc",
               "a weathered war flag on a broken spear", "a hanging heraldic tapestry",
               "a bronze vexillum with a cast beast crest"],
    "dagger": ["a black-bladed parrying knife", "a curved gutting hook", "a needle stiletto",
               "a leaf-bladed bronze dirk", "a glass shard bound in leather cord",
               "a push-dagger with a knuckle guard"],
    "orb": ["an armillary sphere of tarnished brass rings", "a smooth obsidian scrying stone",
            "a cracked glass globe with a storm sealed inside", "a caged moon of silver wire",
            "a suspended drop of black water full of constellations", "a nested orrery"],
    "starlight": ["a sextant of blued steel", "a star-chart disc of pierced copper",
                  "a hanging plumb of meteoric iron", "a lens ground from clear amber",
                  "a ring-dial strung on a knotted cord"],
    "book": ["a chained iron-cornered grimoire", "a bundle of birch-bark leaves stitched with root",
             "a scroll case of carved antler", "a wax tablet diptych",
             "a folio bound in beetle-shell", "a book grown through with living moss"],
    "totem": ["an antler circlet wrapped in storm-lit vines", "a toad-shaped bog-stone weeping fen water",
              "a moth-wing fan of pressed frost", "a corvid skull strung with river pearls",
              "a boar-tusk knot bound in red cord", "a serpent coil of braided root"],
    "grimoire": ["a ledger of the dead bound in grey hide", "a codex sealed with black wax",
                 "a bone-leaved commonplace book", "a rolled vellum necrology in a lead tube"],
    "phylactery": ["a wax-sealed reliquary jar", "a lead soul-flask on a chain",
                   "a stoppered horn of grave-dust", "a hinged silver locket gone black"],
    "skull": ["a horned beast skull", "a crow-picked human cranium set in a bronze cradle",
              "a tiny jawless skull of something small and clever", "a tallow candle burnt into a jaw"],
    "ritual_piece": ["a knotted fetish of knuckle-bones and horsehair", "a censer trailing green smoke",
                     "a circle of nine iron nails", "a bowl of still black water"],
    "wand": ["a blackthorn rod capped in cold iron", "a fused glass baton with a molten core",
             "a bundle of charred reeds bound in wire", "a jointed brass pointer with a spark gap"],
    "aura": ["a ring of suspended embers", "a slowly turning halo of cut glass",
             "a hovering lattice of hot wire", "a wreath of drifting cinder-moths"],
}

# If the subject already names what it is made of ("a scroll case of carved
# antler"), bolting a second material onto it produces "a scroll case of carved
# antler of cold-forged bronze" — a contradiction the model resolves by
# inventing something that is neither. When that happens the material becomes an
# ACCENT on the fittings instead of the body.
MATERIAL_WORDS = ("iron", "brass", "steel", "bronze", "silver", "gold", "copper", "oak",
                  "wood", "wooden", "horn", "bone", "glass", "clay", "stone", "obsidian",
                  "amber", "antler", "vellum", "hide", "leather", "wax", "wire", "lead",
                  "crystal", "birch", "tusk", "shell", "linen", "pearl")

MATERIALS = ["pitted wrought iron", "tarnished brass", "blued steel", "cold-forged bronze",
             "blackened silver", "weathered oak and hide", "cracked horn", "fired clay and glaze",
             "meteoric iron", "bone and waxed cord", "hammered copper gone green"]

AGES = ["freshly made and unmarked", "long-used, the edges worn smooth",
        "salvaged and crudely repaired", "cracked and mended with visible staples",
        "burnt along one side", "scoured by salt", "half-swallowed by corrosion",
        "buried for a century and only just cleaned"]

LIGHTS = ["museum lighting from the upper left", "a single low candle to one side",
          "cold daylight through a high slot window", "firelight from below, throwing the top into shadow",
          "overcast north light, almost shadowless", "a shaft of moonlight across the upper third",
          "backlit, the rim burning and the front in shade"]

CAMERAS = ["shot on an 85mm lens at f/4, centred, eye-level",
           "shot on a 50mm lens at f/2.8, three-quarter view, slightly above",
           "shot on a 135mm lens at f/5.6, tight crop, dead-on",
           "shot on a 35mm lens at f/8, the object small in a wide dark frame",
           "macro at f/2, the near edge sharp and the far edge falling away"]

GROUNDS = ["resting on a bare slab of dark stone", "laid on rumpled undyed linen",
           "floating unsupported, a few inches above its own shadow", "half-buried in cold ash",
           "propped against an unseen edge", "lying in shallow still water",
           "set on a scarred workbench of black oak"]

# The craft axis. All of these still read as the same game; none of them look
# like the same painter had a second cup of coffee.
TREATMENTS = [
    "heavy impasto oil, the paint standing off the panel",
    "thin oil glazes over a warm underpainting, edges melting into shadow",
    "egg tempera on gessoed panel, fine crosshatched modelling",
    "ink and wash with drybrush texture over the darks",
    "gouache with visible paper tooth and flat massed shapes",
    "grisaille underpainting with a single glazed accent colour",
    "woodcut-influenced hard contour with a restricted four-value palette",
]

# ───────────────────────────────── generators ───────────────────────────────
# Each model is asked in its own dialect. Handing an SDXL tag list to FLUX, or a
# FLUX paragraph to a tag-weighted model, wastes most of the prompt.
GENERATORS = {
    "flux": {
        "dialect": "prose",
        "note": "FLUX.2 — prose paragraph, subject first, 30-80 words.",
    },
    "gemini-image": {
        "dialect": "instruction",
        "note": "Gemini image — reads an instruction; say what to paint and what NOT to include only as positive facts.",
    },
    "sdxl": {
        "dialect": "tags",
        "note": "SDXL-class — comma-separated weighted tags, subject tokens first.",
    },
}
# Which generators are actually wired up on the Hermes box. Keep in sync with
# whatever has credentials; the rotation only uses these.
ENABLED = ["flux", "gemini-image"]


# ────────────────────────────────── building ────────────────────────────────
def load_artifacts():
    items = []
    for f in sorted(glob.glob(str(CONTENT / "artifacts" / "**" / "*.json"), recursive=True)):
        try:
            d = json.load(open(f, encoding="utf-8"))
        except Exception:
            continue
        arr = d if isinstance(d, list) else d.get("artifacts", d.get("items", []))
        if isinstance(arr, dict):
            arr = list(arr.values())
        for a in arr:
            if isinstance(a, dict) and a.get("id") and a.get("class"):
                items.append(a)
    return items


def build_prompt(asset_id, kind, slot_pool, cls, name, taken_combos):
    """Compose one prompt. `taken_combos` is the set of (subject, treatment)
    pairs already used by siblings in this slot — a collision is re-drawn rather
    than shipped, because two items of the same slot looking alike is the single
    most visible failure."""
    seed = stable_hash(asset_id)
    palette = PALETTES[CLASS_PALETTE.get(cls, "DAWN")]
    subjects = SUBJECTS.get(slot_pool) or [f"a {slot_pool.replace('_', ' ')}"]

    combo = None
    for attempt in range(12):
        d = Draw(seed + attempt * 0x1234567)
        subject = d.pick(subjects)
        treatment = d.pick(TREATMENTS)
        if (subject, treatment) not in taken_combos:
            combo = (d, subject, treatment)
            break
    if combo is None:                      # every combination used: take the draw anyway
        d = Draw(seed)
        combo = (d, d.pick(subjects), d.pick(TREATMENTS))
    d, subject, treatment = combo
    taken_combos.add((subject, treatment))

    material = d.pick(MATERIALS)
    subj_lower = subject.lower()
    if any(w in subj_lower for w in MATERIAL_WORDS):
        material_clause = f"with fittings of {material}"
    else:
        material_clause = f"of {material}"
    age = d.pick(AGES)
    light = d.pick(LIGHTS)
    camera = d.pick(CAMERAS)
    ground = d.pick(GROUNDS)
    generator = ENABLED[stable_hash(asset_id + "|gen") % len(ENABLED)]
    dialect = GENERATORS[generator]["dialect"]

    if dialect == "tags":
        text = ", ".join([
            subject, material_clause, age, ground, ISOLATION,
            treatment, STYLE_SPINE, palette, light, camera,
        ])
    elif dialect == "instruction":
        text = (f"Paint {subject} {material_clause}, {age}, {ground}. "
                f"{ISOLATION.capitalize()}. Render it as {treatment}. "
                f"{STYLE_SPINE.capitalize()}. Palette: {palette}. Lighting: {light}. {camera}.")
    else:  # prose
        text = (f"{subject.capitalize()} {material_clause}, {age}, {ground}, {ISOLATION}. "
                f"{treatment.capitalize()}. {STYLE_SPINE.capitalize()}. {palette}. {light}, {camera}.")

    return {
        "asset_id": asset_id,
        "kind": kind,
        "class": cls,
        "slot_pool": slot_pool,
        "name": name,
        "generator": generator,
        "dialect": dialect,
        "seed": seed % (2 ** 31),
        "axes": {
            "subject": subject, "material": material, "age": age,
            "light": light, "camera": camera, "ground": ground, "treatment": treatment,
        },
        "prompt": text,
    }


def build_plan():
    rows = []
    by_pool = collections.defaultdict(set)
    for a in sorted(load_artifacts(), key=lambda x: x["id"]):
        pool = a.get("slot_pool") or "relic"
        rows.append(build_prompt(a["id"], "artifact_tile", pool, a["class"],
                                 a.get("name", ""), by_pool[pool]))
    return rows


# ─────────────────────────────────── checks ─────────────────────────────────
def axis_overlap(a, b):
    """How many of the seven axes two prompts share."""
    return sum(1 for k in a["axes"] if a["axes"][k] == b["axes"][k])


def cmd_plan(args):
    rows = build_plan()
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps({"version": 1, "assets": rows}, indent=2, ensure_ascii=False) + "\n")
    gens = collections.Counter(r["generator"] for r in rows)
    print(f"art_prompt: planned {len(rows)} assets -> {OUT.relative_to(REPO)}")
    for g, n in sorted(gens.items()):
        print(f"  {g:<14} {n}")
    return 0


def cmd_show(args):
    for r in build_plan():
        if r["asset_id"] == args.asset_id:
            print(f"# {r['asset_id']}  ({r['class']} / {r['slot_pool']})")
            print(f"# generator: {r['generator']}  dialect: {r['dialect']}  seed: {r['seed']}")
            print()
            print(r["prompt"])
            return 0
    print(f"no such asset: {args.asset_id}", file=sys.stderr)
    return 1


def cmd_variety(args):
    rows = build_plan()
    print(f"{len(rows)} assets\n")
    for axis in ["subject", "material", "age", "light", "camera", "ground", "treatment"]:
        vals = collections.Counter(r["axes"][axis] for r in rows)
        top = vals.most_common(1)[0]
        print(f"  {axis:<10} {len(vals):>3} distinct   most common used {top[1]}x")
    worst = []
    for i, a in enumerate(rows):
        for b in rows[i + 1:]:
            ov = axis_overlap(a, b)
            if ov >= 4:
                worst.append((ov, a["asset_id"], b["asset_id"]))
    worst.sort(reverse=True)
    print(f"\n  pairs sharing 4+ of 7 axes: {len(worst)}")
    for ov, x, y in worst[:8]:
        print(f"    {ov}/7  {x}  vs  {y}")
    same_pool = [(a, b) for i, a in enumerate(rows) for b in rows[i + 1:]
                 if a["slot_pool"] == b["slot_pool"]
                 and a["axes"]["subject"] == b["axes"]["subject"]
                 and a["axes"]["treatment"] == b["axes"]["treatment"]]
    print(f"  siblings sharing subject AND treatment: {len(same_pool)}")
    return 0


def cmd_verify(args):
    rows = build_plan()
    problems = []
    for r in rows:
        if BANNED.search(r["prompt"]):
            problems.append(f"{r['asset_id']}: prompt contains a negative phrase")
        if r["kind"] == "artifact_tile" and HUMAN.search(r["prompt"]):
            problems.append(f"{r['asset_id']}: tile prompt implies a wielder "
                            f"('{HUMAN.search(r['prompt']).group(0)}')")
        if not (20 <= len(r["prompt"].split()) <= 110):
            problems.append(f"{r['asset_id']}: {len(r['prompt'].split())} words, outside 20-110")
    by_pool = collections.defaultdict(list)
    for r in rows:
        by_pool[r["slot_pool"]].append(r)
    for pool, group in by_pool.items():
        for i, a in enumerate(group):
            for b in group[i + 1:]:
                if (a["axes"]["subject"] == b["axes"]["subject"]
                        and a["axes"]["treatment"] == b["axes"]["treatment"]):
                    problems.append(f"{pool}: {a['asset_id']} and {b['asset_id']} are the same idea")
    if problems:
        print(f"art_prompt: {len(problems)} problem(s)", file=sys.stderr)
        for p in problems:
            print(f"  {p}", file=sys.stderr)
        return 1
    print(f"art_prompt: {len(rows)} prompts OK — no negatives, no figures on tiles, no sibling collisions")
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("plan")
    s = sub.add_parser("show"); s.add_argument("asset_id")
    sub.add_parser("variety")
    sub.add_parser("verify")
    args = ap.parse_args()
    return {"plan": cmd_plan, "show": cmd_show, "variety": cmd_variety, "verify": cmd_verify}[args.cmd](args)


if __name__ == "__main__":
    sys.exit(main())

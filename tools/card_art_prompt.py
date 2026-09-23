#!/usr/bin/env python3
"""
card_art_prompt.py — OFFLINE FALLBACK for card art prompts (FABLE-020). The real tool is
art_director.py (FABLE-021): a template, however varied, still paints the same picture.

WHY THIS EXISTS
---------------
Look at the EMBER or VERDANT contact sheet: one figure, full body, dead centre,
eye level, standing in the stratum's one location, lit the same way. That is not
the generator's fault. Every card prompt so far has been

    <subject> + <the same 70-word style spine> + <the ONE context for the
    stratum> + <the ONE camera clause: "full figure, 50mm, low eye-level">

so ~75% of every prompt was identical to every other prompt of that stratum, and
FLUX did exactly what it was asked. The spine also says "single grounded focal
subject staged with breathing room in the manner of a Renaissance tableau" —
that clause, on its own, is an instruction to paint a centred lone figure.

What this tool changes:

  * the subject line is the ONLY hand/LLM-written part, and it is subject only —
    who or what, plus one distinctive detail. No place, no camera, no style;
  * shot, placement, moment, setting, time/weather, lens and which palette colour
    leads are drawn from banks, per card, BALANCED across the stratum (least-used
    option first), so twenty VERDANT cards spread over ten places and ten shots
    instead of twenty clearings;
  * hard rule: no two cards in a stratum share (shot, setting), and no two share
    four or more of the six drawn axes;
  * rituals are painted as EVENTS (impact, aftermath, the phenomenon itself), not
    as a caster posing; relics as objects in a place;
  * the spine is cut to the brand — oil, chiaroscuro, brushwork, palette — and no
    longer dictates composition. (v3.0 is kept below, selectable with --spine v3.0.)

Deterministic: the same card id always gets the same prompt on any machine.

USAGE
  card_art_prompt.py plan [--spine v3.1|v3.0]   write artifacts/card_art_plan.json
  card_art_prompt.py show <card_id>              print one prompt as FLUX gets it
  card_art_prompt.py variety                     how spread out the plan is
  card_art_prompt.py verify                      fail on collisions / negatives / length (a gate)
  card_art_prompt.py brief <card_id>             the LLM request that writes a missing subject line
"""

import argparse
import collections
import glob
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from art_prompt import Draw, stable_hash, PALETTES, BANNED  # noqa: E402  (same hash, same palettes, same rule)

REPO = Path(__file__).resolve().parent.parent
CONTENT = REPO / "content"
OUT = REPO / "artifacts" / "card_art_plan.json"

# ──────────────────────────────── the locked part ────────────────────────────
SPINES = {
    # Trikzos's v3.0 (2026-08-17) — kept verbatim for comparison.
    "v3.0": ("oil painting in the style of classical storybook illustration, "
             "dramatic painted light against deep shadow (chiaroscuro), swirling expressive brushwork, "
             "single grounded focal subject staged with breathing room in the manner of a Renaissance tableau, "
             "atmospheric depth with softly rendered distant background, restrained palette with selective vivid accents, "
             "thick impasto texture, painted by hand, unsigned artwork"),
    # v3.1 — the same look, minus the clauses that fix the composition.
    "v3.1": ("classical storybook oil painting, dramatic chiaroscuro, swirling expressive impasto brushwork, "
             "restrained palette with selective vivid accents, painted by hand, unsigned"),
}

PALETTE_COLOURS = {k: [c.strip() for c in re.split(r" with | and ", v)] for k, v in PALETTES.items()}

# ──────────────────────────────── the varied part ────────────────────────────
# SHOTS carry their own lens, because a lens that fights the framing ("extreme
# close-up … 24mm wide") produces mush.
SHOTS_FIGURE = [
    ("close", "an extreme close-up on the face, filling the frame, the eyes catching the light", "85mm lens at f/2"),
    ("bust", "a head-and-shoulders portrait turned three-quarters away", "85mm lens at f/2.8"),
    ("waist", "seen from the waist up, leaning into the frame", "50mm lens at f/2.8"),
    ("vast", "seen from far off, small against the scale of the place", "24mm wide lens at f/8"),
    ("behind", "seen from behind and slightly above, looking out over the scene", "35mm lens at f/5.6"),
    ("worm", "from a worm's-eye view at ground level, towering overhead", "24mm wide lens at f/5.6"),
    ("bird", "from high above, looking almost straight down", "35mm lens at f/8"),
    ("profile", "in hard profile, a dark silhouette against a bright sky", "135mm lens at f/4"),
    ("over", "over the shoulder of an unseen foe in the foreground, the subject facing us", "50mm lens at f/2"),
    ("cropped", "cropped hard by the frame edge, half of the body out of the picture", "35mm lens at f/2.8"),
    ("reflect", "seen mostly in a reflection on still water, the real figure at the top edge", "50mm lens at f/4"),
]
SHOTS_EVENT = [
    ("impact", "the instant of impact, seen from a safe distance", "35mm lens at f/8"),
    ("after", "the aftermath: the place changed by it, the one who did it already gone", "24mm wide lens at f/8"),
    ("detail", "a tight detail of the phenomenon itself, texture and light filling the frame", "100mm macro lens at f/4"),
    ("door", "glimpsed through a broken doorway, the event beyond", "35mm lens at f/4"),
    ("crowd", "from among onlookers at ground level, their heads in silhouette", "50mm lens at f/2.8"),
    ("sky", "the one who called it tiny at the bottom, the effect filling the sky", "24mm wide lens at f/11"),
    ("top", "from directly above, the effect spreading outward like a map", "35mm lens at f/8"),
    ("hands", "close on the hands as it begins, the rest of the figure in shadow", "85mm lens at f/2"),
]
SHOTS_OBJECT = [
    ("altar", "resting on an altar, seen from a low angle", "50mm lens at f/4"),
    ("buried", "half-buried where it fell, found at last", "35mm lens at f/5.6"),
    ("alcove", "in a shadowed alcove, the only lit thing in the frame", "85mm lens at f/2.8"),
    ("held", "held up by a gloved hand at the edge of the frame", "85mm lens at f/2"),
    ("wide", "small in a wide view of the place that guards it", "24mm wide lens at f/8"),
    ("macro", "in macro, its surface filling the frame", "100mm macro lens at f/5.6"),
]

PLACEMENTS = [
    "placed in the left third of the frame",
    "placed in the right third of the frame",
    "low in the frame beneath a tall sky",
    "high in the frame above a steep drop",
    "on a diagonal from lower left to upper right",
    "just off centre, with the frame opening out behind",
]

MOMENTS_PERSON = [
    "mid-strike, weight fully committed", "bracing for a blow", "resting after a fight, breathing hard",
    "stalking forward, low and slow", "turning sharply toward something off-frame", "caught in mid-leap",
    "kneeling", "calling out to someone unseen", "laughing", "utterly still, waiting",
]
MOMENTS_BEAST = [
    "mid-leap", "prowling low", "rearing up", "curled and watchful", "shaking itself off",
    "lunging at something off-frame", "at rest, one eye open", "howling or roaring",
]

# (setting, indoor?)  Ten or more per stratum — the single CONTEXT line these replace
# is the main reason every card of a stratum shares a background.
SETTINGS = {
    "EMBER": [
        ("inside a roaring dwarven forge", True), ("on a basalt switchback road under falling ash", False),
        ("across a field of black volcanic glass", False), ("in a collapsed smelting hall open to the sky", False),
        ("beside a river of lava in a vast cavern", True), ("in an ash-choked market street", False),
        ("on a cooling slag plain", False), ("on a cliff-top beacon tower", False),
        ("in a charred pine forest still smouldering", False), ("at the rim of a caldera", False),
        ("in a quarry of red rock", False),
    ],
    "TIDE": [
        ("deep in a drowned cathedral", "water"), ("on a storm-lashed sea stack", False),
        ("in a tidal cave as the water rushes in", True), ("on a shipwreck half out of the sea", False),
        ("on a mudflat at low tide", False), ("on a lighthouse gallery", False), ("in a kelp forest under the sea", "water"),
        ("on a drifting ice floe", False), ("in a salt-crusted harbour", False), ("at the edge of a whirlpool", False),
        ("on a pier of black pilings", False),
    ],
    "HOLLOW": [
        ("in a barrow chamber", True), ("in a flooded crypt", True), ("on a gibbet-lined road across a moor", False),
        ("in an empty plague village", False), ("in a chapel built of bones", True), ("in a snowbound graveyard", False),
        ("in the choir of a sunken abbey", True), ("in a fog-filled peat bog", False),
        ("under a dead oak hung with lanterns", False), ("in a vast ossuary gallery", True),
        ("at a crossroads gallows", False),
    ],
    "VERDANT": [
        ("in a meadow of tall grass", False), ("in a fern gully", False), ("high in the canopy on a broad branch", False),
        ("at a waterfall pool", False), ("in a bog of glowing fungus", False), ("inside the hollow of a giant tree", True),
        ("on a heath of standing stones", False), ("in autumn woods", False), ("in a flooded forest", False),
        ("among snow-covered pines", False), ("in an overgrown ruin", False),
    ],
    "DAWN": [
        ("in a marble cloister", True), ("on a mountain pass above the clouds", False),
        ("on a battlefield", False), ("in a golden wheat field", False), ("on cathedral scaffolding", False),
        ("on a city wall", False), ("in a desert of pale dunes", False), ("in a sunlit scriptorium", True),
        ("on the steps of a ruined temple", False), ("in a procession through a crowded street", False),
        ("in a bell tower", True),
    ],
}

TIMES = [
    "at dawn in low mist", "at blazing midday", "at dusk under a red sky", "at night under a sky of stars",
    "in a thunderstorm, lit by a flash of lightning", "in driving rain", "in falling snow", "in thick fog",
    "in the blue hour after sunset", "under a darkened eclipse sun",
]
WATER_LIGHT = [
    "lit by wavering shafts from the surface far above", "lit only by the glow of drifting creatures",
    "in green murk, the light failing with depth", "lit by a sunken lantern still burning",
]
INDOOR_LIGHT = [
    "lit by a single lantern", "lit from below by firelight", "lit by a shaft of light from a high opening",
    "lit by cold light bouncing off water", "backlit by a blaze behind", "lit by dozens of small candles",
]

# The palette stays the stratum's. What rotates is which colour LEADS.
def palette_line(strata, d):
    cols = PALETTE_COLOURS.get(strata) or PALETTE_COLOURS["DAWN"]
    order = d.pick_n(cols, len(cols))
    return f"Colour: mostly {order[0]}, with {order[1]}, and a small accent of {order[2]}"

# Words that mark a subject as a person (for moment choice and the brief).
PERSON = re.compile(r"\b(man|woman|girl|boy|figure|warrior|knight|priest|priestess|mage|scout|guard|healer|"
                    r"shaman|witch|oracle|scholar|seer|herald|prophet|recruit|initiate|raider|archer|trader|"
                    r"adept|diviner|reader|lancer|bearer|elder|dwarf|elf|soldier|monk|nun|hunter|he|she|his|her)\b", re.I)
# Subject clauses carrying a PLACE get cut: the place is drawn now.
PLACE = re.compile(r"\b(in|on|beside|amid|among|before|beneath|under|within|against)\s+(a|an|the)\s+[\w-]+.*"
                   r"(forge|forest|chamber|cavern|cave|grotto|temple|clearing|ruin|hall|crypt|barrow|shore|sea|"
                   r"sky|background|room|field|glade|stone floor|haze|mist|woods|pool)\b|\bbackground\b|visible beyond", re.I)

# LLM brief: the one part a model writes. Rotating the WHO stops "a wiry young man" every time.
WHO = ["an old woman", "a broad young man", "a wiry elderly man", "a tall middle-aged woman", "a stocky youth",
       "a scarred veteran woman", "a heavy-set old man", "a lean girl on the edge of adulthood", "a grey-bearded giant of a man",
       "a small, quick woman"]
BRIEF_SYSTEM = (
    "You write the SUBJECT line for a fantasy card painting. One sentence, 12-30 words: who or what it is, what they "
    "wear or are made of, and ONE distinctive detail. Do NOT describe the place, weather, lighting, camera, composition, "
    "art style or colours — those are added separately. Never use negative phrasing (no/without/not). Output only the sentence."
)


# ─────────────────────────────────── loading ─────────────────────────────────
def load_cards():
    cards = []
    for f in sorted(glob.glob(str(CONTENT / "cards" / "*.json"))):
        d = json.load(open(f, encoding="utf-8"))
        arr = d if isinstance(d, list) else d.get("cards", [])
        cards += [c for c in arr if isinstance(c, dict) and c.get("id") and c.get("type") in ("CREATURE", "RITUAL", "RELIC")
                  and not c["id"].startswith("tut_")]
    return cards


def legacy_subjects():
    """Subject lines already written by hand in the wave scripts (text before the spine)."""
    subs = {}
    for f in sorted(glob.glob(str(REPO / "pipeline" / "work" / "*.py"))):
        src = open(f, encoding="utf-8").read()
        for m in re.finditer(r'"id":\s*"([a-z]{3}_[a-z]_[a-z_]+)",\s*"prompt":\s*\((.*?)f"\{STYLE_SPINE\}', src, re.S):
            subs[m.group(1)] = "".join(re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(2))).strip()
    return subs


LIGHTING = re.compile(r"\b(lit|glow\w*|light|lights|shadows?|illuminat\w*|radian\w*|shaft)\b", re.I)
SUBJECT_MAX_WORDS = 30


def subject_core(text):
    """Keep who/what. Drop clauses that set the place or the light (the plan draws those),
    clauses with negative phrasing (FLUX paints what it is told to leave out), and stop at ~30 words."""
    parts = [p.strip() for p in re.split(r",\s+|;\s+", text.rstrip(". "))]
    keep, words = [], 0
    for i, p in enumerate(parts):
        if i > 0 and PLACE.search(p):
            break
        if i > 0 and (LIGHTING.search(p) or BANNED.search(p)):
            continue
        n = len(p.split())
        if keep and words + n > SUBJECT_MAX_WORDS:
            break
        keep.append(p)
        words += n
    out = ", ".join(keep).rstrip(".")
    if BANNED.search(out):                      # the first clause itself is negative: keep only up to the dash
        out = re.split(r"\s+[—-]\s+", out)[0]
    return out + "."


def kind_of(card, subject):
    if card["type"] == "RITUAL":
        return "event"
    if card["type"] == "RELIC":
        return "object"
    return "person" if PERSON.search(subject) else "beast"


# ─────────────────────────────────── planning ────────────────────────────────
def balanced(d, options, used, key=lambda o: o, exclude=()):
    """Least-used option first; seeded tie-break. `exclude` keys are skipped if anything else is left."""
    cands = [o for o in options if key(o) not in exclude] or list(options)
    low = min(used[key(o)] for o in cands)
    return d.pick([o for o in cands if used[key(o)] == low])


def build_plan(spine="v3.1"):
    legacy = legacy_subjects()
    cards = sorted(load_cards(), key=lambda c: (c["strata"], stable_hash(c["id"])))
    rows = []
    by_strata = collections.defaultdict(list)
    for c in cards:
        by_strata[c["strata"]].append(c)
    for strata, group in by_strata.items():
        used = collections.defaultdict(collections.Counter)
        pairs = set()
        for c in group:
            d = Draw(stable_hash(c["id"] + "|card"))
            raw = (c.get("art") or {}).get("subject") or legacy.get(c["id"]) or ""
            source = "art.subject" if (c.get("art") or {}).get("subject") else ("wave script" if raw else "PLACEHOLDER")
            if not raw:
                raw = f"{c['name']} — {c.get('flavor', '').rstrip('.')}"
            subject = subject_core(raw)
            kind = kind_of(c, subject)
            shots = {"event": SHOTS_EVENT, "object": SHOTS_OBJECT}.get(kind, SHOTS_FIGURE)
            shot = balanced(d, shots, used["shot"], key=lambda s: s[0])
            taken = {s for (sh, s) in pairs if sh == shot[0]}
            setting = balanced(d, SETTINGS.get(strata, SETTINGS["DAWN"]), used["setting"], key=lambda s: s[0], exclude=taken)
            prior = [r for r in rows if r["strata"] == strata]
            ex_place, ex_when, ex_moment = set(), set(), set()
            for _attempt in range(8):   # re-draw the soft axes until no earlier card is a near-twin
                placement = balanced(d, PLACEMENTS, used["placement"], exclude=ex_place)
                when = (balanced(d, WATER_LIGHT, used["light"], exclude=ex_when) if setting[1] == "water"
                        else balanced(d, INDOOR_LIGHT, used["light"], exclude=ex_when) if setting[1]
                        else balanced(d, TIMES, used["time"], exclude=ex_when))
                moment = None
                if kind == "person":
                    moment = balanced(d, MOMENTS_PERSON, used["moment"], exclude=ex_moment)
                elif kind == "beast":
                    moment = balanced(d, MOMENTS_BEAST, used["moment"], exclude=ex_moment)
                pal = palette_line(strata, d)
                trial = {"axes": {"shot": shot[0], "placement": placement, "setting": setting[0],
                                  "when": when, "moment": moment or "", "lead_colour": pal.split(",")[0]}}
                twin = next((r for r in prior if overlap(trial, r) >= 4), None)
                if twin is None:
                    break
                ex_place.add(placement); ex_when.add(when); ex_moment.add(moment)
            for axis, val in (("shot", shot[0]), ("setting", setting[0]), ("placement", placement),
                              ("light" if setting[1] else "time", when), ("moment", moment)):
                if val:
                    used[axis][val] += 1
            pairs.add((shot[0], setting[0]))

            tight = shot[0] in ("close", "bust", "detail", "macro", "reflect", "hands", "top", "bird")
            framing = f"{shot[1][0].upper()}{shot[1][1:]}" + ("" if tight else f", {placement}")
            if moment and shot[0] not in ("close", "bust"):
                framing += f", {moment}"
            prompt = (f"{subject} {framing}. {setting[0][0].upper()}{setting[0][1:]}, {when}. "
                      f"{pal}. Shot on {'an' if shot[2][0] == '8' else 'a'} {shot[2]}. {SPINES[spine][0].upper()}{SPINES[spine][1:]}.")
            rows.append({
                "card_id": c["id"], "name": c["name"], "strata": strata, "type": c["type"], "rarity": c.get("rarity"),
                "kind": kind, "subject_source": source, "generator": "flux", "spine": spine,
                "seed": stable_hash(c["id"]) % (2 ** 31),
                "axes": {"shot": shot[0], "placement": placement, "setting": setting[0],
                         "when": when, "moment": moment or "", "lead_colour": pal.split(",")[0]},
                "prompt": prompt,
            })
    return rows


# ─────────────────────────────────── commands ────────────────────────────────
def cmd_plan(a):
    rows = build_plan(a.spine)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps({"version": 1, "spine": a.spine, "cards": rows}, indent=2, ensure_ascii=False) + "\n")
    src = collections.Counter(r["subject_source"] for r in rows)
    print(f"card_art_prompt: planned {len(rows)} cards -> {OUT.relative_to(REPO)}  (subjects: {dict(src)})")
    return 0


def cmd_show(a):
    for r in build_plan(a.spine):
        if r["card_id"] == a.card_id:
            print(f"# {r['card_id']}  {r['strata']} {r['type']} ({r['kind']})  subject from {r['subject_source']}")
            print(f"# {len(r['prompt'].split())} words; axes: {json.dumps(r['axes'])}\n")
            print(r["prompt"])
            return 0
    print(f"no such card: {a.card_id}", file=sys.stderr)
    return 1


def overlap(a, b):
    return sum(1 for k in a["axes"] if a["axes"][k] and a["axes"][k] == b["axes"][k])


def cmd_variety(a):
    rows = build_plan(a.spine)
    by = collections.defaultdict(list)
    for r in rows:
        by[r["strata"]].append(r)
    for s, g in sorted(by.items()):
        line = [f"{s:<8} {len(g):>3} cards"]
        for axis in ("shot", "setting", "placement", "when"):
            vals = collections.Counter(r["axes"][axis] for r in g)
            line.append(f"{axis} {len(vals)} distinct (max {vals.most_common(1)[0][1]}x)")
        print("  ".join(line))
    worst = sorted(((overlap(x, y), x["card_id"], y["card_id"]) for s, g in by.items()
                    for i, x in enumerate(g) for y in g[i + 1:]), reverse=True)
    print(f"\nmost similar pair shares {worst[0][0]}/6 axes ({worst[0][1]} vs {worst[0][2]})" if worst else "")
    words = [len(r["prompt"].split()) for r in rows]
    print(f"prompt length {min(words)}-{max(words)} words (mean {sum(words) / len(words):.0f})")
    return 0


def cmd_verify(a):
    rows = build_plan(a.spine)
    problems = []
    by = collections.defaultdict(list)
    for r in rows:
        by[r["strata"]].append(r)
        if BANNED.search(r["prompt"]):
            problems.append(f"{r['card_id']}: negative phrase '{BANNED.search(r['prompt']).group(0)}'")
        n = len(r["prompt"].split())
        if not 30 <= n <= 120:
            problems.append(f"{r['card_id']}: {n} words, outside 30-120")
    for s, g in by.items():
        seen = {}
        for r in g:
            k = (r["axes"]["shot"], r["axes"]["setting"])
            if k in seen:
                problems.append(f"{s}: {seen[k]} and {r['card_id']} share shot+setting {k}")
            seen[k] = r["card_id"]
        for i, x in enumerate(g):
            for y in g[i + 1:]:
                if overlap(x, y) >= 4:
                    problems.append(f"{s}: {x['card_id']} and {y['card_id']} share {overlap(x, y)}/6 axes")
    if problems:
        print(f"card_art_prompt: {len(problems)} problem(s)", file=sys.stderr)
        for p in problems:
            print("  " + p, file=sys.stderr)
        return 1
    missing = sum(1 for r in rows if r["subject_source"] == "PLACEHOLDER")
    print(f"card_art_prompt: {len(rows)} prompts OK — no shared shot+setting, no near-twins, no negatives"
          + (f"; {missing} still need a subject line (run `brief`)" if missing else ""))
    return 0


def cmd_brief(a):
    c = next((c for c in load_cards() if c["id"] == a.card_id), None)
    if not c:
        print(f"no such card: {a.card_id}", file=sys.stderr)
        return 1
    who = Draw(stable_hash(c["id"] + "|who")).pick(WHO)
    user = (f"Card: {c['name']} ({c['strata'].title()} {c['type'].lower()}, {c.get('rarity', '').lower()}). "
            f"Flavor: {c.get('flavor', '')} Keywords: {', '.join(c.get('keywords', [])) or 'none'}.")
    if c["type"] == "CREATURE":
        user += f" If this is a person, make them {who}."
    print(json.dumps({"system": BRIEF_SYSTEM, "user": user}, indent=2, ensure_ascii=False))
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    for name in ("plan", "variety", "verify"):
        p = sub.add_parser(name); p.add_argument("--spine", default="v3.1", choices=sorted(SPINES))
    p = sub.add_parser("show"); p.add_argument("card_id"); p.add_argument("--spine", default="v3.1", choices=sorted(SPINES))
    p = sub.add_parser("brief"); p.add_argument("card_id")
    a = ap.parse_args()
    return {"plan": cmd_plan, "show": cmd_show, "variety": cmd_variety, "verify": cmd_verify, "brief": cmd_brief}[a.cmd](a)


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""
art_director.py — every card gets its OWN idea, and the idea has to be new.

WHY
---
Filling the same template for every card ("<creature> standing centre, <place>,
<light>") gives the same picture every time, however long the bank of words gets.
Variety has to come from somewhere that behaves like an art director:

  1. INVENT  A strong text model pitches several different concepts for the card:
             how many figures (none, one, two, a crowd), where the camera is, what
             moment of the story, what is odd about it. It is told what the game
             ALREADY has too much of (from the ledger), and gets a few random
             "sparks" to push it somewhere new.
  2. SCORE   Each concept is scored against EVERY card already in the ledger
             (shot, viewpoint, placement, setting, subject count, moment, action,
             subjects, mood). The most original concept that still fits the card
             wins. If none is original enough, the model is told why and tries again.
  3. PAINT   The chosen concept is painted, 2 candidates by default (optionally on
             different generators).
  4. CHECK   Each painting is scored against every other card painting: layout,
             colour, and — with --vision — what is actually in it. The most
             unique candidate is proposed. Nothing is installed until approved.
  5. REMEMBER The ledger records the concept, its tags and the painting's features.
             Every new card makes the next one work harder to be different, so the
             game stays fresh however deep the card list goes.

The style never varies — oil painting, chiaroscuro, the stratum palette. Everything
else can.

USAGE — the OpenRouter key is read from the environment, or from ~/.hermes/.env (override with
ART_ENV_FILE), so background runs work too. Add --mock to run offline (mock concepts can never be
rendered for real). Candidate paintings go to ~/runewake_art_archive/art_director (ART_DIRECTOR_WORK),
and the ledger to ~/runewake_art_archive/art_ledger.json (ART_LEDGER) — both outside the repo, so a
working-tree reset cannot delete them. `export-ledger` copies the ledger to pipeline/art_ledger.json to commit.
  art_director.py bootstrap [--vision]              put the existing card art in the ledger
  art_director.py plan <card_id…> | --stratum S | --missing | --worst N
  art_director.py render <card_id…> | --planned [--candidates 1] [--models flux,gemini]
  art_director.py sheet [card_id…] [--all|--run ID]  review pages for the LAST run only (default),
                                                    written to artifacts/art_review/<run>/ to commit
  art_director.py approve <card_id> <candidate#>    install as client/content/art/<id>.webp
  art_director.py score [--top 20]                  uniqueness of every card vs all others
  art_director.py show <card_id>
"""
import argparse
import collections
import json
import math
import os
import random
import re
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

TOOLS = Path(__file__).resolve().parent
REPO = TOOLS.parent
sys.path.insert(0, str(TOOLS))
sys.path.insert(0, str(REPO / "pipeline"))
from art_prompt import BANNED, PALETTES, stable_hash  # noqa: E402
from card_art_prompt import SPINES, legacy_subjects, load_cards, palette_line, Draw  # noqa: E402

# The ledger lives next to the paintings, outside the repo, so a working-tree reset between
# commands cannot wipe it. `export-ledger` copies it into the repo when it should be committed.
LEDGER = Path(os.environ.get("ART_LEDGER", str(Path.home() / "runewake_art_archive" / "art_ledger.json")))
WORK = Path(os.environ.get("ART_DIRECTOR_WORK", str(Path.home() / "runewake_art_archive" / "art_director")))
ART = REPO / "client" / "content" / "art"
API = "https://openrouter.ai/api/v1"


def _config_model():
    try:
        for line in open(REPO / "pipeline" / "config.yaml", encoding="utf-8"):
            m = re.match(r'\s*model:\s*"([^"]+)"', line)
            if m:
                return m.group(1)
    except OSError:
        pass
    return "google/gemini-2.5-pro"


DIRECTOR_MODEL = os.environ.get("ART_DIRECTOR_MODEL", _config_model())
VISION_MODEL = os.environ.get("ART_VISION_MODEL", DIRECTOR_MODEL)
GENERATORS = {"flux": "black-forest-labs/flux.2-pro", "gemini": "google/gemini-3-pro-image"}
SPINE = SPINES["v3.1"]

# ─────────────────────────── closed vocabularies (for honest comparison) ─────────
VOCAB = {
    "subject_count": ["none", "one", "two", "few", "many"],
    "shot": ["extreme close-up", "close-up", "medium", "full figure", "wide", "extreme wide"],
    "viewpoint": ["eye level", "low angle", "high angle", "overhead", "worm's-eye", "over the shoulder",
                  "point of view", "through a frame"],
    "placement": ["centre", "left third", "right third", "top", "bottom", "cropped by the edge", "scattered"],
    "story_moment": ["before", "during", "after", "everyday", "ceremony"],
}
FREE = ["subjects", "setting", "time_weather", "light", "action", "mood", "twist", "colour_key"]
WEIGHTS = {"shot": .11, "viewpoint": .08, "placement": .09, "subject_count": .08, "story_moment": .05,
           "setting": .13, "time_weather": .05, "light": .05, "action": .09, "subjects": .09, "mood": .04, "twist": .03,
           "colour_key": .11}

# What every card used to be (v3.0 prompts): the ledger starts by remembering it.
LEGACY_SETTING = {
    "EMBER": "volcanic forge interior, dark basalt walls, ember-lit haze",
    "HOLLOW": "crumbling barrow chamber, bone-littered floor, cold mist",
    "VERDANT": "dense forest clearing, moss-covered stone, shafts of green light through canopy",
    "TIDE": "submerged grotto, coral-covered pillars, dim aquatic light from above",
    "DAWN": "ancient temple interior at first light, warm stone, soft golden haze",
}

# Sparks: optional pushes, a few drawn per request. Combined with the model's own
# invention and the ledger feedback, the space is effectively endless.
SPARKS = [
    # how many
    "two of them, at odds with each other", "a parent and its young", "a whole pack, warband or congregation",
    "the subject absent — only its traces, tracks, shadow or leftovers", "the subject and its quarry",
    "the subject and whoever made or commands it", "a crowd reacting to it", "the last one standing among many fallen",
    "a tiny subject in an enormous world", "the subject so huge only part of it fits",
    # where the camera is
    "seen through the eyes of its prey", "through a keyhole, a crack or a gap in the fingers",
    "from underwater looking up", "from inside its lair looking out", "from a great height, looking straight down",
    "at ground level among grass, ash or bones", "reflected in a blade, a shield, an eye or a puddle",
    "as a shadow thrown across a wall", "from behind someone hiding", "over the shoulder of its opponent",
    # when
    "the second before it happens", "the aftermath", "a quiet everyday moment — eating, sleeping, mending, grooming",
    "a ceremony or a ritual", "a market day or festival", "a funeral", "a lesson or training",
    "an ambush", "a chase", "a rescue", "a betrayal", "a bargain being struck", "a homecoming",
    "the moment it is born, forged or summoned", "the morning after a great battle",
    # framing
    "an extreme close-up on one telling detail — a hand, an eye, a tool, a scar", "framed by an arch, a window or a ribcage",
    "a split composition: two halves that contrast", "a strong diagonal across the whole frame",
    "a silhouette against a blaze of light", "a deep foreground object with the subject far behind it",
    "the subject pushed to the very edge, the rest of the frame empty and meaningful",
    # place and weather
    "the weather is part of the action — storm, blizzard, flood, ashfall", "an eclipse, a blood moon or first light",
    "an interior nobody would expect it in", "the ruins of something once grand", "a place already changed by its power",
    "somewhere tiny — a jar, a nest, a pocket, a crack in a wall", "somewhere vast — a canyon, a cathedral, an open sea",
    "on a road, a bridge or a ship: somewhere in between", "a place of ordinary people: a kitchen, a smithy, a tavern",
    # tone
    "tender", "unexpectedly funny", "eerie and calm", "triumphant", "grieving", "quietly menacing", "full of wonder",
    "exhausted", "curious", "proud", "ashamed",
    # story
    "one detail that hints at a larger story", "an object that does not belong there", "evidence of an old battle",
    "a creature from another card appears in the background", "a humble onlooker gives scale and feeling",
    "an animal reacting to what is happening", "a trophy, an offering or a keepsake", "a message, sign or marking left behind",
]

DIRECTOR_SYSTEM = """You are the art director for Runewake, a dark-fantasy card game painted as classical storybook oil paintings.
Every card painting must be its OWN picture. The collection already has far too many paintings of one figure standing in the centre of the frame, seen full length at eye level, in the stratum's usual backdrop. Do not make another one of those.

For the card you are given, pitch several concepts that are radically different from each other AND from what the game already has (you will be told what is over-used and which existing cards are closest). Think like a film director and a storyteller:
- how many figures: none (show the effect, the traces, the aftermath), one, two, a few, a crowd or swarm
- where the camera is: close on a detail, far away, from above, from below, from the prey's eyes, through a frame
- which moment of the story: before, during, after, an everyday moment, a ceremony
- one surprising twist that makes the picture memorable
Each concept must still make a player think of THIS card at a glance: its name, its type and what it does.

Rules for the "prompt" field (it goes straight to an image model):
- 55 to 95 words of plain visual description, most important subject first
- describe only what is visible: subjects, action, place, weather, light, framing
- do not mention art style, painting, brushwork, colour palette or camera brands — those are added separately
- never use negative wording (no, not, without, never, avoid): describe what IS there
- no written words, letters or text inside the picture
- keep it card-game safe: menace, danger and struggle are fine, but never describe blood, gore, wounds, corpses,
  dead bodies, torture or killing blows — imply them (a shadow, an empty helmet, a broken blade)

Colour: each stratum has signature colours, but they are ACCENTS, not the whole picture. Choose a "colour_key"
for the painting — the dominant colours and light, e.g. "cold dawn greys with one warm lantern", "sunset amber
over black water", "moonlit silver-blue with a violet glow". Vary it as boldly as the composition; cards of the
same stratum must not all share one colour cast.

Return ONLY JSON: {"concepts": [ {
  "title": short name, "pitch": one sentence,
  "subject_count": one of none|one|two|few|many,
  "subjects": comma-separated list of what is in the picture,
  "shot": one of extreme close-up|close-up|medium|full figure|wide|extreme wide,
  "viewpoint": one of eye level|low angle|high angle|overhead|worm's-eye|over the shoulder|point of view|through a frame,
  "placement": one of centre|left third|right third|top|bottom|cropped by the edge|scattered,
  "story_moment": one of before|during|after|everyday|ceremony,
  "setting": where, "time_weather": when and what weather, "light": the light source,
  "action": what is happening, "mood": one or two words, "twist": the surprising element,
  "colour_key": the dominant colours and light of the painting,
  "fit": 1-5 how clearly this reads as the card,
  "prompt": the image prompt
} ] }"""

STOP = set("a an the of in on at to and with its his her their from into over under by for is are as it this that "
           "one two few many some very".split())


def words(s):
    return {w for w in re.findall(r"[a-z']+", (s or "").lower()) if w not in STOP and len(w) > 2}


def jaccard(a, b):
    a, b = words(a), words(b)
    return len(a & b) / len(a | b) if a and b else 0.0


def norm(field, value):
    v = (value or "").strip().lower()
    if field in VOCAB:
        for opt in VOCAB[field]:
            if v == opt or v.startswith(opt):
                return opt
        best = max(VOCAB[field], key=lambda o: jaccard(o, v))
        return best if jaccard(best, v) > 0 else v
    return v


def concept_similarity(a, b):
    s = 0.0
    for k, w in WEIGHTS.items():
        va, vb = a.get(k, ""), b.get(k, "")
        if not va or not vb:
            continue
        s += w * (1.0 if (k in VOCAB and va == vb) else (jaccard(va, vb) if k not in VOCAB else 0.0))
    return s


# ─────────────────────────────────── ledger ──────────────────────────────────
def load_ledger():
    if LEDGER.exists():
        return json.loads(LEDGER.read_text(encoding="utf-8"))
    return {"version": 1, "cards": {}}


def save_ledger(led):
    LEDGER.parent.mkdir(parents=True, exist_ok=True)
    LEDGER.write_text(json.dumps(led, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")


def overused(led, exclude=None):
    """The most common values in the ledger — the director is told to stay away from them."""
    counts = {k: collections.Counter() for k in VOCAB}
    setting_words = collections.Counter()
    n = 0
    for cid, e in led["cards"].items():
        if cid == exclude or not e.get("concept"):
            continue
        n += 1
        for k in VOCAB:
            if e["concept"].get(k):
                counts[k][e["concept"][k]] += 1
        setting_words.update(words(e["concept"].get("setting", "")))
    lines = []
    for k, c in counts.items():
        common = [f"{v} ({round(100 * x / max(n, 1))}%)" for v, x in c.most_common(3) if x / max(n, 1) >= 0.15]
        rare = [v for v in VOCAB[k] if c[v] / max(n, 1) < 0.08]
        if common:
            lines.append(f"- {k}: over-used → {', '.join(common)}; rarely used → {', '.join(rare) or 'none'}")
    top_words = [w for w, _ in setting_words.most_common(10)]
    if top_words:
        lines.append(f"- settings keep using: {', '.join(top_words)}")
    return n, "\n".join(lines)


def nearest(led, concept, k=6, exclude=None):
    scored = [(concept_similarity(concept, e["concept"]), cid) for cid, e in led["cards"].items()
              if cid != exclude and e.get("concept")]
    scored.sort(reverse=True)
    return scored[:k]


# ─────────────────────────────── image features ──────────────────────────────
def image_features(path):
    im = Image.open(path).convert("RGB").resize((64, 94))
    g = im.convert("L")
    small = np.asarray(g.resize((8, 12)), dtype=np.float32).flatten()
    lay = small - small.mean()
    lay /= (np.linalg.norm(lay) + 1e-6)
    hsv = np.asarray(im.convert("HSV"), dtype=np.int32)
    hist, _ = np.histogramdd(hsv.reshape(-1, 3) // np.array([32, 64, 64]), bins=(8, 4, 4), range=((0, 8), (0, 4), (0, 4)))
    hist = (hist.flatten() / hist.sum()).astype(np.float32)
    e = np.asarray(g.filter(ImageFilter.FIND_EDGES), dtype=np.float32) ** 2
    e[:3, :] = e[-3:, :] = 0
    e[:, :3] = e[:, -3:] = 0
    ys, xs = np.mgrid[0:e.shape[0], 0:e.shape[1]]
    w = e.sum() + 1e-6
    cx, cy = float((e * xs).sum() / w / e.shape[1]), float((e * ys).sum() / w / e.shape[0])
    return {"layout": [round(float(x), 4) for x in lay], "hist": [round(float(x), 5) for x in hist],
            "cx": round(cx, 3), "cy": round(cy, 3)}


def image_similarity(a, b):
    lay = max(0.0, float(np.dot(a["layout"], b["layout"])))
    hist = float(np.minimum(a["hist"], b["hist"]).sum())
    place = 1.0 - min(1.0, math.hypot(a["cx"] - b["cx"], a["cy"] - b["cy"]) * 4)
    return 0.5 * lay + 0.3 * hist + 0.2 * place


def uniqueness(led, cid, feats=None, concept=None):
    """0-100 against every other card: concept and painting. Higher is more original."""
    others = [(k, e) for k, e in led["cards"].items() if k != cid]
    c = concept or led["cards"].get(cid, {}).get("concept")
    f = feats or led["cards"].get(cid, {}).get("image")
    cs = max((concept_similarity(c, e["concept"]) for _, e in others if e.get("concept")), default=0) if c else None
    is_ = max((image_similarity(f, e["image"]) for _, e in others if e.get("image")), default=0) if f else None
    parts = [(1 - cs, .6)] if cs is not None else []
    if is_ is not None:
        parts.append((1 - is_, .4))
    if not parts:
        return None
    return round(100 * sum(v * w for v, w in parts) / sum(w for _, w in parts))


# ─────────────────────────────────── model calls ─────────────────────────────
def _key():
    """The key from the environment, else from the Hermes env file — a backgrounded shell often
    drops exported variables, and a missing key must not silently turn into an HTTP 401."""
    k = os.environ.get("OPENROUTER_API_KEY", "")
    if not k:
        env_file = Path(os.environ.get("ART_ENV_FILE", str(Path.home() / ".hermes" / ".env")))
        try:
            for line in env_file.read_text(encoding="utf-8").splitlines():
                m = re.match(r"\s*(?:export\s+)?OPENROUTER_API_KEY\s*=\s*(.+?)\s*$", line)
                if m:
                    k = m.group(1).strip().strip('"').strip("'")
        except OSError:
            pass
        if k:
            os.environ["OPENROUTER_API_KEY"] = k
    if not k:
        sys.exit("FATAL: OPENROUTER_API_KEY not in the environment or ~/.hermes/.env (or run with --mock)")
    return k


def chat_json(model, system, user, image_path=None, temperature=1.0, tries=4):
    """One JSON answer from a chat model. Every way a reply can come back empty — null content
    (a reasoning model that spent its budget thinking, a provider abort, a refusal), no choices,
    an error object, text that is not JSON — counts as a failed attempt and is retried. The last
    two attempts drop response_format, which some providers answer with empty content."""
    content = user
    if image_path:
        import base64
        b64 = base64.b64encode(open(image_path, "rb").read()).decode()
        content = [{"type": "text", "text": user}, {"type": "image_url", "image_url": {"url": f"data:image/png;base64,{b64}"}}]
    last = "no attempt"
    for attempt in range(tries):
        payload = {"model": model, "temperature": temperature, "max_tokens": 16000,
                   "messages": [{"role": "system", "content": system}, {"role": "user", "content": content}]}
        if attempt < tries - 2:
            payload["response_format"] = {"type": "json_object"}
        req = urllib.request.Request(f"{API}/chat/completions", data=json.dumps(payload).encode(),
                                     headers={"Authorization": f"Bearer {_key()}", "Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=300) as r:
                d = json.loads(r.read().decode())
            if d.get("error"):
                raise ValueError(f"provider error: {str(d['error'])[:200]}")
            choice = (d.get("choices") or [{}])[0]
            text = (choice.get("message") or {}).get("content")
            if isinstance(text, list):   # some providers return content parts
                text = "".join(p.get("text", "") for p in text if isinstance(p, dict))
            if not text or not text.strip():
                raise ValueError(f"empty reply (finish_reason={choice.get('finish_reason')!r})")
            text = re.sub(r"^```(?:json)?|```$", "", text.strip(), flags=re.M).strip()
            return json.loads(text[text.index("{"): text.rindex("}") + 1])
        except KeyboardInterrupt:
            raise
        except Exception as e:   # noqa: BLE001 — any bad reply is one failed attempt, never a crash
            last = f"{type(e).__name__}: {e}"
            print(f"  [director] attempt {attempt + 1}/{tries} failed — {last}", file=sys.stderr, flush=True)
            time.sleep(4 * (attempt + 1))
    raise RuntimeError(f"model call failed {tries} times; last: {last}")


def mock_concepts(card, n, rnd):
    """Offline stand-in for the director: random but structurally honest concepts."""
    out = []
    for i in range(n):
        c = {k: rnd.choice(v) for k, v in VOCAB.items()}
        spark = rnd.choice(SPARKS)
        c.update({"title": f"{card['name']} #{i}", "pitch": spark, "subjects": card["name"].lower(),
                  "setting": rnd.choice(["salt marsh", "ruined abbey", "forge", "sea cave", "wheat field", "bone chapel",
                                         "mountain pass", "tavern", "shipwreck", "bog"]),
                  "time_weather": rnd.choice(["dawn fog", "storm", "midnight", "snow", "noon"]),
                  "light": rnd.choice(["lantern", "lightning", "moonlight", "firelight"]),
                  "action": rnd.choice(["hunting", "resting", "fighting", "mourning", "bargaining"]),
                  "mood": rnd.choice(["tender", "menacing", "eerie"]), "twist": spark, "fit": rnd.choice([3, 4, 5]),
                  "prompt": f"{card['name']}, {spark}, in a {c['shot']} from {c['viewpoint']}, placed {c['placement']}, "
                            f"during a moment of {c['story_moment']}. A detailed scene with weather, a clear light source and "
                            f"a strong silhouette that reads at small size on a playing card."})
        out.append(c)
    return out


def mock_paint(out_path, seed):
    rnd = random.Random(seed)
    im = Image.new("RGB", (832, 1216), tuple(rnd.randrange(10, 60) for _ in range(3)))
    d = ImageDraw.Draw(im)
    for _ in range(rnd.randrange(3, 9)):
        x, y, r = rnd.randrange(0, 832), rnd.randrange(0, 1216), rnd.randrange(40, 320)
        d.ellipse((x - r, y - r, x + r, y + r), fill=tuple(rnd.randrange(40, 240) for _ in range(3)))
    im.save(out_path)
    return os.path.getsize(out_path)


# ─────────────────────────────────── building ───────────────────────────────
def card_brief(card, legacy):
    bits = [f"Card: {card['name']}", f"Stratum: {card['strata'].title()}", f"Type: {card['type'].lower()}",
            f"Rarity: {card.get('rarity', '').lower()}"]
    if card.get("type") == "CREATURE":
        bits.append(f"Attack {card.get('attack')} / Vigor {card.get('vigor')}")
    if card.get("keywords"):
        bits.append("Keywords: " + ", ".join(card["keywords"]))
    if card.get("abilities"):
        bits.append("Abilities: " + json.dumps(card["abilities"])[:400])
    if card.get("flavor"):
        bits.append(f"Flavor text: {card['flavor']}")
    if legacy.get(card["id"]):
        bits.append(f"The old painting showed: {legacy[card['id']][:220]} (make something different)")
    return "\n".join(bits)


def finish_prompt(concept, card):
    text = concept["prompt"].strip().rstrip(".")
    # A negative clause summons what it names (FLUX has no negative channel): drop the clause.
    clauses = re.split(r"(?<=[.,;])\s+", text)
    text = " ".join(c for c in clauses if not BANNED.search(c)).strip().rstrip(".,;") or text
    accents = PALETTES.get(card["strata"], "").split(" with ")[-1]
    key = (concept.get("colour_key") or "").strip().rstrip(".")
    colour = (f"Colour: {key}, with accents of {accents}" if key
              else palette_line(card["strata"], Draw(stable_hash(card["id"] + "|pal"))))
    return f"{text}. {colour}. {SPINE[0].upper()}{SPINE[1:]}."


VIOLENT = re.compile(r"\b(blood\w*|gore|gory|wound\w*|corpse\w*|dead body|bodies|carcass\w*|kill\w*|slain|slaughter\w*|"
                     r"decapitat\w*|severed|entrails|guts|butcher\w*|impal\w*|stab\w*|mutilat\w*|dying|death blow)\b", re.I)


def soften(prompt):
    """The generator's safety filter refused the painting: drop every clause that names violence."""
    clauses = re.split(r"(?<=[.,;])\s+", prompt)
    return " ".join(c for c in clauses if not VIOLENT.search(c)).strip() or prompt


def plan_card(led, card, legacy, n=5, mock=False, min_novelty=0.45, tries=3):
    feedback = ""
    rnd = random.Random(stable_hash(card["id"] + "|plan"))
    best = None
    for attempt in range(tries):
        count, digest = overused(led, exclude=card["id"])
        sparks = rnd.sample(SPARKS, 4)
        user = (card_brief(card, legacy) +
                f"\n\nThe game already has {count} card paintings. What it has too much of:\n{digest or '- nothing yet'}\n" +
                f"\nOptional sparks for this card (use one, combine them, or ignore them): {'; '.join(sparks)}.\n" +
                (f"\nYour last concepts were too close to existing cards: {feedback} Go much further.\n" if feedback else "") +
                f"\nPitch {n} concepts.")
        raw = mock_concepts(card, n, rnd) if mock else chat_json(DIRECTOR_MODEL, DIRECTOR_SYSTEM, user)["concepts"]
        cands = []
        for c in raw:
            c = {k: (norm(k, str(v)) if k in VOCAB else v) for k, v in c.items()}
            if not c.get("prompt") or int(c.get("fit", 0) or 0) < 3:
                continue
            near = nearest(led, c, k=1, exclude=card["id"])
            sim = near[0][0] if near else 0.0
            sib = max((concept_similarity(c, o) for o in cands), default=0.0)
            novelty = 1 - max(sim, sib * 0.8)
            score = novelty + 0.05 * int(c.get("fit", 3))
            cands.append(c | {"_novelty": round(novelty, 3), "_score": round(score, 3), "_nearest": near[0][1] if near else None})
        cands.sort(key=lambda c: c["_score"], reverse=True)
        if cands and (best is None or cands[0]["_score"] > best["_score"]):
            best = cands[0]
        if best and best["_novelty"] >= min_novelty:
            break
        if cands:
            top = cands[0]
            feedback = (f"the best one ('{top.get('title')}') still matched {top['_nearest']} on shot/placement/setting "
                        f"({top.get('shot')}, {top.get('placement')}, {top.get('setting')}).")
    if best is None:
        raise RuntimeError(f"{card['id']}: no usable concept")
    concept = {k: v for k, v in best.items() if not k.startswith("_")}
    return concept, best["_novelty"], finish_prompt(concept, card)


def select_cards(args, cards, led):
    by_id = {c["id"]: c for c in cards}
    if getattr(args, "ids", None):
        missing = [i for i in args.ids if i not in by_id]
        if missing:
            sys.exit(f"unknown card(s): {', '.join(missing)}")
        return [by_id[i] for i in args.ids]
    if getattr(args, "stratum", None):
        return [c for c in cards if c["strata"] == args.stratum.upper()]
    if getattr(args, "missing", False):
        return [c for c in cards if not (ART / f"{c['id']}.webp").exists() and c["id"] not in led["cards"]]
    if getattr(args, "worst", 0):
        # Least unique first, but spread across strata: five Tide cards in a row would share one
        # palette and hide whether the compositions really differ.
        scored = sorted((uniqueness(led, c["id"]) or 100, c["id"]) for c in cards if c["id"] in led["cards"])
        by_strata = collections.defaultdict(list)
        for _, i in scored:
            by_strata[by_id[i]["strata"]].append(i)
        picked = []
        while len(picked) < args.worst and any(by_strata.values()):
            for st in sorted(by_strata, key=lambda k: uniqueness(led, by_strata[k][0]) if by_strata[k] else 999):
                if by_strata[st] and len(picked) < args.worst:
                    picked.append(by_strata[st].pop(0))
        return [by_id[i] for i in picked]
    if getattr(args, "planned", False):
        return [by_id[i] for i, e in led["cards"].items() if e.get("status") == "planned" and i in by_id]
    sys.exit("say which cards: ids, --stratum, --missing, --worst N or --planned")


# ─────────────────────────────────── commands ────────────────────────────────
def cmd_bootstrap(a):
    led = load_ledger()
    legacy = legacy_subjects()
    n = 0
    for card in load_cards():
        path = ART / f"{card['id']}.webp"
        if not path.exists() or (card["id"] in led["cards"] and led["cards"][card["id"]].get("status") != "legacy"):
            continue
        feats = image_features(path)
        placement = "centre" if 0.4 <= feats["cx"] <= 0.6 else ("left third" if feats["cx"] < 0.4 else "right third")
        concept = {   # what the v3.0 prompt asked every painting to be — that is what the old art IS
            "subject_count": "one", "shot": "full figure", "viewpoint": "eye level", "placement": placement,
            "story_moment": "during", "subjects": legacy.get(card["id"], card["name"])[:160],
            "setting": LEGACY_SETTING.get(card["strata"], ""), "time_weather": "", "light": "dramatic rim light",
            "action": "standing, posed", "mood": "grim", "twist": "",
        }
        if a.vision and not a.mock:
            got = chat_json(VISION_MODEL, "Describe this card painting for a catalogue. Return ONLY JSON with the keys "
                            + ", ".join(list(VOCAB) + FREE) + ". For " + "; ".join(f"{k} use one of {'|'.join(v)}" for k, v in VOCAB.items())
                            + ".", f"The card is {card['name']}.", image_path=str(path), temperature=0.2)
            concept.update({k: norm(k, str(v)) if k in VOCAB else str(v) for k, v in got.items() if k in WEIGHTS})
        led["cards"][card["id"]] = {"status": "legacy", "strata": card["strata"], "concept": concept, "image": feats,
                                    "image_path": str(path.relative_to(REPO))}
        n += 1
    save_ledger(led)
    print(f"art_director: {n} existing paintings added to {LEDGER} ({len(led['cards'])} cards in the ledger)")
    return 0


def cmd_plan(a):
    led = load_ledger()
    legacy = legacy_subjects()
    failed = []
    for card in select_cards(a, load_cards(), led):
        print(f"{card['id']:<28} asking the art director…")
        try:
            concept, novelty, prompt = plan_card(led, card, legacy, n=a.concepts, mock=a.mock)
        except Exception as e:   # noqa: BLE001 — one bad card must not stop the batch
            failed.append(card["id"])
            print(f"{card['id']:<28} FAILED — {e}")
            continue
        prev = led["cards"].get(card["id"], {})
        led["cards"][card["id"]] = {"status": "planned", "mock": bool(a.mock), "strata": card["strata"], "concept": concept,
                                    "prompt": prompt, "novelty": novelty, "replaces": prev.get("image_path"),
                                    "old_image": prev.get("image")}
        save_ledger(led)   # after every card: the next card must be different from this one too
        print(f"{card['id']:<28} {'MOCK ' if a.mock else ''}novelty {novelty:.2f}  {concept.get('subject_count')}, {concept.get('shot')}, "
              f"{concept.get('viewpoint')}, {concept.get('placement')} — {concept.get('title', '')}")
    if failed:
        print(f"planned with {len(failed)} failure(s): {', '.join(failed)} — run plan again for just those ids")
        return 2
    return 0


def cmd_render(a):
    led = load_ledger()
    models = [m.strip() for m in a.models.split(",") if m.strip()]
    # FABLE-034: every render is a RUN. The ledger remembers every painting ever made, and the
    # review sheet used to show all of them — three runs of 6-7 cards came out as one 19-row
    # sheet that looked like a single enormous spend. Each card now carries the run that
    # painted it, and `sheet` shows the latest run unless told otherwise.
    run = time.strftime("%Y%m%d-%H%M%S")
    todo = [c for c in select_cards(a, load_cards(), led) if led["cards"].get(c["id"], {}).get("prompt")]
    print(f"run {run}: {len(todo)} card(s) × {a.candidates} candidate(s) = {len(todo) * a.candidates} painting(s)", flush=True)
    if not a.mock:
        _key()                        # before the import: gen_image_any reads the key when it loads
        import gen_image_any as gia   # noqa: E402
        gia.KEY = os.environ["OPENROUTER_API_KEY"]
    WORK.mkdir(parents=True, exist_ok=True)
    for card in select_cards(a, load_cards(), led):
        e = led["cards"].get(card["id"])
        if not e or not e.get("prompt"):
            print(f"{card['id']}: plan it first", flush=True)
            continue
        if e.get("mock") and not a.mock:
            print(f"{card['id']}: planned with --mock (placeholder concept) — run plan again without --mock first", flush=True)
            continue
        others = {k: v for k, v in led["cards"].items() if k != card["id"]}
        results = []
        for i in range(a.candidates):
            gen = models[i % len(models)]
            out = WORK / f"{card['id']}_{i + 1}.png"
            def paint(prompt):
                return (gia.via_images(prompt, str(out), GENERATORS[gen], 832, 1216) if gen == "flux"
                        else gia.via_chat(prompt, str(out), GENERATORS.get(gen, gen), "2:3"))
            if a.mock:
                ok = mock_paint(out, stable_hash(card["id"]) + i)
            else:
                ok = paint(e["prompt"])
                if not ok and soften(e["prompt"]) != e["prompt"]:
                    print(f"  {card['id']} candidate {i + 1} ({gen}) refused — retrying without the violent clauses")
                    ok = paint(soften(e["prompt"]))
            if not ok:
                print(f"  {card['id']} candidate {i + 1} ({gen}) failed")
                continue
            f = image_features(out)
            sim = max((image_similarity(f, o["image"]) for o in others.values() if o.get("image")), default=0)
            results.append({"n": i + 1, "generator": gen, "path": str(out), "image": f,
                            "image_similarity": round(sim, 3)})
        results.sort(key=lambda r: r["image_similarity"])
        e["candidates"] = results
        e["status"] = "rendered" if results else "planned"
        if results:
            e["run"] = run
            led["last_run"] = run
        save_ledger(led)
        if results:
            print(f"{card['id']:<28} best candidate #{results[0]['n']} ({results[0]['generator']}) "
                  f"closest match {results[0]['image_similarity']:.2f}")
    return 0


def cmd_sheet(a):
    led = load_ledger()
    run = a.run or led.get("last_run")
    if a.ids:
        ids, label = a.ids, "picked"
    elif a.all or not run:
        ids, label = [k for k, e in led["cards"].items() if e.get("status") == "rendered"], "all"
    else:
        ids, label = [k for k, e in led["cards"].items() if e.get("status") == "rendered" and e.get("run") == run], run
    rows = [(i, led["cards"][i]) for i in ids if led["cards"].get(i, {}).get("candidates")]
    if not rows:
        print("nothing rendered to show" + (f" for run {run} (try --all)" if run else ""))
        return 1
    W, H, TXTW, GAP = 240, 351, 640, 14
    ncand = max(len(e["candidates"]) for _, e in rows)
    row_h = H + GAP
    page_w = (1 + ncand) * (W + GAP) + TXTW
    try:
        font = ImageFont.truetype(str(REPO / "client" / "assets" / "fonts" / "Inter-Variable.ttf"), 16)
        small = ImageFont.truetype(str(REPO / "client" / "assets" / "fonts" / "Inter-Variable.ttf"), 14)
    except OSError:
        font = small = None
    pages = []
    for start in range(0, len(rows), a.rows):
        chunk = rows[start:start + a.rows]
        sheet = Image.new("RGB", (page_w, 40 + len(chunk) * row_h), (14, 12, 10))
        d = ImageDraw.Draw(sheet)
        d.text((10, 10), f"Runewake art review · run {label} · page {len(pages) + 1} of {math.ceil(len(rows) / a.rows)}"
               f" · approve with: art_director.py approve <card_id> <#>", fill=(200, 185, 150), font=font)
        for r, (cid, e) in enumerate(chunk):
            y = 40 + r * row_h
            x = 0
            old = e.get("replaces")
            if old and (REPO / old).exists():
                sheet.paste(Image.open(REPO / old).convert("RGB").resize((W, H)), (x, y))
                d.text((x + 6, y + 6), "OLD", fill=(230, 220, 200), font=font)
            x += W + GAP
            for c in e["candidates"]:
                try:
                    sheet.paste(Image.open(REPO / c["path"]).convert("RGB").resize((W, H)), (x, y))
                except (OSError, FileNotFoundError):
                    d.rectangle((x, y, x + W, y + H), outline=(90, 60, 50))
                    d.text((x + 6, y + 6), "missing file", fill=(200, 120, 100), font=small)
                d.text((x + 6, y + 6), f"#{c['n']} {c['generator']}", fill=(255, 230, 150), font=font)
                d.text((x + 6, y + H - 22), f"closest match {c['image_similarity']:.2f}", fill=(220, 200, 150), font=small)
                x += W + GAP
            x = (1 + ncand) * (W + GAP)   # the text column lines up whatever a row's candidate count
            con = e.get("concept", {})
            text = (f"{cid}  ·  novelty {e.get('novelty', 0):.2f}  ·  {e.get('status')}\n"
                    f"{con.get('title', '')}: {con.get('pitch', '')}")
            for k, line in enumerate(_wrap(text, 78)[:16]):
                d.text((x + 8, y + 6 + k * 20), line, fill=(215, 205, 185), font=small)
        pages.append(sheet)
    WORK.mkdir(parents=True, exist_ok=True)
    outs = []
    for i, pg in enumerate(pages):
        out = WORK / f"review_{label}_p{i + 1}.jpg"
        pg.save(out, quality=85)
        outs.append(out)
    print(f"{len(rows)} card(s) in run {label} → {len(outs)} page(s):")
    for o in outs:
        print(f"  {o}")
    if not a.no_export:
        dest = REPO / "artifacts" / "art_review" / str(label)
        dest.mkdir(parents=True, exist_ok=True)
        for o in outs:
            (dest / o.name).write_bytes(o.read_bytes())
        lines = [f"# Art review — run {label}", "",
                 f"{len(rows)} card(s). Pages: " + ", ".join(o.name for o in outs), "",
                 "Approve a candidate with `python3 tools/art_director.py approve <card_id> <#>`, then re-bake the plates.", ""]
        for cid, e in rows:
            con = e.get("concept", {})
            lines += [f"## {cid}", "", f"**{con.get('title', '')}** — {con.get('pitch', '')}", "",
                      "candidates: " + ", ".join(f"#{c['n']} {c['generator']} (closest {c['image_similarity']:.2f})" for c in e["candidates"]), "",
                      "```", (e.get("prompt") or "").strip(), "```", ""]
        (dest / "README.md").write_text("\n".join(lines), encoding="utf-8")
        print(f"exported to {dest.relative_to(REPO)}/ — commit that folder so the pages travel with the repo")
    return 0


def _wrap(text, n):
    out = []
    for para in text.split("\n"):
        line = ""
        for w in para.split():
            if len(line) + len(w) + 1 > n:
                out.append(line)
                line = w
            else:
                line = f"{line} {w}".strip()
        out.append(line)
    return out


def cmd_approve(a):
    led = load_ledger()
    e = led["cards"].get(a.card_id)
    c = next((c for c in (e or {}).get("candidates", []) if c["n"] == a.candidate), None)
    if not c:
        sys.exit(f"{a.card_id}: no candidate #{a.candidate}")
    dest = ART / f"{a.card_id}.webp"
    Image.open(REPO / c["path"]).convert("RGB").resize((832, 1216)).save(dest, "WEBP", quality=90)
    e.update({"status": "approved", "image": c["image"], "image_path": str(dest.relative_to(REPO)), "generator": c["generator"]})
    save_ledger(led)
    print(f"{a.card_id}: installed {dest.relative_to(REPO)} — uniqueness now {uniqueness(led, a.card_id)}/100 "
          f"(re-bake card plates before shipping)")
    return 0


def cmd_score(a):
    led = load_ledger()
    rows = sorted(((uniqueness(led, cid), cid, e.get("status")) for cid, e in led["cards"].items()),
                  key=lambda r: (r[0] is None, r[0]))
    vals = [r[0] for r in rows if r[0] is not None]
    if not vals:
        print("ledger is empty — run bootstrap")
        return 1
    print(f"{len(vals)} cards   uniqueness mean {sum(vals) / len(vals):.0f}/100   lowest {min(vals)}   highest {max(vals)}")
    print("least unique (regenerate these first):")
    for u, cid, st in rows[:a.top]:
        print(f"  {u:>3}  {cid:<28} {st}")
    return 0


def cmd_export_ledger(a):
    dest = REPO / "pipeline" / "art_ledger.json"
    dest.write_text(LEDGER.read_text(encoding="utf-8"), encoding="utf-8")
    print(f"copied {LEDGER} -> {dest.relative_to(REPO)}")
    return 0


def cmd_show(a):
    e = load_ledger()["cards"].get(a.card_id)
    if not e:
        sys.exit("not in the ledger")
    print(json.dumps({k: v for k, v in e.items() if k not in ("image", "old_image")}, indent=1, ensure_ascii=False))
    return 0


def main():
    sys.stdout.reconfigure(line_buffering=True)   # progress shows up live in a background log
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--mock", action="store_true", help="no API calls: fake concepts and paintings (for testing)")
    sub = ap.add_subparsers(dest="cmd", required=True)
    b = sub.add_parser("bootstrap"); b.add_argument("--vision", action="store_true")
    p = sub.add_parser("plan"); p.add_argument("ids", nargs="*"); p.add_argument("--stratum"); p.add_argument("--missing", action="store_true")
    p.add_argument("--worst", type=int, default=0); p.add_argument("--concepts", type=int, default=5)
    r = sub.add_parser("render"); r.add_argument("ids", nargs="*"); r.add_argument("--planned", action="store_true")
    r.add_argument("--candidates", type=int, default=1); r.add_argument("--models", default="flux")
    s = sub.add_parser("sheet"); s.add_argument("ids", nargs="*"); s.add_argument("--all", action="store_true")
    s.add_argument("--run"); s.add_argument("--rows", type=int, default=4); s.add_argument("--no-export", action="store_true")
    ap_ = sub.add_parser("approve"); ap_.add_argument("card_id"); ap_.add_argument("candidate", type=int)
    sc = sub.add_parser("score"); sc.add_argument("--top", type=int, default=20)
    sh = sub.add_parser("show"); sh.add_argument("card_id")
    sub.add_parser("export-ledger")
    a = ap.parse_args()
    for k in ("vision",):
        setattr(a, k, getattr(a, k, False))
    return {"bootstrap": cmd_bootstrap, "plan": cmd_plan, "render": cmd_render, "sheet": cmd_sheet,
            "approve": cmd_approve, "score": cmd_score, "show": cmd_show, "export-ledger": cmd_export_ledger}[a.cmd](a)


if __name__ == "__main__":
    sys.exit(main())

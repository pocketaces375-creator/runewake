#!/usr/bin/env python3
"""
composition_bank.py — twelve ways to frame a card, twelve ways to frame a relic (FABLE-044).

WHY
---
Trikzos, 2026-09-28: "Every piece looks exactly the same here for the weapons, similar issue for
the card arts themselves. It's all very — character standing in middle, stuff happening
behind/around. We need a good amount of off-centre, and more randomised prompts. Come up with 12
different prompts that give us wildly different resulting arts, and use those prompts randomly
for card generation moving forward."

The relic tiles were written that way ON PURPOSE: tools/gen_astrologist_tiles.sh and
ART_PROMPT_PLAYBOOK's isolation clause say "the object alone at the centre of the frame, resting
on a bare slab of dark stone … centred composition, eye-level". Twelve prompts obeying one
camera clause are one picture twelve times. The card side has art_director.py, which varies
concepts well, but it still lets the model fall back to a centred figure whenever it likes.

This file is the fix for both: a COMPOSITION is not a suggestion, it is the camera. Each of the
twelve below names the framing, where the subject sits, how big it is and what the viewer sees
first — and they are chosen to be as far apart from each other as a painting can be. Every new
painting draws one at random, weighted toward whichever have been used least, never repeating
the last few, so the set spreads out on its own.

The STYLE never changes (oil, chiaroscuro, stratum palette). Only the camera does.

USAGE
  composition_bank.py list [--kind card|relic]         print the twelve
  composition_bank.py pick <id> [--kind card|relic]    draw one (prints the directive)
  composition_bank.py spread <ledger.json>             how evenly the ledger's compositions are used
"""

import argparse
import collections
import json
import random
import sys
from pathlib import Path

# ───────────────────────────── cards: creatures, spells, rituals ─────────────────────────────
# (id, name, directive, tags) — tags use art_director's closed vocabularies so the ledger can
# compare honestly.
CARD = [
    ("offcentre_colossus", "Off-centre colossus",
     "The subject is pushed hard into the LEFT or RIGHT third of the frame and cropped by the edge, so only part of "
     "it fits; the empty two-thirds is open sky or landscape that shows how huge it is. Nothing in the centre.",
     {"shot": "wide", "placement": "cropped by the edge", "viewpoint": "eye level"}),
    ("worms_eye", "Worm's-eye looming",
     "Camera on the ground looking straight UP: the subject towers overhead with extreme foreshortening, its "
     "underside and silhouette against a dramatic sky, the frame's bottom edge full of grass, stones or debris.",
     {"shot": "full figure", "placement": "top", "viewpoint": "worm's-eye"}),
    ("birds_eye", "Bird's-eye",
     "Seen from DIRECTLY ABOVE, as a hawk would: the subject is a small shape on a patterned ground (flagstones, "
     "fields, snow, a map table), placed in a corner of the frame, casting a long hard shadow across it.",
     {"shot": "extreme wide", "placement": "scattered", "viewpoint": "overhead"}),
    ("over_shoulder", "Over the shoulder",
     "A dark, out-of-focus foreground figure (a witness, prey or rival, seen from behind) fills one side of the "
     "frame; the subject stands in the sharp middle distance beyond them, facing the viewer.",
     {"shot": "medium", "placement": "right third", "viewpoint": "over the shoulder"}),
    ("fragment", "The fragment",
     "An EXTREME CLOSE-UP of one part only — an eye, a claw, a hand, a mouth, a wing's edge — filling the frame "
     "and running off it. The rest of the subject is only implied. Texture and light at the scale of skin.",
     {"shot": "extreme close-up", "placement": "cropped by the edge", "viewpoint": "eye level"}),
    ("aftermath", "Aftermath",
     "The subject is GONE or barely visible (a distant silhouette leaving); the painting is what it left behind — "
     "tracks, scorch marks, frost, broken things, a changed place — told in the foreground.",
     {"shot": "wide", "placement": "bottom", "viewpoint": "high angle", "story_moment": "after", "subject_count": "none"}),
    ("diagonal_motion", "Caught mid-motion",
     "The subject is mid-leap, mid-charge or mid-cast along a steep DIAGONAL from one corner to the opposite, "
     "motion smearing the brushwork, debris and spray thrown ahead of it; tilted horizon.",
     {"shot": "full figure", "placement": "scattered", "viewpoint": "low angle", "story_moment": "during"}),
    ("framed_through", "Framed through",
     "Seen THROUGH something dark in the foreground — an archway, a broken window, a ribcage, tangled branches, a "
     "keyhole of rock — which frames the subject off-centre in a pool of light beyond.",
     {"shot": "medium", "placement": "left third", "viewpoint": "through a frame"}),
    ("reflection", "The reflection",
     "The subject is seen mainly as a REFLECTION — in still water, a puddle, a mirror, a polished shield or a "
     "great eye — while the real thing is only a sliver cropped at the top edge of the frame.",
     {"shot": "medium", "placement": "bottom", "viewpoint": "high angle"}),
    ("the_many", "The many",
     "Not one but MANY: a swarm, pack, procession or congregation receding into depth in a repeating rhythm, the "
     "nearest one large and cut off by the frame, the rest shrinking toward a bright horizon.",
     {"shot": "wide", "placement": "scattered", "viewpoint": "eye level", "subject_count": "many"}),
    ("vast_landscape", "Vast landscape",
     "EXTREME WIDE: the landscape and sky take two-thirds of the frame; the subject is a small, unmistakable "
     "silhouette in the lower third, on a ridge, shore or ruin, placed well off-centre.",
     {"shot": "extreme wide", "placement": "bottom", "viewpoint": "eye level"}),
    ("quiet_moment", "A quiet moment",
     "An unexpected INTIMATE moment, not a pose — resting, feeding its young, tending a wound, sharing food, "
     "sleeping — two figures interacting, seen close from a high three-quarter angle, the subject in the upper third.",
     {"shot": "close-up", "placement": "top", "viewpoint": "high angle", "story_moment": "everyday", "subject_count": "two"}),
]

# ───────────────────────────── relics: the artifacts ─────────────────────────────
# The object must still be the hero and readable at a thumb's width (the tiles show ~146 px
# wide), so every relic framing keeps the WHOLE silhouette of the object legible — only the
# camera, the setting and the moment change.
RELIC = [
    ("in_the_grip", "In the grip",
     "Held in an armoured or scarred HAND, mid-use, the hand cropped at the wrist by the frame edge; the object "
     "angled across the frame on a diagonal, sitting in the right third, the background a blurred battle or storm."),
    ("mid_strike", "Mid-strike",
     "In MOTION, its power erupting — a trail of sparks, arcane fire, frost or shadow sweeping behind it in an arc "
     "across the frame; steep diagonal, the object in the upper left, the effect filling the rest."),
    ("embedded", "Embedded",
     "Driven INTO something — lodged in rock, the root of a great tree, a frozen lake or an old altar — seen from a "
     "low angle so it rises against the sky, the landscape falling away behind."),
    ("trophy_wall", "On the trophy wall",
     "Hung or displayed in a dim hall among other relics, banners and candles, seen from across the room; the "
     "object in the right third in the one shaft of light, the rest of the room in warm shadow."),
    ("awakening", "Awakening",
     "LEVITATING above a ritual circle or a cracked floor, seen from below and to one side; light pouring out of "
     "it, dust and small stones hanging in the air around it."),
    ("left_on_the_field", "Left on the field",
     "Lying where it fell after a battle, seen from DIRECTLY ABOVE — in mud, snow or ash, with footprints, a torn "
     "cloak and a broken spear around it; the object placed in a corner of the frame, long shadow."),
    ("at_the_forge", "At the forge",
     "Being MADE: glowing on an anvil or a runesmith's bench, sparks flying, the smith's hands and tongs entering "
     "from the frame edge, the object off-centre in the lower third."),
    ("reflected", "Reflected",
     "Seen as a REFLECTION in dark still water or a pool of rain on flagstones; the real object is only glimpsed, "
     "cropped at the top edge, the reflection rippling and bright."),
    ("against_the_sky", "Against the sky",
     "Raised aloft or planted upright on a hilltop by a small distant figure; a vast, dramatic sky fills most of "
     "the frame and the object's silhouette cuts sharply against it, well off-centre."),
    ("half_revealed", "Half revealed",
     "Half UNWRAPPED — lifted from oilcloth, a lead-lined chest or a nest of straw — seen from a high angle; "
     "wrappings, seals and an old ribbon around it, the object crossing the frame diagonally."),
    ("sunken", "Sunken",
     "Deep UNDERWATER or locked in clear ice, shafts of light falling on it from above, small fish or bubbles "
     "drifting past; the object tilted and placed in the lower right."),
    ("still_life", "Old-master still life",
     "A Dutch-master STILL LIFE on a heavy table: the object laid among related things (a map, a candle stub, a "
     "cup, a skull or flowers to suit it), seen at a low three-quarter angle, the object running out of the left edge."),
]

KINDS = {"card": CARD, "relic": RELIC}


def by_id(kind="card"):
    return {c[0]: c for c in KINDS[kind]}


def pick(kind="card", used=None, recent=(), rng=None, avoid_last=3):
    """Draw one composition at random, weighted toward the least used, never one of the last
    `avoid_last` drawn. `used` = {composition_id: times used}; `recent` = ids, newest last."""
    rng = rng or random.Random()
    used = used or {}
    bank = KINDS[kind]
    blocked = set(list(recent)[-avoid_last:]) if avoid_last else set()
    pool = [c for c in bank if c[0] not in blocked] or list(bank)
    low = min(used.get(c[0], 0) for c in pool)
    # least-used get weight 4, one step above 2, the rest 1 — random, but the set spreads out
    weights = [4 if used.get(c[0], 0) == low else 2 if used.get(c[0], 0) == low + 1 else 1 for c in pool]
    return rng.choices(pool, weights=weights, k=1)[0]


def usage_from_ledger(ledger, kind="card"):
    """Counts and the recent order of compositions recorded in an art ledger."""
    used, order = collections.Counter(), []
    for cid, e in (ledger.get("cards") or {}).items():
        comp = (e.get("concept") or {}).get("composition")
        if comp and (kind == "relic") == cid.startswith("artf_"):
            used[comp] += 1
            order.append((e.get("planned_at", 0), comp))
    order.sort()
    return used, [c for _, c in order]


def main(argv=None):
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("list"); p.add_argument("--kind", default="card", choices=KINDS)
    p = sub.add_parser("pick"); p.add_argument("id"); p.add_argument("--kind", default="card", choices=KINDS)
    p = sub.add_parser("spread"); p.add_argument("ledger")
    a = ap.parse_args(argv)
    if a.cmd == "list":
        for i, c in enumerate(KINDS[a.kind], 1):
            print(f"{i:2}. {c[1]}\n    {c[2]}\n")
    elif a.cmd == "pick":
        c = pick(a.kind)
        print(f"{a.id}: {c[1]} — {c[2]}")
    elif a.cmd == "spread":
        led = json.loads(Path(a.ledger).read_text())
        for kind in KINDS:
            used, _ = usage_from_ledger(led, kind)
            print(f"{kind}: " + ", ".join(f"{c[0]}={used.get(c[0], 0)}" for c in KINDS[kind]))
    return 0


if __name__ == "__main__":
    sys.exit(main())

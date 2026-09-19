# RUNEWAKE ART VARIETY SYSTEM v1 (binding — Fable, 2026-09-19)

Companion to `ART_PROMPT_PLAYBOOK.md`. The playbook says how to write a *good*
prompt. This says how to stop 47 good prompts from producing 47 images that look
like the same picture.

## The problem, stated plainly

Every artifact tile looks like every other artifact tile, and the class
portraits have the same issue. Not because anyone did it wrong — because one
author writing every prompt by hand reaches for the same nouns, the same
lighting and the same camera every time, and a single generator lays its own
house style over all of it. Consistency and monotony are the same behaviour seen
from two sides.

## The rule

> **Variety is not a thing anyone remembers to do. It is a thing the tool cannot
> avoid doing.**

Prompts are no longer written by hand. `tools/art_prompt.py` composes them, and
nothing gets generated that the tool did not plan.

## What varies, and what never does

**Never varies — this is the brand:**

- the style spine: `dark fantasy, dramatic rim light, medieval woodcut influence, painterly edges`
- the isolation clause on tiles: the object alone, empty background falling to black
- the stratum palette for the asset's class, with hex codes
- the two hard rules: tiles are OBJECTS, never people; no negative phrases, ever

**Always varies — seven orthogonal axes, drawn per asset:**

| axis | why it matters |
|---|---|
| subject | the slot is a *category*; each item is a different object of it |
| material | body material, or fittings when the subject already names one |
| age / damage | new, worn, salvaged, burnt, corroded, long-buried |
| light | candle, north light, moonlight, backlit, firelight from below |
| camera | 35mm wide and small in frame vs 135mm tight vs f/2 macro |
| ground | stone slab, linen, floating, ash, shallow water, workbench |
| **treatment** | **impasto oil, tempera, ink wash, gouache, grisaille, woodcut line** |

The treatment axis is the one that does the heavy lifting. Two paintings of
different objects in the same medium still look like siblings; the same object in
egg tempera and in ink-and-wash do not.

## Generators rotate, and each is asked in its own dialect

A FLUX prose paragraph and an SDXL tag list are not the same request. Handing one
to the other wastes most of the prompt.

| generator | dialect | shape of the ask |
|---|---|---|
| `flux` | prose | one paragraph, subject first, 30–80 words |
| `gemini-image` | instruction | "Paint X… Render it as Y…" |
| `sdxl` | tags | comma-separated, subject tokens first |

The generator is chosen by a stable hash of the asset id, so neighbouring assets
usually land on different models and no single model's look dominates a slot.
Only generators listed in `ENABLED` are used — keep that in sync with what
actually has credentials on the Hermes box.

## Randomised, but never random

Every draw is seeded from an FNV-1a hash of the asset id — the same function the
engine uses for opponent loadouts. Python's own `hash()` is salted per process
and would re-roll the art on every run.

Consequences worth knowing:

- the same asset id always produces the same prompt, on any machine, forever;
- a regenerated asset is byte-comparable against its plan;
- changing an asset's **id** changes its art. Renaming ids is an art decision.

## Sibling collision is forbidden

Two items in the same slot pool may not share both subject and treatment. The
planner re-draws up to 12 times to avoid it, and `verify` fails the run if any
pair still collides. Two swords looking alike is the single most visible failure
this system exists to prevent.

## Workflow

```bash
python3 tools/art_prompt.py plan       # writes artifacts/art_plan.json — commit it
python3 tools/art_prompt.py verify     # gate: negatives, figures-on-tiles, collisions
python3 tools/art_prompt.py variety    # how distinct the set actually is
python3 tools/art_prompt.py show <id>  # the exact text one generator will receive
```

Generate from `art_plan.json` and nothing else. If a prompt needs changing,
change the **banks in the tool** so every future asset benefits, rather than
hand-editing one prompt.

`verify` belongs in the art gate alongside `art_check.py`. `art_check.py` judges
the produced image (placeholder, shape, figures, perceptual near-duplicates);
`art_prompt.py verify` judges the request before a single credit is spent.

## Current state

47 artifacts planned. 35 distinct subjects, 7 treatments, 7 lights, 7 grounds,
5 cameras, 11 materials. Zero sibling collisions. 5 pairs out of 1,081 share four
of seven axes and none of those are in the same slot.

## Extending it

Add to the banks, not to the prompts. A new slot pool needs a `SUBJECTS` entry
with at least four visibly different objects — if you cannot think of four, the
slot is too narrow and the design wants revisiting. New treatments are the
highest-value additions: each one multiplies against every other axis.

Class portraits are the one place a person appears, and they are not yet planned
by this tool — they need their own axes (pose, distance, wardrobe era, setting)
and a portrait-specific figure rule. That is the next piece of work here.

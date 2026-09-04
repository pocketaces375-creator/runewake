# RUNEWAKE ART PROMPT PLAYBOOK v1 (binding — Fable, 2026-09-04)

Every image the pipeline generates from now on is written with this playbook. It replaces
ad-hoc prompt writing. Sources: Black Forest Labs / fal.ai FLUX.2 prompt guide, LTX FLUX.2
prompting guide, and our own ART_STYLE_SPEC.md.

## 0. The two hard rules

1. **ARTIFACT TILES ARE OBJECTS. NEVER PEOPLE.** An artifact is the character's weapon or
   relic — the object alone, lying on a surface or floating, filling the frame. No figure, no
   face, no hands, no silhouette holding it, no armour worn by anyone. Class PORTRAITS are the
   only images that contain a person.
2. **NEVER write a negative prompt.** FLUX.2 has no negative channel; "no people", "no text",
   "--no hands" statistically *adds* what you named. State what IS there instead: an empty
   plinth, a bare stone slab, "the object alone", "the workshop is deserted".

## 1. Prompt anatomy (order matters — FLUX weights early tokens hardest)

    <SUBJECT: the object, its material, its condition>,
    <STATE: floating / resting on X / mid-swing on its own>,
    <STYLE: dark fantasy oil painting, heavy impasto, medieval woodcut influence>,
    <CONTEXT: surface, empty background, stratum palette with HEX, light direction>,
    <CAMERA: lens + aperture + framing>

Medium length: **30-80 words**. Longer is fine for a hero image; shorter loses control.
Put the thing that must be right FIRST. Never bury the subject behind mood words.

## 2. Fixed clauses

- Style spine (every image): `dark fantasy oil painting, heavy impasto brushwork, dramatic rim
  light, medieval woodcut influence, painterly edges`
- Isolation clause for tiles: `the object alone at the centre of the frame, resting on a bare
  slab of dark stone, empty unlit background falling to black`
- Camera for tiles: `shot on an 85mm lens at f/4, centred composition, eye-level, museum
  lighting from the upper left`
- Camera for portraits: `full figure, shot on a 50mm lens at f/2.8, low eye-level, rule of thirds`

## 3. Stratum palettes (use the HEX — FLUX honours hex codes)

| Stratum | Palette clause |
|---|---|
| EMBER   | `soot black #14100E with molten orange #C4501B and ash grey #6E655C` |
| TIDE    | `abyssal blue #0E2436 with teal #1F6F72 and pale foam #C9DCD8` |
| HOLLOW  | `bone white #D8CEBB with murky violet #3A2B45 and sickly green #6E7F4A` |
| VERDANT | `deep moss #1E2E1C with emerald #2F6B3A and wet bark brown #4A3626` |
| DAWN    | `warm gold #C8A04A with pale cream #EFE3C6 and amber #8A5A1E` |

(If a hex is mangled by an editor, use the colour words — never drop the palette clause.)

## 4. Variation is mandatory — no two items of a slot look alike

The slot name is a CATEGORY, not a description. Every item in a slot must be a visibly
different object of that category. Draw from these banks (and invent beyond them — creative
freedom is the point):

- **ORB** — an armillary sphere of tarnished brass rings; a smooth obsidian scrying stone; a
  cracked glass globe with a captured storm inside; a caged moon of silver wire; a floating
  drop of black water holding constellations; an eye-shaped amber lens; a nested orrery.
- **TOME / BOOK** — a chained iron-cornered grimoire; a bundle of birch-bark leaves stitched
  with root; a scroll case of carved antler; a wax tablet diptych; a book grown through with
  living moss and mushrooms; a folio bound in beetle-shell.
- **DAGGER** — a black-bladed parrying knife; a curved gutting hook; a thin needle stiletto;
  a leaf-bladed bronze dirk; a glass shard bound in leather cord.
- **BANNER** — a torn cavalry pennant; a processional standard topped with a sun-disc; a
  weathered war flag on a broken spear; a hanging tapestry-of-arms.
- **HAMMER / SWORD / SHIELD** — vary silhouette, era and damage: a smith's sledge vs a
  war-maul; a broken-tipped longsword vs a ritual falchion; a round wooden targe vs a
  battered kite shield.
- **SKULL / RITUAL PIECE** — a horned beast skull; a wax-sealed reliquary jar; a knotted
  fetish of finger-bones and hair; a censer trailing green smoke; a tallow candle in a jaw.
- **DRUID ITEMS — elements and creatures, always different ones.** This is the class's whole
  identity: freedom of combination. Rotate the element (stone, storm, fen-water, wildfire,
  frost, spore, root) AND the creature it belongs to (stag, toad, corvid, wolf, moth, boar,
  serpent, owl) so no two druid artifacts read as the same idea. E.g. "an antler circlet
  wrapped in storm-lit vines"; "a toad-shaped bog-stone weeping fen water"; "a moth-wing
  fan of pressed frost".

## 5. Worked examples

**Good (artifact tile):**
> A cracked crystal scrying orb the size of a fist, hairline fractures holding a captured
> starfield, resting alone on a bare slab of dark stone, empty unlit background falling to
> black. Dark fantasy oil painting, heavy impasto brushwork, dramatic rim light, medieval
> woodcut influence. Abyssal blue #0E2436 with teal #1F6F72 and pale foam #C9DCD8. Shot on an
> 85mm lens at f/4, centred composition, museum lighting from the upper left.

**Bad (why):**
> "an astrologer holding a magic orb, no people, epic, 8k" — puts a person in an item tile,
> uses a negative, and "epic/8k" carries no information FLUX can act on.

## 6. Acceptance checks (automated — tools/art_check.py)

Every generated image is checked before it is committed:

1. **Not a placeholder** — a 64px downsample must have 1000+ distinct colours.
2. **Right shape** — tiles 832x832 (stored 128px webp), portraits 832x1216.
3. **No humans in artifact tiles** — a vision pass answers "does this image contain a person,
   face, hand, or humanoid figure? yes/no". `yes` on an artifact tile = reject and regenerate
   with a rewritten subject clause (a different object from the variation bank).
4. **Variety** — two tiles in the same slot must not be near-duplicates (perceptual hash
   distance above the threshold in art_check.py).

A rejected image is regenerated up to 3 times, then the ticket reports BLOCKED with the
prompt and the reason. Never ship a placeholder, never ship a figure on an item tile.

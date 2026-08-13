# 03 — The Rune System v0.1

Modeled on League of Legends' pre-2017 rune pages: a pre-game loadout of small, stacking passives that you swap to counter what you're about to face. In a TCG this is a *second deck above the deck*, and it solves a real problem — it lets a player who is stuck on a wall change their approach without rebuilding 30 cards.

---

## 1. Structure

A **Rune Page** has 30 slots:

| Slot type | Count | Domain |
|---|---|---|
| **Marks** | 9 | Offense — damage, tempo, aggression |
| **Seals** | 9 | Endurance — Vigor, healing, card economy |
| **Glyphs** | 9 | Arcana — Attunement, Rituals, Excavate |
| **Sigils** | 3 | Identity — large, page-defining effects (our Quintessences) |

You unlock **Rune Pages** over the campaign: 1 at start, 2nd at Region 1 clear, 3rd at Region 2 clear, up to 6. Pages are swappable freely from the map screen, never mid-duel.

## 2. Rune Points (the anti-stacking budget)

Every rune has a **Rune Point (RP)** value of 1–4. A page has an RP budget that grows with your Delver Level:

| Delver Level | RP budget |
|---|---|
| 1 | 12 |
| 5 | 20 |
| 10 | 30 |
| 15 | 40 |
| 20 (cap) | 48 |

Slots are still capped at 9/9/9/3, so budget and slots bind against each other. A max page cannot be 30 copies of the best rune; it has to make choices. This is the single guardrail that keeps runes from becoming raw power creep.

## 3. Flat vs. Growing

Each rune exists in two flavors, mirroring old LoL's flat-vs-per-level split:

- **Flat** — full effect from turn one. Rewards aggression and fast decks.
- **Growing** — weaker early, exceeds the flat version from a stated turn onward (usually turn 5–6). Rewards control decks.

Example: *Mark of the Blade (Flat)* gives +1 Attack to your first creature summoned each turn. *Mark of the Blade (Growing)* gives +0 until turn 4, then +2. Same RP cost. Genuinely different decks want different ones — which is exactly the interesting choice the old system produced.

## 4. Starter rune list (v1 — 30 runes, each with Flat/Growing variants)

### Marks (offense)
| Rune | RP | Effect (Flat) |
|---|---|---|
| Mark of the Blade | 2 | First creature you summon each turn gets +1 Attack. |
| Mark of Kindling | 2 | Your Rituals deal +1 damage. |
| Mark of Haste | 3 | The first creature you summon each game gains Swift. |
| Mark of the Flank | 2 | Your creatures in edge lanes have +1 Attack. |
| Mark of the Vanguard | 1 | Your creatures deal +1 damage to players. |
| Mark of Venom | 4 | The first creature you summon each game gains Venom. |
| Mark of the Siege | 2 | Your creatures have +1 Attack while the opposing lane is empty. |

### Seals (endurance)
| Rune | RP | Effect (Flat) |
|---|---|---|
| Seal of Stone | 1 | +2 starting Vigor. |
| Seal of the Ward | 3 | Your first creature each game gains Ward. |
| Seal of Roots | 2 | Your creatures have +1 Vigor while in the center lane. |
| Seal of Mending | 2 | Heal 1 Vigor at the end of your turn. |
| Seal of the Keep | 3 | Your Guard creatures have +0/+1. |
| Seal of Patience | 2 | If you took no damage last turn, draw at end of turn (once per game). |
| Seal of the Barrow | 1 | Start with 1 card Buried. |

### Glyphs (arcana)
| Rune | RP | Effect (Flat) |
|---|---|---|
| Glyph of Attunement | 4 | +1 Attunement on turn 4 only. |
| Glyph of the Archive | 2 | Your first Ritual each game costs 1 less. |
| Glyph of Excavation | 3 | Excavate 1 at the start of turn 3. |
| Glyph of Insight | 2 | +1 maximum hand size. |
| Glyph of the Seal | 2 | Your Relics' Identify conditions require 1 less. |
| Glyph of Echoes | 4 | Your first Summon ability each game triggers twice. |
| Glyph of the Depths | 1 | Draw 1 fewer card on turn 1; draw 2 on turn 5. |

### Sigils (identity — 3 slots, RP 3–5 each)
| Sigil | RP | Effect |
|---|---|---|
| Sigil of the Delver | 4 | Begin the duel with a random Relic from your deck in hand. |
| Sigil of the Forge | 5 | Your first creature costing 5+ costs 2 less. |
| Sigil of the Drowned | 4 | The first time you Bury a card, draw 1. |
| Sigil of the Unburied | 5 | The first ally that dies returns to your hand. |
| Sigil of the Warden | 3 | Allies adjacent to a Guard creature have +0/+1. |
| Sigil of the Cartographer | 3 | Reveal the enemy's Rune Page at the start of the duel. |

## 5. How runes are earned

Never purchased. Rune **fragments** drop from excavation sites; 4 fragments forge a rune; duplicate runes convert to fragments. Sigils drop only from Warden Bosses and rare challenge encounters. This ties the meta-progression system directly to the archaeology layer, so digging feeds deckbuilding feeds digging.

## 6. Fairness stance for eventual PvP

When PvP ships there will be two queues: **Delve** (runes on) and **Pure** (runes off, cards only). Rune power stays capped by RP budget, and budget maxes at Delver 20 — a ceiling every free player reaches through the campaign alone. No rune is ever sold. State this in store copy; it is a differentiator worth being loud about.

## 7. Engine implementation note

Runes are implemented as a list of `AbilityDef` objects (same DSL as cards) attached to `PlayerState.RuneEffects` at match start and evaluated by the same trigger system. **Do not write a second effect system for runes.** If a rune effect can't be expressed in the card DSL, either extend the DSL for both or cut the rune.

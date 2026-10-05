# 01 — Duel Rules v0.1

Design goal: readable on a phone in one thumb-length screen, resolvable by a deterministic engine, and expressive enough that an LLM can invent thousands of distinct creatures inside it.

---

## 1. Setup

- Deck: exactly **30 cards**, max **2 copies** of any card, max **1 copy** of any Relic-rarity card.
- Deck may contain cards from **one or two Strata** (see §2).
- Each player starts at **25 Vigor** (life).
- Starting hand: **4 cards**. The player going second draws **6** (the "Second Delver" compensation: first-player advantage is offset by a larger opening hand).
- Both players mulligan once: select any subset to shuffle back, redraw the same number.

## 2. Strata (the five colors)

Strata are geological layers as much as factions. Each maps to regions on the world map, which is why deck theme and map theme reinforce each other.

| Stratum | Identity | Mechanical lean |
|---|---|---|
| **VERDANT** | Overgrown ruins, root and beast | Big bodies, growth counters, adjacency buffs |
| **EMBER** | Forge-holds, ash and iron | Direct damage, Swift, aggression, sacrifice-for-tempo |
| **TIDE** | Sunken cities, drowned archives | Draw, bounce, Excavate, delay effects |
| **HOLLOW** | Catacombs, rot, the unburied | Death triggers, Unearth, resource drain |
| **DAWN** | Temple wards, order, preservation | Guard, Ward, healing, protection, taxing effects |

## 3. Resources: Attunement

- Each player has an **Attunement** track. It increases by **1 at the start of each of your turns**, capping at **10**.
- Attunement refills fully each turn. There are no resource cards — no mana screw, no flooding. This is deliberate: on mobile, a loss caused by shuffle variance reads as a bug.
- Cards cost 0–10 Attunement. Attunement can be temporarily raised past 10 by effects but never permanently.

## 4. The board: five lanes

Each player has **5 lanes** (indexed 0–4), one creature per lane. Lanes face each other directly: your lane 0 opposes their lane 0.

```
ENEMY   [ 0 ][ 1 ][ 2 ][ 3 ][ 4 ]
YOU     [ 0 ][ 1 ][ 2 ][ 3 ][ 4 ]
```

This is the single most important rules choice after Attunement. Lanes give us:
- **Positional strategy** that matches the tactics-map fantasy without a full grid.
- A huge, cheap design space for AI-generated cards (adjacent, opposing, flanking, empty-lane, edge-lane effects).
- A clean read on a 6-inch screen.

When you summon a creature you choose an empty lane. Lane choice is a real decision every turn.

## 5. Turn structure

1. **Attune** — Attunement +1, refill.
2. **Draw** — draw 1. (First player skips their turn-one draw.)
3. **Start triggers** — `ON_TURN_START` resolves in board order, yours first.
4. **Main** — play cards, use activated abilities, in any order. Creatures may be declared as attackers here (there is no separate declaration step; tapping a ready creature attacks immediately).
5. **End** — `ON_TURN_END` triggers, hand size checked (max 10, discard excess), pass.

## 6. Combat

A creature that is **Ready** may attack once per turn. Creatures summoned this turn are **Exhausted** unless they have **Swift**.

Attacking is resolved per-lane and immediately:

- If the **opposing lane is occupied**, the two creatures deal damage equal to their Attack to each other simultaneously. Any creature at 0 or less Vigor is destroyed.
- If the **opposing lane is empty**, the attacker deals its Attack to the enemy player — *unless* the enemy controls a creature with **Guard** anywhere on their board, in which case you must attack a Guard creature's lane instead (choose one if multiple).
- **Pierce**: excess damage dealt to a destroyed blocker carries through to the enemy player.

Damage on creatures persists between turns. Creatures do not heal at end of turn.

## 7. Win condition

Reduce the opponent to **0 Vigor**. If a player must draw from an empty deck, they take **Fatigue**: 1 Vigor for the first, 2 for the second, escalating by 1 each time. No decking-out instant loss — fatigue makes long games end on a clock without feeling arbitrary.

## 8. Keyword set (CLOSED — v1)

The AI may only use these. Adding a keyword requires an engine change and a version bump.

| Keyword | Rules text |
|---|---|
| **Guard** | Enemies must attack a lane containing a Guard creature while one exists. |
| **Swift** | Not Exhausted the turn it is summoned. |
| **Pierce** | Excess combat damage to a destroyed creature hits the enemy player. |
| **Ward** | Prevents the next instance of damage dealt to this creature, then is removed. |
| **Venom** | Any creature damaged by this is destroyed at end of combat. |
| **Reach** | May attack the opposing lane or lanes adjacent to it. |
| **Rooted** | Cannot attack. (Used to price up defensive statlines.) |
| **Unearth N** | When destroyed, returns to its owner's hand next turn at cost N. Written without N (`UNEARTH`): it returns once, fresh, at the start of your next turn. |
| **Echo** | This card's `ON_SUMMON` ability triggers twice. Creatures only, and only on a creature that has one *(FABLE-SKILLS-1: it had been printed on 10 creatures with no entry effect and 7 rituals, where it did nothing — those creatures gained small entry effects, the rituals lost the word; tests/Cards/KeywordExplanationTests.cs keeps it so)*. |
| **Fragile** | Destroyed at end of the turn it was summoned. (Used for tokens and big tempo swings.) |
| **Sealed** | Cannot be targeted by enemy abilities. |
| **Exalted** | *(FABLE-DROP-1)* When your first attacker each turn attacks, it gets +1/+1 this turn for each Exalted creature you control. |
| **Armor N** | *(FABLE-DROP-1)* Damage dealt to this is reduced by N — every hit (Ward stops one hit; Armor shaves all of them). Written `ARMOR:N`, N = 1–4. |
| **Dodge N%** | *(FABLE-DROP-1)* N% chance to take no combat damage. Seeded RNG — the same game replays the same. Written `DODGE:N`, N = 10–50. |

Every keyword's player-facing explanation lives in one place, `RulesTextRenderer.KeywordReminder`, and every card view (duel slab, Reliquary page, Deck Forge hold-to-read) shows it. A new keyword without one fails `KeywordExplanationTests`.

### 8b. Statuses, lane rules and card rules (FABLE-DROP-1)

| Rule | Text |
|---|---|
| **Stun** | A stunned creature can't attack. Stun wears off at the end of its controller's next turn. |
| **Burn N** | At the start of its controller's turn, it takes N damage, then Burn drops by 1. A player can Burn too. |
| **Drain N** | That player has N less Attunement at the start of their next turn. |
| **Lock** | Nothing can be summoned into a locked lane, for N of its owner's turns. |
| **Tribute N** | To play this creature, destroy N of your creatures (play it onto one of them to choose; the rest are your weakest). Their death effects fire. |
| **Sigil** | A face-down trap. *Counter*: the next enemy Ritual is negated (still spent). *Ambush N*: the next enemy creature to attack takes N damage first. The count of Sigils is public; what they are is not. |
| **Redirect** | The next enemy attack this side takes hits that creature instead. |
| **Take control** | *Until end of turn*: the creature moves to one of your empty lanes, can attack, and goes home at end of turn. |
| **Swap / Double** | Swap a creature's Attack and Vigor; double them (this turn, unless permanent). |
| **Creature types** | Beast, Undead, Knight, Elemental, Spirit, Construct, Serpent, Starborn — for "your Beasts get…" effects. |
| **Aiming Rituals** | A Ritual is played on a lane. A single-target creature effect picks the creature in that lane (theirs, or yours for an ally effect); "opposing"/"diagonal" effects measure from that lane. |
| **Auras** | A PASSIVE ability on a creature or identified relic is an aura: it applies while the source is on the board and ends the moment it leaves. Losing an aura's Vigor never kills; an aura that takes Vigor away can. |
| **Durations** | "This turn" ends with the current turn; "until your next turn" ends when its owner's next turn starts. (Before FABLE-DROP-1 every buff was permanent.) |

## 9. Signature mechanics (the archaeology layer, in-game)

These three exist to make the duel feel like digging, and to give the generation pipeline flavorful hooks.

**Excavate N** — Look at the top N cards of your deck, put one into your hand, and **Bury** the rest.

**Bury** — Place a card face down in your **Barrow** (a third zone alongside deck and discard). Buried cards are inert but can be retrieved by Hollow and Tide effects, and count for "buried count" conditions. The Barrow is public-count, private-contents.

**Relics and Identification** — Relic cards enter play **Unidentified**: a face-down 0/3 artifact in a lane with no Attack. At the start of your turn, if its **Identify condition** is met, it flips and its full effect comes online permanently. Identify conditions are drawn from a fixed list (`3+ cards in your Barrow`, `you control 3 creatures`, `you took damage last turn`, `turn 6 or later`, `you cast 2 spells this game`). This creates a mid-game archaeology beat inside every match: you plant a mystery, you dig toward it, it wakes up.

## 10. Card types

- **CREATURE** — Attack / Vigor, occupies a lane.
- **RITUAL** — one-shot spell, resolves and goes to discard.
- **RELIC** — permanent, occupies a lane, enters Unidentified.
- **CURSE** — attaches to a creature or player, persistent modifier.
- **TOKEN** — created by effects, never in a deck.

## 11. Determinism and RNG

All randomness draws from a single seeded PRNG stored in `GameState.Rng`. Same seed + same action list = same game, always. Every match writes a replay file of `(seed, contentVersion, List<Action>)`. This is how we debug, how we validate PvP later, and how balance simulation stays reproducible.

## 12. Resolved decisions and remaining open questions

- **[RESOLVED] First-player advantage compensation** — The second player (P1) now draws 6 cards instead of 5. Confirmed by mirror-mismatch study (variant c, P0 winrate 57.3%, down from 63.0% baseline). The original +1 Attunement compensation was retired after testing showed it over-corrected (P0 winrate 19.6%).
- Is 25 Vigor right, or does 20 make games too fast at 5 lanes? (Sim first, then feel.)
- Should Guard force lane-attack or be Hearthstone-style global taunt? Current answer: lane-based, as written.

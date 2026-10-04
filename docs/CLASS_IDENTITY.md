# Runewake — Class Identities & Mechanics Plan v1.0

**Author:** Fable · **Date:** 2026-10-03 · **Status:** BUILT in FABLE-DROP-1 (50 cards, every mechanic below except Phasing) — see §5. Trikzos's direction, captured for the next card drop (~100 cards), which comes AFTER the art style is locked.

> Trikzos: "Cards automatically sort themselves. You can leave a 'high synergy' section … Any class can play any card, but it would suck in the wrong play style."

## 1. The principle

- **Any class can play any card.** Decks stay 30 cards from one or two strata (01_GAME_RULES §1).
- **A class makes some cards great.** Each class's artifacts and identity reward particular mechanics. A Warrior can run a healing card; it just won't sing the way it does for a Druid.
- **Cards sort themselves.** Every card is tagged with the mechanics it uses, derived automatically from its keywords and effects; nobody hand-sorts. Each class lists the mechanics it loves. The deck builder shows a **"High synergy"** shelf for your class: cards whose tags overlap your class's list, best first. Today the only thing close is `core_cards` in classes.json (4 per class); the shelf replaces that with something that grows on its own as cards are added.
- **Strata stay the colour identity.** Classes overlap strata on purpose: Rogue and Necromancer both draw on Hollow, Battlemage and Astrologist both on Tide. Mechanics, not colour, are what separate them.

## 2. The seven classes

✦ = set by Trikzos. ◇ = Fable's interpretation (Trikzos left these open). Change freely.

| Class | Stratum | Identity | What it loves (synergy tags) |
|---|---|---|---|
| **Warrior** | Ember | ✦ Large attack buffs. ◇ Heavy damage, Exalted (a lone attacker is a champion), doubling stats for a turn, Haste. ◇ "The one big swing." | ATTACK_BUFF, EXALTED, DOUBLE_STATS, HEAVY_DAMAGE, HASTE |
| **Battlemage** | Tide | ✦ Return card to hand, ✦ artifact disabling, ✦ counterspells. ◇ Spell-triggered creatures (mana-wyrm style), draw, redirecting. ◇ "Control the tempo, punish the plan." | BOUNCE, SUPPRESS, COUNTER, SPELL_TRIGGERED, DRAW, REDIRECT |
| **Necromancer** | Hollow | ✦ Resurrecting from the graveyard, ✦ token creation, ✦ tribute summoning. ◇ "When this dies" effects, Venom, burn tick (rot). ◇ "Death is a resource." | RESURRECT, TOKENS, TRIBUTE, ON_DEATH, VENOM, BURN |
| **Paladin** | Dawn | ✦ Taunt, ✦ defense buffs. ◇ Damage prevention, damage negation, healing allies, stall. ◇ "Nothing gets through." | GUARD, DEFENSE_BUFF, PREVENT, NEGATE, STALL, HEAL |
| **Druid** | Verdant | ✦ Healing. ◇ Adjacent buffs, spatial buffing (a growing grove), big bodies, attunement increasing (ramp), tribal beasts. ◇ "The garden grows." | HEAL, ADJACENT_BUFF, SPATIAL_BUFF, RAMP, TRIBAL_BEAST |
| **Rogue** | Hollow | ✦ Disabling artifacts, ✦ dodge chance. ◇ Venom/deathtouch, take control, discard/hand destruction, Haste. ◇ "Steal, slip, strike." | SUPPRESS, DODGE, VENOM, TAKE_CONTROL, DISCARD, HASTE |
| **Astrologist** | Tide | ✦ Locking down spaces, ✦ tribute summoning. ◇ Diagonal/directional debuffs, spatial debuffing, stun, flipping power and toughness, attunement decreasing. ◇ "The stars decide where you stand." | LANE_LOCK, TRIBUTE, DIAGONAL, SPATIAL_DEBUFF, STUN, FLIP, ATTUNE_DOWN |

## 3. Mechanics: what exists and what's new

Engine status is from a code read on 2026-10-03, to be verified card by card when the drop is built:
- **EXISTS**: already in the engine and used by cards today.
- **PARTIAL**: in the engine but limited.
- **NEW**: needs engine work.

Every new keyword bumps the closed keyword list in 01_GAME_RULES §8. Everything must stay deterministic (seeded RNG), because online play and co-op compare state hashes.

| Mechanic | Proposed rules text | Status |
|---|---|---|
| Haste | = **Swift** | EXISTS |
| Taunt | = **Guard** (lane-forced) | EXISTS |
| Death touch / venom | = **Venom** | EXISTS |
| Enter-the-battlefield | ON_SUMMON | EXISTS |
| When a card dies | ON_DEATH / ON_ALLY_DEATH / ON_CREATURE_DIES | EXISTS |
| Draw cards | DRAW | EXISTS |
| Healing | HEAL / HEAL_FULL | EXISTS |
| Return card to hand | BOUNCE | EXISTS |
| Disabling artifacts | SUPPRESS (artifact op) | EXISTS (cards can't target artifacts yet: PARTIAL) |
| Resurrect from graveyard | UNEARTH keyword, UNEARTH_FROM_GRAVEYARD, UNBURY | EXISTS |
| Token creation | SUMMON, REVIVE_TOKEN | EXISTS |
| Damage prevention | **Ward**, PREVENT_DAMAGE | EXISTS |
| Large attack / defense buffs | BUFF | EXISTS |
| Heavy damage | DAMAGE at high values | EXISTS (a design job, not engine) |
| Spell-triggered creatures | ON_CAST_RITUAL | EXISTS (one card uses it) |
| Attunement increasing | ATTUNE | EXISTS |
| Discard / hand destruction | DISCARD | PARTIAL (needs "enemy discards", random/chosen) |
| Adjacent buffs | PASSIVE + ADJACENT targeting | PARTIAL (the targeting exists; few cards) |
| Attunement decreasing | **Drain N**: enemy has N less Attunement next turn | NEW |
| Damage negation | **Armor N**: damage dealt to this is reduced by N (every hit, vs Ward's once) | NEW |
| Stun | **Stun**: a stunned creature can't attack on its controller's next turn | NEW |
| Burn tick damage | **Burn N**: at the start of its controller's turn, takes N damage, then Burn drops by 1 | NEW |
| Exalted | **Exalted**: whenever one of your creatures attacks alone this turn, it gets +1/+1 until end of turn for each Exalted you control | NEW |
| Dodge chance | **Dodge N%**: N% chance to take no combat damage (seeded RNG) | NEW |
| Flipping power and toughness | SWAP_STATS op | NEW |
| Doubling stats for a turn | DOUBLE_STATS op (THIS_TURN) | NEW |
| Redirecting | **Redirect**: the next attack/spell aimed at X hits Y instead | NEW |
| Take control | STEAL op: move an enemy creature into your empty lane (permanent or this turn) | NEW |
| Tribal | creature **types** (Beast, Undead, Knight, Elemental, …) + "your Beasts get…" filters | NEW (schema: `types` on cards) |
| Tribute summoning | **Tribute N**: destroy N of your creatures as part of the cost (cheaper/bigger bodies) | NEW (cost rule) |
| Counterspells | **Trap/Sigil**: set face-down; the next enemy ritual is negated. The game has no "respond?" windows, so counters are pre-set | NEW |
| Locking down spaces | **Lock**: a lane can't be summoned into (and/or attacked through) for N turns | NEW (lane status) |
| Diagonal / directional debuffs | targeting selectors DIAGONAL, OPPOSING, ROW, COLUMN for effects | NEW (targeting) |
| Spatial buffing / debuffing | lane auras: "creatures in lanes 1–3 get +1/+0" | NEW (lane auras) |
| Stall cards | e.g. "Enemy creatures can't attack your Vigor this turn", high-Vigor Rooted walls | PARTIAL (Rooted exists) |
| Phasing | — | NOT THIS DROP |

## 4. Build order for the drop (after art is locked)

1. **Engine, wave 1.** New keywords (Armor, Stun, Burn, Exalted, Dodge, Drain, Lock) and new ops (SWAP_STATS, DOUBLE_STATS, STEAL), with a rules test for each. Replay/hash determinism checked by the online rig.
2. **Engine, wave 2.** Card types and tribal filters, tribute cost, traps/counters, the directional selectors, lane auras, Redirect.
3. **Synergy tags + "High synergy" shelf** in the deck builder, and the class love-lists from §2.
4. **~100 cards**, about 14 per class, built on each class's love-list, plus shared cards. The bot learns the new mechanics (the GreedyBot scoring).
5. **Art** in the locked style, through the gold-set pipeline.
6. Balance sims (sim project) per class before ship.

## 5. FABLE-DROP-1 — what was built (2026-10-04)

Trikzos: "New skills all in this drop. 50 cards is good, all new stuff though."

- **Engine foundations fixed first** (each bug reproduced before the fix): Rituals did nothing; "when this enters
  play" re-fired every time anything was summoned; "this turn" buffs never wore off; "when this attacks" never
  fired; the Unearth keyword did nothing; after the 20th trigger of a match nothing ever triggered again;
  creature/relic PASSIVE auras did nothing; relics never identified; Venom and Fragile kills skipped death
  triggers; spells ignored Ward; a buried lane un-buried itself; ten artifacts' "when an ally is attacked" never
  fired; a debuff written as -1 gave +1.
- **Every mechanic in §3** (Phasing excepted) is in the engine with a rules test: tests/Engine/DropMechanicsTests.cs.
- **50 cards** (7 per class, 8 Astrologist) + 3 tokens, set `class_drop_1`, in the strata packs. Generator kept
  out of the repo; the cards are plain JSON.
- **High-synergy shelf**: engine/Cards/Synergy.cs tags every card from its keywords/types/effects; the Deck Forge
  has a "Synergy" chip that lists the cards that suit your class, best first.
- **Existing saves** get one copy of every drop card (two of their own class's) once — client/scripts/DropGrants.cs.
- **Bot** knows the new rules (aims rituals, tributes, avoids locked lanes, values statuses).
- **Balance** (GreedyBot sims, class cards ×2 in a deck of the stratum's best base cards vs every stratum):
  see the drop's handoff note. The base strata themselves are uneven under the bot (Dawn strong, Ember weak) —
  that predates this drop.

## 6. FABLE-DROP-2 — 100 more cards (2026-10-04)

Trikzos: "We're green light to do 100 cards."

- **100 cards** (14 per class; 15 Paladin and Druid) + 1 token (Squire), set `class_drop_2`, built on the drop-1
  mechanics. New in the engine: **"When this takes damage"** (`ON_DAMAGED`) now fires — it was in the card language
  from the start and never did — for the Warrior's enrage cards; aura keywords (a pack leader's Swift) count the
  moment a creature arrives.
- Warrior: enrage, extra attacks (Refresh), lone-champion cards. Battlemage: Sigils, recalls, ritual-fed creatures.
  Necromancer: tribute giants, mass raise-dead, rot. Paladin: Knights, Armor auras, Ward for all. Druid: packs,
  full heals, ramp, lane growth. Rogue: discard, Dodge auras, borrowing your champion, Kingsbane. Astrologist:
  stun-all, flips, lane locks, Drain engines, global debuffs.
- Synergy tags gained ENRAGE / EXTRA_ATTACK (Warrior) and SPELL_DAMAGE (Battlemage); conditions count too
  ("if it's your only creature" → Exalted players).
- Existing saves get the drop once (two copies of the cards that suit their class).

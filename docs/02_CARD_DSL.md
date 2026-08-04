# 02 — Card Effect DSL v0.1

This is the contract between the AI generator and the rules engine. **The LLM never writes rules text that the engine parses. It writes structured JSON from this closed vocabulary, and the human-readable rules text is *generated from the JSON*, not the other way around.**

If a card cannot be expressed in this DSL, the card does not exist. That constraint is a feature — it is what makes tens of thousands of generated cards executable and balanceable.

---

## 1. Card object

```json
{
  "id": "vrd_c_root_warden",
  "set": "buried_age",
  "name": "Root Warden",
  "strata": "VERDANT",
  "type": "CREATURE",
  "rarity": "COMMON",
  "cost": 3,
  "attack": 2,
  "vigor": 4,
  "keywords": ["GUARD"],
  "abilities": [
    {
      "trigger": "ON_SUMMON",
      "condition": null,
      "effects": [
        { "op": "BUFF", "target": { "scope": "ALLY_CREATURE", "filter": "ADJACENT", "count": "ALL" },
          "attack": 0, "vigor": 1, "duration": "PERMANENT" }
      ]
    }
  ],
  "identify_condition": null,
  "flavor": "The grove keeps its own ledgers.",
  "art": { "prompt": "...", "asset": "cdn://art/vrd_c_root_warden.webp" },
  "power_score": 7.1,
  "content_version": 1
}
```

`rules_text` is **derived** by a renderer function from `keywords` + `abilities`. Never authored, never stored as the source of truth. One renderer, one phrasing style, zero inconsistency across 10,000 cards.

## 2. Enumerations (CLOSED)

**STRATA** — `VERDANT | EMBER | TIDE | HOLLOW | DAWN`
**TYPE** — `CREATURE | RITUAL | RELIC | CURSE | TOKEN`
**RARITY** — `COMMON | UNCOMMON | RARE | RELIC`
**DURATION** — `PERMANENT | THIS_TURN | NEXT_TURN | WHILE_PRESENT`

**TRIGGER**
```
ON_SUMMON          ON_DEATH           ON_ATTACK          ON_DAMAGED
ON_TURN_START      ON_TURN_END        ON_CAST_RITUAL     ON_EXCAVATE
ON_RELIC_IDENTIFY  ON_ALLY_DEATH      ON_LANE_VACATED    PASSIVE
ACTIVATED          RESOLVE            /* RESOLVE = the body of a RITUAL */
```

**OP**
```
DAMAGE      HEAL        BUFF          DEBUFF        DESTROY
DRAW        DISCARD     EXCAVATE      BURY          UNBURY
SUMMON      GRANT_KEY   REMOVE_KEY    SILENCE       BOUNCE
ATTUNE      MOVE_LANE   IDENTIFY      GAIN_VIGOR    LOSE_VIGOR
COPY        SET_STAT    REFRESH       /* REFRESH = untap/make Ready */
```

**TARGET.scope**
```
SELF  ALLY_CREATURE  ENEMY_CREATURE  ANY_CREATURE
PLAYER_SELF  PLAYER_ENEMY  LANE  NONE
```

**TARGET.filter**
```
ANY  ADJACENT  OPPOSING  SAME_LANE  EDGE_LANE  CENTER_LANE
RANDOM  LOWEST_VIGOR  HIGHEST_ATTACK  LOWEST_COST  HIGHEST_COST
DAMAGED  UNDAMAGED  STRATA:<STRATUM>  KEYWORD:<KEYWORD>  TYPE:<TYPE>
CHOSEN   /* player picks a legal target */
```

**TARGET.count** — `1 | 2 | 3 | ALL`

**CONDITION.op**
```
ALLY_COUNT_GTE  ENEMY_COUNT_GTE  BARROW_COUNT_GTE  HAND_COUNT_GTE
HAND_COUNT_LTE  TURN_GTE  VIGOR_LTE  VIGOR_GTE  ATTUNEMENT_GTE
CONTROLS_KEYWORD  CONTROLS_STRATA  DAMAGED_THIS_TURN  RITUALS_CAST_GTE
```
Condition shape: `{ "op": "BARROW_COUNT_GTE", "value": 3 }`. Conditions may be combined with `{"all": [...]}` or `{"any": [...]}`, nested at most **two** levels deep.

## 3. Hard authoring rules for the generator

1. Max **2 abilities** per card. Max **2 effects** per ability.
2. Max nesting depth 2 in conditions. No recursion, no ability that creates an ability.
3. `SUMMON` may only summon a token defined in the same content pack, and a summoned token may not itself `SUMMON`. (Prevents infinite loops.)
4. Any `PASSIVE` ability must be `WHILE_PRESENT` duration.
5. Costs are integers 0–10. Attack 0–12. Vigor 1–14.
6. Only `RELIC` type cards may have `identify_condition`, and they must have one.
7. Names must be 1–4 words, English, and must not resemble any existing IP. The pipeline enforces this with a blocklist plus embedding-similarity check against a corpus of known franchise names.

## 4. Power scoring (v0 — to be calibrated by simulation)

```
base   = attack * 1.0 + vigor * 0.75
kw     = sum(KEYWORD_WEIGHT[k])
abil   = sum(effect_weight(e) * trigger_multiplier(t) * condition_discount(c))
score  = base + kw + abil

expected(cost) = 2.35 * cost + 0.9
delta = score - expected(cost)
```

Acceptance bands by rarity (tune these after the first 10k sim games — treat the numbers here as a starting hypothesis, not truth):

| Rarity | Allowed delta |
|---|---|
| COMMON | −0.8 … +0.4 |
| UNCOMMON | −0.5 … +0.9 |
| RARE | −0.3 … +1.5 |
| RELIC | 0.0 … +2.5 |

Starting keyword weights: `GUARD 0.9, SWIFT 1.1, PIERCE 0.6, WARD 1.0, VENOM 1.4, REACH 0.8, ROOTED −1.3, UNEARTH 1.2, ECHO 1.0, FRAGILE −1.6, SEALED 0.9`.

Trigger multipliers: `ON_SUMMON 1.0, ON_DEATH 0.7, ON_ATTACK 0.8, PASSIVE 1.3, ON_TURN_START 1.2, ACTIVATED 0.9`.
Condition discount: `1.0` if no condition, `0.75` if easily met, `0.55` if hard. The pipeline classifies difficulty from a fixed lookup table, not from model judgment.

## 5. Why generated rules text is derived, not written

A single renderer produces every card's text: `"Summon: Give adjacent allies +0/+1."` If we later decide "adjacent allies" should read "flanking allies," we change one function and 4,000 cards update. If instead each card carried an LLM-written sentence, we would own 4,000 slightly-different phrasings and a permanent localization nightmare. This is worth stating plainly because it is the exact place where AI-content projects usually rot.

## 6. Reference cards

See `schema/example_cards.json` — six hand-authored cards, one per Stratum plus one Relic. These are the few-shot examples fed to the generator and the fixtures for the engine's unit tests. They must always stay valid; if a schema change breaks them, the schema change is wrong until they're migrated.

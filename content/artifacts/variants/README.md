# Artifact Variant Files

Each class's extra artifacts live in their own file under this directory so lanes never edit the same file. The engine and client load every `*.json` in this directory in addition to `launch_artifacts.json`, using the same schema and validation.

## Schema

Every file must contain a JSON array of Artifact objects. Each object:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | yes | Unique artifact identifier, e.g. `artf_warrior_bastion_sword` |
| `class` | string | yes | Class this artifact belongs to (e.g. `warrior`, `rogue`, `battlemage`) |
| `slot_pool` | string | yes | Slot pool this artifact draws from (e.g. `sword`, `shield`, `dagger`, `wand`) |
| `name` | string | yes | Display name (single word preferred; two words only when essential to disambiguate) |
| `passive` | object | yes | Passive ability — always-on static effect while the artifact is not Suppressed. Expressed as an effect with `op` and `target`. Use `{ "op": "HEAL", "target": { "scope": "NONE" } }` for artifacts with no passive. |
| `trigger` | object \| null | yes | Triggered ability — fires when the specified event occurs, if condition is met. Null when no trigger exists. |
| `charges` | object \| null | yes | Optional Charge configuration. `{ "max": 3, ... }` or null if this artifact doesn't use Charges. |
| `full_charge` | array \| null | no | Effects to execute when this artifact reaches full charges (in addition to any trigger-defined ability). |
| `flavor` | string | no | Flavor text, max 140 characters. A single dark-fae sentence. |
| `art` | object | no | Art references — prompt for generation, asset URL after rendering. |
| `content_version` | int | no | Schema version. Defaults to 1. |

### Example

```json
[
  {
    "id": "artf_warrior_bastion_sword",
    "class": "warrior",
    "slot_pool": "sword",
    "name": "Bastion",
    "passive": { "op": "BUFF", "target": { "scope": "ALLY_CREATURE", "filter": "GUARD", "count": "ALL" }, "attack": 1, "vigor": 0, "duration": "WHILE_PRESENT" },
    "trigger": null,
    "charges": null,
    "flavor": "The wall that strikes back."
  }
]
```

### File naming convention

`<class>.json` — one file per class (e.g. `warrior.json`, `battlemage.json`, `necromancer.json`). Test fixtures use the prefix `fixture_`.

## Design rule: no strictly-better variants, ever

Every variant must be a **sidegrade** — meaningfully different from both the launch artifact and all other variants in the same slot_pool, without being strictly superior in power level. A variant is "strictly better" if:

1. **Higher raw stats** — same slot_pool, same condition, but larger numbers. If the launch Sword gives +1 attack to ALL attacking creatures, a variant that gives +2 attack to ALL attacking creatures with no compensating downside is strictly better.

2. **Same effect, no downside** — the variant does everything the launch artifact does, plus more, without any trade-off in activation cost, timing, or restriction.

3. **Niche superset** — the variant's effect applies in all the same situations as the launch artifact's effect, but in additional situations too, making the launch artifact obsolete.

Valid sidegrade patterns:

- **Different trigger condition**: one artifact rewards wide attacks (3+ attackers), another rewards tall attacks (single big creature).
- **Opposite timing**: one is defensive (buffs non-attackers), another is offensive (buffs attackers).
- **Risk vs reward**: one gives a reliable small bonus, another gives a larger bonus with a condition that can fail.
- **Pacing**: one gives an immediate effect, another has charge buildup for a bigger delayed payoff.
- **Slot competition**: if a variant is strong in one matchup but weak in another, the player chooses based on expected opponent.

If you are unsure whether a design passes the sidegrade test, check: "Would a player ever choose the launch artifact over this variant in at least some meaningful metagame scenarios?" If the answer is no, the variant is too strong and must be revised.
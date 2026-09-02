# Drops System — TASK-DROPS-DATA-1

## Schema

Every encounter in `content/encounters/*.json` may have a `drops` array:

```json
{
  "drops": [
    { "card_id": "vrd_c_root_warden", "rate": 0.40 },
    { "card_id": "vrd_r_bloomweaver", "rate": 0.10 }
  ]
}
```

- `card_id` — must match a card registered in `CardRegistry`
- `rate` — probability 0.0–1.0; rolled per card independently

## Default Rates by Rarity

| Rarity | Code | Rate |
|--------|------|------|
| Common | `c_`  | 0.40 |
| Uncommon | `u_` | 0.25 |
| Rare | `r_`  | 0.10 |
| Mythic | `x_`/`t_`/`m_` | 0.03 |

## Boss/Warden Rule

Wardens and final bosses drop their signature rare card at rate 1.00 (guaranteed)
in addition to their normal drop table.

## Seeded Roll

Drop rolls are deterministic per duel seed. The engine reads `drops` from the
encounter config, iterates each entry, and calls `GD.Randi()` seeded by the duel's
`GameConfig.Seed` offset by a per-encounter hash. Result is a `DropResult` list
of card IDs that dropped.

### Usage (planned)

1. `DuelScene.OnGameOver()` reads the encounter's drop list
2. Rolls each entry against the duel seed
3. Produces a `List<string>` of card IDs granted to the player
4. Cards are added to `ProgressionState.Collection` before the victory overlay renders
5. Victory overlay shows the drops as a reward row (UI deferred to TASK-DROPS-UI-1)

## Content Test

A permanent test in the engine test suite asserts:
- Every encounter has ≥3 drop entries where `rate < 1.0`
- Every `card_id` in a drop entry exists in `CardRegistry`
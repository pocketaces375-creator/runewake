# REGION_GEN.md — Region Generator Spec Format

## Overview

`tools/region_gen.py` produces all data files for a new campaign region from a single biome spec JSON file. It generates:

- `content/map/region_NN.json` — graph with unlock chain (nodes, types, connections)
- `content/encounters/region_NN_early.json` — early-game encounters
- `content/encounters/region_NN_mid.json` — mid-game encounters + elites
- `content/encounters/region_NN_late.json` — late-game encounters  
- `content/encounters/region_NN_boss.json` — Warden + Boss final encounters
- `content/dig_sites/region_NN_dig.json` — 4×4 dig site grid

Each generated encounter deck:
- Contains exactly **30 unique card IDs** (no duplicates)
- Cards drawn from the **primary stratum's card pool** plus **all cross-strata neutrals**
- Rarity weighted by encounter tier (early = more commons, boss = more rares/mythics)
- Includes **drop tables** following TASK-DROPS-DATA-1 rates (C 0.40 / U 0.25 / R 0.10 / M 0.03)
- Bosses/Wardens get a **signature card at 1.00 drop rate**
- Optionally validated through the **sim gate** against class starter decks (40–60% winrate band)

## Spec Format

The input JSON file has the following structure:

### Required Fields

| Field | Type | Description |
|-------|------|-------------|
| `name` | string | Display name for the region (e.g. "Cinderfall Steps") |
| `stratum` | string | Primary stratum: `VERDANT`, `EMBER`, `TIDE`, `HOLLOW`, or `DAWN` |
| `palette` | object | Color palette with `primary`, `secondary`, `accent`, `text`, `bg_dark` |

### Optional Fields

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `stratum2` | string | `null` | Secondary stratum (e.g. `"DAWN"` for cross-strata regions) |
| `region_id` | string | derived from filename | Override for the region ID (e.g. `"region_02"`) |
| `encounter_slots` | integer | `10` | Number of non-boss encounter nodes (8–12 recommended) |
| `elite_count` | integer | `1` | Number of elite encounters (placed in mid/late tiers) |
| `warden_name` | string | `"Warden"` | Name of the Warden encounter |
| `boss_name` | string | `"Boss"` | Name of the Boss (WardenBoss) encounter |
| `dig_name` | string | `"Dig Site"` | Name of the dig site node |
| `dig_description` | string | `"An ancient excavation site..."` | Flavor text for the dig site |
| `lore_blurb` | string | `""` | Short lore paragraph, not currently output to files |
| `signature_card` | string | `null` | Card ID for the guaranteed drop from Warden and Boss (e.g. `"vrd_r_bloomweaver"`) |
| `entry_encounter_name` | string | `"The Guardian"` | Name for the first encounter node |
| `encounter_names` | array | `[]` | Custom names for each encounter slot (index-mapped) |
| `elite_names` | array | `[]` | Custom names for elite encounters |
| `elite_modifiers` | array | `[]` | Modifier strings for elite encounters (e.g. `"All creatures have +1 attack"`) |
| `warden_intro` | array | auto-generated | Dialogue lines for Warden intro (array of strings) |
| `warden_outro` | array | auto-generated | Dialogue lines for Warden outro |
| `boss_intro` | array | auto-generated | Dialogue lines for Boss intro |
| `boss_outro` | array | auto-generated | Dialogue lines for Boss outro |

## Example Spec

```json
{
  "region_id": "region_02",
  "name": "Cinderfall Steps",
  "stratum": "EMBER",
  "stratum2": "HOLLOW",
  "palette": {
    "primary": "#8a3a2a",
    "secondary": "#c95a2a",
    "accent": "#e8a84c",
    "text": "#e8d8c8",
    "bg_dark": "#2a1a10"
  },
  "encounter_slots": 10,
  "elite_count": 2,
  "warden_name": "The Kilnwarden",
  "boss_name": "Kilnwarden's Fury",
  "dig_name": "The Slag Pit",
  "dig_description": "A collapsed forge-pit, still radiating heat. Something glows in the ash.",
  "signature_card": "emb_r_magma_forger",
  "lore_blurb": "Volcanic terraces rise above an ancient forge city. The air burns."
}
```

## Usage

```bash
# Basic generation
python tools/region_gen.py tools/specs/my_region.json --region-id region_02

# With deterministic seed
python tools/region_gen.py tools/specs/my_region.json --region-id region_02 --seed 12345

# With sim gate validation (requires Runewake.Sim binary)
python tools/region_gen.py tools/specs/my_region.json --region-id region_02 --validate

# Diff generated output against hand-built region_01 reference
python tools/region_gen.py tools/specs/my_region.json --diff
```

## Card Distribution by Tier

The generator distributes cards based on **rarity weights** per encounter tier:

| Tier | COMMON | UNCOMMON | RARE | MYTHIC |
|------|--------|----------|------|-------|
| early | 70% | 20% | 10% | 0% |
| mid | 50% | 30% | 15% | 5% |
| late | 35% | 35% | 20% | 10% |
| elite | 25% | 35% | 25% | 15% |
| warden | 20% | 30% | 30% | 20% |
| boss | 15% | 25% | 35% | 25% |

These weights control the **rarity pool sampling** — the generator picks a rarity by weight, then randomly selects a card of that rarity from the stratum's pool (plus neutrals). Higher tiers get increasing access to rare and mythic cards.

## Card Pool Composition

Each generated deck draws from:

1. **Primary stratum cards** — all cards tagged with the region's `stratum`
2. **Cross-strata neutrals** — cards from ALL other strata (provides variety, the same pattern the hand-built region_01 uses)

The `tutorial_pack.json` cards are intentionally excluded from neutral selection. The total card pool typically contains 65–80 cards across 5 strata.

## Drop Tables

Drops follow TASK-DROPS-DATA-1 conventions:

- Each card in the deck gets a drop entry at the **standard rate for its rarity**
- Standard rates: C=0.40, U=0.25, R=0.10, M=0.03
- **Warden and Boss encounters** insert a guaranteed signature drop at rate 1.00 as the first entry
- Every encounter has at least 3 drop entries (guaranteed by 30-card decks)

## File Output Structure

```
content/
├── map/
│   └── region_02.json          # Region graph (nodes, types, connections, unlocks)
├── encounters/
│   ├── region_02_early.json     # 2-3 early encounters
│   ├── region_02_mid.json       # 2-3 mid encounters + 1-2 elites
│   ├── region_02_late.json      # 2-3 late encounters
│   └── region_02_boss.json      # Warden + Boss (2 encounters)
└── dig_sites/
    └── region_02_dig.json       # 4×4 dig site with 4 strikes
```

## Region Graph Layout

The graph follows a tiered layout matching the `region_01` pattern:

- **Tier 0** (entry): 1 Duel node, no unlock condition
- **Tier 1**: 2 duel/shrine nodes, unlocked by clearing Tier 0
- **Tier 2**: 2 nodes (duel/elite), unlocked by Tier 1
- **Tier 3**: 3 nodes (duel/dig/merchant), unlocked by Tier 2
- **Tier 4**: 2 duel/elite nodes, unlocked by Tier 3
- **Tier 5**: 1 Warden node, unlocked by Tier 4
- **Tier 6**: 1 WardenBoss node, unlocked by clearing Warden

The dig node is placed at the widest tier (Tier 3) with connections mirroring its tier peers.

## Sim Gate Validation

When `--validate` is passed, the tool:
1. Writes each generated encounter deck as a temporary deck pack
2. Runs `Runewake.Sim` against 3 class starter decks (subset for speed)
3. Checks the encounter's winrate falls within the **40–60% band**
4. Reports PASS/FAIL per encounter

The validation is **informational** in the current version — it reports failures but does not automatically regenerate. Future iterations may add automatic regeneration when validation fails.

## Testing

```bash
# Run all tests
python tests/test_region_gen.py

# Tests include:
# - Card pool loading and filtering
# - Deck generation (size, uniqueness, determinism, rarity distribution)
# - Drop table generation per TASK-DROPS-DATA-1
# - Encounter structure (signature drops, dialogue, rewards)
# - Dig site structure (4×4 grid, 16 tiles, 4 strikes)
# - Full generation (all files produced, valid structure)
# - Diff against region_01 hand-built files
```
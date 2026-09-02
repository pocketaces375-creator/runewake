# First-Player Advantage Compensation Study — TASK-BALANCE-MIRROR-1

Generated: 2026-09-01 23:29:36 UTC
Games per class per variant: 500
Base seed: 42

## Variants Tested

| # | Variant | Description |
|---|---------|-------------|
| 0 | (a) baseline |
| 1 | (b) P1 +1 Attunement max on turn 1 |
| 2 | (c) P1 opening hand 6 instead of 5 |
| 3 | (d) b + c combined |
| 4 | (e) P0 turn-1 Attunement ramp delayed one turn |

## Class Name Mapping

The sim infrastructure uses pre-CLASS-7-FIX class names. The real classes
after CLASS-7-FIX and their sim equivalents:

| Sim Name | Real Class (CLASS-7-FIX) | Has Artifacts? |
|----------|--------------------------|----------------|
| warrior | warrior | yes |
| mage | battlemage | yes |
| thief | thief | yes |
| cleric | druid | yes |
| ranger | ranger | yes |
| necromancer | necromancer | yes |
| runesmith | paladin | yes |

Note: battlemage, druid, and paladin have no artifact definitions in
launch_artifacts.json, so their sim equivalents (mage, cleric, runesmith)
run without artifacts. The compensation study is still valid — it measures
structural first-player advantage, not class-specific power.

## P0 Win Rate per Variant (overall)

| Variant | Warrior | Mage | Thief | Cleric | Ranger | Necromancer | Runesmith | **Overall** |
|---|---|---|---|---|---|---|---|---|
| (a) baseline | 70.0% | 58.6% | 65.0% | 66.8% | 67.0% | 54.4% | 59.4% | **63.0%** |
| (b) P1 +1 Attunement max on turn 1 | 23.2% | 16.4% | 26.4% | 19.8% | 18.6% | 21.4% | 11.2% | **19.6%** |
| (c) P1 opening hand 6 instead of 5 | 66.8% | 52.8% | 59.2% | 62.2% | 62.0% | 51.2% | 47.0% | **57.3%** |
| (d) b + c combined | 18.2% | 12.6% | 17.4% | 16.0% | 15.4% | 15.6% | 5.8% | **14.4%** |
| (e) P0 turn-1 Attunement ramp delayed one turn | 19.8% | 17.8% | 20.4% | 19.6% | 18.6% | 20.2% | 13.2% | **18.5%** |

## Warrior (→ Warrior) — Per-Variant Results

| Variant | P0 Wins | P1 Wins | Total | P0 Win Rate | Avg Turns | Avg Cards P0 End | Avg Cards P1 End |
|---------|---------|---------|-------|-------------|-----------|-----------------|-----------------|
| (a) baseline | 350 | 150 | 500 | 70.0% | 7.1 | 4.3 | 5.8 |
| (b) P1 +1 Attunement max on turn 1 | 116 | 384 | 500 | 23.2% | 6.5 | 4.0 | 5.0 |
| (c) P1 opening hand 6 instead of 5 | 334 | 166 | 500 | 66.8% | 7.1 | 4.2 | 6.5 |
| (d) b + c combined | 91 | 409 | 500 | 18.2% | 6.4 | 4.1 | 5.9 |
| (e) P0 turn-1 Attunement ramp delayed one turn | 99 | 401 | 500 | 19.8% | 7.0 | 5.0 | 5.8 |

## Mage (→ Battlemage) — Per-Variant Results

| Variant | P0 Wins | P1 Wins | Total | P0 Win Rate | Avg Turns | Avg Cards P0 End | Avg Cards P1 End |
|---------|---------|---------|-------|-------------|-----------|-----------------|-----------------|
| (a) baseline | 293 | 207 | 500 | 58.6% | 10.2 | 2.9 | 4.2 |
| (b) P1 +1 Attunement max on turn 1 | 82 | 418 | 500 | 16.4% | 8.9 | 3.1 | 4.3 |
| (c) P1 opening hand 6 instead of 5 | 264 | 236 | 500 | 52.8% | 10.4 | 2.9 | 4.7 |
| (d) b + c combined | 63 | 437 | 500 | 12.6% | 8.4 | 3.2 | 5.2 |
| (e) P0 turn-1 Attunement ramp delayed one turn | 89 | 411 | 500 | 17.8% | 9.8 | 3.7 | 4.8 |

## Thief (→ Thief) — Per-Variant Results

| Variant | P0 Wins | P1 Wins | Total | P0 Win Rate | Avg Turns | Avg Cards P0 End | Avg Cards P1 End |
|---------|---------|---------|-------|-------------|-----------|-----------------|-----------------|
| (a) baseline | 325 | 175 | 500 | 65.0% | 10.1 | 3.4 | 3.9 |
| (b) P1 +1 Attunement max on turn 1 | 132 | 368 | 500 | 26.4% | 8.7 | 3.2 | 4.3 |
| (c) P1 opening hand 6 instead of 5 | 296 | 204 | 500 | 59.2% | 10.1 | 3.3 | 4.3 |
| (d) b + c combined | 87 | 413 | 500 | 17.4% | 8.3 | 3.3 | 5.0 |
| (e) P0 turn-1 Attunement ramp delayed one turn | 102 | 398 | 500 | 20.4% | 9.5 | 3.6 | 5.1 |

## Cleric (→ Druid) — Per-Variant Results

| Variant | P0 Wins | P1 Wins | Total | P0 Win Rate | Avg Turns | Avg Cards P0 End | Avg Cards P1 End |
|---------|---------|---------|-------|-------------|-----------|-----------------|-----------------|
| (a) baseline | 334 | 166 | 500 | 66.8% | 7.9 | 3.6 | 5.1 |
| (b) P1 +1 Attunement max on turn 1 | 99 | 401 | 500 | 19.8% | 7.0 | 3.7 | 4.6 |
| (c) P1 opening hand 6 instead of 5 | 311 | 189 | 500 | 62.2% | 7.9 | 3.5 | 5.8 |
| (d) b + c combined | 80 | 420 | 500 | 16.0% | 6.8 | 3.8 | 5.5 |
| (e) P0 turn-1 Attunement ramp delayed one turn | 98 | 402 | 500 | 19.6% | 7.7 | 4.5 | 5.3 |

## Ranger (→ Ranger) — Per-Variant Results

| Variant | P0 Wins | P1 Wins | Total | P0 Win Rate | Avg Turns | Avg Cards P0 End | Avg Cards P1 End |
|---------|---------|---------|-------|-------------|-----------|-----------------|-----------------|
| (a) baseline | 335 | 165 | 500 | 67.0% | 7.9 | 3.5 | 5.1 |
| (b) P1 +1 Attunement max on turn 1 | 93 | 407 | 500 | 18.6% | 7.0 | 3.7 | 4.5 |
| (c) P1 opening hand 6 instead of 5 | 310 | 190 | 500 | 62.0% | 8.0 | 3.4 | 5.8 |
| (d) b + c combined | 77 | 423 | 500 | 15.4% | 6.8 | 3.8 | 5.4 |
| (e) P0 turn-1 Attunement ramp delayed one turn | 93 | 407 | 500 | 18.6% | 7.7 | 4.4 | 5.2 |

## Necromancer (→ Necromancer) — Per-Variant Results

| Variant | P0 Wins | P1 Wins | Total | P0 Win Rate | Avg Turns | Avg Cards P0 End | Avg Cards P1 End |
|---------|---------|---------|-------|-------------|-----------|-----------------|-----------------|
| (a) baseline | 272 | 228 | 500 | 54.4% | 11.4 | 2.6 | 3.5 |
| (b) P1 +1 Attunement max on turn 1 | 107 | 393 | 500 | 21.4% | 10.0 | 2.6 | 3.9 |
| (c) P1 opening hand 6 instead of 5 | 256 | 244 | 500 | 51.2% | 11.7 | 2.4 | 4.0 |
| (d) b + c combined | 78 | 422 | 500 | 15.6% | 9.6 | 2.7 | 4.7 |
| (e) P0 turn-1 Attunement ramp delayed one turn | 101 | 399 | 500 | 20.2% | 11.1 | 2.9 | 4.2 |

## Runesmith (→ Paladin) — Per-Variant Results

| Variant | P0 Wins | P1 Wins | Total | P0 Win Rate | Avg Turns | Avg Cards P0 End | Avg Cards P1 End |
|---------|---------|---------|-------|-------------|-----------|-----------------|-----------------|
| (a) baseline | 297 | 203 | 500 | 59.4% | 10.8 | 2.3 | 3.2 |
| (b) P1 +1 Attunement max on turn 1 | 56 | 444 | 500 | 11.2% | 9.1 | 2.4 | 3.3 |
| (c) P1 opening hand 6 instead of 5 | 235 | 265 | 500 | 47.0% | 11.0 | 2.1 | 3.7 |
| (d) b + c combined | 29 | 471 | 500 | 5.8% | 8.4 | 2.4 | 4.2 |
| (e) P0 turn-1 Attunement ramp delayed one turn | 66 | 434 | 500 | 13.2% | 10.0 | 2.8 | 3.8 |

## Analysis

### Which variant lands nearest 50/50 without tipping to P1?

**Closest to 50/50**: (c) P1 opening hand 6 instead of 5 (overall P0 win rate = 57.3%)

**Note**: Winner is more than 1.5pp from baseline (63.0% vs 57.3%). The difference is significant enough to adopt directly.

| Variant | P0 Win Rate | Distance from 50% |
|---------|------------|-------------------|
| (a) baseline | 63.0% | 13.0pp |
| (b) P1 +1 Attunement max on turn 1 | 19.6% | 30.4pp |
| (c) P1 opening hand 6 instead of 5 | 57.3% | 7.3pp |
| (d) b + c combined | 14.4% | 35.6pp |
| (e) P0 turn-1 Attunement ramp delayed one turn | 18.5% | 31.5pp |


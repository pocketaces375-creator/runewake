# Artifact Metrics — TASK-S1

Generated: 2026-08-14 16:16:54 UTC

## Winrate Matrix (Row = P0 class, Column = P1 class)

| P0 \ P1 | Warrior | Mage | Thief | Cleric | Ranger | Necromancer | Runesmith |
|---|---|---|---|---|---|---|---|
| **Warrior** | 69.5% | 96.0% | 68.0% | 81.0% | 82.0% | 92.5% | 96.5% |
| **Mage** | 26.0% | 54.5% | 25.5% | 30.5% | 32.0% | 46.5% | 56.0% |
| **Thief** | 72.5% | 90.0% | 67.5% | 84.0% | 88.5% | 84.5% | 94.0% |
| **Cleric** | 58.0% | 87.0% | 49.5% | 66.5% | 69.5% | 81.5% | 86.0% |
| **Ranger** | 57.0% | 84.0% | 41.5% | 64.0% | 66.0% | 77.0% | 82.5% |
| **Necromancer** | 26.0% | 57.5% | 27.5% | 37.0% | 42.0% | 49.5% | 55.5% |
| **Runesmith** | 26.0% | 57.5% | 11.5% | 32.5% | 37.0% | 49.5% | 57.0% |

## Deviation Rate (per class, aggregated across all opponents)

For each class as P0: % of non-empty combat turns where the bot chose
NOT to attack with all eligible attackers (target ≥ 25%).

| Class | Total Combat Turns | Deviation Turns | Deviation Rate |
|-------|-------------------|----------------|----------------|
| Warrior | 11841 | 2188 | 18.5% |
| Mage | 17330 | 4834 | 27.9% ✓ |
| Thief | 14386 | 4270 | 29.7% ✓ |
| Cleric | 13325 | 2540 | 19.1% |
| Ranger | 13553 | 2690 | 19.8% |
| Necromancer | 18918 | 5377 | 28.4% ✓ |
| Runesmith | 18372 | 4777 | 26.0% ✓ |

### As P1 (opponent bot decisions)

| Class | Total Combat Turns | Deviation Turns | Deviation Rate |
|-------|-------------------|----------------|----------------|
| Warrior | 12973 | 2206 | 17.0% |
| Mage | 15485 | 4332 | 28.0% ✓ |
| Thief | 17895 | 5267 | 29.4% ✓ |
| Cleric | 13701 | 2700 | 19.7% |
| Ranger | 13607 | 2824 | 20.8% |
| Necromancer | 17646 | 5126 | 29.0% ✓ |
| Runesmith | 16418 | 4221 | 25.7% ✓ |

**Overall**: 26676 deviation turns out of 107725 combat turns (24.8%)
Target: ≥ 25% ✗ NOT MET

## Per-Class Winrates (aggregated across all opponents)

| Class | Wins | Games | Win Rate |
|-------|------|-------|----------|
| Warrior | 1171 | 1400 | 83.6% |
| Mage | 542 | 1400 | 38.7% |
| Thief | 1162 | 1400 | 83.0% |
| Cleric | 996 | 1400 | 71.1% |
| Ranger | 944 | 1400 | 67.4% |
| Necromancer | 590 | 1400 | 42.1% |
| Runesmith | 542 | 1400 | 38.7% |

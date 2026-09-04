# TASK-TUNE-5050-1 — Class Balance Matrix

Generated: 2026-09-04 03:04:56 UTC
Iteration: 2
Games per matchup: 200
Base seed: 42
Target band: [45%, 55%]
Status: IN PROGRESS
Violations: 3 

### Adjustments applied
- Paladin Banner heal: 2 → 1 (bound 1-3)
- Druid Book Familiar attack: 1 → 2 (bound 1-2)
- Druid Book Familiar vigor: 1 → 2 (bound 1-2)
- Constellation Starlight damage: 4 → 5 (bound 3-6)
- battlemage_wand: passive atk 1 → 0
- battlemage_aura: passive vig 1 → 0
- necromancer_skull: COST_MOD 1 → 2
- necromancer_skull: revive atk 3 → 4
- necromancer_skull: revive vig 3 → 4
- paladin_hammer: passive atk 1 → 0
- warrior_sword: passive atk 1 → 0
- warrior_shield: passive vig 1 → 0

## Winrate Matrix (P0 vs P1)

```
       P0\P1  Battlemage  Necromancer  Paladin   Druid   Rogue  Astrologist  Warrior
  Battlemage  51.0%  28.5%  8.5%!  59.0%  26.5%  47.5%  32.0%
 Necromancer  74.0%  43.5%  46.5%  60.0%  54.0%  53.0%  56.0%
     Paladin  96.0%!  61.0%  59.0%  83.5%!  62.0%  81.0%!  82.5%!
       Druid  87.0%!  77.0%!  6.5%!  95.5%!  81.0%!  78.0%!  79.5%!
       Rogue  76.5%!  49.0%  33.5%  60.5%  47.5%  52.0%  52.0%
 Astrologist  59.5%  33.5%  16.5%!  46.5%  35.5%  54.0%  40.5%
     Warrior  68.0%  41.5%  32.5%  63.0%  44.5%  61.5%  53.0%
```

## Per-Class Winrates (mirrors excluded)

| Class | Winrate | Wins | Games | Best Matchup | Worst Matchup |
|-------|---------|------|-------|--------------|---------------|
| Battlemage | 28.4% ⚠ | 682 | 2400 | Astrologist (44.0%) | Paladin (6.2%) |
| Necromancer | 54.4% | 1306 | 2400 | Battlemage (72.8%) | Druid (41.5%) |
| Paladin | 76.8% ⚠ | 1844 | 2400 | Battlemage (93.8%) | Necromancer (57.2%) |
| Druid | 53.0% | 1273 | 2400 | Astrologist (65.8%) | Paladin (11.5%) |
| Rogue | 51.7% | 1240 | 2400 | Battlemage (75.0%) | Paladin (35.8%) |
| Astrologist | 38.2% ⚠ | 918 | 2400 | Battlemage (56.0%) | Paladin (17.8%) |
| Warrior | 47.4% | 1137 | 2400 | Battlemage (68.0%) | Paladin (25.0%) |

## Mirror Match P0 Winrates
- Battlemage: P0 51.0%
- Necromancer: P0 43.5%
- Paladin: P0 59.0%
- Druid: P0 95.5%
- Rogue: P0 47.5%
- Astrologist: P0 54.0%
- Warrior: P0 53.0%

## Violations
- battlemage: 28.4% outside [45%, 55%]
- paladin: 76.8% outside [45%, 55%]
- astrologist: 38.2% outside [45%, 55%]


AI: GreedyBot (TASK-AI-TACTICIAN-1 pending)
Thresholds: per-class [45%, 55%], 200 games/pairing

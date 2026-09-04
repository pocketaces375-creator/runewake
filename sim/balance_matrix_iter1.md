# TASK-TUNE-5050-1 — Class Balance Matrix

Generated: 2026-09-04 10:22:57 UTC
Iteration: 1
Games per matchup: 200
Base seed: 42
Target band: [45%, 55%]
Status: IN PROGRESS
Violations: 7 

## Winrate Matrix (P0 vs P1)

```
       P0\P1  Battlemage  Necromancer  Paladin   Druid   Rogue  Astrologist  Warrior
  Battlemage  65.0%  76.0%!  52.0%  99.5%!  76.5%!  85.5%!  54.0%
 Necromancer  29.5%  46.5%  22.5%!  66.5%  45.0%  54.0%  17.0%!
     Paladin  73.0%  81.5%!  63.5%  98.5%!  82.0%!  96.0%!  60.5%
       Druid  0.0%!  67.0%  0.0%!  98.0%!  78.5%!  77.0%!  0.0%!
       Rogue  35.5%  54.5%  22.0%!  65.0%  47.5%  55.5%  20.5%!
 Astrologist  32.0%  41.0%  13.5%!  52.5%  35.0%  53.5%  14.0%!
     Warrior  76.0%!  91.5%!  67.5%  100.0%!  88.5%!  91.5%!  68.0%
```

## Per-Class Winrates (mirrors excluded)

| Class | Winrate | Wins | Games | Best Matchup | Worst Matchup |
|-------|---------|------|-------|--------------|---------------|
| Battlemage | 66.5% ⚠ | 1595 | 2400 | Druid (99.8%) | Warrior (39.0%) |
| Necromancer | 35.2% ⚠ | 846 | 2400 | Astrologist (56.5%) | Warrior (12.8%) |
| Paladin | 76.2% ⚠ | 1828 | 2400 | Druid (99.2%) | Warrior (46.5%) |
| Druid | 28.4% ⚠ | 681 | 2400 | Astrologist (62.3%) | Warrior (0.0%) |
| Rogue | 37.3% ⚠ RUSH | 895 | 2400 | Astrologist (60.2%) | Warrior (16.0%) |
| Astrologist | 27.4% ⚠ | 657 | 2400 | Necromancer (43.5%) | Paladin (8.8%) |
| Warrior | 79.1% ⚠ | 1898 | 2400 | Druid (100.0%) | Paladin (53.5%) |

## Mirror Match P0 Winrates
- Battlemage: P0 65.0%
- Necromancer: P0 46.5%
- Paladin: P0 63.5%
- Druid: P0 98.0%
- Rogue: P0 47.5%
- Astrologist: P0 53.5%
- Warrior: P0 68.0%

## Violations
- battlemage: 66.5% outside [45%, 55%]
- necromancer: 35.2% outside [45%, 55%]
- paladin: 76.2% outside [45%, 55%]
- druid: 28.4% outside [45%, 55%]
- rogue: 37.3% (RUSH below 45%% minimum)
- astrologist: 27.4% outside [45%, 55%]
- warrior: 79.1% outside [45%, 55%]


AI: GreedyBot (TASK-AI-TACTICIAN-1 pending)
Thresholds: per-class [45%, 55%], 200 games/pairing

# TASK-TUNE-5050-1 — Class Balance Matrix

Generated: 2026-09-04 03:08:00 UTC
Iteration: 3
Games per matchup: 200
Base seed: 42
Target band: [45%, 55%]
Status: REPORT ONLY
Violations: 3 

### Adjustments applied
- Constellation Starlight damage: 5 → 6 (bound 3-6)

## Winrate Matrix (P0 vs P1)

```
       P0\P1  Battlemage  Necromancer  Paladin   Druid   Rogue  Astrologist  Warrior
  Battlemage  51.0%  28.5%  8.5%!  59.0%  26.5%  45.5%  32.0%
 Necromancer  74.0%  43.5%  46.5%  60.0%  54.0%  51.5%  56.0%
     Paladin  96.0%!  61.0%  59.0%  83.5%!  62.0%  78.0%!  82.5%!
       Druid  87.0%!  77.0%!  6.5%!  95.5%!  81.0%!  78.0%!  79.5%!
       Rogue  76.5%!  49.0%  33.5%  60.5%  47.5%  50.5%  52.0%
 Astrologist  59.5%  33.5%  17.5%!  46.0%  36.0%  50.0%  40.5%
     Warrior  68.0%  41.5%  32.5%  63.0%  44.5%  60.0%  53.0%
```

## Per-Class Winrates (mirrors excluded)

| Class | Winrate | Wins | Games | Best Matchup | Worst Matchup |
|-------|---------|------|-------|--------------|---------------|
| Battlemage | 28.2% ⚠ | 678 | 2400 | Astrologist (43.0%) | Paladin (6.2%) |
| Necromancer | 54.3% | 1303 | 2400 | Battlemage (72.8%) | Druid (41.5%) |
| Paladin | 76.5% ⚠ | 1836 | 2400 | Battlemage (93.8%) | Necromancer (57.2%) |
| Druid | 53.1% | 1274 | 2400 | Astrologist (66.0%) | Paladin (11.5%) |
| Rogue | 51.5% | 1236 | 2400 | Battlemage (75.0%) | Paladin (35.8%) |
| Astrologist | 39.1% ⚠ | 939 | 2400 | Battlemage (57.0%) | Paladin (19.8%) |
| Warrior | 47.2% | 1134 | 2400 | Battlemage (68.0%) | Paladin (25.0%) |

## Mirror Match P0 Winrates
- Battlemage: P0 51.0%
- Necromancer: P0 43.5%
- Paladin: P0 59.0%
- Druid: P0 95.5%
- Rogue: P0 47.5%
- Astrologist: P0 50.0%
- Warrior: P0 53.0%

## Violations
- battlemage: 28.2% outside [45%, 55%]
- paladin: 76.5% outside [45%, 55%]
- astrologist: 39.1% outside [45%, 55%]

### Two Worst Outliers (for Fable)
- Paladin: 76.5% (delta 26.5%)
- Battlemage: 28.2% (delta 21.8%)

AI: GreedyBot (TASK-AI-TACTICIAN-1 pending)
Thresholds: per-class [45%, 55%], 200 games/pairing

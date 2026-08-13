# Backlog Summary

Source: `~/runewake/backlog.json`

## Status Legend
- ✅ done — Completed
- 🔵 open — Ready to start
- 🔴 human-action — Requires human

## P0: Foundation (all done ✅)
| ID | Title |
|---|---|
| P0-01 | Repo init |
| P0-02 | Godot .NET mobile smoke test |
| P0-03 | Core state types |

## P1: Rules Engine (all done ✅)
| ID | Title |
|---|---|
| P1-01 | Card definition model + JSON loader |
| P1-02 | Turn loop |
| P1-03 | Lane placement and combat |
| P1-04 | Keyword handlers |
| P1-05 | Effect executor |
| P1-06 | Trigger bus |
| P1-07 | Barrow, Excavate, Bury, Relic identification |
| P1-08 | Replay determinism |

## P2: AI & Simulation (all done ✅)
| ID | Title |
|---|---|
| P2-01 | Greedy heuristic bot |
| P2-02 | Batch runner |
| P2-03 | Card validator CLI |
| P2-04 | 60 hand-authored cards |
| P2-05 | Rules text renderer |

## P3: Client Duel Scene (all done ✅)
| ID | Title |
|---|---|
| P3-01 | Duel scene layout |
| P3-02 | Card view component |
| P3-03 | Input: drag/tap summon, tap-to-attack |
| P3-04 | Engine binding: GameState lifecycle |
| P3-05 | Animation and feedback layer |
| P3-06 | Bot opponent wired in with think-delay |

## P4: World Map & Campaign (all done ✅)
| ID | Title |
|---|---|
| P4-01 | Map data format + loader |
| P4-02 | Map screen: pan/zoom node graph |
| P4-03 | Encounter definitions |
| P4-04 | Progression save: SQLite |
| P4-05 | Deck builder screen with collection filtering |
| P4-06 | Campaign flow: map → encounter → duel → reward loop |

## P5: Rune System (all done ✅)
| ID | Title |
|---|---|
| P5-01 | Rune definitions + RP budget validation |
| P5-02 | Rune page editor UI |
| P5-03 | Runes injected at match start |
| P5-04 | Dig site interaction |
| P5-05 | Fragment → rune forging |
| P5-06 | Lost Relic minting |

## P6: Content Pipeline (all done ✅)
| ID | Title |
|---|---|
| P6-01 | Card JSON Schema finalized |
| P6-02 | Generate module |
| P6-03 | Validate module |
| P6-04 | Score module |
| P6-05 | Simulate module |
| P6-06 | Dedupe + moderate |
| P6-07 | Art module |
| P6-08 | Review UI |
| P6-09 | Publish + content versioning + client hot-update |
| P6-10 | Pipeline orchestration + 60-card set end to end |
| P6-11 | Stage-schema continuity + report hardening |

## P7: Launch (6 open 🔵)
| ID | Title | Status |
|---|---|---|
| P7-01 | Onboarding tutorial (first 3 duels) | 🔵 open |
| P7-02 | Supabase account + relic ledger sync | 🔵 open |
| P7-03 | Telemetry + settings/accessibility | 🔵 open |
| P7-04 | App Store and Play listings + privacy policy + age rating | 🔵 open |
| P7-05 | TestFlight/closed beta with 50+ players | 🔵 open |
| P7-06 | Crash reporting + launch | 🔵 open |
| — | Device QA: install APK and play through campaign | 🔴 human-action |
| — | Store submission | 🔴 human-action |
# RUNEWAKE: The Buried Age

Design and build specification for an AI-backed fantasy TCG with a node-based world map, an old-League-style rune system, and an archaeology progression layer.

**Start here:** `00_MASTER_SPEC.md`
**Hermes starts here:** `07_AGENT_PROTOCOL.md`, then `06_BUILD_ROADMAP.md` ticket P0-01.

| File | Purpose |
|---|---|
| `00_MASTER_SPEC.md` | Pillars, locked tech stack, core loop, scope |
| `01_GAME_RULES.md` | Complete duel rules, 5 lanes, keywords, combat |
| `02_CARD_DSL.md` | The closed effect grammar the AI generates into |
| `03_RUNE_SYSTEM.md` | Rune pages, RP budget, 30 starter runes |
| `04_WORLD_AND_MAP.md` | Regions, node graph, excavation, Lost Relic engraving, Codex |
| `05_AI_PIPELINE.md` | 10-stage generation → simulation → publish pipeline |
| `06_BUILD_ROADMAP.md` | Phases 0–8 as agent-sized tickets |
| `07_AGENT_PROTOCOL.md` | How the coding agent must work |
| `schema/card.schema.json` | Machine-readable card schema (validated) |
| `schema/example_cards.json` | 6 reference cards — few-shot examples and test fixtures |

## The three decisions everything else depends on

1. The rules engine is a pure deterministic state machine, written once in C#, shared by client, simulator, and future server.
2. Cards are JSON built from a closed effect DSL. The AI fills in a grammar; it never writes rules text or code.
3. AI generation is offline and batched, gated behind schema validation, a power-budget formula, and thousands of headless self-play games before a card ever reaches a player.

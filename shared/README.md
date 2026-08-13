# Runewake Shared — Full Project Context

This folder contains every aspect of Runewake: The Buried Age that has been
worked on and discussed. Claude (the Director) should read this folder for
complete project context before making decisions.

## Quick Links

| File | What it covers |
|---|---|
| [GAME_DESIGN.md](GAME_DESIGN.md) | Game overview, pillars, tech stack, pitch |
| [GAME_RULES.md](GAME_RULES.md) | Duel rules, keywords, combat, win conditions |
| [CARD_DSL.md](CARD_DSL.md) | Closed effect grammar for card authoring |
| [RUNE_SYSTEM.md](RUNE_SYSTEM.md) | Rune pages, budgets, starter runes |
| [WORLD_MAP.md](WORLD_MAP.md) | Regions, zones, wardens, excavation, relics |
| [AI_PIPELINE.md](AI_PIPELINE.md) | Card generation, validation, art pipeline |
| [BACKLOG.md](BACKLOG.md) | Current backlog status (P0-P7) |
| [ARCHITECTURE.md](ARCHITECTURE.md) | System architecture, layers, key files |
| [DEPLOYMENT.md](DEPLOYMENT.md) | Build, export, app store submission |
| [TECH_DEBT.md](TECH_DEBT.md) | Known issues, open questions, edge cases |
| [OPEN_QUESTIONS.md](OPEN_QUESTIONS.md) | Decisions awaiting human input |
| [BRIDGE_SYSTEM.md](bridge/BRIDGE_SYSTEM.md) | Bridge architecture, C-systems, agent setup |
| [AGENT_CONFIG.md](agents/AGENT_CONFIG.md) | All bot profiles, displays, MCP servers |
| [CRASH_LOGGING.md](CRASH_LOGGING.md) | Crash reporting setup (Sentry) |
| [SUPABASE_SCHEMA.md](SUPABASE_SCHEMA.md) | Database schema for relic ledger |
| [STORE_LISTINGS.md](STORE_LISTINGS.md) | App Store / Play Store submission data |

## Source of Truth

The actual project lives at `~/runewake/`. This `shared/` folder is a
comprehensive reference — always verify against the actual code before dispatching
instructions. The official spec documents are at `~/runewake/docs/`.
# 00 — Project Overview

Exported 2026-08-31 from the Cowork orchestration session. This export replaces that chat's memory. Where this file and the repo disagree, the repo wins on engine facts; design intent lives here and in the repo's spec docs.

## The game

**Runewake: The Buried Age** — a single-player-first digital trading card game for iOS + Android (offline-first), with a node-based world map, an archaeology/excavation layer, and a rune meta-progression system.

**Pitch:** *You are a Delver in a world that forgot itself; you duel the wardens of each region using runes you unearth, and every relic you pull out of the ground is permanently engraved with your name.*

**Premise:** Something ended the world once. The survivors buried it deliberately under five geological strata. The seals are failing, the buried things wake as creatures, Delvers bind them to rune-cards and duel the regional Wardens keeping the caches shut. The reveal across regions: the Wardens are the descendants of the buriers — and the burial was probably right. The player's progression *is* the transgression.

**Team:** 1 human (Trikzos, final authority on everything) + AI agents (see Operating model).

## Tech stack

| Layer | Tech | Location |
|---|---|---|
| Rules engine | C# .NET 8 class library (`Runewake.Engine`), zero deps, deterministic state machine, seeded PRNG, replay files | `engine/` |
| Client | Godot 4.3 (.NET/C#) | `client/` |
| Headless simulator | C# console app (`Runewake.Sim`) | `sim/` |
| Content pipeline | Python 3.11 + FastAPI + Pydantic; card gen → IP screen → pixel gate → sim gate | `pipeline/` |
| Backend (future) | Supabase (Postgres/Auth/Storage) — scaffolding exists in code, unconfigured | `supabase_schema.sql` |
| Local save | SQLite (versioned save format + auto-repair since TASK-SAVE-1) | client |
| Art generation | FLUX.2 Pro via OpenRouter (locked decision) | pipeline + `tools/` |
| Tests | ~717 C# engine tests + Python pipeline tests | `engine/`, `pipeline/tests/` |

## Repos and machines

- **GitHub (single source of truth):** `github.com/pocketaces375-creator/runewake`, branch `main`. Everything flows through it.
- **The mini PC ("Hermes box"):** Trikzos' always-on machine. Runs the production agents (TcgBot/Jett on deepseek-v4-flash via a "foreman" that polls the repo), Hermes MCP servers (:9100 Jett profile, :9101 tcgbot profile), Claude desktop app (headless on Xvfb :99, x11vnc on localhost:5909, Tailscale for remote). Local repo clone with push rights. Launcher: `~/.hermes/scripts/claude-desktop-launch.sh`.
- **Cloud Claude sessions (Fable/orchestrator):** can FETCH the repo read-only; pushes are blocked by the sandbox git proxy unless the repo is attached at session creation. Working delivery channel to the box: paste text to TcgBot in the Runewake Telegram group; it does the git work.
- **Telegram:** the "Runewake" group (`telegram:-5481648844`) is the delivery/review channel — TcgBot's bot has access; Jett's bot does NOT (Jett must hand deliverables to TcgBot; DM posts count as not delivered).

## Current phase (as of 2026-08-31)

Engine, lane combat, artifacts (weapons) system, 7-class data model, 9 unique encounter decks, Region 1 map, deck builder, saves, title + intro, audio system + hookup, and APK shipping via GitHub Releases all exist. 65 of ~440 launch cards have final art. The duel screen visual rebuild (Root-Bound card border + name auto-fit) is mid-flight with an active rendering regression (see 06-backlog and 08-open-questions). Balance sim gate exists but FAILED with a structural first-player advantage that must be fixed before any class tuning (see 04-decisions-log).

**Build order (agreed):** duel screen → weapons/artifacts polish → save hardening + soak → tutorial → sim gates & balance → backend + accounts → drops/economy/collection → region generator → 375-card production → the Tower; audio & polish woven through.

## Key repo documents (canonical specs — read these before changing anything)

`docs/00_MASTER_SPEC.md`, `docs/01_GAME_RULES.md` (duel rules), `docs/02_CARD_DSL.md`, `docs/03_RUNE_SYSTEM.md`, `docs/04_WORLD_AND_MAP.md`, `FIELD_EFFECT_SPEC.md` + `ARTIFACT_CLASSES.md` + `ARTIFACT_RULINGS.md` (artifact system), `DECK_SPEC.md`, `docs/ART_STYLE_SPEC.md` (v3.0 style lock), `docs/ART_WAVES.md`, `NOTES_FOR_HERMES.md` (agent protocol), `TASKS_QUEUE.md` (live work queue), `HERMES_STATUS.md` (production agent log), `PROJECT_EXPORT.md` (2026-08-12 deep export — older but rich), `LAUNCH_ROADMAP.md`, `docs/TECH_DEBT.md`.

## Operating model (the machine that builds the game)

- **Fable (Claude) = brains.** Direction, specs, review, balance numbers. Never bulk production. Writes `TASKS_QUEUE.md`.
- **TcgBot / deepseek-v4-flash = production.** A foreman on the mini PC polls the repo (~hourly heartbeats), takes the TOP unchecked `- [ ]` task in `TASKS_QUEUE.md`, builds, tests, commits `TASK-<ID>: ...`, pushes, logs a DONE entry in `HERMES_STATUS.md`, takes the next.
- **The queue IS the command channel.** New tasks are inserted immediately after the line `# New tasks MUST be added ABOVE any '## ' subheader in this section...` — one `- [ ] TASK-<ID> — ...` line at column 0, indented continuations, explicit `Acceptance:` clause. NOTE: pasting tasks one-per-message reverses their order (each inserts at top); paste as one block or state the intended final order.
- **Verification is Fable's job and is non-negotiable:** production agents have three times reported DONE with green gates on visually broken or fake results (identical wide/standard captures; border covering all card art). Every visual DONE gets its committed capture opened and judged as an image before acceptance.
- **Scheduled tasks on Trikzos' Claude account:** "Runewake hourly % to alpha" (:24), "Runewake 30-min offset % to alpha" (:54) — read-only status pings; "Fable director check-in (Runewake)" (:09, runs claude-fable-5) — reviews completed work against actual captures, restocks the queue to 5–6 open tasks, pushes or emits paste-ready chunks when the proxy blocks.

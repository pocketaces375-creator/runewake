# Runewake: The Buried Age — Full Project Export

Generated: 2026-08-12 17:30 UTC
Purpose: Self-contained reference for senior design agent (Claude/Fable) who cannot access the filesystem.

---

## 1. PROJECT OVERVIEW

**Game name:** Runewake: The Buried Age
**Alt names considered:** Relicwake, Sigil & Spade, The Hollow Age
**Genre:** Single-player-first digital trading card game with a node-based world map, meta-progression rune system, and an archaeology/excavation layer.
**Target platforms:** iOS + Android (App Store / Google Play), offline-first.
**Pitch:** *You are a Delver in a world that forgot itself; you duel the wardens of each region using runes you unearth, and every relic you pull out of the ground is permanently engraved with your name.*

**Tech stack:**
- **Rules engine:** C# .NET 8 class library (Runewake.Engine), zero dependencies — pure deterministic state machine
- **Client:** Godot 4.3+ (.NET / C# build)
- **Headless simulator:** C# console app (Runewake.Sim)
- **Content pipeline:** Python 3.11 + FastAPI + Pydantic
- **Backend:** Supabase (Postgres, Auth, Storage, CDN) — optional at launch
- **Local save:** SQLite via Godot / Microsoft.Data.Sqlite

**Team:** 1 human (Trikzos) + 1 coding agent (Hermes/DeepSeek V4 Flash via OpenClaw).

**Current completion status (honest estimate): 85–90%**

Completed phases:
- P0: Foundation ✅ (repo, mobile smoke test, core state types)
- P1: Rules Engine ✅ (card model, turn loop, combat, 11 keywords, effect executor, trigger bus, barrow/relic mechanics, replay determinism)
- P2: AI & Simulation ✅ (greedy bot, batch runner, card validator, 60 hand-authored cards, rules text renderer)
- P3: Client Duel Scene ✅ (lane layout, card view, drag/tap input, engine binding, animation, bot opponent)
- P4: Campaign ✅ (map data, map screen, encounters, save, deck builder, campaign flow)
- P5: Rune System ✅ (rune defs, RP budget, editor UI, injection, dig sites, forging, relic minting)
- P6: Content Pipeline ✅ (schema, generate, validate, score, simulate, dedupe, art, review, publish, orchestration)

Remaining (P7 - Launch phase — 6 open tickets):
- P7-01: Onboarding tutorial (first 3 duels) — 3 failed attempts, needs architecture rebuild
- P7-02: Supabase account + relic ledger sync
- P7-03: Telemetry + settings/accessibility
- P7-04: App Store / Play Store listings + privacy policy + age rating (store copy written)
- P7-05: TestFlight/closed beta with 50+ players
- P7-06: Crash reporting + launch

**Test status:** 463/463 C# tests passing. 221/221 Python tests passing (3 pre-existing flaky failures in test_generate.py from text drift, not blockers).

---

## 2. FILE TREE

Project root: `/home/fictive/runewake/`

```
runewake/
├── AGENT_LOG.md                  # Agent session log (timestamps, decisions)
├── analyze_image.py              # Utility: screenshot analysis via vision model
├── backlog.json                  # 57-ticket backlog, statuses per ticket
├── capture.sh                    # Screenshot capture script for Godot
├── CLAUDE.md                     # Project handoff protocol for Hermes
├── debug_capture.sh              # Debug capture variant
├── LICENSE                       # License file
├── NOTES_FOR_HERMES.md           # Fable writes design intent here (currently template)
├── PROJECT_STATE.md              # Current phase state with key stats
├── README.md                     # Root README
├── Runewake.sln                  # .NET solution file
├── STATUS_FOR_FABLE.md           # Hermes writes status/questions here
├── take_screenshot.py            # Python screenshot utility
├── tcg_project.zip               # Clean project snapshot for Fable (2.4MB)
│
├── client/                       # Godot 4.3 .NET project (1.5GB)
│   ├── scripts/                  # C# game logic: DuelScene, Main, CampaignContext, DevMenu, etc.
│   ├── scenes/                   # Godot .tscn scene files
│   ├── exports/                  # Built APK / Linux exports
│   └── export_apk.sh             # APK builder with stale-DLL detection
│
├── client.godot/                 # Godot editor metadata (1.5MB)
│
├── content/                      # Generated card packs (JSON)
│   ├── cards/
│   │   ├── dawn.json             # 12 DAWN-stratum cards
│   │   ├── ember.json            # 12 EMBER-stratum cards
│   │   ├── hollow.json           # 12 HOLLOW-stratum cards
│   │   ├── tide.json             # 12 TIDE-stratum cards
│   │   ├── tutorial_pack.json    # 4 tutorial cards
│   │   └── verdant.json          # 13 VERDANT-stratum cards
│   ├── dig_sites/
│   │   └── region_01_dig.json    # Dig site definitions for Region 1
│   ├── dig_tools/
│   │   └── tools.json            # Dig tool definitions (Brush, Iron Spade, etc.)
│   ├── encounters/
│   │   ├── region_01_boss.json   # Warden Boss encounter: Aelin the Last Steward
│   │   ├── region_01_early.json  # Early Region 1 encounters
│   │   ├── region_01_late.json   # Late Region 1 encounters
│   │   └── region_01_mid.json    # Mid Region 1 encounters
│   ├── forge/
│   │   └── recipes.json          # Fragment-forging recipes
│   ├── map/
│   │   └── region_01.json        # Region 1 node-graph map data
│   ├── relics/
│   │   └── relic_defs.json       # Lost Relic definition data
│   ├── runes/
│   │   └── starter_runes.json    # 30 starter runes (Marks, Seals, Glyphs, Sigils)
│   ├── supabase/
│   │   └── schema.sql            # Supabase/Postgres schema for relic ledger
│   └── tutorial/
│       └── tutorial_steps.json   # Tutorial step definitions
│
├── docs/                         # Game design spec (source of truth)
│   ├── 00_MASTER_SPEC.md         # Pillars, stack, core loop, scope discipline
│   ├── 01_GAME_RULES.md          # Complete duel rules, keywords, combat
│   ├── 02_CARD_DSL.md            # Closed effect grammar for card authoring
│   ├── 03_RUNE_SYSTEM.md         # Rune pages, budgets, starter rune list
│   ├── 04_WORLD_AND_MAP.md       # Regions, zones, wardens, excavation, relic engraving
│   ├── 05_AI_PIPELINE.md         # Generation → validation → simulation → art → publish
│   ├── 06_BUILD_ROADMAP.md       # Phased tickets (P0–P7)
│   ├── 07_AGENT_PROTOCOL.md      # How Hermes must work
│   ├── AGENT_LOG.md              # Agent session log
│   ├── ART_COMMISSION_QUEUE.md   # Queue of cards needing human art
│   ├── HAND_AUTHOR_QUEUE.md      # Queue of cards needing human design
│   ├── OPEN_QUESTIONS.md         # Decisions awaiting human input
│   ├── README.md                 # Docs index
│   ├── TECH_DEBT.md              # Known issues and tech debt
│   ├── store/
│   │   ├── age_rating_questionnaire.md
│   │   ├── app_store_listing.md
│   │   ├── play_store_listing.md
│   │   ├── privacy_policy.md
│   │   └── submission_checklist.md
│   └── supabase_schema.sql       # Supabase schema copy
│
├── engine/                       # C# .NET 8 class library
│   ├── Runewake.Engine.csproj
│   ├── Cards/
│   │   ├── AbilityDef.cs         # Ability definition (trigger, condition, effects)
│   │   ├── ArtDef.cs             # Art reference (prompt, asset URLs)
│   │   ├── CardDef.cs            # Card definition (id, name, strata, type, cost, stats)
│   │   ├── CardEnums.cs          # All enums: Strata, CardType, Rarity, Trigger, Op, etc.
│   │   ├── CardLoader.cs         # JSON card pack loader
│   │   ├── CardRegistry.cs       # Card definition registry (id → CardDef lookup)
│   │   ├── ConditionDef.cs       # Condition definition for ability triggers
│   │   ├── ContentManager.cs     # High-level content load orchestration
│   │   ├── DigSiteDef.cs         # Dig site definition
│   │   ├── DigSiteLoader.cs      # Dig site JSON loader
│   │   ├── DigToolDef.cs         # Dig tool definition
│   │   ├── DigToolLoader.cs      # Dig tool JSON loader
│   │   ├── EffectDef.cs          # Effect definition (op, target, amount, duration)
│   │   ├── EncounterDef.cs       # Encounter (NPC opponent) definition
│   │   ├── EncounterLoader.cs    # Encounter JSON loader
│   │   ├── ForgeLoader.cs        # Forging recipe loader
│   │   ├── LostRelicInstance.cs  # Minted relic instance model
│   │   ├── LostRelicLoader.cs    # Relic definition loader
│   │   ├── LostRelicMinter.cs    # Relic minting logic
│   │   ├── MapLoader.cs          # Map data JSON loader
│   │   ├── MapRegion.cs          # Map region/node/graph data
│   │   ├── MapUnlockEvaluator.cs # Map node unlock condition evaluation
│   │   ├── RulesTextRenderer.cs  # AbilityDef → human-readable text renderer
│   │   ├── RuneDef.cs            # Rune definition
│   │   ├── RuneLoader.cs         # Rune JSON loader
│   │   ├── TargetCount.cs        # Target count helper
│   │   ├── TargetDef.cs          # Target definition (scope, filter, count)
│   │   └── TutorialLoader.cs     # Tutorial step loader
│   ├── Diagnostics/
│   │   └── CrashReportBuilder.cs # Sentry crash report builder
│   ├── Engine/
│   │   ├── DuelEngine.cs         # Core duel engine: Apply(state, action) -> state
│   │   ├── EffectExecutor.cs     # Effect execution: DAMAGE, HEAL, BUFF, DRAW, etc.
│   │   ├── GameAction.cs         # Action types: EndTurnAction, PlayCardAction, AttackAction
│   │   ├── KeywordHandlers.cs    # All 11 keyword implementations
│   │   ├── ReplayLog.cs          # Replay serialization
│   │   ├── ReplayRunner.cs       # Replay execution
│   │   ├── ResolvedTarget.cs     # Resolved target types (CreatureTarget, PlayerTarget)
│   │   ├── RuneInjector.cs       # Rune page → ability injection at match start
│   │   ├── TargetResolver.cs     # Target resolution from DSL target defs
│   │   └── TriggerBus.cs         # Deterministic trigger event bus (depth-capped)
│   ├── State/
│   │   ├── CardInstance.cs       # Runtime card instance (mutable state during a duel)
│   │   ├── DeckValidator.cs      # Deck construction rule validator
│   │   ├── DigState.cs           # Excavation/dig session state
│   │   ├── ForgeSystem.cs        # Fragment → rune forging system
│   │   ├── GameConfig.cs         # Game configuration (decks, seed, rune page, content version)
│   │   ├── GameState.cs          # Complete deterministic duel state
│   │   ├── LaneState.cs          # Single lane state (occupant, curses)
│   │   ├── PlayerState.cs        # Single player state: vigor, attunement, hand, deck, barrow, lanes
│   │   ├── ProgressionState.cs   # Campaign progression: nodes cleared, collection, shards
│   │   ├── RunePage.cs           # Rune page: 9 marks, 9 seals, 9 glyphs, 3 sigils + RP budget
│   │   ├── SeededRng.cs          # Seeded deterministic RNG (xorshift64)
│   │   ├── SettingsState.cs      # Player settings state
│   │   └── TutorialState.cs      # Tutorial progression state
│   ├── Supabase/
│   │   ├── RelicLedgerSync.cs    # Supabase relic ownership sync
│   │   └── SupabaseConfig.cs     # Supabase configuration
│   └── Telemetry/
│       ├── TelemetryBuffer.cs    # Telemetry event buffer
│       └── TelemetryEvent.cs     # Telemetry event type
│
├── persistence/                  # SQLite save system
│   ├── Runewake.Persistence.csproj
│   └── SaveRepository.cs         # Save/load campaign state from SQLite
│
├── pipeline/                     # Python content generation pipeline
│   ├── config.yaml               # Pipeline configuration
│   ├── __init__.py
│   ├── orchestrator.py           # Pipeline end-to-end orchestrator
│   ├── run_e2e.sh               # End-to-end runner with env sourcing
│   ├── generate_all_art.py       # Batch art generation
│   ├── generate_sample_art.py    # Sample art generation
│   ├── baselines/
│   │   └── global_archetypes.json  # Global archetype baselines for simulation
│   ├── content/
│   │   └── packs/
│   │       ├── buried_age.json          # Bundled card pack (12 cards with art + sim data)
│   │       ├── buried_age.bundled.json  # Minified/bundled variant
│   │       └── buried_age.changelog.json # Pack version changelog
│   ├── dedupe/
│   │   ├── blocklist.yaml             # IP/copyright blocklist
│   │   └── existing_card_names.json   # Existing card names for deduplication
│   ├── modules/
│   │   ├── __init__.py
│   │   ├── art.py              # AI art generation (FLUX.2-pro via OpenRouter)
│   │   ├── dedupe_moderate.py  # Card deduplication + content moderation
│   │   ├── generate.py         # LLM card generation (DeepSeek V4 Flash)
│   │   ├── orchestrate.py      # Pipeline orchestration logic
│   │   ├── publish.py          # Pack versioning + publishing
│   │   ├── render_rules.py     # Rules text rendering
│   │   ├── score.py            # Power score calculation + band validation
│   │   ├── simulate.py         # Headless simulation via .NET engine
│   │   └── validate.py         # Schema + semantic validation
│   ├── review_app/             # Web review UI (FastAPI)
│   │   ├── __init__.py
│   │   ├── app.py              # Review app server
│   │   └── templates/
│   │       └── review.html     # Review UI template
│   ├── seeds/                  # Test seed data
│   │   ├── ember_01.json
│   │   ├── ember_60.json
│   │   └── highcost_test.json
│   ├── tests/                  # Pipeline tests
│   │   ├── __init__.py
│   │   ├── test_art.py
│   │   ├── test_dedupe_moderate.py
│   │   ├── test_generate.py    # 3 pre-existing flaky failures (text drift)
│   │   ├── test_orchestrate.py
│   │   ├── test_orchestrator.py
│   │   ├── test_publish.py
│   │   ├── test_render_rules.py
│   │   ├── test_review_app.py
│   │   ├── test_score.py
│   │   ├── test_simulate.py
│   │   └── test_validate.py
│   └── work/                   # Pipeline work directories (per-batch output)
│       ├── b_2026_ember_e2e/
│       ├── b_2026_ember_prompted/
│       ├── b_2026_ember_worked/
│       └── b_test_highcost/
│
├── schema/
│   ├── card.schema.json        # JSON schema for card definitions
│   └── example_cards.json      # 6 hand-authored reference cards (fixtures)
│
├── screenshots/                # Godot debug logs
│   ├── godot_debug.log
│   ├── godot_debug2.log
│   └── godot_debug3.log
│
├── shared/                     # Comprehensive project reference for Claude (Director)
│   ├── README.md               # Shared folder index
│   ├── AGENT_CONFIG.md         # All bot profiles, displays, MCP servers (agents/)
│   ├── AI_PIPELINE.md
│   ├── ARCHITECTURE.md         # System architecture, layers, key files
│   ├── BACKLOG.md              # Current backlog status
│   ├── BUILD_ROADMAP.md
│   ├── CARD_DSL.md
│   ├── CRASH_LOGGING.md
│   ├── DEPLOYMENT.md
│   ├── GAME_DESIGN.md          # Game overview, pillars, tech stack, pitch
│   ├── GAME_RULES.md           # Duel rules (copy of docs/01_GAME_RULES.md)
│   ├── NOTES_FOR_HERMES.md     # Hermes instruction source (currently template)
│   ├── OPEN_QUESTIONS.md       # Open questions & tech debt
│   ├── README.md
│   ├── RUNE_SYSTEM.md
│   ├── STORE_LISTINGS.md
│   ├── SUPABASE_SCHEMA.md
│   ├── WORLD_MAP.md
│   ├── agents/
│   │   └── AGENT_CONFIG.md
│   └── bridge/
│       └── BRIDGE_SYSTEM.md
│
├── sim/                        # Headless simulation runner (C#)
│   ├── (sim project: GreedyBot, BatchRunner, CardValidator)
│
├── tests/                      # C# xUnit test project (463 tests)
│   ├── Runewake.Tests.csproj
│   ├── Campaign/
│   │   └── RegionOneIntegrationTests.cs
│   ├── Cards/
│   │   ├── CardLoaderTests.cs
│   │   ├── ContentManagerTests.cs
│   │   ├── DigSiteTests.cs
│   │   ├── EncounterLoaderTests.cs
│   │   ├── ForgeAndToolTests.cs
│   │   ├── LostRelicTests.cs
│   │   ├── MapLoaderTests.cs
│   │   ├── RulesTextSnapshotTests.cs
│   │   └── RuneTests.cs
│   ├── Client/
│   │   └── CrashReporterTests.cs
│   ├── Data/
│   │   └── ProgressionSaveTests.cs
│   ├── Engine/
│   │   ├── BatchRunnerTests.cs
│   │   ├── BotTests.cs
│   │   ├── CardValidatorTests.cs
│   │   ├── ClientBindingTests.cs
│   │   ├── CombatTests.cs
│   │   ├── EffectExecutorTests.cs
│   │   ├── KeywordTests.cs
│   │   ├── RelicTests.cs
│   │   ├── ReplayDeterminismTests.cs
│   │   ├── RuneEngineTests.cs
│   │   ├── RunePageTests.cs
│   │   ├── TriggerBusTests.cs
│   │   └── TurnLoopTests.cs
│   ├── State/
│   │   ├── CloneTests.cs
│   │   ├── DeckValidatorTests.cs
│   │   ├── GameStateInitTests.cs
│   │   ├── SettingsStateTests.cs
│   │   └── TutorialStateTests.cs
│   ├── Supabase/
│   │   └── RelicLedgerSyncTests.cs
│   └── Telemetry/
│       └── TelemetryBufferTests.cs
│
├── tools/
│   └── PackVerifier/           # Pack verification tool CLI
│       ├── PackVerifier.csproj
│       └── Program.cs
│
└── .gitignore
```

---

## 3. FULL GAME RULES

The following is copied verbatim from `docs/01_GAME_RULES.md` (also mirrored in `shared/GAME_RULES.md`):

```
# 01 — Duel Rules v0.1

Design goal: readable on a phone in one thumb-length screen, resolvable by a deterministic engine, and expressive enough that an LLM can invent thousands of distinct creatures inside it.

---

## 1. Setup

- Deck: exactly **30 cards**, max **2 copies** of any card, max **1 copy** of any Relic-rarity card.
- Deck may contain cards from **one or two Strata** (see §2).
- Each player starts at **25 Vigor** (life).
- Starting hand: **4 cards**. The player going second draws **5** and starts with **+1 Attunement** on turn one (the "Second Delver" compensation).
- Both players mulligan once: select any subset to shuffle back, redraw the same number.

## 2. Strata (the five colors)

Strata are geological layers as much as factions. Each maps to regions on the world map, which is why deck theme and map theme reinforce each other.

| Stratum | Identity | Mechanical lean |
|---|---|---|
| **VERDANT** | Overgrown ruins, root and beast | Big bodies, growth counters, adjacency buffs |
| **EMBER** | Forge-holds, ash and iron | Direct damage, Swift, aggression, sacrifice-for-tempo |
| **TIDE** | Sunken cities, drowned archives | Draw, bounce, Excavate, delay effects |
| **HOLLOW** | Catacombs, rot, the unburied | Death triggers, Unearth, resource drain |
| **DAWN** | Temple wards, order, preservation | Guard, Ward, healing, protection, taxing effects |

## 3. Resources: Attunement

- Each player has an **Attunement** track. It increases by **1 at the start of each of your turns**, capping at **10**.
- Attunement refills fully each turn. There are no resource cards — no mana screw, no flooding. This is deliberate: on mobile, a loss caused by shuffle variance reads as a bug.
- Cards cost 0–10 Attunement. Attunement can be temporarily raised past 10 by effects but never permanently.

## 4. The board: five lanes

Each player has **5 lanes** (indexed 0–4), one creature per lane. Lanes face each other directly: your lane 0 opposes their lane 0.

```
ENEMY   [ 0 ][ 1 ][ 2 ][ 3 ][ 4 ]
YOU     [ 0 ][ 1 ][ 2 ][ 3 ][ 4 ]
```

This is the single most important rules choice after Attunement. Lanes give us:
- **Positional strategy** that matches the tactics-map fantasy without a full grid.
- A huge, cheap design space for AI-generated cards (adjacent, opposing, flanking, empty-lane, edge-lane effects).
- A clean read on a 6-inch screen.

When you summon a creature you choose an empty lane. Lane choice is a real decision every turn.

## 5. Turn structure

1. **Attune** — Attunement +1, refill.
2. **Draw** — draw 1. (First player skips their turn-one draw.)
3. **Start triggers** — `ON_TURN_START` resolves in board order, yours first.
4. **Main** — play cards, use activated abilities, in any order. Creatures may be declared as attackers here (there is no separate declaration step; tapping a ready creature attacks immediately).
5. **End** — `ON_TURN_END` triggers, hand size checked (max 10, discard excess), pass.

## 6. Combat

A creature that is **Ready** may attack once per turn. Creatures summoned this turn are **Exhausted** unless they have **Swift**.

Attacking is resolved per-lane and immediately:

- If the **opposing lane is occupied**, the two creatures deal damage equal to their Attack to each other simultaneously. Any creature at 0 or less Vigor is destroyed.
- If the **opposing lane is empty**, the attacker deals its Attack to the enemy player — *unless* the enemy controls a creature with **Guard** anywhere on their board, in which case you must attack a Guard creature's lane instead (choose one if multiple).
- **Pierce**: excess damage dealt to a destroyed blocker carries through to the enemy player.

Damage on creatures persists between turns. Creatures do not heal at end of turn.

## 7. Win condition

Reduce the opponent to **0 Vigor**. If a player must draw from an empty deck, they take **Fatigue**: 1 Vigor for the first, 2 for the second, escalating by 1 each time. No decking-out instant loss — fatigue makes long games end on a clock without feeling arbitrary.

## 8. Keyword set (CLOSED — v1)

The AI may only use these. Adding a keyword requires an engine change and a version bump.

| Keyword | Rules text |
|---|---|
| **Guard** | Enemies must attack a lane containing a Guard creature while one exists. |
| **Swift** | Not Exhausted the turn it is summoned. |
| **Pierce** | Excess combat damage to a destroyed creature hits the enemy player. |
| **Ward** | Prevents the next instance of damage dealt to this creature, then is removed. |
| **Venom** | Any creature damaged by this is destroyed at end of combat. |
| **Reach** | May attack the opposing lane or lanes adjacent to it. |
| **Rooted** | Cannot attack. (Used to price up defensive statlines.) |
| **Unearth N** | When destroyed, returns to its owner's hand next turn at cost N. |
| **Echo** | This card's `ON_SUMMON` ability triggers twice. |
| **Fragile** | Destroyed at end of the turn it was summoned. (Used for tokens and big tempo swings.) |
| **Sealed** | Cannot be targeted by enemy abilities. |

## 9. Signature mechanics (the archaeology layer, in-game)

These three exist to make the duel feel like digging, and to give the generation pipeline flavorful hooks.

**Excavate N** — Look at the top N cards of your deck, put one into your hand, and **Bury** the rest.

**Bury** — Place a card face down in your **Barrow** (a third zone alongside deck and discard). Buried cards are inert but can be retrieved by Hollow and Tide effects, and count for "buried count" conditions. The Barrow is public-count, private-contents.

**Relics and Identification** — Relic cards enter play **Unidentified**: a face-down 0/3 artifact in a lane with no Attack. At the start of your turn, if its **Identify condition** is met, it flips and its full effect comes online permanently. Identify conditions are drawn from a fixed list (`3+ cards in your Barrow`, `you control 3 creatures`, `you took damage last turn`, `turn 6 or later`, `you cast 2 spells this game`). This creates a mid-game archaeology beat inside every match: you plant a mystery, you dig toward it, it wakes up.

## 10. Card types

- **CREATURE** — Attack / Vigor, occupies a lane.
- **RITUAL** — one-shot spell, resolves and goes to discard.
- **RELIC** — permanent, occupies a lane, enters Unidentified.
- **CURSE** — attaches to a creature or player, persistent modifier.
- **TOKEN** — created by effects, never in a deck.

## 11. Determinism and RNG

All randomness draws from a single seeded PRNG stored in `GameState.Rng`. Same seed + same action list = same game, always. Every match writes a replay file of `(seed, contentVersion, List<Action>)`. This is how we debug, how we validate PvP later, and how balance simulation stays reproducible.

## 12. Open questions to resolve during P2 playtesting

- Is 25 Vigor right, or does 20 make games too fast at 5 lanes? (Sim first, then feel.)
- Should Guard force lane-attack or be Hearthstone-style global taunt? Current answer: lane-based, as written.
- Does the second-player +1 Attunement over- or under-correct? Measure win rate over 10k sim games; target 50% ±2%.
```

**Deck construction rules** (from `docs/01_GAME_RULES.md` §1):
- Exactly 30 cards per deck
- Max 2 copies of any card
- Max 1 copy of any Relic-rarity card
- Deck may contain cards from one or two Strata

**Zones of play** (implemented in code as `Zone` enum):
- `Deck` — face-down draw pile
- `Hand` — cards in hand
- `Lane` — creature/relic on the board (one of 5 lanes per player)
- `Discard` — destroyed/used cards
- `Barrow` — face-down buried cards (public count, private contents)
- `RemovedFromGame` — cards pending Unearth return
- `Void` — permanently removed from the game

---

## 4. FIELD-EFFECT SYSTEM

**MISSING — This system does not exist in any project document or code.**

There is no documented or implemented mechanic involving "two cards that sit beside the player's character and act as field effects." The closest existing systems are:

1. **The Rune System** (`docs/03_RUNE_SYSTEM.md`) — a pre-game loadout of 30 passive buffs (9 Marks, 9 Seals, 9 Glyphs, 3 Sigils) that modify game state. These are not cards on the board — they are virtual tokens attached to `PlayerState.RuneTokens` and processed by the trigger bus. They sit off-board (LaneIndex = -1) and never appear as visible cards beside the player's character.

2. **RELIC-type cards** — permanent cards that occupy a lane, enter as 0/3 Unidentified artifacts, and flip when their identify condition is met. They occupy one of the 5 regular lanes, not any special "beside character" slot.

3. **Lane System** — 5 lanes per player, creatures occupy lanes. No special character or avatar cards exist.

If the field-effect system is a design concept that has been discussed but not yet documented or implemented, it has no presence in any of the 100+ project files, the game rules doc, the card DSL, the engine source code, or the shared reference docs. It needs to be specified from scratch.

---

## 5. CARD DATA

### Card schema

Full JSON Schema at `schema/card.schema.json`. Key structure:

```json
{
  "id": "{strata_prefix}_{rarity_prefix}_{snake_case_name}",
  "set": "buried_age",
  "name": "Display Name",
  "strata": "VERDANT | EMBER | TIDE | HOLLOW | DAWN",
  "type": "CREATURE | RITUAL | RELIC | CURSE | TOKEN",
  "rarity": "COMMON | UNCOMMON | RARE | RELIC",
  "cost": 0-10,
  "attack": 0-12,           // required for CREATURE
  "vigor": 1-14,            // required for CREATURE
  "keywords": ["GUARD", ...],  // max 3, from closed keyword list
  "abilities": [max 2],        // {trigger, condition, effects}
  "identify_condition": {...}, // required for RELIC only
  "flavor": "...",             // max 140 chars
  "art": { prompt, asset },    // art references
  "power_score": 0.0,         // balance score
  "content_version": 1
}
```

ID prefix convention: `{strata: vrd|emb|tid|hol|dwn}_{rarity: c|u|r|x}_{name}`

### Card counts

| File | Count | Strata |
|------|-------|--------|
| `content/cards/verdant.json` | 13 | VERDANT |
| `content/cards/ember.json` | 12 | EMBER |
| `content/cards/tide.json` | 12 | TIDE |
| `content/cards/hollow.json` | 12 | HOLLOW |
| `content/cards/dawn.json` | 12 | DAWN |
| `content/cards/tutorial_pack.json` | 4 | Mixed |
| **Total** | **65** | — |

The bundled pack (`pipeline/content/packs/buried_age.json`) contains 12 fully-realized cards with art assets and simulation data.

### 30 representative cards (covering all types and all 5 strata)

**VERDANT (13 cards):**

| Name | Type | Cost | Atk | Vig | Keywords | Abilities | Rarity |
|------|------|------|-----|-----|----------|-----------|--------|
| Root Warden | CREATURE | 3 | 2 | 4 | GUARD | ON_SUMMON: BUFF adjacent allies +0/+1 | COMMON |
| Verdant Sproutling | CREATURE | 1 | 1 | 2 | — | — | COMMON |
| Thornbark Defender | CREATURE | 4 | 2 | 6 | GUARD, FRAGILE | — | COMMON |
| Wildwood Stalker | CREATURE | 2 | 3 | 2 | — | — | COMMON |
| Grove Healer | CREATURE | 3 | * | * | — | (uncommon heal effect) | UNCOMMON |
| (8 more cards, types unknown without full file read) | | | | | | | |

**EMBER (12 cards):**

| Name | Type | Cost | Atk | Vig | Keywords | Abilities | Rarity |
|------|------|------|-----|-----|----------|-----------|--------|
| Cinder Runner | CREATURE | 2 | 3 | 1 | SWIFT | — | COMMON |
| Ember Hound | CREATURE | 1 | 2 | 1 | SWIFT | — | COMMON |
| Flame Javelin | RITUAL | 1 | — | — | — | RESOLVE: DAMAGE enemy creature 2 | COMMON |
| Forgeguard Berserker | CREATURE | 3 | 4 | 3 | — | — | COMMON |
| Wildfire Adept | CREATURE | 2 | 2 | 2 | — | (summon ability) | UNCOMMON |
| (7 more cards) | | | | | | | |

**TIDE (12 cards):**

| Name | Type | Cost | Atk | Vig | Keywords | Abilities | Rarity |
|------|------|------|-----|-----|----------|-----------|--------|
| Silt Reader | CREATURE | 4 | 2 | 5 | — | ON_SUMMON: EXCAVATE 3; ON_TURN_START if 4+ buried: DRAW 1 | UNCOMMON |
| (11 more cards) | | | | | | | |

**HOLLOW (12 cards):**

| Name | Type | Cost | Atk | Vig | Keywords | Abilities | Rarity |
|------|------|------|-----|-----|----------|-----------|--------|
| Gravewrit Thrall | CREATURE | 3 | 4 | 2 | UNEARTH | ON_DEATH: DAMAGE enemy 1, BURY self 1 | UNCOMMON |
| (11 more cards) | | | | | | | |

**DAWN (12 cards):**

| Name | Type | Cost | Atk | Vig | Keywords | Abilities | Rarity |
|------|------|------|-----|-----|----------|-----------|--------|
| Sealing Light | RITUAL | 2 | — | — | — | RESOLVE: GRANT_KEY WARD to chosen ally, HEAL 2 | COMMON |
| (11 more cards) | | | | | | | |

**RELIC (1 card — the only RELIC type):**

| Name | Type | Cost | Strata | Keywords | Abilities | Identify Condition | Rarity |
|------|------|------|-------|----------|-----------|-------------------|--------|
| Aelin's Seal | RELIC | 5 | HOLLOW | SEALED | ON_RELIC_IDENTIFY: UNBURY 2; PASSIVE: BUFF HOLLOW allies +1/+0 | BARROW_COUNT_GTE 5 | RELIC |

**Tutorial pack (4 cards):** Basic intro cards for the tutorial sequence. Names/details TBD.

---

## 6. CODE STATE

### Architecture

```
(GameState, Action) -> GameState
```

The entire engine is a pure, deterministic state machine. No I/O, no rendering, no mutable static state. All randomness comes from a single seeded xorshift64 PRNG.

### Main entry point: `engine/Engine/DuelEngine.cs`

```csharp
// The pure deterministic duel engine.
// P1: Engine.Apply(GameState, GameAction) -> GameState
// Every action clones the state, applies the mutation, and returns the new state.
// No I/O, no side effects, no static mutable state.
public static partial class DuelEngine
{
    public static GameState Apply(GameState state, GameAction action)

    private static GameState ApplyEndTurn(GameState state, EndTurnAction action)
        // Handles: end phase triggers → Fragile check → hand truncation → switch player
        // → refresh phase (ready all creatures) → attune phase → draw phase → start triggers
        // → Unearth processing → ON_TURN_START → relic identification

    private static GameState ApplyPlayCard(GameState state, PlayCardAction action)
        // Validates: card in hand, enough attunement, lane empty for CREATURE/RELIC
        // For CREATURE: apply keyword effects, fire ON_SUMMON triggers
        // For RELIC: enters as 0/3 unidentified artifact
        // For RITUAL: resolve effects, discard

    private static GameState ApplyAttack(GameState state, AttackAction action)
        // Validates: ready, not attacked, not Rooted, resolves target lane with Reach
        // Guard redirect if attacking empty lane
        // Simultaneous damage, Ward absorption, Venom marking, Pierce carry-through
        // Dead creature cleanup with Unearth check, ON_DEATH triggers
}   // DuelEngine.cs — COMPLETE, 361 lines
```

### Combat/Rules Engine files — all complete (none are stubs)

**`engine/Engine/EffectExecutor.cs` (460 lines)** — COMPLETE
Methods:
- `Execute(effect, source, state, targets)` — dispatches to the correct op handler
- `ApplyDamage(target, amount, state)` — deals damage to creature or player
- `ApplyHeal(target, amount)` — heals creature or player
- `ApplyBuff(target, attack, vigor, duration)` — applies stat modifiers (duration tracking is a TODO placeholder)
- `ApplyDestroy(target, state)` — destroys a creature
- `ApplyDraw(target, amount, state)` — draws cards with fatigue handling
- `ApplyDiscard(target, amount, state)` — discards from hand
- `ApplyExcavate(target, amount, state)` — look at top N, take 1, bury rest
- `ApplyBury(target, amount, state)` — bury from top of deck
- `ApplyUnbury(target, amount, state)` — return from barrow to hand
- `ApplySummon(target, tokenId, source, state)` — summon token to empty lane
- `ApplyGrantKey(target, keyword)` — grants keyword
- `ApplyRemoveKey(target, keyword)` — removes keyword
- `ApplySilence(target)` — silences (removes all keywords)
- `ApplyBounce(target, state)` — returns to hand
- `ApplyAttune(target, amount)` — increases attunement
- `ApplyMoveLane(target, source, state)` — move to another empty lane
- `ApplyIdentify(target)` — identifies a relic
- `ApplyGainVigor/LoseVigor(target, amount)` — modifies player max vigor
- `ApplyCopy(target, source, state)` — creates a copy of source at target
- `ApplySetStat(target, attack, vigor)` — sets base stats
- `ApplyRefresh(target)` — readies a creature (UN-taps)
- `KillCreature(card, state)` — destroys with Unearth check, fires death events

**`engine/Engine/KeywordHandlers.cs` (203 lines)** — COMPLETE
Methods:
- `OnPlay(card)` — sets SummonedThisTurn, handles Swift (not exhausted) and Ward (1 charge)
- `CanAttack(card)` — returns false if Rooted
- `ResolveTargetLane(attacker, sourceLane, requestedTarget)` — handles Reach targeting
- `ApplyWard(target, incomingDamage)` — Ward absorbs damage, decrements charges
- `OnCombatDamageDealt(attacker, defender, actualDamage)` — applies Venom marking
- `ResolveVenom(state, attackerPlayerIndex)` — destroys Venomed creatures post-combat
- `OnDeath(card, owner)` — handles Unearth queueing
- `ProcessUnearth(player)` — at turn start: pay cost → return to hand or discard
- `ProcessFragile(player)` — at end of turn: destroy FRAGILE creatures
- `IsSealed(card)` — checks SEALED keyword
- `DestroyCreature(lane, card, owner, state)` — internal helper

**`engine/Engine/TriggerBus.cs` (232 lines)** — COMPLETE
Methods:
- `Fire(state, trigger, eventPlayerIndex)` — fires all matching abilities from board creatures + rune tokens
- `FireDeathEvents(state, deadCard, controller)` — fires ON_DEATH for a specific creature
- `EvaluateCondition(condition, source, controller, state)` — public condition evaluator
- `CollectAbilities(state, trigger, eventPlayerIndex)` — collects matching abilities in deterministic order
- `CollectFromPlayer(player, trigger, result)` — collects from board + rune tokens
- `ConditionMet(condition, source, controller, state)` — evaluates conditions (13 condition ops)
- `CountCreaturesOnBoard(player)` — helper
- `HasAnyCreatureWithKeyword/Strata` — helpers
- Trigger chain depth capped at 20 (MaxTriggerDepth)

**`engine/Engine/GameAction.cs` (47 lines)** — COMPLETE
Types:
- `GameAction` (abstract base) — has PlayerIndex
- `EndTurnAction : GameAction` — ends the turn
- `PlayCardAction : GameAction` — CardInstanceId, Cost, LaneIndex
- `AttackAction : GameAction` — SourceLane, TargetLane (null = opposing, non-null for Reach)

**`engine/Engine/TargetResolver.cs`** — COMPLETE
Resolves `TargetDef` (scope, filter, count) into a list of `ResolvedTarget` (CreatureTarget or PlayerTarget). Handles all filters: ANY, ADJACENT, OPPOSING, EDGE_LANE, CENTER_LANE, RANDOM, LOWEST_VIGOR, HIGHEST_ATTACK, LOWEST_COST, HIGHEST_COST, DAMAGED, UNDAMAGED, CHOSEN, STRATA:X, KEYWORD:X, TYPE:X.

**`engine/State/GameState.cs` (312 lines)** — COMPLETE
- Constructor, Initialize factory, Clone, ComputeStateHash (FNV-1a)
- Properties: Players[], CurrentPlayerIndex, TurnNumber, Rng, ContentVersion, NextInstanceId, TriggerDepth, IsGameOver, WinnerIndex, ActionLog

**`engine/State/PlayerState.cs` (117 lines)** — COMPLETE
- Vigor, MaxVigor, Attunement, AttunementMax, AttunementPerTurn, FatigueCounter, MaxHandSize
- Deck, Hand, Discard, Barrow, UnearthQueue, RuneTokens (lists)
- Lanes (LaneState[5])
- AttachedCurseIds, HasMulliganed
- Deep Clone()

**`engine/State/LaneState.cs` (38 lines)** — COMPLETE
- Index, Occupant (CardInstance?), AttachedCurseIds

### Function signatures — none are stubs or unfinished

Every method in the engine is fully implemented. There is exactly **one stub** identified:
- `TriggerBus.cs` line 185: `conditionOp.RITUALS_CAST_GTE => 0, // Not tracked yet — stub`

This means the `RITUALS_CAST_GTE` condition is hardcoded to return 0. Ritual cast tracking is not yet wired into the engine.

### Card system files — all COMPLETE

- `CardDef.cs` — full card definition model
- `AbilityDef.cs` — ability definition
- `EffectDef.cs` — effect definition
- `CardEnums.cs` — all enums (Strata, CardType, Rarity, Trigger, Op, Scope, Duration, ConditionOp, Zone, etc.)
- `CardLoader.cs` — JSON file loading
- `CardRegistry.cs` — runtime card registry
- `RulesTextRenderer.cs` — generates human-readable text from abilities
- `ConditionDef.cs` — condition definitions
- All content loaders (DigSite, DigTool, Encounter, Forge, LostRelic, Map, Rune, Tutorial) — COMPLETE

---

## 7. OPEN PROBLEMS

Priority-ordered:

### High Priority

1. **Field-Effect System: MISSING** — The concept of "two cards that sit beside the player's character and act as field effects" does not exist in any project file. No design doc, no engine code, no placeholder. This would need a complete specification: what are they, how are they chosen, when do they trigger, how do they interact with each other and the opponent's field effects, timing rules. The closest existing systems are the Rune System (pre-game loadout of 30 passive buffs injected as ability tokens) and RELIC cards (permanents in lanes that start unidentified). Neither matches the described mechanic.

2. **Tutorial is broken (3 failed attempts)** — Three successive tutorial implementations failed because the teaching model was instruction-before-action (read 6 sentences → then act from memory). The cascade failure: if a single tutorial popup's trigger condition is missed, ~60% of subsequent popups never fire. The correct fix is a consequence-first model (do → see → understand) where each popup fires independently on its own trigger with no condition chains. The bot pauses while popups are open (currently the bot keeps running while the player reads). See `docs/TECH_DEBT.md` for the full postmortem.

3. **Dev menu must be removed before store submission** — `client/scripts/DevMenu.cs` grants jump to boss, +10 dig charges, +20 fragments, unlock all nodes, clear save. Search for `REMOVE BEFORE RELEASE` in .cs files. **Ship-blocker if present.**

### Medium Priority

4. **Combat has no meaningful choices** — Attacking is always correct because: no blocking, no bad trades (attacker chooses targets), no combat tricks, no wait-to-buff incentive. The game needs a blocking system, combat tricks, or a cost-to-attack mechanic. Currently every creature that can attack should attack. Documented in `docs/TECH_DEBT.md`.

5. **Engine tests encode implementation, not specification** — Tests were written by reading code, not docs. When code had bugs, tests matched them. Some test assertions encode wrong values. The `tests/State/` folder has the first tests with proper doc-section citations; all other tests are suspect.

6. **First-player advantage (P0 always wins in bot-vs-bot)** — GreedyBot is deterministic; P0 always wins in 3/3 batch-sim games. May need additional compensation for P1 (extra card, or an additional attunement bump). Not a launch blocker but needs balance pass.

7. **Cost-10 cards never tested** — Cost 10 is reachable since the v0.2 piecewise calibration but no card has ever reached it. Unknown balance implications. Flag for manual review.

### Low Priority

8. **HOLLOW art moderation rejection (~30%)** — FLUX.2-pro blocks ~30% of HOLLOW-stratum art prompts as "Violence" (undead, bones, graves). Options: switch model, tune prompts, hand-commission relic-rarity HOLLOW cards, or live with the 30% fallback rate. Not blocking launch.

9. **3 pre-existing Python test failures** — In `pipeline/tests/test_generate.py` — all from text drift between test assertions and actual prompt strings. Easy fixes, not blockers.

10. **Pacing values are provisional** — All animation timings (summon 0.3s, death 0.4s, damage float 0.9s, bot think-delay 1.5s) are placeholders tuned against grey rectangles. Do not tune until real card art is present.

11. **Engine `RITUALS_CAST_GTE` condition is a stub** — The trigger bus has a condition op for checking how many rituals a player has cast, but ritual cast tracking is not wired into the engine. Returns `0` always.

12. **Android export silently swallows `dotnet publish` failures** — Godot continues the export even if publish fails, packaging stale DLLs. The `client/export_apk.sh` script catches this with DLL timestamp comparison and size checks. Always use the script, never raw Godot CLI.

13. **Exported builds crash on `System.IO.File.*` paths** — `GlobalizePath("res://...")` returns a path that only exists in the editor, not in exported builds. All content loading must use `Godot.FileAccess.GetFileAsString()`. Fixed in P3-02 for all loaders but the pattern may reappear in new code.

### Undocumented Design Decisions

14. **No PvP system** — Deliberately deferred for post-v1. When it ships: two queues (Delve with runes, Pure without).

15. **No in-app purchases for power** — Runes are earned through gameplay, never sold. Monetization: free Region 1, one-time unlock ($6.99) for Regions 2–3, cosmetic frames, expansion DLC regions ($4.99 each).

16. **No energy timers** — Design stance: energy timers that gate campaign play are explicitly rejected as manipulative.

17. **The "two cards beside the player" field-effect system: undefined** — This appears to be a design concept that was discussed but never committed to any document. It needs to be specified from scratch: what are the two cards, what slots do they occupy (separate from the 5 creature lanes), how are they selected pre-game or during the game, what timing priority do they have relative to creature triggers, how do they interact with each other and with the opponent's field effects, and what happens when a field-effect card is destroyed or silenced.
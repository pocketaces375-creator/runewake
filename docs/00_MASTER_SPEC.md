# RUNEWAKE: The Buried Age — Master Spec v0.1

**Working title:** Runewake (fallback names: Relicwake, Sigil & Spade, The Hollow Age)
**Genre:** Single-player-first digital TCG with a node-based world map, meta-progression rune system, and an archaeology/excavation layer.
**Platforms:** iOS + Android (App Store / Google Play), offline-first.
**Team:** 1 human (Trikzos) + 1 coding agent (Hermes / DeepSeek V4 Flash via OpenClaw).

---

## 1. The one-sentence pitch

You are a Delver in a world that forgot itself; you duel the wardens of each region using runes you unearth, and every relic you pull out of the ground is permanently engraved with your name.

---

## 2. Non-negotiable architectural pillars

These five decisions are locked. The agent must not relitigate them.

**P1 — The rules engine is a pure, deterministic state machine.**
`(GameState, Action) -> GameState`. No rendering, no I/O, no randomness except a seeded RNG carried inside the state. This single decision buys us: headless balance simulation, replay files, server-authoritative PvP later, and an AI opponent that can plan by forward-simulating. It is the foundation everything else sits on.

**P2 — Cards are data, never code.**
Every card is a JSON object conforming to `schema/card.schema.json`, built from a *closed* effect DSL (see `02_CARD_DSL.md`). No card ever ships as a script. Consequences: we can patch balance and ship new sets without an App Store resubmission, and an LLM can author cards that the engine is guaranteed to be able to execute.

**P3 — AI generation is offline and batched, never live at runtime.**
No LLM call ever happens during a match or on the player's device. Generation runs as a server-side pipeline that produces validated, simulated, approved cards into a content pack. This protects cost, latency, determinism, balance, and App Store review.

**P4 — Content packs are versioned and hot-updatable.**
Client boots with a bundled pack, then checks for a newer pack version and downloads it. Content version is part of every match record so replays never desync.

**P5 — Single-player is fully offline. Server is optional at launch.**
Progression lives in local SQLite, syncing to Supabase when available. This means v1 ships without a backend dependency and cannot be broken by server downtime.

---

## 3. Locked technology stack

| Layer | Choice | Why |
|---|---|---|
| Rules engine | **C# .NET 8 class library** (`Runewake.Engine`), zero dependencies | Same code runs in the game client, in the headless simulator, and on a future server. One engine, one source of truth. |
| Client | **Godot 4.3+ (.NET / C# build)** | Free, no revenue share, exports iOS + Android, scene files are plain text (agent-editable), and it can reference the engine DLL directly. |
| Headless sim | **C# console app** (`Runewake.Sim`) referencing the engine | Runs thousands of self-play games in CI for balance scoring. |
| Content pipeline | **Python 3.11 + FastAPI + Pydantic** (`pipeline/`) | Generation, validation, art, moderation. Writes JSON; never touches game code. |
| Backend | **Supabase** (Postgres, Auth, Storage, CDN) | Auth, relic ownership ledger, content pack hosting, telemetry. Zero server maintenance. |
| Local save | **SQLite** via Godot / `Microsoft.Data.Sqlite` | Offline-first progression. |

**Approved fallback if Godot .NET mobile export causes pain:** keep the engine in C#, run it server-side, and build the client in Flutter. Do **not** switch without first proving a Godot .NET "hello world" builds to a physical iOS device (this is Ticket P0-02 for exactly that reason).

---

## 4. Document map

| File | Contains |
|---|---|
| `00_MASTER_SPEC.md` | This file. Pillars, stack, loop. |
| `01_GAME_RULES.md` | Complete duel rules, keywords, combat, win conditions. |
| `02_CARD_DSL.md` | The closed effect grammar the AI writes into. |
| `03_RUNE_SYSTEM.md` | Old-LoL-style rune pages, budgets, full starter rune list. |
| `04_WORLD_AND_MAP.md` | Regions, zones, wardens, excavation, relic engraving, lore. |
| `05_AI_PIPELINE.md` | Generation → validation → simulation → art → publish. |
| `06_BUILD_ROADMAP.md` | Phased tickets sized for a fast/small coding model. |
| `07_AGENT_PROTOCOL.md` | How Hermes must work. Read this first, every session. |
| `schema/card.schema.json` | Machine-readable card schema. |
| `schema/example_cards.json` | Six hand-authored reference cards. |

---

## 5. The core loop

**Session loop (2–5 min):**
Pick a node on the region map → choose a Rune Page to counter that warden's theme → duel → win → earn Shards + a Dig Charge.

**Progression loop (30–60 min):**
Spend Dig Charges at excavation sites → unearth rune fragments, lore entries, and Unidentified Relics → identify relics into playable cards → rebuild deck and rune pages → unlock the next node.

**Long loop (weeks):**
Clear a region → its Warden Boss falls → a **Lost Relic** card is minted with your name and the date on the card face → the region's Codex completes and reveals a piece of the world's buried history → the next region unlocks.

The archaeology layer is not decoration. It is the reward schedule: every duel is a shovel-stroke, every boss is a dig site, and the collection screen is a museum of things *you* found rather than things you bought.

---

## 6. Scope discipline for v1

**In v1:** 3 regions, ~24 nodes, 6 wardens, 2 Warden Bosses, ~180 cards, 30 runes, single-player only, no PvP, no gacha.

**Deferred (design for it, don't build it):** PvP, guilds, draft/limited, seasons, cosmetics store, cross-save.

**Never:** loot boxes with hidden odds, energy timers that gate the campaign, pay-to-win runes. Runes are earned. Money buys new *content* (region expansions, cosmetic frames, foils), never power. This is both an ethical stance and a practical one — a TCG's long-term value is trust in the ladder.

---

## 7. Business shape (informing design, not built yet)

- Free campaign through Region 1. One-time unlock (~$6.99) for Regions 2–3.
- Expansion "Digs" as paid DLC regions (~$4.99 each), 3–4 per year, each bringing a set of ~60 cards.
- Cosmetics: relic frames, dig-site dioramas, foil treatments.
- Optional Season "Dig Pass" once PvP exists.

Cost model note: AI generation cost per card should land in the cents, not dollars, because generation is batched and text-only; art is the expensive line item and is the one to negotiate down or partially hand-author. Budget it explicitly in `05_AI_PIPELINE.md`.

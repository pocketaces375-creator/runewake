# Runewake: The Buried Age — Game Design

## Pitch
You are a Delver in a world that forgot itself; you duel the wardens of each
region using runes you unearth, and every relic you pull out of the ground is
permanently engraved with your name.

## Genre
Single-player-first digital TCG with a node-based world map, meta-progression
rune system, and an archaeology/excavation layer.

## Platforms
iOS + Android (App Store / Google Play), offline-first.

## Team
1 human + 1 coding agent (DeepSeek V4 Flash via Hermes).

## Architectural Pillars (locked, do not relitigate)

1. **Pure deterministic state machine.** `(GameState, Action) -> GameState`.
   No rendering, no I/O, seeded RNG only. Buys: headless sim, replays,
   server-authoritative PvP, AI opponent.

2. **Cards are data, never code.** Every card is JSON conforming to
   `schema/card.schema.json`. Built from a *closed* effect DSL.

3. **AI generation is offline and batched.** No LLM call during a match.
   Generation runs as a server-side pipeline producing validated packs.

4. **Content packs are versioned and hot-updatable.** Client checks for
   newer packs. Content version in every match record for replay safety.

5. **Single-player is fully offline.** Progression in local SQLite, syncing
   to Supabase when available. Ships without backend dependency.

## Tech Stack

| Layer | Choice |
|---|---|
| Rules engine | C# .NET 8 class library, zero dependencies |
| Client | Godot 4.3+ (.NET / C# build) |
| Headless sim | C# console app |
| Content pipeline | Python / FastAPI / Pydantic |
| Backend | Supabase (Postgres, Auth, Storage, CDN) |
| Local save | SQLite via Godot / Microsoft.Data.Sqlite |

## Current Phase: P7 (Launch)

P0-P6 complete. Remaining:
- P7-01: Onboarding tutorial (first 3 duels)
- P7-02: Supabase account + relic ledger sync
- P7-03: Telemetry + settings/accessibility
- P7-04: App Store and Play listings + privacy policy + age rating
- P7-05: TestFlight/closed beta with 50+ players
- P7-06: Crash reporting + launch
- Human QA: Device install & playthrough, store submission

## Key Stats
- C# tests: 463/463 passing
- Python tests: 221/221 passing
- Total backlog items: 51 (45 done, 6 open)
- Known replay hash: cc76e76 (P4-04 delivered)
- APK SHA-256: 6a92d0db6c1d203d22f45d450fea7a2ec0e7e005d383903b234864a5426e0699
- Cards generated: 65 total across 5 strata packs
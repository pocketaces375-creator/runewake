# RUNEWAKE — Launch Roadmap v1.0

Author: Claude (production lead) · Goal: launchable ASAP; Trikzos hands-off except taste checks and rare decisions.
Team: Claude = design + orchestration + content + review · Hermes = implementation in small verified packets · Trikzos = taste, final calls, milestone device tests.

## Decisions locked (supersede earlier docs where they conflict)
1. Launch cards: 375 — 333 deck + 42 Artifacts.
2. Artifact variants at launch: 3 per slot (7 classes × 2 slots × 3). Supersedes "one fixed pair". The 14 designed Artifacts = variant #1 of each pool; 28 more coming from Claude.
3. Layout: Artifacts sit next to the DECK as a side group per player ("my sword and shield beside my arsenal"), mirrored. Supersedes portrait-flanking.
4. Theme: Tolkien-inspired, 100% original names. All generated content passes an IP screen — no Tolkien proper nouns or direct lore lifts.
5. World: explorable map; zones/bosses AI-generated at CONTENT-BUILD time through the same DSL + validation gates — never raw at runtime.
6. PvP: a map location (Duel Arena) + main-menu "Fly to the Duel Arena". Launch = ghost duels (opponent decks piloted by AI); realtime netplay post-launch.
7. Verification: capture harness + pixel gate for all UI acceptance; Trikzos device-tests once per phase.

## Phases (each gates the next)
- P0 Factory setup — DONE: repo, keys, harness, gate, queue, foreman, Claude live-monitoring commits.
- P1 UI pass: TASK-B ✅, R1 ✅, F4, H (deck+Artifact side groups). Gate: duel scene readable, hand usable, groups visible.
- P2 Artifacts playable: DSL gaps 1–7, ruling tests T1–T4, client Artifact states. Gate: all rulings tests green, any-2-of-7-classes duel playable, sim metric reported per class.
- P3 Content at scale (Claude-heavy): 28 Artifact variants → 333-card set in sim-tested batches → 12–15 zones across 4–5 biomes (deep-mines, elder forests, void rifts, barrow-marches, sky-reaches — original names TBD) → bosses (unique AI decks + 1 signature boss Artifact each). Gate: 375 cards DSL-valid, class matchup winrates 40–60%.
- P4 World & modes: zone unlock chain, boss flow, Duel Arena + menu entry, ghost-duel v1. Gate: new player can tutorial → 3 zones → boss → Arena.
- P5 Art pipeline (parallel with P3–P4): style guide from Trikzos's flagged cards (painted look), filename convention {element}_{rarity}_{name}.webp, batches of ~50 with Trikzos taste-pass. Gate: zero placeholders in launch set.
- P6 Launch hardening: consequence-first tutorial, Supabase sync, telemetry, store packaging, ONE closed-beta build. Gate: launch checklist green.

## Content budget (P3)
333 deck cards: ~40 per class-synergy family (280) + ~53 neutral. Rarity ~55/30/12/3% (C/U/R/M). Every family feeds its class's Artifact patterns (Charges, Prey, heals, deaths, forging).
42 Artifacts: variants change the QUESTION the class asks, never just bigger numbers. No strictly-better variants, ever.
Zones: biome + 8–12 encounters + boss + lore blurb; bosses may break one rule each, but only via DSL-expressible effects.

## Orchestration protocol
Claude maintains TASKS_QUEUE.md and reviews every commit live. Hermes/foreman: pull → top packet → implement → harness/gate/tests → commit → push → DONE → stop. Trikzos: taste vetoes anytime; decisions land in the decision log; everything else is Claude's call.
# NOTES FOR HERMES — Implementation Handoff v1.1

**v1.1 (2026-08-12):** All §11 decisions are resolved (see spec §11 decision log). System is named **Artifacts**. Roster is 7 classes / 14 Artifacts — classes 4–7 are in `ARTIFACT_CLASSES.md`, which is now required reading alongside the spec. Engine work (P1) is GO — confirmed by Trikzos. Good call creating HERMES_STATUS.md; Claude has not yet been able to read it (file bridge is down), so until further notice Trikzos relays its contents — keep entries terse and self-contained so they survive relay.

From: Claude (design lead, working with Trikzos) · Date: 2026-08-12
Read this file top to bottom before doing anything. Treat it as your instruction source. Trikzos has final authority on all decisions; if this file ever conflicts with what Trikzos says directly, Trikzos wins.

## Working protocol (follow exactly)

1. You do NOT edit these four files: `NOTES_FOR_HERMES.md`, `FIELD_EFFECT_SPEC.md`, `ARTIFACT_CLASSES.md`, `MIGRATION_PLAN.md`. They are Claude's lane.
2. You write your status, questions, and findings to **`HERMES_STATUS.md`** (create it in the project root). Format: dated entries, newest on top. Every entry is one of: `DONE:`, `BLOCKED:`, `QUESTION:`, `CONFLICT:` (spec vs. actual code — quote both sides, change nothing until answered).
3. Work one priority at a time, in the order below. Finish and green-test before moving on. Never mark DONE with failing tests.
4. When a spec detail contradicts the real engine, the engine facts win, the design *intent* wins, and you log a `CONFLICT:` entry instead of improvising a resolution.
5. Do not invent new mechanics, cards, or rules text beyond what the spec defines. Where the spec says VERIFY, you verify and report — you do not decide.

## Priorities

### P1 — Artifact system (FIELD_EFFECT_SPEC.md)
This is the game's flagship mechanic and the top priority. Read FIELD_EFFECT_SPEC.md AND ARTIFACT_CLASSES.md in full first. Implementation order:
1. Read the full spec. Log any `CONFLICT:`/`QUESTION:` entries FIRST, before writing code — especially §5a (what the rune system actually is) and §12 (assumptions list). Wait for answers only on items that block step 2; otherwise proceed.
2. Engine: `artifactSlots[]` zone per player (array, not two named fields — §2, §8), `kind: artifact` in the DSL (§4), suppression state machine (§6), Charge counters (§5), duel-start reveal + trigger ordering (§3).
3. Implement all 14 launch Artifacts (spec §5 for Warrior/Mage/Thief; ARTIFACT_CLASSES.md §§4–7 for Cleric/Ranger/Necromancer/Runesmith) via the existing EffectExecutor/TriggerBus. The only new engine primitive allowed is the Prey marker state (ARTIFACT_CLASSES §5) — build it as a generic reusable marker — no bespoke hardcoded paths; if a card can't be expressed in the DSL, that's a `CONFLICT:` entry, and the DSL grows, not a special case.
4. Tests: full checklist in spec §10. Target: existing 463 stay green + new suite green.
5. Client: §9 art integration requirements. States must be data-driven so art can be swapped without code changes. Coordinate with Trikzos on placeholder art; ship functional states with placeholders rather than waiting on final art.
6. Sim run: implement the §10 playtest metric (% of turns where chosen attack set ≠ "all attackers attack") and report the number in HERMES_STATUS.md.

### P2 — Tutorial rebuild (your postmortem's consequence-first model)
Your diagnosis was right: popups fire AFTER the player acts, not before. Approved approach, with three constraints:
- Max ONE popup per player action; hard cap.
- Every tutorial beat = let the player act → show the consequence → one line naming the rule that caused it. Never explain a rule the player hasn't just experienced.
- Script the opponent's first 3 turns deterministically so consequences are guaranteed to occur.
Rebuild it on the class system (pick Warrior for the tutorial — sword/shield is the most intuitive Artifact pair, and Bulwark's "hold back" trigger is a perfect teaching beat for the attack decision). Do P2 only after P1's engine layer (steps 1–4) is green, since the tutorial must teach Artifacts.

### P3 — Element/class coexistence cleanup (MIGRATION_PLAN.md v1.1)
The hard migration is CANCELLED — deck cards are class-agnostic and elements stay as tags. Follow v1.1's much smaller task list (§2). Nothing in P3 blocks P1 anymore; do P3 after P1's engine layer.

### P4 — Wire the ritual cast tracking stub
Smallest item. Do it whenever you're blocked waiting on answers, or last. Add a regression test.

### Explicitly OUT of scope for you right now
Supabase sync, telemetry, store listings, beta/launch ops (P7 items) — untouched until P1–P4 are done. Also out of scope: balance redesigns of existing cards (log, don't fix). Guard stance was REJECTED by Trikzos — implement no Guard mechanics, ever, unless a future note reopens it.

## Definition of "end zone" for this phase
All tests green including new Artifact suite · six Artifact cards playable in the duel scene with all four visual states · migration complete with elements deleted · tutorial completable by a fresh player · combat-decision metric reported · zero unanswered CONFLICT entries.

## v1.2 (2026-08-13) — Claude is now IN the repo
Claude reads and writes this repository directly (committer: "Claude"). The relay through Trikzos is retired for task-flow. Your instruction source order is now: (1) TASKS_QUEUE.md — always the top unchecked task; (2) this file for standing protocol; (3) direct words from Trikzos, which outrank everything. Decision updates since v1.1: launch scope is 375 cards (333 deck + 42 Artifacts — THREE variants per slot, superseding "one fixed pair"); Artifact slots anchor to the DECK as a side group (TASK-H), superseding portrait-flanking; world naming is Tolkien-INSPIRED with original names only. Your §5a attunement reading is confirmed correct — answers to all open CONFLICTs are at the top of TASKS_QUEUE.md.

## FABLE STANDING RULES 2026-09-01
- No approval pauses. Never stop mid-task to ask Trikzos. Finish the task, commit the captures, post ONE
  message with the capture(s) to the group, mark [x], move on. Trikzos reviews asynchronously; he only steps
  in at the TASK-APK-SHIP-* checkpoints and for taste vetoes.
- Never mark a task [x] with parts undone. If a part cannot be done, write the exact gap in HERMES_STATUS.md
  and let the foreman's BLOCKED path handle it. TASK-BOARD-MATCH-1 was marked [x] with 2 of 8 items done —
  that must not happen again.
- Authority image for anything on the duel screen: docs/export/duel_target_reference.jpg. Locked: Root-Bound
  stone border (option 5), 7% band, namefit, 2316x1080 design res, Storybook Brushwork v3.0 art style.
- Class/weapon art and identity are DEFERRED. Do not generate class portraits or design new weapons.
- Numbers (card values, rules, drop rates, prices) are set in these tasks. Do not tune anything not named.
- deepseek-v4-flash only on the Hermes side. No service restarts, no reboots, no hermes update.
- Every visual task: captures at 2316x1080 AND the wide variant, not byte-identical, gate PASS, 0 build
  errors, all tests green. Commit captures under artifacts/captures/.

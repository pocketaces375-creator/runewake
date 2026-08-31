# 08 — Open Questions & Current Blockers (2026-08-31)

## Blockers, right now

1. **BORDER-FIX-2 unverified (the active fire).** BORDER-FIX-1 shipped card faces fully covered in stone (NinePatchRect margins drawn 1:1; root cause + fix spec in 04/06). Jett announced a follow-up fix at 3:58 PM but nothing new is on origin and the preview thumbnails still looked stone-covered. A scheduled check (~20:46 UTC) verifies the committed captures when pushed. **The APK is blocked behind Trikzos approving the fixed capture in the Telegram group.**
2. **Cloud→repo push access.** Fable cloud sessions fetch but cannot push (proxy allowlist fixed at session creation; connecting the repo mid-session did not take effect, fresh scheduled sessions also refused). Working around via Telegram paste relay. Open items: attach the repo when creating future orchestration tasks (claude.ai/code repository selector works for that surface); or link the mini PC via the desktop app — Dispatch setup (Cowork sidebar → Dispatch → Get started → enable file access + keep-awake; Settings → Computer use ON) was in progress when this export was made, completion unconfirmed.
3. **Tutorial built on a broken layout.** TUT-BUILD-1 completed against the bad border render; its 9 highlight rects need re-verification after BORDER-FIX-2. Separate pre-existing bug: Main.cs doesn't navigate to the duel scene when TutorialScriptId is set, so tutorial captures can't run headless.

## Design questions awaiting decisions/data

4. **First-player advantage (gates all class balance).** Mirrors: P0 wins ~62.5% avg (66–68% for fast classes); matrix "violations" are largely this bias. Awaiting the mirror-harness variant tables from TASK-P2COMP-1's follow-up: (a) baseline, (b) P1 +1 attunement T1, (c) P1 draws 6, (d) both, (e) P0 ramp delayed. Fable picks the compensation; then re-run the gate; only then tune classes.
5. **Sim roster validity.** The sim reports Warrior/Mage/Thief/Cleric/Ranger/Necromancer/Runesmith; game content defines warrior/necromancer/druid + (pending CLASS-7) tidecaller/dawnward/ranger/occultist. Are sim classes stale aliases or non-existent decks? If the latter, all 49 matchup numbers are suspect. P2COMP-1 step (0) was ordered to verify and STOP if mismatched — confirm what its committed report concluded before trusting any sim output.
6. **Duel-screen design resolution.** Captures/gates run at 1152×648 (~88px board cards) while the approved visual target is 2316×1080 (~200px cards). Every "text doesn't fit" and "border invisible" problem is amplified at half-res. Decide the client's design resolution / stretch mode before more pixel-perfect UI work.
7. **Economy:** what do shards buy — packs, crafting, cosmetics? One decision unlocks reward screens, merchant nodes, and the collection "NEW" moment.
8. **Starting Vigor 25 vs 20** (docs Q: does 25 drag at 5 lanes?) — sim then feel; and hand cap / Vigor interactions once compensation changes land.
9. **Q1 HOLLOW art moderation (from docs/OPEN_QUESTIONS.md, still open):** FLUX.2 Pro rejects ~30% of HOLLOW prompts ("Violence": rotting flesh, souls/scythe, revenant/burial). Options: alternate FLUX model for HOLLOW / softened prompt vocabulary / hand-commission the ~3 relic-tier rejects / accept fallback frames. Needs Trikzos before HOLLOW production batches.
10. **Warden Aelin signature-card grant** — flagged in SHIP-STARTERS as "spot-check if a play hook exists"; confirm it actually triggers.

## Infrastructure questions

11. **Was the Dispatch/desktop link completed?** If yes, cloud sessions gain direct hands on the mini PC (device tools) and the paste relay dies. First test on link: run one command, push the pending local commits, confirm loop closed.
12. **Token rotation** (repo-remote PAT + pasted fine-grained PAT) — advised repeatedly, unconfirmed done.
13. **`hermes_daily_check.sh` exits 1** (curl caught by `set -e`) — the daily check has been silently failing since ~08-22. Low priority, unfixed.
14. **TcgBot memory near capacity** — stale-report failure mode documented; AGENTS.md rules added ("pull first, trust repo over memory", status-message convention), but capacity itself unaddressed.
15. **Play Store items** (from PROJECT_STATE, still human-action): device QA on physical phone, TestFlight/internal testing tracks, store listings + privacy policy (mandatory once accounts exist).

## Explicitly parked (state preserved, do not spend time unless reopened)

The FableBot Telegram bridge: two Hermes MCP servers built/running/verified (:9100 Jett default, :9101 tcgbot; SSE; 10 messaging tools; regex hard-block wrapper on messages_send tested; profile isolation proven — Runewake group only on :9101). Known hole: `permissions_respond` exposed to clients (self-approval risk — remove if resumed). FableBot itself designed, not built: ~120-line standalone model-less Python relay on :9102 + @BotFather token needed. Historical blocker: Claude Desktop headless login (Xvfb :99; clicks don't register, keyboard does; `--password-store=basic` keyring fix in the launcher made login persist on 08-12; QEMU now installed for the cowork bridge). If resumed: restore credentials.json, delete copied Firefox cookie files, drive login by keyboard.

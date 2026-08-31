# 07 — Standing Instructions (how to work on this project)

These are rules Trikzos has given repeatedly or that hard experience made standing policy. Any Claude session picking this project up follows them from message one.

## From Trikzos, non-negotiable

1. **Hermes-side agents (mini PC: TcgBot, Jett, foreman) run ONLY deepseek-v4-flash.** Never another model there, never Claude API spend on that side. On deepseek failure: retry once, then leave the task for the next Fable check-in. (Claude-side sessions/scheduled tasks run on his membership and may use any model.)
2. **Nothing may ever take the mini PC offline** from any automated path: no `hermes update`, no systemctl, no gateway restarts, no pkill, no reboots. He is often away from it.
3. **Keep it simple for him.** He is overwhelmed by complexity: shortest-path answers, plain text, no popup/multiple-choice question UI (breaks his client). Tell him exactly what to click/paste, one thing at a time.
4. **Keep the master checklist alive** — update it as pieces land, refer to it for what's next, and line up + execute next tasks proactively instead of waiting to be asked. Ask only when input is genuinely needed.
5. **Trikzos has final authority on everything.** His direct words outrank any doc. Taste decisions (art, borders, feel) are his; present options, don't decide for him.
6. **Numbers are Fable's to move.** Sim/balance tasks REPORT ONLY — never adopt a winner, never change shipped defaults or card values from a production agent.

## The production loop (how work actually ships)

- Direct TcgBot by writing `TASKS_QUEUE.md`: insert `- [ ] TASK-<ID> — ...` blocks immediately after the anchor line `# New tasks MUST be added ABOVE any '## ' subheader...`, column-0 task line, indented continuations, explicit `Acceptance:` clause naming what must be captured/tested/measured. The foreman takes the TOP unchecked task, one per session. Keep 5–6 tasks stocked — the failure mode is the queue emptying while Trikzos is away (foreman heartbeats `empty_queue`).
- Pasting tasks as separate messages REVERSES their order (each inserts at top). Paste as one block or state the intended final order explicitly.
- Cloud sessions can `git fetch` the repo but usually cannot push (proxy allowlist is fixed at session creation). Working delivery: paste text to TcgBot in the Runewake Telegram group; it does the git work. Binary files do NOT survive that relay; text does — ship scripts as `.txt` if needed (python3 ignores extensions).
- Bus messages (`bus/claude_to_hermes.md`, `## MSG <seq>` append-only): processed only when their commit touches ONLY `bus/`.
- All deliverables (captures, DONE reports, APK links) go to the **Runewake Telegram group** via TcgBot. Jett's bot has no group access; a DM post counts as not delivered.
- Every APK ship includes the verified-download hash line (download from the public URL, SHA-256 match, then post).

## Verification doctrine (paid for in blood, three times)

1. **Never trust a DONE claim on visual work.** Open the committed capture as an image and describe what a player would see. If the described screen doesn't match the task goal, it is not done — whatever the gate says.
2. **A gate is a floor, not a proof.** Gates that read expected values out of the artifact under test can pass themselves. Standing gate rules: fail if standard and wide captures are byte-identical; fail if a wide meta doesn't report wide dims (1999×932); fail any occupied card slot whose center region matches its own border.
3. **`git pull` and trust repo state over agent memory.** TcgBot's memory goes stale and it re-reports old work as current.
4. **Check `git show --stat`** on claimed commits — confirm the files claimed changed actually changed (file-mutation verifier warnings have caught silent patch failures).
5. **When a rendering approach changes mid-task, re-verify from zero** — measurements from the previous approach are void.
6. **Trikzos' approval in the group is the release gate** for anything visual, and for every APK.

## Design guardrails (do not relitigate — see 04-decisions-log for reasons)

Root-Bound stone border, 7% computed band, 9-slice (never scale the whole frame PNG) · name auto-fit safe-zone rules, no glyph under 8px · cost rune top-right · Guard stance NO · closed keyword set · deck cards class-agnostic · hard 30 decks · Storybook Brushwork v3.0 + FLUX.2 Pro + the 6-sample wave veto gate · never put stratum names in art prompts · no text/signatures in art · Tolkien-inspired original naming.

## Scheduled infrastructure on Trikzos' account

- `Runewake hourly % to alpha` (:24) and `Runewake 30-min offset % to alpha` (:54) — read-only status pings (Sonnet; that's fine, membership-side).
- `Fable director check-in (Runewake)` (:09 hourly, claude-fable-5, trigger id `trig_01VFXcx8v45GXiqXpfqfTMar`) — pulls repo, verifies completed work against actual captures, reopens false DONEs, restocks the queue to 5–6, pushes or emits paste chunks, one push notification. This is the autonomy backbone; keep it alive.

## Security standing items

Rotate the GitHub PAT embedded in the repo's git remote and the fine-grained PAT pasted into a chat session (github.com/settings/personal-access-tokens). Never commit tokens — the repo is public. The Hermes MCP `permissions_respond` tool should be removed from the exposed set if the Telegram bridge is ever resumed.

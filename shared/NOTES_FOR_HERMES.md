# NOTES_FOR_HERMES.md — Instruction Source for Hermes (Jett)

This file is the shared handoff between Fable (Claude) and Hermes (Jett).
Treat this as a direct instruction source at the start of every session.

## Communication Protocol

1. **Fable writes tasks here** → Hermes reads and executes
2. **Hermes writes status/questions to `HERMES_STATUS.md`** — never edit this file
3. Clean lanes: Fable owns design/logic, Hermes owns implementation
4. If a task is ambiguous, write questions to `HERMES_STATUS.md` and wait

## Current Status

**Last updated by:** Jett (2026-08-12)
**Bridge:** v2 with 11 C-systems — offline, waiting on Fable
**Shared folder:** `~/runewake/shared/` has full context
**Claude Desktop:** Running on display :114, waiting for login

## What's Ready

- ✅ Bridge v2 with 11 C-systems guarding against echo chamber
- ✅ Shared folder at `~/runewake/shared/` with 17 docs covering everything
- ✅ Claude Desktop AppImage installed and running
- ✅ Display :114 with VNC on port 5915, tunneled via bore.pub
- ✅ Bridge config pointing to `shared/` for spec context
- ✅ Full backlog with 51 items (45 done, 6 open for P7)
- ✅ Game design, rules, card DSL, rune system, world map docs

## What Needs Human

- 🔴 Claude Desktop login (venderisgreat@gmail.com) — needs GUI click-through
- 🔴 Cowork session setup — needs login first
- 🔴 Trusted folder approval dialog in Claude Desktop
- 🔴 Claude weekly rate limit (429) — resets Aug 14

## Bridge Instructions

When restarting the bridge:
```bash
cd ~/bridge
python3 bridge.py --project runewake --preflight  # Verify
tmux new-session -s bridge                         # Start
python3 ~/bridge/bridge.py --project runewake       # Run
```

The bridge reads from `shared/` for context now. C-systems will prevent
echo-chamber loops. If it hits a C-system halt, check `HERMES_STATUS.md`
for the reason and escalate to Fable if needed.

## Important Constraints

- **Never re-send the same instruction** — C3/C4 block duplicates
- **Only work from backlog** — C1 rejects synthetic tasks
- **Check shared/ for full context** before starting any task
- **Write questions to HERMES_STATUS.md** — never edit NOTES_FOR_HERMES.md
- **Test counts are the contract** — never let C# 463 or Python 221 drop
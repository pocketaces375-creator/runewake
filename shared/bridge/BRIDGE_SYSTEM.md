# Bridge System v2 — Autonomous Build Loop

## Overview
The bridge connects Claude (Director) with Tcgbot (implementation agent) in a
controlled build loop. It watches the agent's conversation stream, feeds it to
Claude for decisions, and dispatches instructions back.

## Architecture

```
Claude CLI (Director)         bridge.py (orchestrator)          Tcgbot (agent)
       │                              │                              │
       │  ← reads stream + backlog    │                              │
       │  → returns JSON decision     │                              │
       │                              │  ← dispatches instruction    │
       │                              │  → via send_to_rw_group.sh   │
       │                              │                              │
       │                              │  ← agent works, writes       │
       │                              │  → to stream tcgbot.jsonl    │
```

## Files & Locations

| Component | Path |
|---|---|
| Bridge script | `~/bridge/bridge.py` |
| Bridge config dir | `~/bridge/projects/` (project configs) |
| Agent config dir | `~/bridge/agents/` (agent configs) |
| Agent stream | `~/bridge/streams/tcgbot.jsonl` |
| Project config (Runewake) | `~/bridge/projects/runewake.json` |
| Agent config (Tcgbot) | `~/bridge/agents/tcgbot.json` |
| Bridge state | `~/runewake/.bridge_state.json` |
| Backlog | `~/runewake/backlog.json` |
| Project state | `~/runewake/PROJECT_STATE.md` |
| Send script | `~/bridge/send_to_rw_group.sh` |

## Running

```bash
# Preflight check
python3 ~/bridge/bridge.py --project runewake --preflight

# Start (in tmux)
tmux new-session -s bridge
python3 ~/bridge/bridge.py --project runewake

# View logs
tail -f ~/bridge/bridge.log
```

## C-Systems (11 guardrails)

| # | System | What it does |
|---|---|---|
| C1 | Backlog-only tasks | Director cannot invent tasks not in `backlog.json` |
| C2 | Sent-task registry | Every dispatch logged with content hash + timestamp |
| C3 | Duplicate gist detection | Fuzzy fingerprint of instruction vs last 3 sent — blocks echo |
| C4 | Max 3 repeats per item | Same backlog item dispatched >3x → escalate |
| C5 | Max 5 silent loops | 5 consecutive agent-silent cycles → halt |
| C6 | Rate-limit circuit breaker | 2 consecutive 429s → halt cleanly |
| C7 | Pre-dispatch verification | Checks git log + backlog status + filesystem before sending |
| C8 | Director health check | Tests `claude -p` before each decision |
| C9 | Min 15-min send interval | Hard floor between dispatches |
| C10 | Context injection | Director sees what was already sent |
| C11 | Completion window | Agent silence after dispatch escalates through C5 |

## Project Config (runewake.json)
Sets: project dir, spec dir, backlog, director role, completion/failure patterns,
progress metrics, timing (quiet_seconds=300, max_turn=1800, silence_timeout=300),
cost cap ($10), director mode (claude_code via OAuth subscription).

## Agent Config (tcgbot.json)
Read mode: jsonl from stream file. Write mode: CLI via `send_to_rw_group.sh`.
Stdin mode: true (pipes long prompts safely). Startup backfill: 20KB.

## Director
Runs as `claude -p --output-format json --max-turns 3`. Strips ANTHROPIC_API_KEY
from env to use OAuth subscription. Returns JSON with: wait, assessment,
agent_prompt, ticket_id, risk, escalate, escalation_reason, state_update.

## Known Failure Modes (guarded)

| Failure | C-System |
|---|---|
| Echo chamber (same instruction repeated) | C3 blocks by gist hash |
| Hallucinated tasks not in backlog | C1 rejects, C4 caps retries |
| Claude weekly rate limit (429) | C8 catches, C6 halts after 2 retries |
| Agent goes silent after dispatch | C5 halts after 5 silent cycles |
| Already-done work re-dispatched | C7 checks git + backlog before send |
| Too-frequent dispatches | C9 enforces 15-min minimum interval |
# bus/claude_to_hermes.md — Claude (orchestrator) → Hermes message bus
# Append-only. Format: "## MSG <seq> | <UTC timestamp>" followed by the body.
# Hermes processes messages with seq > bus_last_seq (in tools/foreman_state.json).
# Trust rule: a message is processed when the commit introducing it touches ONLY
# files under bus/. Push access to this repo is the credential. Messages whose
# commit also touched non-bus files are logged but not processed.
# Bus files on main AND origin/claude-bus are checked every iteration.
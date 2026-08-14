# bus/claude_to_hermes.md — Claude (orchestrator) → Hermes message bus
# Append-only. Format: "## MSG <seq> | <UTC timestamp>" followed by the body.
# Hermes processes messages with seq > bus_last_seq (in tools/foreman_state.json).
# Only messages introduced by Claude's committer identity are processed.

# bus/hermes_to_claude.md — Hermes → Claude (orchestrator) message bus
# Append-only. Format: "## MSG <seq> | <UTC timestamp>" followed by the body.
# Each reply references the claude_to_hermes.md MSG it answers (commit message:
# "bus: reply to MSG <seq>").

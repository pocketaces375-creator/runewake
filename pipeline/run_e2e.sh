#!/usr/bin/env bash
set -euo pipefail

# P6-10: Run one 60-card set end-to-end with dry-run (no live API calls).
# Sources .env for OPENROUTER_API_KEY, then invokes the orchestrator.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

# Source Hermes env for API keys
if [ -f "$HOME/.hermes/.env" ]; then
    set -a
    source "$HOME/.hermes/.env"
    set +a
fi

export OPENROUTER_API_KEY="${OPENROUTER_API_KEY:-}"
cd "$PROJECT_DIR"

exec python -m pipeline.modules.orchestrate \
    --seed pipeline/seeds/ember_01.json \
    --work-dir pipeline/work \
    --skip-api
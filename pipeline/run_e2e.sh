#!/usr/bin/env bash
# P6-10: Run full pipeline end-to-end for the ember_01 seed.
# Sources the Hermes .env for OPENROUTER_API_KEY.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PIPELINE_DIR="$SCRIPT_DIR"

# Source environment for OPENROUTER_API_KEY
if [ -f "$HOME/.hermes/.env" ]; then
    set -a
    source "$HOME/.hermes/.env"
    set +a
fi

if [ -z "${OPENROUTER_API_KEY:-}" ]; then
    echo "ERROR: OPENROUTER_API_KEY is not set. Source your .env or export it."
    exit 1
fi

export OPENROUTER_API_KEY

# Generate a batch ID with timestamp
BATCH_ID="b_e2e_$(date +%Y%m%d_%H%M%S)"
WORK_DIR="$PIPELINE_DIR/work/$BATCH_ID"

echo "[run_e2e] Starting pipeline for ember_01 seed"
echo "[run_e2e] Batch:     $BATCH_ID"
echo "[run_e2e] Work dir:  $WORK_DIR"
echo "[run_e2e] API key:   ${OPENROUTER_API_KEY:0:8}..."

mkdir -p "$WORK_DIR"

exec python -m modules.orchestrate \
    --seed "$PIPELINE_DIR/seeds/ember_01.json" \
    --work-dir "$WORK_DIR" \
    "$@"
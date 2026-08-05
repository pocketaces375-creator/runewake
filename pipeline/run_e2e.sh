#!/usr/bin/env bash
# Pipeline runner that sources the Hermes .env for API keys,
# then delegates to the Python orchestrator.
# Usage: bash pipeline/run_e2e.sh [--stratum EMBER] [--count 60] [--skip-art]
#   Must be run from the runewake project root.

set -e
PROJECT_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

# Source the Hermes environment file for API keys
ENV_FILE="$HOME/.hermes/.env"
if [ -f "$ENV_FILE" ]; then
    # shellcheck disable=SC1090
    source "$ENV_FILE"
    echo "[run_e2e] Sourced API keys from $ENV_FILE" >&2
else
    echo "[run_e2e] WARNING: $ENV_FILE not found — API calls will fail" >&2
fi

export OPENROUTER_API_KEY

# Ensure the C# Sim binary is built
if [ ! -f "$PROJECT_ROOT/sim/bin/Debug/net8.0/Runewake.Sim" ]; then
    echo "[run_e2e] Building C# Sim..." >&2
    dotnet build "$PROJECT_ROOT/sim/Runewake.Sim.csproj" -q
fi

# Run the orchestrator from the pipeline directory
cd "$PROJECT_ROOT/pipeline"
PYTHONPATH=. python -m orchestrator "$@"

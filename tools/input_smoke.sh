#!/usr/bin/env bash
# tools/input_smoke.sh — TASK-INPUT-SMOKE-1: Prove cards are clickable by machine.
# Runs the client headless into a seeded duel, injects InputEventScreenTouch and
# InputEventMouseButton events, and asserts card interaction works.
# Writes result to artifacts/captures/input_smoke_result.json.
# Usage: bash tools/input_smoke.sh
# Returns 0 on PASS, 1 on FAIL.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT_GODOT="$ROOT/client/project.godot"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"
CAPTURE_DIR="$ROOT/artifacts/captures"
RESULT_FILE="$CAPTURE_DIR/input_smoke_result.json"

mkdir -p "$CAPTURE_DIR"
rm -f "$RESULT_FILE"

echo "=== Input Smoke Test ==="

# Set viewport to 2316x1080
sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=2316|" "$PROJECT_GODOT"
sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=1080|" "$PROJECT_GODOT"

# Run the client headless with input smoke test capture mode
timeout 600 xvfb-run -a "$GODOT_BIN" --path client -- "--capture=input_smoke_test" 2>&1 || true

# Restore project.godot
sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=2316|" "$PROJECT_GODOT"
sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=1080|" "$PROJECT_GODOT"

# Check result file
if [ ! -f "$RESULT_FILE" ]; then
    echo "FAIL: input_smoke_result.json not produced" >&2
    exit 1
fi

VERDICT=$(python3 -c "
import json
with open('$RESULT_FILE') as f:
    data = json.load(f)
print(data.get('verdict', 'UNKNOWN'))
")

echo "Result verdict: $VERDICT"

# Print step details
python3 -c "
import json
with open('$RESULT_FILE') as f:
    data = json.load(f)
print('Steps:')
for s in data.get('steps', []):
    print(f'  {s[\"name\"]}: {s[\"result\"]}')
"

if [ "$VERDICT" = "PASS" ]; then
    echo "=== Input Smoke Test PASSED ==="
    exit 0
else
    echo "=== Input Smoke Test FAILED ===" >&2
    exit 1
fi
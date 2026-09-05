#!/usr/bin/env bash
# tools/touch_smoke.sh — TASK-INPUT-TOUCH-1: Prove cards are playable via PURE touch events.
# Runs the client headless into a seeded duel, injects InputEventScreenTouch ONLY
# (no InputEventMouseButton anywhere), and asserts card interaction works.
# Writes result to artifacts/captures/touch_smoke_result.json.
# Usage: bash tools/touch_smoke.sh
# Returns 0 on PASS, 1 on FAIL.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT_GODOT="$ROOT/client/project.godot"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"
CAPTURE_DIR="$ROOT/artifacts/captures"
RESULT_FILE="$CAPTURE_DIR/touch_smoke_result.json"

mkdir -p "$CAPTURE_DIR"
rm -f "$RESULT_FILE"
rm -f "$ROOT/client/artifacts/captures/touch_smoke_result.json"

echo "=== Touch-Only Smoke Test ==="
echo "No mouse events allowed — InputEventScreenTouch ONLY"

# Set viewport to 2316x1080
sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=2316|" "$PROJECT_GODOT"
sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=1080|" "$PROJECT_GODOT"

# Run the client headless with touch smoke test capture mode
timeout 600 xvfb-run -a "$GODOT_BIN" --path client -- "--capture=touch_smoke_test" 2>&1 || true

# Restore project.godot
sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=2316|" "$PROJECT_GODOT"
sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=1080|" "$PROJECT_GODOT"

# Check result file
if [ ! -f "$RESULT_FILE" ]; then
    CLIENT_RESULT="$ROOT/client/artifacts/captures/touch_smoke_result.json"
    if [ -f "$CLIENT_RESULT" ]; then
        cp "$CLIENT_RESULT" "$RESULT_FILE"
        echo "  Copied result from client/artifacts/captures/"
    fi
fi

if [ ! -f "$RESULT_FILE" ]; then
    echo "FAIL: touch_smoke_result.json not produced" >&2
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
    print(f'  {s}')
"

if [ "$VERDICT" = "PASS" ]; then
    echo "=== Touch-Only Smoke Test PASSED ==="
    echo "Every interaction was driven with InputEventScreenTouch — no mouse events."
    exit 0
else
    echo "=== Touch-Only Smoke Test FAILED ===" >&2
    exit 1
fi
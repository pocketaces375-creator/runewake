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
rm -f "$ROOT/client/artifacts/captures/input_smoke_result.json"

echo "=== Input Smoke Test ==="
echo "=== Phase 1: Mouse+Touch Smoke Test ==="

# Set viewport to 2316x1080
sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=2316|" "$PROJECT_GODOT"
sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=1080|" "$PROJECT_GODOT"

# Run the client headless with input smoke test capture mode
timeout 600 xvfb-run -a "$GODOT_BIN" --path client -- "--capture=input_smoke_test" 2>&1 || true

# Restore project.godot
sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=2316|" "$PROJECT_GODOT"
sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=1080|" "$PROJECT_GODOT"

# Check result file — Godot writes relative to --path client, so it may be in client/artifacts/
if [ ! -f "$RESULT_FILE" ]; then
    CLIENT_RESULT="$ROOT/client/artifacts/captures/input_smoke_result.json"
    if [ -f "$CLIENT_RESULT" ]; then
        cp "$CLIENT_RESULT" "$RESULT_FILE"
        echo "  Copied result from client/artifacts/captures/"
    fi
fi

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

echo "Phase 1 result verdict: $VERDICT"

# Print step details
python3 -c "
import json
with open('$RESULT_FILE') as f:
    data = json.load(f)
print('Steps:')
for s in data.get('steps', []):
    print(f'  {s[\"name\"]}: {s[\"result\"]}')
"

if [ "$VERDICT" != "PASS" ]; then
    echo "=== Phase 1 FAILED — stopping, no touch test without working mouse path ===" >&2
    exit 1
fi

echo ""
echo "=== Phase 2: Touch-Only Smoke Test ==="

TOUCH_RESULT_FILE="$CAPTURE_DIR/touch_smoke_result.json"
rm -f "$TOUCH_RESULT_FILE"
rm -f "$ROOT/client/artifacts/captures/touch_smoke_result.json"

timeout 600 xvfb-run -a "$GODOT_BIN" --path client -- "--capture=touch_smoke_test" 2>&1 || true

if [ ! -f "$TOUCH_RESULT_FILE" ]; then
    CLIENT_TOUCH_RESULT="$ROOT/client/artifacts/captures/touch_smoke_result.json"
    if [ -f "$CLIENT_TOUCH_RESULT" ]; then
        cp "$CLIENT_TOUCH_RESULT" "$TOUCH_RESULT_FILE"
        echo "  Copied touch result from client/artifacts/captures/"
    fi
fi

if [ ! -f "$TOUCH_RESULT_FILE" ]; then
    echo "FAIL: touch_smoke_result.json not produced" >&2
    exit 1
fi

TOUCH_VERDICT=$(python3 -c "
import json
with open('$TOUCH_RESULT_FILE') as f:
    data = json.load(f)
print(data.get('verdict', 'UNKNOWN'))
")

echo "Phase 2 result verdict: $TOUCH_VERDICT"

python3 -c "
import json
with open('$TOUCH_RESULT_FILE') as f:
    data = json.load(f)
print('Steps:')
for s in data.get('steps', []):
    print(f'  {s}:')
"

if [ "$TOUCH_VERDICT" = "PASS" ]; then
    echo "=== Input Smoke Test (both phases) PASSED ==="
    exit 0
else
    echo "=== Input Smoke Test FAILED (Phase 2 touch-only) ===" >&2
    exit 1
fi
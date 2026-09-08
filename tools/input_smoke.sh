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


# The smoke client writes its verdict file and then does NOT quit on its own
# (it used to sit until the 600s timeout, which is why finish_task.sh — which
# allows 180s — failed every task for a day). Run it in the background, wait
# for the verdict file, then stop it ourselves.
run_capture_phase() {
    local mode="$1" resname="$2" cap=150 waited=0
    local out="/tmp/smoke_${mode}_$$.log"
    xvfb-run -a "$GODOT_BIN" --path client -- "--capture=${mode}" > "$out" 2>&1 &
    local pid=$!
    while [ "$waited" -lt "$cap" ]; do
        if [ -f "$CAPTURE_DIR/$resname" ] || [ -f "$ROOT/client/artifacts/captures/$resname" ]; then
            sleep 2; break
        fi
        kill -0 "$pid" 2>/dev/null || break
        sleep 2; waited=$((waited+2))
    done
    pkill -TERM -f -- "--capture=${mode}" 2>/dev/null || true
    sleep 1
    pkill -KILL -f -- "--capture=${mode}" 2>/dev/null || true
    wait "$pid" 2>/dev/null || true
    grep -E '^\[' "$out" | tail -25 || true
    echo "  (phase ${mode}: ${waited}s until verdict)"
}

mkdir -p "$CAPTURE_DIR"
rm -f "$RESULT_FILE"
rm -f "$ROOT/client/artifacts/captures/input_smoke_result.json"

echo "=== Input Smoke Test ==="
echo "=== Phase 1: Mouse+Touch Smoke Test ==="

# Set viewport to 2316x1080
sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=2316|" "$PROJECT_GODOT"
sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=1080|" "$PROJECT_GODOT"

# Run the client headless with input smoke test capture mode
run_capture_phase input_smoke_test input_smoke_result.json

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

run_capture_phase touch_smoke_test touch_smoke_result.json

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
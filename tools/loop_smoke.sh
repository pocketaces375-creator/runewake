#!/usr/bin/env bash
# tools/loop_smoke.sh — Full game-loop smoke test.
# Plays a seeded bot-vs-bot duel headless and asserts the game completes.
# Writes result to artifacts/captures/loop_smoke_result.json.
# Usage: bash tools/loop_smoke.sh
# Returns 0 on PASS, 1 on FAIL.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT_GODOT="$ROOT/client/project.godot"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"
CAPTURE_DIR="$ROOT/artifacts/captures"
RESULT_FILE="$CAPTURE_DIR/loop_smoke_result.json"

mkdir -p "$CAPTURE_DIR"
rm -f "$RESULT_FILE"

echo "=== Loop Smoke Test ==="

# Set viewport to 2316x1080
sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=2316|" "$PROJECT_GODOT"
sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=1080|" "$PROJECT_GODOT"

# Run the client headless with loop smoke test capture mode
timeout 600 xvfb-run -a "$GODOT_BIN" --path client -- "--capture=loop_smoke_test" 2>&1 || true

# Restore project.godot
sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=2316|" "$PROJECT_GODOT"
sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=1080|" "$PROJECT_GODOT"

# Check result file
if [ ! -f "$RESULT_FILE" ]; then
    echo "FAIL: loop_smoke_result.json not produced" >&2
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
    echo "=== Loop Smoke Test PASSED ==="
    exit 0
else
    echo "=== Loop Smoke Test FAILED ===" >&2
    exit 1
fi
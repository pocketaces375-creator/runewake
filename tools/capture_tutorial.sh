#!/usr/bin/env bash
set -euo pipefail
# Headless tutorial capture: runs the warrior_intro tutorial at 2316x1080,
# captures each beat, then runs the gate.
# Usage: bash tools/capture_tutorial.sh

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT_GODOT="$ROOT/client/project.godot"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"
CAPTURE_DIR="$ROOT/artifacts/captures"

echo "=== Tutorial Capture: warrior_intro (2316x1080) ==="

# Clean previous tutorial captures
mkdir -p "$CAPTURE_DIR"
rm -f "$CAPTURE_DIR"/tutorial_*.png "$CAPTURE_DIR"/tutorial_*.meta.json

# Set viewport to 2316x1080
sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=2316|" "$PROJECT_GODOT"
sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=1080|" "$PROJECT_GODOT"

# Run tutorial capture
echo "Running tutorial capture..."
timeout 600 xvfb-run -a "$GODOT_BIN" --path client -- "--tutorial=warrior_intro" 2>&1
RC=$?

echo "Tutorial exit code: $RC"

# List produced captures
echo "=== Produced tutorial captures ==="
ls -la "$CAPTURE_DIR"/tutorial_warrior_intro*.png 2>/dev/null || echo "(no per-beat captures found)"
ls -la "$CAPTURE_DIR"/tutorial_warrior_intro.png 2>/dev/null || echo "(no gate capture found)"

# Restore viewport
sed -i "s/^window\\/size\\/viewport_width=.*/window\\/size\\/viewport_width=2316/" "$PROJECT_GODOT"
sed -i "s/^window\\/size\\/viewport_height=.*/window\\/size\\/viewport_height=1080/" "$PROJECT_GODOT"

# Run gate on the tutorial capture
echo ""
echo "=== Capture Gate ==="
python3 "$ROOT/tools/capture_gate.py" tutorial_warrior_intro 2>&1 || {
    echo "FAIL: Tutorial capture gate failed" >&2
    exit 1
}
echo "Tutorial capture gate PASSED"

echo ""
echo "=== Tutorial capture complete ==="
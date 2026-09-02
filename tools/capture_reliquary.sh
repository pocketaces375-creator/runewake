#!/bin/bash
# capture_reliquary.sh — Capture the Reliquary (collection browser) at both resolutions.
set -euo pipefail

PROJECT="/home/fictive/runewake/client"
CAPTURES="/home/fictive/runewake/artifacts/captures"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"

# Ensure captures directory
mkdir -p "$CAPTURES"

# ---- Standard (2316x1080) ----
echo "=== Reliquary standard capture (2316x1080) ==="
# Patch project.godot to standard resolution
sed -i 's|^window/size/viewport_width=.*|window/size/viewport_width=2316|' "$PROJECT/project.godot"
sed -i 's|^window/size/viewport_height=.*|window/size/viewport_height=1080|' "$PROJECT/project.godot"

xvfb-run -a "$GODOT_BIN" --headless --build-solutions \
    --path "$PROJECT" \
    --capture=reliquary_test \
    2>&1 | grep -E '(\[Reliquary|FAIL|Error|ERROR|\[Main\] )' || true

# Png is saved by the script to CAPTURES/reliquary_test.png
if [ -f "$CAPTURES/reliquary_test.png" ]; then
    echo "Standard capture: $(ls -lh "$CAPTURES/reliquary_test.png" | awk '{print $5}')"
else
    echo "FAIL: reliquary_test.png not found"
fi

# ---- Wide (2999x1080) ----
echo ""
echo "=== Reliquary wide capture (2999x1080) ==="
sed -i 's|^window/size/viewport_width=.*|window/size/viewport_width=2999|' "$PROJECT/project.godot"
sed -i 's|^window/size/viewport_height=.*|window/size/viewport_height=1080|' "$PROJECT/project.godot"

xvfb-run -a "$GODOT_BIN" --headless --build-solutions \
    --path "$PROJECT" \
    --capture=reliquary_test_wide \
    2>&1 | grep -E '(\[Reliquary|FAIL|Error|ERROR|\[Main\] )' || true

if [ -f "$CAPTURES/reliquary_test_wide.png" ]; then
    echo "Wide capture: $(ls -lh "$CAPTURES/reliquary_test_wide.png" | awk '{print $5}')"
else
    echo "FAIL: reliquary_test_wide.png not found"
fi

# ---- Restore project.godot to standard resolution ----
sed -i 's|^window/size/viewport_width=.*|window/size/viewport_width=2316|' "$PROJECT/project.godot"
sed -i 's|^window/size/viewport_height=.*|window/size/viewport_height=1080|' "$PROJECT/project.godot"

echo ""
echo "=== Running gate ==="
python3 /home/fictive/runewake/tools/capture_gate.py reliquary_test
echo ""
python3 /home/fictive/runewake/tools/capture_gate.py reliquary_test_wide

echo ""
echo "=== Done ==="
#!/usr/bin/env bash
set -euo pipefail
# Victory/defeat overlay capture: standard + wide for both states.
# Usage: bash tools/capture_victory_defeat.sh

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT_GODOT="$ROOT/client/project.godot"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"
CAPTURE_DIR="$ROOT/artifacts/captures"

# Clean previous captures
mkdir -p "$CAPTURE_DIR"
rm -f "$CAPTURE_DIR"/victory_overlay*.png "$CAPTURE_DIR"/defeat_overlay*.png

capture_one() {
    local mode="$1"       # "victory_overlay" or "defeat_overlay"
    local suffix="$2"     # "" or "_wide"
    local width="$3"
    local height="$4"

    echo "=== Capture: ${mode}${suffix} (${width}x${height}) ==="

    # Patch project.godot viewport
    sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=${width}|" "$PROJECT_GODOT"
    sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=${height}|" "$PROJECT_GODOT"

    # Run capture
    timeout 600 xvfb-run -a "$GODOT_BIN" --path client -- "--capture=${mode}${suffix}" 2>&1
    local rc=$?

    # Verify output
    local outfile="$CAPTURE_DIR/${mode}${suffix}.png"
    if [ -f "$outfile" ]; then
        local pw ph
        read pw ph <<< "$(python3 -c "
import struct
with open('${outfile}','rb') as f:
    f.read(8)
    while True:
        cl = struct.unpack('>I', f.read(4))[0]
        ct = f.read(4)
        cd = f.read(cl)
        f.read(4)
        if ct == b'IHDR':
            w = struct.unpack('>I', cd[0:4])[0]
            h = struct.unpack('>I', cd[4:8])[0]
            print(w, h)
            break
")"
        echo "  -> ${mode}${suffix}.png: ${pw}x${ph}"
        if [ "$pw" -ne "$width" ] || [ "$ph" -ne "$height" ]; then
            echo "  WARNING: Expected ${width}x${height}, got ${pw}x${ph}" >&2
        fi
    else
        echo "  FAIL: ${mode}${suffix}.png not produced" >&2
        rc=1
    fi

    return $rc
}

ALL_PASS=true

# Victory overlay standard
capture_one "victory_overlay" "" 2316 1080 || ALL_PASS=false

# Victory overlay wide
capture_one "victory_overlay" "_wide" 2999 1080 || ALL_PASS=false

# Defeat overlay standard
capture_one "defeat_overlay" "" 2316 1080 || ALL_PASS=false

# Defeat overlay wide
capture_one "defeat_overlay" "_wide" 2999 1080 || ALL_PASS=false

# Restore to standard viewport
sed -i "s/^window\\/size\\/viewport_width=.*/window\\/size\\/viewport_width=2316/" "$PROJECT_GODOT"
sed -i "s/^window\\/size\\/viewport_height=.*/window\\/size\\/viewport_height=1080/" "$PROJECT_GODOT"

echo ""
echo "=== Overlay capture results ==="
echo "victory_overlay:       $($ALL_PASS || [ -f "$CAPTURE_DIR/victory_overlay.png" ] && echo 'PASS' || echo 'FAIL')"
echo "victory_overlay_wide:  $($ALL_PASS || [ -f "$CAPTURE_DIR/victory_overlay_wide.png" ] && echo 'PASS' || echo 'FAIL')"
echo "defeat_overlay:        $($ALL_PASS || [ -f "$CAPTURE_DIR/defeat_overlay.png" ] && echo 'PASS' || echo 'FAIL')"
echo "defeat_overlay_wide:   $($ALL_PASS || [ -f "$CAPTURE_DIR/defeat_overlay_wide.png" ] && echo 'PASS' || echo 'FAIL')"

if $ALL_PASS && [ -f "$CAPTURE_DIR/victory_overlay.png" ] && [ -f "$CAPTURE_DIR/defeat_overlay.png" ]; then
    echo ""
    echo "=== All overlay captures PASSED ==="
    exit 0
else
    echo "Some captures failed" >&2
    exit 1
fi
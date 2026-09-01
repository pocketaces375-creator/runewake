#!/bin/bash
# tools/capture_choose_path.sh — Run ChooseYourPath capture at two resolutions.
# Uses project.godot patch approach like capture_duel.sh.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT_GODOT="$ROOT/client/project.godot"
GODOT_BIN="${GODOT_BIN:-$HOME/.local/bin/godot}"
CAPTURE_DIR="$ROOT/artifacts/captures"

mkdir -p "$CAPTURE_DIR"

capture_one() {
    local suffix="$1"    # "" or "_wide"
    local width="$2"
    local height="$3"

    echo "=== Capture: choose_path${suffix} (${width}x${height}) ==="

    # Patch project.godot viewport
    sed -i "s|^window/size/viewport_width=.*|window/size/viewport_width=${width}|" "$PROJECT_GODOT"
    sed -i "s|^window/size/viewport_height=.*|window/size/viewport_height=${height}|" "$PROJECT_GODOT"

    # Run capture
    xvfb-run -a "$GODOT_BIN" --path client -- "--capture=choose_path${suffix}" 2>&1 || true

    # Verify output
    if [ -f "$CAPTURE_DIR/choose_path${suffix}.png" ]; then
        local pw ph
        read pw ph <<< "$(python3 -c "
import struct
with open('${CAPTURE_DIR}/choose_path${suffix}.png','rb') as f:
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
        echo "  -> choose_path${suffix}.png: ${pw}x${ph}"
        if [ "$pw" -ne "$width" ] || [ "$ph" -ne "$height" ]; then
            echo "  WARNING: Expected ${width}x${height}, got ${pw}x${ph}" >&2
        fi
    else
        echo "  FAIL: choose_path${suffix}.png not produced" >&2
    fi
}

# 1. Standard capture at 2316x1080
capture_one "" 2316 1080

# 2. Wide capture at 2999x1080
capture_one "_wide" 2999 1080

# Restore to standard viewport
sed -i 's|^window/size/viewport_width=.*|window/size/viewport_width=2316|' "$PROJECT_GODOT"
sed -i 's|^window/size/viewport_height=.*|window/size/viewport_height=1080|' "$PROJECT_GODOT"

echo "=== Choose Path captures complete ==="
ls -la "$CAPTURE_DIR"/choose_path*.png 2>/dev/null
#!/bin/bash
# tools/capture_choose_path.sh — Run ChooseYourPath capture at two resolutions.
set -euo pipefail

GODOT="/home/fictive/godot-bin/Godot_v4.3-stable_mono_linux_x86_64/Godot_v4.3-stable_mono_linux.x86_64"
BASE="/home/fictive/runewake"

capture_at() {
    local W=$1 H=$2 TAG=$3
    echo ""
    echo "═══════════════════════════════════════"
    echo "  Capture ${W}x${H} (${TAG})"
    echo "═══════════════════════════════════════"

    killall Xvfb fluxbox 2>/dev/null; sleep 1
    Xvfb :99 -screen 0 ${W}x${H}x24 >/tmp/xvfb.log 2>&1 &
    sleep 1
    export DISPLAY=:99
    fluxbox -display :99 >/tmp/fluxbox.log 2>&1 &
    sleep 1

    # Run Godot — capture flag as user arg after --
    $GODOT --path "$BASE/client" --resolution ${W}x${H} -- --capture=choose_path >/tmp/godot.log 2>&1 &
    GODOT_PID=$!
    wait $GODOT_PID 2>/dev/null || true
    sleep 1

    echo "--- ${TAG}: ChooseYourPath output ---"
    grep -E "\[ChooseYourPath\]|\[VERIFY\]|\[ART-MISSING\]|\[Main\]" /tmp/godot.log || echo "  (no relevant output)"

    # Check capture file
    CAP="$BASE/artifacts/captures/choose_path.png"
    if [ "$TAG" = "wide" ]; then
        CAP="$BASE/artifacts/captures/choose_path_wide.png"
    fi
    if [ -f "$CAP" ]; then
        local SIZE
        SIZE=$(stat --format=%s "$CAP")
        echo "  ✅ Capture saved: $CAP ($SIZE bytes)"
    else
        echo "  ❌ MISSING: $CAP"
    fi

    killall Xvfb fluxbox 2>/dev/null
    sleep 1
}

mkdir -p "$BASE/artifacts/captures"

capture_at 1152 648 "standard"
capture_at 1999 932 "wide"

echo ""
echo "=== DONE ==="
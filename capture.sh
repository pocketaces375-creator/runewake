#!/bin/bash
# Capture duel scene at a given resolution
W=$1
H=$2
OUT=$3
GODOT="/home/fictive/godot-bin/Godot_v4.3-stable_mono_linux_x86_64/Godot_v4.3-stable_mono_linux.x86_64"
PROJ=/home/fictive/runewake/client

killall Xvfb fluxbox 2>/dev/null
sleep 1

Xvfb :99 -screen 0 ${W}x${H}x24 >/tmp/xvfb.log 2>&1 &
XVFB_PID=$!
sleep 1
export DISPLAY=:99
fluxbox -display :99 >/tmp/fluxbox.log 2>&1 &
sleep 1

$GODOT --path $PROJ --resolution ${W}x${H} >/tmp/godot_${W}x${H}.log 2>&1 &
GODOT_PID=$!
sleep 5

scrot "$OUT" 2>/dev/null

kill $GODOT_PID 2>/dev/null
sleep 1
killall Xvfb fluxbox 2>/dev/null

echo "=== Captured $OUT ==="
echo "=== Godot log ==="
grep -E "SetPlayerAttunement|Error|error" /tmp/godot_${W}x${H}.log 2>/dev/null
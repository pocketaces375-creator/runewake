#!/bin/bash
# Capture CardPlate test at given resolution using scrot (window capture)
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

$GODOT --path $PROJ --scene res://scenes/test/CardPlateTest.tscn --resolution ${W}x${H} >/tmp/godot_cp_${W}x${H}.log 2>&1 &
GODOT_PID=$!
sleep 4

scrot "$OUT" 2>/dev/null

kill $GODOT_PID 2>/dev/null
sleep 1
killall Xvfb fluxbox 2>/dev/null

echo "=== Captured $OUT ==="
echo "=== Godot log ==="
grep -E "Error|error|CardPlateTest|Captured" /tmp/godot_cp_${W}x${H}.log 2>/dev/null
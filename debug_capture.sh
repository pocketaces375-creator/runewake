#!/bin/bash
W=$1
H=$2
GODOT="/home/fictive/godot-bin/Godot_v4.3-stable_mono_linux_x86_64/Godot_v4.3-stable_mono_linux.x86_64"

killall Xvfb fluxbox 2>/dev/null; sleep 1
Xvfb :99 -screen 0 ${W}x${H}x24 >/tmp/xvfb.log 2>&1 &
sleep 1
export DISPLAY=:99
fluxbox -display :99 >/tmp/fluxbox.log 2>&1 &
sleep 1

# List windows BEFORE Godot
echo "=== Windows before Godot ==="
xdotool search --onlyvisible --name "" 2>/dev/null | while read id; do
    name=$(xdotool getwindowname "$id" 2>/dev/null)
    echo "Window $id: $name"
done

# Run Godot
$GODOT --path /home/fictive/runewake/client --resolution ${W}x${H} >/tmp/godot.log 2>&1 &
GODOT_PID=$!
sleep 5

# List windows AFTER Godot
echo "=== Windows after Godot ==="
xdotool search --onlyvisible --name "" 2>/dev/null | while read id; do
    name=$(xdotool getwindowname "$id" 2>/dev/null)
    geom=$(xdotool getwindowgeometry "$id" 2>/dev/null | head -3)
    echo "Window $id: $name"
    echo "$geom"
done

# Try xwd on the Godot window
WIN_ID=$(xdotool search --onlyvisible --name "Godot" 2>/dev/null | head -1)
if [ -z "$WIN_ID" ]; then
    WIN_ID=$(xdotool search --onlyvisible --name "Runewake" 2>/dev/null | head -1)
fi
if [ -z "$WIN_ID" ]; then
    # Try any window
    WIN_ID=$(xdotool search --onlyvisible --name "" 2>/dev/null | tail -1)
fi
echo "=== Capturing window $WIN_ID ==="
if [ -n "$WIN_ID" ]; then
    xdotool windowactivate "$WIN_ID" 2>/dev/null
    sleep 1
    xwd -id "$WIN_ID" -out /tmp/capture.xwd 2>/dev/null
    python3 -c "
import struct
with open('/tmp/capture.xwd','rb') as f:
    hdr = f.read(100)
    w = struct.unpack('>I', hdr[4:8])[0] if len(hdr)>=8 else 0
    h = struct.unpack('>I', hdr[8:12])[0] if len(hdr)>=12 else 0
    print(f'XWD dimensions: {w}x{h}')
"
fi

# Whole display scrot
scrot /tmp/scrot.png 2>/dev/null
echo "=== scrot output ==="
python3 -c "
from PIL import Image
import numpy as np
a = np.array(Image.open('/tmp/scrot.png'))
non_black = np.any(a>10, axis=2).sum()
total = a.shape[0]*a.shape[1]
print(f'Dimensions: {a.shape[1]}x{a.shape[0]}')
print(f'Non-black pixels: {non_black}/{total} ({100*non_black/total:.1f}%)')
"

kill $GODOT_PID 2>/dev/null; sleep 1
killall Xvfb fluxbox 2>/dev/null
echo "=== Godot log ==="
cat /tmp/godot.log
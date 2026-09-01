#!/usr/bin/env python3
"""Capture ChooseYourPath at two resolutions using Xvfb + Godot."""
import subprocess, time, os, sys, signal

GODOT = "/home/fictive/godot-bin/Godot_v4.3-stable_mono_linux_x86_64/Godot_v4.3-stable_mono_linux.x86_64"
BASE = "/home/fictive/runewake"
CAP_DIR = os.path.join(BASE, "artifacts", "captures")
os.makedirs(CAP_DIR, exist_ok=True)

def capture_at(w, h, tag):
    print(f"\n{'='*50}")
    print(f"  Capture {w}x{h} ({tag})")
    print(f"{'='*50}")
    
    # Kill any leftover Xvfb
    subprocess.run(["killall", "Xvfb", "fluxbox"], capture_output=True)
    time.sleep(1)
    
    # Start Xvfb
    xvfb = subprocess.Popen(
        ["Xvfb", ":99", "-screen", "0", f"{w}x{h}x24"],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL
    )
    time.sleep(1)
    os.environ["DISPLAY"] = ":99"
    
    # Start fluxbox
    fluxbox = subprocess.Popen(
        ["fluxbox", "-display", ":99"],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL
    )
    time.sleep(1)
    
    # Run Godot with capture flag
    cap_arg = "--capture=choose_path_wide" if tag == "wide" else "--capture=choose_path"
    godot_proc = subprocess.Popen(
        [GODOT, "--path", f"{BASE}/client", "--resolution", f"{w}x{h}", "--", cap_arg],
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        text=True
    )
    
    try:
        stdout, _ = godot_proc.communicate(timeout=30)
        rc = godot_proc.returncode
    except subprocess.TimeoutExpired:
        godot_proc.kill()
        stdout, _ = godot_proc.communicate()
        rc = -1
        print("  TIMEOUT")
    
    # Extract relevant output
    for line in stdout.splitlines():
        if any(x in line for x in ["[ChooseYourPath]", "[VERIFY]", "[ART-MISSING]", "[Main]", "[DebugCapture]"]):
            print(f"  {line}")
    
    # Check capture file
    cap = os.path.join(CAP_DIR, "choose_path_wide.png" if tag == "wide" else "choose_path.png")
    if os.path.exists(cap):
        size = os.path.getsize(cap)
        print(f"  ✅ Capture: {cap} ({size} bytes)")
    else:
        print(f"  ❌ MISSING: {cap}")
    
    # Cleanup
    xvfb.terminate()
    fluxbox.terminate()
    time.sleep(0.5)
    subprocess.run(["killall", "Xvfb", "fluxbox"], capture_output=True)

capture_at(2316, 1080, "standard")
capture_at(2999, 1080, "wide")
print("\n=== DONE ===")
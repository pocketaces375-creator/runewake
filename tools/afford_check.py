#!/usr/bin/env python3
"""B3: Run 200 headless openings, count how many have no card <= 1."""
import subprocess, sys, re, os, time

GODOT = os.path.expanduser("~/.local/bin/godot")
ROOT = os.path.expanduser("~/runewake")
os.chdir(ROOT)

no_affordable = 0
total = 0
# Use different seeds for each run
base_seed = 42

for i in range(200):
    seed = base_seed + i
    # Timing each run - use 2s timeout, if it's still running kill it after that
    try:
        proc = subprocess.run(
            ["timeout", "15", "xvfb-run", "-a", GODOT, "--path", "client", "--",
             f"--capture=duel_test", f"--seed={seed}"],
            capture_output=True, text=True, timeout=20)
        output = proc.stdout
    except subprocess.TimeoutExpired:
        continue
    
    # Parse hand cards from render log
    # Look for [HAND] or [TURN] no affordable cards line
    turn_lines = [l for l in output.split('\n') if '[TURN]' in l and 'no affordable' in l]
    hand_lines = [l for l in output.split('\n') if l.startswith('[HAND]')]
    
    if turn_lines:
        no_affordable += 1
    elif not hand_lines:
        total -= 1  # skip - didn't reach the duel
        continue
    
    total += 1
    if (i + 1) % 50 == 0:
        print(f"Progress: {i+1}/200, no_affordable so far: {no_affordable}")

print(f"\nB3 Result: {no_affordable}/{total} openings had no card <= 1 cost at turn 1")
if total > 0:
    print(f"Percentage: {100.0 * no_affordable / total:.1f}%")
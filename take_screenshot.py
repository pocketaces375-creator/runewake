#!/usr/bin/env python3
"""Take a screenshot of the running Godot window and save it."""
import subprocess, sys, os, time
result = subprocess.run(
    ["scrot", "-o", sys.argv[1]],
    env={**os.environ, "DISPLAY": ":99"},
    capture_output=True, text=True
)
if result.returncode != 0:
    print(f"scrot failed: {result.stderr}")
    sys.exit(1)
print(f"Screenshot saved to {sys.argv[1]}")
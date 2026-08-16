#!/usr/bin/env python3
"""Generate 3 Wildfire Adept style probe variants for ART-STYLE-3."""
import os, sys, subprocess, time
from PIL import Image

SCRIPT = "pipeline/gen_image_openrouter.py"
OUT_DIR = "pipeline/work/style_probe_s1"
NEG = (
    "digital airbrush, photorealism, 3D render, sci-fi elements, lens flare, text, "
    "typography, lettering, title text, watermark, logo, writing, captions, words, "
    "artist signature, painted signature, autograph, smooth digital rendering, "
    "glossy CGI, game splash art, airbrushed gradient, monochrome orange, "
    "oversaturated, cluttered composition, debris field, excessive sparks, "
    "heavy metal album art, subject filling entire frame, any letters or words"
)
BASE = (
    "oil painting in the style of classical storybook illustration, visible brushwork, "
    "single focal subject composed with breathing room, atmospheric depth with softly "
    "rendered distant background, warm light against cool shadow, restrained palette "
    "with selective vivid accents, painted by hand, unsigned artwork. "
    "charcoal greys and cool slate shadows, lit by molten orange and gold flame accents. "
    "A Wildfire Adept — a robed spellcaster with one hand wreathed in uncontrolled flame, "
    "standing amid scorched earth, sparks dancing through the air, face lit from below by her own fire"
)
DIFFS = {
    "A": "chiaroscuro, Rembrandt lighting, dark composed background, quiet drama",
    "B": "expressive swirling brushstrokes, Van Gogh energy, cool blue-grey night sky contrasting the flame",
    "C": "Renaissance tableau composition, muted earth tones, figure small within a grand painted scene",
}

os.makedirs(OUT_DIR, exist_ok=True)

env_path = os.path.expanduser("~/.hermes/.env")
key = None
with open(env_path) as f:
    for line in f:
        if line.startswith("OPENROUTER_API_KEY"):
            key = line.strip().split("=", 1)[1].strip()
            break
if not key:
    print("FATAL: No API key")
    sys.exit(1)

my_env = os.environ.copy()
my_env["OPENROUTER_API_KEY"] = key

for letter, diff in DIFFS.items():
    prompt = f"{BASE}. {diff}. — {NEG}"
    out = os.path.join(OUT_DIR, f"wildfire_{letter}.jpg")
    print(f"[{letter}/3] Generating {out}...")

    t0 = time.time()
    ret = subprocess.run(
        [sys.executable, SCRIPT, prompt, out],
        capture_output=True, text=True, timeout=300, env=my_env,
    )
    elapsed = time.time() - t0

    if ret.returncode == 0 and os.path.exists(out):
        img = Image.open(out)
        sz = os.path.getsize(out)
        cost = 0.036
        print(f"  SAVED: {sz/1024:.0f} KB, {img.size[0]}x{img.size[1]}, ${cost:.3f}, {elapsed:.0f}s")
    else:
        print(f"  FAILED (exit {ret.returncode})")
        if ret.stderr:
            print(f"  stderr: {ret.stderr[:300]}")
        sys.exit(1)

    time.sleep(2)

print("\nALL 3 STYLE PROBES GENERATED")
for f in sorted(os.listdir(OUT_DIR)):
    if f.endswith(".jpg"):
        path = os.path.join(OUT_DIR, f)
        img = Image.open(path)
        print(f"  {f}: {img.size[0]}x{img.size[1]}, {os.path.getsize(path)/1024:.0f} KB")
#!/usr/bin/env python3
"""WAVE 1 — Ember samples in v3.0 blended style (Trikzos ruled 2026-08-17).
6 samples → pipeline/work/samples_ember_s3/.
Includes RULE (8) corner check for painted signatures/lettering.
FLUX.2 Pro, 832x1216 portrait."""
import subprocess, sys, os, time, struct, zlib
from pathlib import Path

SCRIPT = os.path.join(os.path.dirname(__file__), '..', 'gen_image_openrouter.py')
OUT_DIR = os.path.join(os.path.dirname(__file__), 'samples_ember_s3')
MODEL = "black-forest-labs/flux.2-pro"

# Read API key
env_path = os.path.expanduser('~/.hermes/.env')
api_key = None
with open(env_path) as f:
    for line in f:
        if line.startswith('OPENROUTER_API_KEY='):
            api_key = line.strip().split('=', 1)[1]
            break
if not api_key:
    print("FATAL: OPENROUTER_API_KEY not found")
    sys.exit(1)
my_env = os.environ.copy()
my_env['OPENROUTER_API_KEY'] = api_key

# v3.0 BLENDED SPINE (Trikzos ruled 2026-08-17, FINAL style lock)
STYLE_SPINE = (
    "oil painting in the style of classical storybook illustration, "
    "dramatic painted light against deep shadow (chiaroscuro), "
    "swirling expressive brushwork reserved for skies, smoke, and magical energy, "
    "single grounded focal subject staged with breathing room "
    "in the manner of a Renaissance tableau, "
    "atmospheric depth with softly rendered distant background, "
    "restrained palette with selective vivid accents, "
    "thick impasto texture, painted by hand, unsigned artwork"
)
STYLE_NEG = (
    "digital airbrush, photorealism, 3D render, sci-fi elements, lens flare, text, "
    "typography, lettering, title text, watermark, logo, writing, captions, words, "
    "artist signature, painted signature, autograph, smooth digital rendering, "
    "glossy CGI, game splash art, airbrushed gradient, "
    "monochrome orange, oversaturated, cluttered composition, "
    "debris field, excessive sparks, heavy metal album art, "
    "subject filling entire frame, any letters or words"
)

# Ember palette: "charcoal greys and cool slate shadows, lit by molten orange and gold flame accents"
# NEVER use the stratum name "EMBER" in the prompt.

prompts = [
    (
        "01_emb_c_flame_javelin.jpg",
        f"{STYLE_SPINE}. "
        "charcoal greys and cool slate shadows, lit by molten orange and gold flame accents. "
        "A Flame Javelin — a spear of living fire hurled from the heart of a forge, "
        "trailing embers and smoke, blazing against a dark volcanic sky. "
        "High fantasy ritual magic. — {STYLE_NEG}"
    ),
    (
        "02_emb_u_wildfire_adept.jpg",
        f"{STYLE_SPINE}. "
        "charcoal greys and cool slate shadows, lit by molten orange and gold flame accents. "
        "A Wildfire Adept — a robed spellcaster with one hand wreathed in uncontrolled flame, "
        "standing amid scorched earth, sparks dancing through the air, "
        "face lit from below by her own fire. "
        "High fantasy. — {STYLE_NEG}"
    ),
    (
        "03_emb_u_lava_serpent.jpg",
        f"{STYLE_SPINE}. "
        "charcoal greys and cool slate shadows, lit by molten orange and gold flame accents. "
        "A Lava Serpent — a massive serpentine creature with molten cracks "
        "running through obsidian scales, coiling through rivers of liquid fire, "
        "sparks cascading from its body. "
        "High fantasy. — {STYLE_NEG}"
    ),
    (
        "04_emb_u_cinderstorm_elemental.jpg",
        f"{STYLE_SPINE}. "
        "charcoal greys and cool slate shadows, lit by molten orange and gold flame accents. "
        "A Cinderstorm Elemental — a towering vortex of burning cinders and smoke, "
        "humanoid shape barely visible within the cyclone of flame, "
        "eyes glowing like forge-coals. "
        "High fantasy elemental fury. — {STYLE_NEG}"
    ),
    (
        "05_emb_r_phoenix_ash.jpg",
        f"{STYLE_SPINE}. "
        "charcoal greys and cool slate shadows, lit by molten orange and gold flame accents. "
        "Phoenix Ash — a majestic phoenix rising from a pile of smoldering ashes, "
        "wings spread wide, ember-colored feathers trailing smoke, "
        "reborn in a burst of warm golden light. "
        "High fantasy. — {STYLE_NEG}"
    ),
    (
        "06_emb_x_the_last_ember.jpg",
        f"{STYLE_SPINE}. "
        "charcoal greys and cool slate shadows, lit by molten orange and gold flame accents. "
        "The Last Ember — a single glowing coal cradled in cupped obsidian hands, "
        "the only remaining spark in a vast, cold darkness, tiny embers floating upward. "
        "High fantasy relic, melancholic. — {STYLE_NEG}"
    ),
]


# ── RULE (8) corner check ──────────────────────────────────────────────────────
# Check all four corners of an image for painted signatures/lettering.
# Strategy: sample edge regions — if there's high-contrast non-background
# structure in a corner, flag it as potential lettering.

def check_rule8(path):
    """Check image for corner lettering. Returns (ok, message)."""
    try:
        from PIL import Image
        img = Image.open(path).convert("RGB")
        w, h = img.size
        # Check 40x40px regions in each corner
        corner_size = min(40, w // 8, h // 8)
        corners = {
            "top-left": (0, 0, corner_size, corner_size),
            "top-right": (w - corner_size, 0, w, corner_size),
            "bottom-left": (0, h - corner_size, corner_size, h),
            "bottom-right": (w - corner_size, h - corner_size, w, h),
        }
        issues = []
        for name, (x1, y1, x2, y2) in corners.items():
            pixels = []
            for py in range(y1, y2):
                for px in range(x1, x2):
                    r, g, b = img.getpixel((px, py))
                    pixels.append(0.2126 * r + 0.7152 * g + 0.0722 * b)
            if not pixels:
                continue
            n = len(pixels)
            mean = sum(pixels) / n
            var = sum((p - mean) ** 2 for p in pixels) / n
            std = var ** 0.5
            # High std dev in a corner = likely lettering/signature
            if std > 30:
                issues.append(f"{name} std={std:.1f}")
        if issues:
            return False, "RULE(8) corner check: " + "; ".join(issues)
        return True, "RULE(8) corner check: clean"
    except Exception as e:
        return False, f"RULE(8) check error: {e}"


# ── Main ───────────────────────────────────────────────────────────────────────

os.makedirs(OUT_DIR, exist_ok=True)

print(f"{'='*60}")
print(f"WAVE 1 — Ember samples (v3.0 style) — {len(prompts)} cards")
print(f"FLUX.2 Pro, 832x1216 portrait")
print(f"Estimated cost: ${len(prompts) * 0.036:.2f}")
print(f"{'='*60}")

regenerations = []
total_cost = 0.0

for i, (filename, prompt) in enumerate(prompts, 1):
    out_path = os.path.join(OUT_DIR, filename)
    print(f"\n[{i}/{len(prompts)}] {filename}")

    # Try up to 2 times (initial + 1 RULE 8 regeneration)
    for attempt in range(1, 3):
        print(f"  Attempt {attempt}...")
        start = time.time()
        ret = subprocess.run(
            [sys.executable, SCRIPT, prompt, out_path],
            capture_output=True, text=True, timeout=300, env=my_env,
        )
        elapsed = time.time() - start

        if ret.returncode != 0 or not os.path.exists(out_path) or os.path.getsize(out_path) < 1000:
            print(f"  ✗ API FAILED (exit {ret.returncode})")
            print(f"    stderr: {ret.stderr[:300]}")
            break

        size = os.path.getsize(out_path)
        try:
            from PIL import Image
            img = Image.open(out_path)
            dims = f"{img.size[0]}x{img.size[1]}"
        except Exception:
            dims = "unknown"
        cost = 0.036
        total_cost += cost
        print(f"  ✓ Saved ({size/1024:.0f} KB, {dims}, ${cost:.3f}, {elapsed:.0f}s)")

        # RULE 8 check
        r8_ok, r8_msg = check_rule8(out_path)
        if r8_ok:
            print(f"  ✓ {r8_msg}")
            break  # exit retry loop — image is clean
        else:
            print(f"  ⚠ {r8_msg}")
            if attempt == 1:
                regenerations.append(filename)
                print(f"  → Regenerating with reinforced negatives...")
                # Reinforce — add extra negatives to the prompt
                prompt = (prompt.rstrip(" .\"')") +
                    ". Absolutely NO text, NO letters, NO signatures anywhere in the image.")
            else:
                # Second attempt still has issues — post anyway, flag it
                print(f"  → PERSISTS after regeneration — will flag in Telegram caption")
                break

    time.sleep(2)  # rate limit

# ── Summary ──
print(f"\n{'='*60}")
print(f"WAVE 1 COMPLETE")
print(f"{'='*60}")
print(f"  Output: {OUT_DIR}")
all_ok = True
for f in sorted(os.listdir(OUT_DIR)):
    if f.endswith('.jpg'):
        path = os.path.join(OUT_DIR, f)
        sz = os.path.getsize(path)
        try:
            from PIL import Image
            img = Image.open(path)
            dims = f"{img.size[0]}x{img.size[1]}"
        except Exception:
            dims = "unknown"
        ok = "✓" if os.path.getsize(path) > 1000 else "✗"
        if ok == "✗":
            all_ok = False
        r8_ok, r8_msg = check_rule8(path) if ok == "✓" else (False, "file too small")
        r8_label = "✓" if r8_ok else "⚠"
        print(f"    {ok} {f} ({sz/1024:.0f} KB, {dims}) [R8: {r8_label}]")
        if not r8_ok and ok == "✓":
            print(f"         {r8_msg}")
if regenerations:
    print(f"  RULE 8 regenerations: {len(regenerations)} — {', '.join(regenerations)}")
else:
    print(f"  RULE 8 regenerations: 0 — all clean")
print(f"  Total cost: ${total_cost:.2f}")
print(f"  All OK: {all_ok}")
sys.exit(0 if all_ok else 1)
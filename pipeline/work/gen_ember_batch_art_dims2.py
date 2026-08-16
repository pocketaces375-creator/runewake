#!/usr/bin/env python3
"""Generate all 6 Ember samples via OpenRouter/FLUX pipeline script.
Uses 832x1216 portrait size and strengthened painterly prompt per ART-DIMS-2."""
import subprocess, sys, os, time, json

SCRIPT = os.path.join(os.path.dirname(__file__), '..', 'gen_image_openrouter.py')
OUT_DIR = os.path.join(os.path.dirname(__file__), 'samples_ember_s2')
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

# Set env for subprocess
my_env = os.environ.copy()
my_env['OPENROUTER_API_KEY'] = api_key

# ART_STYLE_SPEC v1.0 prompt spine + negatives:
# "oil painting, visible brushwork, storybook illustration, classical composition,
#  painted light, thick impasto brushstrokes, visible canvas texture, painted by hand,
#  style of Bloomweaver and Thornbark Defender"
# + element palette + card subject
# Negatives: "digital airbrush, photorealism, 3D render, sci-fi elements, lens flare,
#  text, typography, lettering, title text, watermark, logo, writing, captions, words,
#  artist signature, painted signature, autograph, smooth digital rendering, glossy CGI,
#  game splash art, airbrushed gradient"

STYLE_LEAD = (
    "oil painting, visible brushwork, storybook illustration, classical composition, "
    "painted light, thick impasto brushstrokes, visible canvas texture, painted by hand, "
    "style of Bloomweaver and Thornbark Defender. "
)
STYLE_NEG = (
    "digital airbrush, photorealism, 3D render, sci-fi elements, lens flare, text, "
    "typography, lettering, title text, watermark, logo, writing, captions, words, "
    "artist signature, painted signature, autograph, smooth digital rendering, "
    "glossy CGI, game splash art, airbrushed gradient"
)

prompts = [
    (
        "01_emb_c_flame_javelin.jpg",
        f"{STYLE_LEAD}"
        "EMBER: Dark fantasy oil painting, ash and ember palette, soot-black and molten orange, "
        "heavy impasto, dramatic rim light. "
        "A Flame Javelin — a spear of living fire hurled from the heart of a forge, "
        "trailing embers and smoke, blazing against a dark volcanic sky. "
        "High fantasy ritual magic. — {STYLE_NEG}"
    ),
    (
        "02_emb_u_wildfire_adept.jpg",
        f"{STYLE_LEAD}"
        "EMBER: Dark fantasy oil painting, ash and ember palette, soot-black and molten orange, "
        "heavy impasto, dramatic rim light. "
        "A Wildfire Adept — a robed spellcaster with one hand wreathed in uncontrolled flame, "
        "standing amid scorched earth, sparks dancing through the air, face lit from below by her own fire. "
        "High fantasy. — {STYLE_NEG}"
    ),
    (
        "03_emb_u_lava_serpent.jpg",
        f"{STYLE_LEAD}"
        "EMBER: Dark fantasy oil painting, ash and ember palette, soot-black and molten orange, "
        "heavy impasto, dramatic rim light. "
        "A Lava Serpent — a massive serpentine creature with molten cracks running through obsidian scales, "
        "coiling through rivers of liquid fire, sparks cascading from its body. "
        "High fantasy. — {STYLE_NEG}"
    ),
    (
        "04_emb_u_cinderstorm_elemental.jpg",
        f"{STYLE_LEAD}"
        "EMBER: Dark fantasy oil painting, ash and ember palette, soot-black and molten orange, "
        "heavy impasto, dramatic rim light. "
        "A Cinderstorm Elemental — a towering vortex of burning cinders and smoke, "
        "humanoid shape barely visible within the cyclone of flame, eyes glowing like forge-coals. "
        "High fantasy elemental fury. — {STYLE_NEG}"
    ),
    (
        "05_emb_r_phoenix_ash.jpg",
        f"{STYLE_LEAD}"
        "EMBER: Dark fantasy oil painting, ash and ember palette, soot-black and molten orange, "
        "heavy impasto, dramatic rim light. "
        "Phoenix Ash — a majestic phoenix rising from a pile of smoldering ashes, "
        "wings spread wide, ember-colored feathers trailing smoke, reborn in a burst of warm golden light. "
        "High fantasy. — {STYLE_NEG}"
    ),
    (
        "06_emb_x_the_last_ember.jpg",
        f"{STYLE_LEAD}"
        "EMBER: Dark fantasy oil painting, ash and ember palette, soot-black and molten orange, "
        "heavy impasto, dramatic rim light. "
        "The Last Ember — a single glowing coal cradled in cupped obsidian hands, "
        "the only remaining spark in a vast, cold darkness, tiny embers floating upward. "
        "High fantasy relic, melancholic. — {STYLE_NEG}"
    ),
]

os.makedirs(OUT_DIR, exist_ok=True)

print(f"{'='*60}")
print(f"Generating {len(prompts)} Ember samples (832x1216 portrait, painterly prompt)")
print(f"Estimated cost: ${len(prompts) * 0.036:.2f}")
print(f"{'='*60}")

total_cost = 0.0
for i, (filename, prompt) in enumerate(prompts, 1):
    out_path = os.path.join(OUT_DIR, filename)
    print(f"\n[{i}/{len(prompts)}] {filename}")
    print(f"  Prompt: {prompt[:150]}...")

    start = time.time()
    ret = subprocess.run(
        [sys.executable, SCRIPT, prompt, out_path],
        capture_output=True, text=True, timeout=300, env=my_env,
    )
    elapsed = time.time() - start

    if ret.returncode == 0 and os.path.exists(out_path):
        size = os.path.getsize(out_path)
        # Check dimensions
        try:
            from PIL import Image
            img = Image.open(out_path)
            dims = f"{img.size[0]}x{img.size[1]}"
        except Exception:
            dims = "unknown"
        cost = 0.036
        total_cost += cost
        print(f"  ✓ {filename} saved ({size/1024:.0f} KB, {dims}, ${cost:.3f}, {elapsed:.0f}s)")
    else:
        print(f"  ✗ FAILED (exit {ret.returncode})")
        print(f"    stderr: {ret.stderr[:300]}")
        # Don't fail fast — try the rest

    time.sleep(2)  # rate limit

print(f"\n{'='*60}")
print(f"EMBER SAMPLE GENERATION COMPLETE")
print(f"{'='*60}")
print(f"  Files: {OUT_DIR}")
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
        print(f"    {ok} {f} ({sz/1024:.0f} KB, {dims})")
print(f"  Total cost: ${total_cost:.2f}")
print(f"  All OK: {all_ok}")
sys.exit(0 if all_ok else 1)
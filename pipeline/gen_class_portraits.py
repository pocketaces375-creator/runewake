#!/usr/bin/env python3
"""Generate all 7 class portraits via FLUX.2 Pro."""
import subprocess, sys, os, time

# The v3.x painterly style prefix (common to all, no stratum names)
STYLE = (
    "oil painting in the style of classical storybook illustration, "
    "dramatic painted light against deep shadow, "
    "swirling expressive brushwork, "
    "single grounded focal subject with breathing room, "
    "atmospheric depth with softly rendered distant background, "
    "thick impasto texture, painted by hand, unsigned artwork. "
)

PORTRAITS = [
    ("warrior", "An armored forge-warrior wreathed in ember light and drifting ash, heavy plate armor, orange glow, dark forge background"),
    ("necromancer", "A pale robed necromancer amid bone-lanterns, gaunt face, skeletal hands, purple-black shadows, bone-white light"),
    ("druid", "An antlered druid grown into living roots and moss, bark-textured skin, deep forest greens, earthy browns, golden highlights"),
    ("tidecaller", "A tide-cloaked scholar with luminous water coils swirling around them, deep blue-teal depths, pale foam edges, sea-green light"),
    ("dawnward", "A radiant knight-warden at first light, polished golden armor, warm cream and pale gold tones, soft amber dawn light"),
    ("ranger", "A hooded wildland archer half in green forest shade, half in gold morning light, drawn bow, treeline horizon"),
    ("occultist", "A gaunt seer wrapped in dripping votive cloth, pale candles reflected in dark water, murky violet shadows, bone-white highlights"),
]

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(HERE, "gen_image_openrouter.py")
OUT_DIR = os.path.join(HERE, "..", "client", "content", "art", "classes")

# Source the env
subprocess.run('bash -c "source ~/.hermes/profiles/tcgbot/.env; env" > /tmp/hermes_env', shell=True)

os.makedirs(OUT_DIR, exist_ok=True)

procs = []
for class_id, subject in PORTRAITS:
    prompt = STYLE + subject
    out_path = os.path.join(OUT_DIR, f"{class_id}.png")
    print(f"Generating {class_id} -> {out_path}")
    print(f"  Prompt: {prompt[:100]}...")
    
    p = subprocess.Popen(
        ["python3", SCRIPT, prompt, out_path, "--model", "black-forest-labs/flux.2-pro", "--width", "832", "--height", "1216"],
        env={**os.environ, **{k: v for line in open("/tmp/hermes_env") if "=" in line for k, v in [line.strip().split("=", 1)]}},
        stdout=subprocess.PIPE, stderr=subprocess.PIPE
    )
    procs.append((class_id, p))
    time.sleep(1)  # slight stagger

# Wait for all
results = []
for class_id, p in procs:
    out, err = p.communicate(timeout=180)
    out_text = out.decode() if out else ""
    err_text = err.decode() if err else ""
    ok = p.returncode == 0
    results.append((class_id, ok, out_text, err_text))
    print(f"\n=== {class_id} ===\n{out_text}\n{err_text[:200] if err_text else ''}")

print("\n\n=== SUMMARY ===")
for class_id, ok, _, _ in results:
    out_path = os.path.join(OUT_DIR, f"{class_id}.png")
    size = os.path.getsize(out_path) if os.path.exists(out_path) else 0
    print(f"  {class_id}: {'OK' if ok else 'FAIL'} ({size} bytes)")
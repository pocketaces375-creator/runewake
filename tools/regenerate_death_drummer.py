#!/usr/bin/env python3
"""Regenerate hol_r_death_drummer — was flagged for violence. Gentler prompt."""
import subprocess, sys, os, time

SCRIPT = os.path.join(os.path.dirname(__file__), '..', 'pipeline', 'gen_image_openrouter.py')
OUT = os.path.join(os.path.dirname(__file__), '..', 'client', 'content', 'art', 'hol_r_death_drummer.webp')

PROMPT = (
    "Ancient ceremonial armour the colour of aged bone, seated on a stone throne before a war-drum, "
    "the drum's head of stretched hide, drumsticks raised by unseen hands, a soft green glow "
    "emanating from the drum's hollow. "
    "oil painting in the style of classical storybook illustration, "
    "dramatic painted light against deep shadow (chiaroscuro), "
    "swirling expressive brushwork, "
    "single grounded focal subject staged with breathing room "
    "in the manner of a Renaissance tableau, "
    "atmospheric depth with softly rendered distant background, "
    "restrained palette with selective vivid accents, "
    "thick impasto texture, painted by hand, unsigned artwork. "
    "Bone white #D8CEBB with murky violet #3A2B45 and sickly green #6E7F4A. "
    "Crumbling barrow chamber, bone-littered floor, cold mist, "
    "empty background falling to shadow. "
    "Full figure, shot on a 50mm lens at f/2.8, low eye-level, rule of thirds."
)

env = os.environ.copy()
env_path = os.path.expanduser('~/.hermes/.env')
with open(env_path) as f:
    for line in f:
        if line.startswith('OPENROUTER_API_KEY='):
            env['OPENROUTER_API_KEY'] = line.strip().split('=', 1)[1]
            break

print(f"Regenerating hol_r_death_drummer...")
print(f"Prompt: {PROMPT[:100]}...")
ret = subprocess.run(
    [sys.executable, SCRIPT, PROMPT, OUT, "--model", "black-forest-labs/flux.2-pro",
     "--width", "832", "--height", "1216"],
    capture_output=True, text=True, timeout=300, env=env
)

if ret.returncode == 0 and os.path.exists(OUT) and os.path.getsize(OUT) > 1000:
    size = os.path.getsize(OUT)
    print(f"SUCCESS: {OUT} ({size/1024:.0f} KB)")
    
    # Create .import
    import_path = OUT + ".import"
    import_content = f"""[remap]

importer="texture"
type="CompressedTexture2D"
uid="uid://holrdeathdrummer"
path="res://.godot/imported/hol_r_death_drummer.webp-hol_r_death_drummer_placeholder.ctex"
metadata={{
"vram_texture": false
}}

[deps]

source_file="res://content/art/hol_r_death_drummer.webp"
dest_files=["res://.godot/imported/hol_r_death_drummer.webp-hol_r_death_drummer_placeholder.ctex"]

[params]

compress/mode=0
compress/high_quality=true
compress/lossy_quality=0.7
compress/hdr_compression=1
compress/normal
"""
    with open(import_path, "w") as f:
        f.write(import_content)
    print(f"Created: {import_path}")
    sys.exit(0)
else:
    print(f"FAILED: exit={ret.returncode}")
    if ret.stderr:
        print(f"stderr: {ret.stderr[:500]}")
    sys.exit(1)
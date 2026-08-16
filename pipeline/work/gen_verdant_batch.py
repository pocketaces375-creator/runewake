#!/usr/bin/env python3
"""Generate all 6 Verdant samples via OpenRouter/FLUX pipeline script."""
import subprocess, sys, os, time, json

SCRIPT = os.path.join(os.path.dirname(__file__), '..', 'gen_image_openrouter.py')
OUT_DIR = os.path.join(os.path.dirname(__file__), 'samples_verdant_s1')
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

prompts = [
    (
        "01_vrd_c_thornbark_defender.jpg",
        "oil painting, visible brushwork, storybook illustration, classical composition, painted light. "
        "A Thronbark Defender — a thick-limbed treant creature covered in jagged bark and thorny vines, "
        "standing guard in an ancient sun-dappled woodland clearing. High fantasy, figure integrated "
        "into a living detailed environment. Verdant green and gold palette. Renaissance staging, painted light, "
        "Van Gogh-adjacent stroke energy. — digital airbrush, photorealism, 3D render, sci-fi elements, lens flare, text, typography, lettering, title text, watermark, words, captions"
    ),
    (
        "02_vrd_c_wildwood_stalker.jpg",
        "oil painting, visible brushwork, storybook illustration, classical composition, painted light. "
        "A Wildwood Stalker — a feline predator with fur patterned like bark and lichen, crouching low "
        "in dappled forest undergrowth, eyes gleaming with amber light. High fantasy, figure integrated "
        "into a living detailed environment. Verdant green and gold palette. — digital airbrush, photorealism, 3D render, sci-fi elements, lens flare, text, typography, lettering, title text, watermark, words, captions"
    ),
    (
        "03_vrd_u_canopy_archer.jpg",
        "oil painting, visible brushwork, storybook illustration, classical composition, painted light. "
        "A Canopy Archer — an elven figure in moss-green cloak perched on a giant branch, bow drawn, "
        "aiming through a gap in the leaves, morning mist below. High fantasy, figure integrated "
        "into a living detailed environment. Verdant green and gold palette. — digital airbrush, photorealism, 3D render, sci-fi elements, lens flare, text, typography, lettering, title text, watermark, words, captions"
    ),
    (
        "04_vrd_u_elder_treant.jpg",
        "oil painting, visible brushwork, storybook illustration, classical composition, painted light. "
        "An Elder Treant — ancient walking tree with a wise face in its trunk, mossy arms spread wide, "
        "standing in a forest glade where sunbeams pierce the canopy. High fantasy, figure integrated "
        "into a living detailed environment. Verdant green and gold palette. — digital airbrush, photorealism, 3D render, sci-fi elements, lens flare, text, typography, lettering, title text, watermark, words, captions"
    ),
    (
        "05_vrd_r_natures_renewal.jpg",
        "oil painting, visible brushwork, storybook illustration, classical composition, painted light. "
        "Nature's Renewal — glowing green tendrils of life magic spreading across a forest floor, "
        "wildflowers blooming in a spiral pattern, a stag watching from the shadows. High fantasy, "
        "painted magical light. Verdant green and gold palette. — digital airbrush, photorealism, 3D render, sci-fi elements, lens flare, text, typography, lettering, title text, watermark, words, captions"
    ),
    (
        "06_vrd_x_heartwood_relic.jpg",
        "oil painting, visible brushwork, storybook illustration, classical composition, painted light. "
        "The Heartwood Relic — a glowing gemstone embedded in ancient living wood, roots wrapping around "
        "it protectively, floating motes of golden light in a cathedral-like forest clearing. High fantasy, "
        "figure integrated into a living detailed environment. Verdant green and gold palette. — digital airbrush, photorealism, 3D render, sci-fi elements, lens flare, text, typography, lettering, title text, watermark, words, captions"
    ),
]

for i, (filename, prompt) in enumerate(prompts):
    output_path = os.path.join(OUT_DIR, filename)
    print(f"\n[{i+1}/6] Generating {filename}...")
    sys.stdout.flush()
    
    result = subprocess.run(
        ['python3', SCRIPT, prompt, output_path, '--model', MODEL],
        capture_output=True, text=True, timeout=180, env=my_env
    )
    print(f"  stdout: {result.stdout.strip()}")
    if result.stderr:
        print(f"  stderr: {result.stderr.strip()[:300]}")
    print(f"  exit: {result.returncode}")
    
    if result.returncode != 0:
        print(f"  FAILED on {filename}")
        sys.exit(1)
    
    time.sleep(2)

print(f"\n=== Done. Files in {OUT_DIR}/ ===")
subprocess.run(['ls', '-la', OUT_DIR])
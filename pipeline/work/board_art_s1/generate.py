#!/usr/bin/env python3
"""Generate 12 board art samples for War Altar battlefield surface.

Usage: bash -c 'set -a && source ~/.hermes/profiles/tcgbot/.env && set +a && python3 pipeline/work/board_art_s1/generate.py'
"""

import base64
import json
import os
import sys
import time
from pathlib import Path
import requests
from PIL import Image
import io

API_KEY = os.environ.get("OPENROUTER_API_KEY")
if not API_KEY:
    print("FATAL: Set OPENROUTER_API_KEY", file=sys.stderr)
    sys.exit(1)

BASE_URL = "https://openrouter.ai/api/v1"
IMAGE_MODEL = "black-forest-labs/flux.2-pro"
IMAGE_SIZE = "1344x768"  # 16:9 — falls back to 1024x1024 if rejected
OUT_DIR = Path("pipeline/work/board_art_s1")

SPINE = (
    "top-down tabletop game battlefield surface, "
    "dark fantasy oil painting, visible brushwork, "
    "storybook illustration quality, painted light, "
    "a large carved elliptical ring dominating the surface, "
    "ornate detail at the EDGES, center area quiet and dark for card placement, "
    "no text, no creatures, no objects in the center, no border"
)

SAMPLES = [
    # DARK BASALT (4x)
    {"id": "board_silver_ring", "label": "Dark Basalt 1 — silver-chiseled ring",
     "prompt": f"{SPINE}. Dark basalt: near-black volcanic stone slab, a ring of silver-chiseled runes encircling the center, faint silver glow in the carving grooves."},
    {"id": "board_ember_groove", "label": "Dark Basalt 2 — ember-light pooling in ring groove",
     "prompt": f"{SPINE}. Dark basalt: near-black volcanic stone slab, warm ember-orange light pooling in the deep ring groove, glowing softly against the dark stone."},
    {"id": "board_gold_veins", "label": "Dark Basalt 3 — faint gold veins in cracks",
     "prompt": f"{SPINE}. Dark basalt: near-black volcanic stone slab, delicate hairline gold veins spreading through surface cracks, subtle metallic gleam, the central elliptical ring barely incised."},
    {"id": "board_hex_edges", "label": "Dark Basalt 4 — hex basalt column edges",
     "prompt": f"{SPINE}. Dark basalt: near-black volcanic stone with hexagonal basalt column fractures visible at the outer edges, the central ring smooth and polished by ages."},
    # OBSIDIAN (2x)
    {"id": "board_obsidian_candle", "label": "Obsidian 1 — candlelight reflections",
     "prompt": f"{SPINE}. Obsidian: mirror-dark volcanic glass surface, faint candlelight reflections dancing on the glossy face, the carved ring has a matte texture contrasting with the polished surrounding stone."},
    {"id": "board_obsidian_matte_ring", "label": "Obsidian 2 — carved matte ring on glass",
     "prompt": f"{SPINE}. Obsidian: deep black volcanic glass, the central elliptical ring is carved with a matte finish contrasting against the mirror-polished surface, subtle reflections of a distant hearth."},
    # MOSSBOUND GRANITE (2x)
    {"id": "board_moss_granite", "label": "Mossbound Granite 1 — moss in crevices",
     "prompt": f"{SPINE}. Mossbound granite: gray stone with patches of dark green moss growing in crevices at the outer edges, the carved elliptical ring is weathered and worn, damp stone smell."},
    {"id": "board_rune_ring", "label": "Mossbound Granite 2 — weathered rune ring",
     "prompt": f"{SPINE}. Mossbound granite: pale gray stone, the central elliptical ring is carved with ancient weathered runes partially obscured by lichen, small moss tufts at the stone perimeter."},
    # ASHEN FLAGSTONE (2x)
    {"id": "board_flagstone_joints", "label": "Ashen Flagstone 1 — large flagstone joints",
     "prompt": f"{SPINE}. Ashen flagstone: warm temple stone floor made of large rectangular flagstones with visible mortar joints, the elliptical ring spans multiple stones, worn by footsteps."},
    {"id": "board_worn_emblem", "label": "Ashen Flagstone 2 — worn central emblem",
     "prompt": f"{SPINE}. Ashen flagstone: warm gray temple stone, a faded and worn central emblem at the heart of the elliptical ring, barely legible ancient symbols, subtle dust motes."},
    # WILDCARD (2x)
    {"id": "board_moonlit_marble", "label": "Wildcard 1 — moonlit white marble",
     "prompt": f"{SPINE}. Moonlit white marble: pale marble battlefield surface with faint gray veining, the elliptical ring etched in silver-blue, cool moonlight glow, ice-crack details at edges."},
    {"id": "board_bronze_inlay", "label": "Wildcard 2 — bronze-inlaid slate",
     "prompt": f"{SPINE}. Bronze-inlaid slate: dark blue-gray slate with an elliptical ring of polished bronze inlay, verdigris patina at the edges, warm golden hues against the cool stone."},
]

def call_api(prompt: str, size: str) -> bytes | None:
    """Call OpenRouter image API with retry on size rejection."""
    for attempt_size in [size, "1024x1024"]:
        try:
            resp = requests.post(
                f"{BASE_URL}/images/generations",
                headers={
                    "Authorization": f"Bearer {API_KEY}",
                    "Content-Type": "application/json",
                },
                json={
                    "model": IMAGE_MODEL,
                    "prompt": prompt,
                    "n": 1,
                    "size": attempt_size,
                },
                timeout=120,
            )
            if resp.status_code == 400 and attempt_size != "1024x1024":
                print(f"  Size {attempt_size} rejected, falling back to 1024x1024")
                continue
            resp.raise_for_status()
            data = resp.json()
            result = data["data"][0]
            if "b64_json" in result:
                return base64.b64decode(result["b64_json"])
            if "url" in result:
                img_resp = requests.get(result["url"], timeout=60)
                img_resp.raise_for_status()
                return img_resp.content
            print(f"  Unexpected response: {list(result.keys())}")
            return None
        except requests.RequestException as e:
            print(f"  Request failed: {e}", file=sys.stderr)
            if hasattr(e, "response") and e.response is not None:
                try:
                    detail = e.response.json()
                    print(f"  Detail: {json.dumps(detail, indent=2)[:500]}")
                except Exception:
                    print(f"  Body: {e.response.text[:500]}")
            if attempt_size == size:
                print("  Retrying with 1024x1024...")
                continue
            return None

def main():
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    total_cost = 0.0
    success = 0
    failed = 0

    print(f"{'='*60}")
    print(f"Generating {len(SAMPLES)} board art samples")
    print(f"Model: {IMAGE_MODEL}")
    print(f"Size: {IMAGE_SIZE}")
    print(f"Estimated cost: ${len(SAMPLES) * 0.025:.2f}")
    print(f"{'='*60}")

    for i, sample in enumerate(SAMPLES):
        sid = sample["id"]
        label = sample["label"]
        prompt = sample["prompt"]

        print(f"\n[{i+1}/{len(SAMPLES)}] {label} ({sid})")
        print(f"  Prompt: {prompt[:100]}...")

        try:
            img_bytes = call_api(prompt, IMAGE_SIZE)
            if img_bytes:
                out_path = OUT_DIR / f"{sid}.png"
                img = Image.open(io.BytesIO(img_bytes)).convert("RGB")
                img.save(out_path, "PNG")
                file_size = out_path.stat().st_size
                img_dim = img.size
                print(f"  ✓ Saved: {out_path.name} ({file_size/1024:.1f} KB, {img_dim[0]}x{img_dim[1]})")
                total_cost += 0.025
                success += 1
            else:
                print(f"  ✗ No image data returned")
                failed += 1
        except Exception as e:
            print(f"  ✗ Error: {e}")
            failed += 1

        time.sleep(1.5)  # rate limit

    print(f"\n{'='*60}")
    print("BOARD ART GENERATION COMPLETE")
    print(f"{'='*60}")
    print(f"  Success: {success}")
    print(f"  Failed:  {failed}")
    print(f"  Cost:    ${total_cost:.2f}")
    print(f"  Files:   {OUT_DIR}")
    for f in sorted(OUT_DIR.glob("*")):
        print(f"    {f.name} ({f.stat().st_size/1024:.1f} KB)")

    return 0 if failed == 0 else 1

if __name__ == "__main__":
    sys.exit(main())
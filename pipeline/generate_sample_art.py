#!/usr/bin/env python3
"""Generate sample card art — 2 cards per stratum to validate the style.

Usage: bash -c 'set -a && source ~/.hermes/.env && set +a && python3 generate_sample_art.py'
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
IMAGE_WIDTH = 832
IMAGE_HEIGHT = 1216

CLIENT_CARDS_DIR = Path("/home/fictive/runewake/client/content/cards")
CLIENT_ART_DIR = Path("/home/fictive/runewake/client/content/art")

# ── Stratum styles (v3.0 blended spine, 2026-08-17) ───────────────────────────

STRATUM_STYLES = {
    "VERDANT": "oil painting in the style of classical storybook illustration, dramatic painted light against deep shadow (chiaroscuro), swirling expressive brushwork reserved for skies, smoke, and magical energy, single grounded focal subject staged with breathing room in the manner of a Renaissance tableau, atmospheric depth with softly rendered distant background, restrained palette with selective vivid accents, thick impasto texture, painted by hand, unsigned artwork. deep forest greens and earthy moss browns with golden highlights, no text, no border, centered subject",
    "EMBER": "oil painting in the style of classical storybook illustration, dramatic painted light against deep shadow (chiaroscuro), swirling expressive brushwork reserved for skies, smoke, and magical energy, single grounded focal subject staged with breathing room in the manner of a Renaissance tableau, atmospheric depth with softly rendered distant background, restrained palette with selective vivid accents, thick impasto texture, painted by hand, unsigned artwork. charcoal greys and cool slate shadows lit by molten orange and gold flame accents, no text, no border, centered subject",
    "TIDE": "oil painting in the style of classical storybook illustration, dramatic painted light against deep shadow (chiaroscuro), swirling expressive brushwork reserved for skies, smoke, and magical energy, single grounded focal subject staged with breathing room in the manner of a Renaissance tableau, atmospheric depth with softly rendered distant background, restrained palette with selective vivid accents, thick impasto texture, painted by hand, unsigned artwork. abyssal blue-teal depths with pale foam edges and scattered sea-green light, no text, no border, centered subject",
    "HOLLOW": "oil painting in the style of classical storybook illustration, dramatic painted light against deep shadow (chiaroscuro), swirling expressive brushwork reserved for skies, smoke, and magical energy, single grounded focal subject staged with breathing room in the manner of a Renaissance tableau, atmospheric depth with softly rendered distant background, restrained palette with selective vivid accents, thick impasto texture, painted by hand, unsigned artwork. bone-white and murky violet with patches of sickly green, shadow-heavy, no text, no border, centered subject",
    "DAWN": "oil painting in the style of classical storybook illustration, dramatic painted light against deep shadow (chiaroscuro), swirling expressive brushwork reserved for skies, smoke, and magical energy, single grounded focal subject staged with breathing room in the manner of a Renaissance tableau, atmospheric depth with softly rendered distant background, restrained palette with selective vivid accents, thick impasto texture, painted by hand, unsigned artwork. warm cream and pale gold with soft amber light, dawn-sky tones, no text, no border, centered subject",
}

# ── Sample cards: 2 per stratum ───────────────────────────────────────────────

SAMPLE_CARDS = [
    # DAWN
    {"id": "dwn_c_dawn_warder", "name": "Dawn Warder", "strata": "DAWN",
     "prompt": "A stone golem wreathed in pale gold light, standing guard at a temple gateway, one hand raised in a warding gesture, dust motes floating in the beam"},
    {"id": "dwn_u_purifying_light", "name": "Purifying Light", "strata": "DAWN",
     "prompt": "A beam of pure white-gold light descending through a ruined cathedral ceiling, illuminating a stone altar, shadows scattering at the edges"},

    # EMBER
    {"id": "emb_c_ember_hound", "name": "Ember Hound", "strata": "EMBER",
     "prompt": "A lean hound with molten orange cracks running through its dark fur, embers drifting from its jaws, standing on volcanic rock at night"},
    {"id": "emb_c_forgeguard_berserker", "name": "Forgeguard Berserker", "strata": "EMBER",
     "prompt": "A muscular warrior in blackened iron plate, a great hammer resting on one shoulder, forge-glow illuminating his face from below, sparks flying"},

    # HOLLOW
    {"id": "hol_c_skeletal_reaver", "name": "Skeletal Reaver", "strata": "HOLLOW",
     "prompt": "A skeletal figure in tattered black cloth, wielding a scythe made of fused bone, standing in a misty graveyard under a sickly green moon"},
    {"id": "hol_r_curse_of_binding", "name": "Curse of Binding", "strata": "HOLLOW",
     "prompt": "Shadowy chains wrapping around a stone throne, each link inscribed with runes that glow with murky violet light, a lone figure bound in the center"},

    # TIDE
    {"id": "tid_c_tidal_scholar", "name": "Tidal Scholar", "strata": "TIDE",
     "prompt": "A robed figure kneeling in shallow tide pools, reading from a weathered stone tablet, bioluminescent blue algae glowing around their feet"},
    {"id": "tid_c_abyssal_gaze", "name": "Abyssal Gaze", "strata": "TIDE",
     "prompt": "A single enormous eye opening in the dark depths of the ocean, surrounded by drifting kelp and faint blue light, ancient and unknowable"},

    # VERDANT
    {"id": "vrd_c_verdant_sproutling", "name": "Verdant Sproutling", "strata": "VERDANT",
     "prompt": "A small animated sprout with glowing green eyes, tiny leaves unfurling, emerging from a mossy stone in a ancient forest"},
    {"id": "vrd_r_bloomweaver", "name": "Bloomweaver", "strata": "VERDANT",
     "prompt": "An ethereal fey figure with vines weaving through their hair, hands weaving threads of golden pollen, flowers blooming around them in a dark forest glade"},
]

def generate_image(card: dict, prompt: str) -> bytes | None:
    """Generate card art via OpenRouter's image API."""
    strata = card.get("strata", "VERDANT")
    style = STRATUM_STYLES.get(strata, STRATUM_STYLES["VERDANT"])
    full_prompt = f"{style}. {prompt}"

    print(f"  Prompt: {full_prompt[:120]}...")
    print(f"  Model: {IMAGE_MODEL}, Size: {IMAGE_WIDTH}x{IMAGE_HEIGHT}")

    resp = requests.post(
        f"{BASE_URL}/images/generations",
        headers={
            "Authorization": f"Bearer {API_KEY}",
            "Content-Type": "application/json",
        },
        json={
            "model": IMAGE_MODEL,
            "prompt": full_prompt,
            "n": 1,
            "size": f"{IMAGE_WIDTH}x{IMAGE_HEIGHT}",
        },
        timeout=120,
    )
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

def main():
    CLIENT_ART_DIR.mkdir(parents=True, exist_ok=True)

    total_cost = 0.0
    success = 0
    failed = 0

    print(f"{'='*60}")
    print(f"Generating {len(SAMPLE_CARDS)} sample card arts (2 per stratum)")
    print(f"Estimated cost: ${len(SAMPLE_CARDS) * 0.025:.2f}")
    print(f"{'='*60}")

    for i, card in enumerate(SAMPLE_CARDS):
        cid = card["id"]
        name = card["name"]
        strata = card["strata"]
        prompt = card["prompt"]

        print(f"\n[{i+1}/{len(SAMPLE_CARDS)}] {name} ({cid}) [{strata}]")

        try:
            img_bytes = generate_image(card, prompt)
            if img_bytes:
                out_path = CLIENT_ART_DIR / f"{cid}.webp"
                img = Image.open(io.BytesIO(img_bytes)).convert("RGB")
                img.save(out_path, "WEBP", quality=85)
                file_size = out_path.stat().st_size
                img_dim = img.size
                print(f"  ✓ Saved: {out_path.name} ({file_size/1024:.1f} KB, {img_dim[0]}x{img_dim[1]})")
                total_cost += 0.025
                success += 1
            else:
                print(f"  ✗ No image data returned")
                failed += 1
        except requests.exceptions.HTTPError as e:
            print(f"  ✗ HTTP Error: {e}")
            if hasattr(e, "response") and e.response is not None:
                try:
                    detail = e.response.json()
                    print(f"    Detail: {json.dumps(detail, indent=2)[:300]}")
                except Exception:
                    print(f"    Body: {e.response.text[:300]}")
            failed += 1
        except Exception as e:
            print(f"  ✗ Error: {e}")
            failed += 1

        time.sleep(1.5)  # rate limit

    # ── Summary ───────────────────────────────────────────────────────────────
    print(f"\n{'='*60}")
    print("SAMPLE GENERATION COMPLETE")
    print(f"{'='*60}")
    print(f"  Success: {success}")
    print(f"  Failed:  {failed}")
    print(f"  Cost:    ${total_cost:.2f}")
    print(f"  Files:   {CLIENT_ART_DIR}")
    for f in sorted(CLIENT_ART_DIR.glob("*.webp")):
        print(f"    {f.name} ({f.stat().st_size/1024:.1f} KB)")

    # ── Update card JSON files with art.asset for the sample cards ────────────
    print(f"\n{'='*60}")
    print("Updating card JSON files with art references")
    print(f"{'='*60}")

    sample_ids = {c["id"] for c in SAMPLE_CARDS}
    for sf in sorted(CLIENT_CARDS_DIR.glob("*.json")):
        with open(sf) as f:
            cards = json.load(f)
        modified = False
        for c in cards:
            cid = c["id"]
            if cid in sample_ids:
                asset_path = f"res://content/art/{cid}.webp"
                if "art" not in c or not isinstance(c["art"], dict):
                    c["art"] = {}
                c["art"]["asset"] = asset_path
                # Only add prompt if doesn't exist
                if "prompt" not in c["art"]:
                    for sc in SAMPLE_CARDS:
                        if sc["id"] == cid:
                            c["art"]["prompt"] = sc["prompt"]
                            break
                modified = True
                print(f"  Updated {c['name']} ({sf.name})")
        if modified:
            with open(sf, "w") as f:
                json.dump(cards, f, indent=2)

    print(f"\nDone! {success}/{len(SAMPLE_CARDS)} samples generated.")
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
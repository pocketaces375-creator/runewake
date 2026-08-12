#!/usr/bin/env python3
"""Generate all missing card art: prompts + images.

Steps:
1. Read all stratum JSON files
2. Generate art.prompt for cards without one (using a cheap LLM)
3. Generate images for cards without art files (via OpenRouter image API)
4. Save to client/content/art/
5. Update card JSON files with art fields
"""

import base64
import json
import os
import sys
import time
from pathlib import Path

import requests

# ── Config ────────────────────────────────────────────────────────────────────

API_KEY = os.environ.get("OPENROUTER_API_KEY")
if not API_KEY:
    print("FATAL: Set OPENROUTER_API_KEY", file=sys.stderr)
    sys.exit(1)

BASE_URL = "https://openrouter.ai/api/v1"
TEXT_MODEL = "openai/gpt-4o-mini"  # cheap for prompt generation
IMAGE_MODEL = "black-forest-labs/flux.2-pro"
IMAGE_SIZE = 1024

CLIENT_CARDS_DIR = Path("/home/fictive/runewake/client/content/cards")
CLIENT_ART_DIR = Path("/home/fictive/runewake/client/content/art")

# ── Stratum style prefixes (from art.py) ──────────────────────────────────────

STRATUM_STYLES = {
    "VERDANT": "Dark fantasy oil painting, overgrown forest palette, emerald and moss green with deep brown, heavy impasto, dramatic rim light, medieval woodcut influence, no text, no border, centered subject",
    "EMBER": "Dark fantasy oil painting, ash and ember palette, soot-black and molten orange, heavy impasto, dramatic rim light, medieval woodcut influence, no text, no border, centered subject",
    "TIDE": "Dark fantasy oil painting, deep ocean palette, abyssal blue and teal with pale foam, heavy impasto, dramatic rim light, medieval woodcut influence, no text, no border, centered subject",
    "HOLLOW": "Dark fantasy oil painting, decayed palette, bone-white and murky violet with sickly green, heavy impasto, dramatic rim light, medieval woodcut influence, no text, no border, centered subject",
    "DAWN": "Dark fantasy oil painting, radiant palette, gold and pale cream with warm amber, heavy impasto, dramatic rim light, medieval woodcut influence, no text, no border, centered subject",
}

# ── Load all cards ────────────────────────────────────────────────────────────

def load_all_cards():
    """Load all cards from stratum JSON files, grouped by file."""
    strata_files = sorted(CLIENT_CARDS_DIR.glob("*.json"))
    all_cards = []
    for sf in strata_files:
        with open(sf) as f:
            cards = json.load(f)
        for c in cards:
            c["_source_file"] = sf.name
            all_cards.append(c)
    return all_cards, strata_files

def get_existing_art_files():
    return {p.stem for p in CLIENT_ART_DIR.glob("*.webp")}

# ── Generate art prompts ──────────────────────────────────────────────────────

def generate_prompt(card: dict) -> str:
    """Generate an art prompt from card data using a cheap LLM."""
    name = card.get("name", "Unknown")
    strata = card.get("strata", "VERDANT")
    card_type = card.get("type", "CREATURE")
    flavor = card.get("flavor", "")
    keywords = card.get("keywords", [])
    attack = card.get("attack")
    vigor = card.get("vigor")
    abilities = card.get("abilities", [])

    # Build a card description
    desc_parts = [f"A {strata.lower()} {card_type.lower()} card named {name}"]
    if card_type == "CREATURE" and attack is not None and vigor is not None:
        desc_parts.append(f"({attack}/{vigor})")
    if keywords:
        desc_parts.append(f"Keywords: {', '.join(keywords)}")
    if flavor:
        desc_parts.append(f"Flavor: {flavor}")

    system_prompt = (
        "You are a TCG art director. Given a card's name, type, stratum, and flavor text, "
        "generate a concise, vivid image generation prompt (1-2 sentences) that describes "
        "the card's art. Focus on the central subject, mood, lighting, and composition. "
        "Output ONLY the prompt text, no explanation, no markdown, no quotes."
    )

    user_prompt = "Card: " + "\n".join(desc_parts)

    resp = requests.post(
        f"{BASE_URL}/chat/completions",
        headers={
            "Authorization": f"Bearer {API_KEY}",
            "Content-Type": "application/json",
        },
        json={
            "model": TEXT_MODEL,
            "messages": [
                {"role": "system", "content": system_prompt},
                {"role": "user", "content": user_prompt},
            ],
            "temperature": 0.8,
            "max_tokens": 120,
        },
        timeout=30,
    )
    resp.raise_for_status()
    data = resp.json()
    prompt = data["choices"][0]["message"]["content"].strip()
    # Cost tracking
    usage = data.get("usage", {})
    prompt_tokens = usage.get("prompt_tokens", 0)
    completion_tokens = usage.get("completion_tokens", 0)
    cost = (prompt_tokens * 0.15 / 1_000_000) + (completion_tokens * 0.60 / 1_000_000)
    print(f"  [prompt] {name}: {prompt[:80]}... (${cost:.6f}, {prompt_tokens}+{completion_tokens}t)")
    return prompt

# ── Generate image ────────────────────────────────────────────────────────────

def generate_image(card: dict, prompt: str) -> bytes | None:
    """Generate card art via OpenRouter's image API."""
    strata = card.get("strata", "VERDANT")
    style = STRATUM_STYLES.get(strata, STRATUM_STYLES["VERDANT"])
    full_prompt = f"{style}. {prompt}"

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
            "size": f"{IMAGE_SIZE}x{IMAGE_SIZE}",
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
    print(f"  [img] Unexpected response shape: {list(result.keys())}")
    return None

# ── Main ──────────────────────────────────────────────────────────────────────

def main():
    existing_art = get_existing_art_files()
    all_cards, strata_files = load_all_cards()

    print(f"Loaded {len(all_cards)} cards from {len(strata_files)} files")
    print(f"Existing art files: {len(existing_art)} ({', '.join(sorted(existing_art))})")

    # Separate cards by what they need
    need_prompt = []
    need_image = []
    for c in all_cards:
        cid = c["id"]
        has_prompt = "art" in c and isinstance(c["art"], dict) and "prompt" in c["art"]
        has_file = cid in existing_art
        if not has_prompt:
            need_prompt.append(c)
        if not has_file:
            need_image.append(c)

    print(f"\nNeed prompt generation: {len(need_prompt)}")
    print(f"Need image generation: {len(need_image)}")

    total_text_cost = 0.0
    total_image_cost = 0.0

    # ── Step 1: Generate art prompts ──────────────────────────────────────────
    if need_prompt:
        print(f"\n{'='*60}")
        print(f"STEP 1: Generating {len(need_prompt)} art prompts")
        print(f"{'='*60}")
        for i, card in enumerate(need_prompt):
            name = card["name"]
            cid = card["id"]
            print(f"\n[{i+1}/{len(need_prompt)}] {name} ({cid})")
            try:
                prompt = generate_prompt(card)
                if "art" not in card or not isinstance(card["art"], dict):
                    card["art"] = {}
                card["art"]["prompt"] = prompt
                # Track cost from usage
                total_text_cost += 0.0003  # rough estimate, ~$0.0003 per prompt
                time.sleep(0.5)  # rate limit
            except Exception as e:
                print(f"  [ERROR] Failed to generate prompt for {name}: {e}")
                # Fallback prompt
                card["art"] = card.get("art", {})
                strata = card.get("strata", "VERDANT").lower()
                card["art"]["prompt"] = f"A {strata} fantasy illustration of {name}."

    # Write updated prompts back to stratum files
    for sf in strata_files:
        with open(sf) as f:
            cards = json.load(f)
        for c in cards:
            cid = c["id"]
            for updated in all_cards:
                if updated["id"] == cid and "art" in updated:
                    c["art"] = updated["art"]
                    break
        with open(sf, "w") as f:
            json.dump(cards, f, indent=2)
        print(f"  Updated {sf.name}")

    # ── Step 2: Generate images ───────────────────────────────────────────────
    print(f"\n{'='*60}")
    print(f"STEP 2: Generating {len(need_image)} images")
    print(f"Model: {IMAGE_MODEL}, Size: {IMAGE_SIZE}x{IMAGE_SIZE}")
    print(f"Estimated cost: ${len(need_image) * 0.025:.2f} (at ~$0.025/image)")
    print(f"{'='*60}")

    success_count = 0
    fail_count = 0

    # Bundle generation into a single batch for efficiency
    # Actually, the images/generations endpoint only accepts one prompt at a time for flux
    for i, card in enumerate(need_image):
        cid = card["id"]
        name = card["name"]
        strata = card.get("strata", "VERDANT")
        prompt = card.get("art", {}).get("prompt", "")

        if not prompt:
            prompt = f"A {strata.lower()} fantasy illustration of {name}."
            print(f"  [WARN] {name} has no prompt, using fallback")

        print(f"\n[{i+1}/{len(need_image)}] {name} ({cid})")
        print(f"  Prompt: {prompt[:100]}...")

        try:
            img_bytes = generate_image(card, prompt)
            if img_bytes:
                # Save as WebP
                out_path = CLIENT_ART_DIR / f"{cid}.webp"
                # Use PIL to save as WebP
                from PIL import Image
                import io
                img = Image.open(io.BytesIO(img_bytes)).convert("RGB")
                img.save(out_path, "WEBP", quality=85)
                print(f"  Saved: {out_path} ({len(img_bytes)} bytes)")
                total_image_cost += 0.025  # flux.2-pro pricing
                success_count += 1
            else:
                print(f"  [FAIL] No image data returned")
                fail_count += 1
        except requests.exceptions.HTTPError as e:
            print(f"  [HTTP ERROR] {e}")
            if hasattr(e, "response") and e.response is not None:
                try:
                    detail = e.response.json()
                    print(f"  Detail: {json.dumps(detail, indent=2)[:300]}")
                    # Check if it's a content filter rejection
                    error_msg = str(detail).lower()
                    if "content" in error_msg and ("filter" in error_msg or "policy" in error_msg or "safety" in error_msg):
                        print(f"  -> Content filter hit! Trying with modified prompt...")
                        # Try again with a sanitized prompt
                        safe_prompt = f"A fantasy {strata.lower()} illustration, atmospheric, moody lighting, centered composition."
                        try:
                            img_bytes = generate_image(card, safe_prompt)
                            if img_bytes:
                                from PIL import Image
                                import io
                                img = Image.open(io.BytesIO(img_bytes)).convert("RGB")
                                out_path = CLIENT_ART_DIR / f"{cid}.webp"
                                img.save(out_path, "WEBP", quality=85)
                                print(f"  Saved (fallback prompt): {out_path}")
                                total_image_cost += 0.025
                                success_count += 1
                                continue
                        except Exception as e2:
                            print(f"  [FAIL] Fallback also failed: {e2}")
                except Exception:
                    pass
            fail_count += 1
        except Exception as e:
            print(f"  [ERROR] {e}")
            fail_count += 1

        # Rate limit: 1-2 second delay between calls
        time.sleep(1.5)

    # ── Step 3: Update card JSON files with art.asset ─────────────────────────
    print(f"\n{'='*60}")
    print("STEP 3: Updating card JSON files with art.asset references")
    print(f"{'='*60}")

    for sf in strata_files:
        with open(sf) as f:
            cards = json.load(f)
        modified = False
        for c in cards:
            cid = c["id"]
            asset_path = f"res://content/art/{cid}.webp"
            if "art" not in c or not isinstance(c["art"], dict):
                c["art"] = {}
            c["art"]["asset"] = asset_path
            modified = True
        if modified:
            with open(sf, "w") as f:
                json.dump(cards, f, indent=2)
            print(f"  Updated {sf.name}")

    # ── Summary ───────────────────────────────────────────────────────────────
    print(f"\n{'='*60}")
    print("SUMMARY")
    print(f"{'='*60}")
    print(f"  Cards processed: {len(all_cards)}")
    print(f"  Prompts generated: {len(need_prompt)}")
    print(f"  Images generated: {success_count} success, {fail_count} failed")
    print(f"  Estimated text cost: ${total_text_cost:.4f}")
    print(f"  Estimated image cost: ${total_image_cost:.4f}")
    print(f"  Total estimated cost: ${total_text_cost + total_image_cost:.4f}")
    print(f"  Art files in {CLIENT_ART_DIR}: {len(list(CLIENT_ART_DIR.glob('*.webp')))}")

    # Also clean up orphaned art files
    all_ids = {c["id"] for c in all_cards}
    for f in CLIENT_ART_DIR.glob("*.webp"):
        if f.stem not in all_ids:
            print(f"  Orphaned: {f.name} (not in any card data)")
            # Don't delete, just report

    return 0 if fail_count == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
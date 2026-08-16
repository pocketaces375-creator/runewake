#!/usr/bin/env python3
"""P6-07: ART — Image generation via OpenRouter.

Reads cards from a DEDUPE+MODERATE-stage output (05_deduplicated.json),
builds a full prompt from the per-stratum locked style prefix + card-specific
art.prompt suffix, calls OpenRouter's image generation API, saves the result
as WebP at two mip levels (1024px, 512px), and writes the CDN/local paths
back into each card's art.asset field.

Usage:
    python -m pipeline.modules.art --input work/b_2026_ember_01/05_deduplicated.json \\
        --work-dir work/b_2026_ember_01
"""

import argparse
import io
import json
import os
import sys
import time
from pathlib import Path
from typing import Any

import requests
from PIL import Image

# ── Paths ─────────────────────────────────────────────────────────────────────

HERE = Path(__file__).resolve().parent.parent  # pipeline/
DEFAULT_CONFIG = HERE / "config.yaml"

# ── Stratum style prefixes (locked per Stage 8 spec) ─────────────────────────
# Each prefix is a full style description prepended to the card-specific
# art.prompt from Stage 2.  These are the game's visual identity — do not
# change without human review.

STRATUM_STYLES: dict[str, str] = {
    "VERDANT": (
        "Dark fantasy oil painting, overgrown forest palette, "
        "emerald and moss green with deep brown, "
        "heavy impasto, dramatic rim light, medieval woodcut influence, "
        "no text, no border, centered subject"
    ),
    "EMBER": (
        "Dark fantasy oil painting, ash and ember palette, "
        "soot-black and molten orange, heavy impasto, "
        "dramatic rim light, medieval woodcut influence, "
        "no text, no border, centered subject"
    ),
    "TIDE": (
        "Dark fantasy oil painting, deep ocean palette, "
        "abyssal blue and teal with pale foam, "
        "heavy impasto, dramatic rim light, medieval woodcut influence, "
        "no text, no border, centered subject"
    ),
    "HOLLOW": (
        "Dark fantasy oil painting, decayed palette, "
        "bone-white and murky violet with sickly green, "
        "heavy impasto, dramatic rim light, medieval woodcut influence, "
        "no text, no border, centered subject"
    ),
    "DAWN": (
        "Dark fantasy oil painting, radiant palette, "
        "gold and pale cream with warm amber, "
        "heavy impasto, dramatic rim light, medieval woodcut influence, "
        "no text, no border, centered subject"
    ),
}

# ── Fallback colours per Stratum (for missing-art placeholder) ─────────────────
# Each is a (R, G, B) tuple used to tint the fallback frame.

STRATUM_FALLBACK_COLORS: dict[str, tuple[int, int, int]] = {
    "VERDANT": (34, 100, 55),
    "EMBER": (180, 60, 20),
    "TIDE": (20, 80, 130),
    "HOLLOW": (100, 70, 110),
    "DAWN": (190, 150, 60),
}

# ── Rune glyphs per Stratum (simple shapes for the fallback frame) ────────────
# Each glyph is a short string drawn onto the fallback placeholder.

STRATUM_GLYPH: dict[str, str] = {
    "VERDANT": "\u2e19",  # ⸙ — leaf-like
    "EMBER": "\u2e1f",    # ⸟ — flame-like
    "TIDE": "\u2248",     # ≈ — wave-like
    "HOLLOW": "\u2e1b",   # ⸛ — hollow
    "DAWN": "\u2606",     # ☆ — star
}

# ── Image generation parameters ───────────────────────────────────────────────

DEFAULT_MODEL = "black-forest-labs/flux.2-pro"
IMAGE_WIDTH = 832   # px — matches card aspect ratio 13:19 (was 1024x1024)
IMAGE_HEIGHT = 1216  # px
MIP_LEVELS = [1216, 608]  # px (full + one mip, scaled by longest edge)
FALLBACK_SIZE = (832, 1216)

# ── Commission queue ──────────────────────────────────────────────────────────
# When API art generation fails for RARE or RELIC cards, they get flagged here
# for hand-commissioning instead of silently accepting fallback art.

COMMISSION_RARITIES = {"RARE", "RELIC"}
DEFAULT_COMMISSION_QUEUE = Path(__file__).resolve().parent.parent.parent / "docs" / "ART_COMMISSION_QUEUE.md"


# ── Helpers ─────────────────────────────────────────────────────────────────────


def load_config(config_path: Path) -> dict[str, Any]:
    """Load YAML config (fallback to empty dict)."""
    import yaml
    if config_path.exists():
        with open(config_path) as f:
            return yaml.safe_load(f) or {}
    return {}


def build_prompt(card: dict) -> str:
    """Build the full image generation prompt from stratum style + art.prompt."""
    strata = card.get("strata", "VERDANT")
    style_prefix = STRATUM_STYLES.get(strata, STRATUM_STYLES["VERDANT"])
    card_prompt = (card.get("art") or {}).get("prompt", "")
    if card_prompt:
        return f"{style_prefix}. {card_prompt}"
    return f"{style_prefix}. A fantasy {strata.lower()} card illustration."


def generate_image(
    prompt: str,
    api_key: str,
    *,
    model: str = DEFAULT_MODEL,
    width: int = IMAGE_WIDTH,
    height: int = IMAGE_HEIGHT,
    timeout: int = 60,
) -> bytes | None:
    """Call OpenRouter's image generation API and return raw image bytes."""
    url = "https://openrouter.ai/api/v1/images/generations"
    headers = {
        "Authorization": f"Bearer {api_key}",
        "Content-Type": "application/json",
    }
    payload = {
        "model": model,
        "prompt": prompt,
        "n": 1,
        "size": f"{width}x{height}",
    }

    try:
        resp = requests.post(url, headers=headers, json=payload, timeout=timeout)
        resp.raise_for_status()
        data = resp.json()
        # The response format follows OpenAI's image API:
        # { "data": [ { "url": "https://..." } ] }  or  { "data": [ { "b64_json": "..." } ] }
        result = data["data"][0]
        if "b64_json" in result:
            import base64
            return base64.b64decode(result["b64_json"])
        if "url" in result:
            img_resp = requests.get(result["url"], timeout=timeout)
            img_resp.raise_for_status()
            return img_resp.content
        print(f"[art] Unexpected API response shape: {list(result.keys())}")
        return None
    except requests.RequestException as e:
        print(f"[art] API request failed: {e}", file=sys.stderr)
        if hasattr(e, "response") and e.response is not None:
            try:
                detail = e.response.json()
                print(f"[art] API error detail: {json.dumps(detail, indent=2)}", file=sys.stderr)
            except Exception:
                print(f"[art] API error body: {e.response.text[:500]}", file=sys.stderr)
        return None


def save_image(img_bytes: bytes, strata: str, card_name: str, art_dir: Path) -> dict[str, str]:
    """Save image as WebP at multiple mip levels (preserving aspect ratio).

    Returns dict mapping mip level to file path.
    """
    slug = _slugify(card_name)
    img = Image.open(io.BytesIO(img_bytes)).convert("RGB")
    orig_w, orig_h = img.size

    assets: dict[str, str] = {}
    for mip_px in MIP_LEVELS:
        # Scale so the longest edge = mip_px, preserving aspect ratio
        if orig_w >= orig_h:
            new_w = mip_px
            new_h = max(1, int(orig_h * mip_px // orig_w))
        else:
            new_h = mip_px
            new_w = max(1, int(orig_w * mip_px // orig_h))
        resized = img.resize((new_w, new_h), Image.Resampling.LANCZOS)
        mip_name = f"{slug}_{mip_px}px.webp"
        mip_path = art_dir / mip_name
        resized.save(mip_path, "WEBP", quality=85)
        assets[str(mip_px)] = str(mip_path)
    return assets


def _slugify(name: str) -> str:
    """Convert a card name to a filesystem-safe slug."""
    return (
        name.lower()
        .replace(" ", "_")
        .replace("'", "")
        .replace('"', "")
        .replace(":", "")
        .replace("-", "_")
        .replace("__", "_")
        .strip("_")
    )


def append_to_commission_queue(card: dict, queue_path: Path) -> None:
    """Flag a card for hand-commissioning when its art failed to generate.

    Appends a markdown entry to the commission queue file. Used for RARE and
    RELIC cards that deserve hand-crafted art rather than a fallback frame.
    """
    name = card.get("name", "UNKNOWN")
    strata = card.get("strata", "?")
    rarity = card.get("rarity", "?")
    card_id = card.get("id", "?")
    prompt = (card.get("art") or {}).get("prompt", "")

    entry = (
        f"- [ ] **{name}** (id=`{card_id}`, strata={strata}, rarity={rarity})\n"
        f"      Art prompt: {prompt[:140]}\n"
    )

    # Create the file with a header if it doesn't exist
    if not queue_path.exists():
        queue_path.parent.mkdir(parents=True, exist_ok=True)
        queue_path.write_text(
            "# ART COMMISSION QUEUE\n\n"
            "Cards that failed AI art generation (RARE/RELIC) and need "
            "hand-commissioned art. Check off when commissioned.\n\n"
        )

    with open(queue_path, "a") as f:
        f.write(entry)
    print(f"[art]   -> FLAGGED for hand-commission: {name} ({rarity})")


def generate_fallback(strata: str, card_name: str, art_dir: Path) -> dict[str, str]:
    """Generate a Stratum-colored fallback frame with a rune glyph.

    Used when the API call fails or the card has no art.prompt.
    Returns the same asset dict format as save_image().
    """
    color = STRATUM_FALLBACK_COLORS.get(strata, (80, 80, 80))
    glyph = STRATUM_GLYPH.get(strata, "?")

    # Create a solid-colour background
    img = Image.new("RGB", FALLBACK_SIZE, color=color)

    # Draw a simple border frame
    from PIL import ImageDraw
    draw = ImageDraw.Draw(img)
    border_color = tuple(min(c + 40, 255) for c in color)
    for i in range(8):
        draw.rectangle(
            [i, i, FALLBACK_SIZE[0] - 1 - i, FALLBACK_SIZE[1] - 1 - i],
            outline=border_color,
        )

    # Draw the stratum name + card name as text
    try:
        draw.text((FALLBACK_SIZE[0] // 2, FALLBACK_SIZE[1] // 2 - 40),
                  glyph, fill="white", anchor="mt", font=None)
    except TypeError:
        # anchor may not be supported in older PIL
        draw.text((FALLBACK_SIZE[0] // 2, FALLBACK_SIZE[1] // 2 - 40),
                  glyph, fill="white")
    try:
        draw.text((FALLBACK_SIZE[0] // 2, FALLBACK_SIZE[1] // 2 + 20),
                  card_name, fill="white", anchor="mt", font=None)
    except TypeError:
        draw.text((FALLBACK_SIZE[0] // 2, FALLBACK_SIZE[1] // 2 + 20),
                  card_name, fill="white")

    # Save at mip levels
    slug = _slugify(card_name)
    assets: dict[str, str] = {}
    for mip_px in MIP_LEVELS:
        # Scale so the longest edge = mip_px, preserving aspect ratio
        fw, fh = FALLBACK_SIZE
        if fw >= fh:
            new_w = mip_px
            new_h = max(1, int(fh * mip_px // fw))
        else:
            new_h = mip_px
            new_w = max(1, int(fw * mip_px // fh))
        resized = img.resize((new_w, new_h), Image.Resampling.LANCZOS)
        mip_name = f"{slug}_fallback_{mip_px}px.webp"
        mip_path = art_dir / mip_name
        resized.save(mip_path, "WEBP", quality=85)
        assets[str(mip_px)] = str(mip_path)

    return assets


# ── CLI ───────────────────────────────────────────────────────────────────────


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Runewake AI Pipeline — ART stage",
    )
    parser.add_argument("--input", required=True,
                        help="Input card file (from prior stage, e.g. 05_deduplicated.json)")
    parser.add_argument("--work-dir", required=True,
                        help="Work directory for this batch")
    parser.add_argument("--model",
                        help=f"OpenRouter image model (default: {DEFAULT_MODEL})")
    parser.add_argument("--api-key",
                        help="OpenRouter API key (default: $OPENROUTER_API_KEY)")
    parser.add_argument("--skip-api", action="store_true",
                        help="Skip API calls and generate fallback art only (for testing)")
    parser.add_argument("--commission-queue",
                        default=str(DEFAULT_COMMISSION_QUEUE),
                        help="Path to hand-commission queue file (default: docs/ART_COMMISSION_QUEUE.md)")
    parser.add_argument("--config",
                        default=str(DEFAULT_CONFIG),
                        help=f"Config file path (default: {DEFAULT_CONFIG})")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)

    # Load config
    config_path = Path(args.config)
    cfg = load_config(config_path)

    # Resolve API key
    api_key = args.api_key or os.environ.get("OPENROUTER_API_KEY")
    if not api_key and not args.skip_api:
        print("[art] No API key. Set OPENROUTER_API_KEY or pass --api-key, or use --skip-api.",
              file=sys.stderr)
        return 1

    model = args.model or cfg.get("defaults", {}).get("image_model", DEFAULT_MODEL)
    timeout = cfg.get("api", {}).get("timeout_seconds", 60)

    # Resolve paths
    input_path = Path(args.input)
    if not input_path.exists():
        print(f"[art] Input not found: {input_path}", file=sys.stderr)
        return 1

    work_dir = Path(args.work_dir)
    art_dir = work_dir / "art"
    art_dir.mkdir(parents=True, exist_ok=True)
    rejects_dir = work_dir / "rejects"
    rejects_dir.mkdir(parents=True, exist_ok=True)

    # Load cards
    with open(input_path) as f:
        raw = json.load(f)
    cards = raw if isinstance(raw, list) else [raw]
    print(f"[art] Processing {len(cards)} cards (model={model}, skip_api={args.skip_api})")

    passed: list[dict] = []
    rejects: list[tuple[dict, str]] = []
    api_calls = 0
    api_failures = 0

    for i, card in enumerate(cards):
        name = card.get("name", f"card_{i:03d}")
        strata = card.get("strata", "VERDANT")
        prompt = build_prompt(card)

        # Ensure art field exists
        if "art" not in card:
            card["art"] = {}
        if "prompt" not in card["art"]:
            card["art"]["prompt"] = prompt

        print(f"[art] [{i+1}/{len(cards)}] {name} ({strata})")

        if args.skip_api:
            # Generate fallback only
            assets = generate_fallback(strata, name, art_dir)
            card["art"]["asset"] = assets.get(str(MIP_LEVELS[0]), "")
            card["art"]["mips"] = assets
            card["art"]["fallback"] = True
            passed.append(card)
            continue

        # Try API generation
        img_bytes = generate_image(prompt, api_key, model=model, timeout=timeout)  # type: ignore[arg-type]
        api_calls += 1

        if img_bytes:
            assets = save_image(img_bytes, strata, name, art_dir)
            card["art"]["asset"] = assets.get(str(MIP_LEVELS[0]), "")
            card["art"]["mips"] = assets
            card["art"]["fallback"] = False
            passed.append(card)
            print(f"[art]   -> Generated: {card['art']['asset']}")
        else:
            api_failures += 1
            print(f"[art]   -> API failed, generating fallback")
            # Flag RARE/RELIC cards for hand-commissioning
            rarity = card.get("rarity", "COMMON")
            if rarity in COMMISSION_RARITIES:
                append_to_commission_queue(card, Path(args.commission_queue))
            assets = generate_fallback(strata, name, art_dir)
            card["art"]["asset"] = assets.get(str(MIP_LEVELS[0]), "")
            card["art"]["mips"] = assets
            card["art"]["fallback"] = True
            passed.append(card)  # still passes — fallback is valid

    # Write output
    out_path = work_dir / "06_art.json"
    with open(out_path, "w") as f:
        json.dump(passed, f, indent=2)
    print(f"[art] Wrote {len(passed)} cards to {out_path}")

    # Summary
    summary = {
        "batch_id": work_dir.name,
        "total": len(cards),
        "processed": len(passed),
        "api_calls": api_calls,
        "api_failures": api_failures,
        "fallbacks": sum(1 for c in passed if c.get("art", {}).get("fallback")),
        "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    }
    with open(work_dir / "06_summary.json", "w") as f:
        json.dump(summary, f, indent=2)
    print(f"[art] Summary: {summary}")

    # If all cards got fallbacks, warn but don't fail
    if api_failures > 0 and api_failures == api_calls:
        print("[art] WARNING: All API calls failed — every card uses fallback art.",
              file=sys.stderr)
        return 0  # still exit 0 — fallback is part of the design

    return 0


if __name__ == "__main__":
    sys.exit(main())
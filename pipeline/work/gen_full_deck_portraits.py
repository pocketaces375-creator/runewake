#!/usr/bin/env python3
"""FULL-DECK-2: Full production run — ALL 65 launch cards in portrait (832x1216).
v3.1 spine + per-stratum palette + RULE 8 vision-model verification.
Wave stops are LIFTED for this run per Trikzos ruling."""
import json, os, subprocess, sys, time, base64, urllib.request, urllib.error
from pathlib import Path

SCRIPT = os.path.join(os.path.dirname(__file__), '..', 'gen_image_openrouter.py')
OUT_DIR = os.path.join(os.path.dirname(__file__), 'full_deck_portraits')
MODEL = "black-forest-labs/flux.2-pro"
VISION_MODEL = "openai/gpt-4o-mini"  # cheapest with vision on OpenRouter

# API key
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

# Read card packs
REPO = Path(__file__).resolve().parent.parent.parent
CARD_DIR = REPO / "client" / "content" / "cards"

# v3.1 spine
STYLE_SPINE = (
    "oil painting in the style of classical storybook illustration, "
    "dramatic painted light against deep shadow (chiaroscuro), "
    "swirling expressive brushwork reserved for skies, smoke, and magical energy, "
    "single grounded focal subject staged with breathing room "
    "in the manner of a Renaissance tableau, "
    "atmospheric depth with softly rendered distant background, "
    "restrained palette with selective vivid accents, "
    "thick impasto texture, painted by hand, unsigned artwork, "
    "loose expressive brushstrokes, thick visible impasto throughout, "
    "canvas texture showing through, painterly edges rather than crisp digital edges"
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

PALETTES = {
    "EMBER": "charcoal greys and cool slate shadows, lit by molten orange and gold flame accents",
    "VERDANT": "deep forest greens and earthy moss browns with golden highlights",
    "TIDE": "abyssal blue-teal depths with pale foam edges and scattered sea-green light",
    "HOLLOW": "bone-white and murky violet with patches of sickly green, shadow-heavy",
    "DAWN": "warm cream and pale gold with soft amber light, dawn-sky tones",
}

# ── RULE 8: Vision-model lettering check ──────────────────────────────────
BASE_URL = "https://openrouter.ai/api/v1"

def check_rule8_vision(image_path):
    """Send image to vision model. Returns (ok: bool, detail: str)."""
    if not os.path.exists(image_path) or os.path.getsize(image_path) < 1000:
        return False, "file too small or missing"
    
    # Encode image as base64
    with open(image_path, 'rb') as f:
        b64 = base64.b64encode(f.read()).decode()
    
    # Determine media type
    ext = Path(image_path).suffix.lower()
    mime = "image/jpeg" if ext in ('.jpg', '.jpeg') else "image/png"
    
    payload = {
        "model": VISION_MODEL,
        "messages": [{
            "role": "user",
            "content": [
                {
                    "type": "text",
                    "text": "Does this image contain ANY painted text, lettering, words, signature, or autograph anywhere, including corners? Answer YES or NO, then one line saying where."
                },
                {
                    "type": "image_url",
                    "image_url": {"url": f"data:{mime};base64,{b64}"}
                }
            ]
        }]
    }
    
    headers = {
        "Authorization": f"Bearer {api_key}",
        "Content-Type": "application/json",
    }
    
    data = json.dumps(payload).encode("utf-8")
    url = f"{BASE_URL}/chat/completions"
    req = urllib.request.Request(url, data=data, headers=headers, method="POST")
    
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            body = json.loads(resp.read())
            answer = body["choices"][0]["message"]["content"].strip()
            if "YES" in answer.upper()[:10]:
                return False, answer[:200]
            return True, answer[:100]
    except Exception as e:
        return False, f"Vision API error: {e}"


# ── Load cards and build prompts ───────────────────────────────────────────
all_cards = []
for pack_path in sorted(CARD_DIR.glob("*.json")):
    cards = json.load(open(pack_path))
    all_cards.extend(cards)

# Filter out existing portrait art
from PIL import Image
portrait_existing = set()
art_dir = REPO / "client" / "content" / "art"
for c in all_cards:
    ap = art_dir / f"{c['id']}.webp"
    if ap.exists():
        try:
            img = Image.open(ap)
            if img.size[0] != img.size[1]:  # not square = portrait
                portrait_existing.add(c['id'])
        except:
            pass

print(f"Total cards: {len(all_cards)}")
print(f"Already portrait: {len(portrait_existing)}")
print(f"Need generation: {len(all_cards) - len(portrait_existing)}")

os.makedirs(OUT_DIR, exist_ok=True)

# Build prompt for each card
prompts = []
for card in all_cards:
    cid = card['id']
    if cid in portrait_existing:
        continue  # skip already-portrait cards
    name = card.get('name', '?')
    strata = card.get('strata', 'VERDANT')
    flavor = card.get('flavor', '')
    rarity = card.get('rarity', 'COMMON')
    card_text = card.get('text', '')
    
    palette = PALETTES.get(strata, PALETTES['VERDANT'])
    subject = name
    
    # Build richer subject from flavor text
    if flavor:
        # Use first sentence of flavor as subject hint
        subject_hint = flavor.split('.')[0].strip() + '.'
        subject_desc = f"A {name} — {subject_hint}"
    else:
        subject_desc = f"A fantasy {strata.lower()} warrior or spell, named {name}"
    
    prompt = (
        f"{STYLE_SPINE}. "
        f"{palette}. "
        f"{subject_desc}. "
        f"High fantasy card illustration, {rarity.lower()} rarity. "
        f"— {STYLE_NEG}"
    )
    
    filename = f"{cid}.jpg".replace(':', '_').replace('/', '_')
    prompts.append((filename, prompt, cid, name, strata, rarity))

print(f"Generation queue: {len(prompts)} cards")
print(f"Estimated cost: ${len(prompts) * 0.036:.2f}")

# ── Generate ──
total_cost = 0.0
lettering_flagged = []
success = 0
failed = 0

for i, (filename, prompt, cid, name, strata, rarity) in enumerate(prompts, 1):
    out_path = os.path.join(OUT_DIR, filename)
    
    print(f"\n[{i}/{len(prompts)}] {cid} — {name} ({strata}/{rarity})")
    print(f"  Prompt: {prompt[:120]}...")
    
    for attempt in range(1, 4):
        start = time.time()
        ret = subprocess.run(
            [sys.executable, SCRIPT, prompt, out_path],
            capture_output=True, text=True, timeout=300, env=my_env,
        )
        elapsed = time.time() - start
        
        if ret.returncode != 0 or not os.path.exists(out_path) or os.path.getsize(out_path) < 1000:
            print(f"  ✗ Failed (attempt {attempt})")
            time.sleep(5)
            continue
        
        size = os.path.getsize(out_path)
        cost = 0.036
        total_cost += cost
        print(f"  ✓ Generated ({size/1024:.0f} KB, ${cost:.3f}, {elapsed:.0f}s)")
        
        # RULE 8: Vision model check
        print(f"  → RULE 8 vision check...")
        r8_ok, r8_msg = check_rule8_vision(out_path)
        if r8_ok:
            print(f"  ✓ RULE 8: {r8_msg}")
            success += 1
            break
        else:
            print(f"  ⚠ RULE 8: {r8_msg}")
            if attempt < 3:
                # Reinforce negatives
                prompt = f"{prompt.rstrip(' .\")')}. Absolutely NO text, NO letters, NO signatures ANYWHERE in the image."
                print(f"  → Regenerating (attempt {attempt+1})...")
            else:
                lettering_flagged.append(cid)
                print(f"  → FLAGGED after 3 attempts")
                success += 1  # still counts as success
                break
    
    if not os.path.exists(out_path) or os.path.getsize(out_path) < 1000:
        failed += 1
    
    time.sleep(2)  # rate limit

# ── Summary ──
print(f"\n{'='*60}")
print(f"FULL PRODUCTION RUN COMPLETE")
print(f"{'='*60}")
print(f"  Generated: {success}/{len(prompts)}")
print(f"  Failed: {failed}")
print(f"  Total cost: ${total_cost:.2f}")
if lettering_flagged:
    print(f"  RULE 8 flagged: {', '.join(lettering_flagged)}")
print(f"  Output: {OUT_DIR}")

# Write report
report = {
    "batch": "full_deck_portraits",
    "total": len(prompts),
    "success": success,
    "failed": failed,
    "cost": round(total_cost, 2),
    "lettering_flagged": lettering_flagged,
    "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
}
with open(os.path.join(OUT_DIR, "run_report.json"), "w") as f:
    json.dump(report, f, indent=2)
print(f"  Report: {OUT_DIR}/run_report.json")

sys.exit(0 if success > 0 else 1)
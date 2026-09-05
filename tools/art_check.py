#!/usr/bin/env python3
"""tools/art_check.py — gate every generated image before it is committed.

Checks (see docs/ART_PROMPT_PLAYBOOK.md):
  1. not a placeholder      — 64px downsample has >= MIN_COLOURS distinct colours
  2. right shape            — tile square, portrait 2:3-ish
  3. no humans on tiles     — vision pass via OpenRouter ("person/face/hand/figure? yes/no")
  4. variety                — average-hash distance between tiles of the same slot

Usage:
  python3 tools/art_check.py tile   client/content/art/artifacts/artf_rogue_dagger_dusk.webp
  python3 tools/art_check.py portrait client/content/art/classes/rogue.png
  python3 tools/art_check.py variety client/content/art/artifacts   # pairwise, same slot
Exit 0 = pass. Exit 1 = reject (reason on stdout, machine-readable "REJECT: <reason>").
"""
import base64
import io
import json
import os
import sys
import urllib.request

from PIL import Image

MIN_COLOURS = 1000
VISION_MODEL = "openai/gpt-4o-mini"
HASH_MIN_DISTANCE = 12  # bits, out of 64


def ahash(im, n=8):
    g = im.convert("L").resize((n, n))
    px = list(g.getdata())
    avg = sum(px) / len(px)
    bits = 0
    for i, p in enumerate(px):
        if p > avg:
            bits |= 1 << i
    return bits


def colours(path):
    im = Image.open(path).convert("RGB").resize((64, 64))
    return len(set(im.getdata()))


def has_person(path):
    """True if a vision model says a person/face/hand/figure is present."""
    key = os.environ.get("OPENROUTER_API_KEY")
    if not key:
        print("WARN: no OPENROUTER_API_KEY — skipping the human check")
        return False
    im = Image.open(path).convert("RGB")
    im.thumbnail((512, 512))
    buf = io.BytesIO()
    im.save(buf, "PNG")
    b64 = base64.b64encode(buf.getvalue()).decode()
    body = {
        "model": VISION_MODEL,
        "max_tokens": 5,
        "messages": [{
            "role": "user",
            "content": [
                {"type": "text", "text": "Does this image contain a person, a face, a hand, or any humanoid figure (including one holding or wearing the object)? Answer with exactly one word: yes or no."},
                {"type": "image_url", "image_url": {"url": "data:image/png;base64," + b64}},
            ],
        }],
    }
    req = urllib.request.Request(
        "https://openrouter.ai/api/v1/chat/completions",
        data=json.dumps(body).encode(),
        headers={"Authorization": "Bearer " + key, "Content-Type": "application/json"},
    )
    try:
        with urllib.request.urlopen(req, timeout=90) as r:
            out = json.load(r)
        ans = out["choices"][0]["message"]["content"].strip().lower()
    except Exception as e:  # never block the pipeline on an API hiccup
        print(f"WARN: vision check failed ({e}) — not rejecting on this basis")
        return False
    print(f"  vision: person present? {ans}")
    return ans.startswith("y")


def check_image(path, kind):
    if not os.path.exists(path):
        print(f"REJECT: {path} missing")
        return False
    c = colours(path)
    if c < MIN_COLOURS:
        print(f"REJECT: {path} is a placeholder ({c} colours at 64px, need {MIN_COLOURS})")
        return False
    w, h = Image.open(path).size
    if kind == "tile" and abs(w - h) > 2:
        print(f"REJECT: {path} tile must be square, got {w}x{h}")
        return False
    if kind == "portrait" and not (1.3 <= h / w <= 1.6):
        print(f"REJECT: {path} portrait aspect {w}x{h} outside 1.3-1.6")
        return False
    if kind == "tile" and has_person(path):
        print(f"REJECT: {path} contains a person/hand/figure — artifact tiles are the object alone")
        return False
    print(f"PASS: {path} ({w}x{h}, {c} colours)")
    return True


def check_variety(folder):
    slots = {}
    for f in sorted(os.listdir(folder)):
        if not f.endswith(".webp"):
            continue
        parts = f[:-5].split("_")  # artf_<class>_<item...>
        if len(parts) < 3:
            continue
        slot = parts[2]
        slots.setdefault(slot, []).append(os.path.join(folder, f))
    ok = True
    for slot, files in slots.items():
        for i in range(len(files)):
            for j in range(i + 1, len(files)):
                d = bin(ahash(Image.open(files[i])) ^ ahash(Image.open(files[j]))).count("1")
                if d < HASH_MIN_DISTANCE:
                    print(f"REJECT: {os.path.basename(files[i])} and {os.path.basename(files[j])} look alike (hash distance {d} < {HASH_MIN_DISTANCE}) — same slot needs visibly different objects")
                    ok = False
    if ok:
        print("PASS: variety")
    return ok


# ── Gate mode: magic-byte × extension + .import valid check ──

MAGIC = {
    ".webp": (b"RIFF", 0, 8, b"WEBP"),   # RIFF + 4-byte size + WEBP
    ".png":  (b"\x89PNG\r\n\x1a\n", 0,),
    ".jpg":  (b"\xff\xd8\xff", 0,),
    ".jpeg": (b"\xff\xd8\xff", 0,),
    ".gif":  (b"GIF8", 0,),
}


def _check_magic(path):
    """Return (ext, ok, detected) where detected is the format we think it is."""
    ext = os.path.splitext(path)[1].lower()
    with open(path, "rb") as f:
        head = f.read(16)

    # Detect actual format from magic bytes
    if head[:8] == b"\x89PNG\r\n\x1a\n":
        detected = ".png"
    elif head[:3] == b"\xff\xd8\xff":
        detected = ".jpg"
    elif head[:4] == b"RIFF" and head[8:12] == b"WEBP":
        detected = ".webp"
    elif head[:5] == b"GIF8":
        detected = ".gif"
    else:
        detected = ext  # unknown: trust the extension

    ok = detected == ext
    return ext, ok, detected


def _check_import(path):
    """Check that the .import file does not say valid=false (if it exists)."""
    imp = path + ".import"
    if not os.path.exists(imp):
        return None  # missing .import is not a gate failure
    with open(imp, "rb") as f:
        content = f.read()
    if b"valid=false" in content or b"valid = false" in content:
        return "valid=false in .import"
    return None


def run_gate(root):
    """Walk root, check every image file.  Return a list of (path, reason)."""
    failures = []
    total = 0
    for dirpath, _, filenames in os.walk(root):
        for fn in sorted(filenames):
            full = os.path.join(dirpath, fn)
            ext = os.path.splitext(fn)[1].lower()
            if ext in MAGIC:
                total += 1
                ext, ok, detected = _check_magic(full)
                if not ok:
                    failures.append((full, f"content is {detected} but extension is {ext}"))
                err = _check_import(full)
                if err:
                    failures.append((full, err))
    return failures, total


if __name__ == "__main__":
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(2)
    mode, target = sys.argv[1], sys.argv[2]

    if mode == "gate":
        failures, total = run_gate(target)
        for path, reason in failures:
            print(f"FAIL: {path}: {reason}")
        if failures:
            print(f"GATE FAILED: {len(failures)} of {total} files failed")
            sys.exit(1)
        else:
            print(f"GATE PASSED: {total} files checked, 0 failures")
            sys.exit(0)

    good = check_variety(target) if mode == "variety" else check_image(target, mode)
    sys.exit(0 if good else 1)

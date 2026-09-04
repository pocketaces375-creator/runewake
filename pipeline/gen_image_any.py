#!/usr/bin/env python3
"""pipeline/gen_image_any.py — one generator, several providers.

Two call paths, picked automatically:
  * images/generations  — Black Forest Labs FLUX (width/height honoured)
  * chat/completions with modalities=["image","text"] — Gemini "Nano Banana" and
    OpenAI GPT image models, which return the image inline as a data URL.

Usage:
  python3 pipeline/gen_image_any.py "<prompt>" out.png --model google/gemini-3-pro-image
                                    [--width 832] [--height 1216] [--aspect 2:3]
"""
import argparse
import base64
import json
import os
import sys
import urllib.error
import urllib.request

BASE = "https://openrouter.ai/api/v1"
KEY = os.environ.get("OPENROUTER_API_KEY", "")
CHAT_PATH = ("google/", "openai/")          # models that answer through chat/completions


def _post(path, payload, timeout=300):
    req = urllib.request.Request(
        f"{BASE}{path}",
        data=json.dumps(payload).encode(),
        headers={"Authorization": f"Bearer {KEY}", "Content-Type": "application/json"},
    )
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return json.loads(r.read().decode())
    except urllib.error.HTTPError as e:
        body = e.read().decode()[:400]
        print(f"HTTP {e.code}: {body}", file=sys.stderr)
        return None


def _save_b64(b64, out):
    raw = base64.b64decode(b64)
    with open(out, "wb") as f:
        f.write(raw)
    # FLUX sometimes returns JPEG bytes under a .png name; normalise
    if raw[:3] == b"\xff\xd8\xff" and out.lower().endswith(".png"):
        try:
            from PIL import Image
            Image.open(out).save(out, "PNG")
        except Exception:
            pass
    return len(raw)


def via_images(prompt, out, model, width, height):
    d = _post("/images/generations",
              {"model": model, "prompt": prompt, "n": 1, "size": f"{width}x{height}"})
    if not d:
        return 0
    for item in d.get("data", []):
        if item.get("b64_json"):
            return _save_b64(item["b64_json"], out)
        if item.get("url"):
            with urllib.request.urlopen(item["url"], timeout=180) as r, open(out, "wb") as f:
                f.write(r.read())
            return os.path.getsize(out)
    print(f"no image in response: {json.dumps(d)[:300]}", file=sys.stderr)
    return 0


def via_chat(prompt, out, model, aspect):
    payload = {
        "model": model,
        "modalities": ["image", "text"],
        "messages": [{"role": "user", "content": prompt}],
    }
    if aspect:
        payload["image_config"] = {"aspect_ratio": aspect}
    d = _post("/chat/completions", payload)
    if not d:
        return 0
    try:
        msg = d["choices"][0]["message"]
    except (KeyError, IndexError):
        print(f"unexpected response: {json.dumps(d)[:300]}", file=sys.stderr)
        return 0
    for img in msg.get("images") or []:
        url = (img.get("image_url") or {}).get("url") or img.get("url") or ""
        if url.startswith("data:"):
            return _save_b64(url.split(",", 1)[1], out)
        if url.startswith("http"):
            with urllib.request.urlopen(url, timeout=180) as r, open(out, "wb") as f:
                f.write(r.read())
            return os.path.getsize(out)
    content = msg.get("content")
    if isinstance(content, list):
        for part in content:
            u = ((part.get("image_url") or {}).get("url")) if isinstance(part, dict) else None
            if u and u.startswith("data:"):
                return _save_b64(u.split(",", 1)[1], out)
    print(f"no image returned. text was: {str(msg.get('content'))[:200]}", file=sys.stderr)
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("prompt")
    ap.add_argument("out")
    ap.add_argument("--model", default="black-forest-labs/flux.2-pro")
    ap.add_argument("--width", type=int, default=832)
    ap.add_argument("--height", type=int, default=1216)
    ap.add_argument("--aspect", default="2:3")
    a = ap.parse_args()
    if not KEY:
        print("FATAL: OPENROUTER_API_KEY not set", file=sys.stderr)
        sys.exit(1)
    if a.model.startswith(CHAT_PATH):
        n = via_chat(a.prompt, a.out, a.model, a.aspect)
    else:
        n = via_images(a.prompt, a.out, a.model, a.width, a.height)
    if n:
        print(f"saved {a.out} ({n} bytes) via {a.model}")
        sys.exit(0)
    sys.exit(2)


if __name__ == "__main__":
    main()

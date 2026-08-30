#!/usr/bin/env python3
"""
tools/describe_image.py — describe an image via an OpenRouter vision model.
Usage:
  python3 tools/describe_image.py <image_path> [question about the image]
Requires OPENROUTER_API_KEY (sourced from ~/.hermes/.env if present).
"""
import base64, json, mimetypes, os, sys, urllib.request

def main():
    if len(sys.argv) < 2:
        print(__doc__); sys.exit(1)
    path = sys.argv[1]
    question = " ".join(sys.argv[2:]) or (
        "Describe this image in detail. It is likely a screenshot or mockup "
        "from Runewake, a dark-fantasy trading card game (duel board, card "
        "frames, map, or title screen). Identify which screen it shows, "
        "every UI element and its position, any text you can read, and "
        "anything that looks broken, misaligned, or unusual.")
    key = os.environ.get("OPENROUTER_API_KEY")
    if not key:
        env = os.path.expanduser("~/.hermes/.env")
        if os.path.exists(env):
            for line in open(env):
                if line.startswith("OPENROUTER_API_KEY="):
                    key = line.split("=", 1)[1].strip().strip('"')
    if not key:
        print("ERROR: OPENROUTER_API_KEY not set"); sys.exit(1)
    mime = mimetypes.guess_type(path)[0] or "image/jpeg"
    b64 = base64.b64encode(open(path, "rb").read()).decode()
    body = json.dumps({
        "model": os.environ.get("VISION_MODEL", "google/gemini-2.5-flash"),
        "messages": [{"role": "user", "content": [
            {"type": "text", "text": question},
            {"type": "image_url", "image_url": {"url": f"data:{mime};base64,{b64}"}},
        ]}],
    }).encode()
    req = urllib.request.Request(
        "https://openrouter.ai/api/v1/chat/completions", data=body,
        headers={"Authorization": f"Bearer {key}",
                 "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=120) as r:
        out = json.load(r)
    print(out["choices"][0]["message"]["content"])

if __name__ == "__main__":
    main()
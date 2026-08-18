#!/usr/bin/env python3
"""
pipeline/gen_image_openrouter.py — Direct OpenRouter image generation via images/generations endpoint.
Supports configurable size (default: 832x1216 portrait for card aspect 13:19).

Usage:
  python3 pipeline/gen_image_openrouter.py "prompt" output.jpg --model black-forest-labs/flux.2-pro
  python3 pipeline/gen_image_openrouter.py "prompt" output.jpg --width 832 --height 1216

Requires OPENROUTER_API_KEY in environment.
"""
import base64
import json
import os
import sys
import urllib.error
import urllib.request
from io import BytesIO


try:
    from PIL import Image as PILImage
    HAS_PIL = True
except ImportError:
    PILImage = None  # type: ignore
    HAS_PIL = False


def ensure_true_png(path: str) -> None:
    """Check magic bytes of saved image. If JPEG data was saved with .png
    extension (known FLUX.2 Pro behaviour), re-encode as true PNG via PIL.
    Logs and mutates the file in place."""
    if not HAS_PIL:
        print("[ensure_true_png] WARNING: PIL not available — skipping magic-byte check", file=sys.stderr)
        return

    with open(path, "rb") as f:
        magic = f.read(4)

    # JPEG magic bytes: FF D8 FF
    if magic[:3] == b"\xff\xd8\xff":
        print(f"[ensure_true_png] Detected JPEG data in {path} — re-encoding to true PNG", file=sys.stderr)
        img = PILImage.open(path)
        img.save(path, "PNG")
        size = os.path.getsize(path)
        print(f"[ensure_true_png] Re-encoded {path} ({size} bytes, format={img.mode})", file=sys.stderr)
    elif magic[:4] != b"\x89PNG":
        print(f"[ensure_true_png] WARNING: {path} has unexpected magic bytes {magic.hex()} — not JPEG or PNG", file=sys.stderr)


OPENROUTER_API_KEY = os.environ.get("OPENROUTER_API_KEY", "")
if not OPENROUTER_API_KEY:
    print("FATAL: OPENROUTER_API_KEY not set in environment", file=sys.stderr)
    sys.exit(1)

BASE_URL = "https://openrouter.ai/api/v1"
DEFAULT_MODEL = "black-forest-labs/flux.2-pro"
DEFAULT_WIDTH = 832
DEFAULT_HEIGHT = 1216


def generate_image(prompt: str, output_path: str, model: str = DEFAULT_MODEL,
                   width: int = DEFAULT_WIDTH, height: int = DEFAULT_HEIGHT) -> int:
    """Generate an image via OpenRouter's images/generations endpoint.
    Returns HTTP status code from the API call.
    """
    headers = {
        "Authorization": f"Bearer {OPENROUTER_API_KEY}",
        "Content-Type": "application/json",
    }

    payload = {
        "model": model,
        "prompt": prompt,
        "n": 1,
        "size": f"{width}x{height}",
    }

    data = json.dumps(payload).encode("utf-8")
    url = f"{BASE_URL}/images/generations"
    req = urllib.request.Request(url, data=data, headers=headers, method="POST")

    try:
        with urllib.request.urlopen(req, timeout=180) as resp:
            status = resp.status
            body = resp.read()

            if status == 200:
                result = json.loads(body)
                data_array = result.get("data", [])
                if not data_array:
                    print(f"No image data in response", file=sys.stderr)
                    print(f"Response: {json.dumps(result)[:500]}", file=sys.stderr)
                    return status

                entry = data_array[0]
                if "b64_json" in entry:
                    img_bytes = base64.b64decode(entry["b64_json"])
                    with open(output_path, "wb") as f:
                        f.write(img_bytes)
                    ensure_true_png(output_path)
                    size = os.path.getsize(output_path)
                    print(f"Saved: {output_path} ({size} bytes)")
                    return status
                if "url" in entry:
                    img_url = entry["url"]
                    urllib.request.urlretrieve(img_url, output_path)
                    ensure_true_png(output_path)
                    size = os.path.getsize(output_path)
                    print(f"Saved: {output_path} ({size} bytes)")
                    return status

                print(f"Unexpected response shape: {list(entry.keys())}", file=sys.stderr)
                print(f"Response: {json.dumps(result)[:500]}", file=sys.stderr)
                return status
            else:
                error_body = body.decode("utf-8")
                print(f"API error (status {status}): {error_body[:500]}", file=sys.stderr)
                return status

    except urllib.error.HTTPError as e:
        error_body = e.read().decode("utf-8")
        print(f"HTTP {e.code}: {error_body[:500]}", file=sys.stderr)
        return e.code
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        import traceback
        traceback.print_exc()
        return 0


def main():
    if len(sys.argv) < 3:
        print("Usage: python3 gen_image_openrouter.py <prompt> <output.jpg> [--model <model>] [--width <px>] [--height <px>]", file=sys.stderr)
        sys.exit(1)

    prompt = sys.argv[1]
    output_path = sys.argv[2]
    model = DEFAULT_MODEL
    width = DEFAULT_WIDTH
    height = DEFAULT_HEIGHT

    if "--model" in sys.argv:
        idx = sys.argv.index("--model")
        if idx + 1 < len(sys.argv):
            model = sys.argv[idx + 1]
    if "--width" in sys.argv:
        idx = sys.argv.index("--width")
        if idx + 1 < len(sys.argv):
            width = int(sys.argv[idx + 1])
    if "--height" in sys.argv:
        idx = sys.argv.index("--height")
        if idx + 1 < len(sys.argv):
            height = int(sys.argv[idx + 1])

    print(f"Generating: model={model}, size={width}x{height}, prompt={prompt[:100]}...")
    status = generate_image(prompt, output_path, model, width, height)
    print(f"HTTP status: {status}")
    if status == 200 and os.path.exists(output_path) and os.path.getsize(output_path) > 0:
        sys.exit(0)
    else:
        sys.exit(1)


if __name__ == "__main__":
    main()
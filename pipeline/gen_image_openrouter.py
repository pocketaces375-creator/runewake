#!/usr/bin/env python3
"""
pipeline/gen_image_openrouter.py — Direct OpenRouter image generation via POST.
Bypasses the broken pipeline internal path that was missing auth headers.

Usage:
  python3 pipeline/gen_image_openrouter.py "prompt" output.jpg --model black-forest-labs/flux.2-pro
  python3 pipeline/gen_image_openrouter.py "prompt" output.jpg --model google/gemini-2.5-flash-image

Requires OPENROUTER_API_KEY in environment.
"""
import base64
import json
import os
import re
import sys
import urllib.request
import urllib.error


OPENROUTER_API_KEY = os.environ.get("OPENROUTER_API_KEY", "")
if not OPENROUTER_API_KEY:
    print("FATAL: OPENROUTER_API_KEY not set in environment", file=sys.stderr)
    sys.exit(1)


def generate_image(prompt: str, output_path: str, model: str = "black-forest-labs/flux.2-pro") -> int:
    """Generate an image via OpenRouter's chat completions endpoint.
    Returns HTTP status code from the API call.
    """
    headers = {
        "Authorization": f"Bearer {OPENROUTER_API_KEY}",
        "Content-Type": "application/json",
    }

    payload = {
        "model": model,
        "messages": [
            {"role": "user", "content": prompt}
        ],
    }

    data = json.dumps(payload).encode("utf-8")
    url = "https://openrouter.ai/api/v1/chat/completions"
    req = urllib.request.Request(url, data=data, headers=headers, method="POST")

    try:
        with urllib.request.urlopen(req, timeout=180) as resp:
            status = resp.status
            body = resp.read()
            result = json.loads(body)

            if status == 200:
                choice = result.get("choices", [{}])[0]
                msg = choice.get("message", {})
                content = msg.get("content")

                # FLUX models return markdown image URLs in content string
                if isinstance(content, str):
                    # Extract image URLs from markdown: ![alt](url) or bare URLs
                    urls = re.findall(r'https?://[^\s\)"\']+\.(?:png|jpg|jpeg|webp)(?:\?[^\s\)"\']*)?', content)
                    if urls:
                        img_url = urls[0]
                        urllib.request.urlretrieve(img_url, output_path)
                        size = os.path.getsize(output_path)
                        print(f"Saved: {output_path} ({size} bytes)")
                        return status

                    # Also check for image markdown syntax: ![](url)
                    img_md = re.findall(r'!\[.*?\]\((https?://[^\)]+)\)', content)
                    if img_md:
                        img_url = img_md[0]
                        urllib.request.urlretrieve(img_url, output_path)
                        size = os.path.getsize(output_path)
                        print(f"Saved: {output_path} ({size} bytes)")
                        return status

                elif isinstance(content, list):
                    for part in content:
                        if isinstance(part, dict) and part.get("type") == "image_url":
                            img_url = part["image_url"]["url"]
                            if img_url.startswith("data:"):
                                _, b64data = img_url.split(",", 1)
                                img_bytes = base64.b64decode(b64data)
                                with open(output_path, "wb") as f:
                                    f.write(img_bytes)
                            else:
                                urllib.request.urlretrieve(img_url, output_path)
                            size = os.path.getsize(output_path)
                            print(f"Saved: {output_path} ({size} bytes)")
                            return status

                # FLUX models return images in message.images array (not in content)
                images = msg.get("images", [])
                if images:
                    for img in images:
                        if isinstance(img, dict) and "image_url" in img:
                            img_url = img["image_url"]["url"]
                            if img_url.startswith("data:"):
                                _, b64data = img_url.split(",", 1)
                                img_bytes = base64.b64decode(b64data)
                                with open(output_path, "wb") as f:
                                    f.write(img_bytes)
                            else:
                                urllib.request.urlretrieve(img_url, output_path)
                            size = os.path.getsize(output_path)
                            print(f"Saved: {output_path} ({size} bytes)")
                            return status

                # If we got here, dump the response for debugging
                print(f"Content type: {type(content).__name__}", file=sys.stderr)
                print(f"Response preview: {json.dumps(result)[:800]}", file=sys.stderr)
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
        print("Usage: python3 gen_image_openrouter.py <prompt> <output.jpg> [--model <model>]", file=sys.stderr)
        sys.exit(1)

    prompt = sys.argv[1]
    output_path = sys.argv[2]
    model = "black-forest-labs/flux.2-pro"

    if "--model" in sys.argv:
        idx = sys.argv.index("--model")
        if idx + 1 < len(sys.argv):
            model = sys.argv[idx + 1]

    print(f"Generating: model={model}, prompt={prompt[:100]}...")
    status = generate_image(prompt, output_path, model)
    print(f"HTTP status: {status}")
    if status == 200 and os.path.exists(output_path) and os.path.getsize(output_path) > 0:
        sys.exit(0)
    else:
        sys.exit(1)


if __name__ == "__main__":
    main()
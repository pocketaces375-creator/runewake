#!/usr/bin/env python3
"""tools/style_lora.py — FABLE-043: teach the image generator the Runewake look.

Trikzos: "I want the image generator trained off of all of these cards that it's given so that it
creates things in such a fashion."

Two ways to get the house style into new art, and this tool does both:

  REFERENCE MODE (no training, free to try)
      Every art request carries 4 of the best example images, and Gemini's image model matches
      their style. Good for a first look; the style drifts a little card to card.

  TRAINED STYLE (a LoRA — a small add-on trained on the examples)
      The examples are captioned, zipped and sent to fal.ai, which trains a FLUX LoRA in a few
      minutes for about $2. From then on, putting the trigger word in a prompt paints in that
      style. This is the one that "learns".

The pipeline, start to finish:

  1. Trikzos drops images into the INBOX (any format, any size, subfolders fine):
         ~/runewake_art_archive/style_lora/inbox/
     Send the ARTWORK, not whole cards with frames and text — the model learns whatever repeats,
     frames and lettering included. Only images you have the rights to.

  2. style_lora.py gather [--with-game-art]
         copies the inbox (and, with the flag, the game's 148 approved card paintings) into the
         dataset: converted to JPEG, long side 1024, near-duplicates dropped.
  3. style_lora.py caption
         a vision model writes one line per image describing WHAT is in it (never the style —
         the trigger word carries the style). Each caption is a .txt next to its image; edit any
         by hand and re-running never overwrites an edited one.
  4. style_lora.py sheet
         a contact sheet of the dataset with captions, to prune bad images before paying.
  5. style_lora.py train [--steps 1000] [--name v1]
         uploads the zip, trains on fal-ai/flux-lora-fast-training (style mode), and records the
         result; the newest trained model becomes the active one.
  6. style_lora.py test [--n 6] [--scale 1.0]
         paints the same prompts three ways — trained style, reference mode, and today's FLUX —
         side by side on one sheet under artifacts/art_review/style_lora/, for Trikzos to judge.

  And in the normal art pipeline (after hook-art-director):
      art_director.py render <ids> --models lora   (or refs)

  HOUSE-STYLE GUIDE (FABLE-STYLE-2 — no training, OpenRouter only, a few cents)
      style_lora.py distill [--n 24]
         a vision model studies a spread of the images and writes the house style down: a guide
         for people (artifacts/art_review/style_lora/style_guide.md) and one short style clause
         for generators (pipeline/style_lora.json).
      style_lora.py guide on|off|show
         with the guide ON, the clause rides on every art prompt — art_director (after
         hook-art-director), reference mode, and `test --modes guide` (today's FLUX + the clause).

  FREE TRAINING ON KAGGLE (FABLE-STYLE-2): tools/kaggle_lora.py trains an SDXL LoRA on Kaggle's
      free GPUs from this same dataset and paints with it. No fal, no card. See its --help.

  style_lora.py doctor    checks keys, packages, folders and counts.
  style_lora.py hook-art-director [--dry-run]   adds the lora/refs generators to art_director.py,
                          only if its code still matches (otherwise it changes nothing).

KEYS (environment, or ~/.hermes/.env like art_director):
  FAL_KEY             fal.ai — training and painting with the trained style (fal.ai/dashboard/keys)
  OPENROUTER_API_KEY  captions, reference mode, and today's FLUX for the comparison

FILES
  ~/runewake_art_archive/style_lora/inbox/     what Trikzos sends
  ~/runewake_art_archive/style_lora/dataset/   the prepared set (images + .txt captions)
  ~/runewake_art_archive/style_lora/runs/      zips, training logs, test paintings
  pipeline/style_lora.json                     the active trained model + reference picks (commit it)

SAFE TO DROP INTO ANY VERSION OF THE REPO: this file imports nothing from the game or the rest of
the pipeline (only Pillow, and fal-client when training/painting). It READS client/content/cards,
client/content/art and the art ledger; it WRITES only under ~/runewake_art_archive/style_lora/,
pipeline/style_lora.json and artifacts/art_review/style_lora/. No game code, no build, no APK.
"""
import argparse
import base64
import hashlib
import io
import json
import os
import re
import shutil
import sys
import time
import urllib.error
import urllib.request
import zipfile
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, ImageOps

TOOLS = Path(__file__).resolve().parent
REPO = TOOLS.parent
HOME = Path(os.environ.get("STYLE_LORA_HOME", str(Path.home() / "runewake_art_archive" / "style_lora")))
INBOX = HOME / "inbox"
DATASET = HOME / "dataset"
RUNS = HOME / "runs"
STATE = REPO / "pipeline" / "style_lora.json"
REVIEW = REPO / "artifacts" / "art_review" / "style_lora"
GAME_ART = REPO / "client" / "content" / "art"

TRIGGER = os.environ.get("STYLE_LORA_TRIGGER", "rnwk style")
CAPTION_MODEL = os.environ.get("STYLE_CAPTION_MODEL", "google/gemini-2.5-flash")
REFS_MODEL = os.environ.get("STYLE_REFS_MODEL", "google/gemini-3-pro-image")
FLUX_MODEL = os.environ.get("STYLE_FLUX_MODEL", "black-forest-labs/flux.2-pro")
DISTILL_MODEL = os.environ.get("STYLE_DISTILL_MODEL", "google/gemini-2.5-pro")
TRAINER = "fal-ai/flux-lora-fast-training"
PAINTER = "fal-ai/flux-lora"
OPENROUTER = os.environ.get("STYLE_OPENROUTER_BASE", "https://openrouter.ai/api/v1")
IMG_EXT = {".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff", ".avif", ".heic"}
W, H = 832, 1216   # the game's card-art canvas


# ──────────────────────────────── keys & state ────────────────────────────────
def _env_key(name, required=True):
    k = os.environ.get(name, "")
    if not k:
        env_file = Path(os.environ.get("ART_ENV_FILE", str(Path.home() / ".hermes" / ".env")))
        try:
            for line in env_file.read_text(encoding="utf-8").splitlines():
                m = re.match(rf"\s*(?:export\s+)?{name}\s*=\s*(.+?)\s*$", line)
                if m:
                    k = m.group(1).strip().strip('"').strip("'")
        except OSError:
            pass
        if k:
            os.environ[name] = k
    if not k and required:
        sys.exit(f"FATAL: {name} is not set (environment or ~/.hermes/.env). See `style_lora.py doctor`.")
    return k


def load_state():
    try:
        return json.loads(STATE.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {"trigger": TRIGGER, "models": [], "active": None, "refs": []}


def save_state(s):
    STATE.parent.mkdir(parents=True, exist_ok=True)
    STATE.write_text(json.dumps(s, indent=1) + "\n", encoding="utf-8")


def active_model(s=None):
    s = s or load_state()
    return next((m for m in s.get("models", []) if m.get("name") == s.get("active")), None)


def dataset_images():
    return sorted(p for p in DATASET.glob("*.jpg"))


# ──────────────────────────────── gather ──────────────────────────────────────
def _ahash(img, n=12):
    g = img.convert("L").resize((n, n), Image.LANCZOS)
    px = list(g.tobytes())
    avg = sum(px) / len(px)
    return int("".join("1" if p > avg else "0" for p in px), 2)


def _ham(a, b):
    return bin(a ^ b).count("1")


def _open(path):
    try:
        img = Image.open(path)
        img = ImageOps.exif_transpose(img)
        if img.mode in ("RGBA", "LA", "P"):
            img = img.convert("RGBA")
            bg = Image.new("RGB", img.size, (0, 0, 0))
            bg.paste(img, mask=img.split()[-1])
            img = bg
        return img.convert("RGB")
    except Exception as e:  # noqa: BLE001
        print(f"  skip {path.name}: {e}")
        return None


def cmd_gather(a):
    INBOX.mkdir(parents=True, exist_ok=True)
    DATASET.mkdir(parents=True, exist_ok=True)
    sources = [p for p in INBOX.rglob("*") if p.suffix.lower() in IMG_EXT]
    if a.with_game_art:
        sources += sorted(p for p in GAME_ART.glob("*.webp") if not p.name.startswith("card_back"))
    if not sources:
        print(f"Nothing to gather. Put images in {INBOX}" + ("" if a.with_game_art else " (or add --with-game-art)"))
        return 1
    known = {}
    for p in dataset_images():
        img = _open(p)
        if img:
            known[p.name] = _ahash(img)
    added = dupes = small = 0
    for src in sources:
        img = _open(src)
        if img is None:
            continue
        if min(img.size) < 384:
            small += 1
            print(f"  skip {src.name}: too small ({img.size[0]}×{img.size[1]}; want at least 512 on the short side)")
            continue
        h = _ahash(img)
        if any(_ham(h, k) <= a.dupe_bits for k in known.values()):
            dupes += 1
            continue
        img.thumbnail((1024, 1024), Image.LANCZOS)
        stem = re.sub(r"[^a-z0-9_]+", "_", src.stem.lower()).strip("_")[:48] or "img"
        tag = hashlib.sha1(str(src).encode()).hexdigest()[:6]
        name = f"{'game_' if src.is_relative_to(GAME_ART) else ''}{stem}_{tag}.jpg"
        img.save(DATASET / name, "JPEG", quality=94)
        known[name] = h
        added += 1
    total = len(dataset_images())
    print(f"gathered: +{added} new, {dupes} near-duplicates skipped, {small} too small — dataset now {total} image(s) in {DATASET}")
    if total < 15:
        print("  (a style trains best on 20–100 varied images; fewer works, but expect a weaker style)")
    return 0


# ──────────────────────────────── caption ─────────────────────────────────────
CAPTION_SYSTEM = (
    "You write training captions for a style-learning image model. Describe ONLY what is in the "
    "picture: the subject, what it is doing, the setting, the light, the time of day, the camera "
    "framing. NEVER describe the artistic style, medium, technique, brushwork, colour grading or "
    "mood words like 'dark fantasy art' or 'painterly' — the style is what the model must learn "
    "on its own. One sentence, 15 to 35 words, plain English, no quotes, no lists."
)


def _openrouter(payload, timeout=180):
    req = urllib.request.Request(f"{OPENROUTER}/chat/completions", data=json.dumps(payload).encode(),
                                 headers={"Authorization": f"Bearer {os.environ['OPENROUTER_API_KEY']}",
                                          "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.loads(r.read())


def _data_url(path, max_side=768):
    img = _open(Path(path))
    img.thumbnail((max_side, max_side))
    buf = io.BytesIO()
    img.save(buf, "JPEG", quality=88)
    return "data:image/jpeg;base64," + base64.b64encode(buf.getvalue()).decode()


def caption_one(path, mock=False):
    if mock:
        return f"{TRIGGER}, a scene ({path.stem})"
    for attempt in range(3):
        try:
            d = _openrouter({"model": CAPTION_MODEL, "temperature": 0.4, "max_tokens": 200, "messages": [
                {"role": "system", "content": CAPTION_SYSTEM},
                {"role": "user", "content": [{"type": "text", "text": "Caption this image."},
                                             {"type": "image_url", "image_url": {"url": _data_url(path)}}]}]})
            text = (d.get("choices") or [{}])[0].get("message", {}).get("content") or ""
            if isinstance(text, list):
                text = " ".join(p.get("text", "") for p in text if isinstance(p, dict))
            text = re.sub(r"\s+", " ", text).strip().strip('"').rstrip(".")
            if len(text) > 8:
                return f"{TRIGGER}, {text[0].lower() + text[1:]}"
        except (urllib.error.URLError, ValueError, KeyError) as e:
            print(f"  {path.name}: caption attempt {attempt + 1} failed ({e})")
            time.sleep(2 * (attempt + 1))
    return None


def cmd_caption(a):
    imgs = dataset_images()
    if not imgs:
        print("dataset is empty — run gather first")
        return 1
    if not a.mock:
        _env_key("OPENROUTER_API_KEY")
    done = skipped = failed = 0
    for p in imgs:
        txt = p.with_suffix(".txt")
        if txt.exists() and not a.redo:
            skipped += 1
            continue
        if txt.exists() and a.redo and not txt.read_text(encoding="utf-8").startswith(TRIGGER):
            skipped += 1   # hand-edited without the trigger? leave it alone
            continue
        cap = caption_one(p, a.mock)
        if cap:
            txt.write_text(cap + "\n", encoding="utf-8")
            done += 1
            print(f"  {p.name}: {cap}")
        else:
            failed += 1
    print(f"captions: {done} written, {skipped} kept, {failed} failed")
    return 0 if failed == 0 else 2


# ──────────────────────────────── contact sheets ──────────────────────────────
def _font(size):
    for f in (REPO / "client" / "assets" / "fonts" / "CormorantGaramond-Bold.ttf",
              Path("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf")):
        try:
            return ImageFont.truetype(str(f), size)
        except OSError:
            continue
    return ImageFont.load_default()


def _wrap(draw, text, font, width):
    words, lines, cur = [], [], ""
    for w in text.split():   # hard-break words wider than the cell (long file names)
        while draw.textlength(w, font=font) > width and len(w) > 4:
            cut = max(4, int(len(w) * width / draw.textlength(w, font=font)) - 1)
            words.append(w[:cut]); w = w[cut:]
        words.append(w)
    for w in words:
        t = (cur + " " + w).strip()
        if draw.textlength(t, font=font) <= width:
            cur = t
        else:
            lines.append(cur)
            cur = w
    if cur:
        lines.append(cur)
    return lines


def grid(cells, cols, out, cell_w=300, cell_h=440, caption_h=96, title=""):
    """cells: [(image_path or None, caption)]"""
    rows = (len(cells) + cols - 1) // cols
    top = 70 if title else 10
    sheet = Image.new("RGB", (cols * (cell_w + 14) + 14, top + rows * (cell_h + caption_h + 14) + 10), (16, 15, 13))
    d = ImageDraw.Draw(sheet)
    f, ft = _font(18), _font(34)
    if title:
        d.text((16, 16), title, font=ft, fill=(214, 184, 110))
    for i, (path, cap) in enumerate(cells):
        x = 14 + (i % cols) * (cell_w + 14)
        y = top + (i // cols) * (cell_h + caption_h + 14)
        if path and Path(path).exists():
            im = _open(Path(path))
            im.thumbnail((cell_w, cell_h))
            sheet.paste(im, (x + (cell_w - im.width) // 2, y + (cell_h - im.height) // 2))
        else:
            d.rectangle([x, y, x + cell_w, y + cell_h], outline=(90, 70, 50))
            d.text((x + 12, y + cell_h // 2), "failed", font=f, fill=(200, 110, 90))
        for j, line in enumerate(_wrap(d, cap, f, cell_w)[:4]):
            d.text((x, y + cell_h + 6 + j * 22), line, font=f, fill=(210, 200, 180))
    Path(out).parent.mkdir(parents=True, exist_ok=True)
    sheet.save(out, "JPEG", quality=88)
    return out


def cmd_sheet(a):
    imgs = dataset_images()
    if not imgs:
        print("dataset is empty — run gather first")
        return 1
    per = 40
    outs = []
    for page in range(0, len(imgs), per):
        cells = []
        for p in imgs[page:page + per]:
            t = p.with_suffix(".txt")
            cap = t.read_text(encoding="utf-8").strip() if t.exists() else "(no caption yet)"
            cells.append((p, f"{p.name} — {cap}"))
        out = REVIEW / f"dataset_p{page // per + 1}.jpg"
        grid(cells, 8, out, 220, 320, 110, f"Style dataset — {len(imgs)} images (page {page // per + 1})")
        outs.append(out)
    print("dataset sheets:\n  " + "\n  ".join(str(o) for o in outs))
    print("Delete any image (and its .txt) from the dataset folder that shouldn't teach the style, then train.")
    return 0


# ──────────────────────────────── train ───────────────────────────────────────
def _fal():
    _env_key("FAL_KEY")
    try:
        import fal_client  # noqa: WPS433
    except ImportError:
        sys.exit("FATAL: pip install fal-client   (see `style_lora.py doctor`)")
    return fal_client


def _find_url(obj, want=("lora", "safetensors")):
    """The trainer's result names its LoRA file; find it without depending on the exact key."""
    if isinstance(obj, dict):
        for k, v in obj.items():
            if isinstance(v, dict) and isinstance(v.get("url"), str) and any(w in k.lower() for w in want):
                return v["url"]
        for v in obj.values():
            u = _find_url(v, want)
            if u:
                return u
    elif isinstance(obj, list):
        for v in obj:
            u = _find_url(v, want)
            if u:
                return u
    elif isinstance(obj, str) and obj.startswith("http") and obj.endswith(".safetensors"):
        return obj
    return None


def cmd_train(a):
    imgs = dataset_images()
    if len(imgs) < 5:
        print(f"only {len(imgs)} image(s) in the dataset — gather at least 10 (20–100 is better)")
        return 1
    missing = [p.name for p in imgs if not p.with_suffix(".txt").exists()]
    if missing and not a.no_captions:
        print(f"{len(missing)} image(s) have no caption — run caption first (or pass --no-captions)")
        return 1
    name = a.name or time.strftime("v%Y%m%d-%H%M")
    run = RUNS / name
    run.mkdir(parents=True, exist_ok=True)
    zpath = run / "dataset.zip"
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        for p in imgs:
            z.write(p, p.name)
            t = p.with_suffix(".txt")
            if t.exists() and not a.no_captions:
                z.write(t, t.name)
    print(f"{name}: {len(imgs)} images zipped ({zpath.stat().st_size // 1024} KB)")
    args = {"trigger_word": TRIGGER, "is_style": True, "steps": a.steps}
    if a.dry_run:
        print(f"dry run — would upload {zpath} and train {TRAINER} with {json.dumps(args)}")
        return 0
    fal = _fal()
    print("uploading…", flush=True)
    args["images_data_url"] = fal.upload_file(zpath)
    print(f"training on {TRAINER} ({a.steps} steps, ~$2 per 1000) — usually 5–15 minutes…", flush=True)
    t0 = time.time()
    logf = open(run / "train.log", "w", encoding="utf-8")

    def upd(st):
        for lg in getattr(st, "logs", None) or []:
            msg = lg.get("message") if isinstance(lg, dict) else str(lg)
            if msg:
                logf.write(msg + "\n")
                if "step" in msg.lower() or "error" in msg.lower():
                    print(f"  [{int(time.time() - t0)}s] {msg[:140]}", flush=True)

    result = fal.subscribe(TRAINER, arguments=args, with_logs=True, on_queue_update=upd, client_timeout=3600)
    logf.close()
    (run / "result.json").write_text(json.dumps(result, indent=1), encoding="utf-8")
    url = _find_url(result)
    if not url:
        print(f"training finished but no LoRA file in the reply — see {run / 'result.json'}")
        return 2
    s = load_state()
    s["trigger"] = TRIGGER
    s["models"] = [m for m in s.get("models", []) if m.get("name") != name] + [{
        "name": name, "lora_url": url, "trigger": TRIGGER, "steps": a.steps, "images": len(imgs),
        "trained": time.strftime("%Y-%m-%d %H:%M"), "trainer": TRAINER}]
    s["active"] = name
    save_state(s)
    print(f"trained in {int(time.time() - t0)}s → {url}\nactive style: {name} (pipeline/style_lora.json — commit it)")
    print("next: style_lora.py test")
    return 0


def cmd_use(a):
    s = load_state()
    if not any(m["name"] == a.name for m in s.get("models", [])):
        print("no such model; trained: " + ", ".join(m["name"] for m in s.get("models", [])))
        return 1
    s["active"] = a.name
    save_state(s)
    print(f"active style: {a.name}")
    return 0


# ──────────────────────────────── painting ────────────────────────────────────
def _download(url, out):
    with urllib.request.urlopen(url, timeout=120) as r:
        data = r.read()
    Image.open(io.BytesIO(data)).convert("RGB").save(out, "PNG")
    return True


def paint_lora(prompt, out, scale=1.0, seed=None, width=W, height=H):
    """Paint with the active trained style. Returns True on success."""
    m = active_model()
    if not m:
        print("  no trained style yet — run style_lora.py train")
        return False
    fal = _fal()
    trig = m.get("trigger", TRIGGER)
    args = {"prompt": prompt if prompt.lower().startswith(trig) else f"{trig}, {prompt}",
            "loras": [{"path": m["lora_url"], "scale": scale}],
            "image_size": {"width": width, "height": height},
            "num_inference_steps": 28, "guidance_scale": 3.5, "num_images": 1,
            "output_format": "png"}
    if seed is not None:
        args["seed"] = seed
    # Optional fields first; if the endpoint rejects any, retry with just the documented core,
    # then with a preset size — a schema change on their side mustn't stop the pipeline.
    tries = [args,
             {k: v for k, v in args.items() if k not in ("enable_safety_checker", "output_format")},
             {**{k: v for k, v in args.items() if k not in ("enable_safety_checker", "output_format")}, "image_size": "portrait_4_3"}]
    for i, arg in enumerate(tries):
        try:
            r = fal.subscribe(PAINTER, arguments=arg, client_timeout=600)
            url = ((r or {}).get("images") or [{}])[0].get("url")
            if url and _download(url, out):
                return True
            print(f"  lora paint: no image in the reply ({str(r)[:160]})")
        except Exception as e:  # noqa: BLE001
            print(f"  lora paint attempt {i + 1} failed: {str(e)[:200]}")
    return False


def pick_refs(n=4):
    s = load_state()
    refs = [DATASET / r for r in s.get("refs", []) if (DATASET / r).exists()]
    if len(refs) < n:
        pool = [p for p in dataset_images() if p not in refs]
        # spread the picks across the set rather than taking the first few
        step = max(1, len(pool) // max(1, n - len(refs)))
        refs += pool[::step][: n - len(refs)]
    return refs[:n]


def _save_image_bytes(raw, out):
    Image.open(io.BytesIO(raw)).convert("RGB").save(out, "PNG")
    return True


def _save_image_ref(url, out):
    """A data: URL or an http(s) URL from an image reply → PNG at out."""
    if url.startswith("data:"):
        return _save_image_bytes(base64.b64decode(url.split(",", 1)[1]), out)
    if url.startswith("http"):
        return _download(url, out)
    return False


def paint_refs(prompt, out, refs=None):
    """Reference mode: Gemini's image model with example images attached. No training.
    Self-contained (its own OpenRouter call) so it never depends on the rest of the pipeline."""
    _env_key("OPENROUTER_API_KEY")
    refs = refs or pick_refs()
    lead = ("Paint a new card illustration in EXACTLY the artistic style of the attached reference images — "
            "same brushwork, palette, lighting and finish — but a completely new scene: ")
    content = [{"type": "text", "text": lead + with_guide(prompt)}] + \
              [{"type": "image_url", "image_url": {"url": _data_url(r, 1024)}} for r in refs]
    try:
        d = _openrouter({"model": REFS_MODEL, "modalities": ["image", "text"],
                         "image_config": {"aspect_ratio": "2:3"},
                         "messages": [{"role": "user", "content": content}]}, timeout=300)
        msg = d["choices"][0]["message"]
        for img in msg.get("images") or []:
            url = (img.get("image_url") or {}).get("url") or img.get("url") or ""
            if url and _save_image_ref(url, out):
                return True
        for part in msg.get("content") if isinstance(msg.get("content"), list) else []:
            u = (part.get("image_url") or {}).get("url") if isinstance(part, dict) else None
            if u and _save_image_ref(u, out):
                return True
        print(f"  refs: no image in the reply ({str(msg.get('content'))[:160]})")
    except (urllib.error.URLError, ValueError, KeyError, IndexError) as e:
        print(f"  refs paint failed: {str(e)[:200]}")
    return False


def paint_flux(prompt, out):
    """Today's generator (FLUX through OpenRouter), for the side-by-side comparison."""
    _env_key("OPENROUTER_API_KEY")
    req = urllib.request.Request(f"{OPENROUTER}/images/generations",
                                 data=json.dumps({"model": FLUX_MODEL, "prompt": prompt, "n": 1, "size": f"{W}x{H}"}).encode(),
                                 headers={"Authorization": f"Bearer {os.environ['OPENROUTER_API_KEY']}",
                                          "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=300) as r:
            d = json.loads(r.read())
        for item in d.get("data", []):
            if item.get("b64_json"):
                return _save_image_bytes(base64.b64decode(item["b64_json"]), out)
            if item.get("url"):
                return _download(item["url"], out)
        print(f"  flux: no image in the reply ({json.dumps(d)[:160]})")
    except (urllib.error.URLError, ValueError) as e:
        print(f"  flux paint failed: {str(e)[:200]}")
    return False


def _test_prompts(n):
    """Real card prompts from the art ledger when there is one, else card names and flavour."""
    ledger = Path(os.environ.get("ART_LEDGER", str(Path.home() / "runewake_art_archive" / "art_ledger.json")))
    out = []
    try:
        led = json.loads(ledger.read_text(encoding="utf-8"))
        for cid, e in sorted(led.get("cards", {}).items()):
            if e.get("prompt") and not e.get("mock"):
                out.append((cid, e["prompt"]))
    except (OSError, ValueError):
        pass
    if len(out) < n:
        for f in sorted((REPO / "client" / "content" / "cards").glob("*.json")):
            d = json.loads(f.read_text(encoding="utf-8"))
            for c in (d.get("cards") if isinstance(d, dict) else d) or []:
                if c.get("flavor"):
                    out.append((c["id"], f"{c['name']}: {c['flavor']} Fantasy trading-card illustration, one clear subject."))
    # spread across strata
    step = max(1, len(out) // n)
    return out[::step][:n]


def cmd_test(a):
    prompts = _test_prompts(a.n)
    if not prompts:
        print("no prompts found")
        return 1
    modes = [m.strip() for m in a.modes.split(",") if m.strip()]
    name = (active_model() or {}).get("name", "untrained")
    # FABLE-STYLE-2: one folder per trained style, and tiles already painted are kept — so a test cut
    # short (a shell time limit, a dropped connection) picks up where it stopped when run again.
    run = RUNS / f"test_{name}"
    if a.fresh and run.exists():
        shutil.rmtree(run)
    run.mkdir(parents=True, exist_ok=True)
    if "guide" in modes and not guide_clause(force=True):
        print("no house-style guide yet — run `style_lora.py distill` first (or drop 'guide' from --modes)")
        return 1
    cells = []
    for cid, prompt in prompts:
        for mode in modes:
            out = run / f"{cid}_{mode}.png"
            if out.exists() and out.stat().st_size > 0 and not a.mock:
                ok = True
            elif a.mock:
                Image.new("RGB", (W, H), (40, 34, 30)).save(out)
                ok = True
            elif mode == "lora":
                ok = paint_lora(prompt, out, a.scale)
            elif mode == "refs":
                ok = paint_refs(prompt, out)
            elif mode == "guide":
                ok = paint_flux(with_guide(prompt, force=True), out)
            else:
                ok = paint_flux(prompt, out)
            label = {"lora": f"TRAINED ({name})", "refs": "REFERENCE MODE", "flux": "TODAY (FLUX)",
                     "guide": "FLUX + HOUSE GUIDE"}.get(mode, mode)
            print(f"  {cid:<28} {label:<24} {'ok' if ok else 'FAILED'}", flush=True)
            cells.append((out if ok else None, f"{label} — {cid}"))
    out = REVIEW / f"test_{name}{'_' + '-'.join(modes) if a.modes_in_name else ''}.jpg"
    grid(cells, len(modes), out, 360, 526, 50, f"Style test — {name} — same prompt across each row")
    print(f"sheet: {out}")
    return 0


# ──────────────────────────────── house-style guide (FABLE-STYLE-2) ─────────────
DISTILL_LOOK = """You are the art director of a fantasy card game. You are shown a set of its card paintings.
Describe ONLY the visual style they share — never the subjects. Cover: medium and finish; brushwork and
edges; how light and shadow are handled; palette tendencies and saturation; value structure; texture and
level of detail; how figures, creatures and faces are rendered; how backgrounds and atmosphere recede; the
overall mood. Be concrete and visual. Note anything that varies a lot between images as "varies".
Reply as JSON only: {"observations": ["...", "..."]}"""

DISTILL_MERGE = """You are the art director of a fantasy card game. Below are notes on the shared style of its card
paintings, gathered from several batches of images. Write the house style down. Reply as JSON only:
{"guide_md": "a markdown style guide for human artists: a one-paragraph summary, then short sections
  (Medium & finish, Brushwork, Light, Colour, Detail & texture, Figures, Backgrounds & atmosphere, Mood),
  then a short 'Keep it on-model' checklist",
 "style_clause": "ONE line for an image generator, at most 60 words: comma-separated visual descriptors of
  the style only. No subjects, no composition or camera, no artist names, no negatives ('no …', 'without …'),
  no quality boilerplate",
 "drifts": ["the 3-6 most common ways a new painting could look off-model, in plain words"]}"""


def _chat_text(d):
    text = (d.get("choices") or [{}])[0].get("message", {}).get("content") or ""
    if isinstance(text, list):
        text = " ".join(p.get("text", "") for p in text if isinstance(p, dict))
    return text


def _json_from(text):
    """The first JSON object in a model reply (models like to wrap it in ``` fences)."""
    m = re.search(r"\{.*\}", text, re.S)
    if not m:
        raise ValueError(f"no JSON in the reply: {text[:160]!r}")
    return json.loads(m.group(0))


def _spread(items, n):
    if len(items) <= n:
        return list(items)
    step = len(items) / n
    return [items[int(i * step)] for i in range(n)]


def guide_clause(force=False):
    g = load_state().get("style_guide") or {}
    return (g.get("clause") or "") if (force or g.get("enabled")) else ""


def with_guide(prompt, gen=None, force=False):
    """The prompt with the house-style clause added, when the guide is ON (or force). Never twice."""
    clause = guide_clause(force)
    if not clause or clause[:40] in prompt:
        return prompt
    return f"{prompt.rstrip().rstrip('.')}. House style: {clause.rstrip('.')}."


def cmd_distill(a):
    pool = dataset_images() or sorted(GAME_ART.glob("*.webp"))
    if not pool:
        print("no images: run gather first (or the game art folder is empty)")
        return 1
    picks = _spread(pool, a.n)
    print(f"distilling the house style from {len(picks)} of {len(pool)} image(s) with {DISTILL_MODEL}", flush=True)
    notes = []
    if a.mock:
        notes = ["thick oil paint", "warm key light against deep shadow"]
    else:
        _env_key("OPENROUTER_API_KEY")
        for i in range(0, len(picks), a.batch):
            batch = picks[i:i + a.batch]
            content = [{"type": "text", "text": f"{len(batch)} card paintings from the game:"}] + \
                      [{"type": "image_url", "image_url": {"url": _data_url(p, 640)}} for p in batch]
            for attempt in range(3):
                try:
                    d = _openrouter({"model": DISTILL_MODEL, "temperature": 0.3, "messages": [
                        {"role": "system", "content": DISTILL_LOOK}, {"role": "user", "content": content}]}, timeout=300)
                    got = _json_from(_chat_text(d)).get("observations") or []
                    notes += [str(x) for x in got]
                    print(f"  batch {i // a.batch + 1}: {len(got)} observation(s)", flush=True)
                    break
                except (urllib.error.URLError, ValueError, KeyError) as e:
                    print(f"  batch {i // a.batch + 1} attempt {attempt + 1} failed ({str(e)[:160]})", flush=True)
                    time.sleep(3 * (attempt + 1))
        if not notes:
            print("no observations came back — nothing written")
            return 1
    if a.mock:
        out = {"guide_md": "# Runewake house style (mock)\n\nThick oil paint.", "drifts": ["too smooth"],
               "style_clause": "hand-painted oil on canvas, thick visible impasto strokes, warm key light against deep umber shadow"}
    else:
        out = None
        for attempt in range(3):
            try:
                d = _openrouter({"model": DISTILL_MODEL, "temperature": 0.3, "messages": [
                    {"role": "system", "content": DISTILL_MERGE},
                    {"role": "user", "content": "Notes:\n- " + "\n- ".join(notes)}]}, timeout=300)
                out = _json_from(_chat_text(d))
                if out.get("style_clause"):
                    break
            except (urllib.error.URLError, ValueError, KeyError) as e:
                print(f"  merge attempt {attempt + 1} failed ({str(e)[:160]})", flush=True)
                time.sleep(3 * (attempt + 1))
        if not out or not out.get("style_clause"):
            print("the merge step returned no style clause — nothing written")
            return 1
    clause = re.sub(r"\s+", " ", str(out["style_clause"])).strip().strip('"').rstrip(".")
    words = clause.split()
    if len(words) > 70:
        clause = " ".join(words[:70]).rstrip(",")
    s = load_state()
    was_on = bool((s.get("style_guide") or {}).get("enabled"))
    s["style_guide"] = {"clause": clause, "drifts": out.get("drifts") or [], "enabled": was_on,
                        "images": len(picks), "model": DISTILL_MODEL, "made": time.strftime("%Y-%m-%d %H:%M")}
    save_state(s)
    REVIEW.mkdir(parents=True, exist_ok=True)
    md = str(out.get("guide_md") or "").strip()
    md += "\n\n## The clause every art prompt gets (when the guide is on)\n\n> " + clause + "\n"
    if out.get("drifts"):
        md += "\n## Watch for\n\n" + "\n".join(f"- {x}" for x in out["drifts"]) + "\n"
    md += f"\n_Distilled from {len(picks)} images by {DISTILL_MODEL}, {time.strftime('%Y-%m-%d')}._\n"
    (REVIEW / "style_guide.md").write_text(md, encoding="utf-8")
    print(f"style clause ({len(clause.split())} words):\n  {clause}")
    print(f"guide: {REVIEW / 'style_guide.md'}")
    print("the guide is ON — every art prompt gets the new clause" if was_on
          else "the guide is OFF — `style_lora.py guide on` adds the clause to every art prompt")
    return 0


def cmd_guide(a):
    s = load_state()
    g = s.get("style_guide")
    if not g:
        print("no house-style guide yet — run `style_lora.py distill` first")
        return 1
    if a.state in ("on", "off"):
        g["enabled"] = a.state == "on"
        save_state(s)
    print(f"house-style guide: {'ON' if g.get('enabled') else 'OFF'} ({g.get('images')} images, {g.get('made')})")
    print(f"  clause: {g.get('clause')}")
    return 0


# ──────────────────────────────── art_director hook ───────────────────────────
HOOK_MARK = "FABLE-STYLE: \"lora\" paints with the trained house style"
GUIDE_MARK = "FABLE-STYLE-2: the house-style clause rides on every prompt while `style_lora.py guide on`"


def cmd_hook(a):
    """Teach art_director.py the generators `lora` and `refs` — only if its render code still looks
    the way this was written against. Touches nothing otherwise, and is safe to run twice."""
    ad = TOOLS / "art_director.py"
    try:
        text = ad.read_text(encoding="utf-8")
    except OSError:
        print("no tools/art_director.py — skipping the hook (style_lora.py works on its own)")
        return 0
    m = re.search(r"^(?P<ind>[ \t]+)def paint\(prompt\):\n", text, re.M)
    if "style_lora" in text:
        # FABLE-STYLE-2: an already-hooked file gets the house-style guide on every prompt.
        if "with_guide" in text:
            print("art_director.py already knows the lora/refs generators and the house guide — nothing to do")
            return 0
        if not m:
            print("art_director.py's render code has changed shape — NOT adding the guide hook; tell Fable")
            return 1
        ind = m.group("ind") + "    "
        guide = (f"{ind}# {GUIDE_MARK}\n"
                 f"{ind}import style_lora  # noqa: E402\n"
                 f"{ind}prompt = style_lora.with_guide(prompt, gen)\n")
        return _write_hook(ad, text, text[:m.end()] + guide + text[m.end():], guide, a.dry_run)
    if not m or "gia.via_" not in text[m.end():m.end() + 600]:
        print("art_director.py's render code has changed shape — NOT hooking it. Use style_lora.py test "
              "(and paint_lora / paint_refs) directly; tell Fable so the hook can be updated.")
        return 1
    ind = m.group("ind") + "    "
    hook = (f"{ind}# {GUIDE_MARK}\n"
            f"{ind}import style_lora  # noqa: E402\n"
            f"{ind}prompt = style_lora.with_guide(prompt, gen)\n"
            f"{ind}# {HOOK_MARK}, \"refs\" with example images attached (tools/style_lora.py)\n"
            f"{ind}if gen in (\"lora\", \"refs\"):\n"
            f"{ind}    import style_lora  # noqa: E402\n"
            f"{ind}    return style_lora.paint_lora(prompt, out) if gen == \"lora\" else style_lora.paint_refs(prompt, out)\n")
    return _write_hook(ad, text, text[:m.end()] + hook + text[m.end():], hook, a.dry_run)


def _write_hook(ad, text, new, inserted, dry_run):
    if dry_run:
        print("would insert after `def paint(prompt):` in art_director.py:\n" + inserted)
        return 0
    try:
        compile(new, str(ad), "exec")
    except SyntaxError as e:
        print(f"the hooked file would not compile ({e}) — nothing changed")
        return 1
    shutil.copy2(ad, ad.with_suffix(".py.bak_style"))
    ad.write_text(new, encoding="utf-8")
    ad.with_suffix(".py.bak_style").unlink()
    print("art_director.py: --models lora / refs, and the house-style guide on every prompt (while `guide on`)")
    return 0


# ──────────────────────────────── doctor ──────────────────────────────────────
def cmd_doctor(a):
    ok = True
    def line(good, msg):
        nonlocal ok
        ok &= good
        print(("  ✓ " if good else "  ✗ ") + msg)
    print("style_lora doctor")
    for d in (INBOX, DATASET, RUNS):
        d.mkdir(parents=True, exist_ok=True)
    inbox = [p for p in INBOX.rglob("*") if p.suffix.lower() in IMG_EXT]
    line(True, f"inbox: {INBOX} — {len(inbox)} image(s) waiting")
    ds = dataset_images()
    caps = sum(1 for p in ds if p.with_suffix(".txt").exists())
    line(True, f"dataset: {len(ds)} image(s), {caps} captioned")
    line(bool(_env_key("OPENROUTER_API_KEY", required=False)), "OPENROUTER_API_KEY (captions, reference mode, FLUX comparison)")
    have_fal = bool(_env_key("FAL_KEY", required=False))
    print(("  ✓ " if have_fal else "  · ") + "FAL_KEY (optional: fal training) " + ("set" if have_fal else "not set — fine, use kaggle_lora.py"))
    m = active_model()
    line(True, f"active trained style: {m['name'] + ' (' + str(m['images']) + ' images)' if m else 'none yet'}")
    g = load_state().get("style_guide")
    line(True, f"house-style guide: {('ON' if g.get('enabled') else 'OFF') + ', ' + str(g.get('images')) + ' images' if g else 'not distilled yet (style_lora.py distill)'}")
    print("all good" if ok else "fix the ✗ lines, then run doctor again")
    return 0 if ok else 1


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("doctor")
    g = sub.add_parser("gather")
    g.add_argument("--with-game-art", action="store_true", help="also learn from the game's approved card paintings")
    g.add_argument("--dupe-bits", type=int, default=6, help="near-duplicate threshold (lower = stricter)")
    c = sub.add_parser("caption")
    c.add_argument("--redo", action="store_true")
    c.add_argument("--mock", action="store_true")
    sub.add_parser("sheet")
    t = sub.add_parser("train")
    t.add_argument("--steps", type=int, default=1000)
    t.add_argument("--name")
    t.add_argument("--no-captions", action="store_true")
    t.add_argument("--dry-run", action="store_true")
    hk = sub.add_parser("hook-art-director")
    hk.add_argument("--dry-run", action="store_true")
    u = sub.add_parser("use")
    u.add_argument("name")
    te = sub.add_parser("test")
    te.add_argument("--n", type=int, default=6)
    te.add_argument("--scale", type=float, default=1.0)
    te.add_argument("--modes", default="lora,refs,flux", help="any of lora, refs, flux, guide")
    te.add_argument("--mock", action="store_true")
    te.add_argument("--fresh", action="store_true", help="repaint every tile (default: keep tiles already painted)")
    te.add_argument("--modes-in-name", action="store_true", help="name the sheet after the modes too")
    di = sub.add_parser("distill")
    di.add_argument("--n", type=int, default=24, help="how many images the vision model studies")
    di.add_argument("--batch", type=int, default=8, help="images per look")
    di.add_argument("--mock", action="store_true")
    gu = sub.add_parser("guide")
    gu.add_argument("state", nargs="?", choices=["on", "off", "show"], default="show")
    a = ap.parse_args()
    return {"doctor": cmd_doctor, "gather": cmd_gather, "caption": cmd_caption, "sheet": cmd_sheet,
            "train": cmd_train, "use": cmd_use, "test": cmd_test, "hook-art-director": cmd_hook,
            "distill": cmd_distill, "guide": cmd_guide}[a.cmd](a)


if __name__ == "__main__":
    sys.exit(main())

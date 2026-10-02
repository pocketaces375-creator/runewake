#!/usr/bin/env python3
"""tools/kaggle_lora.py — FABLE-STYLE-2: train the Runewake house style for FREE on Kaggle's GPUs.

Trikzos: "I don't want to have to use fal. Is there a free service we could use?" — and the mini PC
has no GPU. Kaggle gives every verified account free GPU time (about 30 hours a week, T4 cards,
up to 12 hours per run). This tool drives it from the mini PC: it uploads the style dataset
(the same one style_lora.py gathers and captions), runs a training script on Kaggle that teaches
Stable Diffusion XL the house style as a LoRA (a small add-on file), downloads the result, and
paints with it — all as private Kaggle datasets and notebooks under Trikzos's account.

SDXL (stabilityai/stable-diffusion-xl-base-1.0) is licensed CreativeML Open RAIL++-M: images
made with it can be used commercially. Only train on images you have the rights to.

ONE-TIME SETUP (Trikzos, in a browser — the tool cannot do these):
  1. Sign up at kaggle.com, then Settings → Phone verification (needed for GPU + internet).
  2. Settings → API → "Generate New Token". Put it on the mini PC in ~/.hermes/.env as
         KAGGLE_API_TOKEN=…         (new-style token)   and   KAGGLE_USERNAME=<your kaggle username>
     (a legacy kaggle.json in ~/.kaggle/ works too.)
  3. pip install --user -U kaggle

THE LOOP
  kaggle_lora.py doctor                 checks the CLI, the token, the username and the dataset
  kaggle_lora.py push-data              uploads ~/runewake_art_archive/style_lora/dataset (images +
                                        captions) as a PRIVATE Kaggle dataset "runewake-style-data"
  kaggle_lora.py train --name v1        pushes the training notebook and waits (30–90 min on a T4);
       [--steps 1500] [--rank 16]       downloads the LoRA + 4 sample paintings; v1 becomes active
       [--no-wait]                      (then later: kaggle_lora.py status / fetch)
  kaggle_lora.py paint --test 6         paints test prompts with the active style on Kaggle and makes
  kaggle_lora.py paint --ids a,b,c      a review sheet; --ids uses the art ledger's prompts, and with
       [--to-ledger] [--per 2]          --to-ledger the paintings become art_director candidates
                                        (then art_director.py sheet / approve work as usual)
       [--scales 0.8,1.0,1.2]           the same prompts at several style strengths, side by side
                                        (the house-style guide does NOT apply here — the LoRA is the style)
  kaggle_lora.py status [train|paint]   what the Kaggle run is doing
  kaggle_lora.py fetch [train|paint]    download a finished run (after --no-wait or a dropped wait)
  kaggle_lora.py use <name>             switch the active trained style

FILES
  ~/runewake_art_archive/style_lora/kaggle/        staging folders pushed to Kaggle
  ~/runewake_art_archive/style_lora/runs/kaggle_*  downloaded LoRAs, samples, logs, paintings
  pipeline/style_lora.json  ("kaggle" section)     trained styles + the active one (commit it)
  artifacts/art_review/style_lora/kaggle_*.jpg     review sheets (commit them)

SAFE: standalone — reads the style dataset and the art ledger; writes only the paths above (and, with
--to-ledger, adds candidates to the art ledger the way art_director render does). No game code.
Set KAGGLE_CLI to use another kaggle executable (Fable's sandbox uses a stand-in).
"""
import argparse
import hashlib
import json
import os
import re
import shlex
import shutil
import subprocess
import sys
import time
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
REPO = TOOLS.parent
HOME = Path(os.environ.get("STYLE_LORA_HOME", str(Path.home() / "runewake_art_archive" / "style_lora")))
DATASET = HOME / "dataset"
RUNS = HOME / "runs"
KWORK = HOME / "kaggle"
STATE = REPO / "pipeline" / "style_lora.json"
REVIEW = REPO / "artifacts" / "art_review" / "style_lora"
TRIGGER = os.environ.get("STYLE_LORA_TRIGGER", "rnwk style")

DATA_SLUG, LORAS_SLUG = "runewake-style-data", "runewake-style-loras"
TRAIN_SLUG, PAINT_SLUG = "runewake-style-train", "runewake-style-paint"
BASE_MODEL = "stabilityai/stable-diffusion-xl-base-1.0"
VAE_FIX = "madebyollin/sdxl-vae-fp16-fix"
# Pinned together so the Kaggle image's newer packages can't break the official training script.
DIFFUSERS_TAG = "v0.32.2"
PIP = ["diffusers==0.32.2", "peft==0.14.0", "accelerate==1.2.1", "transformers==4.47.1",
       "huggingface_hub==0.27.1", "datasets==3.2.0", "tensorboard", "ftfy", "Jinja2"]
SCRIPT_URL = (f"https://raw.githubusercontent.com/huggingface/diffusers/{DIFFUSERS_TAG}"
              "/examples/text_to_image/train_text_to_image_lora_sdxl.py")
W, H = 832, 1216


# ──────────────────────────────── keys, state, CLI ────────────────────────────
def _env(name):
    v = os.environ.get(name, "")
    if not v:
        env_file = Path(os.environ.get("ART_ENV_FILE", str(Path.home() / ".hermes" / ".env")))
        try:
            for line in env_file.read_text(encoding="utf-8").splitlines():
                m = re.match(rf"\s*(?:export\s+)?{name}\s*=\s*(.+?)\s*$", line)
                if m:
                    v = m.group(1).strip().strip('"').strip("'")
        except OSError:
            pass
        if v:
            os.environ[name] = v
    return v


def username():
    u = _env("KAGGLE_USERNAME")
    if not u:
        try:
            u = json.loads((Path.home() / ".kaggle" / "kaggle.json").read_text()).get("username", "")
        except (OSError, ValueError):
            u = ""
    return u


def _need_user():
    u = username()
    if not u:
        sys.exit("FATAL: KAGGLE_USERNAME is not set — add KAGGLE_USERNAME=<your kaggle username> to ~/.hermes/.env")
    return u


# FABLE-STYLE-3: the working copy of the state lives OUTSIDE the repo, next to the dataset, because
# a working-tree reset between commands (the foreman) wiped an uncommitted pipeline/style_lora.json.
# Every save also writes the repo copy, which is the one to commit; a fresh machine starts from it.
STATE_HOME = HOME / "state.json"


def load_state():
    for f in (STATE_HOME, STATE):
        try:
            return json.loads(f.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            continue
    return {"trigger": TRIGGER, "models": [], "active": None, "refs": []}


def save_state(s):
    text = json.dumps(s, indent=1) + "\n"
    for f in (STATE_HOME, STATE):
        f.parent.mkdir(parents=True, exist_ok=True)
        f.write_text(text, encoding="utf-8")


def kstate(s=None):
    s = s if s is not None else load_state()
    return s.setdefault("kaggle", {"models": [], "active": None, "latest_train_output": None, "loras_uploaded": []})


def kaggle(*args, check=True, quiet=False):
    """Run the kaggle CLI. Returns (returncode, combined output)."""
    _env("KAGGLE_API_TOKEN"); _env("KAGGLE_USERNAME"); _env("KAGGLE_KEY")
    exe = shlex.split(os.environ.get("KAGGLE_CLI", "kaggle"))
    try:
        r = subprocess.run(exe + [str(a) for a in args], capture_output=True, text=True, timeout=1800)
    except FileNotFoundError:
        sys.exit("FATAL: the kaggle command is not installed — pip install --user -U kaggle")
    out = (r.stdout or "") + (r.stderr or "")
    if not quiet:
        for line in out.strip().splitlines()[-12:]:
            print("   kaggle: " + line)
    if check and r.returncode != 0:
        sys.exit(f"FATAL: kaggle {' '.join(map(str, args[:2]))} failed (exit {r.returncode}) — see the lines above")
    return r.returncode, out


def dataset_images():
    return sorted(DATASET.glob("*.jpg"))


# ──────────────────────────────── doctor ──────────────────────────────────────
def cmd_doctor(a):
    ok = True
    def line(good, msg):
        nonlocal ok
        ok &= good
        print(("  ✓ " if good else "  ✗ ") + msg)
    print("kaggle_lora doctor")
    exe = shlex.split(os.environ.get("KAGGLE_CLI", "kaggle"))
    have_cli = shutil.which(exe[0]) is not None or Path(exe[0]).exists()
    ver = ""
    if have_cli:
        rc, ver = kaggle("--version", check=False, quiet=True)
        have_cli = rc == 0
    line(have_cli, f"kaggle CLI {ver.strip() if have_cli else '— pip install --user -U kaggle'}")
    tok = _env("KAGGLE_API_TOKEN") or (Path.home() / ".kaggle" / "access_token").exists() \
        or (Path.home() / ".kaggle" / "kaggle.json").exists() or bool(_env("KAGGLE_KEY"))
    line(bool(tok), "Kaggle token (KAGGLE_API_TOKEN in ~/.hermes/.env, or ~/.kaggle/access_token or kaggle.json) — "
                    "kaggle.com → Settings → API → Generate New Token")
    u = username()
    line(bool(u), f"KAGGLE_USERNAME {u or '— add KAGGLE_USERNAME=<your kaggle username> to ~/.hermes/.env'}")
    if have_cli and tok and u:
        rc, out = kaggle("datasets", "list", "--mine", check=False, quiet=True)
        line(rc == 0, "the token works (listed your datasets)" if rc == 0 else f"the token was refused: {out.strip()[-160:]}")
    ds = dataset_images()
    caps = sum(1 for p in ds if p.with_suffix(".txt").exists())
    line(len(ds) >= 10, f"style dataset: {len(ds)} image(s), {caps} captioned ({DATASET}) — style_lora.py gather + caption")
    k = kstate()
    print(f"  · trained styles: {', '.join(m['name'] for m in k['models']) or 'none yet'}; active: {k.get('active') or '—'}")
    print("  · GPU + internet on Kaggle need a phone-verified account (kaggle.com → Settings → Phone verification)")
    print("all good" if ok else "fix the ✗ lines, then run doctor again")
    return 0 if ok else 1


# ──────────────────────────────── push-data ───────────────────────────────────
def _stage_dataset():
    imgs = dataset_images()
    if not imgs:
        sys.exit("the style dataset is empty — run style_lora.py gather (and caption) first")
    stage = KWORK / "data"
    if stage.exists():
        shutil.rmtree(stage)
    stage.mkdir(parents=True)   # flat: images + metadata.jsonl at the dataset's root
    rows, missing = [], 0
    for i, p in enumerate(imgs):
        name = f"{i:04d}_{re.sub(r'[^a-z0-9]+', '_', p.stem.lower())[:40]}.jpg"
        shutil.copy2(p, stage / name)
        cap = p.with_suffix(".txt")
        text = cap.read_text(encoding="utf-8").strip() if cap.exists() else ""
        if not text:
            missing += 1
            text = TRIGGER
        if TRIGGER not in text:
            text = f"{TRIGGER}, {text}"
        rows.append({"file_name": name, "text": text})
    with open(stage / "metadata.jsonl", "w", encoding="utf-8") as f:
        for r in rows:
            f.write(json.dumps(r, ensure_ascii=False) + "\n")
    return stage, len(rows), missing


def cmd_push_data(a):
    u = _need_user()
    stage, n, missing = _stage_dataset()
    (stage / "dataset-metadata.json").write_text(json.dumps(
        {"title": DATA_SLUG, "id": f"{u}/{DATA_SLUG}", "licenses": [{"name": "unknown"}]}, indent=1))
    print(f"staged {n} image(s) ({missing} without a caption — they train on the trigger word alone)")
    exists = kaggle("datasets", "status", f"{u}/{DATA_SLUG}", check=False, quiet=True)[0] == 0
    if exists:
        kaggle("datasets", "version", "-p", stage, "-m", a.note or f"{n} images")
    else:
        kaggle("datasets", "create", "-p", stage)   # private unless --public
    s = load_state()
    k = kstate(s)
    k["data"] = {"images": n, "pushed": time.strftime("%Y-%m-%d %H:%M")}
    save_state(s)
    print(f"dataset {u}/{DATA_SLUG}: {'new version' if exists else 'created (private)'} — Kaggle takes a minute to process it")
    return 0


def _wait_dataset(ref, limit=900):
    t0 = time.time()
    while time.time() - t0 < limit:
        rc, out = kaggle("datasets", "status", ref, check=False, quiet=True)
        if rc == 0 and "ready" in out.lower():
            return True
        if rc == 0 and "error" in out.lower():
            sys.exit(f"FATAL: Kaggle could not process {ref}: {out.strip()[-200:]}")
        time.sleep(20)
    print(f"  {ref} is still processing after {limit // 60} min — pushing anyway")
    return False


# ──────────────────────────────── the scripts that run ON Kaggle ──────────────
TRAIN_PY = r'''# Runewake house-style LoRA training — written by tools/kaggle_lora.py (FABLE-STYLE-2). Runs on Kaggle.
import glob, json, os, shutil, subprocess, sys, time, urllib.request
CFG = json.loads(r"""__CFG__""")
T0 = time.time()
REPORT = {"name": CFG["name"], "ok": False, "stage": "start"}
def save_report():
    REPORT["minutes"] = round((time.time() - T0) / 60, 1)
    json.dump(REPORT, open("/kaggle/working/report.json", "w"), indent=1)
def sh(cmd):
    print("+", cmd, flush=True)
    subprocess.run(cmd, shell=True, check=True)
try:
    found = glob.glob("/kaggle/input/**/metadata.jsonl", recursive=True)
    if not found:
        raise SystemExit("the style dataset is not attached (no metadata.jsonl under /kaggle/input)")
    data = os.path.dirname(found[0])
    REPORT["images"] = sum(1 for _ in open(found[0]))
    print(f"dataset: {data} ({REPORT['images']} images)", flush=True)
    REPORT["stage"] = "install"; save_report()
    sh("pip install -q " + " ".join(CFG["pip"]))
    urllib.request.urlretrieve(CFG["script_url"], "/kaggle/working/train_lora_sdxl.py")
    REPORT["stage"] = "train"; save_report()
    out = "/kaggle/working/lora"
    args = [
        f"--pretrained_model_name_or_path={CFG['base']}",
        f"--pretrained_vae_model_name_or_path={CFG['vae']}",
        f"--train_data_dir={data}", "--image_column=image", "--caption_column=text",
        f"--resolution={CFG['resolution']}", "--center_crop",
        "--train_batch_size=1", f"--gradient_accumulation_steps={CFG['accum']}", "--gradient_checkpointing",
        f"--max_train_steps={CFG['steps']}", f"--learning_rate={CFG['lr']}",
        "--lr_scheduler=constant", "--lr_warmup_steps=0", f"--rank={CFG['rank']}",
        "--mixed_precision=fp16", f"--seed={CFG['seed']}", f"--output_dir={out}",
        f"--checkpointing_steps={max(CFG['steps'], 1) * 10}", "--dataloader_num_workers=2",
        "--report_to=tensorboard",
    ]
    sh("accelerate launch --mixed_precision=fp16 --num_processes=1 --num_machines=1 --dynamo_backend=no "
       "/kaggle/working/train_lora_sdxl.py " + " ".join(args))
    weights = os.path.join(out, "pytorch_lora_weights.safetensors")
    if not os.path.exists(weights):
        raise SystemExit("training finished without writing pytorch_lora_weights.safetensors")
    final = f"/kaggle/working/{CFG['file']}"
    shutil.copy2(weights, final)
    REPORT["weights"] = CFG["file"]; REPORT["weights_mb"] = round(os.path.getsize(final) / 1e6, 1)
    REPORT["stage"] = "samples"; save_report()
    import torch
    from diffusers import AutoencoderKL, StableDiffusionXLPipeline
    vae = AutoencoderKL.from_pretrained(CFG["vae"], torch_dtype=torch.float16)
    pipe = StableDiffusionXLPipeline.from_pretrained(CFG["base"], vae=vae, torch_dtype=torch.float16,
                                                     variant="fp16", use_safetensors=True).to("cuda")
    pipe.load_lora_weights("/kaggle/working", weight_name=CFG["file"], adapter_name="style")
    pipe.set_adapters(["style"], adapter_weights=[CFG["scale"]])
    for i, p in enumerate(CFG["samples"]):
        img = pipe(p, negative_prompt=CFG["negative"], width=CFG["w"], height=CFG["h"], num_inference_steps=30,
                   guidance_scale=6.0, generator=torch.Generator("cuda").manual_seed(1000 + i)).images[0]
        img.save(f"/kaggle/working/sample_{i + 1}.png")
        print(f"sample {i + 1}: {p[:80]}", flush=True)
    REPORT["samples"] = len(CFG["samples"])
    REPORT["ok"] = True; REPORT["stage"] = "done"
except BaseException as e:
    REPORT["error"] = f"{type(e).__name__}: {e}"
    print("FAILED:", REPORT["error"], flush=True)
    raise
finally:
    # keep the downloadable output small: only the LoRA, the samples, the report and the log
    shutil.rmtree("/kaggle/working/lora", ignore_errors=True)
    for f in ("/kaggle/working/train_lora_sdxl.py",):
        if os.path.exists(f):
            os.remove(f)
    save_report()
'''

PAINT_PY = r'''# Runewake house-style painting — written by tools/kaggle_lora.py (FABLE-STYLE-2). Runs on Kaggle.
import glob, json, os, subprocess, time
CFG = json.loads(r"""__CFG__""")
T0 = time.time()
REPORT = {"model": CFG["name"], "ok": False, "done": []}
def save_report():
    REPORT["minutes"] = round((time.time() - T0) / 60, 1)
    json.dump(REPORT, open("/kaggle/working/report.json", "w"), indent=1)
try:
    hits = glob.glob(f"/kaggle/input/**/{CFG['file']}", recursive=True)
    if not hits:
        raise SystemExit(f"{CFG['file']} is not attached (looked under /kaggle/input)")
    subprocess.run("pip install -q " + " ".join(CFG["pip"]), shell=True, check=True)
    import torch
    from diffusers import AutoencoderKL, StableDiffusionXLPipeline
    vae = AutoencoderKL.from_pretrained(CFG["vae"], torch_dtype=torch.float16)
    pipe = StableDiffusionXLPipeline.from_pretrained(CFG["base"], vae=vae, torch_dtype=torch.float16,
                                                     variant="fp16", use_safetensors=True).to("cuda")
    pipe.load_lora_weights(os.path.dirname(hits[0]), weight_name=CFG["file"], adapter_name="style")
    os.makedirs("/kaggle/working/paintings", exist_ok=True)
    scales = CFG.get("scales") or [CFG["scale"]]
    for job in CFG["jobs"]:
        for k in range(CFG["per"]):
            # FABLE-STYLE-4: the same seed at every strength, so a row compares strength and nothing else
            for sc in scales:
                pipe.set_adapters(["style"], adapter_weights=[sc])
                img = pipe(job["prompt"], negative_prompt=CFG["negative"], width=CFG["w"], height=CFG["h"],
                           num_inference_steps=CFG["steps"], guidance_scale=CFG["cfg"],
                           generator=torch.Generator("cuda").manual_seed(job["seed"] + k)).images[0]
                name = f"{job['id']}__s{sc:g}__{k + 1}.png"
                img.save(f"/kaggle/working/paintings/{name}")
                REPORT["done"].append(name)
                print("painted", name, flush=True)
        save_report()
    REPORT["ok"] = True
except BaseException as e:
    REPORT["error"] = f"{type(e).__name__}: {e}"
    print("FAILED:", REPORT["error"], flush=True)
    raise
finally:
    save_report()
'''

# FABLE-STYLE-4: SDXL slides toward photographs and game renders (the mine-v1 knights, the moss close-up):
# name them here, where naming a thing pushes it away.
NEGATIVE = ("photograph, photo, photorealistic, 3d render, cgi, video game screenshot, macro photography, "
            "text, lettering, watermark, signature, card frame, border, blurry, lowres, jpeg artifacts, deformed hands")


def _kernel_dir(slug, code, meta_extra):
    u = _need_user()
    d = KWORK / slug
    if d.exists():
        shutil.rmtree(d)
    d.mkdir(parents=True)
    (d / "run.py").write_text(code, encoding="utf-8")
    meta = {"id": f"{u}/{slug}", "title": slug, "code_file": "run.py", "language": "python", "kernel_type": "script",
            "is_private": True, "enable_gpu": True, "enable_internet": True,
            "dataset_sources": [], "competition_sources": [], "kernel_sources": []}
    meta.update(meta_extra)
    (d / "kernel-metadata.json").write_text(json.dumps(meta, indent=1))
    return d, meta["id"]


def _fill(template, cfg):
    code = template.replace("__CFG__", json.dumps(cfg, ensure_ascii=False).replace('"""', '\\"\\"\\"'))
    compile(code, "kaggle-run.py", "exec")   # never push a script that doesn't even parse
    return code


def _status(ref):
    rc, out = kaggle("kernels", "status", ref, check=False, quiet=True)
    low = out.lower()
    for word in ("complete", "error", "cancel", "running", "queued"):
        if word in low:
            return word, out.strip()
    return ("unknown" if rc == 0 else "missing"), out.strip()


def _wait(ref, limit_h=12.5):
    t0, last = time.time(), None
    while time.time() - t0 < limit_h * 3600:
        st, raw = _status(ref)
        if st != last:
            print(f"  [{time.strftime('%H:%M')}] {ref}: {st}", flush=True)
            last = st
        if st in ("complete", "error", "cancel"):
            return st
        time.sleep(60)
    return "timeout"


def _download(ref, dest):
    dest.mkdir(parents=True, exist_ok=True)
    kaggle("kernels", "output", ref, "-p", dest, "-o")
    return dest


def _short_prompt(prompt, words=55):
    """SDXL reads ~75 tokens: keep the scene, drop the colour/style tail (the LoRA carries the style)."""
    text = re.split(r"\.\s+(?:Colour|Color|House style|Classical storybook)", prompt, maxsplit=1)[0]
    w = text.split()
    text = " ".join(w[:words]).rstrip(",;")
    return f"{TRIGGER}, {text}"


def _stable_seed(s):
    return int(hashlib.sha1(s.encode()).hexdigest()[:8], 16) % 2_000_000


def _test_prompts(n):
    sys.path.insert(0, str(TOOLS))
    try:
        import style_lora  # noqa: E402
        return style_lora._test_prompts(n)
    except Exception as e:  # noqa: BLE001
        print(f"  (no style_lora prompts: {e})")
        return []


# ──────────────────────────────── train ───────────────────────────────────────
def cmd_train(a):
    u = _need_user()
    s = load_state()
    k = kstate(s)
    name = a.name or f"k{len(k['models']) + 1}"
    if any(m["name"] == name for m in k["models"]) and not a.force:
        sys.exit(f"a trained style named {name} exists — pick another --name (or --force)")
    if not k.get("data") and not a.skip_data_check:
        sys.exit("push the dataset first: kaggle_lora.py push-data")
    _wait_dataset(f"{u}/{DATA_SLUG}")
    samples = [_short_prompt(p) for _, p in _test_prompts(4)] or [f"{TRIGGER}, a lone knight at dawn on a ruined bridge"]
    cfg = {"name": name, "file": f"runewake_style_{name}.safetensors", "pip": PIP, "script_url": SCRIPT_URL,
           "base": BASE_MODEL, "vae": VAE_FIX, "resolution": a.resolution, "steps": a.steps, "rank": a.rank,
           "lr": a.lr, "accum": a.accum, "seed": 42, "scale": 1.0, "samples": samples, "negative": NEGATIVE,
           "w": W, "h": H}
    d, ref = _kernel_dir(TRAIN_SLUG, _fill(TRAIN_PY, cfg),
                         {"dataset_sources": [f"{u}/{DATA_SLUG}"], "machine_shape": a.accelerator})
    print(f"training {name}: {a.steps} steps, rank {a.rank}, on {a.accelerator} — pushing {ref}")
    kaggle("kernels", "push", "-p", d)
    k["pending_train"] = {"name": name, "cfg": {x: cfg[x] for x in ("steps", "rank", "lr", "resolution")},
                          "pushed": time.strftime("%Y-%m-%d %H:%M")}
    save_state(s)
    print(f"running on Kaggle — watch it at https://www.kaggle.com/code/{ref}")
    if a.no_wait:
        print("not waiting: `kaggle_lora.py status` to check, `kaggle_lora.py fetch` when it says complete")
        return 0
    st = _wait(ref)
    return _fetch_train(ref) if st == "complete" else _report_failure(ref, st, "train")


def _report_failure(ref, st, which):
    dest = RUNS / f"kaggle_failed_{which}_{time.strftime('%m%d-%H%M')}"
    try:
        _download(ref, dest)
        rep = dest / "report.json"
        if rep.exists():
            r = json.loads(rep.read_text())
            print(f"  stage: {r.get('stage')}  error: {r.get('error')}")
        logs = sorted(dest.glob("*.log"))
        if logs:
            tail = logs[0].read_text(errors="replace")[-3000:]
            print("  --- end of the Kaggle log ---\n" + tail)
    except SystemExit:
        pass
    print(f"the {which} run ended '{st}'. Logs saved in {dest} — send Fable report.json and the .log")
    return 1


def _fetch_train(ref):
    s = load_state()
    k = kstate(s)
    pend = k.get("pending_train") or {}
    name = pend.get("name") or "latest"
    dest = RUNS / f"kaggle_{name}"
    if dest.exists():
        shutil.rmtree(dest)   # a re-fetch of the same style: no leftovers from an earlier download
    _download(ref, dest)
    rep = json.loads((dest / "report.json").read_text()) if (dest / "report.json").exists() else {}
    w = dest / f"runewake_style_{name}.safetensors"
    if not w.exists():
        found = sorted(dest.glob("runewake_style_*.safetensors"))
        if not found:
            print(f"no LoRA in the output (report: {rep.get('error') or rep.get('stage')}) — see {dest}")
            return 1
        w = found[0]
        name = w.stem.replace("runewake_style_", "")
    k["models"] = [m for m in k["models"] if m["name"] != name] + [{
        "name": name, "file": w.name, "path": str(w), "images": rep.get("images"), "minutes": rep.get("minutes"),
        "trained": time.strftime("%Y-%m-%d %H:%M"), **(pend.get("cfg") or {})}]
    k["active"] = name
    k["latest_train_output"] = name
    k.pop("pending_train", None)
    save_state(s)
    samples = sorted(dest.glob("sample_*.png"))
    sheet = _sheet([(p, f"{name} — sample {i + 1}") for i, p in enumerate(samples)], f"kaggle_{name}_samples.jpg",
                   f"Kaggle SDXL style {name} — first samples")
    print(f"trained style {name}: {w} ({w.stat().st_size / 1e6:.0f} MB, {rep.get('minutes')} min) — now active")
    if sheet:
        print(f"samples sheet: {sheet}")
    return 0


def _sheet(cells, filename, title, cols=None):
    if not cells:
        return None
    sys.path.insert(0, str(TOOLS))
    try:
        import style_lora  # noqa: E402
        out = REVIEW / filename
        style_lora.grid(cells, cols or min(4, len(cells)), out, 300, 440, 60, title)
        return out
    except Exception as e:  # noqa: BLE001
        print(f"  (no sheet: {e})")
        return None


# ──────────────────────────────── paint ───────────────────────────────────────
def _jobs(a):
    jobs = []
    if a.prompts:
        raw = json.loads(Path(a.prompts).read_text(encoding="utf-8"))
        for i, r in enumerate(raw):
            cid, p = (r.get("id", f"p{i + 1}"), r["prompt"]) if isinstance(r, dict) else (f"p{i + 1}", str(r))
            jobs.append({"id": re.sub(r"[^A-Za-z0-9_-]+", "_", cid), "prompt": p if TRIGGER in p else _short_prompt(p)})
    elif a.ids:
        ledger = Path(os.environ.get("ART_LEDGER", str(Path.home() / "runewake_art_archive" / "art_ledger.json")))
        try:
            led = json.loads(ledger.read_text(encoding="utf-8")).get("cards", {})
        except (OSError, ValueError):
            led = {}
        for cid in [x.strip() for x in a.ids.split(",") if x.strip()]:
            p = (led.get(cid) or {}).get("prompt")
            if not p:
                print(f"  {cid}: no planned prompt in the art ledger — plan it with art_director.py first; skipped")
                continue
            jobs.append({"id": cid, "prompt": _short_prompt(p)})
    else:
        jobs = [{"id": cid, "prompt": _short_prompt(p)} for cid, p in _test_prompts(a.test)]
    for j in jobs:
        j["seed"] = _stable_seed(j["id"])
    return jobs


def cmd_paint(a):
    u = _need_user()
    s = load_state()
    k = kstate(s)
    name = a.model or k.get("active")
    m = next((x for x in k["models"] if x["name"] == name), None)
    if not m:
        sys.exit("no trained style yet — kaggle_lora.py train first" if not name else f"no trained style named {name}")
    jobs = _jobs(a)
    if not jobs:
        sys.exit("nothing to paint")
    sources = {"kernel_sources": [], "dataset_sources": []}
    if k.get("latest_train_output") == name:
        sources["kernel_sources"] = [f"{u}/{TRAIN_SLUG}"]
    else:
        # not the training notebook's newest output: carry the LoRA over as a (private) dataset
        if name not in k.get("loras_uploaded", []):
            _push_lora(u, m)
            k.setdefault("loras_uploaded", []).append(name)
            save_state(s)
        _wait_dataset(f"{u}/{LORAS_SLUG}")
        sources["dataset_sources"] = [f"{u}/{LORAS_SLUG}"]
    scales = [float(x) for x in a.scales.split(",") if x.strip()] if a.scales else [a.scale]
    cfg = {"name": name, "file": m["file"], "pip": PIP, "base": BASE_MODEL, "vae": VAE_FIX, "scale": scales[0],
           "scales": scales,
           "steps": a.steps, "cfg": a.cfg, "per": a.per, "negative": NEGATIVE, "w": W, "h": H, "jobs": jobs}
    d, ref = _kernel_dir(PAINT_SLUG, _fill(PAINT_PY, cfg), {**sources, "machine_shape": a.accelerator})
    print(f"painting {len(jobs)} prompt(s) × {a.per} × strength {', '.join(f'{x:g}' for x in scales)} "
          f"with style {name} — pushing {ref}")
    kaggle("kernels", "push", "-p", d)
    k["pending_paint"] = {"model": name, "ids": [j["id"] for j in jobs], "to_ledger": bool(a.to_ledger), "scales": scales,
                          "prompts": {j["id"]: j["prompt"] for j in jobs}, "pushed": time.strftime("%Y-%m-%d %H:%M")}
    save_state(s)
    print(f"running on Kaggle — https://www.kaggle.com/code/{ref}")
    if a.no_wait:
        print("not waiting: `kaggle_lora.py status paint`, then `kaggle_lora.py fetch paint`")
        return 0
    st = _wait(ref, limit_h=6)
    return _fetch_paint(ref) if st == "complete" else _report_failure(ref, st, "paint")


def _push_lora(u, m):
    stage = KWORK / "loras"
    stage.mkdir(parents=True, exist_ok=True)
    src = Path(m["path"])
    if not src.exists():
        sys.exit(f"the LoRA file for {m['name']} is missing: {src}")
    shutil.copy2(src, stage / m["file"])
    (stage / "dataset-metadata.json").write_text(json.dumps(
        {"title": LORAS_SLUG, "id": f"{u}/{LORAS_SLUG}", "licenses": [{"name": "unknown"}]}, indent=1))
    exists = kaggle("datasets", "status", f"{u}/{LORAS_SLUG}", check=False, quiet=True)[0] == 0
    if exists:
        kaggle("datasets", "version", "-p", stage, "-m", f"add {m['name']}")
    else:
        kaggle("datasets", "create", "-p", stage)


def _fetch_paint(ref):
    s = load_state()
    k = kstate(s)
    pend = k.get("pending_paint") or {}
    run = time.strftime("%Y%m%d-%H%M%S")
    dest = RUNS / f"kaggle_paint_{run}"
    n = 1
    while dest.exists():
        n += 1
        dest = RUNS / f"kaggle_paint_{run}_{n}"
    _download(ref, dest)
    def order(p):   # by prompt, then copy, then strength (numerically — "s1.2" must come after "s1")
        bits = p.stem.split("__")
        sc = next((b[1:] for b in bits[1:] if b.startswith("s")), "0")
        try:
            scv = float(sc)
        except ValueError:
            scv = 0.0
        return (bits[0], bits[-1], scv)
    pics = sorted(dest.rglob("*__*.png"), key=order)
    if not pics:
        print(f"no paintings in the output — see {dest}")
        return 1
    model = pend.get("model", "latest")
    # FABLE-STYLE-4: one row per prompt, one column per strength; every run gets its own sheet
    def label(p):
        bits = p.stem.split("__")
        sc = next((b[1:] for b in bits[1:] if b.startswith("s")), "")
        return f"{bits[0]}" + (f" · strength {sc}" if sc else "") + (f" #{bits[-1]}" if bits[-1].isdigit() and bits[-1] != "1" else "")
    cols = max(1, len(pend.get("scales") or [1])) or 4
    sheet = _sheet([(p, label(p)) for p in pics], f"kaggle_{model}_paint_{run}.jpg",
                   f"Style {model} — {len(pics)} painting(s)" +
                   (f" — strength {' / '.join(f'{x:g}' for x in pend['scales'])}" if len(pend.get('scales') or []) > 1 else ""),
                   cols=cols if cols > 1 else None)
    print(f"{len(pics)} painting(s) in {dest}")
    if sheet:
        print(f"review sheet: {sheet}")
    if pend.get("to_ledger"):
        _to_ledger(pics, model, run)
    k.pop("pending_paint", None)
    save_state(s)
    return 0


def _to_ledger(pics, model, run):
    """Add the paintings as art_director candidates, exactly the shape `render` writes."""
    sys.path.insert(0, str(TOOLS))
    try:
        import art_director as ad  # noqa: E402
    except Exception as e:  # noqa: BLE001
        print(f"  --to-ledger skipped: art_director would not load ({e})")
        return
    led = ad.load_ledger()
    ad.WORK.mkdir(parents=True, exist_ok=True)
    added = 0
    for p in pics:
        cid = p.stem.split("__")[0]
        e = led["cards"].get(cid)
        if not e:
            continue
        cands = e.setdefault("candidates", [])
        n = max([c["n"] for c in cands], default=0) + 1
        dest = ad.WORK / f"{cid}_{n}.png"
        shutil.copy2(p, dest)
        f = ad.image_features(dest)
        others = {kk: v for kk, v in led["cards"].items() if kk != cid}
        sim = max((ad.image_similarity(f, o["image"]) for o in others.values() if o.get("image")), default=0)
        cands.append({"n": n, "generator": f"sdxl-{model}", "path": str(dest), "image": f, "image_similarity": round(sim, 3)})
        cands.sort(key=lambda c: c["image_similarity"])
        e["status"] = "rendered"
        e["run"] = run
        added += 1
    if added:
        led["last_run"] = run
        ad.save_ledger(led)
    print(f"  {added} candidate(s) added to the art ledger — art_director.py sheet, then approve <card> <n>")


# ──────────────────────────────── status / fetch / use ────────────────────────
def cmd_status(a):
    u = _need_user()
    ref = f"{u}/{TRAIN_SLUG if a.which == 'train' else PAINT_SLUG}"
    st, raw = _status(ref)
    print(f"{ref}: {st}\n  {raw[-200:]}")
    k = kstate()
    pend = k.get("pending_train" if a.which == "train" else "pending_paint")
    if pend:
        print(f"  waiting to fetch: {json.dumps({x: pend[x] for x in pend if x != 'prompts'})}")
    return 0


def cmd_fetch(a):
    u = _need_user()
    ref = f"{u}/{TRAIN_SLUG if a.which == 'train' else PAINT_SLUG}"
    st, _ = _status(ref)
    if st != "complete":
        print(f"{ref} is '{st}', not complete yet")
        return 1 if st in ("running", "queued") else _report_failure(ref, st, a.which)
    return _fetch_train(ref) if a.which == "train" else _fetch_paint(ref)


def cmd_use(a):
    s = load_state()
    k = kstate(s)
    if not any(m["name"] == a.name for m in k["models"]):
        sys.exit(f"no trained style named {a.name} (have: {', '.join(m['name'] for m in k['models']) or 'none'})")
    k["active"] = a.name
    save_state(s)
    print(f"active Kaggle style: {a.name}")
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("doctor")
    pd = sub.add_parser("push-data")
    pd.add_argument("--note", default="")
    t = sub.add_parser("train")
    t.add_argument("--name")
    t.add_argument("--steps", type=int, default=1500)
    t.add_argument("--rank", type=int, default=16)
    t.add_argument("--lr", type=float, default=1e-4)
    t.add_argument("--accum", type=int, default=1)
    t.add_argument("--resolution", type=int, default=1024)
    t.add_argument("--accelerator", default=os.environ.get("KAGGLE_ACCELERATOR", "NvidiaTeslaT4"))
    t.add_argument("--no-wait", action="store_true")
    t.add_argument("--force", action="store_true")
    t.add_argument("--skip-data-check", action="store_true")
    p = sub.add_parser("paint")
    g = p.add_mutually_exclusive_group()
    g.add_argument("--ids", help="card ids (comma-separated) — prompts from the art ledger")
    g.add_argument("--test", type=int, default=6, help="N test prompts (the default)")
    g.add_argument("--prompts", help="a JSON file: [\"prompt\", …] or [{\"id\":…, \"prompt\":…}, …]")
    p.add_argument("--model")
    p.add_argument("--per", type=int, default=1, help="paintings per prompt")
    p.add_argument("--scale", type=float, default=1.0, help="style strength")
    p.add_argument("--scales", help="several strengths side by side, e.g. 0.8,1.0,1.2 (same seed per row)")
    p.add_argument("--steps", type=int, default=30)
    p.add_argument("--cfg", type=float, default=6.0)
    p.add_argument("--accelerator", default=os.environ.get("KAGGLE_ACCELERATOR", "NvidiaTeslaT4"))
    p.add_argument("--to-ledger", action="store_true", help="add the paintings as art_director candidates")
    p.add_argument("--no-wait", action="store_true")
    st = sub.add_parser("status")
    st.add_argument("which", nargs="?", choices=["train", "paint"], default="train")
    fe = sub.add_parser("fetch")
    fe.add_argument("which", nargs="?", choices=["train", "paint"], default="train")
    us = sub.add_parser("use")
    us.add_argument("name")
    a = ap.parse_args()
    return {"doctor": cmd_doctor, "push-data": cmd_push_data, "train": cmd_train, "paint": cmd_paint,
            "status": cmd_status, "fetch": cmd_fetch, "use": cmd_use}[a.cmd](a)


if __name__ == "__main__":
    sys.exit(main())

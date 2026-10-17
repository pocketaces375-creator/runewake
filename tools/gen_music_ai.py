#!/usr/bin/env python3
"""FABLE-070: three new pieces of music from an AI music generator (Trikzos: "ancient, epic, Renaissance faire").

  probe      which music generators this machine has a key for (prints key NAMES only, never values)
  generate   render each track with the first generator that works, master it (loudness -18 LUFS, gentle
             fades so it loops without a click), and save candidates to ~/runewake_art_archive/music/.
             Nothing goes into the game until Trikzos picks.
  install    copy picked tracks into client/content/audio/music/ and register them in audio_manifest.json.

Generators, tried in this order (keys read from the environment or ~/.hermes/.env):
  ELEVENLABS_API_KEY   ElevenLabs Music          (paid plans allow commercial use)
  FAL_KEY              fal.ai ACE-Step           (Apache-2.0 model)
  REPLICATE_API_TOKEN  Replicate ACE-Step / Stable Audio
  HF_TOKEN             Hugging Face MusicGen     (model licence is non-commercial: flagged in the report)
Each track's provider is written next to it (<id>.provider.txt) so the credits can say exactly where it came from.

  python3 tools/gen_music_ai.py probe
  python3 tools/gen_music_ai.py generate [--only ferrymans_toll]
  python3 tools/gen_music_ai.py install ferrymans_toll faire_of_the_long_hall oath_of_the_old_kings
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
WORK = Path(os.environ.get("MUSIC_WORK", str(Path.home() / "runewake_art_archive" / "music")))
MUSIC = REPO / "client" / "content" / "audio" / "music"
MANIFEST = REPO / "client" / "content" / "audio" / "audio_manifest.json"

SECONDS = 120
TRACKS = {
    "ferrymans_toll": {
        "title": "The Ferryman's Toll",
        "use": "Card Packs / the Styx offering",
        "prompt": "Epic ancient Greek underworld theme, instrumental. Slow and solemn at 70 BPM in D Phrygian. Deep "
                  "male choir humming, a plucked lyre and concert harp, low frame drums like distant footsteps, a "
                  "lone duduk melody, brass swells and a huge choir climax halfway, rumbling timpani, cavernous "
                  "reverb, mysterious and awe-struck, like paying the ferryman on the river Styx. No vocals with words.",
        "tags": "epic, ancient greek, underworld, choir, lyre, harp, duduk, frame drum, timpani, cinematic, solemn, 70 bpm, instrumental",
    },
    "faire_of_the_long_hall": {
        "title": "Faire of the Long Hall",
        "use": "Map / town / menus",
        "prompt": "Joyful Renaissance faire dance, instrumental. Lively 6/8 galliard at 120 BPM in G Mixolydian. "
                  "Lute and cittern strumming, recorder and shawm trading the tune, hurdy-gurdy drone, bagpipe "
                  "joining for the second verse, tabor drum and tambourine, hand claps, a stomping tavern crowd feel, "
                  "warm and festive, medieval market day. No vocals.",
        "tags": "renaissance faire, medieval, folk dance, galliard, lute, recorder, hurdy-gurdy, bagpipe, tabor, tambourine, festive, 120 bpm, instrumental",
    },
    "oath_of_the_old_kings": {
        "title": "Oath of the Old Kings",
        "use": "Duels / boss fights",
        "prompt": "Epic ancient battle anthem, instrumental. Driving 112 BPM in D Dorian. Thunderous taiko and frame "
                  "drums, a hurdy-gurdy and bowed lyre riff, low brass and horns, war chant of a male choir on vowels, "
                  "fiddle runs, rising strings, a heroic horn melody over the top, relentless and triumphant, Norse "
                  "and Greek heroes marching to war. No vocals with words.",
        "tags": "epic, battle, ancient, nordic, taiko, frame drum, hurdy-gurdy, horns, war chant choir, fiddle, heroic, 112 bpm, instrumental",
    },
}


def keys():
    k = {n: v for n, v in os.environ.items()}
    f = Path(os.environ.get("ART_ENV_FILE", str(Path.home() / ".hermes" / ".env")))
    if f.exists():
        for line in f.read_text().splitlines():
            if "=" in line and not line.lstrip().startswith("#"):
                n, v = line.split("=", 1)
                k.setdefault(n.strip().replace("export ", ""), v.strip().strip('"').strip("'"))
    return k


def http(url, payload=None, headers=None, timeout=600, raw=False):
    data = json.dumps(payload).encode() if payload is not None else None
    req = urllib.request.Request(url, data=data, headers={"Content-Type": "application/json", **(headers or {})},
                                 method="POST" if data is not None else "GET")
    with urllib.request.urlopen(req, timeout=timeout) as r:
        body = r.read()
        return body if raw else json.loads(body.decode())


def download(url, out, headers=None):
    req = urllib.request.Request(url, headers=headers or {})
    with urllib.request.urlopen(req, timeout=600) as r, open(out, "wb") as f:
        shutil.copyfileobj(r, f)


# ── providers: each returns the path of the raw audio it saved, or raises ──────────────────────────

def elevenlabs(k, t, out):
    body = http("https://api.elevenlabs.io/v1/music?output_format=mp3_44100_192",
                {"prompt": t["prompt"], "music_length_ms": SECONDS * 1000, "model_id": "music_v1", "force_instrumental": True},
                {"xi-api-key": k["ELEVENLABS_API_KEY"]}, raw=True)
    p = out.with_suffix(".mp3")
    p.write_bytes(body)
    return p


def fal(k, t, out):
    h = {"Authorization": f"Key {k['FAL_KEY']}"}
    last = None
    for endpoint, payload in [
        ("https://fal.run/fal-ai/ace-step", {"tags": t["tags"], "lyrics": "[instrumental]", "duration": SECONDS}),
        ("https://fal.run/fal-ai/stable-audio-25/text-to-audio", {"prompt": t["prompt"], "seconds_total": min(SECONDS, 190)}),
        ("https://fal.run/fal-ai/stable-audio", {"prompt": t["prompt"], "seconds_total": 47}),
    ]:
        try:
            d = http(endpoint, payload, h)
            a = d.get("audio") or d.get("audio_file") or d.get("audio_url")
            url = a.get("url") if isinstance(a, dict) else a
            if not url:
                raise RuntimeError(f"no audio url in {str(d)[:200]}")
            p = out.with_suffix(Path(url.split("?")[0]).suffix or ".wav")
            download(url, p)
            (out.parent / (out.stem + ".endpoint.txt")).write_text(endpoint)
            return p
        except Exception as e:  # try the next model
            last = e
            print(f"   fal {endpoint.rsplit('/', 2)[-2:]} failed: {str(e)[:160]}", flush=True)
    raise last or RuntimeError("fal: nothing worked")


def replicate(k, t, out):
    h = {"Authorization": f"Bearer {k['REPLICATE_API_TOKEN']}", "Prefer": "wait"}
    last = None
    for model, payload in [
        ("lucataco/ace-step", {"tags": t["tags"], "lyrics": "[instrumental]", "duration": SECONDS}),
        ("stability-ai/stable-audio-2.5", {"prompt": t["prompt"], "duration": min(SECONDS, 190)}),
    ]:
        try:
            d = http(f"https://api.replicate.com/v1/models/{model}/predictions", {"input": payload}, h)
            for _ in range(120):
                if d.get("status") in ("succeeded", "failed", "canceled"):
                    break
                time.sleep(5)
                d = http(d["urls"]["get"], None, h)
            if d.get("status") != "succeeded":
                raise RuntimeError(f"{d.get('status')}: {str(d.get('error'))[:160]}")
            o = d["output"]
            url = o if isinstance(o, str) else (o[0] if isinstance(o, list) else o.get("audio") or o.get("url"))
            p = out.with_suffix(Path(url.split("?")[0]).suffix or ".wav")
            download(url, p)
            (out.parent / (out.stem + ".endpoint.txt")).write_text("replicate " + model)
            return p
        except Exception as e:
            last = e
            print(f"   replicate {model} failed: {str(e)[:160]}", flush=True)
    raise last or RuntimeError("replicate: nothing worked")


def huggingface(k, t, out):
    h = {"Authorization": f"Bearer {k['HF_TOKEN']}"}
    last = None
    for url in ["https://router.huggingface.co/hf-inference/models/facebook/musicgen-small",
                "https://api-inference.huggingface.co/models/facebook/musicgen-small"]:
        try:
            body = http(url, {"inputs": t["prompt"]}, h, raw=True)
            p = out.with_suffix(".flac")
            p.write_bytes(body)
            return p
        except Exception as e:
            last = e
    raise last or RuntimeError("huggingface failed")


PROVIDERS = [("ELEVENLABS_API_KEY", "ElevenLabs Music", elevenlabs),
             ("FAL_KEY", "fal.ai", fal),
             ("REPLICATE_API_TOKEN", "Replicate", replicate),
             ("HF_TOKEN", "Hugging Face MusicGen (non-commercial licence)", huggingface)]


def master(raw, ogg):
    """-18 LUFS, 44.1 kHz stereo, 1.5 s fade in, 3 s fade out, Ogg Vorbis."""
    dur = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(raw)],
                               capture_output=True, text=True).stdout.strip() or 0)
    af = f"loudnorm=I=-18:TP=-1.5:LRA=11,afade=t=in:d=1.5,afade=t=out:st={max(0.0, dur - 3):.2f}:d=3"
    subprocess.run(["ffmpeg", "-y", "-v", "error", "-i", str(raw), "-af", af, "-ar", "44100", "-ac", "2", "-c:a", "libvorbis", "-q:a", "5", str(ogg)], check=True)
    return dur


def probe():
    k = keys()
    have = [(n, label) for n, label, _ in PROVIDERS if k.get(n)]
    print("music generators with keys: " + (", ".join(f"{label} ({n})" for n, label in have) if have else "NONE"))
    print("ffmpeg: " + ("yes" if shutil.which("ffmpeg") else "MISSING"))
    return have


def generate(only):
    k = keys()
    WORK.mkdir(parents=True, exist_ok=True)
    have = [(n, label, fn) for n, label, fn in PROVIDERS if k.get(n)]
    if not have:
        print("NO_PROVIDER: no music-generator key on this machine (looked for " + ", ".join(n for n, _, _ in PROVIDERS) + ")")
        return 2
    made = 0
    for tid, t in TRACKS.items():
        if only and tid not in only:
            continue
        for n, label, fn in have:
            try:
                print(f"{tid}: trying {label}", flush=True)
                raw = fn(k, t, WORK / f"{tid}_raw")
                dur = master(raw, WORK / f"{tid}.ogg")
                (WORK / f"{tid}.provider.txt").write_text(label)
                print(f"TRACK {tid} {WORK / (tid + '.ogg')} {dur:.0f}s via {label}", flush=True)
                made += 1
                break
            except Exception as e:
                print(f"{tid}: {label} failed: {str(e)[:200]}", flush=True)
    return 0 if made else 3


def install(ids):
    man = json.loads(MANIFEST.read_text())
    for tid in ids:
        src = WORK / f"{tid}.ogg"
        if not src.exists():
            sys.exit(f"missing {src}")
        shutil.copy(src, MUSIC / f"{tid}.ogg")
        man["music"][tid] = {"path": f"res://content/audio/music/{tid}.ogg", "bus": "Music", "volume": -1.0}
        prov = (WORK / f"{tid}.provider.txt").read_text().strip() if (WORK / f"{tid}.provider.txt").exists() else "AI generator"
        print(f"installed {tid} ({TRACKS.get(tid, {}).get('title', tid)}, {prov})")
    MANIFEST.write_text(json.dumps(man, indent=2) + "\n")


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("probe")
    g = sub.add_parser("generate")
    g.add_argument("--only", default="")
    i = sub.add_parser("install")
    i.add_argument("ids", nargs="+")
    a = ap.parse_args()
    if a.cmd == "probe":
        probe()
    elif a.cmd == "generate":
        sys.exit(generate([x for x in a.only.split(",") if x]))
    else:
        install(a.ids)


if __name__ == "__main__":
    main()

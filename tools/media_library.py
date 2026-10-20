#!/usr/bin/env python3
"""FABLE-077: the Runewake media library — every picture and piece of music we have made or used, in one place.

Trikzos: "Save all current media that we've used and are using somewhere, cause we'll always find a place for
everything. Start a library for this game."

  build     gather everything into ~/runewake_library (LIBRARY_HOME):
              in_game/     what the game ships or keeps in the repo (client/content art + audio)
              candidates/  everything the generators made (~/runewake_art_archive: mockups, music takes, map and
                           button candidates, art-director paintings, relic art, LoRA samples, previews ...)
              meta/        the art ledger and spend log, as they were
            then write catalog.json, CATALOG.md and one gallery page per section (thumbnails in thumbs/).
            A candidate whose bytes match a file the game uses is marked "in game".
  publish   commit and push it to the private GitHub repo pocketaces375-creator/runewake-library (made on first
            run), in batches so no single push is too big. Files over 95 MB are listed, never pushed.

  python3 tools/media_library.py build
  python3 tools/media_library.py publish

Add new media by saving it under ~/runewake_art_archive (every generator already does) and running both commands.
"""
import argparse
import fnmatch
import hashlib
import json
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CONTENT = REPO / "client" / "content"
ARCHIVE = Path(os.environ.get("ART_ARCHIVE", str(Path.home() / "runewake_art_archive")))
LIB = Path(os.environ.get("LIBRARY_HOME", str(Path.home() / "runewake_library")))
GH_REPO = os.environ.get("LIBRARY_REPO", "pocketaces375-creator/runewake-library")

IMAGE = {".png", ".jpg", ".jpeg", ".webp", ".gif"}
AUDIO = {".ogg", ".mp3", ".wav", ".flac", ".m4a"}
VIDEO = {".mp4", ".webm", ".mov"}
SIDECAR = {".txt", ".json", ".md"}          # prompts, providers, notes next to the media they describe
MAX_BYTES = 95 * 1024 * 1024                # GitHub refuses files over 100 MB
BATCH_BYTES = 800 * 1024 * 1024             # per push
SKIP_PARTS = {"kaggle", "dataset", ".git", "__pycache__"}   # training staging and duplicated datasets, not media

# Patterns the APK export leaves out (client/export_presets.cfg): kept in the repo, not shipped.
NOT_SHIPPED = ["*raw/*", "*nano_v7/*", "*flux_v5/*", "art/classes/raw/*", "art/artifacts/raw/*",
               "art/frame/basalt_unused/*", "art/pocketed/*", "art/border/legacy/*"]

# Where each in-game music track plays (AudioManager.SceneTracks / DuelMusic).
MUSIC_USE = {
    "gods_of_old_answer_1": "Title screen (default title music)",
    "gods_of_old_answer": "Title music option",
    "road_beneath_the_world": "World map, open world, deck builder, Reliquary, Rune Page, dig sites, rewards",
    "clash_at_the_crossroads": "PvP: online and arena duels",
    "oath_of_the_old_kings": "Warden and boss duels",
    "faire_of_the_long_hall": "Merchant and Forge",
    "ferrymans_toll": "Purchase Drachmae (the offering)",
    "thorn_reach": "Campaign duels",
    "stones_and_dust": "Quiet bed (fallback)",
    "gilded_court": "Title music option", "hall_of_runes": "Title music option (original)",
    "sands_of_the_old_age": "Title music option", "barrow_hymn": "Title music option",
    "hearthfire_reckoning": "Title music option", "the_long_ascent": "Title music option",
    "ambient_reach": "Ambient bed",
}

ART_USE = [
    ("art/map/", "World map"), ("art/title/", "Title screen"), ("art/classes/", "Class portraits and load art"),
    ("art/board/", "Duel boards"), ("art/artifacts/", "Artifact art"), ("art/border/", "Card borders"),
    ("art/frame/", "Card frames"), ("art/styx/", "The offering (Purchase Drachmae)"), ("art/cards_baked/", "Baked card faces"),
    ("art/relics/", "Relics"), ("art/ui/", "Interface"), ("art/icons/", "Icons"), ("art/", "Card art"),
    ("audio/sfx/", "Sound effect"), ("audio/ambient/", "Ambient sound"),
]


def kind(p: Path) -> str:
    e = p.suffix.lower()
    return "image" if e in IMAGE else "audio" if e in AUDIO else "video" if e in VIDEO else "note" if e in SIDECAR else ""


def sha1(p: Path) -> str:
    h = hashlib.sha1()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest()


def info(p: Path, k: str) -> str:
    try:
        if k == "image":
            from PIL import Image
            with Image.open(p) as im:
                return f"{im.width}x{im.height}"
        if k in ("audio", "video"):
            out = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(p)],
                                 capture_output=True, text=True, timeout=30).stdout.strip()
            return f"{float(out):.0f}s" if out else ""
    except Exception:
        pass
    return ""


def thumb(src: Path, rel: str) -> str:
    t = LIB / "thumbs" / (rel.replace("/", "__") + ".jpg")
    if not t.exists() or t.stat().st_mtime < src.stat().st_mtime:
        try:
            from PIL import Image
            t.parent.mkdir(parents=True, exist_ok=True)
            with Image.open(src) as im:
                im = im.convert("RGB")
                im.thumbnail((360, 360))
                im.save(t, quality=82)
        except Exception:
            return ""
    return str(t.relative_to(LIB))


def copy_tree(src_root: Path, dst_root: Path, keep):
    n = 0
    for p in sorted(src_root.rglob("*")):
        if not p.is_file() or any(part in SKIP_PARTS for part in p.relative_to(src_root).parts):
            continue
        if p.name.endswith(".import") or not keep(p):
            continue
        d = dst_root / p.relative_to(src_root)
        if not d.exists() or d.stat().st_size != p.stat().st_size or d.stat().st_mtime < p.stat().st_mtime:
            d.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(p, d)
        n += 1
    return n


def build():
    LIB.mkdir(parents=True, exist_ok=True)
    media = lambda p: kind(p) in ("image", "audio", "video")
    with_notes = lambda p: kind(p) != ""
    n_game = 0
    for sub in ("art", "audio"):
        if (CONTENT / sub).exists():
            n_game += copy_tree(CONTENT / sub, LIB / "in_game" / sub, media)
    n_cand = copy_tree(ARCHIVE, LIB / "candidates", with_notes) if ARCHIVE.exists() else 0
    (LIB / "meta").mkdir(exist_ok=True)
    for f in ("art_ledger.json", "spend.jsonl"):
        if (ARCHIVE / f).exists():
            shutil.copy2(ARCHIVE / f, LIB / "meta" / f)
    print(f"copied {n_game} in-game files and {n_cand} candidate files into {LIB}", flush=True)

    entries, game_hash = [], {}
    for p in sorted((LIB / "in_game").rglob("*")):
        k = kind(p)
        if not p.is_file() or k not in ("image", "audio", "video"):
            continue
        rel = str(p.relative_to(LIB))
        inner = rel.split("in_game/", 1)[1]
        shipped = not any(fnmatch.fnmatch(inner, "*" + pat) or fnmatch.fnmatch(inner, pat) for pat in NOT_SHIPPED)
        use = ""
        if k == "audio" and "/music/" in rel:
            stem = p.stem
            base = stem.rsplit("_", 1)[0] if stem.rsplit("_", 1)[-1].isdigit() else stem
            use = MUSIC_USE.get(stem) or MUSIC_USE.get(base, "Music")
        else:
            use = next((label for prefix, label in ART_USE if inner.startswith(prefix)), "")
        h = sha1(p)
        game_hash[h] = rel
        entries.append({"path": rel, "kind": k, "status": "in game" if shipped else "kept (not in the APK)",
                        "use": use, "size": p.stat().st_size, "info": info(p, k), "sha1": h, "section": "in_game/" + inner.split("/")[0]})
    for p in sorted((LIB / "candidates").rglob("*")):
        k = kind(p)
        if not p.is_file() or k not in ("image", "audio", "video"):
            continue
        rel = str(p.relative_to(LIB))
        inner = rel.split("candidates/", 1)[1]
        h = sha1(p)
        notes = [str(s.relative_to(LIB)) for s in p.parent.glob(p.stem.replace("_raw", "") + "*")
                 if s != p and kind(s) == "note"][:4]
        entries.append({"path": rel, "kind": k, "status": "in game" if h in game_hash else "candidate",
                        "use": ("same as " + game_hash[h]) if h in game_hash else "", "size": p.stat().st_size,
                        "info": info(p, k), "sha1": h, "notes": notes,
                        "section": "candidates/" + (inner.split("/")[0] if "/" in inner else "loose")})
    big = [e["path"] for e in entries if e["size"] > MAX_BYTES]
    catalog = {"built": time.strftime("%Y-%m-%d %H:%M"), "counts": {}, "too_big_for_github": big, "items": entries}
    for e in entries:
        catalog["counts"][e["section"]] = catalog["counts"].get(e["section"], 0) + 1
    (LIB / "catalog.json").write_text(json.dumps(catalog, indent=1))
    write_pages(catalog)
    total = sum(e["size"] for e in entries)
    print(f"LIBRARY {len(entries)} items, {total / 1048576:.0f} MB, {len(catalog['counts'])} sections, {len(big)} too big for GitHub", flush=True)


def write_pages(catalog):
    sections = {}
    for e in catalog["items"]:
        sections.setdefault(e["section"], []).append(e)
    pages = LIB / "pages"
    pages.mkdir(exist_ok=True)
    lines = ["# Runewake media library", "",
             f"Everything we have made or used for the game, as of {catalog['built']}. "
             "**in game** = the game uses it; **kept** = in the repo but not shipped; **candidate** = made, not picked yet "
             "(we'll always find a place for everything).", "",
             "| Section | Items | |", "|---|---|---|"]
    for name in sorted(sections):
        page = name.replace("/", "__") + ".md"
        lines.append(f"| {name} | {len(sections[name])} | [open](pages/{page}) |")
        write_section(pages / page, name, sections[name])
    music = [e for e in catalog["items"] if e["kind"] == "audio" and "/music/" in e["path"]]
    if music:
        lines += ["", "## Music", "", "| Track | Status | Where it plays | Length |", "|---|---|---|---|"]
        for e in music:
            lines.append(f"| [{Path(e['path']).stem}]({e['path']}) | {e['status']} | {e['use']} | {e['info']} |")
    if catalog["too_big_for_github"]:
        lines += ["", "## Kept only on the mini PC (over 95 MB)", ""] + [f"- {p}" for p in catalog["too_big_for_github"]]
    (LIB / "CATALOG.md").write_text("\n".join(lines) + "\n")
    readme = LIB / "README.md"
    readme.write_text(
        "# Runewake media library\n\nEvery picture, painting, mock-up and piece of music made for Runewake — the ones in the "
        "game and the ones waiting for a place. Start at [CATALOG.md](CATALOG.md).\n\n"
        "- `in_game/` — what the game uses (copied from `client/content`)\n"
        "- `candidates/` — everything the generators made (from `~/runewake_art_archive` on the mini PC)\n"
        "- `meta/` — the art ledger and spend log\n- `thumbs/`, `pages/` — the browsable galleries\n\n"
        "Rebuilt by `python3 tools/media_library.py build && python3 tools/media_library.py publish` in the game repo.\n")


def write_section(path: Path, name: str, items):
    out = [f"# {name}", "", f"[← library](../CATALOG.md) · {len(items)} items", ""]
    imgs = [e for e in items if e["kind"] == "image"]
    other = [e for e in items if e["kind"] != "image"]
    cells = []
    for e in imgs:
        t = thumb(LIB / e["path"], e["path"])
        label = Path(e["path"]).name + (" ✅" if e["status"] == "in game" else "")
        cells.append(f'<a href="../{e["path"]}"><img src="../{t}" width="170"></a><br><sub>{label}</sub>' if t
                     else f'<a href="../{e["path"]}">{label}</a>')
    if cells:
        out += ["| | | | |", "|---|---|---|---|"]
        for i in range(0, len(cells), 4):
            row = cells[i:i + 4]
            out.append("| " + " | ".join(row + [""] * (4 - len(row))) + " |")
    if other:
        out += ["", "| File | Kind | Status | Use | Length |", "|---|---|---|---|---|"]
        for e in other:
            out.append(f"| [{Path(e['path']).name}](../{e['path']}) | {e['kind']} | {e['status']} | {e['use']} | {e['info']} |")
    path.write_text("\n".join(out) + "\n")


def git(*a, check=True):
    return subprocess.run(["git", "-C", str(LIB), *a], capture_output=True, text=True, check=check)


def publish():
    if not (LIB / "catalog.json").exists():
        sys.exit("run build first")
    big = set(json.loads((LIB / "catalog.json").read_text()).get("too_big_for_github", []))
    if not (LIB / ".git").exists():
        git("init", "-q", "-b", "main")
        git("config", "user.name", "Runewake Library")
        git("config", "user.email", "library@runewake.game")
    (LIB / ".gitignore").write_text("\n".join(sorted(big)) + "\n")
    remote = os.environ.get("LIBRARY_REMOTE")          # tests point this at a local bare repo
    if not remote:
        if subprocess.run(["gh", "repo", "view", GH_REPO], capture_output=True).returncode != 0:
            r = subprocess.run(["gh", "repo", "create", GH_REPO, "--private", "--description",
                                "Runewake media library: every picture and piece of music, used or waiting"], capture_output=True, text=True)
            if r.returncode != 0:
                sys.exit("could not create the library repo: " + (r.stderr or r.stdout)[-300:])
        subprocess.run(["gh", "auth", "setup-git"], capture_output=True)
        remote = f"https://github.com/{GH_REPO}.git"
    if git("remote", check=False).stdout.strip() == "":
        git("remote", "add", "origin", remote)
    # small things first (pages, catalog, thumbs), then the media in batches
    git("add", "README.md", "CATALOG.md", "catalog.json", ".gitignore", check=False)
    for d in ("pages", "thumbs", "meta"):
        if (LIB / d).exists():
            git("add", d, check=False)
    listed = git("ls-files", "-z", "--others", "--modified", "--exclude-standard").stdout.split("\0")
    files = [f for f in dict.fromkeys(listed) if f and f not in big]
    batch, size, pushed = [], 0, 0

    def flush(msg):
        nonlocal batch, size, pushed
        if batch:
            for i in range(0, len(batch), 200):
                git("add", "--", *batch[i:i + 200], check=False)
        if git("diff", "--cached", "--quiet", check=False).returncode != 0:
            git("commit", "-qm", msg)
            r = git("push", "-q", "-u", "origin", "main", check=False)
            if r.returncode != 0:
                sys.exit("push failed: " + r.stderr[-300:])
            pushed += len(batch)
        batch, size = [], 0

    flush("Library catalog and galleries")
    for f in files:
        p = LIB / f
        if not p.is_file():
            continue
        batch.append(f)
        size += p.stat().st_size
        if size >= BATCH_BYTES:
            flush(f"Library media ({time.strftime('%Y-%m-%d')})")
    flush(f"Library media ({time.strftime('%Y-%m-%d')})")
    print(f"PUBLISHED https://github.com/{GH_REPO} ({pushed} files pushed, {len(big)} kept only on this machine)", flush=True)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("cmd", choices=["build", "publish"])
    a = ap.parse_args()
    build() if a.cmd == "build" else publish()


if __name__ == "__main__":
    main()

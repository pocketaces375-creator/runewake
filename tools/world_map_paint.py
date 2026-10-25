#!/usr/bin/env python3
"""FABLE-105: paint one page of the shared world FROM ITS TERRAIN PLAN (docs/WORLD_TERRAIN.md).

The engine plans the page's terrain (which features, in which 3x3 cells) and writes the painter's prompt;
the places on the page already stand on that terrain and are named for it. This script only carries the
prompt to a painter and files the result where the game looks for it, so the dots land on the painting.

    python3 tools/world_map_paint.py --biome elvenwood --instance 0 --page 1            # one page
    python3 tools/world_map_paint.py --biome elvenwood --instance 0 --all-pages          # its whole first area
    python3 tools/world_map_paint.py --biome elvenwood --page 1 --print                  # just show the prompt
    python3 tools/world_map_paint.py ... --painter lora|gemini|flux                     # default: lora if trained, else gemini
    python3 tools/world_map_paint.py ... --install                                       # ONLY after Trikzos picked it

Paintings land in ~/runewake_art_archive/world_maps/<key>.png (every piece of media is kept - docs/LIBRARY.md).
`--install` converts the picked one to client/content/art/world/<biome>_<instance>_<page>.webp, the path
WorldMapScene loads. Nothing installs on its own.
"""
import argparse
import json
import os
import subprocess
import sys
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
ROOT = TOOLS.parent
SIM = ROOT / "sim" / "bin" / "Debug" / "net8.0" / "Runewake.Sim.dll"
ARCHIVE = Path(os.environ.get("RUNEWAKE_ART_ARCHIVE", str(Path.home() / "runewake_art_archive"))) / "world_maps"
W, H = 1600, 960   # 5:3, the world page canvas (1000 x 600) at paint size


def plan(biome, instance, page):
    if not SIM.exists():
        subprocess.run(["dotnet", "build", str(ROOT / "sim" / "Runewake.Sim.csproj"), "-v", "q"], check=True)
    out = subprocess.run(["dotnet", str(SIM), "world-page", "--root", str(ROOT), "--biome", biome,
                          "--instance", str(instance), "--page", str(page)], capture_output=True, text=True, check=True).stdout
    return json.loads(out)


def pages_in_area(biome, instance):
    # the generator says how many pages an area has; ask page by page until it refuses
    n = 0
    while n < 12:
        try:
            plan(biome, instance, n)
            n += 1
        except subprocess.CalledProcessError:
            break
    return n


def paint(p, out, painter):
    prompt, neg = p["prompt"], p.get("negative") or ""
    out.parent.mkdir(parents=True, exist_ok=True)
    if painter == "lora":
        sys.path.insert(0, str(TOOLS))
        import style_lora  # noqa: E402
        if not style_lora.active_model():
            print("no trained style yet - painting with gemini instead (style_lora.py train makes the map LoRA)")
            painter = "gemini"
        else:
            return style_lora.paint_lora(prompt, out, width=W, height=H)
    model = "google/gemini-3-pro-image" if painter == "gemini" else "black-forest-labs/flux.2-pro"
    full = prompt + (f" Avoid: {neg}." if neg else "")
    q = subprocess.run([sys.executable, str(ROOT / "pipeline" / "gen_image_any.py"), full, str(out), "--model", model,
                        "--aspect", "5:3", "--width", str(W), "--height", str(H)], capture_output=True, text=True, timeout=600)
    cost = next((x.split()[1] for x in q.stdout.splitlines() if x.startswith("COST ")), "?")
    ok = q.returncode == 0 and out.exists()
    print(("IMAGE " if ok else "FAILED ") + f"{out.name} {cost}" + ("" if ok else " " + q.stderr[-160:].replace("\n", " ")), flush=True)
    return ok


def install(p, src):
    from PIL import Image  # noqa: E402
    dst = ROOT / "client" / "content" / "art" / "world" / f"{p['key'].split(':')[1]}_{p['key'].split(':')[2]}_{p['key'].split(':')[3]}.webp"
    dst.parent.mkdir(parents=True, exist_ok=True)
    im = Image.open(src).convert("RGB")
    im.thumbnail((1600, 960))
    im.save(dst, "WEBP", quality=86, method=6)
    print(f"INSTALLED {dst.relative_to(ROOT)} ({dst.stat().st_size // 1024} KB) - run `godot --headless --import --path client` before a capture or a build")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--biome", required=True)
    ap.add_argument("--instance", type=int, default=0)
    ap.add_argument("--page", type=int, default=0)
    ap.add_argument("--all-pages", action="store_true")
    ap.add_argument("--painter", choices=["lora", "gemini", "flux"], default="lora")
    ap.add_argument("--print", action="store_true", help="show the prompt and the plan, paint nothing")
    ap.add_argument("--install", action="store_true", help="install the archived painting of this page (after the pick)")
    a = ap.parse_args()
    pages = range(pages_in_area(a.biome, a.instance)) if a.all_pages else [a.page]
    for pg in pages:
        p = plan(a.biome, a.instance, pg)
        out = ARCHIVE / (p["key"].replace(":", "_") + ".png")
        if a.print:
            print(f"== {p['key']}  ({len(p['places'])} places)\n{p['prompt']}\n")
            for f in p["features"]:
                print(f"   {f['Id']:16} {f['Cells']:26} spots={len(f['spots'])}")
            continue
        if a.install:
            if out.exists():
                install(p, out)
            else:
                print(f"no painting for {p['key']} at {out}")
            continue
        if out.exists():
            print(f"kept {out.name} (already painted; delete it to repaint)")
            continue
        paint(p, out, a.painter)


if __name__ == "__main__":
    main()

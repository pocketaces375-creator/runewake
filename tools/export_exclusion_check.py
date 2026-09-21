#!/usr/bin/env python3
"""
tools/export_exclusion_check.py — FABLE-019

Find every res:// file the CODE references that the Android export will NOT
ship. Such a file exists on the build machine (so every headless capture and
every test sees it) and is missing on the phone (so the feature silently
doesn't happen). That is exactly how the title animation lost its vortex and
mist for three rounds: both PNGs sat in content/art/title/layers/, which
export_presets.cfg excludes.

Godot's include/exclude filters are comma-separated fnmatch globs matched
against the res-relative path.  Exit 1 if anything referenced is excluded.
"""
import fnmatch, os, re, sys, configparser

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
CLIENT = os.path.join(ROOT, "client")

def presets():
    cp = configparser.ConfigParser(interpolation=None, strict=False)
    cp.read(os.path.join(CLIENT, "export_presets.cfg"))
    out = []
    for sec in cp.sections():
        if re.fullmatch(r"preset\.\d+", sec):
            name = cp.get(sec, "name", fallback=sec).strip('"')
            exc = cp.get(sec, "exclude_filter", fallback='""').strip('"')
            out.append((name, [g.strip() for g in exc.split(",") if g.strip()]))
    return out

REF = re.compile(r'"res://([^"]+?\.(?:png|jpg|jpeg|webp|svg|ogg|wav|mp3|ttf|otf|json|tscn|tres|gdshader|shader))"')

def references():
    refs = {}
    for dirpath, _, files in os.walk(os.path.join(CLIENT, "scripts")):
        for f in files:
            if not f.endswith(".cs"): continue
            p = os.path.join(dirpath, f)
            for i, line in enumerate(open(p, encoding="utf-8", errors="replace"), 1):
                s = line.strip()
                if s.startswith("//") or s.startswith("///") or s.startswith("*"): continue
                for m in REF.finditer(line):
                    refs.setdefault(m.group(1), []).append(f"{os.path.relpath(p, ROOT)}:{i}")
    return refs

def main():
    refs = references()
    bad = 0
    for name, globs in presets():
        hits = []
        for path, where in sorted(refs.items()):
            g = next((g for g in globs if fnmatch.fnmatch(path, g)), None)
            if g and os.path.exists(os.path.join(CLIENT, path)):
                hits.append((path, g, where))
        if hits:
            print(f"✗ preset '{name}': {len(hits)} file(s) the code loads are EXCLUDED from the APK")
            for path, g, where in hits:
                print(f"    res://{path}\n        excluded by '{g}'\n        loaded at {where[0]}" + (f" (+{len(where)-1} more)" if len(where) > 1 else ""))
            bad += len(hits)
        else:
            print(f"✓ preset '{name}': every existing file the code references ships ({len(refs)} references checked)")
    sys.exit(1 if bad else 0)

if __name__ == "__main__":
    main()

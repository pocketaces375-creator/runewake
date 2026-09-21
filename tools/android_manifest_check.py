#!/usr/bin/env python3
"""
tools/android_manifest_check.py — FABLE-019

Android permissions that the game needs, read from client/export_presets.cfg
(Godot writes them into the APK manifest at export). A desktop run can never
see these: the first accounts build shipped with permissions/internet=false,
and the Supabase smoke test passed 21/21 from Hermes's machine while every
request on the phone died with "No connection".

Exit 0 when the "Android Release" preset requests everything listed.
"""
import configparser, os, sys

REQUIRED = {
    "permissions/internet": "accounts + cloud save (Supabase) — without it every request fails on the phone",
}

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
cp = configparser.ConfigParser(interpolation=None, strict=False)
cp.read(os.path.join(ROOT, "client", "export_presets.cfg"))

preset = next((s for s in cp.sections() if cp.get(s, "name", fallback="").strip('"') == "Android Release"), None)
if preset is None:
    print("✗ no 'Android Release' preset in client/export_presets.cfg"); sys.exit(1)
opts = f"{preset}.options"
bad = 0
for key, why in REQUIRED.items():
    val = cp.get(opts, key, fallback="(missing)").strip()
    if val == "true":
        print(f"✓ {key}=true")
    else:
        print(f"✗ {key}={val} — {why}"); bad += 1
sys.exit(1 if bad else 0)

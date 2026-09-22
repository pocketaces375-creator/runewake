#!/usr/bin/env python3
"""FABLE-019b — BLOCKING. Game code must never make web requests with .NET's
own HTTPS stack: on Android it aborts the app ("No usable version of libssl
was found"), uncatchably, on the first https request. Every HttpClient in
client/scripts must come from Http.Create() (Godot TLS, GodotHttpHandler.cs),
and every Supabase service must be handed one."""
import re, sys, pathlib
root = pathlib.Path(__file__).resolve().parent.parent / "client" / "scripts"
bad = []
for f in root.rglob("*.cs"):
    if f.name.startswith("Zz") or f.name == "GodotHttpHandler.cs":
        continue
    for i, line in enumerate(f.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
        code = line.split("//", 1)[0]
        if re.search(r"new\s+(System\.Net\.Http\.)?HttpClient\s*[\({]", code):
            bad.append(f"{f.relative_to(root.parent.parent)}:{i}: new HttpClient — use Http.Create()")
        if re.search(r"new\s+(System\.Net\.Http\.)?(HttpClientHandler|SocketsHttpHandler)\b", code):
            bad.append(f"{f.relative_to(root.parent.parent)}:{i}: .NET socket handler — use Http.Create()")
        m = re.search(r"new\s+(SupabaseAuth|CloudSaveSync|RelicLedgerSync)\s*\(([^()]*)\)", code)
        if m and "Http.Create" not in m.group(2):
            bad.append(f"{f.relative_to(root.parent.parent)}:{i}: {m.group(1)} without Http.Create() — it would fall back to .NET HTTPS")
if bad:
    print("✗ .NET HTTPS in game code (aborts on Android):"); print("\n".join("  " + b for b in bad)); sys.exit(1)
print("✓ every web request goes through Godot TLS (Http.Create)")

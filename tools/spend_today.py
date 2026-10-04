#!/usr/bin/env python3
"""
tools/spend_today.py — where today's OpenRouter money went, in three lines. Spends nothing.

  OpenRouter key, today   what OpenRouter itself says this key spent today (its day is UTC)
  art tools               every paid call the art tools logged (~/runewake_art_archive/spend.jsonl):
                          paintings, screener looks, planning — with the count of each
  everything else         the difference: agent chat and lane sessions on the same key
                          (Tcgbot, self-improvement reviews, foreman lanes)

Usage: python3 tools/spend_today.py
"""
import json
import os
import time
import urllib.request

LOG = os.path.expanduser("~/runewake_art_archive/spend.jsonl")


def env_key():
    if os.environ.get("OPENROUTER_API_KEY"):
        return os.environ["OPENROUTER_API_KEY"]
    try:
        for line in open(os.path.expanduser("~/.hermes/.env"), encoding="utf-8"):
            if line.strip().startswith("OPENROUTER_API_KEY="):
                return line.split("=", 1)[1].strip().strip('"').strip("'")
    except OSError:
        pass
    return ""


def key_usage():
    try:
        req = urllib.request.Request("https://openrouter.ai/api/v1/auth/key",
                                     headers={"Authorization": "Bearer " + env_key()})
        with urllib.request.urlopen(req, timeout=20) as r:
            d = json.load(r).get("data") or {}
        return d.get("usage_daily"), d.get("usage")
    except Exception as e:  # noqa: BLE001
        print(f"(could not read the OpenRouter key: {e})")
        return None, None


def main():
    utc_today = time.strftime("%Y-%m-%d", time.gmtime())
    kinds = {}
    try:
        for line in open(LOG, encoding="utf-8"):
            try:
                r = json.loads(line)
                local = time.mktime(time.strptime(r["t"], "%Y-%m-%d %H:%M:%S"))   # logged in local time
            except Exception:  # noqa: BLE001
                continue
            if time.strftime("%Y-%m-%d", time.gmtime(local)) != utc_today:
                continue
            k = kinds.setdefault(r.get("kind", "?"), [0, 0.0, 0])
            k[0] += 1
            if isinstance(r.get("cost"), (int, float)):
                k[1] += r["cost"]
                k[2] += 1
    except OSError:
        pass
    art = sum(v[1] for v in kinds.values())
    day, total = key_usage()
    print(f"SPEND TODAY (OpenRouter's day, UTC {utc_today}):")
    print(f"  OpenRouter key, today : {'n/a' if day is None else f'${day:.2f}'}")
    names = {"paint": "painting", "look": "screener look", "text": "planning call"}
    print(f"  art tools             : ${art:.2f}  (" + ", ".join(
        f"{n} {names.get(k, k)}{'s' if n != 1 else ''} ${c:.2f}" for k, (n, c, _) in sorted(kinds.items())) + ")")
    if day is not None:
        print(f"  everything else       : ${day - art:.2f}  (agent chat + lane sessions on the same key)")
    paints = kinds.get("paint", [0, 0.0, 0])
    if paints[2]:
        print(f"  per painting          : ${paints[1] / paints[2]:.3f}")


if __name__ == "__main__":
    main()

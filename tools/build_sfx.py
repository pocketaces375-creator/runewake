#!/usr/bin/env python3
"""
tools/build_sfx.py — create the 12 Runewake SFX from CC0 source samples.

Reads from client/content/audio/sfx/ (where the 80 CC0 RPG SFX pack lives).
Writes to  client/content/audio/sfx/<id>.ogg  for each of the 12 named IDs.

Simple mappings are copied/trimmed. Composite ones (card_play, weapon_fire,
turn_start, victory, defeat, card_unlock) are built via ffmpeg layering,
pitch-shifting, and reverb.

Usage:  python3 tools/build_sfx.py [--dry-run]
"""
import os, subprocess, sys

SFX_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                       "client", "content", "audio", "sfx")
SRC = lambda name: os.path.join(SFX_DIR, name)
DST = lambda name: os.path.join(SFX_DIR, name)

def ff(cmd, desc=""):
    """Run ffmpeg, print description."""
    if "--dry-run" in sys.argv:
        print(f"[DRY-RUN] {desc}")
        print(f"  ffmpeg {' '.join(cmd)}")
        return True
    print(f"  {desc}")
    r = subprocess.run(["ffmpeg", "-y", "-loglevel", "error"] + cmd)
    return r.returncode == 0


def main():
    # ── 1. ui_tap — soft stone tick ──────────────────────────────────
    # item_stone_01.ogg is 0.73s — trim to first 0.25s, fade out
    ff([
        "-i", SRC("item_stone_01.ogg"),
        "-af", "atrim=0:0.25,afade=t=out:d=0.1",
        "-ac", "1", DST("ui_tap.ogg")
    ], "ui_tap — trimmed stone tick")

    # ── 2. card_lift — leather/parchment shift ────────────────────────
    # book_01.ogg is 0.72s paper rustle — trim, slight pitch down
    ff([
        "-i", SRC("book_01.ogg"),
        "-af", "atrim=0:0.4,afade=t=out:d=0.1,asetrate=42000",
        "-ac", "1", DST("card_lift.ogg")
    ], "card_lift — parchment shift, slightly pitched down")

    # ── 3. card_play — stone thunk + faint chime (layer) ──────────────
    # mix item_stone_02.ogg (thunk) + item_gem_01.ogg (chime)
    ff([
        "-i", SRC("item_stone_02.ogg"),
        "-i", SRC("item_gem_01.ogg"),
        "-filter_complex",
        "[0:a]atrim=0:0.35,afade=t=out:d=0.12,volume=1.0[a];"
        "[1:a]atrim=0:0.25,adelay=200|200,afade=t=in:d=0.02,volume=0.5[b];"
        "[a][b]amix=inputs=2:duration=first,volume=1.2",
        "-ac", "1", DST("card_play.ogg")
    ], "card_play — stone thunk + faint gem chime")

    # ── 4. attack — short blade/impact ────────────────────────────────
    ff([
        "-i", SRC("blade_01.ogg"),
        "-af", "volume=1.5",
        "-ac", "1", DST("attack.ogg")
    ], "attack — blade impact, slightly boosted")

    # ── 5. creature_death — dry crumble / bone settle ────────────────
    # creature_die_01.ogg already exists — just copy
    ff([
        "-i", SRC("creature_die_01.ogg"),
        "-af", "volume=1.0",
        "-ac", "1", DST("creature_death.ogg")
    ], "creature_death — dry crumble (from creature_die_01)")

    # ── 6. weapon_pip — small crystal ting ────────────────────────────
    ff([
        "-i", SRC("item_gem_01.ogg"),
        "-af", "atrim=0:0.25,afade=t=out:d=0.15,volume=1.0",
        "-ac", "1", DST("weapon_pip.ogg")
    ], "weapon_pip — crystal ting")

    # ── 7. weapon_fire — deep resonant bloom ──────────────────────────
    # spell_01.ogg layered with low synth tone
    ff([
        "-i", SRC("spell_01.ogg"),
        "-filter_complex",
        "[0:a]atrim=0:0.8,volume=0.7[a];"
        "[0:a]atrim=0:0.3,lowpass=f=200,"
        "volume=0.4,adelay=100|100[b];"
        "[a][b]amix=inputs=2:duration=first,volume=1.3",
        "-ac", "1", DST("weapon_fire.ogg")
    ], "weapon_fire — resonant bloom (spell + lowpass layer)")

    # ── 8. weapon_suppress — hollow smothering thud ──────────────────
    ff([
        "-i", SRC("wood_01.ogg"),
        "-af", "lowpass=f=400,volume=0.8",
        "-ac", "1", DST("weapon_suppress.ogg")
    ], "weapon_suppress — hollow muffled thud")

    # ── 9. turn_start — soft low gong ─────────────────────────────────
    ff([
        "-i", SRC("metal_01.ogg"),
        "-af", "lowpass=f=300,atrim=0:0.5,afade=t=out:d=0.3,volume=0.5",
        "-ac", "1", DST("turn_start.ogg")
    ], "turn_start — soft low gong")

    # ── 10. victory — short rising phrase, flute + hum ────────────────
    # Synthesize: sine sweep from 200→600Hz over 1.5s + sine 350Hz hum
    ff([
        "-f", "lavfi", "-i",
        "sine=frequency=200:duration=1.5,volume=0.15",
        "-f", "lavfi", "-i",
        "sine=frequency=350:duration=1.5,volume=0.1",
        "-filter_complex",
        "[0:a]asetrate=44100*1.5,bass=g=4:f=200:w=0.5[a];"
        "[1:a]adelay=100|100,volume=0.6[b];"
        "[a][b]amix=inputs=2:duration=first,afade=t=out:d=0.5,"
        "aformat=sample_rates=44100:channel_layouts=mono",
        "-ac", "1", DST("victory.ogg")
    ], "victory — rising sine phrase (placeholder — replace with real sample if needed)")

    # ── 11. defeat — low descending drone ─────────────────────────────
    ff([
        "-f", "lavfi", "-i",
        "sine=frequency=160:duration=2.0,volume=0.12",
        "-af",
        "asetrate=44100*0.92,volume=0.5,afade=t=out:d=0.8,"
        "aformat=sample_rates=44100:channel_layouts=mono",
        "-ac", "1", DST("defeat.ogg")
    ], "defeat — low descending sine drone")

    # ── 12. card_unlock — rising shimmer ──────────────────────────────
    # item_gem_01 layered with higher chime + gentle ascending tone
    ff([
        "-i", SRC("item_gem_01.ogg"),
        "-i", SRC("item_gem_02.ogg"),
        "-f", "lavfi", "-i",
        "sine=frequency=880:duration=1.5,volume=0.08",
        "-filter_complex",
        "[0:a]atrim=0:0.5,afade=t=out:d=0.3,volume=0.7[a];"
        "[1:a]atrim=0:0.3,adelay=150|150,afade=t=in:d=0.05,volume=0.5[b];"
        "[2:a]atrim=0:1.0,adelay=50|50,afade=t=in:d=0.1,"
        "afade=t=out:d=0.4,volume=0.4[c];"
        "[a][b][c]amix=inputs=3:duration=longest,volume=1.5",
        "-ac", "1", DST("card_unlock.ogg")
    ], "card_unlock — rising shimmer (gems + sine overlay)")

    # ── Verify ────────────────────────────────────────────────────────
    ids = ["ui_tap", "card_lift", "card_play", "attack", "creature_death",
           "weapon_pip", "weapon_fire", "weapon_suppress", "turn_start",
           "victory", "defeat", "card_unlock"]
    print("\nGenerated:")
    for sid in ids:
        path = DST(f"{sid}.ogg")
        if os.path.exists(path):
            dur = subprocess.run(
                ["ffprobe", "-v", "error", "-show_entries", "format=duration",
                 "-of", "csv=p=0", path],
                capture_output=True, text=True).stdout.strip()
            sz = os.path.getsize(path)
            print(f"  {sid}.ogg  {dur}s  {sz//1024}KB")
        else:
            print(f"  {sid}.ogg  MISSING!")


if __name__ == "__main__":
    main()
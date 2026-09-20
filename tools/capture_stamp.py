#!/usr/bin/env python3
"""
capture_stamp.py — prove a screenshot is a real render of the current code.

WHY THIS EXISTS
---------------
On 2026-09-18 the FABLE-006/007 landing was reported green on every automated
check, and the title_test.png everyone looked at afterwards was five days old —
rendered before TitleAtmosphere.cs existed. Nothing was lying; there was simply
no way, looking at a PNG, to tell which code produced it. This is the third time
this project has hit that (TASK-VERIFY-TITLE-1, TASK-COMMIT-TITLE-1, FABLE-007).

A capture is trustworthy only if you can answer three questions about it:

  1. Is it REAL?    A rendered frame, not a black screen, a flat fill, or the
                    same frame copied under a second mode's name.
  2. Is it FRESH?   Rendered from the code that is in the tree right now.
  3. Is it INTACT?  The bytes on disk are the bytes the capture run produced.

This tool answers all three, and writes the answers into a committed file so
they survive the chat window they were posted in.

HOW IT WORKS
------------
`--record` runs at the end of a capture run. It computes a fingerprint of the
render-affecting source tree and writes artifacts/captures/CAPTURE_STAMP.json,
mapping every PNG to {sha256, size, dimensions, fingerprint, captured_at}.
That file is committed alongside the PNGs.

`--verify` recomputes the fingerprint from the tree as it stands now and
compares. Because the fingerprint covers client/, engine/ and content/, any
edit to rendering code makes every capture report STALE until it is re-rendered.
There is nothing to remember and nothing to trust.

The fingerprint deliberately EXCLUDES tests/, sim/, and the packaging files
under client/ (export_presets.cfg, android/, .godot/). A guard that cries stale
over a change that cannot alter a pixel — a versionCode bump, a new unit test —
is a guard people learn to ignore, and then it protects nothing.

USAGE
-----
  capture_stamp.py --record [--run-started <unix_ts>]
      Stamp every PNG in artifacts/captures/. With --run-started, refuses to
      stamp a PNG older than that timestamp — that PNG did not come from this
      run, which is exactly the bug this tool exists to catch.

  capture_stamp.py --verify [name ...]
      Verify the named captures (default: all stamped ones). Exit 0 only if
      every one is FRESH, INTACT and REAL.

  capture_stamp.py --status
      Human-readable table of every capture and its verdict. Always exits 0.
      This is the "is the thing I am looking at real" command.

  capture_stamp.py --fingerprint
      Print the current tree fingerprint and exit.
"""

import argparse
import hashlib
import json
import os
import struct
import subprocess
import sys
import time
from pathlib import Path

try:
    from PIL import Image
    HAS_PIL = True
except ImportError:  # same graceful degradation as tools/ui_lint.py
    HAS_PIL = False

REPO = Path(__file__).resolve().parent.parent
CAPTURE_DIR = REPO / "artifacts" / "captures"
STAMP_PATH = CAPTURE_DIR / "CAPTURE_STAMP.json"

# Directories whose contents can change what a rendered frame looks like.
# tests/ and sim/ are deliberately absent — see module docstring.
RENDER_DIRS = ["client", "engine", "content"]

# Paths inside those directories that cannot change a pixel. Bumping the
# versionCode in export_presets.cfg before an APK build must not mark every
# screenshot stale — a guard that fires on a version bump is a guard that
# gets disabled the first time it blocks a release.
NON_RENDER_PATHS = [
    ":(exclude)client/export_presets.cfg",   # APK packaging only
    ":(exclude)client/android",              # Android build template
    ":(exclude)client/.godot",               # Godot's local import cache
]

# A capture whose pixels are this uniform did not render anything meaningful.
# Tuned against the real captures in this repo: the darkest legitimate screen
# (defeat_overlay, a dimmed board behind a panel) sits far above these floors.
MIN_STDDEV = 6.0          # near-flat fill, e.g. a solid black or grey frame
MIN_DISTINCT_FRAC = 0.02  # fraction of sampled pixels that must be distinct

# Slack between "the run started" and a capture's mtime, for filesystem mtime
# granularity and clock skew. Generous enough not to nag, far below the hours
# or days that separate a genuinely stale capture from a fresh one.
MTIME_GRACE = 5.0

# Captures already broken before this tool existed. They are listed so the
# pipeline is usable today, NOT so the breakage is forgiven — every run prints
# them, and anything not on this list that is blank or duplicated fails hard.
# Fix one, delete its line. Do not add to this list to make a red run go green.
KNOWN_BROKEN = {
    "slots_test.png": "renders a flat black frame — never worked (noted in TASK-CAPTURE-COVERAGE-2)",
    "slots_test_deleted.png": "flat black frame, same cause as slots_test",
    "slots_test_filled.png": "flat black frame, same cause as slots_test",
    "duel_test_hand10.png": "byte-identical to duel_test — the 10-card hand variant never renders",
    "accept_h_hand_true.png": "byte-identical to accept_d_hand_card_true — one-off BORDER-FIX crop",
}


def run(cmd, cwd=REPO):
    try:
        out = subprocess.run(
            cmd, cwd=str(cwd), capture_output=True, text=True, timeout=120
        )
        return out.stdout.strip(), out.returncode
    except Exception:
        return "", 1


def tree_fingerprint():
    """Identity of the bytes that draw the screens, as they sit on disk.

    This is a fingerprint of WORKING TREE CONTENT, not of git history, and
    that distinction is the whole design. The obvious implementation — hash
    the committed tree SHAs, then mix in a hash of the uncommitted diff —
    changes its answer when you commit, even though not one byte of source
    moved. Captures taken before the commit would flip to STALE the moment
    the commit landed, every single time, and a guard that cries wolf on
    every commit is a guard that gets switched off. So: content only.

    Fast path: git's index already holds the blob SHA of every unmodified
    tracked file, so those cost nothing to look up. Only files that differ
    from the index, plus untracked ones, are hashed from disk. On this repo
    that is ~0.02s against ~11s for hashing all 89 MB of art every time.
    """
    contents = {}  # path -> blob sha of the bytes currently on disk

    # 1. Tracked files, as recorded in the index: "<mode> <sha> <stage>\t<path>"
    listing, rc = run(["git", "ls-files", "-s", "--"] + RENDER_DIRS + NON_RENDER_PATHS)
    if rc != 0:
        raise SystemExit("capture_stamp: not a git repository (git ls-files failed)")
    for line in listing.splitlines():
        meta, _, path = line.partition("\t")
        fields = meta.split()
        if path and len(fields) >= 2:
            contents[path] = fields[1]

    # 2. Files whose worktree bytes differ from the index, plus untracked
    #    ones: hash what is actually on disk, overwriting the index answer.
    modified, _ = run(["git", "diff-files", "--name-only", "--"] + RENDER_DIRS + NON_RENDER_PATHS)
    untracked, _ = run(
        ["git", "ls-files", "--others", "--exclude-standard", "--"] + RENDER_DIRS + NON_RENDER_PATHS
    )
    dirty = [p for p in (modified.splitlines() + untracked.splitlines()) if p]
    for path in dirty:
        full = REPO / path
        if not full.is_file():
            contents.pop(path, None)  # deleted since the index was written
            continue
        sha, rc = run(["git", "hash-object", "--", path])
        if rc == 0 and sha:
            contents[path] = sha

    joined = "\n".join(f"{p} {contents[p]}" for p in sorted(contents))
    return hashlib.sha256(joined.encode()).hexdigest()[:12]


def png_dimensions(path):
    """Read width/height from the IHDR chunk without decoding the image."""
    try:
        with open(path, "rb") as f:
            if f.read(8) != b"\x89PNG\r\n\x1a\n":
                return None
            f.read(4)
            if f.read(4) != b"IHDR":
                return None
            ihdr = f.read(8)
            return struct.unpack(">II", ihdr)
    except Exception:
        return None


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def realness(path):
    """Is this a rendered frame, or a blank/flat placeholder?

    Returns (ok, detail). Without PIL we cannot look at pixels, so we say so
    rather than passing silently — an unchecked capture is not a checked one.
    """
    if not HAS_PIL:
        return True, "pixels unchecked (PIL not installed)"
    try:
        with Image.open(path) as im:
            # Sample rather than decode-and-scan the whole 2316x1080 frame.
            small = im.convert("RGB").resize((160, 90))
            raw = small.tobytes()
            px = [tuple(raw[i:i + 3]) for i in range(0, len(raw), 3)]
    except Exception as e:
        return False, f"unreadable ({e})"

    lum = [0.299 * r + 0.587 * g + 0.114 * b for r, g, b in px]
    n = len(lum)
    mean = sum(lum) / n
    var = sum((v - mean) ** 2 for v in lum) / n
    stddev = var ** 0.5
    distinct = len(set(px)) / n

    if stddev < MIN_STDDEV:
        return False, f"near-flat frame (stddev {stddev:.1f} < {MIN_STDDEV})"
    if distinct < MIN_DISTINCT_FRAC:
        return False, f"almost no distinct colours ({distinct:.3f})"
    return True, f"stddev {stddev:.1f}, distinct {distinct:.2f}"


def live_partners(entry):
    """Duplicate partners that are not already tracked as broken.

    If a good capture's only twin is a known-broken mode, the good one is not
    the problem and must not be flagged — otherwise one broken mode poisons
    the verdict of a screen that rendered perfectly well.
    """
    return [p for p in entry.get("duplicate_of", []) if p not in KNOWN_BROKEN]


def list_captures():
    if not CAPTURE_DIR.is_dir():
        return []
    return sorted(p for p in CAPTURE_DIR.glob("*.png"))


def load_stamp():
    if not STAMP_PATH.exists():
        return None
    try:
        return json.loads(STAMP_PATH.read_text())
    except Exception:
        return None


def cmd_record(args):
    fp = tree_fingerprint()
    now = time.time()
    pngs = list_captures()
    if not pngs:
        print("capture_stamp: no PNGs in artifacts/captures/ — nothing to record", file=sys.stderr)
        return 1

    # Only the modes this run promised to produce are held to the freshness
    # rule. artifacts/captures/ also holds one-off debug crops from old tasks
    # (shot_*, accept_*, step5_*) that no run regenerates; holding those to
    # "must be newer than this run" would fail every run forever.
    expected = set()
    if args.expect:
        for n in args.expect.split(","):
            n = n.strip()
            if n:
                expected.add(n if n.endswith(".png") else n + ".png")

    entries = {}
    too_old = []
    missing = sorted(n for n in expected if not (CAPTURE_DIR / n).exists())
    for p in pngs:
        mtime = p.stat().st_mtime
        if (args.run_started is not None
                and (not expected or p.name in expected)
                and mtime < args.run_started - MTIME_GRACE):
            too_old.append((p.name, mtime))
            continue
        dims = png_dimensions(p)
        ok, detail = realness(p)
        entries[p.name] = {
            "sha256": sha256_file(p),
            "bytes": p.stat().st_size,
            "width": dims[0] if dims else None,
            "height": dims[1] if dims else None,
            "captured_at": round(mtime, 3),
            "real": ok,
            "real_detail": detail,
            "one_off": bool(expected) and p.name not in expected,
        }

    if missing:
        print("capture_stamp: REFUSING TO STAMP — these modes ran but produced no file:",
              file=sys.stderr)
        for name in missing:
            print(f"  {name}", file=sys.stderr)
        return 1

    if too_old:
        print("capture_stamp: REFUSING TO STAMP — these PNGs predate this capture run:", file=sys.stderr)
        for name, mtime in too_old:
            age = (args.run_started - mtime) / 60.0
            print(f"  {name}  ({age:.1f} min older than the run)", file=sys.stderr)
        print("  Their capture did not produce a file, so the previous one is still on disk.", file=sys.stderr)
        print("  That is the stale-screenshot bug. Fix the capture, do not stamp around it.", file=sys.stderr)
        return 1

    # A mode that renders the identical frame as another mode is almost always
    # the capture harness writing the same screen twice, not two real screens.
    by_hash = {}
    for name, e in entries.items():
        by_hash.setdefault(e["sha256"], []).append(name)
    for names in by_hash.values():
        if len(names) > 1:
            for n in names:
                entries[n]["duplicate_of"] = sorted(x for x in names if x != n)

    dupes = {n: live_partners(e) for n, e in entries.items()
             if n not in KNOWN_BROKEN and live_partners(e)}
    fakes = {n: e["real_detail"] for n, e in entries.items()
             if not e["real"] and n not in KNOWN_BROKEN}
    tracked = sorted(n for n in entries
                     if n in KNOWN_BROKEN
                     and (not entries[n]["real"] or entries[n].get("duplicate_of")))

    stamp = {
        "fingerprint": fp,
        "recorded_at": round(now, 3),
        "recorded_at_iso": time.strftime("%Y-%m-%dT%H:%M:%S%z", time.localtime(now)),
        "head": run(["git", "rev-parse", "HEAD"])[0],
        "render_dirs": RENDER_DIRS,
        "pil_available": HAS_PIL,
        "captures": entries,
    }
    CAPTURE_DIR.mkdir(parents=True, exist_ok=True)
    STAMP_PATH.write_text(json.dumps(stamp, indent=2, sort_keys=True) + "\n")

    print(f"capture_stamp: recorded {len(entries)} captures at fingerprint {fp}")
    rc = 0
    if dupes:
        print("capture_stamp: IDENTICAL FRAMES under different mode names:", file=sys.stderr)
        for name, others in sorted(dupes.items()):
            print(f"  {name} == {', '.join(others)}", file=sys.stderr)
        print("  Two modes rendering byte-identical frames means one of them never ran.", file=sys.stderr)
        rc = 1
    if fakes:
        print("capture_stamp: NOT A RENDERED FRAME:", file=sys.stderr)
        for name, detail in sorted(fakes.items()):
            print(f"  {name}: {detail}", file=sys.stderr)
        rc = 1
    if tracked:
        print(f"capture_stamp: {len(tracked)} known-broken capture(s) still broken "
              f"(tracked in KNOWN_BROKEN, not blocking): {', '.join(tracked)}")
    return rc


def verdicts(names=None):
    """Yield (name, verdict, detail) for each requested capture."""
    stamp = load_stamp()
    current = tree_fingerprint()

    if stamp is None:
        for n in (names or [p.stem for p in list_captures()]):
            yield n, "UNSTAMPED", "no CAPTURE_STAMP.json — run capture_stamp.py --record"
        return

    recorded_fp = stamp.get("fingerprint")
    entries = stamp.get("captures", {})
    # With no names, judge the modes the capture run actually produces. The
    # directory also holds one-off debug crops from finished tasks; those are
    # stale by definition the moment any code changes, and failing the APK
    # gate forever over a September screenshot crop helps nobody. Name one
    # explicitly and you still get a verdict on it.
    wanted = names or sorted(
        e[:-4] for e, v in entries.items() if not v.get("one_off")
    )

    for name in wanted:
        fname = name if name.endswith(".png") else name + ".png"
        path = CAPTURE_DIR / fname
        entry = entries.get(fname)

        if entry is None:
            yield name, "UNSTAMPED", "not in CAPTURE_STAMP.json"
            continue
        if not path.exists():
            yield name, "MISSING", "stamped but the file is gone"
            continue
        actual = sha256_file(path)
        if actual != entry["sha256"]:
            yield name, "TAMPERED", (
                f"file changed since it was stamped "
                f"({entry['sha256'][:8]} → {actual[:8]})"
            )
            continue
        if fname in KNOWN_BROKEN:
            yield name, "KNOWN-BAD", KNOWN_BROKEN[fname]
            continue
        if not entry.get("real", True):
            yield name, "FAKE", entry.get("real_detail", "not a rendered frame")
            continue
        partners = live_partners(entry)
        if partners:
            yield name, "DUPLICATE", (
                f"byte-identical to {', '.join(partners)} — "
                f"one of these modes never rendered"
            )
            continue
        if recorded_fp != current:
            when = stamp.get("recorded_at_iso")
            when = f" on {when[:16]}" if when else ""
            yield name, "STALE", (
                f"rendered{when} from code {recorded_fp}; tree is now {current}"
            )
            continue
        yield name, "FRESH", (
            f"{entry.get('width')}x{entry.get('height')} @ {recorded_fp}"
        )


def cmd_verify(args):
    names = args.names or None
    bad, known = 0, 0
    for name, verdict, detail in verdicts(names):
        ok = verdict in ("FRESH", "KNOWN-BAD")
        marker = "ok " if verdict == "FRESH" else ("-- " if verdict == "KNOWN-BAD" else "!! ")
        stream = sys.stdout if verdict == "FRESH" else sys.stderr
        print(f"{marker}{verdict:<9} {name:<26} {detail}", file=stream)
        if verdict == "KNOWN-BAD":
            known += 1
        elif not ok:
            bad += 1
    if bad:
        print(
            f"\ncapture_stamp: {bad} capture(s) are not a trustworthy picture of the "
            f"current code.\nRegenerate them (tools/regen_captures.sh) before drawing "
            f"any conclusion from them.",
            file=sys.stderr,
        )
        return 1
    tail = f" ({known} known-broken, tracked)" if known else ""
    print(f"\ncapture_stamp: all requested captures are fresh renders of the current tree{tail}.")
    return 0


def cmd_list_current(args):
    """The capture names the last run actually produced.

    artifacts/captures/ also holds 44 layout files from modes nothing
    regenerates — old tutorial beats, uxwalk frames, hand4/hand10 variants.
    Anything judging "the captures" needs to know which ones are current, or it
    ends up failing a task over a screenshot from a build that no longer exists.
    """
    stamp = load_stamp()
    if stamp:
        names = sorted(n[:-4] for n, v in stamp.get("captures", {}).items()
                       if not v.get("one_off"))
        if names:
            print("\n".join(names))
            return 0
    # No stamp yet: fall back to the runner's own MODES list so this still
    # answers correctly on a fresh clone.
    runner = REPO / "tools" / "regen_captures.sh"
    if runner.exists():
        import re as _re
        modes = _re.findall(r'"([a-z0-9_]+):\d+:\d+"', runner.read_text())
        if modes:
            print("\n".join(sorted(set(modes))))
            return 0
    return 1


def cmd_status(args):
    stamp = load_stamp()
    current = tree_fingerprint()
    print(f"tree fingerprint now : {current}")
    if stamp:
        print(f"captures rendered at : {stamp.get('fingerprint')} "
              f"({stamp.get('recorded_at_iso', '?')})")
        print(f"head at capture time : {str(stamp.get('head'))[:12]}")
    else:
        print("captures rendered at : (never stamped)")
    print()
    rows = list(verdicts(args.names or None))
    width = max([len(n) for n, _, _ in rows] + [10])
    for name, verdict, detail in rows:
        print(f"  {name:<{width}}  {verdict:<9}  {detail}")
    fresh = sum(1 for _, v, _ in rows if v == "FRESH")
    print(f"\n  {fresh}/{len(rows)} fresh")
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--record", action="store_true", help="stamp the captures on disk")
    g.add_argument("--verify", action="store_true", help="fail unless captures are fresh")
    g.add_argument("--status", action="store_true", help="print a verdict table")
    g.add_argument("--fingerprint", action="store_true", help="print the tree fingerprint")
    g.add_argument("--list-current", action="store_true",
                   help="print the capture names the last run produced, one per line")
    ap.add_argument("--run-started", type=float, default=None,
                    help="unix timestamp the capture run began; refuses to stamp older files")
    ap.add_argument("--expect", default=None,
                    help="comma-separated modes this run produced; only these are held "
                         "to the freshness rule (others are one-off crops)")
    ap.add_argument("names", nargs="*", help="capture names (default: all)")
    args = ap.parse_args()

    if args.fingerprint:
        print(tree_fingerprint())
        return 0
    if args.list_current:
        return cmd_list_current(args)
    if args.record:
        return cmd_record(args)
    if args.verify:
        return cmd_verify(args)
    return cmd_status(args)


if __name__ == "__main__":
    sys.exit(main())

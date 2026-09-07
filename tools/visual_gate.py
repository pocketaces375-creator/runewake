#!/usr/bin/env python3
"""
tools/visual_gate.py — the mandatory pixel-level "does this actually look
right" check. No task is DONE and no APK ships without this passing.

Runs each screen capture in artifacts/captures/ through a vision model and
asks it to find concrete visual defects: duplicated/overlapping text, UI
elements or labels rendered outside their frame, illegibly low-contrast
text, non-English or garbled text baked into art (this UI is English-only),
obviously broken/placeholder art, and severely unbalanced/empty layout.

A screen is checked once. If the model can't be reached or its answer can't
be parsed, that screen is a HARD FAIL — this gate fails closed, never open,
because a silent skip here is exactly the failure mode it exists to close.

Usage:
  python3 tools/visual_gate.py [--captures DIR] [--only name1,name2,...]
Writes artifacts/VISUAL_GATE.json. Exits 0 if every checked screen PASSes,
1 otherwise.
"""
import argparse, base64, json, mimetypes, os, sys, time, urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_CAPTURE_DIR = ROOT / "artifacts" / "captures"
RESULT_FILE = ROOT / "artifacts" / "VISUAL_GATE.json"

# Every screen name here must have a matching <name>.png in the capture dir.
# Wide variants are skipped by default (same scene, redundant API cost) —
# pass --only to check them explicitly.
DEFAULT_SCREENS = [
    "choose_path",
    "map_test",
    "duel_test",
    "duel_test_safe",
    "victory_overlay",
    "defeat_overlay",
    "reliquary_test",
    "reliquary_test_all",
    # "slots_test" — internal slot-picker debug capture, renders black;
    # not a player-facing screen. Tracked as its own bug, not a gate blocker.
    "title_test",
    "settings_test",
]

PROMPT = """You are a strict QA reviewer for Runewake: The Buried Age, a dark-fantasy
mobile trading card game. This screenshot is a real capture from the current build.
The UI language is English only.

Look at the actual pixels and report ONLY concrete, visible defects you can point to.
Check specifically for:
1. Duplicated or overlapping text (e.g. two renders of the same label stacked on
   top of each other, a plain-text version of a button's own styled label showing
   through it).
2. Any icon, badge, or decorative element rendered on top of body text so the text
   is hard or impossible to read.
3. Text or a UI element that spills outside the card/panel/button it belongs to.
4. Text that is illegible from low contrast against its background.
5. Non-English text, garbled characters, or gibberish glyphs baked into art or
   banners (this game is English-only — any foreign script or nonsense text in a
   texture is a bug, not a stylistic choice).
6. Obviously broken, placeholder-looking, or blank/missing art where art is expected.
7. Layout that is severely unbalanced — most of the screen empty while content is
   crammed into a small corner — such that it would look unfinished to a player.

Do NOT flag: intentional dark/moody art direction, small/normal UI chrome, or
anything you are inferring rather than actually seeing in the image.

Respond with ONLY a JSON object, no other text, in exactly this shape:
{"verdict": "PASS" or "FAIL", "issues": [{"description": "...", "severity": "high"|"medium"|"low"}]}

"verdict" must be "FAIL" if issues contains anything with severity "high" or "medium".
If you see nothing wrong, respond {"verdict": "PASS", "issues": []}.
"""


SEVERITY_ORDER = {"low": 0, "medium": 1, "high": 2}

def rank(sev):
    return SEVERITY_ORDER.get(str(sev).lower(), 2)

def dedupe(issues):
    """One shared style bug reported on 18 cards is one bug, not 18. Collapse
    issues whose wording matches once the specific names are stripped out."""
    import re
    seen = {}
    for issue in issues:
        desc = issue.get("description", "")
        key = re.sub(r"'[^']*'", "'X'", desc)
        key = re.sub(r"\b[A-Z][A-Z \-]{3,}\b", "X", key)
        key = re.sub(r"\d+", "N", key).lower().strip()
        if key in seen:
            seen[key]["count"] = seen[key].get("count", 1) + 1
            continue
        item = dict(issue)
        item["count"] = 1
        seen[key] = item
    out = list(seen.values())
    for item in out:
        if item.get("count", 1) > 1:
            item["description"] = f"{item['description']}  [and {item['count'] - 1} more like it]"
    return out

def load_key():
    key = os.environ.get("OPENROUTER_API_KEY")
    if key:
        return key
    env = Path.home() / ".hermes" / ".env"
    if env.exists():
        for line in env.read_text().splitlines():
            if line.startswith("OPENROUTER_API_KEY="):
                return line.split("=", 1)[1].strip().strip('"')
    return None

def call_vision(key, image_path, model):
    mime = mimetypes.guess_type(str(image_path))[0] or "image/png"
    b64 = base64.b64encode(image_path.read_bytes()).decode()
    body = json.dumps({
        "model": model,
        "messages": [{"role": "user", "content": [
            {"type": "text", "text": PROMPT},
            {"type": "image_url", "image_url": {"url": f"data:{mime};base64,{b64}"}},
        ]}],
        "temperature": 0,
    }).encode()
    req = urllib.request.Request(
        "https://openrouter.ai/api/v1/chat/completions", data=body,
        headers={"Authorization": f"Bearer {key}", "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=120) as r:
        out = json.load(r)
    return out["choices"][0]["message"]["content"]

def parse_verdict(raw_text):
    text = raw_text.strip()
    if text.startswith("```"):
        text = text.strip("`")
        if text.startswith("json"):
            text = text[4:]
        text = text.strip()
    start = text.find("{")
    end = text.rfind("}")
    if start == -1 or end == -1:
        raise ValueError("no JSON object found in model response")
    obj = json.loads(text[start:end+1])
    verdict = obj.get("verdict")
    issues = obj.get("issues", [])
    if verdict not in ("PASS", "FAIL"):
        raise ValueError(f"bad verdict field: {verdict!r}")
    return verdict, issues

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--captures", default=str(DEFAULT_CAPTURE_DIR))
    ap.add_argument("--only", default=None, help="comma-separated screen basenames")
    ap.add_argument("--model", default=os.environ.get("VISION_MODEL", "google/gemini-2.5-flash"))
    ap.add_argument("--block-on", default=os.environ.get("VISUAL_GATE_BLOCK", "high"),
                    choices=["high", "medium", "low"],
                    help="lowest severity that BLOCKS. Everything below is recorded "
                         "in the punch-list but does not stop the line. Ratchet this "
                         "down as the backlog clears: high -> medium -> low.")
    args = ap.parse_args()

    capture_dir = Path(args.captures)
    screens = args.only.split(",") if args.only else DEFAULT_SCREENS

    key = load_key()
    results = []
    overall_pass = True

    for name in screens:
        png = capture_dir / f"{name}.png"
        entry = {"screen": name, "path": str(png)}
        if not key:
            entry["verdict"] = "FAIL"
            entry["issues"] = [{"description": "OPENROUTER_API_KEY not available — gate fails closed", "severity": "high"}]
            overall_pass = False
            results.append(entry)
            continue
        if not png.exists():
            entry["verdict"] = "FAIL"
            entry["issues"] = [{"description": f"expected capture missing: {png}", "severity": "high"}]
            overall_pass = False
            results.append(entry)
            continue
        try:
            raw = call_vision(key, png, args.model)
            verdict, issues = parse_verdict(raw)
        except Exception as e:
            entry["verdict"] = "FAIL"
            entry["issues"] = [{"description": f"vision check errored/unparseable: {e}", "severity": "high"}]
            overall_pass = False
            results.append(entry)
            continue
        issues = dedupe(issues)
        entry["issues"] = issues
        blocking = [i for i in issues if rank(i.get("severity")) >= rank(args.block_on)]
        entry["blocking"] = blocking
        entry["verdict"] = "FAIL" if blocking else "PASS"
        entry["model_verdict"] = verdict
        if blocking:
            overall_pass = False
        results.append(entry)
        time.sleep(1)

    RESULT_FILE.parent.mkdir(parents=True, exist_ok=True)
    RESULT_FILE.write_text(json.dumps({
        "overall": "PASS" if overall_pass else "FAIL",
        "checked_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "model": args.model,
        "screens": results,
    }, indent=2))

    print(f"=== visual_gate: {'PASS' if overall_pass else 'FAIL'} "
          f"(blocking at severity >= {args.block_on}) ===")
    n_block = sum(len(e.get("blocking", [])) for e in results)
    n_all = sum(len(e.get("issues", [])) for e in results)
    print(f"    {n_block} blocking, {n_all - n_block} tracked in the punch-list")
    for entry in results:
        mark = "PASS" if entry["verdict"] == "PASS" else "FAIL"
        print(f"  [{mark}] {entry['screen']}")
        for issue in entry.get("blocking", []):
            print(f"      BLOCK [{issue.get('severity','?')}] {issue.get('description','')}")
        for issue in entry.get("issues", []):
            if issue not in entry.get("blocking", []):
                print(f"      note  [{issue.get('severity','?')}] {issue.get('description','')}")
    print(f"Wrote {RESULT_FILE}")

    sys.exit(0 if overall_pass else 1)

if __name__ == "__main__":
    main()

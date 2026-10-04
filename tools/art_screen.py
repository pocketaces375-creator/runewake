#!/usr/bin/env python3
"""tools/art_screen.py — FABLE-SCREEN-1: the quality gate between the art generator and the game.

Trikzos: "Can you please make a screener that will review the images after they're made to filter out ones
like #2 & #3 — 2 is too cut off of a picture, 3 is a duplicate again basically. I want something to filter
these so what gets pushed to the game is beautiful and finished."

Every painting is screened in three layers, cheapest first. Nothing reaches client/content/art unless it passes.

  1. TECHNICAL (local, free)   right shape for the card window · not blank or flat · not crushed black or blown
                               out · not soft/blurry · no letterbox bars or solid borders
  2. ORIGINALITY (local, free) compared with every card already in the game and every other painting in the
                               batch: too close in layout and colour to another card's art = "looks like <card>"
  3. ART DIRECTOR (vision)     a vision model (Gemini 2.5 Flash on OpenRouter, ~$0.001 a look) judges it like a
                               strict art director, with the card's name and text in hand:
                                 · is the main subject whole, or cut off by the edge?            (Trikzos's #2)
                                 · does it survive the card frame (stat band at the bottom, rim)?
                                 · defects: extra/missing limbs, mangled hands or faces, melted geometry, text,
                                   watermarks, borders, collage panels, unfinished or photographic look
                                 · does it show THIS card? does it fit the house style? is it beautiful? finished?
                               and names the subject in a few words — the same subject on another card is a
                               repeat even when the pixels differ (the burning meteor again — Trikzos's #3)

  PASS goes on to the game. FAIL never does. REVIEW (one score just under the bar) waits for a human.

COMMANDS
  art_screen.py screen [--run RUN | --dir DIR] [--no-vision]
        screens the last art_director render (or a given run, or any folder of images); writes the verdict onto
        each ledger candidate, and a sheet with ✓/✗ and the reason under every painting:
        artifacts/art_review/screen/screen_<run>.jpg
  art_screen.py pick [--install] [--allow-review]
        for each card in that run: the best PASS candidate. --install approves it through art_director (installs
        client/content/art/<id>.webp) and re-bakes its card face. Cards with no pass are listed with why.
  art_screen.py cycle <card ids…> | --stratum S | --missing  [--rounds 3] [--candidates 2] [--install]
        the whole loop, hands-off: plan fresh concepts → paint (gold references + rotating moods) → screen →
        repaint only the failures (a NEW concept when it was a repeat or off-card, a new mood otherwise) →
        … → install the winners. --max-paintings caps the spend (default 60).
  art_screen.py batch [ids…] [--n 10] [--max-paintings 14] [--max-usd X]          ← FABLE-BATCH-1, the normal way
        the preview: 10 cards not yet in the new style, spread over the strata, ONE painting each (gold references
        + rotating moods + house style), screened; only the failures are repainted, under a hard cap. Ends with
        ONE sheet — one painting per card, no comparisons — and what the batch cost (OpenRouter's own numbers).
        artifacts/art_review/preview/preview_<batch>.jpg
  art_screen.py batch --approve [BATCH]   installs exactly what that sheet showed (default: the last batch)
  art_screen.py spend              where the money goes: OpenRouter account totals, every logged call by model,
                                   paintings per run (~/runewake_art_archive/spend.jsonl)
  art_screen.py show <card id>     the screen history of one card
  art_screen.py hook               makes art_director's own `approve` refuse anything the screen hasn't passed

Set SCREEN_MODEL to use another vision model (e.g. google/gemini-2.5-pro for a stricter, slower eye).
Standalone except for its two partners: style_lora.py (OpenRouter call, sheets, house style) and art_director.py
(ledger, rendering, approval). Writes: the art ledger (candidate verdicts), ~/runewake_art_archive/style_lora/
screen.json (a cache, so a painting is never paid for twice), artifacts/art_review/screen/.
"""
import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

from PIL import Image, ImageFilter, ImageStat

TOOLS = Path(__file__).resolve().parent
REPO = TOOLS.parent
sys.path.insert(0, str(TOOLS))
import style_lora as sl  # noqa: E402

SCREEN_MODEL = os.environ.get("SCREEN_MODEL", "google/gemini-2.5-flash")
CACHE = sl.HOME / "screen.json"
OUT = REPO / "artifacts" / "art_review" / "screen"
ART = REPO / "client" / "content" / "art"

# the bar — every number here is a rule Trikzos can tighten
BAR = {"beauty": 7, "finished": 7, "style_match": 6, "matches_card": 5}
DUP_IMAGE = 0.88          # layout+colour similarity to another card's art (art_director's measure, 0–1)
DUP_SUBJECT = 0.6         # word overlap of the subject with another card's subject
SEVERE = {"extra or missing limbs", "mangled hands", "distorted face", "melted or broken geometry", "text or lettering",
          "watermark or signature", "frame or border", "collage or split panels", "blurry or unfinished"}


def _ad():
    import art_director  # noqa: E402  (numpy etc. — only when the ledger is needed)
    return art_director


def _cards():
    out = {}
    for f in sorted((REPO / "client" / "content" / "cards").glob("*.json")):
        try:
            d = json.loads(f.read_text(encoding="utf-8"))
        except ValueError:
            continue
        for c in (d.get("cards") if isinstance(d, dict) else d) or []:
            if isinstance(c, dict) and c.get("id"):
                out[c["id"]] = c
    return out


def _cache():
    try:
        return json.loads(CACHE.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}


def _save_cache(c):
    CACHE.parent.mkdir(parents=True, exist_ok=True)
    CACHE.write_text(json.dumps(c, indent=1), encoding="utf-8")


def _digest(path):
    return hashlib.sha1(Path(path).read_bytes()).hexdigest()


# ──────────────────────────────── 1. technical ────────────────────────────────
def technical(path):
    """Free checks on the pixels. Returns (problems that fail, notes that only warn)."""
    fail, warn = [], []
    try:
        im = Image.open(path).convert("RGB")
    except Exception as e:  # noqa: BLE001
        return [f"unreadable image ({e})"], []
    w, h = im.size
    if min(w, h) < 640:
        fail.append(f"too small ({w}×{h})")
    ratio = (w / h) / (2 / 3)
    if not 0.8 <= ratio <= 1.25:
        fail.append(f"wrong shape for a card ({w}×{h}) — the frame would crop a lot away")
    elif not 0.92 <= ratio <= 1.08:
        warn.append(f"not quite 2:3 ({w}×{h}) — the frame will crop the edges")
    g = im.convert("L").resize((256, int(256 * h / w)))
    st = ImageStat.Stat(g)
    if st.stddev[0] < 14:
        fail.append("flat or blank — almost no contrast")
    hist = g.histogram()
    n = sum(hist)
    if sum(hist[:10]) / n > 0.55:
        fail.append("mostly crushed black")
    if sum(hist[246:]) / n > 0.25:
        fail.append("blown out — large areas of pure white")
    edges = ImageStat.Stat(g.filter(ImageFilter.FIND_EDGES)).var[0]
    if edges < 40:
        warn.append("soft — very little fine detail")
    # letterbox bars / solid borders: an edge band whose rows (or columns) are all one flat colour
    px = g.load()
    W, H = g.size

    def flat_band(coords):
        vals = [px[x, y] for x, y in coords]
        mean = sum(vals) / len(vals)
        return (sum((v - mean) ** 2 for v in vals) / len(vals)) ** 0.5 < 3.0
    band = max(4, int(0.03 * H))
    for name, rows in (("top", range(0, band)), ("bottom", range(H - band, H))):
        if all(flat_band([(x, y) for x in range(W)]) for y in rows):
            fail.append(f"solid bar along the {name} edge")
    bandx = max(4, int(0.03 * W))
    for name, cols in (("left", range(0, bandx)), ("right", range(W - bandx, W))):
        if all(flat_band([(x, y) for y in range(H)]) for x in cols):
            fail.append(f"solid bar along the {name} edge")
    return fail, warn


# ──────────────────────────────── 2. originality ──────────────────────────────
def originality(path, cid, led, batch_feats):
    """Too close to another card's art (in the game, or elsewhere in this batch)?"""
    try:
        ad = _ad()
    except Exception:  # noqa: BLE001 — no numpy / no art_director: skip this layer, never block on it
        return [], None
    f = ad.image_features(path)
    best, who = 0.0, None
    for other, e in led.get("cards", {}).items():
        if other == cid or not e.get("image") or e.get("status") != "approved":
            continue
        s = ad.image_similarity(f, e["image"])
        if s > best:
            best, who = s, other
    for other, feats in batch_feats:
        if other == cid:
            continue
        s = ad.image_similarity(f, feats)
        if s > best:
            best, who = s, other
    batch_feats.append((cid, f))
    if best >= DUP_IMAGE:
        return [f"looks like {who}'s art (similarity {best:.2f})"], f
    return [], f


# ──────────────────────────────── 3. art director (vision) ────────────────────
LOOK = """You are the strict art director of a fantasy trading-card game. You approve only art that is beautiful
and FINISHED. You are shown one painting meant for the card described below. The painting is shown inside a card
frame: about 6% of every edge is covered by the frame's rim and the bottom 15% sits behind the stat band.
House style: {style}

Judge it and reply as JSON only:
{{"subject": "the main subject in at most 10 words",
  "subject_complete": true/false   (false if the main subject — a figure, creature, object — is cut off by an
                                    edge in a way that loses its head, wings, weapon, or most of its body),
  "cut_off": "what is cut off, or empty",
  "fits_card_window": true/false    (false if the important part sits under the rim or the bottom stat band),
  "defects": [any of: "extra or missing limbs", "mangled hands", "distorted face", "melted or broken geometry",
              "text or lettering", "watermark or signature", "frame or border", "collage or split panels",
              "blurry or unfinished", "photographic look", "duplicated subject"],
  "matches_card": 0-10, "style_match": 0-10, "beauty": 0-10, "finished": 0-10,
  "reason": "one short sentence: the single biggest strength or problem"}}"""


def vision(path, card):
    style = sl.guide_clause(force=True) or "luminous high-fantasy painting with a soft painterly finish"
    brief = f"Card: {card.get('name', '?')} ({str(card.get('type', '')).lower()}, {str(card.get('strata', '')).title()})."
    if card.get("flavor"):
        brief += f" Flavor: {card['flavor']}"
    if card.get("abilities"):
        brief += f" Abilities: {json.dumps(card['abilities'])[:240]}"
    sl._env_key("OPENROUTER_API_KEY")
    last = None
    for attempt in range(3):
        try:
            d = sl._openrouter({"model": SCREEN_MODEL, "temperature": 0.2, "messages": [
                {"role": "system", "content": LOOK.format(style=style)},
                {"role": "user", "content": [{"type": "text", "text": brief},
                                             {"type": "image_url", "image_url": {"url": sl._data_url(path, 1024)}}]}]},
                timeout=180)
            return sl._json_from(sl._chat_text(d))
        except Exception as e:  # noqa: BLE001
            last = e
            time.sleep(3 * (attempt + 1))
    raise RuntimeError(f"vision check failed: {last}")


def _subject_words(text):
    return sl._words(text)


def decide(tech_fail, tech_warn, dup, v, subject_repeat):
    reasons, review = list(tech_fail) + list(dup), []
    if subject_repeat:
        reasons.append(subject_repeat)
    if v:
        if v.get("subject_complete") is False:
            reasons.append("subject cut off" + (f" — {v['cut_off']}" if v.get("cut_off") else ""))
        if v.get("fits_card_window") is False:
            reasons.append("important part hidden by the card frame")
        bad = [x for x in v.get("defects") or [] if x in SEVERE]
        if bad:
            reasons.append("defects: " + ", ".join(bad))
        for k, need in BAR.items():
            got = v.get(k)
            if not isinstance(got, (int, float)):
                continue
            if got < need - 1:
                reasons.append(f"{k.replace('_', ' ')} {got}/10")
            elif got < need:
                review.append(f"{k.replace('_', ' ')} {got}/10")
    verdict = "fail" if reasons else ("review" if review else "pass")
    score = 0.0
    if v:
        score = round(0.4 * v.get("beauty", 0) + 0.3 * v.get("finished", 0) + 0.2 * v.get("style_match", 0)
                      + 0.1 * v.get("matches_card", 0), 2)
    return {"verdict": verdict, "score": score, "reasons": reasons or review, "warnings": tech_warn}


def screen_one(path, cid, card, led, batch_feats, batch_subjects, cache, use_vision=True):
    key = _digest(path)
    cached = cache.get(key)
    tech_fail, tech_warn = technical(path)
    dup, _ = originality(path, cid, led, batch_feats)
    v = None
    if use_vision and not tech_fail:
        v = (cached or {}).get("vision") or vision(path, card)
    # a subject another card already shows (in the game, or earlier in this batch) is a repeat
    repeat = ""
    if v and v.get("subject"):
        mine = _subject_words(v["subject"])
        pool = list(batch_subjects) + [(c, e.get("screen_subject", "")) for c, e in led.get("cards", {}).items()
                                       if c != cid and e.get("status") == "approved" and e.get("screen_subject")]
        for other, subj in pool:
            if other == cid or not subj:
                continue
            theirs = _subject_words(subj)
            if mine and theirs and len(mine & theirs) / len(mine | theirs) >= DUP_SUBJECT:
                repeat = f"same subject as {other} ({subj})"
                break
        batch_subjects.append((cid, v["subject"]))
    res = decide(tech_fail, tech_warn, dup, v, repeat)
    res.update({"subject": (v or {}).get("subject", ""), "vision": v, "model": SCREEN_MODEL if v else None,
                "screened": time.strftime("%Y-%m-%d %H:%M")})
    cache[key] = {"vision": v}
    return res


# ──────────────────────────────── screen a run / a folder ─────────────────────
def _run_items(led, run):
    """(card id, candidate dict) for every candidate painted in that run."""
    items = []
    for cid, e in sorted(led.get("cards", {}).items()):
        if e.get("run") == run:
            for c in e.get("candidates") or []:
                if c.get("path") and Path(c["path"]).exists():
                    items.append((cid, c))
    return items


def _label(cid, res, n=None):
    mark = {"pass": "✓ PASS", "review": "? REVIEW", "fail": "✗ FAIL"}[res["verdict"]]
    head = f"{mark} {res['score']:.1f}  {cid}" + (f" #{n}" if n else "")
    why = "; ".join(res["reasons"][:2]) if res["reasons"] else (res.get("subject") or "")
    return f"{head} — {why}"


def cmd_screen(a):
    cards, cache = _cards(), _cache()
    batch_feats, batch_subjects, cells = [], [], []
    # FABLE-BATCH-1: paintings already kept earlier in this batch count as "the batch" too, so a repaint
    # can't come back with the subject another card just got
    for cid, c in getattr(a, "keep", None) or []:
        if c.get("image"):
            batch_feats.append((cid, c["image"]))
        if (c.get("screen") or {}).get("subject"):
            batch_subjects.append((cid, c["screen"]["subject"]))
    if a.dir:
        led = {"cards": {}}
        try:
            led = _ad().load_ledger()
        except Exception:  # noqa: BLE001
            pass
        files = sorted(p for p in Path(a.dir).expanduser().iterdir() if p.suffix.lower() in sl.IMG_EXT)
        results = {}
        for p in files:
            cid = re.split(r"__|_(?:refs|flux|guide|lora)|_\d+$", p.stem)[0]
            res = screen_one(p, cid, cards.get(cid, {"name": cid}), led, batch_feats, batch_subjects, cache, not a.no_vision)
            results[str(p)] = res
            cells.append((p, _label(cid, res)))
            print(f"  {_label(cid, res)}", flush=True)
        _save_cache(cache)
        tag = Path(a.dir).name
        (OUT / f"screen_{tag}.json").parent.mkdir(parents=True, exist_ok=True)
        (OUT / f"screen_{tag}.json").write_text(json.dumps(results, indent=1), encoding="utf-8")
    else:
        ad = _ad()
        led = ad.load_ledger()
        run = a.run or led.get("last_run")
        items = _run_items(led, run) if run else []
        if not items:
            print(f"nothing to screen (run {run or '—'}): render with art_director.py first")
            return 1
        print(f"screening run {run}: {len(items)} painting(s) with {SCREEN_MODEL if not a.no_vision else 'local checks only'}")
        for cid, c in items:
            res = screen_one(c["path"], cid, cards.get(cid, {"name": cid}), led, batch_feats, batch_subjects,
                             cache, not a.no_vision)
            c["screen"] = {k: res[k] for k in ("verdict", "score", "reasons", "warnings", "subject", "screened")}
            cells.append((c["path"], _label(cid, res, c.get("n"))))
            print(f"  {_label(cid, res, c.get('n'))}", flush=True)
            _save_cache(cache)
        ad.save_ledger(led)
        tag = run
    counts = {k: sum(1 for _, lab in cells if lab.startswith(m)) for k, m in (("pass", "✓"), ("review", "?"), ("fail", "✗"))}
    sheet = sl.grid(cells, 4, OUT / f"screen_{tag}.jpg", 300, 440, 92,
                    f"Screen {tag} — {counts['pass']} pass · {counts['review']} review · {counts['fail']} fail")
    print(f"{counts['pass']} pass · {counts['review']} review · {counts['fail']} fail — sheet: {sheet}")
    return 0


# ──────────────────────────────── pick / install ──────────────────────────────
def _best(e, allow_review=False):
    ok = ("pass", "review") if allow_review else ("pass",)
    good = [c for c in e.get("candidates") or [] if (c.get("screen") or {}).get("verdict") in ok]
    return max(good, key=lambda c: (c["screen"]["verdict"] == "pass", c["screen"]["score"]), default=None)


def _install(ad, winners, batch_id=None):
    """Approve each (card id, candidate) through art_director, remember its subject, re-bake the card faces."""
    installed = []
    for cid, b in winners:
        env = dict(os.environ, ART_FORCE="1") if b["screen"]["verdict"] == "review" else None
        r = subprocess.run([sys.executable, str(TOOLS / "art_director.py"), "approve", cid, str(b["n"])],
                           capture_output=True, text=True, env=env)
        print("   " + (r.stdout or r.stderr).strip().splitlines()[-1] if (r.stdout or r.stderr).strip() else f"   {cid}: approved")
        if r.returncode == 0:
            installed.append(cid)
            led = ad.load_ledger()
            led["cards"][cid]["screen_subject"] = b["screen"].get("subject", "")
            led["cards"][cid]["style_v"] = 2
            if batch_id:
                led["cards"][cid]["batch"] = batch_id
            ad.save_ledger(led)
    bake = REPO / "pipeline" / "bake_cards.py"
    if installed and bake.exists():
        r = subprocess.run([sys.executable, str(bake)] + installed, capture_output=True, text=True, cwd=str(REPO))
        print(f"   re-baked {len(installed)} card face(s)" if r.returncode == 0 else f"   bake failed: {(r.stderr or r.stdout)[-300:]}")
    return installed


def cmd_pick(a):
    ad = _ad()
    led = ad.load_ledger()
    run = a.run or led.get("last_run")
    ids = sorted(cid for cid, e in led.get("cards", {}).items() if e.get("run") == run)
    winners, losers = [], []
    already = []
    for cid in ids:
        e = led["cards"][cid]
        if e.get("status") == "approved":
            already.append(cid)
            continue
        if not any(c.get("screen") for c in e.get("candidates") or []):
            losers.append((cid, "not screened yet — art_screen.py screen"))
            continue
        b = _best(e, a.allow_review)
        if b:
            winners.append((cid, b))
        else:
            why = "; ".join(r for c in e.get("candidates") or [] for r in (c.get("screen") or {}).get("reasons", [])[:1])
            losers.append((cid, why or "no candidate passed"))
    for cid, b in winners:
        print(f"  ✓ {cid:<28} candidate #{b['n']}  score {b['screen']['score']:.1f}  ({b['screen'].get('subject', '')})")
    for cid, why in losers:
        print(f"  ✗ {cid:<28} {why}")
    if already:
        print(f"  ({len(already)} card(s) from this run are already in the game)")
    if a.install and winners:
        installed = _install(ad, winners)
        print(f"installed {len(installed)} card painting(s); {len(losers)} card(s) still need a painting that passes")
    elif winners:
        print(f"{len(winners)} ready — run again with --install to put them in the game")
    return 0


# ──────────────────────────────── the full loop ───────────────────────────────
MOCK = False


def _ad_cli(*args, env=None):
    cmd = [sys.executable, str(TOOLS / "art_director.py")] + (["--mock"] if MOCK else []) + [str(x) for x in args]
    print("  $ art_director.py " + " ".join(str(x) for x in args), flush=True)
    r = subprocess.run(cmd, env=env)
    return r.returncode


def cmd_cycle(a):
    global MOCK
    MOCK = a.mock
    ad = _ad()
    if a.ids:
        todo = list(a.ids)
    else:
        sel = argparse.Namespace(ids=[], stratum=a.stratum, missing=a.missing, worst=0, planned=False)
        todo = [c["id"] for c in ad.select_cards(sel, ad.load_cards(), ad.load_ledger())]
    if not todo:
        print("no cards selected")
        return 1
    spent, done = 0, []
    replan = set(todo) if a.fresh_concepts else set()
    for rnd in range(1, a.rounds + 1):
        if not todo:
            break
        if spent + len(todo) * a.candidates > a.max_paintings:
            print(f"stopping: the next round would pass the cap of {a.max_paintings} paintings ({spent} so far)")
            break
        print(f"\n── round {rnd}: {len(todo)} card(s) × {a.candidates} candidate(s) ──", flush=True)
        led = ad.load_ledger()
        need_plan = [c for c in todo if c in replan or not (led["cards"].get(c) or {}).get("prompt")
                     or (led["cards"].get(c) or {}).get("mock")]
        if need_plan and _ad_cli("plan", *need_plan) not in (0, 2):
            print("planning failed — stopping")
            return 1
        env = dict(os.environ, STYLE_MOOD_SALT=f"{rnd}")    # a new round draws new moods
        if _ad_cli("render", *todo, "--models", a.models, "--candidates", a.candidates, env=env) != 0:
            print("render failed — stopping")
            return 1
        spent += len(todo) * a.candidates
        led = ad.load_ledger()
        keep = _winners(led, done, a.allow_review)
        cmd_screen(argparse.Namespace(run=None, dir=None, no_vision=False, keep=keep))
        led = ad.load_ledger()
        nxt, replan = [], set()
        for cid in todo:
            e = led["cards"].get(cid) or {}
            if _best(e, a.allow_review):
                done.append(cid)
                continue
            nxt.append(cid)
            reasons = " ".join(r for c in e.get("candidates") or [] for r in (c.get("screen") or {}).get("reasons", []))
            # a repeat or an off-card painting needs a new idea, not just a new coat of paint
            if re.search(r"same subject|looks like|matches card", reasons):
                replan.add(cid)
        print(f"round {rnd}: {len(todo) - len(nxt)} passed, {len(nxt)} to repaint "
              f"({len(replan)} with a fresh concept)", flush=True)
        todo = nxt
        if a.install:
            cmd_pick(argparse.Namespace(run=None, install=True, allow_review=a.allow_review))
    print(f"\ncycle done: {len(done)} card(s) passed, {len(todo)} still failing{': ' + ', '.join(todo) if todo else ''} "
          f"— {spent} painting(s) made")
    return 0


# ──────────────────────────────── spend ───────────────────────────────────────
BATCHES = sl.HOME.parent / "batches"
PREVIEW = REPO / "artifacts" / "art_review" / "preview"


def account_usage():
    """What OpenRouter says this key has spent (USD): {'day':…, 'week':…, 'month':…, 'total':…}, or None."""
    try:
        sl._env_key("OPENROUTER_API_KEY")
        req = urllib.request.Request(f"{sl.OPENROUTER}/key", headers={"Authorization": f"Bearer {os.environ['OPENROUTER_API_KEY']}"})
        with urllib.request.urlopen(req, timeout=30) as r:
            d = (json.loads(r.read()) or {}).get("data") or {}
        got = {"total": d.get("usage"), "day": d.get("usage_daily"), "week": d.get("usage_weekly"), "month": d.get("usage_monthly")}
        return got if any(isinstance(v, (int, float)) for v in got.values()) else None
    except Exception:  # noqa: BLE001
        return None


def _usd(x):
    return "n/a" if x is None else (f"${x:,.2f}" if abs(x) >= 1 else f"${x:.3f}")


def _spend_rows():
    rows = []
    try:
        for line in sl.SPEND_LOG.read_text(encoding="utf-8").splitlines():
            try:
                rows.append(json.loads(line))
            except ValueError:
                pass
    except OSError:
        pass
    return rows


def cmd_spend(a):
    print("WHERE THE MONEY GOES — everything below is read from your own records, nothing is guessed\n")
    u = account_usage()
    if u:
        print(f"OpenRouter account (as OpenRouter reports it):  today {_usd(u['day'])} · this week {_usd(u['week'])} · "
              f"this month {_usd(u['month'])} · all time {_usd(u['total'])}")
    else:
        print("OpenRouter account totals: couldn't read them from here — see openrouter.ai/activity (it lists every call, by model)")
    try:
        led = _ad().load_ledger()
    except Exception:  # noqa: BLE001
        led = {"cards": {}}
    runs = {}
    for cid, e in led.get("cards", {}).items():
        for c in e.get("candidates") or []:
            runs.setdefault(e.get("run", "?"), [0, set()])
            runs[e.get("run", "?")][0] += 1
            runs[e.get("run", "?")][1].add(cid)
    if runs:
        print(f"\nPaintings the art ledger still holds: {sum(v[0] for v in runs.values())} across {len(runs)} run(s)  (each is a paid image)")
        for r, (n, ids) in sorted(runs.items())[-8:]:
            print(f"   run {r}: {n} painting(s) for {len(ids)} card(s)")
    rows = _spend_rows()
    if rows:
        print(f"\nCalls logged since spend logging began ({rows[0]['t']} →):")
        by = {}
        for r in rows:
            k = (r.get("kind", "?"), str(r.get("model", "?")))
            b = by.setdefault(k, [0, 0.0, 0])
            b[0] += 1
            if isinstance(r.get("cost"), (int, float)):
                b[1] += r["cost"]
                b[2] += 1
        for (kind, model), (n, cost, known) in sorted(by.items(), key=lambda kv: -kv[1][1]):
            print(f"   {kind:<6} {model:<34} {n:>4} call(s)  {_usd(cost) if known else 'cost not reported'}")
        tags = {}
        for r in rows:
            if r.get("tag") and isinstance(r.get("cost"), (int, float)):
                tags[r["tag"]] = tags.get(r["tag"], 0) + r["cost"]
        for t, c in sorted(tags.items())[-6:]:
            print(f"   batch {t}: {_usd(c)}")
    else:
        print("\nNo per-call log yet — it starts with this version. From now on every call is recorded with its cost.")
    print("\nWhat costs money (the table above has the real amounts once calls are logged):\n"
          "   1. paintings (Gemini image model with 6 reference pictures attached) — one per candidate per card per round;\n"
          "      the most expensive call per use\n"
          "   2. comparison sheets (`style_lora.py test`) — a painting for every mode of every prompt (now switched off)\n"
          "   3. the art director's planning (an LLM call or more per card, only when a card needs a new concept)\n"
          "   4. screener looks (a small vision call per painting, cached by picture so it is never paid twice)\n"
          "   5. Tcgbot's own thinking (deepseek via OpenRouter) — it lands in the same account total\n"
          "Kaggle training is free.")
    return 0


# ──────────────────────────────── the preview batch ───────────────────────────
def _pick_batch(cards, n, led, ids=None):
    """n cards that are not yet in the new style, spread over the strata and card types (stable order)."""
    if ids:
        return list(ids)
    pool = [c for c in cards.values() if (led.get("cards", {}).get(c["id"]) or {}).get("style_v") != 2]
    by = {}
    for c in sorted(pool, key=lambda c: hashlib.sha1(c["id"].encode()).hexdigest()):
        by.setdefault(str(c.get("strata", "")), []).append(c)
    out, seen_types = [], {}
    while len(out) < n and any(by.values()):
        for st in sorted(by):
            if by[st] and len(out) < n:
                # prefer a card type this stratum hasn't had yet in the batch
                pick = next((c for c in by[st] if str(c.get("type", "")) not in seen_types.get(st, set())), by[st][0])
                by[st].remove(pick)
                seen_types.setdefault(st, set()).add(str(pick.get("type", "")))
                out.append(pick["id"])
    return out


def _winners(led, ids, allow_review):
    return [(cid, b) for cid in ids for b in [_best(led["cards"].get(cid) or {}, allow_review)] if b]


def _shown(led, ids):
    """What the preview sheet shows (and `--approve` installs): each card's best PASS, else its best REVIEW
    (one score just under the bar — Trikzos is the human who decides those). Never a FAIL."""
    return _winners(led, ids, True)


def _preview_sheet(led, cards, ids, bid):
    cells = []
    for cid, b in _shown(led, ids):
        nm = cards.get(cid, {}).get("name", cid)
        mark = "" if b["screen"]["verdict"] == "pass" else "  (?)"
        cells.append((b["path"], f"{nm}{mark}\n{b['screen'].get('subject', '')}"[:110]))
    if not cells:
        return None
    return sl.grid(cells, 5, PREVIEW / f"preview_{bid}.jpg", 300, 440, 70, f"New art — {len(cells)} card(s)")


def cmd_batch(a):
    global MOCK
    MOCK = a.mock
    ad = _ad()
    cards = _cards()
    led = ad.load_ledger()
    BATCHES.mkdir(parents=True, exist_ok=True)

    if a.approve:
        path = BATCHES / f"{a.approve}.json" if a.approve != "last" else None
        if path is None:
            files = sorted(BATCHES.glob("*.json"))
            if not files:
                print("no batch to approve — run `art_screen.py batch` first")
                return 1
            path = files[-1]
        rec = json.loads(path.read_text(encoding="utf-8"))
        ids = rec["ids"]
        winners = _shown(led, ids)
        if not winners:
            print("nothing in that batch passed — nothing to install")
            return 1
        print(f"installing {len(winners)} painting(s) from batch {rec['id']}:")
        installed = _install(ad, winners, rec["id"])
        rec["installed"] = installed
        path.write_text(json.dumps(rec, indent=1), encoding="utf-8")
        print(f"installed {len(installed)} of {len(ids)} — the rest were not good enough yet")
        return 0

    ids = _pick_batch(cards, a.n, led, a.ids)
    bad = [i for i in ids if i not in cards]
    if bad:
        print(f"unknown card(s): {', '.join(bad)}")
        return 1
    if not ids:
        print("every card is already in the new style")
        return 0
    bid = time.strftime("%Y%m%d-%H%M%S")
    need_plan = [c for c in ids if a.fresh_concepts or not (led["cards"].get(c) or {}).get("prompt")
                 or (led["cards"].get(c) or {}).get("mock")]
    cap = min(a.max_paintings, len(ids) * a.rounds)
    print(f"PREVIEW BATCH {bid}: {len(ids)} cards, one painting each, then only the failures are repainted.\n"
          f"  cards: {', '.join(ids)}\n"
          f"  planned spend: {len(need_plan)} planning call(s) + about {len(ids)} paintings"
          f" (hard cap {cap}) + one cheap screener look per painting")
    before = account_usage()
    os.environ["SPEND_TAG"] = bid
    spent, todo = 0, list(ids)
    replan = set(need_plan)
    for rnd in range(1, a.rounds + 1):
        if not todo:
            break
        if spent >= cap:
            print(f"stopping: reached the cap of {cap} paintings")
            break
        if spent + len(todo) > cap:
            print(f"only {cap - spent} painting(s) left under the cap — repainting {cap - spent} of {len(todo)}")
            todo = todo[:cap - spent]
        if before and a.max_usd:
            now = account_usage()
            if now and now.get("total") is not None and before.get("total") is not None \
                    and now["total"] - before["total"] >= a.max_usd:
                print(f"stopping: this batch has spent ${now['total'] - before['total']:.2f}, the cap is ${a.max_usd:.2f}")
                break
        print(f"\n── round {rnd}: {len(todo)} painting(s) ──", flush=True)
        led = ad.load_ledger()
        plan_now = [c for c in todo if c in replan or not (led["cards"].get(c) or {}).get("prompt")
                    or (led["cards"].get(c) or {}).get("mock")]
        if plan_now and _ad_cli("plan", *plan_now) not in (0, 2):
            print("planning failed — stopping")
            return 1
        env = dict(os.environ, STYLE_MOOD_SALT=f"{rnd}")
        if _ad_cli("render", *todo, "--models", "refs", "--candidates", 1, env=env) != 0:
            print("render failed — stopping")
            return 1
        spent += len(todo)
        led = ad.load_ledger()
        keep = [(cid, b) for cid, b in _shown(led, [i for i in ids if i not in todo])]
        cmd_screen(argparse.Namespace(run=None, dir=None, no_vision=False, keep=keep))
        led = ad.load_ledger()
        nxt, replan = [], set()
        for cid in todo:
            e = led["cards"].get(cid) or {}
            if _best(e, True):        # a pass, or a near-miss kept for Trikzos to judge (a repaint would overwrite it)
                continue
            nxt.append(cid)
            reasons = " ".join(r for c in e.get("candidates") or [] for r in (c.get("screen") or {}).get("reasons", []))
            if re.search(r"same subject|looks like|matches card", reasons):
                replan.add(cid)
        print(f"round {rnd}: {len(todo) - len(nxt)} good, {len(nxt)} to repaint", flush=True)
        todo = nxt
    led = ad.load_ledger()
    good = [cid for cid, _ in _shown(led, ids)]
    unsure = [cid for cid, b in _shown(led, ids) if b["screen"]["verdict"] != "pass"]
    sheet = _preview_sheet(led, cards, ids, bid)
    after = account_usage()
    cost = None
    if before and after and before.get("total") is not None and after.get("total") is not None:
        cost = after["total"] - before["total"]
    logged = sum(r["cost"] for r in _spend_rows() if r.get("tag") == bid and isinstance(r.get("cost"), (int, float)))
    rec = {"id": bid, "ids": ids, "good": good, "unsure": unsure, "paintings": spent,
           "cost_account_delta": cost, "cost_logged": logged, "sheet": str(sheet) if sheet else None}
    (BATCHES / f"{bid}.json").write_text(json.dumps(rec, indent=1), encoding="utf-8")
    print(f"\nbatch {bid}: {len(good)} of {len(ids)} cards have art to show — {spent} painting(s) made")
    if unsure:
        print(f"marked (?) on the sheet — just under the bar, your call: {', '.join(unsure)}")
    if cost is not None:
        print(f"spent on this batch (OpenRouter's own account total, before vs after): {_usd(cost)}")
    elif logged:
        print(f"spent on the calls this batch made (logged): {_usd(logged)}")
    else:
        print("spend: OpenRouter didn't report a cost — check openrouter.ai/activity")
    missing = [c for c in ids if c not in good]
    if missing:
        print(f"no passing art yet for: {', '.join(missing)}")
    if sheet:
        print(f"ONE SHEET, one painting per card: {sheet}")
    print(f"if you like them: python3 tools/art_screen.py batch --approve {bid}")
    return 0


GUARD_MARK = "FABLE-SCREEN-1: only screened, passing paintings go into the game"


def cmd_hook(a):
    """Make art_director's own `approve` refuse a painting the screen hasn't passed (ART_FORCE=1 overrides)."""
    ad = TOOLS / "art_director.py"
    text = ad.read_text(encoding="utf-8")
    if GUARD_MARK in text:
        print("art_director.py approve is already guarded by the screen — nothing to do")
        return 0
    anchor = '        sys.exit(f"{a.card_id}: no candidate #{a.candidate}")\n'
    if anchor not in text:
        print("art_director.py's approve has changed shape — NOT guarding it (the screen still works); tell Fable")
        return 1
    guard = (anchor +
             f"    # {GUARD_MARK} (ART_FORCE=1 overrides)\n"
             "    if (c.get(\"screen\") or {}).get(\"verdict\") != \"pass\" and os.environ.get(\"ART_FORCE\") != \"1\":\n"
             "        s = c.get(\"screen\") or {}\n"
             "        sys.exit(f\"{a.card_id} #{a.candidate}: the screen said {s.get('verdict', 'nothing yet')} \"\n"
             "                 f\"({'; '.join(s.get('reasons', [])) or 'run art_screen.py screen'}) — not installed. ART_FORCE=1 to override\")\n")
    new = text.replace(anchor, guard, 1)
    try:
        compile(new, str(ad), "exec")
    except SyntaxError as e:
        print(f"the guarded file would not compile ({e}) — nothing changed")
        return 1
    if a.dry_run:
        print("would add to art_director.py approve:\n" + guard)
        return 0
    ad.write_text(new, encoding="utf-8")
    print("art_director.py approve now refuses paintings the screen hasn't passed")
    return 0


def cmd_show(a):
    led = _ad().load_ledger()
    e = led.get("cards", {}).get(a.card_id)
    if not e:
        print(f"{a.card_id}: not in the ledger")
        return 1
    print(f"{a.card_id}: {e.get('status')} (run {e.get('run')})")
    for c in e.get("candidates") or []:
        s = c.get("screen") or {}
        print(f"  #{c['n']} {c.get('generator')}: {s.get('verdict', 'not screened')} {s.get('score', '')} "
              f"{'; '.join(s.get('reasons', []))}  [{s.get('subject', '')}]")
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("screen")
    s.add_argument("--run")
    s.add_argument("--dir", help="screen any folder of images (e.g. a style test)")
    s.add_argument("--no-vision", action="store_true", help="local checks only (free)")
    p = sub.add_parser("pick")
    p.add_argument("--run")
    p.add_argument("--install", action="store_true")
    p.add_argument("--allow-review", action="store_true")
    c = sub.add_parser("cycle")
    c.add_argument("ids", nargs="*")
    c.add_argument("--stratum")
    c.add_argument("--missing", action="store_true")
    c.add_argument("--rounds", type=int, default=3)
    c.add_argument("--candidates", type=int, default=1)
    c.add_argument("--models", default="refs")
    c.add_argument("--max-paintings", type=int, default=30)
    c.add_argument("--fresh-concepts", action="store_true", help="plan a new concept for every card first")
    c.add_argument("--allow-review", action="store_true")
    c.add_argument("--install", action="store_true")
    c.add_argument("--mock", action="store_true", help="art_director --mock (no paid paintings) — for testing")
    b = sub.add_parser("batch")
    b.add_argument("ids", nargs="*", help="card ids (default: a spread of cards not yet in the new style)")
    b.add_argument("--n", type=int, default=10)
    b.add_argument("--rounds", type=int, default=2, help="1 painting each, then repaint only the failures (default 2 rounds)")
    b.add_argument("--max-paintings", type=int, default=14, help="hard cap on paintings (default 14)")
    b.add_argument("--max-usd", type=float, default=0, help="stop between rounds once the account has spent this much on the batch")
    b.add_argument("--fresh-concepts", action="store_true", help="plan a new concept for every card first")
    b.add_argument("--approve", nargs="?", const="last", metavar="BATCH", help="install a batch's passing art in the game")
    b.add_argument("--mock", action="store_true", help="art_director --mock (no paid paintings) — for testing")
    sub.add_parser("spend")
    hk = sub.add_parser("hook")
    hk.add_argument("--dry-run", action="store_true")
    sh = sub.add_parser("show")
    sh.add_argument("card_id")
    a = ap.parse_args()
    return {"screen": cmd_screen, "pick": cmd_pick, "cycle": cmd_cycle, "batch": cmd_batch, "spend": cmd_spend, "show": cmd_show, "hook": cmd_hook}[a.cmd](a)


if __name__ == "__main__":
    sys.exit(main())

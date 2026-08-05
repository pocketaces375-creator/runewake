#!/usr/bin/env python3
"""P6-08: APPROVE / REVIEW UI — FastAPI local review interface.

Reads cards from an ART-stage output (06_art.json) and sim results
(04_simulated.json), then serves a review UI for approving, rejecting,
or editing cards.

Usage:
    python -m pipeline.review_app.app --work-dir work/b_2026_ember_01
    # Opens at http://localhost:8080
"""

import argparse
import json
import sys
import time
from pathlib import Path
from typing import Any

import uvicorn
from fastapi import FastAPI, Form, Request
from fastapi.responses import HTMLResponse, JSONResponse, Response
from jinja2 import Environment, FileSystemLoader

# Add pipeline to path
HERE = Path(__file__).resolve().parent.parent  # pipeline/
sys.path.insert(0, str(HERE))

from modules.render_rules import render_rules_text

# ── Paths ─────────────────────────────────────────────────────────────────────

TEMPLATES_DIR = HERE / "review_app" / "templates"
ART_SUB_DIR = "art"

# ── App state (set by CLI) ────────────────────────────────────────────────────

work_dir: Path | None = None
cards: list[dict] = []
sim_map: dict[str, dict] = {}
commission_queue_path: Path | None = None

# ── FastAPI app ──────────────────────────────────────────────────────────────

app = FastAPI(title="Runewake Review UI")

template_env = Environment(loader=FileSystemLoader(str(TEMPLATES_DIR)))


@app.on_event("startup")
def startup():
    """Ensure the review app is fully loaded."""
    global cards, sim_map
    # cards and sim_map are loaded by main() before startup
    print(f"[review] Loaded {len(cards)} cards, {len(sim_map)} sim results")
    if commission_queue_path and commission_queue_path.exists():
        qtext = commission_queue_path.read_text()
        pending = qtext.count("- [ ]")
        print(f"[review] Commission queue: {pending} pending items")


def _load_data(input_dir: Path) -> tuple[list[dict], dict[str, dict]]:
    """Load cards from 06_art.json and sim results from 04_simulated.json."""
    cards_path = input_dir / "06_art.json"
    if cards_path.exists():
        raw = json.loads(cards_path.read_text())
        all_cards = raw if isinstance(raw, list) else [raw]
    else:
        all_cards = []
        print(f"[review] WARNING: no 06_art.json found in {input_dir}")

    sim_path = input_dir / "04_simulated.json"
    sim_results: dict[str, dict] = {}
    if sim_path.exists():
        raw_sim = json.loads(sim_path.read_text())
        sim_list = raw_sim if isinstance(raw_sim, list) else [raw_sim]
        for s in sim_list:
            cid = s.get("card_id", "")
            if cid:
                sim_results[cid] = s

    return all_cards, sim_results


def _card_id(card: dict) -> str:
    """Get a stable card identifier."""
    return card.get("id", card.get("name", "unknown"))


# ── Routes ────────────────────────────────────────────────────────────────────


@app.get("/", response_class=HTMLResponse)
def review_list(request: Request):
    """Main review UI — list all cards."""
    global cards, sim_map

    # Group cards by strata for display
    strata_order = ["VERDANT", "EMBER", "TIDE", "HOLLOW", "DAWN"]

    card_list = []
    for card in cards:
        cid = _card_id(card)
        sim = sim_map.get(cid, {})
        art = card.get("art", {})
        is_fallback = art.get("fallback", False)
        flags = sim.get("flags", [])
        is_flagged = len(flags) > 0
        rarity = card.get("rarity", "COMMON")

        card_list.append({
            "id": cid,
            "name": card.get("name", "?"),
            "strata": card.get("strata", "?"),
            "type": card.get("type", "?"),
            "rarity": rarity,
            "cost": card.get("cost", "?"),
            "attack": card.get("attack", "-"),
            "vigor": card.get("vigor", "-"),
            "power_score": card.get("power_score", "?"),
            "sim_avg_delta": sim.get("avg_delta"),
            "sim_flags": flags,
            "is_flagged": is_flagged,
            "is_fallback": is_fallback,
            "art_asset": art.get("asset", ""),
        })

    # Commission queue status
    commission_pending = 0
    if commission_queue_path and commission_queue_path.exists():
        qtext = commission_queue_path.read_text()
        commission_pending = qtext.count("- [ ]")

    html = template_env.get_template("review.html").render(
        cards=card_list,
        strata_order=strata_order,
        commission_pending=commission_pending,
        work_dir_name=work_dir.name if work_dir else "?",
    )
    return HTMLResponse(html)


@app.get("/card/{card_id}", response_class=HTMLResponse)
def card_detail(request: Request, card_id: str):
    """Card detail page — full view with rendered rules and sim data."""
    global cards, sim_map

    card = None
    for c in cards:
        if _card_id(c) == card_id:
            card = c
            break

    if card is None:
        return HTMLResponse("<h1>Card not found</h1>", status_code=404)

    sim = sim_map.get(card_id, {})
    art = card.get("art", {})
    is_fallback = art.get("fallback", False)
    rules_text = render_rules_text(card)

    # Build sim matchup table
    matchups = []
    mu_results = sim.get("matchup_results", {})
    for opp_name, mu in mu_results.items():
        matchups.append({
            "opponent": opp_name,
            "win_rate": f"{mu.get('win_rate', 0)*100:.1f}%",
            "baseline": f"{mu.get('baseline_win_rate', 0)*100:.1f}%",
            "delta": f"{mu.get('delta', 0)*100:+.1f}%",
            "avg_turns": mu.get("avg_turns", "?"),
        })

    html = template_env.get_template("card_detail.html").render(
        card=card,
        card_id=_card_id(card),
        rules_text=rules_text,
        sim=sim,
        matchups=matchups,
        art=art,
        is_fallback=is_fallback,
        work_dir_name=work_dir.name if work_dir else "?",
    )
    return HTMLResponse(html)


@app.post("/card/{card_id}/approve")
def approve_card(card_id: str) -> JSONResponse:
    """Approve a card — writes a decision token."""
    global cards
    entry = _find_card(card_id)
    if entry is None:
        return JSONResponse({"error": "not found"}, status_code=404)

    _record_decision(card_id, "approved", {})
    return JSONResponse({"status": "approved", "card_id": card_id})


@app.post("/card/{card_id}/reject")
def reject_card(
    card_id: str,
    reason: str = Form(...),
    detail: str = Form(""),
) -> JSONResponse:
    """Reject a card with a reason code. Reason is mandatory."""
    if not reason or reason.strip() == "":
        return JSONResponse({"error": "reason is required"}, status_code=400)

    entry = _find_card(card_id)
    if entry is None:
        return JSONResponse({"error": "not found"}, status_code=404)

    metadata = {}
    if detail:
        metadata["detail"] = detail

    _record_decision(card_id, "rejected", {"reason": reason, **metadata})
    return JSONResponse({"status": "rejected", "card_id": card_id, "reason": reason})


@app.post("/card/{card_id}/edit")
def edit_card(
    card_id: str,
    card_json: str = Form(...),
    reason: str = Form(""),
) -> JSONResponse:
    """Edit a card — saves the updated JSON and optionally records an edit reason."""
    global cards

    try:
        updated = json.loads(card_json)
    except json.JSONDecodeError as e:
        return JSONResponse({"error": f"Invalid JSON: {e}"}, status_code=400)

    # Find and update the card in our list (merge to preserve identity fields)
    for i, c in enumerate(cards):
        if _card_id(c) == card_id:
            merged = {**c, **updated}
            merged["id"] = c.get("id", card_id)  # never lose the id
            cards[i] = merged
            break
    else:
        return JSONResponse({"error": "not found"}, status_code=404)

    # Persist the update
    _save_cards()

    # Record decision (with edit qualifier)
    metadata = {"updated_fields": list(updated.keys())}
    if reason:
        metadata["reason"] = reason
    _record_decision(card_id, "edited", metadata)

    return JSONResponse({"status": "edited", "card_id": card_id})


@app.get("/health")
def health():
    return {"status": "ok", "cards": len(cards), "sim_results": len(sim_map)}


@app.get("/edit-fetch/{card_id}")
def edit_fetch(card_id: str) -> JSONResponse:
    """Return the card JSON for the edit modal."""
    card = _find_card(card_id)
    if card is None:
        return JSONResponse({"error": "not found"}, status_code=404)
    return JSONResponse({"card": card})


@app.get("/art/{filename:path}")
def serve_art(filename: str) -> Response:
    """Serve art files from the work directory."""
    if work_dir is None:
        return Response("No work directory", status_code=404)
    art_path = work_dir / "art" / filename
    if not art_path.exists():
        return Response("Art not found", status_code=404)
    # Determine content type
    ext = art_path.suffix.lower()
    ct = {"webp": "image/webp", "png": "image/png", "jpg": "image/jpeg", "jpeg": "image/jpeg"}.get(ext, "application/octet-stream")
    return Response(art_path.read_bytes(), media_type=ct)


# ── Internal helpers ──────────────────────────────────────────────────────────


def _find_card(card_id: str) -> dict | None:
    for c in cards:
        if _card_id(c) == card_id:
            return c
    return None


def _save_cards():
    """Persist the current card list back to 06_art.json."""
    if work_dir is None:
        return
    out_path = work_dir / "06_art.json"
    out_path.write_text(json.dumps(cards, indent=2))


DECISIONS_FILE = "07_decisions.json"


def _record_decision(card_id: str, action: str, metadata: dict):
    """Append a decision to the decisions log."""
    if work_dir is None:
        return
    decisions_path = work_dir / DECISIONS_FILE
    decisions: list[dict] = []
    if decisions_path.exists():
        decisions = json.loads(decisions_path.read_text())

    entry: dict[str, Any] = {
        "card_id": card_id,
        "action": action,
        "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    }
    entry.update(metadata)
    decisions.append(entry)
    decisions_path.write_text(json.dumps(decisions, indent=2))


# ── CLI ───────────────────────────────────────────────────────────────────────


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Runewake AI Pipeline — REVIEW / APPROVE stage",
    )
    parser.add_argument("--work-dir", required=True,
                        help="Work directory for a completed batch (containing 06_art.json)")
    parser.add_argument("--port", type=int, default=8080,
                        help="HTTP port (default: 8080)")
    parser.add_argument("--host", default="127.0.0.1",
                        help="Bind address (default: 127.0.0.1)")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    global work_dir, cards, sim_map, commission_queue_path

    args = parse_args(argv)

    work_dir = Path(args.work_dir)
    if not work_dir.exists():
        print(f"[review] Work directory not found: {work_dir}", file=sys.stderr)
        return 1

    # Load data into globals
    cards, sim_map = _load_data(work_dir)

    if len(cards) == 0:
        print("[review] No cards loaded — nothing to review.", file=sys.stderr)
        return 1

    # Try to find commission queue
    project_root = HERE.parent
    cq = project_root / "docs" / "ART_COMMISSION_QUEUE.md"
    if cq.exists():
        commission_queue_path = cq

    print(f"[review] Starting review UI for {len(cards)} cards...")
    print(f"[review]   Sim results: {len(sim_map)}")
    print(f"[review]   Commission queue: {commission_queue_path}")
    print(f"[review]   URL: http://{args.host}:{args.port}")
    print(f"[review]   Press Ctrl+C to stop")

    uvicorn.run(app, host=args.host, port=args.port, log_level="info")

    return 0


if __name__ == "__main__":
    sys.exit(main())
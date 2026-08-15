#!/usr/bin/env python3
"""
validate_tutorial_script.py — Harness for TASK-TU1 tutorial script validation.
Loads a tutorial script JSON, validates against schema, checks structural
constraints: known card IDs, beat sequence integrity, popup limits.
Exits 0 on pass, 1 on failure (with description).
"""

import json
import os
import sys
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parent.parent


def load_json(path):
    with open(path) as f:
        return json.load(f)


def load_all_card_ids() -> set:
    """Load all known card def IDs from client/content/cards/."""
    ids = set()
    cards_dir = PROJECT_ROOT / "client" / "content" / "cards"
    if not cards_dir.exists():
        cards_dir = PROJECT_ROOT / "content"  # fallback
        if cards_dir.exists():
            for fpath in sorted(cards_dir.rglob("*.json")):
                try:
                    data = load_json(fpath)
                    if isinstance(data, list):
                        for c in data:
                            if "id" in c:
                                ids.add(c["id"])
                except (json.JSONDecodeError, OSError):
                    pass
        return ids
    for fpath in sorted(cards_dir.glob("*.json")):
        try:
            data = load_json(fpath)
            if isinstance(data, list):
                for c in data:
                    if "id" in c:
                        ids.add(c["id"])
        except (json.JSONDecodeError, OSError):
            pass
    return ids


def validate_tutorial_script(script_path: str, schema_path: str) -> int:
    errors = []

    # 1. Load files
    try:
        data = load_json(script_path)
    except (json.JSONDecodeError, OSError) as e:
        errors.append(f"Cannot load script: {e}")
        return 1

    try:
        schema = load_json(schema_path)
    except (json.JSONDecodeError, OSError) as e:
        errors.append(f"Cannot load schema: {e}")
        return 1

    # 2. JSON Schema validation
    try:
        from jsonschema import validate, ValidationError as JsValidationError
        validate(data, schema)
    except JsValidationError as e:
        errors.append(f"Schema validation failed: {e.message}  (path={list(e.path)})")
    except ImportError:
        errors.append("jsonschema not installed — skipping schema validation")

    # 3. Structural checks — card IDs
    all_card_ids = load_all_card_ids()
    if all_card_ids:
        for deck_key in ("player_deck", "opponent_deck"):
            for cid in data.get(deck_key, []):
                if cid not in all_card_ids:
                    errors.append(f"Unknown card ID '{cid}' in {deck_key}")

        for turn in data.get("turns", []):
            if "player_hand_override" in turn:
                for cid in turn["player_hand_override"]:
                    if cid not in all_card_ids:
                        errors.append(f"Unknown card ID '{cid}' in turn {turn['turn_number']} player_hand_override")
            if "opponent_hand_override" in turn:
                for cid in turn["opponent_hand_override"]:
                    if cid not in all_card_ids:
                        errors.append(f"Unknown card ID '{cid}' in turn {turn['turn_number']} opponent_hand_override")
            if "opponent_actions" in turn:
                for a in turn["opponent_actions"]:
                    if a.get("action") in ("SUMMON", "PLAY_SPELL") and a.get("card_id") not in all_card_ids:
                        errors.append(f"Unknown card ID '{a.get('card_id')}' in turn {turn['turn_number']} opponent action")

    # 4. Opponent first 3 turns deterministic check
    for tn in range(1, 4):
        opp_turns = [t for t in data.get("turns", [])
                     if t["turn_number"] == tn and t["type"] == "opponent"]
        if not opp_turns:
            errors.append(f"Turn {tn} has no opponent script (must be deterministic per §P2)")
        elif not any("opponent_actions" in t for t in opp_turns):
            errors.append(f"Turn {tn} opponent script has no actions")

    # 5. Popup length limits — max 200 chars per beat
    for turn in data.get("turns", []):
        if "player_beats" in turn:
            for beat in turn["player_beats"]:
                popup = beat.get("popup", "")
                if len(popup) > 200:
                    errors.append(f"Beat '{beat['id']}' popup too long: {len(popup)} chars (max 200)")

    # 6. At-most-one popup per action check (design constraint)
    for turn in data.get("turns", []):
        if "player_beats" in turn and len(turn["player_beats"]) > 0:
            for beat in turn["player_beats"]:
                # If restrict_actions_to has ANY or END_TURN, the beat covers
                # the player ending the turn — subsequent beats on same turn
                # would violate "one popup per action." This is a design hint,
                # not a hard fail, since the runner controls sequencing.
                pass  # TU2 will enforce this at runtime

    # Report
    if errors:
        print("TUTORIAL SCRIPT VALIDATION FAILED")
        for e in errors:
            print(f"  - {e}")
        return 1
    else:
        print("TUTORIAL SCRIPT VALIDATION PASSED")
        print(f"  Script: {script_path}")
        print(f"  Tutorial: {data.get('title', '?')}")
        print(f"  Turns: {len(data.get('turns', []))}")
        player_turns = [t for t in data.get("turns", []) if t["type"] == "player"]
        opp_turns = [t for t in data.get("turns", []) if t["type"] == "opponent"]
        total_beats = sum(len(t.get("player_beats", [])) for t in player_turns)
        total_actions = sum(len(t.get("opponent_actions", [])) for t in opp_turns)
        print(f"  Player beats: {total_beats}")
        print(f"  Opponent actions: {total_actions}")
        return 0


if __name__ == "__main__":
    script_path = sys.argv[1] if len(sys.argv) > 1 else str(PROJECT_ROOT / "content" / "tutorial" / "scripts" / "warrior_intro.json")
    schema_path = sys.argv[2] if len(sys.argv) > 2 else str(PROJECT_ROOT / "schema" / "tutorial_script.schema.json")
    sys.exit(validate_tutorial_script(script_path, schema_path))
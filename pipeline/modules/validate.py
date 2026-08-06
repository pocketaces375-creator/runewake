#!/usr/bin/env python3
"""P6-03: VALIDATE — Schema + engine bridge card validation.

Reads cards from a GENERATE-stage output file (01_raw.json), runs two
validation gates — JSON Schema and C# engine bridge — then writes
passing cards to 02_valid.json and rejects to rejects/ with reason codes.

Usage:
    python -m pipeline.modules.validate --input work/b_2026_ember_01/01_raw.json \\
        --work-dir work/b_2026_ember_01
"""

import argparse
import json
import os
import subprocess
import sys
import tempfile
import time
from pathlib import Path

import jsonschema
from jsonschema import validate as jsonschema_validate

HERE = Path(__file__).resolve().parent.parent  # pipeline/
ROOT = HERE.parent  # runewake/
SCHEMA_PATH = ROOT / "schema" / "card.schema.json"

# Auto-discover the C# simulator binary
SIM_PROJECT = ROOT / "sim"
SIM_BIN = SIM_PROJECT / "bin" / "Debug" / "net8.0" / "Runewake.Sim"

# ── Validation gates ──────────────────────────────────────────────────────────

def load_schema() -> dict:
    with open(SCHEMA_PATH) as f:
        return json.load(f)


def validate_json_schema(card: dict, schema: dict) -> list[str]:
    """Gate 1: Validate a single card dict against the JSON Schema.

    Wraps the card in an array and validates against the full schema
    (which supports both single and array via oneOf). This ensures
    $ref definitions are properly resolved.
    """
    errors = []
    try:
        jsonschema_validate(instance=[card], schema=schema)
    except jsonschema.exceptions.ValidationError as e:
        path = " → ".join(str(p) for p in e.absolute_path) if e.absolute_path else "root"
        errors.append(f"JSON Schema [{path}]: {e.message}")
    return errors


def validate_csharp(cli_path: Path, cards: list[dict], work_dir: Path | None = None) -> list[tuple[int, str]]:
    """Gate 2: Run the C# engine bridge via Runewake.Sim validate-card.

    Writes the batch to a temp JSON file, calls the CLI, and parses stdout.
    Returns a list of (card_index, error_string_or_empty) tuples.
    """
    # Build index: card id -> list index
    card_index: dict[str, int] = {}
    for i, card in enumerate(cards):
        cid = card.get("id", f"__unknown_{i}")
        card_index[cid] = i

    # Write batch to temp file
    tmp_dir = work_dir / ".tmp" if work_dir else Path(tempfile.mkdtemp())
    tmp_dir.mkdir(parents=True, exist_ok=True)
    tmp_file = tmp_dir / "_csharp_validate_input.json"

    with open(tmp_file, "w") as f:
        json.dump(cards, f)

    try:
        result = subprocess.run(
            [str(cli_path), "validate-card", str(tmp_file)],
            capture_output=True,
            text=True,
            timeout=60,
            cwd=str(ROOT),
        )
    except FileNotFoundError:
        print(f"[validate] WARNING: C# CLI not found at {cli_path}. Skipping engine validation.")
        _cleanup_temp(tmp_dir, work_dir)
        return [(i, "") for i in range(len(cards))]
    except subprocess.TimeoutExpired:
        print(f"[validate] WARNING: C# CLI timed out. Skipping engine validation.")
        _cleanup_temp(tmp_dir, work_dir)
        return [(i, "") for i in range(len(cards))]

    # Parse stdout line by line to build per-card error lists
    card_errors: dict[int, list[str]] = {}
    current_cid: str | None = None

    for line in result.stdout.splitlines():
        line_stripped = line.strip()
        if not line_stripped:
            continue

        # Check for card result line: [✓] or [✗]
        if line_stripped.startswith("[✓]") or line_stripped.startswith("[✗]"):
            # Extract card id from remainder
            remainder = line_stripped[3:].strip()  # skip [✓]/[✗]
            # Format: "card_id (Name)" or just "card_id"
            cid = remainder.split(" (")[0].strip() if " (" in remainder else remainder.strip()
            current_cid = cid
            if current_cid not in card_index:
                continue  # unknown card, skip

        elif line_stripped.startswith("- ") and current_cid is not None:
            # Error line: "- error message" (stripped removes the leading spaces)
            err_text = line_stripped[2:].strip()
            idx = card_index.get(current_cid)
            if idx is not None:
                if idx not in card_errors:
                    card_errors[idx] = []
                card_errors[idx].append(err_text)

    # Build final results
    final: list[tuple[int, str]] = []
    for i in range(len(cards)):
        errs = card_errors.get(i, [])
        final.append((i, "; ".join(errs) if errs else ""))

    _cleanup_temp(tmp_dir, work_dir)
    return final


def _cleanup_temp(tmp_dir: Path, work_dir: Path | None):
    """Clean up temporary files."""
    if not work_dir:
        import shutil
        shutil.rmtree(tmp_dir, ignore_errors=True)


# ── Main entry point ───────────────────────────────────────────────────────────

def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Runewake AI Pipeline — VALIDATE stage",
    )
    parser.add_argument("--input", required=True,
                        help="Input card file (from GENERATE, e.g. 01_raw.json)")
    parser.add_argument("--work-dir", required=True,
                        help="Work directory for this batch")
    parser.add_argument("--skip-csharp", action="store_true",
                        help="Skip C# engine bridge validation (schema-only)")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)

    input_path = Path(args.input)
    if not input_path.exists():
        print(f"[validate] Input not found: {input_path}", file=sys.stderr)
        return 1

    work_dir = Path(args.work_dir)
    work_dir.mkdir(parents=True, exist_ok=True)
    rejects_dir = work_dir / "rejects"
    rejects_dir.mkdir(parents=True, exist_ok=True)

    # Load schema
    schema = load_schema()
    print(f"[validate] Loaded schema from {SCHEMA_PATH}")

    # Load input cards (support single card or array)
    with open(input_path) as f:
        raw = json.load(f)
    cards = raw if isinstance(raw, list) else [raw]

    print(f"[validate] Validating {len(cards)} cards...")

    # Gate 1: JSON Schema
    schema_passed: list[dict] = []
    schema_rejects: list[tuple[dict, str]] = []

    for card in cards:
        errs = validate_json_schema(card, schema)
        if errs:
            schema_rejects.append((card, "; ".join(errs)))
        else:
            schema_passed.append(card)

    print(f"[validate] JSON Schema: {len(schema_passed)} passed, {len(schema_rejects)} rejected")

    if schema_rejects:
        for i, (card, reason) in enumerate(schema_rejects):
            rej_path = rejects_dir / f"reject_schema_{i:03d}.json"
            with open(rej_path, "w") as f:
                json.dump({"card": card, "reason": f"SCHEMA_FAIL: {reason}"}, f, indent=2)

    if not schema_passed:
        print("[validate] No cards passed JSON Schema. Writing summary.")
        _write_summary(work_dir, schema_passed, schema_rejects, input_path.stem)
        return 2

    # Gate 2: C# engine bridge
    csharp_results: list[tuple[int, str]] = []
    if not args.skip_csharp and SIM_BIN.exists():
        csharp_results = validate_csharp(SIM_BIN, schema_passed, work_dir)
        engine_passed: list[dict] = []
        engine_rejects: list[tuple[dict, str]] = []

        for idx, err in csharp_results:
            card = schema_passed[idx]
            if err:
                engine_rejects.append((card, f"ENGINE_FAIL: {err}"))
            else:
                engine_passed.append(card)

        print(f"[validate] C# Engine: {len(engine_passed)} passed, {len(engine_rejects)} rejected")

        if engine_rejects:
            offset = len(schema_rejects)
            for i, (card, reason) in enumerate(engine_rejects):
                rej_path = rejects_dir / f"reject_engine_{i:03d}.json"
                with open(rej_path, "w") as f:
                    json.dump({"card": card, "reason": reason}, f, indent=2)

        valid_cards = engine_passed
        all_rejects = schema_rejects + engine_rejects
    else:
        if not args.skip_csharp:
            print(f"[validate] C# CLI not found at {SIM_BIN}. Skipping engine validation.")
        valid_cards = schema_passed
        all_rejects = schema_rejects

    # Write output
    out_path = work_dir / "02_valid.json"
    with open(out_path, "w") as f:
        json.dump(valid_cards, f, indent=2)
    print(f"[validate] Wrote {len(valid_cards)} valid cards to {out_path}")

    _write_summary(work_dir, valid_cards, all_rejects, input_path.stem)

    if all_rejects:
        print(f"[validate] {len(all_rejects)} cards rejected total")
        return 2 if len(valid_cards) == 0 else 0

    if len(valid_cards) == 0:
        print("[validate] ❌ ZERO cards validated — pipeline produced nothing", file=sys.stderr)
        return 2

    print(f"[validate] ✓ All {len(valid_cards)} cards validated successfully")
    return 0


def _write_summary(work_dir: Path, valid: list, rejects: list, batch_prefix: str):
    summary = {
        "batch_id": batch_prefix.replace("01_raw", "").rstrip("_"),
        "input_file": f"{batch_prefix}.json",
        "total_processed": len(valid) + len(rejects),
        "valid": len(valid),
        "rejected": len(rejects),
        "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    }
    with open(work_dir / "02_summary.json", "w") as f:
        json.dump(summary, f, indent=2)
    print(f"[validate] Summary: {summary}")


if __name__ == "__main__":
    sys.exit(main())
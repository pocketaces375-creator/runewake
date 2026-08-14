#!/usr/bin/env python3
"""P6-04: SCORE — Power score formula + rarity band validation.

Reads cards from a VALIDATE-stage output (02_valid.json), computes each
card's power score using the DSL §4 formula, checks the rarity band, and
optionally auto-adjusts cost. Writes passing cards to 03_scored.json and
rejects to rejects/ with reason codes.

Usage:
    python -m pipeline.modules.score --input work/b_2026_ember_01/02_valid.json \\
        --work-dir work/b_2026_ember_01
"""

import argparse
import json
import sys
import time
from pathlib import Path
from typing import Any

HERE = Path(__file__).resolve().parent.parent  # pipeline/


# ── Key weights from DSL §4 ──────────────────────────────────────────────────

KEYWORD_WEIGHTS: dict[str, float] = {
    "GUARD": 0.9,
    "SWIFT": 1.1,
    "PIERCE": 0.6,
    "WARD": 1.0,
    "VENOM": 1.4,
    "REACH": 0.8,
    "ROOTED": -1.3,
    "UNEARTH": 1.2,
    "ECHO": 1.0,
    "FRAGILE": -1.6,
    "SEALED": 0.9,
    "ANCESTRAL_SHIELD": 1.5,
    "STEALTH_STRIKE": 1.2,
}

# Trigger multipliers (default 1.0 for unspecified triggers)
TRIGGER_MULTIPLIERS: dict[str, float] = {
    "ON_SUMMON": 1.0,
    "ON_DEATH": 0.7,
    "ON_ATTACK": 0.8,
    "PASSIVE": 1.3,
    "ON_TURN_START": 1.2,
    "ACTIVATED": 0.9,
}

# Effect weights — v0 heuristic values per OP
# Effect with an amount uses amount * weight; others use a flat weight
EFFECT_FLAT_WEIGHTS: dict[str, float] = {
    "DAMAGE": 1.0,
    "HEAL": 0.8,
    "BUFF": 0.0,  # uses attack/vigor fields directly
    "DEBUFF": 0.0,
    "DESTROY": 3.0,
    "DRAW": 1.5,
    "DISCARD": -1.0,
    "EXCAVATE": 1.0,
    "BURY": 0.5,
    "UNBURY": 1.0,
    "SUMMON": 0.0,  # depends on token_id stats
    "GRANT_KEY": 0.8,
    "REMOVE_KEY": 0.5,
    "SILENCE": 1.5,
    "BOUNCE": 1.0,
    "ATTUNE": 1.0,
    "MOVE_LANE": 0.5,
    "IDENTIFY": 0.5,
    "GAIN_VIGOR": 0.75,
    "LOSE_VIGOR": -0.75,
    "COPY": 2.0,
    "SET_STAT": 0.0,  # uses attack/vigor fields
    "REFRESH": 0.8,
}

# Condition difficulty lookup (fixed, not model judgment)
# ops that are "easy" to meet → 0.75 discount multiplier
EASY_CONDITION_OPS: set[str] = {
    "TURN_GTE", "DAMAGED_THIS_TURN", "HAND_COUNT_GTE",
    "ALLY_COUNT_GTE", "ATTUNEMENT_GTE",
}
# ops that are "hard" to meet → 0.55 discount multiplier
HARD_CONDITION_OPS: set[str] = {
    "BARROW_COUNT_GTE", "CONTROLS_KEYWORD", "CONTROLS_STRATA",
    "RITUALS_CAST_GTE", "HAND_COUNT_LTE", "VIGOR_LTE",
}

# Rarity acceptance bands (delta = score - expected(cost))
RARITY_BANDS: dict[str, tuple[float, float]] = {
    "COMMON": (-0.8, 0.4),
    "UNCOMMON": (-0.5, 0.9),
    "RARE": (-0.3, 1.5),
    "RELIC": (0.0, 2.5),
}

# Default effect weight when amount is present — treat amount as the weight
# unless the OP has a specific weight in EFFECT_FLAT_WEIGHTS
DEFAULT_AMOUNT_WEIGHT: float = 1.0


# ── Scoring functions ────────────────────────────────────────────────────────

def compute_base(card: dict) -> float:
    """Compute base stat score: attack * 1.0 + vigor * 0.75."""
    attack = card.get("attack") or 0
    vigor = card.get("vigor") or 0
    return attack * 1.0 + vigor * 0.75


def compute_keywords(card: dict) -> float:
    """Sum keyword weights."""
    total = 0.0
    for kw in card.get("keywords", []):
        total += KEYWORD_WEIGHTS.get(kw, 0.0)
    return total


def effect_weight(effect: dict) -> float:
    """Compute the weight of a single effect."""
    op = effect.get("op", "")
    amount = effect.get("amount")
    attack = effect.get("attack")
    vigor_adj = effect.get("vigor")

    # Handle BUFF/DEBUFF/SET_STAT with attack/vigor fields
    if op in ("BUFF", "DEBUFF", "SET_STAT"):
        total = 0.0
        if attack is not None:
            total += attack * 1.0
        if vigor_adj is not None:
            total += vigor_adj * 0.75
        if total == 0:
            flat = EFFECT_FLAT_WEIGHTS.get(op, 0.5)
            total = flat if op == "DEBUFF" else flat
        return total

    # SUMMON — estimate based on token stats (default 2/2 = 3.5)
    if op == "SUMMON":
        return 3.5

    # Ops with amount field
    if amount is not None:
        weight_per = EFFECT_FLAT_WEIGHTS.get(op, DEFAULT_AMOUNT_WEIGHT)
        return amount * weight_per

    # Flat-weight ops (DESTROY, DRAW, SILENCE etc.)
    return EFFECT_FLAT_WEIGHTS.get(op, 0.5)


def trigger_multiplier(trigger: str) -> float:
    """Get the trigger multiplier."""
    return TRIGGER_MULTIPLIERS.get(trigger, 1.0)


def condition_discount(condition: Any) -> float:
    """Determine condition difficulty discount."""
    if condition is None:
        return 1.0

    if isinstance(condition, dict):
        # Nested all/any — take the average discount of children
        children = condition.get("all") or condition.get("any")
        if children:
            discounts = [condition_discount(c) for c in children]
            return sum(discounts) / len(discounts)

        op = condition.get("op", "")
        if op in EASY_CONDITION_OPS:
            return 0.75
        if op in HARD_CONDITION_OPS:
            return 0.55

    return 0.75  # default for unknown conditions


def compute_abilities(card: dict) -> float:
    """Compute ability score: sum of effect_weight * trigger_mult * condition_discount."""
    total = 0.0
    for ability in card.get("abilities", []):
        trigger = ability.get("trigger", "")
        tm = trigger_multiplier(trigger)
        cd = condition_discount(ability.get("condition"))
        for effect in ability.get("effects", []):
            ew = effect_weight(effect)
            total += ew * tm * cd
    return total


def compute_power_score(card: dict) -> float:
    """Compute the full power score for a card.

    RELIC-type cards have no attack or vigor. Their effective base is
    1.8 × cost (replacing missing stat contribution), so they can compete
    with creatures of the same cost. See docs/02_CARD_DSL.md §4.
    """
    base = compute_base(card)
    # RELIC-type cards: replace zero stat base with 1.8 × cost
    if card.get("type") == "RELIC" and "attack" not in card:
        base = 1.8 * card.get("cost", 0)
    kw = compute_keywords(card)
    abil = compute_abilities(card)
    return base + kw + abil


def expected_score(cost: int) -> float:
    """Expected score for a given cost.

    Piecewise linear formula (see docs/02_CARD_DSL.md §4):
      cost ≤ 5: 2.35 × cost + 0.9       (same as original — validated low/mid curve)
      cost > 5: 12.65 + 1.5 × (cost − 5)  (gentler slope — opens design space at high
                costs where the original 2.35× slope pushed cards to the stat cap)
    """
    if cost <= 5:
        return 2.35 * cost + 0.9
    else:
        return 12.65 + 1.5 * (cost - 5)


def check_rarity_band(delta: float, rarity: str) -> str | None:
    """Check if delta falls within the rarity band. Returns None if OK, or error message."""
    band = RARITY_BANDS.get(rarity)
    if band is None:
        return f"Unknown rarity: {rarity}"
    low, high = band
    if delta < low or delta > high:
        return f"Delta {delta:+.2f} outside rarity band [{low:+.2f}, {high:+.2f}]"
    return None


# ── Auto-adjustment ──────────────────────────────────────────────────────────

def auto_adjust(card: dict) -> tuple[dict, str | None]:
    """Search cost ±2 of the original for a cost that lands the card in its rarity band.

    Capped at ±2 to prevent absurd cost swings (e.g. a cost-8 ritual becoming
    cost-1). Score does not depend on cost (only on stats/abilities), so this is
    a pure search over expected = 2.35*cost + 0.9. We prefer the cost nearest
    the original (smallest re-balance) and, among ties, the lower cost.

    Returns (adjusted_card, None) on success, or (card, error) if no cost in
    [original-2, original+2] ∩ [0, 10] lands in band.
    """
    score = compute_power_score(card)
    original = card["cost"]
    candidates = []
    for new_cost in range(max(0, original - 2), min(11, original + 3)):
        if new_cost == original:
            continue
        exp = expected_score(new_cost)
        delta = score - exp
        error = check_rarity_band(delta, card["rarity"])
        if error is None:
            candidates.append(new_cost)

    if not candidates:
        return card, "Auto-adjust failed (no cost ±2 lands in band)"

    # Prefer smallest |new_cost - original|, then lowest cost on ties.
    best = min(candidates, key=lambda c: (abs(c - original), c))

    adjusted = dict(card)
    adjusted["cost"] = best
    adjusted["power_score"] = round(score, 2)
    return adjusted, None


# ── Main entry point ──────────────────────────────────────────────────────────

def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Runewake AI Pipeline — SCORE stage",
    )
    parser.add_argument("--input", required=True,
                        help="Input card file (from VALIDATE, e.g. 02_valid.json)")
    parser.add_argument("--work-dir", required=True,
                        help="Work directory for this batch")
    parser.add_argument("--no-adjust", action="store_true",
                        help="Skip auto-adjustment (reject out-of-band immediately)")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)

    input_path = Path(args.input)
    if not input_path.exists():
        print(f"[score] Input not found: {input_path}", file=sys.stderr)
        return 1

    work_dir = Path(args.work_dir)
    work_dir.mkdir(parents=True, exist_ok=True)
    rejects_dir = work_dir / "rejects"
    rejects_dir.mkdir(parents=True, exist_ok=True)

    with open(input_path) as f:
        raw = json.load(f)
    cards = raw if isinstance(raw, list) else [raw]

    print(f"[score] Scoring {len(cards)} cards...")

    scored: list[dict] = []
    rejects: list[tuple[dict, str]] = []
    auto_adjusted: list[dict] = []

    for card in cards:
        # Compute base score
        score = compute_power_score(card)
        cost = card["cost"]
        exp = expected_score(cost)
        delta = score - exp
        rarity = card.get("rarity", "COMMON")

        # Check band
        band_error = check_rarity_band(delta, rarity)

        if band_error:
            if not args.no_adjust:
                adjusted, adj_error = auto_adjust(card)
                if adj_error is None:
                    auto_adjusted.append(adjusted)
                    scored.append(adjusted)
                    continue

            # Reject
            reject_reason = f"SCORE_FAIL: {band_error}"
            if not args.no_adjust:
                reject_reason += " (auto-adjust attempted and failed)"
            rejects.append((card, reject_reason))
            continue

        # In band — store the score
        out = dict(card)
        out["power_score"] = round(score, 2)
        scored.append(out)

    # Write outputs
    out_path = work_dir / "03_scored.json"
    with open(out_path, "w") as f:
        json.dump(scored, f, indent=2)

    if rejects:
        for i, (card, reason) in enumerate(rejects):
            rej_path = rejects_dir / f"reject_score_{i:03d}.json"
            with open(rej_path, "w") as f:
                json.dump({"card": card, "reason": reason}, f, indent=2)

    if auto_adjusted:
        adj_path = work_dir / "03_adjusted.json"
        with open(adj_path, "w") as f:
            json.dump(auto_adjusted, f, indent=2)
        print(f"[score] {len(auto_adjusted)} cards auto-adjusted (cost re-balanced ±2)")

    # Summary
    total_rejected = len(rejects)
    summary = {
        "batch_id": input_path.stem,
        "input_file": str(input_path),
        "total_processed": len(cards),
        "scored": len(scored),
        "rejected": total_rejected,
        "auto_adjusted": len(auto_adjusted),
        "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    }
    with open(work_dir / "03_summary.json", "w") as f:
        json.dump(summary, f, indent=2)

    print(f"[score] Wrote {len(scored)} scored cards to {out_path}")
    print(f"[score] Summary: {summary}")

    if total_rejected:
        print(f"[score] {total_rejected} cards rejected")
        return 2 if len(scored) == 0 else 0

    if len(scored) == 0:
        print("[score] ❌ ZERO cards scored — pipeline produced nothing", file=sys.stderr)
        return 2

    print(f"[score] ✓ All {len(scored)} cards scored successfully")
    return 0


if __name__ == "__main__":
    sys.exit(main())
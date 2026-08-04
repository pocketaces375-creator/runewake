#!/usr/bin/env python3
"""P6-02: GENERATE — AI card generation via OpenRouter with retry.

Usage:
    python -m pipeline.modules.generate --seed seeds/ember_01.json \\
        --work-dir work/b_2026_ember_01

Reads a seed JSON, builds a prompt with system rules + few-shot examples,
calls an instruct model via OpenRouter, validates JSON output, retries once
on parse failure, and writes results to the work directory.
"""

import argparse
import json
import os
import sys
import time
import re
from pathlib import Path

import yaml
from openai import OpenAI


# ── Paths ──────────────────────────────────────────────────────────────────

HERE = Path(__file__).resolve().parent.parent  # pipeline/
DEFAULT_CONFIG = HERE / "config.yaml"
EXAMPLE_CARDS = HERE.parent / "schema" / "example_cards.json"
SCHEMA_PATH = HERE.parent / "schema" / "card.schema.json"


# ── System prompt — grammar rules ────────────────────────────────────────────

SYSTEM_RULES = """You are a CCG card designer for "Runewake: The Buried Age". Generate cards as a JSON array using the closed-vocabulary DSL below.

## Enumerations (use these exact strings only)

STRATA: VERDANT | EMBER | TIDE | HOLLOW | DAWN
TYPE: CREATURE | RITUAL | RELIC | CURSE | TOKEN
RARITY: COMMON | UNCOMMON | RARE | RELIC
DURATION: PERMANENT | THIS_TURN | NEXT_TURN | WHILE_PRESENT

TRIGGERS: ON_SUMMON | ON_DEATH | ON_ATTACK | ON_DAMAGED | ON_TURN_START | ON_TURN_END | ON_CAST_RITUAL | ON_EXCAVATE | ON_RELIC_IDENTIFY | ON_ALLY_DEATH | ON_LANE_VACATED | PASSIVE | ACTIVATED | RESOLVE

OPS: DAMAGE | HEAL | BUFF | DEBUFF | DESTROY | DRAW | DISCARD | EXCAVATE | BURY | UNBURY | SUMMON | GRANT_KEY | REMOVE_KEY | SILENCE | BOUNCE | ATTUNE | MOVE_LANE | IDENTIFY | GAIN_VIGOR | LOSE_VIGOR | COPY | SET_STAT | REFRESH

SCOPES: SELF | ALLY_CREATURE | ENEMY_CREATURE | ANY_CREATURE | PLAYER_SELF | PLAYER_ENEMY | LANE | NONE

FILTERS: ANY | ADJACENT | OPPOSING | SAME_LANE | EDGE_LANE | CENTER_LANE | RANDOM | LOWEST_VIGOR | HIGHEST_ATTACK | LOWEST_COST | HIGHEST_COST | DAMAGED | UNDAMAGED | CHOSEN | STRATA:<STRATUM> | KEYWORD:<KEYWORD> | TYPE:<TYPE>

CONDITION_OPS: ALLY_COUNT_GTE | ENEMY_COUNT_GTE | BARROW_COUNT_GTE | HAND_COUNT_GTE | HAND_COUNT_LTE | TURN_GTE | VIGOR_LTE | VIGOR_GTE | ATTUNEMENT_GTE | CONTROLS_KEYWORD | CONTROLS_STRATA | DAMAGED_THIS_TURN | RITUALS_CAST_GTE

KEYWORDS: GUARD | SWIFT | PIERCE | WARD | VENOM | REACH | ROOTED | UNEARTH | ECHO | FRAGILE | SEALED

## Hard rules (never violate these)

1. Max 2 abilities per card. Max 2 effects per ability.
2. Max nesting depth 2 in conditions. No recursion, no ability that creates an ability.
3. SUMMON may only reference a token_id from this same batch. A summoned token may not itself SUMMON.
4. Any PASSIVE ability must have duration WHILE_PRESENT.
5. Costs: 0-10. Attack: 0-12. Vigor: 1-14.
6. Only RELIC type cards may have identify_condition, and they MUST have one.
7. Names: 1-4 English words, original, not resembling existing IP.
8. CREATURE cards MUST have attack and vigor fields. RELIC cards MUST have identify_condition.
9. RITUAL, CURSE, and TOKEN cards must NOT have attack or vigor.
10. If you use a condition, it must have an "op" and a "value" field.
11. Card IDs follow the pattern: {strata_prefix}_{rarity_prefix}_{snake_case_name}
    (e.g., "vrd_c_root_warden", "emb_r_magma_forger", "hlo_x_shadow_veil")
    Rare card prefix: r. Relic card prefix: x.
12. COMMON rarity is the most frequent in a set (roughly 47%)."""


# ── Helpers ─────────────────────────────────────────────────────────────────

def load_config(config_path: Path) -> dict:
    with open(config_path) as f:
        cfg = yaml.safe_load(f)
    return cfg


def load_examples() -> list:
    """Load the 6 few-shot example cards from schema/."""
    with open(EXAMPLE_CARDS) as f:
        examples = json.load(f)
    # examples is either a single object or array — normalize
    if isinstance(examples, dict):
        return [examples]
    return examples


def load_existing_names(stratum: str) -> list[str]:
    """Load existing card names for this stratum for the avoidance list."""
    pack_path = HERE.parent / "content" / "cards" / f"{stratum.lower()}.json"
    if not pack_path.exists():
        return []
    with open(pack_path) as f:
        cards = json.load(f)
    return [c["name"] for c in cards if c.get("name")]


def build_prompt(seed: dict, examples: list, existing_names: list[str]) -> str:
    """Build the full user prompt: seed config + few-shot examples + avoidance list."""
    parts = []

    # Seed config
    parts.append("## Generation Request\n")
    parts.append(f"Batch: {seed.get('batch_id', 'unknown')}")
    parts.append(f"Strata: {seed['strata']}")
    parts.append(f"Count: {seed['count']}")
    parts.append(f"Theme: {seed.get('theme', 'none')}")
    parts.append(f"Mechanic emphasis: {seed.get('mechanic_emphasis', [])}")
    parts.append(f"Forbidden mechanics: {seed.get('forbidden_mechanics', [])}")
    parts.append("")

    # Type distribution
    type_mix = seed.get("type_mix", {})
    parts.append(f"Type distribution: {json.dumps(type_mix)}")

    # Cost curve
    cost_curve = seed.get("cost_curve", {})
    parts.append(f"Cost curve: {json.dumps(cost_curve)}")

    # Rarity distribution
    rarity_mix = seed.get("rarity_mix", {})
    parts.append(f"Rarity distribution: {json.dumps(rarity_mix)}")
    parts.append("")

    # Avoidance list
    if existing_names:
        parts.append(f"## EXISTING NAMES IN THIS STRATUM (AVOID)\nDo NOT produce cards with names similar to these:\n")
        for n in existing_names:
            parts.append(f"- {n}")
        parts.append("")

    # Few-shot examples
    parts.append("## FEW-SHOT EXAMPLES (study the format)\n")
    for i, ex in enumerate(examples, 1):
        parts.append(f"--- Example {i} ---")
        parts.append(json.dumps(ex, indent=2))
        parts.append("")

    # Output instruction
    parts.append("## INSTRUCTIONS\n")
    parts.append(f"Generate exactly {seed['count']} card objects as a JSON array.")
    parts.append("Return ONLY valid JSON. No markdown, no code fences, no explanation.")
    parts.append("Every card MUST have: id, set='buried_age', name, strata, type, rarity, cost, abilities, power_score, content_version=1.")
    parts.append("CREATURE cards additionally need: attack, vigor, keywords (can be empty).")
    parts.append("RELIC cards need: identify_condition, keywords (includes SEALED).")
    parts.append("RITUAL cards must have at least one ability with trigger RESOLVE.")
    parts.append("The strata MUST be " + seed['strata'] + ".")
    parts.append("")

    return "\n".join(parts)


def repair_json(text: str) -> str:
    """Attempt to extract a JSON array from model output that may have markdown fences or preamble."""
    # Remove markdown code fences
    text = re.sub(r'```(?:json)?\s*', '', text).strip()
    # Find the first '[' and last ']'
    start = text.find('[')
    end = text.rfind(']')
    if start != -1 and end != -1 and end > start:
        return text[start:end+1]
    return "[]"


def call_llm(client: OpenAI, model: str, system: str, user: str,
             temperature: float, timeout: int) -> str:
    """Call the OpenRouter API and return raw text response."""
    resp = client.chat.completions.create(
        model=model,
        messages=[
            {"role": "system", "content": system},
            {"role": "user", "content": user},
        ],
        temperature=temperature,
        timeout=timeout,
    )
    return resp.choices[0].message.content


def write_output(work_dir: Path, batch_id: str, cards: list, rejects: list):
    """Write accepted cards and rejects to the work directory."""
    work_dir.mkdir(parents=True, exist_ok=True)
    rejects_dir = work_dir / "rejects"
    rejects_dir.mkdir(parents=True, exist_ok=True)

    # Accepted
    out_path = work_dir / "01_raw.json"
    with open(out_path, "w") as f:
        json.dump(cards, f, indent=2)
    print(f"[generate] Wrote {len(cards)} cards to {out_path}")

    # Rejects
    for i, reject in enumerate(rejects):
        rej_path = rejects_dir / f"reject_{i:03d}.json"
        with open(rej_path, "w") as f:
            json.dump(reject, f, indent=2)
        print(f"[generate] Wrote reject #{i} to {rej_path}")

    # Summary
    summary = {
        "batch_id": batch_id,
        "total_generated": len(cards) + len(rejects),
        "accepted": len(cards),
        "rejected": len(rejects),
        "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    }
    with open(work_dir / "01_summary.json", "w") as f:
        json.dump(summary, f, indent=2)
    print(f"[generate] Summary: {summary}")


# ── CLI ─────────────────────────────────────────────────────────────────────

def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Runewake AI Pipeline — GENERATE stage",
    )
    parser.add_argument("--seed", required=True,
                        help="Path to the seed JSON file")
    parser.add_argument("--work-dir", required=True,
                        help="Output directory for this batch (e.g. work/b_2026_ember_01)")
    parser.add_argument("--model",
                        help="OpenRouter model name (overrides config)")
    parser.add_argument("--temperature", type=float,
                        help="Generation temperature (overrides config)")
    parser.add_argument("--api-key",
                        help="OpenRouter API key (default: $OPENROUTER_API_KEY)")
    parser.add_argument("--config",
                        default=str(DEFAULT_CONFIG),
                        help=f"Config file path (default: {DEFAULT_CONFIG})")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)

    # Load config
    config_path = Path(args.config)
    if not config_path.exists():
        print(f"[generate] Config not found: {config_path}", file=sys.stderr)
        return 1
    cfg = load_config(config_path)
    defaults = cfg.get("defaults", {})
    api_cfg = cfg.get("api", {})

    # Resolve settings (CLI > env > config)
    api_key = args.api_key or os.environ.get("OPENROUTER_API_KEY")
    if not api_key:
        print("[generate] No API key. Set OPENROUTER_API_KEY or pass --api-key.",
              file=sys.stderr)
        return 1

    model = args.model or defaults.get("model", "openai/gpt-4o-mini")
    temperature = args.temperature if args.temperature is not None else defaults.get("temperature", 0.9)
    timeout = api_cfg.get("timeout_seconds", 120)
    max_retries = defaults.get("max_retries", 1)

    # Load seed
    seed_path = Path(args.seed)
    if not seed_path.exists():
        print(f"[generate] Seed not found: {seed_path}", file=sys.stderr)
        return 1
    with open(seed_path) as f:
        seed = json.load(f)

    batch_id = seed.get("batch_id", seed_path.stem)
    work_dir = Path(args.work_dir)
    stratum = seed["strata"]

    print(f"[generate] Starting batch={batch_id} strata={stratum} model={model}")

    # Build prompt
    examples = load_examples()
    existing_names = load_existing_names(stratum)
    user_prompt = build_prompt(seed, examples, existing_names)
    system_prompt = SYSTEM_RULES

    # Initialize client
    client = OpenAI(
        base_url=api_cfg.get("base_url", "https://openrouter.ai/api/v1"),
        api_key=api_key,
    )

    # LLM call with retry
    all_cards = []
    rejects = []
    cards_needed = seed["count"]
    attempt = 0

    while cards_needed > 0 and attempt <= max_retries:
        attempt += 1
        print(f"[generate] Attempt {attempt}/{max_retries + 1} — requesting {cards_needed} cards")

        # Adjust user prompt for retries after partial success
        if attempt > 1:
            user_prompt += (
                f"\n## Retry notes\n"
                f"The previous attempt failed to parse or was incomplete. "
                f"Still need {cards_needed} cards. "
                f"Output ONLY a valid JSON array — no other text.\n"
            )

        try:
            raw = call_llm(client, model, system_prompt, user_prompt,
                          temperature, timeout)
        except Exception as e:
            print(f"[generate] LLM call failed: {e}", file=sys.stderr)
            # Don't reject all pending if it's a transient error
            continue

        # Try to parse
        cleaned = repair_json(raw)
        try:
            batch = json.loads(cleaned)
        except json.JSONDecodeError as e:
            print(f"[generate] JSON parse failed: {e}")
            print(f"[generate] Raw output snippet: {raw[:200]}...")
            if attempt <= max_retries:
                print("[generate] Will retry with parse error feedback")
                user_prompt += f"\n## Parse error from previous attempt: {e}\n"
                continue
            else:
                # Exhausted retries — everything goes to rejects
                reason = f"ParseError: {e}"
                rejects.append({"reason": reason, "raw_snippet": raw[:500]})
                break

        # Validate each card has required fields
        for card in batch:
            if not isinstance(card, dict):
                rejects.append({"reason": "Not a dict", "raw": str(card)[:200]})
                continue
            if card.get("strata") != stratum:
                rejects.append({"reason": f"Wrong strata: {card.get('strata')}",
                                "name": card.get("name", "?")})
                continue
            if "name" not in card or "id" not in card:
                rejects.append({"reason": "Missing name or id", "card": str(card)[:200]})
                continue
            all_cards.append(card)

        cards_needed = seed["count"] - len(all_cards)
        print(f"[generate] Got {len(batch)} cards in batch, {len(all_cards)} accepted total")

    # Write output
    write_output(work_dir, batch_id, all_cards, rejects)

    if rejects:
        print(f"[generate] {len(rejects)} cards rejected (see work dir for details)")
        return 2 if len(all_cards) == 0 else 0

    print(f"[generate] ✓ {len(all_cards)} cards generated successfully")
    return 0


if __name__ == "__main__":
    sys.exit(main())
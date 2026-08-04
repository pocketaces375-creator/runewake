#!/usr/bin/env python3
"""P6-06: DEDUPE + MODERATE — Similarity check, name dedup, content safety.

Reads cards from a SIMULATE-stage output (04_simulated.json or 03_scored.json),
runs three gates — exact name match, fuzzy name similarity (Jaro-Winkler),
n-gram text cosine similarity — plus a moderation blocklist check.

Usage:
    python -m pipeline.modules.dedupe_moderate --input work/b_2026_ember_01/04_simulated.json \\
        --work-dir work/b_2026_ember_01
"""

import argparse
import json
import math
import re
import sys
import time
from pathlib import Path
from typing import Any

import numpy as np
import yaml

HERE = Path(__file__).resolve().parent.parent  # pipeline/
ROOT = HERE.parent  # runewake/
DEDUPE_DIR = HERE / "dedupe"
BLOCKLIST_PATH = DEDUPE_DIR / "blocklist.yaml"
EXISTING_NAMES_PATH = DEDUPE_DIR / "existing_card_names.json"

# Default similarity thresholds (from spec §6)
NAME_FUZZY_THRESHOLD = 0.85  # Jaro-Winkler similarity
TEXT_SIMILARITY_THRESHOLD = 0.80  # n-gram cosine similarity (lower than embedding-based 0.93)

# ── Blocklist loading ────────────────────────────────────────────────────────

_blocklist_cache: dict[str, list[str]] | None = None


def load_blocklist() -> dict[str, list[str]]:
    """Load the YAML blocklist."""
    global _blocklist_cache
    if _blocklist_cache is not None:
        return _blocklist_cache
    with open(BLOCKLIST_PATH) as f:
        data = yaml.safe_load(f)
    _blocklist_cache = data
    return data


def load_existing_names() -> set[str]:
    """Load the set of existing card names for exact-match dedup."""
    if not EXISTING_NAMES_PATH.exists():
        return set()
    with open(EXISTING_NAMES_PATH) as f:
        return set(json.load(f))


# ── Fuzzy string matching (Jaro-Winkler) ────────────────────────────────────

def _jaro(s1: str, s2: str) -> float:
    """Compute Jaro similarity between two strings."""
    if s1 == s2:
        return 1.0
    len_s1, len_s2 = len(s1), len(s2)
    match_dist = max(len_s1, len_s2) // 2 - 1
    if match_dist < 0:
        match_dist = 0

    s1_matches = [False] * len_s1
    s2_matches = [False] * len_s2
    matches = 0
    transpositions = 0

    for i in range(len_s1):
        start = max(0, i - match_dist)
        end = min(i + match_dist + 1, len_s2)
        for j in range(start, end):
            if s2_matches[j]:
                continue
            if s1[i] != s2[j]:
                continue
            s1_matches[i] = True
            s2_matches[j] = True
            matches += 1
            break

    if matches == 0:
        return 0.0

    k = 0
    for i in range(len_s1):
        if not s1_matches[i]:
            continue
        while not s2_matches[k]:
            k += 1
        if s1[i] != s2[k]:
            transpositions += 1
        k += 1

    transpositions /= 2
    return (matches / len_s1 + matches / len_s2 +
            (matches - transpositions) / matches) / 3.0


def _jaro_winkler(s1: str, s2: str, prefix_scale: float = 0.1) -> float:
    """Compute Jaro-Winkler similarity (boost for common prefix)."""
    jaro_sim = _jaro(s1, s2)
    if jaro_sim < 0.7:
        return jaro_sim
    # Find common prefix length (max 4)
    prefix_len = 0
    for i in range(min(len(s1), len(s2), 4)):
        if s1[i] == s2[i]:
            prefix_len += 1
        else:
            break
    return jaro_sim + prefix_scale * prefix_len * (1.0 - jaro_sim)


def jaro_winkler_similarity(s1: str, s2: str) -> float:
    """Public interface for Jaro-Winkler similarity between two names."""
    return _jaro_winkler(s1.lower().strip(), s2.lower().strip())


# ── N-gram text similarity ──────────────────────────────────────────────────

def _char_ngrams(text: str, n: int = 3) -> dict[str, int]:
    """Build character n-gram frequency dict from text."""
    text = text.lower().strip()
    ngrams: dict[str, int] = {}
    for i in range(len(text) - n + 1):
        gram = text[i:i + n]
        ngrams[gram] = ngrams.get(gram, 0) + 1
    return ngrams


def _ngram_vector(ngrams: dict[str, int], vocab: list[str]) -> np.ndarray:
    """Convert n-gram dict to a dense vector using the global vocabulary."""
    vec = np.zeros(len(vocab), dtype=np.float32)
    for i, gram in enumerate(vocab):
        vec[i] = ngrams.get(gram, 0)
    return vec


def compute_text_similarity(text_a: str, text_b: str) -> float:
    """Compute cosine similarity of character trigram vectors."""
    if not text_a or not text_b:
        return 0.0
    grams_a = _char_ngrams(text_a)
    grams_b = _char_ngrams(text_b)
    all_grams = list(set(list(grams_a.keys()) + list(grams_b.keys())))
    if not all_grams:
        return 0.0
    vec_a = _ngram_vector(grams_a, all_grams)
    vec_b = _ngram_vector(grams_b, all_grams)
    norm_a = np.linalg.norm(vec_a)
    norm_b = np.linalg.norm(vec_b)
    if norm_a == 0 or norm_b == 0:
        return 0.0
    return float(np.dot(vec_a, vec_b) / (norm_a * norm_b))


# ── Moderation gates ─────────────────────────────────────────────────────────

def build_blocklist_patterns(blocklist: dict[str, list[str]]) -> list[tuple[str, re.Pattern]]:
    """Build a flat list of (category, compiled pattern) from the blocklist.

    Each entry in every category becomes a case-insensitive regex word-boundary pattern.
    """
    patterns: list[tuple[str, re.Pattern]] = []
    for category, terms in blocklist.items():
        if not terms:
            continue
        for term in terms:
            # Skip comments and empty lines
            term = term.strip()
            if not term or term.startswith("#"):
                continue
            # Check for sub-entries (yaml list items)
            # Escape regex special chars
            escaped = re.escape(term)
            pattern = re.compile(r"\b" + escaped + r"\b", re.IGNORECASE)
            patterns.append((category, pattern))
    return patterns


def check_blocklist(name: str, patterns: list[tuple[str, re.Pattern]]) -> list[str]:
    """Check a card name against the blocklist. Returns list of violations."""
    violations: list[str] = []
    for category, pattern in patterns:
        if pattern.search(name):
            violations.append(f"BLOCKLIST_{category.upper()}: name matches '{category}' pattern")
    return violations


def check_card_text_safety(card: dict) -> list[str]:
    """Basic text safety checks on card fields."""
    violations: list[str] = []
    for field in ("name", "flavor"):
        text = card.get(field, "")
        if not text:
            continue
        # Check for HTML/script injection
        if re.search(r"<[a-z]+[^>]*>", text, re.IGNORECASE):
            violations.append(f"MODERATION_HTML: {field} contains HTML tags")
        # Check for URLs
        if re.search(r"https?://\S+", text, re.IGNORECASE):
            violations.append(f"MODERATION_URL: {field} contains URL")
    return violations


# ── Main dedupe + moderate logic ─────────────────────────────────────────────

_NGRAM_CACHE: dict[str, dict[str, int]] = {}


def _get_ngrams(text: str) -> dict[str, int]:
    """Cached n-gram extraction."""
    if text not in _NGRAM_CACHE:
        _NGRAM_CACHE[text] = _char_ngrams(text)
    return _NGRAM_CACHE[text]


def _ngram_cosine_similarity(grams_a: dict[str, int], grams_b: dict[str, int]) -> float:
    """Cosine similarity between two n-gram dicts (no vocab needed)."""
    all_keys = set(list(grams_a.keys()) + list(grams_b.keys()))
    if not all_keys:
        return 0.0
    vec_a = np.array([grams_a.get(k, 0) for k in all_keys], dtype=np.float32)
    vec_b = np.array([grams_b.get(k, 0) for k in all_keys], dtype=np.float32)
    norm_a = np.linalg.norm(vec_a)
    norm_b = np.linalg.norm(vec_b)
    if norm_a == 0 or norm_b == 0:
        return 0.0
    return float(np.dot(vec_a, vec_b) / (norm_a * norm_b))


def check_duplicate_name(name: str, existing_names: set[str],
                          patterns: list[tuple[str, re.Pattern]]) -> list[str]:
    """Check card name against existing names and blocklist.

    Returns list of violation strings (empty = clean).
    """
    violations: list[str] = []
    name_lower = name.lower().strip()

    # Exact match against existing catalog
    if name in existing_names:
        violations.append(f"DEDUPE_EXACT_NAME: '{name}' matches an existing card name")

    # Fuzzy match against existing catalog
    for existing in existing_names:
        sim = jaro_winkler_similarity(name, existing)
        if sim >= NAME_FUZZY_THRESHOLD and name.lower() != existing.lower():
            violations.append(
                f"DEDUPE_FUZZY_NAME: '{name}' ({sim:.2%}) similar to existing '{existing}'"
            )

    # Blocklist check
    blocklist_violations = check_blocklist(name, patterns)
    violations.extend(blocklist_violations)

    return violations


def check_duplicate_text(name: str, card: dict,
                         existing_cards: list[dict]) -> list[str]:
    """Check card against existing cards using text similarity.

    Returns list of violation strings (empty = clean).
    """
    violations: list[str] = []

    if not existing_cards:
        return violations

    # Build a search key from the candidate card
    # Use name + flavor + keywords for comparison
    keywords = ", ".join(card.get("keywords", []))
    abilities_text = _render_abilities_compact(card)
    search_text = f"{name.lower()} {card.get('flavor', '').lower()} {keywords.lower()} {abilities_text}"
    search_grams = _get_ngrams(search_text)

    for existing in existing_cards:
        e_name = existing.get("name", "")
        if e_name == name:
            continue  # skip exact name match (already caught by dedupe)
        e_keywords = ", ".join(existing.get("keywords", []))
        e_abilities = _render_abilities_compact(existing)
        e_text = f"{e_name.lower()} {existing.get('flavor', '').lower()} {e_keywords.lower()} {e_abilities}"
        e_grams = _get_ngrams(e_text)
        sim = _ngram_cosine_similarity(search_grams, e_grams)
        if sim >= TEXT_SIMILARITY_THRESHOLD:
            violations.append(
                f"DEDUPE_TEXT_SIM: '{name}' ({sim:.2%}) text-similar to '{e_name}'"
            )

    # Deduplicate error messages
    seen = set()
    unique: list[str] = []
    for v in violations:
        if v not in seen:
            seen.add(v)
            unique.append(v)
    return unique


def _render_abilities_compact(card: dict) -> str:
    """Render abilities to a compact string for similarity comparison."""
    parts: list[str] = []
    for ability in card.get("abilities", []):
        trigger = ability.get("trigger", "")
        effects = ability.get("effects", [])
        for effect in effects:
            op = effect.get("op", "")
            amount = effect.get("amount")
            target = effect.get("target", {})
            scope = target.get("scope", "")
            parts.append(f"{trigger} {op} {amount or ''} {scope}")
    return " ".join(parts)


# ── Main entry point ─────────────────────────────────────────────────────────

def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Runewake AI Pipeline — DEDUPE + MODERATE stage",
    )
    parser.add_argument("--input", required=True,
                        help="Input card file (from prior stage)")
    parser.add_argument("--work-dir", required=True,
                        help="Work directory for this batch")
    parser.add_argument("--existing-cards",
                        help="Optional path to existing card pack for text dedup")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)

    input_path = Path(args.input)
    if not input_path.exists():
        print(f"[dedupe] Input not found: {input_path}", file=sys.stderr)
        return 1

    work_dir = Path(args.work_dir)
    work_dir.mkdir(parents=True, exist_ok=True)
    rejects_dir = work_dir / "rejects"
    rejects_dir.mkdir(parents=True, exist_ok=True)

    with open(input_path) as f:
        raw = json.load(f)
    cards = raw if isinstance(raw, list) else [raw]

    # Load blocklist + existing names
    blocklist = load_blocklist()
    patterns = build_blocklist_patterns(blocklist)
    existing_names = load_existing_names()

    # Load existing cards for text dedup
    existing_cards: list[dict] = []
    if args.existing_cards:
        ec_path = Path(args.existing_cards)
        if ec_path.exists():
            with open(ec_path) as f:
                existing_cards = json.load(f)
            existing_cards = existing_cards if isinstance(existing_cards, list) else [existing_cards]
            print(f"[dedupe] Loaded {len(existing_cards)} existing cards for text dedup")

    # Also add hand-authored cards
    for strata in ["verdant", "ember", "tide", "hollow", "dawn"]:
        pack_path = ROOT / "content" / "cards" / f"{strata}.json"
        if pack_path.exists():
            with open(pack_path) as f:
                existing_cards.extend(json.load(f))

    print(f"[dedupe] Checking {len(cards)} cards...")
    print(f"[dedupe] Existing names: {len(existing_names)}, blocklist patterns: {len(patterns)}")

    passed: list[dict] = []
    rejects: list[tuple[dict, str]] = []

    for card in cards:
        name = card.get("name", "")
        violations: list[str] = []

        # Gate 1: Name dedup + blocklist
        name_violations = check_duplicate_name(name, existing_names, patterns)
        violations.extend(name_violations)

        # Gate 2: Text safety
        safety_violations = check_card_text_safety(card)
        violations.extend(safety_violations)

        # Gate 3: Text similarity (dedup against existing catalog)
        if not name_violations:  # skip if already rejected for exact match
            text_violations = check_duplicate_text(name, card, existing_cards)
            violations.extend(text_violations)

        if violations:
            reason = "; ".join(violations)
            rejects.append((card, f"DEDUPE_MODERATE_FAIL: {reason}"))
        else:
            passed.append(card)

    # Write outputs
    out_path = work_dir / "05_deduplicated.json"
    with open(out_path, "w") as f:
        json.dump(passed, f, indent=2)

    if rejects:
        for i, (card, reason) in enumerate(rejects):
            rej_path = rejects_dir / f"reject_dedupe_{i:03d}.json"
            with open(rej_path, "w") as f:
                json.dump({"card": card, "reason": reason}, f, indent=2)

    summary = {
        "input_file": str(input_path),
        "total_processed": len(cards),
        "passed": len(passed),
        "rejected": len(rejects),
        "blocklist_patterns": len(patterns),
        "existing_names": len(existing_names),
        "timestamp": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    }
    with open(work_dir / "05_summary.json", "w") as f:
        json.dump(summary, f, indent=2)

    print(f"[dedupe] Wrote {len(passed)} passed cards to {out_path}")
    print(f"[dedupe] Summary: {summary}")

    if rejects:
        print(f"[dedupe] {len(rejects)} cards rejected")
        return 2 if len(passed) == 0 else 0

    print(f"[dedupe] ✓ All {len(passed)} cards passed dedupe + moderation")
    return 0


if __name__ == "__main__":
    sys.exit(main())
#!/usr/bin/env python3
"""TASK-CARD-TEXT-GEN-1: Golden-master test for card rules text generation.

Loads every card from content/cards/*.json, renders its rules text via
render_rules_text(), and compares the result against a stored golden file.
If any card's rendered text differs from its golden entry, the test fails
with a diff of every changed card. Regenerate the golden file by running:

    python pipeline/tests/regenerate_card_text.py
"""

import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent.parent  # pipeline/
sys.path.insert(0, str(HERE))

from modules.render_rules import render_rules_text

# The golden file lives alongside the test file
GOLDEN_PATH = HERE / "tests" / "cards_rules_text.json"


def load_all_cards() -> dict[str, dict]:
    """Load every card from content/cards/*.json, keyed by card id."""
    content_dir = HERE.parent / "content" / "cards"
    all_cards: dict[str, dict] = {}
    for path in sorted(content_dir.glob("*.json")):
        with open(path) as f:
            cards = json.load(f)
        for card in cards:
            cid = card.get("id")
            if cid:
                all_cards[cid] = card
    return all_cards


def render_all_cards(cards: dict[str, dict]) -> dict[str, str]:
    """Render every card's rules text into a {card_id: text} dict."""
    result: dict[str, str] = {}
    for cid, card in cards.items():
        result[cid] = render_rules_text(card)
    return result


def load_golden() -> dict[str, str]:
    """Load the stored golden rules text."""
    if not GOLDEN_PATH.exists():
        return {}
    with open(GOLDEN_PATH) as f:
        return json.load(f)


def save_golden(text_map: dict[str, str]):
    """Save the current rendered text as the golden reference."""
    with open(GOLDEN_PATH, "w") as f:
        json.dump(text_map, f, indent=2, sort_keys=True)
    print(f"[golden] Saved {len(text_map)} card text entries to {GOLDEN_PATH}")


def test_all_card_text_matches_golden():
    """Golden-master: every card's rendered text must match its stored golden.

    Fail if any card's rendered text has changed. Show the diff for every
    changed card so the developer can see whether the change is intentional.
    """
    cards = load_all_cards()
    rendered = render_all_cards(cards)
    golden = load_golden()

    if not golden:
        # First run: no golden file yet — save it and pass
        save_golden(rendered)
        return

    # Compare every card
    failures: list[tuple[str, str, str]] = []
    extra_in_render: set[str] = set(rendered.keys()) - set(golden.keys())
    extra_in_golden: set[str] = set(golden.keys()) - set(rendered.keys())

    if extra_in_render:
        for cid in sorted(extra_in_render):
            failures.append((cid, "(missing from golden)", rendered[cid][:80]))
    if extra_in_golden:
        for cid in sorted(extra_in_golden):
            failures.append((cid, f"(removed, was {golden[cid][:80]})", ""))

    for cid in sorted(set(rendered.keys()) & set(golden.keys())):
        if rendered[cid] not in (golden.get(cid), None):
            failures.append((cid, golden.get(cid, "(none)"), rendered[cid]))

    if failures:
        lines = [f"\n❌ {len(failures)} card(s) have changed rules text:"]
        for cid, expected, actual in failures:
            lines.append(f"\n  [{cid}]")
            lines.append(f"    expected: {expected[:100]}")
            lines.append(f"    actual:   {actual[:100]}")
        raise AssertionError("\n".join(lines))

    print(f"[test] ✅ All {len(rendered)} card texts match golden reference")


def test_keyword_reminders_are_shorter():
    """Every keyword reminder should be shorter than its full description."""
    from modules.render_rules import KEYWORD_REMINDERS, KEYWORD_DESCRIPTIONS
    for kw in KEYWORD_REMINDERS:
        reminder = KEYWORD_REMINDERS[kw]
        desc = KEYWORD_DESCRIPTIONS.get(kw, "")
        assert len(reminder) <= len(desc), (
            f"Reminder for {kw} ({len(reminder)} chars) is longer than "
            f"description ({len(desc)} chars)"
        )
    print(f"[test] ✅ All {len(KEYWORD_REMINDERS)} reminders are shorter than their descriptions")


def test_every_keyword_has_reminder():
    """Every keyword defined in KEYWORD_DESCRIPTIONS should have a reminder."""
    from modules.render_rules import KEYWORD_REMINDERS, KEYWORD_DESCRIPTIONS
    for kw in KEYWORD_DESCRIPTIONS:
        assert kw in KEYWORD_REMINDERS, (
            f"Keyword {kw} has a description but no reminder text"
        )
    print(f"[test] ✅ All {len(KEYWORD_DESCRIPTIONS)} keywords have reminder text")


if __name__ == "__main__":
    # When run directly, test + optionally regenerate
    import argparse
    parser = argparse.ArgumentParser(description="Card text golden-master test")
    parser.add_argument("--regenerate", action="store_true",
                        help="Regenerate the golden file from current cards")
    args = parser.parse_args()

    cards = load_all_cards()
    rendered = render_all_cards(cards)

    if args.regenerate:
        save_golden(rendered)
        sys.exit(0)

    # Run tests
    try:
        test_all_card_text_matches_golden()
        test_keyword_reminders_are_shorter()
        test_every_keyword_has_reminder()
        print("\n✅ All card text golden-master tests passed")
    except AssertionError as e:
        print(f"\n❌ {e}")
        sys.exit(1)
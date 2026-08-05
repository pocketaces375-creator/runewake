#!/usr/bin/env python3
"""Tests for pipeline/modules/render_rules.py — rules text renderer."""

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(HERE))

from modules.render_rules import (
    render_rules_text,
    render_ability,
    render_effect,
    render_target,
    KEYWORD_DESCRIPTIONS,
)


# ── Keyword tests ─────────────────────────────────────────────────────────────


def test_all_keywords_have_descriptions():
    """Every defined keyword should have a human-readable description."""
    for kw in ["GUARD", "SWIFT", "PIERCE", "WARD", "VENOM", "REACH",
               "ROOTED", "UNEARTH", "ECHO", "FRAGILE", "SEALED"]:
        assert kw in KEYWORD_DESCRIPTIONS
        assert len(KEYWORD_DESCRIPTIONS[kw]) > 10


def test_card_with_keywords():
    """Keywords should appear in rules text."""
    card = {"keywords": ["GUARD", "SWIFT"], "abilities": []}
    text = render_rules_text(card)
    assert "Guard" in text
    assert "Swift" in text


# ── Target rendering tests ────────────────────────────────────────────────────


def test_target_self():
    assert render_target({"scope": "SELF"}) == "a itself"


def test_target_ally_creature():
    assert "ally creature" in render_target({"scope": "ALLY_CREATURE"})


def test_target_all_adjacent():
    assert render_target({"scope": "ALLY_CREATURE", "filter": "ADJACENT", "count": "ALL"}) == "all adjacent allied creatures"


def test_target_random_enemy():
    text = render_target({"scope": "ENEMY_CREATURE", "filter": "RANDOM"})
    assert "random" in text
    assert "enemy" in text


# ── Effect rendering tests ────────────────────────────────────────────────────


def test_effect_damage():
    text = render_effect({"op": "DAMAGE", "value": 3, "target": {"scope": "ENEMY_CREATURE"}})
    assert "Deal 3 damage" in text
    assert "enemy creature" in text


def test_effect_buff():
    text = render_effect({"op": "BUFF", "attack": 1, "vigor": 2,
                          "target": {"scope": "ALLY_CREATURE", "filter": "ADJACENT", "count": "ALL"},
                          "duration": "THIS_TURN"})
    assert "Grant +1/+2" in text
    assert "this turn" in text


def test_effect_draw():
    text = render_effect({"op": "DRAW", "value": 2, "target": {"scope": "PLAYER_SELF"}})
    assert "Draw 2 card(s)" in text


# ── Ability rendering tests ────────────────────────────────────────────────────


def test_ability_on_summon():
    card = {
        "abilities": [
            {"trigger": "ON_SUMMON", "condition": None, "effects": [
                {"op": "BUFF", "attack": 0, "vigor": 1,
                 "target": {"scope": "ALLY_CREATURE", "filter": "ADJACENT", "count": "ALL"},
                 "duration": "PERMANENT"}
            ]}
        ]
    }
    text = render_rules_text(card)
    assert "When this enters play" in text
    assert "Grant +0/+1" in text


# ── Full card tests ────────────────────────────────────────────────────────────


def test_full_creature_card():
    """Render a complete creature card with keywords and abilities."""
    card = {
        "name": "Root Warden",
        "keywords": ["GUARD"],
        "abilities": [
            {
                "trigger": "ON_SUMMON",
                "condition": None,
                "effects": [
                    {
                        "op": "BUFF", "attack": 0, "vigor": 1,
                        "target": {"scope": "ALLY_CREATURE", "filter": "ADJACENT", "count": "ALL"},
                        "duration": "PERMANENT",
                    }
                ],
            }
        ],
        "flavor": "The grove keeps its own ledgers.",
    }
    text = render_rules_text(card)
    assert "Guard" in text
    assert "When this enters play" in text
    assert "Grant +0/+1" in text
    assert "adjacent" in text
    assert "The grove keeps its own ledgers" in text


def test_card_no_abilities_no_keywords():
    """A vanilla creature should return empty-ish rules text."""
    card = {"keywords": [], "abilities": []}
    text = render_rules_text(card)
    assert "(no rules text)" in text


def test_multiple_effects_one_ability():
    """An ability with multiple effects should list them."""
    card = {
        "abilities": [
            {
                "trigger": "ON_DEATH",
                "effects": [
                    {"op": "DAMAGE", "value": 2, "target": {"scope": "ENEMY_CREATURE", "filter": "RANDOM"}},
                    {"op": "HEAL", "value": 1, "target": {"scope": "PLAYER_SELF"}},
                ],
            }
        ]
    }
    text = render_rules_text(card)
    assert "When this dies" in text
    assert "Deal 2 damage" in text
    assert "Heal 1" in text


def test_card_with_summon_token():
    """Summon effects should render correctly."""
    card = {
        "abilities": [
            {
                "trigger": "ON_SUMMON",
                "effects": [
                    {"op": "SUMMON", "value": 2, "target": {"scope": "LANE"}}
                ],
            }
        ]
    }
    text = render_rules_text(card)
    assert "Summon 2 token(s)" in text
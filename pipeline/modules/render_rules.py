#!/usr/bin/env python3
"""Rules text renderer — converts card JSON into human-readable rules text.

The renderer takes a card's keywords + abilities array and produces
consistent, human-readable rules text. This is the **only** rules text
renderer — one phrasing style, zero inconsistency across 10,000 cards.

Usage:
    from modules.render_rules import render_rules_text
    text = render_rules_text(card_dict)
"""

from typing import Any

# ── Keyword descriptions ──────────────────────────────────────────────────────

KEYWORD_DESCRIPTIONS: dict[str, str] = {
    "GUARD": "Guard — this creature may block for adjacent allies.",
    "SWIFT": "Swift — this creature can attack the turn it is played.",
    "PIERCE": "Pierce — excess damage carries over to the enemy player.",
    "WARD": "Ward — the first time this creature would be targeted by an enemy ability, negate it.",
    "VENOM": "Venom — when this creature deals damage, it deals 1 additional damage to the target.",
    "REACH": "Reach — this creature can attack any lane, not just the opposing lane.",
    "ROOTED": "Rooted — this creature cannot be moved or returned to hand.",
    "UNEARTH": "Unearth — when this creature dies, return it to hand instead.",
    "ECHO": "Echo — when this creature is played, copy the last ability played by either player.",
    "FRAGILE": "Fragile — when this creature takes damage, it dies.",
    "SEALED": "Sealed — this relic starts unidentified. It is revealed when its identify condition is met.",
    "ANCESTRAL_SHIELD": "Ancestral Shield — the first time each turn an ally would be reduced to below 1 Vigor by an enemy spell, clamp it to 1 instead (damage triggers still resolve).",
    "STEALTH_STRIKE": "Stealth Strike — when this creature attacks, it deals no counter-damage to the defender.",
}

# ── Keyword reminders (short descriptions for tooltip / long-press) ──────────

KEYWORD_REMINDERS: dict[str, str] = {
    "GUARD": "May block for adjacent allies.",
    "SWIFT": "May attack the turn it is played.",
    "PIERCE": "Excess damage carries over to the enemy player.",
    "WARD": "Negate the first enemy ability that targets this creature.",
    "VENOM": "Deals 1 extra damage to the target.",
    "REACH": "May attack any lane.",
    "ROOTED": "Cannot be moved or returned to hand.",
    "UNEARTH": "Return to hand when this dies.",
    "ECHO": "Copy the last ability played.",
    "FRAGILE": "Dies when it takes damage.",
    "SEALED": "Starts unidentified; revealed when its condition is met.",
    "ANCESTRAL_SHIELD": "Once per turn, clamp ally Vigor to 1 when hit by an enemy spell.",
    "STEALTH_STRIKE": "Deals no counter-damage when attacking.",
}

# ── Trigger descriptions ──────────────────────────────────────────────────────

TRIGGER_LABELS: dict[str, str] = {
    "ON_SUMMON": "When this enters play",
    "ON_DEATH": "When this dies",
    "ON_ATTACK": "When this attacks",
    "ON_DAMAGED": "When this takes damage",
    "ON_TURN_START": "At the start of your turn",
    "ON_TURN_END": "At the end of your turn",
    "ON_CAST_RITUAL": "When you cast a ritual",
    "ON_EXCAVATE": "When you excavate",
    "ON_RELIC_IDENTIFY": "When a relic is identified",
    "ON_ALLY_DEATH": "When an ally dies",
    "ON_LANE_VACATED": "When a lane becomes empty",
    "PASSIVE": "Passive — while this is in play",
    "ACTIVATED": "Activated (tap this card)",
    "RESOLVE": "On resolution",
}

# ── Op descriptions ────────────────────────────────────────────────────────────

OP_LABELS: dict[str, str] = {
    "DAMAGE": "Deal {value} damage",
    "HEAL": "Heal {value}",
    "BUFF": "Grant +{attack}/+{vigor}",
    "DEBUFF": "Apply -{attack}/-{vigor}",
    "DESTROY": "Destroy",
    "DRAW": "Draw {value} card(s)",
    "SCY": "Scry {value}",
    "DISCARD": "Discard {value} card(s)",
    "EXCAVATE": "Excavate {value}",
    "BURY": "Bury {value} card(s)",
    "UNBURY": "Unbury {value} card(s)",
    "SUMMON": "Summon {value} token(s)",
    "GRANT_KEY": "Grant {keyword}",
    "REMOVE_KEY": "Remove {keyword}",
    "SILENCE": "Silence",
    "BOUNCE": "Return to hand",
    "ATTUNE": "Attune to {strata}",
    "MOVE_LANE": "Move to another lane",
    "IDENTIFY": "Identify",
    "GAIN_VIGOR": "Gain {value} vigor",
    "LOSE_VIGOR": "Lose {value} vigor",
    "COPY": "Copy target ability",
    "SET_STAT": "Set attack to {attack}, vigor to {vigor}",
    "REFRESH": "Refresh",
}

# ── Scope descriptions ────────────────────────────────────────────────────────

SCOPE_LABELS: dict[str, str] = {
    "SELF": "itself",
    "ALLY_CREATURE": "an ally creature",
    "ENEMY_CREATURE": "an enemy creature",
    "ANY_CREATURE": "any creature",
    "PLAYER_SELF": "you",
    "PLAYER_ENEMY": "the enemy player",
    "LANE": "the lane",
    "NONE": "",
}

SCOPE_COUNT: dict[str, str] = {
    "SELF": "itself",
    "ALLY_CREATURE": "allied creatures",
    "ENEMY_CREATURE": "enemy creatures",
    "ANY_CREATURE": "creatures",
    "PLAYER_SELF": "you",
    "PLAYER_ENEMY": "the enemy player",
    "LANE": "the lane",
    "NONE": "",
}

# ── Duration ──────────────────────────────────────────────────────────────────

DURATION_LABELS: dict[str, str] = {
    "PERMANENT": "",
    "THIS_TURN": "this turn",
    "NEXT_TURN": "next turn",
    "WHILE_PRESENT": "while this is in play",
}

# ── Filter ────────────────────────────────────────────────────────────────────

FILTER_LABELS: dict[str, str] = {
    "ANY": "any",
    "ADJACENT": "adjacent",
    "OPPOSING": "opposing",
    "SAME_LANE": "same lane",
    "EDGE_LANE": "edge lane",
    "CENTER_LANE": "center lane",
    "RANDOM": "random",
    "LOWEST_VIGOR": "with lowest vigor",
    "HIGHEST_ATTACK": "with highest attack",
    "LOWEST_COST": "with lowest cost",
    "HIGHEST_COST": "with highest cost",
    "CHOSEN": "chosen",
    "DAMAGED": "damaged",
    "EXHAUSTED": "exhausted",
    "UNDAMAGED": "undamaged",
}

# ── Dynamic filter helpers ────────────────────────────────────────────────────


def render_filter(filter_val: str | None) -> str:
    """Render a filter value to human-readable text, handling KEYWORD:* filters."""
    if not filter_val or filter_val in ("ANY", "NONE"):
        return ""
    if filter_val.startswith("KEYWORD:"):
        kw = filter_val[len("KEYWORD:"):]
        kw_lower = kw.lower().replace("_", " ")
        return f"with {kw_lower}"
    return FILTER_LABELS.get(filter_val, filter_val)


# ── Renderer ──────────────────────────────────────────────────────────────────


def render_target(target: dict[str, Any] | None) -> str:
    """Render a target spec to human-readable text."""
    if not target:
        return ""

    scope = target.get("scope", "SELF")
    filt = target.get("filter", "ANY")
    count = target.get("count", 1)

    # Build scope text
    count_str = str(count) if isinstance(count, int) and count > 1 else (
        "all" if count == "ALL" else "a"
    )

    if not isinstance(count, int) and count == "ALL":
        prefix = "all"
    elif isinstance(count, int) and count > 1:
        prefix = f"{count}"
    else:
        prefix = "a"

    if scope in SCOPE_COUNT and count in ("ALL", "all", None):
        scope_text = SCOPE_COUNT.get(scope, scope)
    elif scope in SCOPE_LABELS:
        scope_text = SCOPE_LABELS.get(scope, scope)
    else:
        scope_text = scope

    # Add filter
    if filt and filt != "ANY" and filt != "NONE":
        filter_text = render_filter(filt)
        if filter_text == "any":
            return f"{prefix} {scope_text}"
        return f"{prefix} {filter_text} {scope_text}"

    return f"{prefix} {scope_text}"


def render_effect(effect: dict[str, Any]) -> str:
    """Render a single effect to human-readable text."""
    op = effect.get("op", "?")
    target = effect.get("target")
    duration = effect.get("duration", "PERMANENT")

    # Build the op phrase
    template = OP_LABELS.get(op, f"Apply {op}")

    # Fill in template values
    value = effect.get("value", effect.get("amount", 1))
    attack = effect.get("attack", 0)
    vigor = effect.get("vigor", 0)
    keyword = effect.get("keyword", "")
    strata = effect.get("strata", "")

    op_text = template.format(
        value=value,
        attack=attack,
        vigor=vigor,
        keyword=keyword,
        strata=strata,
    )

    # Build target phrase
    target_text = render_target(target)

    # Build duration
    dur_text = DURATION_LABELS.get(duration, duration)

    parts = [op_text]
    if target_text:
        parts.append(target_text)
    if dur_text:
        parts.append(dur_text)

    return " ".join(parts)


def render_ability(ability: dict[str, Any]) -> str:
    """Render a single ability to human-readable text."""
    trigger = ability.get("trigger", "?")
    effects = ability.get("effects", [])
    condition = ability.get("condition")

    trigger_text = TRIGGER_LABELS.get(trigger, trigger)

    # Render condition
    condition_text = ""
    if condition and condition.get("op"):
        cond_op = condition.get("op", "")
        cond_value = condition.get("value", "")
        condition_text = f" if {cond_op} {cond_value}"

    # Render each effect
    effect_texts = [render_effect(e) for e in effects]

    if not effect_texts:
        return f"{trigger_text}{condition_text}."

    lines = []
    if len(effect_texts) == 1:
        lines.append(f"{trigger_text}{condition_text}: {effect_texts[0]}.")
    else:
        lines.append(f"{trigger_text}{condition_text}:")
        for et in effect_texts:
            lines.append(f"  \u2022 {et}.")

    return "\n".join(lines)


def render_rules_text(card: dict[str, Any]) -> str:
    """Render a complete card to its human-readable rules text.

    Returns a string with human-readable rules text, including keywords
    and abilities. One line per keyword, abilities grouped by trigger.
    """
    lines: list[str] = []

    # Keywords
    keywords = card.get("keywords", [])
    if not keywords:
        keywords = []
    for kw in keywords:
        desc = KEYWORD_DESCRIPTIONS.get(kw, kw)
        lines.append(desc)

    # Abilities
    abilities = card.get("abilities", [])
    if not abilities:
        abilities = []
    for ab in abilities:
        rendered = render_ability(ab)
        if rendered:
            lines.append(str(rendered))


    # Flavor text
    flavor = card.get("flavor", "")
    if flavor:
        lines.append(f'"{flavor}"')

    return "\n\n".join(lines) if lines else "(no rules text)"
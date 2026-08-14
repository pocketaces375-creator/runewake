using System.Text;
using System.Text.Json;

namespace Runewake.Engine.Cards;

/// <summary>
/// Renders a <see cref="CardDef"/> into human-readable rules text.
/// All text is driven from the DSL — no manual strings embedded in card data.
/// </summary>
public static class RulesTextRenderer
{
    /// <summary>
    /// Render full card rules text: stat line, keywords, identify condition, abilities, flavor.
    /// </summary>
    public static string Render(CardDef card)
    {
        var sb = new StringBuilder();

        // Stat line for creatures/tokens
        bool hasStatLine = card.Type is CardType.CREATURE or CardType.TOKEN;
        if (hasStatLine && card.Attack.HasValue && card.Vigor.HasValue)
        {
            sb.Append($"{card.Attack}/{card.Vigor}");
            if (card.Keywords.Count > 0)
            {
                sb.Append(" — ");
                sb.AppendJoin(", ", card.Keywords.Select(FormatKeyword));
            }
        }
        else if (card.Keywords.Count > 0)
        {
            sb.AppendJoin(", ", card.Keywords.Select(FormatKeyword));
        }

        // Identify condition for relics
        if (card.IdentifyCondition != null)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append("⛭ Identify: ");
            sb.Append(RenderCondition(card.IdentifyCondition));
        }

        // Abilities
        foreach (var ability in card.Abilities)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(RenderAbility(ability));
        }

        // Flavor
        if (!string.IsNullOrEmpty(card.Flavor))
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append('"');
            sb.Append(card.Flavor);
            sb.Append('"');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Render only the ability text portion of a card (no stats, keywords, flavor, or identify).
    /// </summary>
    public static string RenderAbilityTextOnly(CardDef card)
    {
        var sb = new StringBuilder();
        foreach (var ability in card.Abilities)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(RenderAbility(ability));
        }
        return sb.ToString();
    }

    // ——— Ability rendering ———

    private static string RenderAbility(AbilityDef ability)
    {
        string condition = ability.Condition != null
            ? $"if {RenderCondition(ability.Condition)}, "
            : "";

        string effects = string.Join(". ", ability.Effects.Select(RenderEffect));

        // RESOLVE — no prefix (rituals)
        if (ability.Trigger == Trigger.RESOLVE)
            return condition + effects;

        // PASSIVE — just "Passive: effects"
        if (ability.Trigger == Trigger.PASSIVE)
            return "Passive: " + condition + effects;

        // ACTIVATED — "Activate (cost):" or "Activate:"
        if (ability.Trigger == Trigger.ACTIVATED)
        {
            string prefix = ability.ActivationCost is > 0
                ? $"Activate ({ability.ActivationCost}): "
                : "Activate: ";
            return prefix + condition + effects;
        }

        // Triggered abilities — "Trigger: condition, effects"
        string trigger = RenderTriggerName(ability.Trigger);
        return trigger + ": " + condition + effects;
    }

    private static string RenderTriggerName(Trigger trigger) => trigger switch
    {
        Trigger.ON_SUMMON => "When this enters play",
        Trigger.ON_DEATH => "When this dies",
        Trigger.ON_ATTACK => "When this attacks",
        Trigger.ON_DAMAGED => "When this takes damage",
        Trigger.ON_TURN_START => "At the start of your turn",
        Trigger.ON_TURN_END => "At the end of your turn",
        Trigger.ON_CAST_RITUAL => "When you play a Ritual",
        Trigger.ON_EXCAVATE => "When you Excavate",
        Trigger.ON_RELIC_IDENTIFY => "When this identifies",
        Trigger.ON_ALLY_DEATH => "When an ally dies",
        Trigger.ON_LANE_VACATED => "When a lane becomes empty",
        _ => "?"
    };

    // ——— Effect rendering ———

    private static string RenderEffect(EffectDef effect)
    {
        var target = effect.Target;
        var scope = target?.Scope;

        switch (effect.Op)
        {
            case Op.DAMAGE:
            {
                string amt = effect.Amount?.ToString() ?? "?";
                if (scope == Scope.PLAYER_ENEMY)
                    return $"Deal {amt} damage to the enemy";
                string tgt = RenderTargetPhrase(target);
                return $"Deal {amt} damage to {tgt}";
            }

            case Op.HEAL:
            {
                string amt = effect.Amount?.ToString() ?? "?";
                string tgt = RenderTargetPhrase(target);
                return $"Heal {amt} from {tgt}";
            }

            case Op.BUFF:
            {
                string bonus = RenderStatBonus(effect.Attack, effect.Vigor, true);
                string tgt = RenderTargetPhrase(target);
                return $"Give {tgt} {bonus}";
            }

            case Op.DEBUFF:
            {
                string bonus = RenderStatBonus(effect.Attack, effect.Vigor, false);
                string tgt = RenderTargetPhrase(target);
                return $"Give {tgt} {bonus}";
            }

            case Op.DESTROY:
            {
                string tgt = RenderTargetPhrase(target);
                return $"Destroy {tgt}";
            }

            case Op.DRAW:
            {
                string amt = effect.Amount?.ToString() ?? "?";
                string cardPlural = effect.Amount == 1 ? "card" : "cards";
                if (scope == Scope.PLAYER_ENEMY)
                    return $"The enemy draws {amt} {cardPlural}";
                return $"Draw {amt} {cardPlural}";
            }

            case Op.DISCARD:
            {
                string amt = effect.Amount?.ToString() ?? "?";
                string tgt = scope == Scope.PLAYER_ENEMY ? "the enemy" : "";
                if (string.IsNullOrEmpty(tgt))
                    return $"Discard {amt}";
                return $"{tgt} discards {amt}";
            }

            case Op.EXCAVATE:
                return $"Excavate {effect.Amount ?? 1}";

            case Op.BURY:
                return $"Bury {effect.Amount ?? 1}";

            case Op.UNBURY:
                return $"Unbury {effect.Amount ?? 1}";

            case Op.SUMMON:
                return RenderSummon(effect.TokenId);

            case Op.GRANT_KEY:
            {
                string key = effect.Keyword ?? "?";
                string tgt = RenderTargetPhrase(target);
                return $"Grant {tgt} {FormatKeyword(key)}";
            }

            case Op.REMOVE_KEY:
            {
                string key = effect.Keyword ?? "?";
                string tgt = RenderTargetPhrase(target);
                return $"Remove {FormatKeyword(key)} from {tgt}";
            }

            case Op.SILENCE:
            {
                string tgt = RenderTargetPhrase(target);
                return $"Silence {tgt}";
            }

            case Op.BOUNCE:
            {
                string tgt = RenderTargetPhrase(target);
                return $"Return {tgt} to hand";
            }

            case Op.ATTUNE:
                return $"Gain {effect.Amount ?? 1} attunement";

            case Op.GAIN_VIGOR:
                return $"Gain {effect.Amount ?? 1} max vigor";

            case Op.LOSE_VIGOR:
                return $"Lose {effect.Amount ?? 1} max vigor";

            case Op.COPY:
            {
                string tgt = RenderTargetPhrase(target);
                return $"Copy {tgt}";
            }

            case Op.SET_STAT:
            {
                int a = effect.Attack ?? 0;
                int v = effect.Vigor ?? 0;
                string tgt = RenderTargetPhrase(target);
                return $"Set {tgt} to {a}/{v}";
            }

            case Op.REFRESH:
            {
                string tgt = RenderTargetPhrase(target);
                return $"Refresh {tgt}";
            }

            case Op.IDENTIFY:
                return "Identify";

            case Op.MOVE_LANE:
            {
                string tgt = RenderTargetPhrase(target);
                return $"Move {tgt} to another lane";
            }

            case Op.PREVENT_DAMAGE:
            {
                string amt = effect.Amount?.ToString() ?? "?";
                string tgt = RenderTargetPhrase(target);
                return $"Prevent {amt} damage to {tgt}";
            }

            case Op.COST_MOD:
                return RenderCostMod(effect);

            case Op.RESET_CHARGES:
                return "Reset Charges";

            default:
                return $"?{effect.Op}";
        }
    }

    /// <summary>
    /// Render a COST_MOD discount: "Your first spell each turn costs 1 less",
    /// "Creatures with attack ≤ 2 cost 1 less", etc.
    /// </summary>
    private static string RenderCostMod(EffectDef effect)
    {
        int amt = effect.Amount ?? 0;
        string applies = (effect.AppliesTo?.ToUpperInvariant() ?? "ANY") switch
        {
            "CREATURE" => "Creatures",
            "SPELL" => "Spells",
            _ => "Cards"
        };

        string cardFilter = effect.Filter?.ToUpperInvariant() switch
        {
            "ATTACK_LTE" => $" with attack ≤ {effect.Value ?? 0}",
            "FIRST_SPELL_EACH_TURN" => "", // handled by frequency phrase below
            _ => ""
        };

        string freq = effect.Filter?.ToUpperInvariant() switch
        {
            "FIRST_SPELL_EACH_TURN" => "Your first spell each turn",
            _ => applies
        };

        string tail = freq + cardFilter;

        string condition = effect.Condition is not null
            ? $" if {RenderCondition(effect.Condition)}"
            : "";
        string duration = effect.Duration == Duration.THIS_TURN ? " this turn" : "";

        return $"{tail} cost{PluralS(freq)} {amt} less{condition}{duration}";
    }

    private static string PluralS(string subject)
        => subject.EndsWith("s", StringComparison.Ordinal) ? "" : "s";

    // ——— Target phrase ———

    /// <summary>
    /// Renders a TargetDef into a prepositional phrase like "target enemy creature" or "all ally creatures".
    /// </summary>
    private static string RenderTargetPhrase(TargetDef? target)
    {
        if (target == null) return "?";
        var scope = target.Scope;

        if (scope == Scope.PLAYER_SELF) return "you";
        if (scope == Scope.PLAYER_ENEMY) return "the enemy";
        if (scope == Scope.NONE) return "";
        if (scope == Scope.SELF) return "itself";
        if (scope == Scope.LANE) return "the lane";

        // Creature scopes
        string baseNoun = scope switch
        {
            Scope.ALLY_CREATURE => "ally creature",
            Scope.ENEMY_CREATURE => "enemy creature",
            Scope.ANY_CREATURE => "creature",
            _ => "?"
        };

        // Filter adjective
        string adjective = "";
        if (!string.IsNullOrEmpty(target.Filter))
        {
            adjective = target.Filter switch
            {
                "ANY" => "",
                "DAMAGED" => "damaged ",
                "ADJACENT" => "adjacent ",
                "EXHAUSTED" => "exhausted ",
                "CHOSEN" => "chosen ",
                _ => target.Filter.ToLowerInvariant() + " "
            };
        }

        // Count
        bool plural = false;
        string countPrefix = "";
        if (target.Count.HasValue)
        {
            if (target.Count.Value.IsAll)
            {
                countPrefix = "all ";
                plural = true;
            }
            else if (target.Count.Value.Value > 1)
            {
                countPrefix = target.Count.Value.Value + " ";
                plural = true;
            }
            else
            {
                // Could add "a"/"an" but not strictly necessary
            }
        }

        string noun = plural ? baseNoun + "s" : baseNoun;
        return countPrefix + adjective + noun;
    }

    // ——— Helpers ———

    private static string RenderStatBonus(int? attack, int? vigor, bool positive)
    {
        if (attack.HasValue && vigor.HasValue)
        {
            string a = attack.Value >= 0 ? $"+{attack.Value}" : $"{attack.Value}";
            string v = vigor.Value >= 0 ? $"+{vigor.Value}" : $"{vigor.Value}";
            return $"{a}/{v}";
        }

        if (attack.HasValue)
        {
            string sign = positive ? "+" : "";
            string val = attack.Value >= 0 ? $"{sign}{attack.Value}" : $"{attack.Value}";
            return $"{val} attack";
        }

        if (vigor.HasValue)
        {
            string sign = positive ? "+" : "";
            string val = vigor.Value >= 0 ? $"{sign}{vigor.Value}" : $"{vigor.Value}";
            return $"{val} vigor";
        }

        return "?/?";
    }

    private static string RenderSummon(string? tokenId)
    {
        if (tokenId == null) return "Summon a token";
        var token = CardRegistry.Get(tokenId);
        if (token != null)
            return $"Summon a {token.Name}";
        return $"Summon a token ({tokenId})";
    }

    private static string RenderCondition(ConditionDef condition)
    {
        if (condition.All is { Count: > 0 })
            return string.Join(" and ", condition.All.Select(RenderSimpleCondition));

        if (condition.Any is { Count: > 0 })
            return string.Join(" or ", condition.Any.Select(RenderSimpleCondition));

        return RenderSimpleCondition(condition);
    }

    private static string RenderSimpleCondition(ConditionDef condition)
    {
        if (condition.Op == null) return "?";
        return condition.Op.Value switch
        {
            ConditionOp.ALLY_COUNT_GTE => $"you control {RenderCondValue(condition.Value)}+ allies",
            ConditionOp.ENEMY_COUNT_GTE => $"the enemy controls {RenderCondValue(condition.Value)}+ allies",
            ConditionOp.BARROW_COUNT_GTE => $"your barrow has {RenderCondValue(condition.Value)}+ cards",
            ConditionOp.HAND_COUNT_GTE => $"you have {RenderCondValue(condition.Value)}+ cards in hand",
            ConditionOp.HAND_COUNT_LTE => $"you have {RenderCondValue(condition.Value)}- cards in hand",
            ConditionOp.TURN_GTE => $"turn ≥ {RenderCondValue(condition.Value)}",
            ConditionOp.VIGOR_LTE => $"your vigor ≤ {RenderCondValue(condition.Value)}",
            ConditionOp.VIGOR_GTE => $"your vigor ≥ {RenderCondValue(condition.Value)}",
            ConditionOp.ATTUNEMENT_GTE => $"you have {RenderCondValue(condition.Value)}+ attunement",
            ConditionOp.CONTROLS_KEYWORD => $"you control a creature with {RenderCondValue(condition.Value)}",
            ConditionOp.CONTROLS_STRATA => $"you control a {RenderCondValue(condition.Value)} creature",
            ConditionOp.DAMAGED_THIS_TURN => "a creature was damaged this turn",
            ConditionOp.ATTACKERS_THIS_TURN_GTE => $"you attacked {RenderCondValue(condition.Value)}+ times this turn",
            ConditionOp.ATTACKERS_THIS_TURN_EQ => $"you attacked exactly {RenderCondValue(condition.Value)} times this turn",
            ConditionOp.SPELLS_CAST_THIS_TURN_GTE => $"you cast {RenderCondValue(condition.Value)}+ spells this turn",
            ConditionOp.SPELLS_CAST_THIS_TURN_EQ => $"you cast exactly {RenderCondValue(condition.Value)} spells this turn",
            ConditionOp.NO_ATTACKERS_LAST_TURN => "you didn't attack on your last turn",
            ConditionOp.CREATURE_DIED_THIS_TURN => RenderCreatureDiedThisTurn(condition),
            _ => "?"
        };
    }

    private static string RenderCondValue(JsonElement? element)
    {
        if (element == null) return "?";
        var el = element.Value;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
            return n.ToString();
        if (el.ValueKind == JsonValueKind.String)
            return el.GetString() ?? "?";
        if (el.ValueKind == JsonValueKind.True)
            return "";
        if (el.ValueKind == JsonValueKind.False)
            return "";
        return "?";
    }

    private static string RenderCreatureDiedThisTurn(ConditionDef condition)
    {
        string side = condition.Side?.ToUpperInvariant() ?? "ANY";
        string value = RenderCondValue(condition.Value);
        return side switch
        {
            "ALLY" => "a friendly creature died this turn",
            "ENEMY" => "an enemy creature died this turn",
            _ => string.IsNullOrEmpty(value) || value == "?" ? "a creature died this turn" : $"{value}+ creatures died this turn"
        };
    }

    /// <summary>
    /// Format a keyword constant to display form.
    /// </summary>
    public static string FormatKeyword(string keyword) => keyword switch
    {
        "GUARD" => "Guard",
        "SWIFT" => "Swift",
        "PIERCE" => "Pierce",
        "WARD" => "Ward",
        "VENOM" => "Venom",
        "REACH" => "Reach",
        "ROOTED" => "Rooted",
        "UNEARTH" => "Unearth",
        "ECHO" => "Echo",
        "FRAGILE" => "Fragile",
        "SEALED" => "Sealed",
        _ => keyword
    };
}
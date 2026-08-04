using System.Text.RegularExpressions;
using Runewake.Engine.Cards;

namespace Runewake.Sim;

/// <summary>
/// Validates a <see cref="CardDef"/> against the game rules and schema constraints.
/// Produces a list of error strings, empty if the card is valid.
/// </summary>
public static class CardValidator
{
    private static readonly Regex IdPattern = new(@"^[a-z]{3}_[a-z]_[a-z0-9_]+$", RegexOptions.Compiled);

    private static readonly HashSet<string> ValidKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "GUARD", "SWIFT", "PIERCE", "WARD", "VENOM", "REACH",
        "ROOTED", "UNEARTH", "ECHO", "FRAGILE", "SEALED"
    };

    private static readonly HashSet<string> ValidTriggers = new(StringComparer.OrdinalIgnoreCase)
    {
        "ON_SUMMON", "ON_DEATH", "ON_ATTACK", "ON_DAMAGED",
        "ON_TURN_START", "ON_TURN_END", "ON_CAST_RITUAL", "ON_EXCAVATE",
        "ON_RELIC_IDENTIFY", "ON_ALLY_DEATH", "ON_LANE_VACATED",
        "PASSIVE", "ACTIVATED", "RESOLVE"
    };

    private static readonly HashSet<string> ValidOps = new(StringComparer.OrdinalIgnoreCase)
    {
        "DAMAGE", "HEAL", "BUFF", "DEBUFF", "DESTROY", "DRAW", "DISCARD",
        "EXCAVATE", "BURY", "UNBURY", "SUMMON", "GRANT_KEY", "REMOVE_KEY",
        "SILENCE", "BOUNCE", "ATTUNE", "MOVE_LANE", "IDENTIFY",
        "GAIN_VIGOR", "LOSE_VIGOR", "COPY", "SET_STAT", "REFRESH"
    };

    private static readonly HashSet<string> ValidScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELF", "ALLY_CREATURE", "ENEMY_CREATURE", "ANY_CREATURE",
        "PLAYER_SELF", "PLAYER_ENEMY", "LANE", "NONE"
    };

    private static readonly HashSet<string> ValidDurations = new(StringComparer.OrdinalIgnoreCase)
    {
        "PERMANENT", "THIS_TURN", "NEXT_TURN", "WHILE_PRESENT"
    };

    private static readonly HashSet<string> ValidCondOps = new(StringComparer.OrdinalIgnoreCase)
    {
        "ALLY_COUNT_GTE", "ENEMY_COUNT_GTE", "BARROW_COUNT_GTE",
        "HAND_COUNT_GTE", "HAND_COUNT_LTE", "TURN_GTE", "VIGOR_LTE",
        "VIGOR_GTE", "ATTUNEMENT_GTE", "CONTROLS_KEYWORD",
        "CONTROLS_STRATA", "DAMAGED_THIS_TURN", "RITUALS_CAST_GTE"
    };

    /// <summary>
    /// Validates a card definition. Returns an empty list if the card is valid.
    /// </summary>
    public static List<string> Validate(CardDef card)
    {
        var errors = new List<string>();

        // Required fields
        if (string.IsNullOrWhiteSpace(card.Id))
            errors.Add("id is required");
        else if (!IdPattern.IsMatch(card.Id))
            errors.Add($"id '{card.Id}' does not match pattern ^[a-z]{{3}}_[a-z]_[a-z0-9_]+$");

        if (string.IsNullOrWhiteSpace(card.Set))
            errors.Add("set is required");

        if (string.IsNullOrWhiteSpace(card.Name))
            errors.Add("name is required");
        else if (card.Name.Length < 2 || card.Name.Length > 40)
            errors.Add($"name must be 2-40 characters (got {card.Name.Length})");

        if (!Enum.IsDefined(card.Strata))
            errors.Add($"invalid strata: {card.Strata}");

        if (!Enum.IsDefined(card.Type))
            errors.Add($"invalid card type: {card.Type}");

        if (!Enum.IsDefined(card.Rarity))
            errors.Add($"invalid rarity: {card.Rarity}");

        // Cost
        if (card.Cost < 0 || card.Cost > 10)
            errors.Add($"cost must be 0-10 (got {card.Cost})");

        // Type-specific checks
        if (card.Type == CardType.CREATURE)
        {
            if (card.Attack is null)
                errors.Add("CREATURE requires attack");
            else if (card.Attack < 0 || card.Attack > 12)
                errors.Add($"attack must be 0-12 (got {card.Attack})");

            if (card.Vigor is null)
                errors.Add("CREATURE requires vigor");
            else if (card.Vigor < 1 || card.Vigor > 14)
                errors.Add($"vigor must be 1-14 (got {card.Vigor})");
        }
        else if (card.Type == CardType.RITUAL)
        {
            if (card.Attack is not null)
                errors.Add("RITUAL should not have attack");
            if (card.Vigor is not null)
                errors.Add("RITUAL should not have vigor");
            if (card.IdentifyCondition is not null)
                errors.Add("RITUAL should not have identify_condition");
        }
        else if (card.Type == CardType.RELIC)
        {
            if (card.IdentifyCondition is null)
                errors.Add("RELIC requires identify_condition");
            if (card.Attack is not null)
                errors.Add("RELIC should not have attack in definition (set to 0 at runtime)");
            if (card.Vigor is not null)
                errors.Add("RELIC should not have vigor in definition (set to 3 at runtime)");
        }

        // Content version
        if (card.ContentVersion < 1)
            errors.Add($"content_version must be >= 1 (got {card.ContentVersion})");

        // Power score
        if (card.PowerScore is not null && card.PowerScore < 0)
            errors.Add($"power_score must be >= 0 (got {card.PowerScore})");

        // Flavor
        if (card.Flavor?.Length > 140)
            errors.Add($"flavor must be <= 140 characters (got {card.Flavor.Length})");

        // Keywords
        if (card.Keywords.Count > 3)
            errors.Add($"max 3 keywords (got {card.Keywords.Count})");
        foreach (var kw in card.Keywords)
        {
            if (!ValidKeywords.Contains(kw))
                errors.Add($"unknown keyword: '{kw}'");
        }

        // Abilities
        if (card.Abilities.Count > 2)
            errors.Add($"max 2 abilities (got {card.Abilities.Count})");
        foreach (var ability in card.Abilities)
        {
            ValidateAbility(ability, errors);
        }

        return errors;
    }

    private static void ValidateAbility(AbilityDef ability, List<string> errors)
    {
        if (!ValidTriggers.Contains(ability.Trigger.ToString()))
            errors.Add($"unknown trigger: '{ability.Trigger}'");

        if (ability.ActivationCost is not null && (ability.ActivationCost < 0 || ability.ActivationCost > 6))
            errors.Add($"activation_cost must be 0-6 (got {ability.ActivationCost})");

        if (ability.Effects.Count < 1 || ability.Effects.Count > 2)
            errors.Add($"abilities must have 1-2 effects (got {ability.Effects.Count})");

        foreach (var effect in ability.Effects)
        {
            ValidateEffect(effect, errors);
        }
    }

    private static void ValidateEffect(EffectDef effect, List<string> errors)
    {
        if (!ValidOps.Contains(effect.Op.ToString()))
            errors.Add($"unknown effect op: '{effect.Op}'");

        if (effect.Target is not null)
        {
            if (!ValidScopes.Contains(effect.Target.Scope.ToString()))
                errors.Add($"unknown target scope: '{effect.Target.Scope}'");
        }

        if (effect.Amount is not null && (effect.Amount < -12 || effect.Amount > 12))
            errors.Add($"amount must be -12 to 12 (got {effect.Amount})");

        if (effect.Attack is not null && (effect.Attack < -12 || effect.Attack > 12))
            errors.Add($"attack modifier must be -12 to 12 (got {effect.Attack})");

        if (effect.Vigor is not null && (effect.Vigor < -12 || effect.Vigor > 12))
            errors.Add($"vigor modifier must be -12 to 12 (got {effect.Vigor})");

        if (effect.Keyword is not null && !ValidKeywords.Contains(effect.Keyword))
            errors.Add($"unknown keyword in effect: '{effect.Keyword}'");

        if (effect.Duration is not null && !ValidDurations.Contains(effect.Duration.ToString()))
            errors.Add($"unknown duration: '{effect.Duration}'");
    }
}
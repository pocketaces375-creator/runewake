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

        // FABLE-DROP-1: creature types and Tribute
        if (card.Types.Count > 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendJoin(" ", card.Types.Select(t => Title(t)));
        }
        if (card.Tribute is int trib && trib > 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append($"Tribute {trib} (destroy {trib} of your creatures to play this)");
        }

        // Identify condition for relics
        if (card.IdentifyCondition != null)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append("⛭ Identify: ");
            sb.Append(RenderCondition(card.IdentifyCondition));
        }

        // Abilities — FABLE-054: an artifact with printed text shows exactly that
        if (!string.IsNullOrEmpty(card.PrintedText))
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(card.PrintedText);
        }
        else
            foreach (var ability in card.Abilities)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(RenderAbility(ability));
            }

        // FABLE-DROP-1: rituals are aimed by the lane they are played on
        if (card.Type == CardType.RITUAL && card.Abilities.Any(a => a.Effects.Any(IsAimed)))
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append("Play it on a lane to aim it.");
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
    /// FABLE-054: an artifact as a card for any card view (rules slab, Deck Forge inspector). Its printed
    /// text is used word for word when it has one; older artifacts fall back to rendering their abilities.
    /// </summary>
    public static CardDef ArtifactAsCard(ArtifactDef a)
    {
        var card = new CardDef { Id = a.Id, Name = a.Name, Type = CardType.ARTIFACT, Flavor = a.Flavor, PrintedText = a.Text, Abilities = new List<AbilityDef>() };
        if (a.Passive is { } p && !(p.Op == Op.HEAL && p.Target?.Scope == Scope.NONE))
            card.Abilities.Add(new AbilityDef { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { p } });
        if (a.Trigger is { Effects.Count: > 0 } t) card.Abilities.Add(t);
        if (a.ExtraTriggers != null) card.Abilities.AddRange(a.ExtraTriggers);
        return card;
    }

    /// <summary>
    /// Render only the ability text portion of a card (no stats, keywords, flavor, or identify).
    /// </summary>
    public static string RenderAbilityTextOnly(CardDef card)
    {
        if (!string.IsNullOrEmpty(card.PrintedText)) return card.PrintedText!;
        var sb = new StringBuilder();
        foreach (var ability in card.Abilities)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(RenderAbility(ability));
        }
        return sb.ToString();
    }

    /// <summary>FABLE-DROP-1: a single-target creature effect with no positional rule — the ritual's aim picks it.</summary>
    public static bool IsAimed(EffectDef e) =>
        e.Target is { } t && t.Scope is Scope.ENEMY_CREATURE or Scope.ALLY_CREATURE or Scope.ANY_CREATURE
        && (t.Count is null || (!t.Count.Value.IsAll && t.Count.Value.Value <= 1))
        && (string.IsNullOrEmpty(t.Filter) || t.Filter is "ANY" or "CHOSEN")
        || e.Target is { Scope: Scope.LANE } lt && (lt.Filter is null or "OPPOSING");

    // ——— Ability rendering ———

    private static string RenderAbility(AbilityDef ability)
    {
        string condition = ability.Condition != null
            ? $"if {RenderCondition(ability.Condition)}, "
            : "";

        // FABLE-DROP-1: every sentence starts with a capital ("the enemy discards 1" → "The enemy…"),
        // except the first one straight after a condition ("if …, give …")
        var sentences = ability.Effects.Select(RenderEffect).Select(Cap).ToList();
        if (condition.Length > 0 && sentences.Count > 0)
            sentences[0] = Uncap(sentences[0]);
        string effects = string.Join(". ", sentences);

        // RESOLVE — no prefix (rituals)
        if (ability.Trigger == Trigger.RESOLVE)
            return Cap(condition + effects);

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
        Trigger.ON_ALLY_ATTACKED => "When an ally is attacked",
        Trigger.ON_HEAL => "When you heal",
        Trigger.ON_CREATURE_DIES => "When a creature dies",
        _ => "?"
    };

    private static string DurationTail(Duration? d) => d switch
    {
        Duration.THIS_TURN => " this turn",
        Duration.UNTIL_YOUR_NEXT_TURN => " until your next turn",
        Duration.NEXT_TURN => " until the end of your next turn",
        _ => ""
    };

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    private static string Uncap(string s) => s.Length > 1 && char.IsUpper(s[0]) && !char.IsUpper(s[1]) ? char.ToLowerInvariant(s[0]) + s[1..] : s;

    private static string TribePlural(string tribe)
    {
        string t = Title(tribe);
        return t is "Undead" or "Starborn" ? t : t + "s";
    }

    private static string Title(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

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
                if (scope == Scope.PLAYER_SELF)
                    return $"You heal {amt}";   // FABLE-DROP-2: was "Heal 2 from you"
                string tgt = RenderTargetPhrase(target);
                return $"Heal {amt} from {tgt}";
            }

            case Op.BUFF:
            {
                string bonus = RenderStatBonus(effect.Attack, effect.Vigor, true);
                string tgt = RenderTargetPhrase(target);
                return $"Give {tgt} {bonus}{DurationTail(effect.Duration)}";
            }

            case Op.DEBUFF:
            {
                // FABLE-DROP-1: a debuff always takes away, whatever sign the data uses
                string bonus = RenderStatBonus(effect.Attack is int a ? -Math.Abs(a) : null,
                    effect.Vigor is int v ? -Math.Abs(v) : null, false);
                string tgt = RenderTargetPhrase(target);
                return $"Give {tgt} {bonus}{DurationTail(effect.Duration)}";
            }

            // ——— FABLE-DROP-1 ———
            case Op.STUN:
                return target?.Count is { IsAll: true } or { Value: > 1 }
                    ? $"Stun {RenderTargetPhrase(target)} (they can't attack on their next turn)"
                    : $"Stun {RenderTargetPhrase(target)} (it can't attack on its next turn)";
            case Op.BURN:
                return scope == Scope.PLAYER_ENEMY
                    ? $"Burn the enemy for {effect.Amount ?? 1} (it fades by 1 each turn)"
                    : $"Give {RenderTargetPhrase(target)} Burn {effect.Amount ?? 1} (damage at the start of each of its turns, fading by 1)";
            case Op.DRAIN:
                return $"The enemy has {effect.Amount ?? 1} less Attunement next turn";
            case Op.LOCK_LANE:
            {
                int n = effect.Amount ?? 1;
                string which = target?.Filter switch
                {
                    "ENEMY_EMPTY" => "every empty enemy lane",
                    "ENEMY_ANY" => "every enemy lane",
                    _ => "the opposing enemy lane"
                };
                return $"Lock {which} for {n} turn{(n == 1 ? "" : "s")} (nothing can be summoned there)";
            }
            case Op.SWAP_STATS:
                return $"Swap the Attack and Vigor of {RenderTargetPhrase(target)}";
            case Op.DOUBLE_STATS:
                return $"Double the Attack and Vigor of {RenderTargetPhrase(target)}{(effect.Duration == Duration.PERMANENT ? "" : " this turn")}";
            case Op.STEAL:
                return effect.Duration == Duration.THIS_TURN
                    ? $"Take control of {RenderTargetPhrase(target)} until end of turn. It can attack"
                    : $"Take control of {RenderTargetPhrase(target)}";
            case Op.SET_TRAP:
                return (effect.Keyword ?? "COUNTER_RITUAL").ToUpperInvariant() == "AMBUSH"
                    ? $"Set a Sigil: the next enemy creature to attack takes {effect.Amount ?? 0} damage first"
                    : "Set a Sigil: counter the next enemy Ritual";
            case Op.REDIRECT:
                return target?.Filter is string rf && rf.StartsWith("TRIBE:") && !rf.Contains('+')
                    ? $"The next enemy attack hits the {Title(rf[6..])} instead"
                    : $"The next enemy attack hits {RenderTargetPhrase(target)} instead";
            case Op.UNEARTH_FROM_GRAVEYARD:
                return effect.Value is > 0
                    ? $"Return your strongest dead creature costing {effect.Value} or less to play"
                    : "Return your strongest dead creature to play";
            case Op.HEAL_FULL:
                return $"Fully heal {RenderTargetPhrase(target)}";
            case Op.SUPPRESS:
                return $"Suppress the enemy's artifacts for {effect.Amount ?? 1} turn{((effect.Amount ?? 1) == 1 ? "" : "s")}";

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
                return $"Grant {tgt} {FormatKeyword(key.ToUpperInvariant())}{DurationTail(effect.Duration)}";
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

            case Op.SCY:
            {
                string amt = effect.Amount?.ToString() ?? "2";
                return $"Scry {amt}";
            }

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
            "FIRST_CREATURE_EACH_TURN" => "", // handled by frequency phrase below
            _ => ""
        };

        string freq = effect.Filter?.ToUpperInvariant() switch
        {
            "FIRST_SPELL_EACH_TURN" => "Your first spell each turn",
            "FIRST_CREATURE_EACH_TURN" => "Your first creature each turn",
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

        // Filter adjective (FABLE-DROP-1: filters chain with "+"; lanes read as "in lanes 2–4" after the noun)
        var parts = string.IsNullOrEmpty(target.Filter) ? new List<string>() : target.Filter.Split('+').Select(f => f.Trim()).ToList();
        string lanesTail = string.Concat(parts.Where(f => f.StartsWith("LANES:")).Select(f =>
        {
            var r = LaneRange(f[6..]);
            return r.Contains('–') ? $" in lanes {r}" : $" in lane {r}";
        }));
        parts.RemoveAll(f => f.StartsWith("LANES:"));
        // FABLE-DROP-2: "enemy creatures with 2 or less attack" reads better than "attack ≤ 2 enemy creatures"
        lanesTail += string.Concat(parts.Where(f => f.StartsWith("ATTACK_LTE:")).Select(f => $" with {f[11..]} or less attack"));
        parts.RemoveAll(f => f.StartsWith("ATTACK_LTE:"));
        string adjective = string.Concat(parts.Where(f => !f.StartsWith("TRIBE:") && f != "OTHER").Select(FilterAdjective));
        bool other = parts.Contains("OTHER");
        string? tribe = parts.FirstOrDefault(f => f.StartsWith("TRIBE:"))?[6..];

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
        }

        // FABLE-DROP-1: "your other Undead", "your adjacent Beasts" read better than "all beast ally creatures"
        if (tribe is not null && plural && target.Count!.Value.IsAll)
            return (scope == Scope.ENEMY_CREATURE ? "enemy " : scope == Scope.ALLY_CREATURE ? "your " : "all ")
                + (other ? "other " : "") + adjective + TribePlural(tribe) + lanesTail;
        if (tribe is not null)
            adjective = (other ? "other " : "") + adjective + Title(tribe) + " ";
        else if (other)
            adjective = adjective + "other ";   // "strongest other ally creature"

        string noun = plural ? baseNoun + "s" : baseNoun;
        return countPrefix + adjective + noun + lanesTail;
    }

    private static string FilterAdjective(string filter) => filter switch
    {
                "ANY" => "",
                "DAMAGED" => "damaged ",
                "ADJACENT" => "adjacent ",
                "EXHAUSTED" => "exhausted ",
                "CHOSEN" => "chosen ",
                "FIRST_ATTACKER" => "first attacker ",
                "SECOND_ATTACKER" => "second attacker ",
                "CURRENT_ATTACKER" => "attacking ",
                // FABLE-031: artifact filters used to print raw ("has_not_attacked ally creatures").
                "HAS_NOT_ATTACKED" => "unattacked ",
                "FIRST_ATTACKED" => "first-attacked ",
                "MOST_WOUNDED" => "most wounded ",
                var s when s.StartsWith("ATTACK_LTE:") => $"attack ≤ {s[11..]} ",
                // FABLE-DROP-1
                "OPPOSING" => "opposing ",
                "DIAGONAL" => "diagonal ",
                "OPPOSING_AND_DIAGONAL" => "opposing and diagonal ",
                "LEFT" => "left-hand ",
                "RIGHT" => "right-hand ",
                "OTHER" => "other ",
                "STUNNED" => "stunned ",
                "LAST_ATTACKED" => "attacked ",
                "BURNING" => "burning ",
                "RANDOM" => "random ",
                "HIGHEST_ATTACK" => "strongest ",
                "LOWEST_VIGOR" => "weakest ",
                "HIGHEST_VIGOR" => "toughest ",
                "UNDAMAGED" => "undamaged ",
                "SAME_LANE" => "same-lane ",
                "EDGE_LANE" => "edge-lane ",
                "CENTER_LANE" => "center-lane ",
                var s when s.StartsWith("TRIBE:") => Title(s[6..]) + " ",
                var s when s.StartsWith("KEYWORD:") => FormatKeyword(s[8..]) + " ",
                var s when s.StartsWith("LANES:") => $"lane {LaneRange(s[6..])} ",
                _ => filter.ToLowerInvariant() + " "
    };

    private static string LaneRange(string r)
    {
        var p = r.Split('-');
        int lo = int.TryParse(p[0], out var a) ? a + 1 : 1;
        int hi = p.Length > 1 && int.TryParse(p[1], out var b) ? b + 1 : lo;
        return lo == hi ? $"{lo}" : $"{lo}–{hi}";
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
            ConditionOp.CONTROLS_TRIBE_GTE => $"you control {Math.Max(1, ParseInt(condition.Value))}+ {Title(condition.Tribe ?? "?")}s",
            ConditionOp.ALONE => "it's your only creature",
            ConditionOp.ENEMY_HAND_LTE => $"the enemy has {RenderCondValue(condition.Value)} or fewer cards in hand",
            ConditionOp.DURING_YOUR_TURN => "it's your turn",
            _ => "?"
        };
    }

    private static int ParseInt(JsonElement? el) => el is JsonElement e && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) ? n : 0;

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
    /// FABLE-SKILLS-1: what a keyword DOES, in one plain sentence — the single source for
    /// every place a card is shown (duel slab, Reliquary page, Deck Forge card view).
    /// Written from the engine's behaviour and docs/01_GAME_RULES.md §8, so the words can't
    /// drift from the rules again (the duel slab used to say Reach hits "any lane" and Venom
    /// deals "1 extra damage"). Empty for an unknown keyword.
    /// </summary>
    public static string KeywordReminder(string keyword, CardType cardType = CardType.CREATURE)
    {
        var k = (keyword ?? "").ToUpperInvariant();
        if (k.StartsWith("ARMOR:")) return $"Takes {k[6..]} less damage from every hit.";
        if (k.StartsWith("DODGE:")) return $"{k[6..]}% chance to take no combat damage.";
        if (k.StartsWith("UNEARTH:")) return $"When it dies, it comes back to your hand next turn, costing {k[8..]}.";
        return k switch
        {
            "GUARD" => "While it's on the board, enemies must attack its lane.",
            "SWIFT" => "Can attack the turn it's played.",
            "PIERCE" => "When it destroys a creature in combat, the leftover damage hits the enemy player.",
            "WARD" => "The first damage it would take is blocked.",
            "VENOM" => "Any creature it damages is destroyed.",
            "REACH" => "Can attack the lane opposite it or either lane beside that one.",
            "ROOTED" => "Can't attack.",
            "UNEARTH" => "When it dies, it comes back to your hand at the start of your next turn (once).",
            "ECHO" => cardType == CardType.RITUAL
                ? "Its effect happens twice."
                : "Its \"when this enters play\" effect happens twice.",
            "FRAGILE" => "Destroyed at the end of the turn it was played.",
            "SEALED" => "Enemy abilities can't target it.",
            "EXALTED" => "When your first attacker each turn attacks, it gets +1/+1 this turn for each Exalted creature you control.",
            "ANCESTRAL_SHIELD" => "Once per turn, an enemy spell can't take an ally below 1 Vigor.",
            "STEALTH_STRIKE" => "Takes no damage back when it attacks.",
            _ => "",
        };
    }

    /// <summary>
    /// "Reach: Can attack…" — one line per keyword on the card, then one per game term its
    /// abilities use that isn't plain English (Excavate, Bury, Burn, Stun, Sigil, Tribute…), for
    /// any card view.
    /// </summary>
    public static List<string> KeywordReminderLines(CardDef card)
    {
        var lines = new List<string>();
        foreach (var kw in card.Keywords)
        {
            var r = KeywordReminder(kw, card.Type);
            lines.Add(string.IsNullOrEmpty(r) ? FormatKeyword(kw) : $"{FormatKeyword(kw)}: {r}");
        }
        var terms = new List<string>();
        if (card.Tribute is > 0) terms.Add("Tribute: to play this, destroy that many of your creatures (play it onto one to choose which).");
        foreach (var a in card.Abilities)
            foreach (var e in a.Effects)
            {
                string? t = e.Op switch
                {
                    Op.EXCAVATE => "Excavate: look at the top cards of your deck, put one in your hand and bury the rest.",
                    Op.BURY => "Bury: put cards from the top of your deck face down in your Barrow, where some cards can bring them back.",
                    Op.BURN => "Burn: takes that much damage at the start of its controller's turn, then the Burn drops by 1.",
                    Op.DRAIN => "Drain: that player has that much less Attunement next turn.",
                    Op.STUN => "Stun: can't attack until the end of its controller's next turn.",
                    Op.LOCK_LANE => "Lock: nothing can be played into that lane for a few turns.",
                    Op.SET_TRAP => "Sigil: a hidden trap. Counter stops the next enemy Ritual; Ambush hits the next enemy attacker first.",
                    _ => null,
                };
                if (t != null && !terms.Contains(t)) terms.Add(t);
            }
        lines.AddRange(terms);
        return lines;
    }

    /// <summary>
    /// FABLE-054: the reminder lines for an artifact — every keyword it hands out and every term it uses
    /// (Burn, Stun, Sigil, Suppressed, Charges…), so the Deck Forge can explain it in full.
    /// </summary>
    public static List<string> ArtifactReminderLines(ArtifactDef a)
    {
        var all = new List<AbilityDef>();
        if (a.Trigger != null) all.Add(a.Trigger);
        if (a.ExtraTriggers != null) all.AddRange(a.ExtraTriggers);
        if (a.FullCharge is { Count: > 0 }) all.Add(new AbilityDef { Trigger = Trigger.ON_CHARGE_FULL, Effects = a.FullCharge });
        var effects = all.SelectMany(x => x.Effects).ToList();
        var card = new CardDef
        {
            Type = CardType.CREATURE, Abilities = all,
            Keywords = effects.Where(e => e.Op == Op.GRANT_KEY && !string.IsNullOrEmpty(e.Keyword)).Select(e => e.Keyword!.ToUpperInvariant()).Distinct().ToList(),
        };
        // keywords the printed text names without handing them out ("Guard creatures heal 2", "a Squire with Guard")
        foreach (var kw in new[] { "GUARD", "WARD", "VENOM", "PIERCE", "SWIFT", "REACH", "EXALTED" })
            if (!card.Keywords.Contains(kw) && (a.Text ?? "").Contains(FormatKeyword(kw))) card.Keywords.Add(kw);
        var lines = KeywordReminderLines(card);
        void Add(string t) { if (!lines.Contains(t)) lines.Add(t); }
        if (a.Charges is { Max: > 0 }) Add("Charges: the artifact fills up as it says. When it is full, the effect happens and it starts again from empty.");
        foreach (var e in effects)
            switch (e.Op)
            {
                case Op.SUPPRESS: Add("Suppressed: an artifact that does nothing at all for that long."); break;
                case Op.BOUNCE: Add("Return to hand: the creature leaves the board and goes back to its owner's hand, to be played again."); break;
                case Op.SWAP_STATS: Add("Swap: its Attack and Vigor trade places."); break;
                case Op.STEAL: Add("Take control: the creature fights for you until the end of the turn, then goes home."); break;
                case Op.REFRESH: Add("Attack again: a creature that already attacked this turn is ready to attack once more."); break;
                case Op.UNEARTH_FROM_GRAVEYARD: Add("Rises again: the creature comes back from your discard pile into an empty lane."); break;
            }
        return lines;
    }

    /// <summary>
    /// Format a keyword constant to display form.
    /// </summary>
    public static string FormatKeyword(string keyword) => keyword switch
    {
        // FABLE-DROP-1
        var k when k.StartsWith("ARMOR:") => "Armor " + k[6..],
        var k when k.StartsWith("DODGE:") => "Dodge " + k[6..] + "%",
        "EXALTED" => "Exalted",
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
        "ANCESTRAL_SHIELD" => "Ancestral Shield",
        "STEALTH_STRIKE" => "Stealth Strike",
        _ => keyword
    };
}
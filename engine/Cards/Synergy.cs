namespace Runewake.Engine.Cards;

/// <summary>
/// FABLE-DROP-1: cards sort themselves (docs/CLASS_IDENTITY.md §1). Every card gets mechanic tags derived
/// from its keywords, types and effects — nobody hand-sorts — and each class lists the mechanics it loves.
/// A card whose tags overlap a class's list is on that class's "High synergy" shelf, best first. Any class
/// can still play any card.
/// </summary>
public static class Synergy
{
    /// <summary>The classes' love-lists (CLASS_IDENTITY.md §2).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Loves = new Dictionary<string, string[]>
    {
        ["warrior"] = new[] { "ATTACK_BUFF", "EXALTED", "DOUBLE_STATS", "HEAVY_DAMAGE", "HASTE", "ENRAGE", "EXTRA_ATTACK" },
        ["battlemage"] = new[] { "BOUNCE", "SUPPRESS", "COUNTER", "SPELL_TRIGGERED", "DRAW", "REDIRECT", "SPELL_DAMAGE" },
        ["necromancer"] = new[] { "RESURRECT", "TOKENS", "TRIBUTE", "ON_DEATH", "VENOM", "BURN", "TRIBAL_UNDEAD" },
        ["paladin"] = new[] { "GUARD", "DEFENSE_BUFF", "PREVENT", "NEGATE", "STALL", "HEAL", "TRIBAL_KNIGHT" },
        ["druid"] = new[] { "HEAL", "ADJACENT_BUFF", "SPATIAL_BUFF", "RAMP", "TRIBAL_BEAST" },
        ["rogue"] = new[] { "SUPPRESS", "DODGE", "VENOM", "TAKE_CONTROL", "DISCARD", "HASTE" },
        ["astrologist"] = new[] { "LANE_LOCK", "TRIBUTE", "DIAGONAL", "SPATIAL_DEBUFF", "STUN", "FLIP", "ATTUNE_DOWN", "TRIBAL_STARBORN" },
    };

    public static IEnumerable<string> ClassIds => Loves.Keys;

    /// <summary>The mechanic tags of a card.</summary>
    public static HashSet<string> Tags(CardDef card)
    {
        var tags = new HashSet<string>();
        foreach (var raw in card.Keywords)
        {
            var k = raw.ToUpperInvariant();
            if (k == "SWIFT") tags.Add("HASTE");
            else if (k == "GUARD") tags.Add("GUARD");
            else if (k == "VENOM") tags.Add("VENOM");
            else if (k == "WARD") tags.Add("PREVENT");
            else if (k == "ROOTED") tags.Add("STALL");
            else if (k == "UNEARTH") tags.Add("RESURRECT");
            else if (k == "EXALTED") tags.Add("EXALTED");
            else if (k.StartsWith("ARMOR:")) tags.Add("NEGATE");
            else if (k.StartsWith("DODGE:")) tags.Add("DODGE");
        }
        foreach (var t in card.Types)
            tags.Add("TRIBAL_" + t.ToUpperInvariant());
        if (card.Tribute is > 0) tags.Add("TRIBUTE");

        foreach (var a in card.Abilities)
        {
            // FABLE-DROP-2: what a condition asks for says who the card is for
            foreach (var c in new[] { a.Condition }.Concat(a.Condition?.All ?? new()).Concat(a.Condition?.Any ?? new()))
            {
                if (c?.Op == ConditionOp.ALONE) tags.Add("EXALTED");
                if (c?.Op == ConditionOp.ENEMY_HAND_LTE) tags.Add("DISCARD");
                if (c?.Op is ConditionOp.SPELLS_CAST_THIS_TURN_EQ or ConditionOp.SPELLS_CAST_THIS_TURN_GTE) tags.Add("SPELL_TRIGGERED");
            }
            if (a.Trigger == Trigger.ON_DEATH || a.Trigger == Trigger.ON_ALLY_DEATH) tags.Add("ON_DEATH");
            if (a.Trigger == Trigger.ON_CAST_RITUAL) tags.Add("SPELL_TRIGGERED");
            // FABLE-DROP-2
            if (a.Trigger == Trigger.ON_DAMAGED) tags.Add("ENRAGE");
            if (a.Trigger == Trigger.ON_CREATURE_DIES) tags.Add("ON_DEATH");
            if (a.Trigger == Trigger.ON_HEAL) tags.Add("HEAL");
            foreach (var e in a.Effects)
            {
                string f = e.Target?.Filter ?? "";
                bool ally = e.Target?.Scope is Scope.ALLY_CREATURE or Scope.SELF;
                switch (e.Op)
                {
                    case Op.BUFF:
                        if ((e.Attack ?? 0) >= 2 || (ally && (e.Attack ?? 0) >= 1 && (e.Vigor ?? 0) == 0)) tags.Add("ATTACK_BUFF");
                        if ((e.Vigor ?? 0) >= 1) tags.Add("DEFENSE_BUFF");
                        if (f.Contains("ADJACENT")) tags.Add("ADJACENT_BUFF");
                        if (f.Contains("LANES:")) tags.Add("SPATIAL_BUFF");
                        if (f.Contains("TRIBE:")) tags.Add("TRIBAL_" + TribeOf(f));
                        break;
                    case Op.DEBUFF:
                        if (e.Target?.Scope == Scope.ENEMY_CREATURE || f.Contains("LANES:") || f.Contains("OPPOSING") || f.Contains("DIAGONAL")) tags.Add("SPATIAL_DEBUFF");
                        if (f.Contains("DIAGONAL")) tags.Add("DIAGONAL");
                        break;
                    case Op.DAMAGE:
                        if ((e.Amount ?? 0) >= 4 || (e.Target?.Count is { IsAll: true } && (e.Amount ?? 0) >= 2)) tags.Add("HEAVY_DAMAGE");
                        else if (card.Type == CardType.RITUAL && e.Target?.Scope is Scope.ENEMY_CREATURE or Scope.PLAYER_ENEMY) tags.Add("SPELL_DAMAGE");
                        if (f.Contains("DIAGONAL")) tags.Add("DIAGONAL");
                        break;
                    case Op.DOUBLE_STATS: tags.Add("DOUBLE_STATS"); tags.Add("ATTACK_BUFF"); break;
                    case Op.BOUNCE: tags.Add("BOUNCE"); break;
                    case Op.REFRESH: tags.Add("EXTRA_ATTACK"); break;
                    case Op.SCY: tags.Add("DRAW"); break;
                    case Op.DESTROY: if (ally) tags.Add("TRIBUTE"); else tags.Add("HEAVY_DAMAGE"); break;
                    case Op.SUPPRESS: tags.Add("SUPPRESS"); break;
                    case Op.SET_TRAP: tags.Add("COUNTER"); break;
                    case Op.DRAW: if (e.Target?.Scope != Scope.PLAYER_ENEMY) tags.Add("DRAW"); break;
                    case Op.REDIRECT: tags.Add("REDIRECT"); break;
                    case Op.UNEARTH_FROM_GRAVEYARD: case Op.UNBURY: tags.Add("RESURRECT"); break;
                    case Op.SUMMON: case Op.REVIVE_TOKEN:
                        tags.Add("TOKENS");
                        if (e.TokenId is string tok && CardRegistry.Get(tok) is CardDef td)
                            foreach (var tt in td.Types) tags.Add("TRIBAL_" + tt.ToUpperInvariant());
                        break;
                    case Op.BURN: tags.Add("BURN"); break;
                    case Op.PREVENT_DAMAGE: tags.Add("PREVENT"); break;
                    case Op.HEAL: case Op.HEAL_FULL: tags.Add("HEAL"); break;
                    case Op.ATTUNE: tags.Add("RAMP"); break;
                    case Op.STEAL: tags.Add("TAKE_CONTROL"); break;
                    case Op.DISCARD: if (e.Target?.Scope == Scope.PLAYER_ENEMY) tags.Add("DISCARD"); break;
                    case Op.LOCK_LANE: tags.Add("LANE_LOCK"); break;
                    case Op.STUN: tags.Add("STUN"); if (f.Contains("DIAGONAL")) tags.Add("DIAGONAL"); break;
                    case Op.SWAP_STATS: tags.Add("FLIP"); break;
                    case Op.DRAIN: tags.Add("ATTUNE_DOWN"); break;
                    case Op.GRANT_KEY:
                        var gk = (e.Keyword ?? "").ToUpperInvariant();
                        if (gk.StartsWith("ARMOR:")) tags.Add("NEGATE");
                        else if (gk.StartsWith("DODGE:")) tags.Add("DODGE");
                        else if (gk == "WARD") tags.Add("PREVENT");
                        else if (gk == "GUARD") tags.Add("GUARD");
                        else if (gk == "SWIFT") tags.Add("HASTE");
                        else if (gk == "VENOM") tags.Add("VENOM");
                        else if (gk == "PIERCE") tags.Add("ATTACK_BUFF");
                        break;
                }
            }
        }
        return tags;
    }

    private static string TribeOf(string filter)
    {
        foreach (var part in filter.Split('+'))
            if (part.StartsWith("TRIBE:")) return part[6..].ToUpperInvariant();
        return "";
    }

    /// <summary>How well a card fits a class: the number of shared tags (+0.5 for the class's stratum).</summary>
    public static double Score(string classId, CardDef card, Strata? classStrata = null)
    {
        if (!Loves.TryGetValue(classId.ToLowerInvariant(), out var loves)) return 0;
        var tags = Tags(card);
        double score = loves.Count(tags.Contains);
        if (score > 0 && classStrata is Strata s && card.Strata == s) score += 0.5;
        return score;
    }

    /// <summary>The class's High-synergy shelf: every playable card that fits, best first.</summary>
    public static List<CardDef> Shelf(string classId, IEnumerable<CardDef> cards, Strata? classStrata = null) =>
        cards.Where(c => c.Type is not (CardType.TOKEN or CardType.ARTIFACT))
             .Select(c => (c, s: Score(classId, c, classStrata)))
             .Where(x => x.s > 0)
             .OrderByDescending(x => x.s).ThenBy(x => x.c.Cost).ThenBy(x => x.c.Name, StringComparer.Ordinal)
             .Select(x => x.c)
             .ToList();
}

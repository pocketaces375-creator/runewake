using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Engine.Engine;

/// <summary>
/// Pure resolver that converts a <see cref="TargetDef"/> into a concrete
/// list of <see cref="ResolvedTarget"/> within the given game context.
/// Handles scope selection, filter narrowing, and count limiting.
/// </summary>
public static class TargetResolver
{
    /// <summary>
    /// Resolve a target definition against the current game state.
    /// </summary>
    /// <param name="target">The target definition from the effect.</param>
    /// <param name="source">The card instance that owns the effect.</param>
    /// <param name="sourcePlayer">The player who controls the source card.</param>
    /// <param name="opponent">The opposing player.</param>
    /// <param name="state">The current game state.</param>
    /// <returns>Ordered list of resolved targets, already limited by count.</returns>
    public static List<ResolvedTarget> Resolve(
        TargetDef target,
        CardInstance source,
        PlayerState sourcePlayer,
        PlayerState opponent,
        GameState state)
    {
        // Build candidate pool from scope
        var pool = target.Scope switch
        {
            Scope.SELF => new List<ResolvedTarget>
            {
                new CreatureTarget(source, sourcePlayer.Index, source.LaneIndex ?? 0)
            },
            Scope.ALLY_CREATURE => GetCreaturesOnBoard(sourcePlayer),
            Scope.ENEMY_CREATURE => GetCreaturesOnBoard(opponent),
            Scope.ANY_CREATURE => GetAllCreaturesOnBoard(sourcePlayer, opponent),
            Scope.PLAYER_SELF => new List<ResolvedTarget> { new PlayerTarget(sourcePlayer) },
            Scope.PLAYER_ENEMY => new List<ResolvedTarget> { new PlayerTarget(opponent) },
            Scope.LANE => new List<ResolvedTarget>(), // Not directly resolved as a target type for effects
            Scope.NONE => new List<ResolvedTarget>(),
            _ => new List<ResolvedTarget>()
        };

        // Apply filter
        if (!string.IsNullOrEmpty(target.Filter) && target.Filter != "ANY")
            pool = ApplyFilter(pool, target.Filter, source);

        // Apply count
        pool = ApplyCount(pool, target.Count);

        return pool;
    }

    // ——— Pool builders ———

    private static List<ResolvedTarget> GetCreaturesOnBoard(PlayerState player)
    {
        var list = new List<ResolvedTarget>();
        for (int i = 0; i < 5; i++)
        {
            var occ = player.Lanes[i].Occupant;
            if (occ is not null)
                list.Add(new CreatureTarget(occ, player.Index, i));
        }
        return list;
    }

    private static List<ResolvedTarget> GetAllCreaturesOnBoard(PlayerState p1, PlayerState p2)
    {
        var list = GetCreaturesOnBoard(p1);
        list.AddRange(GetCreaturesOnBoard(p2));
        return list;
    }

    // ——— Filters ———

    private static List<ResolvedTarget> ApplyFilter(
        List<ResolvedTarget> pool,
        string filter,
        CardInstance source)
    {
        // Positional filters require knowing the source lane
        int? srcLane = source.LaneIndex;

        return filter switch
        {
            "ADJACENT" => pool.Where(t => t is CreatureTarget ct && srcLane is not null
                && System.Math.Abs(ct.LaneIndex - srcLane.Value) == 1).ToList(),
            "OPPOSING" => pool.Where(t => t is CreatureTarget ct
                && ct.LaneIndex == (source.LaneIndex ?? 0)).ToList(),
            "SAME_LANE" => pool.Where(t => t is CreatureTarget ct
                && ct.LaneIndex == (source.LaneIndex ?? -1)).ToList(),
            "EDGE_LANE" => pool.Where(t => t is CreatureTarget ct
                && (ct.LaneIndex == 0 || ct.LaneIndex == 4)).ToList(),
            "CENTER_LANE" => pool.Where(t => t is CreatureTarget ct
                && ct.LaneIndex == 2).ToList(),
            "DAMAGED" => pool.Where(t => t is CreatureTarget ct && ct.Card.Damage > 0).ToList(),
            "UNDAMAGED" => pool.Where(t => t is CreatureTarget ct && ct.Card.Damage == 0).ToList(),
            var s when s.StartsWith("STRATA:") => pool.Where(t =>
            {
                var strataStr = s[7..];
                return t is CreatureTarget ct && ct.Card.Strata.ToString() == strataStr;
            }).ToList(),
            var s when s.StartsWith("KEYWORD:") => pool.Where(t =>
            {
                var kw = s[8..];
                return t is CreatureTarget ct && ct.Card.EffectiveKeywords.Contains(kw);
            }).ToList(),
            var s when s.StartsWith("TYPE:") => pool.Where(t =>
            {
                var typeStr = s[5..];
                return t is CreatureTarget ct && ct.Card.CardType.ToString() == typeStr;
            }).ToList(),
            "RANDOM" => pool, // Ordering done later, select top N
            "LOWEST_VIGOR" => pool.OrderBy(t => t is CreatureTarget ct ? ct.Card.CurrentVigor : 0).ToList(),
            "HIGHEST_ATTACK" => pool.OrderByDescending(t => t is CreatureTarget ct ? ct.Card.CurrentAttack : 0).ToList(),
            "LOWEST_COST" => pool.OrderBy(t => t is CreatureTarget ct ? ct.Card.Cost : 0).ToList(),
            "HIGHEST_COST" => pool.OrderByDescending(t => t is CreatureTarget ct ? ct.Card.Cost : 0).ToList(),
            "CHOSEN" => pool, // Player would choose; engine selects first valid
            _ => pool
        };
    }

    // ——— Count ———

    private static List<ResolvedTarget> ApplyCount(
        List<ResolvedTarget> pool,
        TargetCount? count)
    {
        if (count is null)
            return pool.Take(1).ToList(); // default to 1

        var c = count.Value;
        if (c.IsAll)
            return pool;

        return pool.Take(Math.Max(1, c.Value)).ToList();
    }
}
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Engine.Engine;

/// <summary>
/// Deterministic trigger bus for firing card abilities on game events.
/// Ordering: current player's creatures first (by lane index 0-4),
/// then opponent's creatures (by lane index 0-4).
/// Trigger chain depth is capped at 20 to prevent infinite loops.
/// </summary>
public static class TriggerBus
{
    /// <summary>
    /// Fire ON_DEATH triggers for a specific creature that just died.
    /// Unlike Fire(), this directly uses the creature's own abilities
    /// since it's no longer on the board.
    /// </summary>
    public static void FireDeathEvents(GameState state, CardInstance deadCard, int controller)
    {
        if (state.TriggerDepth >= MaxTriggerDepth)
            return;

        var opponent = state.Player(state.OpponentIndex(controller));

        foreach (var ability in deadCard.Abilities)
        {
            if (ability.Trigger != Trigger.ON_DEATH)
                continue;

            if (state.TriggerDepth >= MaxTriggerDepth)
                return;

            if (!ConditionMet(ability.Condition, deadCard, controller, state))
                continue;

            state.TriggerDepth++;

            foreach (var effect in ability.Effects)
            {
                var targets = TargetResolver.Resolve(
                    effect.Target ?? new TargetDef { Scope = Scope.NONE },
                    deadCard,
                    state.Player(controller),
                    opponent,
                    state);
                EffectExecutor.Execute(effect, deadCard, state, targets);
            }
        }
    }

    /// <summary>
    /// Public entry point for evaluating a condition on a card for a player.
    /// Used by the engine for relic identification checks.
    /// </summary>
    public static bool EvaluateCondition(ConditionDef? condition, CardInstance source, int controller, GameState state)
        => ConditionMet(condition, source, controller, state);

    /// <summary>
    /// Maximum depth for nested trigger chains. Hard stop at 20.
    /// </summary>
    public const int MaxTriggerDepth = 20;

    /// <summary>
    /// Fire all abilities matching the given trigger type.
    /// </summary>
    /// <param name="state">The game state (already cloned by caller).</param>
    /// <param name="trigger">The trigger event type.</param>
    /// <param name="eventPlayerIndex">The player who caused the event (or whose turn it is).</param>
    public static void Fire(GameState state, Trigger trigger, int eventPlayerIndex)
    {
        // Collect all matching abilities from creatures on the board
        var pending = CollectAbilities(state, trigger, eventPlayerIndex);

        foreach (var (ability, source, controller, laneIdx) in pending)
        {
            // Check trigger depth
            if (state.TriggerDepth >= MaxTriggerDepth)
                return; // hard stop

            // Check condition
            if (!ConditionMet(ability.Condition, source, controller, state))
                continue;

            state.TriggerDepth++;

            // Resolve targets for each effect and execute
            var opponent = state.Player(state.OpponentIndex(controller));
            foreach (var effect in ability.Effects)
            {
                var targets = TargetResolver.Resolve(
                    effect.Target ?? new TargetDef { Scope = Scope.NONE },
                    source,
                    state.Player(controller),
                    opponent,
                    state);
                EffectExecutor.Execute(effect, source, state, targets);
            }
        }
    }

    /// <summary>
    /// Collect all abilities matching a trigger from creatures on the board,
    /// ordered: event player's creatures first (lane 0-4), then the other player's (lane 0-4).
    /// </summary>
    private static List<(AbilityDef ability, CardInstance source, int controller, int laneIndex)> CollectAbilities(
        GameState state, Trigger trigger, int eventPlayerIndex)
    {
        var result = new List<(AbilityDef, CardInstance, int, int)>();

        int otherPlayer = state.OpponentIndex(eventPlayerIndex);

        // Event player's creatures first
        CollectFromPlayer(state.Player(eventPlayerIndex), trigger, result);
        // Then the other player's
        CollectFromPlayer(state.Player(otherPlayer), trigger, result);

        return result;
    }

    private static void CollectFromPlayer(
        PlayerState player, Trigger trigger,
        List<(AbilityDef, CardInstance, int, int)> result)
    {
        for (int i = 0; i < 5; i++)
        {
            var occ = player.Lanes[i].Occupant;
            if (occ is null) continue;

            foreach (var ability in occ.Abilities)
            {
                if (ability.Trigger == trigger)
                {
                    result.Add((ability, occ, player.Index, i));
                }
            }
        }

        // Also collect from rune tokens (off-board, lane -1)
        foreach (var token in player.RuneTokens)
        {
            foreach (var ability in token.Abilities)
            {
                if (ability.Trigger == trigger)
                {
                    result.Add((ability, token, player.Index, -1));
                }
            }
        }
    }

    /// <summary>
    /// Evaluate a condition against the current game state.
    /// </summary>
    private static bool ConditionMet(ConditionDef? condition, CardInstance source, int controller, GameState state)
    {
        if (condition is null) return true;

        // Compound conditions
        if (condition.All is { Count: > 0 } all)
            return all.All(c => ConditionMet(c, source, controller, state));
        if (condition.Any is { Count: > 0 } any)
            return any.Any(c => ConditionMet(c, source, controller, state));

        // Single condition
        if (condition.Op is null) return true;

        var player = state.Player(controller);
        var opponent = state.Player(state.OpponentIndex(controller));

        int actual = condition.Op switch
        {
            ConditionOp.ALLY_COUNT_GTE => CountCreaturesOnBoard(player),
            ConditionOp.ENEMY_COUNT_GTE => CountCreaturesOnBoard(opponent),
            ConditionOp.BARROW_COUNT_GTE => player.Barrow.Count,
            ConditionOp.HAND_COUNT_GTE => player.Hand.Count,
            ConditionOp.HAND_COUNT_LTE => player.Hand.Count,
            ConditionOp.TURN_GTE => state.TurnNumber,
            ConditionOp.VIGOR_LTE => player.Vigor,
            ConditionOp.VIGOR_GTE => player.Vigor,
            ConditionOp.ATTUNEMENT_GTE => player.AttunementMax,
            ConditionOp.CONTROLS_KEYWORD => HasAnyCreatureWithKeyword(player, condition.Value?.GetString() ?? "") ? 1 : 0,
            ConditionOp.CONTROLS_STRATA => HasAnyCreatureWithStrata(player, condition.Value?.GetString() ?? "") ? 1 : 0,
            ConditionOp.DAMAGED_THIS_TURN => player.Vigor < player.MaxVigor ? 1 : 0,
            ConditionOp.RITUALS_CAST_GTE => 0, // Not tracked yet — stub
            _ => 0
        };

        int threshold = condition.Value?.GetInt32() ?? 0;

        return condition.Op switch
        {
            ConditionOp.ALLY_COUNT_GTE => actual >= threshold,
            ConditionOp.ENEMY_COUNT_GTE => actual >= threshold,
            ConditionOp.BARROW_COUNT_GTE => actual >= threshold,
            ConditionOp.HAND_COUNT_GTE => actual >= threshold,
            ConditionOp.HAND_COUNT_LTE => actual <= threshold,
            ConditionOp.TURN_GTE => actual >= threshold,
            ConditionOp.VIGOR_LTE => actual <= threshold,
            ConditionOp.VIGOR_GTE => actual >= threshold,
            ConditionOp.ATTUNEMENT_GTE => actual >= threshold,
            ConditionOp.CONTROLS_KEYWORD => actual >= 1,
            ConditionOp.CONTROLS_STRATA => actual >= 1,
            ConditionOp.DAMAGED_THIS_TURN => actual >= threshold,
            ConditionOp.RITUALS_CAST_GTE => actual >= threshold,
            _ => true
        };
    }

    private static int CountCreaturesOnBoard(PlayerState player)
    {
        int count = 0;
        for (int i = 0; i < 5; i++)
            if (player.Lanes[i].Occupant is not null) count++;
        return count;
    }

    private static bool HasAnyCreatureWithKeyword(PlayerState player, string keyword)
    {
        for (int i = 0; i < 5; i++)
            if (player.Lanes[i].Occupant?.EffectiveKeywords.Contains(keyword) == true)
                return true;
        return false;
    }

    private static bool HasAnyCreatureWithStrata(PlayerState player, string strata)
    {
        for (int i = 0; i < 5; i++)
            if (player.Lanes[i].Occupant?.Strata.ToString() == strata)
                return true;
        return false;
    }
}
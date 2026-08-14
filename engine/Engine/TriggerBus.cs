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
    /// Fire matching abilities of ONE artifact slot's occupant only.
    /// Used for per-artifact events like ON_CHARGE_FULL and ON_CHARGE_GAINED
    /// where the event belongs to a specific artifact (G6: each player's
    /// Charges/triggers are their own — the opponent's mirror copy must NOT
    /// fire when yours fills).
    /// </summary>
    /// <param name="state">The game state (already cloned by caller).</param>
    /// <param name="trigger">The trigger event type.</param>
    /// <param name="controller">The player who controls the artifact slot.</param>
    /// <param name="slotIndex">The slot index of the artifact.</param>
    public static void FireArtifactSlot(GameState state, Trigger trigger, int controller, int slotIndex)
    {
        var player = state.Player(controller);
        if (slotIndex < 0 || slotIndex >= player.ArtifactSlots.Length)
            return;
        var slot = player.ArtifactSlots[slotIndex];
        if (slot.Occupant is null || slot.IsSuppressed)
            return;

        foreach (var ability in slot.Occupant.Abilities)
        {
            if (ability.Trigger != trigger)
                continue;
            if (state.TriggerDepth >= MaxTriggerDepth)
                return;
            if (!ConditionMet(ability.Condition, slot.Occupant, controller, state))
                continue;

            state.TriggerDepth++;
            var opponent = state.Player(state.OpponentIndex(controller));
            foreach (var effect in ability.Effects)
            {
                var targets = TargetResolver.Resolve(
                    effect.Target ?? new TargetDef { Scope = Scope.NONE },
                    slot.Occupant,
                    player,
                    opponent,
                    state);
                EffectExecutor.Execute(effect, slot.Occupant, state, targets);
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

        // Also collect from Artifact slots (off-board, lane -1)
        // Suppressed Artifacts do NOT contribute passives or triggers
        foreach (var slot in player.ArtifactSlots)
        {
            if (slot.Occupant is null || slot.IsSuppressed) continue;

            var artCard = slot.Occupant;
            foreach (var ability in artCard.Abilities)
            {
                if (ability.Trigger == trigger)
                {
                    result.Add((ability, artCard, player.Index, -1));
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
            // Artifact conditions
            ConditionOp.ATTACKERS_THIS_TURN_GTE => player.AttackCountThisTurn,
            ConditionOp.ATTACKERS_THIS_TURN_EQ => player.AttackCountThisTurn,
            ConditionOp.SPELLS_CAST_THIS_TURN_GTE => player.SpellCastCountThisTurn,
            ConditionOp.SPELLS_CAST_THIS_TURN_EQ => player.SpellCastCountThisTurn,
            ConditionOp.NO_ATTACKERS_LAST_TURN => player.AttackCountLastTurn == 0 ? 1 : 0,
            ConditionOp.CREATURE_DIED_THIS_TURN => CreatureDiedThisTurnCount(condition, player, opponent, state),
            ConditionOp.FEWER_ALLY_CREATURES_THAN_ENEMY => CountCreaturesOnBoard(player) < CountCreaturesOnBoard(opponent) ? 1 : 0,
            ConditionOp.ALLY_CREATURE_EXISTS => CountCreaturesOnBoard(player) >= 1 ? 1 : 0,
            ConditionOp.PARTNER_CHARGES_GTE => PartnerCharges(source, player),
            ConditionOp.DURING_YOUR_TURN => state.CurrentPlayerIndex == controller ? 1 : 0,
            ConditionOp.NTH_ATTACKER_ON_PREY_THIS_TURN => player.PreyAttackCountThisTurn,
            ConditionOp.FRIENDLY => state.LastDeathPlayerIndex == controller ? 1 : 0,
            ConditionOp.ENEMY => state.LastDeathPlayerIndex != controller ? 1 : 0,
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
            // Artifact conditions
            ConditionOp.ATTACKERS_THIS_TURN_GTE => actual >= threshold,
            ConditionOp.ATTACKERS_THIS_TURN_EQ => actual == threshold,
            ConditionOp.SPELLS_CAST_THIS_TURN_GTE => actual >= threshold,
            ConditionOp.SPELLS_CAST_THIS_TURN_EQ => actual == threshold,
            ConditionOp.NO_ATTACKERS_LAST_TURN => actual >= 1,
            ConditionOp.CREATURE_DIED_THIS_TURN => actual >= Math.Max(1, threshold),
            ConditionOp.FEWER_ALLY_CREATURES_THAN_ENEMY => actual >= 1,
            ConditionOp.ALLY_CREATURE_EXISTS => actual >= 1,
            ConditionOp.PARTNER_CHARGES_GTE => actual >= threshold,
            ConditionOp.DURING_YOUR_TURN => actual >= 1,
            ConditionOp.NTH_ATTACKER_ON_PREY_THIS_TURN => actual >= threshold,
            ConditionOp.FRIENDLY => actual >= 1,
            ConditionOp.ENEMY => actual >= 1,
            _ => true
        };
    }

    /// <summary>
    /// Side-aware death count for CREATURE_DIED_THIS_TURN.
    /// Side "ALLY" = creatures controlled by the condition's player that died this turn,
    /// "ENEMY" = the opponent's, anything else (or null) = both sides (G7 default).
    /// </summary>
    private static int CreatureDiedThisTurnCount(ConditionDef condition, PlayerState player, PlayerState opponent, GameState state)
    {
        string side = condition.Side?.ToUpperInvariant() ?? "ANY";
        return side switch
        {
            "ALLY" => state.CreatureDiedThisTurnCount[player.Index],
            "ENEMY" => state.CreatureDiedThisTurnCount[opponent.Index],
            _ => state.CreatureDiedThisTurnCount[0] + state.CreatureDiedThisTurnCount[1]
        };
    }

    private static int PartnerCharges(CardInstance source, PlayerState player)
    {
        if (source == null || player.ArtifactSlots.Length == 0)
            return 0;
        // Find the slot this Artifact occupies (matching card def id), then return the partner slot's charges
        for (int i = 0; i < player.ArtifactSlots.Length; i++)
        {
            var slot = player.ArtifactSlots[i];
            if (slot.Occupant?.CardDefId == source.CardDefId)
            {
                int partner = i == 0 ? 1 : 0;
                if (partner < player.ArtifactSlots.Length)
                    return player.ArtifactSlots[partner].Charges;
                return 0;
            }
        }
        // If source isn't found in slots (shouldn't happen), use the first non-suppressed slot's partner
        return 0;
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
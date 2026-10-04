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

        // FABLE-DROP-1: "whenever an ally dies" — the dead creature's side (it is already off the board)
        if (!state.IsGameOver)
            FireSide(state, Trigger.ON_ALLY_DEATH, controller);
    }

    /// <summary>
    /// FABLE-DROP-1: run one ability of one source (depth cap + condition), resolving each effect's targets.
    /// </summary>
    public static void RunAbility(GameState state, AbilityDef ability, CardInstance source, int controller)
    {
        if (state.TriggerDepth >= MaxTriggerDepth)
            return;
        if (!ConditionMet(ability.Condition, source, controller, state))
            return;
        state.TriggerDepth++;
        var opponent = state.Player(state.OpponentIndex(controller));
        foreach (var effect in ability.Effects)
        {
            if (state.IsGameOver) return;
            var targets = TargetResolver.Resolve(
                effect.Target ?? new TargetDef { Scope = Scope.NONE },
                source,
                state.Player(controller),
                opponent,
                state);
            EffectExecutor.Execute(effect, source, state, targets);
        }
    }

    /// <summary>
    /// FABLE-DROP-1: an event that belongs to ONE card (it entered play, it attacked). Its own abilities
    /// fire — twice for ON_SUMMON with Echo — and then the off-board listeners (artifacts, rune tokens)
    /// that watch for that event. Other creatures on the board do NOT fire: before this, summoning any
    /// creature re-ran the ON_SUMMON of every creature already in play.
    /// </summary>
    public static void FireCardEvent(GameState state, Trigger trigger, CardInstance card, int controller, bool listenersBothSides)
    {
        int times = trigger == Trigger.ON_SUMMON && card.EffectiveKeywords.Contains("ECHO") ? 2 : 1;
        for (int t = 0; t < times; t++)
            foreach (var ability in card.Abilities.ToList())
                if (ability.Trigger == trigger)
                    RunAbility(state, ability, card, controller);

        var listeners = new List<(AbilityDef, CardInstance, int, int)>();
        CollectListeners(state.Player(controller), trigger, listeners);
        if (listenersBothSides)
            CollectListeners(state.Player(state.OpponentIndex(controller)), trigger, listeners);
        foreach (var (ability, source, ctl, _) in listeners)
            RunAbility(state, ability, source, ctl);
    }

    /// <summary>
    /// FABLE-DROP-2: "when this takes damage" — a creature that was hurt and is still standing. (The trigger was in
    /// the card language from the start and never fired.)
    /// </summary>
    public static void FireDamaged(GameState state, CardInstance card)
    {
        if (state.IsGameOver || card.Zone != Zone.Lane || card.CurrentVigor <= 0) return;
        if (!card.Abilities.Any(a => a.Trigger == Trigger.ON_DAMAGED)) return;
        FireCardEvent(state, Trigger.ON_DAMAGED, card, card.Controller, listenersBothSides: false);
    }

    /// <summary>
    /// FABLE-DROP-1: an event that belongs to one player's side ("whenever you cast a ritual", "whenever
    /// one of your creatures dies"): that player's creatures, rune tokens and artifacts only.
    /// </summary>
    public static void FireSide(GameState state, Trigger trigger, int playerIndex)
    {
        var pending = new List<(AbilityDef, CardInstance, int, int)>();
        CollectFromPlayer(state.Player(playerIndex), trigger, pending);
        foreach (var (ability, source, ctl, _) in pending)
        {
            if (state.IsGameOver) return;
            // a creature that left the board since the list was made doesn't act
            if ((source.CardType == CardType.CREATURE || source.CardType == CardType.TOKEN || source.CardType == CardType.RELIC)
                && source.Zone != Zone.Lane)
                continue;
            RunAbility(state, ability, source, ctl);
        }
    }

    private static void CollectListeners(PlayerState player, Trigger trigger, List<(AbilityDef, CardInstance, int, int)> result)
    {
        foreach (var token in player.RuneTokens)
            foreach (var ability in token.Abilities)
                if (ability.Trigger == trigger)
                    result.Add((ability, token, player.Index, -1));
        foreach (var slot in player.ArtifactSlots)
        {
            if (slot.Occupant is null || slot.IsSuppressed) continue;
            foreach (var ability in slot.Occupant.Abilities)
                if (ability.Trigger == trigger)
                    result.Add((ability, slot.Occupant, player.Index, -1));
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
            if (state.IsGameOver)
                return;
            // FABLE-DROP-1: a creature destroyed by an earlier trigger in this same list doesn't act
            if (laneIdx >= 0 && source.Zone != Zone.Lane)
                continue;

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
        // Then the other player's (but not for ON_TURN_START — that's per-player,
        // only the player whose turn it is should have their turn-start abilities fire)
        // FABLE-DROP-1: a CREATURE's "at the end of your turn" fired at the end of BOTH turns before. Now only
        // the active player's creatures; artifacts and runes on both sides still fire (ruling G1: active first).
        if (trigger == Trigger.ON_TURN_END)
            CollectListeners(state.Player(otherPlayer), trigger, result);
        else if (trigger != Trigger.ON_TURN_START)
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
            // an unidentified relic is a face-down 0/3: its abilities are not online yet (rules §9)
            if (occ.CardType == CardType.RELIC && !occ.IsIdentified) continue;

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
            // FABLE-DROP-1
            ConditionOp.CONTROLS_TRIBE_GTE => Enumerable.Range(0, 5).Count(i =>
                player.Lanes[i].Occupant?.Types.Contains((condition.Tribe ?? "").ToUpperInvariant()) == true),
            ConditionOp.ALONE => CountCreaturesOnBoard(player) == 1 ? 1 : 0,
            ConditionOp.ENEMY_HAND_LTE => opponent.Hand.Count,
            _ => 0
        };

        // FABLE-DROP-1: "value": true (a flag condition) used to crash with "requires an element of type Number"
        int threshold = condition.Value is System.Text.Json.JsonElement v
            ? v.ValueKind switch
            {
                System.Text.Json.JsonValueKind.Number => v.TryGetInt32(out int n) ? n : 0,
                System.Text.Json.JsonValueKind.True => 1,
                System.Text.Json.JsonValueKind.String => int.TryParse(v.GetString(), out int sn) ? sn : 0,
                _ => 0
            }
            : 0;

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
            ConditionOp.CONTROLS_TRIBE_GTE => actual >= Math.Max(1, threshold),
            ConditionOp.ALONE => actual >= 1,
            ConditionOp.ENEMY_HAND_LTE => actual <= threshold,
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
            if (player.Lanes[i].Occupant?.EffectiveKeywords.Any(k => k == keyword || k.StartsWith(keyword + ":")) == true)
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
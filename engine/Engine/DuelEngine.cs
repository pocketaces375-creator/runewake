using Runewake.Engine.State;
using Runewake.Engine.Cards;

namespace Runewake.Engine.Engine;

/// <summary>
/// The pure deterministic duel engine.
/// P1: <c>Engine.Apply(GameState, GameAction) -> GameState</c>
/// Every action clones the state, applies the mutation, and returns the new state.
/// No I/O, no side effects, no static mutable state.
/// </summary>
public static partial class DuelEngine
{
    /// <summary>
    /// Applies a player action to the game state and returns the new state.
    /// The original state is never mutated.
    /// </summary>
    public static GameState Apply(GameState state, GameAction action)
    {
        state = state.Clone();
        state.ActionLog.Add(action);

        switch (action)
        {
            case EndTurnAction e:
                return ApplyEndTurn(state, e);
            case PlayCardAction p:
                return ApplyPlayCard(state, p);
            case AttackAction a:
                return ApplyAttack(state, a);
            default:
                throw new ArgumentException($"Unknown action type: {action.GetType()}");
        }
    }

    // ——— Action handlers ———

    private static GameState ApplyEndTurn(GameState state, EndTurnAction action)
    {
        var endingPlayer = state.Player(action.PlayerIndex);

        // THIS_TURN cost discounts expire when the owning player ends their turn
        // (a discount created during the enemy's turn — Aura — survives into the
        // owner's turn and is cleared when the owner ends it).
        endingPlayer.CostMods.RemoveAll(m => m.Duration == Duration.THIS_TURN);

        // 1. End phase — ON_TURN_END triggers, Fragile check, then hand size check
        TriggerBus.Fire(state, Trigger.ON_TURN_END, action.PlayerIndex);
        KeywordHandlers.ProcessFragile(endingPlayer);
        TruncateHand(endingPlayer);

        // Tick suppression on the ending player's Artifacts (counted in owner's turns)
        // But we also tick AFTER triggers so ON_ARTIFACT_UNSUPPRESS can fire correctly
        TickArtifactSuppression(endingPlayer);

        // 2. Switch to next player
        state.CurrentPlayerIndex = state.OpponentIndex(action.PlayerIndex);
        if (state.CurrentPlayerIndex == 0)
            state.TurnNumber++;

        // 2.5 Refresh phase — ready all of the next player's creatures
        // Per rules §5-6: creatures summoned on a previous turn are Ready
        // at the start of your turn. Clear exhaustion, attack flags,
        // and summoned-this-turn markers.
        var nextPlayer = state.CurrentPlayer;
        foreach (var lane in nextPlayer.Lanes)
        {
            if (lane.Occupant is CardInstance creature)
            {
                creature.IsExhausted = false;
                creature.HasAttackedThisTurn = false;
                creature.SummonedThisTurn = false;
            }
        }

        // 3. Attune phase — increase attunement and refill
        nextPlayer = state.CurrentPlayer;
        int newMax = Math.Min(
            nextPlayer.AttunementMax + nextPlayer.AttunementPerTurn,
            10);
        nextPlayer.AttunementMax = newMax;
        nextPlayer.Attunement = newMax;

        // 4. Draw phase
        bool firstPlayerSkipsDraw =
            state.CurrentPlayerIndex == 0
            && state.TurnNumber == 1;

        if (!firstPlayerSkipsDraw)
            ExecuteDraw(nextPlayer, state);

        // 5. Start triggers — Unearth processing + ON_TURN_START triggers + relic identification
        KeywordHandlers.ProcessUnearth(nextPlayer);
        TriggerBus.Fire(state, Trigger.ON_TURN_START, state.CurrentPlayerIndex);
        IdentifyRelics(state, nextPlayer);

        // 6. Per-turn tracking reset for the current player
        nextPlayer.AttackCountLastTurn = nextPlayer.AttackCountThisTurn;
        nextPlayer.AttackCountThisTurn = 0;
        nextPlayer.SpellCastCountThisTurn = 0;
        nextPlayer.HasAttackedThisTurn = false;
        nextPlayer.SpellCastThisTurn = false;
        nextPlayer.PreyAttackCountThisTurn = 0;
        nextPlayer.FirstAttackerLaneIndex = null;
        nextPlayer.FirstAttackedLaneIndex = null;
        state.CreatureDiedThisTurnCount[0] = 0;
        state.CreatureDiedThisTurnCount[1] = 0;
        // Reset damage-prevention shield usage counters (R5: resets at start of EVERY turn, both players).
        DamageInterceptor.ResetUsage(state);

        // 7. Apply Artifact passives for this turn (re-applied each turn, cleared if suppressed)
        // PASSIVE abilities with WHILE_PRESENT duration are refreshed each turn.
        // Suppressed Artifacts skip this — their buffs naturally expire.
        ApplyArtifactPassives(state, nextPlayer);

        return state;
    }

    private static GameState ApplyPlayCard(GameState state, PlayCardAction action)
    {
        var player = state.Player(action.PlayerIndex);
        var card = player.Hand.FirstOrDefault(c => c.InstanceId == action.CardInstanceId)
            ?? throw new ArgumentException($"Card instance {action.CardInstanceId} not found in hand.");

        if (card.Zone != Zone.Hand)
            throw new InvalidOperationException("Card is not in hand.");

        // COST_MOD discounts (the discount mechanic) reduce the effective cost
        // at play time — the engine charges the discounted amount (floor 0).
        int effectiveCost = CostInterceptor.GetEffectiveCost(state, card, action.PlayerIndex);

        if (player.Attunement < effectiveCost)
            throw new InvalidOperationException($"Not enough attunement: have {player.Attunement}, need {effectiveCost}.");

        player.Attunement -= effectiveCost;

        // Consume per-turn discount gates (FIRST_SPELL_EACH_TURN) after a successful play.
        CostInterceptor.ConsumePerTurnMods(state, card, action.PlayerIndex);

        // Track spell casting for Artifact conditions
        if (card.CardType == CardType.RITUAL)
        {
            player.SpellCastThisTurn = true;
            player.SpellCastCountThisTurn++;
        }

        player.Hand.Remove(card);
        card.Controller = action.PlayerIndex;

        if (card.CardType == CardType.CREATURE || card.CardType == CardType.RELIC)
        {
            if (action.LaneIndex is not int laneIdx || laneIdx < 0 || laneIdx > 4)
                throw new ArgumentException($"Invalid lane index: {action.LaneIndex}.");
            var lane = player.Lanes[laneIdx];
            if (lane.Occupant is not null)
                throw new InvalidOperationException($"Lane {laneIdx} is already occupied.");
            lane.Occupant = card;
            card.Zone = Zone.Lane;
            card.LaneIndex = laneIdx;

            if (card.CardType == CardType.CREATURE)
            {
                // Apply keyword effects: Swift, Ward, SummonedThisTurn, etc.
                KeywordHandlers.OnPlay(card);

                // Fire ON_SUMMON triggers (and any chained triggers, depth-limited)
                TriggerBus.Fire(state, Trigger.ON_SUMMON, action.PlayerIndex);
            }
            else if (card.CardType == CardType.RELIC)
            {
                // Relic enters as a 0/3 unidentified artifact
                card.BaseAttack = 0;
                card.BaseVigor = 3;
                card.IsIdentified = false;
                card.IsExhausted = true;
            }
        }
        else if (card.CardType == CardType.RITUAL)
        {
            // Resolve effects (no-op until P1-05), then discard
            card.Zone = Zone.Discard;
            player.Discard.Add(card);
        }

        return state;
    }

    /// <summary>
    /// Track attack counts for Artifact system conditions.
    /// </summary>
    private static GameState ApplyAttack(GameState state, AttackAction action)
    {
        var player = state.Player(action.PlayerIndex);
        var opponent = state.Player(state.OpponentIndex(action.PlayerIndex));

        var sourceLane = player.Lanes[action.SourceLane];
        var attacker = sourceLane.Occupant
            ?? throw new InvalidOperationException($"No creature in lane {action.SourceLane} to attack with.");

        // Validate Ready
        if (attacker.IsExhausted)
            throw new InvalidOperationException("Attacker is exhausted.");
        if (attacker.HasAttackedThisTurn)
            throw new InvalidOperationException("Attacker has already attacked this turn.");

        // Rooted cannot attack
        if (!KeywordHandlers.CanAttack(attacker))
            throw new InvalidOperationException("Attacker has Rooted and cannot attack.");

        // Resolve target lane (handles Reach targeting)
        int? resolvedTarget = KeywordHandlers.ResolveTargetLane(attacker, action.SourceLane, action.TargetLane);
        if (resolvedTarget is null)
            throw new InvalidOperationException("Invalid attack target.");

        // Track attack for Artifact conditions
        bool isFirstAttack = player.AttackCountThisTurn == 0;
        player.AttackCountThisTurn++;
        player.HasAttackedThisTurn = true;
        if (isFirstAttack)
            player.FirstAttackerLaneIndex = action.SourceLane;

        int targetLaneIdx = resolvedTarget.Value;

        // Determine final target: creature or face (with Guard redirect)
        var targetLane = opponent.Lanes[targetLaneIdx];
        int? actualTargetLaneIdx;

        if (targetLane.Occupant is not null)
        {
            // Occupied — fight the blocker
            actualTargetLaneIdx = targetLaneIdx;
        }
        else
        {
            // Empty opposing lane — check Guard
            var guardLane = FindGuardLane(opponent);
            if (guardLane is not null)
            {
                // Redirect to Guard lane
                actualTargetLaneIdx = guardLane;
            }
            else
            {
                // Face damage
                actualTargetLaneIdx = null;
            }
        }

        int attackPower = attacker.CurrentAttack;

        if (actualTargetLaneIdx is int tgtIdx)
        {
            var actualLane = opponent.Lanes[tgtIdx];
            var defender = actualLane.Occupant!;

            // Track first creature attacked on the defender's side (Bulwark FIRST_ATTACKED)
            if (opponent.FirstAttackedLaneIndex is null)
                opponent.FirstAttackedLaneIndex = tgtIdx;

            // Prey tracking: if the defender is this player's marked Prey, count the attack (Quiver R17)
            if (player.PreyTargetId == defender.InstanceId)
                player.PreyAttackCountThisTurn++;

            // Ward reduces attacker's damage to defender
            int damageToDefender = KeywordHandlers.ApplyWard(defender, attackPower);

            // Simultaneous damage (defender always hits back with full power)
            int atkDamage = defender.CurrentAttack;
            // Combat damage is intercepted by PREVENT_DAMAGE shields (source ATTACK).
            defender.Damage += DamageInterceptor.Reduce(state, defender, damageToDefender, DamageInterceptor.SourceAttack);
            attacker.Damage += DamageInterceptor.Reduce(state, attacker, atkDamage, DamageInterceptor.SourceAttack);

            // Venom marking
            KeywordHandlers.OnCombatDamageDealt(attacker, defender, damageToDefender);

            // Pierce: excess damage to defender carries to face
            bool defenderKilled = defender.CurrentVigor <= 0;
            if (defenderKilled && attacker.EffectiveKeywords.Contains("PIERCE"))
            {
                int neededToKill = defender.BaseVigor + defender.VigorModifier;
                int excessDamage = System.Math.Max(0, attackPower - neededToKill);
                excessDamage = DamageInterceptor.Reduce(state, opponent, excessDamage, DamageInterceptor.SourceAttack);
                opponent.Vigor -= excessDamage;
                CheckGameOver(state, opponent);
            }

            // Remove dead defender (check Unearth first)
            if (defenderKilled)
            {
                state.CreatureDiedThisTurnCount[opponent.Index]++;
                state.LastDeathPlayerIndex = opponent.Index;
                bool isUnearthed = false;
                if (!KeywordHandlers.OnDeath(defender, opponent))
                {
                    actualLane.Occupant = null;
                    defender.Zone = Zone.Discard;
                    opponent.Discard.Add(defender);
                }
                else
                {
                    actualLane.Occupant = null;
                    isUnearthed = true; // card is in UnearthQueue, not discard
                }
                // Fire ON_DEATH triggers
                TriggerBus.FireDeathEvents(state, defender, opponent.Index);
                TriggerBus.Fire(state, Trigger.ON_CREATURE_DIES, opponent.Index);
            }
        }
        else
        {
            // Face damage (intercepted by PREVENT_DAMAGE shields, source ATTACK).
            attackPower = DamageInterceptor.Reduce(state, opponent, attackPower, DamageInterceptor.SourceAttack);
            opponent.Vigor -= attackPower;
            CheckGameOver(state, opponent);
        }

        // Resolve Venom (destroy any creatures marked by Venom this combat)
        KeywordHandlers.ResolveVenom(state, action.PlayerIndex);

        // Remove dead attacker (check Unearth first)
        if (attacker.CurrentVigor <= 0)
        {
            state.CreatureDiedThisTurnCount[player.Index]++;
            state.LastDeathPlayerIndex = player.Index;
            if (!KeywordHandlers.OnDeath(attacker, player))
            {
                sourceLane.Occupant = null;
                attacker.Zone = Zone.Discard;
                player.Discard.Add(attacker);
            }
            else
            {
                sourceLane.Occupant = null;
            }
            TriggerBus.FireDeathEvents(state, attacker, player.Index);
            TriggerBus.Fire(state, Trigger.ON_CREATURE_DIES, player.Index);
        }
        else
        {
            // Mark attacker as used if it survived
            attacker.HasAttackedThisTurn = true;
            attacker.IsExhausted = true;
        }

        return state;
    }

    /// <summary>
    /// Find the first lane index (0–4) on the given player's board that holds
    /// a creature with the Guard keyword, or null if none exists.
    /// </summary>
    private static int? FindGuardLane(PlayerState player)
    {
        for (int i = 0; i < 5; i++)
        {
            var occ = player.Lanes[i].Occupant;
            if (occ is not null && occ.EffectiveKeywords.Contains("GUARD"))
                return i;
        }
        return null;
    }

    /// <summary>
    /// Checks if the given player's Vigor has reached 0 or below and sets game-over state.
    /// </summary>
    private static void CheckGameOver(GameState state, PlayerState player)
    {
        if (player.Vigor <= 0)
        {
            state.IsGameOver = true;
            state.WinnerIndex = state.OpponentIndex(player.Index);
        }
    }

    /// <summary>
    /// Check all relics belonging to the given player. Any that have an identify condition
    /// that is now met get flipped (IsIdentified = true) and fire ON_RELIC_IDENTIFY.
    /// </summary>
    private static void IdentifyRelics(GameState state, PlayerState player)
    {
        for (int i = 0; i < 5; i++)
        {
            var occ = player.Lanes[i].Occupant;
            if (occ is null || occ.CardType != CardType.RELIC || occ.IsIdentified)
                continue;

            if (occ.IdentifyCondition is not null &&
                TriggerBus.EvaluateCondition(occ.IdentifyCondition, occ, player.Index, state))
            {
                occ.IsIdentified = true;
                // Fire ON_RELIC_IDENTIFY triggers (the relic's own abilities come online)
                TriggerBus.Fire(state, Trigger.ON_RELIC_IDENTIFY, player.Index);
            }
        }
    }

    // ——— Phase helpers ———

    private static void ExecuteDraw(PlayerState player, State.GameState state)
    {
        if (player.Deck.Count > 0)
        {
            var drawn = player.Deck[0];
            player.Deck.RemoveAt(0);
            drawn.Zone = Zone.Hand;
            player.Hand.Add(drawn);
        }
        else
        {
            // Fatigue
            player.FatigueCounter++;
            player.Vigor -= player.FatigueCounter;
            if (player.Vigor <= 0)
            {
                state.IsGameOver = true;
                state.WinnerIndex = state.OpponentIndex(player.Index);
            }
        }
    }

    private static void TruncateHand(PlayerState player)
    {
        while (player.Hand.Count > player.MaxHandSize)
        {
            var discarded = player.Hand[^1];
            player.Hand.RemoveAt(player.Hand.Count - 1);
            discarded.Zone = Zone.Discard;
            player.Discard.Add(discarded);
        }
    }

    // ——— Artifact system helpers ———

    /// <summary>
    /// Apply Artifact PASSIVE abilities for the given player.
    /// Called at the start of each turn. Suppressed Artifacts don't apply theirs.
    /// Passives with WHILE_PRESENT duration are re-applied each turn so
    /// suppression naturally suspends them.
    /// </summary>
    private static void ApplyArtifactPassives(GameState state, PlayerState player)
    {
        foreach (var slot in player.ArtifactSlots)
        {
            if (slot.Occupant is null || slot.IsSuppressed)
                continue;

            foreach (var ability in slot.Occupant.Abilities)
            {
                if (ability.Trigger != Trigger.PASSIVE)
                    continue;

                var opponent = state.Player(state.OpponentIndex(player.Index));

                // Check ANY condition on the passive ability
                if (!TriggerBus.EvaluateCondition(ability.Condition, slot.Occupant, player.Index, state))
                    continue;

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
    }

    /// <summary>
    /// Tick suppression counters on the given player's Artifacts.
    /// Called at the end of the player's turn (counted in that player's turns).
    /// When suppression expires, fires ON_ARTIFACT_UNSUPPRESS triggers.
    /// </summary>
    private static void TickArtifactSuppression(PlayerState player)
    {
        foreach (var slot in player.ArtifactSlots)
        {
            if (slot.IsSuppressed)
            {
                slot.TickSuppression();
            }
        }
    }
}
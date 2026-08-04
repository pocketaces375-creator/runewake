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

        // 1. End phase — Fragile check, then hand size check
        KeywordHandlers.ProcessFragile(endingPlayer);
        TruncateHand(endingPlayer);

        // 2. Switch to next player
        state.CurrentPlayerIndex = state.OpponentIndex(action.PlayerIndex);
        if (state.CurrentPlayerIndex == 0)
            state.TurnNumber++;

        // 3. Attune phase — increase attunement and refill
        var nextPlayer = state.CurrentPlayer;
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

        // 5. Start triggers — Unearth processing + trigger bus stub
        KeywordHandlers.ProcessUnearth(nextPlayer);

        return state;
    }

    private static GameState ApplyPlayCard(GameState state, PlayCardAction action)
    {
        var player = state.Player(action.PlayerIndex);
        var card = player.Hand.FirstOrDefault(c => c.InstanceId == action.CardInstanceId)
            ?? throw new ArgumentException($"Card instance {action.CardInstanceId} not found in hand.");

        if (card.Zone != Zone.Hand)
            throw new InvalidOperationException("Card is not in hand.");

        if (player.Attunement < action.Cost)
            throw new InvalidOperationException($"Not enough attunement: have {player.Attunement}, need {action.Cost}.");

        player.Attunement -= action.Cost;

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

            // Ward reduces attacker's damage to defender
            int damageToDefender = KeywordHandlers.ApplyWard(defender, attackPower);

            // Simultaneous damage (defender always hits back with full power)
            int atkDamage = defender.CurrentAttack;
            defender.Damage += damageToDefender;
            attacker.Damage += atkDamage;

            // Venom marking
            KeywordHandlers.OnCombatDamageDealt(attacker, defender, damageToDefender);

            // Pierce: excess damage to defender carries to face
            bool defenderKilled = defender.CurrentVigor <= 0;
            if (defenderKilled && attacker.EffectiveKeywords.Contains("PIERCE"))
            {
                int neededToKill = defender.BaseVigor + defender.VigorModifier;
                int excessDamage = System.Math.Max(0, attackPower - neededToKill);
                opponent.Vigor -= excessDamage;
                CheckGameOver(state, opponent);
            }

            // Remove dead defender (check Unearth first)
            if (defenderKilled)
            {
                if (!KeywordHandlers.OnDeath(defender, opponent))
                {
                    actualLane.Occupant = null;
                    defender.Zone = Zone.Discard;
                    opponent.Discard.Add(defender);
                }
                else
                {
                    actualLane.Occupant = null; // removed from lane, now in UnearthQueue
                }
            }
        }
        else
        {
            // Face damage
            opponent.Vigor -= attackPower;
            CheckGameOver(state, opponent);
        }

        // Resolve Venom (destroy any creatures marked by Venom this combat)
        KeywordHandlers.ResolveVenom(state, action.PlayerIndex);

        // Remove dead attacker (check Unearth first)
        if (attacker.CurrentVigor <= 0)
        {
            if (!KeywordHandlers.OnDeath(attacker, player))
            {
                sourceLane.Occupant = null;
                attacker.Zone = Zone.Discard;
                player.Discard.Add(attacker);
            }
            else
            {
                sourceLane.Occupant = null; // removed from lane, now in UnearthQueue
            }
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
}
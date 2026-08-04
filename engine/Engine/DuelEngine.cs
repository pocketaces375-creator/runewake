using Runewake.Engine.State;

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
        var player = state.Player(action.PlayerIndex);

        // 1. End phase — hand size check
        TruncateHand(player);

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

        // 5. Start triggers (no-op until P1-06)

        return state;
    }

    private static GameState ApplyPlayCard(GameState state, PlayCardAction action)
    {
        // Stub — not tested in P1-02
        throw new NotImplementedException("PlayCard not implemented yet.");
    }

    private static GameState ApplyAttack(GameState state, AttackAction action)
    {
        // Stub — not tested in P1-02
        throw new NotImplementedException("Attack not implemented yet.");
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

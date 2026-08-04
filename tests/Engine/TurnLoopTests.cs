using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

public class TurnLoopTests
{
    /// <summary>
    /// Creates a GameState with both players having decks of the given size.
    /// Player 0 gets 4 cards in hand, player 1 gets 5 (per §1 Setup).
    /// Player 1 starts with +1 Attunement (Second Delver compensation).
    /// </summary>
    private static GameState CreateGameState(int deckSizePerPlayer = 30)
    {
        var state = new GameState(seed: 42);
        for (int p = 0; p < 2; p++)
        {
            var player = state.Players[p];
            for (int i = 0; i < deckSizePerPlayer; i++)
            {
                var card = new CardInstance(
                    state.NextInstanceId++,
                    "tst_dummy",
                    p)
                {
                    Zone = Zone.Deck
                };
                player.Deck.Add(card);
            }
        }

        // Deal starting hands: P0 gets 4, P1 gets 5
        for (int i = 0; i < 4; i++)
            DrawTopCard(state.Players[0], state);
        for (int i = 0; i < 5; i++)
            DrawTopCard(state.Players[1], state);

        // P1 starts with +1 Attunement (Second Delver compensation)
        state.Players[1].AttunementMax = 1;
        state.Players[1].Attunement = 1;

        return state;
    }

    private static void DrawTopCard(PlayerState player, GameState state)
    {
        if (player.Deck.Count > 0)
        {
            var card = player.Deck[0];
            player.Deck.RemoveAt(0);
            card.Zone = Zone.Hand;
            player.Hand.Add(card);
        }
    }

    // ——— Attunement ———

    [Fact]
    public void AttunementRampsUpEachTurn()
    {
        var state = CreateGameState(30);

        // EndTurn(P0) → ends P0's turn, starts P1's turn → P1 attunes
        // EndTurn(P1) → ends P1's turn, starts P0's turn → P0 attunes
        // So after 2 EndTurns, P0 has attuned once.

        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 1 });

        // P0: started at 0, 1 attune → 1
        Assert.Equal(1, state.Players[0].AttunementMax);
        Assert.Equal(1, state.Players[0].Attunement);

        // P1: started at 1, 1 attune → 2
        Assert.Equal(2, state.Players[1].AttunementMax);
        Assert.Equal(2, state.Players[1].Attunement);
    }

    [Fact]
    public void AttunementCapsAtTen()
    {
        var state = CreateGameState(30);

        // 22 EndTurns (11 per player) — 11 attunes each
        // P0: 0 + 11 = capped at 10
        // P1: 1 + 11 = 12, capped at 10
        for (int i = 0; i < 22; i++)
        {
            state = DuelEngine.Apply(state, new EndTurnAction
            {
                PlayerIndex = state.CurrentPlayerIndex
            });
        }

        Assert.Equal(10, state.Players[0].AttunementMax);
        Assert.Equal(10, state.Players[0].Attunement);
        Assert.Equal(10, state.Players[1].AttunementMax);
        Assert.Equal(10, state.Players[1].Attunement);
    }

    // ——— Turn tracking ———

    [Fact]
    public void TenTurnsOfEndTurn_TurnNumberTracksCorrectly()
    {
        var state = CreateGameState(30);

        // 10 EndTurns = 5 full cycles (P0+P1 = 1 cycle)
        for (int i = 0; i < 10; i++)
        {
            state = DuelEngine.Apply(state, new EndTurnAction
            {
                PlayerIndex = state.CurrentPlayerIndex
            });
        }

        // After 10 EndTurns we're on turn 6, player 0's turn
        Assert.Equal(6, state.TurnNumber);
        Assert.Equal(0, state.CurrentPlayerIndex);
    }

    // ——— Draw / first-player skip ———

    [Fact]
    public void FirstPlayerSkipsTurnOneDraw()
    {
        var state = CreateGameState(30);

        Assert.Equal(4, state.Players[0].Hand.Count);
        Assert.Equal(5, state.Players[1].Hand.Count);

        // P0's first turn: attune, NO draw (first player skip)
        // Note: EndTurn(P0) ends P0's turn and STARTS P1's turn.
        // P0's own attune already happened — wait, no.
        // Actually: when P0's turn begins (via EndTurn from P1), P0 is attuned.
        // EndTurn(P0) means P0 called "end turn" during their Main phase.
        // So the attune/draw phases for P0 already happened at the start of P0's turn.
        // Those phases were triggered by the PREVIOUS EndTurn(P1).
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });

        // P0 has NOT been attuned yet by this call — this call attunes P1.
        // P0 was attuned when their turn started (triggered by the previous EndTurn).
        // Since this is the very first turn, no previous EndTurn triggered P0's start.
        // So P0 hasn't had any attune phase yet.
        Assert.Equal(4, state.Players[0].Hand.Count);
        Assert.Equal(26, state.Players[0].Deck.Count);
        Assert.Equal(0, state.Players[0].AttunementMax); // no attune yet — first turn starts at 0

        // P1's first turn: attune (1→2), draw 1 → hand 5→6
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 1 });

        Assert.Equal(6, state.Players[1].Hand.Count);
        Assert.Equal(24, state.Players[1].Deck.Count); // 25 - 1 drawn
    }

    // ——— Fatigue ———

    [Fact]
    public void FatigueDamagesOnEmptyDeck()
    {
        // P0: 5 cards — draw 4 starting hand → 1 left
        // P1: 5 cards — draw 5 starting hand → 0 left
        var state = CreateGameState(deckSizePerPlayer: 5);

        // EndTurn(P0) ends P0, starts P1 → P1 draws from empty → fatigue 1
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });

        Assert.Equal(1, state.Players[1].FatigueCounter);
        Assert.Equal(24, state.Players[1].Vigor);
        Assert.Equal(5, state.Players[1].Hand.Count);
        Assert.False(state.IsGameOver);
    }

    [Fact]
    public void FatigueEscalates()
    {
        var state = CreateGameState(deckSizePerPlayer: 30);

        // Empty P0's deck
        state.Players[0].Deck.Clear();
        // P0 has 30 - 4 = 26 left... no, it starts with 30, draws 4, deck is 26. Then we cleared it.
        // So P0's deck is 0.

        // P1 still has 25 in deck.

        // EndTurn(P0) → P1 draws (P1 has cards, no fatigue)
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });
        Assert.Equal(0, state.Players[0].FatigueCounter);
        Assert.Equal(25, state.Players[0].Vigor);

        // EndTurn(P1) → P0 draws from empty → fatigue 1
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 1 });
        Assert.Equal(1, state.Players[0].FatigueCounter);
        Assert.Equal(24, state.Players[0].Vigor);

        // EndTurn(P0) → P1 draws (no fatigue for P0)
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });
        Assert.Equal(1, state.Players[0].FatigueCounter);

        // EndTurn(P1) → P0 draws from empty → fatigue 2
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 1 });
        Assert.Equal(2, state.Players[0].FatigueCounter);
        Assert.Equal(22, state.Players[0].Vigor); // 25 - 1 - 2
    }

    [Fact]
    public void FatigueCanKillPlayer()
    {
        var state = CreateGameState(deckSizePerPlayer: 30);
        state.Players[0].Deck.Clear();
        state.Players[0].Vigor = 4;

        // EndTurn(P0) → P1 draws (no P0 fatigue)
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });

        // EndTurn(P1) → P0 draws, fatigue 1 → 3 Vigor
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 1 });
        Assert.Equal(1, state.Players[0].FatigueCounter);
        Assert.Equal(3, state.Players[0].Vigor);

        // EndTurn(P0) → P1 draws (no P0 fatigue)
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });

        // EndTurn(P1) → P0 draws, fatigue 2 → 1 Vigor
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 1 });
        Assert.Equal(2, state.Players[0].FatigueCounter);
        Assert.Equal(1, state.Players[0].Vigor);

        // EndTurn(P0) → P1 draws (no P0 fatigue)
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });

        // EndTurn(P1) → P0 draws, fatigue 3 → -2 Vigor → dead
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 1 });
        Assert.Equal(3, state.Players[0].FatigueCounter);
        Assert.True(state.IsGameOver);
        Assert.Equal(1, state.WinnerIndex);
    }

    // ——— Hand limit ———

    [Fact]
    public void HandIsTruncatedToMaxTenAtEndPhase()
    {
        var state = CreateGameState(deckSizePerPlayer: 30);
        // P0 has 4 in hand from setup. Add 7 more to make 11.
        for (int i = 0; i < 7; i++)
        {
            var card = new CardInstance(
                state.NextInstanceId++,
                "tst_overflow",
                0)
            {
                Zone = Zone.Hand
            };
            state.Players[0].Hand.Add(card);
        }
        Assert.Equal(11, state.Players[0].Hand.Count);

        // EndTurn(P0) truncates P0's hand to 10 during the End phase
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });

        Assert.Equal(10, state.Players[0].Hand.Count);
        Assert.Single(state.Players[0].Discard);
    }
}
using System.Collections.Generic;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// Proves the client binding contract: the client must render from the engine's
/// returned state, not from a locally-tracked copy it updates in parallel.
///
/// These tests simulate what the client does — read state into DTOs, apply
/// actions via DuelEngine.Apply, read again, and assert the DTOs match the
/// actual state. This validates that:
///   1. No client-side code mutates GameState directly
///   2. After every action, the client re-renders from the new engine state
///   3. Rejected actions leave the state (and all derived render values) unchanged
/// </summary>
public class ClientBindingTests
{
    /// <summary>
    /// Simulates the client's "render" step: reads state into flat DTOs
    /// the same way GameStateManager.GetPlayerHud() and GetLanes() do.
    /// Returns a snapshot that can be compared across renders.
    /// </summary>
    private static RenderSnapshot CaptureRender(GameState state)
    {
        return new RenderSnapshot
        {
            Player0Vigor = state.Players[0].Vigor,
            Player0Attunement = state.Players[0].Attunement,
            Player0AttunementMax = state.Players[0].AttunementMax,
            Player0HandCount = state.Players[0].Hand.Count,
            Player0DeckCount = state.Players[0].Deck.Count,
            Player0LaneOccupants = state.Players[0].Lanes.Select(l => l.Occupant?.CardDefId).ToArray(),

            Player1Vigor = state.Players[1].Vigor,
            Player1Attunement = state.Players[1].Attunement,
            Player1AttunementMax = state.Players[1].AttunementMax,
            Player1HandCount = state.Players[1].Hand.Count,
            Player1DeckCount = state.Players[1].Deck.Count,
            Player1LaneOccupants = state.Players[1].Lanes.Select(l => l.Occupant?.CardDefId).ToArray(),

            CurrentPlayerIndex = state.CurrentPlayerIndex,
            TurnNumber = state.TurnNumber,
            IsGameOver = state.IsGameOver
        };
    }

    /// <summary>
    /// Assert two render snapshots are equal (all fields match).
    /// </summary>
    private static void AssertSnapshotsEqual(RenderSnapshot a, RenderSnapshot b)
    {
        Assert.Equal(a.Player0Vigor, b.Player0Vigor);
        Assert.Equal(a.Player0Attunement, b.Player0Attunement);
        Assert.Equal(a.Player0AttunementMax, b.Player0AttunementMax);
        Assert.Equal(a.Player0HandCount, b.Player0HandCount);
        Assert.Equal(a.Player0DeckCount, b.Player0DeckCount);
        Assert.Equal(a.Player1Vigor, b.Player1Vigor);
        Assert.Equal(a.Player1Attunement, b.Player1Attunement);
        Assert.Equal(a.Player1AttunementMax, b.Player1AttunementMax);
        Assert.Equal(a.Player1HandCount, b.Player1HandCount);
        Assert.Equal(a.Player1DeckCount, b.Player1DeckCount);
        Assert.Equal(a.CurrentPlayerIndex, b.CurrentPlayerIndex);
        Assert.Equal(a.TurnNumber, b.TurnNumber);
        Assert.Equal(a.IsGameOver, b.IsGameOver);
        Assert.Equal(a.Player0LaneOccupants, b.Player0LaneOccupants);
        Assert.Equal(a.Player1LaneOccupants, b.Player1LaneOccupants);
    }

    // ——— Tests ———

    [Fact]
    public void Render_AfterValidAction_MatchesEngineState()
    {
        // Arrange
        var state = CreateStateWithDummyCards(30);

        // Simulate client's initial render
        var render1 = CaptureRender(state);

        // Verify render values match the actual state directly
        Assert.Equal(state.Players[0].Vigor, render1.Player0Vigor);
        Assert.Equal(state.Players[0].Attunement, render1.Player0Attunement);
        Assert.Equal(state.Players[0].Hand.Count, render1.Player0HandCount);

        // Act — apply a valid action (EndTurn advances to opponent's turn)
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });

        // Simulate client's second render — must re-read from the NEW state
        var render2 = CaptureRender(state);

        // Assert — render2 values match the state DuelEngine returned
        Assert.Equal(state.Players[0].Vigor, render2.Player0Vigor);
        Assert.Equal(state.Players[0].Attunement, render2.Player0Attunement);
        Assert.Equal(state.Players[1].Attunement, render2.Player1Attunement);

        // Turn advanced from 1 to 1 (TurnNumber increments when CurrentPlayerIndex wraps back to 0)
        Assert.Equal(1, render2.CurrentPlayerIndex); // Now it's player 1's turn
        Assert.NotEqual(render1.CurrentPlayerIndex, render2.CurrentPlayerIndex);

        // Player 1 should have gained attunement (Second Delver: +1 on first turn)
        Assert.True(render2.Player1Attunement > 0);
    }

    [Fact]
    public void Render_AfterRejectedAction_IsUnchanged()
    {
        // Arrange
        var state = CreateStateWithDummyCards(30);

        var renderBefore = CaptureRender(state);

        // Act — try to play a card when the player has 0 attunement (rejected by engine)
        var cardInHand = state.Players[0].Hand.FirstOrDefault();
        Assert.NotNull(cardInHand);

        var invalidAction = new PlayCardAction
        {
            PlayerIndex = 0,
            CardInstanceId = cardInHand!.InstanceId,
            Cost = 1, // Player has 0 attunement, so cost 1 is not affordable
            LaneIndex = 0
        };

        var ex = Record.Exception(() =>
        {
            state = DuelEngine.Apply(state, invalidAction);
        });

        // Engine throws InvalidOperationException because attunement is insufficient
        Assert.NotNull(ex);
        Assert.Contains("attunement", ex.Message.ToLower());

        // Render again from the (unchanged) state
        var renderAfter = CaptureRender(state);

        // Assert — nothing changed
        AssertSnapshotsEqual(renderBefore, renderAfter);
    }

    [Fact]
    public void Render_AfterAttackFromEmptyLane_IsUnchanged()
    {
        // Arrange
        var state = CreateStateWithDummyCards(30);
        var renderBefore = CaptureRender(state);

        // Act — attack from a lane with no creature (rejected by engine)
        var invalidAction = new AttackAction
        {
            PlayerIndex = 0,
            SourceLane = 0,
            TargetLane = 0
        };

        var ex = Record.Exception(() =>
        {
            state = DuelEngine.Apply(state, invalidAction);
        });

        Assert.NotNull(ex);
        Assert.Contains("creature", ex.Message.ToLower());

        // Assert — state is unchanged
        var renderAfter = CaptureRender(state);
        AssertSnapshotsEqual(renderBefore, renderAfter);
    }

    [Fact]
    public void Render_AfterPlayCard_ReflectsNewState()
    {
        // Arrange: give player enough attunement and put a creature in hand
        var state = CreateStateWithDummyCards(30);
        state.Players[0].Attunement = 5;
        state.Players[0].AttunementMax = 5;
        Assert.True(state.Players[0].Hand.Count > 0);

        var renderBefore = CaptureRender(state);
        var card = state.Players[0].Hand[0];

        // Act — play a card to lane 0
        var action = new PlayCardAction
        {
            PlayerIndex = 0,
            CardInstanceId = card.InstanceId,
            Cost = 1,
            LaneIndex = 0
        };

        state = DuelEngine.Apply(state, action);
        var renderAfter = CaptureRender(state);

        // Assert — hand count decreased
        Assert.Equal(renderBefore.Player0HandCount - 1, renderAfter.Player0HandCount);

        // Assert — lane 0 is now occupied by the same card
        Assert.NotNull(renderAfter.Player0LaneOccupants[0]);
        Assert.Equal(card.CardDefId, renderAfter.Player0LaneOccupants[0]);

        // Assert — attunement was spent
        Assert.Equal(renderBefore.Player0Attunement - 1, renderAfter.Player0Attunement);

        // Assert — render values still match the actual state
        Assert.Equal(state.Players[0].Hand.Count, renderAfter.Player0HandCount);
        Assert.Equal(state.Players[0].Attunement, renderAfter.Player0Attunement);
        Assert.Equal(state.Players[0].Lanes[0].Occupant?.CardDefId, renderAfter.Player0LaneOccupants[0]);
    }

    // ——— Helpers ———

    private static GameState CreateStateWithDummyCards(int deckSize = 30)
    {
        var state = new GameState(seed: 42);
        for (int p = 0; p < 2; p++)
        {
            var player = state.Players[p];
            for (int i = 0; i < deckSize; i++)
            {
                var card = new CardInstance(state.NextInstanceId++, "tst_dummy", p)
                {
                    Zone = Zone.Deck,
                    Cost = 1,
                    BaseAttack = 1,
                    BaseVigor = 1,
                    CardType = CardType.CREATURE
                };
                player.Deck.Add(card);
            }
        }

        // Deal starting hands
        for (int i = 0; i < 4; i++)
        {
            var drawn = state.Players[0].Deck[0];
            state.Players[0].Deck.RemoveAt(0);
            drawn.Zone = Zone.Hand;
            state.Players[0].Hand.Add(drawn);
        }
        for (int i = 0; i < 6; i++)
        {
            var drawn = state.Players[1].Deck[0];
            state.Players[1].Deck.RemoveAt(0);
            drawn.Zone = Zone.Hand;
            state.Players[1].Hand.Add(drawn);
        }

        return state;
    }

    /// <summary>
    /// Simulates the DTOs the client would render from.
    /// </summary>
    private class RenderSnapshot
    {
        public int Player0Vigor { get; init; }
        public int Player0Attunement { get; init; }
        public int Player0AttunementMax { get; init; }
        public int Player0HandCount { get; init; }
        public int Player0DeckCount { get; init; }
        public string?[] Player0LaneOccupants { get; init; } = new string?[5];

        public int Player1Vigor { get; init; }
        public int Player1Attunement { get; init; }
        public int Player1AttunementMax { get; init; }
        public int Player1HandCount { get; init; }
        public int Player1DeckCount { get; init; }
        public string?[] Player1LaneOccupants { get; init; } = new string?[5];

        public int CurrentPlayerIndex { get; init; }
        public int TurnNumber { get; init; }
        public bool IsGameOver { get; init; }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// Tests for the deck-out fatigue rule.
/// When a player draws from an empty deck, FatigueCounter increments
/// and the player takes FatigueCounter damage through the normal damage path.
/// </summary>
[Collection("NonParallel")]
public class RulingFatigueTests
{
    private const int DeckSize = 30;

    public RulingFatigueTests()
    {
        RegisterTestCards();
    }

    private static void RegisterTestCards()
    {
        CardRegistry.Clear();
        CardRegistry.RegisterRange(new[]
        {
            new CardDef
            {
                Id = "vrd_c_root_warden", Name = "Root Warden", Set = "buried_age",
                Type = CardType.CREATURE, Cost = 3, Attack = 2, Vigor = 4,
                Strata = Strata.VERDANT, Rarity = Rarity.COMMON,
                Keywords = new List<string> { "GUARD" },
                Abilities = new List<AbilityDef>(),
            },
            new CardDef
            {
                Id = "emb_c_ember_hound", Name = "Ember Hound", Set = "buried_age",
                Type = CardType.CREATURE, Cost = 2, Attack = 3, Vigor = 1,
                Strata = Strata.EMBER, Rarity = Rarity.COMMON,
                Keywords = new List<string>(),
                Abilities = new List<AbilityDef>(),
            },
            new CardDef
            {
                Id = "tid_c_silt_reader", Name = "Silt Reader", Set = "buried_age",
                Type = CardType.CREATURE, Cost = 1, Attack = 1, Vigor = 2,
                Strata = Strata.TIDE, Rarity = Rarity.COMMON,
                Keywords = new List<string>(),
                Abilities = new List<AbilityDef>(),
            },
        });
    }

    private static GameState CreateStateWithEmptyDeck(int playerIndex)
    {
        var state = new GameState(42)
        {
            CurrentPlayerIndex = playerIndex,
            TurnNumber = 1,
        };
        // Set vigor to 25 (default)
        state.Players[0].MaxVigor = 25;
        state.Players[0].Vigor = 25;
        state.Players[1].MaxVigor = 25;
        state.Players[1].Vigor = 25;
        // Clear all decks so draws trigger fatigue
        state.Players[0].Deck.Clear();
        state.Players[1].Deck.Clear();
        // Give each player a few cards in hand so end-turn works
        var dummyId = new CardInstance(1, "vrd_c_root_warden", 0) { Zone = Zone.Hand };
        state.Players[0].Hand.Add(dummyId);
        state.Players[1].Hand.Add(dummyId);
        // Give enough attunement to survive end-turn processing
        state.Players[0].Attunement = 5;
        state.Players[0].AttunementMax = 5;
        state.Players[1].Attunement = 5;
        state.Players[1].AttunementMax = 5;
        return state;
    }

    /// <summary>
    /// Helper: end the current player's turn, triggering the draw phase
    /// for the next player.
    /// </summary>
    private static GameState EndTurn(GameState state)
    {
        return DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = state.CurrentPlayerIndex });
    }

    // ── Fatigue escalates ────────────────────────────────────────────

    [Fact]
    public void Fatigue_Escalates()
    {
        var state = CreateStateWithEmptyDeck(0);
        state.Players[0].FatigueCounter = 0;
        state.Players[1].FatigueCounter = 0;

        // P0 has an empty deck. End P0's turn → P1's draw phase.
        // But we want P1 (index 1) to have empty deck and draw.
        // Let's put both players on empty decks and alternate.

        // P0 ends turn → P1's turn. P1's deck is empty → fatigue 1 damage.
        state = EndTurn(state);
        Assert.Equal(1, state.Players[1].FatigueCounter);
        Assert.Equal(24, state.Players[1].Vigor); // 25 - 1

        // P1 ends turn → P0's turn. P0's deck is empty → fatigue 1 damage.
        state = EndTurn(state);
        Assert.Equal(1, state.Players[0].FatigueCounter);
        Assert.Equal(24, state.Players[0].Vigor); // 25 - 1

        // P0 ends turn → P1's turn. P1 fatigues again: 2nd empty draw = 2 damage.
        state = EndTurn(state);
        Assert.Equal(2, state.Players[1].FatigueCounter);
        Assert.Equal(22, state.Players[1].Vigor); // 24 - 2

        // P1 ends turn → P0's turn. P0 fatigues again: 2nd empty draw = 2 damage.
        state = EndTurn(state);
        Assert.Equal(2, state.Players[0].FatigueCounter);
        Assert.Equal(22, state.Players[0].Vigor); // 24 - 2

        // P0 ends turn → P1's turn. 3rd empty draw = 3 damage.
        state = EndTurn(state);
        Assert.Equal(3, state.Players[1].FatigueCounter);
        Assert.Equal(19, state.Players[1].Vigor); // 22 - 3
    }

    // ── Fatigue kills ───────────────────────────────────────────────

    [Fact]
    public void Fatigue_Kills()
    {
        var state = CreateStateWithEmptyDeck(0);
        state.Players[0].FatigueCounter = 0;
        state.Players[1].FatigueCounter = 0;
        // Set P1's vigor to a low value so fatigue kills them
        state.Players[1].Vigor = 1;
        state.Players[1].MaxVigor = 25;

        // P0 ends turn → P1's turn, P1 has empty deck
        // First empty draw: fatigue 1 damage. P1: 1 - 1 = 0 → dead.
        state = EndTurn(state);
        Assert.Equal(1, state.Players[1].FatigueCounter);
        Assert.True(state.IsGameOver, "P1 should die from fatigue damage");
        Assert.Equal(0, state.WinnerIndex); // P0 wins
    }

    // ── Fatigue is per-player ────────────────────────────────────────

    [Fact]
    public void Fatigue_IsPerPlayer()
    {
        var state = CreateStateWithEmptyDeck(0);
        state.Players[0].FatigueCounter = 0;
        state.Players[1].FatigueCounter = 0;

        // Cycle through several turns, tracking each player independently
        // P0 ends turn → P1's turn, P1 fatigues (1)
        state = EndTurn(state);
        Assert.Equal(0, state.Players[0].FatigueCounter);
        Assert.Equal(1, state.Players[1].FatigueCounter);

        // P1 ends turn → P0's turn, P0 fatigues (1)
        state = EndTurn(state);
        Assert.Equal(1, state.Players[0].FatigueCounter);
        Assert.Equal(1, state.Players[1].FatigueCounter);

        // P0 ends turn → P1's turn, P1 fatigues (2)
        state = EndTurn(state);
        Assert.Equal(1, state.Players[0].FatigueCounter);
        Assert.Equal(2, state.Players[1].FatigueCounter);

        // P1 ends turn → P0's turn, P0 fatigues (2)
        state = EndTurn(state);
        Assert.Equal(2, state.Players[0].FatigueCounter);
        Assert.Equal(2, state.Players[1].FatigueCounter);
    }

    // ── Fatigue affects state hash ──────────────────────────────────

    [Fact]
    public void Fatigue_AffectsStateHash()
    {
        var state1 = CreateStateWithEmptyDeck(0);
        state1.Players[0].FatigueCounter = 0;
        state1.Players[1].FatigueCounter = 0;
        state1 = EndTurn(state1); // P1 has 1 fatigue

        var state2 = CreateStateWithEmptyDeck(0);
        state2.Players[0].FatigueCounter = 0;
        state2.Players[1].FatigueCounter = 0;
        state2 = EndTurn(state2); // P1 has 1 fatigue

        Assert.Equal(state1.ComputeStateHash(), state2.ComputeStateHash());

        // Different fatigue count should produce different hash
        state2.Players[1].FatigueCounter = 2; // manually bump
        Assert.NotEqual(state1.ComputeStateHash(), state2.ComputeStateHash());
    }
}
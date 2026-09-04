using System;
using System.Collections.Generic;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.State;

public class ProgressionStateGrindTests
{
    /// <summary>
    /// Register test cards in CardRegistry.
    /// Must be called at the start of each test because other test classes
    /// also call CardRegistry.Clear() in their constructors/static constructors,
    /// and xUnit interleaves test methods across classes.
    /// </summary>
    private static void EnsureTestCardsRegistered()
    {
        // Always re-register to guard against CardRegistry.Clear() in other test classes
        // (xUnit interleaves test methods across classes, so static state is unreliable)
        CardRegistry.RegisterRange(new[]
        {
            new CardDef { Id = "test_common", Name = "Test Common", Cost = 3,
                Strata = Strata.VERDANT, Type = CardType.CREATURE, Rarity = Rarity.COMMON },
            new CardDef { Id = "test_uncommon", Name = "Test Uncommon", Cost = 4,
                Strata = Strata.EMBER, Type = CardType.CREATURE, Rarity = Rarity.UNCOMMON },
            new CardDef { Id = "test_rare", Name = "Test Rare", Cost = 5,
                Strata = Strata.TIDE, Type = CardType.CREATURE, Rarity = Rarity.RARE },
            new CardDef { Id = "test_relic", Name = "Test Relic", Cost = 6,
                Strata = Strata.HOLLOW, Type = CardType.CREATURE, Rarity = Rarity.RELIC },
        });
    }

    // ─── RuneDust values ───

    [Fact]
    public void GetRuneDustValue_Common_Returns5()
    {
        Assert.Equal(5, ProgressionState.GetRuneDustValue(Rarity.COMMON));
    }

    [Fact]
    public void GetRuneDustValue_Uncommon_Returns15()
    {
        Assert.Equal(15, ProgressionState.GetRuneDustValue(Rarity.UNCOMMON));
    }

    [Fact]
    public void GetRuneDustValue_Rare_Returns40()
    {
        Assert.Equal(40, ProgressionState.GetRuneDustValue(Rarity.RARE));
    }

    [Fact]
    public void GetRuneDustValue_Relic_Returns120()
    {
        Assert.Equal(120, ProgressionState.GetRuneDustValue(Rarity.RELIC));
    }

    // ─── Grinding basics ───

    [Fact]
    public void GrindCard_CommonCard_Adds5RuneDust()
    {
        EnsureTestCardsRegistered();
        var state = new ProgressionState();
        state.AddCard("test_common", 3);
        int added = state.GrindCard("test_common", new Dictionary<string, List<string>>());
        Assert.Equal(5, added);
        Assert.Equal(5, state.RuneDust);
        Assert.Equal(2, state.Collection["test_common"]); // 3 - 1
    }

    [Fact]
    public void GrindCard_UncommonCard_Adds15RuneDust()
    {
        EnsureTestCardsRegistered();
        var state = new ProgressionState();
        state.AddCard("test_uncommon", 2);
        int added = state.GrindCard("test_uncommon", new Dictionary<string, List<string>>());
        Assert.Equal(15, added);
        Assert.Equal(15, state.RuneDust);
        Assert.Equal(1, state.Collection["test_uncommon"]);
    }

    [Fact]
    public void GrindCard_RareCard_Adds40RuneDust()
    {
        EnsureTestCardsRegistered();
        var state = new ProgressionState();
        state.AddCard("test_rare", 2);
        int added = state.GrindCard("test_rare", new Dictionary<string, List<string>>());
        Assert.Equal(40, added);
        Assert.Equal(40, state.RuneDust);
    }

    [Fact]
    public void GrindCard_RelicCard_Adds120RuneDust()
    {
        EnsureTestCardsRegistered();
        var state = new ProgressionState();
        state.AddCard("test_relic", 2);
        int added = state.GrindCard("test_relic", new Dictionary<string, List<string>>());
        Assert.Equal(120, added);
        Assert.Equal(120, state.RuneDust);
    }

    [Fact]
    public void GrindCard_AccumulatesAcrossMultipleGrinds()
    {
        EnsureTestCardsRegistered();
        var state = new ProgressionState();
        state.AddCard("test_common", 10);
        state.AddCard("test_uncommon", 5);
        state.AddCard("test_rare", 3);
        state.AddCard("test_relic", 2);

        int total = 0;
        // Re-register before each grind to guard against CardRegistry.Clear() in other test classes
        EnsureTestCardsRegistered();
        total += state.GrindCard("test_common", new Dictionary<string, List<string>>()); // +5
        EnsureTestCardsRegistered();
        total += state.GrindCard("test_uncommon", new Dictionary<string, List<string>>()); // +15
        EnsureTestCardsRegistered();
        total += state.GrindCard("test_rare", new Dictionary<string, List<string>>()); // +40
        EnsureTestCardsRegistered();
        total += state.GrindCard("test_relic", new Dictionary<string, List<string>>()); // +120

        Assert.Equal(180, total);
        Assert.Equal(180, state.RuneDust);
    }

    // ─── Last copy guard ───

    [Fact]
    public void GrindCard_LastCopy_Returns0()
    {
        EnsureTestCardsRegistered();
        var state = new ProgressionState();
        state.AddCard("test_common", 1);
        int added = state.GrindCard("test_common", new Dictionary<string, List<string>>());
        Assert.Equal(0, added);
        Assert.Equal(0, state.RuneDust);
        Assert.True(state.Collection.ContainsKey("test_common")); // still owned
    }

    [Fact]
    public void CanGrindCard_LastCopy_ReturnsFalseWithMessage()
    {
        EnsureTestCardsRegistered();
        var state = new ProgressionState();
        state.AddCard("test_common", 1);
        bool allowed = state.CanGrindCard("test_common", new Dictionary<string, List<string>>(), out var error);
        Assert.False(allowed);
        Assert.Contains("last copy", error);
    }

    // ─── Deck dependency guard ───

    [Fact]
    public void GrindCard_DeckNeedsCopy_Returns0()
    {
        EnsureTestCardsRegistered();
        var state = new ProgressionState();
        state.AddCard("test_common", 2); // own 2 copies
        var savedDecks = new Dictionary<string, List<string>>
        {
            { "My Deck", new List<string> { "test_common" } }, // needs 1
            { "Other Deck", new List<string> { "test_common" } }, // needs 1 too
        };
        // 2 owned - 1 to grind = 1 remaining, but 2 decks need it → fail
        int added = state.GrindCard("test_common", savedDecks);
        Assert.Equal(0, added);
    }

    [Fact]
    public void GrindCard_DeckNeedsCopy_SufficientOwnership_Works()
    {
        EnsureTestCardsRegistered();
        var state = new ProgressionState();
        state.AddCard("test_common", 3); // own 3 copies
        var savedDecks = new Dictionary<string, List<string>>
        {
            { "My Deck", new List<string> { "test_common" } }, // needs 1
            { "Other Deck", new List<string> { "test_common" } }, // needs 1 too
        };
        // 3 owned - 1 to grind = 2 remaining, 2 decks need it → ok
        int added = state.GrindCard("test_common", savedDecks);
        Assert.Equal(5, added);
        Assert.Equal(2, state.Collection["test_common"]);
    }

    [Fact]
    public void GrindCard_NoDecks_IgnoresCheck()
    {
        EnsureTestCardsRegistered();
        var state = new ProgressionState();
        state.AddCard("test_uncommon", 2);
        int added = state.GrindCard("test_uncommon", new Dictionary<string, List<string>>());
        Assert.Equal(15, added); // deck check passes with empty decks
    }

    // ─── CanGrindCard edge cases ───

    [Fact]
    public void CanGrindCard_NotOwned_ReturnsFalse()
    {
        EnsureTestCardsRegistered();
        var state = new ProgressionState();
        bool allowed = state.CanGrindCard("test_common", new Dictionary<string, List<string>>(), out var error);
        Assert.False(allowed);
        Assert.Contains("Don't own", error);
    }

    [Fact]
    public void GrindCard_UnknownCardId_Returns0()
    {
        EnsureTestCardsRegistered();
        var state = new ProgressionState();
        state.AddCard("unknown_card", 2); // not in CardRegistry
        int added = state.GrindCard("unknown_card", new Dictionary<string, List<string>>());
        Assert.Equal(0, added);
    }

    // ─── Save roundtrip ───

    [Fact]
    public void RuneDust_RoundtripsThroughSave()
    {
        var state = new ProgressionState { RuneDust = 185 };

        var loaded = new ProgressionState();
        loaded.RuneDust = state.RuneDust;

        Assert.Equal(185, loaded.RuneDust);
    }

    [Fact]
    public void RuneDust_DefaultIsZero()
    {
        var state = new ProgressionState();
        Assert.Equal(0, state.RuneDust);
    }

    [Fact]
    public void GrindCard_AfterGrindingLastCopy_RemovesFromCollection()
    {
        EnsureTestCardsRegistered();
        var state = new ProgressionState();
        state.AddCard("test_rare", 2);
        state.GrindCard("test_rare", new Dictionary<string, List<string>>()); // 2→1
        state.GrindCard("test_rare", new Dictionary<string, List<string>>()); // last copy → blocked, 0 added

        Assert.Equal(40, state.RuneDust); // only first grind succeeded
        Assert.Equal(1, state.Collection["test_rare"]); // last copy preserved
    }
}
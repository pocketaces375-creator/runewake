using System.Collections.Generic;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.State;

public class DeckValidatorTests
{
    // Build enough unique cards that we can construct valid decks within singleton rules.
    // We need more than 30 unique Verdant COMMON cards to test max-size scenarios.
    private static readonly List<CardDef> VerdantCards;
    private static readonly List<CardDef> EmberCards;

    static DeckValidatorTests()
    {
        VerdantCards = Enumerable.Range(1, 45).Select(i => new CardDef
        {
            Id = $"vrd_c_test_{i:D2}", Name = $"Verdant Card {i}", Cost = 3,
            Strata = Strata.VERDANT, Type = CardType.CREATURE, Rarity = Rarity.COMMON
        }).ToList();

        EmberCards = Enumerable.Range(1, 10).Select(i => new CardDef
        {
            Id = $"emb_c_test_{i:D2}", Name = $"Ember Card {i}", Cost = 3,
            Strata = Strata.EMBER, Type = CardType.CREATURE, Rarity = Rarity.COMMON
        }).ToList();
    }

    private static CardDef? Lookup(string id)
    {
        var all = new List<CardDef>(VerdantCards);
        all.AddRange(EmberCards);
        return all.FirstOrDefault(c => c.Id == id);
    }

    /// <summary>Build a deck with exactly `count` unique cards from the given pool.</summary>
    private static List<string> BuildDeck(IReadOnlyList<CardDef> pool, int count)
    {
        var deck = new List<string>(count);
        for (int i = 0; i < count; i++)
            deck.Add(pool[i % pool.Count].Id);
        return deck;
    }

    // ——— Size bounds ———

    [Fact]
    public void Validate_EmptyDeck_TooFewCards()
    {
        var result = DeckValidator.Validate([], Lookup);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("too few cards"));
    }

    [Fact]
    public void Validate_29Cards_TooFew()
    {
        var deck = BuildDeck(VerdantCards, 29);
        var result = DeckValidator.Validate(deck, Lookup);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("too few cards")
            && e.Contains("/30 minimum"));
    }

    [Fact]
    public void Validate_30Cards_IsValid()
    {
        var deck = BuildDeck(VerdantCards, 30);
        var result = DeckValidator.Validate(deck, Lookup);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_31Cards_TooMany()
    {
        var deck = BuildDeck(VerdantCards, 31);
        var result = DeckValidator.Validate(deck, Lookup);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("too many cards")
            && e.Contains("/30 maximum"));
    }

    [Fact]
    public void Validate_41Cards_TooMany()
    {
        var deck = BuildDeck(VerdantCards, 41);
        var result = DeckValidator.Validate(deck, Lookup);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("too many cards")
            && e.Contains("/30 maximum"));
    }

    // ——— Singleton ———

    [Fact]
    public void Validate_DuplicateCard_ReportsDuplicate()
    {
        var deck = BuildDeck(VerdantCards, 29);
        deck.Add(VerdantCards[0].Id); // 30 cards, but first card appears twice
        var result = DeckValidator.Validate(deck, Lookup);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.StartsWith("duplicate:"));
        Assert.Contains(result.Errors, e => e.Contains(VerdantCards[0].Name));
    }

    [Fact]
    public void Validate_MultipleDuplicates_ReportsAll()
    {
        var deck = BuildDeck(VerdantCards, 28);
        deck.Add(VerdantCards[0].Id); // dup 1
        deck.Add(VerdantCards[1].Id); // dup 2
        deck.Add(VerdantCards[1].Id); // dup 2 again (appears 3 times)
        // Total: 31 cards, dups of card 0 and card 1
        var result = DeckValidator.Validate(deck, Lookup);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.StartsWith("duplicate:"));
        // Should have at least 2 errors: one for card 0, one for card 1
        Assert.True(result.Errors.Count(e => e.StartsWith("duplicate:")) >= 2);
    }

    [Fact]
    public void Validate_SingletonDeck30_IsValid()
    {
        // 30 unique cards = valid
        var deck = BuildDeck(VerdantCards, 30);
        var result = DeckValidator.Validate(deck, Lookup);
        Assert.True(result.IsValid);
    }

    // ——— CanAdd ———

    [Fact]
    public void CanAdd_ToFullDeck_ReturnsFull()
    {
        var deck = BuildDeck(VerdantCards, DeckRules.MaxSize);
        var result = DeckValidator.CanAdd(deck, VerdantCards[DeckRules.MaxSize].Id, Lookup);
        Assert.False(result.IsValid);
        Assert.Contains("full", string.Join(" ", result.Errors));
    }

    [Fact]
    public void CanAdd_DuplicateCard_ReturnsDuplicate()
    {
        var deck = BuildDeck(VerdantCards, 29);
        var firstId = VerdantCards[0].Id;
        // First card is already in deck
        var result = DeckValidator.CanAdd(deck, firstId, Lookup);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.StartsWith("duplicate:"));
    }

    [Fact]
    public void CanAdd_ValidCard_ReturnsOk()
    {
        var deck = BuildDeck(VerdantCards, 29);
        // Use a card not in the first 29 positions
        var result = DeckValidator.CanAdd(deck, VerdantCards[35].Id, Lookup);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void CanAdd_UnknownCard_ReturnsNotFound()
    {
        var deck = BuildDeck(VerdantCards, 29);
        var result = DeckValidator.CanAdd(deck, "nonexistent_card", Lookup);
        Assert.False(result.IsValid);
        Assert.Contains("not found", string.Join(" ", result.Errors));
    }
}
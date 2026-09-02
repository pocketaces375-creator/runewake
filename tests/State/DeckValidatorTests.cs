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

    // ──── TASK-COLLECTION-DATA-1: ValidateCollection ────

    [Fact]
    public void ValidateCollection_EmptyDecks_NoErrors()
    {
        var errors = DeckValidator.ValidateCollection(
            new Dictionary<string, int> { { "vrd_c_root_warden", 1 } },
            new Dictionary<string, List<string>>());
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateCollection_NullArguments_NoErrors()
    {
        Assert.Empty(DeckValidator.ValidateCollection(null!, null!));
    }

    [Fact]
    public void ValidateCollection_SufficientOwnership_Passes()
    {
        var collection = new Dictionary<string, int>
        {
            { "vrd_c_root_warden", 3 },
            { "emb_c_ember_hound", 2 },
            { "dwn_c_dawn_warder", 1 },
        };
        var savedDecks = new Dictionary<string, List<string>>
        {
            { "Deck A", new List<string> { "vrd_c_root_warden", "emb_c_ember_hound" } },
            { "Deck B", new List<string> { "vrd_c_root_warden", "dwn_c_dawn_warder" } },
            // root_warden appears in 2 decks → own 3 (ok)
            // ember_hound appears in 1 deck → own 2 (ok)
            // dawn_warder appears in 1 deck → own 1 (ok)
        };
        var errors = DeckValidator.ValidateCollection(collection, savedDecks);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateCollection_MissingCopies_ReportsError()
    {
        var collection = new Dictionary<string, int>
        {
            { "vrd_c_root_warden", 1 },
            { "emb_c_ember_hound", 1 },
        };
        var savedDecks = new Dictionary<string, List<string>>
        {
            { "Deck A", new List<string> { "vrd_c_root_warden" } },
            { "Deck B", new List<string> { "vrd_c_root_warden" } },
            // root_warden appears in 2 decks → own 1 (needs 2)
        };
        var errors = DeckValidator.ValidateCollection(collection, savedDecks);
        Assert.Single(errors);
        Assert.Contains("Need 2 copies", errors[0]);
        Assert.Contains("vrd_c_root_warden", errors[0]);
    }

    [Fact]
    public void ValidateCollection_MultipleCardsNeedCopies_ReportsAll()
    {
        var collection = new Dictionary<string, int>
        {
            { "card_a", 1 },
            { "card_b", 1 },
        };
        var savedDecks = new Dictionary<string, List<string>>
        {
            { "Deck A", new List<string> { "card_a", "card_b" } },
            { "Deck B", new List<string> { "card_a", "card_b" } },
        };
        var errors = DeckValidator.ValidateCollection(collection, savedDecks);
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Contains("card_a") && e.Contains("Need 2 copies"));
        Assert.Contains(errors, e => e.Contains("card_b") && e.Contains("Need 2 copies"));
    }

    [Fact]
    public void ValidateCollection_CardInManyDecks_RequiresManyCopies()
    {
        var collection = new Dictionary<string, int> { { "card_x", 2 } };
        var savedDecks = new Dictionary<string, List<string>>
        {
            { "D1", new List<string> { "card_x" } },
            { "D2", new List<string> { "card_x" } },
            { "D3", new List<string> { "card_x" } },
        };
        var errors = DeckValidator.ValidateCollection(collection, savedDecks);
        Assert.Single(errors);
        Assert.Contains("Need 3 copies", errors[0]);
    }

    [Fact]
    public void ValidateCollection_CardOwnedButNotInDecks_NoError()
    {
        var collection = new Dictionary<string, int> { { "card_y", 5 }, { "card_x", 1 } };
        var savedDecks = new Dictionary<string, List<string>>
        {
            { "D1", new List<string> { "card_x" } },
        };
        var errors = DeckValidator.ValidateCollection(collection, savedDecks);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateCollection_EmptyCollectionWithDecks_ReportsAllCards()
    {
        var savedDecks = new Dictionary<string, List<string>>
        {
            { "D1", new List<string> { "card_a", "card_b" } },
        };
        var errors = DeckValidator.ValidateCollection(
            new Dictionary<string, int>(),
            savedDecks);
        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Contains("Need 1 copies", e));
    }
}
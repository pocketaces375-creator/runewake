using System.Collections.Generic;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.State;

public class DeckValidatorTests
{
    // Build 15 unique Verdant COMMON cards and 10 unique Ember COMMON cards
    // so we can construct valid 30-card decks within the 2-copy-per-card limit.
    private static readonly List<CardDef> VerdantCards;
    private static readonly List<CardDef> EmberCards;
    private static readonly List<CardDef> TideCards;

    static DeckValidatorTests()
    {
        VerdantCards = Enumerable.Range(1, 15).Select(i => new CardDef
        {
            Id = $"vrd_c_test_{i:D2}", Name = $"Verdant Card {i}", Cost = 3,
            Strata = Strata.VERDANT, Type = CardType.CREATURE, Rarity = Rarity.COMMON
        }).ToList();

        EmberCards = Enumerable.Range(1, 10).Select(i => new CardDef
        {
            Id = $"emb_c_test_{i:D2}", Name = $"Ember Card {i}", Cost = 3,
            Strata = Strata.EMBER, Type = CardType.CREATURE, Rarity = Rarity.COMMON
        }).ToList();

        TideCards = Enumerable.Range(1, 5).Select(i => new CardDef
        {
            Id = $"tid_c_test_{i:D2}", Name = $"Tide Card {i}", Cost = 3,
            Strata = Strata.TIDE, Type = CardType.CREATURE, Rarity = Rarity.COMMON
        }).ToList();
    }

    private static readonly CardDef RelicCard = new()
    {
        Id = "vrd_c_relic_test", Name = "Test Relic", Cost = 6,
        Strata = Strata.VERDANT, Type = CardType.RELIC, Rarity = Rarity.RELIC
    };

    private static CardDef? Lookup(string id)
    {
        var all = new List<CardDef>(VerdantCards);
        all.AddRange(EmberCards);
        all.AddRange(TideCards);
        all.Add(RelicCard);
        return all.FirstOrDefault(c => c.Id == id);
    }

    /// <summary>Build a deck with exactly `count` cards, drawing evenly from the given pool.
    /// Each card appears at most 2 times. Only use this for pools with enough unique IDs
    /// to cover half of `count`.</summary>
    private static List<string> BuildDeck(IReadOnlyList<CardDef> pool, int count)
    {
        var deck = new List<string>(count);
        int idx = 0;
        while (deck.Count < count)
        {
            var def = pool[idx % pool.Count];
            int existing = deck.Count(id => id == def.Id);
            if (existing < 2)
                deck.Add(def.Id);
            idx++;
        }
        return deck;
    }

    // ——— Size ———

    [Fact]
    public void Validate_EmptyDeck_IsInvalid()
    {
        var result = DeckValidator.Validate([], Lookup);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("30 cards"));
    }

    [Fact]
    public void Validate_29Cards_IsInvalid()
    {
        var deck = BuildDeck(VerdantCards, 29);
        var result = DeckValidator.Validate(deck, Lookup);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("30 cards"));
    }

    [Fact]
    public void Validate_30CardsMonoStrata_IsValid()
    {
        var deck = BuildDeck(VerdantCards, 30);
        var result = DeckValidator.Validate(deck, Lookup);
        Assert.True(result.IsValid);
    }

    // ——— Copy limit ———

    [Fact]
    public void Validate_3CopiesOfOneCard_IsInvalid()
    {
        // Explicitly build a deck where card 0 appears 3 times
        var deck = new List<string>();
        deck.AddRange(Enumerable.Repeat(VerdantCards[0].Id, 3));
        // Fill to 30 with other cards (2 copies each)
        for (int i = 1; i < VerdantCards.Count; i++)
        {
            if (deck.Count >= 30) break;
            deck.Add(VerdantCards[i].Id);
            if (deck.Count < 30) deck.Add(VerdantCards[i].Id);
        }
        // Trim to exactly 30
        while (deck.Count > 30) deck.RemoveAt(deck.Count - 1);

        var result = DeckValidator.Validate(deck, Lookup);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("max 2"));
    }

    // ——— RELIC ———

    [Fact]
    public void Validate_2RelicCards_IsInvalid()
    {
        var deck = BuildDeck(VerdantCards, 28);
        deck.Add(RelicCard.Id);
        deck.Add(RelicCard.Id); // 2 copies of the same relic

        var result = DeckValidator.Validate(deck, Lookup);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("2 RELIC") || e.Contains("RELIC"));
    }

    [Fact]
    public void Validate_1RelicCard_IsValid()
    {
        var deck = BuildDeck(VerdantCards, 28);
        // Need 2 more cards, but RelicCard would take 1 slot
        // Use 1 existing Verdant + 1 Relic = fill the rest
        var extra = new List<string> { VerdantCards[14].Id, RelicCard.Id };
        deck.AddRange(extra);

        var result = DeckValidator.Validate(deck, Lookup);
        Assert.True(result.IsValid);
    }

    // ——— Strata diversity ———

    [Fact]
    public void Validate_3Strata_IsInvalid()
    {
        var deck = new List<string>();
        // 10 Verdant × 2 = 20, 5 Ember × 2 = 10 = 30? No, that's 2 strata
        // Add Tide to make it 3: 8 Verdant×2=16, 5 Ember×2=10, 2 Tide×2=4 = 30
        for (int i = 0; i < 8; i++) deck.Add(VerdantCards[i].Id);
        for (int i = 8; i < 10; i++) deck.Add(VerdantCards[i].Id); // wait, this is all verdant
        // Let me be more explicit
        deck.Clear();
        foreach (var c in VerdantCards.Take(8)) { deck.Add(c.Id); deck.Add(c.Id); } // 16
        foreach (var c in EmberCards.Take(5)) { deck.Add(c.Id); deck.Add(c.Id); } // 10
        // Now we have 26 cards. Need 4 more — but only 1 Tide card×2 = 2
        foreach (var c in TideCards.Take(2)) { deck.Add(c.Id); deck.Add(c.Id); } // 4 → 30
        // This has 3 Strata
        var result = DeckValidator.Validate(deck, Lookup);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("3 Strata"));
    }

    [Fact]
    public void Validate_2Strata_IsValid()
    {
        var deck = new List<string>();
        foreach (var c in VerdantCards.Take(10)) { deck.Add(c.Id); deck.Add(c.Id); } // 20
        foreach (var c in EmberCards.Take(5)) { deck.Add(c.Id); deck.Add(c.Id); } // 10
        Assert.Equal(30, deck.Count);

        var result = DeckValidator.Validate(deck, Lookup);
        Assert.True(result.IsValid);
    }

    // ——— CanAdd ———

    [Fact]
    public void CanAdd_ToFullDeck_ReturnsReason()
    {
        var deck = BuildDeck(VerdantCards, 30);
        var result = DeckValidator.CanAdd(deck, VerdantCards[0].Id, Lookup);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CanAdd_ThirdCopy_ReturnsReason()
    {
        var deck = BuildDeck(VerdantCards, 29);
        var firstId = VerdantCards[0].Id;
        // Already has 2 copies of first card (from BuildDeck with 2-copy max)
        // Replace a card to make it 3 copies
        deck[0] = firstId;
        deck[1] = firstId;

        var result = DeckValidator.CanAdd(deck, firstId, Lookup);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CanAdd_SecondRelic_ReturnsReason()
    {
        var deck = BuildDeck(VerdantCards, 28);
        deck.Add(RelicCard.Id); // already one relic

        var result = DeckValidator.CanAdd(deck, RelicCard.Id, Lookup);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CanAdd_ThirdStrata_ReturnsReason()
    {
        var deck = new List<string>();
        foreach (var c in VerdantCards.Take(10)) { deck.Add(c.Id); deck.Add(c.Id); } // 20
        foreach (var c in EmberCards.Take(5)) { deck.Add(c.Id); deck.Add(c.Id); } // 10

        // Adding a Tide card would exceed 2 strata
        var result = DeckValidator.CanAdd(deck, TideCards[0].Id, Lookup);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CanAdd_ValidCard_ReturnsOk()
    {
        var deck = BuildDeck(VerdantCards, 29);
        var result = DeckValidator.CanAdd(deck, VerdantCards[14].Id, Lookup);
        Assert.True(result.IsValid);
    }
}
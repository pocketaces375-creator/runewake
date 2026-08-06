using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.State;

/// <summary>
/// Tests for GameState.Initialize(GameConfig) — the factory method used by
/// the real game entry point (DuelScene._Ready calls GameStateManager.Initialize
/// which calls this). The test helpers in other test classes manually construct
/// GameState and populate decks directly, bypassing Initialize entirely. That
/// left the actual game-start path uncovered — and missing a player.Deck.Add()
/// call that made the game produce empty boards and empty hands.
///
/// Every assertion here documents a real invariant the game depends on.
/// </summary>
[Collection("NonParallel")]
public class GameStateInitTests
{
    private const int DeckSize = 30;

    public GameStateInitTests()
    {
        RegisterTestCards();
    }

    /// <summary>
    /// Register a minimal set of card definitions for test games.
    /// Uses the same card IDs the hand-authored content packs define,
    /// so Initialize can resolve them from the registry.
    /// </summary>
    private static void RegisterTestCards()
    {
        CardRegistry.Clear();
        CardRegistry.RegisterRange(new[]
        {
            // 3 distinct cards, each used multiple times to fill the 30-card deck
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

    /// <summary>
    /// Build a 30-card deck from the registered test card IDs,
    /// cycling through them to fill the required count.
    /// </summary>
    private static List<string> BuildDeckIds(int size = DeckSize)
    {
        var ids = new[] { "vrd_c_root_warden", "emb_c_ember_hound", "tid_c_silt_reader" };
        var deck = new List<string>(size);
        for (int i = 0; i < size; i++)
            deck.Add(ids[i % ids.Length]);
        return deck;
    }

    private static GameConfig MakeConfig(ulong seed = 42, int deckSize = DeckSize)
    {
        var deckIds = BuildDeckIds(deckSize);
        return new GameConfig
        {
            Seed = seed,
            ContentVersion = 1,
            Player0DeckIds = deckIds,
            Player1DeckIds = deckIds,
        };
    }

    // ─────────────────────────────────────────────────────
    //  Deck assertions
    // ─────────────────────────────────────────────────────

    [Fact]
    public void Initialize_EachPlayerDeck_HasCorrectCardCount()
    {
        var config = MakeConfig(deckSize: 30);
        var state = GameState.Initialize(config);

        // Each player started with 30 cards, dealt 4 (P0) or 5 (P1)
        Assert.Equal(26, state.Players[0].Deck.Count);
        Assert.Equal(25, state.Players[1].Deck.Count);
    }

    [Fact]
    public void Initialize_EachPlayerDeck_TotalEqualsConfig()
    {
        var config = MakeConfig(deckSize: 30);
        var state = GameState.Initialize(config);

        // Total cards = deck + hand (no discards or lanes at start)
        Assert.Equal(30, state.Players[0].Deck.Count + state.Players[0].Hand.Count);
        Assert.Equal(30, state.Players[1].Deck.Count + state.Players[1].Hand.Count);
    }

    [Fact]
    public void Initialize_VariableDeckSizes_AllWork()
    {
        foreach (var size in new[] { 20, 30, 40, 60 })
        {
            CardRegistry.Clear();
            RegisterTestCards();

            var config = MakeConfig(deckSize: size);
            var state = GameState.Initialize(config);

            int p0Expected = size - 4; // P0 dealt 4
            int p1Expected = size - 5; // P1 dealt 5
            Assert.Equal(p0Expected, state.Players[0].Deck.Count);
            Assert.Equal(p1Expected, state.Players[1].Deck.Count);
        }
    }

    // ─────────────────────────────────────────────────────
    //  Hand assertions
    // ─────────────────────────────────────────────────────

    [Fact]
    public void Initialize_StartingHands_AreCorrectSize()
    {
        var config = MakeConfig();
        var state = GameState.Initialize(config);

        // P0 gets 4 (first player), P1 gets 5 (second delver)
        Assert.Equal(4, state.Players[0].Hand.Count);
        Assert.Equal(5, state.Players[1].Hand.Count);
    }

    [Fact]
    public void Initialize_HandCards_HaveZoneHand()
    {
        var config = MakeConfig();
        var state = GameState.Initialize(config);

        foreach (var card in state.Players[0].Hand)
            Assert.Equal(Zone.Hand, card.Zone);
        foreach (var card in state.Players[1].Hand)
            Assert.Equal(Zone.Hand, card.Zone);
    }

    [Fact]
    public void Initialize_HandCards_HaveCorrectController()
    {
        var config = MakeConfig();
        var state = GameState.Initialize(config);

        foreach (var card in state.Players[0].Hand)
            Assert.Equal(0, card.Controller);
        foreach (var card in state.Players[1].Hand)
            Assert.Equal(1, card.Controller);
    }

    // ─────────────────────────────────────────────────────
    //  Zone emptiness assertions
    // ─────────────────────────────────────────────────────

    [Fact]
    public void Initialize_NoZoneIsUnexpectedlyEmpty()
    {
        var config = MakeConfig();
        var state = GameState.Initialize(config);

        // Every card that was in the config must be somewhere
        for (int p = 0; p < 2; p++)
        {
            int total = state.Players[p].Deck.Count
                      + state.Players[p].Hand.Count
                      + state.Players[p].Discard.Count          // 0 at start
                      + state.Players[p].Barrow.Count;          // 0 at start
            Assert.Equal(30, total);
        }
    }

    [Fact]
    public void Initialize_Lanes_AllEmpty()
    {
        var config = MakeConfig();
        var state = GameState.Initialize(config);

        for (int p = 0; p < 2; p++)
            for (int i = 0; i < 5; i++)
                Assert.Null(state.Players[p].Lanes[i].Occupant);
    }

    [Fact]
    public void Initialize_DiscardAndBarrow_Empty()
    {
        var config = MakeConfig();
        var state = GameState.Initialize(config);

        for (int p = 0; p < 2; p++)
        {
            Assert.Empty(state.Players[p].Discard);
            Assert.Empty(state.Players[p].Barrow);
            Assert.Empty(state.Players[p].UnearthQueue);
        }
    }

    // ─────────────────────────────────────────────────────
    //  Card instance integrity
    // ─────────────────────────────────────────────────────

    [Fact]
    public void Initialize_CardInstances_HaveRequiredFields()
    {
        var config = MakeConfig();
        var state = GameState.Initialize(config);

        for (int p = 0; p < 2; p++)
        {
            foreach (var card in state.Players[p].Deck)
            {
                Assert.NotEqual(0, card.InstanceId);
                Assert.NotNull(card.CardDefId);
                Assert.NotEmpty(card.CardDefId);
                Assert.Equal(p, card.Controller);
                Assert.Equal(Zone.Deck, card.Zone);
            }
            foreach (var card in state.Players[p].Hand)
            {
                Assert.NotEqual(0, card.InstanceId);
                Assert.NotNull(card.CardDefId);
                Assert.NotEmpty(card.CardDefId);
                Assert.Equal(p, card.Controller);
                Assert.Equal(Zone.Hand, card.Zone);
            }
        }
    }

    [Fact]
    public void Initialize_UniqueInstanceIds()
    {
        var config = MakeConfig();
        var state = GameState.Initialize(config);

        var allIds = new HashSet<int>();
        for (int p = 0; p < 2; p++)
        {
            foreach (var card in state.Players[p].Deck)
                Assert.True(allIds.Add(card.InstanceId), $"Duplicate InstanceId {card.InstanceId}");
            foreach (var card in state.Players[p].Hand)
                Assert.True(allIds.Add(card.InstanceId), $"Duplicate InstanceId {card.InstanceId}");
        }
        // 30 cards per player × 2 players = 60 unique IDs
        Assert.Equal(60, allIds.Count);
    }

    // ─────────────────────────────────────────────────────
    //  Player state assertions
    // ─────────────────────────────────────────────────────

    [Fact]
    public void Initialize_PlayerState_HasCorrectInitialValues()
    {
        var config = MakeConfig();
        var state = GameState.Initialize(config);

        // Both players start at 25 Vigor
        Assert.Equal(25, state.Players[0].Vigor);
        Assert.Equal(25, state.Players[1].Vigor);
        Assert.Equal(25, state.Players[0].MaxVigor);
        Assert.Equal(25, state.Players[1].MaxVigor);

        // P0 starts with 0 attunement, P1 starts with 1 (Second Delver)
        Assert.Equal(0, state.Players[0].Attunement);
        Assert.Equal(0, state.Players[0].AttunementMax);
        Assert.Equal(1, state.Players[1].Attunement);
        Assert.Equal(1, state.Players[1].AttunementMax);

        // Turn starts at 1, P0 goes first
        Assert.Equal(1, state.TurnNumber);
        Assert.Equal(0, state.CurrentPlayerIndex);

        // Game not over
        Assert.False(state.IsGameOver);
        Assert.Null(state.WinnerIndex);
    }

    // ─────────────────────────────────────────────────────
    //  Error handling
    // ─────────────────────────────────────────────────────

    [Fact]
    public void Initialize_UnknownCardId_Throws()
    {
        CardRegistry.Clear();
        RegisterTestCards(); // Only the 3 standard test cards

        var config = new GameConfig
        {
            Seed = 42,
            ContentVersion = 1,
            Player0DeckIds = new List<string> { "nonexistent_card_id" },
            Player1DeckIds = new List<string> { "vrd_c_root_warden" },
        };

        var ex = Assert.Throws<InvalidOperationException>(() => GameState.Initialize(config));
        Assert.Contains("nonexistent_card_id", ex.Message);
    }

    [Fact]
    public void Initialize_MultipleSeeds_ProduceDifferentShuffles()
    {
        // Same deck, different seeds should produce different hand orders
        var deckIds = BuildDeckIds();

        var config1 = new GameConfig { Seed = 42, ContentVersion = 1, Player0DeckIds = deckIds, Player1DeckIds = deckIds };
        var config2 = new GameConfig { Seed = 99999, ContentVersion = 1, Player0DeckIds = deckIds, Player1DeckIds = deckIds };

        CardRegistry.Clear();
        RegisterTestCards();
        var state1 = GameState.Initialize(config1);

        CardRegistry.Clear();
        RegisterTestCards();
        var state2 = GameState.Initialize(config2);

        // Hands should differ between seeds (extremely unlikely to match)
        var hand0a = state1.Players[0].Hand.Select(c => c.CardDefId).ToList();
        var hand0b = state2.Players[0].Hand.Select(c => c.CardDefId).ToList();
        Assert.NotEqual(hand0a, hand0b);
    }
}

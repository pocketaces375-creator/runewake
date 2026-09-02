using System.IO;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Xunit;

namespace Runewake.Tests.Cards;

public class EncounterLoaderTests
{
    private static readonly EncounterPack EarlyPack = EncounterLoader.LoadPack(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "content", "encounters", "region_01_early.json"));

    private static readonly EncounterPack MidPack = EncounterLoader.LoadPack(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "content", "encounters", "region_01_mid.json"));

    private static readonly EncounterPack LatePack = EncounterLoader.LoadPack(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "content", "encounters", "region_01_late.json"));

    private static readonly EncounterPack BossPack = EncounterLoader.LoadPack(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "content", "encounters", "region_01_boss.json"));

    private static readonly EncounterDef[] AllEncounters =
        EarlyPack.Encounters
            .Concat(MidPack.Encounters)
            .Concat(LatePack.Encounters)
            .Concat(BossPack.Encounters)
            .ToArray();

    [Fact]
    public void LoadsAllNineEncounters()
    {
        Assert.Equal(9, AllEncounters.Length);
    }

    [Fact]
    public void AllEncounters_HaveIdAndName()
    {
        foreach (var e in AllEncounters)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Id));
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
        }
    }

    [Fact]
    public void AllEncounters_Have30CardDeck()
    {
        foreach (var e in AllEncounters)
        {
            Assert.True(e.Deck.Count == 30,
                $"Encounter {e.Id} has {e.Deck.Count} cards (expected 30)");
        }
    }

    [Fact]
    public void AllEncounters_HaveDialogueIntro()
    {
        foreach (var e in AllEncounters)
        {
            Assert.NotNull(e.DialogueIntro);
            Assert.NotEmpty(e.DialogueIntro);
        }
    }

    [Fact]
    public void AllEncounters_HaveShardReward()
    {
        foreach (var e in AllEncounters)
        {
            Assert.True(e.ShardReward > 0, $"Encounter {e.Id} has no shard reward");
        }
    }

    [Fact]
    public void EliteEncounters_HaveModifiers()
    {
        var elites = AllEncounters.Where(e => e.Id.Contains("elite"));
        foreach (var e in elites)
        {
            Assert.NotNull(e.Modifier);
            Assert.NotEmpty(e.Modifier);
        }
    }

    [Fact]
    public void BossEncounters_HavePortraits()
    {
        var bosses = AllEncounters.Where(e => e.Id.Contains("warden") || e.Id.Contains("elite"));
        foreach (var e in bosses)
        {
            Assert.NotNull(e.Portrait);
            Assert.NotEmpty(e.Portrait);
        }
    }

    [Fact]
    public void Wayfarer_IsFirstEncounter()
    {
        var wayfarer = AllEncounters.First(e => e.Id == "r1_duel_wayfarer");
        Assert.Equal("The Wayfarer", wayfarer.Name);
        Assert.Equal(30, wayfarer.ShardReward);
        Assert.Equal(3, wayfarer.DialogueIntro!.Count);
        Assert.Equal(2, wayfarer.DialogueOutro!.Count);
    }

    [Fact]
    public void WardenAelin_HasCorrectRewards()
    {
        var aelin = AllEncounters.First(e => e.Id == "r1_warden_aelin");
        Assert.Equal("Warden Aelin", aelin.Name);
        Assert.Equal(150, aelin.ShardReward);
        Assert.Equal(3, aelin.DigChargeReward);
        Assert.Equal("dawn:3", aelin.FragmentReward);
    }

    [Fact]
    public void BossAelin_HasHighestRewards()
    {
        var boss = AllEncounters.First(e => e.Id == "r1_boss_warden_aelin");
        Assert.Equal(300, boss.ShardReward);
        Assert.Equal(5, boss.DigChargeReward);
        Assert.Equal("dawn:5", boss.FragmentReward);
    }

    [Fact]
    public void FromString_DeserializesCorrectly()
    {
        const string json = """
        {
            "encounters": [
                {
                    "id": "test_encounter",
                    "name": "Test Wielder",
                    "deck": ["vrd_c_root_warden", "emb_c_ember_hound"],
                    "dialogue_intro": ["Hello."],
                    "dialogue_outro": ["Goodbye."],
                    "shard_reward": 50,
                    "dig_charge_reward": 1
                }
            ]
        }
        """;
        var pack = EncounterLoader.LoadPackFromString(json);
        Assert.Single(pack.Encounters);
        Assert.Equal("test_encounter", pack.Encounters[0].Id);
        Assert.Equal("Test Wielder", pack.Encounters[0].Name);
        Assert.Equal(2, pack.Encounters[0].Deck.Count);
        Assert.Equal(50, pack.Encounters[0].ShardReward);
        Assert.Equal(1, pack.Encounters[0].DigChargeReward);
    }

    [Fact]
    public void AllMapNodeEncounters_ResolveToDefinedEncounters()
    {
        // Every combat map node (Duel/Elite/Warden/WardenBoss) that references an
        // encounter must have a matching definition. This proves the wiring between
        // map layout and encounter data. Dig/Shrine/Merchant/Cache nodes reference
        // other content types (dig sites, etc.) and are validated separately.
        var encounterIds = AllEncounters.Select(e => e.Id).ToHashSet();

        var combatNodes = Region.Nodes.Where(n =>
            n.Type is MapNodeType.Duel or MapNodeType.Elite
                or MapNodeType.Warden or MapNodeType.WardenBoss);

        Assert.NotEmpty(combatNodes); // guard: the map must actually have combat nodes
        foreach (var node in combatNodes)
        {
            Assert.NotNull(node.Encounter);
            Assert.True(encounterIds.Contains(node.Encounter),
                $"Map node {node.Id} references encounter '{node.Encounter}' which is not defined. "
                + $"Check content/encounters/ for the missing definition.");
        }
    }

    [Fact]
    public void AllEncounters_HavePortraitSlot()
    {
        // Portrait paths are nullable; the slot must exist so art drops in without a refactor.
        foreach (var e in AllEncounters)
        {
            Assert.NotNull(e.Portrait);
            Assert.NotEmpty(e.Portrait);
        }
    }

    private static readonly MapRegion Region = MapLoader.LoadRegion(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "content", "map", "region_01.json"));

    [Fact]
    public void AllEncounterDeckCards_ExistInCardRegistry()
    {
        // Register every card pack (no Clear — other tests may have registered their
        // own fixtures in the shared static registry). Then validate that every deck
        // reference resolves. This is the mechanical guarantee behind "legible per
        // archetype" — a deck referencing missing cards can never be played.
        var setIds = new[] { "verdant", "ember", "tide", "hollow", "dawn", "tutorial_pack" };
        foreach (var setId in setIds)
        {
            var pack = CardLoader.LoadPack(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "content", "cards", $"{setId}.json"));
            CardRegistry.RegisterRange(pack);
        }

        foreach (var e in AllEncounters)
        {
            foreach (var cardId in e.Deck)
            {
                Assert.True(CardRegistry.Get(cardId) != null,
                    $"Encounter {e.Id} deck references '{cardId}' which is not in any card pack.");
            }
        }
    }

    [Fact]
    public void AllEncounters_HaveAtLeastThreeDrops()
    {
        foreach (var e in AllEncounters)
        {
            // Must have drops array defined
            Assert.NotNull(e.Drops);
            // Must have at least 3 non-guaranteed drop entries (rate < 1.0)
            int nonGuaranteed = e.Drops.Count(d => d.Rate < 1.0);
            Assert.True(nonGuaranteed >= 3,
                $"Encounter {e.Id} has {nonGuaranteed} non-guaranteed drops (minimum 3).");
        }
    }

    [Fact]
    public void AllDrops_ReferenceValidCardIds()
    {
        // Register card packs for lookup
        var setIds = new[] { "verdant", "ember", "tide", "hollow", "dawn", "tutorial_pack" };
        foreach (var setId in setIds)
        {
            var pack = CardLoader.LoadPack(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "content", "cards", $"{setId}.json"));
            CardRegistry.RegisterRange(pack);
        }

        foreach (var e in AllEncounters)
        {
            if (e.Drops == null) continue;
            foreach (var drop in e.Drops)
            {
                Assert.True(CardRegistry.Get(drop.CardId) != null,
                    $"Encounter {e.Id} drop references '{drop.CardId}' which is not in any card pack.");
            }
        }
    }

    [Fact]
    public void BossEncounters_HaveSignatureDropAt100Percent()
    {
        var bossEncounters = AllEncounters.Where(e => e.Id == "r1_warden_aelin" || e.Id == "r1_boss_warden_aelin");
        foreach (var e in bossEncounters)
        {
            Assert.NotNull(e.Drops);
            bool hasGuaranteed = e.Drops.Exists(d => d.Rate >= 0.999);
            Assert.True(hasGuaranteed,
                $"Boss encounter {e.Id} has no guaranteed (rate=1.00) drop.");
        }
    }

    [Fact]
    public void DropRoller_ProducesDeterministicResults()
    {
        var aelin = AllEncounters.First(e => e.Id == "r1_warden_aelin");
        var result1 = DropRoller.Roll(aelin, 42);
        var result2 = DropRoller.Roll(aelin, 42);
        Assert.Equal(result1.Count, result2.Count);
        // With same seed, results must be identical
        for (int i = 0; i < result1.Count; i++)
            Assert.Equal(result1[i], result2[i]);

        // Different seed should produce different results (extremely high probability)
        var result3 = DropRoller.Roll(aelin, 9999);
        // At least the guaranteed drop (rate=1.00) should always appear regardless of seed
        Assert.Contains("vrd_r_bloomweaver", result3);
    }

    [Fact]
    public void DropRoller_AlwaysIncludesGuaranteedDrops()
    {
        foreach (var e in AllEncounters)
        {
            if (e.Drops == null || e.Drops.Count == 0) continue;
            var result = DropRoller.Roll(e, 12345);
            // Every guaranteed drop (rate >= 1.0) must appear
            foreach (var drop in e.Drops)
            {
                if (drop.Rate >= 1.0)
                    Assert.True(result.Contains(drop.CardId),
                        $"Encounter {e.Id} guaranteed drop '{drop.CardId}' missing from roll results.");
            }
        }
    }
}
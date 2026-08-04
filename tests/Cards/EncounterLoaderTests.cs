using System.IO;
using System.Linq;
using Runewake.Engine.Cards;
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
}
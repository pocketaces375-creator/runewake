using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Engine.Tower;
using Xunit;

namespace Runewake.Tests.World;

/// <summary>FABLE-021: boss rules bend a fight the way the floor file says, and nothing else.</summary>
public class BossRulesTests
{
    private static List<string> Deck()
    {
        var root = WorldGeneratorTests.Root();
        var cards = Directory.GetFiles(Path.Combine(root, "content", "cards"), "*.json").SelectMany(CardLoader.LoadPack).ToList();
        CardRegistry.Clear();
        CardRegistry.RegisterRange(cards);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "client", "content", "decks", "starter_decks.json")));
        return doc.RootElement.GetProperty("starters")[0].GetProperty("cards").EnumerateArray().Select(x => x.GetString()!).ToList();
    }

    private static GameState Start(params string[] rules)
    {
        var d = Deck();
        var s = GameState.Initialize(new GameConfig
        {
            Seed = 7, Player0DeckIds = d, Player1DeckIds = d, Player1StartingVigor = 40, BossRules = rules,
        });
        s.Players[0].HasMulliganed = true;
        s.Players[1].HasMulliganed = true;
        return s;
    }

    private static GameState End(GameState s) => DuelEngine.Apply(s, new EndTurnAction { PlayerIndex = s.CurrentPlayerIndex });

    [Fact]
    public void Rules_Parse_Validate_And_Describe()
    {
        Assert.True(BossRules.IsValid("extra_draw"));
        Assert.True(BossRules.IsValid("regen:2"));
        Assert.True(BossRules.IsValid("crumble:14:2"));
        Assert.False(BossRules.IsValid("crumble:14"), "crumble needs two numbers");
        Assert.False(BossRules.IsValid("fly"), "unknown rule");
        Assert.Contains("heals 3", BossRules.Describe("regen:3"));
    }

    [Fact]
    public void Challenger_Vigor_Sets_The_Players_Start()
    {
        var s = Start("challenger_vigor:18");
        Assert.Equal(18, s.Players[0].Vigor);
        Assert.Equal(18, s.Players[0].MaxVigor);
        Assert.Equal(40, s.Players[1].Vigor);
    }

    [Fact]
    public void Extra_Draw_Gives_The_Boss_One_More_Card_Each_Turn()
    {
        var plain = End(Start());
        var bent = End(Start("extra_draw"));
        Assert.Equal(plain.Players[1].Hand.Count + 1, bent.Players[1].Hand.Count);
        Assert.Equal(plain.Players[0].Hand.Count, bent.Players[0].Hand.Count);
    }

    [Fact]
    public void Regen_Heals_The_Boss_But_Never_Past_Its_Max()
    {
        var s = Start("regen:5");
        s.Players[1].Vigor = 30;
        s = End(s);                                  // boss's turn begins → +5
        Assert.Equal(35, s.Players[1].Vigor);
        s = End(End(s));
        Assert.Equal(40, s.Players[1].Vigor);
        s = End(End(s));
        Assert.Equal(40, s.Players[1].Vigor);
    }

    [Fact]
    public void Crumble_Starts_On_Its_Turn_And_Can_End_The_Fight()
    {
        var s = Start("crumble:3:4");
        int before = s.Players[0].Vigor;
        s = End(End(s));                             // turn 2 begins for the challenger: nothing yet
        Assert.Equal(before, s.Players[0].Vigor);
        s = End(End(s));                             // turn 3: −4
        Assert.Equal(before - 4, s.Players[0].Vigor);
        s.Players[0].Vigor = 3;
        s = End(End(s));
        Assert.True(s.IsGameOver, "the floor gave way");
        Assert.Equal(1, s.WinnerIndex);
    }

    [Fact]
    public void Rules_Survive_Clone_And_Floor_One_Is_Valid()
    {
        var s = Start("regen:2", "extra_draw");
        var c = s.Clone();
        Assert.Equal(2, c.BossRules.Count);
        var floor = TowerFloorDef.FromJson(File.ReadAllText(Path.Combine(WorldGeneratorTests.Root(), "content", "tower", "floor_001.json")));
        Assert.Empty(floor.Validate(id => CardRegistry.Get(id) != null));
        Assert.True(floor.Raid.PoolFor(1) < floor.Raid.PoolFor(5), "a solo raid faces a smaller pool");
        Assert.Equal(floor.Raid.PoolFor(5), floor.Raid.PoolFor(9));
    }
}

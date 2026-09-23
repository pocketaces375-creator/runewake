using System.Collections.Generic;
using System.IO;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.Tower;
using Xunit;

namespace Runewake.Tests.World;

/// <summary>FABLE-020: Tower floors are valid, expansive, and the boss needs every key.</summary>
public class TowerTests
{
    private static TowerFloorDef Floor1() =>
        TowerFloorDef.FromJson(File.ReadAllText(Path.Combine(WorldGeneratorTests.Root(), "content", "tower", "floor_001.json")));

    [Fact]
    public void Floor_1_Is_Valid_With_Real_Cards()
    {
        var ids = WorldGeneratorTests.Cards().Select(c => c.Id).ToHashSet();
        var errs = Floor1().Validate(ids.Contains);
        Assert.True(errs.Count == 0, string.Join("; ", errs));
    }

    [Fact]
    public void Floor_1_Is_Expansive_And_Themed()
    {
        var f = Floor1();
        Assert.True(f.Places.Count >= 20, $"{f.Places.Count} places");
        Assert.True(f.Wings.Count >= 4, "several wings");
        Assert.True(f.Places.Count(p => p.Kind == TowerPlaceKind.Keeper) == 3, "three keepers");
        Assert.True(f.Places.Any(p => p.Kind == TowerPlaceKind.Lore), "lore to find");
        Assert.True(f.Places.Any(p => p.Kind == TowerPlaceKind.Event), "events");
        Assert.Equal(100, f.UnlockThreshold);
        Assert.True(f.Raid.MaxPlayers == 5 && f.Raid.SharedPool > 0, "a five-player raid with a shared pool");
    }

    [Fact]
    public void The_Boss_Opens_Only_When_Every_Keeper_Falls()
    {
        var f = Floor1();
        var cleared = new HashSet<string>(f.Places.Where(p => p.Kind != TowerPlaceKind.Boss && p.Id != "f7").Select(p => p.Id));
        Assert.False(TowerProgress.IsOpen(f, f.Boss, cleared), "two keys are not enough");
        cleared.Add("f7");
        Assert.True(TowerProgress.IsOpen(f, f.Boss, cleared), "three keys open the Colossus");
    }

    [Fact]
    public void You_Start_At_The_Courtyard_And_Branch_Into_Three_Wings()
    {
        var f = Floor1();
        var open = TowerProgress.Open(f, new HashSet<string>()).Select(p => p.Id).ToHashSet();
        Assert.True(open.SetEquals(new[] { "c1", "c2" }), string.Join(",", open));
        var after = TowerProgress.Open(f, new HashSet<string> { "c1", "c2", "c3" }).Select(p => p.Wing).ToHashSet();
        Assert.True(after.IsSupersetOf(new[] { "thorn", "ossuary", "flooded" }), "all three wings open from the Hall");
    }

    [Fact]
    public void Validation_Catches_Broken_Floors()
    {
        var f = Floor1();
        f.Places.First(p => p.Id == "t1").Next.Add("nowhere");
        f.Places.First(p => p.Id == "c1").Encounter!.Deck.RemoveAt(0);
        var errs = f.Validate();
        Assert.True(errs.Any(e => e.Contains("unknown place nowhere")), "dangling road");
        Assert.True(errs.Any(e => e.Contains("29 cards")), "short deck");
    }

    [Fact]
    public void Round_Trips_Through_Json()
    {
        var f = Floor1();
        var g = TowerFloorDef.FromJson(f.ToJson());
        Assert.Equal(f.Places.Count, g.Places.Count);
        Assert.Equal(f.Boss.Encounter!.Deck.Count, g.Boss.Encounter!.Deck.Count);
        Assert.Equal(f.Raid.SharedPool, g.Raid.SharedPool);
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Tests.World;
using Xunit;

namespace Runewake.Tests.Cards;

/// <summary>FABLE-068: every class starts in its own land, on the same node graph.</summary>
public class StartZoneTests
{
    private static string Root => WorldGeneratorTests.Root();
    private static StartZoneSet Zones => StartZoneSet.Parse(File.ReadAllText(Path.Combine(Root, "client", "content", "start_zones.json")));
    private static MapRegion Region1() => MapLoader.LoadRegion(Path.Combine(Root, "client", "content", "map", "region_01.json"));
    private static Dictionary<string, EncounterDef> Encounters() =>
        EncounterLoader.LoadPack(Path.Combine(Root, "client", "content", "encounters", "start_zones.json")).Encounters.ToDictionary(e => e.Id);

    private static readonly string[] Classes = { "battlemage", "necromancer", "paladin", "druid", "rogue", "astrologist", "warrior" };

    [Fact]
    public void Every_class_has_its_own_zone_with_its_own_name()
    {
        var z = Zones;
        foreach (var c in Classes) Assert.NotNull(z.For(c));
        Assert.Equal(Classes.Length, Classes.Select(c => z.For(c)!.Name).Distinct().Count());
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "client", "content", "classes.json")));
        foreach (var cls in doc.RootElement.EnumerateArray())
            Assert.NotNull(z.For(cls.GetProperty("id").GetString()));
    }

    [Fact]
    public void Dressing_keeps_the_graph_and_changes_what_stands_on_it()
    {
        var plain = Region1();
        foreach (var c in Classes)
        {
            var r = Zones.Apply(Region1(), c);
            Assert.Equal("region_01", r.Id);
            Assert.Equal(Zones.For(c)!.Name, r.Name);
            Assert.Equal(plain.Nodes.Select(n => n.Id), r.Nodes.Select(n => n.Id));
            Assert.Equal(plain.Nodes.Select(n => n.Type), r.Nodes.Select(n => n.Type));
            Assert.Equal(plain.Nodes.Select(n => string.Join(",", n.Connects)), r.Nodes.Select(n => string.Join(",", n.Connects)));
            Assert.Equal(plain.Nodes.Select(n => string.Join(",", n.Unlock?.Value ?? new())), r.Nodes.Select(n => string.Join(",", n.Unlock?.Value ?? new())));
            foreach (var n in r.Nodes)
            {
                Assert.InRange(n.Position[0], 60, 1476);    // on the 1536×704 map plate
                Assert.InRange(n.Position[1], 60, 650);
                if (n.Type == MapNodeType.Dig) Assert.Equal("region_01_dig", n.Encounter);
                else if (n.Encounter != null) Assert.StartsWith($"sz_{c}_", n.Encounter);
            }
        }
    }

    [Fact]
    public void Other_regions_and_unknown_classes_are_untouched()
    {
        var r = Zones.Apply(Region1(), "nobody");
        Assert.Equal("The Fallow Reach", r.Name);
        Assert.Equal("r1_duel_wayfarer", r.Nodes[0].Encounter);
        var r2 = MapLoader.LoadRegion(Path.Combine(Root, "client", "content", "map", "region_02.json"));
        string before = r2.Name;
        Assert.Equal(before, Zones.Apply(r2, "druid").Name);
    }

    [Fact]
    public void Zone_encounters_are_complete_fights()
    {
        var all = Encounters();
        var cardIds = Directory.GetFiles(Path.Combine(Root, "content", "cards"), "*.json").SelectMany(CardLoader.LoadPack).Select(c => c.Id).ToHashSet();
        var wayfarer = EncounterLoader.LoadPack(Path.Combine(Root, "content", "encounters", "region_01_early.json")).Encounters.First(e => e.Id == "r1_duel_wayfarer");
        foreach (var c in Classes)
            foreach (var (node, id) in Zones.For(c)!.Encounters)
            {
                Assert.True(all.TryGetValue(id, out var e), $"{c} {node}: no encounter {id}");
                Assert.Equal(30, e!.Deck.Count);
                Assert.All(e.Deck, d => Assert.Contains(d, cardIds));
                Assert.All(e.Drops, d => Assert.Contains(d.CardId, cardIds));
                Assert.True(e.Drops.Count(d => d.Rate < 1.0) >= 3, id);
                Assert.NotEmpty(e.DialogueIntro!);
                Assert.True(e.ShardReward > 0);
                Assert.True(string.IsNullOrEmpty(e.Modifier) || e.Modifier is "ELITE" or "WARDEN");
                Assert.InRange(e.EnemyVigor ?? 25, 14, 22);   // the gentle ramp
                if (node == "r1_n01")
                {
                    Assert.True(e.IsTutorial);
                    Assert.Equal(wayfarer.Deck, e.Deck);       // the guided first duel scripts against this exact deck
                    Assert.Null(e.Lesson);
                }
                else
                {
                    Assert.False(e.IsTutorial);
                    Assert.Equal(2, e.Lesson!.Count);
                }
                if (node is "r1_n11" or "r1_n12") Assert.Contains(e.Drops, d => d.Rate >= 1.0);
                if (node == "r1_n12") Assert.Equal("root_choked", e.OpeningRule);
            }
    }

    [Fact]
    public void Only_the_guided_first_duels_are_tutorials()
    {
        Assert.Equal(Classes.Length, Encounters().Values.Count(e => e.IsTutorial));
    }
}

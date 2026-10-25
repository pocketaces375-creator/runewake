using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Runewake.Engine.State;
using Runewake.Engine.World;
using Xunit;

namespace Runewake.Tests.World;

/// <summary>
/// FABLE-105: the terrain rules. The painting, the places and the names of every world page come from one
/// plan: the prompt says where each feature is, the places stand on those features, and are named for them.
/// </summary>
[Collection("NonParallel")]
public class TerrainTests
{
    private static TerrainLexicon Lexicon() =>
        TerrainLexicon.FromJson(File.ReadAllText(Path.Combine(WorldGeneratorTests.Root(), "content", "world", "terrain.json")));

    private static WorldGenerator Gen() => new(WorldGeneratorTests.Atlas(), Lexicon());

    [Fact]
    public void Lexicon_Loads_AndEveryAtlasBiomeHasTerrain()
    {
        var lex = Lexicon();
        var atlas = WorldGeneratorTests.Atlas();
        foreach (var b in atlas.Biomes)
            Assert.True(lex.Biomes.ContainsKey(b.Id), $"terrain.json has no biome entry for '{b.Id}'");
        foreach (var (id, f) in lex.Features)
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Paint), $"{id} has no paint phrase");
            Assert.Contains(f.Spots, s => s.Fits.Contains("Duel"));   // every feature can hold an ordinary fight
        }
        // every biome's keep can hold the guardian
        foreach (var (id, b) in lex.Biomes)
            Assert.Contains(lex.Features[b.Keep].Spots, s => s.Fits.Contains("Warden") || s.Fits.Contains("AreaBoss"));
    }

    [Fact]
    public void Plan_IsDeterministic_AndKeepStandsInTheEast()
    {
        var lex = Lexicon();
        var a = TerrainPlanner.Plan(new SeededRng(42), lex, "elvenwood", "The Elvenwood", "VERDANT");
        var b = TerrainPlanner.Plan(new SeededRng(42), lex, "elvenwood", "The Elvenwood", "VERDANT");
        Assert.Equal(a.Prompt, b.Prompt);
        Assert.Equal(a.Spots.Select(s => (s.X, s.Y, s.Kind)), b.Spots.Select(s => (s.X, s.Y, s.Kind)));
        var keep = a.Keep;
        Assert.NotNull(keep);
        Assert.True(keep!.X0 >= 600, "the keep belongs in the east third, where the guardian waits");
        Assert.Contains("forest", a.Features.Select(f => f.Id));   // the Elvenwood always has a forest
        Assert.InRange(a.Features.Count, 5, 9);
    }

    [Fact]
    public void Prompt_NamesEveryFeatureInItsCell()
    {
        var lex = Lexicon();
        var plan = TerrainPlanner.Plan(new SeededRng(7), lex, "deepdelve", "The Deepdelve Mines", "EMBER");
        Assert.StartsWith(lex.Painter.Trigger, plan.Prompt);
        foreach (var f in plan.Features)
        {
            Assert.Contains(f.Paint, plan.Prompt);
            Assert.Contains("in the " + f.Cells, plan.Prompt);
        }
        Assert.DoesNotContain("Strata", plan.Prompt);   // the painter is never told about the strata gems
        Assert.Equal(lex.Painter.Negative, plan.NegativePrompt);
    }

    [Fact]
    public void Spots_StayInsideTheirFeature_AndAreUnique()
    {
        var lex = Lexicon();
        foreach (var biome in lex.Biomes.Keys)
        {
            var plan = TerrainPlanner.Plan(new SeededRng(StableHash.Of(biome)), lex, biome, biome, "DAWN");
            var seen = new HashSet<(int, int)>();
            foreach (var f in plan.Features)
            {
                Assert.True(f.Spots.Count >= 5, $"{biome}/{f.Id} has only {f.Spots.Count} spots");
                foreach (var s in f.Spots)
                {
                    Assert.InRange(s.X, f.X0, f.X1); Assert.InRange(s.Y, f.Y0, f.Y1);
                    Assert.True(seen.Add((s.X, s.Y)), $"{biome}: two spots at {s.X},{s.Y}");
                }
            }
        }
    }

    [Fact]
    public void Page_PlacesStandOnTerrain_AndAreNamedForIt()
    {
        var g = Gen();
        var lex = g.Terrain;
        var addr = new PageAddress("elvenwood", 0, 1);
        var page = g.Page(addr);
        Assert.NotNull(page.Terrain);
        int onSpot = 0;
        var names = new HashSet<string>();
        foreach (var b in page.Blips)
        {
            if (b.Kind is BlipKind.Warden or BlipKind.AreaBoss) { Assert.Equal(page.Terrain!.Keep!.Id, b.Terrain); continue; }
            if (b.Kind == BlipKind.Gateway) continue;
            Assert.False(string.IsNullOrEmpty(b.Terrain), $"{b.Id} stands on nothing");
            Assert.True(lex.Features.ContainsKey(b.Terrain!), $"{b.Id} stands on unknown terrain {b.Terrain}");
            var spot = lex.Features[b.Terrain!].Spots.First(s => s.Kind == b.Spot);
            Assert.Contains(spot.Nouns, n => b.Name.EndsWith(n) || b.Name.Contains(n + " "));
            Assert.True(names.Add(b.Name), $"duplicate place name {b.Name}");
            if (page.Terrain!.Spots.Any(s => s.X == b.X && s.Y == b.Y)) onSpot++;
            Assert.InRange(b.X, 30, 900); Assert.InRange(b.Y, 30, 570);
        }
        Assert.True(onSpot >= page.Blips.Count / 2, $"only {onSpot} of {page.Blips.Count} places found a terrain spot");
    }

    [Fact]
    public void Page_TheChoosyKindsGetFittingSpots()
    {
        var g = Gen();
        var lex = g.Terrain;
        int checkedPlaces = 0;
        foreach (var biome in new[] { "elvenwood", "deepdelve", "saltglass", "barrowmarch", "sunscar" })
            for (int pg = 0; pg < 3; pg++)
            {
                var page = g.Page(new PageAddress(biome, 0, pg));
                foreach (var b in page.Blips.Where(b => b.Kind is BlipKind.Elite or BlipKind.Event or BlipKind.Lore))
                {
                    if (b.Terrain == null || b.Spot == null) continue;
                    var spot = lex.Features[b.Terrain].Spots.First(s => s.Kind == b.Spot);
                    string fit = TerrainNames.FitKey(b.Kind, b.Event);
                    // a place that claimed a spot was promised a fit; a place that fell back to the nearest feature
                    // only gets one when the feature has one — either way an Elite is never "at the Market"
                    if (page.Terrain!.Spots.Any(s => s.X == b.X && s.Y == b.Y)) Assert.Contains(fit, spot.Fits);
                    checkedPlaces++;
                }
            }
        Assert.True(checkedPlaces > 20);
    }

    [Fact]
    public void Page_TerrainChangesNothingThatDiscoveriesHangOff()
    {
        // ids, kinds, roads and the guardian are from the old stream; only x/y/name moved onto the terrain
        var plain = new WorldGenerator(WorldGeneratorTests.Atlas(), TerrainLexicon.BuiltIn()).Page(new PageAddress("saltglass", 2, 1));
        var full = Gen().Page(new PageAddress("saltglass", 2, 1));
        Assert.Equal(plain.Blips.Count, full.Blips.Count);
        for (int i = 0; i < plain.Blips.Count; i++)
        {
            Assert.Equal(plain.Blips[i].Id, full.Blips[i].Id);
            Assert.Equal(plain.Blips[i].Kind, full.Blips[i].Kind);
            Assert.Equal(plain.Blips[i].Event, full.Blips[i].Event);
            Assert.Equal(plain.Blips[i].Next, full.Blips[i].Next);
            Assert.Equal(plain.Blips[i].Column, full.Blips[i].Column);
        }
        Assert.Equal(plain.Guardian.Name, full.Guardian.Name);
        Assert.Equal((955, 300), (full.Guardian.X, full.Guardian.Y));
    }

    [Fact]
    public void Page_PlacesKeepTheirColumnOrder()
    {
        var g = Gen();
        var page = g.Page(new PageAddress("barrowmarch", 1, 0));
        foreach (var grp in page.Blips.Where(b => b.Kind != BlipKind.Warden && b.Kind != BlipKind.AreaBoss).GroupBy(b => b.Column))
        {
            // every place in a column stays within the column's band of the canvas (a road never runs backwards)
            double bandX = 70 + grp.Key * (840.0 / (page.Blips.Max(b => b.Column)));
            foreach (var b in grp) Assert.InRange(b.X, bandX - 170, bandX + 170);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.World;
using Xunit;

namespace Runewake.Tests.World;

/// <summary>FABLE-020: the shared endless world must be stable, connected, sized as specified, and endless.</summary>
public class WorldGeneratorTests
{
    internal static string Root()
    {
        var env = Environment.GetEnvironmentVariable("RUNEWAKE_ROOT");
        if (!string.IsNullOrEmpty(env)) return env;
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "content", "world", "atlas.json"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("repo root with content/world/atlas.json");
    }

    internal static AtlasDef Atlas() => AtlasDef.FromJson(File.ReadAllText(Path.Combine(Root(), "content", "world", "atlas.json")));

    internal static List<CardDef> Cards() =>
        Directory.GetFiles(Path.Combine(Root(), "content", "cards"), "*.json").SelectMany(CardLoader.LoadPack).ToList();

    [Fact]
    public void Atlas_Loads_And_Validates()
    {
        var a = Atlas();
        Assert.True(a.Biomes.Count >= 5, "at least 5 biomes");
        Assert.True(a.OpenBiomes.Count() >= 3, "several open biomes at the Crossroads");
        Assert.True(a.Biomes.Any(b => b.Hidden), "at least one hidden biome behind a gateway");
        Assert.True(a.Biomes.Any(b => b.Gateways.Any(g => g.To == "lost_forge")), "the Deepdelve Mines can lead to the Ancient Lost Forge");
    }

    [Fact]
    public void Same_Address_Same_Page_On_Every_Generator()
    {
        var a = Atlas();
        var p1 = new WorldGenerator(a).Page(new PageAddress("elvenwood", 3, 2));
        var p2 = new WorldGenerator(Atlas()).Page(new PageAddress("elvenwood", 3, 2));
        Assert.Equal(p1.Blips.Count, p2.Blips.Count);
        for (int i = 0; i < p1.Blips.Count; i++)
        {
            Assert.Equal(p1.Blips[i].Id, p2.Blips[i].Id);
            Assert.Equal(p1.Blips[i].Name, p2.Blips[i].Name);
            Assert.Equal(p1.Blips[i].Kind, p2.Blips[i].Kind);
            Assert.Equal(string.Join(",", p1.Blips[i].Next), string.Join(",", p2.Blips[i].Next));
            Assert.Equal(p1.Blips[i].X, p2.Blips[i].X);
        }
    }

    [Fact]
    public void Different_Places_Differ()
    {
        var g = new WorldGenerator(Atlas());
        var a = g.Page(new PageAddress("elvenwood", 0, 0));
        var b = g.Page(new PageAddress("elvenwood", 0, 1));
        var c = g.Page(new PageAddress("deepdelve", 0, 0));
        Assert.False(a.Blips.Select(x => x.Name).SequenceEqual(b.Blips.Select(x => x.Name)));
        Assert.False(a.Blips.Select(x => x.Name).SequenceEqual(c.Blips.Select(x => x.Name)));
    }

    [Fact]
    public void Pages_And_Blip_Counts_Match_The_Atlas()
    {
        var atlas = Atlas();
        var g = new WorldGenerator(atlas);
        foreach (var biome in atlas.Biomes)
            for (int inst = 0; inst < 4; inst++)
            {
                int pages = g.PagesInArea(new AreaAddress(biome.Id, inst));
                Assert.True(pages >= biome.PagesMin && pages <= biome.PagesMax, $"{biome.Id}.{inst}: {pages} pages");
                for (int p = 0; p < pages; p++)
                {
                    var page = g.Page(new PageAddress(biome.Id, inst, p));
                    Assert.True(page.Blips.Count >= biome.BlipsMin && page.Blips.Count <= biome.BlipsMax, $"{page.Address}: {page.Blips.Count} blips");
                }
            }
    }

    [Fact]
    public void Every_Place_Is_Reachable_From_An_Entry_And_Leads_To_The_Guardian()
    {
        var atlas = Atlas();
        var g = new WorldGenerator(atlas);
        foreach (var biome in atlas.Biomes)
            for (int p = 0; p < 3; p++)
            {
                var page = g.Page(new PageAddress(biome.Id, 1, p));
                // forward reachability from entries
                var seen = new HashSet<int>(page.Blips.Where(b => b.IsEntry).Select(b => b.Index));
                var q = new Queue<int>(seen);
                while (q.Count > 0) foreach (var n in page.Blips[q.Dequeue()].Next) if (seen.Add(n)) q.Enqueue(n);
                Assert.Equal(page.Blips.Count, seen.Count);
                // backward: every place can reach the guardian
                int gi = page.Guardian.Index;
                foreach (var b in page.Blips)
                {
                    var s2 = new HashSet<int> { b.Index }; var q2 = new Queue<int>(s2); bool ok = b.Index == gi;
                    while (q2.Count > 0 && !ok) foreach (var n in page.Blips[q2.Dequeue()].Next) { if (n == gi) ok = true; if (s2.Add(n)) q2.Enqueue(n); }
                    Assert.True(ok, $"{b.Id} cannot reach the guardian");
                }
                Assert.True(page.Blips.Count(b => b.IsEntry) >= 2, "2+ entries");
                Assert.True(page.Blips.All(b => b.Next.All(n => page.Blips[n].Column > b.Column)), "roads only run forward");
            }
    }

    [Fact]
    public void Guardian_Is_Warden_Then_AreaBoss_On_The_Last_Page()
    {
        var g = new WorldGenerator(Atlas());
        var area = new AreaAddress("deepdelve", 2);
        int pages = g.PagesInArea(area);
        for (int p = 0; p < pages; p++)
        {
            var page = g.Page(new PageAddress(area.Biome, area.Instance, p));
            Assert.Equal(p == pages - 1 ? BlipKind.AreaBoss : BlipKind.Warden, page.Guardian.Kind);
            Assert.Equal(1, page.Blips.Count(b => b.Kind is BlipKind.Warden or BlipKind.AreaBoss));
        }
    }

    [Fact]
    public void The_Road_Never_Ends()
    {
        var g = new WorldGenerator(Atlas());
        var addr = new PageAddress("saltglass", 0, 0);
        int areas = 0;
        for (int step = 0; step < 150; step++)   // ~25 areas deep
        {
            var page = g.Page(addr);
            Assert.True(page.Blips.Count > 0, "page has places");
            var next = g.NextPage(addr);
            if (next.Instance != addr.Instance) areas++;
            Assert.Equal(addr, g.PreviousPage(next)!.Value);
            addr = next;
        }
        Assert.True(areas >= 20, $"walked through {areas} areas");
    }

    [Fact]
    public void Difficulty_Rises_With_Depth_But_Stays_Below_One()
    {
        var g = new WorldGenerator(Atlas());
        double Avg(int inst) => Enumerable.Range(0, 3).SelectMany(p => g.Page(new PageAddress("elvenwood", inst, p)).Blips).Average(b => b.Difficulty);
        double d0 = Avg(0), d3 = Avg(3), d20 = Avg(20);
        Assert.True(d0 < d3 && d3 < d20, $"{d0:F2} < {d3:F2} < {d20:F2}");
        Assert.True(d20 < 1.0, "bounded");
    }

    [Fact]
    public void Lore_Is_Rare_And_Gateways_Are_Rarer()
    {
        var atlas = Atlas();
        var g = new WorldGenerator(atlas);
        int total = 0, lore = 0, gates = 0;
        for (int inst = 0; inst < 12; inst++)
            for (int p = 0; p < g.PagesInArea(new AreaAddress("deepdelve", inst)); p++)
            {
                var page = g.Page(new PageAddress("deepdelve", inst, p));
                total += page.Blips.Count;
                lore += page.Blips.Count(b => b.Kind == BlipKind.Lore);
                gates += page.Blips.Count(b => b.Kind == BlipKind.Gateway);
                Assert.True(page.Blips.Count(b => b.Kind == BlipKind.Gateway) <= 1, "at most one gateway per page");
                Assert.True(page.Blips.Where(b => b.Kind == BlipKind.Gateway).All(b => b.GatewayTo == "lost_forge"), "mines gateways lead to the Lost Forge");
            }
        double loreRate = (double)lore / total;
        Assert.True(loreRate > 0.002 && loreRate < 0.03, $"lore rate {loreRate:P2} (~1 in 100)");
        Assert.True(gates < lore + 5, $"gateways ({gates}) rarer than lore ({lore})");
    }

    [Fact]
    public void Blip_Ids_Round_Trip()
    {
        var g = new WorldGenerator(Atlas());
        var b = g.Page(new PageAddress("barrowmarch", 7, 3)).Blips[5];
        Assert.True(WorldGenerator.TryParseBlipId(b.Id, out int v, out var page, out int idx), "parses");
        Assert.Equal(1, v);
        Assert.Equal(new PageAddress("barrowmarch", 7, 3), page);
        Assert.Equal(5, idx);
        Assert.False(WorldGenerator.TryParseBlipId("r1_n01", out _, out _, out _));
    }

    [Fact]
    public void Encounters_Are_Playable_Stable_And_Scale()
    {
        var atlas = Atlas();
        var g = new WorldGenerator(atlas);
        var pool = EncounterForge.PlayablePool(Cards());
        var ids = pool.Select(c => c.Id).ToHashSet();
        var shallow = g.Page(new PageAddress("elvenwood", 0, 0));
        var deep = g.Page(new PageAddress("elvenwood", 15, 2));
        foreach (var page in new[] { shallow, deep })
            foreach (var b in page.Blips.Where(EncounterForge.IsFight))
            {
                var e = EncounterForge.For(atlas, page, b, pool);
                Assert.Equal(EncounterForge.DeckSize, e.Deck.Count);
                Assert.Equal(e.Deck.Count, e.Deck.Distinct().Count());
                Assert.True(e.Deck.All(ids.Contains), "deck uses only playable cards");
                Assert.True(!string.IsNullOrWhiteSpace(e.Name), "named");
                var again = EncounterForge.For(atlas, page, b, pool);
                Assert.Equal(string.Join(",", e.Deck), string.Join(",", again.Deck));
            }
        double AvgRare(WorldPage page) => page.Blips.Where(EncounterForge.IsFight)
            .Select(b => EncounterForge.For(atlas, page, b, pool))
            .Average(e => e.Deck.Count(id => pool.First(c => c.Id == id).Rarity >= Rarity.RARE));
        double AvgVigor(WorldPage page) => page.Blips.Where(EncounterForge.IsFight)
            .Select(b => EncounterForge.For(atlas, page, b, pool)).Average(e => e.EnemyVigor ?? 25);
        Assert.True(AvgRare(deep) > AvgRare(shallow), $"deep decks carry more rares ({AvgRare(deep):F1} vs {AvgRare(shallow):F1})");
        Assert.True(AvgVigor(deep) > AvgVigor(shallow), "deep foes have more Vigor");
        var boss = EncounterForge.For(atlas, deep, deep.Guardian, pool);
        Assert.Equal(1.0, boss.Drops[0].Rate);
    }
}

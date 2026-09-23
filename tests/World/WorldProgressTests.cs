using System.Collections.Generic;
using System.Linq;
using Runewake.Engine.World;
using Xunit;

namespace Runewake.Tests.World;

/// <summary>FABLE-020: what each player can see and where they can go in the shared world.</summary>
public class WorldProgressTests
{
    private static (WorldGenerator g, WorldProgress p, WorldPage page) Fresh()
    {
        var g = new WorldGenerator(WorldGeneratorTests.Atlas());
        var p = new WorldProgress(g) { CrossroadsOpen = true };
        return (g, p, g.Page(new PageAddress("elvenwood", 0, 0)));
    }

    [Fact]
    public void Nothing_Is_Open_Before_The_Crossroads()
    {
        var g = new WorldGenerator(WorldGeneratorTests.Atlas());
        var p = new WorldProgress(g);
        Assert.False(p.IsPageOpen(new PageAddress("elvenwood", 0, 0)));
        Assert.Empty(p.CrossroadsAreas);
        p.CrossroadsOpen = true;
        Assert.True(p.IsPageOpen(new PageAddress("elvenwood", 0, 0)));
        Assert.False(p.IsPageOpen(new PageAddress("lost_forge", 0, 0)));   // hidden: gateway only
        Assert.False(p.IsPageOpen(new PageAddress("elvenwood", 0, 1)));    // needs page 0's warden
    }

    [Fact]
    public void Undiscovered_Places_Show_Only_The_Road_Discovered_Ones_Show_Dimmed()
    {
        var (_, p, page) = Fresh();
        var entry = page.Blips.First(b => b.IsEntry);
        var none = new HashSet<string>();
        Assert.Equal(BlipVisibility.Uncharted, p.VisibilityOf(page, entry, none));
        Assert.Equal(BlipVisibility.Known, p.VisibilityOf(page, entry, new HashSet<string> { entry.Id }));
        var deep = page.Blips.First(b => b.Column == 3);
        Assert.Equal(BlipVisibility.Hidden, p.VisibilityOf(page, deep, new HashSet<string> { deep.Id }));
    }

    [Fact]
    public void Clearing_Reveals_Exactly_One_Step_Further()
    {
        var (_, p, page) = Fresh();
        var entry = page.Blips.First(b => b.IsEntry);
        Assert.True(p.Visit(page, entry));
        Assert.Equal(BlipVisibility.Visited, p.VisibilityOf(page, entry, new HashSet<string>()));
        p.Clear(page, entry);
        var everyoneFoundEverything = page.Blips.Select(b => b.Id).ToHashSet();
        foreach (var n in entry.Next)
            Assert.Equal(BlipVisibility.Known, p.VisibilityOf(page, page.Blips[n], everyoneFoundEverything));
        foreach (var n in entry.Next)
            foreach (var n2 in page.Blips[n].Next)
                Assert.Equal(BlipVisibility.Hidden, p.VisibilityOf(page, page.Blips[n2], everyoneFoundEverything));
    }

    [Fact]
    public void You_Cannot_Jump_Ahead()
    {
        var (_, p, page) = Fresh();
        Assert.False(p.Visit(page, page.Guardian));
        Assert.False(p.Visit(page, page.Blips.First(b => b.Column == 4)));
    }

    [Fact]
    public void Warden_Opens_The_Next_Page_And_AreaBoss_The_Next_Area()
    {
        var (g, p, page) = Fresh();
        var unlock = p.Clear(page, page.Guardian);
        Assert.Equal(WorldUnlockKind.NextPage, unlock.Kind);
        Assert.True(p.IsPageOpen(new PageAddress("elvenwood", 0, 1)));
        int pages = g.PagesInArea(new AreaAddress("elvenwood", 0));
        var last = g.Page(new PageAddress("elvenwood", 0, pages - 1));
        Assert.False(p.IsPageOpen(new PageAddress("elvenwood", 1, 0)));
        var u2 = p.Clear(last, last.Guardian);
        Assert.Equal(WorldUnlockKind.NextArea, u2.Kind);
        Assert.Equal(new PageAddress("elvenwood", 1, 0), u2.Page!.Value);
        Assert.True(p.IsPageOpen(new PageAddress("elvenwood", 1, 0)));
    }

    [Fact]
    public void A_Gateway_Opens_Its_Hidden_Biome()
    {
        var g = new WorldGenerator(WorldGeneratorTests.Atlas());
        WorldPage? withGate = null;
        for (int inst = 0; inst < 40 && withGate == null; inst++)
            for (int pg = 0; pg < g.PagesInArea(new AreaAddress("deepdelve", inst)) && withGate == null; pg++)
            {
                var page = g.Page(new PageAddress("deepdelve", inst, pg));
                if (page.Blips.Any(b => b.Kind == BlipKind.Gateway)) withGate = page;
            }
        Assert.NotNull(withGate);
        var p = new WorldProgress(g) { CrossroadsOpen = true };
        var gate = withGate!.Blips.First(b => b.Kind == BlipKind.Gateway);
        var u = p.Clear(withGate, gate);
        Assert.Equal(WorldUnlockKind.HiddenArea, u.Kind);
        Assert.True(p.IsPageOpen(new PageAddress("lost_forge", 0, 0)));
    }

    [Fact]
    public void Save_Round_Trip()
    {
        var (g, p, page) = Fresh();
        var entry = page.Blips.First(b => b.IsEntry);
        p.Clear(page, entry);
        p.Visit(page, page.Blips[entry.Next[0]]);
        p.OpenedAreas.Add(new AreaAddress("lost_forge", 0));
        var q = new WorldProgress(g);
        q.LoadSaveEntries(p.ToSaveEntries().Concat(new[] { "r1_n01", "r1_n02" }));
        Assert.True(q.CrossroadsOpen, "crossroads");
        Assert.True(q.Cleared.SetEquals(p.Cleared), "cleared");
        Assert.True(q.Visited.SetEquals(p.Visited), "visited");
        Assert.True(q.OpenedAreas.SetEquals(p.OpenedAreas), "areas");
    }
}

using System.IO;
using System.Linq;
using Runewake.Engine.Cards;
using Xunit;

namespace Runewake.Tests.Cards;

public class MapLoaderTests
{
    private static readonly MapRegion Region = MapLoader.LoadRegion(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "content", "map", "region_01.json"));

    [Fact]
    public void LoadRegion_ValidJson_DeserializesCorrectly()
    {
        Assert.NotNull(Region);
        Assert.Equal("region_01", Region.Id);
        Assert.Equal("The Fallow Reach", Region.Name);
        Assert.Equal("VERDANT", Region.Strata);
        Assert.Equal("DAWN", Region.Strata2);
    }

    [Fact]
    public void LoadRegion_HasAllNodeTypes()
    {
        var types = Region.Nodes.Select(n => n.Type).ToHashSet();
        Assert.Contains(MapNodeType.Duel, types);
        Assert.Contains(MapNodeType.Elite, types);
        Assert.Contains(MapNodeType.Warden, types);
        Assert.Contains(MapNodeType.WardenBoss, types);
        Assert.Contains(MapNodeType.Dig, types);
        Assert.Contains(MapNodeType.Shrine, types);
        Assert.Contains(MapNodeType.Merchant, types);
    }

    [Fact]
    public void LoadRegion_AllNodesHaveIdAndPosition()
    {
        foreach (var node in Region.Nodes)
        {
            Assert.False(string.IsNullOrWhiteSpace(node.Id));
            Assert.Equal(2, node.Position.Length);
        }
    }

    [Fact]
    public void LoadRegion_ConnectsReferencesAreValid()
    {
        var allIds = Region.Nodes.Select(n => n.Id).ToHashSet();
        foreach (var node in Region.Nodes)
        {
            foreach (var target in node.Connects)
            {
                Assert.True(allIds.Contains(target),
                    $"Node {node.Id} connects to {target} which does not exist.");
            }
        }
    }

    [Fact]
    public void LoadRegion_FallbackReach_Has12Nodes()
    {
        Assert.Equal(12, Region.Nodes.Count);
    }

    [Fact]
    public void LoadRegion_FromString_DeserializesCorrectly()
    {
        const string json = """
        {
            "id": "test_region",
            "name": "Test",
            "strata": "VERDANT",
            "nodes": [
                {
                    "id": "r1_n01",
                    "type": "DUEL",
                    "position": [100, 200],
                    "connects": ["r1_n02"],
                    "encounter": "test_duel"
                },
                {
                    "id": "r1_n02",
                    "type": "WARDEN",
                    "position": [300, 400],
                    "connects": [],
                    "unlock": { "op": "NODES_CLEARED", "value": ["r1_n01"] }
                }
            ]
        }
        """;
        var region = MapLoader.LoadRegionFromString(json);
        Assert.Equal("test_region", region.Id);
        Assert.Equal(2, region.Nodes.Count);
        Assert.Equal(MapNodeType.Duel, region.Nodes[0].Type);
        Assert.Equal(MapNodeType.Warden, region.Nodes[1].Type);
        Assert.Equal(100, region.Nodes[0].Position[0]);
        Assert.Equal(200, region.Nodes[0].Position[1]);
        Assert.NotNull(region.Nodes[1].Unlock);
        Assert.Equal("NODES_CLEARED", region.Nodes[1].Unlock.Op);
        Assert.Contains("r1_n01", region.Nodes[1].Unlock.Value);
        Assert.Equal("test_duel", region.Nodes[0].Encounter);
    }

    [Fact]
    public void LoadRegion_FromString_NodeWithoutUnlock_IsNull()
    {
        const string json = """
        {
            "id": "test",
            "name": "Test",
            "strata": "VERDANT",
            "nodes": [
                {
                    "id": "n01",
                    "type": "DUEL",
                    "position": [0, 0],
                    "connects": []
                }
            ]
        }
        """;
        var region = MapLoader.LoadRegionFromString(json);
        Assert.Single(region.Nodes);
        Assert.Null(region.Nodes[0].Unlock);
    }

    [Fact]
    public void LoadRegion_FromString_NodeWithRewards_ParsesList()
    {
        const string json = """
        {
            "id": "test",
            "name": "Test",
            "strata": "VERDANT",
            "nodes": [
                {
                    "id": "n01",
                    "type": "DUEL",
                    "position": [0, 0],
                    "connects": [],
                    "rewards": ["shard:30", "dig_charge:1"]
                }
            ]
        }
        """;
        var region = MapLoader.LoadRegionFromString(json);
        Assert.Equal(2, region.Nodes[0].Rewards!.Count);
        Assert.Equal("shard:30", region.Nodes[0].Rewards[0]);
    }
}
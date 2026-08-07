using System.Collections.Generic;
using System.IO;
using System.Linq;
using Runewake.Engine.Cards;
using Xunit;

namespace Runewake.Tests.Cards;

public class MapLoaderTests
{
    private static readonly MapRegion Region = MapLoader.LoadRegion(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "content", "map", "region_01.json"));

    // ——— Deserialization ———

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

    // ——— Unlock evaluation ———

    [Fact]
    public void Unlock_InitialState_OnlyNullUnlockNodesUnlocked()
    {
        // In region_01, r1_n01 has no unlock condition → the only unlocked node
        var cleared = new HashSet<string>();
        var unlocked = MapUnlockEvaluator.GetUnlockedNodes(Region, cleared);
        Assert.Contains("r1_n01", unlocked);
        Assert.Equal(1, unlocked.Count);
    }

    [Fact]
    public void Unlock_ClearFirstNode_ConnectingNodesBecomeAvailable()
    {
        // Clear r1_n01 → r1_n02 and r1_n03 unlock (they require only r1_n01)
        var cleared = new HashSet<string> { "r1_n01" };
        var unlocked = MapUnlockEvaluator.GetUnlockedNodes(Region, cleared);
        Assert.Contains("r1_n01", unlocked); // cleared node is still "unlocked" for display
        Assert.Contains("r1_n02", unlocked);
        Assert.Contains("r1_n03", unlocked);
        // r1_n04 requires r1_n02 (not cleared) → still locked
        Assert.DoesNotContain("r1_n04", unlocked);
    }

    [Fact]
    public void Unlock_ClearChain_DeepNodeUnlocks()
    {
        // r1_n04 (Elite) requires r1_n02 → which requires r1_n01
        var cleared = new HashSet<string> { "r1_n01", "r1_n02" };
        var unlocked = MapUnlockEvaluator.GetUnlockedNodes(Region, cleared);
        Assert.Contains("r1_n04", unlocked);
        // r1_n06 requires r1_n04 → still locked
        Assert.DoesNotContain("r1_n06", unlocked);
    }

    [Fact]
    public void Unlock_MultiPrereq_RequiresAllToBeCleared()
    {
        // r1_n07 (Dig) requires [r1_n04, r1_n05]
        var cleared = new HashSet<string> { "r1_n01", "r1_n02", "r1_n03", "r1_n04" };
        var unlocked = MapUnlockEvaluator.GetUnlockedNodes(Region, cleared);
        Assert.DoesNotContain("r1_n07", unlocked); // r1_n05 not cleared yet

        cleared.Add("r1_n05");
        unlocked = MapUnlockEvaluator.GetUnlockedNodes(Region, cleared);
        Assert.Contains("r1_n07", unlocked);
    }

    [Fact]
    public void Unlock_FullChain_BossUnlocksAtEnd()
    {
        // Clear every node before r1_n12 (the boss)
        var cleared = new HashSet<string>
        {
            "r1_n01", "r1_n02", "r1_n03", "r1_n04", "r1_n05",
            "r1_n06", "r1_n07", "r1_n08", "r1_n09", "r1_n10", "r1_n11"
        };
        var unlocked = MapUnlockEvaluator.GetUnlockedNodes(Region, cleared);
        Assert.Contains("r1_n12", unlocked); // requires r1_n11
    }

    [Fact]
    public void Unlock_TransitionMechanic_ClearingNodeUnlocksConnected()
    {
        // r1_n02 starts locked (requires r1_n01 cleared)
        var cleared = new HashSet<string>();
        Assert.False(MapUnlockEvaluator.IsUnlocked(
            Region.Nodes.First(n => n.Id == "r1_n02"), cleared));

        // After clearing r1_n01, r1_n02 becomes available
        cleared.Add("r1_n01");
        Assert.True(MapUnlockEvaluator.IsUnlocked(
            Region.Nodes.First(n => n.Id == "r1_n02"), cleared));
    }

    [Fact]
    public void Unlock_NodeWithoutCondition_AlwaysUnlocked()
    {
        var node = Region.Nodes.First(n => n.Id == "r1_n01");
        Assert.Null(node.Unlock);
        Assert.True(MapUnlockEvaluator.IsUnlocked(node, new HashSet<string>()));
    }

    [Fact]
    public void Unlock_UnknownOp_IsLocked()
    {
        // Node with an unknown op should be locked (conservative)
        var node = Region.Nodes.First(n => n.Id == "r1_n02");
        // Temporarily swap the op
        var originalOp = node.Unlock!.Op;
        node.Unlock.Op = "UNKNOWN_OP";
        Assert.False(MapUnlockEvaluator.IsUnlocked(node, new HashSet<string> { "r1_n01" }));
        node.Unlock.Op = originalOp; // restore
    }

    [Fact]
    public void Unlock_EmptyPrereqs_IsLocked()
    {
        // NODES_CLEARED with empty value list should be locked
        var node = Region.Nodes.First(n => n.Id == "r1_n02");
        var originalValue = node.Unlock!.Value;
        node.Unlock.Value = new List<string>();
        Assert.False(MapUnlockEvaluator.IsUnlocked(node, new HashSet<string>()));
        node.Unlock.Value = originalValue; // restore
    }
}
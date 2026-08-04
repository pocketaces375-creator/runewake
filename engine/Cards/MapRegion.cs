using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// Node types in the campaign map graph.
/// </summary>
public enum MapNodeType
{
    Duel,
    Elite,
    Warden,
    WardenBoss,
    Dig,
    Shrine,
    Cache,
    Merchant
}

/// <summary>
/// Condition required to unlock a map node.
/// </summary>
public class UnlockCondition
{
    /// <summary>Operator for the condition (e.g. "NODES_CLEARED").</summary>
    public string Op { get; set; } = string.Empty;

    /// <summary>Value(s) for the condition (e.g. node IDs to clear).</summary>
    public List<string> Value { get; set; } = new();
}

/// <summary>
/// A single node on the campaign map.
/// </summary>
public class MapNode
{
    /// <summary>Unique node identifier (e.g. "r1_n01").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Node type determining encounter kind.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MapNodeType Type { get; set; }

    /// <summary>Screen-space position [x, y] for the map renderer.</summary>
    public int[] Position { get; set; } = { 0, 0 };

    /// <summary>IDs of nodes this one connects to (edges).</summary>
    public List<string> Connects { get; set; } = new();

    /// <summary>Optional unlock condition. If null, the node starts unlocked.</summary>
    public UnlockCondition? Unlock { get; set; }

    /// <summary>Reference to an encounter definition (for DUEL/ELITE/WARDEN/WARDEN_BOSS).</summary>
    public string? Encounter { get; set; }

    /// <summary>Reward strings (e.g. "shard:120", "fragment:ember:2", "dig_charge:1").</summary>
    public List<string>? Rewards { get; set; }

    /// <summary>Optional zone grouping label for the map renderer.</summary>
    public string? Zone { get; set; }
}

/// <summary>
/// A campaign region containing a node graph.
/// Serialized as a single JSON file (e.g. content/map/region_01.json).
/// </summary>
public class MapRegion
{
    /// <summary>Unique region identifier (e.g. "region_01").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name for the region.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Primary strata (e.g. "VERDANT").</summary>
    public string Strata { get; set; } = string.Empty;

    /// <summary>Secondary strata, if the region uses two.</summary>
    public string? Strata2 { get; set; }

    /// <summary>All nodes in this region.</summary>
    public List<MapNode> Nodes { get; set; } = new();
}
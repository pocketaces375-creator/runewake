using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// What effect a dig tool has on the dig interaction.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DigToolEffect
{
    /// <summary>Increases the number of strikes at a dig site.</summary>
    EXTRA_STRIKE,
    /// <summary>Reveals all tiles adjacent to a struck tile (radius 1).</summary>
    REVEAL_RADIUS,
    /// <summary>Highlights one tile that contains a non-empty reward before the player strikes.</summary>
    HIGHLIGHT_TILE,
    /// <summary>Adds a flat bonus to the headline threshold reduction (makes it easier).</summary>
    LOWER_THRESHOLD
}

/// <summary>
/// A permanent dig tool unlocked from Elite nodes.
/// Modifies the dig interaction in various ways.
/// </summary>
public class DigToolDef
{
    /// <summary>Unique tool identifier (e.g. "tool_brush").</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name (e.g. "Brush", "Iron Spade").</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Flavour description of the tool and its effect.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>The type of effect this tool provides.</summary>
    [JsonPropertyName("effect")]
    public DigToolEffect Effect { get; set; }

    /// <summary>Magnitude of the effect (e.g. +2 strikes, reveal radius 1).</summary>
    [JsonPropertyName("value")]
    public int Value { get; set; } = 1;

    /// <summary>Optional strata affinity for thematic matching.</summary>
    [JsonPropertyName("strata")]
    public Strata? Strata { get; set; }
}

/// <summary>
/// Container for dig tool definitions (one file = one pack).
/// </summary>
public class DigToolPack
{
    [JsonPropertyName("tools")]
    public List<DigToolDef> Tools { get; set; } = new();
}
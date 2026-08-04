using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// What a single dig tile can contain when revealed.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DigRewardType
{
    SHARD,
    RUNE_FRAGMENT,
    CODEX_PAGE,
    RELIC,
    EMPTY
}

/// <summary>
/// Definition of a single tile in the dig grid.
/// </summary>
public class DigTileDef
{
    /// <summary>Type of reward this tile holds.</summary>
    [JsonPropertyName("type")]
    public DigRewardType Type { get; set; } = DigRewardType.EMPTY;

    /// <summary>
    /// Value associated with the reward.
    /// For SHARD: shard count (e.g. "20").
    /// For RUNE_FRAGMENT: strata:count (e.g. "ember:2").
    /// For CODEX_PAGE: codex entry ID (e.g. "r1_codex_01").
    /// For RELIC: relic card ID (e.g. "r1_relic_01").
    /// </summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

/// <summary>
/// Defines a dig site — a grid-based excavation mini-interaction on the campaign map.
/// </summary>
public class DigSiteDef
{
    /// <summary>Unique dig site identifier (e.g. "region_01_dig").</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name (e.g. "The Earthen Maw").</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional flavour text shown when entering the dig.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Number of rows in the grid.</summary>
    [JsonPropertyName("rows")]
    public int Rows { get; set; } = 4;

    /// <summary>Number of columns in the grid.</summary>
    [JsonPropertyName("cols")]
    public int Cols { get; set; } = 4;

    /// <summary>Base number of strikes the player gets.</summary>
    [JsonPropertyName("strikes")]
    public int Strikes { get; set; } = 3;

    /// <summary>
    /// Minimum number of tiles that must be revealed to claim the headline find.
    /// </summary>
    [JsonPropertyName("headline_threshold")]
    public int HeadlineThreshold { get; set; } = 4;

    /// <summary>
    /// The headline find reward (e.g. "relic:r1_relic_01" or "codex:r1_warden_secret").
    /// Awarded when tiles revealed >= headline_threshold.
    /// </summary>
    [JsonPropertyName("headline_reward")]
    public string? HeadlineReward { get; set; }

    /// <summary>
    /// Tile definitions in row-major order (index = row * cols + col).
    /// Must have exactly rows * cols entries.
    /// </summary>
    [JsonPropertyName("tiles")]
    public List<DigTileDef> Tiles { get; set; } = new();
}

/// <summary>
/// Container for a pack of dig site definitions (one file = one pack).
/// </summary>
public class DigSitePack
{
    [JsonPropertyName("dig_sites")]
    public List<DigSiteDef> DigSites { get; set; } = new();
}
using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// The immutable definition of a Runewake card. Cards are data, never code.
/// Every field maps to a property in <c>schema/card.schema.json</c>.
/// </summary>
public sealed class CardDef
{
    /// <summary>Unique card identifier, e.g. "vrd_c_root_warden".</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Content set this card belongs to, e.g. "buried_age".</summary>
    [JsonPropertyName("set")]
    public string Set { get; set; } = string.Empty;

    /// <summary>Display name (2–40 characters).</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Stratum (color/region).</summary>
    [JsonPropertyName("strata")]
    public Strata Strata { get; set; }

    [JsonPropertyName("type")]
    public CardType Type { get; set; }

    [JsonPropertyName("rarity")]
    public Rarity Rarity { get; set; }

    [JsonPropertyName("cost")]
    public int Cost { get; set; }

    /// <summary>Attack value (0–12). Required for CREATURE, absent otherwise.</summary>
    [JsonPropertyName("attack")]
    public int? Attack { get; set; }

    /// <summary>Base vigor (1–14). Required for CREATURE, absent otherwise.</summary>
    [JsonPropertyName("vigor")]
    public int? Vigor { get; set; }

    /// <summary>Keywords on the card (max 3).</summary>
    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; set; } = new();

    /// <summary>Abilities on the card (max 2).</summary>
    [JsonPropertyName("abilities")]
    public List<AbilityDef> Abilities { get; set; } = new();

    /// <summary>Condition for identifying a RELIC. Only RELICs may have this.</summary>
    [JsonPropertyName("identify_condition")]
    public ConditionDef? IdentifyCondition { get; set; }

    /// <summary>Flavor text (max 140 characters).</summary>
    [JsonPropertyName("flavor")]
    public string? Flavor { get; set; }

    /// <summary>Art references — prompt for generation, asset URL after rendering.</summary>
    [JsonPropertyName("art")]
    public ArtDef? Art { get; set; }

    /// <summary>Estimated power score for balance checking.</summary>
    [JsonPropertyName("power_score")]
    public double? PowerScore { get; set; }

    /// <summary>Version of the content schema this card targets.</summary>
    [JsonPropertyName("content_version")]
    public int ContentVersion { get; set; } = 1;
}

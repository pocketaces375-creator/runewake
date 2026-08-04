using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// Which slot type a rune occupies on the rune page.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RuneSlotType
{
    OFFENSIVE,
    DEFENSIVE,
    UTILITY,
    MYTHIC
}

/// <summary>
/// An equippable rune — reuses <see cref="AbilityDef"/> for its triggered effect.
/// Runes provide passive bonuses or triggered abilities that activate during a duel.
/// </summary>
public sealed class RuneDef
{
    /// <summary>Unique rune identifier, e.g. "rune_vrd_sharp_roots".</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Flavour/description text.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Stratum affinity (optional — for thematic matching).</summary>
    [JsonPropertyName("strata")]
    public Strata? Strata { get; set; }

    /// <summary>Which slot type this rune occupies.</summary>
    [JsonPropertyName("slot_type")]
    public RuneSlotType SlotType { get; set; }

    /// <summary>Rune Point cost (1–20). Counts toward the rune page budget.</summary>
    [JsonPropertyName("cost")]
    public int Cost { get; set; }

    /// <summary>
    /// The ability this rune grants. Reuses the full AbilityDef model.
    /// Trigger can be ON_TURN_START, ON_SUMMON (of any creature), PASSIVE, etc.
    /// </summary>
    [JsonPropertyName("ability")]
    public AbilityDef Ability { get; set; } = new();
}

/// <summary>
/// A pack of rune definitions loaded from a single JSON file.
/// </summary>
public sealed class RunePack
{
    [JsonPropertyName("runes")]
    public List<RuneDef> Runes { get; set; } = new();
}
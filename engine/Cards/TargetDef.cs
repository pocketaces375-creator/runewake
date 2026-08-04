using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// Describes which entities an effect targets and how many.
/// The <see cref="Filter"/> selects from the <see cref="Scope"/> pool.
/// </summary>
public sealed class TargetDef
{
    /// <summary>Which pool of entities to select from.</summary>
    [JsonPropertyName("scope")]
    public Scope Scope { get; set; }

    /// <summary>
    /// Optional filter narrowing the scope (e.g. "ADJACENT", "DAMAGED", "STRATA:HOLLOW").
    /// May be null when the scope is self-targeting or player-targeting.
    /// </summary>
    [JsonPropertyName("filter")]
    public string? Filter { get; set; }

    /// <summary>
    /// How many targets to select. Defaults to 1 when absent.
    /// </summary>
    [JsonPropertyName("count")]
    public TargetCount? Count { get; set; }
}

using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// A single effect within an ability.
/// Maps to the DSL opcode system — every card effect is composed of these.
/// </summary>
public sealed class EffectDef
{
    /// <summary>The operation to perform.</summary>
    [JsonPropertyName("op")]
    public Op Op { get; set; }

    /// <summary>Which entity/entities to apply the effect to.</summary>
    [JsonPropertyName("target")]
    public TargetDef? Target { get; set; }

    /// <summary>Numeric amount for DAMAGE, HEAL, DRAW, EXCAVATE, etc.</summary>
    [JsonPropertyName("amount")]
    public int? Amount { get; set; }

    /// <summary>Attack value for BUFF/SET_STAT.</summary>
    [JsonPropertyName("attack")]
    public int? Attack { get; set; }

    /// <summary>Vigor value for BUFF/SET_STAT.</summary>
    [JsonPropertyName("vigor")]
    public int? Vigor { get; set; }

    /// <summary>Keyword to grant/remove for GRANT_KEY/REMOVE_KEY.</summary>
    [JsonPropertyName("keyword")]
    public string? Keyword { get; set; }

    /// <summary>Token card ID for SUMMON.</summary>
    [JsonPropertyName("token_id")]
    public string? TokenId { get; set; }

    /// <summary>Duration of the effect.</summary>
    [JsonPropertyName("duration")]
    public Duration? Duration { get; set; }
}

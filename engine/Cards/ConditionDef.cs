using System.Text.Json;
using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// A condition that must be true for an ability to trigger or an effect to resolve.
/// Supports compound conditions via <see cref="All"/> / <see cref="Any"/> with
/// nesting up to 2 levels deep.
/// </summary>
public sealed class ConditionDef
{
    /// <summary>The comparison operator, e.g. BARROW_COUNT_GTE.</summary>
    [JsonPropertyName("op")]
    public ConditionOp? Op { get; set; }

    /// <summary>Value to compare against. Type depends on the condition op.</summary>
    [JsonPropertyName("value")]
    public JsonElement? Value { get; set; }

    /// <summary>All of these sub-conditions must be true (AND).</summary>
    [JsonPropertyName("all")]
    public List<ConditionDef>? All { get; set; }

    /// <summary>Any one of these sub-conditions must be true (OR).</summary>
    [JsonPropertyName("any")]
    public List<ConditionDef>? Any { get; set; }
}

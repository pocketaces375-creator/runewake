using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// An ability on a card. Triggered by a game event and produces effects.
/// Max 2 effects per ability, max 2 abilities per card.
/// </summary>
public sealed class AbilityDef
{
    /// <summary>What event causes this ability to fire.</summary>
    [JsonPropertyName("trigger")]
    public Trigger Trigger { get; set; }

    /// <summary>Optional condition that must be true for the ability to fire.</summary>
    [JsonPropertyName("condition")]
    public ConditionDef? Condition { get; set; }

    /// <summary>Cost in attunement for ACTIVATED abilities (0 when not applicable).</summary>
    [JsonPropertyName("activation_cost")]
    public int? ActivationCost { get; set; }

    /// <summary>The effects produced when this ability fires (1–2).</summary>
    [JsonPropertyName("effects")]
    public List<EffectDef> Effects { get; set; } = new();

    /// <summary>
    /// Optional timing modifier for when the trigger fires.
    /// "END_OF_TURN" means the trigger resolves at end of turn instead of immediately
    /// (used by ON_CHARGE_FULL for Censer, Grimoire per G8).
    /// Null (default) = fire immediately.
    /// </summary>
    [JsonPropertyName("timing")]
    public string? Timing { get; set; }
}

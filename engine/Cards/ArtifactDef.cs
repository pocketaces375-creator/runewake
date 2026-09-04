using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// Definition of an Artifact card — a class-specific field-effect card
/// that occupies a permanent Artifact Slot. Artifacts are not part of the
/// 30-card deck and can never be drawn, discarded, or change zones.
/// 
/// Each Artifact has:
/// - A passive (always-on static effect)
/// - A trigger (one triggered ability)
/// - Optional Charge counters
/// 
/// See FIELD_EFFECT_SPEC.md for the full specification.
/// </summary>
public sealed class ArtifactDef
{
    /// <summary>Unique artifact identifier, e.g. "artf_warrior_sword_01".</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>The class this Artifact belongs to (e.g. "warrior", "rogue", "battlemage").</summary>
    [JsonPropertyName("class")]
    public string Class { get; set; } = string.Empty;

    /// <summary>The slot pool this Artifact draws from (e.g. "sword", "shield", "dagger", "wand").</summary>
    [JsonPropertyName("slot_pool")]
    public string SlotPool { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Passive ability — always-on static effect while the Artifact is not Suppressed.
    /// Expressed as a single effect with op and target. Typically a WHILE_PRESENT buff
    /// or a conditional modifier applied at the start of each turn.
    /// </summary>
    [JsonPropertyName("passive")]
    public EffectDef Passive { get; set; } = new();

    /// <summary>
    /// Triggered ability — fires when the specified event occurs, if condition is met.
    /// Expressed as a trigger type + optional condition + effect list.
    /// </summary>
    [JsonPropertyName("trigger")]
    public AbilityDef Trigger { get; set; } = new();

    /// <summary>
    /// Optional Charge configuration. Null if this Artifact doesn't use Charges.
    /// </summary>
    [JsonPropertyName("charges")]
    public ChargeConfig? Charges { get; set; }

    /// <summary>
    /// Flavor text (max 140 characters).
    /// </summary>
    [JsonPropertyName("flavor")]
    public string? Flavor { get; set; }

    /// <summary>
    /// Art references — prompt for generation, asset URL after rendering.
    /// </summary>
    [JsonPropertyName("art")]
    public ArtDef? Art { get; set; }

    /// <summary>
    /// Optional — effects to execute when this artifact reaches full charges.
    /// When set, the engine automatically creates an ON_CHARGE_FULL ability
    /// with these effects (in addition to any trigger-defined ability).
    /// This allows the artifact's trigger to handle charge-gain events while
    /// the full-charge effect fires separately.
    /// </summary>
    [JsonPropertyName("full_charge")]
    public List<EffectDef>? FullCharge { get; set; }

    /// <summary>
    /// Version of the content schema this artifact targets.</summary>
    [JsonPropertyName("content_version")]
    public int ContentVersion { get; set; } = 1;
}

/// <summary>
/// Configuration for Artifact Charge counters.
/// </summary>
public sealed class ChargeConfig
{
    /// <summary>Maximum number of Charges this Artifact can hold.</summary>
    [JsonPropertyName("max")]
    public int Max { get; set; }

    /// <summary>
    /// When Charges are gained. Values: "on_trigger_event", "on_death", "on_attack_character", etc.
    /// The engine uses this to know when to call AddCharges().
    /// </summary>
    [JsonPropertyName("gain_on")]
    public string GainOn { get; set; } = string.Empty;

    /// <summary>
    /// Trigger event that spends Charges. Values: "on_expend" (auto-spend on trigger),
    /// "manual" (effects reference Charges directly).
    /// </summary>
    [JsonPropertyName("spend_on")]
    public string SpendOn { get; set; } = string.Empty;

    /// <summary>
    /// Maximum total Charges that can be gained per turn (across all sources).
    /// 0 or null = unlimited.
    /// Enforced at ADD_CHARGE time. Reset when the owner's turn starts.
    /// Used by: Censer (max_per_turn: 1).
    /// </summary>
    [JsonPropertyName("max_per_turn")]
    public int MaxPerTurn { get; set; }

    /// <summary>
    /// Maximum Charges that can be gained per turn from a single creature.
    /// 0 or null = unlimited.
    /// Enforced at ADD_CHARGE time by tracking which creature instance triggered the gain.
    /// Used by: Duskfang (max_per_creature_per_turn: 1).
    /// </summary>
    [JsonPropertyName("max_per_creature_per_turn")]
    public int MaxPerCreaturePerTurn { get; set; }
}
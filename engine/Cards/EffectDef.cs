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

    /// <summary>
    /// Damage-source filter for PREVENT_DAMAGE: "ATTACK" or "SPELL" (null = any source).
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>
    /// Frequency gate for PREVENT_DAMAGE: "FIRST_ATTACK_EACH_TURN" or "ONCE_PER_ENEMY_TURN"
    /// (null = unlimited). Some cards express this via the effect-level "filter" field
    /// (e.g. Aura passive uses "filter": "FIRST_ATTACK_EACH_TURN").
    /// </summary>
    [JsonPropertyName("frequency")]
    public string? Frequency { get; set; }

    /// <summary>
    /// Effect-level filter string. For PREVENT_DAMAGE this is an alias for
    /// <see cref="Frequency"/> (Aura passive encodes the frequency here).
    /// </summary>
    [JsonPropertyName("filter")]
    public string? Filter { get; set; }

    /// <summary>
    /// Condition evaluated at damage-application time for PREVENT_DAMAGE (R21),
    /// e.g. FEWER_ALLY_CREATURES_THAN_ENEMY. Applies to the effect's target.
    /// </summary>
    [JsonPropertyName("condition")]
    public ConditionDef? Condition { get; set; }

    /// <summary>
    /// Card-type filter for COST_MOD: "CREATURE" or "SPELL" (null = any card type).
    /// </summary>
    [JsonPropertyName("applies_to")]
    public string? AppliesTo { get; set; }

    /// <summary>
    /// Companion value for COST_MOD card filters (e.g. ATTACK_LTE value 2 =
    /// creatures with attack ≤ 2).
    /// </summary>
    [JsonPropertyName("value")]
    public int? Value { get; set; }

    /// <summary>
    /// Whether repeated applications of the same COST_MOD stack additively
    /// (true, e.g. Aura trigger) or replace each other (false/null, e.g. a
    /// passive re-applied each turn).
    /// </summary>
    [JsonPropertyName("stacks")]
    public bool? Stacks { get; set; }

    /// <summary>
    /// Cadence for artifact passive effects — when the passive resolves.
    /// <see cref="CadenceOnTurnStart"/> = at the start of the owner's turn,
    /// BEFORE the draw phase (R11, R15). Null = refreshed each turn by the
    /// generic passive step (existing behavior).
    /// </summary>
    [JsonPropertyName("cadence")]
    public string? Cadence { get; set; }

    /// <summary>
    /// Explicit ordering key within a cadence phase.
    /// <see cref="OrderBeforeAllOtherTurnStartEffects"/> resolves before all
    /// other turn-start effects (R15 Prey marking). Null = default order.
    /// </summary>
    [JsonPropertyName("order")]
    public string? Order { get; set; }

    /// <summary>Cadence value: passive fires at the start of the owner's turn (before draw).</summary>
    public const string CadenceOnTurnStart = "ON_TURN_START";

    /// <summary>Ordering key: resolve before all other turn-start effects (R15 Prey marking).</summary>
    public const string OrderBeforeAllOtherTurnStartEffects = "BEFORE_ALL_OTHER_TURN_START_EFFECTS";

    /// <summary>
    /// Which source to spend from for FORGE / similar ops.
    /// "PARTNER_SLOT" = the twin artifact slot.
    /// </summary>
    [JsonPropertyName("spend_from")]
    public string? SpendFrom { get; set; }

    /// <summary>
    /// How much to spend for FORGE: "ALL" = spend all charges.
    /// </summary>
    [JsonPropertyName("spend")]
    public string? Spend { get; set; }

    /// <summary>
    /// Stats gained per charge spent for FORGE.
    /// JSON shape: { "attack": 1, "vigor": 1 }
    /// </summary>
    [JsonPropertyName("per_charge")]
    public PerChargeStats? PerCharge { get; set; }
}

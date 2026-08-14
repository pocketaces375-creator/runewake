using Runewake.Engine.Cards;

namespace Runewake.Engine.State;

/// <summary>
/// A standing cost discount (COST_MOD op). Registered on a player; applied at
/// card-play time by <see cref="Engine.CostInterceptor"/>. Reduces the play
/// cost of matching cards by <see cref="Amount"/> when the card-type filter
/// (<see cref="AppliesTo"/>), per-card filter (<see cref="Filter"/> +
/// <see cref="Value"/>), per-turn consumption gate (FIRST_SPELL_EACH_TURN),
/// condition (evaluated at play time), and suppression status all pass.
/// The effective cost never drops below 0 (floor 0).
/// </summary>
public sealed class CostMod
{
    /// <summary>How much the play cost is reduced by (positive = discount).</summary>
    public int Amount { get; set; }

    /// <summary>
    /// Card-type filter: "CREATURE", "SPELL", or null (any type).
    /// "CREATURE" matches creatures (and tokens); "SPELL" matches Rituals.
    /// </summary>
    public string? AppliesTo { get; set; }

    /// <summary>
    /// Per-card filter evaluated against the card being played:
    /// "ATTACK_LTE" (with <see cref="Value"/>), "FIRST_SPELL_EACH_TURN"
    /// (per-turn consumption gate), or null (any card).
    /// </summary>
    public string? Filter { get; set; }

    /// <summary>Companion value for the card filter (e.g. ATTACK_LTE value 2).</summary>
    public int? Value { get; set; }

    /// <summary>
    /// Condition evaluated at card-play time (e.g. CREATURE_DIED_THIS_TURN).
    /// Null = always active.
    /// </summary>
    public ConditionDef? Condition { get; set; }

    /// <summary>
    /// Duration of the discount: THIS_TURN = cleared when the owning player
    /// ends their turn; null = persists until removed or the source Artifact
    /// is suppressed.
    /// </summary>
    public Duration? Duration { get; set; }

    /// <summary>
    /// True when repeated applications of the same Artifact's discount add
    /// up (Aura trigger). False = re-application replaces (passive re-applied
    /// each turn).
    /// </summary>
    public bool Stacks { get; set; }

    /// <summary>
    /// Number of times this mod's per-turn consumption gate has fired this
    /// turn (FIRST_SPELL_EACH_TURN). Reset by re-application at turn start.
    /// </summary>
    public int UsedThisTurn { get; set; }

    /// <summary>Card def id of the Artifact that created this mod (suppression lookup).</summary>
    public string SourceArtifactDefId { get; set; } = string.Empty;

    /// <summary>Instance id of the Artifact that created this mod (suppression lookup).</summary>
    public int SourceArtifactInstanceId { get; set; }

    /// <summary>Player index that controls the source Artifact (condition side + suppression lookup).</summary>
    public int SourceController { get; set; }

    public CostMod Clone() => new()
    {
        Amount = Amount,
        AppliesTo = AppliesTo,
        Filter = Filter,
        Value = Value,
        Condition = Condition,
        Duration = Duration,
        Stacks = Stacks,
        UsedThisTurn = UsedThisTurn,
        SourceArtifactDefId = SourceArtifactDefId,
        SourceArtifactInstanceId = SourceArtifactInstanceId,
        SourceController = SourceController
    };
}

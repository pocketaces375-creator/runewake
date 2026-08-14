using Runewake.Engine.Cards;

namespace Runewake.Engine.State;

/// <summary>
/// A standing damage-prevention shield (PREVENT_DAMAGE op).
/// Registered on a player or creature; intercepts incoming damage at
/// damage-application time and reduces it by <see cref="Amount"/> when the
/// source filter, frequency gate, condition (evaluated at damage time, R21),
/// and suppression status all pass.
/// </summary>
public sealed class DamageShield
{
    /// <summary>How much damage to prevent per application.</summary>
    public int Amount { get; set; }

    /// <summary>
    /// Damage-source filter: "ATTACK", "SPELL", or null (any source).
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Frequency gate: "FIRST_ATTACK_EACH_TURN", "ONCE_PER_ENEMY_TURN", or null (unlimited).
    /// </summary>
    public string? Frequency { get; set; }

    /// <summary>
    /// Condition evaluated at damage-application time (e.g. FEWER_ALLY_CREATURES_THAN_ENEMY).
    /// Null = always active.
    /// </summary>
    public ConditionDef? Condition { get; set; }

    /// <summary>Card def id of the Artifact that created this shield (suppression lookup).</summary>
    public string SourceArtifactDefId { get; set; } = string.Empty;

    /// <summary>Instance id of the Artifact that created this shield (suppression lookup).</summary>
    public int SourceArtifactInstanceId { get; set; }

    /// <summary>Player index that controls the source Artifact (condition side + suppression lookup).</summary>
    public int SourceController { get; set; }

    /// <summary>
    /// Number of times this shield has fired this turn (frequency gating).
    /// Reset at the start of every turn for FIRST_ATTACK_EACH_TURN and
    /// ONCE_PER_ENEMY_TURN shields (R5: resets at the start of EVERY turn).
    /// </summary>
    public int UsedThisTurn { get; set; }

    public DamageShield Clone() => new()
    {
        Amount = Amount,
        Source = Source,
        Frequency = Frequency,
        Condition = Condition,
        SourceArtifactDefId = SourceArtifactDefId,
        SourceArtifactInstanceId = SourceArtifactInstanceId,
        SourceController = SourceController,
        UsedThisTurn = UsedThisTurn
    };
}

using Runewake.Engine.Cards;

namespace Runewake.Engine.State;

/// <summary>
/// One of the Artifact slots on a player's side of the board.
/// Artifacts are permanent field-effect cards that flank the character portrait.
/// Each slot holds at most one Artifact at a time.
/// Artifacts are indestructible but suppressible.
/// Supports 1-slot, 2-slot, and 3-slot classes via the array length (§8).
/// </summary>
public sealed class ArtifactSlot
{
    /// <summary>Slot index (0-based, left-to-right in UI).</summary>
    public int Index { get; }

    /// <summary>
    /// The Artifact occupying this slot, or null if empty.
    /// Artifacts are permanent and never leave their slot — this is null only
    /// during initialization before Artifacts are assigned.
    /// </summary>
    public CardInstance? Occupant { get; set; }

    /// <summary>
    /// Is the Artifact currently Suppressed?
    /// While Suppressed: passive off, triggers don't fire, Charges frozen.
    /// </summary>
    public bool IsSuppressed { get; set; }

    /// <summary>
    /// Remaining turns of suppression, counted in the *owner's* turns.
    /// Decremented at end of owner's turn when > 0.
    /// "Suppress for 1 turn" = until end of that player's next turn.
    /// </summary>
    public int SuppressionRemaining { get; set; }

    /// <summary>
    /// Current Charge count on this Artifact.
    /// Charges are a counter some Artifacts accumulate and spend.
    /// </summary>
    public int Charges { get; set; }

    /// <summary>
    /// Maximum Charges this Artifact can hold (0 if it doesn't use Charges).
    /// </summary>
    public int MaxCharges { get; set; }

    /// <summary>
    /// Source entity id that applied the current suppression (for stacking rules).
    /// Same source id refreshes; different source ids extend.
    /// </summary>
    public string? SuppressionSourceId { get; set; }

    /// <summary>
    /// Whether this Artifact has fired its trigger this turn (for once-per-turn gating).
    /// Reset at start of owner's turn.
    /// </summary>
    public bool HasTriggeredThisTurn { get; set; }

    /// <summary>
    /// Whether the passive effect was applied for the current turn.
    /// Reset at start of owner's turn to allow re-evaluation.
    /// </summary>
    public bool PassiveAppliedThisTurn { get; set; }

    public ArtifactSlot(int index)
    {
        Index = index;
    }

    private ArtifactSlot(ArtifactSlot other)
    {
        Index = other.Index;
        Occupant = other.Occupant?.Clone();
        IsSuppressed = other.IsSuppressed;
        SuppressionRemaining = other.SuppressionRemaining;
        Charges = other.Charges;
        MaxCharges = other.MaxCharges;
        SuppressionSourceId = other.SuppressionSourceId;
        HasTriggeredThisTurn = other.HasTriggeredThisTurn;
        PassiveAppliedThisTurn = other.PassiveAppliedThisTurn;
    }

    /// <summary>
    /// Returns a deep clone of this Artifact slot.
    /// </summary>
    public ArtifactSlot Clone() => new(this);

    /// <summary>
    /// Apply suppression to this slot from a given source.
    /// </summary>
    public void ApplySuppression(int turns, string sourceId)
    {
        if (SuppressionSourceId == sourceId)
        {
            // Same source: refresh duration, don't extend
            SuppressionRemaining = Math.Max(SuppressionRemaining, turns);
        }
        else
        {
            // Different source: extend
            SuppressionRemaining += turns;
        }
        SuppressionSourceId = sourceId;
        IsSuppressed = true;
    }

    /// <summary>
    /// Decrement suppression counter at end of owner's turn.
    /// Clears suppression when counter reaches 0.
    /// </summary>
    public void TickSuppression()
    {
        if (SuppressionRemaining > 0)
        {
            SuppressionRemaining--;
            if (SuppressionRemaining <= 0)
            {
                IsSuppressed = false;
                SuppressionSourceId = null;
            }
        }
    }

    /// <summary>
    /// Add Charges, capping at MaxCharges.
    /// </summary>
    public void AddCharges(int amount)
    {
        if (MaxCharges > 0)
            Charges = Math.Min(MaxCharges, Charges + amount);
    }

    /// <summary>
    /// Spend all Charges and return the count spent.
    /// </summary>
    public int SpendAllCharges()
    {
        int spent = Charges;
        Charges = 0;
        return spent;
    }
}
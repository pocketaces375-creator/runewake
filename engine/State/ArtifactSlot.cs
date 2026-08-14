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

    // ——— Per-turn charge tracking (TASK-DSL-5) ———

    /// <summary>
    /// Total Charges gained this turn across all sources.
    /// Used to enforce <see cref="ChargeConfigMaxPerTurn"/>.
    /// Reset at start of the owner's turn.
    /// </summary>
    public int ChargesGainedThisTurn { get; set; }

    /// <summary>
    /// Charges gained this turn keyed by creature instance ID.
    /// Used to enforce <see cref="ChargeConfigMaxPerCreaturePerTurn"/>.
    /// Reset at start of the owner's turn.
    /// </summary>
    public Dictionary<int, int> ChargesGainedThisTurnByCreature { get; set; } = new();

    /// <summary>
    /// Whether this slot has a pending ON_CHARGE_FULL trigger that should
    /// fire at end of turn (for triggers with timing "END_OF_TURN").
    /// Set when ADD_CHARGE fills charges and the artifact's ON_CHARGE_FULL
    /// ability has timing END_OF_TURN.
    /// Cleared after the end-of-turn firing.
    /// </summary>
    public bool PendingChargeFull { get; set; }

    /// <summary>
    /// Per-turn charge gain cap from the artifact's ChargeConfig (max_per_turn).
    /// 0 = unlimited.
    /// Set when the artifact is assigned to this slot.
    /// </summary>
    public int ChargeConfigMaxPerTurn { get; set; }

    /// <summary>
    /// Per-creature per-turn charge gain cap from the artifact's ChargeConfig
    /// (max_per_creature_per_turn). 0 = unlimited.
    /// Set when the artifact is assigned to this slot.
    /// </summary>
    public int ChargeConfigMaxPerCreaturePerTurn { get; set; }

    /// <summary>
    /// Whether this artifact's ON_CHARGE_FULL trigger has timing END_OF_TURN
    /// (meaning the trigger fires at end of turn, not immediately).
    /// Derived from the artifact's ability definitions when assigned.
    /// </summary>
    public bool HasDeferredChargeFull { get; set; }

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
        ChargesGainedThisTurn = other.ChargesGainedThisTurn;
        ChargesGainedThisTurnByCreature = new Dictionary<int, int>(other.ChargesGainedThisTurnByCreature);
        PendingChargeFull = other.PendingChargeFull;
        ChargeConfigMaxPerTurn = other.ChargeConfigMaxPerTurn;
        ChargeConfigMaxPerCreaturePerTurn = other.ChargeConfigMaxPerCreaturePerTurn;
        HasDeferredChargeFull = other.HasDeferredChargeFull;
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
    /// Enforces per-turn limits (<see cref="ChargeConfigMaxPerTurn"/> and
    /// <see cref="ChargeConfigMaxPerCreaturePerTurn"/>).
    /// Returns the actual number of charges added (0 if capped).
    /// </summary>
    public int AddCharges(int amount, int? creatureInstanceId = null)
    {
        if (MaxCharges <= 0 || amount <= 0 || IsSuppressed)
            return 0;

        int capped = amount;

        // Total per-turn cap (max_per_turn)
        if (ChargeConfigMaxPerTurn > 0)
        {
            int remainingTurn = ChargeConfigMaxPerTurn - ChargesGainedThisTurn;
            if (remainingTurn <= 0)
                return 0; // capped for this turn
            capped = Math.Min(capped, remainingTurn);
        }

        // Per-creature per-turn cap (max_per_creature_per_turn)
        if (ChargeConfigMaxPerCreaturePerTurn > 0 && creatureInstanceId.HasValue)
        {
            int creatureId = creatureInstanceId.Value;
            if (!ChargesGainedThisTurnByCreature.TryGetValue(creatureId, out int creatureCharges))
                creatureCharges = 0;
            int remainingCreature = ChargeConfigMaxPerCreaturePerTurn - creatureCharges;
            if (remainingCreature <= 0)
                return 0; // this creature capped
            capped = Math.Min(capped, remainingCreature);
        }

        // Enforce hard ceiling to MaxCharges
        int before = Charges;
        capped = Math.Min(capped, MaxCharges - before);
        if (capped <= 0)
            return 0;

        Charges = before + capped;
        ChargesGainedThisTurn += capped;
        if (creatureInstanceId.HasValue)
        {
            int creatureId = creatureInstanceId.Value;
            ChargesGainedThisTurnByCreature.TryGetValue(creatureId, out int current);
            ChargesGainedThisTurnByCreature[creatureId] = current + capped;
        }

        return capped;
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

    /// <summary>
    /// Reset Charges to 0 without any trigger side effects.
    /// Used by the RESET_CHARGES op (Duskfang, Censer, Grimoire triggers).
    /// </summary>
    public void ResetCharges()
    {
        Charges = 0;
    }

    /// <summary>
    /// Reset per-turn charge tracking counters.
    /// Called at the start of the owner's turn.
    /// </summary>
    public void ResetChargeTracking()
    {
        ChargesGainedThisTurn = 0;
        ChargesGainedThisTurnByCreature.Clear();
        PendingChargeFull = false;
    }
}
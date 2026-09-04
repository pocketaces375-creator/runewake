namespace Runewake.Engine.State;

/// <summary>
/// Represents one of the five lanes on a player's side of the board.
/// Each lane can hold at most one creature at a time.
/// </summary>
public sealed class LaneState
{
    /// <summary>Lane index (0–4).</summary>
    public int Index { get; }

    /// <summary>
    /// The creature or relic occupying this lane, or null if empty.
    /// </summary>
    public CardInstance? Occupant { get; set; }

    /// <summary>
    /// IDs of curse instances attached to this lane (by their InstanceId).
    /// </summary>
    public List<int> AttachedCurseIds { get; } = new();

    /// <summary>
    /// If true, this lane is buried — no creature can be played to it.
    /// Used by opening rules (e.g. Root-choked) and similar effects.
    /// </summary>
    public bool IsBuried { get; set; }

    public LaneState(int index)
    {
        Index = index;
    }

    private LaneState(LaneState other)
    {
        Index = other.Index;
        Occupant = other.Occupant?.Clone();
        AttachedCurseIds = new List<int>(other.AttachedCurseIds);
    }

    /// <summary>
    /// Returns a deep clone of this lane state.
    /// </summary>
    public LaneState Clone() => new(this);
}

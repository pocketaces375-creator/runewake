namespace Runewake.Engine.State;

/// <summary>
/// Enumeration of tutorial step identifiers.
/// None = not in tutorial (already completed or never started).
/// Complete = tutorial finished, player knows the basics.
/// </summary>
public enum TutorialStep
{
    None,
    Lanes_SummonCreature,
    Lanes_Attack,
    Lanes_EndTurn,
    Excavate_PlayExcavate,
    Excavate_BuryResolved,
    Runes_OpenRunePage,
    Runes_EquipRune,
    Complete,
}

/// <summary>
/// Mutable tutorial state data. Part of ProgressionState.
/// Pure data — no Godot dependency.
/// </summary>
public class TutorialState
{
    public TutorialStep CurrentStep { get; set; } = TutorialStep.None;
    public bool IsComplete { get; set; }

    /// <summary>
    /// Creates a deep copy for save/load isolation.
    /// </summary>
    public TutorialState Clone()
    {
        return new TutorialState
        {
            CurrentStep = CurrentStep,
            IsComplete = IsComplete,
        };
    }
}
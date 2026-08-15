namespace Runewake.Engine.State;

/// <summary>
/// Visual states for Artifact cards, driven entirely by engine data.
/// Per FIELD_EFFECT_SPEC §9: each artifact renders one of four states.
/// No client-side state guesswork — the engine always drives this.
/// </summary>
public enum ArtifactVisualState
{
    /// <summary>Default state — idle, passive active, ready to influence the field.</summary>
    READY = 0,

    /// <summary>Has accumulated charges (Charges > 0). Shows charge pips or intensity scaling.</summary>
    CHARGED = 1,

    /// <summary>
    /// Suppressed — passive off, triggers don't fire, charges frozen.
    /// Artifact is dimmed/frosted per §6. Turn counter pip visible.
    /// </summary>
    SUPPRESSED = 2,

    /// <summary>
    /// Trigger has fired this turn — spent for this turn cycle.
    /// Muted visual until reset at turn start.
    /// </summary>
    SPENT = 3
}
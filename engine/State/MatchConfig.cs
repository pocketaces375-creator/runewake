namespace Runewake.Engine.State;

/// <summary>
/// Per-match configuration overrides.
/// Default values keep shipped behavior unchanged.
/// Sim-only flags (StartingVigor20, InvokeMode, AltarMode) are TEST HARNESS ONLY
/// and default to false — no shipped rules are affected.
/// </summary>
public sealed class MatchConfig
{
    /// <summary>Starting Vigor for both players (always 25 in shipped code).</summary>
    public int StartingVigor => 25;

    /// <summary>
    /// TASK-FUN-SIM-1(a): Override StartingVigor to 20.
    /// TEST HARNESS ONLY — never shipped.
    /// </summary>
    public bool StartingVigor20 { get; init; } = false;

    /// <summary>
    /// TASK-FUN-SIM-1(b): INVOKE mode — when an artifact reaches 3 charges,
    /// the charge-full effect is HELD until the owner taps the artifact,
    /// instead of auto-firing.
    /// TEST HARNESS ONLY — never shipped.
    /// </summary>
    public bool InvokeMode { get; init; } = false;

    /// <summary>
    /// TASK-FUN-SIM-1(c): ALTAR mode — lane 2 is the War Altar (attacker +1,
    /// double combat damage taken); edge lanes 0 and 4 are the hedge
    /// (Pierce does not carry through them).
    /// TEST HARNESS ONLY — never shipped.
    /// </summary>
    public bool AltarMode { get; init; } = false;
}
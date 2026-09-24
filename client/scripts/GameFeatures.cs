namespace Runewake.Client;

/// <summary>
/// FABLE-026: switches for whole features that are built but not shown yet.
/// Flip one to true to bring the feature back — the code behind it is untouched.
/// </summary>
public static class GameFeatures
{
    /// <summary>
    /// The opening-hand mulligan screen (DuelScene.BuildMulliganOverlay). Off for now: Trikzos
    /// found it rough ("kinda ishy") and wants it back later only if the game needs it. While off,
    /// both players simply keep their opening hands. To restore it, set this to true — and give the
    /// overlay a proper design pass first (it is a plain text list over a dark panel).
    /// </summary>
    public static bool Mulligan = false;
}

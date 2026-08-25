namespace Runewake.Engine.State;

/// <summary>
/// Single source of truth for deck construction rules.
/// Both bounds live here — never scattered constants.
///
/// Current rules at launch:
///   • exactly 30 cards (min == max — Trikzos directive 2026-08-25:
///     one hard number, no range, no confusion)
///   • Singleton: max 1 copy of each unique card id
///   • Artifacts are NOT deck cards (chosen separately via artifactSlots)
/// </summary>
public static class DeckRules
{
    /// <summary>Minimum deck size (inclusive).</summary>
    public const int MinSize = 30;

    /// <summary>Maximum deck size (inclusive).</summary>
    public const int MaxSize = 30;

    /// <summary>
    /// True = at most one copy of any card definition id per deck.
    /// No exceptions at launch.
    /// </summary>
    public const bool IsSingleton = true;
}
using Runewake.Engine.State;

namespace Runewake.Engine.Engine;

/// <summary>
/// Handles opening rules — scripted effects active from turn 1 for boss encounters.
/// An encounter JSON may declare one "opening_rule" identifier. This class
/// applies the rule at game init and checks lift conditions as the game progresses.
///
/// Seat-agnostic: the rule owner is read from GameState.OpeningRuleOwner (set from
/// GameConfig.OpeningRuleOwner). All lane and player references are resolved relative
/// to that owner, so a Warden can sit in either seat (P0 or P1).
/// </summary>
public static partial class OpeningRuleHandler
{
    /// <summary>
    /// Apply the opening rule to the game state after initialization.
    /// Called from GameState.Initialize when Config.OpeningRule is set.
    /// </summary>
    public static void ApplyRule(GameState state, string ruleId)
    {
        switch (ruleId)
        {
            case "root_choked":
                ApplyRootChoked(state);
                break;
            // Future rules added here as switch cases
            default:
                // Unknown rule — log and ignore
                System.Console.Error.WriteLine($"[OpeningRuleHandler] Unknown opening rule: {ruleId}");
                break;
        }
    }

    /// <summary>
    /// Check if any opening rule lift condition is met after a creature dies.
    /// Called from DuelEngine after every creature death.
    /// </summary>
    public static void CheckLiftConditions(GameState state, int deadCreaturePlayerIndex)
    {
        if (string.IsNullOrEmpty(state.OpeningRule))
            return;

        switch (state.OpeningRule)
        {
            case "root_choked":
                CheckRootChokedLift(state, deadCreaturePlayerIndex);
                break;
        }
    }

    /// <summary>
    /// Root-choked: the challenger's leftmost lane is buried
    /// until the Warden's first creature dies.
    ///
    /// Seat-agnostic: the rule owner (Warden) is read from state.OpeningRuleOwner.
    /// The challenger is the opponent (1 - owner).
    /// The lift checks whether a creature belonging to the owner has died.
    /// </summary>
    private static void ApplyRootChoked(GameState state)
    {
        int owner = state.OpeningRuleOwner;
        int challenger = 1 - owner;

        // Bury the challenger's leftmost lane (lane 0)
        state.Players[challenger].Lanes[0].IsBuried = true;
    }

    private static void CheckRootChokedLift(GameState state, int deadCreaturePlayerIndex)
    {
        int owner = state.OpeningRuleOwner;
        int challenger = 1 - owner;

        // Rule lifts when the Warden (rule owner) loses their first creature
        if (deadCreaturePlayerIndex == owner && !state.OpeningRuleLifted[owner])
        {
            state.OpeningRuleLifted[owner] = true;
            // Un-bury the challenger's lane 0
            state.Players[challenger].Lanes[0].IsBuried = false;
        }
    }
}
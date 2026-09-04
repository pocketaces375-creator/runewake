using Runewake.Engine.State;

namespace Runewake.Engine.Engine;

/// <summary>
/// Handles opening rules — scripted effects active from turn 1 for boss encounters.
/// An encounter JSON may declare one "opening_rule" identifier. This class
/// applies the rule at game init and checks lift conditions as the game progresses.
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
    /// Root-choked: the challenger's (P0) leftmost lane (0) is buried
    /// until the Warden's (P1) first creature dies.
    /// 
    /// P1 = the enemy/Warden. When any creature controlled by P1 dies for
    /// the first time, the rule lifts — un-bury P0's lane 0.
    /// </summary>
    private static void ApplyRootChoked(GameState state)
    {
        // Bury the challenger's (P0) leftmost lane
        state.Players[0].Lanes[0].IsBuried = true;
    }

    private static void CheckRootChokedLift(GameState state, int deadCreaturePlayerIndex)
    {
        // Rule lifts when the Warden (P1) loses their first creature
        if (deadCreaturePlayerIndex == 1 && !state.OpeningRuleLifted[1])
        {
            state.OpeningRuleLifted[1] = true;
            // Un-bury P0's lane 0
            state.Players[0].Lanes[0].IsBuried = false;
        }
    }
}
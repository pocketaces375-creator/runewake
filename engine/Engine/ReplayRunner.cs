using Runewake.Engine.State;

namespace Runewake.Engine.Engine;

/// <summary>
/// Replays a <see cref="ReplayLog"/> against the engine, producing
/// the identical final game state as the original run.
/// </summary>
public static class ReplayRunner
{
    /// <summary>
    /// Replays the given log from scratch. Creates the initial state via
    /// <see cref="GameState.Initialize"/> then feeds every action through
    /// <see cref="DuelEngine.Apply"/> in order.
    /// </summary>
    public static GameState Replay(ReplayLog log)
    {
        var state = GameState.Initialize(log.Config);

        foreach (var action in log.Actions)
        {
            state = DuelEngine.Apply(state, action);
        }

        return state;
    }
}
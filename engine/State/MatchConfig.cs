namespace Runewake.Engine.State;

/// <summary>
/// Per-match configuration parameters.
/// Created by the pre-duel UI (Title → Decks → brass dial) and passed
/// through to GameConfig so the engine uses it at initialization.
/// </summary>
public sealed class MatchConfig
{
    private int _startingVigor = DefaultStartingVigor;

    /// <summary>Default starting vigor when no config is specified.</summary>
    public const int DefaultStartingVigor = 25;

    /// <summary>Minimum starting vigor allowed.</summary>
    public const int MinStartingVigor = 20;

    /// <summary>Maximum starting vigor allowed.</summary>
    public const int MaxStartingVigor = 30;

    /// <summary>
    /// Starting life (Vigor) for each player at duel start.
    /// Clamped to [20, 30]. Default: 25.
    /// All existing healing/damage/artifact vigor math unchanged —
    /// only the starting/max value becomes data.
    /// </summary>
    public int StartingVigor
    {
        get => _startingVigor;
        set => _startingVigor = Math.Clamp(value, MinStartingVigor, MaxStartingVigor);
    }

    /// <summary>
    /// Creates a MatchConfig with the default starting vigor (25).
    /// </summary>
    public MatchConfig()
    {
        _startingVigor = DefaultStartingVigor;
    }

    /// <summary>
    /// Creates a MatchConfig with a specific starting vigor.
    /// The value is clamped to [20, 30].
    /// </summary>
    public MatchConfig(int startingVigor)
    {
        StartingVigor = startingVigor;
    }
}
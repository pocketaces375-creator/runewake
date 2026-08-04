namespace Runewake.Engine.State;

/// <summary>
/// The complete deterministic state of a Runewake duel.
/// P1: (GameState, Action) -> GameState.
/// Every field is included in Clone() so replay and what-if simulation
/// each operate on independent copies.
/// </summary>
public sealed class GameState
{
    /// <summary>Both players, indexed 0 (first player) and 1 (second player).</summary>
    public PlayerState[] Players { get; }

    /// <summary>Index of the player whose turn it is (0 or 1).</summary>
    public int CurrentPlayerIndex { get; set; }

    /// <summary>Current turn number. Starts at 1 and increments after the End step.</summary>
    public int TurnNumber { get; set; }

    /// <summary>The seeded deterministic RNG used for all randomness.</summary>
    public SeededRng Rng { get; set; }

    /// <summary>Content version identifier for replay validation.</summary>
    public int ContentVersion { get; set; }

    /// <summary>
    /// Next available instance ID for a newly created card token or copy.
    /// Monotonically increasing within the game.
    /// </summary>
    public int NextInstanceId { get; set; }

    /// <summary>
    /// Current trigger chain depth. Hard cap at 20 to prevent infinite loops.
    /// </summary>
    public int TriggerDepth { get; set; }

    /// <summary>
    /// True when the game has ended (a player reached 0 Vigor).
    /// </summary>
    public bool IsGameOver { get; set; }

    /// <summary>
    /// Index of the winning player, or null if the game is not yet over.
    /// </summary>
    public int? WinnerIndex { get; set; }

    /// <summary>
    /// The action log for replay generation: every action applied this game.
    /// Not cloned for performance — replays build their own.
    /// </summary>
    public List<object> ActionLog { get; } = new();

    public GameState(ulong seed, int contentVersion = 1)
    {
        Players = new PlayerState[2];
        Players[0] = new PlayerState(0);
        Players[1] = new PlayerState(1);
        CurrentPlayerIndex = 0;
        TurnNumber = 1;
        Rng = new SeededRng(seed);
        ContentVersion = contentVersion;
        NextInstanceId = 1;
    }

    private GameState(GameState other)
    {
        Players = new PlayerState[2];
        Players[0] = other.Players[0].Clone();
        Players[1] = other.Players[1].Clone();
        CurrentPlayerIndex = other.CurrentPlayerIndex;
        TurnNumber = other.TurnNumber;
        Rng = other.Rng.Clone();
        ContentVersion = other.ContentVersion;
        NextInstanceId = other.NextInstanceId;
        TriggerDepth = other.TriggerDepth;
        IsGameOver = other.IsGameOver;
        WinnerIndex = other.WinnerIndex;
    }

    /// <summary>
    /// Returns a deep clone of the entire game state.
    /// The ActionLog is not cloned (it's append-only and replay-constructed).
    /// </summary>
    public GameState Clone() => new(this);

    /// <summary>
    /// Shortcut for the current player.
    /// </summary>
    public PlayerState CurrentPlayer => Players[CurrentPlayerIndex];

    /// <summary>
    /// Shortcut for the opponent of the current player.
    /// </summary>
    public PlayerState Opponent => Players[1 - CurrentPlayerIndex];

    /// <summary>
    /// Returns a player state by index (0 or 1).
    /// </summary>
    public PlayerState Player(int index) => Players[index];

    /// <summary>
    /// Returns the opposing player index.
    /// </summary>
    public int OpponentIndex(int playerIndex) => 1 - playerIndex;
}

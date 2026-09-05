namespace Runewake.Engine.State;

/// <summary>
/// Configuration for initializing a deterministic game state.
/// Contains the seed, content version, and deck composition for both players.
/// Together these fully determine the initial state — decks are shuffled
/// and starting hands are dealt using the seeded RNG.
/// </summary>
public sealed class GameConfig
{
    /// <summary>Seed for the deterministic RNG. Same seed => same shuffle, same draws, same game.</summary>
    public ulong Seed { get; init; }

    /// <summary>Content version for replay validation and rule-set gating.</summary>
    public int ContentVersion { get; init; } = 1;

    /// <summary>Card definition IDs for player 0's deck (top-to-bottom before shuffle).</summary>
    public List<string> Player0DeckIds { get; init; } = new();

    /// <summary>Card definition IDs for player 1's deck (top-to-bottom before shuffle).</summary>
    public List<string> Player1DeckIds { get; init; } = new();

    /// <summary>Optional rune page for player 0 (human). Runes are injected at match start.</summary>
    public RunePage? RunePage { get; init; }

    // ——— Artifact system ———

    /// <summary>Artifact definition IDs for player 0 (array length = slot count, 2 for launch).</summary>
    public string[] Player0ArtifactIds { get; init; } = Array.Empty<string>();

    /// <summary>Artifact definition IDs for player 1.</summary>
    public string[] Player1ArtifactIds { get; init; } = Array.Empty<string>();

    /// <summary>Class name for player 0 (e.g. "warrior"). Empty = no Artifacts.</summary>
    public string Player0Class { get; init; } = string.Empty;

    /// <summary>Class name for player 1.</summary>
    public string Player1Class { get; init; } = string.Empty;

    /// <summary>
    /// Per-match configuration (starting vigor, etc.).
    /// When null, the engine uses default values (StartingVigor=25).
    /// </summary>
    public MatchConfig? MatchConfig { get; init; }

    /// <summary>
    /// Optional opening rule identifier from the encounter (e.g. "root_choked").
    /// Applied at game init. Shown as a banner card in the UI.
    /// </summary>
    public string? OpeningRule { get; init; }

    /// <summary>
    /// Player index (0 or 1) that owns the opening rule (the Warden/enemy).
    /// Defaults to 1 for backward compatibility (encounter is always P1 in campaign).
    /// The rule is resolved relative to this owner: the opponent is the challenger.
    /// </summary>
    public int OpeningRuleOwner { get; init; } = 1;
}
using System;
using System.Collections.Generic;
using System.Linq;
using Runewake.Engine.Cards;

namespace Runewake.Engine.State;

/// <summary>
/// Runtime state for a dig site interaction — tracks which tiles have been
/// struck, rewards earned, and completion status.
/// </summary>
public class DigState
{
    /// <summary>ID of the dig site this state belongs to.</summary>
    public string DigSiteId { get; }

    /// <summary>How many strikes the player has left.</summary>
    public int StrikesRemaining { get; private set; }

    /// <summary>Boolean array tracking which tiles have been revealed (indexed by tile position).</summary>
    public bool[] TilesRevealed { get; }

    /// <summary>Number of tiles revealed so far.</summary>
    public int TilesCleared { get; private set; }

    /// <summary>Whether the headline find has been claimed.</summary>
    public bool HeadlineClaimed { get; private set; }

    /// <summary>Rewards earned so far (tile rewards + headline reward).</summary>
    public List<DigRewardEntry> RewardsEarned { get; } = new();

    /// <summary>Whether the dig is complete (no strikes left or all tiles revealed).</summary>
    public bool IsComplete => StrikesRemaining <= 0 || TilesCleared >= TilesRevealed.Length;

    private readonly int _headlineThreshold;

    private DigState(string siteId, int strikes, int totalTiles, int headlineThreshold)
    {
        DigSiteId = siteId;
        StrikesRemaining = strikes;
        TilesRevealed = new bool[totalTiles];
        _headlineThreshold = headlineThreshold;
    }

    // Private constructor for cloning
    private DigState(DigState other)
    {
        DigSiteId = other.DigSiteId;
        StrikesRemaining = other.StrikesRemaining;
        TilesRevealed = (bool[])other.TilesRevealed.Clone();
        TilesCleared = other.TilesCleared;
        HeadlineClaimed = other.HeadlineClaimed;
        _headlineThreshold = other._headlineThreshold;
        RewardsEarned = new List<DigRewardEntry>(other.RewardsEarned);
    }

    /// <summary>
    /// Create a dig state from a site definition.
    /// </summary>
    public static DigState FromDef(DigSiteDef site)
    {
        return new DigState(site.Id, site.Strikes, site.Tiles.Count, site.HeadlineThreshold);
    }

    /// <summary>
    /// Apply a strike at the given tile index. Returns the reward, or null if
    /// the tile was already revealed or strikes are exhausted.
    /// </summary>
    public DigRewardEntry? ApplyStrike(int tileIndex, DigSiteDef site)
    {
        if (IsComplete) return null;
        if (tileIndex < 0 || tileIndex >= TilesRevealed.Length) return null;
        if (TilesRevealed[tileIndex]) return null;

        StrikesRemaining--;
        TilesRevealed[tileIndex] = true;
        TilesCleared++;

        var tileDef = site.Tiles[tileIndex];
        var reward = new DigRewardEntry(tileDef.Type, tileDef.Value);
        RewardsEarned.Add(reward);

        // Check headline threshold — add headline reward as an extra entry
        if (!HeadlineClaimed && TilesCleared >= _headlineThreshold && site.HeadlineReward != null)
        {
            HeadlineClaimed = true;
            RewardsEarned.Add(new DigRewardEntry(DigRewardType.RELIC, site.HeadlineReward));
        }

        return reward;
    }

    /// <summary>
    /// Creates a deep copy of this dig state.
    /// </summary>
    public DigState Clone() => new(this);
}

/// <summary>
/// A reward entry earned from a dig strike.
/// </summary>
public class DigRewardEntry
{
    public DigRewardType Type { get; }
    public string? Value { get; }

    public DigRewardEntry(DigRewardType type, string? value)
    {
        Type = type;
        Value = value;
    }
}
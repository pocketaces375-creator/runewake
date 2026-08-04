using System.Collections.Generic;
using System.Linq;
using Runewake.Engine.Cards;

namespace Runewake.Engine.State;

/// <summary>
/// A reward earned from a dig tile reveal.
/// </summary>
public class DigRewardEntry
{
    /// <summary>Type of reward.</summary>
    public DigRewardType Type { get; set; }

    /// <summary>Value string (same semantics as DigTileDef.Value).</summary>
    public string? Value { get; set; }
}

/// <summary>
/// Runtime state for an in-progress dig session.
/// Tracks which tiles have been revealed, strikes remaining, and rewards earned.
/// </summary>
public class DigState
{
    /// <summary>Which dig site definition this session is using.</summary>
    public string DigSiteId { get; set; } = string.Empty;

    /// <summary>Number of strikes remaining (decremented per reveal).</summary>
    public int StrikesRemaining { get; set; }

    /// <summary>Revealed state for each tile, row-major order.</summary>
    public bool[] TilesRevealed { get; set; } = System.Array.Empty<bool>();

    /// <summary>Total number of tiles revealed so far.</summary>
    public int TilesCleared { get; set; }

    /// <summary>Whether the headline find has been claimed.</summary>
    public bool HeadlineClaimed { get; set; }

    /// <summary>Rewards earned from reveals during this dig session.</summary>
    public List<DigRewardEntry> RewardsEarned { get; set; } = new();

    /// <summary>True when no more strikes remain or headline has been claimed.</summary>
    public bool IsComplete => StrikesRemaining <= 0 || HeadlineClaimed;

    /// <summary>
    /// Apply a strike at the given tile index.
    /// Returns the reward entry if the tile was newly revealed, null if already revealed.
    /// </summary>
    public DigRewardEntry? ApplyStrike(int tileIndex, DigSiteDef siteDef)
    {
        if (IsComplete) return null;
        if (tileIndex < 0 || tileIndex >= TilesRevealed.Length) return null;
        if (TilesRevealed[tileIndex]) return null;

        // Reveal the tile
        TilesRevealed[tileIndex] = true;
        TilesCleared++;
        StrikesRemaining--;

        // Get the reward from site definition
        var tileDef = siteDef.Tiles[tileIndex];
        var reward = new DigRewardEntry
        {
            Type = tileDef.Type,
            Value = tileDef.Value
        };
        RewardsEarned.Add(reward);

        // Check headline threshold
        if (TilesCleared >= siteDef.HeadlineThreshold && siteDef.HeadlineReward != null && !HeadlineClaimed)
        {
            HeadlineClaimed = true;
            RewardsEarned.Add(new DigRewardEntry
            {
                Type = DigRewardType.RELIC,
                Value = siteDef.HeadlineReward
            });
        }

        return reward;
    }

    /// <summary>
    /// Factory method: create a fresh DigState from a DigSiteDef.
    /// </summary>
    public static DigState FromDef(DigSiteDef siteDef)
    {
        return new DigState
        {
            DigSiteId = siteDef.Id,
            StrikesRemaining = siteDef.Strikes,
            TilesRevealed = new bool[siteDef.Rows * siteDef.Cols],
            TilesCleared = 0,
            HeadlineClaimed = false,
            RewardsEarned = new List<DigRewardEntry>()
        };
    }

    /// <summary>
    /// Create a deep clone for replay or snapshot purposes.
    /// </summary>
    public DigState Clone()
    {
        return new DigState
        {
            DigSiteId = DigSiteId,
            StrikesRemaining = StrikesRemaining,
            TilesRevealed = (bool[])TilesRevealed.Clone(),
            TilesCleared = TilesCleared,
            HeadlineClaimed = HeadlineClaimed,
            RewardsEarned = RewardsEarned.Select(r => new DigRewardEntry
            {
                Type = r.Type,
                Value = r.Value
            }).ToList()
        };
    }
}
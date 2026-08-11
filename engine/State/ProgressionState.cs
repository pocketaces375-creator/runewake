using System.Collections.Generic;
using Runewake.Engine.Cards;

namespace Runewake.Engine.State;

/// <summary>
/// In-memory representation of player progression.
/// All fields are mutable; call <see cref="SaveManager.Save"/> to persist.
/// </summary>
public class ProgressionState
{
    /// <summary>Save format version for future migrations.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Currency: earned from duels and spent at merchants.</summary>
    public int Shards { get; set; }

    /// <summary>Dig site interaction currency.</summary>
    public int DigCharges { get; set; }

    /// <summary>Node IDs that have been cleared (won).</summary>
    public HashSet<string> ClearedNodes { get; } = new();

    /// <summary>Card IDs the player has collected (keys). Value is copy count.</summary>
    public Dictionary<string, int> Collection { get; } = new();

    /// <summary>Rune fragment counts per strata (e.g. "verdant" → 4).</summary>
    public Dictionary<string, int> Fragments { get; } = new();

    /// <summary>Rune IDs the player has forged or acquired.</summary>
    public HashSet<string> OwnedRuneIds { get; } = new();

    /// <summary>Dig tool IDs the player has unlocked from Elite nodes.</summary>
    public HashSet<string> UnlockedTools { get; } = new();

    /// <summary>Discovered Lost Relic instances.</summary>
    public List<LostRelicInstance> DiscoveredRelics { get; } = new();

    /// <summary>
    /// The player's saved 30-card deck. Populated by the deck builder;
    /// loaded on next startup. Empty if no custom deck has been saved yet.
    /// </summary>
    public List<string> DeckCardIds { get; } = new();

    /// <summary>
    /// Global discovery index counter. Incremented each time a relic is minted.
    /// per card_id. The discovery_index on a relic is the value at mint time.
    /// </summary>
    public int GlobalDiscoveryIndex { get; set; }

    /// <summary>Whether the player has completed the first-run tutorial/intro duel.</summary>
    public bool HasCompletedTutorial { get; set; }

    /// <summary>Current Delver Level (1-20). Starts at 1.</summary>
    public int DelverLevel { get; set; } = 1;

    /// <summary>Cumulative XP earned toward next Delver Level.</summary>
    public int DelverXp { get; set; }

    /// <summary>Serialized rune page slot data (JSON). Loaded on startup into CampaignContext.CurrentRunePage.</summary>
    public string? SavedRunePageJson { get; set; }

    /// <summary>Tutorial state (null for existing saves — treated as None).</summary>
    public TutorialState? Tutorial { get; set; }

    /// <summary>Add shards (positive) or spend (negative). Returns false if insufficient.</summary>
    public bool SpendShards(int amount)
    {
        if (amount < 0 || Shards < amount) return false;
        Shards -= amount;
        return true;
    }

    /// <summary>Add dig charges (positive) or spend (negative). Returns false if insufficient.</summary>
    public bool SpendDigCharge()
    {
        if (DigCharges < 1) return false;
        DigCharges--;
        return true;
    }

    /// <summary>Mark a node as cleared. Returns false if already cleared.</summary>
    public bool MarkNodeCleared(string nodeId)
    {
        return ClearedNodes.Add(nodeId);
    }

    /// <summary>Check if a node is cleared.</summary>
    public bool IsNodeCleared(string nodeId) => ClearedNodes.Contains(nodeId);

    /// <summary>Add a card to the collection (or increment its count).</summary>
    public void AddCard(string cardId, int count = 1)
    {
        if (Collection.TryGetValue(cardId, out var existing))
            Collection[cardId] = existing + count;
        else
            Collection[cardId] = count;
    }

    /// <summary>Add fragments of a given strata.</summary>
    public void AddFragments(string strata, int count)
    {
        if (Fragments.TryGetValue(strata, out var existing))
            Fragments[strata] = existing + count;
        else
            Fragments[strata] = count;
    }

    /// <summary>Check if a rune has been acquired.</summary>
    public bool OwnsRune(string runeId) => OwnedRuneIds.Contains(runeId);

    /// <summary>Mark a rune as acquired. Returns false if already owned.</summary>
    public bool AddOwnedRune(string runeId) => OwnedRuneIds.Add(runeId);

    /// <summary>Check if a dig tool has been unlocked.</summary>
    public bool HasTool(string toolId) => UnlockedTools.Contains(toolId);

    /// <summary>Unlock a dig tool. Returns false if already unlocked.</summary>
    public bool UnlockTool(string toolId) => UnlockedTools.Add(toolId);

    /// <summary>Add a discovered relic instance. Returns the relic's discovery index.</summary>
    public LostRelicInstance AddRelic(LostRelicInstance relic)
    {
        DiscoveredRelics.Add(relic);
        GlobalDiscoveryIndex++;
        return relic;
    }

    /// <summary>Check if a specific encounter's relic has already been discovered by this player.</summary>
    public bool HasDiscoveredEncounterRelic(string encounterId)
    {
        // Check if any discovered relic matches an encounter by looking up the encounter's
        // card_id in the relic def index (supplied externally via CampaignContext)
        // For engine-level check without CampaignContext, just check by card_id match
        return false; // runtime check happens in DuelScene via LostRelicIndex
    }
}
using System.Collections.Generic;

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

    /// <summary>Whether the player has completed the first-run tutorial/intro duel.</summary>
    public bool HasCompletedTutorial { get; set; }

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
}
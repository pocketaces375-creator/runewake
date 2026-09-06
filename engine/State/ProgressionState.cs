using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Named decks saved by the player. Key = deck name, value = card IDs.
    /// Populated via Deck Forge's SAVE button. Survives corrupt-save auto-repair
    /// (repairs to empty dict, never to null/blank).
    /// </summary>
    public Dictionary<string, List<string>> SavedDecks { get; } = new();

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

    /// <summary>
    /// Grant the starter collection for a chosen class: adds 1 copy of each
    /// card in the starter deck to the player's collection.
    /// </summary>
    /// <param name="starterCardIds">The list of card IDs from the chosen class's starter deck.</param>
    public void GrantStarterCollection(List<string> starterCardIds)
    {
        foreach (var cardId in starterCardIds)
            AddCard(cardId);
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
        return false; // runtime check happens in DuelScene via LostRelicIndex
    }

    /// <summary>Currency from grinding extra card copies into runes. Display label \"Runes\".</summary>
    public int RuneDust { get; set; }

    /// <summary>
    /// Shop rotation counter. Incremented each time the card shop refreshes.
    /// Used with a fixed seed to deterministically select which ~6 cards are offered.
    /// </summary>
    public int ShopRotationDay { get; set; }

    /// <summary>Arena duel win count.</summary>
    public int ArenaWins { get; set; }

    /// <summary>Arena duel loss count.</summary>
    public int ArenaLosses { get; set; }

    /// <summary>RuneDust value per card rarity (C/U/R/M).</summary>
    private static readonly Dictionary<Rarity, int> RuneDustValues = new()
    {
        { Rarity.COMMON, 5 },
        { Rarity.UNCOMMON, 15 },
        { Rarity.RARE, 40 },
        { Rarity.RELIC, 120 },
    };

    /// <summary>Get the RuneDust yield for a given rarity. Returns 0 for unknown rarities.</summary>
    public static int GetRuneDustValue(Rarity rarity) =>
        RuneDustValues.GetValueOrDefault(rarity, 0);

    /// <summary>
    /// Check whether a card can be ground into RuneDust.
    /// A card CANNOT be ground if:
    ///   - owned count &lt;= 1 (cannot grind the last copy)
    ///   - owned - 1 &lt; number of saved decks containing that card (deck dependency)
    /// </summary>
    /// <param name="cardId">The card ID to check.</param>
    /// <param name="savedDecks">All saved decks. Key = deck name, value = card IDs.</param>
    /// <param name="error">Human-readable reason if grinding is not allowed, or null if it is allowed.</param>
    /// <returns>True if grinding is allowed.</returns>
    public bool CanGrindCard(string cardId, IReadOnlyDictionary<string, List<string>> savedDecks, out string? error)
    {
        error = null;
        if (!Collection.TryGetValue(cardId, out var owned) || owned <= 0)
        {
            error = $"Don't own any copy of \"{cardId}\".";
            return false;
        }

        // Cannot grind the last copy
        if (owned <= 1)
        {
            error = "Cannot grind the last copy — keep at least one.";
            return false;
        }

        // Count how many decks use this card
        int deckCount = 0;
        if (savedDecks != null)
        {
            foreach (var (_, cardIds) in savedDecks)
            {
                if (cardIds != null && cardIds.Contains(cardId))
                    deckCount++;
            }
        }

        // After grinding, owned would be (owned - 1). Must still satisfy all decks.
        if (owned - 1 < deckCount)
        {
            error = $"Needs {deckCount} copy(-ies) in saved decks. Cannot grind — keep at least {owned - deckCount}.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Grind one copy of a card into RuneDust.
    /// Returns the amount of RuneDust added. Returns 0 if the card cannot be ground.
    /// The caller should check <see cref="CanGrindCard"/> first and surface the error to the player.
    /// </summary>
    /// <param name="cardId">The card ID to grind.</param>
    /// <param name="savedDecks">All saved decks for dependency checking.</param>
    /// <returns>The amount of RuneDust added, or 0 if the grind was rejected.</returns>
    public int GrindCard(string cardId, IReadOnlyDictionary<string, List<string>> savedDecks)
    {
        if (!CanGrindCard(cardId, savedDecks, out var _))
            return 0;

        // Determine rarity from card definition
        var def = CardRegistry.Get(cardId);
        if (def == null)
            return 0;

        int value = GetRuneDustValue(def.Rarity);
        if (value <= 0)
            return 0;

        // Decrement collection
        Collection[cardId] = Collection[cardId] - 1;
        if (Collection[cardId] <= 0)
            Collection.Remove(cardId);

        // Add rune dust
        RuneDust += value;
        return value;
    }

    /// <summary>
    /// Spend RuneDust. Returns false if insufficient.
    /// </summary>
    public bool SpendRuneDust(int amount, out int shortfall)
    {
        shortfall = 0;
        if (amount <= 0) return true;
        if (RuneDust < amount)
        {
            shortfall = amount - RuneDust;
            return false;
        }
        RuneDust -= amount;
        return true;
    }

    /// <summary>
    /// Spend RuneDust. Simple overload for cases where shortfall is not needed.
    /// </summary>
    public bool SpendRuneDust(int amount)
    {
        return SpendRuneDust(amount, out _);
    }

    /// <summary>Card IDs that have been seen (viewed) in the Reliquary. Cleared on first view.</summary>
    public HashSet<string> SeenCardIds { get; } = new();

    /// <summary>Check if a card has been seen in the Reliquary (NEW badge cleared on view).</summary>
    public bool IsCardSeen(string cardId) => SeenCardIds.Contains(cardId);

    /// <summary>Mark a card as seen (clears the NEW badge).</summary>
    public void MarkCardSeen(string cardId) => SeenCardIds.Add(cardId);
}
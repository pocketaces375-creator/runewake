using System.Collections.Concurrent;
using System.Linq;

namespace Runewake.Engine.Cards;

/// <summary>
/// A thread-safe registry of card definitions used for game initialization.
/// Cards are registered by their <c>Id</c> and resolved when building initial decks.
/// </summary>
public static class CardRegistry
{
    private static readonly ConcurrentDictionary<string, CardDef> _cards = new();

    /// <summary>
    /// Register a card definition so it can be resolved by ID.
    /// Replaces any existing registration with the same ID.
    /// </summary>
    public static void Register(CardDef def)
    {
        _cards[def.Id] = def;
    }

    /// <summary>
    /// Register multiple card definitions at once.
    /// </summary>
    public static void RegisterRange(IEnumerable<CardDef> defs)
    {
        foreach (var def in defs)
            _cards[def.Id] = def;
    }

    /// <summary>
    /// Resolve a card definition by ID, or null if not found.
    /// </summary>
    public static CardDef? Get(string id)
    {
        _cards.TryGetValue(id, out var def);
        return def;
    }

    /// <summary>
    /// Clear all registered cards (for testing teardown).
    /// </summary>
    public static void Clear()
    {
        _cards.Clear();
    }

    /// <summary>
    /// Get all registered card definitions.
    /// </summary>
    public static List<CardDef> GetAll()
    {
        return _cards.Values.ToList();
    }
}
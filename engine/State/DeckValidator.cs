using System;
using System.Collections.Generic;
using System.Linq;
using Runewake.Engine.Cards;

namespace Runewake.Engine.State;

/// <summary>
/// Result of validating a deck against game rules.
/// The engine owns legality — the client reads the verdict.
/// </summary>
public sealed class DeckValidationResult
{
    /// <summary>True if the deck is legal (30 cards, ≤2 copies, ≤1 RELIC, 1-2 Strata).</summary>
    public bool IsValid { get; set; }

    /// <summary>Global validation errors describing why the deck is illegal.</summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>Per-card rejection reasons. CardId → human-readable reason.</summary>
    public Dictionary<string, string> PerCardErrors { get; set; } = new();
}

/// <summary>
/// Engine-owned deck legality rules.
///
/// A legal deck must satisfy ALL of:
///   • 30 cards exactly
///   • At most 2 copies of any card
///   • At most 1 RELIC-rarity card
///   • Cards from at most 2 distinct Strata
///
/// Use <see cref="Validate"/> for final save-button validation and
/// <see cref="CanAdd"/> for per-card grey-out feedback.
/// </summary>
public static class DeckValidator
{
    /// <summary>Validate a complete deck. Returns errors and per-card reasons.</summary>
    public static DeckValidationResult Validate(
        IReadOnlyList<string> deckIds,
        Func<string, CardDef?> lookup)
    {
        var result = new DeckValidationResult();
        if (deckIds == null) throw new ArgumentNullException(nameof(deckIds));

        // Count
        if (deckIds.Count != 30)
            result.Errors.Add($"Deck must be exactly 30 cards (currently {deckIds.Count}).");

        // Copy counts
        var copyCount = new Dictionary<string, int>();
        foreach (var id in deckIds)
        {
            copyCount.TryGetValue(id, out var c);
            copyCount[id] = c + 1;
        }
        foreach (var (id, count) in copyCount)
        {
            if (count > 2)
            {
                string name = lookup(id)?.Name ?? id;
                result.Errors.Add($"\"{name}\" appears {count} times (max 2).");
            }
        }

        // RELIC count
        int relicCount = 0;
        string? firstRelicName = null;
        foreach (var id in deckIds)
        {
            var def = lookup(id);
            if (def?.Rarity == Rarity.RELIC)
            {
                relicCount++;
                firstRelicName ??= def.Name;
            }
        }
        if (relicCount > 1)
            result.Errors.Add($"Deck has {relicCount} RELIC cards (max 1). Remove all but one.");

        // Strata diversity
        var strata = new HashSet<Strata>();
        foreach (var id in deckIds)
        {
            var def = lookup(id);
            if (def != null) strata.Add(def.Strata);
        }
        if (strata.Count > 2)
        {
            var names = string.Join(", ", strata.Select(s => s.ToString()));
            result.Errors.Add($"Deck uses {strata.Count} Strata ({names}; max 2).");
        }

        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    /// <summary>
    /// Check whether a hypothetical add would leave the deck legal.
    /// Returns per-card errors keyed by the proposed card ID.
    /// The caller uses this to grey out add buttons.
    /// </summary>
    public static DeckValidationResult CanAdd(
        IReadOnlyList<string> currentDeck,
        string cardId,
        Func<string, CardDef?> lookup)
    {
        var result = new DeckValidationResult();
        if (cardId == null) throw new ArgumentNullException(nameof(cardId));
        var def = lookup(cardId);
        if (def == null)
        {
            result.Errors.Add($"Card \"{cardId}\" not found.");
            result.PerCardErrors[cardId] = "Card not found.";
            return result;
        }

        if (currentDeck.Count >= 30)
        {
            result.PerCardErrors[cardId] = "Deck is full (30/30).";
            result.Errors.Add("Deck is full.");
            return result;
        }

        // Copy count check
        int copies = currentDeck.Count(id => id == cardId);
        if (copies >= 2)
        {
            result.PerCardErrors[cardId] = $"Already have {copies} copies (max 2).";
            result.Errors.Add($"\"{def.Name}\" would exceed 2 copies.");
            return result;
        }

        // RELIC count check
        if (def.Rarity == Rarity.RELIC)
        {
            int existingRelic = currentDeck.Count(id => lookup(id)?.Rarity == Rarity.RELIC);
            if (existingRelic >= 1)
            {
                result.PerCardErrors[cardId] = "Only 1 RELIC card allowed.";
                result.Errors.Add("Would exceed max 1 RELIC card.");
                return result;
            }
        }

        // Strata diversity check
        var strata = new HashSet<Strata>();
        foreach (var id in currentDeck)
        {
            var d = lookup(id);
            if (d != null) strata.Add(d.Strata);
        }
        strata.Add(def.Strata);
        if (strata.Count > 2)
        {
            result.PerCardErrors[cardId] = $"Would add a 3rd Strata ({def.Strata}).";
            result.Errors.Add($"Adding \"{def.Name}\" would make {strata.Count} Strata (max 2).");
            return result;
        }

        result.IsValid = true;
        return result;
    }
}
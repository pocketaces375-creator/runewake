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
    /// <summary>True if the deck is legal (30-40 cards, singleton).</summary>
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
///   • exactly 30 cards (DeckRules.MinSize == DeckRules.MaxSize)
///   • Singleton: at most 1 copy of any card id
///
/// Rules are defined in <see cref="DeckRules"/> — the single source of truth.
/// Error strings are specific so the client can surface them as red-ink annotations.
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

        // Size bounds
        if (deckIds.Count < DeckRules.MinSize)
            result.Errors.Add($"too few cards ({deckIds.Count}/{DeckRules.MinSize} minimum)");
        else if (deckIds.Count > DeckRules.MaxSize)
            result.Errors.Add($"too many cards ({deckIds.Count}/{DeckRules.MaxSize} maximum)");

        // Singleton: max 1 copy of each unique card id
        var seenIds = new HashSet<string>();
        foreach (var id in deckIds)
        {
            if (!seenIds.Add(id))
            {
                string name = lookup(id)?.Name ?? id;
                result.Errors.Add($"duplicate: {name}");
            }
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

        // Size check
        if (currentDeck.Count >= DeckRules.MaxSize)
        {
            result.PerCardErrors[cardId] = $"Deck is full ({currentDeck.Count}/{DeckRules.MaxSize}).";
            result.Errors.Add("Deck is full.");
            return result;
        }

        // Singleton check
        if (currentDeck.Contains(cardId))
        {
            string name = def.Name;
            result.PerCardErrors[cardId] = $"Already have 1 copy of \"{name}\" (singleton).";
            result.Errors.Add($"duplicate: {name}");
            return result;
        }

        result.IsValid = true;
        return result;
    }
}
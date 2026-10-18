using System;
using System.Collections.Generic;
using System.Linq;
using Runewake.Engine.Cards;

namespace Runewake.Engine.State;

/// <summary>
/// FABLE-066: one set of rules for a campaign's decks, so "the deck I edited" and "the deck I play" can
/// never drift apart again (Trikzos: "I've made new decks or redone mine, every time it resets").
///
/// The campaign save is the truth:
///   SavedDecks[name]      every deck, finished or not
///   DeckArtifacts[name]   its two artifacts
///   ActiveDeckName        the one you play
///   DeckCardIds / ActiveArtifacts   a copy of the active deck's LAST COMPLETE version (what duels use)
///
/// The bug this replaces: the Forge's "Save" wrote only SavedDecks, but the Forge opened on — and duels
/// used — DeckCardIds and decks.json, so a saved edit to the deck you play looked like it had reset.
/// Now saving the active deck updates what you play (when it is complete), and every deck is listed with
/// a clear "Play this deck".
/// </summary>
public static class DeckBook
{
    public const string DefaultName = "My Deck";

    public sealed record Deck(string Name, List<string> Cards, List<string> Artifacts, bool IsActive, bool IsComplete);

    /// <summary>Name of the deck being played, repairing older saves that never recorded one.</summary>
    public static string? ActiveName(ProgressionState p)
    {
        if (!string.IsNullOrEmpty(p.ActiveDeckName) && p.SavedDecks.ContainsKey(p.ActiveDeckName)) return p.ActiveDeckName;
        // an older save: the active deck is whichever saved deck holds exactly the cards being played
        if (p.DeckCardIds.Count > 0)
        {
            var match = p.SavedDecks.FirstOrDefault(kv => kv.Value != null && kv.Value.SequenceEqual(p.DeckCardIds));
            if (match.Key != null) return match.Key;
        }
        return null;
    }

    /// <summary>
    /// Make sure the campaign's active deck is a named, listed deck. Older saves can hold a played deck that no
    /// saved deck matches (the exact situation the old Save button produced); it is listed as "My Deck" (or
    /// "My Deck 2"…), never thrown away. Returns true if anything changed.
    /// </summary>
    public static bool Normalize(ProgressionState p)
    {
        bool changed = false;
        var name = ActiveName(p);
        if (name == null && p.DeckCardIds.Count > 0)
        {
            name = FreeName(p, DefaultName);
            p.SavedDecks[name] = new List<string>(p.DeckCardIds);
            if (p.ActiveArtifacts.Count > 0) p.DeckArtifacts[name] = new List<string>(p.ActiveArtifacts);
            changed = true;
        }
        if (name != null && p.ActiveDeckName != name) { p.ActiveDeckName = name; changed = true; }
        if (name == null && !string.IsNullOrEmpty(p.ActiveDeckName)) { p.ActiveDeckName = ""; changed = true; }
        return changed;
    }

    public static IEnumerable<Deck> All(ProgressionState p, Func<string, CardDef?> lookup)
    {
        var active = ActiveName(p);
        foreach (var kv in p.SavedDecks.OrderBy(k => k.Key == active ? 0 : 1).ThenBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            yield return new Deck(kv.Key, new List<string>(kv.Value),
                p.DeckArtifacts.TryGetValue(kv.Key, out var a) ? new List<string>(a) : new List<string>(),
                kv.Key == active, IsComplete(kv.Value, lookup));
    }

    public static bool IsComplete(IReadOnlyList<string> cards, Func<string, CardDef?> lookup) =>
        DeckValidator.Validate(cards.ToList(), lookup).IsValid;

    /// <summary>
    /// Keep a deck under its name, at any size. If it is the deck being played (or nothing is) and it is
    /// complete, it is what you play from now on. Returns true when it became / stayed the played deck with
    /// these exact cards.
    /// </summary>
    public static bool Save(ProgressionState p, string name, IReadOnlyList<string> cards, IReadOnlyList<string>? artifacts, Func<string, CardDef?> lookup)
    {
        name = Clean(name);
        p.SavedDecks[name] = cards.ToList();
        if (artifacts != null) p.DeckArtifacts[name] = artifacts.Where(a => !string.IsNullOrEmpty(a)).ToList();
        var active = ActiveName(p);
        if ((active == null || active == name) && IsComplete(cards, lookup))
        {
            Activate(p, name);
            return true;
        }
        if (active == null) p.ActiveDeckName = name;   // even unfinished, it is the campaign's deck
        return false;
    }

    /// <summary>Play this deck from now on. Only a complete deck can be played. False (and nothing changes) otherwise.</summary>
    public static bool SetActive(ProgressionState p, string name, Func<string, CardDef?> lookup)
    {
        if (!p.SavedDecks.TryGetValue(name, out var cards) || !IsComplete(cards, lookup)) return false;
        Activate(p, name);
        return true;
    }

    private static void Activate(ProgressionState p, string name)
    {
        var cards = p.SavedDecks[name];
        p.ActiveDeckName = name;
        p.DeckCardIds.Clear();
        p.DeckCardIds.AddRange(cards);
        // FABLE-072: a deck with no artifact record (an older deck, a cloud copy from before artifacts were kept
        // per deck) used to wipe the pair you had — every activation went back to the class defaults. Now the
        // pair you were playing stays, and becomes this deck's record.
        if (p.DeckArtifacts.TryGetValue(name, out var a) && a.Count > 0)
        {
            p.ActiveArtifacts.Clear();
            p.ActiveArtifacts.AddRange(a);
        }
        else if (p.ActiveArtifacts.Count > 0)
            p.DeckArtifacts[name] = new List<string>(p.ActiveArtifacts);
    }

    /// <summary>Rename a deck (cards, artifacts and "active" move with it). False if the new name is taken.</summary>
    public static bool Rename(ProgressionState p, string from, string to)
    {
        to = Clean(to);
        if (from == to) return true;
        if (!p.SavedDecks.TryGetValue(from, out var cards) || p.SavedDecks.ContainsKey(to)) return false;
        p.SavedDecks.Remove(from);
        p.SavedDecks[to] = cards;
        if (p.DeckArtifacts.Remove(from, out var arts)) p.DeckArtifacts[to] = arts;
        if (p.ActiveDeckName == from) p.ActiveDeckName = to;
        return true;
    }

    /// <summary>A copy under a free name ("Name (copy)"). Returns the new name.</summary>
    public static string Duplicate(ProgressionState p, string name)
    {
        if (!p.SavedDecks.TryGetValue(name, out var cards)) return name;
        string copy = FreeName(p, $"{name} (copy)");
        p.SavedDecks[copy] = new List<string>(cards);
        if (p.DeckArtifacts.TryGetValue(name, out var a)) p.DeckArtifacts[copy] = new List<string>(a);
        return copy;
    }

    /// <summary>
    /// Delete a deck. The deck being played can only go when another complete deck exists — that one becomes
    /// the played deck (a campaign must always have something to play). False if refused.
    /// </summary>
    public static bool Delete(ProgressionState p, string name, Func<string, CardDef?> lookup)
    {
        if (!p.SavedDecks.ContainsKey(name)) return false;
        if (ActiveName(p) == name)
        {
            var next = p.SavedDecks.Where(kv => kv.Key != name && IsComplete(kv.Value, lookup)).Select(kv => kv.Key).OrderBy(k => k).FirstOrDefault();
            if (next == null) return false;
            p.SavedDecks.Remove(name);
            p.DeckArtifacts.Remove(name);
            Activate(p, next);
            return true;
        }
        p.SavedDecks.Remove(name);
        p.DeckArtifacts.Remove(name);
        return true;
    }

    /// <summary>"New Deck", "New Deck 2", … whichever is free.</summary>
    public static string FreeName(ProgressionState p, string stem)
    {
        stem = Clean(stem);
        if (!p.SavedDecks.ContainsKey(stem)) return stem;
        for (int n = 2; n < 1000; n++)
        {
            string c = $"{stem} {n}";
            if (!p.SavedDecks.ContainsKey(c)) return c;
        }
        return stem + " " + Guid.NewGuid().ToString("N")[..4];
    }

    private static string Clean(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length > 40) name = name[..40].Trim();
        return name.Length == 0 ? DefaultName : name;
    }
}

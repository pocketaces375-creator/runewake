using System.Collections.Generic;
using System.IO;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Runewake.Persistence;
using Xunit;

namespace Runewake.Tests.State;

/// <summary>FABLE-066: "I've made new decks or redone mine, every time it resets."</summary>
public class DeckBookTests
{
    // a lookup where every id is a distinct legal card, so 30 distinct ids = a complete deck
    private static CardDef? Look(string id) => new() { Id = id, Name = id, Type = CardType.CREATURE, Rarity = Rarity.COMMON, Cost = 1, Attack = 1, Vigor = 1 };
    private static List<string> Deck(string prefix, int n = 30) => Enumerable.Range(0, n).Select(i => $"{prefix}_{i:00}").ToList();

    [Fact]
    public void Saving_the_played_deck_changes_what_you_play()
    {
        var p = new ProgressionState();
        Assert.True(DeckBook.Save(p, "Drowned Archive", Deck("a"), null, Look));
        Assert.Equal("Drowned Archive", DeckBook.ActiveName(p));

        var edited = Deck("a"); edited[0] = "swap_in";
        Assert.True(DeckBook.Save(p, "Drowned Archive", edited, null, Look));   // the old Save left DeckCardIds behind
        Assert.Equal(edited, p.DeckCardIds);
        Assert.Equal(edited, p.SavedDecks["Drowned Archive"]);
    }

    [Fact]
    public void An_unfinished_edit_is_kept_but_the_last_complete_version_is_still_played()
    {
        var p = new ProgressionState();
        DeckBook.Save(p, "Main", Deck("a"), null, Look);
        var half = Deck("a", 29);
        Assert.False(DeckBook.Save(p, "Main", half, null, Look));
        Assert.Equal(29, p.SavedDecks["Main"].Count);
        Assert.Equal(30, p.DeckCardIds.Count);
        Assert.Equal("Main", DeckBook.ActiveName(p));
    }

    [Fact]
    public void A_second_deck_does_not_take_over_until_you_pick_it()
    {
        var p = new ProgressionState();
        DeckBook.Save(p, "First", Deck("a"), null, Look);
        Assert.False(DeckBook.Save(p, "Second", Deck("b"), new[] { "x", "y" }, Look));
        Assert.Equal("First", DeckBook.ActiveName(p));
        Assert.True(DeckBook.SetActive(p, "Second", Look));
        Assert.Equal("Second", p.ActiveDeckName);
        Assert.Equal(Deck("b"), p.DeckCardIds);
        Assert.Equal(new[] { "x", "y" }, p.ActiveArtifacts);
        Assert.False(DeckBook.SetActive(p, "nope", Look));
    }

    [Fact]
    public void An_old_save_whose_played_deck_matches_no_saved_deck_keeps_it_as_My_Deck()
    {
        // exactly what the old Save button left behind: the deck was edited under its name, the played copy wasn't
        var p = new ProgressionState();
        p.DeckCardIds.AddRange(Deck("old"));
        p.SavedDecks["Drowned Archive"] = Deck("new");
        Assert.True(DeckBook.Normalize(p));
        Assert.Equal("My Deck", p.ActiveDeckName);
        Assert.Equal(Deck("old"), p.SavedDecks["My Deck"]);
        Assert.Equal(Deck("new"), p.SavedDecks["Drowned Archive"]);
        Assert.False(DeckBook.Normalize(p));
    }

    [Fact]
    public void Rename_duplicate_and_delete_keep_a_deck_to_play()
    {
        var p = new ProgressionState();
        DeckBook.Save(p, "A", Deck("a"), null, Look);
        Assert.True(DeckBook.Rename(p, "A", "Alpha"));
        Assert.Equal("Alpha", p.ActiveDeckName);
        string copy = DeckBook.Duplicate(p, "Alpha");
        Assert.Equal("Alpha (copy)", copy);
        Assert.False(DeckBook.Rename(p, "Alpha", copy));                 // taken
        DeckBook.Save(p, "Draft", Deck("d", 10), null, Look);
        Assert.True(DeckBook.Delete(p, "Alpha", Look));                  // the copy is complete: it takes over
        Assert.Equal(copy, p.ActiveDeckName);
        Assert.False(DeckBook.Delete(p, copy, Look));                    // only an unfinished draft left: refused
        Assert.True(DeckBook.Delete(p, "Draft", Look));
        var q = new ProgressionState();
        Assert.Equal("New Deck", DeckBook.FreeName(q, "New Deck"));
        q.SavedDecks["New Deck"] = new List<string>();
        Assert.Equal("New Deck 2", DeckBook.FreeName(q, "New Deck"));
    }

    [Fact]
    public void The_played_deck_name_survives_a_save_and_load()
    {
        string path = Path.Combine(Path.GetTempPath(), $"deckbook_{System.Guid.NewGuid():N}.db");
        try
        {
            var p = new ProgressionState { Version = SaveRepository.CurrentSchemaVersion };
            DeckBook.Save(p, "First", Deck("a"), null, Look);
            DeckBook.Save(p, "Second", Deck("b"), null, Look);
            DeckBook.SetActive(p, "Second", Look);
            var repo = new SaveRepository(path);
            Assert.True(repo.Save(p));
            var back = new SaveRepository(path).Load();
            Assert.Equal("Second", back.ActiveDeckName);
            Assert.Equal(Deck("b"), back.DeckCardIds);
            Assert.Equal(2, back.SavedDecks.Count);
        }
        finally { foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(f); }
    }

    [Fact]
    public void A_save_that_cannot_be_read_is_copied_before_anything_writes_over_it()
    {
        string path = Path.Combine(Path.GetTempPath(), $"unreadable_{System.Guid.NewGuid():N}.db");
        try
        {
            File.WriteAllText(path, "this is not a sqlite database, but it is the player's data");
            var repo = new SaveRepository(path);
            var st = repo.Load();
            Assert.True(repo.LastLoadFailed);
            Assert.Empty(st.ClearedNodes);
            var copies = Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + ".unreadable_*");
            Assert.Single(copies);
            Assert.Contains("player's data", File.ReadAllText(copies[0]));
        }
        finally { foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(f); }
    }
}

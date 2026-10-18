using System.Collections.Generic;
using System.Linq;
using Runewake.Engine.State;
using Runewake.Engine.Supabase;
using Xunit;

namespace Runewake.Tests.State;

/// <summary>FABLE-072: the two artifacts a deck plays never silently fall back to the class defaults.</summary>
public class ArtifactPersistenceTests
{
    private static readonly List<string> Picks = new() { "artf_warrior_twinblade", "artf_warrior_champions_oath" };

    [Fact]
    public void Activating_a_deck_with_no_artifact_record_keeps_the_pair_you_had()
    {
        var p = new ProgressionState();
        p.SavedDecks["Old"] = Enumerable.Range(0, 30).Select(i => $"x{i}").ToList();   // a deck from before artifacts were kept per deck
        p.ActiveArtifacts.AddRange(Picks);
        p.SavedDecks["New"] = Enumerable.Range(0, 30).Select(i => $"y{i}").ToList();
        p.DeckArtifacts["New"] = new List<string>(Picks);
        p.ActiveDeckName = "New";
        Assert.True(DeckBook.SetActive(p, "Old", id => new Runewake.Engine.Cards.CardDef { Id = id, Name = id, Type = Runewake.Engine.Cards.CardType.CREATURE }));
        Assert.Equal(Picks, p.ActiveArtifacts);
        Assert.Equal(Picks, p.DeckArtifacts["Old"]);        // and the deck now remembers them
    }

    [Fact]
    public void A_cloud_copy_without_artifact_picks_does_not_erase_them()
    {
        var local = new ProgressionState();
        local.ActiveArtifacts.AddRange(Picks);
        local.DeckArtifacts["Mine"] = new List<string>(Picks);
        var cloud = ProgressionSnapshot.FromState(new ProgressionState { Shards = 50 });
        cloud.ApplyTo(local);
        Assert.Equal(50, local.Shards);
        Assert.Equal(Picks, local.ActiveArtifacts);
        Assert.Equal(Picks, local.DeckArtifacts["Mine"]);
    }

    [Fact]
    public void A_cloud_copy_with_picks_replaces_them()
    {
        var local = new ProgressionState();
        local.ActiveArtifacts.Add("old");
        var src = new ProgressionState();
        src.ActiveArtifacts.AddRange(Picks);
        src.DeckArtifacts["Mine"] = new List<string>(Picks);
        ProgressionSnapshot.FromState(src).ApplyTo(local);
        Assert.Equal(Picks, local.ActiveArtifacts);
    }
}

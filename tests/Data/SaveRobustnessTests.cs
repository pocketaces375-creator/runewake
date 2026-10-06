using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Runewake.Persistence;
using Xunit;

namespace Runewake.Tests.Data;

/// <summary>
/// FABLE-055: "⚠ Save unavailable — progress won't be saved this session" on Trikzos's phone, and his
/// decks not sticking. A save that failed for ANY reason (one bad row, another save running at the same
/// moment) made the repository DELETE the save file and try again — so one awkward value cost the whole
/// save, and the retry failed the same way, which is what lit the warning. Each test here failed on
/// FABLE-054 main.
/// </summary>
public class SaveRobustnessTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rw_robust_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_path) + "*"))
            try { File.Delete(f); } catch { }
    }

    private static ProgressionState WithDeck()
    {
        var s = new ProgressionState { Shards = 9 };
        s.AddCard("vrd_c_root_warden", 2);
        s.SavedDecks["My Deck"] = new() { "vrd_c_root_warden" };
        s.DeckCardIds.Add("vrd_c_root_warden");
        return s;
    }

    [Fact]
    public void A_relic_listed_twice_does_not_break_saving_or_wipe_the_file()
    {
        var repo = new SaveRepository(_path);
        Assert.True(repo.Save(WithDeck()));

        var s = WithDeck();
        var relic = new LostRelicInstance { RelicInstanceId = "rel-1", CardId = "relic_x" };
        s.DiscoveredRelics.Add(relic);
        s.DiscoveredRelics.Add(relic);          // the same relic twice (a merge or restore gone sideways)
        Assert.True(repo.Save(s));

        var back = new SaveRepository(_path).Load();
        Assert.Equal("My Deck", back.SavedDecks.Keys.Single());
        Assert.Single(back.DiscoveredRelics);
    }

    [Fact]
    public void A_missing_text_value_saves_as_empty_instead_of_failing()
    {
        var repo = new SaveRepository(_path);
        var s = WithDeck();
        s.DiscoveredRelics.Add(new LostRelicInstance { RelicInstanceId = "rel-2", CardId = "relic_y", Site = null!, AcquirerName = null!, EngravingStyle = null! });
        s.SavedRunePageJson = null;
        Assert.True(repo.Save(s));
        Assert.Single(new SaveRepository(_path).Load().DiscoveredRelics);
    }

    [Fact]
    public void A_failed_save_never_deletes_the_existing_save()
    {
        var repo = new SaveRepository(_path) { BusyTimeoutMs = 200 };
        Assert.True(repo.Save(WithDeck()));
        // another connection holds the file locked for longer than the save is willing to wait
        using (var hold = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path};Pooling=False"))
        {
            hold.Open();
            using (var c = hold.CreateCommand()) { c.CommandText = "BEGIN EXCLUSIVE"; c.ExecuteNonQuery(); }
            var s = WithDeck(); s.Shards = 99;
            Assert.False(repo.Save(s));
            Assert.Contains("locked", repo.LastSaveError ?? "", StringComparison.OrdinalIgnoreCase);
            using (var c = hold.CreateCommand()) { c.CommandText = "ROLLBACK"; c.ExecuteNonQuery(); }
        }
        Assert.True(File.Exists(_path), "the save file was deleted by a failed save");
        Assert.Equal("My Deck", new SaveRepository(_path).Load().SavedDecks.Keys.Single());
        Assert.True(repo.Save(WithDeck()));                 // and the next save simply works
        Assert.Null(repo.LastSaveError);
    }

    [Fact]
    public void Saves_from_several_threads_at_once_all_succeed()
    {
        // the cloud sync saves from a background thread while the game saves on the main one
        var results = new bool[24];
        Parallel.For(0, results.Length, i =>
        {
            var s = WithDeck(); s.Shards = i;
            results[i] = new SaveRepository(_path).Save(s);
        });
        Assert.All(results, Assert.True);
        Assert.Equal("My Deck", new SaveRepository(_path).Load().SavedDecks.Keys.Single());
    }
}

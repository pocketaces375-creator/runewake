using System;
using System.IO;
using Runewake.Engine.State;
using Runewake.Persistence;
using Xunit;

namespace Runewake.Tests.Data;

/// <summary>
/// FABLE-069: on the phone SQLite never started (TypeInitializationException), so nothing was ever saved.
/// Every save now also writes a plain JSON file, and the game loads whichever is newer.
/// </summary>
public class SaveFileMirrorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rw_mirror_" + Guid.NewGuid().ToString("N"));
    public SaveFileMirrorTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static ProgressionState Sample()
    {
        var s = new ProgressionState { Shards = 321, DelverLevel = 4 };
        s.MarkNodeCleared("r1_n01"); s.MarkNodeCleared("r1_n02");
        s.AddCard("tid_c_deep_one", 2);
        s.SavedDecks["Tides"] = new() { "tid_c_deep_one", "tid_c_deep_one" };
        s.ActiveDeckName = "Tides";
        return s;
    }

    [Fact]
    public void Every_save_writes_the_file_and_it_alone_brings_the_game_back()
    {
        string db = Path.Combine(_dir, "save.db");
        var repo = new SaveRepository(db);
        Assert.True(repo.Save(Sample()));
        Assert.True(File.Exists(db + ".json"));
        File.Delete(db);                                         // the database is gone (or never worked)
        var back = new SaveRepository(db).Load();
        Assert.Equal(321, back.Shards);
        Assert.True(back.IsNodeCleared("r1_n02"));
        Assert.Equal("Tides", back.ActiveDeckName);
        Assert.Equal(2, back.Collection["tid_c_deep_one"]);
    }

    [Fact]
    public void A_database_that_cannot_open_still_saves_and_loads_through_the_file()
    {
        string db = Path.Combine(_dir, "a_directory_not_a_file");
        Directory.CreateDirectory(db);                           // SQLite cannot open a directory
        var repo = new SaveRepository(db);
        Assert.True(repo.Save(Sample()));
        Assert.Null(repo.LastSaveError);
        var back = new SaveRepository(db).Load();
        Assert.Equal(321, back.Shards);
    }

    [Fact]
    public void The_newer_of_the_two_wins()
    {
        string db = Path.Combine(_dir, "save.db");
        var repo = new SaveRepository(db);
        var s = Sample();
        Assert.True(repo.Save(s));
        string olderFile = File.ReadAllText(db + ".json");
        s.Shards = 999;
        Assert.True(repo.Save(s));
        File.WriteAllText(db + ".json", olderFile);              // the file is now older than the database
        Assert.Equal(999, new SaveRepository(db).Load().Shards);
    }

    [Fact]
    public void A_broken_file_falls_back_to_its_previous_copy()
    {
        string db = Path.Combine(_dir, "a_dir");
        Directory.CreateDirectory(db);
        var repo = new SaveRepository(db);
        var s = Sample();
        Assert.True(repo.Save(s));
        s.Shards = 5;
        Assert.True(repo.Save(s));                               // the first save is now the .bak
        File.WriteAllText(db + ".json", "{ broken");
        Assert.Equal(321, new SaveRepository(db).Load().Shards);
    }

    [Fact]
    public void Settings_survive_without_the_database()
    {
        string db = Path.Combine(_dir, "a_dir2");
        Directory.CreateDirectory(db);
        var repo = new SaveRepository(db);
        repo.SaveSettings(new SettingsState { CardStyle = 3, MusicVolume = 0.25f });
        var back = new SaveRepository(db).LoadSettings();
        Assert.Equal(3, back.CardStyle);
        Assert.Equal(0.25f, back.MusicVolume);
    }
}

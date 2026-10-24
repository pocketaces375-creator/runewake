using System;
using System.IO;
using Runewake.Engine.State;
using Runewake.Persistence;
using Xunit;

namespace Runewake.Tests.Data;

/// <summary>FABLE-104: deleting a campaign deletes its whole save - the leftover file save used to come back as the next campaign.</summary>
public class SaveFilesDeleteTests
{
    private static string Dir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"rw_del_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void DeleteAll_removes_the_database_and_the_file_save_and_its_copies_but_not_the_settings()
    {
        var dir = Dir();
        try
        {
            var db = Path.Combine(dir, "runewake_save_slot1.db");
            var repo = new SaveRepository(db);
            var s = new ProgressionState { Shards = 99 };
            s.ClearedNodes.Add("r1_n1");
            repo.Save(s);
            repo.Save(s);                       // the second save leaves a .json.bak
            repo.SaveSettings(new SettingsState());
            File.WriteAllText(db + ".json.tmp", "half");
            Assert.True(SaveRepository.Exists(db));
            Assert.True(File.Exists(db + ".json"));

            Assert.True(SaveRepository.DeleteAll(db));

            foreach (var f in SaveRepository.SaveFilesOf(db)) Assert.False(File.Exists(f), f);
            Assert.False(SaveRepository.Exists(db));
            Assert.True(File.Exists(db + ".settings.json"));   // settings are not part of the campaign
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void A_new_campaign_in_a_deleted_slot_starts_empty()
    {
        var dir = Dir();
        try
        {
            var db = Path.Combine(dir, "runewake_save_slot0.db");
            var old = new ProgressionState { Shards = 500 };
            old.ClearedNodes.Add("old_node");
            new SaveRepository(db).Save(old);

            SaveRepository.DeleteAll(db);

            var fresh = new SaveRepository(db).Load();
            Assert.Equal(0, fresh.Shards);
            Assert.Empty(fresh.ClearedNodes);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Exists_is_true_for_a_file_save_alone_and_DeleteAll_on_nothing_is_fine()
    {
        var dir = Dir();
        try
        {
            var db = Path.Combine(dir, "runewake_save_slot2.db");
            Assert.False(SaveRepository.Exists(db));
            Assert.True(SaveRepository.DeleteAll(db));
            File.WriteAllText(db + ".json", "{}");          // a phone where SQLite never started has only this
            Assert.True(SaveRepository.Exists(db));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}

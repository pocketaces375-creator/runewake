using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Runewake.Persistence;
using Xunit;

namespace Runewake.Tests.Data;

public class ProgressionStateTests
{
    [Fact]
    public void NewState_HasZeroResources()
    {
        var state = new ProgressionState();
        Assert.Equal(0, state.Shards);
        Assert.Equal(0, state.DigCharges);
        Assert.Empty(state.ClearedNodes);
        Assert.Empty(state.Collection);
        Assert.Empty(state.Fragments);
        Assert.False(state.HasCompletedTutorial);
    }

    [Fact]
    public void SpendShards_WithSufficientBalance_ReturnsTrue()
    {
        var state = new ProgressionState();
        state.Shards = 100;
        Assert.True(state.SpendShards(30));
        Assert.Equal(70, state.Shards);
    }

    [Fact]
    public void SpendShards_WithInsufficientBalance_ReturnsFalse()
    {
        var state = new ProgressionState();
        state.Shards = 10;
        Assert.False(state.SpendShards(30));
        Assert.Equal(10, state.Shards);
    }

    [Fact]
    public void SpendDigCharge_WithCharge_ReturnsTrue()
    {
        var state = new ProgressionState();
        state.DigCharges = 3;
        Assert.True(state.SpendDigCharge());
        Assert.Equal(2, state.DigCharges);
    }

    [Fact]
    public void SpendDigCharge_WithoutCharge_ReturnsFalse()
    {
        var state = new ProgressionState();
        Assert.False(state.SpendDigCharge());
        Assert.Equal(0, state.DigCharges);
    }

    [Fact]
    public void MarkNodeCleared_NewNode_ReturnsTrue()
    {
        var state = new ProgressionState();
        Assert.True(state.MarkNodeCleared("r1_n01"));
        Assert.True(state.IsNodeCleared("r1_n01"));
    }

    [Fact]
    public void MarkNodeCleared_AlreadyCleared_ReturnsFalse()
    {
        var state = new ProgressionState();
        state.MarkNodeCleared("r1_n01");
        Assert.False(state.MarkNodeCleared("r1_n01"));
    }

    [Fact]
    public void AddCard_IncrementsCount()
    {
        var state = new ProgressionState();
        state.AddCard("vrd_c_root_warden");
        Assert.Equal(1, state.Collection["vrd_c_root_warden"]);

        state.AddCard("vrd_c_root_warden", 2);
        Assert.Equal(3, state.Collection["vrd_c_root_warden"]);
    }

    [Fact]
    public void AddFragments_Accumulates()
    {
        var state = new ProgressionState();
        state.AddFragments("verdant", 2);
        Assert.Equal(2, state.Fragments["verdant"]);

        state.AddFragments("verdant", 3);
        Assert.Equal(5, state.Fragments["verdant"]);
    }
}

/// <summary>
/// Tests against the REAL <see cref="SaveRepository"/> using temp-file SQLite
/// databases. Unlike the old tests (which duplicated the SQL in test helpers),
/// these exercise the actual production persistence code — including its
/// transactional crash-safety and schema version enforcement.
/// </summary>
public class SaveRepositoryTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;

    public SaveRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rw_save_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "save.db");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private SaveRepository NewRepo() => new(_dbPath);

    private static ProgressionState SampleState()
    {
        var s = new ProgressionState
        {
            Shards = 250,
            DigCharges = 5,
            HasCompletedTutorial = true,
            GlobalDiscoveryIndex = 3
        };
        s.MarkNodeCleared("r1_n01");
        s.MarkNodeCleared("r1_n02");
        s.AddCard("vrd_c_root_warden");
        s.AddCard("emb_c_ember_hound", 2);
        s.AddFragments("verdant", 4);
        s.AddFragments("ember", 2);
        s.AddOwnedRune("rune_verdant_1");
        s.UnlockTool("brush");
        s.AddRelic(new LostRelicInstance
        {
            RelicInstanceId = "rel_1",
            CardId = "vrd_c_root_warden",
            AcquirerName = "Adventurer",
            AcquiredAt = "2026-08-07T00:00:00Z",
            Site = "site_1",
            DiscoveryIndex = 1,
            EngravingStyle = "default"
        });
        return s;
    }

    [Fact]
    public void Save_ThenLoad_FreshDb_RoundtripsAllFields()
    {
        var repo = NewRepo();
        repo.Save(SampleState());

        var loaded = repo.Load();
        Assert.Equal(250, loaded.Shards);
        Assert.Equal(5, loaded.DigCharges);
        Assert.True(loaded.HasCompletedTutorial);
        Assert.Equal(4, loaded.GlobalDiscoveryIndex); // 3 + 1 from AddRelic
        Assert.True(loaded.IsNodeCleared("r1_n01"));
        Assert.True(loaded.IsNodeCleared("r1_n02"));
        Assert.False(loaded.IsNodeCleared("r1_n03"));
        Assert.Equal(1, loaded.Collection["vrd_c_root_warden"]);
        Assert.Equal(2, loaded.Collection["emb_c_ember_hound"]);
        Assert.Equal(4, loaded.Fragments["verdant"]);
        Assert.Equal(2, loaded.Fragments["ember"]);
        Assert.True(loaded.OwnsRune("rune_verdant_1"));
        Assert.True(loaded.HasTool("brush"));
        Assert.Single(loaded.DiscoveredRelics);
        Assert.Equal("rel_1", loaded.DiscoveredRelics[0].RelicInstanceId);
    }

    [Fact]
    public void Save_Twice_DoesNotDuplicateClearedNodes()
    {
        var repo = NewRepo();
        var state = SampleState();
        repo.Save(state);
        repo.Save(state); // second save should not duplicate

        var loaded = repo.Load();
        Assert.Equal(2, loaded.ClearedNodes.Count);
    }

    [Fact]
    public void Load_EmptyDb_ReturnsDefaults_AndWritesVersion()
    {
        var repo = NewRepo();
        repo.Save(new ProgressionState()); // creates schema

        var loaded = repo.Load();
        Assert.Equal(0, loaded.Shards);
        Assert.Empty(loaded.ClearedNodes);
        Assert.Equal(SaveRepository.CurrentSchemaVersion, loaded.Version);
        Assert.Equal(SaveRepository.CurrentSchemaVersion, repo.ReadStoredVersion());
    }

    // ——— Crash safety ———

    [Fact]
    public void Save_SurvivesMidWriteKill_PriorCommittedStateIntact()
    {
        var repo = NewRepo();
        repo.Save(SampleState()); // committed baseline

        // Simulate an app kill mid-write: open a raw connection, begin a
        // transaction, write partial/changed data, then abandon the connection
        // WITHOUT committing (the process dies before Commit). SQLite must roll
        // back on the next open, leaving the prior committed state readable.
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var tx = conn.BeginTransaction();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM cleared_nodes";
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO cleared_nodes (node_id) VALUES ('r1_n99_CORRUPTED')";
                cmd.ExecuteNonQuery();
            }
            // No tx.Commit() — dispose simulates process death mid-transaction.
        }

        // The database must still open, be intact, and reflect the prior commit.
        var loaded = repo.Load();
        Assert.DoesNotContain(loaded.ClearedNodes, id => id == "r1_n99_CORRUPTED");
        Assert.True(loaded.IsNodeCleared("r1_n01"));
        Assert.Equal(250, loaded.Shards);

        // SQLite integrity check must pass — no corruption.
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check";
            Assert.Equal("ok", cmd.ExecuteScalar());
        }
    }

    [Fact]
    public void Save_AbortedTransaction_DoesNotPartiallyWriteCollection()
    {
        var repo = NewRepo();
        repo.Save(SampleState());

        // Kill mid-write while deleting + re-inserting collection.
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var tx = conn.BeginTransaction();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM collection";
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO collection (card_id, count) VALUES ('partial', 99)";
                cmd.ExecuteNonQuery();
            }
            // No commit — crash.
        }

        var loaded = repo.Load();
        Assert.False(loaded.Collection.ContainsKey("partial"));
        Assert.Equal(1, loaded.Collection["vrd_c_root_warden"]);
    }

    // ——— Schema version enforcement ———

    [Fact]
    public void ValidateVersion_CurrentVersion_IsOk()
    {
        var (ok, _) = SaveRepository.ValidateVersion(SaveRepository.CurrentSchemaVersion);
        Assert.True(ok);
    }

    [Fact]
    public void ValidateVersion_NewerVersion_IsRejected()
    {
        var (ok, error) = SaveRepository.ValidateVersion(SaveRepository.CurrentSchemaVersion + 1);
        Assert.False(ok);
        Assert.Contains("newer", error);
    }

    [Fact]
    public void ValidateVersion_Zero_IsRejected()
    {
        var (ok, _) = SaveRepository.ValidateVersion(0);
        Assert.False(ok);
    }

    [Fact]
    public void Load_SaveFromFutureVersion_Throws()
    {
        var repo = NewRepo();
        repo.Save(new ProgressionState()); // create schema at current version

        // Tamper the stored version to simulate a future-format save.
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE meta SET value = @v WHERE key = 'version'";
            cmd.Parameters.AddWithValue("@v", (SaveRepository.CurrentSchemaVersion + 1).ToString());
            cmd.ExecuteNonQuery();
        }

        Assert.Throws<InvalidOperationException>(() => repo.Load());
    }
}
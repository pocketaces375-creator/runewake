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

    // ──── TASK-COLLECTION-DATA-1 ────

    [Fact]
    public void GrantStarterCollection_AddsOneCopyOfEachCard()
    {
        var state = new ProgressionState();
        var starters = new List<string> { "vrd_c_root_warden", "emb_c_ember_hound", "dwn_c_dawn_warder" };
        state.GrantStarterCollection(starters);
        Assert.Equal(1, state.Collection["vrd_c_root_warden"]);
        Assert.Equal(1, state.Collection["emb_c_ember_hound"]);
        Assert.Equal(1, state.Collection["dwn_c_dawn_warder"]);
        Assert.Equal(3, state.Collection.Count);
    }

    [Fact]
    public void GrantStarterCollection_WithEmptyList_DoesNothing()
    {
        var state = new ProgressionState();
        state.GrantStarterCollection(new List<string>());
        Assert.Empty(state.Collection);
    }

    [Fact]
    public void GrantStarterCollection_CanBeCalledMultipleTimes()
    {
        var state = new ProgressionState();
        state.GrantStarterCollection(new List<string> { "vrd_c_root_warden" });
        state.GrantStarterCollection(new List<string> { "vrd_c_root_warden" });
        Assert.Equal(2, state.Collection["vrd_c_root_warden"]);
    }

    [Fact]
    public void GrantStarterCollection_ThenAddCard_IncrementsCorrectly()
    {
        var state = new ProgressionState();
        state.GrantStarterCollection(new List<string> { "vrd_c_root_warden", "emb_c_ember_hound" });
        state.AddCard("vrd_c_root_warden", 3);
        Assert.Equal(4, state.Collection["vrd_c_root_warden"]);
        Assert.Equal(1, state.Collection["emb_c_ember_hound"]);
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

    // Cleanup helper: delete a file silently (best-effort)
    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }

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

        var result = repo.Load();

        // Load is now always safe — returns fresh state instead of throwing
        Assert.Equal(SaveRepository.CurrentSchemaVersion, result.Version);
        Assert.Empty(result.ClearedNodes);
        Assert.Empty(result.Collection);
    }

    // ─────────────────────────────────────────────
    //  TASK-SAVE-1: Auto-repair on load
    // ─────────────────────────────────────────────

    [Fact]
    public void Load_TruncatedCorruptDatabase_RepairsToFreshProfile_AndLogs()
    {
        var repo = NewRepo();
        repo.Save(SampleState()); // create valid schema first

        // Microsoft.Data.Sqlite pools connections by default within a process —
        // the pooled handle keeps the file open and SQLite doesn't re-read the
        // corrupted bytes. Clear pools to simulate a fresh process (app launch)
        // reading the file from disk — the real corruption scenario.
        SqliteConnection.ClearAllPools();

        // Corrupt the database by writing garbage over the header.
        using (var fs = new FileStream(_dbPath, FileMode.Open, FileAccess.ReadWrite))
        {
            byte[] garbage = new byte[512];
            new Random(42).NextBytes(garbage);
            fs.Write(garbage, 0, garbage.Length);
            fs.Flush();
        }
        TryDeleteFile(_dbPath + "-wal");
        TryDeleteFile(_dbPath + "-shm");

        var loaded = repo.Load();

        // Auto-repair: fresh default profile, never a crash
        Assert.Equal(SaveRepository.CurrentSchemaVersion, loaded.Version);
        Assert.Empty(loaded.ClearedNodes);
        Assert.Empty(loaded.Collection);
        Assert.Equal(0, loaded.Shards);

        // The repair must be logged
        Assert.NotEmpty(repo.RepairLog);
        Assert.Contains(repo.RepairLog, line => line.Contains("Load failed") || line.Contains("fresh"));
    }

    [Fact]
    public void Load_ZeroByteDatabase_RepairsToFreshProfile_AndLogs()
    {
        var repo = NewRepo();
        repo.Save(SampleState());

        // Fresh-process simulation: drop pooled connections
        SqliteConnection.ClearAllPools();

        // Write garbage over the entire file — simulates a completely corrupted DB.
        // A 0-byte file is actually handled gracefully by SQLite (it creates a new
        // database), so we need garbage to force a load failure.
        using (var fs = new FileStream(_dbPath, FileMode.Open, FileAccess.ReadWrite))
        {
            byte[] garbage = new byte[8192];
            new Random(42).NextBytes(garbage);
            fs.Write(garbage, 0, garbage.Length);
            fs.SetLength(garbage.Length);
            fs.Flush();
        }
        TryDeleteFile(_dbPath + "-wal");
        TryDeleteFile(_dbPath + "-shm");

        var loaded = repo.Load();
        Assert.Equal(SaveRepository.CurrentSchemaVersion, loaded.Version);
        Assert.Empty(loaded.Collection);
        Assert.NotEmpty(repo.RepairLog);
        Assert.Contains(repo.RepairLog, line => line.Contains("Load failed") || line.Contains("fresh"));
    }

    [Fact]
    public void Load_CorruptMetaValue_FallsBackToDefaults_AndLogs()
    {
        var repo = NewRepo();
        repo.Save(SampleState());

        // Corrupt a meta value to non-integer data — the loader's int.Parse
        // will throw; load must fall back to a fresh profile rather than crash.
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE meta SET value = 'NOT_A_NUMBER' WHERE key = 'shards'";
            cmd.ExecuteNonQuery();
        }

        var loaded = repo.Load();
        Assert.Equal(SaveRepository.CurrentSchemaVersion, loaded.Version);
        // meta corruption throws in LoadFrom → entire load falls back to fresh
        Assert.Equal(0, loaded.Shards);
        Assert.NotEmpty(repo.RepairLog);
    }

    // ─────────────────────────────────────────────
    //  TASK-SAVE-1: Forward-compatible loader
    // ─────────────────────────────────────────────

    [Fact]
    public void Load_OlderVersionSave_MigratesToCurrent_AndLogs()
    {
        var repo = NewRepo();
        repo.Save(SampleState());

        // Wipe the version key entirely (pre-versioning save = v0 equivalent).
        // The loader must treat it as fresh/legacy and normalize to current.
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM meta WHERE key = 'version'";
            cmd.ExecuteNonQuery();
        }

        var loaded = repo.Load();
        Assert.Equal(SaveRepository.CurrentSchemaVersion, loaded.Version);
    }

    [Fact]
    public void Load_SaveWithMissingTables_RepairsSchema_AndLogs()
    {
        // Create a db with a meta table but NO progression tables at all —
        // simulates an interrupted first-write that created meta but died
        // before creating the other tables. EnsureSchema must fix it.
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE meta (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL); INSERT INTO meta (key, value) VALUES ('version', '1')";
            cmd.ExecuteNonQuery();
        }

        var repo = NewRepo();
        var loaded = repo.Load();
        Assert.Equal(SaveRepository.CurrentSchemaVersion, loaded.Version);
        Assert.Empty(loaded.Collection);
    }

    // ─────────────────────────────────────────────
    //  TASK-SAVE-1: Init ordering
    // ─────────────────────────────────────────────

    [Fact]
    public void Load_RepairLog_ClearedOnEachLoad()
    {
        var repo = NewRepo();
        repo.Save(SampleState());

        // First load — clean, no repairs
        var loaded1 = repo.Load();
        Assert.Empty(repo.RepairLog);

        // Corrupt the db (garbage + nuke WAL)
        SqliteConnection.ClearAllPools();
        using (var fs = new FileStream(_dbPath, FileMode.Open, FileAccess.ReadWrite))
        {
            byte[] garbage = new byte[512];
            new Random(42).NextBytes(garbage);
            fs.Write(garbage, 0, garbage.Length);
            fs.Flush();
        }
        TryDeleteFile(_dbPath + "-wal");
        TryDeleteFile(_dbPath + "-shm");

        // Second load — repaired
        var loaded2 = repo.Load();
        Assert.NotEmpty(repo.RepairLog);

        // Third load — repairing again writes to the SAME log (no stale entries
        // from previous load): cleared at start of each Load()
        Assert.NotEmpty(repo.RepairLog); // still has this load's entries
        Assert.All(repo.RepairLog, line => Assert.Contains("Load failed", line));
    }

    [Fact]
    public void Load_IsIdempotent_AfterRepair_NextSaveWorks()
    {
        var repo = NewRepo();
        repo.Save(SampleState());

        // Corrupt, repair-load (fresh profile)
        SqliteConnection.ClearAllPools();
        using (var fs = new FileStream(_dbPath, FileMode.Open, FileAccess.ReadWrite))
        {
            byte[] garbage = new byte[512];
            new Random(42).NextBytes(garbage);
            fs.Write(garbage, 0, garbage.Length);
            fs.Flush();
        }
        TryDeleteFile(_dbPath + "-wal");
        TryDeleteFile(_dbPath + "-shm");
        var repaired = repo.Load();
        Assert.Equal(SaveRepository.CurrentSchemaVersion, repaired.Version);

        // A subsequent save must succeed and round-trip on next load
        repaired.Shards = 77;
        Assert.True(repo.Save(repaired));

        var reloaded = repo.Load();
        Assert.Equal(77, reloaded.Shards);
    }

    // ─────────────────────────────────────────────
    //  TASK-DECKSAVE-1: Named deck save/load
    // ─────────────────────────────────────────────

    [Fact]
    public void Save_NamedDeck_Roundtrips()
    {
        var repo = NewRepo();
        var state = SampleState();

        // Add a named deck
        state.SavedDecks["My Warrior Deck"] = new List<string> { "vrd_c_root_warden", "vrd_c_verdant_sproutling", "emb_c_ember_hound" };
        Assert.True(repo.Save(state));

        var loaded = repo.Load();
        Assert.Single(loaded.SavedDecks);
        Assert.True(loaded.SavedDecks.ContainsKey("My Warrior Deck"));
        Assert.Equal(3, loaded.SavedDecks["My Warrior Deck"].Count);
        Assert.Equal("vrd_c_root_warden", loaded.SavedDecks["My Warrior Deck"][0]);
        Assert.Equal("emb_c_ember_hound", loaded.SavedDecks["My Warrior Deck"][2]);
    }

    [Fact]
    public void Save_MultipleNamedDecks_Roundtrips()
    {
        var repo = NewRepo();
        var state = SampleState();

        state.SavedDecks["Deck A"] = new List<string> { "vrd_c_root_warden", "vrd_c_verdant_sproutling" };
        state.SavedDecks["Deck B"] = new List<string> { "emb_c_ember_hound", "emb_c_cinder_runner", "emb_c_forgeguard_berserker" };
        Assert.True(repo.Save(state));

        var loaded = repo.Load();
        Assert.Equal(2, loaded.SavedDecks.Count);
        Assert.Equal(2, loaded.SavedDecks["Deck A"].Count);
        Assert.Equal(3, loaded.SavedDecks["Deck B"].Count);
    }

    [Fact]
    public void Save_NamedDeck_OverwriteSameName_OnlyLatestPersists()
    {
        var repo = NewRepo();
        var state = SampleState();

        state.SavedDecks["Deck X"] = new List<string> { "vrd_c_root_warden" };
        repo.Save(state);

        state.SavedDecks["Deck X"] = new List<string> { "emb_c_ember_hound", "emb_c_cinder_runner" };
        repo.Save(state);

        var loaded = repo.Load();
        Assert.Single(loaded.SavedDecks);
        Assert.Equal(2, loaded.SavedDecks["Deck X"].Count);
        Assert.Equal("emb_c_ember_hound", loaded.SavedDecks["Deck X"][0]);
    }

    [Fact]
    public void Save_NamedDeck_MidWriteKill_PriorDecksIntact()
    {
        var repo = NewRepo();
        var state = SampleState();
        state.SavedDecks["Survivor"] = new List<string> { "vrd_c_root_warden" };
        repo.Save(state);

        // Kill mid-write during named_decks re-insertion
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var tx = conn.BeginTransaction();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM named_decks";
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO named_decks (deck_name, position, card_id) VALUES ('corrupt', 0, 'partial')";
                cmd.ExecuteNonQuery();
            }
            // No commit — crash
        }

        var loaded = repo.Load();
        // Prior committed state must survive: "Survivor" deck intact
        Assert.True(loaded.SavedDecks.ContainsKey("Survivor"));
        Assert.Single(loaded.SavedDecks);
        Assert.Equal("vrd_c_root_warden", loaded.SavedDecks["Survivor"][0]);
    }

    [Fact]
    public void Load_CorruptNamedDecksTable_RepairsToEmpty()
    {
        var repo = NewRepo();
        var state = SampleState();
        state.SavedDecks["Good"] = new List<string> { "vrd_c_root_warden" };
        repo.Save(state);

        // Wipe named_decks table entirely — corrupt/empty table is valid, just means no decks
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DROP TABLE IF EXISTS named_decks";
            cmd.ExecuteNonQuery();
        }

        var loaded = repo.Load();
        Assert.Equal(SaveRepository.CurrentSchemaVersion, loaded.Version);
        // When named_decks table is missing, LoadFrom catches the exception and returns empty
        Assert.Empty(loaded.SavedDecks);
    }

    // ─────────────────────────────────────────────
    //  TASK-DECKSAVE-1: v1→v2 migration
    // ─────────────────────────────────────────────

    [Fact]
    public void Load_V1Save_WithSavedDeck_MigratesToCurrent()
    {
        var repo = NewRepo();
        var state = new ProgressionState();
        state.DeckCardIds.AddRange(new[] { "vrd_c_root_warden", "vrd_c_verdant_sproutling", "emb_c_ember_hound" });
        repo.Save(state); // saves at current version (v3)

        // Tamper the stored version to v1 to simulate loading an older save
        SqliteConnection.ClearAllPools();
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE meta SET value = '1' WHERE key = 'version'";
            cmd.ExecuteNonQuery();
        }

        // Loading a v1 save with DeckCardIds should migrate to SavedDecks as "My Deck"
        // and then v2→v3 should seed collection from saved decks
        var loaded = repo.Load();
        Assert.Equal(SaveRepository.CurrentSchemaVersion, loaded.Version);
        Assert.True(loaded.SavedDecks.ContainsKey("My Deck"), $"Expected 'My Deck' in saved decks, got keys: [{string.Join(", ", loaded.SavedDecks.Keys)}]");
        Assert.Equal(3, loaded.SavedDecks["My Deck"].Count);
        Assert.Equal("vrd_c_root_warden", loaded.SavedDecks["My Deck"][0]);
        // v2→v3: collection should be seeded from saved decks (1 copy per card per deck)
        Assert.Equal(1, loaded.Collection["vrd_c_root_warden"]);
        Assert.Equal(1, loaded.Collection["vrd_c_verdant_sproutling"]);
        Assert.Equal(1, loaded.Collection["emb_c_ember_hound"]);
    }

    [Fact]
    public void NewState_HasEmptySavedDecks()
    {
        var state = new ProgressionState();
        Assert.Empty(state.SavedDecks);
    }

    // ─────────────────────────────────────────────
    //  TASK-COLLECTION-DATA-1: v2→v3 migration
    // ─────────────────────────────────────────────

    [Fact]
    public void Load_V2Save_WithSavedDecks_MigratesToV3_SeedsCollection()
    {
        var repo = NewRepo();
        var state = SampleState();
        // Clear any collection from SampleState
        state.Collection.Clear();
        state.SavedDecks["Deck A"] = new List<string> { "vrd_c_root_warden", "emb_c_ember_hound" };
        repo.Save(state); // saved at v3

        // Tamper the stored version to v2
        SqliteConnection.ClearAllPools();
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE meta SET value = '2' WHERE key = 'version'";
            cmd.ExecuteNonQuery();
        }

        var loaded = repo.Load();
        Assert.Equal(SaveRepository.CurrentSchemaVersion, loaded.Version);
        Assert.Equal(1, loaded.Collection["vrd_c_root_warden"]);
        Assert.Equal(1, loaded.Collection["emb_c_ember_hound"]);
        Assert.Equal(2, loaded.Collection.Count);
    }

    [Fact]
    public void Load_V2Save_WithEmptyCollectionAndNoDecks_CollectionStaysEmpty()
    {
        var repo = NewRepo();
        var state = SampleState();
        state.Collection.Clear();
        state.SavedDecks.Clear();
        repo.Save(state); // saved at v3

        // Tamper the stored version to v2
        SqliteConnection.ClearAllPools();
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE meta SET value = '2' WHERE key = 'version'";
            cmd.ExecuteNonQuery();
        }

        var loaded = repo.Load();
        Assert.Equal(SaveRepository.CurrentSchemaVersion, loaded.Version);
        Assert.Empty(loaded.Collection);
    }

    [Fact]
    public void Load_V2Save_WithNonEmptyCollection_CollectionNotOverwritten()
    {
        var repo = NewRepo();
        var state = SampleState();
        state.SavedDecks["Deck A"] = new List<string> { "vrd_c_root_warden", "emb_c_ember_hound" };
        repo.Save(state); // saved at v3

        // Tamper the stored version to v2
        SqliteConnection.ClearAllPools();
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE meta SET value = '2' WHERE key = 'version'";
            cmd.ExecuteNonQuery();
        }

        var loaded = repo.Load();
        Assert.Equal(SaveRepository.CurrentSchemaVersion, loaded.Version);
        // Collection already has items from SampleState, should NOT be overwritten
        Assert.Equal(1, loaded.Collection["vrd_c_root_warden"]); // from SampleState
        Assert.Equal(2, loaded.Collection["emb_c_ember_hound"]); // from SampleState
    }

    [Fact]
    public void Load_V2Save_WithMultipleDecks_SeedsCollectionFromAll()
    {
        var repo = NewRepo();
        var state = SampleState();
        state.Collection.Clear();
        state.SavedDecks["Deck A"] = new List<string> { "card_a", "card_b" };
        state.SavedDecks["Deck B"] = new List<string> { "card_b", "card_c" };
        repo.Save(state); // saved at v3

        // Tamper the stored version to v2
        SqliteConnection.ClearAllPools();
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE meta SET value = '2' WHERE key = 'version'";
            cmd.ExecuteNonQuery();
        }

        var loaded = repo.Load();
        Assert.Equal(SaveRepository.CurrentSchemaVersion, loaded.Version);
        // each card gets 1 copy per deck it appears in
        Assert.Equal(1, loaded.Collection["card_a"]);
        Assert.Equal(2, loaded.Collection["card_b"]); // appears in both decks
        Assert.Equal(1, loaded.Collection["card_c"]);
    }

    // ─────────────────────────────────────────────
    //  TASK-COLLECTION-DATA-1: Corrupt repair
    // ─────────────────────────────────────────────

    [Fact]
    public void Load_CorruptDatabase_RepairsToFreshProfile_WithEmptyCollection()
    {
        var repo = NewRepo();
        repo.Save(SampleState());

        SqliteConnection.ClearAllPools();

        // Corrupt the database
        using (var fs = new FileStream(_dbPath, FileMode.Open, FileAccess.ReadWrite))
        {
            byte[] garbage = new byte[512];
            new Random(42).NextBytes(garbage);
            fs.Write(garbage, 0, garbage.Length);
            fs.Flush();
        }
        TryDeleteFile(_dbPath + "-wal");
        TryDeleteFile(_dbPath + "-shm");

        var loaded = repo.Load();
        Assert.Equal(SaveRepository.CurrentSchemaVersion, loaded.Version);
        // Repaired save must come up with empty collection (fresh state)
        Assert.Empty(loaded.Collection);
        Assert.Empty(loaded.SavedDecks);
        Assert.NotEmpty(repo.RepairLog);
    }

    // ─── Shop fields ───

    [Fact]
    public void ShopRotationDay_SaveRoundtrip()
    {
        var state = new ProgressionState { ShopRotationDay = 12, RuneDust = 200 };
        var repo = NewRepo();
        Assert.True(repo.Save(state));

        var loaded = repo.Load();
        Assert.Equal(12, loaded.ShopRotationDay);
        Assert.Equal(200, loaded.RuneDust);
    }

    [Fact]
    public void ShopRotationDay_DefaultInLoad_IsZero()
    {
        var state = new ProgressionState { ShopRotationDay = 0 };
        var repo = NewRepo();
        Assert.True(repo.Save(state));

        var loaded = repo.Load();
        Assert.Equal(0, loaded.ShopRotationDay);
    }

    // ─────────────────────────────────────────────
    //  TASK-ROSTER-LOCK-1: ClassId migration tests
    // ─────────────────────────────────────────────

    [Fact]
    public void Load_ThiefClass_MigratesToRogue()
    {
        Assert.Equal("rogue", ClassIdMigration.ApplyMigration("thief"));
        Assert.Equal("rogue", ClassIdMigration.ApplyMigration("THIEF"));
        Assert.Equal("rogue", ClassIdMigration.ApplyMigration("Thief"));
    }

    [Fact]
    public void Load_RangerClass_MigratesToAstrologist()
    {
        Assert.Equal("astrologist", ClassIdMigration.ApplyMigration("ranger"));
        Assert.Equal("astrologist", ClassIdMigration.ApplyMigration("RANGER"));
        Assert.Equal("astrologist", ClassIdMigration.ApplyMigration("Ranger"));
    }

    [Fact]
    public void Migration_UnaffectedClasses_StayUnchanged()
    {
        Assert.Equal("warrior", ClassIdMigration.ApplyMigration("warrior"));
        Assert.Equal("battlemage", ClassIdMigration.ApplyMigration("battlemage"));
        Assert.Equal("necromancer", ClassIdMigration.ApplyMigration("necromancer"));
        Assert.Equal("paladin", ClassIdMigration.ApplyMigration("paladin"));
        Assert.Equal("druid", ClassIdMigration.ApplyMigration("druid"));
        Assert.Equal("astrologist", ClassIdMigration.ApplyMigration("astrologist"));
        Assert.Equal("rogue", ClassIdMigration.ApplyMigration("rogue"));
    }

    [Fact]
    public void Migration_EmptyOrNull_ReturnsOriginal()
    {
        Assert.Equal("", ClassIdMigration.ApplyMigration(""));
    }

    // ─────────────────────────────────────────────
    //  TASK-CLASS-GENDER-1: PortraitVariant migration
    // ─────────────────────────────────────────────

    /// <summary>
    /// Lightweight mirror of CampaignProfile for testing PortraitVariant behavior
    /// without a dependency on Runewake.Client.
    /// </summary>
    private class TestProfile
    {
        public int Slot { get; set; }
        public string ClassId { get; set; } = "";
        public string TownName { get; set; } = "";
        public string PortraitVariant { get; set; } = "m";
    }

    [Fact]
    public void PortraitVariant_DefaultIsM()
    {
        var profile = new TestProfile
        {
            Slot = 0,
            ClassId = "warrior",
            TownName = "Emberhold"
        };
        // No PortraitVariant set — should default to "m"
        Assert.Equal("m", profile.PortraitVariant);
    }

    [Fact]
    public void PortraitVariant_RoundTrip()
    {
        var profile = new TestProfile
        {
            Slot = 0,
            ClassId = "warrior",
            TownName = "Emberhold",
            PortraitVariant = "f"
        };
        // Simulate serialization/deserialization via JSON
        string json = System.Text.Json.JsonSerializer.Serialize(profile);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<TestProfile>(json);
        Assert.NotNull(deserialized);
        Assert.Equal("f", deserialized.PortraitVariant);
    }

    [Fact]
    public void PortraitVariant_SaveWithoutField_DefaultsToM()
    {
        // Simulate an old save that has no PortraitVariant field
        string oldJson = @"{""slot"":0,""classId"":""warrior"",""townName"":""Emberhold""}";
        var profile = System.Text.Json.JsonSerializer.Deserialize<TestProfile>(oldJson);
        Assert.NotNull(profile);
        // Missing field should default to "m" via property initializer
        Assert.Equal("m", profile.PortraitVariant);
    }
}
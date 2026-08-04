using System.IO;
using Microsoft.Data.Sqlite;
using Runewake.Engine.State;
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

public class SaveManagerTests
{
    private static SqliteConnection CreateInMemoryConnection()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        // Create tables manually
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS meta (
                key TEXT PRIMARY KEY NOT NULL,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS cleared_nodes (
                node_id TEXT PRIMARY KEY NOT NULL
            );
            CREATE TABLE IF NOT EXISTS collection (
                card_id TEXT PRIMARY KEY NOT NULL,
                count INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE IF NOT EXISTS fragments (
                strata TEXT PRIMARY KEY NOT NULL,
                count INTEGER NOT NULL DEFAULT 0
            );
        """;
        cmd.ExecuteNonQuery();

        return conn;
    }

    [Fact]
    public void SaveAndLoad_EmptyState_PreservesDefaults()
    {
        using var conn = CreateInMemoryConnection();

        var state = new ProgressionState();
        SaveViaConnection(conn, state);

        var loaded = LoadViaConnection(conn);
        Assert.Equal(0, loaded.Shards);
        Assert.Equal(0, loaded.DigCharges);
        Assert.Empty(loaded.ClearedNodes);
        Assert.Empty(loaded.Collection);
        Assert.Empty(loaded.Fragments);
        Assert.False(loaded.HasCompletedTutorial);
    }

    [Fact]
    public void SaveAndLoad_WithData_Roundtrips()
    {
        using var conn = CreateInMemoryConnection();

        var state = new ProgressionState
        {
            Shards = 250,
            DigCharges = 5,
            HasCompletedTutorial = true
        };
        state.MarkNodeCleared("r1_n01");
        state.MarkNodeCleared("r1_n02");
        state.AddCard("vrd_c_root_warden");
        state.AddCard("emb_c_ember_hound", 2);
        state.AddFragments("verdant", 4);
        state.AddFragments("ember", 2);

        SaveViaConnection(conn, state);

        var loaded = LoadViaConnection(conn);
        Assert.Equal(250, loaded.Shards);
        Assert.Equal(5, loaded.DigCharges);
        Assert.True(loaded.HasCompletedTutorial);
        Assert.True(loaded.IsNodeCleared("r1_n01"));
        Assert.True(loaded.IsNodeCleared("r1_n02"));
        Assert.False(loaded.IsNodeCleared("r1_n03"));
        Assert.Equal(1, loaded.Collection["vrd_c_root_warden"]);
        Assert.Equal(2, loaded.Collection["emb_c_ember_hound"]);
        Assert.Equal(4, loaded.Fragments["verdant"]);
        Assert.Equal(2, loaded.Fragments["ember"]);
    }

    [Fact]
    public void SaveAndLoad_WithNodeClears_DoesNotDuplicate()
    {
        using var conn = CreateInMemoryConnection();

        var state = new ProgressionState();
        state.MarkNodeCleared("r1_n01");
        SaveViaConnection(conn, state);

        state.MarkNodeCleared("r1_n01"); // no-op
        SaveViaConnection(conn, state);

        var loaded = LoadViaConnection(conn);
        Assert.Single(loaded.ClearedNodes);
    }

    /// <summary>Simulates SaveManager.Save() logic against a given connection.</summary>
    private static void SaveViaConnection(SqliteConnection conn, ProgressionState state)
    {
        using var tx = conn.BeginTransaction();

        // Clear
        using (var cmd = conn.CreateCommand()) { cmd.CommandText = "DELETE FROM meta"; cmd.ExecuteNonQuery(); }
        using (var cmd = conn.CreateCommand()) { cmd.CommandText = "DELETE FROM cleared_nodes"; cmd.ExecuteNonQuery(); }
        using (var cmd = conn.CreateCommand()) { cmd.CommandText = "DELETE FROM collection"; cmd.ExecuteNonQuery(); }
        using (var cmd = conn.CreateCommand()) { cmd.CommandText = "DELETE FROM fragments"; cmd.ExecuteNonQuery(); }

        // Insert meta
        InsertMeta(conn, "version", state.Version.ToString());
        InsertMeta(conn, "shards", state.Shards.ToString());
        InsertMeta(conn, "dig_charges", state.DigCharges.ToString());
        InsertMeta(conn, "tutorial_done", state.HasCompletedTutorial ? "1" : "0");

        // Nodes
        foreach (var id in state.ClearedNodes)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO cleared_nodes (node_id) VALUES (@id)";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // Collection
        foreach (var (cardId, count) in state.Collection)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO collection (card_id, count) VALUES (@id, @c)";
            cmd.Parameters.AddWithValue("@id", cardId);
            cmd.Parameters.AddWithValue("@c", count);
            cmd.ExecuteNonQuery();
        }

        // Fragments
        foreach (var (strata, count) in state.Fragments)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO fragments (strata, count) VALUES (@s, @c)";
            cmd.Parameters.AddWithValue("@s", strata);
            cmd.Parameters.AddWithValue("@c", count);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    private static void InsertMeta(SqliteConnection conn, string key, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO meta (key, value) VALUES (@k, @v)";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Simulates SaveManager.Load() logic against a given connection.</summary>
    private static ProgressionState LoadViaConnection(SqliteConnection conn)
    {
        var state = new ProgressionState();

        // Meta
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT key, value FROM meta";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                switch (reader.GetString(0))
                {
                    case "version": state.Version = int.Parse(reader.GetString(1)); break;
                    case "shards": state.Shards = int.Parse(reader.GetString(1)); break;
                    case "dig_charges": state.DigCharges = int.Parse(reader.GetString(1)); break;
                    case "tutorial_done": state.HasCompletedTutorial = reader.GetString(1) == "1"; break;
                }
            }
        }

        // Nodes
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT node_id FROM cleared_nodes";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) state.ClearedNodes.Add(reader.GetString(0));
        }

        // Collection
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT card_id, count FROM collection";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) state.Collection[reader.GetString(0)] = reader.GetInt32(1);
        }

        // Fragments
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT strata, count FROM fragments";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) state.Fragments[reader.GetString(0)] = reader.GetInt32(1);
        }

        return state;
    }
}
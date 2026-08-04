using System;
using System.Collections.Generic;
using Godot;
using Microsoft.Data.Sqlite;

using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// SQLite-backed persistence for player progression.
/// Creates the database on first run, loads on init, saves on demand.
/// The DB file lives in <see cref="ProjectSettings.GlobalizePath"/> under user data.
/// </summary>
public class SaveManager
{
    private readonly string _dbPath;
    private SqliteConnection? _connection;

    /// <summary>Current in-memory progression state.</summary>
    public ProgressionState State { get; } = new();

    /// <summary>True after <see cref="Load"/> completes successfully.</summary>
    public bool IsLoaded { get; private set; }

    public SaveManager()
    {
        string dataDir = ProjectSettings.GlobalizePath("user://");
        _dbPath = System.IO.Path.Combine(dataDir, "runewake_save.db");
    }

    /// <summary>
    /// Initialize the database (create tables if missing) and load existing save data.
    /// </summary>
    public void Initialize()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();

        CreateTables();
        Load();
        IsLoaded = true;
    }

    private void CreateTables()
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;

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
    }

    /// <summary>
    /// Load save data from SQLite into <see cref="State"/>.
    /// </summary>
    public void Load()
    {
        if (_connection == null) return;

        // Meta
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT key, value FROM meta";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string key = reader.GetString(0);
                string value = reader.GetString(1);
                switch (key)
                {
                    case "version":
                        State.Version = int.Parse(value);
                        break;
                    case "shards":
                        State.Shards = int.Parse(value);
                        break;
                    case "dig_charges":
                        State.DigCharges = int.Parse(value);
                        break;
                    case "tutorial_done":
                        State.HasCompletedTutorial = value == "1";
                        break;
                }
            }
        }

        // Cleared nodes
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT node_id FROM cleared_nodes";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                State.ClearedNodes.Add(reader.GetString(0));
        }

        // Collection
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT card_id, count FROM collection";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                State.Collection[reader.GetString(0)] = reader.GetInt32(1);
        }

        // Fragments
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT strata, count FROM fragments";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                State.Fragments[reader.GetString(0)] = reader.GetInt32(1);
        }
    }

    /// <summary>
    /// Persist the current <see cref="State"/> to SQLite.
    /// Uses a transaction for atomicity.
    /// </summary>
    public void Save()
    {
        if (_connection == null) return;

        using var tx = _connection.BeginTransaction();

        try
        {
            // Clear and re-insert meta
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM meta";
                cmd.ExecuteNonQuery();
            }

            InsertMeta("version", State.Version.ToString());
            InsertMeta("shards", State.Shards.ToString());
            InsertMeta("dig_charges", State.DigCharges.ToString());
            InsertMeta("tutorial_done", State.HasCompletedTutorial ? "1" : "0");

            // Cleared nodes: clear + re-insert
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM cleared_nodes";
                cmd.ExecuteNonQuery();
            }
            foreach (var nodeId in State.ClearedNodes)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "INSERT INTO cleared_nodes (node_id) VALUES (@id)";
                cmd.Parameters.AddWithValue("@id", nodeId);
                cmd.ExecuteNonQuery();
            }

            // Collection: clear + re-insert
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM collection";
                cmd.ExecuteNonQuery();
            }
            foreach (var (cardId, count) in State.Collection)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "INSERT INTO collection (card_id, count) VALUES (@id, @c)";
                cmd.Parameters.AddWithValue("@id", cardId);
                cmd.Parameters.AddWithValue("@c", count);
                cmd.ExecuteNonQuery();
            }

            // Fragments: clear + re-insert
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM fragments";
                cmd.ExecuteNonQuery();
            }
            foreach (var (strata, count) in State.Fragments)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "INSERT INTO fragments (strata, count) VALUES (@s, @c)";
                cmd.Parameters.AddWithValue("@s", strata);
                cmd.Parameters.AddWithValue("@c", count);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private void InsertMeta(string key, string value)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "INSERT INTO meta (key, value) VALUES (@k, @v)";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Close the database connection. Call when the game exits.
    /// </summary>
    public void Close()
    {
        _connection?.Close();
        _connection?.Dispose();
        _connection = null;
    }
}
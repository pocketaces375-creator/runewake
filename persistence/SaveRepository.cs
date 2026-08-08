using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Persistence;

/// <summary>
/// SQLite-backed persistence for player progression.
///
/// This is the single source of authority for the save schema and its version.
/// The engine owns what progression *means* (<see cref="ProgressionState"/>);
/// this repository owns how it is *stored*. It is pure .NET (no Godot
/// dependency) so it can be unit-tested directly — including crash-safety under
/// a mid-write kill.
/// </summary>
public sealed class SaveRepository
{
    /// <summary>Current save schema version. Bump when the schema changes; add a migration step.</summary>
    public const int CurrentSchemaVersion = 1;

    private readonly string _dbPath;

    /// <summary>Create a repository rooted at the given SQLite file path.</summary>
    public SaveRepository(string dbPath)
    {
        _dbPath = dbPath;
    }

    /// <summary>
    /// Validate that a stored schema version can be loaded by this build.
    /// A save from a *newer* build is refused (loading it mid-migration would
    /// corrupt it); a save from an *older* build is migrated forward.
    /// </summary>
    public static (bool ok, string? error) ValidateVersion(int stored)
    {
        if (stored > CurrentSchemaVersion)
            return (false, $"Save format v{stored} is newer than this build supports (v{CurrentSchemaVersion}). Refusing to load — upgrade the game first.");
        if (stored < 1)
            return (false, $"Save format v{stored} is corrupt (minimum v1).");
        return (true, null);
    }

    /// <summary>
    /// Load progression state from the database file. Creates the file and
    /// tables if they do not exist, applying version migration as needed.
    /// </summary>
    public ProgressionState Load()
    {
        using var conn = OpenConnection();
        EnsureSchema(conn);
        return LoadFrom(conn);
    }

    /// <summary>
    /// Persist the given progression state atomically. All writes happen inside
    /// a single transaction so a process kill mid-write leaves the previous
    /// committed state intact (SQLite rolls back the uncommitted transaction).
    /// </summary>
    public void Save(ProgressionState state)
    {
        using var conn = OpenConnection();
        EnsureSchema(conn);
        SaveTo(conn, state);
    }

    /// <summary>Read the current schema version stored in the database (0 if none).</summary>
    public int ReadStoredVersion()
    {
        using var conn = OpenConnection();
        EnsureSchema(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = 'version'";
        var result = cmd.ExecuteScalar();
        return result == null ? 0 : int.Parse((string)result);
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }
        return conn;
    }

    private static void EnsureSchema(SqliteConnection conn)
    {
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

            CREATE TABLE IF NOT EXISTS owned_runes (
                rune_id TEXT PRIMARY KEY NOT NULL
            );

            CREATE TABLE IF NOT EXISTS unlocked_tools (
                tool_id TEXT PRIMARY KEY NOT NULL
            );

            CREATE TABLE IF NOT EXISTS discovered_relics (
                relic_instance_id TEXT PRIMARY KEY NOT NULL,
                card_id TEXT NOT NULL,
                acquirer_name TEXT NOT NULL,
                acquired_at TEXT NOT NULL,
                site TEXT NOT NULL,
                discovery_index INTEGER NOT NULL,
                engraving_style TEXT NOT NULL DEFAULT 'default'
            );

            CREATE TABLE IF NOT EXISTS saved_deck (
                position INTEGER NOT NULL,
                card_id TEXT NOT NULL,
                PRIMARY KEY (position)
            );
        """;
        cmd.ExecuteNonQuery();
    }

    private static ProgressionState LoadFrom(SqliteConnection conn)
    {
        var state = new ProgressionState();

        // Meta
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT key, value FROM meta";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string key = reader.GetString(0);
                string value = reader.GetString(1);
                switch (key)
                {
                    case "version": state.Version = int.Parse(value); break;
                    case "shards": state.Shards = int.Parse(value); break;
                    case "dig_charges": state.DigCharges = int.Parse(value); break;
                    case "tutorial_done": state.HasCompletedTutorial = value == "1"; break;
                    case "tutorial_step":
                        if (Enum.TryParse<TutorialStep>(value, out var ts))
                        {
                            state.Tutorial ??= new TutorialState();
                            state.Tutorial.CurrentStep = ts;
                        }
                        break;
                    case "tutorial_complete":
                        if (state.Tutorial != null)
                            state.Tutorial.IsComplete = value == "1";
                        break;
                    case "global_discovery_index": state.GlobalDiscoveryIndex = int.Parse(value); break;
                }
            }
        }

        // Cleared nodes
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

        // Owned runes
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT rune_id FROM owned_runes";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) state.OwnedRuneIds.Add(reader.GetString(0));
        }

        // Unlocked tools
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT tool_id FROM unlocked_tools";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) state.UnlockedTools.Add(reader.GetString(0));
        }

        // Discovered relics
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT relic_instance_id, card_id, acquirer_name, acquired_at, site, discovery_index, engraving_style FROM discovered_relics";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                state.DiscoveredRelics.Add(new LostRelicInstance
                {
                    RelicInstanceId = reader.GetString(0),
                    CardId = reader.GetString(1),
                    AcquirerName = reader.GetString(2),
                    AcquiredAt = reader.GetString(3),
                    Site = reader.GetString(4),
                    DiscoveryIndex = reader.GetInt32(5),
                    EngravingStyle = reader.GetString(6)
                });
            }
        }

        // Saved deck
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT card_id FROM saved_deck ORDER BY position";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) state.DeckCardIds.Add(reader.GetString(0));
        }

        // Version not present (fresh DB or pre-versioning save) → normalize to current
        if (state.Version == 0)
        {
            state.Version = CurrentSchemaVersion;
            // Fresh save — start tutorial
            if (state.Tutorial == null)
                state.Tutorial = new TutorialState { CurrentStep = TutorialStep.Lanes_SummonCreature };
        }

        var validity = ValidateVersion(state.Version);
        if (!validity.ok)
            throw new InvalidOperationException(validity.error);

        return state;
    }

    private static void SaveTo(SqliteConnection conn, ProgressionState state)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            using (var cmd = conn.CreateCommand()) { cmd.CommandText = "DELETE FROM meta"; cmd.ExecuteNonQuery(); }
            InsertMeta(conn, "version", CurrentSchemaVersion.ToString());
            InsertMeta(conn, "shards", state.Shards.ToString());
            InsertMeta(conn, "dig_charges", state.DigCharges.ToString());
            InsertMeta(conn, "tutorial_done", state.HasCompletedTutorial ? "1" : "0");
            InsertMeta(conn, "tutorial_step", state.Tutorial?.CurrentStep.ToString() ?? "");
            InsertMeta(conn, "tutorial_complete", state.Tutorial?.IsComplete == true ? "1" : "0");
            InsertMeta(conn, "global_discovery_index", state.GlobalDiscoveryIndex.ToString());

            using (var cmd = conn.CreateCommand()) { cmd.CommandText = "DELETE FROM cleared_nodes"; cmd.ExecuteNonQuery(); }
            foreach (var nodeId in state.ClearedNodes)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO cleared_nodes (node_id) VALUES (@id)";
                cmd.Parameters.AddWithValue("@id", nodeId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = conn.CreateCommand()) { cmd.CommandText = "DELETE FROM collection"; cmd.ExecuteNonQuery(); }
            foreach (var (cardId, count) in state.Collection)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO collection (card_id, count) VALUES (@id, @c)";
                cmd.Parameters.AddWithValue("@id", cardId);
                cmd.Parameters.AddWithValue("@c", count);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = conn.CreateCommand()) { cmd.CommandText = "DELETE FROM fragments"; cmd.ExecuteNonQuery(); }
            foreach (var (strata, count) in state.Fragments)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO fragments (strata, count) VALUES (@s, @c)";
                cmd.Parameters.AddWithValue("@s", strata);
                cmd.Parameters.AddWithValue("@c", count);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = conn.CreateCommand()) { cmd.CommandText = "DELETE FROM owned_runes"; cmd.ExecuteNonQuery(); }
            foreach (var runeId in state.OwnedRuneIds)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO owned_runes (rune_id) VALUES (@id)";
                cmd.Parameters.AddWithValue("@id", runeId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = conn.CreateCommand()) { cmd.CommandText = "DELETE FROM unlocked_tools"; cmd.ExecuteNonQuery(); }
            foreach (var toolId in state.UnlockedTools)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO unlocked_tools (tool_id) VALUES (@id)";
                cmd.Parameters.AddWithValue("@id", toolId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = conn.CreateCommand()) { cmd.CommandText = "DELETE FROM discovered_relics"; cmd.ExecuteNonQuery(); }
            foreach (var relic in state.DiscoveredRelics)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO discovered_relics
                    (relic_instance_id, card_id, acquirer_name, acquired_at, site, discovery_index, engraving_style)
                    VALUES (@id, @cid, @name, @date, @site, @idx, @style)
                    """;
                cmd.Parameters.AddWithValue("@id", relic.RelicInstanceId);
                cmd.Parameters.AddWithValue("@cid", relic.CardId);
                cmd.Parameters.AddWithValue("@name", relic.AcquirerName);
                cmd.Parameters.AddWithValue("@date", relic.AcquiredAt);
                cmd.Parameters.AddWithValue("@site", relic.Site);
                cmd.Parameters.AddWithValue("@idx", relic.DiscoveryIndex);
                cmd.Parameters.AddWithValue("@style", relic.EngravingStyle);
                cmd.ExecuteNonQuery();
            }

            // Saved deck: clear + re-insert
            using (var cmd = conn.CreateCommand()) { cmd.CommandText = "DELETE FROM saved_deck"; cmd.ExecuteNonQuery(); }
            for (int i = 0; i < state.DeckCardIds.Count; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO saved_deck (position, card_id) VALUES (@pos, @id)";
                cmd.Parameters.AddWithValue("@pos", i);
                cmd.Parameters.AddWithValue("@id", state.DeckCardIds[i]);
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

    private static void InsertMeta(SqliteConnection conn, string key, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO meta (key, value) VALUES (@k, @v)";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }
}
using System;
using System.Collections.Generic;
using System.IO;
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
    public const int CurrentSchemaVersion = 5;

    private readonly string _dbPath;

    /// <summary>
    /// Collection of repair actions taken during the most recent load.
    /// Each entry describes what was corrupted/missing and what default was used.
    /// Cleared at the start of each Load() call.
    /// </summary>
    public List<string> RepairLog { get; } = new();

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
    /// Migrate a loaded state from an older schema version to the current version.
    /// Each version gap runs its own migration step. The state's Version is updated
    /// to CurrentSchemaVersion after all steps complete.
    /// </summary>
    private static void MigrateToCurrent(ProgressionState state, List<string> repairLog)
    {
        int sourceVersion = state.Version;
        while (state.Version < CurrentSchemaVersion)
        {
            int from = state.Version;
            int to = from + 1;
            if (from == 1 && to == 2)
            {
                // v1→v2: migrate the single saved_deck into named_decks as "My Deck"
                if (state.DeckCardIds.Count > 0 && !state.SavedDecks.ContainsKey("My Deck"))
                {
                    state.SavedDecks["My Deck"] = new List<string>(state.DeckCardIds);
                    repairLog.Add($"Migrated saved deck ({state.DeckCardIds.Count} cards) into named_decks as 'My Deck'");
                }
            }
            else if (from == 2 && to == 3)
            {
                // v2→v3: seed collection from saved decks if collection is empty
                if (state.Collection.Count == 0 && state.SavedDecks.Count > 0)
                {
                    foreach (var (deckName, cardIds) in state.SavedDecks)
                    {
                        foreach (var cardId in cardIds)
                        {
                            state.AddCard(cardId);
                        }
                    }
                    repairLog.Add($"Seeded collection from {state.SavedDecks.Count} saved deck(s) ({state.Collection.Count} card copies total)");
                }
            }
            else if (from == 3 && to == 4)
            {
                // v3→v4: add RuneDust field (initializes to 0 — no migration needed)
                state.RuneDust = 0;
                repairLog.Add("Initialized RuneDust to 0 (v3→v4 migration)");
            }
            else if (from == 4 && to == 5)
            {
                // v4→v5: add RuneSlotUnlockCounts (defaults already set in constructor)
                // and RuneUpgradeTiers (empty — no migration needed)
                if (state.RuneSlotUnlockCounts.Count == 0)
                {
                    state.RuneSlotUnlockCounts[nameof(RuneSlotType.OFFENSIVE)] = 1;
                    state.RuneSlotUnlockCounts[nameof(RuneSlotType.DEFENSIVE)] = 1;
                    state.RuneSlotUnlockCounts[nameof(RuneSlotType.UTILITY)] = 1;
                    state.RuneSlotUnlockCounts[nameof(RuneSlotType.MYTHIC)] = 1;
                }
                repairLog.Add("Initialized RuneSlotUnlockCounts to defaults (v4→v5 migration)");
            }
            state.Version = to;
            repairLog.Add($"Migrated save from v{from} to v{to}");
        }
        if (sourceVersion > 0 && sourceVersion < CurrentSchemaVersion)
            repairLog.Add($"Schema migration complete: v{sourceVersion} → v{CurrentSchemaVersion}");
    }

    /// <summary>
    /// Load progression state from the database file. Creates the file and
    /// tables if they do not exist, applying version migration as needed.
    /// Returns a fresh state on failure — save errors never block the game.
    /// Auto-repair details are recorded in <see cref="RepairLog"/>.
    /// </summary>
    public ProgressionState Load()
    {
        RepairLog.Clear();
        try
        {
            using var conn = OpenConnection();
            EnsureSchema(conn);
            var state = LoadFrom(conn, RepairLog);
            return state;
        }
        catch (Exception ex)
        {
            RepairLog.Add($"Load failed: {ex.GetType().Name}: {ex.Message}. Returning fresh default profile.");
            var fresh = new ProgressionState { Version = CurrentSchemaVersion };
            return fresh;
        }
    }

    /// <summary>
    /// Persist the given progression state atomically. All writes happen inside
    /// a single transaction so a process kill mid-write leaves the previous
    /// committed state intact (SQLite rolls back the uncommitted transaction).
    /// Returns true on success, false on failure (logs the error internally).
    /// If the database file is corrupted (e.g. after a repaired load), the
    /// file is deleted and recreated transparently.
    /// </summary>
    public bool Save(ProgressionState state)
    {
        try
        {
            // Try a normal save first
            if (TrySaveInternal(state))
                return true;

            // The DB file is corrupt or unreadable — delete and recreate
            RepairLog.Add("Save failed — deleting corrupted database file and recreating.");
            TryDeleteFile(_dbPath);
            TryDeleteFile(_dbPath + "-wal");
            TryDeleteFile(_dbPath + "-shm");

            // Retry the save on a fresh file
            return TrySaveInternal(state);
        }
        catch (Exception ex)
        {
            RepairLog.Add($"Save failed after retry: {ex.GetType().Name}: {ex.Message}. Progress will NOT persist this session.");
            System.Diagnostics.Debug.WriteLine($"[SaveRepository] Save failed after retry: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Internal save attempt — does NOT retry on failure.</summary>
    private bool TrySaveInternal(ProgressionState state)
    {
        try
        {
            using var conn = OpenConnection();
            EnsureSchema(conn);
            SaveTo(conn, state);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SaveRepository] Save attempt failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Test that the database can be opened, written to, and read from.
    /// Returns (success, errorMessage). Used by the on-device diagnostics
    /// button to surface SQLite errors to the player.
    /// </summary>
    public (bool Success, string? ErrorMessage) TestReadWrite()
    {
        try
        {
            using var conn = OpenConnection();
            EnsureSchema(conn);

            // Write a diagnostic marker
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO meta (key, value) VALUES (@key, @val)";
                cmd.Parameters.AddWithValue("@key", "_diag_test");
                cmd.Parameters.AddWithValue("@val", "ok");
                cmd.ExecuteNonQuery();
            }

            // Read it back
            string? readBack;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT value FROM meta WHERE key = '_diag_test'";
                readBack = cmd.ExecuteScalar() as string;
            }

            // Clean up
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM meta WHERE key = '_diag_test'";
                cmd.ExecuteNonQuery();
            }

            bool ok = readBack == "ok";
            return (ok, ok ? null : $"Read-back mismatch: got '{readBack}' expected 'ok'");
        }
        catch (Exception ex)
        {
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Save settings to a key/value table.
    /// </summary>
    public void SaveSettings(SettingsState settings)
    {
        using var conn = OpenConnection();
        EnsureSchema(conn);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL)";
        cmd.ExecuteNonQuery();

        using (var clear = conn.CreateCommand()) { clear.CommandText = "DELETE FROM settings"; clear.ExecuteNonQuery(); }

        InsertSetting(conn, "master_volume", settings.MasterVolume.ToString());
        InsertSetting(conn, "music_volume", settings.MusicVolume.ToString());
        InsertSetting(conn, "sfx_volume", settings.SfxVolume.ToString());
        InsertSetting(conn, "reduce_motion", settings.ReduceMotion ? "1" : "0");
        InsertSetting(conn, "large_text", settings.LargeText ? "1" : "0");
        InsertSetting(conn, "high_contrast", settings.HighContrast ? "1" : "0");
        InsertSetting(conn, "intro_seen", settings.IntroSeen ? "1" : "0");
        InsertSetting(conn, "language", settings.Language);
    }

    /// <summary>
    /// Load settings from the key/value table.
    /// Returns default settings if table empty or missing.
    /// Missing keys get default values.
    /// </summary>
    public SettingsState LoadSettings()
    {
        var s = new SettingsState();
        try
        {
            using var conn = OpenConnection();
            EnsureSchema(conn);

            // Ensure settings table exists
            using var createCmd = conn.CreateCommand();
            createCmd.CommandText = "CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL)";
            createCmd.ExecuteNonQuery();

            var dict = new Dictionary<string, string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT key, value FROM settings";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    dict[reader.GetString(0)] = reader.GetString(1);
            }

            if (dict.TryGetValue("master_volume", out var mv) && float.TryParse(mv, out var mvf)) s.MasterVolume = mvf;
            if (dict.TryGetValue("music_volume", out var musv) && float.TryParse(musv, out var musf)) s.MusicVolume = musf;
            if (dict.TryGetValue("sfx_volume", out var sfx) && float.TryParse(sfx, out var sfxf)) s.SfxVolume = sfxf;
            if (dict.TryGetValue("reduce_motion", out var rm)) s.ReduceMotion = rm == "1";
            if (dict.TryGetValue("large_text", out var lt)) s.LargeText = lt == "1";
            if (dict.TryGetValue("high_contrast", out var hc)) s.HighContrast = hc == "1";
            if (dict.TryGetValue("intro_seen", out var ins)) s.IntroSeen = ins == "1";
            if (dict.TryGetValue("language", out var lang)) s.Language = lang;
        }
        catch
        {
            // Return defaults on any failure
        }
        return s;
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
        // Pooling=False: every connection is a real file open/close.
        // Game saves are infrequent, and pooling keeps the file handle alive
        // which prevents the auto-repair retry (deleting a corrupted file and
        // recreating it) from working. No measurable performance difference
        // for a single-user game save system.
        var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
        conn.Open();
        try
        {
            // WAL-mode: faster reads, supports concurrent readers — but not all
            // Android filesystems support it (F2FS, some /data partitions reject it).
            using (var pragma = conn.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=WAL";
                pragma.ExecuteNonQuery();
            }
        }
        catch
        {
            // WAL not supported — fall back to DELETE journal mode
            try
            {
                using var pragma = conn.CreateCommand();
                pragma.CommandText = "PRAGMA journal_mode=DELETE";
                pragma.ExecuteNonQuery();
            }
            catch { /* best effort */ }
        }
        try
        {
            using (var pragma = conn.CreateCommand())
            {
                pragma.CommandText = "PRAGMA foreign_keys=ON";
                pragma.ExecuteNonQuery();
            }
        }
        catch { /* best effort */ }
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

            CREATE TABLE IF NOT EXISTS named_decks (
                deck_name TEXT NOT NULL,
                position INTEGER NOT NULL,
                card_id TEXT NOT NULL,
                PRIMARY KEY (deck_name, position)
            );
        """;
        cmd.ExecuteNonQuery();
    }

    private static ProgressionState LoadFrom(SqliteConnection conn, List<string> repairLog)
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
                    case "rune_page": state.SavedRunePageJson = value; break;
                    case "rune_dust": state.RuneDust = int.Parse(value); break;
                    case "rune_slot_unlock_counts":
                        var slotCounts = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(value, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (slotCounts != null)
                        {
                            state.RuneSlotUnlockCounts.Clear();
                            foreach (var kv in slotCounts)
                                state.RuneSlotUnlockCounts[kv.Key] = kv.Value;
                        }
                        break;
                    case "rune_upgrade_tiers":
                        var tiers = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(value, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (tiers != null)
                        {
                            state.RuneUpgradeTiers.Clear();
                            foreach (var kv in tiers)
                                state.RuneUpgradeTiers[kv.Key] = kv.Value;
                        }
                        break;
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

        // Named decks (schema v2+)
        try
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT deck_name, card_id FROM named_decks ORDER BY deck_name, position";
                using var reader = cmd.ExecuteReader();
                Dictionary<string, List<string>> decks = new();
                while (reader.Read())
                {
                    string name = reader.GetString(0);
                    string cardId = reader.GetString(1);
                    if (!decks.ContainsKey(name))
                        decks[name] = new List<string>();
                    decks[name].Add(cardId);
                }
                foreach (var kv in decks)
                    state.SavedDecks[kv.Key] = kv.Value;
            }
        }
        catch
        {
            // named_decks table may not exist on v1 schema — skip silently
        }

        // Version not present (fresh DB or pre-versioning save) → normalize to current
        if (state.Version == 0)
        {
            repairLog.Clear();
            if (state.Shards > 0 || state.ClearedNodes.Count > 0 || state.Collection.Count > 0)
                repairLog.Add("Save missing version field but contains data — treating as v1, data preserved.");
            else
                repairLog.Add("No saved profile found — starting fresh.");
            state.Version = CurrentSchemaVersion;
            // Fresh save — start tutorial
            if (state.Tutorial == null)
                state.Tutorial = new TutorialState { CurrentStep = TutorialStep.Lanes_SummonCreature };
        }

        // Validate stored version
        var validity = ValidateVersion(state.Version);
        if (!validity.ok)
        {
            repairLog.Add(validity.error ?? "Unknown schema validation error.");
            throw new InvalidOperationException(validity.error);
        }

        // Migrate forward if older than current
        if (state.Version < CurrentSchemaVersion)
            MigrateToCurrent(state, repairLog);

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
            InsertMeta(conn, "rune_page", state.SavedRunePageJson ?? "");
            InsertMeta(conn, "rune_dust", state.RuneDust.ToString());
            InsertMeta(conn, "rune_slot_unlock_counts", System.Text.Json.JsonSerializer.Serialize(state.RuneSlotUnlockCounts));
            InsertMeta(conn, "rune_upgrade_tiers", System.Text.Json.JsonSerializer.Serialize(state.RuneUpgradeTiers));

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

            // Named decks (schema v2)
            using (var cmd = conn.CreateCommand()) { cmd.CommandText = "DELETE FROM named_decks"; cmd.ExecuteNonQuery(); }
            foreach (var (deckName, cardIds) in state.SavedDecks)
            {
                for (int i = 0; i < cardIds.Count; i++)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "INSERT INTO named_decks (deck_name, position, card_id) VALUES (@name, @pos, @id)";
                    cmd.Parameters.AddWithValue("@name", deckName);
                    cmd.Parameters.AddWithValue("@pos", i);
                    cmd.Parameters.AddWithValue("@id", cardIds[i]);
                    cmd.ExecuteNonQuery();
                }
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

    private static void InsertSetting(SqliteConnection conn, string key, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO settings (key, value) VALUES (@k, @v)";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Delete a file silently (best-effort). Used to nuke a corrupted DB before recreating it.</summary>
    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }
}
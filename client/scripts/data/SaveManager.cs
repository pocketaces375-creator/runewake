using System;
using Godot;
using Runewake.Engine.State;
using Runewake.Persistence;

namespace Runewake.Client;

/// <summary>
/// Thin Godot-facing wrapper over <see cref="SaveRepository"/>.
/// The client owns ONLY where the save file lives (user:// sandboxed storage);
/// the <see cref="SaveRepository"/> owns the schema, versioning, and atomic
/// save/load. All progression semantics live in engine <see cref="ProgressionState"/>.
///
/// Save failures are always non-fatal: the game continues with a fresh in-memory
/// profile and surfaces the error to the UI via <see cref="LastError"/>.
/// </summary>
public class SaveManager
{
    private readonly SaveRepository _repository;

    /// <summary>Current in-memory progression state.</summary>
    public ProgressionState State { get; } = new();

    /// <summary>True after <see cref="Initialize"/> completes (even on failure).</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>Error message from the last failed load, or null if the save is working.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// True if the save system is fully functional (DB opened, written, read successfully).
    /// False means the game is running on a temporary in-memory profile.
    /// </summary>
    public bool IsFunctional { get; private set; } = true;

    /// <summary>
    /// Repair log from the most recent load. Empty if no repairs were needed.
    /// Each entry describes what was corrupted/missing and what fallback was used.
    /// Cleared on every call to <see cref="Initialize"/>.
    /// </summary>
    public IReadOnlyList<string> RepairLog => _repository.RepairLog;

    /// <summary>True if the most recent load performed any repair or migration.</summary>
    public bool WasRepaired => _repository.RepairLog.Count > 0;

    public SaveManager()
    {
        // user:// is the Godot-managed, platform sandboxed data directory.
        // On Android it resolves to the app's internal storage (not res://,
        // not an absolute hardcoded path) — safe against app sandboxing.
        string dataDir = ProjectSettings.GlobalizePath("user://");
        string dbPath = System.IO.Path.Combine(dataDir, "runewake_save.db");
        _repository = new SaveRepository(dbPath);
    }

    /// <summary>
    /// Load existing save data (creating tables if missing) into <see cref="State"/>.
    /// On failure: logs the error, sets <see cref="LastError"/>, marks
    /// <see cref="IsFunctional"/> = false, and continues with a fresh profile.
    /// The game NEVER blocks on save failure.
    /// </summary>
    public void Initialize()
    {
        try
        {
            var loaded = _repository.Load();
            CopyInto(loaded, State);

            // Log any repairs that occurred
            if (WasRepaired)
            {
                string log = string.Join("; ", _repository.RepairLog);
                GD.Print($"[SaveManager] Save load completed with repairs: {log}");
            }

            IsLoaded = true;
            IsFunctional = true;
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = $"{ex.GetType().Name}: {ex.Message}";
            IsFunctional = false;
            IsLoaded = true; // game continues with fresh in-memory state
            GD.PrintErr($"[SaveManager] Load failed (non-fatal): {LastError}");
        }
    }

    /// <summary>
    /// Persist the current <see cref="State"/> atomically.
    /// Returns true on success. On failure, logs and returns false — the
    /// in-memory state is still valid, it just didn't reach disk.
    /// </summary>
    public bool Save()
    {
        bool ok = _repository.Save(State);
        if (!ok)
        {
            IsFunctional = false;
            LastError ??= "Save failed (see log)";
            GD.PrintErr("[SaveManager] Save failed");
        }
        return ok;
    }

    /// <summary>Close the repository. Call when the game exits.</summary>
    public void Close()
    {
        IsLoaded = false;
    }

    /// <summary>
    /// Load settings from the repository.
    /// </summary>
    public SettingsState LoadSettings()
    {
        return _repository.LoadSettings();
    }

    /// <summary>
    /// Save settings to the repository.
    /// </summary>
    public void SaveSettings(SettingsState settings)
    {
        _repository.SaveSettings(settings);
    }

    /// <summary>
    /// Run a diagnostic write+read-back test on the database.
    /// Returns (success, errorMessage) with the raw exception text on failure.
    /// This is called from the on-device diagnostics button.
    /// </summary>
    public (bool Success, string? Error) TestReadWrite()
    {
        return _repository.TestReadWrite();
    }

    /// <summary>Copy a freshly-loaded state into the live mutable state object.</summary>
    private static void CopyInto(ProgressionState from, ProgressionState to)
    {
        to.Version = from.Version;
        to.Shards = from.Shards;
        to.DigCharges = from.DigCharges;
        to.HasCompletedTutorial = from.HasCompletedTutorial;
        to.GlobalDiscoveryIndex = from.GlobalDiscoveryIndex;

        to.ClearedNodes.Clear();
        foreach (var id in from.ClearedNodes) to.ClearedNodes.Add(id);

        to.Collection.Clear();
        foreach (var (k, v) in from.Collection) to.Collection[k] = v;

        to.Fragments.Clear();
        foreach (var (k, v) in from.Fragments) to.Fragments[k] = v;

        to.OwnedRuneIds.Clear();
        foreach (var id in from.OwnedRuneIds) to.OwnedRuneIds.Add(id);

        to.UnlockedTools.Clear();
        foreach (var id in from.UnlockedTools) to.UnlockedTools.Add(id);

        to.DiscoveredRelics.Clear();
        to.DiscoveredRelics.AddRange(from.DiscoveredRelics);

        to.DeckCardIds.Clear();
        to.DeckCardIds.AddRange(from.DeckCardIds);
    }
}
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
    private SaveRepository _repository;
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

    /// <summary>FABLE-047: which save file (runewake_save_slot{N}.db) this manager reads and writes.</summary>
    public int CurrentSlot { get; private set; }

    public SaveManager()
    {
        // user:// is the Godot-managed, platform sandboxed data directory.
        // On Android it resolves to the app's internal storage (not res://,
        // not an absolute hardcoded path) — safe against app sandboxing.
        _repository = CreateRepository(0);
    }

    /// <summary>
    /// Create a repository for a specific campaign slot.
    /// </summary>
    public SaveManager(int slotIndex)
    {
        _repository = CreateRepository(slotIndex);
        CurrentSlot = slotIndex;
    }

    /// <summary>
    /// Create the appropriate repository for a given slot.
    /// </summary>
    private static SaveRepository CreateRepository(int slotIndex)
    {
        string dataDir = ProjectSettings.GlobalizePath("user://");
        string dbPath = slotIndex >= 0
            ? System.IO.Path.Combine(dataDir, $"runewake_save_slot{slotIndex}.db")
            : System.IO.Path.Combine(dataDir, "runewake_save.db");
        return new SaveRepository(dbPath);
    }

    /// <summary>
    /// Switch to a different campaign slot, saving the current state first.
    /// The new slot's data is loaded into <see cref="State"/>.
    /// </summary>
    public void SwitchSlot(int slotIndex, bool saveCurrent = true)
    {
        // Save current progression to the old slot's DB.
        // FABLE-047: ONLY a state that was actually loaded. On a fresh launch State is still the empty
        // default when the profiles load and switch to the active slot — saving it here wrote an EMPTY
        // save over slot 0 on every launch (cards, cleared nodes, dust: all gone). And never when the
        // caller has just deleted the old slot's file: that would write the deleted campaign back.
        if (saveCurrent && IsLoaded && IsFunctional)
            _repository.Save(State);

        // Create a new repository for the target slot
        _repository = CreateRepository(slotIndex);
        CurrentSlot = slotIndex;

        // Reset state
        LastError = null;
        IsFunctional = true;
        IsLoaded = false;

        // Load the new slot's data
        Initialize();
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
        else
        {
            // FABLE-018: every local save is a candidate for the cloud. The
            // SyncManager coalesces bursts and pushes once things go quiet.
            // Best-effort, never throws, no-op when accounts are off.
            try { CampaignContext.SyncManager?.NotifySaved(); } catch { /* never let sync break a save */ }
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
        // FABLE-ACCOUNTS-1: copy EVERY field. The hand-written copy this replaces
        // skipped Delver level/XP, the rune page, tutorial state, shop day, Duel
        // Arena record and named decks, so those quietly reset on load. The cloud
        // snapshot is the one list of all fields (AccountsTests checks it by
        // reflection), so reuse it rather than keep a second list in step.
        to.Version = from.Version;
        Runewake.Engine.Supabase.ProgressionSnapshot.FromState(from).ApplyTo(to);
    }
}
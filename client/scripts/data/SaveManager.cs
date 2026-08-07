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
/// </summary>
public class SaveManager
{
    private readonly SaveRepository _repository;

    /// <summary>Current in-memory progression state.</summary>
    public ProgressionState State { get; } = new();

    /// <summary>True after <see cref="Initialize"/> completes successfully.</summary>
    public bool IsLoaded { get; private set; }

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
    /// </summary>
    public void Initialize()
    {
        var loaded = _repository.Load();
        CopyInto(loaded, State);
        IsLoaded = true;
    }

    /// <summary>Persist the current <see cref="State"/> atomically.</summary>
    public void Save()
    {
        _repository.Save(State);
    }

    /// <summary>Close the repository. Call when the game exits.</summary>
    public void Close()
    {
        IsLoaded = false;
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
    }
}
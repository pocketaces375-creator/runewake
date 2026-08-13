using System.Collections.Generic;

namespace Runewake.Engine.Cards;

/// <summary>
/// Registry for Artifact definitions. Similar to <see cref="CardRegistry"/>
/// but for Artifact cards (field-effect cards that sit in permanent slots).
/// Artifacts are never drawn, discarded, or loaded from deck packs — they
/// are loaded from the class configuration at game start.
/// </summary>
public static class ArtifactRegistry
{
    private static readonly Dictionary<string, ArtifactDef> _artifacts = new();

    /// <summary>
    /// Register a single Artifact definition.
    /// </summary>
    public static void Register(ArtifactDef def)
    {
        _artifacts[def.Id] = def;
    }

    /// <summary>
    /// Register multiple Artifact definitions at once.
    /// </summary>
    public static void RegisterMany(IEnumerable<ArtifactDef> defs)
    {
        foreach (var def in defs)
            _artifacts[def.Id] = def;
    }

    /// <summary>
    /// Look up an Artifact definition by ID, or null if not found.
    /// </summary>
    public static ArtifactDef? Get(string id)
    {
        return _artifacts.TryGetValue(id, out var def) ? def : null;
    }

    /// <summary>
    /// Returns all registered Artifact definitions.
    /// </summary>
    public static IEnumerable<ArtifactDef> GetAll() => _artifacts.Values;

    /// <summary>
    /// Clears all registered Artifacts (for testing).
    /// </summary>
    public static void Clear()
    {
        _artifacts.Clear();
    }
}
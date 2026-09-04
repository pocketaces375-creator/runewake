namespace Runewake.Engine.State;

/// <summary>
/// Migrates legacy class IDs from old saves (TASK-ROSTER-LOCK-1).
/// thief → rogue, ranger → astrologist.
/// This lives in the engine so tests can verify it without Godot dependencies.
/// </summary>
public static class ClassIdMigration
{
    /// <summary>
    /// Map a legacy class ID to its current name. Returns null if no mapping applies.
    /// </summary>
    public static string? MapLegacyClassId(string classId)
    {
        if (string.IsNullOrEmpty(classId)) return null;
        string lower = classId.ToLowerInvariant();
        return lower switch
        {
            "thief" => "rogue",
            "ranger" => "astrologist",
            _ => null
        };
    }

    /// <summary>
    /// Apply mapping. Returns the mapped ID, or the original if no mapping applies.
    /// </summary>
    public static string ApplyMigration(string classId)
    {
        return MapLegacyClassId(classId) ?? classId;
    }
}
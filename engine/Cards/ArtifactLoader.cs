using System.IO;
using System.Linq;
using System.Text.Json;

namespace Runewake.Engine.Cards;

/// <summary>
/// Loads Artifact definitions from JSON files.
/// Artifacts are registered in <see cref="ArtifactRegistry"/> at app start.
/// </summary>
public static class ArtifactLoader
{
    /// <summary>
    /// Load Artifact definitions from a JSON file and register them in the ArtifactRegistry.
    /// </summary>
    public static int LoadPack(string path)
    {
        var json = System.IO.File.ReadAllText(path);
        var artifacts = JsonSerializer.Deserialize<List<ArtifactDef>>(json, _jsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse Artifact file: {path}");

        ArtifactRegistry.RegisterMany(artifacts);
        return artifacts.Count;
    }

    /// <summary>
    /// Load all Artifact variant files from a directory.
    /// Every *.json file in the directory is loaded with the same schema and validation
    /// as launch_artifacts.json. Non-existent directories are silently skipped.
    /// </summary>
    public static int LoadAllVariants(string variantsDir)
    {
        if (!Directory.Exists(variantsDir))
            return 0;

        int total = 0;
        foreach (var file in Directory.GetFiles(variantsDir, "*.json"))
        {
            total += LoadPack(file);
        }
        return total;
    }

    /// <summary>
    /// Load Artifact definitions from a JSON string (for testing or in-memory loading).
    /// </summary>
    public static int LoadFromString(string json)
    {
        var artifacts = JsonSerializer.Deserialize<List<ArtifactDef>>(json, _jsonOptions)
            ?? throw new InvalidOperationException("Failed to parse Artifact JSON string.");

        ArtifactRegistry.RegisterMany(artifacts);
        return artifacts.Count;
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };
}
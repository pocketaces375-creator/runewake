using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// Static loader for dig site definition JSON files.
/// Follows the same pattern as <see cref="CardLoader"/>, <see cref="EncounterLoader"/>, <see cref="RuneLoader"/>.
/// </summary>
public static class DigSiteLoader
{
    /// <summary>
    /// JSON serializer options matching other loader conventions.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Loads a dig site pack from a JSON file path.
    /// </summary>
    public static DigSitePack LoadPack(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<DigSitePack>(json, JsonOptions)
               ?? throw new JsonException("Dig site pack deserialized to null.");
    }

    /// <summary>
    /// Loads a dig site pack from a JSON string (for testing).
    /// </summary>
    public static DigSitePack LoadPackFromString(string json)
    {
        return JsonSerializer.Deserialize<DigSitePack>(json, JsonOptions)
               ?? throw new JsonException("Dig site pack deserialized to null.");
    }
}
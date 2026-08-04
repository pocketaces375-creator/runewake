using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// Static loader for dig tool definition JSON files.
/// Follows the same pattern as <see cref="CardLoader"/>, <see cref="DigSiteLoader"/>, etc.
/// </summary>
public static class DigToolLoader
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
    /// Loads a dig tool pack from a JSON file path.
    /// </summary>
    public static DigToolPack LoadPack(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<DigToolPack>(json, JsonOptions)
               ?? throw new JsonException("Dig tool pack deserialized to null.");
    }

    /// <summary>
    /// Loads a dig tool pack from a JSON string (for testing).
    /// </summary>
    public static DigToolPack LoadPackFromString(string json)
    {
        return JsonSerializer.Deserialize<DigToolPack>(json, JsonOptions)
               ?? throw new JsonException("Dig tool pack deserialized to null.");
    }
}
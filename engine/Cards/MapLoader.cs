using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// Static loader for map region JSON files.
/// Reads a <see cref="MapRegion"/> from the given path.
/// </summary>
public static class MapLoader
{
    /// <summary>
    /// JSON serializer options matching CardLoader conventions.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Loads a map region from a JSON file path.
    /// </summary>
    /// <param name="path">Absolute or relative path to the JSON file.</param>
    /// <returns>The deserialized map region.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="JsonException">The file is not valid map JSON.</exception>
    public static MapRegion LoadRegion(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<MapRegion>(json, JsonOptions)
               ?? throw new JsonException("Map region deserialized to null.");
    }

    /// <summary>
    /// Loads a map region from a JSON string (for testing).
    /// </summary>
    public static MapRegion LoadRegionFromString(string json)
    {
        return JsonSerializer.Deserialize<MapRegion>(json, JsonOptions)
               ?? throw new JsonException("Map region deserialized to null.");
    }
}
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// Static loader for encounter pack JSON files.
/// Reads an <see cref="EncounterPack"/> from the given path.
/// </summary>
public static class EncounterLoader
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
    /// Loads an encounter pack from a JSON file path.
    /// </summary>
    public static EncounterPack LoadPack(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<EncounterPack>(json, JsonOptions)
               ?? throw new JsonException("Encounter pack deserialized to null.");
    }

    /// <summary>
    /// Loads an encounter pack from a JSON string (for testing).
    /// </summary>
    public static EncounterPack LoadPackFromString(string json)
    {
        return JsonSerializer.Deserialize<EncounterPack>(json, JsonOptions)
               ?? throw new JsonException("Encounter pack deserialized to null.");
    }
}
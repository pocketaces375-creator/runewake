using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Runewake.Engine.Cards;

namespace Runewake.Engine.Cards;

/// <summary>
/// Static loader for rune pack JSON files.
/// Follows the same pattern as <see cref="CardLoader"/> and <see cref="EncounterLoader"/>.
/// </summary>
public static class RuneLoader
{
    /// <summary>
    /// JSON serializer options matching the conventions used elsewhere.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Loads a rune pack from a JSON file path.
    /// </summary>
    public static RunePack LoadPack(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RunePack>(json, JsonOptions)
               ?? throw new JsonException("Rune pack deserialized to null.");
    }

    /// <summary>
    /// Loads a rune pack from a JSON string (for testing).
    /// </summary>
    public static RunePack LoadPackFromString(string json)
    {
        return JsonSerializer.Deserialize<RunePack>(json, JsonOptions)
               ?? throw new JsonException("Rune pack deserialized to null.");
    }
}
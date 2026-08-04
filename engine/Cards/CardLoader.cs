using System.Text.Json;
using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// Static loader for card pack JSON files.
/// Reads a JSON array of <see cref="CardDef"/> from the given path.
/// </summary>
public static class CardLoader
{
    /// <summary>
    /// JSON serializer options used by the card loader. Exposed for reuse by the validator CLI.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Loads a card pack from a JSON file path.
    /// </summary>
    /// <param name="path">Absolute or relative path to the JSON file.</param>
    /// <returns>List of card definitions.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="JsonException">The file is not valid card JSON.</exception>
    public static List<CardDef> LoadPack(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<CardDef>>(json, JsonOptions)
               ?? throw new JsonException("Card pack deserialized to null.");
    }

    /// <summary>
    /// Loads a card pack from a JSON string (for testing).
    /// </summary>
    public static List<CardDef> LoadPackFromString(string json)
    {
        return JsonSerializer.Deserialize<List<CardDef>>(json, JsonOptions)
               ?? throw new JsonException("Card pack deserialized to null.");
    }
}

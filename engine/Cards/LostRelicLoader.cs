using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// Static loader for Lost Relic definition JSON files.
/// </summary>
public static class LostRelicLoader
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static LostRelicPack LoadPack(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<LostRelicPack>(json, JsonOptions)
               ?? throw new JsonException("Lost relic pack deserialized to null.");
    }

    public static LostRelicPack LoadPackFromString(string json)
    {
        return JsonSerializer.Deserialize<LostRelicPack>(json, JsonOptions)
               ?? throw new JsonException("Lost relic pack deserialized to null.");
    }
}
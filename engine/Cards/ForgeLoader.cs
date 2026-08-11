using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// Model for forge recipes — maps strata to lists of forgeable rune IDs.
/// </summary>
public class ForgeRecipeBook
{
    /// <summary>
    /// Dictionary: strata name → list of rune IDs forgeable from that strata's fragments.
    /// </summary>
    [JsonPropertyName("recipes")]
    public Dictionary<string, List<string>> Recipes { get; set; } = new();
}

/// <summary>
/// Loads forge recipe data from JSON files or strings.
/// </summary>
public static class ForgeLoader
{
    public static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static ForgeRecipeBook LoadPack(string path)
    {
        string json = System.IO.File.ReadAllText(path);
        return LoadPackFromString(json);
    }

    public static ForgeRecipeBook LoadPackFromString(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize<ForgeRecipeBook>(json, JsonOptions)
               ?? new ForgeRecipeBook();
    }
}
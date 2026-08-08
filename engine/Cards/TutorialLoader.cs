using System.Collections.Generic;
using System.Text.Json;
using Runewake.Engine.State;

namespace Runewake.Engine.Cards;

/// <summary>
/// Model for a single tutorial step definition loaded from JSON.
/// </summary>
public class TutorialStepDef
{
    public TutorialStep Step { get; set; }
    public string Highlight { get; set; } = "";
    public string Message { get; set; } = "";
}

/// <summary>
/// Loads tutorial step definitions from JSON files or strings.
/// Pure engine code — no Godot dependency.
/// </summary>
public static class TutorialLoader
{
    /// <summary>
    /// Load step definitions from a file path.
    /// </summary>
    public static List<TutorialStepDef> LoadSteps(string path)
    {
        var json = System.IO.File.ReadAllText(path);
        return LoadStepsFromString(json);
    }

    /// <summary>
    /// Load step definitions from a JSON string.
    /// </summary>
    public static List<TutorialStepDef> LoadStepsFromString(string json)
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        var steps = System.Text.Json.JsonSerializer.Deserialize<List<TutorialStepDef>>(json, options);
        return steps ?? new List<TutorialStepDef>();
    }
}
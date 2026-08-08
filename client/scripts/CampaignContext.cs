using System.Collections.Generic;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Static bridge for campaign flow state between scenes.
/// No DI framework — scenes read/write this directly.
/// </summary>
public static class CampaignContext
{
    /// <summary>The encounter the player is about to face (set by MapScene before transition).</summary>
    public static EncounterDef? CurrentEncounter { get; set; }

    /// <summary>The map node ID the player is entering (for reward routing).</summary>
    public static string? CurrentNodeId { get; set; }

    /// <summary>Persistent save manager — initialized once at title screen.</summary>
    public static SaveManager SaveManager { get; } = new();

    /// <summary>Shortcut to the progression state.</summary>
    public static ProgressionState Progression => SaveManager.State;

    /// <summary>Player's current deck (card IDs). Defaults to a starter pool until deck builder is used.</summary>
    public static List<string> PlayerDeckIds { get; set; } = new();

    /// <summary>All loaded encounters keyed by encounter ID (e.g. "r1_duel_wayfarer").</summary>
    public static readonly Dictionary<string, EncounterDef> EncounterIndex = new();

    /// <summary>All loaded runes keyed by rune ID.</summary>
    public static readonly Dictionary<string, RuneDef> RuneIndex = new();

    /// <summary>All loaded dig sites keyed by dig site ID.</summary>
    public static readonly Dictionary<string, DigSiteDef> DigSiteIndex = new();

    /// <summary>All loaded dig tools keyed by tool ID.</summary>
    public static readonly Dictionary<string, DigToolDef> DigToolIndex = new();

    /// <summary>All loaded Lost Relic definitions keyed by encounter ID.</summary>
    public static readonly Dictionary<string, LostRelicDef> LostRelicIndex = new();

    /// <summary>The dig site ID the player is about to enter (set by MapScene).</summary>
    public static string? CurrentDigSiteId { get; set; }

    /// <summary>The current rune page configuration.</summary>
    public static RunePage CurrentRunePage { get; set; } = new();

    /// <summary>Shortcut to the tutorial state from progression.</summary>
    public static TutorialState? Tutorial => Progression?.Tutorial;

    /// <summary>Loaded tutorial step definitions (from tutorial_steps.json).</summary>
    public static List<TutorialStepDef> TutorialSteps { get; set; } = new();

    /// <summary>
    /// Load all encounter packs from the content directory.
    /// Call once at title screen.
    /// </summary>
    public static void LoadEncounters()
    {
        EncounterIndex.Clear();
        var packs = new[]
        {
            "res://content/encounters/region_01_early.json",
            "res://content/encounters/region_01_mid.json",
            "res://content/encounters/region_01_late.json",
            "res://content/encounters/region_01_boss.json"
        };

        foreach (var resPath in packs)
        {
            string json = Godot.FileAccess.GetFileAsString(resPath);
            var pack = EncounterLoader.LoadPackFromString(json);
            foreach (var enc in pack.Encounters)
                EncounterIndex[enc.Id] = enc;
        }
    }

    /// <summary>
    /// Load all rune packs and initialize the current rune page.
    /// Call once at title screen after card packs are loaded.
    /// </summary>
    public static void LoadRunes()
        {
            RuneIndex.Clear();
            string json = Godot.FileAccess.GetFileAsString("res://content/runes/starter_runes.json");
            var pack = RuneLoader.LoadPackFromString(json);
            foreach (var rune in pack.Runes)
                RuneIndex[rune.Id] = rune;
        }

    /// <summary>
    /// Load all dig site definitions from the content directory.
    /// Call once at title screen.
    /// </summary>
    public static void LoadDigSites()
        {
            DigSiteIndex.Clear();
            var paths = new[]
            {
                "res://content/dig_sites/region_01_dig.json"
            };

            foreach (var resPath in paths)
            {
                string json = Godot.FileAccess.GetFileAsString(resPath);
                var pack = DigSiteLoader.LoadPackFromString(json);
                foreach (var site in pack.DigSites)
                    DigSiteIndex[site.Id] = site;
            }
        }

    /// <summary>
    /// Load all dig tool definitions from the content directory.
    /// Call once at title screen.
    /// </summary>
    public static void LoadDigTools()
        {
            DigToolIndex.Clear();
            string json = Godot.FileAccess.GetFileAsString("res://content/dig_tools/tools.json");
            var pack = DigToolLoader.LoadPackFromString(json);
            foreach (var tool in pack.Tools)
                DigToolIndex[tool.Id] = tool;
        }

    /// <summary>
    /// Load all Lost Relic definitions from the content directory.
    /// Call once at title screen.
    /// </summary>
    public static void LoadLostRelics()
        {
            LostRelicIndex.Clear();
            string json = Godot.FileAccess.GetFileAsString("res://content/relics/relic_defs.json");
            var pack = LostRelicLoader.LoadPackFromString(json);
            foreach (var relic in pack.Relics)
                LostRelicIndex[relic.EncounterId] = relic;
        }
}
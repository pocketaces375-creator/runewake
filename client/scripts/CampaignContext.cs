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
    /// Supabase relic ledger sync manager.
    /// Set by Main.cs after LoadGameData().
    /// Null when not configured — check before use.
    /// </summary>
    public static SyncManager? SyncManager { get; set; }

    /// <summary>
    /// Telemetry service for recording gameplay events.
    /// Set by Main.cs after LoadGameData().
    /// Null when not configured — check before use.
    /// </summary>
    public static TelemetryService? Telemetry { get; set; }

    /// <summary>
    /// Current settings/accessibility state.
    /// Loaded from SQLite by Main.cs after save init.
    /// </summary>
    public static SettingsState Settings { get; set; } = new();

    /// <summary>
    /// Convenience shortcut — true when ReduceMotion is enabled.
    /// </summary>
    public static bool ReduceMotion => Settings.ReduceMotion;

    /// <summary>Test hook: auto-navigate to duel scene and capture screenshot after render.
    /// Set by Main.LoadGameData before switching to DuelScene.
    /// </summary>
    public static bool AutoCaptureScreenshot { get; set; }

    /// <summary>Test hook: auto-navigate to map scene, select first unlocked node, capture.
    /// Set by --capture-map CLI arg.
    /// </summary>
    public static bool CaptureMapScreenshot { get; set; }

    /// <summary>Test hook: auto-navigate to deck builder scene and capture screenshot.
    /// Set by --capture=deck_test CLI arg via DebugCapture.
    /// </summary>
    public static bool CaptureDeckBuilderScreenshot { get; set; }

    /// <summary>Fixed seed for deterministic capture tests. Null = random seed.</summary>
    public static ulong? DebugSeed { get; set; }

    /// <summary>Tutorial script mode: set by DebugCapture for --tutorial CLI arg. Non-null = use TutorialRunner.</summary>
    public static string? TutorialScriptId { get; set; }

    /// <summary>Artifact def IDs for the player in tutorial script mode (set by TutorialRunner).</summary>
    public static string[] TutorialPlayerArtifactIds { get; set; } = System.Array.Empty<string>();

    /// <summary>Player class for tutorial script mode (set by TutorialRunner).</summary>
    public static string TutorialPlayerClass { get; set; } = string.Empty;

    /// <summary>
    /// Per-match configuration (starting vigor, etc.).
    /// Set by the pre-duel UI (brass dial); null uses engine defaults.
    /// </summary>
    public static MatchConfig? MatchConfig { get; set; }

    /// <summary>

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
    /// Deserialize the saved rune page from ProgressionState into CurrentRunePage.
    /// Call after LoadRunes() so the rune index is populated.
    /// </summary>
    public static void LoadSavedRunePage()
    {
        CurrentRunePage = new RunePage();
        if (Progression?.SavedRunePageJson == null || Progression.SavedRunePageJson.Length < 2)
            return;

        try
        {
            var savedData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<string?>>>(Progression.SavedRunePageJson);
            if (savedData == null) return;

            var page = new RunePage();
            foreach (var (slotTypeKey, slotIds) in savedData)
            {
                var slotType = slotTypeKey switch
                {
                    "offensive" => RuneSlotType.OFFENSIVE,
                    "defensive" => RuneSlotType.DEFENSIVE,
                    "utility" => RuneSlotType.UTILITY,
                    "mythic" => RuneSlotType.MYTHIC,
                    _ => (RuneSlotType?)null
                };
                if (slotType == null) continue;

                var slots = slotType.Value switch
                {
                    RuneSlotType.OFFENSIVE => page.OffensiveSlots,
                    RuneSlotType.DEFENSIVE => page.DefensiveSlots,
                    RuneSlotType.UTILITY => page.UtilitySlots,
                    RuneSlotType.MYTHIC => page.MythicSlots,
                    _ => null
                };
                if (slots == null) continue;

                for (int i = 0; i < slots.Length && i < slotIds.Count; i++)
                {
                    if (slotIds[i] != null && RuneIndex.TryGetValue(slotIds[i]!, out var runeDef))
                        slots[i] = runeDef;
                }
            }
            CurrentRunePage = page;
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[CampaignContext] Failed to load saved rune page: {ex.Message}");
            CurrentRunePage = new RunePage();
        }
    }

    /// <summary>
    /// Serialize the current rune page into ProgressionState for saving.
    /// </summary>
    public static void SaveCurrentRunePage()
    {
        if (Progression == null) return;
        var data = new Dictionary<string, List<string?>>
        {
            ["offensive"] = CurrentRunePage.OffensiveSlots.Select(s => s?.Id).ToList(),
            ["defensive"] = CurrentRunePage.DefensiveSlots.Select(s => s?.Id).ToList(),
            ["utility"] = CurrentRunePage.UtilitySlots.Select(s => s?.Id).ToList(),
            ["mythic"] = CurrentRunePage.MythicSlots.Select(s => s?.Id).ToList()
        };
        Progression.SavedRunePageJson = System.Text.Json.JsonSerializer.Serialize(data);
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
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

    /// <summary>Test hook: capture title screen with Decks button, then navigate to deck builder.
    /// Set by --capture=title_deck CLI arg via DebugCapture.
    /// </summary>
    public static bool CaptureTitleDeckScreenshot { get; set; }
    
    /// <summary>Test hook: capture title screen only (captures and quits).
        /// Set by --capture=title_test CLI arg via DebugCapture.</summary>
        public static bool CaptureTitleTestScreenshot { get; set; }
    
        /// <summary>Test hook: capture choose path screenshot.
        /// Set by --capture=choose_path CLI arg via DebugCapture.</summary>
        public static bool CaptureChoosePathScreenshot { get; set; }
    
        /// <summary>Test hook: capture duel VICTORY overlay with encounter name. Auto-ends duel as win.</summary>
    public static bool CaptureVictoryOverlay { get; set; }

    /// <summary>Test hook: capture duel DEFEAT overlay with encounter name. Auto-ends duel as loss.</summary>
    public static bool CaptureDefeatOverlay { get; set; }

    /// <summary>Test hook: capture duel at wide aspect (1999×932) instead of standard (1152×648).
    /// Set by --capture=duel_test_wide CLI arg via DebugCapture.</summary>
    public static bool WideCaptureMode { get; set; }

    /// <summary>Test hook: capture duel with visible slot outlines over the plate (no cards).
    /// Set by --capture=duel_test_align CLI arg via DebugCapture.</summary>
    public static bool DebugAlignMode { get; set; }

    /// <summary>Fixed seed for deterministic capture tests. Null = random seed.</summary>
    public static ulong? DebugSeed { get; set; }

    /// <summary>Tutorial script mode: set by DebugCapture for --tutorial CLI arg. Non-null = use TutorialRunner.</summary>
    public static string? TutorialScriptId { get; set; }

    /// <summary>Artifact def IDs for the player in tutorial script mode (set by TutorialRunner).</summary>
    public static string[] TutorialPlayerArtifactIds { get; set; } = System.Array.Empty<string>();

    /// <summary>Player class for tutorial script mode (set by TutorialRunner).</summary>
    public static string TutorialPlayerClass { get; set; } = string.Empty;

    /// <summary>
        /// Reserved — starting vigor is always 25.
        /// </summary>
        public static MatchConfig? MatchConfig { get; set; }

        /// <summary>
        /// Chosen class from ChooseYourPath screen (e.g. "warrior").
        /// </summary>
        public static string ChosenClass { get; set; } = "";

        /// <summary>
        /// Chosen town from ChooseYourPath screen (e.g. "Emberhold").
        /// </summary>
        public static string ChosenTown { get; set; } = "";

        /// <summary>
        /// Core card IDs for the chosen class (set by ChooseYourPath screen).
        /// Read by DeckBuilderScene on _Ready.
        /// </summary>
        public static List<string>? CoreCardIds { get; set; }

        /// <summary>Path to campaign profile JSON (user:// sandbox).</summary>
        private const string CampaignProfilePath = "user://campaign.json";
        private const string ProfilesPathV2 = "user://profiles.json";

        /// <summary>
        /// All saved campaign profiles. Null when no campaigns exist.
        /// </summary>
        public static List<CampaignProfile> Profiles { get; set; } = new();

        /// <summary>The currently active profile slot, or -1 if none.</summary>
        public static int ActiveProfileSlot { get; set; } = -1;

        /// <summary>True when at least one campaign profile exists on disk.</summary>
        public static bool HasSavedCampaign => Profiles.Count > 0;

        /// <summary>Get the currently active profile, or null.</summary>
        public static CampaignProfile? ActiveProfile =>
            ActiveProfileSlot >= 0 && ActiveProfileSlot < Profiles.Count ? Profiles[ActiveProfileSlot] : null;

        /// <summary>Save ALL profiles to disk.</summary>
        public static void SaveCampaignProfile()
        {
            try
            {
                var data = new ProfilesData { Profiles = Profiles };
                string json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                using var file = Godot.FileAccess.Open(ProfilesPathV2, Godot.FileAccess.ModeFlags.Write);
                if (file != null)
                {
                    file.StoreString(json);
                    GD.Print($"[CampaignContext] {Profiles.Count} profiles saved");
                }
                else
                    GD.PrintErr("[CampaignContext] Failed to save profiles");
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"[CampaignContext] Save profiles failed: {ex.Message}");
            }
        }

        /// <summary>Load profiles from disk with migration from old format.</summary>
        public static void LoadCampaignProfile()
        {
            Profiles = new List<CampaignProfile>();
            ActiveProfileSlot = -1;

            try
            {
                // Try new v2 format first (profiles array)
                if (Godot.FileAccess.FileExists(ProfilesPathV2))
                {
                    using var file = Godot.FileAccess.Open(ProfilesPathV2, Godot.FileAccess.ModeFlags.Read);
                    if (file != null)
                    {
                        string json = file.GetAsText().Trim();
                        if (json.Length > 2)
                        {
                            var data = System.Text.Json.JsonSerializer.Deserialize<ProfilesData>(json);
                            if (data?.Profiles != null && data.Profiles.Count > 0)
                            {
                                Profiles = data.Profiles;
                                ActiveProfileSlot = 0;
                                ChosenClass = Profiles[0].ClassId;
                                ChosenTown = Profiles[0].TownName ?? "";
                                GD.Print($"[CampaignContext] {Profiles.Count} profiles loaded (v2 format)");
                                return;
                            }
                        }
                    }
                }

                // Try migration from old v1 format (single profile in campaign.json)
                if (Godot.FileAccess.FileExists(CampaignProfilePath))
                {
                    using var file = Godot.FileAccess.Open(CampaignProfilePath, Godot.FileAccess.ModeFlags.Read);
                    if (file != null)
                    {
                        string json = file.GetAsText().Trim();
                        if (json.Length > 2)
                        {
                            // Try as old CampaignProfile format
                            var old = System.Text.Json.JsonSerializer.Deserialize<CampaignProfile>(json);
                            if (old != null && !string.IsNullOrEmpty(old.ClassId))
                            {
                                old.Slot = 0;
                                old.ActiveDeckId = "";
                                old.MapProgress = "";
                                old.StoryFlags = "";
                                Profiles.Add(old);
                                ActiveProfileSlot = 0;
                                ChosenClass = old.ClassId;
                                ChosenTown = old.TownName ?? "";
                                // Save to new format immediately
                                SaveCampaignProfile();
                                GD.Print($"[CampaignContext] Migrated v1 profile to v2: {old.ClassId}");
                                return;
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"[CampaignContext] Load profiles failed: {ex.Message}");
            }

            GD.Print("[CampaignContext] No profiles found");
        }

        /// <summary>Add or update a profile in the list (max 3 slots).</summary>
        public static void AddOrUpdateProfile(string classId, string townName, int slot = -1)
        {
            if (slot >= 0 && slot < Profiles.Count)
            {
                // Update existing
                var p = Profiles[slot];
                p.ClassId = classId;
                p.TownName = townName;
                ActiveProfileSlot = slot;
            }
            else
            {
                // Add new — find first empty slot or append
                int newSlot = 0;
                for (int i = 0; i < Profiles.Count; i++)
                {
                    if (Profiles[i].Slot == -1 || Profiles[i].Slot == Profiles.Count)
                        newSlot = i;
                }
                if (Profiles.Count >= 3)
                {
                    GD.PrintErr("[CampaignContext] Max 3 profiles reached — replacing oldest");
                    Profiles.RemoveAt(0);
                }
                var profile = new CampaignProfile
                {
                    Slot = Profiles.Count,
                    ClassId = classId,
                    TownName = townName,
                    CreatedAt = DateTime.UtcNow.ToString("O"),
                    ActiveDeckId = "",
                    MapProgress = "",
                    StoryFlags = ""
                };
                Profiles.Add(profile);
                ActiveProfileSlot = Profiles.Count - 1;
            }

            // Sync static fields
            ChosenClass = classId;
            ChosenTown = townName;
            SaveCampaignProfile();
        }

        /// <summary>Delete a profile by slot.</summary>
        public static void DeleteProfile(int slot)
        {
            if (slot >= 0 && slot < Profiles.Count)
            {
                Profiles.RemoveAt(slot);
                if (ActiveProfileSlot == slot)
                {
                    ActiveProfileSlot = -1;
                    ChosenClass = "";
                    ChosenTown = "";
                }
                else if (ActiveProfileSlot > slot)
                    ActiveProfileSlot--;
                SaveCampaignProfile();
                GD.Print($"[CampaignContext] Deleted profile slot {slot}");
            }
        }

        /// <summary>Delete the active profile. Kept for backward compat.</summary>
        public static void DeleteCampaignProfile()
        {
            if (ActiveProfileSlot >= 0)
                DeleteProfile(ActiveProfileSlot);
            else
            {
                Profiles.Clear();
                ChosenClass = "";
                ChosenTown = "";
                SaveCampaignProfile();
            }
        }

        /// <summary>
        /// Serializable wrapper for the profiles array
        /// </summary>
        public class ProfilesData
        {
            public List<CampaignProfile> Profiles { get; set; } = new();
        }

        /// <summary>
        /// Serializable campaign profile data
        /// </summary>
        public class CampaignProfile
        {
            public int Slot { get; set; } = 0;
            public string ClassId { get; set; } = "";
            public string TownName { get; set; } = "";
            public string MapProgress { get; set; } = "";
            public string StoryFlags { get; set; } = "";
            public string ActiveDeckId { get; set; } = "";
            public string CreatedAt { get; set; } = "";
        }

        // ════════════════════════════════════════════════════════════════
        // DECK LIBRARY — account-wide, independent of paths
        // ════════════════════════════════════════════════════════════════

        /// <summary>Path to deck library JSON (user:// sandbox).</summary>
        private const string DeckLibraryPath = "user://decks.json";

        /// <summary>All saved decks (account-wide).</summary>
        public static List<DeckProfile> DeckLibrary { get; set; } = new();

        /// <summary>
        /// Config constant: when true, each deck is locked to the path that created it.
        /// Default false — any path can use any class-matching deck.
        /// Flip in docs/COMMS.md or campaign config when Trikzos decides.
        /// </summary>
        public static bool DecksLockedToPath { get; set; } = false;

        /// <summary>Save all decks to disk.</summary>
        public static void SaveDeckLibrary()
        {
            try
            {
                var data = new DeckLibraryData { Decks = DeckLibrary };
                string json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                using var file = Godot.FileAccess.Open(DeckLibraryPath, Godot.FileAccess.ModeFlags.Write);
                if (file != null)
                {
                    file.StoreString(json);
                    GD.Print($"[CampaignContext] {DeckLibrary.Count} decks saved");
                }
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"[CampaignContext] Save deck library failed: {ex.Message}");
            }
        }

        /// <summary>Load all decks from disk.</summary>
        public static void LoadDeckLibrary()
        {
            DeckLibrary = new List<DeckProfile>();
            try
            {
                if (!Godot.FileAccess.FileExists(DeckLibraryPath))
                {
                    GD.Print("[CampaignContext] No deck library found — starting fresh");
                    return;
                }
                using var file = Godot.FileAccess.Open(DeckLibraryPath, Godot.FileAccess.ModeFlags.Read);
                if (file == null) return;
                string json = file.GetAsText().Trim();
                if (json.Length <= 2) return;
                var data = System.Text.Json.JsonSerializer.Deserialize<DeckLibraryData>(json);
                if (data?.Decks != null)
                    DeckLibrary = data.Decks;
                GD.Print($"[CampaignContext] {DeckLibrary.Count} decks loaded");
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"[CampaignContext] Load deck library failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Add or update a deck in the library. Tags it with the class it was built under.
        /// </summary>
        public static string SaveDeck(string name, string classId, List<string> cardIds)
        {
            string deckId = $"{classId}_{name.ToLowerInvariant().Replace(" ", "_")}";
            var existing = DeckLibrary.FindIndex(d => d.DeckId == deckId);
            var deck = new DeckProfile
            {
                DeckId = deckId,
                Name = name,
                ClassId = classId.ToLowerInvariant(),
                Cards = new List<string>(cardIds)
            };
            if (existing >= 0)
                DeckLibrary[existing] = deck;
            else
                DeckLibrary.Add(deck);
            SaveDeckLibrary();
            GD.Print($"[CampaignContext] Deck saved: {deckId} ({cardIds.Count} cards)");
            return deckId;
        }

        /// <summary>Get decks whose class matches a given class ID.</summary>
        public static List<DeckProfile> GetDecksForClass(string classId)
        {
            string cid = classId.ToLowerInvariant();
            return DeckLibrary.Where(d => d.ClassId == cid).ToList();
        }

        /// <summary>Get all decks (no class filter).</summary>
        public static List<DeckProfile> GetAllDecks() => new(DeckLibrary);

        /// <summary>
        /// Serializable deck library wrapper
        /// </summary>
        public class DeckLibraryData
        {
            public List<DeckProfile> Decks { get; set; } = new();
        }

        /// <summary>
        /// A single saved deck profile.
        /// </summary>
        public class DeckProfile
        {
            public string DeckId { get; set; } = "";
            public string Name { get; set; } = "";
            public string ClassId { get; set; } = "";
            public List<string> Cards { get; set; } = new();
        }

        /// <summary>
        /// [[ Hole left by earlier refactor — keep as sentinel. ]]
        /// </summary>
        /// <summary>
        /// Load all encounter packs
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
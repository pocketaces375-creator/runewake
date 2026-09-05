using System.Collections.Generic;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Runewake.Persistence;

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

    /// <summary>Current region shown on the map. Defaults to region_01; switches to region_02 after r1_n12 cleared.</summary>
    public static string CurrentRegionId { get; set; } = "region_01";

    /// <summary>
    /// Board skin ID for the current region (e.g. "default", "ember").
    /// Set by MapScene when loading a region; read by DuelScene to apply the correct tint.
    /// Defaults to "default" when no region skin is specified.
    /// </summary>
    public static string CurrentRegionSkinId { get; set; } = "default";

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

    /// <summary>R2 variant: if true, board cards render ~10% larger (wider art share).</summary>
    public static bool R2CardScale { get; set; } = false;

    /// <summary>Test hook: auto-navigate to duel scene and capture screenshot after render.
    /// Set by Main.LoadGameData before switching to DuelScene.
    /// </summary>
    public static bool AutoCaptureScreenshot { get; set; }

    /// <summary>Input smoke test: inject touch/mouse events into a seeded duel and verify card interaction.
    /// Set by --capture=input_smoke_test CLI arg via DebugCapture.</summary>
    public static bool InputSmokeTest { get; set; }

    /// <summary>Test hook: auto-navigate to map scene, select first unlocked node, capture.
        /// Set by --capture-map CLI arg or --capture=map_test/duel_map_test.
        /// </summary>
        public static bool CaptureMapScreenshot { get; set; }

        /// <summary>Test hook: capture Region 2 map screenshot with ember skin tint.
        /// Set by --capture=map_test_r2 CLI arg.</summary>
        public static bool CaptureMapR2Screenshot { get; set; }

    /// <summary>Crash recovery test mode: triggers a test exception on the title screen, proving the recovery handler fires.</summary>
    public static bool CrashTestMode { get; set; }

    /// <summary>Test hook: auto-navigate to deck builder scene and capture screenshot.
    /// Set by --capture=deck_test CLI arg via DebugCapture.
    /// </summary>
    public static bool CaptureDeckBuilderScreenshot { get; set; }

    /// <summary>
    /// When >= 0, DeckBuilderScene uses this strata index instead of the default ALL (0).
    /// Used by DebugCapture to produce captures with a non-ALL filter selected.
    /// </summary>
    public static int CaptureOverrideStrataIdx { get; set; } = -1;

    /// <summary>Capture basename override for Reliquary screenshots (e.g. "reliquary_test_all").</summary>
    public static string CaptureReliquaryBasename { get; set; } = "reliquary_test";

    /// <summary>Test hook: phone-resolution (390x844) capture mode.
    /// Set by --capture=deck_test_phone CLI arg via DebugCapture.
    /// </summary>
    public static bool PhoneCaptureMode { get; set; }

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

    /// <summary>Test hook: capture reliquary screenshot.</summary>
    public static bool CaptureReliquaryScreenshot { get; set; }

    /// <summary>Test hook: capture slot picker screenshot.</summary>
    public static bool CaptureSlotPickerScreenshot { get; set; }

    /// <summary>Test hook: capture slot picker, then create a slot, load it, delete it, capture again.</summary>
    public static bool SlotPickerTestMode { get; set; }

    /// <summary>Test hook: capture accounts carousel screenshot.</summary>
    public static bool CaptureAccountsCarouselScreenshot { get; set; }

    /// <summary>Test hook: capture card shop screenshot.</summary>
    public static bool CaptureShopScreenshot { get; set; }

    /// <summary>Test hook: capture duel DEFEAT overlay with encounter name. Auto-ends duel as loss.</summary>
    public static bool CaptureDefeatOverlay { get; set; }

    /// <summary>Test hook: after the overlay capture, auto-navigate to map to prove the round-trip flow.</summary>
    public static bool FlowTestAfterOverlay { get; set; }

    /// <summary>Test hook: capture the map screen after a flow test to prove round-trip completed.</summary>
    public static bool CaptureFlowTestMap { get; set; }

    /// <summary>Test hook: capture settings screen.</summary>
    public static bool CaptureSettingsScreenshot { get; set; }

    /// <summary>Test hook: capture dig/encounter screen.</summary>
    public static bool CaptureDigScreenshot { get; set; }

    /// <summary>BOT-FIX-1: headless bot-duel harness — passive P0 auto-ends turns,
    /// bot plays P1; logs per-turn actions and vigor, then quits. Set by --capture=bot_duel.</summary>
    public static bool BotDuelTest { get; set; }

    /// <summary>BOT-FIX-1: bot_duel harness uses the Wayfarer tutorial encounter
    /// (30 Thorn Sprout tokens, IsTutorial=true) to mirror campaign node 1.</summary>
    public static bool BotDuelTutorialVariant { get; set; }

    /// <summary>Test hook: capture duel at wide aspect (2999×1080) instead of standard (2316×1080).
    /// Set by --capture=duel_test_wide CLI arg via DebugCapture.</summary>
    public static bool WideCaptureMode { get; set; }

    /// <summary>Test hook: simulate Android safe-area insets (bottom 48px, top 32px) for
    /// hand-position and layout checks. Set by --capture=duel_test_safe CLI arg via DebugCapture.
    /// When true, GetDisplaySafeArea() returns simulated insets instead of the headless defaults.</summary>
    public static bool DebugSafeAreaMode { get; set; }

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

        /// <summary>
        /// Chosen portrait variant for the active class ("m" or "f").
        /// Used everywhere the class portrait appears.
        /// Defaults to "m" for existing saves with no field set.
        /// </summary>
        public static string PortraitVariant { get; set; } = "m";

        /// <summary>
        /// Path to campaign profile JSON (user:// sandbox).
        /// </summary>
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

        /// <summary>
        /// Region label for a given slot, derived from its MapProgress.
        /// Returns "Region 1" as default, or a descriptive label if progress indicates further.
        /// </summary>
        public static string GetSlotRegion(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= Profiles.Count)
                return "New Path";
            var profile = Profiles[slotIndex];
            if (string.IsNullOrEmpty(profile.MapProgress))
                return "Region 1";
            return profile.MapProgress;
        }

        /// <summary>
        /// Pieces (unique cards) collected for a given slot.
        /// Returns the count from the save DB if available, or 0.
        /// </summary>
        public static int GetSlotPiecesCollected(int slotIndex)
        {
            // If this is the active slot, read from Progression directly
            if (slotIndex == ActiveProfileSlot)
                return Progression?.Collection.Count ?? 0;

            // For other slots, try to read from their DB
            int activeSaveSlot = ActiveProfileSlot;
            try
            {
                // Temporarily switch to read the other slot's data
                string dataDir = ProjectSettings.GlobalizePath("user://");
                string dbPath = System.IO.Path.Combine(dataDir, $"runewake_save_slot{slotIndex}.db");
                if (System.IO.File.Exists(dbPath))
                {
                    var repo = new SaveRepository(dbPath);
                    var state = repo.Load();
                    return state.Collection.Count;
                }
            }
            catch
            {
                // If reading fails, slot is corrupt — report 0
            }

            return 0;
        }

        /// <summary>
        /// Get class portrait texture path for a given class ID and variant.
        /// Handles migration from old class names if needed.
        /// Variant is "m" or "f"; when provided, returns the gendered variant path.
        /// When null or empty, returns the plain fallback portrait path.
        /// </summary>
        public static string GetClassPortraitPath(string classId, string? variant = null)
        {
            if (string.IsNullOrEmpty(classId))
                return "";
            string mapped = ClassIdMigration.ApplyMigration(classId);

            if (!string.IsNullOrEmpty(variant) && (variant == "m" || variant == "f"))
                return $"res://content/art/classes/{mapped}_{variant}.png";

            return $"res://content/art/classes/{mapped}.png";
        }

        /// <summary>
        /// Save ALL profiles to disk.</summary>
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
                                MigrateLegacyClassIds();
                                ChosenClass = Profiles[0].ClassId;
                                ChosenTown = Profiles[0].TownName ?? "";
                                PortraitVariant = Profiles[0].PortraitVariant;

                                // Switch SaveManager to slot 0's per-slot DB
                                try { SaveManager.SwitchSlot(0); }
                                catch (Exception ex) { GD.PrintErr($"[CampaignContext] Slot 0 save switch: {ex.Message}"); }

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
                                MigrateLegacyClassIds();
                                ChosenClass = old.ClassId;
                                ChosenTown = old.TownName ?? "";
                                PortraitVariant = old.PortraitVariant;
                                // Save to new format immediately
                                SaveCampaignProfile();

                                // Switch SaveManager to slot 0's per-slot DB
                                try { SaveManager.SwitchSlot(0); }
                                catch (Exception ex) { GD.PrintErr($"[CampaignContext] Slot 0 save switch: {ex.Message}"); }

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

        /// <summary>
        /// Migrate legacy class IDs from old saves to the new roster (TASK-ROSTER-LOCK-1).
        /// thief → rogue, ranger → astrologist.
        /// </summary>
        private static void MigrateLegacyClassIds()
        {
            foreach (var profile in Profiles)
            {
                if (string.IsNullOrEmpty(profile.ClassId)) continue;
                string mapped = ClassIdMigration.ApplyMigration(profile.ClassId);
                if (mapped != profile.ClassId)
                {
                    GD.Print($"[CampaignContext] Migrating profile class: {profile.ClassId} → {mapped}");
                    profile.ClassId = mapped;
                }
            }
            // Also migrate deck library
            foreach (var deck in DeckLibrary)
            {
                if (string.IsNullOrEmpty(deck.ClassId)) continue;
                string mapped = ClassIdMigration.ApplyMigration(deck.ClassId);
                if (mapped != deck.ClassId)
                {
                    deck.ClassId = mapped;
                }
            }
        }

        /// <summary>Add or update a profile in the list (max 3 slots).</summary>
        public static void AddOrUpdateProfile(string classId, string townName, int slot = -1, string? portraitVariant = null)
        {
            if (slot >= 0 && slot < Profiles.Count)
            {
                // Update existing — save current progression to old slot, then switch
                SaveManager.SwitchSlot(slot);
                var p = Profiles[slot];
                p.ClassId = classId;
                p.TownName = townName;
                if (!string.IsNullOrEmpty(portraitVariant))
                    p.PortraitVariant = portraitVariant;
                ActiveProfileSlot = slot;
                ChosenClass = classId;
                ChosenTown = townName;
                PortraitVariant = p.PortraitVariant;
                SaveCampaignProfile();
                GD.Print($"[CampaignContext] Updated profile slot {slot}: {classId} variant={p.PortraitVariant}");
                return;
            }

            // Add new — first empty slot or append
            int newSlot = Profiles.Count;
            if (Profiles.Count >= 3)
            {
                GD.PrintErr("[CampaignContext] Max 3 profiles reached — replacing oldest");
                Profiles.RemoveAt(0);
                newSlot = 2;
            }

            var profile = new CampaignProfile
            {
                Slot = newSlot,
                ClassId = classId,
                TownName = townName,
                CreatedAt = DateTime.UtcNow.ToString("O"),
                ActiveDeckId = "",
                MapProgress = "",
                StoryFlags = "",
                PortraitVariant = portraitVariant ?? "m"
            };

            // Switch SaveManager to this slot before adding to list
            SaveManager.SwitchSlot(newSlot);
            Profiles.Add(profile);
            ActiveProfileSlot = Profiles.Count - 1;
            ChosenClass = classId;
            ChosenTown = townName;
            PortraitVariant = profile.PortraitVariant;
            SaveCampaignProfile();
            GD.Print($"[CampaignContext] New profile slot {newSlot}: {classId} variant={profile.PortraitVariant}");
        }

        /// <summary>Delete a profile by slot.</summary>
        public static void DeleteProfile(int slot)
        {
            if (slot >= 0 && slot < Profiles.Count)
            {
                // Delete the slot's save database file if it exists
                try
                {
                    string dataDir = ProjectSettings.GlobalizePath("user://");
                    string dbPath = System.IO.Path.Combine(dataDir, $"runewake_save_slot{slot}.db");
                    if (System.IO.File.Exists(dbPath))
                    {
                        System.IO.File.Delete(dbPath);
                        GD.Print($"[CampaignContext] Deleted save DB for slot {slot}");
                    }
                    // Also clean up WAL/SHM files
                    foreach (var ext in new[] { "-wal", "-shm" })
                    {
                        string extra = dbPath + ext;
                        if (System.IO.File.Exists(extra))
                            System.IO.File.Delete(extra);
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[CampaignContext] Failed to delete DB for slot {slot}: {ex.Message}");
                }

                Profiles.RemoveAt(slot);
                if (ActiveProfileSlot == slot)
                {
                    ActiveProfileSlot = -1;
                    ChosenClass = "";
                    ChosenTown = "";

                    // If there are remaining profiles, load the first one
                    if (Profiles.Count > 0)
                    {
                        ActiveProfileSlot = 0;
                        var p = Profiles[0];
                        ChosenClass = p.ClassId;
                        ChosenTown = p.TownName ?? "";
                        PortraitVariant = p.PortraitVariant;
                        SaveManager.SwitchSlot(0);
                        GD.Print($"[CampaignContext] Switched to remaining profile slot 0: {p.ClassId}");
                    }
                }
                else if (ActiveProfileSlot > slot)
                {
                    ActiveProfileSlot--;
                }
                SaveCampaignProfile();
                GD.Print($"[CampaignContext] Deleted profile slot {slot}");
            }
        }

        /// <summary>Delete the active profile. In multi-slot mode, use DeleteProfile(slot) instead.</summary>
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
            public string PortraitVariant { get; set; } = "m";
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

        // ════════════════════════════════════════════════════════════════
        // STARTER DECKS — every class ships with a prebuilt deck so the
        // first thing a new player sees is the MAP, not the deck builder.
        // Content: res://content/decks/starter_decks.json
        // ════════════════════════════════════════════════════════════════

        /// <summary>One class's prebuilt starter deck + its first-boss signature card.</summary>
        public class StarterDeckDef
        {
            [System.Text.Json.Serialization.JsonPropertyName("class_id")]
            public string ClassId { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("deck_name")]
            public string DeckName { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("signature_card")]
            public string SignatureCard { get; set; } = "";
            [System.Text.Json.Serialization.JsonPropertyName("cards")]
            public List<string> Cards { get; set; } = new();
        }

        /// <summary>Serializable wrapper for starter_decks.json.</summary>
        public class StarterDecksData
        {
            [System.Text.Json.Serialization.JsonPropertyName("starters")]
            public List<StarterDeckDef> Starters { get; set; } = new();
        }

        /// <summary>Loaded starter decks keyed by class id (lowercase).</summary>
        public static readonly Dictionary<string, StarterDeckDef> StarterDeckIndex = new();

        /// <summary>Load starter deck content (idempotent).</summary>
        public static void LoadStarterDecks()
        {
            if (StarterDeckIndex.Count > 0) return;
            string json = Godot.FileAccess.GetFileAsString("res://content/decks/starter_decks.json");
            if (string.IsNullOrWhiteSpace(json))
            {
                GD.PrintErr("[CampaignContext] starter_decks.json missing or empty");
                return;
            }
            try
            {
                var data = System.Text.Json.JsonSerializer.Deserialize<StarterDecksData>(json);
                if (data?.Starters != null)
                    foreach (var s in data.Starters)
                        StarterDeckIndex[s.ClassId.ToLowerInvariant()] = s;
                GD.Print($"[CampaignContext] {StarterDeckIndex.Count} starter decks loaded");
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"[CampaignContext] starter_decks.json parse failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Guarantee the given class has a playable deck: create the class
        /// starter in the library if the class has no decks yet, make it the
        /// active profile's deck if none is set, load it as the duel deck,
        /// and register its cards in the collection so the Forge shows them.
        /// Safe to call every time a campaign starts or resumes.
        /// </summary>
        public static void EnsureStarterDeck(string classId)
        {
            if (string.IsNullOrEmpty(classId)) return;
            LoadStarterDecks();
            string cid = classId.ToLowerInvariant();
            StarterDeckIndex.TryGetValue(cid, out var starter);

            // Prefer the profile's own active deck when it exists and matches the class
            var classDecks = GetDecksForClass(cid);
            DeckProfile? deck = null;
            var active = ActiveProfile;
            if (active != null && !string.IsNullOrEmpty(active.ActiveDeckId))
                deck = classDecks.Find(d => d.DeckId == active.ActiveDeckId);
            if (deck == null && classDecks.Count > 0)
                deck = classDecks[0];
            if (deck == null && starter != null)
            {
                string deckId = SaveDeck(starter.DeckName, cid, starter.Cards);
                deck = DeckLibrary.Find(d => d.DeckId == deckId);
                GD.Print($"[CampaignContext] Starter deck created for {cid}: {starter.DeckName}");
            }
            if (deck == null)
            {
                GD.PrintErr($"[CampaignContext] No deck available for class '{cid}' and no starter defined");
                return;
            }

            if (active != null && active.ActiveDeckId != deck.DeckId)
            {
                active.ActiveDeckId = deck.DeckId;
                SaveCampaignProfile();
            }

            if (deck.Cards.Count >= DeckRules.MinSize)
                PlayerDeckIds = new List<string>(deck.Cards);

            // Starter cards belong to the collection so the Forge can edit freely
            foreach (var c in deck.Cards)
                if (!Progression.Collection.ContainsKey(c))
                    Progression.AddCard(c);
        }

        /// <summary>The class's signature card id (first-boss reward), or null.</summary>
        public static string? GetSignatureCardId(string classId)
        {
            if (string.IsNullOrEmpty(classId)) return null;
            LoadStarterDecks();
            return StarterDeckIndex.TryGetValue(classId.ToLowerInvariant(), out var s) && s.SignatureCard.Length > 0
                ? s.SignatureCard
                : null;
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
            "res://content/encounters/region_01_boss.json",
            "res://content/encounters/region_02_early.json",
            "res://content/encounters/region_02_mid.json",
            "res://content/encounters/region_02_late.json",
            "res://content/encounters/region_02_boss.json",
            "res://content/encounters/region_03_early.json",
            "res://content/encounters/region_03_mid.json",
            "res://content/encounters/region_03_late.json",
            "res://content/encounters/region_03_boss.json",
            "res://content/encounters/region_04_early.json",
            "res://content/encounters/region_04_mid.json",
            "res://content/encounters/region_04_late.json",
            "res://content/encounters/region_04_boss.json"
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
                "res://content/dig_sites/region_01_dig.json",
                "res://content/dig_sites/region_02_dig.json",
                "res://content/dig_sites/region_03_dig.json",
                "res://content/dig_sites/region_04_dig.json"
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

        // ════════════════════════════════════════════════
        // SOAK LOOP TEST
        // ════════════════════════════════════════════════

        /// <summary>Soak test: active flag. Set by --capture=map_loop_soak.</summary>
        public static bool SoakActive { get; set; }

        /// <summary>Soak test: which node we're on (index into SoakNodeOrder).</summary>
        public static int SoakPhase { get; set; }

        /// <summary>Soak test: ordered list of node IDs to clear in sequence.</summary>
        public static List<string> SoakNodeOrder { get; set; } = new();

        /// <summary>Soak test: log of screen names visited so far.</summary>
        public static List<string> SoakScreenLog { get; set; } = new();

        /// <summary>Soak test: max nodes to clear before quitting (0 = no limit).</summary>
        public static int SoakMaxNodes { get; set; } = 0;

        /// <summary>Soak test: phase label (save_quit, defeat_test, resume, etc).</summary>
        public static string SoakPhaseLabel { get; set; } = "";

        /// <summary>Soak test: defeat retry flag — set to true after first retry to prevent infinite loops.</summary>
        public static bool SoakDefeatHasRetried { get; set; } = false;
        public static bool LoopSmokeTest { get; set; }

        /// <summary>Soak test: if true, quit the soak after completing one defeat→retry cycle.</summary>
        public static bool SoakStopAfterRetry { get; set; } = false;

        /// <summary>Soak test: the seed being used for this run.</summary>
        public static ulong SoakSeed { get; set; }

        /// <summary>Soak test: the seed as a string for feeding to DebugSeed.</summary>
        public static string SoakSeedStr { get; set; } = "";

        /// <summary>Soak test: if true, we are doing the defeat→retry sub-test.</summary>
        public static bool SoakDefeatPhase { get; set; }

        /// <summary>Soak test: the encounter ID to use for the defeat test (strongest boss).</summary>
        public static string SoakDefeatEncounterId { get; set; } = "";

        /// <summary>Soak test: the node ID to use for the defeat test.</summary>
        public static string SoakDefeatNodeId { get; set; } = "";

        /// <summary>Soak test: if true, in the save/quit/resume sub-test.</summary>
        public static bool SoakSaveQuitPhase { get; set; }

        /// <summary>
        /// Test hook: force a specific region ID for map capture.
        /// Set by --capture=map_test_r3 or --capture=map_test_r4.
        /// When set, overrides the normal progression-based unlock chain.
        /// </summary>
        public static string ForceRegionId { get; set; } = "";

        /// <summary>
        /// Determine which region's map to show based on progression state.
        /// Chain: region_01 → r1_n12 → region_02 → region_02_n11 → region_03 → region_03_n11 → region_04
        /// Each WardenBoss node unlock advances to the next region.
        /// ForceRegionId overrides for capture tests.
        /// </summary>
        public static string GetRegionIdForMap()
        {
            if (!string.IsNullOrEmpty(ForceRegionId))
                return ForceRegionId;
            if (Progression != null && Progression.IsNodeCleared("region_03_n11"))
                return "region_04";
            if (Progression != null && Progression.IsNodeCleared("region_02_n11"))
                return "region_03";
            if (Progression != null && Progression.IsNodeCleared("r1_n12"))
                return "region_02";
            return CurrentRegionId;
        }
    };
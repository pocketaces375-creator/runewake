using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Runewake.Engine.Supabase;

namespace Runewake.Client;

/// <summary>
/// Title screen — entry point for the Runewake client.
/// Loads card packs, encounters, and save data on start.
/// </summary>
public partial class Main : Control
{
    private Button _startButton = default!;
    private Button _runeButton = default!;
    private Button _forgeButton = default!;
    private Label _statusLabel = default!;
    private bool _loading;

    public override void _Ready()
    {
        // Title label
        var title = new Label
        {
            Text = "RUNEWAKE",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0.6f
        };
        title.AddThemeFontSizeOverride("font_size", 64);
        AddChild(title);

        // Subtitle
        var subtitle = new Label
        {
            Text = "The Buried Age",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0.45f,
            AnchorBottom = 0.55f,
            AutoTranslate = false
        };
        subtitle.AddThemeFontSizeOverride("font_size", 24);
        subtitle.Modulate = new Color(0.7f, 0.7f, 0.8f);
        AddChild(subtitle);

        // Status label (loading feedback)
        _statusLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0.6f,
            AnchorBottom = 0.75f
        };
        _statusLabel.AddThemeFontSizeOverride("font_size", 16);
        _statusLabel.Modulate = new Color(0.5f, 0.5f, 0.6f);
        AddChild(_statusLabel);

        // Start Campaign button
        _startButton = new Button
        {
            Text = "Start Campaign",
            AnchorLeft = 0.35f,
            AnchorRight = 0.65f,
            AnchorTop = 0.75f,
            AnchorBottom = 0.85f,
            Disabled = true
        };
        _startButton.Pressed += OnStartCampaign;
        AddChild(_startButton);

        // Rune Page button
        var runeButton = new Button
        {
            Text = "Rune Page",
            AnchorLeft = 0.35f,
            AnchorRight = 0.65f,
            AnchorTop = 0.87f,
            AnchorBottom = 0.92f,
            Disabled = true
        };
        runeButton.Pressed += OnOpenRunePage;
        AddChild(runeButton);

        // Forge button
        var forgeButton = new Button
        {
            Text = "Rune Forge",
            AnchorLeft = 0.35f,
            AnchorRight = 0.65f,
            AnchorTop = 0.93f,
            AnchorBottom = 0.98f,
            Disabled = true
        };
        forgeButton.Pressed += OnOpenForge;
        AddChild(forgeButton);
        _forgeButton = forgeButton;

        // Begin loading
        Callable.From(LoadGameData).CallDeferred();

        // Store rune button reference for enabling after load
        _runeButton = runeButton;
    }

    private void LoadGameData()
    {
        _statusLabel.Text = "Loading content packs...";

        // Load card packs via Godot FileAccess (works in editor AND exported builds)
        var setIds = new[] { "verdant", "ember", "tide", "hollow", "dawn" };
        int loadedPacks = 0;

        foreach (var setId in setIds)
        {
            string resPath = $"res://content/cards/{setId}.json";
            try
            {
                string json = Godot.FileAccess.GetFileAsString(resPath);
                var cards = CardLoader.LoadPackFromString(json);
                CardRegistry.RegisterRange(cards);
                loadedPacks++;
                GD.Print($"Loaded {cards.Count} cards from {setId}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to load card pack {setId}: {ex.Message}");
            }
        }

        if (loadedPacks == 0)
        {
            GD.PrintErr("No card packs loaded — game cannot function.");
        }

        _statusLabel.Text = "Loading encounters...";

        // Load encounter definitions
        CampaignContext.LoadEncounters();

        _statusLabel.Text = "Loading runes...";

        // Load rune definitions
        CampaignContext.LoadRunes();

        _statusLabel.Text = "Loading dig sites...";

        // Load dig site definitions
        CampaignContext.LoadDigSites();

        _statusLabel.Text = "Loading dig tools...";

        // Load dig tool definitions
        CampaignContext.LoadDigTools();

        _statusLabel.Text = "Loading relics...";

        // Load Lost Relic definitions
        CampaignContext.LoadLostRelics();

        _statusLabel.Text = "Loading tutorial...";

        // Load tutorial step definitions
        var tutorialJson = Godot.FileAccess.GetFileAsString("res://content/tutorial/tutorial_steps.json");
        if (!string.IsNullOrEmpty(tutorialJson))
            CampaignContext.TutorialSteps = TutorialLoader.LoadStepsFromString(tutorialJson);

        _statusLabel.Text = "Loading save data...";

        // Initialize save manager — loads saved deck from persistence
        CampaignContext.SaveManager.Initialize();

        // Use the saved deck if it exists and is valid; otherwise rebuild from collection
        var savedDeck = CampaignContext.Progression.DeckCardIds;
        if (savedDeck.Count == 30)
        {
            // Validate the saved deck; if valid, use it directly
            var validation = DeckValidator.Validate(savedDeck, id => CardRegistry.Get(id));
            if (validation.IsValid)
            {
                CampaignContext.PlayerDeckIds = new List<string>(savedDeck);
            }
            else
            {
                // Saved deck is invalid — clear it and rebuild
                savedDeck.Clear();
            }
        }

        if (CampaignContext.PlayerDeckIds.Count == 0)
        {
            // Build deck from collection or give a starter deck
            if (CampaignContext.Progression.Collection.Count > 0)
            {
                var deck = new List<string>();
                foreach (var (cardId, count) in CampaignContext.Progression.Collection)
                {
                    for (int i = 0; i < count && deck.Count < 30; i++)
                        deck.Add(cardId);
                }
                while (deck.Count < 30)
                    deck.Add("vrd_c_root_warden");
                CampaignContext.PlayerDeckIds = deck;
            }
            else
            {
                // First run — starter deck
                var allCards = CardRegistry.GetAll();
                var deck = new List<string>();
                foreach (var card in allCards)
                {
                    if (deck.Count >= 30) break;
                    deck.Add(card.Id);
                }
                CampaignContext.PlayerDeckIds = deck;
                foreach (var card in allCards)
                    CampaignContext.Progression.AddCard(card.Id);
                CampaignContext.SaveManager.Save();
            }
        }

        _statusLabel.Text = "";
        _startButton.Disabled = false;
        _runeButton.Disabled = false;
        _forgeButton.Disabled = false;

        // Check if tutorial should run
        var tutorialCtrl = GetNodeOrNull<TutorialController>("/root/TutorialController");
        if (tutorialCtrl != null && tutorialCtrl.ShouldRunTutorial())
        {
            GD.Print("[Main] Tutorial needed — routing to tutorial.");
            tutorialCtrl.StartTutorial();
        }

        // Initialize Supabase sync (offline-first — no-op when not configured)
        var supabaseConfig = LoadSupabaseConfig();
        var syncManager = new SyncManager();
        AddChild(syncManager);
        syncManager.Initialize(supabaseConfig, CampaignContext.Progression!, CampaignContext.SaveManager!);
        CampaignContext.SyncManager = syncManager;
        _ = syncManager.RunStartupSync(); // fire and forget

        // Load and apply settings
        CampaignContext.Settings = CampaignContext.SaveManager!.LoadSettings();
        ApplyAudioSettings(CampaignContext.Settings);

        // Initialize telemetry service
        var telemetry = new TelemetryService();
        AddChild(telemetry);
        telemetry.Initialize(supabaseConfig, null); // accountId resolved lazily by SyncManager
        CampaignContext.Telemetry = telemetry;

        // Upload any pending crash reports (fire-and-forget, no-op if not configured)
        const string supabaseUrl = "https://placeholder.supabase.co";
        const string supabaseKey = "placeholder-anon-key";
        CrashReporter.UploadPendingReports(supabaseUrl, supabaseKey);
    }

    /// <summary>
    /// Load Supabase config from user://supabase_config.json.
    /// Returns empty config (IsConfigured=false) if file missing or unreadable.
    /// </summary>
    private static SupabaseConfig LoadSupabaseConfig()
    {
        const string path = "user://supabase_config.json";
        try
        {
            if (Godot.FileAccess.FileExists(path))
            {
                string json = Godot.FileAccess.GetFileAsString(path);
                var config = System.Text.Json.JsonSerializer.Deserialize<SupabaseConfig>(json);
                if (config != null)
                {
                    GD.Print($"[Main] Loaded Supabase config (url={config.Url})");
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Main] Failed to load Supabase config: {ex.Message}");
        }

        GD.Print("[Main] No Supabase config found — sync disabled.");
        return new SupabaseConfig();
    }

    private void OnStartCampaign()
    {
        GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
    }

    /// <summary>
    /// Apply volume settings to Godot audio buses.
    /// Safe to call even if buses don't exist (buses are created by AudioServer on startup).
    /// </summary>
    private static void ApplyAudioSettings(SettingsState s)
    {
        int masterIdx = AudioServer.GetBusIndex("Master");
        if (masterIdx >= 0)
            AudioServer.SetBusVolumeDb(masterIdx, Mathf.LinearToDb(s.MasterVolume));

        int musicIdx = AudioServer.GetBusIndex("Music");
        if (musicIdx >= 0)
            AudioServer.SetBusVolumeDb(musicIdx, Mathf.LinearToDb(s.MusicVolume));

        int sfxIdx = AudioServer.GetBusIndex("SFX");
        if (sfxIdx >= 0)
            AudioServer.SetBusVolumeDb(sfxIdx, Mathf.LinearToDb(s.SfxVolume));
    }

    private void OnOpenRunePage()
    {
        GetTree().ChangeSceneToFile("res://scenes/rune/RunePageScene.tscn");
    }

    private void OnOpenForge()
    {
        GetTree().ChangeSceneToFile("res://scenes/forge/ForgeScene.tscn");
    }
}
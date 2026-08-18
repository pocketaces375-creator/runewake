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
/// Save failures are always non-fatal: the game continues with a fresh
/// in-memory profile and displays a persistent warning on screen.
/// </summary>
public partial class Main : Control
{
    private Button _startButton = default!;
    private Button _decksButton = default!;
    private Button _runeButton = default!;
    private Button _forgeButton = default!;
    private Label _statusLabel = default!;
    private Label _saveWarningLabel = default!;
    private Button _diagButton = default!;
    private Panel? _diagPanel;
    private bool _loading;

    public override void _Ready()
    {
        AssertProjectSettings();

        // ——— Hero art background (full-bleed) ———
        var heroArt = new TextureRect
        {
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore
        };
        if (ResourceLoader.Exists("res://content/art/title/hero_art.png"))
        {
            var tex = ResourceLoader.Load<Texture2D>("res://content/art/title/hero_art.png");
            if (tex != null)
                heroArt.Texture = tex;
            else
                GD.PrintErr("[ART-MISSING] hero_art.png: ResourceLoader.Load returned null");
        }
        else
        {
            GD.PrintErr("[ART-MISSING] hero_art.png: resource does not exist at res://content/art/title/hero_art.png");
        }
        AddChild(heroArt);

        // ——— Dark scrim behind title text for readability ———
        var scrim = new ColorRect
        {
            Color = new Color(0.05f, 0.03f, 0.01f, 0.55f),  // very dark brown, 55% opaque
            AnchorLeft = 0.15f, AnchorRight = 0.85f,
            AnchorTop = 0.06f, AnchorBottom = 0.30f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(scrim);

        // ——— Title "RUNEWAKE" (large Cinzel, upper third, gold #D4B84C) ———
        var title = new Label
        {
            Text = "RUNEWAKE",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0.08f, AnchorBottom = 0.22f,
            AutoTranslate = false
        };
        ThemeTokens.ApplyHeaderFont(title, 54);
        title.Modulate = Color.FromHtml("#D4B84C"); // gold
        AddChild(title);

        // ——— Subtitle "The Buried Age" (smaller Cinzel, warm beige #C8B88A) ———
        var subtitle = new Label
        {
            Text = "The Buried Age",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0.22f, AnchorBottom = 0.28f,
            AutoTranslate = false
        };
        ThemeTokens.ApplyHeaderFont(subtitle, ThemeTokens.FontSubtitle);
        subtitle.Modulate = Color.FromHtml("#C8B88A"); // warm beige
        AddChild(subtitle);

        // ——— Status label (loading feedback, at very bottom, slightly transparent) ———
        _statusLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0.93f, AnchorBottom = 0.97f
        };
        _statusLabel.AddThemeFontSizeOverride("font_size", 12);
        _statusLabel.Modulate = new Color(0.5f, 0.45f, 0.35f, 0.4f);
        AddChild(_statusLabel);

        // ——— Stone-styled buttons (Play, Decks, Settings) ———
        var stoneNormal = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#3A3530"),
            BorderColor = Color.FromHtml("#5A5048"),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 16, ContentMarginTop = 16,
            ContentMarginRight = 16, ContentMarginBottom = 16
        };
        var stoneHover = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#4A4540"),
            BorderColor = Color.FromHtml("#C9A84C"),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 16, ContentMarginTop = 16,
            ContentMarginRight = 16, ContentMarginBottom = 16
        };
        var stonePressed = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#2A2520"),
            BorderColor = Color.FromHtml("#A08838"),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 16, ContentMarginTop = 16,
            ContentMarginRight = 16, ContentMarginBottom = 16
        };

        Button MakeStoneButton(string text)
        {
            var btn = new Button
            {
                Text = text,
                AnchorLeft = 0.40f, AnchorRight = 0.60f,
            };
            btn.AddThemeFontSizeOverride("font_size", 15);
            btn.AddThemeColorOverride("font_color", Color.FromHtml("#E8DCC8"));
            btn.AddThemeColorOverride("font_pressed_color", Color.FromHtml("#B8A878"));
            btn.AddThemeColorOverride("font_hover_color", Color.FromHtml("#F0E8D0"));
            btn.AddThemeStyleboxOverride("normal", stoneNormal);
            btn.AddThemeStyleboxOverride("hover", stoneHover);
            btn.AddThemeStyleboxOverride("pressed", stonePressed);
            btn.AddThemeStyleboxOverride("disabled", stoneNormal);
            var labelFont = ThemeTokens.GetHeaderFont(15);
            if (labelFont != null)
                btn.AddThemeFontOverride("font", labelFont);
            return btn;
        }

        // Play button → Start Campaign
        var playButton = MakeStoneButton("Play");
        playButton.AnchorTop = 0.55f;
        playButton.AnchorBottom = 0.63f;
        playButton.Pressed += OnStartCampaign;
        AddChild(playButton);
        _startButton = playButton;

        // Decks button
        var decksButton = MakeStoneButton("Decks");
        decksButton.AnchorTop = 0.65f;
        decksButton.AnchorBottom = 0.73f;
        decksButton.Pressed += OnOpenDecks;
        AddChild(decksButton);
        _decksButton = decksButton;

        // Settings button
        var settingsButton = MakeStoneButton("Settings");
        settingsButton.AnchorTop = 0.75f;
        settingsButton.AnchorBottom = 0.83f;
        settingsButton.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/settings/SettingsScene.tscn");
        AddChild(settingsButton);

        // Rune Page button (hidden — accessible from Decks/Settings screens)
        _runeButton = new Button { Visible = false, Disabled = false };
        _runeButton.Pressed += OnOpenRunePage;
        AddChild(_runeButton);

        // Forge button (hidden — accessible from Decks/Settings screens)
        _forgeButton = new Button { Visible = false, Disabled = false };
        _forgeButton.Pressed += OnOpenForge;
        AddChild(_forgeButton);

        // Persistent save warning label (hidden until/unless a save error occurs)
        _saveWarningLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0.935f, AnchorBottom = 0.97f,
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.Word
        };
        _saveWarningLabel.AddThemeFontSizeOverride("font_size", 12);
        AddChild(_saveWarningLabel);

        // Diagnostics button (debug builds only — never shown in release/exported)
        if (OS.IsDebugBuild())
        {
            _diagButton = new Button
            {
                Text = "Diag",
                Position = new Vector2(8, 8),
                Size = new Vector2(60, 32)
            };
            _diagButton.AddThemeFontSizeOverride("font_size", 10);
            _diagButton.AddThemeColorOverride("font_color", new Color(0.5f, 0.45f, 0.35f, 0.6f));
            _diagButton.AddThemeStyleboxOverride("normal", new StyleBoxFlat
            {
                BgColor = new Color(0.1f, 0.08f, 0.06f, 0.5f),
                BorderColor = new Color(0.3f, 0.25f, 0.15f, 0.3f),
                BorderWidthLeft = 1, BorderWidthTop = 1,
                BorderWidthRight = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
                CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3
            });
            _diagButton.Pressed += OnDiagnosticsPressed;
            AddChild(_diagButton);
        }

        // Begin loading
        // Check for --verify flag to enable layout verification gate
        var cmdArgs = OS.GetCmdlineArgs();
        if (cmdArgs != null)
        {
            foreach (var arg in cmdArgs)
            {
                if (arg == "--verify")
                {
                    CampaignContext.AutoCaptureScreenshot = true;
                    GD.Print("[Main] Layout verification mode enabled (--verify flag)");
                }
                else if (arg == "--capture-map")
                {
                    CampaignContext.CaptureMapScreenshot = true;
                    CampaignContext.AutoCaptureScreenshot = true;
                    GD.Print("[Main] Map capture mode enabled (--capture-map flag)");
                }
            }
        }
        Callable.From(LoadGameData).CallDeferred();
    }

    /// <summary>
    /// Verify critical project settings at launch so silent config-file
    /// regressions (viewport, orientation, stretch, main scene) are
    /// impossible to miss. Logs loudly on every mismatch.
    /// </summary>
    private static void AssertProjectSettings()
    {
        var checks = new (string Key, string Expected, string Label)[]
        {
            ("display/window/stretch/mode", "canvas_items", "Stretch mode"),
            ("display/window/stretch/aspect", "expand", "Stretch aspect"),
            ("display/window/handheld/orientation", "0", "Orientation (landscape)"),
            ("display/window/size/viewport_width", "1152", "Viewport width"),
            ("display/window/size/viewport_height", "648", "Viewport height"),
        };

        bool anyBad = false;
        foreach (var (key, expected, label) in checks)
        {
            var actual = ProjectSettings.GetSetting(key, "<unset>").ToString();
            if (actual != expected)
            {
                GD.PrintErr($"[SETTING ASSERT] {label}: expected \"{expected}\", got \"{actual}\"");
                anyBad = true;
            }
        }

        if (anyBad)
            GD.PrintErr("[SETTING ASSERT] ⚠️ One or more critical display settings are wrong or missing. UI scaling/layout will be broken on device.");
        else
            GD.Print("[SETTING ASSERT] ✅ All 4 critical display settings verified.");
    }

    private void LoadGameData()
    {
        _statusLabel.Text = "Loading content packs...";

        // Load card packs via Godot FileAccess (works in editor AND exported builds)
        var setIds = new[] { "verdant", "ember", "tide", "hollow", "dawn", "tutorial_pack" };
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

        // Load saved rune page (if any)
        CampaignContext.LoadSavedRunePage();

        _statusLabel.Text = "Loading dig sites...";

        // Load dig site definitions
        CampaignContext.LoadDigSites();

        _statusLabel.Text = "Loading dig tools...";

        // Load dig tool definitions
        CampaignContext.LoadDigTools();

        _statusLabel.Text = "Loading relics...";

        // Load Lost Relic definitions
        CampaignContext.LoadLostRelics();

        _statusLabel.Text = "Validating content IDs...";

        // Validate every content ID reference resolves to a real definition.
        // This catches silent failures like encounter IDs that don't match
        // their definitions, deck cards that don't exist in any pack, etc.
        // Same class as the deck card-ID check in EncounterLoaderTests.
        ValidateContentIds();

        _statusLabel.Text = "Loading save data...";

        // Initialize save manager — this is now always safe (returns fresh profile on error)
        CampaignContext.SaveManager.Initialize();

        // Check for save errors and show persistent warning if DB is not functional
        if (!CampaignContext.SaveManager.IsFunctional)
        {
            string warn = "⚠ Save unavailable — progress won't be saved this session";
            _saveWarningLabel.Text = warn;
            _saveWarningLabel.Modulate = new Color(1f, 0.6f, 0.1f); // orange
            _saveWarningLabel.Visible = true;
            _statusLabel.Text = "Save error — see warning below";
            _statusLabel.Modulate = new Color(1f, 0.5f, 0.2f);
        }

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
                // First run — starter deck with curated curve
                // Breakdown: ~1/3 cost 1-2, ~1/3 cost 3-4, ~1/3 cost 5+
                // Ensures playable turns 1-3
                var deck = new List<string>
                {
                    // Cost-1 plays (6 cards, 20%)
                    "vrd_c_verdant_sproutling",
                    "vrd_c_verdant_sproutling",
                    "emb_c_ember_hound",
                    "emb_c_flame_javelin",
                    "hol_c_skeletal_reaver",
                    "dwn_u_purifying_light",

                    // Cost-2 plays (8 cards, 27%)
                    "vrd_c_wildwood_stalker",
                    "vrd_c_wildwood_stalker",
                    "emb_c_cinder_runner",
                    "emb_c_cinder_runner",
                    "tid_c_tidal_scholar",
                    "hol_c_ossuary_guard",
                    "dwn_c_dawn_warder",
                    "dwn_c_dawn_warder",

                    // Cost-3 plays (8 cards, 27%)
                    "vrd_c_root_warden",
                    "vrd_u_grove_healer",
                    "emb_c_forgeguard_berserker",
                    "emb_c_forgeguard_berserker",
                    "tid_c_deep_one",
                    "hol_c_gravewrit_thrall",
                    "dwn_c_sunblade_recruit",
                    "dwn_c_sunblade_recruit",

                    // Cost-4 plays (5 cards, 17%)
                    "vrd_c_thornbark_defender",
                    "vrd_u_canopy_archer",
                    "tid_c_silt_reader",
                    "dwn_c_golden_retainer",
                    "dwn_c_dawnbreaker_charger",

                    // Cost 5+ bombs (3 cards, 10%)
                    "vrd_u_saphoof_charger",
                    "dwn_u_steadfast_bulwark",
                    "vrd_u_elder_treant",
                };
                CampaignContext.PlayerDeckIds = deck;
                // Add all cards to collection (deck cards + extras) for later deck building
                var allCards = CardRegistry.GetAll();
                foreach (var card in allCards)
                    CampaignContext.Progression.AddCard(card.Id);

                // Attempt to save the fresh profile — non-fatal if it fails
                CampaignContext.SaveManager.Save();
            }
        }

        _statusLabel.Text = "";
        _statusLabel.Modulate = new Color(0.5f, 0.5f, 0.6f);
        _startButton.Disabled = false;
        _decksButton.Disabled = false;
        _runeButton.Disabled = false;
        _forgeButton.Disabled = false;

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

        // ═══ CAPTURE HOOK (gated): auto-navigate to appropriate screen ═══
        if (CampaignContext.AutoCaptureScreenshot)
        {
            if (CampaignContext.CaptureTitleTestScreenshot)
            {
                // Capture title screen only
                GD.Print("[Main] Title test capture mode");
                var titleCapTimer = new Godot.Timer();
                titleCapTimer.OneShot = true;
                titleCapTimer.WaitTime = 1.0f;
                titleCapTimer.Timeout += () =>
                {
                    var suffix = CampaignContext.WideCaptureMode ? "_wide" : "";
                    var img = GetViewport().GetTexture().GetImage();
                    if (img != null)
                        img.SavePng($"/home/fictive/runewake/artifacts/captures/title_test{suffix}.png");
                    GD.Print($"[Main] title_test{suffix}.png saved");
                    GetTree().Quit();
                };
                AddChild(titleCapTimer);
                titleCapTimer.Start();
                return;
            }
            if (CampaignContext.CaptureTitleDeckScreenshot)
            {
                // Capture title screen with Decks button visible, then navigate to deck builder
                GD.Print("[Main] Title+Deck capture mode — will capture title screen then navigate");
                var titleCapTimer = new Godot.Timer();
                titleCapTimer.OneShot = true;
                titleCapTimer.WaitTime = 1.0f;
                titleCapTimer.Timeout += () =>
                {
                    // Capture title screen
                    var img = GetViewport().GetTexture().GetImage();
                    if (img != null)
                        img.SavePng("/home/fictive/runewake/artifacts/captures/title_deck.png");
                    GD.Print("[Main] title_deck.png saved");

                    // Write meta for title screen
                    var meta = new System.Text.StringBuilder();
                    meta.Append("{\n");
                    meta.Append("  \"capture_type\": \"title_deck\",\n");
                    meta.Append("  \"view_width\": " + (int)GetViewportRect().Size.X + ",\n");
                    meta.Append("  \"view_height\": " + (int)GetViewportRect().Size.Y + ",\n");
                    meta.Append("  \"decks_button_rect\": { \"x\": " +
                        (int)(GetViewportRect().Size.X * 0.32f) + ", \"y\": " +
                        (int)(GetViewportRect().Size.Y * 0.86f) + ", \"w\": " +
                        (int)(GetViewportRect().Size.X * 0.36f) + ", \"h\": " +
                        (int)(GetViewportRect().Size.Y * 0.05f) + " },\n");
                    meta.Append("  \"expected_deck_button_label\": \"Decks\"\n");
                    meta.Append("}\n");

                    var metaPath = "/home/fictive/runewake/artifacts/captures/title_deck.meta.json";
                    using (var writer = new System.IO.StreamWriter(metaPath))
                    {
                        writer.Write(meta.ToString());
                    }
                    GD.Print("[Main] title_deck.meta.json saved");

                    // Now navigate to deck builder for the tome capture
                    GD.Print("[Main] Navigating to deck builder for tome capture");
                    CampaignContext.CaptureDeckBuilderScreenshot = true;
                    GetTree().ChangeSceneToFile("res://scenes/deck/DeckBuilderScene.tscn");
                };
                AddChild(titleCapTimer);
                titleCapTimer.Start();
            }
            else if (CampaignContext.CaptureMapScreenshot)
            {
                // Navigate to map for map capture
                Callable.From(() => GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn")).CallDeferred();
            }
            else if (CampaignContext.CaptureDeckBuilderScreenshot)
            {
                // Navigate to deck builder for deck capture
                Callable.From(() => GetTree().ChangeSceneToFile("res://scenes/deck/DeckBuilderScene.tscn")).CallDeferred();
            }
            else
            {
                // Navigate to duel for duel capture
                Callable.From(() =>
                {
                    // If DebugCapture set a test encounter, use it; otherwise null
                    if (CampaignContext.CurrentEncounter == null || CampaignContext.CurrentEncounter.Id == "debug_test")
                    {
                        // Keep test encounter if set
                    }
                    else
                    {
                        CampaignContext.CurrentEncounter = null;
                    }
                    GetTree().ChangeSceneToFile("res://scenes/duel/DuelScene.tscn");
                }).CallDeferred();
            }
        }
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

    /// <summary>
    /// Run a diagnostic write+read-back test on the save database and display results.
    /// </summary>
    private void OnDiagnosticsPressed()
    {
        if (_diagPanel != null)
        {
            // Toggle off if already showing
            _diagPanel.QueueFree();
            _diagPanel = null;
            return;
        }

        _diagButton.Text = "Diag...";
        _diagButton.Disabled = true;

        // Run test on a short delay so the UI updates
        Callable.From(() =>
        {
            var (success, error) = CampaignContext.SaveManager.TestReadWrite();
            ShowDiagResult(success, error);
            _diagButton.Text = "Diag";
            _diagButton.Disabled = false;
        }).CallDeferred();
    }

    private void ShowDiagResult(bool success, string? error)
    {
        // Remove previous panel if any
        if (_diagPanel != null) { _diagPanel.QueueFree(); _diagPanel = null; }

        var panel = new Panel();
        panel.AnchorLeft = 0.05f;
        panel.AnchorRight = 0.95f;
        panel.AnchorTop = 0.1f;
        panel.AnchorBottom = 0.9f;

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.06f, 0.06f, 0.1f, 0.97f);
        style.BorderColor = new Color(0.3f, 0.3f, 0.5f);
        style.BorderWidthLeft = 2;
        style.BorderWidthTop = 2;
        style.BorderWidthRight = 2;
        style.BorderWidthBottom = 2;
        panel.AddThemeStyleboxOverride("panel", style);

        AddChild(panel);
        _diagPanel = panel;

        // Scroll container for long error messages
        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        scroll.AnchorLeft = 0.03f;
        scroll.AnchorRight = 0.97f;
        scroll.AnchorTop = 0.03f;
        scroll.AnchorBottom = 0.85f;
        scroll.SizeFlagsVertical = (Control.SizeFlags)7; // expand + fill
        panel.AddChild(scroll);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vbox.SizeFlagsHorizontal = (Control.SizeFlags)3; // expand
        scroll.AddChild(vbox);

        // Title
        var title = new Label
        {
            Text = "Save Diagnostics",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutoTranslate = false
        };
        title.AddThemeFontSizeOverride("font_size", 20);
        vbox.AddChild(title);

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

        // Result line
        var resultLabel = new Label
        {
            Text = success ? "✅ PASS — Database read/write OK" : "❌ FAIL — Database error",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutoTranslate = false
        };
        resultLabel.AddThemeFontSizeOverride("font_size", 16);
        resultLabel.Modulate = success ? new Color(0.3f, 1f, 0.3f) : new Color(1f, 0.3f, 0.3f);
        vbox.AddChild(resultLabel);

        // Save status summary
        var statusLabel = new Label
        {
            Text = $"Save system: {(CampaignContext.SaveManager.IsFunctional ? "functional" : "NOT functional")}",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutoTranslate = false
        };
        statusLabel.AddThemeFontSizeOverride("font_size", 13);
        statusLabel.Modulate = CampaignContext.SaveManager.IsFunctional
            ? new Color(0.5f, 0.8f, 0.5f) : new Color(1f, 0.6f, 0.2f);
        vbox.AddChild(statusLabel);

        // Last error from load, if any
        if (CampaignContext.SaveManager.LastError != null)
        {
            var loadErrLabel = new Label
            {
                Text = $"Load error: {CampaignContext.SaveManager.LastError}",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutoTranslate = false,
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            loadErrLabel.AddThemeFontSizeOverride("font_size", 12);
            loadErrLabel.Modulate = new Color(1f, 0.7f, 0.3f);
            vbox.AddChild(loadErrLabel);
        }

        // Error details
        if (error != null)
        {
            vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

            var errTitle = new Label
            {
                Text = "Exception Details:",
                AutoTranslate = false
            };
            errTitle.AddThemeFontSizeOverride("font_size", 13);
            vbox.AddChild(errTitle);

            var errBox = new Label
            {
                Text = error,
                AutoTranslate = false,
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            errBox.AddThemeFontSizeOverride("font_size", 11);
            errBox.Modulate = new Color(0.8f, 0.5f, 0.5f);
            vbox.AddChild(errBox);
        }

        // Path details
        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        var pathLabel = new Label
        {
            Text = "DB path: user://runewake_save.db",
            AutoTranslate = false,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        pathLabel.AddThemeFontSizeOverride("font_size", 11);
        pathLabel.Modulate = new Color(0.5f, 0.5f, 0.6f);
        vbox.AddChild(pathLabel);

        // Close button at bottom of panel
        var closeBtn = new Button
        {
            Text = "Close",
            AnchorLeft = 0.3f,
            AnchorRight = 0.7f,
            AnchorTop = 0.88f,
            AnchorBottom = 0.97f
        };
        closeBtn.Pressed += () =>
        {
            panel.QueueFree();
            _diagPanel = null;
        };
        panel.AddChild(closeBtn);
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

    private void OnOpenDecks()
    {
        GetTree().ChangeSceneToFile("res://scenes/deck/DeckBuilderScene.tscn");
    }

    private void OnOpenForge()
    {
        GetTree().ChangeSceneToFile("res://scenes/forge/ForgeScene.tscn");
    }

    // ═══════════════════════════════════════════════════
    // Content ID validation — runs at startup to catch
    // silent failures from broken cross-references.
    // ═══════════════════════════════════════════════════

    private void ValidateContentIds()
    {
        int errors = 0;

        // 1. Every encounter deck card ID must resolve in CardRegistry
        foreach (var enc in CampaignContext.EncounterIndex.Values)
        {
            foreach (var cardId in enc.Deck)
            {
                if (CardRegistry.Get(cardId) == null)
                {
                    GD.PrintErr($"[ContentValidation] ENCOUNTER '{enc.Id}' references unknown card '{cardId}'");
                    errors++;
                }
            }
        }

        // 2. Every Lost Relic encounter_id must resolve to a real encounter
        foreach (var relic in CampaignContext.LostRelicIndex.Values)
        {
            if (!CampaignContext.EncounterIndex.ContainsKey(relic.EncounterId))
            {
                GD.PrintErr($"[ContentValidation] RELIC '{relic.Name}' references unknown encounter '{relic.EncounterId}'");
                errors++;
            }
        }

        // 3. Every map node encounter must resolve to a real encounter or dig site
        string mapJson = Godot.FileAccess.GetFileAsString("res://content/map/region_01.json");
        var mapRegion = MapLoader.LoadRegionFromString(mapJson);
        if (mapRegion != null)
        {
            foreach (var node in mapRegion.Nodes)
            {
                if (node.Encounter != null)
                {
                    if (CampaignContext.EncounterIndex.ContainsKey(node.Encounter))
                        continue;
                    if (CampaignContext.DigSiteIndex.ContainsKey(node.Encounter))
                        continue;
                    GD.PrintErr($"[ContentValidation] MAP NODE '{node.Id}' references unknown encounter/dig site '{node.Encounter}'");
                    errors++;
                }
            }
        }

        // 4. Every dig site headline reward relic reference should resolve
        foreach (var site in CampaignContext.DigSiteIndex.Values)
        {
            if (site.HeadlineReward != null && site.HeadlineReward.StartsWith("relic:"))
            {
                string relicId = site.HeadlineReward.Replace("relic:", "");
                if (!CampaignContext.LostRelicIndex.Values.Any(r => r.CardId == relicId))
                {
                    GD.Print($"[ContentValidation] DIG SITE '{site.Id}' headline reward '{site.HeadlineReward}' — not in relic index (may be intentional)");
                }
            }
        }

        if (errors > 0)
        {
            GD.PrintErr($"[ContentValidation] {errors} content ID error(s) found. See above for details.");
            _statusLabel.Text = $"⚠ {errors} content error(s) — check logs";
            _statusLabel.Modulate = new Color(1f, 0.5f, 0.2f);
        }
        else
        {
            GD.Print("[ContentValidation] All content IDs resolve correctly.");
        }
    }
}
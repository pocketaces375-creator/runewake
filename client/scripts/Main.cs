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
    private Label _statusLabel = default!;
    private Label _saveWarningLabel = default!;
    private Button _decksButton = default!;
    private Button _runeButton = default!;
    private Button _forgeButton = default!;
    private Button _diagButton = default!;
    private Control? _slotPickerContainer;
    private Control? _diagPanel;
    private bool _loading;

    public override void _Ready()
    {
        // ——— Force landscape orientation at runtime (mobile fallback) ———
        // This hard-locks landscape even if the AndroidManifest merge doesn't
        // apply the project setting correctly on some devices.
        try
        {
            if (DisplayServer.ScreenGetOrientation() != DisplayServer.ScreenOrientation.Landscape)
                DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.Landscape);
        }
        catch
        {
            // Non-mobile platforms may not support runtime orientation change;
            // that's fine — the project setting handles desktop correctly.
        }

        AssertProjectSettings();

        // ——— Hero art background (full-bleed) ———
        var heroArt = new TextureRect
        {
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
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
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
        };
        ThemeTokens.ApplyHeaderFont(title, ThemeTokens.FontTitleScreen);
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
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
        };
        ThemeTokens.ApplyBodyFont(subtitle, ThemeTokens.FontSecondary);
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
 CustomMinimumSize = new Vector2(0, ThemeTokens.MinButtonHeight),
 };
            btn.AddThemeFontSizeOverride("font_size", ThemeTokens.FontButtonPrimary);
            btn.AddThemeColorOverride("font_color", Color.FromHtml("#E8DCC8"));
            btn.AddThemeColorOverride("font_pressed_color", Color.FromHtml("#B8A878"));
            btn.AddThemeColorOverride("font_hover_color", Color.FromHtml("#F0E8D0"));
            btn.AddThemeStyleboxOverride("normal", stoneNormal);
            btn.AddThemeStyleboxOverride("hover", stoneHover);
            btn.AddThemeStyleboxOverride("pressed", stonePressed);
            btn.AddThemeStyleboxOverride("disabled", stoneNormal);
            var labelFont = ThemeTokens.GetButtonFont(ThemeTokens.FontButtonPrimary);
            if (labelFont != null)
                btn.AddThemeFontOverride("font", labelFont);
            return btn;
        }

        // ═══ Build slot picker (3 campaign slots) ═══
        BuildSlotPicker();

        // ── Decks button ──
        var decksButton = MakeStoneButton("Decks");
        decksButton.AnchorTop = 0.65f;
        decksButton.AnchorBottom = 0.73f;
        decksButton.Pressed += OnOpenDecks;
        AddChild(decksButton);
        _decksButton = decksButton;

        // Reliquary button
        var reliquaryButton = MakeStoneButton("Reliquary");
        reliquaryButton.AnchorTop = 0.75f;
        reliquaryButton.AnchorBottom = 0.83f;
        reliquaryButton.Pressed += () => {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            GetTree().ChangeSceneToFile("res://scenes/reliquary/ReliquaryScene.tscn");
        };
        AddChild(reliquaryButton);

        // Settings button (bottom row)
        var settingsButton = MakeStoneButton("Settings");
        settingsButton.AnchorTop = 0.85f;
        settingsButton.AnchorBottom = 0.93f;
        settingsButton.Pressed += () => {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            GetTree().ChangeSceneToFile("res://scenes/settings/SettingsScene.tscn");
        };
        AddChild(settingsButton);

        // Rune Page button (hidden — accessible from Decks/Settings screens)
        _runeButton = new Button { Visible = false, Disabled = false };
        _runeButton.Pressed += OnOpenRunePage;
        AddChild(_runeButton);

        // Forge button (hidden — accessible from Decks/Settings screens)
        _forgeButton = new Button { Visible = false, Disabled = false };
        _forgeButton.Pressed += OnOpenForge;
        AddChild(_forgeButton);

        // ── Campaign profile (v2 save system: 3 slots) ──
        CampaignContext.LoadCampaignProfile();
        CampaignContext.LoadDeckLibrary();

        // ═══ SAVE LOAD: synchronous, before any deferred work ═══
        // (must happen AFTER LoadCampaignProfile which sets up per-slot SaveManager)
        // Critical: the save MUST be loaded before the first scene reads it.
        // The race condition (deferred LoadGameData leaving IsLoaded=false when
        // buttons are interactive) is the suspected root cause of skipped
        // deck-select on some devices. Initialize is now called here,
        // synchronously in _Ready, before any CallDeferred.
        CampaignContext.SaveManager.Initialize();

        // Refresh slot picker to show current state
        BuildSlotPicker();

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
            ("display/window/stretch/aspect", "keep", "Stretch aspect"),
            ("display/window/handheld/orientation", "0", "Orientation (landscape)"),
            ("display/window/size/viewport_width", "2316", "Viewport width"),
            ("display/window/size/viewport_height", "1080", "Viewport height"),
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

        _statusLabel.Text = "Loading artifacts...";

        // Load launch artifacts + variant files
        var artifactsDir = "res://content/artifacts";
        string launchArtifactPath = $"{artifactsDir}/launch_artifacts.json";
        try
        {
            string json = Godot.FileAccess.GetFileAsString(launchArtifactPath);
            if (!string.IsNullOrEmpty(json))
            {
                int count = ArtifactLoader.LoadFromString(json);
                GD.Print($"Loaded {count} artifacts from launch_artifacts.json");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to load launch_artifacts.json: {ex.Message}");
        }

        // Load all variant files
        string variantsDir = $"{artifactsDir}/variants";
        try
        {
            var dir = Godot.DirAccess.Open(variantsDir);
            if (dir != null)
            {
                dir.ListDirBegin();
                string fileName;
                int variantCount = 0;
                while ((fileName = dir.GetNext()) != "")
                {
                    if (!fileName.EndsWith(".json")) continue;
                    string variantPath = $"{variantsDir}/{fileName}";
                    string variantJson = Godot.FileAccess.GetFileAsString(variantPath);
                    if (!string.IsNullOrEmpty(variantJson))
                    {
                        variantCount += ArtifactLoader.LoadFromString(variantJson);
                    }
                }
                dir.ListDirEnd();
                if (variantCount > 0)
                    GD.Print($"Loaded {variantCount} artifacts from {variantCount} variant file(s)");
            }
            else
            {
                GD.Print("No artifacts/variants directory — skipping variant artifacts");
            }
        }
        catch (Exception ex)
        {
            GD.Print($"No variant artifact files: {ex.Message}");
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

        // Save already initialized synchronously in _Ready() — no second call needed.
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

        // ═══ INTRO SPLASH (first-launch only, skipped during capture mode) ═══
        // Show the story intro page full-bleed on top of everything. Tap/key
        // dismisses instantly, marks seen, and saves so it never shows again.
        if (!CampaignContext.Settings.IntroSeen && !CampaignContext.AutoCaptureScreenshot)
        {
            var introOverlay = new TextureRect
            {
                Texture = GD.Load<Texture2D>("res://content/art/title/intro_splash.png"),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                AnchorsPreset = (int)LayoutPreset.FullRect,
                MouseFilter = MouseFilterEnum.Stop
            };
            AddChild(introOverlay);
            // Raise to top so it's above all title screen UI
            MoveChild(introOverlay, GetChildCount() - 1);

            introOverlay.GuiInput += (InputEvent @event) =>
            {
                if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
                    || @event is InputEventKey { Pressed: true, KeyLabel: Key.Space or Key.Enter })
                {
                    RemoveChild(introOverlay);
                    introOverlay.QueueFree();
                    CampaignContext.Settings.IntroSeen = true;
                    CampaignContext.SaveManager!.SaveSettings(CampaignContext.Settings);
                    GD.Print("[Main] Intro dismissed — marking seen");
                }
            };
        }

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
            // Slot picker test runs on the title screen itself
            if (CampaignContext.SlotPickerTestMode)
            {
                GD.Print("[Main] Slot picker test mode — running slot create/load/delete test");
                // Use deferred call so the title screen has rendered
                Callable.From(RunSlotPickerTest).CallDeferred();
                return;
            }
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
                        img.SavePng($"{ProjectPaths.Artifacts}/captures/title_test{suffix}.png");
                    DebugCapture.WriteLayoutJson(this, $"title_test{suffix}");
                    GD.Print($"[Main] title_test{suffix}.png saved");

                    // TASK-UI-LINT-1: Dump layout JSON
                    DebugCapture.DumpLayoutJSON($"title_test{suffix}", this);
                    GetTree().Quit();
                };
                AddChild(titleCapTimer);
                titleCapTimer.Start();
                return;
            }
            if (CampaignContext.CrashTestMode)
            {
                // Crash recovery test: show title, trigger crash, capture the recovery screen
                GD.Print("[Main] Crash test capture mode");
                var crashCapTimer = new Godot.Timer();
                crashCapTimer.OneShot = true;
                crashCapTimer.WaitTime = 1.0f;
                crashCapTimer.Timeout += () =>
                {
                    GD.Print("[Main] Triggering test crash for capture...");
                    // The crash will be caught by CrashReporter which shows the recovery overlay.
                    // Schedule the capture 1.5s after the crash so the overlay has rendered.
                    var captureTimer = new Godot.Timer();
                    captureTimer.OneShot = true;
                    captureTimer.WaitTime = 1.5f;
                    captureTimer.Timeout += () =>
                    {
                        var img = GetViewport().GetTexture().GetImage();
                        if (img != null)
                            img.SavePng(ProjectPaths.Artifacts + "/captures/crash_test.png");
                        DebugCapture.WriteLayoutJson(this, "crash_test");
                        GD.Print("[Main] crash_test.png saved");
                        DebugCapture.DumpLayoutJSON("crash_test", this);
                        GetTree().Quit();
                    };
                    AddChild(captureTimer);
                    captureTimer.Start();

                    // Trigger crash through the recovery handler directly
                    GD.Print("[Main] Triggering test crash via CrashReporter.TriggerCrashRecovery...");
                    CrashReporter.TriggerCrashRecovery(new InvalidOperationException(
                        "TEST CRASH from crash_test capture mode — the recovery overlay should be visible now."));
                };
                AddChild(crashCapTimer);
                crashCapTimer.Start();
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
                        img.SavePng(ProjectPaths.Artifacts + "/captures/title_deck.png");
                    DebugCapture.WriteLayoutJson(this, "title_deck");
                    GD.Print("[Main] title_deck.png saved");

                    // TASK-UI-LINT-1: Dump layout JSON
                    DebugCapture.DumpLayoutJSON("title_deck", this);

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

                    var metaPath = ProjectPaths.Artifacts + "/captures/title_deck.meta.json";
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
            else if (CampaignContext.CaptureChoosePathScreenshot)
            {
                // Navigate to choose your path for carousel capture
                GD.Print("[Main] Navigating to ChooseYourPath for carousel capture");
                Callable.From(() =>
                {
                    GetTree().ChangeSceneToFile("res://scenes/choose_path/ChooseYourPathScene.tscn");
                }).CallDeferred();
            }
            else if (CampaignContext.CaptureSettingsScreenshot)
            {
                // Navigate to settings screen
                GD.Print("[Main] Navigating to Settings for capture");
                Callable.From(() =>
                {
                    GetTree().ChangeSceneToFile("res://scenes/settings/SettingsScene.tscn");
                }).CallDeferred();
            }
            else if (CampaignContext.CaptureDigScreenshot)
            {
                // Navigate to dig scene
                GD.Print("[Main] Navigating to Dig scene for capture");
                Callable.From(() =>
                {
                    GetTree().ChangeSceneToFile("res://scenes/dig/DigScene.tscn");
                }).CallDeferred();
            }
            else if (CampaignContext.CaptureReliquaryScreenshot)
            {
                // Navigate to reliquary for collection browser capture — direct call, not deferred
                GD.Print("[Main] Navigating to Reliquary for capture");
                GetTree().ChangeSceneToFile("res://scenes/reliquary/ReliquaryScene.tscn");
            }
            else if (CampaignContext.CaptureShopScreenshot)
            {
                // Navigate to card shop for rotating shop capture
                GD.Print("[Main] Navigating to Card Shop for capture");
                CardShopScene.SetUpShopTest();
                GetTree().ChangeSceneToFile("res://scenes/shop/CardShopScene.tscn");
            }
            else if (CampaignContext.SoakActive)
            {
                // Soak loop mode — route through normal campaign flow
                GD.Print("[Main] Soak loop mode active — starting campaign");
                if (CampaignContext.HasSavedCampaign)
                {
                    Callable.From(() => GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn")).CallDeferred();
                }
                else
                {
                    Callable.From(() => GetTree().ChangeSceneToFile("res://scenes/choose_path/ChooseYourPathScene.tscn")).CallDeferred();
                }
            }
            else
            {
                // Navigate to duel for duel capture (or tutorial script)
                GD.Print($"[Main] Duel navigation mode (TutorialScriptId={(string.IsNullOrEmpty(CampaignContext.TutorialScriptId) ? "null" : CampaignContext.TutorialScriptId)})");
                // If DebugCapture set a test encounter, use it; otherwise null
                bool isTutorialScript = !string.IsNullOrEmpty(CampaignContext.TutorialScriptId);
                if (!isTutorialScript && CampaignContext.CurrentEncounter is { Id: not "debug_test" })
                {
                    CampaignContext.CurrentEncounter = null;
                }
                // For tutorial script mode, the encounter was already set up by
                // DebugCapture.SetUpTutorialEncounter / TutorialRunner.SetupEncounter.
                // Use direct call (not CallDeferred) in headless so the scene change
                // fires immediately without waiting for the next idle frame.
                GetTree().ChangeSceneToFile("res://scenes/duel/DuelScene.tscn");
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
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
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
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
        };
        title.AddThemeFontSizeOverride("font_size", 20);
        vbox.AddChild(title);

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

        // Result line
        var resultLabel = new Label
        {
            Text = success ? "✅ PASS — Database read/write OK" : "❌ FAIL — Database error",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
        };
        resultLabel.AddThemeFontSizeOverride("font_size", 16);
        resultLabel.Modulate = success ? new Color(0.3f, 1f, 0.3f) : new Color(1f, 0.3f, 0.3f);
        vbox.AddChild(resultLabel);

        // Save status summary
        var statusLabel = new Label
        {
            Text = $"Save system: {(CampaignContext.SaveManager.IsFunctional ? "functional" : "NOT functional")}",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
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
                AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
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
                AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
            };
            errTitle.AddThemeFontSizeOverride("font_size", 13);
            vbox.AddChild(errTitle);

            var errBox = new Label
            {
                Text = error,
                AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
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
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        pathLabel.AddThemeFontSizeOverride("font_size", 11);
        pathLabel.Modulate = new Color(0.5f, 0.5f, 0.6f);
        vbox.AddChild(pathLabel);

        // Action button row at bottom of panel
        var buttonRow = new HBoxContainer
        {
            AnchorLeft = 0.2f,
            AnchorRight = 0.8f,
            AnchorTop = 0.88f,
            AnchorBottom = 0.97f
        };
        panel.AddChild(buttonRow);

        // Close button
        var closeBtn = new Button
        {
            Text = "Close",
            SizeFlagsHorizontal = Control.SizeFlags.Expand | Control.SizeFlags.Fill
        };
        closeBtn.Pressed += () =>
        {
            panel.QueueFree();
            _diagPanel = null;
        };
        buttonRow.AddChild(closeBtn);

        // Test Crash button (debug builds only — triggers the crash handler)
        if (OS.IsDebugBuild())
        {
            var spacer = new Control { CustomMinimumSize = new Vector2(16, 0) };
            buttonRow.AddChild(spacer);

            var crashBtn = new Button
            {
                Text = "Test Crash",
                SizeFlagsHorizontal = Control.SizeFlags.Expand | Control.SizeFlags.Fill
            };
            crashBtn.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.2f));
            crashBtn.Pressed += () =>
            {
                panel.QueueFree();
                _diagPanel = null;
                // Short delay so the panel is removed before the crash
                var crashTimer = new Godot.Timer();
                crashTimer.OneShot = true;
                crashTimer.WaitTime = 0.3f;
                crashTimer.Timeout += () =>
                {
                    GD.Print("[Main] Diagnostics: triggering crash via CrashReporter.TriggerCrashRecovery...");
                    CrashReporter.TriggerCrashRecovery(new InvalidOperationException(
                        "TEST CRASH from diagnostics panel — this is intentional."));
                };
                AddChild(crashTimer);
                crashTimer.Start();
            };
            buttonRow.AddChild(crashBtn);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // SLOT PICKER — 3 campaign slots on the title screen
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Build or rebuild the 3 campaign slot cards in the middle of the screen.
    /// Removes previous container if one exists.
    /// </summary>
    private void BuildSlotPicker()
    {
        // Remove previous container
        if (_slotPickerContainer != null && IsInstanceValid(_slotPickerContainer))
            _slotPickerContainer.QueueFree();

        _slotPickerContainer = new HBoxContainer
        {
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = 0.38f, AnchorBottom = 0.63f,
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            SizeFlagsVertical = Control.SizeFlags.Fill,
            MouseFilter = MouseFilterEnum.Stop
        };
        AddChild(_slotPickerContainer);

        var profiles = CampaignContext.Profiles;

        for (int i = 0; i < 3; i++)
        {
            int slotIdx = i;
            bool occupied = i < profiles.Count && !string.IsNullOrEmpty(profiles[i].ClassId);

            var slotCard = new PanelContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.Expand | Control.SizeFlags.Fill,
                SizeFlagsVertical = Control.SizeFlags.Fill,
                MouseFilter = MouseFilterEnum.Stop,
                CustomMinimumSize = new Vector2(0, 180)
            };

            var slotStyle = new StyleBoxFlat
            {
                BgColor = occupied
                    ? Color.FromHtml("#3A3530")
                    : Color.FromHtml("#2A2520"),
                BorderColor = occupied
                    ? Color.FromHtml("#6A6048")
                    : Color.FromHtml("#4A4038"),
                BorderWidthLeft = 1, BorderWidthTop = 1,
                BorderWidthRight = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
                CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
                ContentMarginLeft = 10, ContentMarginTop = 8,
                ContentMarginRight = 10, ContentMarginBottom = 8
            };
            slotCard.AddThemeStyleboxOverride("panel", slotStyle);

            var vbox = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.Fill,
                SizeFlagsVertical = Control.SizeFlags.Fill
            };
            slotCard.AddChild(vbox);

            if (occupied)
            {
                // ── Occupied slot ──
                var profile = profiles[i];
                string classId = profile.ClassId;
                string className = char.ToUpper(classId[0]) + classId.Substring(1);

                // Class portrait — use profile's portrait variant
                string portraitPath = CampaignContext.GetClassPortraitPath(classId, profile.PortraitVariant);
                if (ResourceLoader.Exists(portraitPath))
                {
                    var portrait = new TextureRect
                    {
                        Texture = ResourceLoader.Load<Texture2D>(portraitPath),
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                        CustomMinimumSize = new Vector2(48, 48),
                        SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                        ExpandMode = TextureRect.ExpandModeEnum.FitWidth
                    };
                    vbox.AddChild(portrait);
                }

                // Class name
                var nameLabel = new Label
                {
                    Text = className,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
                };
                nameLabel.AddThemeFontSizeOverride("font_size", 14);
                nameLabel.Modulate = Color.FromHtml("#E8DCC8");
                vbox.AddChild(nameLabel);

                // Region
                string region = CampaignContext.GetSlotRegion(i);
                var regionLabel = new Label
                {
                    Text = region,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
                };
                regionLabel.AddThemeFontSizeOverride("font_size", 11);
                regionLabel.Modulate = new Color(0.7f, 0.65f, 0.55f);
                vbox.AddChild(regionLabel);

                // Pieces collected
                int pieces = CampaignContext.GetSlotPiecesCollected(i);
                var piecesLabel = new Label
                {
                    Text = $"Cards: {pieces}",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
                };
                piecesLabel.AddThemeFontSizeOverride("font_size", 11);
                piecesLabel.Modulate = new Color(0.65f, 0.6f, 0.5f);
                vbox.AddChild(piecesLabel);

                // Spacer
                var spacer = new Control { SizeFlagsVertical = Control.SizeFlags.Expand };
                vbox.AddChild(spacer);

                // Buttons row
                var btnHbox = new HBoxContainer
                {
                    SizeFlagsHorizontal = Control.SizeFlags.Fill,
                    Alignment = BoxContainer.AlignmentMode.Center
                };
                vbox.AddChild(btnHbox);

                // Continue button
                var continueBtn = new Button
                {
                    Text = "Continue",
                    CustomMinimumSize = new Vector2(90, 28),
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
                };
                continueBtn.AddThemeFontSizeOverride("font_size", 12);
                continueBtn.AddThemeColorOverride("font_color", Color.FromHtml("#D4B84C"));
                continueBtn.AddThemeColorOverride("font_hover_color", Color.FromHtml("#F0E8D0"));
                var contNormal = new StyleBoxFlat
                {
                    BgColor = new Color(0.3f, 0.25f, 0.1f, 0.5f),
                    BorderColor = Color.FromHtml("#C9A84C"),
                    BorderWidthLeft = 1, BorderWidthTop = 1,
                    BorderWidthRight = 1, BorderWidthBottom = 1,
                    CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
                    CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
                };
                continueBtn.AddThemeStyleboxOverride("normal", contNormal);
                continueBtn.Pressed += () => OnSlotContinueClicked(slotIdx);
                btnHbox.AddChild(continueBtn);

                // Delete button
                var deleteBtn = new Button
                {
                    Text = "Delete",
                    CustomMinimumSize = new Vector2(90, 28),
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
                };
                deleteBtn.AddThemeFontSizeOverride("font_size", 12);
                deleteBtn.AddThemeColorOverride("font_color", new Color(0.8f, 0.3f, 0.2f));
                deleteBtn.AddThemeColorOverride("font_hover_color", new Color(1f, 0.4f, 0.3f));
                var delNormal = new StyleBoxFlat
                {
                    BgColor = new Color(0.3f, 0.1f, 0.05f, 0.3f),
                    BorderColor = new Color(0.6f, 0.2f, 0.1f, 0.4f),
                    BorderWidthLeft = 1, BorderWidthTop = 1,
                    BorderWidthRight = 1, BorderWidthBottom = 1,
                    CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
                    CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
                };
                deleteBtn.AddThemeStyleboxOverride("normal", delNormal);
                int capturedSlot = slotIdx;
                deleteBtn.Pressed += () => OnSlotDeleteClicked(capturedSlot);
                btnHbox.AddChild(deleteBtn);

                // Overwrite — New Campaign on top of existing
                var newOverwriteBtn = new Button
                {
                    Text = "New",
                    CustomMinimumSize = new Vector2(90, 28),
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
                };
                newOverwriteBtn.AddThemeFontSizeOverride("font_size", 11);
                newOverwriteBtn.AddThemeColorOverride("font_color", new Color(0.6f, 0.55f, 0.45f, 0.7f));
                newOverwriteBtn.AddThemeColorOverride("font_hover_color", new Color(0.9f, 0.8f, 0.6f));
                var newNormal = new StyleBoxFlat
                {
                    BgColor = new Color(0.2f, 0.18f, 0.15f, 0.4f),
                    BorderColor = new Color(0.4f, 0.35f, 0.25f, 0.3f),
                    BorderWidthLeft = 1, BorderWidthTop = 1,
                    BorderWidthRight = 1, BorderWidthBottom = 1,
                    CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
                    CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3
                };
                newOverwriteBtn.AddThemeStyleboxOverride("normal", newNormal);
                newOverwriteBtn.Pressed += () => OnSlotNewClicked(slotIdx, overwrite: true);
                btnHbox.AddChild(newOverwriteBtn);
            }
            else
            {
                // ── Empty slot — "New Campaign" ──
                var emptySpacer = new Control { SizeFlagsVertical = Control.SizeFlags.Expand };
                vbox.AddChild(emptySpacer);

                var emptyLabel = new Label
                {
                    Text = "Empty",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    SizeFlagsHorizontal = Control.SizeFlags.Fill,
                    AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
                };
                emptyLabel.AddThemeFontSizeOverride("font_size", 13);
                emptyLabel.Modulate = new Color(0.5f, 0.45f, 0.35f, 0.5f);
                vbox.AddChild(emptyLabel);

                var newBtn = new Button
                {
                    Text = "New Campaign",
                    CustomMinimumSize = new Vector2(130, 32),
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
                };
                newBtn.AddThemeFontSizeOverride("font_size", 12);
                newBtn.AddThemeColorOverride("font_color", Color.FromHtml("#D4B84C"));
                newBtn.AddThemeColorOverride("font_hover_color", Color.FromHtml("#F0E8D0"));
                var newBtnNormal = new StyleBoxFlat
                {
                    BgColor = new Color(0.3f, 0.25f, 0.1f, 0.5f),
                    BorderColor = Color.FromHtml("#C9A84C"),
                    BorderWidthLeft = 1, BorderWidthTop = 1,
                    BorderWidthRight = 1, BorderWidthBottom = 1,
                    CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
                    CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
                };
                newBtn.AddThemeStyleboxOverride("normal", newBtnNormal);
                newBtn.Pressed += () => OnSlotNewClicked(slotIdx, overwrite: false);
                vbox.AddChild(newBtn);

                var emptySpacer2 = new Control { SizeFlagsVertical = Control.SizeFlags.Expand };
                vbox.AddChild(emptySpacer2);
            }

            _slotPickerContainer.AddChild(slotCard);
        }
    }

    /// <summary>
    /// Handle clicking "New Campaign" on a slot (empty or overwrite).
    /// </summary>
    private void OnSlotNewClicked(int slotIndex, bool overwrite)
    {
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
        if (overwrite)
        {
            // Confirm overwrite first
            var confirm = new ConfirmationDialog
            {
                DialogText = "Create a new campaign in this slot? All existing progress will be lost.",
                OkButtonText = "Overwrite",
                CancelButtonText = "Cancel",
                Title = "New Campaign"
            };
            int capturedSlot = slotIndex;
            confirm.Confirmed += () =>
            {
                // Delete old slot data first
                CampaignContext.DeleteProfile(capturedSlot);
                // Add new empty profile
                CampaignContext.AddOrUpdateProfile("", "");
                CampaignContext.ChosenClass = "";
                CampaignContext.ChosenTown = "";
                GD.Print($"[Main] New campaign starting in slot {capturedSlot}");
                GetTree().ChangeSceneToFile("res://scenes/choose_path/ChooseYourPathScene.tscn");
            };
            AddChild(confirm);
            confirm.PopupCentered();
        }
        else
        {
            // Empty slot — just start new campaign
            CampaignContext.AddOrUpdateProfile("", "");
            CampaignContext.ChosenClass = "";
            CampaignContext.ChosenTown = "";
            GD.Print($"[Main] New campaign starting in slot {slotIndex}");
            GetTree().ChangeSceneToFile("res://scenes/choose_path/ChooseYourPathScene.tscn");
        }
    }

    /// <summary>
    /// Handle clicking "Continue" on an occupied slot.
    /// </summary>
    private void OnSlotContinueClicked(int slotIndex)
    {
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
        if (slotIndex < 0 || slotIndex >= CampaignContext.Profiles.Count)
        {
            GD.PrintErr($"[Main] Cannot continue slot {slotIndex} — no profile");
            return;
        }

        var profile = CampaignContext.Profiles[slotIndex];
        if (string.IsNullOrEmpty(profile.ClassId))
        {
            GD.PrintErr($"[Main] Cannot continue slot {slotIndex} — class not set");
            return;
        }

        // Switch SaveManager to this slot's database
        CampaignContext.ActiveProfileSlot = slotIndex;
        CampaignContext.SaveManager.SwitchSlot(slotIndex);
        CampaignContext.ChosenClass = profile.ClassId;
        CampaignContext.ChosenTown = profile.TownName ?? "";

        GD.Print($"[Main] Continuing slot {slotIndex}: {profile.ClassId} ({profile.TownName})");

        // Ensure starter deck exists for this class
        CampaignContext.EnsureStarterDeck(profile.ClassId);
        GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
    }

    /// <summary>
    /// Handle clicking "Delete" on an occupied slot — shows confirmation dialog.
    /// </summary>
    private void OnSlotDeleteClicked(int slotIndex)
    {
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
        var profile = CampaignContext.Profiles[slotIndex];
        string className = char.ToUpper(profile.ClassId[0]) + profile.ClassId.Substring(1);

        var dialog = new ConfirmationDialog
        {
            DialogText = $"Delete {className}'s campaign? All progress in this slot will be lost forever.",
            OkButtonText = "Delete",
            CancelButtonText = "Cancel",
            Title = "Delete Campaign"
        };
        int capturedSlot = slotIndex;
        dialog.Confirmed += () =>
        {
            CampaignContext.DeleteProfile(capturedSlot);
            GD.Print($"[Main] Deleted slot {capturedSlot}");
            // Rebuild the slot picker to reflect the change
            BuildSlotPicker();
        };
        AddChild(dialog);
        dialog.PopupCentered();
    }

    /// <summary>
    /// Check if the slot picker test mode is active and handle it.
    /// Creates a slot, captures, continues, deletes, captures again.
    /// </summary>
    private void RunSlotPickerTest()
    {
        // Phase 1: Capture the initial empty slot picker
        string suffix = CampaignContext.WideCaptureMode ? "_wide" : "";
        var img = GetViewport().GetTexture().GetImage();
        if (img != null)
            img.SavePng($"{ProjectPaths.Artifacts}/captures/slots_test{suffix}.png");
        DebugCapture.WriteLayoutJson(this, $"slots_test{suffix}");
        GD.Print($"[Main] slots_test{suffix}.png saved (initial empty slots)");

        // Phase 2: Create a new campaign (simulate class selection)
        CampaignContext.AddOrUpdateProfile("warrior", "Emberhold");
        // Set some progression data
        CampaignContext.Progression.AddCard("vrd_c_root_warden");
        CampaignContext.Progression.AddCard("emb_c_ember_hound");
        CampaignContext.Progression.AddCard("dwn_c_dawn_warder");
        CampaignContext.SaveManager.Save();
        GD.Print("[Main] Slot 0 created: warrior in Emberhold with 3 cards");

        // Rebuild picker and capture
        BuildSlotPicker();
        var img2 = GetViewport().GetTexture().GetImage();
        if (img2 != null)
            img2.SavePng($"{ProjectPaths.Artifacts}/captures/slots_test{suffix}_filled.png");
        DebugCapture.WriteLayoutJson(this, $"slots_test{suffix}_filled");
        GD.Print($"[Main] slots_test{suffix}_filled.png saved (occupied slot)");

        // Phase 3: Load the slot (trigger a Continue flow) — just switch slot and verify
        CampaignContext.ActiveProfileSlot = 0;
        CampaignContext.ChosenClass = "warrior";
        CampaignContext.ChosenTown = "Emberhold";
        GD.Print("[Main] Slot 0 loaded (continue flow)");

        // Phase 4: Delete the slot
        CampaignContext.DeleteProfile(0);
        GD.Print("[Main] Slot 0 deleted");

        // Rebuild picker and capture final state
        BuildSlotPicker();
        var img3 = GetViewport().GetTexture().GetImage();
        if (img3 != null)
            img3.SavePng($"{ProjectPaths.Artifacts}/captures/slots_test{suffix}_deleted.png");
        GD.Print($"[Main] slots_test{suffix}_deleted.png saved (after delete)");

        GetTree().Quit();
    }

    /// <summary>
    /// Apply volume settings to Godot audio buses.
    /// Safe to call even if buses don't exist (buses are created by AudioServer on startup).
    /// </summary>
    private static void ApplyAudioSettings(SettingsState s)
    {
        int masterIdx = AudioServer.GetBusIndex("Master");
        if (masterIdx >= 0)
        {
            AudioServer.SetBusVolumeDb(masterIdx, Mathf.LinearToDb(s.MasterVolume));
            AudioServer.SetBusMute(masterIdx, s.MasterMute);
        }

        int musicIdx = AudioServer.GetBusIndex("Music");
        if (musicIdx >= 0)
            AudioServer.SetBusVolumeDb(musicIdx, Mathf.LinearToDb(s.MusicVolume));

        int sfxIdx = AudioServer.GetBusIndex("SFX");
        if (sfxIdx >= 0)
            AudioServer.SetBusVolumeDb(sfxIdx, Mathf.LinearToDb(s.SfxVolume));

        int ambIdx = AudioServer.GetBusIndex("Ambient");
        if (ambIdx >= 0)
            AudioServer.SetBusVolumeDb(ambIdx, Mathf.LinearToDb(s.AmbientVolume));
    }

    private void OnOpenRunePage()
    {
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
        GetTree().ChangeSceneToFile("res://scenes/rune/RunePageScene.tscn");
    }

    private void OnOpenDecks()
    {
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
        GetTree().ChangeSceneToFile("res://scenes/deck/DeckBuilderScene.tscn");
    }

    private void OnOpenForge()
    {
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
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
        var regionIds = new[] { "region_01", "region_02" };
        foreach (var regionId in regionIds)
        {
            string mapJson = Godot.FileAccess.GetFileAsString($"res://content/map/{regionId}.json");
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
                        GD.PrintErr($"[ContentValidation] MAP NODE '{node.Id}' (region {regionId}) references unknown encounter/dig site '{node.Encounter}'");
                        errors++;
                    }
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
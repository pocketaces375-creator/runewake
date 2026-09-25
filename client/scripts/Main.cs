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
    private Button _accountChip = default!;   // FABLE-018
    private Label _saveWarningLabel = default!;
    private Button _decksButton = default!;
    private Button _runeButton = default!;
    private Button _forgeButton = default!;
    private Button _diagButton = default!;
    // FABLE-019d: title-menu button metrics (the global MinButtonHeight/FontButtonPrimary are 120/44).
    private const int TitleButtonHeight = 84;
    private const int TitleButtonFont = 34;
    private Control? _slotPickerContainer;
    private Control? _diagPanel;
    private bool _loading;

    // ——— Rune wheel animation fields ———
    private TextureRect? _runeWheel;
    private TextureRect? _runeWheelReverse;
    private double _runeWheelAngle;

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

        // ——— Rune wheel overlay (transparent PNG, rotates forever) ———
        // Positioned over the painted wheel in hero_art.png. The wheel is at
        // roughly (722, 350) in the 1536×864 source image. With KeepAspectCovered
        // on a target viewport (Vw×Vh), scale = max(Vw/1536, Vh/864) and the
        // image is centred within the viewport. We compute the viewport position
        // from the source coords so it stays on the painted wheel at any size.
        const float WHEEL_SRC_CX = 722f;
        const float WHEEL_SRC_CY = 350f;
        const float WHEEL_SRC_RADIUS = 180f;     // painted wheel radius in source pixels

        float vw = GetViewportRect().Size.X;
        float vh = GetViewportRect().Size.Y;
        float scale = Mathf.Max(vw / 1536f, vh / 864f);
        float imgW = 1536f * scale;
        float imgH = 864f * scale;
        float offsetX = (imgW - vw) / 2f;
        float offsetY = (imgH - vh) / 2f;

        float wheelViewportCx = WHEEL_SRC_CX * scale - offsetX;
        float wheelViewportCy = WHEEL_SRC_CY * scale - offsetY;

        // FABLE-032: the painted vortex sits at 47% of the picture's width, so under a centred
        // menu it read a hair left ("the spiral is off centre left of the Continue button").
        // Slide the painting right by exactly that difference; the vortex is now the axis the
        // menu hangs on. TitleAtmosphere.StartPushIn keeps enough overscan to hide the slide.
        float heroShiftX = vw / 2f - wheelViewportCx;
        heroArt.Position = new Vector2(heroShiftX, heroArt.Position.Y);
        TitleAtmosphere.HeroShiftX = heroShiftX;
        wheelViewportCx = vw / 2f;
        GD.Print($"[TITLE] vortex centred: painting slid {heroShiftX:F0}px");
        float wheelViewportRadius = WHEEL_SRC_RADIUS * scale;
        float wheelSize = wheelViewportRadius * 2f;

        if (ResourceLoader.Exists("res://content/art/title/rune_wheel.png"))
        {
            var wheelTex = ResourceLoader.Load<Texture2D>("res://content/art/title/rune_wheel.png");
            if (wheelTex != null)
            {
                // Primary wheel — CW rotation
                var wheel = new TextureRect
                {
                    Texture = wheelTex,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    MouseFilter = MouseFilterEnum.Ignore,
                    Position = new Vector2(
                        wheelViewportCx - wheelSize / 2f,
                        wheelViewportCy - wheelSize / 2f),
                    Size = new Vector2(wheelSize, wheelSize)
                };
                AddChild(wheel);
                _runeWheel = wheel;

                // Secondary wheel — CCW, faint, half speed for depth
                var wheelRev = new TextureRect
                {
                    Texture = wheelTex,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    MouseFilter = MouseFilterEnum.Ignore,
                    Modulate = new Color(1f, 1f, 1f, 0.18f),
                    Position = new Vector2(
                        wheelViewportCx - wheelSize / 2f,
                        wheelViewportCy - wheelSize / 2f),
                    Size = new Vector2(wheelSize, wheelSize)
                };
                AddChild(wheelRev);
                _runeWheelReverse = wheelRev;

                GD.Print($"[RUNE-WHEEL] Wheel at ({wheelViewportCx:F0},{wheelViewportCy:F0}), size {wheelSize:F0}px @ scale {scale:F2}");
            }
            else
            {
                GD.PrintErr("[ART-MISSING] rune_wheel.png: ResourceLoader.Load returned null");
            }
        }
        else
        {
            GD.Print("[RUNE-WHEEL] rune_wheel.png not found — wheel disabled");
        }

        // ——— FABLE-007: the screen comes alive ———
        // Light shafts, caustics on the flooded floor, drifting dust, a breathing
        // vignette and a slow push-in on the painting. Added here so it sits in
        // front of the art and the rune wheels, and behind every piece of UI
        // built below. Costs no asset bytes — see TitleAtmosphere.
        TitleAtmosphere.Attach(this, heroArt);

        // ——— Soft pool of shade behind the title, for readability ———
        // FABLE-010: this was a hard-edged ColorRect — a black bar ruled straight
        // across the columns, and the first thing your eye found on the screen.
        // Same job, done with a radial gradient that has no edge to notice.
        var scrim = new TextureRect
        {
            Texture = TitleAtmosphere.MakeTitleShadeTexture(),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            AnchorLeft = 0.04f, AnchorRight = 0.96f,
            AnchorTop = 0.00f, AnchorBottom = 0.40f,
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
        MenuButtons.BreatheTitle(title);

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
            AnchorTop = 0.965f, AnchorBottom = 0.995f
        };
        _statusLabel.AddThemeFontSizeOverride("font_size", 12);
        _statusLabel.Modulate = new Color(0.5f, 0.45f, 0.35f, 0.4f);
        AddChild(_statusLabel);

        // ——— FABLE-018: account chip, top-right ———
        // Reads "Guest 3F2A · Backed up" / "adam@… · Backed up" / "No
        // connection", live, and opens the Account panel. Deliberately a quiet
        // text button in the corner: the first-launch experience must not
        // acquire a sign-in wall — the player is already signed in, they just
        // don't know it yet.
        _accountChip = new Button
        {
            Text = "Account",
            Flat = true,
            AnchorLeft = 0.72f, AnchorRight = 0.985f,
            AnchorTop = 0.015f, AnchorBottom = 0.07f,
            OffsetLeft = 0, OffsetRight = 0, OffsetTop = 0, OffsetBottom = 0,
            Alignment = HorizontalAlignment.Right,
            ClipText = true,
        };
        _accountChip.AddThemeFontSizeOverride("font_size", ThemeTokens.FontLargeBody);
        _accountChip.AddThemeColorOverride("font_color", Color.FromHtml("#B8A88A"));
        _accountChip.AddThemeColorOverride("font_hover_color", Color.FromHtml("#E8DCC8"));
        _accountChip.AddThemeColorOverride("font_pressed_color", Color.FromHtml("#C9A84C"));
        _accountChip.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            if (CampaignContext.SyncManager != null) AccountPanel.Open(this, CampaignContext.SyncManager);
        };
        AddChild(_accountChip);

        // ——— Stone-styled buttons (Play, Decks, Settings) ———
        // FABLE-007: carved plates rather than flat rectangles — see MenuButtons.
        var stoneNormal = MenuButtons.Normal();
        var stoneHover = MenuButtons.Hover();
        var stonePressed = MenuButtons.Pressed();

        Button MakeStoneButton(string text)
        {
            var btn = new Button
            {
                Text = text,
                AnchorLeft = 0.40f, AnchorRight = 0.60f,
                // FABLE-019d: 84px, not the global 120 — the title menu was
                // hiding most of the hall ("I can hardly see anything").
                CustomMinimumSize = new Vector2(0, TitleButtonHeight),
            };
            btn.AddThemeFontSizeOverride("font_size", TitleButtonFont);
            btn.AddThemeColorOverride("font_color", Color.FromHtml("#E8DCC8"));
            btn.AddThemeColorOverride("font_pressed_color", Color.FromHtml("#B8A878"));
            btn.AddThemeColorOverride("font_hover_color", Color.FromHtml("#F0E8D0"));
            btn.AddThemeStyleboxOverride("normal", stoneNormal);
            btn.AddThemeStyleboxOverride("hover", stoneHover);
            btn.AddThemeStyleboxOverride("pressed", stonePressed);
            btn.AddThemeStyleboxOverride("disabled", stoneNormal);
            var labelFont = ThemeTokens.GetButtonFont(TitleButtonFont);
            if (labelFont != null)
                btn.AddThemeFontOverride("font", labelFont);
            MenuButtons.Animate(btn);
            return btn;
        }

        // ═══ Build slot picker (single campaign panel) ═══
        BuildSlotPicker();

        // ═══ FABLE-031: one quiet row of four, low, so the hall and the vortex own the screen ═══
        // Trikzos: "I want the focal point to in part be the gorgeous background." The 2×2 grid
        // and the full-width account plate covered a third of the hall; this is a single row.
        var buttonRow = new HBoxContainer
        {
            AnchorLeft = 0.20f, AnchorRight = 0.80f,
            AnchorTop = 0.815f, AnchorBottom = 0.885f,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        buttonRow.AddThemeConstantOverride("separation", 14);
        AddChild(buttonRow);

        Button Quiet(string text)
        {
            var b = MakeStoneButton(text);
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            b.CustomMinimumSize = new Vector2(0, 72);
            b.AddThemeFontSizeOverride("font_size", 30);
            var f = ThemeTokens.GetButtonFont(30);
            if (f != null) b.AddThemeFontOverride("font", f);
            b.AddThemeStyleboxOverride("normal", MenuButtons.QuietNormal());
            b.AddThemeStyleboxOverride("disabled", MenuButtons.QuietNormal());
            return b;
        }

        var decksButton = Quiet("Decks");
        decksButton.Pressed += OnOpenDecks;
        buttonRow.AddChild(decksButton);
        _decksButton = decksButton;

        var reliquaryButton = Quiet("Reliquary");
        reliquaryButton.Pressed += () => {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            GetTree().ChangeSceneToFile("res://scenes/reliquary/ReliquaryScene.tscn");
        };
        buttonRow.AddChild(reliquaryButton);

        var arenaButton = Quiet("Duel Arena");
        arenaButton.Pressed += () => {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            GetTree().ChangeSceneToFile("res://scenes/arena/ArenaScene.tscn");
        };
        buttonRow.AddChild(arenaButton);

        var settingsButton = Quiet("Settings");
        settingsButton.Pressed += () => {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            GetTree().ChangeSceneToFile("res://scenes/settings/SettingsScene.tscn");
        };
        buttonRow.AddChild(settingsButton);

        // "Create New Account" is rare: a small text link in the corner, not a plate.
        var newAccountBtn = new Button
        {
            Text = "Create new account",
            Flat = true,
            AnchorLeft = 0.72f, AnchorRight = 0.985f,
            AnchorTop = 0.915f, AnchorBottom = 0.975f,
        };
        newAccountBtn.AddThemeFontSizeOverride("font_size", 24);
        var linkFont = ThemeTokens.GetBodyFont(24);
        if (linkFont != null) newAccountBtn.AddThemeFontOverride("font", linkFont);
        newAccountBtn.AddThemeColorOverride("font_color", new Color(0.78f, 0.70f, 0.52f, 0.85f));
        newAccountBtn.AddThemeColorOverride("font_hover_color", Color.FromHtml("#F0E8D0"));
        newAccountBtn.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        newAccountBtn.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
        newAccountBtn.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        newAccountBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        newAccountBtn.Alignment = HorizontalAlignment.Right;
        newAccountBtn.Pressed += OnOpenAccountsCarousel;
        AddChild(newAccountBtn);

        // FABLE-007: the menu assembles itself — title, subtitle, then each plate
        // in turn — instead of snapping into existence all at once.
        // The wordmark is left out on purpose: BreatheTitle already owns its
        // modulate on a loop, and two tweens driving the same property is a
        // flicker, not an entrance.
        MenuButtons.RevealStagger(new Control[]
        {
            subtitle, decksButton, reliquaryButton,
            settingsButton, arenaButton, newAccountBtn,
        });

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

    public override void _Process(double delta)
    {
        // Rotate the primary rune wheel CW, one revolution per 90 seconds
        const double radiansPerSec = Mathf.Tau / 90.0;  // 2π / 90
        _runeWheelAngle += delta * radiansPerSec;
        // Wrap to prevent float creep on very long sessions
        if (_runeWheelAngle > Mathf.Tau)
            _runeWheelAngle -= Mathf.Tau;

        if (_runeWheel != null)
            _runeWheel.Rotation = (float)_runeWheelAngle;

        // Secondary wheel: CCW at half speed, faint (18% opacity set in _Ready)
        if (_runeWheelReverse != null)
            _runeWheelReverse.Rotation = (float)(-_runeWheelAngle * 0.5);
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
        // FABLE-018: config is baked into the APK (res://supabase_config.json,
        // gitignored) with a user:// override for dev. Anonymous sign-in on
        // first launch, whole-progression cloud save, email linking from the
        // Account panel. All best-effort: no config or no network changes
        // nothing about play.
        var supabaseConfig = SyncManager.LoadConfig();
        // FABLE-019e: ONE SyncManager for the whole run, parented to the root.
        // It used to be a child of this title screen, so it was freed the
        // moment a duel started — every save after that (the end of every
        // fight) scheduled a cloud push on a dead node and threw
        // ObjectDisposedException from inside a deferred call, right in the
        // middle of the scene change. Reproduced in the sandbox with real
        // saves and accounts on. Now it outlives every scene; coming back to
        // the title re-uses it instead of creating a second one.
        var syncManager = CampaignContext.SyncManager;
        bool freshSync = syncManager == null || !IsInstanceValid(syncManager);
        if (freshSync)
        {
            syncManager = new SyncManager { Name = "SyncManager" };
            syncManager.Initialize(supabaseConfig, CampaignContext.Progression!, CampaignContext.SaveManager!);
            CampaignContext.SyncManager = syncManager;
            var sm = syncManager;
            // The root is busy adding THIS scene during _Ready: add next frame. The tree is
            // captured now: a capture run swaps this scene out within the frame, and GetTree()
            // on a node that has left the tree is null (FABLE-033).
            var tree = GetTree();
            Callable.From(() => { if (tree != null && IsInstanceValid(sm) && sm.GetParent() == null) tree.Root.AddChild(sm); }).CallDeferred();
        }
        syncManager!.StatusChanged += status =>
        {
            if (!IsInstanceValid(this)) return;
            if (_accountChip != null && IsInstanceValid(_accountChip))
                _accountChip.Text = status.Length > 64 ? status.Substring(0, 63) + "…" : status;   // FABLE-019: room for the reason; full text in the panel
        };
        _accountChip.Text = syncManager.Status;
        syncManager.ConflictDetected += () => { if (IsInstanceValid(this)) AccountPanel.Open(this, syncManager); };
        syncManager.CloudSaveApplied += () =>
        {
            // The cloud save was written under us. Only reload if we are still
            // the title screen — mid-duel, the next launch will pick it up.
            if (IsInstanceValid(this) && IsInsideTree() && GetTree().CurrentScene == this)
            {
                GD.Print("[Main] cloud save applied — reloading title");
                GetTree().ReloadCurrentScene();
            }
        };
        if (freshSync) _ = syncManager.RunStartupSync(); // fire and forget, once per launch

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
        // FABLE-019e: same as SyncManager — one instance, on the root, for the run.
        if (CampaignContext.Telemetry == null || !IsInstanceValid(CampaignContext.Telemetry))
        {
            var telemetry = new TelemetryService { Name = "TelemetryService" };
            telemetry.Initialize(supabaseConfig, null); // accountId resolved lazily by SyncManager
            CampaignContext.Telemetry = telemetry;
            var tree2 = GetTree();
            Callable.From(() => { if (tree2 != null && IsInstanceValid(telemetry) && telemetry.GetParent() == null) tree2.Root.AddChild(telemetry); }).CallDeferred();
        }

        // Upload any pending crash reports (fire-and-forget, no-op if not configured)
        // FABLE-018: was a hard-coded placeholder URL, so this never once
        // delivered a report. Uses the real config now; no-op when unset.
        ShowStuckTraceIfAny();   // FABLE-025: if the last session got stuck, say where, on screen

        if (supabaseConfig.IsConfigured)
        {
            CrashReporter.QueueExitTrace();   // FABLE-022: send the last duel-exit trace too
            CrashReporter.UploadPendingReports(supabaseConfig.Url, supabaseConfig.AnonKey);
        }

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
            if (CampaignContext.CaptureAccountsCarouselScreenshot)
            {
                // Navigate to accounts carousel for capture
                GD.Print("[Main] Navigating to accounts carousel for capture");
                // Create a warrior account first so the carousel has content
                CampaignContext.AddOrUpdateProfile("warrior", "Emberhold");
                CampaignContext.Progression.AddCard("vrd_c_root_warden");
                CampaignContext.Progression.AddCard("emb_c_ember_hound");
                CampaignContext.SaveManager.Save();
                CampaignContext.ChosenClass = "warrior";
                CampaignContext.ChosenTown = "Emberhold";
                CampaignContext.ActiveProfileSlot = 0;
                CampaignContext.SaveCampaignProfile();
                // Schedule deferred navigation so the title screen renders first
                Callable.From(() => GetTree().ChangeSceneToFile("res://scenes/accounts/AccountsCarouselScene.tscn")).CallDeferred();
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
    /// Run a diagnostic write+read-back test on the save database and display results.
    /// </summary>
    /// <summary>
    /// FABLE-025. Release builds have no Diag button, and the grey screen after a win could not be
    /// seen from here. If the last recorded step of leaving a screen was not a clean arrival, the
    /// title shows the last lines of user://duel_exit_trace.txt once, in words, so a screenshot
    /// is enough to find the step that failed. Never throws; shows each stuck trace only once.
    /// </summary>
    private void ShowStuckTraceIfAny()
    {
        try
        {
            if (!Godot.FileAccess.FileExists(DuelScene.ExitTracePath)) return;
            string trace = Godot.FileAccess.GetFileAsString(DuelScene.ExitTracePath).TrimEnd();
            if (string.IsNullOrWhiteSpace(trace)) return;
            var lines = trace.Split('\n');
            string last = lines[^1];
            // FABLE-028: judge only what happened AFTER the last clean arrival. An old freeze higher
            // up in the file (the trace keeps 60 lines) must not re-open this box on every launch.
            var list = lines.ToList();
            int lastProblem = list.FindLastIndex(l => l.Contains("FROZEN") || l.Contains("THREW") || l.Contains("EMPTY") || l.Contains("WATCHDOG:"));
            int lastClean = list.FindLastIndex(l => l.Contains("all good") || l.Contains("_Ready done") || l.Contains("— running") || l.Contains("arrived: MapScene _Ready done"));
            bool midTransition = last.Contains("swap scheduled") || last.Contains("loading scene") || last.Contains("ChangeScene")
                                 || last.Contains("Continue pressed") || last.Contains("_Ready begin") || last.Contains("zone transition");
            bool stuck = lastProblem > lastClean || midTransition;
            if (!stuck) return;
            const string shownPath = "user://duel_exit_trace.shown";
            string key = trace.Length + ":" + last;
            if (Godot.FileAccess.FileExists(shownPath) && Godot.FileAccess.GetFileAsString(shownPath) == key) return;
            using (var f = Godot.FileAccess.Open(shownPath, Godot.FileAccess.ModeFlags.Write)) f?.StoreString(key);

            var tail = string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 12)));
            // The engine's own errors from that session (file logging is on since FABLE-022).
            try
            {
                string dir = ProjectSettings.GlobalizePath("user://logs");
                var prev = System.IO.Directory.Exists(dir)
                    ? System.IO.Directory.GetFiles(dir, "godot*.log").Where(f => System.IO.Path.GetFileName(f) != "godot.log")
                        .OrderByDescending(System.IO.File.GetLastWriteTimeUtc).FirstOrDefault()
                    : null;
                if (prev != null)
                {
                    // FABLE-027: the whole end of that session's log, not just error lines — the
                    // freeze shows up as WHAT the game was doing, which is rarely an error. Repeated
                    // lines are collapsed so a spam loop cannot push everything else off the screen.
                    var all = System.IO.File.ReadAllLines(prev).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                    var collapsed = new List<string>();
                    foreach (var l in all.Skip(Math.Max(0, all.Count - 400)))
                    {
                        string t = l.Length > 180 ? l[..180] + "…" : l;
                        if (collapsed.Count > 0 && collapsed[^1].StartsWith(t)) { collapsed[^1] = t + "  (repeated)"; continue; }
                        collapsed.Add(t);
                    }
                    tail += "\n— that session's log, NEWEST FIRST —\n" + string.Join("\n", collapsed.TakeLast(45).Reverse());
                }
            }
            catch { /* the trace alone is still worth showing */ }
            var layer = new CanvasLayer { Layer = 120, Name = "StuckTrace" };
            var panel = new PanelContainer
            {
                AnchorLeft = 0.04f, AnchorRight = 0.96f, AnchorTop = 0.06f, AnchorBottom = 0.94f,
            };
            panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.06f, 0.04f, 0.03f, 0.97f), BorderColor = new Color(0.66f, 0.23f, 0.16f),
                BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
                ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 16, ContentMarginBottom = 16,
            });
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 10);
            panel.AddChild(box);
            var head = new Label { Text = "Last time, the game got stuck. Screenshot this for Fable:" };
            head.AddThemeFontSizeOverride("font_size", 30);
            head.AddThemeColorOverride("font_color", new Color(1.0f, 0.62f, 0.52f));
            box.AddChild(head);
            var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            var body = new Label { Text = tail, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            body.AddThemeFontSizeOverride("font_size", 18);
            body.AddThemeColorOverride("font_color", new Color(0.92f, 0.88f, 0.8f));
            scroll.AddChild(body);
            box.AddChild(scroll);
            var hintLbl = new Label { Text = "Scroll down — take a screenshot of each screenful." };
            hintLbl.AddThemeFontSizeOverride("font_size", 18);
            hintLbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.65f, 0.55f));
            box.AddChild(hintLbl);
            var ok = new Button { Text = "Close", CustomMinimumSize = new Vector2(260, 70), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
            ok.AddThemeFontSizeOverride("font_size", 30);
            ok.Pressed += () => layer.QueueFree();
            box.AddChild(ok);
            layer.AddChild(panel);
            Callable.From(() => { if (IsInstanceValid(this)) GetTree().Root.AddChild(layer); }).CallDeferred();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Main] could not show the stuck trace: {ex.Message}");
        }
    }

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

        // FABLE-019d: the last duel exit, step by step, from the phone itself.
        // Written by DuelScene.ExitTrace. When Continue stalls, this is the
        // screenshot to send: it says which step was the last one reached.
        {
            string trace = "";
            try
            {
                if (Godot.FileAccess.FileExists(DuelScene.ExitTracePath))
                    trace = Godot.FileAccess.GetFileAsString(DuelScene.ExitTracePath).Trim();
            }
            catch (System.Exception ex) { trace = "(could not read: " + ex.Message + ")"; }
            vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });
            var traceTitle = new Label
            {
                Text = "Last duel exit (newest at the bottom)",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
            };
            traceTitle.AddThemeFontSizeOverride("font_size", 14);
            vbox.AddChild(traceTitle);
            var traceLabel = new Label
            {
                Text = string.IsNullOrEmpty(trace) ? "(no duel exit recorded yet)" : trace,
                AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            traceLabel.AddThemeFontSizeOverride("font_size", 12);
            traceLabel.Modulate = new Color(0.85f, 0.85f, 0.8f);
            vbox.AddChild(traceLabel);
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

        // FABLE-031: ONE plate, no panel around it. Trikzos: "new campaign effectively has 2
        // boxes … let's just have the smaller box inside if anything." With a campaign going, the
        // plate is Continue (class · region on a second line); without one, New Campaign. It sits
        // just under the vortex's eye, on the centre line, so the hall stays the picture.
        _slotPickerContainer = new Control
        {
            AnchorLeft = 0.33f, AnchorRight = 0.67f,
            AnchorTop = 0.615f, AnchorBottom = 0.735f,
            MouseFilter = MouseFilterEnum.Stop
        };
        AddChild(_slotPickerContainer);

        var profiles = CampaignContext.Profiles;
        bool hasActiveProfile = CampaignContext.ActiveProfileSlot >= 0
            && CampaignContext.ActiveProfileSlot < profiles.Count
            && !string.IsNullOrEmpty(profiles[CampaignContext.ActiveProfileSlot].ClassId);

        var plate = new Button { Name = "PrimaryPlate", FocusMode = FocusModeEnum.None, MouseFilter = MouseFilterEnum.Stop };
        plate.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        // The plate's own Text carries its meaning for tooling (the loop smoke test finds the
        // button by Text) but is drawn invisibly — the two child labels are what the player sees.
        plate.Text = hasActiveProfile ? "Continue" : "New Campaign";
        foreach (var st in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color", "font_disabled_color" })
            plate.AddThemeColorOverride(st, Colors.Transparent);
        plate.AddThemeStyleboxOverride("normal", MenuButtons.PrimaryNormal());
        plate.AddThemeStyleboxOverride("hover", MenuButtons.PrimaryHover());
        plate.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed());
        plate.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        MenuButtons.Animate(plate);
        _slotPickerContainer.AddChild(plate);

        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        col.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        col.AddThemeConstantOverride("separation", 2);
        plate.AddChild(col);

        Label Line(string text, int size, Font? font, Color color)
        {
            var l = new Label
            {
                Text = text, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore,
                AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
            };
            if (font != null) l.AddThemeFontOverride("font", font);
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", color);
            l.AddThemeConstantOverride("outline_size", 3);
            l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.55f));
            col.AddChild(l);
            return l;
        }

        if (hasActiveProfile)
        {
            var profile = profiles[CampaignContext.ActiveProfileSlot];
            string classId = profile.ClassId;
            string className = char.ToUpper(classId[0]) + classId.Substring(1);
            // The real region name ("The Fallow Reach"), not "Region 1".
            string region;
            try { region = CampaignRun.LoadCurrentRegion()?.Name ?? CampaignContext.GetSlotRegion(CampaignContext.ActiveProfileSlot); }
            catch { region = CampaignContext.GetSlotRegion(CampaignContext.ActiveProfileSlot); }
            Line("Continue", 44, ThemeTokens.GetButtonFont(44), Color.FromHtml("#F2DFA6"));
            Line($"{className}  ·  {region}", 24, ThemeTokens.GetBodyFont(24), new Color(0.86f, 0.80f, 0.66f));
            plate.Pressed += () => OnSlotContinueClicked(CampaignContext.ActiveProfileSlot);

            // Starting over is rare and destructive: a small link under the plate, not a red button.
            var startOver = new Button
            {
                Text = "Start a new campaign", Flat = true,
                AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 1.08f, AnchorBottom = 1.50f,
            };
            startOver.AddThemeFontSizeOverride("font_size", 26);
            var lf = ThemeTokens.GetBodyFont(22);
            if (lf != null) startOver.AddThemeFontOverride("font", lf);
            startOver.AddThemeColorOverride("font_color", new Color(0.86f, 0.78f, 0.58f, 0.95f));
            startOver.AddThemeConstantOverride("outline_size", 6);
            startOver.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.6f));
            startOver.AddThemeColorOverride("font_hover_color", Color.FromHtml("#F0E8D0"));
            foreach (var st in new[] { "normal", "hover", "pressed", "focus" }) startOver.AddThemeStyleboxOverride(st, new StyleBoxEmpty());
            startOver.Pressed += () => OnSlotDeleteClicked(CampaignContext.ActiveProfileSlot);
            _slotPickerContainer.AddChild(startOver);
        }
        else
        {
            Line("New Campaign", 44, ThemeTokens.GetButtonFont(44), Color.FromHtml("#F2DFA6"));
            plate.Pressed += () => OnSlotNewClicked(0, overwrite: false);
        }
    }

    /// <summary>
    /// Handle tapping "Create New Account" — opens the accounts carousel screen.
    /// </summary>
    private void OnOpenAccountsCarousel()
    {
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
        GD.Print("[Main] Opening accounts carousel");
        GetTree().ChangeSceneToFile("res://scenes/accounts/AccountsCarouselScene.tscn");
    }

    /// <summary>
    /// Handle clicking "New Campaign" on a slot (empty or overwrite).
    /// </summary>
    private void OnSlotNewClicked(int slotIndex, bool overwrite)
    {
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");

        // Create new empty profile and go to ChooseYourPath
        CampaignContext.AddOrUpdateProfile("", "");
        CampaignContext.ChosenClass = "";
        CampaignContext.ChosenTown = "";
        GD.Print($"[Main] Starting new campaign");
        GetTree().ChangeSceneToFile("res://scenes/choose_path/ChooseYourPathScene.tscn");
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
        CampaignContext.ChosenClass = "warrior";
        CampaignContext.ChosenTown = "Emberhold";
        CampaignContext.ActiveProfileSlot = 0;
        GD.Print("[Main] Profile created: warrior in Emberhold with 3 cards");

        // Rebuild picker and capture
        BuildSlotPicker();
        var img2 = GetViewport().GetTexture().GetImage();
        if (img2 != null)
            img2.SavePng($"{ProjectPaths.Artifacts}/captures/slots_test{suffix}_filled.png");
        DebugCapture.WriteLayoutJson(this, $"slots_test{suffix}_filled");
        GD.Print($"[Main] slots_test{suffix}_filled.png saved (occupied slot)");

        // Phase 3: Load the slot (trigger a Continue flow) — just verify it works
        CampaignContext.ActiveProfileSlot = 0;
        CampaignContext.ChosenClass = "warrior";
        CampaignContext.ChosenTown = "Emberhold";
        GD.Print("[Main] Profile loaded (continue flow)");

        // Phase 4: Delete the profile
        CampaignContext.DeleteProfile(0);
        GD.Print("[Main] Profile deleted");

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
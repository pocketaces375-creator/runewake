using Godot;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Settings screen — code-driven Godot UI.
/// Volume sliders apply live to audio buses, Save persists to disk.
/// Styled in Cinzel/gold on dark stone matching the title screen.
/// </summary>
public partial class SettingsScene : Control
{
    // ── Controls ────────────────────────────────────────────────────
    private HSlider? _musicSlider;
    private Label? _musicLabel;
    private HSlider? _sfxSlider;
    private Label? _sfxLabel;
    private HSlider? _ambientSlider;
    private Label? _ambientLabel;
    private CheckButton? _muteToggle;
    private Button? _backBtn;

    // Credits overlay
    private Control? _creditsOverlay;

    // Reset confirm overlay
    private Control? _resetOverlay;
    private LineEdit? _resetInput;
    private Button? _resetConfirmBtn;
    private Label? _resetError;

    // Stored pre-save so we only write to disk on explicit Save
    private bool _dirty;

    public override void _Ready()
    {
        BuildUI();
        LoadCurrentSettings();

        // Capture hook for --capture=settings_test[_wide]
        if (CampaignContext.AutoCaptureScreenshot && CampaignContext.CaptureSettingsScreenshot)
        {
            var timer = GetTree().CreateTimer(0.5f);
            timer.Timeout += () =>
            {
                var image = GetViewport().GetTexture().GetImage();
                if (image != null)
                {
                    string path = CampaignContext.WideCaptureMode
                        ? ProjectPaths.Artifacts + "/captures/settings_test_wide.png"
                        : ProjectPaths.Artifacts + "/captures/settings_test.png";
                    image.SavePng(path);
                    string baseName = CampaignContext.WideCaptureMode ? "settings_test_wide" : "settings_test";
                    DebugCapture.WriteLayoutJson(this, baseName);
                    GD.Print($"[SettingsScene] Captured to {path}");

                    // TASK-UI-LINT-1: Dump layout JSON
                    string settingsBasename = CampaignContext.WideCaptureMode ? "settings_test_wide" : "settings_test";
                    DebugCapture.DumpLayoutJSON(settingsBasename, this);
                }
                GetTree().Quit(0);
            };
        }
    }

    private void BuildUI()
    {
        // ── Dark stone background ──
        var bg = new ColorRect
        {
            Color = Color.FromHtml("#1A1816"),
            AnchorsPreset = (int)LayoutPreset.FullRect,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(bg);

        // ── Title ──
        var title = new Label
        {
            Text = "SETTINGS",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0.1f, AnchorRight = 0.9f,
            AnchorTop = 0.02f, AnchorBottom = 0.09f
        };
        ThemeTokens.ApplyHeaderFont(title, ThemeTokens.FontTitle);
        title.Modulate = Color.FromHtml("#C9A84C"); // gold
        AddChild(title);

        // ── Decorative separator line ──
        var sep = new ColorRect
        {
            Color = Color.FromHtml("#C9A84C"),
            AnchorLeft = 0.15f, AnchorRight = 0.85f,
            AnchorTop = 0.10f, AnchorBottom = 0.102f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(sep);

        // ── Content scroll container ──
        var scroll = new ScrollContainer
        {
            AnchorLeft = 0.08f, AnchorRight = 0.92f,
            AnchorTop = 0.12f, AnchorBottom = 0.82f
        };
        AddChild(scroll);

        var vbox = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            SizeFlagsVertical = Control.SizeFlags.Fill,
            CustomMinimumSize = new Vector2(0, 0)
        };
        scroll.AddChild(vbox);

        // ═══ VOLUME SECTION ═══
        AddSectionHeader(vbox, "VOLUME");
        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        AddSliderRow(vbox, "Music", out _musicSlider, out _musicLabel, OnVolumeChanged);
        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
        AddSliderRow(vbox, "SFX", out _sfxSlider, out _sfxLabel, OnVolumeChanged);
        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
        AddSliderRow(vbox, "Ambient", out _ambientSlider, out _ambientLabel, OnVolumeChanged);

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

        // ═══ MASTER MUTE ═══
        _muteToggle = AddToggle(vbox, "Mute All", OnMuteToggled);

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

        // ═══ GRAPHICS QUALITY ═══
        var gfxHbox = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            CustomMinimumSize = new Vector2(0, 44)
        };
        vbox.AddChild(gfxHbox);

        var gfxLbl = new Label
        {
            Text = "Graphics Quality",
            CustomMinimumSize = new Vector2(100, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        ThemeTokens.ApplyBodyFont(gfxLbl, ThemeTokens.FontBody);
        gfxLbl.Modulate = Color.FromHtml("#E8DCC8");
        gfxHbox.AddChild(gfxLbl);

        gfxHbox.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.Fill });

        var gfxValue = new Label
        {
            Text = "Low",
            CustomMinimumSize = new Vector2(60, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        ThemeTokens.ApplyBodyFont(gfxValue, ThemeTokens.FontSmall);
        gfxValue.Modulate = Color.FromHtml("#C9A84C");
        gfxHbox.AddChild(gfxValue);

        var gfxToggle = new CheckButton
        {
            CustomMinimumSize = new Vector2(48, 0),
            ButtonPressed = CampaignContext.Settings.GraphicsQuality
        };
        gfxValue.Text = gfxToggle.ButtonPressed ? "High" : "Low";
        gfxToggle.Toggled += on =>
        {
            _dirty = true;
            CampaignContext.Settings.GraphicsQuality = on;
            gfxValue.Text = on ? "High" : "Low";
            ApplyGraphicsQuality(on);
            GD.Print($"[Settings] Graphics quality set to {(on ? "High" : "Low")}");
        };
        gfxHbox.AddChild(gfxToggle);

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

        // ═══ REPLAY INTRO ═══
        var replayBtn = MakeStoneButton("Replay Intro");
        replayBtn.Pressed += OnReplayIntro;
        vbox.AddChild(replayBtn);

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        // ═══ CREDITS BUTTON ═══
        var creditsBtn = MakeStoneButton("Audio Credits");
        creditsBtn.Pressed += ShowCreditsOverlay;
        vbox.AddChild(creditsBtn);

        // ═══ RESET PROGRESS ═══
        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        var resetBtn = MakeStoneButton("Reset Progress");
        resetBtn.Pressed += ShowResetConfirm;
        vbox.AddChild(resetBtn);

        // ═══ SAVE + BACK BUTTONS ═══
        var btnHbox = new HBoxContainer
        {
            AnchorLeft = 0.15f, AnchorRight = 0.85f,
            AnchorTop = 0.85f, AnchorBottom = 0.96f,
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
        };
        AddChild(btnHbox);

        var saveBtn = MakeStoneButton("Save");
        saveBtn.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        saveBtn.Pressed += OnSavePressed;
        btnHbox.AddChild(saveBtn);

        btnHbox.AddChild(new Control { CustomMinimumSize = new Vector2(16, 0) });

        _backBtn = MakeStoneButton("Back");
        _backBtn.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        _backBtn.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn");
        };
        btnHbox.AddChild(_backBtn);

        // ═══ VERSION + BUILD HASH (bottom-right) ═══
        var versionLabel = new Label
        {
            AnchorLeft = 0.60f, AnchorRight = 0.96f,
            AnchorTop = 0.96f, AnchorBottom = 0.995f,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            MouseFilter = MouseFilterEnum.Ignore
        };
        versionLabel.AddThemeFontSizeOverride("font_size", 10);
        versionLabel.Modulate = Color.FromHtml("#5A5048"); // muted stone

        // Build version string from project settings and build info
        string version = ProjectSettings.GetSetting("application/config/version", "dev").AsString();
        string hash = "unknown";
        try
        {
            if (Godot.FileAccess.FileExists("res://content/misc/build_info.txt"))
            {
                string buildInfo = Godot.FileAccess.GetFileAsString("res://content/misc/build_info.txt");
                var lines = buildInfo.Split('\n');
                if (lines.Length >= 2 && lines[1].StartsWith("sha:"))
                    hash = lines[1].Substring(4, Math.Min(8, lines[1].Length - 4));
            }
        }
        catch { /* best-effort */ }

        versionLabel.Text = $"{version} ({hash})";
        AddChild(versionLabel);
    }

    // ── UI helpers ──────────────────────────────────────────────────

    private static void AddSectionHeader(VBoxContainer parent, string text)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        ThemeTokens.ApplyHeaderFont(label, ThemeTokens.FontLargeBody);
        label.Modulate = Color.FromHtml("#C9A84C"); // gold
        parent.AddChild(label);
    }

    private static void AddSliderRow(VBoxContainer parent, string labelText,
        out HSlider slider, out Label valLabel, Action<double> handler)
    {
        var hbox = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            CustomMinimumSize = new Vector2(0, 44) // 44px touch target
        };

        var lbl = new Label
        {
            Text = labelText,
            CustomMinimumSize = new Vector2(100, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        ThemeTokens.ApplyBodyFont(lbl, ThemeTokens.FontBody);
        lbl.Modulate = Color.FromHtml("#E8DCC8"); // cream
        hbox.AddChild(lbl);

        slider = new HSlider
        {
            MinValue = 0, MaxValue = 100, Step = 1,
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            CustomMinimumSize = new Vector2(100, 0)
        };
        slider.ValueChanged += v => handler(v);
        slider.AddThemeStyleboxOverride("slider", ThemeTokens.StyleWornBorder(
            borderColor: Color.FromHtml("#C9A84C"), width: 1, radius: 2));
        hbox.AddChild(slider);

        valLabel = new Label
        {
            Text = "100",
            CustomMinimumSize = new Vector2(40, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ThemeTokens.ApplyBodyFont(valLabel, ThemeTokens.FontSmall);
        valLabel.Modulate = Color.FromHtml("#E8DCC8");
        var capturedLabel = valLabel; // local copy for lambda capture
        slider.ValueChanged += v => capturedLabel.Text = ((int)v).ToString();
        hbox.AddChild(valLabel);

        parent.AddChild(hbox);
    }

    private static CheckButton AddToggle(VBoxContainer parent, string labelText, Action<bool> handler)
    {
        var hbox = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            CustomMinimumSize = new Vector2(0, 44)
        };

        var lbl = new Label
        {
            Text = labelText,
            CustomMinimumSize = new Vector2(100, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        ThemeTokens.ApplyBodyFont(lbl, ThemeTokens.FontBody);
        lbl.Modulate = Color.FromHtml("#E8DCC8");
        hbox.AddChild(lbl);

        hbox.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.Fill });

        var toggle = new CheckButton
        {
            CustomMinimumSize = new Vector2(48, 0)
        };
        toggle.Toggled += on => handler(on);
        hbox.AddChild(toggle);

        parent.AddChild(hbox);
        return toggle;
    }

    private static Button MakeStoneButton(string text)
    {
        var btn = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, 44)
        };
        btn.AddThemeFontSizeOverride("font_size", ThemeTokens.FontBody);
        btn.AddThemeColorOverride("font_color", Color.FromHtml("#E8DCC8"));
        btn.AddThemeColorOverride("font_pressed_color", Color.FromHtml("#B8A878"));
        btn.AddThemeColorOverride("font_hover_color", Color.FromHtml("#F0E8D0"));

        var normal = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#3A3530"),
            BorderColor = Color.FromHtml("#5A5048"),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 16, ContentMarginTop = 12,
            ContentMarginRight = 16, ContentMarginBottom = 12
        };
        var hover = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#4A4540"),
            BorderColor = Color.FromHtml("#C9A84C"),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 16, ContentMarginTop = 12,
            ContentMarginRight = 16, ContentMarginBottom = 12
        };
        var pressed = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#2A2520"),
            BorderColor = Color.FromHtml("#A08838"),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 16, ContentMarginTop = 12,
            ContentMarginRight = 16, ContentMarginBottom = 12
        };
        btn.AddThemeStyleboxOverride("normal", normal);
        btn.AddThemeStyleboxOverride("hover", hover);
        btn.AddThemeStyleboxOverride("pressed", pressed);
        btn.AddThemeStyleboxOverride("disabled", normal);
        return btn;
    }

    // ── Credits overlay ──────────────────────────────────────────────

    private void ShowCreditsOverlay()
    {
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");

        // Close existing overlay if open
        if (_creditsOverlay != null && IsInstanceValid(_creditsOverlay))
        {
            RemoveChild(_creditsOverlay);
            _creditsOverlay.QueueFree();
            _creditsOverlay = null;
            return;
        }

        _creditsOverlay = new Control
        {
            AnchorsPreset = (int)LayoutPreset.FullRect,
            MouseFilter = MouseFilterEnum.Stop
        };
        AddChild(_creditsOverlay);

        // Dim background
        var dimBg = new ColorRect
        {
            Color = new Color(0.1f, 0.09f, 0.08f, 0.85f),
            AnchorsPreset = (int)LayoutPreset.FullRect,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _creditsOverlay.AddChild(dimBg);

        // Centered panel
        var panel = new PanelContainer
        {
            AnchorsPreset = (int)LayoutPreset.Center,
            AnchorLeft = 0.15f, AnchorRight = 0.85f,
            AnchorTop = 0.10f, AnchorBottom = 0.85f,
            CustomMinimumSize = new Vector2(0, 0)
        };
        var panelBg = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#2A2520"),
            BorderColor = Color.FromHtml("#5A5048"),
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8
        };
        panel.AddThemeStyleboxOverride("panel", panelBg);
        _creditsOverlay.AddChild(panel);

        var panelVbox = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            SizeFlagsVertical = Control.SizeFlags.Fill
        };
        panel.AddChild(panelVbox);

        // Title
        var credTitle = new Label
        {
            Text = "AUDIO CREDITS",
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(0, 40)
        };
        ThemeTokens.ApplyHeaderFont(credTitle, ThemeTokens.FontLargeBody);
        credTitle.Modulate = Color.FromHtml("#C9A84C");
        panelVbox.AddChild(credTitle);

        // Scrollable credits content
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.Fill,
            SizeFlagsHorizontal = Control.SizeFlags.Fill
        };
        panelVbox.AddChild(scroll);

        string creditsText = "All audio files shipped with Runewake are CC0 / public domain.\n\n";
        try
        {
            if (Godot.FileAccess.FileExists("res://content/audio/AUDIO_CREDITS.md"))
            {
                creditsText = Godot.FileAccess.GetFileAsString("res://content/audio/AUDIO_CREDITS.md");
            }
            else
            {
                creditsText += "AUDIO_CREDITS.md not found — see content/audio/ directory for full details.";
            }
        }
        catch
        {
            creditsText += "Could not load credits file.";
        }

        var credBody = new Label
        {
            Text = creditsText,
            AutowrapMode = TextServer.AutowrapMode.Word,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.Fill
        };
        ThemeTokens.ApplyBodyFont(credBody, ThemeTokens.FontSmall);
        credBody.Modulate = Color.FromHtml("#C8B88A");
        scroll.AddChild(credBody);

        // Close button
        var closeBtn = MakeStoneButton("Close");
        closeBtn.CustomMinimumSize = new Vector2(0, 40);
        closeBtn.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            if (_creditsOverlay != null && IsInstanceValid(_creditsOverlay))
            {
                RemoveChild(_creditsOverlay);
                _creditsOverlay.QueueFree();
                _creditsOverlay = null;
            }
        };
        panelVbox.AddChild(closeBtn);
    }

    // ── Reset progress confirm ──────────────────────────────────────

    private void ShowResetConfirm()
    {
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");

        if (_resetOverlay != null && IsInstanceValid(_resetOverlay))
        {
            // Already open — remove it
            RemoveChild(_resetOverlay);
            _resetOverlay.QueueFree();
            _resetOverlay = null;
            return;
        }

        _resetOverlay = new Control
        {
            AnchorsPreset = (int)LayoutPreset.FullRect,
            MouseFilter = MouseFilterEnum.Stop
        };
        AddChild(_resetOverlay);

        // Dim background (darker red tint for danger)
        var dimBg = new ColorRect
        {
            Color = new Color(0.15f, 0.05f, 0.05f, 0.85f),
            AnchorsPreset = (int)LayoutPreset.FullRect,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _resetOverlay.AddChild(dimBg);

        // Centered panel
        var panel = new PanelContainer
        {
            AnchorsPreset = (int)LayoutPreset.Center,
            AnchorLeft = 0.20f, AnchorRight = 0.80f,
            AnchorTop = 0.30f, AnchorBottom = 0.70f
        };
        var panelBg = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#2A1A18"),
            BorderColor = Color.FromHtml("#A03828"),
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8
        };
        panel.AddThemeStyleboxOverride("panel", panelBg);
        _resetOverlay.AddChild(panel);

        var panelVbox = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            SizeFlagsVertical = Control.SizeFlags.Fill
        };
        panel.AddChild(panelVbox);

        // Warning title
        var warnTitle = new Label
        {
            Text = "RESET PROGRESS",
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(0, 36)
        };
        ThemeTokens.ApplyHeaderFont(warnTitle, ThemeTokens.FontLargeBody);
        warnTitle.Modulate = Color.FromHtml("#D4442A"); // danger red
        panelVbox.AddChild(warnTitle);

        // Warning text
        var warnText = new Label
        {
            Text = "This will permanently delete ALL saved data:\n- Card collection\n- Runes and upgrades\n- Saved decks\n- Map progress\n- Profile\n\nThis cannot be undone.",
            AutowrapMode = TextServer.AutowrapMode.Word,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        ThemeTokens.ApplyBodyFont(warnText, ThemeTokens.FontBody);
        warnText.Modulate = Color.FromHtml("#E8DCC8");
        panelVbox.AddChild(warnText);

        // Instruction
        var instrLabel = new Label
        {
            Text = "Type RESET to confirm:",
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(0, 28)
        };
        ThemeTokens.ApplyBodyFont(instrLabel, ThemeTokens.FontSmall);
        instrLabel.Modulate = Color.FromHtml("#C9A84C");
        panelVbox.AddChild(instrLabel);

        // Line edit for typed confirm
        _resetInput = new LineEdit
        {
            PlaceholderText = "type RESET here",
            CustomMinimumSize = new Vector2(0, 36),
            MaxLength = 10,
            SizeFlagsHorizontal = Control.SizeFlags.Fill
        };
        _resetInput.AddThemeColorOverride("font_color", Color.FromHtml("#E8DCC8"));
        _resetInput.AddThemeColorOverride("placeholder_color", Color.FromHtml("#5A5048"));
        _resetInput.AddThemeColorOverride("background_color", Color.FromHtml("#3A3530"));
        var inputBorder = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#3A3530"),
            BorderColor = Color.FromHtml("#5A5048"),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        };
        _resetInput.AddThemeStyleboxOverride("normal", inputBorder);
        _resetInput.AddThemeStyleboxOverride("focus", inputBorder);
        _resetInput.TextChanged += _ => OnResetTextChanged();
        panelVbox.AddChild(_resetInput);

        // Error label (hidden by default)
        _resetError = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(0, 22),
            MouseFilter = MouseFilterEnum.Ignore
        };
        ThemeTokens.ApplyBodyFont(_resetError, ThemeTokens.FontSmall);
        _resetError.Modulate = Color.FromHtml("#D4442A");
        panelVbox.AddChild(_resetError);

        // Button row
        var btnHbox = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            CustomMinimumSize = new Vector2(0, 44)
        };
        panelVbox.AddChild(btnHbox);

        var cancelBtn = MakeStoneButton("Cancel");
        cancelBtn.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        cancelBtn.Pressed += DismissResetConfirm;
        btnHbox.AddChild(cancelBtn);

        btnHbox.AddChild(new Control { CustomMinimumSize = new Vector2(12, 0) });

        _resetConfirmBtn = MakeStoneButton("Delete Everything");
        _resetConfirmBtn.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        _resetConfirmBtn.Disabled = true;
        _resetConfirmBtn.Modulate = Color.FromHtml("#5A4038"); // dimmed
        _resetConfirmBtn.Pressed += ExecuteResetProgress;
        btnHbox.AddChild(_resetConfirmBtn);

        _resetInput.GrabFocus();
    }

    private void OnResetTextChanged()
    {
        if (_resetInput == null || _resetConfirmBtn == null || _resetError == null) return;

        bool matches = _resetInput.Text.Trim().ToUpperInvariant() == "RESET";
        _resetConfirmBtn.Disabled = !matches;
        if (matches)
        {
            _resetError.Text = "";
            _resetConfirmBtn.Modulate = Color.FromHtml("#D4442A"); // danger red when enabled
        }
        else if (_resetInput.Text.Length >= 3)
        {
            _resetError.Text = "Type RESET exactly to enable";
        }
        else
        {
            _resetError.Text = "";
        }
    }

    private void DismissResetConfirm()
    {
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
        if (_resetOverlay != null && IsInstanceValid(_resetOverlay))
        {
            RemoveChild(_resetOverlay);
            _resetOverlay.QueueFree();
            _resetOverlay = null;
        }
        _resetInput = null;
        _resetConfirmBtn = null;
        _resetError = null;
    }

    private void ExecuteResetProgress()
    {
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");

        GD.Print("[Settings] Resetting all progress...");

        try
        {
            // Wipe in-memory progression — reset to fresh defaults
            var fresh = new ProgressionState();
            CampaignContext.SaveManager.State.Version = fresh.Version;
            CampaignContext.SaveManager.State.Shards = fresh.Shards;
            CampaignContext.SaveManager.State.DigCharges = fresh.DigCharges;
            CampaignContext.SaveManager.State.RuneDust = fresh.RuneDust;
            CampaignContext.SaveManager.State.HasCompletedTutorial = fresh.HasCompletedTutorial;
            CampaignContext.SaveManager.State.GlobalDiscoveryIndex = fresh.GlobalDiscoveryIndex;
            CampaignContext.SaveManager.State.ClearedNodes.Clear();
            CampaignContext.SaveManager.State.Collection.Clear();
            CampaignContext.SaveManager.State.Fragments.Clear();
            CampaignContext.SaveManager.State.OwnedRuneIds.Clear();
            CampaignContext.SaveManager.State.SeenCardIds.Clear();
            CampaignContext.SaveManager.State.UnlockedTools.Clear();
            CampaignContext.SaveManager.State.DiscoveredRelics.Clear();
            CampaignContext.SaveManager.State.DeckCardIds.Clear();
            CampaignContext.SaveManager.State.SavedDecks.Clear();
            // RuneSlotUnlockCounts/RuneUpgradeTiers were removed from ProgressionState (v4→v5)
            CampaignContext.SaveManager.State.SavedRunePageJson = "";
            CampaignContext.SaveManager.State.Tutorial = null;
            CampaignContext.Progression.Collection.Clear();

            // Persist empty state to disk (replaces DB content)
            CampaignContext.SaveManager.Save();

            // Reset settings, keep current audio prefs but reset IntroSeen
            var s = CampaignContext.Settings;
            s.IntroSeen = false;
            CampaignContext.SaveManager.SaveSettings(s);

            // Clear deck library
            CampaignContext.DeckLibrary.Clear();
            CampaignContext.SaveDeckLibrary();

            // Clear campaign profiles
            CampaignContext.Profiles.Clear();
            CampaignContext.ActiveProfileSlot = -1;
            CampaignContext.ChosenClass = "";
            CampaignContext.ChosenTown = "";
            CampaignContext.SaveCampaignProfile();

            GD.Print("[Settings] Progress reset complete — all save data cleared.");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[Settings] Reset progress failed: {ex.Message}");
        }

        // Dismiss overlay
        DismissResetConfirm();

        // Show feedback toast
        var toast = new Label
        {
            Text = "Progress reset. Intro will play on next launch.",
            AnchorsPreset = (int)LayoutPreset.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ThemeTokens.ApplyBodyFont(toast, ThemeTokens.FontLargeBody);
        toast.Modulate = Color.FromHtml("#C9A84C");
        AddChild(toast);

        var toastTimer = GetTree().CreateTimer(3.0f);
        toastTimer.Timeout += () =>
        {
            if (IsInstanceValid(toast))
            {
                RemoveChild(toast);
                toast.QueueFree();
            }
        };
    }

    // ── Event handlers ──────────────────────────────────────────────

    private void LoadCurrentSettings()
    {
        var s = CampaignContext.Settings;
        if (_musicSlider != null) _musicSlider.Value = (int)(s.MusicVolume * 100);
        if (_sfxSlider != null) _sfxSlider.Value = (int)(s.SfxVolume * 100);
        if (_ambientSlider != null) _ambientSlider.Value = (int)(s.AmbientVolume * 100);
        if (_muteToggle != null) _muteToggle.ButtonPressed = s.MasterMute;

        // Apply saved graphics quality
        ApplyGraphicsQuality(s.GraphicsQuality);
    }

    private void OnVolumeChanged(double value)
    {
        _dirty = true;
        // Apply live to bus
        var s = CampaignContext.Settings;
        if (_musicSlider != null) s.MusicVolume = (float)_musicSlider.Value / 100f;
        if (_sfxSlider != null) s.SfxVolume = (float)_sfxSlider.Value / 100f;
        if (_ambientSlider != null) s.AmbientVolume = (float)_ambientSlider.Value / 100f;
        ApplyAudioSettings(s);
    }

    private void OnMuteToggled(bool muted)
    {
        _dirty = true;
        var s = CampaignContext.Settings;
        s.MasterMute = muted;
        ApplyAudioSettings(s);
    }

    private void OnReplayIntro()
    {
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");

        var s = CampaignContext.Settings;
        s.IntroSeen = false;
        if (CampaignContext.SaveManager != null)
        {
            CampaignContext.SaveManager.SaveSettings(s);
        }
        GD.Print("[Settings] IntroSeen reset to false — intro will show on next launch.");
    }

    private void OnSavePressed()
    {
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
        if (!_dirty) return;
        var s = CampaignContext.Settings;
        if (_musicSlider != null) s.MusicVolume = (float)_musicSlider.Value / 100f;
        if (_sfxSlider != null) s.SfxVolume = (float)_sfxSlider.Value / 100f;
        if (_ambientSlider != null) s.AmbientVolume = (float)_ambientSlider.Value / 100f;
        if (_muteToggle != null) s.MasterMute = _muteToggle.ButtonPressed;

        CampaignContext.SaveManager!.SaveSettings(s);
        ApplyAudioSettings(s);
        _dirty = false;

        GD.Print("[Settings] Saved and applied.");
    }

    private void ApplyGraphicsQuality(bool highQuality)
    {
        var filter = highQuality
            ? Viewport.DefaultCanvasItemTextureFilter.Linear
            : Viewport.DefaultCanvasItemTextureFilter.Nearest;
        // Apply to the main viewport — affects all 2D rendering
        var tree = GetTree();
        if (tree != null && tree.Root != null)
        {
            tree.Root.CanvasItemDefaultTextureFilter = filter;
            GD.Print($"[Settings] Viewport texture filter set to {(highQuality ? "Linear" : "Nearest")}");
        }
    }

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
}
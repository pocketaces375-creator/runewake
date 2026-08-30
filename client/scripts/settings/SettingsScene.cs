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
    private Label? _creditsLabel;
    private Button? _backBtn;

    // Stored pre-save so we only write to disk on explicit Save
    private bool _dirty;

    public override void _Ready()
    {
        BuildUI();
        LoadCurrentSettings();
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

        // ═══ REPLAY INTRO ═══
        var replayBtn = MakeStoneButton("Replay Intro");
        replayBtn.Pressed += OnReplayIntro;
        vbox.AddChild(replayBtn);

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

        // ═══ CREDITS ═══
        _creditsLabel = new Label
        {
            Text = "Runewake: The Buried Age\nA fantasy TCG by Nimbus Digital\n\nAudio credits: content/audio/AUDIO_CREDITS.md",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            AutowrapMode = TextServer.AutowrapMode.Word,
            CustomMinimumSize = new Vector2(0, 80)
        };
        ThemeTokens.ApplyBodyFont(_creditsLabel, ThemeTokens.FontSmall);
        _creditsLabel.Modulate = Color.FromHtml("#8A7D6B"); // muted
        vbox.AddChild(_creditsLabel);

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
        _backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn");
        btnHbox.AddChild(_backBtn);
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

    // ── Event handlers ──────────────────────────────────────────────

    private void LoadCurrentSettings()
    {
        var s = CampaignContext.Settings;
        if (_musicSlider != null) _musicSlider.Value = (int)(s.MusicVolume * 100);
        if (_sfxSlider != null) _sfxSlider.Value = (int)(s.SfxVolume * 100);
        if (_ambientSlider != null) _ambientSlider.Value = (int)(s.AmbientVolume * 100);
        if (_muteToggle != null) _muteToggle.ButtonPressed = s.MasterMute;
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
        CampaignContext.Settings.IntroSeen = false;
        CampaignContext.SaveManager!.SaveSettings(CampaignContext.Settings);
        GD.Print("[Settings] IntroSeen reset — next launch will show intro");
    }

    private void OnSavePressed()
    {
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
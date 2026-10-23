using Godot;
using System.Linq;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Settings — FABLE-039 rebuild.
///
/// Trikzos: "the UI is so wonky… make it a super clean and professional looking settings page."
///
/// One dark-glass panel over the dimmed hero art, two columns:
///   SOUND     master / music / effects / ambience sliders (gold fill, round grabber, % readout)
///             and a mute switch
///   GAMEPLAY  reduce-motion switch
///   HELP & STORY  replay the tutorial, replay the intro, audio credits (list rows with a chevron)
///   reset all progress, in ember, set apart at the bottom
///
/// Changes apply at once and save themselves (on slider release, on every switch, and when the
/// screen closes) — no Save button to forget. The old "Graphics Quality: Low" switch is gone:
/// "Low" set the whole viewport to NEAREST filtering the moment this screen opened (it was the
/// default), which made every card and portrait crunchy for the rest of the session.
/// </summary>
public partial class SettingsScene : Control
{
    // palette
    private static readonly Color Gold = Color.FromHtml("#C9A84C");
    private static readonly Color GoldBright = Color.FromHtml("#E8C96A");
    private static readonly Color Cream = Color.FromHtml("#EDE2CC");
    private static readonly Color Muted = Color.FromHtml("#9C8F78");
    private static readonly Color Rule = new(0.79f, 0.66f, 0.30f, 0.22f);
    private static readonly Color EmberText = Color.FromHtml("#E0735A");

    // ── Controls ────────────────────────────────────────────────────
    private HSlider? _masterSlider, _musicSlider, _sfxSlider, _ambientSlider;
    private SettingsSwitch? _muteSwitch, _motionSwitch;
    private Button? _backBtn;
    private Label? _status;

    private Control? _creditsOverlay;
    private Control? _resetOverlay;
    private LineEdit? _resetInput;
    private Button? _resetConfirmBtn;
    private Label? _resetError;

    private bool _dirty;
    private bool _loading;
    private float _statusFade;

    public override void _Ready()
    {
        SceneGuard.Build(this, "SettingsScene", BuildUI, "res://scenes/main/Main.tscn", "Back to title");
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
                    string baseName = CampaignContext.WideCaptureMode ? "settings_test_wide" : "settings_test";
                    string path = ProjectPaths.Artifacts + $"/captures/{baseName}.png";
                    image.SavePng(path);
                    DebugCapture.WriteLayoutJson(this, baseName);
                    GD.Print($"[SettingsScene] Captured to {path}");
                    DebugCapture.DumpLayoutJSON(baseName, this);
                }
                GetTree().Quit(0);
            };
        }
    }

    public override void _ExitTree()
    {
        if (_dirty) Persist();
    }

    public override void _Process(double delta)
    {
        if (_status == null || _statusFade <= 0) return;
        _statusFade -= (float)delta;
        _status.Modulate = new Color(1, 1, 1, Mathf.Clamp(_statusFade / 0.6f, 0f, 1f));
    }

    // ════════════════════════════════════════════════════════════════
    //  Layout
    // ════════════════════════════════════════════════════════════════

    private void BuildUI()
    {
        var vp = GetViewportRect().Size;
        if (ScreenSkin.Variant("SettingsScene") == "tree") { BuildTreeUI(vp); return; }
        if (ScreenSkin.Faithful("SettingsScene")) { BuildArtUI(vp); return; }

        var bg = new ColorRect { Color = Color.FromHtml("#1A1714"), MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        const string heroPath = "res://content/art/title/hero_art.png";
        if (ResourceLoader.Exists(heroPath))
        {
            var hero = new TextureRect
            {
                Texture = GD.Load<Texture2D>(heroPath),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Modulate = new Color(0.55f, 0.55f, 0.55f, 0.60f),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            hero.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(hero);
        }
        var veil = new ColorRect { Color = new Color(0.03f, 0.025f, 0.02f, 0.40f), MouseFilter = MouseFilterEnum.Ignore };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(veil);

        // ── Header: SETTINGS between two short ornament rules ──
        var title = new Label
        {
            Text = "SETTINGS",
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(0, 34), Size = new Vector2(vp.X, 100),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        title.AddThemeFontOverride("font", ThemeTokens.GetHeaderFont(78));
        title.AddThemeFontSizeOverride("font_size", 78);
        title.AddThemeColorOverride("font_color", Gold);
        AddChild(title);
        float titleHalf = 290f, ruleLen = 260f, ruleY = 84f;
        AddChild(new OrnamentRule { Position = new Vector2(vp.X / 2 - titleHalf - ruleLen, ruleY - 10), Size = new Vector2(ruleLen, 20), PointRight = true });
        AddChild(new OrnamentRule { Position = new Vector2(vp.X / 2 + titleHalf, ruleY - 10), Size = new Vector2(ruleLen, 20), PointRight = false });

        // ── The glass panel ──
        float panelW = Mathf.Min(1680f, vp.X - 80f);
        const float panelTop = 160f, panelBottomGap = 150f;
        var panel = new PanelContainer
        {
            Position = new Vector2((vp.X - panelW) / 2, panelTop),
            Size = new Vector2(panelW, vp.Y - panelTop - panelBottomGap),
            MouseFilter = MouseFilterEnum.Stop,
        };
        panel.AddThemeStyleboxOverride("panel", Glass());
        AddChild(panel);

        var cols = new HBoxContainer();
        cols.AddThemeConstantOverride("separation", 0);
        panel.AddChild(cols);

        // left column — SOUND
        var left = Column();
        cols.AddChild(left);
        SectionHeader(left, "SOUND");
        _masterSlider = SliderRow(left, "Master");
        _musicSlider = SliderRow(left, "Music");
        _sfxSlider = SliderRow(left, "Effects");
        _ambientSlider = SliderRow(left, "Ambience");
        Spacer(left, 6);
        _muteSwitch = SwitchRow(left, "Mute all sound", null, OnMuteToggled);
        // FABLE-055: pick the title music (tap to hear the next one)
        Label? musicLabel = null;
        musicLabel = ActionRow(left, "Title music:  " + AudioManager.TitleTrackName(AudioManager.TitleTrack), false, () =>
        {
            Click();
            var list = AudioManager.TitleTracks;
            int i = System.Array.FindIndex(list, t => t.Id == AudioManager.TitleTrack);
            var next = list[(i + 1) % list.Length];
            AudioManager.TitleTrack = next.Id;
            musicLabel!.Text = "Title music:  " + next.Name;
            GetNodeOrNull<AudioManager>("/root/AudioManager")?.CrossfadeMusic(next.Id, 1.0f);   // hear it now
        });

        // divider
        cols.AddChild(new ColorRect { Color = Rule, CustomMinimumSize = new Vector2(2, 0), SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });

        // right column — GAMEPLAY, HELP & STORY, reset
        var right = Column();
        cols.AddChild(right);
        SectionHeader(right, "GAMEPLAY");
        _motionSwitch = SwitchRow(right, "Reduce motion", "Fewer flashes, shakes and sweeps", on =>
        {
            CampaignContext.Settings.ReduceMotion = on;
            MarkChanged(true);
        });
        // FABLE-067: how cards stand out from the board
        Label? styleLabel = null;
        styleLabel = ActionRow(right, "Card style:  " + CardContrast.Names[Math.Clamp(CampaignContext.Settings.CardStyle, 0, 3)], false, () =>
        {
            Click();
            CampaignContext.Settings.CardStyle = (CampaignContext.Settings.CardStyle + 1) % 4;
            if (styleLabel != null) styleLabel.Text = "Card style:  " + CardContrast.Names[CampaignContext.Settings.CardStyle];
            MarkChanged(true);
        });
        Spacer(right, 14);
        SectionHeader(right, "HELP & STORY");
        ActionRow(right, "Symbol key — what every sign in a fight means", false, () => { Click(); if (_dirty) Persist(); LoadScreen.Go(GetTree(), SymbolKeyScene.ScenePath); });
        ActionRow(right, "Replay the tutorial", false, OnReplayTutorial);
        ActionRow(right, "Replay the intro", false, OnReplayIntro);
        ActionRow(right, "Audio credits", false, ShowCreditsOverlay);
        ActionRow(right, "Save check — is my progress safe?", false, ShowSaveCheck);
        right.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        ActionRow(right, "Reset all progress", true, ShowResetConfirm);

        // ── Footer ──
        _backBtn = Plate("◂  Back", false, 300, 84);
        _backBtn.Position = new Vector2((vp.X - panelW) / 2, vp.Y - 118);
        _backBtn.Pressed += () =>
        {
            Click();
            if (_dirty) Persist();
            LoadScreen.Go(GetTree(), "res://scenes/main/Main.tscn");
        };
        AddChild(_backBtn);

        _status = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(vp.X / 2 - 500, vp.Y - 118), Size = new Vector2(1000, 84),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _status.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(30));
        _status.AddThemeFontSizeOverride("font_size", 30);
        _status.AddThemeColorOverride("font_color", Muted);
        AddChild(_status);

        var versionLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(vp.X - (vp.X - panelW) / 2 - 600, vp.Y - 118), Size = new Vector2(600, 84),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        versionLabel.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(24));
        versionLabel.AddThemeFontSizeOverride("font_size", 24);
        versionLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.50f, 0.42f));
        versionLabel.Text = $"Runewake {VersionString()}";
        AddChild(versionLabel);
    }

    // ── FABLE-088: Trikzos's picked Settings painting is the screen; every row sits on its painted strip ──
    private bool _art;
    private bool _words;   // FABLE-097: the painting keeps its own static lettering; the game draws only live values
    private float _artRowH = 76f;
    /// <summary>A label the painting already shows: kept for layout, not drawn.</summary>
    private void Painted(Control c) { if (_words) c.Modulate = new Color(1, 1, 1, 0); }

    private Vector2 A(Vector2 vp, float fx, float fy) => ScreenSkin.Art("SettingsScene", vp, fx, fy);

    private VBoxContainer Slot(Vector2 vp, float x0, float y0, float x1, float y1)
    {
        var a = A(vp, x0, y0); var b = A(vp, x1, y1);
        var v = new VBoxContainer { Position = a, Size = b - a, Alignment = BoxContainer.AlignmentMode.Center };
        v.AddThemeConstantOverride("separation", 0);
        AddChild(v);
        _artRowH = b.Y - a.Y;
        return v;
    }

    private void BuildArtUI(Vector2 vp)
    {
        _art = true;
        _words = ScreenSkin.Words("SettingsScene");
        var bg = new ColorRect { Color = Color.FromHtml("#1A1714"), MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);   // hidden by the painting (ScreenSkin), shows only if it fails to load

        var t0 = A(vp, 0.37f, 0.218f); var t1 = A(vp, 0.63f, 0.288f);
        var title = new Label { Text = "SETTINGS", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Position = t0, Size = t1 - t0, MouseFilter = MouseFilterEnum.Ignore };
        title.AddThemeFontOverride("font", ThemeTokens.GetHeaderFont(56));
        title.AddThemeFontSizeOverride("font_size", 56);
        title.AddThemeColorOverride("font_color", Gold);
        AddChild(title);
        Painted(title);

        float[] sliderY = { 0.385f, 0.459f, 0.533f, 0.607f };
        _masterSlider = SliderRow(Slot(vp, 0.180f, sliderY[0] - 0.034f, 0.488f, sliderY[0] + 0.034f), "Master");
        _musicSlider = SliderRow(Slot(vp, 0.180f, sliderY[1] - 0.034f, 0.488f, sliderY[1] + 0.034f), "Music");
        _sfxSlider = SliderRow(Slot(vp, 0.180f, sliderY[2] - 0.034f, 0.488f, sliderY[2] + 0.034f), "Effects");
        _ambientSlider = SliderRow(Slot(vp, 0.180f, sliderY[3] - 0.034f, 0.488f, sliderY[3] + 0.034f), "Ambience");
        _muteSwitch = SwitchRow(Slot(vp, 0.180f, 0.648f, 0.488f, 0.712f), "Mute all sound", null, OnMuteToggled);
        Label? musicLabel = null;
        musicLabel = ActionRow(Slot(vp, 0.180f, 0.727f, 0.484f, 0.781f), "Title music:  " + AudioManager.TitleTrackName(AudioManager.TitleTrack), false, () =>
        {
            Click();
            var list = AudioManager.TitleTracks;
            int i = System.Array.FindIndex(list, t => t.Id == AudioManager.TitleTrack);
            var next = list[(i + 1) % list.Length];
            AudioManager.TitleTrack = next.Id;
            musicLabel!.Text = "Title music:  " + next.Name;
            GetNodeOrNull<AudioManager>("/root/AudioManager")?.CrossfadeMusic(next.Id, 1.0f);
        });

        _motionSwitch = SwitchRow(Slot(vp, 0.522f, 0.364f, 0.808f, 0.420f), "Reduce motion", null, on =>
        {
            CampaignContext.Settings.ReduceMotion = on;
            MarkChanged(true);
        });
        Label? styleLabel = null;
        styleLabel = ActionRow(Slot(vp, 0.522f, 0.420f, 0.808f, 0.472f), "Card style:  " + CardContrast.Names[Math.Clamp(CampaignContext.Settings.CardStyle, 0, 3)], false, () =>
        {
            Click();
            CampaignContext.Settings.CardStyle = (CampaignContext.Settings.CardStyle + 1) % 4;
            if (styleLabel != null) styleLabel.Text = "Card style:  " + CardContrast.Names[CampaignContext.Settings.CardStyle];
            MarkChanged(true);
        });
        (string, Action)[] help =
        {
            ("Symbol key — what every sign in a fight means", () => { Click(); if (_dirty) Persist(); LoadScreen.Go(GetTree(), SymbolKeyScene.ScenePath); }),
            ("Replay the tutorial", OnReplayTutorial), ("Replay the intro", OnReplayIntro),
            ("Audio credits", ShowCreditsOverlay), ("Save check — is my progress safe?", ShowSaveCheck),
        };
        for (int i = 0; i < help.Length; i++)
        {
            float y = 0.548f + i * 0.0555f;
            Painted(ActionRow(Slot(vp, 0.522f, y - 0.027f, 0.808f, y + 0.027f), help[i].Item1, false, help[i].Item2).GetParent<Control>());
        }
        Painted(ActionRow(Slot(vp, 0.522f, 0.822f, 0.806f, 0.878f), "Reset all progress", true, ShowResetConfirm).GetParent<Control>());

        var b0 = A(vp, 0.192f, 0.806f); var b1 = A(vp, 0.300f, 0.872f);
        _backBtn = Plate("◂  Back", false, b1.X - b0.X, b1.Y - b0.Y);
        _backBtn.AddThemeFontSizeOverride("font_size", 26);
        _backBtn.Position = b0;
        _backBtn.Pressed += () =>
        {
            Click();
            if (_dirty) Persist();
            LoadScreen.Go(GetTree(), "res://scenes/main/Main.tscn");
        };
        AddChild(_backBtn);
        // FABLE-091: the painting already has a bronze frame here - the word sits in it (no second plaque inside)
        _backBtn.AddThemeFontSizeOverride("font_size", 32);
        _backBtn.AddThemeColorOverride("font_color", GoldBright);
        StyleBoxFlat Wood(Color bg, Color edge) => new()
        {
            BgColor = bg, BorderColor = edge, BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ShadowColor = new Color(0, 0, 0, 0.45f), ShadowSize = 6,
        };
        if (false)   // (the v8 painting has no Back plaque of its own: the bronze button below is drawn in both modes)
        {
            _backBtn.AddThemeStyleboxOverride("normal", Wood(new Color(0, 0, 0, 0), new Color(0, 0, 0, 0)));
            _backBtn.AddThemeStyleboxOverride("hover", Wood(new Color(1f, 0.86f, 0.45f, 0.14f), new Color(0.90f, 0.74f, 0.42f, 0.5f)));
            _backBtn.AddThemeStyleboxOverride("pressed", Wood(new Color(0, 0, 0, 0.28f), new Color(0, 0, 0, 0)));
            _backBtn.AddThemeColorOverride("font_color", new Color(0, 0, 0, 0)); _backBtn.AddThemeColorOverride("font_hover_color", new Color(0, 0, 0, 0));
            _backBtn.AddThemeColorOverride("font_pressed_color", new Color(0, 0, 0, 0));
        }
        else
        {
            _backBtn.AddThemeStyleboxOverride("normal", Wood(new Color(0.13f, 0.085f, 0.05f, 0.92f), new Color(0.62f, 0.47f, 0.24f)));
            _backBtn.AddThemeStyleboxOverride("hover", Wood(new Color(0.20f, 0.13f, 0.07f, 0.95f), new Color(0.90f, 0.74f, 0.42f)));
            _backBtn.AddThemeStyleboxOverride("pressed", Wood(new Color(0.08f, 0.05f, 0.03f, 0.95f), new Color(0.62f, 0.47f, 0.24f)));
        }
        _backBtn.SetMeta("skinned", true);
        _backBtn.AddThemeColorOverride("font_hover_color", new Color(1f, 0.9f, 0.6f));

        var s0 = A(vp, 0.305f, 0.806f); var s1 = A(vp, 0.488f, 0.872f);
        _status = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Position = s0, Size = s1 - s0, MouseFilter = MouseFilterEnum.Ignore };
        _status.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(22));
        _status.AddThemeFontSizeOverride("font_size", 22);
        _status.AddThemeColorOverride("font_color", Cream);
        AddChild(_status);

        var v0 = A(vp, 0.520f, 0.872f); var v1 = A(vp, 0.806f, 0.892f);
        var versionLabel = new Label { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Position = v0, Size = v1 - v0,
            MouseFilter = MouseFilterEnum.Ignore, Text = $"Runewake {VersionString()}" };
        versionLabel.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(17));
        versionLabel.AddThemeFontSizeOverride("font_size", 17);
        versionLabel.AddThemeColorOverride("font_color", new Color(0.62f, 0.56f, 0.46f));
        AddChild(versionLabel);
    }

    private static string VersionString()
    {
        string version = ProjectSettings.GetSetting("application/config/version", "dev").AsString();
        string hash = "";
        try
        {
            if (Godot.FileAccess.FileExists("res://content/misc/build_info.txt"))
            {
                var lines = Godot.FileAccess.GetFileAsString("res://content/misc/build_info.txt").Split('\n');
                if (lines.Length >= 2 && lines[1].StartsWith("sha:"))
                    hash = lines[1].Substring(4, Math.Min(8, lines[1].Length - 4)).Trim();
            }
        }
        catch { /* best-effort */ }
        return string.IsNullOrEmpty(hash) ? $"v{version}" : $"v{version} · {hash}";
    }

    // ── building blocks ─────────────────────────────────────────────

    private static StyleBoxFlat Glass(Color? border = null, float alpha = 0.84f, int margin = 0)
    {
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.082f, 0.072f, 0.061f, alpha),
            BorderColor = border ?? new Color(0.79f, 0.66f, 0.30f, 0.38f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 18, CornerRadiusTopRight = 18, CornerRadiusBottomLeft = 18, CornerRadiusBottomRight = 18,
            ShadowColor = new Color(0, 0, 0, 0.55f), ShadowSize = 28,
            ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin, ContentMarginBottom = margin,
        };
        return box;
    }

    private static VBoxContainer Column()
    {
        var margin = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("separation", 0);
        // inner padding via a MarginContainer would nest one more level; a VBox with side
        // padding on each row keeps the tree flat and the rows full-width for touch.
        return margin;
    }

    private const float Pad = 56f;

    private static void Spacer(VBoxContainer col, float h) =>
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, h), MouseFilter = MouseFilterEnum.Ignore });

    private static void SectionHeader(VBoxContainer col, string text)
    {
        var wrap = new MarginContainer { CustomMinimumSize = new Vector2(0, 92) };
        wrap.AddThemeConstantOverride("margin_left", (int)Pad);
        wrap.AddThemeConstantOverride("margin_right", (int)Pad);
        wrap.AddThemeConstantOverride("margin_top", 34);
        col.AddChild(wrap);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 10);
        wrap.AddChild(v);
        var l = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", ThemeTokens.GetHeaderFont(30));
        l.AddThemeFontSizeOverride("font_size", 30);
        l.AddThemeColorOverride("font_color", Gold);
        l.AddThemeConstantOverride("outline_size", 0);
        v.AddChild(l);
        v.AddChild(new ColorRect { Color = Rule, CustomMinimumSize = new Vector2(0, 1), MouseFilter = MouseFilterEnum.Ignore });
    }

    private static Label RowLabel(string text, int size = 34, Color? colour = null)
    {
        var l = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(size));
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", colour ?? Cream);
        return l;
    }

    private HSlider SliderRow(VBoxContainer col, string label)
    {
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, _art ? _artRowH : 88) };
        row.AddThemeConstantOverride("separation", _art ? 14 : 24);
        var wrap = new MarginContainer();
        wrap.AddThemeConstantOverride("margin_left", _art ? 10 : (int)Pad);
        wrap.AddThemeConstantOverride("margin_right", _art ? 6 : (int)Pad);
        wrap.AddChild(row);
        col.AddChild(wrap);

        var name = RowLabel(label, _art ? 28 : 34);
        name.CustomMinimumSize = new Vector2(_art ? 140 : 190, 0);
        Painted(name);
        row.AddChild(name);

        var slider = new HSlider
        {
            MinValue = 0, MaxValue = 100, Step = 1,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(0, 56),
            FocusMode = FocusModeEnum.None,
            Scrollable = false,
        };
        StyleSlider(slider);
        row.AddChild(slider);

        var value = RowLabel("100%", _art ? 24 : 30, _art ? Cream : Muted);
        value.CustomMinimumSize = new Vector2(_art ? 66 : 92, 0);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(value);

        slider.ValueChanged += v =>
        {
            value.Text = $"{(int)v}%";
            OnVolumeChanged();
        };
        slider.DragEnded += _ => { if (_dirty) Persist(); };
        return slider;
    }

    private SettingsSwitch SwitchRow(VBoxContainer col, string label, string? hint, Action<bool> onToggle)
    {
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, _art ? _artRowH : hint == null ? 88 : 104) };
        row.AddThemeConstantOverride("separation", 24);
        var wrap = new MarginContainer();
        wrap.AddThemeConstantOverride("margin_left", _art ? 12 : (int)Pad);
        wrap.AddThemeConstantOverride("margin_right", _art ? 12 : (int)Pad);
        wrap.AddChild(row);
        col.AddChild(wrap);

        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        text.AddThemeConstantOverride("separation", 0);
        var swLabel = RowLabel(label, _art ? 28 : 34); Painted(swLabel);
        text.AddChild(swLabel);
        if (hint != null) { var h = RowLabel(hint, 26, Muted); Painted(h); text.AddChild(h); }
        row.AddChild(text);

        var sw = new SettingsSwitch { SizeFlagsVertical = SizeFlags.ShrinkCenter, Brass = _art };
        sw.Toggled += on => { if (!_loading) { Click(); onToggle(on); } };
        row.AddChild(sw);

        // the whole row is the hit area
        wrap.MouseFilter = MouseFilterEnum.Stop;
        var rowTap = new TapGuard();   // FABLE-095: one tap is one flip (a touch arrives twice: as a touch and as an emulated click)
        wrap.GuiInput += e =>
        {
            if (!rowTap.AcceptRelease(e)) return;
            var at = e is InputEventMouseButton mb ? mb.GlobalPosition : e is InputEventScreenTouch st ? st.Position : Vector2.Zero;
            if (!sw.GetGlobalRect().HasPoint(at)) sw.Flip();
        };
        return sw;
    }

    private Label ActionRow(VBoxContainer col, string label, bool danger, Action onPress)
    {
        var wrap = new MarginContainer();
        wrap.AddThemeConstantOverride("margin_left", _art ? 0 : (int)(Pad - 20));
        wrap.AddThemeConstantOverride("margin_right", _art ? 0 : (int)(Pad - 20));
        wrap.AddThemeConstantOverride("margin_bottom", danger && !_art ? 30 : 0);
        col.AddChild(wrap);

        var b = new Button
        {
            Text = "", CustomMinimumSize = new Vector2(0, _art ? _artRowH : 80), FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        var normal = new StyleBoxFlat
        {
            BgColor = danger ? new Color(0.42f, 0.12f, 0.08f, 0.16f) : new Color(1, 1, 1, 0.0f),
            BorderColor = danger ? new Color(0.88f, 0.45f, 0.35f, 0.45f) : new Color(0, 0, 0, 0),
            BorderWidthLeft = danger ? 1 : 0, BorderWidthRight = danger ? 1 : 0, BorderWidthTop = danger ? 1 : 0, BorderWidthBottom = danger ? 1 : 0,
            CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12, CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = danger ? new Color(0.50f, 0.14f, 0.09f, 0.30f) : new Color(0.79f, 0.66f, 0.30f, 0.10f);
        var pressed = (StyleBoxFlat)normal.Duplicate();
        pressed.BgColor = danger ? new Color(0.55f, 0.15f, 0.10f, 0.42f) : new Color(0.79f, 0.66f, 0.30f, 0.18f);
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", pressed);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        wrap.AddChild(b);

        var inner = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        inner.SetAnchorsPreset(LayoutPreset.FullRect);
        inner.OffsetLeft = 20; inner.OffsetRight = -20;
        b.AddChild(inner);
        var l = RowLabel(label, _art ? 26 : 34, danger ? (_art ? Cream : EmberText) : Cream);
        l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        inner.AddChild(l);
        inner.AddChild(RowLabel("›", _art ? 34 : 44, danger ? (_art ? Cream : EmberText) : Gold));

        b.Pressed += () => onPress();
        return l;
    }

    /// <summary>FABLE-096: Settings on the World-Tree night-sky painting Trikzos picked (093): every row on its painted strip.</summary>
    private void BuildTreeUI(Vector2 vp)
    {
        _art = true;
        var bg = new ColorRect { Color = Color.FromHtml("#1A1714"), MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        Label Word(string text, float x0, float y0, float x1, float y1, int px, Color col, HorizontalAlignment align = HorizontalAlignment.Center)
        {
            var a = A(vp, x0, y0); var b = A(vp, x1, y1);
            var l = new Label { Text = text, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center, Position = a, Size = b - a, MouseFilter = MouseFilterEnum.Ignore };
            l.AddThemeFontOverride("font", ThemeTokens.GetHeaderFont(px)); l.AddThemeFontSizeOverride("font_size", px);
            l.AddThemeColorOverride("font_color", col);
            l.AddThemeConstantOverride("outline_size", 5); l.AddThemeColorOverride("font_outline_color", new Color(0.03f, 0.02f, 0.01f, 0.85f));
            l.SetMeta("skinned", true);
            AddChild(l);
            return l;
        }
        Word("SETTINGS", 0.407f, 0.023f, 0.592f, 0.091f, 52, Gold);
        Word("SOUND", 0.186f, 0.212f, 0.488f, 0.252f, 28, GoldBright, HorizontalAlignment.Left);
        Word("GAMEPLAY", 0.522f, 0.212f, 0.808f, 0.252f, 28, GoldBright, HorizontalAlignment.Left);
        Word("HELP & STORY", 0.522f, 0.455f, 0.808f, 0.497f, 28, GoldBright, HorizontalAlignment.Left);

        // the sound block: four sliders on the parchment, the mute switch on the wood below, the title music on its strip
        float[] sliderY = { 0.2956f, 0.3737f, 0.4518f, 0.5300f };
        _masterSlider = SliderRow(Slot(vp, 0.188f, sliderY[0] - 0.036f, 0.480f, sliderY[0] + 0.036f), "Master");
        _musicSlider = SliderRow(Slot(vp, 0.188f, sliderY[1] - 0.036f, 0.480f, sliderY[1] + 0.036f), "Music");
        _sfxSlider = SliderRow(Slot(vp, 0.188f, sliderY[2] - 0.036f, 0.480f, sliderY[2] + 0.036f), "Effects");
        _ambientSlider = SliderRow(Slot(vp, 0.188f, sliderY[3] - 0.036f, 0.480f, sliderY[3] + 0.036f), "Ambience");
        _muteSwitch = SwitchRow(Slot(vp, 0.184f, 0.582f, 0.486f, 0.648f), "Mute all sound", null, OnMuteToggled);
        Label? musicLabel = null;
        musicLabel = ActionRow(Slot(vp, 0.182f, 0.725f, 0.483f, 0.779f), "Title music:  " + AudioManager.TitleTrackName(AudioManager.TitleTrack), false, () =>
        {
            Click();
            var list = AudioManager.TitleTracks;
            int i = System.Array.FindIndex(list, t => t.Id == AudioManager.TitleTrack);
            var next = list[(i + 1) % list.Length];
            AudioManager.TitleTrack = next.Id;
            musicLabel!.Text = "Title music:  " + next.Name;
            GetNodeOrNull<AudioManager>("/root/AudioManager")?.CrossfadeMusic(next.Id, 1.0f);
        });

        _motionSwitch = SwitchRow(Slot(vp, 0.522f, 0.262f, 0.807f, 0.348f), "Reduce motion", "Fewer flashes, shakes and sweeps", on =>
        {
            CampaignContext.Settings.ReduceMotion = on;
            MarkChanged(true);
        });
        Label? styleLabel = null;
        styleLabel = ActionRow(Slot(vp, 0.522f, 0.362f, 0.807f, 0.419f), "Card style:  " + CardContrast.Names[Math.Clamp(CampaignContext.Settings.CardStyle, 0, 3)], false, () =>
        {
            Click();
            CampaignContext.Settings.CardStyle = (CampaignContext.Settings.CardStyle + 1) % 4;
            if (styleLabel != null) styleLabel.Text = "Card style:  " + CardContrast.Names[CampaignContext.Settings.CardStyle];
            MarkChanged(true);
        });
        (string, Action)[] help =
        {
            ("Symbol key — what every sign in a fight means", () => { Click(); if (_dirty) Persist(); LoadScreen.Go(GetTree(), SymbolKeyScene.ScenePath); }),
            ("Replay the tutorial", OnReplayTutorial), ("Replay the intro", OnReplayIntro),
            ("Audio credits", ShowCreditsOverlay), ("Save check — is my progress safe?", ShowSaveCheck),
        };
        for (int i = 0; i < help.Length; i++)
        {
            float y = 0.547f + i * 0.056f;
            ActionRow(Slot(vp, 0.524f, y - 0.027f, 0.805f, y + 0.027f), help[i].Item1, false, help[i].Item2);
        }
        ActionRow(Slot(vp, 0.527f, 0.822f, 0.806f, 0.881f), "Reset all progress", true, ShowResetConfirm);

        var b0 = A(vp, 0.153f, 0.844f); var b1 = A(vp, 0.292f, 0.902f);
        _backBtn = Plate("◂  Back", false, b1.X - b0.X, b1.Y - b0.Y);
        _backBtn.Position = b0;
        _backBtn.Pressed += () =>
        {
            Click();
            if (_dirty) Persist();
            LoadScreen.Go(GetTree(), "res://scenes/main/Main.tscn");
        };
        AddChild(_backBtn);
        _backBtn.AddThemeFontSizeOverride("font_size", 32);
        _backBtn.AddThemeColorOverride("font_color", GoldBright);
        _backBtn.AddThemeColorOverride("font_hover_color", new Color(1f, 0.9f, 0.6f));
        StyleBoxFlat Wood(Color bgc, Color edge) => new()
        {
            BgColor = bgc, BorderColor = edge, BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        };
        _backBtn.AddThemeStyleboxOverride("normal", Wood(new Color(0, 0, 0, 0), new Color(0, 0, 0, 0)));   // the painted plaque is the button
        _backBtn.AddThemeStyleboxOverride("hover", Wood(new Color(1f, 0.86f, 0.45f, 0.12f), new Color(0.90f, 0.74f, 0.42f, 0.6f)));
        _backBtn.AddThemeStyleboxOverride("pressed", Wood(new Color(0, 0, 0, 0.25f), new Color(0, 0, 0, 0)));
        _backBtn.SetMeta("skinned", true);

        var s0 = A(vp, 0.300f, 0.844f); var s1 = A(vp, 0.488f, 0.902f);
        _status = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Position = s0, Size = s1 - s0, MouseFilter = MouseFilterEnum.Ignore };
        _status.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(22));
        _status.AddThemeFontSizeOverride("font_size", 22);
        _status.AddThemeColorOverride("font_color", Cream);
        AddChild(_status);

        var v0 = A(vp, 0.520f, 0.888f); var v1 = A(vp, 0.806f, 0.915f);
        var versionLabel = new Label { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Position = v0, Size = v1 - v0,
            MouseFilter = MouseFilterEnum.Ignore, Text = $"Runewake {VersionString()}" };
        versionLabel.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(17));
        versionLabel.AddThemeFontSizeOverride("font_size", 17);
        versionLabel.AddThemeColorOverride("font_color", new Color(0.72f, 0.66f, 0.56f));
        AddChild(versionLabel);
    }

    private void StyleSlider(HSlider s)
    {
        if (_art) { StyleBrassSlider(s); return; }
        StyleBoxFlat Track(Color c) => new()
        {
            BgColor = c,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginTop = 5, ContentMarginBottom = 5,
        };
        var groove = Track(new Color(0.16f, 0.14f, 0.12f));
        groove.BorderColor = new Color(0.36f, 0.31f, 0.25f);
        groove.BorderWidthLeft = groove.BorderWidthRight = groove.BorderWidthTop = groove.BorderWidthBottom = 1;
        s.AddThemeStyleboxOverride("slider", groove);
        s.AddThemeStyleboxOverride("grabber_area", Track(Gold));
        s.AddThemeStyleboxOverride("grabber_area_highlight", Track(GoldBright));
        var knob = Knob(false);
        s.AddThemeIconOverride("grabber", knob);
        s.AddThemeIconOverride("grabber_highlight", Knob(true));
        s.AddThemeIconOverride("grabber_disabled", knob);
    }

    /// <summary>FABLE-091: the painting's look - a dark inset rail, a bronze fill and a brass knob.</summary>
    private static void StyleBrassSlider(HSlider s)
    {
        StyleBoxFlat Rail(Color c, Color border) => new()
        {
            BgColor = c, BorderColor = border, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
            ContentMarginTop = 4, ContentMarginBottom = 4,
        };
        var groove = Rail(new Color(0.09f, 0.065f, 0.045f), new Color(0.50f, 0.38f, 0.20f));
        groove.ShadowColor = new Color(0, 0, 0, 0.45f); groove.ShadowSize = 2;
        s.AddThemeStyleboxOverride("slider", groove);
        s.AddThemeStyleboxOverride("grabber_area", Rail(new Color(0.74f, 0.56f, 0.27f), new Color(0.93f, 0.78f, 0.45f)));
        s.AddThemeStyleboxOverride("grabber_area_highlight", Rail(new Color(0.84f, 0.65f, 0.32f), new Color(1f, 0.86f, 0.52f)));
        s.AddThemeIconOverride("grabber", Brass(false));
        s.AddThemeIconOverride("grabber_highlight", Brass(true));
        s.AddThemeIconOverride("grabber_disabled", Brass(false));
    }

    private static ImageTexture? _brass, _brassHi;

    /// <summary>A 42px brass knob: dark rim, warm body, light falling from the top left.</summary>
    internal static ImageTexture Brass(bool hi)
    {
        if (hi && _brassHi != null) return _brassHi;
        if (!hi && _brass != null) return _brass;
        const int n = 42;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        float c = (n - 1) / 2f, r = 17.5f;
        var lightP = new Vector2(c - 5, c - 6);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2(x, y);
                float d = p.DistanceTo(new Vector2(c, c));
                var px = new Color(0, 0, 0, Mathf.Clamp((r + 3f - p.DistanceTo(new Vector2(c, c + 2))) / 3f, 0, 1) * 0.45f);
                float a = Mathf.Clamp(r + 0.5f - d, 0, 1);
                if (a > 0)
                {
                    float lt = Mathf.Clamp(1f - p.DistanceTo(lightP) / (r * 1.6f), 0, 1);
                    var body = new Color(0.46f, 0.31f, 0.13f).Lerp(new Color(0.98f, 0.85f, 0.55f), lt * lt * 0.9f + 0.1f);
                    if (hi) body = body.Lightened(0.12f);
                    float rim = Mathf.Clamp((d - (r - 2.6f)) / 2.6f, 0, 1);
                    body = body.Lerp(new Color(0.24f, 0.15f, 0.06f), rim * 0.85f);
                    float groove = Mathf.Abs(d - r * 0.55f) < 0.8f ? 0.25f : 0f;   // a turned ring on the face
                    body = body.Darkened(groove);
                    px = px.Lerp(new Color(body.R, body.G, body.B, 1), a);
                }
                img.SetPixel(x, y, px);
            }
        var tex = ImageTexture.CreateFromImage(img);
        if (hi) _brassHi = tex; else _brass = tex;
        return tex;
    }

    private static ImageTexture? _knob, _knobHi;

    /// <summary>A 44px round grabber: cream face, gold ring, soft shadow — drawn once, cached.</summary>
    private static ImageTexture Knob(bool hi)
    {
        if (hi && _knobHi != null) return _knobHi;
        if (!hi && _knob != null) return _knob;
        const int n = 44;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        float c = (n - 1) / 2f, rOuter = 19.5f, rRing = 16.5f;
        var face = hi ? new Color(1f, 0.97f, 0.88f) : new Color(0.95f, 0.91f, 0.82f);
        var ring = hi ? GoldBright : Gold;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c - 1) * (y - c - 1));
                float shadowA = Mathf.Clamp((rOuter + 2.5f - d) / 3f, 0, 1) * 0.35f;
                var px = new Color(0, 0, 0, shadowA);
                float dd = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float aOuter = Mathf.Clamp(rOuter + 0.5f - dd, 0, 1);
                if (aOuter > 0)
                {
                    float tRing = Mathf.Clamp(rRing + 0.5f - dd, 0, 1);    // 1 inside the face
                    var col = ring.Lerp(face, tRing);
                    px = px.Lerp(new Color(col.R, col.G, col.B, 1), aOuter);
                }
                img.SetPixel(x, y, px);
            }
        var tex = ImageTexture.CreateFromImage(img);
        if (hi) _knobHi = tex; else _knob = tex;
        return tex;
    }

    private static Button Plate(string text, bool primary, float w, float h)
    {
        var b = new Button { Text = text, Size = new Vector2(w, h), CustomMinimumSize = new Vector2(w, h), FocusMode = FocusModeEnum.None };
        b.AddThemeFontOverride("font", ThemeTokens.GetButtonFont(32)); b.AddThemeFontSizeOverride("font_size", 32);
        b.AddThemeColorOverride("font_color", primary ? Color.FromHtml("#F2DFA6") : Color.FromHtml("#D8CBB0"));
        b.AddThemeColorOverride("font_disabled_color", new Color(0.45f, 0.42f, 0.36f));
        b.AddThemeStyleboxOverride("normal", primary ? MenuButtons.PrimaryNormal() : MenuButtons.QuietNormal());
        b.AddThemeStyleboxOverride("hover", primary ? MenuButtons.PrimaryHover() : MenuButtons.Hover());
        b.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed());
        b.AddThemeStyleboxOverride("disabled", MenuButtons.QuietNormal());
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        MenuButtons.Animate(b);
        return b;
    }

    private void Click() => GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("click");

    private void Say(string text, float seconds = 3.2f)
    {
        if (_status == null) return;
        _status.Text = text;
        _status.Modulate = Colors.White;
        _statusFade = seconds;
    }

    // ════════════════════════════════════════════════════════════════
    //  Overlays
    // ════════════════════════════════════════════════════════════════

    /// <summary>Full-screen dim + centred glass card. Returns (overlay, content column).</summary>
    private (Control overlay, VBoxContainer body) Modal(float w, float h, bool danger)
    {
        var vp = GetViewportRect().Size;
        var overlay = new Control { MouseFilter = MouseFilterEnum.Stop, ZIndex = 20 };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(overlay);
        var dim = new ColorRect { Color = danger ? new Color(0.10f, 0.03f, 0.02f, 0.82f) : new Color(0.02f, 0.02f, 0.015f, 0.80f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(dim);
        var card = new PanelContainer { Position = new Vector2((vp.X - w) / 2, (vp.Y - h) / 2), Size = new Vector2(w, h) };
        card.AddThemeStyleboxOverride("panel", Glass(danger ? new Color(0.88f, 0.45f, 0.35f, 0.65f) : null, 0.96f, 48));
        overlay.AddChild(card);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 18);
        card.AddChild(body);
        return (overlay, body);
    }

    private static Label ModalTitle(string text, Color colour)
    {
        var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        l.AddThemeFontOverride("font", ThemeTokens.GetHeaderFont(44));
        l.AddThemeFontSizeOverride("font_size", 44);
        l.AddThemeColorOverride("font_color", colour);
        return l;
    }

    private void ShowCreditsOverlay()
    {
        Click();
        if (_creditsOverlay != null && IsInstanceValid(_creditsOverlay)) return;

        var (overlay, body) = Modal(1400, 860, false);
        _creditsOverlay = overlay;
        body.AddChild(ModalTitle("AUDIO CREDITS", Gold));

        string creditsText = "All audio files shipped with Runewake are CC0 / public domain.";
        try
        {
            if (Godot.FileAccess.FileExists("res://content/audio/AUDIO_CREDITS.md"))
                creditsText = Godot.FileAccess.GetFileAsString("res://content/audio/AUDIO_CREDITS.md");
        }
        catch { /* keep the default line */ }

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.AddChild(scroll);
        var text = RowLabel(creditsText.Replace("#", "").Trim(), 26, Color.FromHtml("#CFC2A6"));
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.VerticalAlignment = VerticalAlignment.Top;
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(text);
        DragScroll.Attach(scroll);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        body.AddChild(row);
        var close = Plate("Close", true, 320, 84);
        close.Pressed += () =>
        {
            Click();
            if (_creditsOverlay != null && IsInstanceValid(_creditsOverlay)) _creditsOverlay.QueueFree();
            _creditsOverlay = null;
        };
        row.AddChild(close);
    }

    /// <summary>
    /// FABLE-066: everything about this phone's save in plain words, so "it reset" can be answered with a
    /// screenshot: which campaign is open, what it holds, when it last saved, what the cloud did.
    /// </summary>
    private void ShowSaveCheck()
    {
        Click();
        var (overlay, body) = Modal(1400, 860, false);
        body.AddChild(ModalTitle("SAVE CHECK", Gold));
        var sm = CampaignContext.SaveManager;
        var p = CampaignContext.Progression;
        var prof = CampaignContext.ActiveProfile;
        var sync = CampaignContext.SyncManager;
        string playing = Runewake.Engine.State.DeckBook.ActiveName(p) ?? "—";
        var lines = new System.Collections.Generic.List<string>
        {
            $"Build: {BuildInfo.Stamp} · {BuildInfo.Device()}",
            $"Campaign: {(prof != null ? $"{prof.ClassId} (file {sm.CurrentSlot}, {CampaignContext.Profiles.Count} campaign(s) on this phone)" : $"none open (file {sm.CurrentSlot})")}",
            $"Map: {p.ClearedNodes.Count} places cleared · {p.Shards} shards · Delver level {p.DelverLevel}",
            $"Decks: {p.SavedDecks.Count} saved · you play \"{playing}\" ({p.DeckCardIds.Count} cards)",
            $"Collection: {p.Collection.Count} different cards",
            $"Last saved: {(sm.LastSavedAt is System.DateTime t ? t.ToString("HH:mm:ss") : "not this session")}"
                + (sm.IsFunctional ? "" : $" · PROBLEM: {sm.LastError}"),
            sm.LastLoadFailed ? "⚠ The save could not be read at launch — a copy was kept. Tell Fable." : "Save file read fine at launch.",
            sm.SavingToFileOnly ? "Storage: save file (the database can't start on this phone — the file holds everything)." : "Storage: save file + database.",
            $"Artifacts you play: {string.Join(", ", p.ActiveArtifacts.Select(a => Runewake.Engine.Cards.ArtifactRegistry.Get(a)?.Name ?? a))} · recorded for {p.DeckArtifacts.Count} deck(s)",
            "Recent saves:",
            string.Join("\n", SaveManager.TrailTail(6).Select(l => "  " + l)),
            sync == null || !sync.IsConfigured ? "Cloud: accounts are off in this build — progress lives on this phone only."
                : $"Cloud: {sync.Status} · {(sync.IsLinked ? "signed in" : "GUEST — sign in to keep progress if the app is reinstalled")} · last check: {sync.LastDecision}",
            "",
            "Installing a new version over the old one keeps everything. Uninstalling first erases this phone's save —",
            "only a signed-in account brings it back.",
        };
        var text = RowLabel(string.Join("\n", lines), 27, Color.FromHtml("#CFC2A6"));
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.VerticalAlignment = VerticalAlignment.Top;
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.SizeFlagsVertical = SizeFlags.ExpandFill;
        body.AddChild(text);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 24);
        body.AddChild(row);
        var saveNow = Plate("Save now", false, 320, 84);
        saveNow.Pressed += () =>
        {
            Click();
            bool ok = sm.IsLoaded && sm.Save();
            CampaignContext.SaveCampaignProfile();
            CampaignContext.SaveDeckLibrary();
            overlay.QueueFree();
            ShowSaveCheck();
            if (!ok) GD.PrintErr("[Settings] Save now failed: " + sm.LastError);
        };
        row.AddChild(saveNow);
        var close = Plate("Close", true, 320, 84);
        close.Pressed += () => { Click(); overlay.QueueFree(); };
        row.AddChild(close);
    }

    private void ShowResetConfirm()
    {
        Click();
        if (_resetOverlay != null && IsInstanceValid(_resetOverlay)) return;

        var (overlay, body) = Modal(1100, 590, true);
        _resetOverlay = overlay;
        body.AddChild(ModalTitle("RESET ALL PROGRESS", EmberText));

        var warn = RowLabel("This deletes every card you own, your runes and upgrades, saved decks, map progress and your profile. It cannot be undone.", 30);
        warn.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        warn.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(warn);

        var instr = RowLabel("Type RESET to confirm", 28, Gold);
        instr.HorizontalAlignment = HorizontalAlignment.Center;
        body.AddChild(instr);

        var editRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        body.AddChild(editRow);
        _resetInput = new LineEdit { PlaceholderText = "RESET", CustomMinimumSize = new Vector2(420, 80), MaxLength = 10, Alignment = HorizontalAlignment.Center };
        _resetInput.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(34));
        _resetInput.AddThemeFontSizeOverride("font_size", 34);
        _resetInput.AddThemeColorOverride("font_color", Cream);
        _resetInput.AddThemeColorOverride("font_placeholder_color", new Color(0.45f, 0.40f, 0.34f));
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.075f, 0.065f), BorderColor = new Color(0.62f, 0.34f, 0.26f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 16, ContentMarginRight = 16,
        };
        _resetInput.AddThemeStyleboxOverride("normal", box);
        _resetInput.AddThemeStyleboxOverride("focus", box);
        _resetInput.TextChanged += _ => OnResetTextChanged();
        editRow.AddChild(_resetInput);

        _resetError = RowLabel("", 26, EmberText);
        _resetError.HorizontalAlignment = HorizontalAlignment.Center;
        _resetError.CustomMinimumSize = new Vector2(0, 34);
        body.AddChild(_resetError);

        var btnRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        btnRow.AddThemeConstantOverride("separation", 28);
        body.AddChild(btnRow);
        var cancel = Plate("Keep my progress", true, 400, 84);
        cancel.Pressed += DismissResetConfirm;
        btnRow.AddChild(cancel);
        _resetConfirmBtn = Plate("Delete everything", false, 400, 84);
        _resetConfirmBtn.Disabled = true;
        _resetConfirmBtn.Pressed += ExecuteResetProgress;
        btnRow.AddChild(_resetConfirmBtn);

        _resetInput.GrabFocus();
    }

    private void OnResetTextChanged()
    {
        if (_resetInput == null || _resetConfirmBtn == null || _resetError == null) return;
        bool matches = _resetInput.Text.Trim().ToUpperInvariant() == "RESET";
        _resetConfirmBtn.Disabled = !matches;
        _resetConfirmBtn.AddThemeColorOverride("font_color", matches ? EmberText : Color.FromHtml("#D8CBB0"));
        _resetError.Text = !matches && _resetInput.Text.Length >= 3 ? "Type RESET exactly to enable" : "";
    }

    private void DismissResetConfirm()
    {
        Click();
        if (_resetOverlay != null && IsInstanceValid(_resetOverlay)) _resetOverlay.QueueFree();
        _resetOverlay = null;
        _resetInput = null;
        _resetConfirmBtn = null;
        _resetError = null;
    }

    private void ExecuteResetProgress()
    {
        Click();
        GD.Print("[Settings] Resetting all progress...");
        try
        {
            var fresh = new ProgressionState();
            var st = CampaignContext.SaveManager.State;
            st.Version = fresh.Version;
            st.Shards = fresh.Shards;
            st.DigCharges = fresh.DigCharges;
            st.RuneDust = fresh.RuneDust;
            st.HasCompletedTutorial = fresh.HasCompletedTutorial;
            st.GlobalDiscoveryIndex = fresh.GlobalDiscoveryIndex;
            st.ClearedNodes.Clear();
            st.Collection.Clear();
            st.Fragments.Clear();
            st.OwnedRuneIds.Clear();
            st.SeenCardIds.Clear();
            st.UnlockedTools.Clear();
            st.DiscoveredRelics.Clear();
            st.DeckCardIds.Clear();
            st.SavedDecks.Clear(); st.DeckArtifacts.Clear(); st.ActiveArtifacts.Clear();
            st.SavedRunePageJson = "";
            st.Tutorial = null;
            CampaignContext.Progression.Collection.Clear();
            CampaignContext.SaveManager.Save();

            var s = CampaignContext.Settings;
            s.IntroSeen = false;
            CampaignContext.SaveManager.SaveSettings(s);

            CampaignContext.DeckLibrary.Clear();
            CampaignContext.SaveDeckLibrary();

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
        DismissResetConfirm();
        Say("Progress reset.", 5f);
    }

    // ════════════════════════════════════════════════════════════════
    //  Behaviour
    // ════════════════════════════════════════════════════════════════

    private void LoadCurrentSettings()
    {
        _loading = true;
        var s = CampaignContext.Settings;
        if (_masterSlider != null) _masterSlider.Value = Mathf.RoundToInt(s.MasterVolume * 100);
        if (_musicSlider != null) _musicSlider.Value = Mathf.RoundToInt(s.MusicVolume * 100);
        if (_sfxSlider != null) _sfxSlider.Value = Mathf.RoundToInt(s.SfxVolume * 100);
        if (_ambientSlider != null) _ambientSlider.Value = Mathf.RoundToInt(s.AmbientVolume * 100);
        _muteSwitch?.SetOn(s.MasterMute, instant: true);
        _motionSwitch?.SetOn(s.ReduceMotion, instant: true);
        _loading = false;
        _dirty = false;
    }

    private void OnVolumeChanged()
    {
        if (_loading) return;
        var s = CampaignContext.Settings;
        if (_masterSlider != null) s.MasterVolume = (float)_masterSlider.Value / 100f;
        if (_musicSlider != null) s.MusicVolume = (float)_musicSlider.Value / 100f;
        if (_sfxSlider != null) s.SfxVolume = (float)_sfxSlider.Value / 100f;
        if (_ambientSlider != null) s.AmbientVolume = (float)_ambientSlider.Value / 100f;
        ApplyAudioSettings(s);
        _dirty = true;
    }

    private void OnMuteToggled(bool muted)
    {
        var s = CampaignContext.Settings;
        s.MasterMute = muted;
        ApplyAudioSettings(s);
        MarkChanged(true);
    }

    private void MarkChanged(bool persistNow)
    {
        _dirty = true;
        if (persistNow) Persist();
    }

    private void Persist()
    {
        try
        {
            CampaignContext.SaveManager?.SaveSettings(CampaignContext.Settings);
            _dirty = false;
            Say("Saved");
        }
        catch (System.Exception ex) { GD.PrintErr($"[Settings] save failed: {ex.Message}"); }
    }

    private void OnReplayIntro()
    {
        Click();
        // FABLE-ACCOUNTS-1: the opening screen plays on every launch now; replay it right away.
        StartScreen.ShownThisRun = false;
        GD.Print("[Settings] Replaying the opening screen.");
        LoadScreen.Go(GetTree(), "res://scenes/main/Main.tscn");
    }

    private void OnReplayTutorial()
    {
        Click();
        if (_dirty) Persist();
        // FABLE-002: replay the guided first duel on the real Wayfarer encounter
        CampaignContext.TutorialScriptId = "first_duel";
        CampaignContext.TutorialHeadless = false;
        if (CampaignContext.EncounterIndex.Count == 0)
            CampaignContext.LoadEncounters();
        if (CampaignContext.EncounterIndex.TryGetValue("r1_duel_wayfarer", out var wayfarer))
            CampaignContext.CurrentEncounter = wayfarer;
        var profile = CampaignContext.ActiveProfile
            ?? (CampaignContext.Profiles.Count > 0 ? CampaignContext.Profiles[0] : null);
        if (profile != null)
            profile.TutorialDone = false;
        LoadScreen.Go(GetTree(), "res://scenes/duel/DuelScene.tscn");
    }

    private static void ApplyAudioSettings(SettingsState s)
    {
        int masterIdx = AudioServer.GetBusIndex("Master");
        if (masterIdx >= 0)
        {
            AudioServer.SetBusVolumeDb(masterIdx, Mathf.LinearToDb(s.MasterVolume));
            AudioServer.SetBusMute(masterIdx, s.MasterMute);
        }
        // FABLE-072: mute every bus, not only Master — on some Android audio paths a muted Master still let the
        // child buses through, so "Mute all sound" did nothing you could hear
        foreach (var bus in new[] { "Music", "SFX", "Ambient" })
        {
            int bi = AudioServer.GetBusIndex(bus);
            if (bi >= 0) AudioServer.SetBusMute(bi, s.MasterMute);
        }
        int musicIdx = AudioServer.GetBusIndex("Music");
        if (musicIdx >= 0) AudioServer.SetBusVolumeDb(musicIdx, Mathf.LinearToDb(s.MusicVolume));
        int sfxIdx = AudioServer.GetBusIndex("SFX");
        if (sfxIdx >= 0) AudioServer.SetBusVolumeDb(sfxIdx, Mathf.LinearToDb(s.SfxVolume));
        int ambIdx = AudioServer.GetBusIndex("Ambient");
        if (ambIdx >= 0) AudioServer.SetBusVolumeDb(ambIdx, Mathf.LinearToDb(s.AmbientVolume));
    }
}

/// <summary>FABLE-039: a pill switch — gold track when on, dark when off, cream knob that slides.</summary>
public partial class SettingsSwitch : Control
{
    public event Action<bool>? Toggled;
    public bool On { get; private set; }
    /// <summary>FABLE-091: bronze and rune-teal to match the painted Settings; the old gold pill otherwise.</summary>
    public bool Brass { get; set; }
    private float _t;   // 0 = off, 1 = on (animated)

    public SettingsSwitch()
    {
        CustomMinimumSize = new Vector2(104, 56);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public void SetOn(bool on, bool instant = false)
    {
        On = on;
        if (instant) { _t = on ? 1 : 0; QueueRedraw(); }
    }

    public void Flip()
    {
        On = !On;
        Toggled?.Invoke(On);
    }

    private readonly TapGuard _tap = new();

    public override void _GuiInput(InputEvent e)
    {
        // FABLE-072: a finger as well as a mouse (a touch that is not turned into a mouse event never flipped it)
        // FABLE-095: but one finger is one flip - the touch and its emulated click flipped it twice, so it looked dead
        if (_tap.AcceptRelease(e))
        {
            Flip();
            AcceptEvent();
        }
    }

    public override void _Process(double delta)
    {
        float target = On ? 1 : 0;
        if (Mathf.IsEqualApprox(_t, target)) return;
        _t = CampaignContext.ReduceMotion ? target : Mathf.MoveToward(_t, target, (float)delta * 7f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Brass) { DrawBrass(); return; }
        var sz = Size;
        float h = 48, w = 96;
        var o = new Vector2(sz.X - w, (sz.Y - h) / 2);
        var off = new Color(0.17f, 0.15f, 0.13f);
        var on = Color.FromHtml("#C9A84C");
        var track = off.Lerp(on, _t);
        var box = new StyleBoxFlat
        {
            BgColor = track,
            BorderColor = new Color(0.45f, 0.38f, 0.26f).Lerp(Color.FromHtml("#E8C96A"), _t),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 24, CornerRadiusTopRight = 24, CornerRadiusBottomLeft = 24, CornerRadiusBottomRight = 24,
        };
        DrawStyleBox(box, new Rect2(o, new Vector2(w, h)));
        float r = 17;
        float x = Mathf.Lerp(o.X + 6 + r, o.X + w - 6 - r, _t);
        var c = new Vector2(x, o.Y + h / 2);
        DrawCircle(c + new Vector2(0, 2), r + 1, new Color(0, 0, 0, 0.35f));
        DrawCircle(c, r, new Color(0.96f, 0.92f, 0.84f));
    }

    private void DrawBrass()
    {
        var sz = Size;
        float h = 44, w = 92;
        var o = new Vector2(sz.X - w, (sz.Y - h) / 2);
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.07f, 0.05f).Lerp(new Color(0.06f, 0.30f, 0.30f), _t),
            BorderColor = new Color(0.55f, 0.41f, 0.21f).Lerp(new Color(0.45f, 0.90f, 0.86f), _t),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 22, CornerRadiusTopRight = 22, CornerRadiusBottomLeft = 22, CornerRadiusBottomRight = 22,
            ShadowColor = new Color(0.40f, 0.95f, 0.90f, 0.35f * _t), ShadowSize = (int)(10 * _t),
        };
        DrawStyleBox(box, new Rect2(o, new Vector2(w, h)));
        var tex = SettingsScene.Brass(false);
        float x = Mathf.Lerp(o.X + 3, o.X + w - 3 - h + 2, _t);
        DrawTextureRect(tex, new Rect2(new Vector2(x, o.Y + 1), new Vector2(h - 2, h - 2)), false);
    }
}

/// <summary>FABLE-039 ornament: a thin gold rule that fades out toward one end and ends in a small diamond at the other.</summary>
public partial class OrnamentRule : Control
{
    public bool PointRight { get; set; }

    public OrnamentRule() { MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        var gold = Color.FromHtml("#C9A84C");
        float y = Size.Y / 2, w = Size.X;
        int steps = 24;
        for (int i = 0; i < steps; i++)
        {
            float a0 = i / (float)steps, a1 = (i + 1) / (float)steps;
            float fade = PointRight ? a0 : 1 - a1;
            float x0 = a0 * (w - 14), x1 = a1 * (w - 14);
            if (!PointRight) { x0 += 14; x1 += 14; }
            DrawLine(new Vector2(x0, y), new Vector2(x1, y), new Color(gold, 0.15f + 0.65f * fade), 2);
        }
        float dx = PointRight ? w - 7 : 7;
        DrawColoredPolygon(new[] { new Vector2(dx - 7, y), new Vector2(dx, y - 7), new Vector2(dx + 7, y), new Vector2(dx, y + 7) }, gold);
    }
}

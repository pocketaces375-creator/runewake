using Godot;
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
        Spacer(right, 14);
        SectionHeader(right, "HELP & STORY");
        ActionRow(right, "Replay the tutorial", false, OnReplayTutorial);
        ActionRow(right, "Replay the intro", false, OnReplayIntro);
        ActionRow(right, "Audio credits", false, ShowCreditsOverlay);
        right.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        ActionRow(right, "Reset all progress", true, ShowResetConfirm);

        // ── Footer ──
        _backBtn = Plate("◂  Back", false, 300, 84);
        _backBtn.Position = new Vector2((vp.X - panelW) / 2, vp.Y - 118);
        _backBtn.Pressed += () =>
        {
            Click();
            if (_dirty) Persist();
            GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn");
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
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 88) };
        row.AddThemeConstantOverride("separation", 24);
        var wrap = new MarginContainer();
        wrap.AddThemeConstantOverride("margin_left", (int)Pad);
        wrap.AddThemeConstantOverride("margin_right", (int)Pad);
        wrap.AddChild(row);
        col.AddChild(wrap);

        var name = RowLabel(label);
        name.CustomMinimumSize = new Vector2(190, 0);
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

        var value = RowLabel("100%", 30, Muted);
        value.CustomMinimumSize = new Vector2(92, 0);
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
        var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, hint == null ? 88 : 104) };
        row.AddThemeConstantOverride("separation", 24);
        var wrap = new MarginContainer();
        wrap.AddThemeConstantOverride("margin_left", (int)Pad);
        wrap.AddThemeConstantOverride("margin_right", (int)Pad);
        wrap.AddChild(row);
        col.AddChild(wrap);

        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        text.AddThemeConstantOverride("separation", 0);
        text.AddChild(RowLabel(label));
        if (hint != null) text.AddChild(RowLabel(hint, 26, Muted));
        row.AddChild(text);

        var sw = new SettingsSwitch { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        sw.Toggled += on => { if (!_loading) { Click(); onToggle(on); } };
        row.AddChild(sw);

        // the whole row is the hit area
        wrap.MouseFilter = MouseFilterEnum.Stop;
        wrap.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } mb && !sw.GetGlobalRect().HasPoint(mb.GlobalPosition))
                sw.Flip();
        };
        return sw;
    }

    private void ActionRow(VBoxContainer col, string label, bool danger, Action onPress)
    {
        var wrap = new MarginContainer();
        wrap.AddThemeConstantOverride("margin_left", (int)(Pad - 20));
        wrap.AddThemeConstantOverride("margin_right", (int)(Pad - 20));
        wrap.AddThemeConstantOverride("margin_bottom", danger ? 30 : 0);
        col.AddChild(wrap);

        var b = new Button
        {
            Text = "", CustomMinimumSize = new Vector2(0, 80), FocusMode = FocusModeEnum.None,
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
        var l = RowLabel(label, 34, danger ? EmberText : Cream);
        l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        inner.AddChild(l);
        inner.AddChild(RowLabel("›", 44, danger ? EmberText : Gold));

        b.Pressed += () => onPress();
    }

    private static void StyleSlider(HSlider s)
    {
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
            st.SavedDecks.Clear();
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
        GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn");
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
        GetTree().ChangeSceneToFile("res://scenes/duel/DuelScene.tscn");
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

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
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

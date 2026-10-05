using System;
using System.Collections.Generic;
using Godot;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// FABLE-ACCOUNTS-1: the opening screen. The Buried Age intro — the game
/// world's first words — and, under it, the front door to accounts.
///
/// Every launch, once:
///   1. The title art, darkened, and the intro lines fading in one by one.
///      A tap shows them all at once.
///   2. Then the bottom of the screen:
///        signed in to an account → "Signed in as …" · tap to begin
///        otherwise               → Create account · Sign in · Play as guest
///        no accounts in the build → tap to begin
///
/// Replaces the first-launch-only intro_splash.png overlay, which baked the
/// text into a 2316×1080 picture: its rune columns were missing glyphs
/// (boxes) and its "tap to continue" line sat below the bottom edge. Here the
/// text is live, so it fits any screen.
///
/// "Play as guest" is the interim path (Trikzos: "to be removed later") —
/// remove the button and the guest branch goes with it. A Google Play
/// sign-in button belongs next to Create account / Sign in when that lands.
/// </summary>
public partial class StartScreen : Control
{
    /// <summary>Once per launch: reloading the title (e.g. after a cloud save loads) must not replay it.</summary>
    public static bool ShownThisRun;

    private static readonly string[] Story =
    {
        "Before the maps had edges, the Old Age sang its last.",
        "Mountains knelt. Seas traded places with the sky.",
        "And the world, grown weary of its own wonders, buried them —",
        "its weapons, its wards, its wandering gods —",
        "and lay down over them like a stone upon a grave.",
        "",
        "Ages passed. The grave grew fields. The fields grew kingdoms.",
        "And the kingdoms forgot what slept beneath their feet.",
        "",
        "Now the runes are waking.",
        "The seals thin. The barrows hum at night.",
        "And what the Old Age buried is digging its way back.",
    };

    private static readonly Color StoryColour = new(0.80f, 0.76f, 0.68f);

    private SyncManager? _sync;
    private Control _host = null!;
    private readonly List<Label> _lines = new();
    private Control _bottom = null!;
    private Tween? _reveal;
    private bool _revealed;
    private bool _leaving;
    private bool _tapToBegin;

    /// <summary>Show the opening screen over <paramref name="host"/> (the title screen).</summary>
    public static StartScreen Show(Control host, SyncManager? sync)
    {
        ShownThisRun = true;
        var s = new StartScreen { Name = "StartScreen", _sync = sync, _host = host };
        host.AddChild(s);
        host.MoveChild(s, host.GetChildCount() - 1);
        return s;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ZIndex = 800;   // under the AccountPanel (900), over everything on the title
        MouseFilter = MouseFilterEnum.Stop;

        // ── backdrop: the title art, darkened, edges to black ──
        AddChild(Full(new ColorRect { Color = BgVoid }));
        var art = GD.Load<Texture2D>("res://content/art/title/hero_art.png");
        if (art != null)
        {
            AddChild(Full(new TextureRect
            {
                Texture = art,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                Modulate = new Color(0.40f, 0.38f, 0.35f),
            }));
        }
        var grad = new Gradient();
        grad.SetColor(0, new Color(0, 0, 0, 0.15f));
        grad.SetColor(1, new Color(0, 0, 0, 0.92f));
        AddChild(Full(new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = grad, Width = 256, Height = 128,
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.42f), FillTo = new Vector2(1.08f, 0.42f),
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
        }));

        // ── the story ──
        var story = new VBoxContainer
        {
            AnchorLeft = 0.08f, AnchorRight = 0.92f, AnchorTop = 0.045f, AnchorBottom = 0.66f,
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        story.AddThemeConstantOverride("separation", 4);
        AddChild(story);

        var title = MakeLabel("THE BURIED AGE", 58, Gold, header: true);
        story.AddChild(title);
        _lines.Add(title);
        story.AddChild(new Control { CustomMinimumSize = new Vector2(0, 18), MouseFilter = MouseFilterEnum.Ignore });
        foreach (var line in Story)
        {
            if (line.Length == 0) { story.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20), MouseFilter = MouseFilterEnum.Ignore }); continue; }
            var l = MakeLabel(line, 34, StoryColour, header: true);
            story.AddChild(l);
            _lines.Add(l);
        }
        foreach (var l in _lines) l.Modulate = new Color(l.Modulate, 0f);

        // ── the bottom: account door, or "tap to begin" ──
        _bottom = new VBoxContainer
        {
            AnchorLeft = 0.2f, AnchorRight = 0.8f, AnchorTop = 0.69f, AnchorBottom = 0.98f,
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _bottom.AddThemeConstantOverride("separation", 14);
        AddChild(_bottom);
        BuildBottom();
        _bottom.Modulate = new Color(1, 1, 1, 0);
        _bottom.Visible = false;

        if (_sync != null)
        {
            _sync.StatusChanged += OnSyncStatus;
            _sync.CloudSaveApplied += Leave;
        }

        StartReveal();
    }

    public override void _ExitTree()
    {
        if (_sync != null && IsInstanceValid(_sync))
        {
            _sync.StatusChanged -= OnSyncStatus;
            _sync.CloudSaveApplied -= Leave;
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Reveal
    // ═════════════════════════════════════════════════════════════════════

    private void StartReveal()
    {
        _reveal = CreateTween();
        _reveal.TweenInterval(0.35);
        for (int i = 0; i < _lines.Count; i++)
        {
            var l = _lines[i];
            _reveal.TweenProperty(l, "modulate:a", 1f, i == 0 ? 0.9 : 0.6);
            _reveal.TweenInterval(i == 0 ? 0.35 : 0.12);
        }
        _reveal.TweenCallback(Callable.From(ShowBottom));
    }

    /// <summary>Everything at once (a tap during the reveal).</summary>
    private void RevealNow()
    {
        _reveal?.Kill();
        foreach (var l in _lines) l.Modulate = new Color(l.Modulate, 1f);
        ShowBottom();
    }

    private void ShowBottom()
    {
        if (_revealed) return;
        _revealed = true;
        _bottom.Visible = true;
        CreateTween().TweenProperty(_bottom, "modulate:a", 1f, 0.45);
    }

    public override void _GuiInput(InputEvent e)
    {
        // Mouse only: on a phone the touch arrives as an emulated mouse press
        // too, and counting both would turn one tap into reveal-AND-begin.
        bool tap = e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
                   || e is InputEventKey { Pressed: true, KeyLabel: Key.Space or Key.Enter };
        if (!tap) return;
        AcceptEvent();
        if (!_revealed) { RevealNow(); return; }
        if (_tapToBegin) Leave();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Bottom: the account door
    // ═════════════════════════════════════════════════════════════════════

    private void BuildBottom()
    {
        foreach (var c in _bottom.GetChildren()) { _bottom.RemoveChild(c); c.QueueFree(); }
        _tapToBegin = false;

        if (_sync == null || !_sync.IsConfigured || _sync.HasAccount)
        {
            if (_sync != null && _sync.HasAccount)
                _bottom.AddChild(MakeLabel("Signed in as " + _sync.Session!.DisplayLabel(), 30, TextSecondary, header: false));
            var begin = MakeLabel("Tap to begin", 36, Gold, header: true);
            _bottom.AddChild(begin);
            var pulse = CreateTween().SetLoops();
            pulse.TweenProperty(begin, "modulate:a", 0.45f, 1.1).SetTrans(Tween.TransitionType.Sine);
            pulse.TweenProperty(begin, "modulate:a", 1f, 1.1).SetTrans(Tween.TransitionType.Sine);
            _tapToBegin = true;
            return;
        }

        // FABLE-ACCOUNTS-2: a sign-up still waiting on its email survives leaving the game (or Android
        // closing it): say so, and Sign In comes up with the email filled in.
        if (_sync.PendingSignupEmail is string pending)
            _bottom.AddChild(MakeLabel($"Almost done — tap the link we emailed to {pending}, then Sign In.", 28, TextSecondary, header: false));

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 28);
        _bottom.AddChild(row);
        row.AddChild(DoorButton("Create Account", 560, () => AccountPanel.OpenCreate(_host, _sync).AccountReady += OnAccountReady));
        row.AddChild(DoorButton("Sign In", 560, () => AccountPanel.OpenSignIn(_host, _sync).AccountReady += OnAccountReady));

        var guest = new Button { Text = "Play as guest for now", Flat = true, FocusMode = FocusModeEnum.None };
        guest.AddThemeFontSizeOverride("font_size", 28);
        var gf = GetBodyFont(28); if (gf != null) guest.AddThemeFontOverride("font", gf);
        guest.AddThemeColorOverride("font_color", new Color(0.78f, 0.70f, 0.52f, 0.9f));
        guest.AddThemeColorOverride("font_hover_color", TextPrimary);
        guest.Pressed += () =>
        {
            Click();
            // A guest who already has a session is backing up already (Main
            // started the sync); one without gets a quiet anonymous user now.
            if (_sync.Session == null) _sync.ContinueAsGuest();
            Leave();
        };
        _bottom.AddChild(guest);
    }

    private Button DoorButton(string text, float width, Action act)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(width, MinButtonHeight), FocusMode = FocusModeEnum.None };
        b.AddThemeFontSizeOverride("font_size", 36);
        var f = GetButtonFont(36); if (f != null) b.AddThemeFontOverride("font", f);
        b.AddThemeStyleboxOverride("normal", MenuButtons.Normal());
        b.AddThemeStyleboxOverride("hover", MenuButtons.Hover());
        b.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed());
        b.AddThemeColorOverride("font_color", TextPrimary);
        b.Pressed += () => { Click(); act(); };
        return b;
    }

    public override void _Notification(int what)
    {
        // FABLE-ACCOUNTS-2: back from the email app with the panel closed — finish the sign-up quietly.
        if (what == NotificationApplicationFocusIn && _sync != null && !_leaving && !_sync.HasAccount)
            _ = FinishPending();
    }

    private async System.Threading.Tasks.Task FinishPending()
    {
        try
        {
            var r = await _sync!.TryFinishPendingSignup();
            if (r != null && r.Ok && IsInstanceValid(this)) Leave();
        }
        catch (Exception ex) { GD.PrintErr($"[StartScreen] finish sign-up: {ex.Message}"); }
    }

    private void OnAccountReady()
    {
        if (IsInstanceValid(this) && _sync != null && _sync.HasAccount) Leave();
    }

    private void OnSyncStatus(string _)
    {
        // Signed in from somewhere else (the panel opened for a conflict, say): the door has done its job.
        if (IsInstanceValid(this) && _revealed && !_tapToBegin && _sync != null && _sync.HasAccount) Leave();
    }

    private void Leave()
    {
        if (_leaving || !IsInstanceValid(this)) return;
        _leaving = true;
        MouseFilter = MouseFilterEnum.Ignore;
        var t = CreateTween();
        t.TweenProperty(this, "modulate:a", 0f, 0.4);
        t.TweenCallback(Callable.From(QueueFree));
        GD.Print("[StartScreen] leaving");
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════════

    private static T Full<T>(T c) where T : Control
    {
        c.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        c.MouseFilter = MouseFilterEnum.Ignore;
        return c;
    }

    private static Label MakeLabel(string text, int size, Color colour, bool header)
    {
        var l = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if (header) ApplyHeaderFont(l, size); else ApplyBodyFont(l, size);
        l.Modulate = colour;
        l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        l.AddThemeConstantOverride("shadow_offset_x", 2);
        l.AddThemeConstantOverride("shadow_offset_y", 2);
        return l;
    }

    private void Click() => GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("click");
}

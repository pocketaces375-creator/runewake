using System;
using Godot;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>What a <see cref="RuneConfirm"/> asks.</summary>
public sealed record RuneConfirmSpec
{
    /// <summary>Small tracked caps above the title — what kind of decision this is ("DELETE CAMPAIGN").</summary>
    public string Eyebrow { get; init; } = "";
    /// <summary>The subject, large ("Battlemage · The Fallow Reach").</summary>
    public string Title { get; init; } = "";
    /// <summary>One or two sentences: the consequence, in plain words.</summary>
    public string Body { get; init; } = "";
    public string ConfirmText { get; init; } = "Confirm";
    public string CancelText { get; init; } = "Cancel";
    /// <summary>Destructive: the confirm plate and the sigil turn ember, and CANCEL is the gold primary.</summary>
    public bool Danger { get; init; } = true;
    public Action? OnConfirm { get; init; }
    public Action? OnCancel { get; init; }
}

/// <summary>
/// FABLE-043: the game's one confirmation dialog.
///
/// Trikzos, on the Delete Campaign popup: "I can't read it — redo it with a very sophisticated
/// looking UI." It was Godot's stock ConfirmationDialog: a grey OS window with 16px system text
/// and two tiny buttons, the one screen in the game that looked like a settings panel on a
/// desktop. On the phone it was unreadable.
///
/// This is drawn in the same materials as the rest of the game (docs/VISUAL_BIBLE.md: carved
/// stone, old gold): a chamfered slab with a gold keyline and an inset inner rule, diamond studs
/// at the corners, a round rune seal sitting on the top edge, a tracked-caps eyebrow, the subject
/// in Cinzel, a gold divider and the consequence in Cormorant at a size a phone can read.
///
/// Behaviour: it is a real modal — the dim swallows every tap — and tapping outside the slab or
/// pressing Back is "Cancel". The outside-tap is disarmed for the first 0.35 s because a phone
/// sends every tap twice (touch + the emulated mouse click, see TapGuard): the second half of the
/// tap that OPENED the dialog would otherwise land on the dim and close it at once.
/// Everything is laid out against a 1080-tall reference and scaled, so it is the same size on
/// every screen.
/// </summary>
public partial class RuneConfirm : Control
{
    private const float RefH = 1080f;
    private const float PanelW = 1180f;
    private const float PanelH = 570f;
    private const float ArmDelay = 0.35f;

    internal static readonly Color GoldLine = Color.FromHtml("#C9A84C");
    internal static readonly Color GoldBright = Color.FromHtml("#E8D48C");
    internal static readonly Color EmberLine = Color.FromHtml("#C8563E");
    private static readonly Color Parchment = Color.FromHtml("#EDE2CB");
    private static readonly Color TitleInk = Color.FromHtml("#F2E4C2");

    private RuneConfirmSpec _spec = new();
    private float _s = 1f;
    private ColorRect _dim = default!;
    private RuneSlab _slab = default!;
    private bool _closing;
    private double _age;

    /// <summary>Open a confirmation over <paramref name="host"/>. Returns the dialog (already in the tree).</summary>
    public static RuneConfirm Show(Node host, RuneConfirmSpec spec)
    {
        var d = new RuneConfirm { _spec = spec, Name = "RuneConfirm" };
        host.AddChild(d);
        return d;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ZIndex = 950;
        var vp = GetViewportRect().Size;
        _s = vp.Y / RefH;
        TopLevel = true;          // the viewport, not the host, is our frame
        Position = Vector2.Zero;
        Size = vp;

        // ── the dim: the whole game recedes ──
        _dim = new ColorRect { Color = new Color(0.02f, 0.015f, 0.01f, 0.78f), MouseFilter = MouseFilterEnum.Stop };
        _dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _dim.GuiInput += OnDimInput;
        AddChild(_dim);

        // ── the slab ──
        var size = new Vector2(Mathf.Min(PanelW * _s, vp.X - 64f * _s), PanelH * _s);
        _slab = new RuneSlab
        {
            Danger = _spec.Danger, S = _s,
            Size = size, CustomMinimumSize = size,
            Position = ((vp - size) / 2f).Round(),
            PivotOffset = size / 2f,
            MouseFilter = MouseFilterEnum.Stop,   // taps on the slab are not "outside"
        };
        AddChild(_slab);

        float padX = 84f * _s, padTop = 78f * _s, padBottom = 70f * _s;
        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        col.SetAnchorsPreset(LayoutPreset.FullRect);
        col.OffsetLeft = padX; col.OffsetRight = -padX; col.OffsetTop = padTop; col.OffsetBottom = -padBottom;
        col.AddThemeConstantOverride("separation", (int)(14 * _s));
        _slab.AddChild(col);

        if (!string.IsNullOrEmpty(_spec.Eyebrow))
        {
            var eyebrowFont = new FontVariation { BaseFont = GetHeaderFont(24), SpacingGlyph = (int)(7 * _s) };
            AddLabel(col, _spec.Eyebrow.ToUpperInvariant(), eyebrowFont, 25, _spec.Danger ? Color.FromHtml("#E0866C") : GoldLine, outline: false);
        }
        AddLabel(col, _spec.Title, GetHeaderFont(48), 48, TitleInk, outline: true);
        col.AddChild(new RuneDivider { Danger = _spec.Danger, S = _s, CustomMinimumSize = new Vector2(0, 26f * _s), MouseFilter = MouseFilterEnum.Ignore });
        var body = AddLabel(col, _spec.Body, GetBodyFont(33), 33, Parchment, outline: false);
        body.AddThemeConstantOverride("line_spacing", (int)(2 * _s));

        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 14f * _s), MouseFilter = MouseFilterEnum.Ignore });

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", (int)(34 * _s));
        col.AddChild(row);

        // Destructive dialogs put the SAFE answer in the gold primary plate, on the left, so a
        // reflexive tap on "the obvious button" never costs anybody a campaign.
        var cancel = Plate(_spec.CancelText, primary: _spec.Danger, danger: false);
        var confirm = Plate(_spec.ConfirmText, primary: !_spec.Danger, danger: _spec.Danger);
        row.AddChild(cancel);
        row.AddChild(confirm);
        cancel.Pressed += () => Close(confirmed: false);
        confirm.Pressed += () => Close(confirmed: true);

        Enter();
        GD.Print($"[RuneConfirm] open: {_spec.Eyebrow} — {_spec.Title}");
    }

    public override void _Process(double delta) => _age += delta;

    public override void _Input(InputEvent e)
    {
        // Android Back / Escape = the safe answer.
        if (!_closing && e.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            Close(confirmed: false);
        }
    }

    private void OnDimInput(InputEvent e)
    {
        if (_closing || _age < ArmDelay) return;
        if (e is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } or InputEventScreenTouch { Pressed: false })
            Close(confirmed: false);
    }

    private Label AddLabel(Container parent, string text, Font? font, int refSize, Color color, bool outline)
    {
        int px = Mathf.RoundToInt(refSize * _s);
        var l = new Label
        {
            Text = text, HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore,
            AutoTranslateMode = AutoTranslateModeEnum.Disabled,
        };
        if (font != null) l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", px);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.75f));
        l.AddThemeConstantOverride("shadow_offset_y", Mathf.Max(1, Mathf.RoundToInt(2 * _s)));
        if (outline)
        {
            l.AddThemeConstantOverride("outline_size", Mathf.RoundToInt(5 * _s));
            l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.55f));
        }
        parent.AddChild(l);
        return l;
    }

    private Button Plate(string text, bool primary, bool danger)
    {
        var b = new Button
        {
            Text = text, FocusMode = FocusModeEnum.None, MouseFilter = MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(340f * _s, 96f * _s),
        };
        StyleBox normal = danger ? MenuButtons.DangerNormal() : primary ? MenuButtons.PrimaryNormal() : MenuButtons.QuietNormal();
        StyleBox hover = danger ? MenuButtons.DangerHover() : primary ? MenuButtons.PrimaryHover() : MenuButtons.Hover();
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed());
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        int px = Mathf.RoundToInt(34 * _s);
        var f = GetButtonFont(px);
        if (f != null) b.AddThemeFontOverride("font", f);
        b.AddThemeFontSizeOverride("font_size", px);
        var ink = danger ? Color.FromHtml("#F4B3A0") : Color.FromHtml("#F2DFA6");
        b.AddThemeColorOverride("font_color", ink);
        b.AddThemeColorOverride("font_hover_color", danger ? Color.FromHtml("#FFD0C2") : Color.FromHtml("#FFF1C8"));
        b.AddThemeColorOverride("font_pressed_color", ink);
        b.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.6f));
        b.AddThemeConstantOverride("outline_size", Mathf.RoundToInt(4 * _s));
        MenuButtons.Animate(b);
        return b;
    }

    private void Enter()
    {
        if (CampaignContext.ReduceMotion) return;
        _dim.Modulate = new Color(1, 1, 1, 0);
        _slab.Modulate = new Color(1, 1, 1, 0);
        _slab.Scale = new Vector2(0.955f, 0.955f);
        var t = CreateTween().SetParallel();
        t.TweenProperty(_dim, "modulate:a", 1f, 0.22f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        t.TweenProperty(_slab, "modulate:a", 1f, 0.26f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out).SetDelay(0.04f);
        t.TweenProperty(_slab, "scale", Vector2.One, 0.34f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out).SetDelay(0.04f);
    }

    private void Close(bool confirmed)
    {
        if (_closing) return;
        _closing = true;
        GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("click");
        GD.Print($"[RuneConfirm] {(confirmed ? "confirmed" : "cancelled")}: {_spec.Eyebrow}");

        // The callback runs NOW, not after the fade: a confirm that rebuilds the screen should not
        // wait on an animation, and if the host scene changes, this node goes with it anyway.
        try { (confirmed ? _spec.OnConfirm : _spec.OnCancel)?.Invoke(); }
        catch (Exception ex) { GD.PrintErr($"[RuneConfirm] handler threw: {ex}"); }

        if (!IsInsideTree()) return;
        MouseFilter = MouseFilterEnum.Ignore;
        _dim.MouseFilter = MouseFilterEnum.Ignore;
        _slab.MouseFilter = MouseFilterEnum.Ignore;
        if (CampaignContext.ReduceMotion) { QueueFree(); return; }
        var t = CreateTween().SetParallel();
        t.TweenProperty(this, "modulate:a", 0f, 0.16f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        t.TweenProperty(_slab, "scale", new Vector2(0.97f, 0.97f), 0.16f);
        t.Chain().TweenCallback(Callable.From(QueueFree));
    }

    // ── geometry ──

    internal static Vector2[] Chamfer(Rect2 r, float c)
    {
        c = Mathf.Min(c, Mathf.Min(r.Size.X, r.Size.Y) / 2f);
        float x0 = r.Position.X, y0 = r.Position.Y, x1 = r.End.X, y1 = r.End.Y;
        return new[]
        {
            new Vector2(x0 + c, y0), new Vector2(x1 - c, y0), new Vector2(x1, y0 + c), new Vector2(x1, y1 - c),
            new Vector2(x1 - c, y1), new Vector2(x0 + c, y1), new Vector2(x0, y1 - c), new Vector2(x0, y0 + c),
        };
    }

    internal static Vector2[] Closed(Vector2[] pts)
    {
        var o = new Vector2[pts.Length + 1];
        Array.Copy(pts, o, pts.Length);
        o[^1] = pts[0];
        return o;
    }
}

// ═════════════════════════════════════════════════════════════════════
// The slab: drawn, not described (same reasoning as MenuButtons.PlateTexture).
// ═════════════════════════════════════════════════════════════════════

internal partial class RuneSlab : Control
{
    public bool Danger;
    public float S = 1f;

    public override void _Draw()
    {
        var sz = Size;
        float c = 26f * S;                        // chamfer
        var accent = Danger ? RuneConfirm.EmberLine : RuneConfirm.GoldLine;

        // Drop shadow: a few soft, offset, widening slabs.
        for (int i = 6; i >= 1; i--)
        {
            float g = i * 5f * S;
            var sh = RuneConfirm.Chamfer(new Rect2(new Vector2(-g, -g * 0.6f + 10f * S), sz + new Vector2(2 * g, 2 * g)), c + g);
            DrawColoredPolygon(sh, new Color(0, 0, 0, 0.07f));
        }

        // Outer aura — a faint breath of the accent colour around the stone.
        for (int i = 3; i >= 1; i--)
        {
            float g = i * 3f * S;
            DrawPolyline(RuneConfirm.Closed(RuneConfirm.Chamfer(new Rect2(new Vector2(-g, -g), sz + new Vector2(2 * g, 2 * g)), c + g)),
                new Color(accent.R, accent.G, accent.B, 0.05f * (4 - i)), 2f * S, true);
        }

        // The stone face: warm at the top, falling into shadow.
        var face = RuneConfirm.Chamfer(new Rect2(Vector2.Zero, sz), c);
        var cols = new Color[face.Length];
        var top = Danger ? Color.FromHtml("#241814") : Color.FromHtml("#221D17");
        var bottom = Color.FromHtml("#0C0A08");
        for (int i = 0; i < face.Length; i++)
            cols[i] = top.Lerp(bottom, Mathf.Clamp(face[i].Y / sz.Y, 0f, 1f) * 0.92f);
        DrawPolygon(face, cols);

        // A soft light pooled behind the title.
        var glowC = new Vector2(sz.X / 2f, sz.Y * 0.30f);
        for (int i = 10; i >= 1; i--)
            DrawCircle(glowC, sz.X * 0.045f * i, new Color(accent.R, accent.G, accent.B, 0.010f));

        // Outer keyline (gold) and a hairline highlight just inside it.
        DrawPolyline(RuneConfirm.Closed(face), accent, 2.5f * S, true);
        DrawPolyline(RuneConfirm.Closed(RuneConfirm.Chamfer(new Rect2(new Vector2(3.5f * S, 3.5f * S), sz - new Vector2(7f * S, 7f * S)), c - 1.5f * S)),
            new Color(0.95f, 0.85f, 0.6f, 0.10f), 1f * S, true);

        // Inner rule, inset — the engraved double line.
        float inset = 16f * S;
        var inner = new Rect2(new Vector2(inset, inset), sz - new Vector2(2 * inset, 2 * inset));
        DrawPolyline(RuneConfirm.Closed(RuneConfirm.Chamfer(inner, c * 0.6f)), new Color(RuneConfirm.GoldLine.R, RuneConfirm.GoldLine.G, RuneConfirm.GoldLine.B, 0.34f), 1.2f * S, true);

        // Diamond studs on the inner rule's corners.
        float d = 7f * S;
        foreach (var p in new[]
        {
            inner.Position + new Vector2(c * 0.6f, 0), inner.Position + new Vector2(inner.Size.X - c * 0.6f, 0),
            inner.Position + new Vector2(c * 0.6f, inner.Size.Y), inner.End - new Vector2(c * 0.6f, 0),
        })
            Diamond(p, d, RuneConfirm.GoldBright);

        // The seal: a round rune medallion sitting on the top edge.
        var sc = new Vector2(sz.X / 2f, 0);
        float r = 40f * S;
        DrawCircle(sc + new Vector2(0, 4f * S), r + 6f * S, new Color(0, 0, 0, 0.45f));
        DrawCircle(sc, r, Color.FromHtml("#16120E"));
        DrawArc(sc, r, 0, Mathf.Tau, 64, accent, 2.5f * S, true);
        DrawArc(sc, r - 7f * S, 0, Mathf.Tau, 64, new Color(accent.R, accent.G, accent.B, 0.45f), 1.2f * S, true);
        // Rune glyph: othala-like — a diamond over two splayed legs.
        float k = 15f * S;
        var ink = Danger ? Color.FromHtml("#F0A08A") : RuneConfirm.GoldBright;
        var g0 = sc + new Vector2(0, -k * 1.15f);
        var g1 = sc + new Vector2(k * 0.8f, -k * 0.1f);
        var g2 = sc + new Vector2(0, k * 0.55f);
        var g3 = sc + new Vector2(-k * 0.8f, -k * 0.1f);
        float w = 2.6f * S;
        DrawPolyline(new[] { g0, g1, g2, g3, g0 }, ink, w, true);
        DrawLine(g2, sc + new Vector2(k * 0.85f, k * 1.2f), ink, w, true);
        DrawLine(g2, sc + new Vector2(-k * 0.85f, k * 1.2f), ink, w, true);
        // gold studs either side of the seal, on the keyline
        Diamond(sc + new Vector2(r + 16f * S, 0), 5f * S, accent);
        Diamond(sc - new Vector2(r + 16f * S, 0), 5f * S, accent);
    }

    private void Diamond(Vector2 p, float d, Color col)
    {
        DrawColoredPolygon(new[] { p + new Vector2(0, -d), p + new Vector2(d, 0), p + new Vector2(0, d), p + new Vector2(-d, 0) }, col);
    }
}

/// <summary>A gold rule that fades out to both sides, with a diamond at its heart.</summary>
internal partial class RuneDivider : Control
{
    public bool Danger;
    public float S = 1f;

    public override void _Draw()
    {
        var sz = Size;
        var mid = new Vector2(sz.X / 2f, sz.Y / 2f);
        float half = Mathf.Min(sz.X * 0.36f, 330f * S);
        var col = Danger ? RuneConfirm.EmberLine : RuneConfirm.GoldLine;
        const int steps = 24;
        for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < steps; i++)
            {
                float a0 = (float)i / steps, a1 = (float)(i + 1) / steps;
                var p0 = mid + new Vector2(side * (16f * S + a0 * half), 0);
                var p1 = mid + new Vector2(side * (16f * S + a1 * half), 0);
                DrawLine(p0, p1, new Color(col.R, col.G, col.B, 0.85f * (1f - a0)), 1.4f * S, true);
            }
        float d = 7f * S;
        DrawColoredPolygon(new[] { mid + new Vector2(0, -d), mid + new Vector2(d, 0), mid + new Vector2(0, d), mid + new Vector2(-d, 0) }, RuneConfirm.GoldBright);
        DrawArc(mid, d + 5f * S, 0, Mathf.Tau, 24, new Color(col.R, col.G, col.B, 0.5f), 1f * S, true);
    }
}

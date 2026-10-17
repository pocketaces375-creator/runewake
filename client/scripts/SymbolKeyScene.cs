using System;
using System.Collections.Generic;
using Godot;

namespace Runewake.Client;

/// <summary>
/// FABLE-064: the Symbol Key — every glyph and callout the duel can show, each with a looping little
/// animation and one line on what it means. Opens as its own screen (Settings → "Symbol key") and as an
/// overlay over a duel (the ? button beside Concede), so a player can check a symbol mid-fight.
/// </summary>
public partial class SymbolKeyScene : Control
{
    public const string ScenePath = "res://scenes/symbolkey/SymbolKeyScene.tscn";

    private static readonly Color Gold = ThemeTokens.Gold;
    private static readonly Color Parchment = new(0.91f, 0.86f, 0.78f);
    private static readonly Color Muted = new(0.66f, 0.60f, 0.50f);

    private bool _overlay;
    private Action? _onClose;
    private readonly List<(TextureRect icon, Color color)> _demos = new();
    private double _clock;
    private float _s = 1f;

    private sealed record Entry(string Glyph, string Title, string Line, Color Color, string Group);

    private static Color Strata(Runewake.Engine.Cards.Strata s) => ThemeTokens.StrataColor(s);

    private static IEnumerable<Entry> Entries()
    {
        var ember = Strata(Runewake.Engine.Cards.Strata.EMBER);
        var tide = Strata(Runewake.Engine.Cards.Strata.TIDE);
        var verdant = Strata(Runewake.Engine.Cards.Strata.VERDANT);
        var hollow = Strata(Runewake.Engine.Cards.Strata.HOLLOW);
        var dawn = Strata(Runewake.Engine.Cards.Strata.DAWN);
        const string A = "WHAT YOU SEE IN A FIGHT", B = "KEYWORDS", C = "CONDITIONS";
        yield return new("sword", "Damage  −N", "A number in the attacker's colour, on whatever was hit. Hits on a player land at their crest.", ember, A);
        yield return new("heart", "Heal  +N", "Vigor restored — green, on the creature or player that got it.", verdant, A);
        yield return new("skull", "Death", "A creature left the board. The word under the skull says why: Venom, Burned, Destroyed, Tribute…", Parchment, A);
        yield return new("up", "Buff  +A/+V", "Attack and Vigor went up. Red arrows pointing down mean they went down.", verdant, A);
        yield return new("spell", "Ritual cast", "A spell is shown large in the middle of the board — yours and the enemy's. The small icons under it say what it does.", tide, A);
        yield return new("rune", "Ability fired", "A rune blooms over a creature whose ability triggered. A Relic waking fills the whole screen with its name.", dawn, A);
        yield return new("charge", "Artifact fired", "An artifact flashes and an arc points at what it touched. +N CHARGE counts toward its next trigger.", Gold, A);
        yield return new("sigil", "Sigil sprang", "A face-down trap went off: it countered a ritual, or ambushed an attacker.", tide, A);
        yield return new("card", "Cards moving", "DRAW, EXCAVATE, BURY, DISCARD: cards changing zones for a player.", tide, A);
        yield return new("shield", "GUARD", "Attacks on an empty lane hit this creature first.", dawn, B);
        yield return new("ward", "WARD", "Blocks the next damage, once. The ring shows it was spent.", tide, B);
        yield return new("dodge", "DODGE", "A chance to take no combat damage at all.", tide, B);
        yield return new("armor", "ARMOR", "Takes that much less damage from every hit.", dawn, B);
        yield return new("venom", "VENOM", "Anything it damages in combat is destroyed.", hollow, B);
        yield return new("pierce", "PIERCE", "Damage past a killed blocker carries on to the player.", ember, B);
        yield return new("swift", "SWIFT", "Can attack the turn it arrives.", dawn, B);
        yield return new("exalted", "EXALTED", "Your first attacker each turn gets +1/+1 for each Exalted creature you control.", Gold, B);
        yield return new("shieldface", "SHIELDED", "An artifact prevented some of the damage.", dawn, B);
        yield return new("burn", "BURN", "Takes that much damage at the start of its turn, then burns one less.", ember, C);
        yield return new("stun", "STUNNED", "Can't attack until the stun wears off.", dawn, C);
        yield return new("lock", "LOCKED", "Nothing can be played into this lane for a few turns.", hollow, C);
        yield return new("down", "DRAIN", "Attunement is taken at the start of the turn.", hollow, C);
    }

    public override void _Ready()
    {
        if (_overlay) return;
        SceneGuard.Build(this, "SymbolKeyScene", () => Build(false), "res://scenes/settings/SettingsScene.tscn", "Back");
    }

    /// <summary>Open the key over a running scene (a duel). Closing removes it; nothing else changes.</summary>
    public static SymbolKeyScene Overlay(Node parent, Action? onClose = null)
    {
        var k = new SymbolKeyScene { Name = "SymbolKeyOverlay", _overlay = true, _onClose = onClose, ZIndex = 950 };
        k.SetAnchorsPreset(LayoutPreset.FullRect);
        parent.AddChild(k);
        k.Build(true);
        return k;
    }

    private void Build(bool overlay)
    {
        var vp = GetViewportRect().Size;
        _s = vp.Y / 1080f;
        MouseFilter = MouseFilterEnum.Stop;

        var bg = new ColorRect { Color = overlay ? new Color(0.04f, 0.035f, 0.03f, 0.94f) : new Color(0.05f, 0.045f, 0.035f), MouseFilter = MouseFilterEnum.Stop };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        Text("EVERY SIGN, EXPLAINED", 0, 18 * _s, vp.X, 26, ThemeTokens.GetHeaderFont((int)(24 * _s)), new Color(0.75f, 0.68f, 0.52f));
        var title = Text("Symbol Key", 0, 44 * _s, vp.X, 70, ThemeTokens.GetCardNameFont((int)(64 * _s)), Gold);

        var scroll = new ScrollContainer
        {
            Position = new Vector2(60 * _s, 130 * _s), Size = new Vector2(vp.X - 120 * _s, vp.Y - 250 * _s),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        AddChild(scroll);
        DragScroll.Attach(scroll);

        var grid = new GridContainer { Columns = vp.X > vp.Y * 1.4f ? 3 : 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", (int)(26 * _s));
        grid.AddThemeConstantOverride("v_separation", (int)(18 * _s));
        scroll.AddChild(grid);

        float tileW = (scroll.Size.X - 26 * _s * (grid.Columns - 1)) / grid.Columns - 2;
        string group = "";
        foreach (var e in Entries())
        {
            if (e.Group != group)
            {
                group = e.Group;
                for (int i = 0; i < grid.Columns; i++)
                {
                    var head = new Label { Text = i == 0 ? group : "", CustomMinimumSize = new Vector2(tileW, 44 * _s), VerticalAlignment = VerticalAlignment.Bottom };
                    head.AddThemeFontOverride("font", ThemeTokens.GetHeaderFont((int)(22 * _s)));
                    head.AddThemeFontSizeOverride("font_size", (int)(22 * _s));
                    head.AddThemeColorOverride("font_color", new Color(0.75f, 0.68f, 0.52f));
                    grid.AddChild(head);
                }
            }
            grid.AddChild(Tile(e, tileW));
        }

        var back = Plate(overlay ? "✕  Close" : "◀  Back", 300 * _s, 76 * _s);
        back.Position = new Vector2(vp.X / 2f - 150 * _s, vp.Y - 100 * _s);
        back.Pressed += Close;
        AddChild(back);

        MenuButtons.BreatheTitle(title);
    }

    private Control Tile(Entry e, float w)
    {
        float h = 150 * _s;
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(w, h), MouseFilter = MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.08f, 0.07f, 0.9f), BorderColor = new Color(e.Color, 0.45f),
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 16 * _s, ContentMarginRight = 16 * _s, ContentMarginTop = 12 * _s, ContentMarginBottom = 12 * _s,
        });
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", (int)(18 * _s));
        panel.AddChild(row);

        // the glyph, in a little stage where its demo loops
        var stage = new Control { CustomMinimumSize = new Vector2(110 * _s, 110 * _s), MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var tex = CombatFx.Glyph(e.Glyph);
        var icon = new TextureRect
        {
            Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Size = new Vector2(78 * _s, 78 * _s), Position = new Vector2(16 * _s, 16 * _s), Modulate = e.Color, MouseFilter = MouseFilterEnum.Ignore,
        };
        icon.PivotOffset = icon.Size / 2f;
        stage.AddChild(icon);
        row.AddChild(stage);
        _demos.Add((icon, e.Color));

        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", (int)(4 * _s));
        var t = new Label { Text = e.Title, MouseFilter = MouseFilterEnum.Ignore };
        t.AddThemeFontOverride("font", ThemeTokens.GetHeaderFont((int)(28 * _s)));
        t.AddThemeFontSizeOverride("font_size", (int)(28 * _s));
        t.AddThemeColorOverride("font_color", e.Color);
        col.AddChild(t);
        var l = new Label { Text = e.Line, AutowrapMode = TextServer.AutowrapMode.Word, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        l.AddThemeFontOverride("font", ThemeTokens.GetBodyFont((int)(24 * _s)));
        l.AddThemeFontSizeOverride("font_size", (int)(24 * _s));
        l.AddThemeColorOverride("font_color", Parchment);
        col.AddChild(l);
        row.AddChild(col);
        return panel;
    }

    public override void _Process(double delta)
    {
        // every 2.4 s each glyph "fires": a quick pop and a ring, staggered so the page feels alive
        _clock += delta;
        if (CampaignContext.ReduceMotion) return;
        for (int i = 0; i < _demos.Count; i++)
        {
            double phase = (_clock + i * 0.35) % 2.4;
            var (icon, color) = _demos[i];
            if (!IsInstanceValid(icon) || !icon.IsInsideTree()) continue;
            float k = phase < 0.3 ? (float)(phase / 0.3) : phase < 0.6 ? (float)(1 - (phase - 0.3) / 0.3) : 0f;
            icon.Scale = Vector2.One * (1f + 0.14f * k);
            if (phase < (double)delta + 0.001 && icon.GetParent() is Control stage && IsVisibleInTree())
            {
                var ring = new FxRing { Color = color, Radius = 20 * _s, Width = 3 * _s, Position = stage.Size / 2f };
                stage.AddChild(ring);
                var tw = ring.CreateTween();
                tw.SetParallel();
                tw.TweenMethod(Callable.From<float>(r => { if (ring.IsInsideTree()) ring.Radius = r; }), 20f * _s, 58f * _s, 0.55f).SetEase(Tween.EaseType.Out);
                tw.TweenProperty(ring, "modulate:a", 0f, 0.55f);
                tw.SetParallel(false);
                tw.TweenCallback(Callable.From(ring.QueueFree));
            }
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMGoBackRequest) Close();
    }

    private void Close()
    {
        GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("click");
        if (_overlay) { _onClose?.Invoke(); QueueFree(); return; }
        LoadScreen.Go(GetTree(), "res://scenes/settings/SettingsScene.tscn");
    }

    private Label Text(string text, float x, float y, float w, int size, Font font, Color colour)
    {
        var l = new Label
        {
            Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(x, y), Size = new Vector2(w, size * _s + 16), MouseFilter = MouseFilterEnum.Ignore,
        };
        if (font != null) l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", (int)(size * _s));
        l.AddThemeColorOverride("font_color", colour);
        AddChild(l);
        return l;
    }

    private Button Plate(string text, float w, float h)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(w, h), Size = new Vector2(w, h), FocusMode = FocusModeEnum.None };
        b.AddThemeFontOverride("font", ThemeTokens.GetButtonFont((int)(30 * _s))); b.AddThemeFontSizeOverride("font_size", (int)(30 * _s));
        b.AddThemeColorOverride("font_color", Color.FromHtml("#D8CBB0"));
        b.AddThemeStyleboxOverride("normal", MenuButtons.QuietNormal());
        b.AddThemeStyleboxOverride("hover", MenuButtons.Hover());
        b.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed());
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        MenuButtons.Animate(b);
        return b;
    }
}

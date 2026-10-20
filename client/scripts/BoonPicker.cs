using System;
using Godot;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// FABLE-075: "The Gods of Old answer" — before every campaign fight the player takes ONE boon
/// (Endurance, Insight or Attunement; numbers live in BattleBoons). Replaces the Shrine node. Built in the
/// game's existing "choose a road" card language (the Crossroads / old Shrine cards); painted art can
/// come later through image generation.
/// </summary>
public partial class BoonPicker : Control
{
    private static readonly Color Parchment = new(0.92f, 0.86f, 0.74f);
    private static readonly Color[] Tints =
    {
        new(0.86f, 0.36f, 0.30f),   // Endurance — the red of the Vigor shield
        new(0.30f, 0.56f, 0.86f),   // Insight — deep-sky blue
        new(0.36f, 0.82f, 0.78f),   // Attunement — the teal of the attunement gems
    };
    private Action<BattleBoons.Boon>? _onPicked;
    private bool _picked;

    /// <summary>Real players only: the soak, capture and walkthrough tools play the fight without it.</summary>
    public static bool ShouldOffer()
    {
        foreach (var a in OS.GetCmdlineArgs()) if (a.StartsWith("--boontest")) return true;
        return !(CampaignContext.SoakActive || CampaignContext.AutoCaptureScreenshot || CampaignContext.LoopSmokeTest
                 || CampaignContext.BotDuelTest || CampaignContext.UxWalk);
    }

    public static BoonPicker Show(Node parent, Action<BattleBoons.Boon> onPicked)
    {
        var layer = new CanvasLayer { Name = "BoonPickerLayer", Layer = 31 };
        parent.AddChild(layer);
        var picker = new BoonPicker { Name = "BoonPicker", _onPicked = onPicked };
        layer.AddChild(picker);
        picker.Build();
        return picker;
    }

    private void Build()
    {
        var vp = GetViewportRect().Size;
        float s = Mathf.Max(0.55f, vp.Y / 1080f);
        Position = Vector2.Zero;
        Size = vp;
        MouseFilter = MouseFilterEnum.Stop;

        AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.78f), Size = vp, MouseFilter = MouseFilterEnum.Ignore });
        var glow = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = Grad(new Color(0.95f, 0.78f, 0.40f, 0.22f), new Color(0.95f, 0.78f, 0.40f, 0f)),
                Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.4f), FillTo = new Vector2(0.5f, 1f), Width = 64, Height = 64,
            },
            StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore, Size = vp,
        };
        AddChild(glow);

        float y = 120 * s;
        y += Line("BEFORE THE FIGHT", y, 26 * s, ThemeTokens.GetHeaderFont((int)(26 * s)), new Color(0.78f, 0.70f, 0.52f)) + 6 * s;
        var title = new Label();
        y += Line("The Gods of Old Answer", y, 76 * s, ThemeTokens.GetCardNameFont((int)(76 * s)), ThemeTokens.Gold, title) + 10 * s;
        y += Line("Choose one blessing for this battle.", y, 34 * s, ThemeTokens.GetBodyFont((int)(34 * s)), Parchment) + 50 * s;

        float cw = 470 * s, ch = 300 * s, gap = 36 * s;
        float x0 = (vp.X - (3 * cw + 2 * gap)) / 2f;
        for (int i = 0; i < BattleBoons.All.Count; i++)
        {
            var boon = BattleBoons.All[i];
            var card = Card(boon, Tints[i % Tints.Length], s, new Vector2(cw, ch));
            card.Position = new Vector2(x0 + i * (cw + gap), y);
            AddChild(card);
            // rise in one after another
            card.Modulate = new Color(1, 1, 1, 0);
            var t = card.CreateTween().SetParallel();
            t.TweenProperty(card, "modulate:a", 1f, 0.3f).SetDelay(0.15f + 0.1f * i);
            t.TweenProperty(card, "position:y", y, 0.35f).From(y + 30 * s).SetDelay(0.15f + 0.1f * i)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }

        Modulate = new Color(1, 1, 1, 0);
        CreateTween().TweenProperty(this, "modulate:a", 1f, 0.25f);
        MenuButtons.BreatheTitle(title);
    }

    private float Line(string text, float y, float size, Font font, Color color, Label? into = null)
    {
        var l = into ?? new Label();
        l.Text = text;
        l.HorizontalAlignment = HorizontalAlignment.Center;
        l.MouseFilter = MouseFilterEnum.Ignore;
        l.Position = new Vector2(0, y);
        l.Size = new Vector2(GetViewportRect().Size.X, size + 16);
        l.PivotOffset = l.Size / 2f;
        l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", (int)size);
        l.AddThemeColorOverride("font_color", color);
        AddChild(l);
        return size + 16;
    }

    private Control Card(BattleBoons.Boon boon, Color tint, float s, Vector2 size)
    {
        var btn = new Button { Name = "Boon_" + boon.Id, Text = "", Size = size, CustomMinimumSize = size, FocusMode = FocusModeEnum.None };
        StyleBoxFlat Box(float a, float border) => new()
        {
            BgColor = new Color(tint.R * 0.26f, tint.G * 0.26f, tint.B * 0.26f, a),
            BorderColor = new Color(tint.R, tint.G, tint.B, border),
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
            CornerRadiusTopLeft = 16, CornerRadiusTopRight = 16, CornerRadiusBottomLeft = 16, CornerRadiusBottomRight = 16,
            ShadowColor = new Color(tint.R, tint.G, tint.B, 0.25f), ShadowSize = (int)(14 * s),
        };
        btn.AddThemeStyleboxOverride("normal", Box(0.92f, 0.85f));
        btn.AddThemeStyleboxOverride("hover", Box(1f, 1f));
        btn.AddThemeStyleboxOverride("pressed", Box(0.75f, 1f));
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        col.SetAnchorsPreset(LayoutPreset.FullRect);
        col.AddThemeConstantOverride("separation", (int)(14 * s));
        btn.AddChild(col);
        foreach (var (text, size2, font, color) in new[]
        {
            ("BLESSING", 22 * s, ThemeTokens.GetHeaderFont((int)(22 * s)), new Color(0.82f, 0.76f, 0.62f)),
            (boon.Name, 54 * s, ThemeTokens.GetCardNameFont((int)(54 * s)), ThemeTokens.Gold),
            (boon.Effect, 32 * s, ThemeTokens.GetBodyFont((int)(32 * s)), Parchment),
        })
        {
            var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
            l.AddThemeFontOverride("font", font);
            l.AddThemeFontSizeOverride("font_size", (int)size2);
            l.AddThemeColorOverride("font_color", color);
            col.AddChild(l);
        }
        btn.Pressed += () => Pick(boon);
        return btn;
    }

    private void Pick(BattleBoons.Boon boon)
    {
        if (_picked) return;
        _picked = true;
        GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("unlock");
        var t = CreateTween();
        t.TweenProperty(this, "modulate:a", 0f, 0.25f);
        t.TweenCallback(Callable.From(() =>
        {
            _onPicked?.Invoke(boon);
            GetParent()?.QueueFree();   // the layer
        }));
    }

    /// <summary>For test hooks: take a boon without a tap.</summary>
    public void PickById(string id) { if (BattleBoons.Find(id) is { } b) Pick(b); }

    private static Gradient Grad(Color a, Color b)
    {
        var g = new Gradient();
        g.SetColor(0, a);
        g.SetColor(1, b);
        return g;
    }
}

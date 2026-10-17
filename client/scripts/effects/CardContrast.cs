using System;
using Godot;
using Runewake.Engine.Cards;

namespace Runewake.Client;

/// <summary>
/// FABLE-067: lift cards off the painted board (Trikzos: "contrast between the background and the card
/// borders … to really highlight them"). Three looks, picked in Settings → Card style:
///
///   1  Shadow &amp; rim   a soft dark shadow under each card and a thin gold rim around it
///   2  Colour glow     a halo in the card's stratum colour behind it and a dark rim
///   3  Dark stage      each card sits on a dark stone plinth, and the board itself is dimmed
///
/// The look is a child host behind the card (ShowBehindParent) plus a rim on top, so nothing about the
/// card's own layout changes. 0 = off (the old look).
/// </summary>
public static class CardContrast
{
    public const string HostName = "ContrastHost";
    public const string RimName = "ContrastRim";

    private static Texture2D? _shadow, _glow;

    /// <summary>The current style: Settings, or a --cardstyle=N override (for screenshots).</summary>
    public static int Style
    {
        get
        {
            foreach (var a in OS.GetCmdlineArgs())
                if (a.StartsWith("--cardstyle=") && int.TryParse(a.Substring(12), out int v)) return Math.Clamp(v, 0, 3);
            return Math.Clamp(CampaignContext.Settings?.CardStyle ?? 1, 0, 3);
        }
    }

    public static readonly string[] Names = { "Off", "Shadow & rim", "Colour glow", "Dark stage" };

    private static Texture2D ShadowTex => _shadow ??= new GradientTexture2D
    {
        Gradient = new Gradient { Offsets = new[] { 0f, 0.55f, 1f }, Colors = new[] { new Color(0, 0, 0, 0.95f), new Color(0, 0, 0, 0.55f), new Color(0, 0, 0, 0) } },
        Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f), Width = 64, Height = 64,
    };

    private static Texture2D GlowTex => _glow ??= new GradientTexture2D
    {
        Gradient = new Gradient { Offsets = new[] { 0f, 0.5f, 1f }, Colors = new[] { new Color(1, 1, 1, 0.9f), new Color(1, 1, 1, 0.35f), new Color(1, 1, 1, 0) } },
        Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f), Width = 64, Height = 64,
    };

    /// <summary>Give <paramref name="card"/> (a lane slot or hand card) the current look, in its stratum's colour.</summary>
    public static void Apply(Control card, Strata strata, bool show = true)
    {
        int style = Style;
        var host = card.GetNodeOrNull<Control>(HostName);
        var rim = card.GetNodeOrNull<Panel>(RimName);
        if (style == 0 || !show)
        {
            if (host != null) host.Visible = false;
            if (rim != null) rim.Visible = false;
            return;
        }
        if (host == null)
        {
            // a plain Control host: the card is a PanelContainer, which would resize anything placed in it directly
            host = new Control { Name = HostName, ShowBehindParent = true, MouseFilter = Control.MouseFilterEnum.Ignore };
            card.AddChild(host);
            card.MoveChild(host, 0);
        }
        if (rim == null)
        {
            rim = new Panel { Name = RimName, MouseFilter = Control.MouseFilterEnum.Ignore };
            card.AddChild(rim);
        }
        host.Visible = true;
        rim.Visible = true;
        foreach (var c in host.GetChildren()) c.QueueFree();

        float s = Mathf.Max(0.5f, card.GetViewportRect().Size.Y / 1080f);
        var color = ThemeTokens.StrataColor(strata);
        switch (style)
        {
            case 1:   // shadow & rim
                host.AddChild(BoxShadow(new Color(0, 0, 0, 0.88f), (int)(24 * s), new Vector2(0, 10f * s), (int)(8 * s)));
                rim.AddThemeStyleboxOverride("panel", Border(new Color(0.95f, 0.80f, 0.42f, 0.95f), Mathf.Max(2, (int)(2.5f * s)), (int)(7 * s)));
                break;
            case 2:   // colour glow
                host.AddChild(BoxShadow(new Color(color.Lerp(Colors.White, 0.15f), 0.85f), (int)(30 * s), Vector2.Zero, (int)(10 * s)));
                host.AddChild(BoxShadow(new Color(0, 0, 0, 0.6f), (int)(10 * s), new Vector2(0, 6f * s), (int)(8 * s)));
                rim.AddThemeStyleboxOverride("panel", Border(new Color(0.05f, 0.04f, 0.03f, 0.95f), Mathf.Max(2, (int)(3 * s)), (int)(7 * s), color.Lerp(Colors.White, 0.3f)));
                break;
            case 3:   // dark stage
                var plinth = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
                plinth.AddThemeStyleboxOverride("panel", new StyleBoxFlat
                {
                    BgColor = new Color(0.035f, 0.03f, 0.025f, 0.92f),
                    BorderColor = new Color(0.62f, 0.52f, 0.30f, 0.85f),
                    BorderWidthLeft = Mathf.Max(2, (int)(2 * s)), BorderWidthTop = Mathf.Max(2, (int)(2 * s)),
                    BorderWidthRight = Mathf.Max(2, (int)(2 * s)), BorderWidthBottom = Mathf.Max(3, (int)(4 * s)),
                    CornerRadiusTopLeft = (int)(10 * s), CornerRadiusTopRight = (int)(10 * s),
                    CornerRadiusBottomLeft = (int)(10 * s), CornerRadiusBottomRight = (int)(10 * s),
                    ShadowColor = new Color(0, 0, 0, 0.7f), ShadowSize = (int)(14 * s), ShadowOffset = new Vector2(0, 6 * s),
                });
                Stretch(plinth, 10f * s, Vector2.Zero);
                host.AddChild(plinth);
                rim.AddThemeStyleboxOverride("panel", Border(new Color(0, 0, 0, 0.9f), Mathf.Max(1, (int)(2 * s)), (int)(6 * s)));
                break;
        }
    }

    /// <summary>Dark stage dims the painted board so the cards carry the eye; the other looks leave it alone.</summary>
    /// FABLE-069: every look dims the board a little (Trikzos: "board still needs contrast"); Dark stage the most.
    public static Color BoardTint(Color skinTint) => skinTint * (Style == 3 ? new Color(0.56f, 0.56f, 0.60f, 1f)
                                                               : Style == 0 ? Colors.White : new Color(0.70f, 0.70f, 0.74f, 1f));

    /// <summary>FABLE-069: a soft dark vignette over the board's edges, so the eye lands on the lanes.</summary>
    public static TextureRect Vignette() => new()
    {
        Name = "BoardVignette", MouseFilter = Control.MouseFilterEnum.Ignore,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
        AnchorRight = 1, AnchorBottom = 1,
        Texture = new GradientTexture2D
        {
            Gradient = new Gradient { Offsets = new[] { 0f, 0.55f, 1f }, Colors = new[] { new Color(0, 0, 0, 0f), new Color(0, 0, 0, 0.18f), new Color(0, 0, 0, 0.62f) } },
            Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.48f), FillTo = new Vector2(1.05f, 0.48f), Width = 128, Height = 128,
        },
    };

    /// <summary>A soft box shadow (or glow) the exact shape of the card: a Panel whose StyleBox only casts.</summary>
    private static Panel BoxShadow(Color color, int size, Vector2 offset, int radius)
    {
        var p = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(color, 0.001f), ShadowColor = color, ShadowSize = Math.Max(1, size), ShadowOffset = offset,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        });
        Stretch(p, 0f, Vector2.Zero);
        return p;
    }

    private static TextureRect Fill(Texture2D tex, Color modulate, float pad, Vector2 offset, bool additive)
    {
        var t = new TextureRect
        {
            Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            Modulate = modulate, MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (additive) t.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        Stretch(t, pad, offset);
        return t;
    }

    /// <summary>Cover the host (= the card's rect) plus <paramref name="pad"/> on every side, shifted by offset.</summary>
    private static void Stretch(Control c, float pad, Vector2 offset)
    {
        c.AnchorLeft = 0; c.AnchorTop = 0; c.AnchorRight = 1; c.AnchorBottom = 1;
        c.OffsetLeft = -pad + offset.X; c.OffsetTop = -pad + offset.Y;
        c.OffsetRight = pad + offset.X; c.OffsetBottom = pad + offset.Y;
    }

    private static StyleBoxFlat Border(Color color, int width, int radius, Color? glow = null)
    {
        var b = new StyleBoxFlat
        {
            DrawCenter = false, BorderColor = color,
            BorderWidthLeft = width, BorderWidthTop = width, BorderWidthRight = width, BorderWidthBottom = width,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            AntiAliasing = true,
        };
        if (glow is Color g) { b.ShadowColor = new Color(g, 0.55f); b.ShadowSize = width * 3; }
        return b;
    }
}

using System;
using Godot;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// FABLE-045: the Crescent Dial — the duel HUD Trikzos picked ("option 4") on 2026-09-28.
///
/// Before this, everything a player needs about the two sides (vigor, attunement, deck, barrow,
/// hand, whose turn it is) was small text stacked in one column on the right, and he "couldn't
/// really get any of the information from there very effectively".
///
/// Now each side owns a CORNER on the right edge: the enemy's the top-right, yours the bottom-right.
/// A dark ring is drawn around the corner, and on it:
///   • the HUB — the quarter-disc in the corner itself: the enemy's class portrait; on your side,
///     the End Turn button (DuelScene's own button, re-shaped — see StyleEndTurn);
///   • ATTUNEMENT as crystals along the arc (lit = still available this turn), so it reads at a glance;
///   • VIGOR on a big gem crest (red for them, teal for you), with the name on a plate beneath it;
///   • DECK · BARROW · HAND as three tokens around the outer edge.
/// The artifacts sit in their reliquaries just left of each dial; DuelScene places them.
///
/// Geometry is a 1080-tall reference scaled by S, measured from the screen's right edge, so it
/// holds on 2316- and 2400-wide phones alike.
/// </summary>
public partial class CrescentHud : Control
{
    // ── reference geometry (1080-tall screen) ──
    public const float HubR = 190f;          // the corner quarter-disc
    private const float BandIn = 262f, BandOut = 368f;
    private const float GoldR = 350f;
    private const float CrystalR = 318f;
    private const float TokenR = 432f;
    private const float CrestR = 58f;
    private static readonly Vector2 CrestOffset = new(-268f, 72f);  // from the corner, enemy orientation (y down)
    /// <summary>How far in from the right edge the HUD reaches; the artifacts sit left of this.</summary>
    public const float Reach = 446f;

    private static readonly Color TealGem = Color.FromHtml("#43D6C8");
    private static readonly Color CrimsonGem = Color.FromHtml("#E4504A");
    private static readonly Color GoldLine = Color.FromHtml("#C9A84C");
    private static readonly Color GoldHi = Color.FromHtml("#F3DE95");
    private static readonly Color GoldDeep = Color.FromHtml("#5B441A");

    public float S { get; set; } = 1f;

    private readonly Side[] _sides = { new(0), new(1) };
    private Vector2 _vp;

    /// <summary>Your vigor crest — damage numbers float from it and the tutorial rings it.</summary>
    public Control PlayerCrest => _sides[0].Crest;
    public Control EnemyCrest => _sides[1].Crest;
    /// <summary>The box around your attunement crystals (the tutorial's "attunement_meter").</summary>
    public Control PlayerAttuneAnchor => _sides[0].AttuneAnchor;
    /// <summary>Your deck + barrow tokens as one box (the tutorial points at it).</summary>
    public Control PlayerPilesAnchor => _sides[0].PilesAnchor;

    private sealed class Side
    {
        public readonly int View;               // 0 = you (bottom corner), 1 = the enemy (top corner)
        public Side(int view) { View = view; }
        public int Vigor, MaxVigor = 25, Attune, AttuneMax, Deck, Barrow, Hand;
        public string Name = "";
        public Control Crest = default!;
        public Label CrestValue = default!, CrestMax = default!, NamePlate = default!;
        public Control AttuneAnchor = default!, PilesAnchor = default!;
        public Label AttuneText = default!;
        public (Control box, Label value)[] Tokens = new (Control, Label)[3];
        public TextureRect? Portrait;
        public Control? PortraitClip;
    }

    public CrescentHud()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Name = "CrescentHud";
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _vp = GetViewportRect().Size;
        foreach (var side in _sides) BuildSide(side);
        Layout();
    }

    // ───────────────────────────── building ─────────────────────────────

    private Vector2 Corner(int view) => view == 1 ? new Vector2(_vp.X, 0) : new Vector2(_vp.X, _vp.Y);
    /// <summary>A point on the dial. deg is the usual maths angle for the ENEMY corner (y down): 90 = straight
    /// down the right edge, 180 = straight left along the top edge. Your corner is the mirror image.</summary>
    private Vector2 OnDial(int view, float r, float deg)
    {
        float a = Mathf.DegToRad(deg);
        var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * S;
        return Corner(view) + (view == 1 ? d : new Vector2(d.X, -d.Y));
    }
    private Vector2 Off(int view, Vector2 enemyOffset) =>
        Corner(view) + (view == 1 ? enemyOffset : new Vector2(enemyOffset.X, -enemyOffset.Y)) * S;

    private void BuildSide(Side sd)
    {
        var gem = sd.View == 0 ? TealGem : CrimsonGem;

        // the enemy's hub: their portrait, clipped to the corner quarter-disc
        if (sd.View == 1)
        {
            float r = HubR * S;
            var clip = new Panel { MouseFilter = MouseFilterEnum.Ignore, ClipChildren = CanvasItem.ClipChildrenMode.AndDraw };
            clip.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = Color.FromHtml("#14110D"),
                CornerRadiusBottomLeft = Mathf.RoundToInt(r), AntiAliasing = true,
            });
            AddChild(clip);
            var tex = new TextureRect
            {
                MouseFilter = MouseFilterEnum.Ignore, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            };
            clip.AddChild(tex);
            sd.Portrait = tex;
            sd.PortraitClip = clip;
        }

        // vigor crest
        var crest = new CrestGem { Gem = gem, S = S, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(crest);
        sd.Crest = crest;
        sd.CrestValue = MakeLabel(crest, GetHeaderFont(44), 44, Colors.White, 6);
        sd.CrestMax = MakeLabel(crest, GetHeaderFont(15), 15, new Color(1, 1, 1, 0.85f), 4);

        sd.NamePlate = MakeLabel(this, GetHeaderFont(17), 17, sd.View == 0 ? Color.FromHtml("#9FE6DE") : Color.FromHtml("#F0B3A8"), 5);
        sd.NamePlate.ClipText = true;
        sd.AttuneText = MakeLabel(this, GetHeaderFont(15), 15, sd.View == 0 ? Color.FromHtml("#9FE6DE") : Color.FromHtml("#F0B3A8"), 5);

        string[] caps = { "DECK", "BARROW", "HAND" };
        for (int i = 0; i < 3; i++)
        {
            var box = new Panel { MouseFilter = MouseFilterEnum.Ignore };
            box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.047f, 0.039f, 0.031f, 0.9f), BorderColor = GoldLine,
                BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
                CornerRadiusTopLeft = 99, CornerRadiusTopRight = 99, CornerRadiusBottomLeft = 99, CornerRadiusBottomRight = 99,
                ShadowColor = new Color(0, 0, 0, 0.55f), ShadowSize = Mathf.RoundToInt(6 * S), AntiAliasing = true,
            });
            AddChild(box);
            var v = MakeLabel(box, GetHeaderFont(28), 28, Color.FromHtml("#F2E4C2"), 4);
            var c = MakeLabel(box, GetBodyFont(15), 15, Color.FromHtml("#C9B994"), 0);
            c.Text = caps[i];
            c.Name = "Caption";
            sd.Tokens[i] = (box, v);
        }

        sd.AttuneAnchor = new Control { MouseFilter = MouseFilterEnum.Ignore, Name = sd.View == 0 ? "PlayerAttuneAnchor" : "EnemyAttuneAnchor" };
        AddChild(sd.AttuneAnchor);
        sd.PilesAnchor = new Control { MouseFilter = MouseFilterEnum.Ignore, Name = sd.View == 0 ? "PlayerPilesAnchor" : "EnemyPilesAnchor" };
        AddChild(sd.PilesAnchor);
    }

    private Label MakeLabel(Node parent, Font? font, int refPx, Color color, int outline)
    {
        int px = Mathf.RoundToInt(refPx * S);
        var l = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore, AutoTranslateMode = AutoTranslateModeEnum.Disabled,
        };
        if (font != null) l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", px);
        l.AddThemeColorOverride("font_color", color);
        if (outline > 0)
        {
            l.AddThemeConstantOverride("outline_size", Mathf.RoundToInt(outline * S));
            l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        }
        parent.AddChild(l);
        return l;
    }

    // ───────────────────────────── layout ─────────────────────────────

    private void Layout()
    {
        foreach (var sd in _sides)
        {
            int v = sd.View;
            // hub portrait (enemy)
            if (sd.PortraitClip != null && sd.Portrait != null)
            {
                float r = HubR * S;
                sd.PortraitClip.Position = new Vector2(_vp.X - r, 0);
                sd.PortraitClip.Size = new Vector2(r, r);
                sd.Portrait.Position = new Vector2(-r * 0.35f, -r * 0.30f);
                sd.Portrait.Size = new Vector2(r * 1.7f, r * 1.7f);
            }

            // crest + name plate beneath it (above it, on your side)
            float cr = CrestR * S;
            var cc = Off(v, CrestOffset);
            sd.Crest.Position = cc - new Vector2(cr, cr);
            sd.Crest.Size = new Vector2(cr * 2, cr * 2);
            sd.CrestValue.Position = new Vector2(0, cr * 0.18f);
            sd.CrestValue.Size = new Vector2(cr * 2, cr * 1.05f);
            sd.CrestMax.Position = new Vector2(0, cr * 1.12f);
            sd.CrestMax.Size = new Vector2(cr * 2, cr * 0.5f);
            float plateW = 190f * S, plateH = 26f * S;
            float plateY = v == 1 ? cc.Y + cr + 6f * S : cc.Y - cr - 6f * S - plateH;
            sd.NamePlate.Position = new Vector2(cc.X - plateW / 2f, plateY);
            sd.NamePlate.Size = new Vector2(plateW, plateH);

            // attunement text sits at the start of the crystal arc, by the screen edge
            var at = OnDial(v, CrystalR - 44f, 99f);
            sd.AttuneText.Position = at - new Vector2(60f * S, 12f * S);
            sd.AttuneText.Size = new Vector2(120f * S, 24f * S);

            // tokens on the outer edge
            float[] angles = { 97f, 118f, 139f };
            float tw = 104f * S, th = 70f * S;
            var pilesMin = new Vector2(float.MaxValue, float.MaxValue);
            var pilesMax = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 3; i++)
            {
                var p = OnDial(v, TokenR, angles[i]);
                var (box, val) = sd.Tokens[i];
                box.Position = p - new Vector2(tw / 2f, th / 2f);
                box.Size = new Vector2(tw, th);
                val.Position = new Vector2(0, 5f * S);
                val.Size = new Vector2(tw, th * 0.55f);
                var cap = box.GetNode<Label>("Caption");
                cap.Position = new Vector2(0, th * 0.56f);
                cap.Size = new Vector2(tw, th * 0.36f);
                if (i < 2)
                {
                    pilesMin = pilesMin.Min(box.Position);
                    pilesMax = pilesMax.Max(box.Position + box.Size);
                }
            }
            sd.PilesAnchor.Position = pilesMin;
            sd.PilesAnchor.Size = pilesMax - pilesMin;

            var (a0, a1) = CrystalSpan(Mathf.Max(1, sd.AttuneMax));
            var b0 = OnDial(v, CrystalR, a0);
            var b1 = OnDial(v, CrystalR, a1);
            float pad = 34f * S;
            var mn = b0.Min(b1) - new Vector2(pad, pad);
            var mx = b0.Max(b1) + new Vector2(pad, pad);
            sd.AttuneAnchor.Position = mn;
            sd.AttuneAnchor.Size = mx - mn;
        }
        QueueRedraw();
    }

    /// <summary>First and last crystal angle for n crystals: from the screen edge outward, never under the crest.</summary>
    private static (float, float) CrystalSpan(int n)
    {
        const float start = 103f, end = 138f;
        if (n <= 1) return (start, start);
        float step = Mathf.Min(14f, (end - start) / (n - 1));
        return (start, start + step * (n - 1));
    }

    // ───────────────────────────── data ─────────────────────────────

    /// <summary>Update one side. view 0 = you, 1 = the enemy.</summary>
    public void SetSide(int view, string name, int vigor, int maxVigor, int attune, int attuneMax, int deck, int barrow, int hand)
    {
        var sd = _sides[view];
        bool relayout = sd.AttuneMax != attuneMax;
        sd.Name = name; sd.Vigor = vigor; sd.MaxVigor = maxVigor; sd.Attune = attune; sd.AttuneMax = attuneMax;
        sd.Deck = deck; sd.Barrow = barrow; sd.Hand = hand;
        if (sd.CrestValue == null) return;   // before _Ready
        sd.CrestValue.Text = vigor.ToString();
        sd.CrestMax.Text = $"/ {maxVigor}";
        sd.NamePlate.Text = (name ?? "").ToUpperInvariant();
        sd.AttuneText.Text = $"{attune}/{attuneMax}";
        sd.Tokens[0].value.Text = deck.ToString();
        sd.Tokens[1].value.Text = barrow.ToString();
        sd.Tokens[2].value.Text = hand.ToString();
        if (relayout) Layout(); else QueueRedraw();
    }

    /// <summary>The enemy hub's portrait (their class art).</summary>
    public void SetEnemyPortrait(Texture2D? tex)
    {
        if (_sides[1].Portrait != null) _sides[1].Portrait.Texture = tex;
    }

    /// <summary>A hit on this side's vigor: the crest jolts.</summary>
    public void Jolt(int view)
    {
        var c = _sides[view].Crest;
        if (c == null || CampaignContext.ReduceMotion) return;
        var home = c.Position;
        var t = CreateTween();
        t.TweenProperty(c, "position", home + new Vector2(7f * S, 0), 0.04f);
        t.TweenProperty(c, "position", home - new Vector2(7f * S, 0), 0.04f);
        t.TweenProperty(c, "position", home + new Vector2(3f * S, 0), 0.04f);
        t.TweenProperty(c, "position", home, 0.04f);
    }

    // ───────────────────────────── drawing ─────────────────────────────

    public override void _Draw()
    {
        foreach (var sd in _sides)
        {
            int v = sd.View;
            var c = Corner(v);
            float from = v == 1 ? Mathf.Pi / 2f : Mathf.Pi;
            float to = v == 1 ? Mathf.Pi : Mathf.Pi * 1.5f;
            var gem = v == 0 ? TealGem : CrimsonGem;

            // the dark ring, soft at both edges
            float mid = (BandIn + BandOut) / 2f * S, w = (BandOut - BandIn) * S;
            DrawArc(c, mid, from, to, 96, new Color(0.04f, 0.03f, 0.024f, 0.62f), w, true);
            DrawArc(c, BandIn * S + 4f * S, from, to, 96, new Color(0.04f, 0.03f, 0.024f, 0.30f), 10f * S, true);
            DrawArc(c, BandOut * S - 4f * S, from, to, 96, new Color(0.04f, 0.03f, 0.024f, 0.30f), 10f * S, true);
            // gold rules
            DrawArc(c, GoldR * S, from, to, 96, new Color(GoldLine.R, GoldLine.G, GoldLine.B, 0.85f), 2f * S, true);
            DrawArc(c, BandIn * S, from, to, 96, new Color(GoldLine.R, GoldLine.G, GoldLine.B, 0.45f), 1.4f * S, true);
            // the hub's own gold rim (the enemy's; your End Turn button draws its own)
            if (v == 1)
            {
                DrawArc(c, HubR * S + 2f * S, from, to, 72, GoldDeep, 7f * S, true);
                DrawArc(c, HubR * S, from, to, 72, GoldLine, 3.5f * S, true);
                DrawArc(c, HubR * S + 12f * S, from, to, 72, new Color(gem.R, gem.G, gem.B, 0.35f), 12f * S, true);
            }

            // attunement crystals — lit ones are still to spend this turn
            int n = sd.AttuneMax;
            if (n > 0)
            {
                var (a0, a1) = CrystalSpan(n);
                float step = n > 1 ? (a1 - a0) / (n - 1) : 0f;
                float size = Mathf.Min(1f, 0.55f + 3.2f / n);   // shrinks a little when there are many
                for (int i = 0; i < n; i++)
                {
                    var p = OnDial(v, CrystalR, a0 + i * step);
                    Crystal(p, size, i < sd.Attune, gem);
                }
            }
        }
    }

    private void Crystal(Vector2 p, float k, bool lit, Color gem)
    {
        float hw = 15f * S * k, hh = 23f * S * k;
        var pts = new[]
        {
            p + new Vector2(0, -hh), p + new Vector2(hw, -hh * 0.2f), p + new Vector2(0, hh), p + new Vector2(-hw, -hh * 0.2f),
        };
        // gold setting
        var outer = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++) outer[i] = p + (pts[i] - p) * 1.22f;
        DrawColoredPolygon(outer, GoldDeep);
        var mid = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++) mid[i] = p + (pts[i] - p) * 1.12f;
        DrawColoredPolygon(mid, lit ? GoldHi : GoldLine.Darkened(0.35f));
        if (lit)
        {
            if (!CampaignContext.ReduceMotion)
                DrawCircle(p, hh * 1.1f, new Color(gem.R, gem.G, gem.B, 0.16f));
            DrawPolygon(pts, new[] { Colors.White, gem.Lightened(0.15f), gem.Darkened(0.55f), gem });
            DrawColoredPolygon(new[] { p + new Vector2(0, -hh * 0.82f), p + new Vector2(hw * 0.35f, -hh * 0.3f), p + new Vector2(-hw * 0.2f, -hh * 0.25f) },
                new Color(1, 1, 1, 0.7f));
        }
        else
        {
            DrawColoredPolygon(pts, new Color(0.09f, 0.075f, 0.06f, 0.95f));
        }
    }
}

/// <summary>The round vigor gem: gold rim, a lit sphere, the number on top (labels are children).</summary>
public partial class CrestGem : Control
{
    public Color Gem;
    public float S = 1f;

    public override void _Draw()
    {
        var c = Size / 2f;
        float r = Mathf.Min(Size.X, Size.Y) / 2f;
        DrawCircle(c + new Vector2(0, 5f * S), r + 2f * S, new Color(0, 0, 0, 0.5f));
        DrawCircle(c, r, Color.FromHtml("#5B441A"));
        DrawCircle(c, r - 2.5f * S, Color.FromHtml("#F3DE95"));
        DrawCircle(c, r - 5f * S, Gem.Darkened(0.72f));
        // a lit sphere: rings shading from the light (upper left) toward the rim
        for (int i = 0; i < 14; i++)
        {
            float t = i / 13f;
            var col = Gem.Darkened(0.62f).Lerp(Gem.Lightened(0.08f), t * t);
            float rr = (r - 5f * S) * (1f - t * 0.62f);
            DrawCircle(c + new Vector2(-r * 0.18f, -r * 0.22f) * t, rr, col);
        }
        DrawCircle(c + new Vector2(-r * 0.32f, -r * 0.38f), r * 0.12f, new Color(1, 1, 1, 0.55f));
    }
}

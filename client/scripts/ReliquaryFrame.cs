using System;
using System.Collections.Generic;
using Godot;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// FABLE-044: the reliquary — the frame an ARTIFACT sits in on the duel board.
///
/// Trikzos: "The artifacts should really have some kind of impressive border that signifies
/// them as the most important cards of the game — something really epic and beautiful that
/// 1) separates them from each other, 2) draws attention and focus. They should also say the
/// title of what weapon they are: sword, shield, aura, etc."
///
/// Before this, the two artifacts wore the same thin Root-Bound border as a creature, sat
/// flush against each other, and their names were dim text on the art. Now each one is its own
/// shrine:
///   • a gilded double border — dark bronze lip, bright gold band, inner gold hairline —
///     with chamfered corners and gold filigree brackets, a gem set into every corner
///     (TEAL for your side, CRIMSON for the enemy's), so the two sides read apart at a glance;
///   • a TYPE PLAQUE crowning the top edge — SWORD, SHIELD, WAND, AURA… in tracked Cinzel;
///   • a NAME PLAQUE across the foot, with the charge pips as small gems beside it;
///   • a slow breathing halo in the side's colour and a spark of light that travels around
///     the gold every few seconds — the only moving border on the board, so the eye goes there.
///     It burns brighter when the artifact is fully charged, and goes ashen when suppressed.
/// Drawn in code (like MenuButtons / RuneConfirm): no new asset bytes, nothing that can be
/// excluded from the APK by an export filter. ReduceMotion stops the halo and the spark.
/// </summary>
public partial class ReliquaryFrame : Control
{
    private static readonly Color GoldDeep = Color.FromHtml("#6E5220");
    private static readonly Color GoldMid = Color.FromHtml("#C9A84C");
    private static readonly Color GoldHi = Color.FromHtml("#F3DE95");
    // FABLE-048: Trikzos — "a cyan or purple hue/glow". Yours burn cyan, the enemy's violet.
    private static readonly Color TealGem = Color.FromHtml("#3FE6F2");
    private static readonly Color CrimsonGem = Color.FromHtml("#B46BFF");
    private static readonly Color Ash = Color.FromHtml("#6D6A66");

    /// <summary>0 = this phone's player (teal), 1 = the opponent (crimson).</summary>
    public int Side { get; set; }
    /// <summary>Reference-height scale (viewport height / 1080).</summary>
    public float S { get; set; } = 1f;

    private string _type = "";
    private string _name = "";
    private int _charges, _maxCharges;
    private bool _suppressed, _occupied;
    private double _t;
    private float _flash;          // 0..1, decays — a trigger fired
    private Label _typeLabel = default!;
    private Label _nameLabel = default!;

    public ReliquaryFrame()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>The weapon word for an artifact's slot pool ("dagger" → "DAGGER", "book" → "TOME").</summary>
    public static string TypeWord(string? slotPool) => (slotPool ?? "").ToLowerInvariant() switch
    {
        "sword" => "SWORD",
        "shield" => "SHIELD",
        "wand" => "WAND",
        "aura" => "AURA",
        "grimoire" => "SKULL",
        "phylactery" => "RITUAL",
        "hammer" => "HAMMER",
        "banner" => "BANNER",
        "book" => "TOME",
        "totem" => "BOND",
        "dagger" => "DAGGER",
        "orb" => "ORB",
        "starlight" => "STARLIGHT",
        "" => "RELIC",
        var p => p.ToUpperInvariant(),
    };

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ZIndex = 2;

        var typeFont = new FontVariation { BaseFont = GetHeaderFont(18), SpacingGlyph = Mathf.RoundToInt(3 * S) };
        _typeLabel = MakeLabel(typeFont, Mathf.RoundToInt(17 * S), GoldHi);
        _nameLabel = MakeLabel(GetHeaderFont(16), Mathf.RoundToInt(15 * S), Color.FromHtml("#F2E4C2"));
        _nameLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        _nameLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        ApplyTexts();
        Layout();
        Resized += Layout;
    }

    private Label MakeLabel(Font font, int px, Color color)
    {
        var l = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore, AutoTranslateMode = AutoTranslateModeEnum.Disabled,
            ClipText = true,
        };
        l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", px);
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeConstantOverride("outline_size", Mathf.RoundToInt(4 * S));
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.75f));
        AddChild(l);
        return l;
    }

    /// <summary>Update what the reliquary shows. Cheap; call on every HUD render.</summary>
    public void Set(bool occupied, string typeWord, string name, int charges, int maxCharges, bool suppressed)
    {
        bool changed = occupied != _occupied || typeWord != _type || name != _name
            || charges != _charges || maxCharges != _maxCharges || suppressed != _suppressed;
        _occupied = occupied; _type = typeWord; _name = name;
        _charges = charges; _maxCharges = maxCharges; _suppressed = suppressed;
        if (_typeLabel == null) return;
        ApplyTexts();
        if (changed) { Layout(); QueueRedraw(); }
    }

    private void ApplyTexts()
    {
        bool occupied = _occupied, suppressed = _suppressed;
        string typeWord = _type, name = _name;
        _typeLabel.Text = occupied ? typeWord : "";
        // "Wand" under a WAND plaque says nothing twice — the name plaque only when it adds something.
        bool nameAddsInfo = occupied && !string.IsNullOrEmpty(name) && !name.Equals(typeWord, StringComparison.OrdinalIgnoreCase);
        _nameRaw = nameAddsInfo ? name : "";
        _nameLabel.Text = _nameRaw;
        _nameLabel.Modulate = suppressed ? new Color(0.7f, 0.7f, 0.7f) : Colors.White;
        _typeLabel.Modulate = suppressed ? new Color(0.7f, 0.7f, 0.7f) : Colors.White;
    }

    /// <summary>A trigger fired: the gold flares for a moment.</summary>
    public void Flash() => _flash = 1f;

    private bool Full => _maxCharges > 0 && _charges >= _maxCharges && !_suppressed;
    private Color Gem => _suppressed ? Ash : Side == 0 ? TealGem : CrimsonGem;

    // ── geometry shared by Layout and _Draw ──
    private float Band => 9f * S;                          // gilded band thickness
    private float Chamfer => 14f * S;
    private Rect2 TypePlaque()
    {
        var f = Font(_typeLabel);
        float tw = string.IsNullOrEmpty(_type) ? 0 : f.GetStringSize(_type, HorizontalAlignment.Left, -1, _typeLabel.GetThemeFontSize("font_size")).X;
        // FABLE-048: the plaque may overhang the frame a little, and the word shrinks to fit (Layout) —
        // "STARLIGHT" used to be clipped to "STARLIGH".
        float w = Mathf.Clamp(tw + 30f * S, 70f * S, Size.X + 14f * S);
        float h = 30f * S;
        return new Rect2((Size.X - w) / 2f, -h * 0.42f, w, h);
    }
    private bool HasNameLine => !string.IsNullOrEmpty(_nameLabel?.Text);
    private bool _nameTwoLines;
    private string _nameRaw = "";
    private Rect2 NamePlaque()
    {
        float nameH = _nameTwoLines ? 44f : 28f;
        float h = (HasNameLine ? nameH + (_maxCharges > 0 ? 16f : 2f) : 30f) * S;
        return new Rect2(Band + 2f * S, Size.Y - Band - h - 2f * S, Size.X - 2 * (Band + 2f * S), h);
    }
    private static Font Font(Label l) => l.GetThemeFont("font");

    private void Layout()
    {
        if (_typeLabel == null) return;
        // FABLE-048: the type word always fits its plaque (shrink before clipping)
        {
            int tpx = Mathf.RoundToInt(17 * S), tfloor = Mathf.RoundToInt(11 * S);
            var tf = Font(_typeLabel);
            float maxW = Size.X + 14f * S - 22f * S;
            while (tpx > tfloor && !string.IsNullOrEmpty(_type) && tf.GetStringSize(_type, HorizontalAlignment.Left, -1, tpx).X > maxW) tpx--;
            _typeLabel.AddThemeFontSizeOverride("font_size", tpx);
            _typeLabel.ClipText = false;
        }
        // FABLE-048: a long name breaks onto two balanced lines instead of ending in "…".
        // Explicit "\n" split with autowrap off: Label autowrap inside this hand-laid frame rendered blank.
        {
            var nf = Font(_nameLabel);
            float innerW = Size.X - 2 * (Band + 2f * S) - 8f * S - 4f * S;
            string raw = _nameRaw ?? "";
            _nameTwoLines = raw.Contains(' ') && nf.GetStringSize(raw, HorizontalAlignment.Left, -1, Mathf.RoundToInt(13 * S)).X > innerW;
            _nameLabel.Text = _nameTwoLines ? SplitBalanced(raw) : raw;
            _nameLabel.AutowrapMode = TextServer.AutowrapMode.Off;
            _nameLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            _nameLabel.ClipText = false;
        }
        var tp = TypePlaque();
        _typeLabel.Position = tp.Position;
        _typeLabel.Size = tp.Size;
        var np = NamePlaque();
        float lineH = (_nameTwoLines ? 44f : 28f) * S;
        _nameLabel.Position = np.Position + new Vector2(4f * S, 1f * S);
        _nameLabel.Size = new Vector2(np.Size.X - 8f * S, lineH);
        // the whole name, always: shrink before wrapping, wrap before trimming
        int px = Mathf.RoundToInt(16 * S), floor = Mathf.RoundToInt(13 * S);
        var f = Font(_nameLabel);
        if (_nameTwoLines) { px = Mathf.RoundToInt(15 * S); floor = Mathf.RoundToInt(11 * S); }
        foreach (var line in _nameLabel.Text.Split('\n'))
            while (px > floor && f.GetStringSize(line, HorizontalAlignment.Left, -1, px).X > _nameLabel.Size.X - 4f * S) px--;
        _nameLabel.AddThemeFontSizeOverride("font_size", px);
    }

    /// <summary>Break at the space that leaves the two lines closest in length.</summary>
    private static string SplitBalanced(string s)
    {
        int best = -1, bestDiff = int.MaxValue;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != ' ') continue;
            int d = Math.Abs(i - (s.Length - i - 1));
            if (d < bestDiff) { bestDiff = d; best = i; }
        }
        return best < 0 ? s : s[..best] + "\n" + s[(best + 1)..];
    }

    public override void _Process(double delta)
    {
        if (!_occupied && _flash <= 0f) return;
        if (CampaignContext.ReduceMotion) { if (_flash > 0f) { _flash = 0f; QueueRedraw(); } return; }
        _t += delta;
        if (_flash > 0f) _flash = Mathf.Max(0f, _flash - (float)delta * 2.2f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var sz = Size;
        if (sz.X < 4 || sz.Y < 4) return;
        float c = Chamfer, b = Band;
        var gem = Gem;
        bool motion = !CampaignContext.ReduceMotion;

        // ── halo: breathing, in the side's colour ──
        if (_occupied)
        {
            float breathe = motion ? 0.5f + 0.5f * Mathf.Sin((float)_t * 1.7f + Side * 1.3f) : 0.6f;
            float strength = (_suppressed ? 0.10f : 0.34f + 0.20f * breathe) + (Full ? 0.25f : 0f) + _flash * 0.45f;
            for (int i = 1; i <= 14; i++)
            {
                float g = i * 1.4f * S;
                var ring = Closed(ChamferPts(new Rect2(-g, -g, sz.X + 2 * g, sz.Y + 2 * g), c + g));
                float k = 1f - i / 15f;
                DrawPolyline(ring, new Color(gem.R, gem.G, gem.B, strength * k * k * 0.42f), 1.8f * S, true);
            }
        }

        // ── the gilded band: bronze lip → gold → bright edge → gold, as nested chamfered lines ──
        var outer = new Rect2(Vector2.Zero, sz);
        // FABLE-048: the metal itself is enchanted — gold drawn toward the side's hue (cyan / violet)
        var gold = _suppressed ? Ash : GoldMid.Lerp(gem, 0.55f);
        var hi = _suppressed ? Ash.Lightened(0.25f) : GoldHi.Lerp(gem.Lightened(0.55f), 0.6f).Lerp(Colors.White, _flash * 0.6f);
        var deep = _suppressed ? Ash.Darkened(0.5f) : GoldDeep.Lerp(gem.Darkened(0.65f), 0.6f);
        DrawBand(outer, c, 0f, 2.2f * S, deep);
        DrawBand(outer, c, 2.0f * S, 2.6f * S, gold);
        DrawBand(outer, c, 4.2f * S, 1.4f * S, hi);
        DrawBand(outer, c, 5.4f * S, 2.4f * S, gold);
        DrawBand(outer, c, 7.6f * S, 1.6f * S, deep);
        // inner hairline, inset from the band
        DrawBand(outer, c, b + 3.5f * S, 1.1f * S, new Color(hi.R, hi.G, hi.B, 0.55f));

        // ── the travelling spark ──
        if (_occupied && motion && !_suppressed)
        {
            var path = Closed(ChamferPts(new Rect2(new Vector2(4.8f * S, 4.8f * S), sz - new Vector2(9.6f * S, 9.6f * S)), c - 4f * S));
            float per = PathLength(path);
            float head = (float)((_t * 0.22 + Side * 0.5) % 1.0) * per;
            const int segs = 14;
            float tail = 46f * S;
            for (int i = 0; i < segs; i++)
            {
                float a0 = head - tail * i / segs, a1 = head - tail * (i + 1) / segs;
                float k = 1f - (float)i / segs;
                DrawLine(PointAt(path, per, a0), PointAt(path, per, a1), new Color(gem.Lightened(0.7f).R, gem.Lightened(0.7f).G, gem.Lightened(0.7f).B, 0.9f * k * k), 2.6f * S * k + 0.6f, true);
            }
        }

        // ── the window's depth: the art sits BEHIND the gold, shadowed at its edges ──
        {
            float i0 = b, d = 16f * S;
            var inner0 = new Rect2(i0, i0, sz.X - 2 * i0, sz.Y - 2 * i0);
            var dark = new Color(0, 0, 0, 0.55f); var clear = new Color(0, 0, 0, 0);
            DrawPolygon(new[] { inner0.Position, inner0.Position + new Vector2(inner0.Size.X, 0), inner0.Position + new Vector2(inner0.Size.X, d), inner0.Position + new Vector2(0, d) }, new[] { dark, dark, clear, clear });
            DrawPolygon(new[] { inner0.Position, inner0.Position + new Vector2(d, 0), inner0.Position + new Vector2(d, inner0.Size.Y), inner0.Position + new Vector2(0, inner0.Size.Y) }, new[] { dark, clear, clear, dark });
            DrawPolygon(new[] { inner0.End - new Vector2(d, inner0.Size.Y), inner0.End - new Vector2(0, inner0.Size.Y), inner0.End, inner0.End - new Vector2(d, 0) }, new[] { clear, dark, dark, clear });
        }

        // ── studs at the middle of each long side, and a crest gem at the foot ──
        foreach (var mp in new[] { new Vector2(b * 0.5f, sz.Y / 2f), new Vector2(sz.X - b * 0.5f, sz.Y / 2f) })
        {
            Diamond(mp, 6.5f * S, deep);
            Diamond(mp, 5f * S, hi);
            Diamond(mp, 2.6f * S, gem);
        }
        {
            var cp = new Vector2(sz.X / 2f, sz.Y - b * 0.45f);
            DrawCircle(cp, 9f * S, deep);
            DrawCircle(cp, 7.8f * S, gold);
            Diamond(cp, 6f * S, gem.Darkened(0.3f));
            Diamond(cp + new Vector2(0, -1f * S), 4f * S, gem);
            DrawCircle(cp + new Vector2(-1.5f * S, -2.5f * S), 1.4f * S, new Color(1, 1, 1, 0.9f));
            DrawLine(cp + new Vector2(-24f * S, 0), cp + new Vector2(-11f * S, 0), hi, 1.4f * S, true);
            DrawLine(cp + new Vector2(11f * S, 0), cp + new Vector2(24f * S, 0), hi, 1.4f * S, true);
        }

        // ── corner filigree: gold brackets + a set gem ──
        foreach (var (p, dx, dy) in new[]
        {
            (new Vector2(0, 0), 1f, 1f), (new Vector2(sz.X, 0), -1f, 1f),
            (new Vector2(0, sz.Y), 1f, -1f), (new Vector2(sz.X, sz.Y), -1f, -1f),
        })
        {
            var inCorner = p + new Vector2(dx, dy) * (b + 1f * S);
            float arm = 22f * S;
            DrawLine(inCorner, inCorner + new Vector2(dx * arm, 0), hi, 1.6f * S, true);
            DrawLine(inCorner, inCorner + new Vector2(0, dy * arm), hi, 1.6f * S, true);
            // curl at the arm ends
            DrawArc(inCorner + new Vector2(dx * arm, dy * 3f * S), 3f * S, 0, Mathf.Tau, 12, hi, 1.2f * S, true);
            DrawArc(inCorner + new Vector2(dx * 3f * S, dy * arm), 3f * S, 0, Mathf.Tau, 12, hi, 1.2f * S, true);
            // the gem sits on the chamfer
            var gp = p + new Vector2(dx, dy) * (c * 0.52f);
            float r = 5.6f * S;
            DrawCircle(gp, r + 2.2f * S, deep);
            DrawCircle(gp, r + 1.2f * S, gold);
            DrawCircle(gp, r, gem.Darkened(0.35f));
            DrawCircle(gp + new Vector2(-r * 0.2f, -r * 0.2f), r * 0.62f, gem);
            DrawCircle(gp + new Vector2(-r * 0.35f, -r * 0.38f), r * 0.24f, new Color(1, 1, 1, 0.85f));
        }

        if (!_occupied) return;

        // ── name plaque at the foot ──
        var np = NamePlaque();
        bool hasName = !string.IsNullOrEmpty(_nameLabel?.Text);
        if (hasName || _maxCharges > 0)
        {
            DrawRect(np, new Color(0.05f, 0.04f, 0.03f, 0.86f));
            DrawLine(np.Position, np.Position + new Vector2(np.Size.X, 0), new Color(gold.R, gold.G, gold.B, 0.9f), 1.4f * S, true);
            // charge pips as small gems at the right of the plaque
            if (_maxCharges > 0)
            {
                float pr = 4.6f * S, step = 13f * S;
                float x = np.GetCenter().X - (_maxCharges - 1) * step / 2f;
                float y = hasName ? np.End.Y - 10f * S : np.GetCenter().Y;
                for (int i = 0; i < _maxCharges; i++)
                {
                    var pc = new Vector2(x + i * step, y);
                    bool lit = i < _charges && !_suppressed;
                    Diamond(pc, pr + 1.4f * S, deep);
                    Diamond(pc, pr, lit ? gem.Lightened(Full ? 0.3f : 0f) : new Color(0.18f, 0.16f, 0.14f));
                    if (lit) Diamond(pc + new Vector2(0, -pr * 0.35f), pr * 0.35f, new Color(1, 1, 1, 0.7f));
                }
            }
        }

        // ── type plaque crowning the top edge ──
        if (!string.IsNullOrEmpty(_type))
        {
            var tp = TypePlaque();
            var pts = new[]
            {
                tp.Position + new Vector2(tp.Size.Y * 0.5f, 0), tp.Position + new Vector2(tp.Size.X - tp.Size.Y * 0.5f, 0),
                tp.Position + new Vector2(tp.Size.X, tp.Size.Y * 0.5f), tp.End - new Vector2(tp.Size.Y * 0.5f, 0),
                tp.Position + new Vector2(tp.Size.Y * 0.5f, tp.Size.Y), tp.Position + new Vector2(0, tp.Size.Y * 0.5f),
            };
            // soft shadow under the plaque
            var sh = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++) sh[i] = pts[i] + new Vector2(0, 2.5f * S);
            DrawColoredPolygon(sh, new Color(0, 0, 0, 0.55f));
            var cols = new Color[pts.Length];
            for (int i = 0; i < pts.Length; i++)
                cols[i] = Color.FromHtml("#2A2118").Lerp(Color.FromHtml("#0F0C09"), (pts[i].Y - tp.Position.Y) / tp.Size.Y);
            DrawPolygon(pts, cols);
            DrawPolyline(Closed(pts), gold, 1.8f * S, true);
            var inset = new Vector2[pts.Length];
            var ctr = tp.GetCenter();
            for (int i = 0; i < pts.Length; i++) inset[i] = ctr + (pts[i] - ctr) * new Vector2(1f - 6f * S / tp.Size.X, 1f - 6f * S / tp.Size.Y);
            DrawPolyline(Closed(inset), new Color(hi.R, hi.G, hi.B, 0.45f), 1f * S, true);
            // gem studs at the plaque's points
            Diamond(pts[5], 3.6f * S, gem);
            Diamond(pts[2], 3.6f * S, gem);
        }
    }

    // ── helpers ──

    /// <summary>One ring of the gilded band, lit from the upper left: each edge takes the light by
    /// the way it faces, so the gold reads as a raised, bevelled metal rim rather than a flat line.</summary>
    private void DrawBand(Rect2 outer, float c, float inset, float width, Color col)
    {
        float m = inset + width / 2f;
        var r = new Rect2(outer.Position + new Vector2(m, m), outer.Size - new Vector2(2 * m, 2 * m));
        var pts = Closed(ChamferPts(r, Mathf.Max(1f, c - m * 0.6f)));
        var light = new Vector2(-0.55f, -0.83f);
        var centre = r.GetCenter();
        for (int i = 1; i < pts.Length; i++)
        {
            var mid = (pts[i - 1] + pts[i]) / 2f;
            float lit = (mid - centre).Normalized().Dot(light);        // -1 shadowed … +1 facing the light
            var shade = lit >= 0 ? col.Lerp(Colors.White, lit * 0.28f) : col.Lerp(Colors.Black, -lit * 0.42f);
            DrawLine(pts[i - 1], pts[i], shade, width, true);
        }
    }

    private void Diamond(Vector2 p, float d, Color col) =>
        DrawColoredPolygon(new[] { p + new Vector2(0, -d), p + new Vector2(d, 0), p + new Vector2(0, d), p + new Vector2(-d, 0) }, col);

    private static Vector2[] ChamferPts(Rect2 r, float c)
    {
        c = Mathf.Min(c, Mathf.Min(r.Size.X, r.Size.Y) / 2f);
        float x0 = r.Position.X, y0 = r.Position.Y, x1 = r.End.X, y1 = r.End.Y;
        return new[]
        {
            new Vector2(x0 + c, y0), new Vector2(x1 - c, y0), new Vector2(x1, y0 + c), new Vector2(x1, y1 - c),
            new Vector2(x1 - c, y1), new Vector2(x0 + c, y1), new Vector2(x0, y1 - c), new Vector2(x0, y0 + c),
        };
    }

    private static Vector2[] Closed(Vector2[] pts)
    {
        var o = new Vector2[pts.Length + 1];
        Array.Copy(pts, o, pts.Length);
        o[^1] = pts[0];
        return o;
    }

    private static float PathLength(Vector2[] p)
    {
        float l = 0;
        for (int i = 1; i < p.Length; i++) l += p[i - 1].DistanceTo(p[i]);
        return l;
    }

    private static Vector2 PointAt(Vector2[] p, float per, float d)
    {
        d %= per;
        if (d < 0) d += per;
        for (int i = 1; i < p.Length; i++)
        {
            float seg = p[i - 1].DistanceTo(p[i]);
            if (d <= seg) return p[i - 1].Lerp(p[i], seg <= 0 ? 0 : d / seg);
            d -= seg;
        }
        return p[^1];
    }
}

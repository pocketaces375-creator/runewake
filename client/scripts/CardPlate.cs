using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// CardPlate — addendum-compliant draw order: art → name plate → stat strip + badges →
/// gold keyline → frame → cost badge. Frame is last on top with transparent window.
/// </summary>
public partial class CardPlate : Control
{
    public CardPlate() { MouseFilter = MouseFilterEnum.Ignore; }

    private static bool _loaded = false;
    private static Vector4 _artWin, _nameR, _statR, _wellL, _wellR, _keyRect, _wind;
    private static Vector2 _costAnchor;
    private static float _keyThick, _costDiam;

    private static void L()
    {
        if (_loaded) return;
        using var f = Godot.FileAccess.Open("res://content/art/frame/card_template.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null) { GD.PrintErr("[CT] JSON not found"); return; }
        var d = Json.ParseString(f.GetAsText()).AsGodotDictionary();
        var a4 = (Godot.Collections.Array v) => new Vector4((float)(double)v[0], (float)(double)v[1], (float)(double)v[2], (float)(double)v[3]);
        var a2 = (Godot.Collections.Array v) => new Vector2((float)(double)v[0], (float)(double)v[1]);
        _wind = a4(d["window"].AsGodotArray());
        _artWin = a4(d["art_window"].AsGodotArray());
        _nameR = a4(d["name_plate"].AsGodotArray());
        _statR = a4(d["stat_strip"].AsGodotArray());
        _wellL = a4(d["stat_well_left"].AsGodotArray());
        _wellR = a4(d["stat_well_right"].AsGodotArray());
        var k = d["gold_keyline"].AsGodotDictionary();
        _keyRect = a4(k["rect"].AsGodotArray());
        _keyThick = (float)(double)k["thickness"];
        var c = d["cost_badge"].AsGodotDictionary();
        _costDiam = (float)(double)c["diameter"];
        _costAnchor = a2(c["anchor"].AsGodotArray());
        _loaded = true;
    }

    private TextureRect? _frameBg;
    private TextureRect? _artBg;
    private Control? _namePlate;
    private Label? _cardName;
    private Control? _statPlate;
    private Label? _atkBadge, _vigBadge;
    private ColorRect? _keyline;

    private float _cw, _ch;

    // Addendum exact colors
    private static Color GOLD_KEY = new Color(0.788f, 0.659f, 0.298f);  // #C9A84C
    private static Color NAME_BG = new Color(0.784f, 0.722f, 0.596f);   // #C8B898
    private static Color NAME_FG = new Color(0.227f, 0.157f, 0.086f);   // #3A2816
    private static Color STAT_BG = new Color(0.126f, 0.118f, 0.102f);   // #201E1A
    private static Color ATK_BORDER = new Color(0.722f, 0.220f, 0.180f);// #B8382E
    private static Color ATK_FILL = new Color(0.788f, 0.294f, 0.235f);  // #C94A3C
    private static Color VIG_BORDER = new Color(0.243f, 0.478f, 0.243f);// #3E7A3E
    private static Color VIG_FILL = new Color(0.306f, 0.549f, 0.306f);  // #4E8C4E
    private static Color STAT_NUM = new Color(0.941f, 0.894f, 0.816f);  // #F0E4D0
    private static Color COST_BG = new Color(0.165f, 0.118f, 0.071f);   // #2A1E12
    private static Color COST_RING = new Color(0.788f, 0.659f, 0.298f); // #C9A84C
    private static Color COST_NUM = new Color(0.910f, 0.851f, 0.659f);  // #E8D9A8

    public void Setup(string name, int? attack, int? vigor, Strata strata,
        float cardWidth, float cardHeight, int cost = 0, bool isArtifact = false,
        Texture2D? artTexture = null)
    {
        L();
        _cw = cardWidth; _ch = cardHeight;
        Position = Vector2.Zero; Size = new Vector2(cardWidth, cardHeight);

        if (_frameBg == null)
        {
            // 1. CARD ART
            _artBg = new TextureRect { MouseFilter = MouseFilterEnum.Ignore, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize };
            AddChild(_artBg);

            // 2. NAME PLATE — pale parchment
            _namePlate = new Control { MouseFilter = MouseFilterEnum.Ignore };
            AddChild(_namePlate);
            _cardName = new Label
            {
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.Off,
                MaxLinesVisible = 1,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
            };
            _cardName.AddThemeColorOverride("font_color", NAME_FG);
            _cardName.AddThemeConstantOverride("outline_size", 0);
            _namePlate.AddChild(_cardName);

            // 3. STAT STRIP + BADGES
            _statPlate = new Control { MouseFilter = MouseFilterEnum.Ignore };
            AddChild(_statPlate);
            _atkBadge = MakeBadge(ATK_FILL, ATK_BORDER);
            _statPlate.AddChild(_atkBadge);
            _vigBadge = MakeBadge(VIG_FILL, VIG_BORDER);
            _statPlate.AddChild(_vigBadge);

            // 4. GOLD KEYLINE — inner edge of window, beneath frame
            _keyline = new ColorRect { MouseFilter = MouseFilterEnum.Ignore };
            AddChild(_keyline);

            // 5. FRAME — LAST on top
            _frameBg = new TextureRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Texture = GD.Load<Texture2D>("res://content/art/frame/card_frame.png"),
                Position = Vector2.Zero,
                Size = new Vector2(cardWidth, cardHeight)
            };
            AddChild(_frameBg);
        }

        // Position art
        float ax = _artWin.X * cardWidth, ay = _artWin.Y * cardHeight;
        float aw = _artWin.Z * cardWidth, ah = _artWin.W * cardHeight;
        _artBg.Position = new Vector2(ax, ay);
        _artBg.Size = new Vector2(aw, ah);
        if (artTexture != null) _artBg.Texture = artTexture;

        // Name plate — spans full window width
        float nx = _nameR.X * cardWidth, ny = _nameR.Y * cardHeight;
        float nw = _nameR.Z * cardWidth, nh = _nameR.W * cardHeight;
        _namePlate.Position = new Vector2(nx, ny);
        _namePlate.Size = new Vector2(nw, nh);
        _namePlate.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = NAME_BG });

        _cardName.Size = new Vector2(nw, nh);
        _cardName.Text = name;
        int nameFs = Mathf.Clamp(Mathf.RoundToInt(nh * 0.62f), 9, 24);
        _cardName.AddThemeFontSizeOverride("font_size", nameFs);

        // Stat strip — dark charcoal recess
        float sx = _statR.X * cardWidth, sy = _statR.Y * cardHeight;
        float sw = _statR.Z * cardWidth, sh = _statR.W * cardHeight;
        _statPlate.Position = new Vector2(sx, sy);
        _statPlate.Size = new Vector2(sw, sh);
        _statPlate.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = STAT_BG });

        // Left badge (attack) and right badge (vigor)
        float lx = _wellL.X * cardWidth, ly = _wellL.Y * cardHeight;
        float lw = _wellL.Z * cardWidth, lh = _wellL.W * cardHeight;
        float rx = _wellR.X * cardWidth, ry = _wellR.Y * cardHeight;
        float rw = _wellR.Z * cardWidth, rh = _wellR.W * cardHeight;
        float cornerR = Mathf.RoundToInt(lw * 0.08f);

        _atkBadge.Visible = attack.HasValue;
        if (attack.HasValue) SetBadge(_atkBadge, attack!.Value.ToString(), lx, ly, lw, lh, cornerR, ATK_FILL, ATK_BORDER);
        _vigBadge.Visible = vigor.HasValue;
        if (vigor.HasValue) SetBadge(_vigBadge, vigor!.Value.ToString(), rx, ry, rw, rh, cornerR, VIG_FILL, VIG_BORDER);

        // Gold keyline — rectangle along window inner edge
        float kthick = Mathf.Max(_keyThick * cardWidth, 1f);
        float kw = _wind.Z * cardWidth, kh = _wind.W * cardHeight;
        _keyline.Position = new Vector2(_wind.X * cardWidth, _wind.Y * cardHeight);
        _keyline.Size = new Vector2(kw, kh);
        _keyline.Color = Colors.Transparent;
        _keyline.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Colors.Transparent,
            BorderColor = GOLD_KEY,
            BorderWidthLeft = Mathf.RoundToInt(kthick),
            BorderWidthTop = Mathf.RoundToInt(kthick),
            BorderWidthRight = Mathf.RoundToInt(kthick),
            BorderWidthBottom = Mathf.RoundToInt(kthick),
            ContentMarginLeft = 0, ContentMarginTop = 0, ContentMarginRight = 0, ContentMarginBottom = 0
        });

        // Frame already positioned — it's on top, window is transparent

        // Cost badge — above frame, at window top-right corner
        // (This is not created in Setup — too expensive per card; needs a static helper)
    }

    private static Label MakeBadge(Color fill, Color border)
    {
        var l = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        l.AddThemeColorOverride("font_color", STAT_NUM);
        l.AddThemeColorOverride("font_outline_color", Colors.Black);
        l.AddThemeConstantOverride("outline_size", 1);
        return l;
    }

    private static void SetBadge(Label l, string text, float x, float y, float w, float h, float cr, Color fill, Color border)
    {
        l.Text = text;
        l.Position = new Vector2(x, y);
        l.Size = new Vector2(w, h);
        int fs = Mathf.Clamp(Mathf.RoundToInt(h * 0.55f), 9, 20);
        l.AddThemeFontSizeOverride("font_size", fs);
        l.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = fill,
            BorderColor = border,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = Mathf.RoundToInt(cr),
            CornerRadiusTopRight = Mathf.RoundToInt(cr),
            CornerRadiusBottomLeft = Mathf.RoundToInt(cr),
            CornerRadiusBottomRight = Mathf.RoundToInt(cr),
            ContentMarginLeft = 2, ContentMarginRight = 2, ContentMarginTop = 1, ContentMarginBottom = 1
        });
    }

    public static Label MakeCostRune(int cost, float cardWidth, float cardHeight, out float diam)
    {
        L();
        diam = _costDiam * cardWidth;
        float cx = _costAnchor.X * cardWidth, cy = _costAnchor.Y * cardHeight;
        // Circle centred on window top-right corner
        float bx = cx - diam / 2f;
        float by = cy;

        var l = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Text = cost.ToString()
        };
        l.AddThemeColorOverride("font_color", COST_NUM);
        l.AddThemeConstantOverride("outline_size", 0);
        l.Position = new Vector2(bx, by);
        l.Size = new Vector2(diam, diam);
        l.AddThemeFontSizeOverride("font_size", Mathf.Max(11, Mathf.RoundToInt(diam * 0.5f)));
        l.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = COST_BG,
            BorderColor = COST_RING,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = Mathf.RoundToInt(diam / 2f),
            CornerRadiusTopRight = Mathf.RoundToInt(diam / 2f),
            CornerRadiusBottomLeft = Mathf.RoundToInt(diam / 2f),
            CornerRadiusBottomRight = Mathf.RoundToInt(diam / 2f)
        });
        return l;
    }

    public static void UpdateCostRuneStyle(Label label, float diam)
    {
        label.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = COST_BG,
            BorderColor = COST_RING,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = Mathf.RoundToInt(diam / 2f),
            CornerRadiusTopRight = Mathf.RoundToInt(diam / 2f),
            CornerRadiusBottomLeft = Mathf.RoundToInt(diam / 2f),
            CornerRadiusBottomRight = Mathf.RoundToInt(diam / 2f)
        });
    }

    public void SetStatValues(int? attack, int? vigor)
    {
        if (attack.HasValue && _atkBadge != null) _atkBadge.Text = attack.Value.ToString();
        if (vigor.HasValue && _vigBadge != null) _vigBadge.Text = vigor.Value.ToString();
    }

    public Label? GetNameLabel() => _cardName;
    public Rect2 GetNameRect() => _cardName?.GetRect() ?? new Rect2();
}
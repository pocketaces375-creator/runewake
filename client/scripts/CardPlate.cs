using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// CardPlate — draws frame LAST on top, with interior regions inside the transparent window.
/// Draw order: card art → name plate → stat strip → frame.
/// </summary>
public partial class CardPlate : Control
{
    public CardPlate() { MouseFilter = MouseFilterEnum.Ignore; }

    private static bool _templateLoaded = false;
    private static Vector4 _wind = Vector4.Zero;
    private static Vector4 _artWin = Vector4.Zero;
    private static Vector4 _nameR = Vector4.Zero;
    private static Vector4 _statR = Vector4.Zero;
    private static Vector4 _wellL = Vector4.Zero;
    private static Vector4 _wellR = Vector4.Zero;
    private static Vector2 _costA = Vector2.Zero;

    private static void Load()
    {
        if (_templateLoaded) return;
        using var file = Godot.FileAccess.Open("res://content/art/frame/card_template.json", Godot.FileAccess.ModeFlags.Read);
        if (file == null) { GD.PrintErr("[CT] JSON not found"); return; }
        var d = Json.ParseString(file.GetAsText()).AsGodotDictionary();
        var a = (Godot.Collections.Array v) => new Vector4((float)(double)v[0], (float)(double)v[1], (float)(double)v[2], (float)(double)v[3]);
        _wind = a(d["window"].AsGodotArray());
        _artWin = a(d["art_window"].AsGodotArray());
        _nameR = a(d["name_plate"].AsGodotArray());
        _statR = a(d["stat_strip"].AsGodotArray());
        _wellL = a(d["stat_well_left"].AsGodotArray());
        _wellR = a(d["stat_well_right"].AsGodotArray());
        var ca = d["cost_badge_anchor"].AsGodotArray();
        _costA = new Vector2((float)(double)ca[0], (float)(double)ca[1]);
        _templateLoaded = true;
    }

    private TextureRect? _frameBg;
    private TextureRect? _artBg;
    private Control? _namePlate;
    private Label? _cardName;
    private Control? _statPlate;
    private Label? _attackBadge;
    private Label? _vigorBadge;

    private float _cw, _ch;
    private bool _hasAtk, _hasVig;

    private static Color _nameBg = new Color(0.95f, 0.90f, 0.80f);
    private static Color _statBg = new Color(0.12f, 0.10f, 0.08f);
    private static Color _nameText = new Color(0.25f, 0.18f, 0.10f);
    private static Color _badgeAtk = new Color(0.70f, 0.08f, 0.05f);
    private static Color _badgeVig = new Color(0.05f, 0.45f, 0.18f);
    private static Color _badgeText = new Color(1f, 1f, 1f);

    public void Setup(string name, int? attack, int? vigor, Strata strata,
        float cardWidth, float cardHeight, int cost = 0, bool isArtifact = false,
        Texture2D? artTexture = null)
    {
        Load();
        _cw = cardWidth; _ch = cardHeight;
        _hasAtk = attack.HasValue; _hasVig = vigor.HasValue;
        Position = Vector2.Zero; Size = new Vector2(cardWidth, cardHeight);

        if (_frameBg == null)
        {
            // 1. CARD ART — inside art_window
            _artBg = new TextureRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize
            };
            AddChild(_artBg);

            // 2. NAME PLATE — pale parchment filling name_plate
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
            _cardName.AddThemeColorOverride("font_color", _nameText);
            _cardName.AddThemeConstantOverride("outline_size", 0);
            _namePlate.AddChild(_cardName);

            // 3. STAT STRIP — dark recess
            _statPlate = new Control { MouseFilter = MouseFilterEnum.Ignore };
            AddChild(_statPlate);
            _attackBadge = MakeBadge(_badgeAtk);
            _statPlate.AddChild(_attackBadge);
            _vigorBadge = MakeBadge(_badgeVig);
            _statPlate.AddChild(_vigorBadge);

            // 4. FRAME — LAST, on top of everything
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

        // Position art inside art_window
        float ax = _artWin.X * cardWidth, ay = _artWin.Y * cardHeight;
        float aw = _artWin.Z * cardWidth, ah = _artWin.W * cardHeight;
        _artBg.Position = new Vector2(ax, ay);
        _artBg.Size = new Vector2(aw, ah);
        if (artTexture != null) _artBg.Texture = artTexture;

        // Name plate — pale strip
        float nx = _nameR.X * cardWidth, ny = _nameR.Y * cardHeight;
        float nw = _nameR.Z * cardWidth, nh = _nameR.W * cardHeight;
        _namePlate.Position = new Vector2(nx, ny);
        _namePlate.Size = new Vector2(nw, nh);
        var ns = new StyleBoxFlat
        {
            BgColor = _nameBg,
            ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 1, ContentMarginBottom = 1
        };
        _namePlate.AddThemeStyleboxOverride("panel", ns);

        _cardName.Size = new Vector2(nw, nh);
        _cardName.Text = name;
        int fontSize = Mathf.Clamp(Mathf.RoundToInt(nh * 0.58f), 9, 22);
        _cardName.AddThemeFontSizeOverride("font_size", fontSize);

        // Stat strip — dark recess
        float sx = _statR.X * cardWidth, sy = _statR.Y * cardHeight;
        float sw = _statR.Z * cardWidth, sh = _statR.W * cardHeight;
        _statPlate.Position = new Vector2(sx, sy);
        _statPlate.Size = new Vector2(sw, sh);
        var ss = new StyleBoxFlat { BgColor = _statBg };
        _statPlate.AddThemeStyleboxOverride("panel", ss);

        // Stat wells
        float lx = _wellL.X * cardWidth, ly = _wellL.Y * cardHeight;
        float lw = _wellL.Z * cardWidth, lh = _wellL.W * cardHeight;
        float rx = _wellR.X * cardWidth, ry = _wellR.Y * cardHeight;
        float rw = _wellR.Z * cardWidth, rh = _wellR.W * cardHeight;

        float badgeH = lh * 0.7f;
        float badgeY = ly + (lh - badgeH) / 2f;

        _attackBadge.Visible = _hasAtk;
        if (_hasAtk)
        {
            _attackBadge.Text = attack!.Value.ToString();
            _attackBadge.Size = new Vector2(lw, badgeH);
            _attackBadge.Position = new Vector2(lx + (lw - lw) / 2f, badgeY);
            int fs = Mathf.Clamp(Mathf.RoundToInt(badgeH * 0.55f), 9, 18);
            _attackBadge.AddThemeFontSizeOverride("font_size", fs);
        }

        _vigorBadge.Visible = _hasVig;
        if (_hasVig)
        {
            _vigorBadge.Text = vigor!.Value.ToString();
            _vigorBadge.Size = new Vector2(rw, badgeH);
            _vigorBadge.Position = new Vector2(rx + (rw - rw) / 2f, badgeY);
            int fs = Mathf.Clamp(Mathf.RoundToInt(badgeH * 0.55f), 9, 18);
            _vigorBadge.AddThemeFontSizeOverride("font_size", fs);
        }

        // Frame is already positioned — it's on top, window is transparent
    }

    private static Label MakeBadge(Color bg)
    {
        var l = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        l.AddThemeColorOverride("font_color", _badgeText);
        l.AddThemeColorOverride("font_outline_color", Colors.Black);
        l.AddThemeConstantOverride("outline_size", 1);
        var s = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = new Color(0.15f, 0.12f, 0.08f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            ContentMarginLeft = 2, ContentMarginRight = 2, ContentMarginTop = 1, ContentMarginBottom = 1
        };
        l.AddThemeStyleboxOverride("normal", s);
        return l;
    }

    public static Label MakeCostRune(int cost, float cardWidth, float cardHeight, out float hexSize)
    {
        Load();
        hexSize = cardWidth * 0.12f;
        float ax = _costA.X * cardWidth, ay = _costA.Y * cardHeight;
        float hx = ax - hexSize - 2f, hy = ay + 2f;

        var l = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Text = cost.ToString()
        };
        l.AddThemeColorOverride("font_color", FrameHexText);
        l.AddThemeConstantOverride("outline_size", 1);
        l.AddThemeColorOverride("font_outline_color", Colors.Black);
        l.Position = new Vector2(hx, hy);
        l.Size = new Vector2(hexSize, hexSize);
        l.AddThemeFontSizeOverride("font_size", Mathf.Max(11, Mathf.RoundToInt(hexSize * 0.5f)));
        var hs = new StyleBoxFlat
        {
            BgColor = FrameHexFill, BorderColor = FrameHexBorder,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = Mathf.RoundToInt(hexSize / 2f), CornerRadiusTopRight = Mathf.RoundToInt(hexSize / 2f),
            CornerRadiusBottomLeft = Mathf.RoundToInt(hexSize / 2f), CornerRadiusBottomRight = Mathf.RoundToInt(hexSize / 2f)
        };
        l.AddThemeStyleboxOverride("normal", hs);
        return l;
    }

    public static void UpdateCostRuneStyle(Label label, float hexSize)
    {
        var hs = new StyleBoxFlat
        {
            BgColor = FrameHexFill, BorderColor = FrameHexBorder,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = Mathf.RoundToInt(hexSize / 2f), CornerRadiusTopRight = Mathf.RoundToInt(hexSize / 2f),
            CornerRadiusBottomLeft = Mathf.RoundToInt(hexSize / 2f), CornerRadiusBottomRight = Mathf.RoundToInt(hexSize / 2f)
        };
        label.AddThemeStyleboxOverride("normal", hs);
    }

    public void SetStatValues(int? attack, int? vigor)
    {
        if (_hasAtk && attack.HasValue && _attackBadge != null) _attackBadge.Text = attack.Value.ToString();
        if (_hasVig && vigor.HasValue && _vigorBadge != null) _vigorBadge.Text = vigor.Value.ToString();
    }

    public Label? GetNameLabel() => _cardName;
    public Rect2 GetNameRect() => _cardName?.GetRect() ?? new Rect2();
}
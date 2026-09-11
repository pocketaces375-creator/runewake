using Godot;
using Runewake.Engine.Cards;

namespace Runewake.Client;

/// <summary>
/// CardPlate — addendum draw order: art → name plate → stat strip + badges →
/// gold keyline → frame → cost badge. All regions hardcoded from frame_1 measurements.
/// </summary>
public partial class CardPlate : Control
{
    public CardPlate() { MouseFilter = MouseFilterEnum.Ignore; }

    // Hardcoded from frame_1 window (180,199)-(655,1023) = 57.09%×67.76% of 832x1216
    private static readonly float W_X = 0.2163f, W_Y = 0.1637f, W_W = 0.5709f, W_H = 0.6776f;
    // Art = top 83%, Name = next 7%, Stat = bottom 10% of window height
    private static readonly float A_X = W_X, A_Y = W_Y, A_W = W_W, A_H = W_H * 0.83f;
    private static readonly float N_X = W_X, N_Y = W_Y + W_H * 0.83f, N_W = W_W, N_H = W_H * 0.07f;
    private static readonly float S_X = W_X, S_Y = W_Y + W_H * 0.90f, S_W = W_W, S_H = W_H * 0.10f;
    // Badges: side = 78% of strip height, inset 6% window width from ends
    private static readonly float _bdgS = S_H * 0.78f;
    private static readonly float _bdgY = S_Y + (S_H - _bdgS) / 2f;
    private static readonly float _inset = W_W * 0.06f;
    private static readonly float BL_X = W_X + _inset, BL_Y = _bdgY, BL_S = _bdgS;
    private static readonly float BR_X = W_X + W_W - _inset - _bdgS, BR_Y = _bdgY, BR_S = _bdgS;
    // Cost badge: diameter 12% of card width, centre on window top-right
    private static readonly float _costD = 0.12f;
    private static readonly float _costCX = W_X + W_W;
    private static readonly float _costCY = W_Y;

    // Addendum exact colors
    private static readonly Color GOLD = new Color(0.788f, 0.659f, 0.298f);
    private static readonly Color N_BG = new Color(0.784f, 0.722f, 0.596f);
    private static readonly Color N_FG = new Color(0.227f, 0.157f, 0.086f);
    private static readonly Color S_BG = new Color(0.126f, 0.118f, 0.102f);
    private static readonly Color AB = new Color(0.722f, 0.220f, 0.180f);
    private static readonly Color AF = new Color(0.788f, 0.294f, 0.235f);
    private static readonly Color VB = new Color(0.243f, 0.478f, 0.243f);
    private static readonly Color VF = new Color(0.306f, 0.549f, 0.306f);
    private static readonly Color SN = new Color(0.941f, 0.894f, 0.816f);
    private static readonly Color CB = new Color(0.165f, 0.118f, 0.071f);
    private static readonly Color CR = new Color(0.788f, 0.659f, 0.298f);
    private static readonly Color CN = new Color(0.910f, 0.851f, 0.659f);

    private TextureRect? _frameBg, _artBg;
    private Control? _nameCtrl, _statCtrl;
    private Label? _nameLabel, _atkL, _vigL;
    private ColorRect? _keyline;

    public void Setup(string name, int? attack, int? vigor, Strata strata,
        float cw, float ch, int cost = 0, bool isArtifact = false,
        Texture2D? artTexture = null)
    {
        Position = Vector2.Zero;
        Size = new Vector2(cw, ch);

        if (_frameBg == null)
        {
            // 1. Card art
            _artBg = new TextureRect { MouseFilter = MouseFilterEnum.Ignore, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize };
            AddChild(_artBg);

            // 2. Name plate
            _nameCtrl = new Control { MouseFilter = MouseFilterEnum.Ignore };
            AddChild(_nameCtrl);
            _nameLabel = new Label
            {
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.Off,
                MaxLinesVisible = 1,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
            };
            _nameLabel.AddThemeColorOverride("font_color", N_FG);
            _nameCtrl.AddChild(_nameLabel);

            // 3. Stat strip + badges
            _statCtrl = new Control { MouseFilter = MouseFilterEnum.Ignore };
            AddChild(_statCtrl);
            _atkL = MkBadge(AF, AB);
            _statCtrl.AddChild(_atkL);
            _vigL = MkBadge(VF, VB);
            _statCtrl.AddChild(_vigL);

            // 4. Gold keyline
            _keyline = new ColorRect { MouseFilter = MouseFilterEnum.Ignore };
            AddChild(_keyline);

            // 5. Frame — LAST on top
            _frameBg = new TextureRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Texture = GD.Load<Texture2D>("res://content/art/frame/card_frame.png"),
                Position = Vector2.Zero,
                Size = new Vector2(cw, ch)
            };
            AddChild(_frameBg);
        }

        // Position art
        _artBg.Position = new Vector2(A_X * cw, A_Y * ch);
        _artBg.Size = new Vector2(A_W * cw, A_H * ch);
        if (artTexture != null) _artBg.Texture = artTexture;

        // Name plate
        _nameCtrl.Position = new Vector2(N_X * cw, N_Y * ch);
        _nameCtrl.Size = new Vector2(N_W * cw, N_H * ch);
        _nameCtrl.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = N_BG });

        _nameLabel.Size = new Vector2(N_W * cw, N_H * ch);
        _nameLabel.Text = name;
        _nameLabel.AddThemeFontSizeOverride("font_size", Mathf.Clamp(Mathf.RoundToInt(N_H * ch * 0.62f), 9, 24));

        // Stat strip
        _statCtrl.Position = new Vector2(S_X * cw, S_Y * ch);
        _statCtrl.Size = new Vector2(S_W * cw, S_H * ch);
        _statCtrl.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = S_BG });

        float badgeS = BL_S * cw;
        float cr = Mathf.RoundToInt(badgeS * 0.08f);
        _atkL.Visible = attack.HasValue;
        if (attack.HasValue) Place(_atkL, attack!.Value.ToString(), BL_X * cw, BL_Y * ch, badgeS, cr, AF, AB);
        _vigL.Visible = vigor.HasValue;
        if (vigor.HasValue) Place(_vigL, vigor!.Value.ToString(), BR_X * cw, BR_Y * ch, badgeS, cr, VF, VB);

        // Gold keyline — inner edge of window
        float kt = Mathf.Max(0.005f * cw, 1f);
        _keyline.Position = new Vector2(W_X * cw, W_Y * ch);
        _keyline.Size = new Vector2(W_W * cw, W_H * ch);
        _keyline.Color = Colors.Transparent;
        _keyline.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Colors.Transparent,
            BorderColor = GOLD,
            BorderWidthLeft = Mathf.RoundToInt(kt),
            BorderWidthTop = Mathf.RoundToInt(kt),
            BorderWidthRight = Mathf.RoundToInt(kt),
            BorderWidthBottom = Mathf.RoundToInt(kt)
        });

        // Frame is on top — positioned at creation, window is transparent
    }

    private static Label MkBadge(Color fill, Color border)
    {
        var l = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        l.AddThemeColorOverride("font_color", SN);
        l.AddThemeColorOverride("font_outline_color", Colors.Black);
        l.AddThemeConstantOverride("outline_size", 1);
        return l;
    }

    private static void Place(Label l, string text, float x, float y, float s, float cr, Color fill, Color border)
    {
        l.Text = text;
        l.Position = new Vector2(x, y);
        l.Size = new Vector2(s, s);
        l.AddThemeFontSizeOverride("font_size", Mathf.Clamp(Mathf.RoundToInt(s * 0.55f), 9, 20));
        l.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = fill, BorderColor = border,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = Mathf.RoundToInt(cr), CornerRadiusTopRight = Mathf.RoundToInt(cr),
            CornerRadiusBottomLeft = Mathf.RoundToInt(cr), CornerRadiusBottomRight = Mathf.RoundToInt(cr),
            ContentMarginLeft = 2, ContentMarginRight = 2, ContentMarginTop = 1, ContentMarginBottom = 1
        });
    }

    public static Label MakeCostRune(int cost, float cw, float ch, out float diam)
    {
        diam = _costD * cw;
        float cx = _costCX * cw, cy = _costCY * ch;
        float bx = cx - diam / 2f, by = cy;

        var l = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Text = cost.ToString()
        };
        l.AddThemeColorOverride("font_color", CN);
        l.Position = new Vector2(bx, by);
        l.Size = new Vector2(diam, diam);
        l.AddThemeFontSizeOverride("font_size", Mathf.Max(11, Mathf.RoundToInt(diam * 0.5f)));
        l.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = CB, BorderColor = CR,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = Mathf.RoundToInt(diam / 2f), CornerRadiusTopRight = Mathf.RoundToInt(diam / 2f),
            CornerRadiusBottomLeft = Mathf.RoundToInt(diam / 2f), CornerRadiusBottomRight = Mathf.RoundToInt(diam / 2f)
        });
        return l;
    }

    public static void UpdateCostRuneStyle(Label label, float diam)
    {
        label.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = CB, BorderColor = CR,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = Mathf.RoundToInt(diam / 2f), CornerRadiusTopRight = Mathf.RoundToInt(diam / 2f),
            CornerRadiusBottomLeft = Mathf.RoundToInt(diam / 2f), CornerRadiusBottomRight = Mathf.RoundToInt(diam / 2f)
        });
    }

    public void SetStatValues(int? attack, int? vigor)
    {
        if (attack.HasValue && _atkL != null) _atkL.Text = attack.Value.ToString();
        if (vigor.HasValue && _vigL != null) _vigL.Text = vigor.Value.ToString();
    }

    public Label? GetNameLabel() => _nameLabel;
    public Rect2 GetNameRect() => _nameLabel?.GetRect() ?? new Rect2();
}
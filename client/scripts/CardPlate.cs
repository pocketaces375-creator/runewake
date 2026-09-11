using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// CardPlate — art fills full card, thin code-drawn border, translucent name band.
/// </summary>
public partial class CardPlate : Control
{
    public CardPlate() { MouseFilter = MouseFilterEnum.Ignore; }

    private TextureRect? _artBg;
    private Control? _nameBar;
    private Label? _cardName;
    private Label? _attackBadge;
    private Label? _vigorBadge;
    private NinePatchRect? _borderDecor;
    private bool _firstSetup = true;

    private float _designCardWidth;
    private float _designCardHeight;
    private bool _hasAttack;
    private bool _hasVigor;

    private static Color _borderColor = new Color(0.08f, 0.06f, 0.04f);
    private static Color _nameBarBg = new Color(0.06f, 0.04f, 0.03f, 0.75f);
    private static Color _nameText = new Color(0.95f, 0.88f, 0.72f);
    private static Color _nameOutline = new Color(0.05f, 0.03f, 0.02f);
    private static Color _badgeBgAttack = new Color(0.65f, 0.08f, 0.05f);
    private static Color _badgeBgVigor = new Color(0.05f, 0.45f, 0.18f);
    private static Color _badgeBorder = new Color(0.15f, 0.12f, 0.08f);
    private static Color _badgeText = new Color(1f, 0.97f, 0.88f);

    public void Setup(string name, int? attack, int? vigor, Strata strata,
        float cardWidth, float cardHeight, int cost = 0, bool isArtifact = false,
        Texture2D? artTexture = null)
    {
        _designCardWidth = cardWidth;
        _designCardHeight = cardHeight;
        _hasAttack = attack.HasValue;
        _hasVigor = vigor.HasValue;

        Position = Vector2.Zero;
        Size = new Vector2(cardWidth, cardHeight);

        // First-time node setup
        if (_firstSetup)
        {
            _firstSetup = false;

            // Card border — thin dark edge
            var borderStyle = new StyleBoxFlat
            {
                BgColor = Colors.Transparent,
                BorderColor = _borderColor,
                BorderWidthLeft = 3, BorderWidthTop = 3, BorderWidthRight = 3, BorderWidthBottom = 3,
                CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
                CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
                ContentMarginLeft = 3, ContentMarginTop = 3, ContentMarginRight = 3, ContentMarginBottom = 3
            };
            AddThemeStyleboxOverride("panel", borderStyle);

            // Art fills entire card
            _artBg = new TextureRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Position = Vector2.Zero,
                Size = new Vector2(cardWidth, cardHeight)
            };
            AddChild(_artBg);

            // Name bar — translucent, overlays the art bottom
            _nameBar = new Control
            {
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(_nameBar);

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
            _cardName.AddThemeColorOverride("font_outline_color", _nameOutline);
            _cardName.AddThemeConstantOverride("outline_size", 3);
            _cardName.AddThemeFontSizeOverride("font_size", 18);
            _nameBar.AddChild(_cardName);

            // Stat badges
            _attackBadge = MakeStatBadge(_badgeBgAttack);
            AddChild(_attackBadge);
            _vigorBadge = MakeStatBadge(_badgeBgVigor);
            AddChild(_vigorBadge);
        }

        // Set art
        if (artTexture != null) _artBg.Texture = artTexture;

        // Name bar — bottom quarter of card, full width inside border
        float inset = 4f;
        float nameH = cardHeight * 0.045f;
        if (nameH < 14f) nameH = 14f;
        float nameY = cardHeight - nameH - cardHeight * 0.04f - inset;

        _nameBar.Position = new Vector2(inset, nameY);
        _nameBar.Size = new Vector2(cardWidth - inset * 2f, nameH);
        var nameStyle = new StyleBoxFlat
        {
            BgColor = _nameBarBg,
            BorderColor = new Color(0.5f, 0.4f, 0.25f, 0.3f),
            BorderWidthLeft = 0, BorderWidthTop = 1, BorderWidthRight = 0, BorderWidthBottom = 0,
            ContentMarginLeft = 6, ContentMarginRight = 6
        };
        _nameBar.AddThemeStyleboxOverride("panel", nameStyle);

        _cardName.Size = new Vector2(_nameBar.Size.X, nameH);
        _cardName.Text = name;
        int fontSize = Mathf.Clamp(Mathf.RoundToInt(nameH * 0.55f), 9, 22);
        _cardName.AddThemeFontSizeOverride("font_size", fontSize);
        _cardName.SetPosition(Vector2.Zero);

        // Stat badges — bottom corners, inside border
        float statSize = cardHeight * 0.045f;
        if (statSize < 16f) statSize = 16f;
        float statY = cardHeight - statSize - inset;
        float sidePad = inset + 2f;

        _attackBadge.Visible = _hasAttack;
        if (_hasAttack)
        {
            _attackBadge.Text = attack!.Value.ToString();
            _attackBadge.Size = new Vector2(statSize * 1.4f, statSize);
            int fs = Mathf.Clamp(Mathf.RoundToInt(statSize * 0.48f), 9, 18);
            _attackBadge.AddThemeFontSizeOverride("font_size", fs);
            _attackBadge.Position = new Vector2(sidePad, statY);
        }

        _vigorBadge.Visible = _hasVigor;
        if (_hasVigor)
        {
            _vigorBadge.Text = vigor!.Value.ToString();
            _vigorBadge.Size = new Vector2(statSize * 1.4f, statSize);
            int fs = Mathf.Clamp(Mathf.RoundToInt(statSize * 0.48f), 9, 18);
            _vigorBadge.AddThemeFontSizeOverride("font_size", fs);
            _vigorBadge.Position = new Vector2(cardWidth - sidePad - statSize * 1.4f, statY);
        }
    }

    private static Label MakeStatBadge(Color bgColor)
    {
        var badge = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.AddThemeColorOverride("font_color", _badgeText);
        badge.AddThemeColorOverride("font_outline_color", Colors.Black);
        badge.AddThemeConstantOverride("outline_size", 2);
        var style = new StyleBoxFlat
        {
            BgColor = bgColor,
            BorderColor = _badgeBorder,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 4, ContentMarginTop = 1, ContentMarginRight = 4, ContentMarginBottom = 1
        };
        badge.AddThemeStyleboxOverride("normal", style);
        return badge;
    }

    public static Label MakeCostRune(int cost, float cardWidth, float cardHeight, out float hexSize)
    {
        hexSize = cardWidth * 0.14f;
        float hexX = cardWidth - hexSize - 2f;
        float hexY = 2f;

        var label = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Text = cost.ToString()
        };
        label.AddThemeColorOverride("font_color", FrameHexText);
        label.AddThemeConstantOverride("outline_size", 1);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.Position = new Vector2(hexX, hexY);
        label.Size = new Vector2(hexSize, hexSize);
        int costFontSize = Mathf.Max(11, Mathf.RoundToInt(hexSize * 0.5f));
        label.AddThemeFontSizeOverride("font_size", costFontSize);
        var hexStyle = new StyleBoxFlat
        {
            BgColor = FrameHexFill, BorderColor = FrameHexBorder,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = Mathf.RoundToInt(hexSize / 2f), CornerRadiusTopRight = Mathf.RoundToInt(hexSize / 2f),
            CornerRadiusBottomLeft = Mathf.RoundToInt(hexSize / 2f), CornerRadiusBottomRight = Mathf.RoundToInt(hexSize / 2f)
        };
        label.AddThemeStyleboxOverride("normal", hexStyle);
        return label;
    }

    public static void UpdateCostRuneStyle(Label label, float hexSize)
    {
        var hexStyle = new StyleBoxFlat
        {
            BgColor = FrameHexFill, BorderColor = FrameHexBorder,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = Mathf.RoundToInt(hexSize / 2f), CornerRadiusTopRight = Mathf.RoundToInt(hexSize / 2f),
            CornerRadiusBottomLeft = Mathf.RoundToInt(hexSize / 2f), CornerRadiusBottomRight = Mathf.RoundToInt(hexSize / 2f)
        };
        label.AddThemeStyleboxOverride("normal", hexStyle);
    }

    public void SetStatValues(int? attack, int? vigor)
    {
        if (_attackBadge == null || _vigorBadge == null) return;
        if (_hasAttack && attack.HasValue) _attackBadge.Text = attack.Value.ToString();
        if (_hasVigor && vigor.HasValue) _vigorBadge.Text = vigor.Value.ToString();
    }

    public Label? GetNameLabel() => _cardName;
    public Rect2 GetNameRect() => _cardName?.GetRect() ?? new Rect2();
}
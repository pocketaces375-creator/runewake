using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// CardPlate — paints the dark vignette frame template behind card art.
/// Art fills the large central window (~82% height × 75% width).
/// Name plate renders just below art, tone-tinted from the card's artwork.
/// Attack/vigor badges sit at bottom with high contrast.
/// </summary>
public partial class CardPlate : Control
{
    public CardPlate() { MouseFilter = MouseFilterEnum.Ignore; }

    private static bool _templateLoaded = false;
    private static Vector4 _artWindow = Vector4.Zero;
    private static Vector4 _namePlate = Vector4.Zero;
    private static Vector4 _statStrip = Vector4.Zero;
    private static Vector2 _costAnchor = Vector2.Zero;
    private static float _bandFrac = 0.126f;

    private static void LoadTemplateRegions()
    {
        if (_templateLoaded) return;
        var path = "res://content/art/frame/card_template.json";
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null) { GD.PrintErr("[CARDTEMPLATE] JSON not found"); return; }
        var json = Json.ParseString(file.GetAsText());
        var dict = json.AsGodotDictionary();
        var aw = dict["art_window"].AsGodotArray();
        var np = dict["name_plate"].AsGodotArray();
        var ss = dict["stat_strip"].AsGodotArray();
        var ca = dict["cost_badge_anchor"].AsGodotArray();
        _artWindow = new Vector4((float)(double)aw[0], (float)(double)aw[1], (float)(double)aw[2], (float)(double)aw[3]);
        _namePlate = new Vector4((float)(double)np[0], (float)(double)np[1], (float)(double)np[2], (float)(double)np[3]);
        _statStrip = new Vector4((float)(double)ss[0], (float)(double)ss[1], (float)(double)ss[2], (float)(double)ss[3]);
        _costAnchor = new Vector2((float)(double)ca[0], (float)(double)ca[1]);
        if (dict.ContainsKey("band_fraction"))
            _bandFrac = (float)(double)dict["band_fraction"];
        _templateLoaded = true;
    }

    private TextureRect? _templateBg;
    private TextureRect? _artBg;
    private Control? _nameBar;
    private Label? _cardName;
    private Label? _attackBadge;
    private Label? _vigorBadge;

    private float _designCardWidth;
    private float _designCardHeight;
    private string _cardNameText = "";
    private bool _hasAttack;
    private bool _hasVigor;
    private bool _isArtifact;

    public float PlateHeight => _designCardHeight;

    private static Color _namePlateBg = new Color(0.08f, 0.06f, 0.05f, 0.75f);
    private static Color _nameText = new Color(0.92f, 0.85f, 0.70f);
    private static Color _nameOutline = new Color(0.05f, 0.03f, 0.02f);

    public void Setup(string name, int? attack, int? vigor, Strata strata,
        float cardWidth, float cardHeight, int cost = 0, bool isArtifact = false,
        Texture2D? artTexture = null)
    {
        LoadTemplateRegions();
        _designCardWidth = cardWidth;
        _designCardHeight = cardHeight;
        _cardNameText = name;
        _hasAttack = attack.HasValue;
        _hasVigor = vigor.HasValue;
        _isArtifact = isArtifact;

        Position = Vector2.Zero;
        Size = new Vector2(cardWidth, cardHeight);

        if (_templateBg == null)
        {
            _templateBg = new TextureRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Texture = GD.Load<Texture2D>("res://content/art/frame/card_template.png"),
                Position = Vector2.Zero,
                Size = new Vector2(cardWidth, cardHeight)
            };
            AddChild(_templateBg);

            _artBg = new TextureRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize
            };
            AddChild(_artBg);

            // Name bar — semi-transparent dark band below art
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
            _cardName.AddThemeConstantOverride("outline_size", 2);
            _cardName.AddThemeFontSizeOverride("font_size", 18);
            _nameBar.AddChild(_cardName);

            // Stat badges
            _attackBadge = MakeStatBadge(FrameStatAttack, 5f, cardWidth);
            AddChild(_attackBadge);
            _vigorBadge = MakeStatBadge(FrameStatVigor, 5f, cardWidth);
            AddChild(_vigorBadge);
        }

        // Art window — fill from template proportions
        float awX = _artWindow.X * cardWidth;
        float awY = _artWindow.Y * cardHeight;
        float awW = _artWindow.Z * cardWidth;
        float awH = _artWindow.W * cardHeight;
        _artBg.Position = new Vector2(awX, awY);
        _artBg.Size = new Vector2(awW, awH);
        _artBg.Texture = artTexture;

        // Name bar — below art window, spanning the card width
        float band = _bandFrac * cardWidth;
        float npX = band;
        float npY = awY + awH + 1f;
        float npW = cardWidth - band * 2f;
        float npH = cardHeight * 0.045f;
        if (npH < 14f) npH = 14f;

        _nameBar.Position = new Vector2(npX, npY);
        _nameBar.Size = new Vector2(npW, npH);
        var nameStyle = new StyleBoxFlat
        {
            BgColor = _namePlateBg,
            BorderColor = new Color(0.7f, 0.6f, 0.4f),
            BorderWidthLeft = 0, BorderWidthTop = 1, BorderWidthRight = 0, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 0, CornerRadiusTopRight = 0,
            CornerRadiusBottomLeft = 0, CornerRadiusBottomRight = 0,
            ContentMarginLeft = 4, ContentMarginRight = 4
        };
        _nameBar.AddThemeStyleboxOverride("panel", nameStyle);

        _cardName.Position = Vector2.Zero;
        _cardName.Size = new Vector2(npW, npH);
        _cardName.Text = name;
        int fontSize = Mathf.Clamp(Mathf.RoundToInt(npH * 0.55f), 9, 24);
        _cardName.AddThemeFontSizeOverride("font_size", fontSize);

        // Stat badges — at bottom of card, inside the frame
        float statY = cardHeight - _bandFrac * cardHeight - npH * 0.7f;
        float statSize = cardHeight * 0.045f;
        if (statSize < 14f) statSize = 14f;
        float statInset = band + 3f;

        _attackBadge.Visible = _hasAttack;
        if (_hasAttack)
        {
            _attackBadge.Text = attack!.Value.ToString();
            _attackBadge.Size = new Vector2(statSize * 1.5f, statSize);
            int fs = Mathf.Clamp(Mathf.RoundToInt(statSize * 0.5f), 8, 20);
            _attackBadge.AddThemeFontSizeOverride("font_size", fs);
            _attackBadge.Position = new Vector2(statInset, statY);
        }

        _vigorBadge.Visible = _hasVigor;
        if (_hasVigor)
        {
            _vigorBadge.Text = vigor!.Value.ToString();
            _vigorBadge.Size = new Vector2(statSize * 1.5f, statSize);
            int fs = Mathf.Clamp(Mathf.RoundToInt(statSize * 0.5f), 8, 20);
            _vigorBadge.AddThemeFontSizeOverride("font_size", fs);
            _vigorBadge.Position = new Vector2(cardWidth - statInset - statSize * 1.5f, statY);
        }
    }

    private static Label MakeStatBadge(Color bgColor, float radius, float cardWidth)
    {
        var badge = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        badge.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
        badge.AddThemeConstantOverride("outline_size", 2);
        var style = new StyleBoxFlat
        {
            BgColor = bgColor,
            BorderColor = new Color(0.2f, 0.2f, 0.2f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = Mathf.RoundToInt(radius), CornerRadiusTopRight = Mathf.RoundToInt(radius),
            CornerRadiusBottomLeft = Mathf.RoundToInt(radius), CornerRadiusBottomRight = Mathf.RoundToInt(radius),
            ContentMarginLeft = 6, ContentMarginTop = 1, ContentMarginRight = 6, ContentMarginBottom = 1
        };
        badge.AddThemeStyleboxOverride("normal", style);
        return badge;
    }

    public static Label MakeCostRune(int cost, float cardWidth, float cardHeight, out float hexSize)
    {
        LoadTemplateRegions();
        hexSize = cardWidth * 0.14f;
        float anchorX = _costAnchor.X * cardWidth;
        float anchorY = _costAnchor.Y * cardHeight;
        float hexX = anchorX - hexSize - 2f;
        float hexY = anchorY + 2f;

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
using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

public partial class CardPlate : Control
{
    public CardPlate() { MouseFilter = MouseFilterEnum.Ignore; }

    private static bool _templateLoaded = false;
    private static Vector4 _artWindow = Vector4.Zero;
    private static Vector4 _namePlate = Vector4.Zero;
    private static Vector4 _statStrip = Vector4.Zero;
    private static Vector2 _costAnchor = Vector2.Zero;

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
        _templateLoaded = true;
    }

    private TextureRect? _templateBg;
    private TextureRect? _artBg;
    private Control? _nameClipContainer;
    private Label? _cardName;
    private Label? _attackBadge;
    private Label? _vigorBadge;

    private float _designCardWidth;
    private float _designCardHeight;
    private string _cardNameText = "";
    private bool _hasAttack;
    private bool _hasVigor;
    private bool _isArtifact;

    private struct NameFitResult { public int FontSize; public int LineCount; public float TextHeight; }

    public float PlateHeight => _designCardHeight * _namePlate.W;

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

            _nameClipContainer = new Control
            {
                MouseFilter = MouseFilterEnum.Ignore,
                ClipContents = true
            };
            AddChild(_nameClipContainer);

            _cardName = new Label
            {
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.Off,
                MaxLinesVisible = 1,
                TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming
            };
            _cardName.AddThemeColorOverride("font_color", FrameNameText);
            _nameClipContainer.AddChild(_cardName);

            _attackBadge = MakeStatBadge(FrameStatAttack, 4f);
            AddChild(_attackBadge);

            _vigorBadge = MakeStatBadge(FrameStatVigor, 4f);
            AddChild(_vigorBadge);
        }

        // Art window — composite card art to cover the black region
        float awX = _artWindow.X * cardWidth;
        float awY = _artWindow.Y * cardHeight;
        float awW = _artWindow.Z * cardWidth;
        float awH = _artWindow.W * cardHeight;
        _artBg.Position = new Vector2(awX, awY);
        _artBg.Size = new Vector2(awW, awH);
        if (artTexture != null) _artBg.Texture = artTexture;

        // Name plate — use template proportions or fallback to 7% card height
        float npX = _namePlate.X * cardWidth;
        float npY = _namePlate.Y * cardHeight;
        float npW = _namePlate.Z * cardWidth;
        float npH = _namePlate.W * cardHeight;
        if (npH < 16f) npH = cardHeight * 0.07f;
        if (npW < 40f) npW = cardWidth - npX * 2;
        if (npY < awY + awH) npY = awY + awH + 2f;

        _nameClipContainer.Position = new Vector2(npX, npY);
        _nameClipContainer.Size = new Vector2(npW, npH);
        _cardName.Position = Vector2.Zero;
        _cardName.Size = new Vector2(npW, npH);
        _cardName.Text = name;
        ApplyCardNameFont(_cardName, FontCardName);
        _cardName.AddThemeColorOverride("font_color", new Color(0.25f, 0.18f, 0.10f));
        _cardName.AddThemeConstantOverride("outline_size", 0);
        var fit = FitCardNameAuto(npW * 0.85f, npH);
        _cardName.AddThemeFontSizeOverride("font_size", fit.FontSize);
        _cardName.MaxLinesVisible = fit.LineCount;

        // Stat strip
        float ssY = _statStrip.Y * cardHeight;
        float ssH = _statStrip.W * cardHeight;
        if (ssH < 20f) ssH = cardHeight * 0.06f;
        if (ssY < npY + npH) ssY = cardHeight - ssH - 4f;

        float statW = npW * 0.28f;
        float statH = ssH * 0.65f;
        float statY = ssY + (ssH - statH) / 2f;
        float statInset = npX + 4f;

        _attackBadge.Visible = _hasAttack;
        if (_hasAttack)
        {
            _attackBadge.Text = attack!.Value.ToString();
            _attackBadge.Size = new Vector2(statW, statH);
            int fontSize = Mathf.Clamp(Mathf.RoundToInt(statH * 0.56f), 8, FontStat);
            _attackBadge.AddThemeFontSizeOverride("font_size", fontSize);
            _attackBadge.Position = new Vector2(statInset, statY);
        }

        _vigorBadge.Visible = _hasVigor;
        if (_hasVigor)
        {
            _vigorBadge.Text = vigor!.Value.ToString();
            _vigorBadge.Size = new Vector2(statW, statH);
            int fontSize = Mathf.Clamp(Mathf.RoundToInt(statH * 0.56f), 8, FontStat);
            _vigorBadge.AddThemeFontSizeOverride("font_size", fontSize);
            _vigorBadge.Position = new Vector2(cardWidth - statInset - statW, statY);
        }
    }

    private static Label MakeStatBadge(Color bgColor, float radius)
    {
        var badge = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.AddThemeColorOverride("font_color", FrameStatText);
        badge.AddThemeColorOverride("font_outline_color", Colors.Black);
        badge.AddThemeConstantOverride("outline_size", 1);
        var style = new StyleBoxFlat
        {
            BgColor = bgColor, BorderColor = FrameHexBorder,
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = Mathf.RoundToInt(radius), CornerRadiusTopRight = Mathf.RoundToInt(radius),
            CornerRadiusBottomLeft = Mathf.RoundToInt(radius), CornerRadiusBottomRight = Mathf.RoundToInt(radius),
            ContentMarginLeft = 4, ContentMarginTop = 1, ContentMarginRight = 4, ContentMarginBottom = 1
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

    private NameFitResult FitCardNameAuto(float safeWidth, float maxBandH)
    {
        var empty = new NameFitResult { FontSize = 12, LineCount = 1, TextHeight = 0 };
        if (_cardName == null || string.IsNullOrEmpty(_cardNameText) || safeWidth <= 0) return empty;
        var measureFont = _cardName.GetThemeFont("font") ?? _cardName.GetThemeDefaultFont();
        if (measureFont == null) { _cardName.AddThemeFontSizeOverride("font_size", 12); return empty; }
        string text = _cardNameText.Replace("\n", " ");
        int baseSize = Mathf.Max(6, Mathf.RoundToInt(18f * _designCardWidth / 236f));
        int floorSize = _isArtifact ? 8 : Mathf.Max(10, Mathf.RoundToInt(_designCardWidth * 0.040f));
        float W(int s) => measureFont.GetStringSize(text, HorizontalAlignment.Left, -1, s).X;
        float LH(int s) => measureFont.GetHeight(s);
        int size = baseSize;
        while (size > floorSize && W(size) > safeWidth) size--;
        while (size > floorSize && LH(size) > maxBandH) size--;
        _cardName.Text = text;
        _cardName.MaxLinesVisible = 1;
        _cardName.AutowrapMode = TextServer.AutowrapMode.Off;
        _cardName.TextOverrunBehavior = W(size) > safeWidth ? TextServer.OverrunBehavior.TrimEllipsis : TextServer.OverrunBehavior.NoTrimming;
        _cardName.AddThemeFontSizeOverride("font_size", size);
        return new NameFitResult { FontSize = size, LineCount = 1, TextHeight = LH(size) };
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
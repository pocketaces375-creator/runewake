using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// Unified card frame plate — one template used by HandCard, LaneSlot, and Artifact cards.
/// Provides the Root-Bound border, name band, stat rail, and top-right cost rune.
/// 
/// Layout (top to bottom):
///   [cost rune]    top-right corner (created by parent, not here)
///   [art area]     full card face (art fills behind all overlays)
///   [name band]    dynamic-height band at bottom of art area (grows for two-line names)
///   [stat rail]    attack left, vigor right, docked inside bottom edge
/// 
/// The Root-Bound 9-slice border overlay is handled by RootBoundBorder.
/// </summary>
public partial class CardPlate : Control
{
    public CardPlate()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }
    /// <summary>
    /// A painted plate for a label: the texture is stretched to the label's rect (zero 9-slice
    /// margins), so the art pieces are generated at the aspect they are drawn at. Material comes from
    /// client/content/art/frame — carved basalt and gold, generated from the card border itself, so
    /// the plaque, medallions and coin are the same stone as the frame. Never a flat rectangle.
    /// </summary>
    private static StyleBoxTexture PlateStyle(string piece)
    {
        var sb = new StyleBoxTexture { Texture = GD.Load<Texture2D>($"res://content/art/frame/{piece}.png") };
        sb.ContentMarginLeft = 4; sb.ContentMarginRight = 4; sb.ContentMarginTop = 1; sb.ContentMarginBottom = 1;
        return sb;
    }

    // ── Persistent child nodes ──
    private TextureRect? _nameBandBg;
    private ColorRect? _statRailBg;
    private Label? _cardName;
    private Label? _attackBadge;
    private Label? _vigorBadge;
    /// <summary>Container for name label that clips to name band height.</summary>
    private Control? _nameClipContainer;

    // Cached design dimensions
    private float _designCardWidth;
    private float _designCardHeight;
    private string _cardNameText = "";
    private bool _hasAttack;
    private bool _hasVigor;
    private bool _isArtifact;

    /// <summary>Total height of name band + stat rail</summary>
    public float PlateHeight => _designCardHeight * (FrameNameBandFraction + FrameStatRailFraction);

    // No _Ready — all child nodes created lazily in Setup().

    /// <summary>
    /// Result of the name auto-fit: the chosen font size, line count, and rendered text height.
    /// </summary>
    private struct NameFitResult
    {
        public int FontSize;
        public int LineCount;
        public float TextHeight;
    }

    /// <summary>
    /// Create a stat badge label with styled background — pill-shaped medallion with gold ring.
    /// </summary>
    private static Label MakeStatBadge(Color bgColor, float pillRadius)
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
            CornerRadiusTopLeft = Mathf.RoundToInt(pillRadius), CornerRadiusTopRight = Mathf.RoundToInt(pillRadius),
            CornerRadiusBottomLeft = Mathf.RoundToInt(pillRadius), CornerRadiusBottomRight = Mathf.RoundToInt(pillRadius),
            ContentMarginLeft = 4, ContentMarginTop = 1, ContentMarginRight = 4, ContentMarginBottom = 1
        };
        badge.AddThemeStyleboxOverride("normal", style);
        return badge;
    }

    /// <summary>
    /// Configure the plate for a specific card. Call whenever card size or content changes.
    /// Safe to call from _Ready of parent — internal nodes are created lazily if needed.
    /// </summary>
    public void Setup(string name, int? attack, int? vigor, Strata strata,
        float cardWidth, float cardHeight, int cost = 0, bool isArtifact = false)
    {
        _designCardWidth = cardWidth;
        _designCardHeight = cardHeight;
        _cardNameText = name;
        _hasAttack = attack.HasValue;
        _hasVigor = vigor.HasValue;
        _isArtifact = isArtifact;

        // Lazy init
        if (_nameBandBg == null)
        {
            // ── Name band background — gradient scrim ──
            _nameBandBg = new TextureRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize
            };
            // Vertical gradient: transparent at top → beige/cream at bottom (shader material)
            var gradMat = new ShaderMaterial();
            var gradShader = new Shader();
            gradShader.Code = "shader_type canvas_item; void fragment() { vec4 c = vec4(0.776, 0.741, 0.667, 1.0); COLOR = vec4(c.rgb, c.a * UV.y); }";
            gradMat.Shader = gradShader;
            _nameBandBg.Material = gradMat;
            // Assign a 1x1 white pixel texture so the shader has pixels to alpha-ramp
            var whiteImg = new Image();
            whiteImg.SetData(1, 1, false, Image.Format.Rgba8, new byte[] { 255, 255, 255, 255 });
            _nameBandBg.Texture = ImageTexture.CreateFromImage(whiteImg);
            AddChild(_nameBandBg);

            // ── Stat rail background ──
            _statRailBg = new ColorRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Color = FrameStatRail
            };
            AddChild(_statRailBg);

            // ── Name clipping container — prevents text from overflowing into stat rail ──
            _nameClipContainer = new Control
            {
                MouseFilter = MouseFilterEnum.Ignore,
                ClipContents = true
            };
            AddChild(_nameClipContainer);

            // ── Card name label (inside clipping container) ──
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
            _cardName.AddThemeConstantOverride("outline_size", 2);
            _cardName.AddThemeColorOverride("font_outline_color", Color.FromHtml("#0A0806FF"));
            _nameClipContainer.AddChild(_cardName);

            // ── Attack badge ──
            _attackBadge = MakeStatBadge(FrameStatAttack, 8f);
            AddChild(_attackBadge);

            // ── Vigor badge ──
            _vigorBadge = MakeStatBadge(FrameStatVigor, 8f);
            AddChild(_vigorBadge);
        }

        // ═══ LAYOUT ═══
        // Reserve the stat rail FIRST (fixed fraction of card height). The name band
        // occupies the remainder ABOVE the rail; it can grow beyond its baseline
        // fraction when a two-line name needs more height (never touches the rail).

        float railH = cardHeight * FrameStatRailFraction;
        float baseBandH = cardHeight * FrameNameBandFraction;
        // Rail is reserved first; the band may grow upward into the art remainder.
        float maxBandH = Mathf.Max(baseBandH, cardHeight - railH - 2f);

        int bandPx = Mathf.Max(1, Mathf.RoundToInt(cardWidth * 0.07f));
        int bufferPx = Mathf.Max(Mathf.RoundToInt(cardWidth * 0.06f), 10);
        float safeWidth = cardWidth - bandPx * 2 - bufferPx * 2;

        // Fit the name against the MAXIMUM available band height (rail already reserved).
        // The clip container is sized to max initially; Setup re-sizes it to the actual band.
        _nameClipContainer.Position = new Vector2(bandPx + bufferPx, 0);
        _nameClipContainer.Size = new Vector2(safeWidth, maxBandH - 2f);

        _cardName.Position = Vector2.Zero;
        _cardName.Size = new Vector2(safeWidth, maxBandH - 2f);
        _cardName.Text = name;
        ApplyCardNameFont(_cardName, FontCardName);
        // TASK-CARDNAME-COLOR-1: dark brown, no outline (Trikzos approved)
        _cardName.AddThemeColorOverride("font_color", new Color(0.25f, 0.18f, 0.10f));
        _cardName.AddThemeConstantOverride("outline_size", 0);
        var fit = FitCardNameAuto(safeWidth, maxBandH);

        // Actual band height = text height + small padding, never below baseline, never
        // above the rail-reserved remainder.
        float nameBandH = Mathf.Clamp(fit.TextHeight + 4f, baseBandH, maxBandH);

        // Position this control so the rail is docked at the card bottom.
        float plateH = nameBandH + railH;
        Position = new Vector2(0, cardHeight - plateH);
        Size = new Vector2(cardWidth, plateH);

        // ── Name band (top of plate) ──
        _nameBandBg.Position = new Vector2(0, 0);
        _nameBandBg.Size = new Vector2(cardWidth, nameBandH);

        // ── Stat rail (bottom of plate, docked) ──
        _statRailBg.Position = new Vector2(0, nameBandH);
        _statRailBg.Size = new Vector2(cardWidth, railH);

        // ── Name label clip container — exact band height ──
        _nameClipContainer.Position = new Vector2(bandPx + bufferPx, 2f);
        _nameClipContainer.Size = new Vector2(safeWidth, Mathf.Max(1f, nameBandH - 6f));
        _cardName.Size = new Vector2(safeWidth, Mathf.Max(1f, nameBandH - 6f));

        // Re-apply the fitted label properties (font size, text, lines) — the auto-fit
        // already applied them against the max band; sizes are unchanged, this just
        // guarantees consistency after the container is re-sized.
        _cardName.AddThemeFontSizeOverride("font_size", fit.FontSize);
        _cardName.MaxLinesVisible = fit.LineCount;


        // ── Stat rail: attack left, vigor right, DOCKED INSIDE (no overhang) — pill-shaped gold-ring medallions ──
        // Card law: a stat sits in a keyline box on the soil band — 19% of card
        // width, 70% of the rail height, square corners, not a floating pill.
        float statChipW = cardWidth * 0.190f;
        float statChipH = railH * 0.70f;
        float pillRadius = Mathf.Max(1f, cardWidth * 0.010f);
        float statChipY = nameBandH + (railH - statChipH) / 2f;

        _attackBadge.Visible = _hasAttack;
        if (_hasAttack)
        {
            _attackBadge.Text = attack!.Value.ToString();
            _attackBadge.Size = new Vector2(statChipW, statChipH);
            int fontSize = Mathf.Clamp(Mathf.RoundToInt(statChipH * 0.56f), 8, FontStat);
            _attackBadge.AddThemeFontSizeOverride("font_size", fontSize);
            // BOARD-MATCH-2: sit flush at frame's bottom corners — inside Root-Bound border
            float chipBandInset = bandPx + 2f;
            _attackBadge.Position = new Vector2(chipBandInset, statChipY);
            // Recreate stylebox with correct pill radius for current size
            var attStyle = new StyleBoxFlat
            {
                BgColor = FrameStatAttack, BorderColor = FrameHexBorder,
                BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = Mathf.RoundToInt(pillRadius), CornerRadiusTopRight = Mathf.RoundToInt(pillRadius),
                CornerRadiusBottomLeft = Mathf.RoundToInt(pillRadius), CornerRadiusBottomRight = Mathf.RoundToInt(pillRadius),
                ContentMarginLeft = 4, ContentMarginTop = 1, ContentMarginRight = 4, ContentMarginBottom = 1
            };
            _attackBadge.AddThemeStyleboxOverride("normal", attStyle);
        }

        _vigorBadge.Visible = _hasVigor;
        if (_hasVigor)
        {
            _vigorBadge.Text = vigor!.Value.ToString();
            _vigorBadge.Size = new Vector2(statChipW, statChipH);
            int fontSize = Mathf.Clamp(Mathf.RoundToInt(statChipH * 0.56f), 8, FontStat);
            _vigorBadge.AddThemeFontSizeOverride("font_size", fontSize);
            // BOARD-MATCH-2: sit flush at frame's bottom corners — inside Root-Bound border
            float vigorX = cardWidth - statChipW - bandPx - 2f;
            _vigorBadge.Position = new Vector2(vigorX, statChipY);
            var vigStyle = new StyleBoxFlat
            {
                BgColor = FrameStatVigor, BorderColor = FrameHexBorder,
                BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = Mathf.RoundToInt(pillRadius), CornerRadiusTopRight = Mathf.RoundToInt(pillRadius),
                CornerRadiusBottomLeft = Mathf.RoundToInt(pillRadius), CornerRadiusBottomRight = Mathf.RoundToInt(pillRadius),
                ContentMarginLeft = 4, ContentMarginTop = 1, ContentMarginRight = 4, ContentMarginBottom = 1
            };
            _vigorBadge.AddThemeStyleboxOverride("normal", vigStyle);
        }
    }

    /// <summary>
    /// Create a cost rune label at the top-right of the card.
    /// Caller adds to the card's Content node.
    /// </summary>
    public static Label MakeCostRune(int cost, float cardWidth, float cardHeight, out float hexSize)
    {
        hexSize = cardWidth * FrameHexSizeFraction;
        int bandPx = Mathf.Max(1, Mathf.RoundToInt(cardWidth * 0.07f));

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

        // Position at top-right inside the Root-Bound border
        float hexX = cardWidth - bandPx - hexSize - 2f;
        float hexY = bandPx + 2f;
        label.Position = new Vector2(hexX, hexY);
        label.Size = new Vector2(hexSize, hexSize);

        int costFontSize = Mathf.Max(11, Mathf.RoundToInt(hexSize * 0.5f));
        label.AddThemeFontSizeOverride("font_size", costFontSize);

        // Hex border via stylebox — circular dark badge with gold ring
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

    /// <summary>
    /// Update the cost rune style to be a proper circle at the current size.
    /// Call after changing label.Size to ensure corner radius matches.
    /// </summary>
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

    /// <summary>
    /// Fit the card name on ONE line. The card law, restated by Trikzos on
    /// 2026-09-08 after a worker made long names wrap: a name is one line,
    /// always. Shrink from the base size down to a floor; ellipsis only if it
    /// still will not fit. Never a second line, never a \n, never autowrap.
    /// </summary>
    private NameFitResult FitCardNameAuto(float safeWidth, float maxBandH)
    {
        var empty = new NameFitResult { FontSize = 12, LineCount = 1, TextHeight = 0 };
        if (_cardName == null) return empty;
        if (string.IsNullOrEmpty(_cardNameText)) return empty;
        if (safeWidth <= 0) return empty;

        var measureFont = _cardName.GetThemeFont("font") ?? _cardName.GetThemeDefaultFont();
        if (measureFont == null)
        {
            _cardName.AddThemeFontSizeOverride("font_size", 12);
            return empty;
        }

        string text = _cardNameText.Replace("\n", " ");
        // Cap height ~5.4% of card width: 18px at a 236px card.
        int baseSize = Mathf.Max(6, Mathf.RoundToInt(18f * _designCardWidth / 236f));
        int floorSize = _isArtifact ? 8 : Mathf.Max(10, Mathf.RoundToInt(_designCardWidth * 0.040f));

        float Width(int sz) => measureFont.GetStringSize(text, HorizontalAlignment.Left, -1, sz).X;
        float LineHeight(int sz) => measureFont.GetHeight(sz);

        int size = baseSize;
        while (size > floorSize && Width(size) > safeWidth) size--;
        while (size > floorSize && LineHeight(size) > maxBandH) size--;

        _cardName.Text = text;
        _cardName.MaxLinesVisible = 1;
        _cardName.AutowrapMode = TextServer.AutowrapMode.Off;
        _cardName.TextOverrunBehavior = Width(size) > safeWidth
            ? TextServer.OverrunBehavior.TrimEllipsis
            : TextServer.OverrunBehavior.NoTrimming;
        _cardName.AddThemeFontSizeOverride("font_size", size);

        return new NameFitResult { FontSize = size, LineCount = 1, TextHeight = LineHeight(size) };
    }

    /// <summary>Split words into two balanced lines by character count.</summary>
    private static string[] BalancedSplit(string[] words)
    {
        string bestA = "", bestB = "";
        int bestDiff = int.MaxValue;
        for (int i = 1; i < words.Length; i++)
        {
            string a = string.Join(" ", words[..i]);
            string b = string.Join(" ", words[i..]);
            int diff = Mathf.Abs(a.Length - b.Length);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestA = a;
                bestB = b;
            }
        }
        return new[] { bestA, bestB };
    }

    /// <summary>
    /// Update visible stat values (during gameplay when vigor changes).
    /// </summary>
    public void SetStatValues(int? attack, int? vigor)
    {
        if (_attackBadge == null || _vigorBadge == null) return;
        if (_hasAttack && attack.HasValue)
            _attackBadge.Text = attack.Value.ToString();
        if (_hasVigor && vigor.HasValue)
            _vigorBadge.Text = vigor.Value.ToString();
    }

    /// <summary>Get the name label node for metadata capture purposes.</summary>
    public Label? GetNameLabel() => _cardName;

    /// <summary>Get the screen-space rect of the name label area.</summary>
    public Rect2 GetNameRect() => _cardName?.GetRect() ?? new Rect2();
}
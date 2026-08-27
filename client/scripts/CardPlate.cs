using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// Unified card frame plate — one template used by HandCard, LaneSlot, and Artifact cards.
/// Provides the gold two-layer border, hex cost top-left, fixed-height name band,
/// and stat rail (attack/vigor) docked inside the bottom edge.
/// Nothing overhangs the card silhouette.
/// 
/// Layout (top to bottom):
///   [hex cost]  top-left corner
///   [art area]  full card face (art fills behind all overlays)
///   [name band] fixed-height band at bottom
///   [stat rail] attack left, vigor right, docked inside bottom edge
/// 
/// The gold two-layer border is drawn by the parent PanelContainer's StyleBoxFlat.
/// This component draws the inner highlight line and all interior overlays.
/// </summary>
public partial class CardPlate : Control
{
    // ── Persistent child nodes ──
    private ColorRect? _nameBandBg;
    private ColorRect? _statRailBg;
    private ColorRect? _innerBorderTop;
    private ColorRect? _innerBorderLeft;
    private ColorRect? _innerBorderRight;
    private Label? _cardName;
    private Label? _attackBadge;
    private Label? _vigorBadge;
    private Label? _costLabel;

    // Cached design dimensions
    private float _designCardWidth;
    private float _designCardHeight;
    private string _cardNameText = "";
    private bool _hasAttack;
    private bool _hasVigor;
    private int _cardCost;
    private bool _isArtifact;
    private bool _hasCost;

    /// <summary>Total height of name band + stat rail</summary>
    public float PlateHeight => _designCardHeight * (FrameNameBandFraction + FrameStatRailFraction);

    // No _Ready — all child nodes created lazily in Setup().

    /// <summary>
    /// Create a stat badge label with styled background.
    /// </summary>
    private static Label MakeStatBadge(Color bgColor, Color borderColor)
    {
        var badge = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.AddThemeColorOverride("font_color", FrameStatText);
        badge.AddThemeConstantOverride("outline_size", 1);
        badge.AddThemeColorOverride("font_outline_color", Colors.Black);

        var style = new StyleBoxFlat
        {
            BgColor = bgColor,
            BorderColor = borderColor,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 2,
            ContentMarginLeft = 2,
            ContentMarginTop = 0,
            ContentMarginRight = 2,
            ContentMarginBottom = 0
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
        _cardCost = cost;
        _isArtifact = isArtifact;
        _hasCost = cost > 0 || !isArtifact; // show cost for non-artifact cards

        // Lazy init
        if (_nameBandBg == null)
        {
            // ── Name band background ──
            _nameBandBg = new ColorRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Color = FrameNameBand
            };
            AddChild(_nameBandBg);

            // ── Stat rail background ──
            _statRailBg = new ColorRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Color = FrameStatRail
            };
            AddChild(_statRailBg);

            // ── Inner border highlight lines (1px inside the gold border) ──
            _innerBorderTop = new ColorRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Color = FrameGoldInner,
                Size = new Vector2(1, 1)
            };
            AddChild(_innerBorderTop);

            _innerBorderLeft = new ColorRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Color = FrameGoldInner,
                Size = new Vector2(1, 1)
            };
            AddChild(_innerBorderLeft);

            _innerBorderRight = new ColorRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Color = FrameGoldInner,
                Size = new Vector2(1, 1)
            };
            AddChild(_innerBorderRight);

            // ── Card name label ──
            _cardName = new Label
            {
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.Word,
                MaxLinesVisible = 2,
                TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming
            };
            _cardName.AddThemeColorOverride("font_color", FrameNameText);
            _cardName.AddThemeConstantOverride("outline_size", 1);
            _cardName.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.6f));
            AddChild(_cardName);

            // ── Attack badge ──
            _attackBadge = MakeStatBadge(FrameStatAttack, Color.FromHtml("#7A2A1A"));
            AddChild(_attackBadge);

            // ── Vigor badge ──
            _vigorBadge = MakeStatBadge(FrameStatVigor, Color.FromHtml("#3A6A2A"));
            AddChild(_vigorBadge);

            // ── Cost hex label ──
            _costLabel = new Label
            {
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _costLabel.AddThemeColorOverride("font_color", FrameHexText);
            _costLabel.AddThemeConstantOverride("outline_size", 1);
            _costLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
            AddChild(_costLabel);
        }

        // ═══ LAYOUT ═══

        float nameBandH = cardHeight * FrameNameBandFraction;
        float statRailH = cardHeight * FrameStatRailFraction;
        float plateH = nameBandH + statRailH;
        float borderW = FrameBorderWidth;
        float innerW = FrameInnerBorderWidth;

        // Position this control at the bottom of the card
        Position = new Vector2(0, cardHeight - plateH);
        Size = new Vector2(cardWidth, plateH);

        // ── Name band (top section of plate) ──
        _nameBandBg.Position = new Vector2(0, 0);
        _nameBandBg.Size = new Vector2(cardWidth, nameBandH);

        // ── Stat rail (bottom section of plate) ──
        _statRailBg.Position = new Vector2(0, nameBandH);
        _statRailBg.Size = new Vector2(cardWidth, statRailH);

        // ── Inner border highlight lines (1px inside the gold border) ──
        // Top: just below the card's gold border top edge
        _innerBorderTop.Position = new Vector2(innerW, -borderW + innerW);
        _innerBorderTop.Size = new Vector2(cardWidth - innerW * 2, innerW);

        // Left: just inside the card's gold border left edge
        _innerBorderLeft.Position = new Vector2(-borderW + innerW, 0);
        _innerBorderLeft.Size = new Vector2(innerW, plateH);

        // Right: just inside the gold border right edge
        _innerBorderRight.Position = new Vector2(cardWidth - borderW, 0);
        _innerBorderRight.Size = new Vector2(innerW, plateH);

        // ── Name label ──
        float chipSize = cardWidth * FrameStatChipFraction;
        float padX = chipSize + 4f;
        _cardName.Position = new Vector2(padX, 1);
        _cardName.Size = new Vector2(cardWidth - padX * 2, nameBandH - 2);
        int startFontSize = Mathf.Max(9, Mathf.RoundToInt(cardHeight * 0.065f));
        ApplyHeaderFont(_cardName, startFontSize);
        _cardName.Text = name;
        FitCardName(startFontSize);

        // ── Stat rail: attack left, vigor right, DOCKED INSIDE (no overhang) ──
        float statChipW = chipSize;
        float statChipH = statRailH * 0.8f; // 80% of rail height, centered
        float statChipY = nameBandH + (statRailH - statChipH) / 2f;

        _attackBadge.Visible = _hasAttack;
        if (_hasAttack)
        {
            _attackBadge.Text = attack!.Value.ToString();
            _attackBadge.Size = new Vector2(statChipW, statChipH);
            int fontSize = Mathf.Max(9, Mathf.RoundToInt(statChipH * 0.55f));
            _attackBadge.AddThemeFontSizeOverride("font_size", fontSize);
            _attackBadge.Position = new Vector2(3, statChipY);
        }

        _vigorBadge.Visible = _hasVigor;
        if (_hasVigor)
        {
            _vigorBadge.Text = vigor!.Value.ToString();
            _vigorBadge.Size = new Vector2(statChipW, statChipH);
            int fontSize = Mathf.Max(9, Mathf.RoundToInt(statChipH * 0.55f));
            _vigorBadge.AddThemeFontSizeOverride("font_size", fontSize);
            _vigorBadge.Position = new Vector2(cardWidth - statChipW - 3, statChipY);
        }

        // ── Cost hex badge (top-left of card, inside frame) ──
        if (_hasCost)
        {
            _costLabel.Visible = true;
            _costLabel.Text = _cardCost.ToString();

            // Hex badge positioned top-left, overlapping the card's art area
            // Positioned at the very top-left of the card, inside the border
            float hexSize = cardWidth * FrameHexSizeFraction;
            float hexX = borderW + 2f;
            float hexY = borderW + 2f;
            _costLabel.Position = new Vector2(hexX, hexY);
            _costLabel.Size = new Vector2(hexSize, hexSize);
            int costFontSize = Mathf.Max(11, Mathf.RoundToInt(hexSize * 0.5f));
            _costLabel.AddThemeFontSizeOverride("font_size", costFontSize);

            // Draw hex border via stylebox
            var hexStyle = new StyleBoxFlat
            {
                BgColor = FrameHexFill,
                BorderColor = FrameHexBorder,
                BorderWidthLeft = 1,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 2,
                CornerRadiusTopRight = 2,
                CornerRadiusBottomLeft = 2,
                CornerRadiusBottomRight = 2
            };
            _costLabel.AddThemeStyleboxOverride("normal", hexStyle);
        }
        else
        {
            _costLabel.Visible = false;
        }
    }

    /// <summary>
    /// Auto-fit card name: step down 1px at a time to floor 9px, then ellipsize.
    /// </summary>
    private void FitCardName(int startSize)
    {
        if (string.IsNullOrEmpty(_cardNameText)) return;

        float availWidth = _cardName.Size.X;
        if (availWidth <= 0) return;

        int fontSize = startSize;
        var font = _cardName.GetThemeDefaultFont();
        if (font == null)
        {
            fontSize = Mathf.Max(9, startSize - 2);
            _cardName.AddThemeFontSizeOverride("font_size", fontSize);
            return;
        }

        while (fontSize >= 9)
        {
            _cardName.AddThemeFontSizeOverride("font_size", fontSize);
            _cardName.Text = _cardNameText;

            float lineWidth = font.GetStringSize(_cardNameText,
                HorizontalAlignment.Left, -1, fontSize).X;

            if (lineWidth <= availWidth + 2f)
                return;

            float halfWidth = availWidth * 0.92f;
            if (lineWidth <= halfWidth * 2.2f)
                return;

            fontSize--;
        }

        _cardName.AddThemeFontSizeOverride("font_size", 9);
        _cardName.Text = _cardNameText;
        _cardName.MaxLinesVisible = 2;
        _cardName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
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
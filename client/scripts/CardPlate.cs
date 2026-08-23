using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// Uniform card-bottom plate component — one template used by HandCard, LaneSlot,
/// and the deck builder grid. Fixed height = 22% of card height, anchored to card bottom.
///
/// Auto-fit card name: start at fontSize = cardHeight * 0.075, wrap to 2 lines on word
/// boundaries, step down 1px to floor 9px, then ellipsize. NEVER clip mid-word, NEVER
/// overflow the plate.
///
/// Stat chips (attack bottom-left, vigor bottom-right) overlap the plate's bottom corners
/// by half their height. Uniform size = 15% of card width. Spells show no chips.
/// Cost chip (top-left) is handled by the parent card, not this component.
/// </summary>
public partial class CardPlate : Control
{
    // ── Persistent child nodes (created lazily in Setup) ──
    private ColorRect? _plateBg;
    private ColorRect? _plateTopBorder;
    private Label? _cardName;
    private Label? _attackBadge;
    private Label? _vigorBadge;

    // Cached design dimensions (set by Setup)
    private float _designCardWidth;
    private float _designCardHeight;
    private string _cardNameText = "";
    private bool _hasAttack;
    private bool _hasVigor;

    /// <summary>Current plate height factoring in any scaling.</summary>
    public float PlateHeight => _designCardHeight * 0.22f;

    // No _Ready — all child nodes created lazily in Setup() so the component
    // works immediately when constructed programmatically (before scene tree entry).

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
        badge.AddThemeColorOverride("font_color", Colors.White);
        badge.AddThemeConstantOverride("outline_size", 2);
        badge.AddThemeColorOverride("font_outline_color", Colors.Black);

        var style = new StyleBoxFlat
        {
            BgColor = bgColor,
            BorderColor = borderColor,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
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
        float cardWidth, float cardHeight)
    {
        _designCardWidth = cardWidth;
        _designCardHeight = cardHeight;
        _cardNameText = name;
        _hasAttack = attack.HasValue;
        _hasVigor = vigor.HasValue;

        // Lazy init: if _Ready hasn't run yet, create nodes now
        if (_plateBg == null)
        {
            _plateBg = new ColorRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Color = new Color(0.039f, 0.031f, 0.024f, 0.85f)
            };
            AddChild(_plateBg);

            _plateTopBorder = new ColorRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Color = Color.FromHtml("#5A5048"),
                Size = new Vector2(1, 1)
            };
            AddChild(_plateTopBorder);

            _cardName = new Label
            {
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.Word,
                MaxLinesVisible = 2,
                TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming
            };
            _cardName.AddThemeColorOverride("font_color", Color.FromHtml("#E8DCC8"));
            _cardName.AddThemeConstantOverride("outline_size", 1);
            _cardName.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.6f));
            AddChild(_cardName);

            _attackBadge = MakeStatBadge(Color.FromHtml("#4A1710"), Color.FromHtml("#A8402E"));
            AddChild(_attackBadge);

            _vigorBadge = MakeStatBadge(Color.FromHtml("#1D3317"), Color.FromHtml("#5D8F46"));
            AddChild(_vigorBadge);
        }

        float plateH = cardHeight * 0.22f;
        float chipSize = cardWidth * 0.15f;
        float chipOverlap = plateH * 0.5f; // half the plate height

        // Position this control at the bottom of the card
        Position = new Vector2(0, cardHeight - plateH);
        Size = new Vector2(cardWidth, plateH);

        // Plate background fills the entire plate area
        _plateBg.Position = Vector2.Zero;
        _plateBg.Size = Size;

        // 1px top border — full width
        _plateTopBorder.Position = Vector2.Zero;
        _plateTopBorder.Size = new Vector2(cardWidth, 1);

        // Name label — centered in plate, inset from both sides by chip width + gap
        float padX = chipSize + 4f;
        _cardName.Position = new Vector2(padX, 2);
        _cardName.Size = new Vector2(cardWidth - padX * 2, plateH - 4);

        // Apply Cinzel header font — start size = cardHeight * 0.075
        int startFontSize = Mathf.Max(9, Mathf.RoundToInt(cardHeight * 0.075f));
        ApplyHeaderFont(_cardName, startFontSize);
        _cardName.Text = name;

        // Auto-fit: try 2 lines, step down to floor 9px
        FitCardName(startFontSize);

        // Attack badge — bottom-left corner, overlaps plate bottom by half height
        _attackBadge.Visible = _hasAttack;
        if (_hasAttack)
        {
            _attackBadge.Text = attack!.Value.ToString();
            _attackBadge.Size = new Vector2(chipSize, chipSize);
            int fontSize = Mathf.Max(11, Mathf.RoundToInt(chipSize * 0.45f));
            _attackBadge.AddThemeFontSizeOverride("font_size", fontSize);
            _attackBadge.Position = new Vector2(2, plateH - chipOverlap);
        }

        // Vigor badge — bottom-right corner
        _vigorBadge.Visible = _hasVigor;
        if (_hasVigor)
        {
            _vigorBadge.Text = vigor!.Value.ToString();
            _vigorBadge.Size = new Vector2(chipSize, chipSize);
            int fontSize = Mathf.Max(11, Mathf.RoundToInt(chipSize * 0.45f));
            _vigorBadge.AddThemeFontSizeOverride("font_size", fontSize);
            _vigorBadge.Position = new Vector2(cardWidth - chipSize - 2, plateH - chipOverlap);
        }
    }

    /// <summary>
    /// Auto-fit card name: start at given font size, allow word wrap to 2 lines,
    /// step down 1px at a time to floor 9px. Below floor, ellipsize.
    /// Never clips mid-word, never overflows the plate.
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
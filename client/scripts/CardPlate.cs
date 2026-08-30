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
///   [name band]    fixed-height band at bottom of art area
///   [stat rail]    attack left, vigor right, docked inside bottom edge
/// 
/// The Root-Bound 9-slice border overlay is handled by RootBoundBorder.
/// </summary>
public partial class CardPlate : Control
{
    // ── Persistent child nodes ──
    private ColorRect? _nameBandBg;
    private ColorRect? _statRailBg;
    private Label? _cardName;
    private Label? _attackBadge;
    private Label? _vigorBadge;

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
        _isArtifact = isArtifact;

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
        }

        // ═══ LAYOUT ═══

        float nameBandH = cardHeight * FrameNameBandFraction;
        float statRailH = cardHeight * FrameStatRailFraction;
        float plateH = nameBandH + statRailH;

        // Position this control at the bottom of the card
        Position = new Vector2(0, cardHeight - plateH);
        Size = new Vector2(cardWidth, plateH);

        // ── Name band (top section of plate) ──
        _nameBandBg.Position = new Vector2(0, 0);
        _nameBandBg.Size = new Vector2(cardWidth, nameBandH);

        // ── Stat rail (bottom section of plate) ──
        _statRailBg.Position = new Vector2(0, nameBandH);
        _statRailBg.Size = new Vector2(cardWidth, statRailH);

        // ── Name label with auto-fit ──
        int bandPx = Mathf.Max(1, Mathf.RoundToInt(cardWidth * 0.07f));
        int bufferPx = Mathf.Max(Mathf.RoundToInt(cardWidth * 0.06f), 10);
        float safeWidth = cardWidth - bandPx * 2 - bufferPx * 2;
        float safeHeight = nameBandH - 2;
        _cardName.Position = new Vector2(bandPx + bufferPx, 0);
        _cardName.Size = new Vector2(safeWidth, safeHeight);
        _cardName.Text = name;
        ApplyHeaderFont(_cardName, 14);
        FitCardNameAuto(safeWidth);

        // ── Stat rail: attack left, vigor right, DOCKED INSIDE (no overhang) ──
        float chipSize = cardWidth * FrameStatChipFraction;
        float statChipW = chipSize;
        float statChipH = statRailH * 0.8f;
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

        // Hex border via stylebox
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
        label.AddThemeStyleboxOverride("normal", hexStyle);

        return label;
    }

    /// <summary>
    /// Auto-fit card name: the name NEVER escapes its safe zone.
    /// Ported from tools/namefit.py to Godot C#.
    /// 
    /// Safe zone = art window inset by buffer = max(6% of card_width, 10px @ 236px width) on each side.
    /// Base size = 24px scaled linearly with card width (at 236px reference width).
    /// Floor = 62% of base. If it won't fit: split into two lines balanced by character count,
    /// restart at base-2, shrink to hard minimum 12 (8 on artifact minis).
    /// </summary>
    private void FitCardNameAuto(float safeWidth)
    {
        if (string.IsNullOrEmpty(_cardNameText)) return;
        if (safeWidth <= 0) return;

        var font = _cardName.GetThemeDefaultFont();
        if (font == null)
        {
            _cardName.AddThemeFontSizeOverride("font_size", 12);
            return;
        }

        int hardMin = _isArtifact ? 8 : 12;
        // Compute base size: 24px at 236px card width, scaled linearly
        int baseSize = Mathf.Max(6, Mathf.RoundToInt(24f * _designCardWidth / 236f));
        int floor = Mathf.Max(hardMin, Mathf.RoundToInt(baseSize * 0.62f));

        float Measure(string text, int sz)
        {
            return font.GetStringSize(text, HorizontalAlignment.Left, -1, sz).X;
        }

        // Try single line, shrink from base to floor
        int sz = baseSize;
        while (sz > floor && Measure(_cardNameText, sz) > safeWidth)
            sz--;
        if (Measure(_cardNameText, sz) <= safeWidth)
        {
            _cardName.AddThemeFontSizeOverride("font_size", sz);
            _cardName.Text = _cardNameText;
            _cardName.MaxLinesVisible = 1;
            _cardName.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
            return;
        }

        // Still won't fit at floor — try two-line balanced split
        string[] words = _cardNameText.Split(' ');
        if (words.Length > 1)
        {
            // Find best balanced split
            string[] bestLines = BalancedSplit(words);
            sz = Mathf.Max(hardMin, baseSize - 2);
            float widest = Mathf.Max(Measure(bestLines[0], sz), Measure(bestLines[1], sz));
            while (sz > hardMin && widest > safeWidth)
            {
                sz--;
                widest = Mathf.Max(Measure(bestLines[0], sz), Measure(bestLines[1], sz));
            }
            _cardName.AddThemeFontSizeOverride("font_size", sz);
            _cardName.Text = string.Join("\n", bestLines);
            _cardName.MaxLinesVisible = 2;
            _cardName.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
            return;
        }

        // Single unbreakable word — shrink to hard minimum
        while (sz > hardMin && Measure(_cardNameText, sz) > safeWidth)
            sz--;
        _cardName.AddThemeFontSizeOverride("font_size", sz);
        _cardName.Text = _cardNameText;
        _cardName.MaxLinesVisible = 1;
        _cardName.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
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
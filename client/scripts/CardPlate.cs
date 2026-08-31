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
                AutowrapMode = TextServer.AutowrapMode.Word,
                MaxLinesVisible = 2,
                TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming
            };
            _cardName.AddThemeColorOverride("font_color", FrameNameText);
            _cardName.AddThemeConstantOverride("outline_size", 1);
            _cardName.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.6f));
            _nameClipContainer.AddChild(_cardName);

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
        
        // Clipping container fills the safe name zone
        _nameClipContainer.Position = new Vector2(bandPx + bufferPx, 0);
        _nameClipContainer.Size = new Vector2(safeWidth, safeHeight);
        
        // Card name label fills its parent (the clipping container)
        _cardName.Position = Vector2.Zero;
        _cardName.Size = new Vector2(safeWidth, safeHeight);
        _cardName.Text = name;
        ApplyHeaderFont(_cardName, 14);
        FitCardNameAuto(safeWidth, safeHeight, nameBandH);

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
    /// Auto-fit card name: the name NEVER escapes its safe zone, and NEVER
    /// overflows into the stat rail (reserve the rail first, fit name into
    /// what remains, shrink or re-split until it fits — HARD RULE).
    /// 
    /// Safe zone = art window inset by buffer = max(6% of card_width, 10px @ 236px width) on each side.
    /// Base size = 24px scaled linearly with card width (at 236px reference width).
    /// Floor = 62% of base. If it won't fit: split into two lines balanced by character count,
    /// restart at base-2, shrink to hard minimum 12 (8 on artifact minis).
    /// 
    /// Height constraint: the stat rail is reserved first (statRailH). The name
    /// occupies the remaining band (nameBandH). Its FULL rendered height for
    /// the chosen number of lines must NOT exceed nameBandH, or the text crosses
    /// the stat rail / badge boundary. Shrink until both width and height fit.
    /// 
    /// The name label sits inside _nameClipContainer (ClipContents=true), which
    /// physically prevents ANY text from rendering outside the name band bounds.
    /// 
    /// FLOOR vs HARDMIN: the SINGLE-LINE floor is 62% of base (no hardMin clamping —
    /// hardMin only constrains the two-line WIDTH shrink). The HEIGHT floor is the
    /// absolute minimum glyph size (8px), not hardMin — height and width constraints
    /// are independent; a name that fits widthwise at 12px but needs 10px to fit
    /// height should render at 10px, not be pushed to ellipsis.
    /// </summary>
    private void FitCardNameAuto(float safeWidth, float safeHeight, float nameBandH)
    {
        if (string.IsNullOrEmpty(_cardNameText)) return;
        if (safeWidth <= 0) return;

        var font = _cardName.GetThemeDefaultFont();
        if (font == null)
        {
            _cardName.AddThemeFontSizeOverride("font_size", 12);
            return;
        }
        // Use the font that will actually render (Cinzel override from ApplyHeaderFont).
        // GetThemeDefaultFont() returns the theme-level default which may differ from the
        // overridden font — measure with the override directly for accurate string sizes.
        var measureFont = _cardName.GetThemeFont("font");
        if (measureFont == null) measureFont = font;

        int hardMin = _isArtifact ? 8 : 12;
        // Compute base size: 24px at 236px card width, scaled linearly
        int baseSize = Mathf.Max(6, Mathf.RoundToInt(24f * _designCardWidth / 236f));
        // Single-line floor = 62% of base, min 8px — NOT clamped to hardMin
        int singleLineFloor = Mathf.Max(8, Mathf.RoundToInt(baseSize * 0.62f));
        // Absolute height minimum: 8px per spec "no glyph below 8px"
        const int heightFloor = 8;

        float Measure(string text, int sz)
        {
            return measureFont.GetStringSize(text, HorizontalAlignment.Left, -1, sz).X;
        }

        float LineHeight(int sz)
        {
            return measureFont.GetHeight(sz);
        }

        // Set the label text and size, return the font size after height check
        int ApplyFit(int sz, string displayText, int maxLines, string[]? linesForHeight)
        {
            int actualLines = linesForHeight != null ? linesForHeight.Length : (displayText.Contains("\n") ? displayText.Split('\n').Length : 1);
            float textH = actualLines * LineHeight(sz);
            
            // Shrink until height fits — use absolute 8px floor, not hardMin
            while (textH > nameBandH && sz > heightFloor)
            {
                sz--;
                textH = actualLines * LineHeight(sz);
            }

            // If still overflows height at floor, use ellipsis on single line
            if (textH > nameBandH)
            {
                _cardName.AddThemeFontSizeOverride("font_size", sz);
                _cardName.Text = displayText;
                _cardName.MaxLinesVisible = 1;
                _cardName.AutowrapMode = TextServer.AutowrapMode.Off;
                _cardName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                return sz;
            }
            
            sz = Mathf.Max(1, sz);
            _cardName.AddThemeFontSizeOverride("font_size", sz);
            _cardName.Text = displayText;
            _cardName.MaxLinesVisible = maxLines;
            _cardName.AutowrapMode = TextServer.AutowrapMode.Word;
            _cardName.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
            
            return sz;
        }

        // ─── Try single line, shrink from base to singleLineFloor ───
        int sz = baseSize;
        while (sz > singleLineFloor && Measure(_cardNameText, sz) > safeWidth)
            sz--;
        if (Measure(_cardNameText, sz) <= safeWidth)
        {
            float textH = 1 * LineHeight(sz);
            if (textH <= nameBandH)
            {
                ApplyFit(sz, _cardNameText, 1, null);
                return;
            }
            // Single line fits width but overflows height — shrink further to heightFloor
            while (sz > heightFloor && 1 * LineHeight(sz) > nameBandH)
                sz--;
            if (1 * LineHeight(sz) <= nameBandH)
            {
                ApplyFit(sz, _cardNameText, 1, null);
                return;
            }
            // Even at absolute floor, height overflows — use ellipsis
            ApplyFit(sz, _cardNameText, 1, null);
            return;
        }

        // ─── Two-line balanced split ───
        string[] words = _cardNameText.Split(' ');
        if (words.Length > 1)
        {
            string[] bestLines = BalancedSplit(words);
            sz = Mathf.Max(heightFloor, baseSize - 2);
            // Width shrink: use hardMin as the width floor
            float widest = Mathf.Max(Measure(bestLines[0], sz), Measure(bestLines[1], sz));
            while (sz > hardMin && widest > safeWidth)
            {
                sz--;
                widest = Mathf.Max(Measure(bestLines[0], sz), Measure(bestLines[1], sz));
            }
            
            // Height check: 2 lines * lineHeight must fit in nameBandH
            // Shrink until height fits — use absolute 8px floor
            float twoLineH = 2 * LineHeight(sz);
            while (twoLineH > nameBandH && sz > heightFloor)
            {
                sz--;
                twoLineH = 2 * LineHeight(sz);
                if (sz >= heightFloor)
                {
                    // Re-check width at reduced size
                    float w1 = Measure(bestLines[0], sz);
                    float w2 = Measure(bestLines[1], sz);
                    if (w1 > safeWidth || w2 > safeWidth)
                    {
                        // Width doesn't fit — try re-split at smaller size
                        string[] reSplit = BalancedSplit(words);
                        widest = Mathf.Max(Measure(reSplit[0], sz), Measure(reSplit[1], sz));
                        if (widest <= safeWidth)
                            bestLines = reSplit;
                    }
                }
            }
            
            // If still overflows height at absolute floor, single-line ellipsis
            if (twoLineH > nameBandH)
            {
                // Two-line doesn't fit at floor — single line with ellipsis
                sz = hardMin;
                while (sz > heightFloor && Measure(_cardNameText, sz) > safeWidth)
                    sz--;
                _cardName.AddThemeFontSizeOverride("font_size", sz);
                _cardName.Text = _cardNameText;
                _cardName.MaxLinesVisible = 1;
                _cardName.AutowrapMode = TextServer.AutowrapMode.Off;
                _cardName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                return;
            }
            
            _cardName.AddThemeFontSizeOverride("font_size", sz);
            _cardName.Text = string.Join("\n", bestLines);
            _cardName.MaxLinesVisible = 2;
            _cardName.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
            return;
        }

        // Single unbreakable word — shrink to hardMin, ellipsis at absolute floor
        while (sz > hardMin && Measure(_cardNameText, sz) > safeWidth)
            sz--;
        if (Measure(_cardNameText, sz) > safeWidth)
        {
            // Even at hardMin width overflows — shrink further to heightFloor
            while (sz > heightFloor && Measure(_cardNameText, sz) > safeWidth)
                sz--;
            _cardName.AddThemeFontSizeOverride("font_size", sz);
            _cardName.Text = _cardNameText;
            _cardName.MaxLinesVisible = 1;
            if (Measure(_cardNameText, sz) > safeWidth)
            {
                _cardName.AutowrapMode = TextServer.AutowrapMode.Off;
                _cardName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            }
            else
                _cardName.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
            return;
        }
        while (sz > heightFloor && 1 * LineHeight(sz) > nameBandH)
            sz--;
        if (1 * LineHeight(sz) > nameBandH)
        {
            // Single line at absolute floor overflows height — use ellipsis
            _cardName.AddThemeFontSizeOverride("font_size", sz);
            _cardName.Text = _cardNameText;
            _cardName.MaxLinesVisible = 1;
            _cardName.AutowrapMode = TextServer.AutowrapMode.Off;
            _cardName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            return;
        }
        ApplyFit(sz, _cardNameText, 1, null);
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
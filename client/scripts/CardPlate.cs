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

        var style = PlateStyle(bgColor == FrameStatAttack ? "medal_attack" : "medal_vigor");
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
            _nameBandBg = new TextureRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Texture = GD.Load<Texture2D>("res://content/art/frame/plaque.png"),
                StretchMode = TextureRect.StretchModeEnum.Scale,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize
            };
            AddChild(_nameBandBg);

            // ── Stat rail background ──
            _statRailBg = new ColorRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Color = new Color(0, 0, 0, 0)
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
        ApplyHeaderFont(_cardName, FontCardName);
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
        _nameClipContainer.Position = new Vector2(bandPx + bufferPx, 0);
        _nameClipContainer.Size = new Vector2(safeWidth, nameBandH - 2f);
        _cardName.Size = new Vector2(safeWidth, nameBandH - 2f);

        // Re-apply the fitted label properties (font size, text, lines) — the auto-fit
        // already applied them against the max band; sizes are unchanged, this just
        // guarantees consistency after the container is re-sized.
        _cardName.AddThemeFontSizeOverride("font_size", fit.FontSize);
        _cardName.MaxLinesVisible = fit.LineCount;

        // ── Stat rail: attack left, vigor right, DOCKED INSIDE (no overhang) — pill-shaped gold-ring medallions ──
        float chipSize = cardWidth * FrameStatChipFraction;
        float statChipW = chipSize;
        float statChipH = railH * 0.8f;
        float pillRadius = statChipH / 2f;
        float statChipY = nameBandH + (railH - statChipH) / 2f;

        _attackBadge.Visible = _hasAttack;
        if (_hasAttack)
        {
            _attackBadge.Text = attack!.Value.ToString();
            _attackBadge.Size = new Vector2(statChipW, statChipH);
            int fontSize = Mathf.Clamp(Mathf.RoundToInt(statChipH * 0.75f), 24, FontStat);
            _attackBadge.AddThemeFontSizeOverride("font_size", fontSize);
            // BOARD-MATCH-2: sit flush at frame's bottom corners — inside Root-Bound border
            float chipBandInset = bandPx + 2f;
            _attackBadge.Position = new Vector2(chipBandInset, statChipY);
            // Recreate stylebox with correct pill radius for current size
            var attStyle = PlateStyle("medal_attack");
            _attackBadge.AddThemeStyleboxOverride("normal", attStyle);
        }

        _vigorBadge.Visible = _hasVigor;
        if (_hasVigor)
        {
            _vigorBadge.Text = vigor!.Value.ToString();
            _vigorBadge.Size = new Vector2(statChipW, statChipH);
            int fontSize = Mathf.Clamp(Mathf.RoundToInt(statChipH * 0.75f), 24, FontStat);
            _vigorBadge.AddThemeFontSizeOverride("font_size", fontSize);
            // BOARD-MATCH-2: sit flush at frame's bottom corners — inside Root-Bound border
            float vigorX = cardWidth - statChipW - bandPx - 2f;
            _vigorBadge.Position = new Vector2(vigorX, statChipY);
            var vigStyle = PlateStyle("medal_vigor");
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
        var hexStyle = PlateStyle("coin");
        label.AddThemeStyleboxOverride("normal", hexStyle);

        return label;
    }

    /// <summary>
    /// Update the cost rune style to be a proper circle at the current size.
    /// Call after changing label.Size to ensure corner radius matches.
    /// </summary>
    public static void UpdateCostRuneStyle(Label label, float hexSize)
    {
        var hexStyle = PlateStyle("coin");
        label.AddThemeStyleboxOverride("normal", hexStyle);
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
    /// Height constraint: the stat rail is reserved first (railH). The name
    /// occupies the remaining band (maxBandH). Its FULL rendered height for
    /// the chosen number of lines must NOT exceed maxBandH, or the text crosses
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
    private NameFitResult FitCardNameAuto(float safeWidth, float maxBandH)
    {
        var empty = new NameFitResult { FontSize = 12, LineCount = 1, TextHeight = 0 };
        if (string.IsNullOrEmpty(_cardNameText)) return empty;
        if (safeWidth <= 0) return empty;

        var font = _cardName.GetThemeDefaultFont();
        if (font == null)
        {
            _cardName.AddThemeFontSizeOverride("font_size", 12);
            return empty;
        }
        // Use the font that will actually render (Cinzel override from ApplyHeaderFont).
        var measureFont = _cardName.GetThemeFont("font");
        if (measureFont == null) measureFont = font;

        int hardMin = _isArtifact ? 8 : 14;
        // Compute base size: 24px at 236px card width, scaled linearly
        int baseSize = Mathf.Max(6, Mathf.RoundToInt(42f * _designCardWidth / 236f));
        // Single-line floor = 62% of base, min 8px — NOT clamped to hardMin
        int singleLineFloor = Mathf.Max(8, Mathf.RoundToInt(baseSize * 0.62f));
        // Absolute height minimum: 8px per spec "no glyph below 8px"
        const int heightFloor = 12;

        float Measure(string text, int sz)
        {
            return measureFont.GetStringSize(text, HorizontalAlignment.Left, -1, sz).X;
        }

        float LineHeight(int sz)
        {
            return measureFont.GetHeight(sz);
        }

        NameFitResult Result(int sz, int lines)
        {
            return new NameFitResult { FontSize = sz, LineCount = lines, TextHeight = lines * LineHeight(sz) };
        }

        // Apply the fitted state to the label.
        void Apply(int sz, string displayText, int maxLines, TextServer.OverrunBehavior overrun)
        {
            sz = Mathf.Max(1, sz);
            _cardName.AddThemeFontSizeOverride("font_size", sz);
            _cardName.Text = displayText;
            _cardName.MaxLinesVisible = maxLines;
            // Two-line mode: we provide the balanced split ourselves via \n,
            // so AutowrapMode must be Off — Word wrapping would re-wrap the
            // second line and create an invisible third line on overflow.
            // Single-line: Off is fine (no wrapping needed for one line).
            _cardName.AutowrapMode = TextServer.AutowrapMode.Off;
            _cardName.TextOverrunBehavior = overrun;
        }

        // ─── Try single line, shrink from base to singleLineFloor ───
        int sz = baseSize;
        while (sz > singleLineFloor && Measure(_cardNameText, sz) > safeWidth)
            sz--;
        if (Measure(_cardNameText, sz) <= safeWidth)
        {
            float textH = 1 * LineHeight(sz);
            if (textH <= maxBandH)
            {
                Apply(sz, _cardNameText, 1, TextServer.OverrunBehavior.NoTrimming);
                return Result(sz, 1);
            }
            // Single line fits width but overflows height — shrink further to heightFloor
            while (sz > heightFloor && 1 * LineHeight(sz) > maxBandH)
                sz--;
            if (1 * LineHeight(sz) <= maxBandH)
            {
                Apply(sz, _cardNameText, 1, TextServer.OverrunBehavior.NoTrimming);
                return Result(sz, 1);
            }
            // Even at absolute floor, height overflows — use ellipsis
            Apply(sz, _cardNameText, 1, TextServer.OverrunBehavior.TrimEllipsis);
            return Result(sz, 1);
        }

        // ─── Two-line balanced split ───
        string[] words = _cardNameText.Split(' ');
        if (words.Length > 1)
        {
            string[] bestLines = BalancedSplit(words);
            sz = Mathf.Max(heightFloor, baseSize - 2);
            // Width shrink: continue to heightFloor (autowrap is Off after fix,
            // so any remaining overflow is clipped; shrinking further reduces it).
            float widest = Mathf.Max(Measure(bestLines[0], sz), Measure(bestLines[1], sz));
            while (sz > heightFloor && widest > safeWidth)
            {
                sz--;
                widest = Mathf.Max(Measure(bestLines[0], sz), Measure(bestLines[1], sz));
            }
            
            // Height check: 2 lines * lineHeight must fit in maxBandH
            // Shrink until height fits — use absolute 8px floor
            float twoLineH = 2 * LineHeight(sz);
            while (twoLineH > maxBandH && sz > heightFloor)
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
            if (twoLineH > maxBandH)
            {
                // Two-line doesn't fit at floor — single line with ellipsis
                sz = hardMin;
                while (sz > heightFloor && Measure(_cardNameText, sz) > safeWidth)
                    sz--;
                Apply(sz, _cardNameText, 1, TextServer.OverrunBehavior.TrimEllipsis);
                return Result(sz, 1);
            }
            
            Apply(sz, string.Join("\n", bestLines), 2, TextServer.OverrunBehavior.NoTrimming);
            return Result(sz, 2);
        }

        // Single unbreakable word — shrink to hardMin, ellipsis at absolute floor
        while (sz > hardMin && Measure(_cardNameText, sz) > safeWidth)
            sz--;
        if (Measure(_cardNameText, sz) > safeWidth)
        {
            // Even at hardMin width overflows — shrink further to heightFloor
            while (sz > heightFloor && Measure(_cardNameText, sz) > safeWidth)
                sz--;
            Apply(sz, _cardNameText, 1, Measure(_cardNameText, sz) > safeWidth
                ? TextServer.OverrunBehavior.TrimEllipsis
                : TextServer.OverrunBehavior.NoTrimming);
            return Result(sz, 1);
        }
        while (sz > heightFloor && 1 * LineHeight(sz) > maxBandH)
            sz--;
        if (1 * LineHeight(sz) > maxBandH)
        {
            // Single line at absolute floor overflows height — use ellipsis
            Apply(sz, _cardNameText, 1, TextServer.OverrunBehavior.TrimEllipsis);
            return Result(sz, 1);
        }
        Apply(sz, _cardNameText, 1, TextServer.OverrunBehavior.NoTrimming);
        return Result(sz, 1);
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
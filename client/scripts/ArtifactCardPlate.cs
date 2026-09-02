using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// Unified artifact card frame — one frame for all artifact cards (player shrine,
/// enemy HUD, board-visible). Uses the Root-Bound 9-slice border + ARTIFACT tag +
/// charge-pip rail as the artifact identity.
///
/// Layout (top to bottom inside the Root-Bound border):
///   [ARTIFACT tag]  small label top-center, inside the rim
///   [art area]      full card face (art fills behind all overlays)
///   [name band]     dynamic-height band at bottom of art area (grows for two-line names)
///   [charge rail]   charge pips (filled/empty) docked inside bottom edge
///
/// Suppressed state: desaturated/ashen overlay over the entire face.
/// </summary>
public partial class ArtifactCardPlate : Control
{
    // ── Persistent child nodes ──
    private ColorRect? _nameBandBg;
    private ColorRect? _chargeRailBg;
    private ColorRect? _suppressedOverlay;
    private ColorRect? _artBg;           // BOARD-MATCH-2: art background (parchment when no texture)
    private TextureRect? _artRect;       // BOARD-MATCH-2: artifact art thumbnail
    private Label? _artifactTag;
    private Label? _cardName;
    private Label? _chargeDisplay;
    /// <summary>Container for name label that clips to name band height.</summary>
    private Control? _nameClipContainer;
    private bool _suppressed;
    private bool _showCharges;

    // Cached design dimensions
    private float _designCardWidth;
    private float _designCardHeight;
    private string _cardNameText = "";
    private bool _isArtifact = true;

    // TASK-ARTF-P2: Trigger flash overlay — brief bright gold-white pulse when artifact trigger fires
    private ColorRect? _triggerFlashOverlay;

    // Use same proportions as CardPlate for consistency
    private const float NameBandFraction = 0.20f;
    private const float ChargeRailFraction = 0.12f;
    private const float TagHeightFraction = 0.08f;

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
    /// Configure the artifact plate. Call whenever card size or content changes.
    /// Safe to call from _Ready of parent — internal nodes are created lazily if needed.
    /// </summary>
    public void Setup(string name, float cardWidth, float cardHeight,
        int charges = 0, int maxCharges = 0, bool suppressed = false)
    {
        _designCardWidth = cardWidth;
        _designCardHeight = cardHeight;
        _cardNameText = name;
        _suppressed = suppressed;
        _showCharges = maxCharges > 0;

        // Lazy init
        if (_artifactTag == null)
        {
            // ── Art artwork background (parchment fill when no art texture) ──
            _artBg = new ColorRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Color = new Color(0.15f, 0.12f, 0.10f, 1.0f) // dark parchment
            };
            _artBg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(_artBg);

            // ── Art artwork thumbnail ──
            _artRect = new TextureRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize
            };
            _artRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(_artRect);

            // ── ARTIFACT tag (top of card, inside root-bound rim) ──
            _artifactTag = new Label
            {
                Text = "ARTIFACT",
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _artifactTag.AddThemeColorOverride("font_color", ArtifactTagColor);
            _artifactTag.AddThemeConstantOverride("outline_size", 1);
            _artifactTag.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.6f));
            AddChild(_artifactTag);

            // ── Name band background ──
            _nameBandBg = new ColorRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Color = FrameNameBand
            };
            AddChild(_nameBandBg);

            // ── Charge rail background ──
            _chargeRailBg = new ColorRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Color = FrameStatRail
            };
            AddChild(_chargeRailBg);

            // ── Name clipping container — prevents text from overflowing into charge rail ──
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

            // ── Charge display label ──
            _chargeDisplay = new Label
            {
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _chargeDisplay.AddThemeColorOverride("font_color", ChargeFilled);
            _chargeDisplay.AddThemeConstantOverride("outline_size", 1);
            _chargeDisplay.AddThemeColorOverride("font_outline_color", Colors.Black);
            AddChild(_chargeDisplay);

            // ── Suppressed overlay — ashen desaturated tint ──
            _suppressedOverlay = new ColorRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Color = ArtifactSuppressedOverlay,
                Visible = false
            };
            _suppressedOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(_suppressedOverlay);

            // ── Trigger flash overlay — brief bright pulse when trigger fires ──
            _triggerFlashOverlay = new ColorRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Color = new Color(1.0f, 0.95f, 0.7f, 0.0f),
                Visible = true
            };
            _triggerFlashOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(_triggerFlashOverlay);
        }

        // ═══ LAYOUT ═══
        // Reserve the charge rail FIRST; the name band grows into the remainder when
        // a two-line name needs more height (never touches the reserved rail).

        float tagH = cardHeight * TagHeightFraction;
        float railH = cardHeight * ChargeRailFraction;
        float baseBandH = cardHeight * NameBandFraction;
        // Rail + tag are reserved; band may grow between them (leave 1px margins).
        float maxBandH = Mathf.Max(baseBandH, cardHeight - railH - tagH - 2f);
        int bandPx = Mathf.Max(1, Mathf.RoundToInt(cardWidth * 0.07f));
        int bufferPx = Mathf.Max(Mathf.RoundToInt(cardWidth * 0.06f), 10);
        float safeWidth = cardWidth - bandPx * 2 - bufferPx * 2;

        // Fit the name against the MAXIMUM available band height (rail already reserved).
        _nameClipContainer.Position = new Vector2(bandPx + bufferPx, 0);
        _nameClipContainer.Size = new Vector2(safeWidth, maxBandH - 2f);

        _cardName.Position = Vector2.Zero;
        _cardName.Size = new Vector2(safeWidth, maxBandH - 2f);
        _cardName.Text = name;
        ApplyHeaderFont(_cardName, 12);
        var fit = FitCardNameAuto(safeWidth, maxBandH);

        float nameBandH = Mathf.Clamp(fit.TextHeight + 4f, baseBandH, maxBandH);

        float plateH = tagH + nameBandH + railH;
        Position = new Vector2(0, cardHeight - plateH);
        Size = new Vector2(cardWidth, plateH);

        // ── ARTIFACT tag ──
        _artifactTag.Position = new Vector2(bandPx, -tagH);
        _artifactTag.Size = new Vector2(cardWidth - bandPx * 2, tagH);
        int tagFontSize = Mathf.Max(7, Mathf.RoundToInt(tagH * 0.60f));
        _artifactTag.AddThemeFontSizeOverride("font_size", tagFontSize);

        // ── Name band (middle section of plate) ──
        _nameBandBg.Position = new Vector2(0, 0);
        _nameBandBg.Size = new Vector2(cardWidth, nameBandH);

        // ── Name label clip container — exact band height ──
        _nameClipContainer.Position = new Vector2(bandPx + bufferPx, 0);
        _nameClipContainer.Size = new Vector2(safeWidth, nameBandH - 2f);
        _cardName.Size = new Vector2(safeWidth, nameBandH - 2f);
        _cardName.AddThemeFontSizeOverride("font_size", fit.FontSize);
        _cardName.MaxLinesVisible = fit.LineCount;

        // ── Charge rail (bottom section of plate) ──
        _chargeRailBg.Position = new Vector2(0, nameBandH);
        _chargeRailBg.Size = new Vector2(cardWidth, railH);

        // ── Charge pips ──
        float pipW = railH * 0.6f;
        float pipH = railH * 0.6f;
        float pipY = nameBandH + (railH - pipH) / 2f;
        _chargeDisplay.Position = new Vector2(bandPx + 4, pipY);
        _chargeDisplay.Size = new Vector2(cardWidth - bandPx * 2 - 8, pipH);
        int chargeFontSize = Mathf.Max(8, Mathf.RoundToInt(pipH * 0.7f));
        _chargeDisplay.AddThemeFontSizeOverride("font_size", chargeFontSize);
        _chargeDisplay.Visible = _showCharges;

        if (_showCharges)
        {
            int filled = System.Math.Min(charges, maxCharges);
            int empty = maxCharges - filled;
            _chargeDisplay.Text = new string('•', filled) + new string('∘', empty);
            _chargeDisplay.Modulate = suppressed ? ChargeEmpty : ChargeFilled;
        }

        // ── Suppressed overlay ──
        _suppressedOverlay.Visible = suppressed;
    }

    /// <summary>
    /// Auto-fit card name: the name NEVER escapes its safe zone.
    /// Hard minimum 8px for artifact minis.
    /// Name sits inside _nameClipContainer (ClipContents=true), preventing overflow
    /// into the charge rail.
    /// 
    /// FLOOR vs HARDMIN: the SINGLE-LINE floor is 62% of base (no hardMin clamping —
    /// hardMin only constrains the two-line WIDTH shrink). The HEIGHT floor is the
    /// absolute minimum glyph size (8px) — height and width constraints are independent.
    /// </summary>
    private NameFitResult FitCardNameAuto(float safeWidth, float maxBandH)
    {
        var empty = new NameFitResult { FontSize = 8, LineCount = 1, TextHeight = 0 };
        if (string.IsNullOrEmpty(_cardNameText)) return empty;
        if (safeWidth <= 0) return empty;

        var font = _cardName.GetThemeDefaultFont();
        if (font == null)
        {
            _cardName.AddThemeFontSizeOverride("font_size", 8);
            return empty;
        }
        // Use the font that will actually render (Cinzel override from ApplyHeaderFont).
        var measureFont = _cardName.GetThemeFont("font");
        if (measureFont == null) measureFont = font;

        const int hardMin = 8;
        int baseSize = Mathf.Max(6, Mathf.RoundToInt(24f * _designCardWidth / 236f));
        // Single-line floor = 62% of base, min 8px — NOT clamped to hardMin
        int singleLineFloor = Mathf.Max(8, Mathf.RoundToInt(baseSize * 0.62f));
        // Absolute height minimum: 8px
        const int heightFloor = 8;

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

        void Apply(int sz, string displayText, int maxLines, TextServer.OverrunBehavior overrun)
        {
            sz = Mathf.Max(1, sz);
            _cardName.AddThemeFontSizeOverride("font_size", sz);
            _cardName.Text = displayText;
            _cardName.MaxLinesVisible = maxLines;
            _cardName.TextOverrunBehavior = overrun;
        }

        // Try single line, shrink from base to singleLineFloor
        int sz = baseSize;
        while (sz > singleLineFloor && Measure(_cardNameText, sz) > safeWidth)
            sz--;
        if (Measure(_cardNameText, sz) <= safeWidth)
        {
            // Height check — shrink if single line overflows, never below heightFloor
            while (sz > heightFloor && 1 * LineHeight(sz) > maxBandH)
                sz--;
            if (1 * LineHeight(sz) <= maxBandH)
            {
                Apply(sz, _cardNameText, 1, TextServer.OverrunBehavior.NoTrimming);
                return Result(sz, 1);
            }
            // Single line at heightFloor still overflows — use ellipsis
            Apply(sz, _cardNameText, 1, TextServer.OverrunBehavior.TrimEllipsis);
            return Result(sz, 1);
        }

        // Still won't fit at floor — try two-line balanced split
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
            
            // Height check: shrink until two-line fits name band
            float twoLineH = 2 * LineHeight(sz);
            while (twoLineH > maxBandH && sz > heightFloor)
            {
                sz--;
                twoLineH = 2 * LineHeight(sz);
                if (widest > safeWidth && sz >= heightFloor)
                {
                    widest = Mathf.Max(Measure(bestLines[0], sz), Measure(bestLines[1], sz));
                    if (widest > safeWidth)
                    {
                        // Re-split at smaller size
                        string[] reSplit = BalancedSplit(words);
                        widest = Mathf.Max(Measure(reSplit[0], sz), Measure(reSplit[1], sz));
                        if (widest <= safeWidth)
                            bestLines = reSplit;
                    }
                }
            }
            
            // If still overflows, fall back to single-line with ellipsis
            if (twoLineH > maxBandH)
            {
                sz = hardMin;
                while (sz > heightFloor && Measure(_cardNameText, sz) > safeWidth)
                    sz--;
                Apply(sz, _cardNameText, 1, TextServer.OverrunBehavior.TrimEllipsis);
                return Result(sz, 1);
            }
            
            Apply(sz, string.Join("\n", bestLines), 2, TextServer.OverrunBehavior.NoTrimming);
            return Result(sz, 2);
        }

        // Single unbreakable word — shrink to hardMin, ellipsis at heightFloor
        while (sz > hardMin && Measure(_cardNameText, sz) > safeWidth)
            sz--;
        if (Measure(_cardNameText, sz) > safeWidth)
        {
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
    /// Update charge pips and suppressed state without full re-layout.
    /// Called from RenderHud during gameplay.
    /// </summary>
    public void SetState(int charges, int maxCharges, bool suppressed)
    {
        _suppressed = suppressed;

        if (_chargeDisplay != null && _showCharges)
        {
            int filled = System.Math.Min(charges, maxCharges);
            int empty = maxCharges - filled;
            _chargeDisplay.Text = new string('•', filled) + new string('∘', empty);
            _chargeDisplay.Modulate = suppressed ? ChargeEmpty : ChargeFilled;
        }

        if (_suppressedOverlay != null)
            _suppressedOverlay.Visible = suppressed;

        // Update parent border to suppressed state when needed
        var parent = GetParent();
        if (parent != null)
        {
            var panel = parent as PanelContainer ?? parent?.GetParent() as PanelContainer;
            if (panel != null)
            {
                var style = panel.GetThemeStylebox("panel") as StyleBoxFlat;
                if (style != null)
                {
                    if (suppressed)
                    {
                        style.BorderColor = ArtifactSuppressedBorder;
                        style.BgColor = new Color(0.06f, 0.05f, 0.06f, 0.70f);
                    }
                    else
                    {
                        style.BorderColor = ArtifactFrameOuter;
                        style.BgColor = ArtifactFrameFill;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Set suppressed only, without changing charges.
    /// </summary>
    public void SetSuppressed(bool suppressed)
    {
        if (_chargeDisplay != null)
            _chargeDisplay.Modulate = suppressed ? ChargeEmpty : ChargeFilled;
        if (_suppressedOverlay != null)
            _suppressedOverlay.Visible = suppressed;

        var panel = GetParent() as PanelContainer ?? GetParent()?.GetParent() as PanelContainer;
        if (panel != null)
        {
            var style = panel.GetThemeStylebox("panel") as StyleBoxFlat;
            if (style != null)
            {
                style.BorderColor = suppressed ? ArtifactSuppressedBorder : ArtifactFrameOuter;
                style.BgColor = suppressed ? new Color(0.06f, 0.05f, 0.06f, 0.70f) : ArtifactFrameFill;
            }
        }
    }

    /// <summary>Get the name label node for metadata capture purposes.</summary>
    public Label? GetNameLabel() => _cardName;

    /// <summary>
    /// BOARD-MATCH-5: Load artifact art thumbnail with .webp + .png fallback.
    /// Falls back gracefully if no art file found.
    /// Art paths: res://content/art/artifacts/{artId}.webp or .png
    /// </summary>
    public void SetArt(string artId)
    {
        if (_artRect == null) return;

        // Try .webp first, then .png
        Texture2D? LoadTexture(string ext)
        {
            string path = $"res://content/art/artifacts/{artId}.{ext}";
            if (ResourceLoader.Exists(path))
                return ResourceLoader.Load<Texture2D>(path);
            return null;
        }

        var tex = LoadTexture("webp") ?? LoadTexture("png");
        if (tex != null)
        {
            _artRect.Texture = tex;
            _artRect.Visible = true;
            if (_artBg != null)
                _artBg.Visible = false;
            return;
        }

        // No art file — show dark parchment background
        _artRect.Texture = null;
        _artRect.Visible = false;
        if (_artBg != null)
            _artBg.Visible = true;
    }

    /// <summary>
    /// TASK-ARTF-P2: Brief bright flash when this artifact's trigger fires.
    /// <0.5s golden-white pulse that fades out.
    /// </summary>
    public void PlayTriggerFlash()
    {
        if (_triggerFlashOverlay == null || !IsInstanceValid(_triggerFlashOverlay))
            return;

        // Kill any existing flash tween
        var existing = _triggerFlashOverlay.GetMeta("flash_tween", Variant.From<Godot.Tween?>(null));
        if (existing.VariantType != Variant.Type.Nil)
        {
            var tween = existing.AsGodotObject() as Godot.Tween;
            if (tween != null && IsInstanceValid(tween))
                tween.Kill();
        }

        _triggerFlashOverlay.Modulate = new Color(1.0f, 0.95f, 0.75f, 0.55f);
        var flash = CreateTween();
        flash.TweenProperty(_triggerFlashOverlay, "modulate",
            new Color(1.0f, 0.95f, 0.75f, 0.0f), 0.35f);
        flash.SetMeta("flash_tween", Variant.From(flash));
    }
}
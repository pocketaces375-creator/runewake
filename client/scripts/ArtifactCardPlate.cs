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
///   [name band]     fixed-height band at bottom of art area
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

        float tagH = cardHeight * TagHeightFraction;
        float nameBandH = cardHeight * NameBandFraction;
        float chargeRailH = cardHeight * ChargeRailFraction;
        float plateH = tagH + nameBandH + chargeRailH;
        int bandPx = Mathf.Max(1, Mathf.RoundToInt(cardWidth * 0.07f));

        // Position this control at the bottom of the card
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

        // ── Name label with auto-fit + clipping container ──
        int bufferPx = Mathf.Max(Mathf.RoundToInt(cardWidth * 0.06f), 10);
        float safeWidth = cardWidth - bandPx * 2 - bufferPx * 2;
        
        // Clipping container fills the safe name zone
        _nameClipContainer.Position = new Vector2(bandPx + bufferPx, 0);
        _nameClipContainer.Size = new Vector2(safeWidth, nameBandH - 2);
        
        // Card name label fills its parent
        _cardName.Position = Vector2.Zero;
        _cardName.Size = new Vector2(safeWidth, nameBandH - 2);
        _cardName.Text = name;
        ApplyHeaderFont(_cardName, 12);
        FitCardNameAuto(safeWidth, nameBandH);

        // ── Charge rail (bottom section of plate) ──
        _chargeRailBg.Position = new Vector2(0, nameBandH);
        _chargeRailBg.Size = new Vector2(cardWidth, chargeRailH);

        // ── Charge pips ──
        float pipW = chargeRailH * 0.6f;
        float pipH = chargeRailH * 0.6f;
        float pipY = nameBandH + (chargeRailH - pipH) / 2f;
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
    /// </summary>
    private void FitCardNameAuto(float safeWidth, float nameBandH)
    {
        if (string.IsNullOrEmpty(_cardNameText)) return;
        if (safeWidth <= 0) return;

        var font = _cardName.GetThemeDefaultFont();
        if (font == null)
        {
            _cardName.AddThemeFontSizeOverride("font_size", 8);
            return;
        }

        const int hardMin = 8;
        int baseSize = Mathf.Max(6, Mathf.RoundToInt(24f * _designCardWidth / 236f));
        int floor = Mathf.Max(hardMin, Mathf.RoundToInt(baseSize * 0.62f));

        float Measure(string text, int sz)
        {
            return font.GetStringSize(text, HorizontalAlignment.Left, -1, sz).X;
        }

        float LineHeight(int sz)
        {
            return font.GetHeight(sz);
        }

        // Try single line, shrink from base to floor
        int sz = baseSize;
        while (sz > floor && Measure(_cardNameText, sz) > safeWidth)
            sz--;
        if (Measure(_cardNameText, sz) <= safeWidth)
        {
            // Height check — shrink if single line overflows, never below hardMin
            while (sz > hardMin && 1 * LineHeight(sz) > nameBandH)
                sz--;
            if (1 * LineHeight(sz) <= nameBandH)
            {
                _cardName.AddThemeFontSizeOverride("font_size", sz);
                _cardName.Text = _cardNameText;
                _cardName.MaxLinesVisible = 1;
                _cardName.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
                return;
            }
            // Single line at hardMin still overflows — use ellipsis
            _cardName.AddThemeFontSizeOverride("font_size", sz);
            _cardName.Text = _cardNameText;
            _cardName.MaxLinesVisible = 1;
            _cardName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            return;
        }

        // Still won't fit at floor — try two-line balanced split
        string[] words = _cardNameText.Split(' ');
        if (words.Length > 1)
        {
            string[] bestLines = BalancedSplit(words);
            sz = Mathf.Max(hardMin, baseSize - 2);
            float widest = Mathf.Max(Measure(bestLines[0], sz), Measure(bestLines[1], sz));
            while (sz > hardMin && widest > safeWidth)
            {
                sz--;
                widest = Mathf.Max(Measure(bestLines[0], sz), Measure(bestLines[1], sz));
            }
            
            // Height check: shrink until two-line fits name band, never below hardMin
            float twoLineH = 2 * LineHeight(sz);
            while (twoLineH > nameBandH && sz > hardMin)
            {
                sz--;
                twoLineH = 2 * LineHeight(sz);
                if (widest > safeWidth && sz > 0)
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
            if (twoLineH > nameBandH)
            {
                sz = hardMin;
                while (sz > hardMin && Measure(_cardNameText, sz) > safeWidth)
                    sz--;
                _cardName.AddThemeFontSizeOverride("font_size", sz);
                _cardName.Text = _cardNameText;
                _cardName.MaxLinesVisible = 1;
                _cardName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                return;
            }
            
            _cardName.AddThemeFontSizeOverride("font_size", sz);
            _cardName.Text = string.Join("\n", bestLines);
            _cardName.MaxLinesVisible = 2;
            _cardName.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
            return;
        }

        // Single unbreakable word — shrink to hard minimum, ellipsis at floor
        while (sz > hardMin && Measure(_cardNameText, sz) > safeWidth)
            sz--;
        if (Measure(_cardNameText, sz) > safeWidth)
        {
            // Even at hardMin width overflows — use ellipsis
            _cardName.AddThemeFontSizeOverride("font_size", sz);
            _cardName.Text = _cardNameText;
            _cardName.MaxLinesVisible = 1;
            _cardName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            return;
        }
        while (sz > hardMin && 1 * LineHeight(sz) > nameBandH)
            sz--;
        if (1 * LineHeight(sz) > nameBandH)
        {
            // Single line at hardMin overflows height — use ellipsis
            _cardName.AddThemeFontSizeOverride("font_size", sz);
            _cardName.Text = _cardNameText;
            _cardName.MaxLinesVisible = 1;
            _cardName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            return;
        }
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
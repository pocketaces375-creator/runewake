using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// Unified artifact card frame — one frame for all artifact cards (player shrine,
/// enemy HUD, board-visible). Uses the teal-gold rim + ARTIFACT tag + charge-pip rail
/// as the artifact identity (TASK-UI4-ARSENAL).
///
/// Layout (top to bottom inside the teal-gold border):
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
    private ColorRect? _innerBorderTop;
    private ColorRect? _innerBorderLeft;
    private ColorRect? _innerBorderRight;
    private Label? _artifactTag;
    private Label? _cardName;
    private Label? _chargeDisplay;
    private bool _suppressed;
    private bool _showCharges;

    // Cached design dimensions
    private float _designCardWidth;
    private float _designCardHeight;
    private string _cardNameText = "";

    // TASK-ARTF-P2: Trigger flash overlay — bright gold-white pulse when artifact trigger fires
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
            // ── ARTIFACT tag (top of card, inside teal-gold rim) ──
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

            // ── Inner border highlight lines (1px inside the teal-gold border) ──
            _innerBorderTop = new ColorRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Color = ArtifactFrameInner,
                Size = new Vector2(1, 1)
            };
            AddChild(_innerBorderTop);

            _innerBorderLeft = new ColorRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Color = ArtifactFrameInner,
                Size = new Vector2(1, 1)
            };
            AddChild(_innerBorderLeft);

            _innerBorderRight = new ColorRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Color = ArtifactFrameInner,
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
                MaxLinesVisible = 1,
                TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming
            };
            _cardName.AddThemeColorOverride("font_color", FrameNameText);
            _cardName.AddThemeConstantOverride("outline_size", 1);
            _cardName.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.6f));
            AddChild(_cardName);

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
        float borderW = FrameBorderWidth;
        float innerW = FrameInnerBorderWidth;

        // Position this control at the bottom of the card
        Position = new Vector2(0, cardHeight - plateH);
        Size = new Vector2(cardWidth, plateH);

        // ── ARTIFACT tag ──
        _artifactTag.Position = new Vector2(0, -tagH);
        _artifactTag.Size = new Vector2(cardWidth, tagH);
        int tagFontSize = Mathf.Max(7, Mathf.RoundToInt(tagH * 0.60f));
        _artifactTag.AddThemeFontSizeOverride("font_size", tagFontSize);

        // ── Name band (middle section of plate) ──
        _nameBandBg.Position = new Vector2(0, 0);
        _nameBandBg.Size = new Vector2(cardWidth, nameBandH);

        // ── Name label ──
        float padX = 4f;
        _cardName.Position = new Vector2(padX, 0);
        _cardName.Size = new Vector2(cardWidth - padX * 2, nameBandH);
        int startFontSize = Mathf.Max(9, Mathf.RoundToInt(cardHeight * 0.065f));
        ApplyHeaderFont(_cardName, startFontSize);
        _cardName.Text = name;
        FitCardName(startFontSize);

        // ── Charge rail (bottom section of plate) ──
        _chargeRailBg.Position = new Vector2(0, nameBandH);
        _chargeRailBg.Size = new Vector2(cardWidth, chargeRailH);

        // ── Charge pips ──
        float pipW = chargeRailH * 0.6f;
        float pipH = chargeRailH * 0.6f;
        float pipY = nameBandH + (chargeRailH - pipH) / 2f;
        _chargeDisplay.Position = new Vector2(4, pipY);
        _chargeDisplay.Size = new Vector2(cardWidth - 8, pipH);
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

        // ── Inner border highlight lines ──
        _innerBorderTop.Position = new Vector2(innerW, -borderW + innerW);
        _innerBorderTop.Size = new Vector2(cardWidth - innerW * 2, innerW);

        _innerBorderLeft.Position = new Vector2(-borderW + innerW, 0);
        _innerBorderLeft.Size = new Vector2(innerW, plateH + tagH);

        _innerBorderRight.Position = new Vector2(cardWidth - borderW, 0);
        _innerBorderRight.Size = new Vector2(innerW, plateH + tagH);

        // ── Suppressed overlay ──
        _suppressedOverlay.Visible = suppressed;
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
        _cardName.MaxLinesVisible = 1;
        _cardName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
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
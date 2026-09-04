using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// "CHOOSE YOUR PATH" screen — campaign entry point with looping class carousel.
/// Layout: title block (fixed, ~6% vh) → carousel (fills) → class-core row (~12% vh, fixed) → Begin button (fixed, padded).
/// Centre carousel card >= 55% viewport height; carousel spans >= 70% viewport width.
/// Neighbour cards are dimmed but art is always visible — never blank.
/// No card text overlaps any other card's text rect.
/// CLASS CORE strip small, centred, clearly subordinate, beneath the carousel.
/// BEGIN on its own row at the bottom with >= 24px clearance.
/// </summary>
public partial class ChooseYourPathScene : Control
{
    // ── Data ──
    private readonly List<ClassDef> _classes = new();
    private int _selectedIdx = 0; // centre = selected
    private PanelContainer _beginButton;
    private Label _beginLabel;
    private ColorRect _dotsArea;
    private readonly List<ColorRect> _dotIndicators = new();
    private Control _coreCardsArea;
    private Label _coreLabel;
    private HBoxContainer _coreCardRow;
    private Control _carouselClipContainer;
    private Control _carouselSection;
    private readonly List<Control> _panelNodes = new();

    // Carousel drag state
    private bool _dragging;
    private float _dragStartX;
    private float _dragOffset;

    // Arrow buttons
    private Button _leftArrow;
    private Button _rightArrow;

    // Layout constants — derived from viewport on each layout pass
    private float _panelFullW = 220f;
    private float _panelFullH = 310f;
    private float _centerX;
    private float _viewportW;
    private float _viewportH;

    // Carousel tuning — centre card >= 55% vh, carousel span >= 70% vw
    private const float ScaleStep = 0.22f;
    private const float MinScale = 0.45f;
    private const float BrightStep = 0.28f;
    private const float MinBright = 0.42f; // neighbours are dimmed, never blank
    private const float SpacingRatio = 0.68f; // distance between card centres as fraction of full width
    private const float OverlapMarginRatio = 0.12f; // extra push for fanned right-side cards
    private const float TextMarginRatio = 0.10f; // horizontal margin fraction for text inside card (avoids text overlap)

    // Container references
    private VBoxContainer _mainVBox;
    private bool _layoutDone;

    // Capture
    private bool _captureMode;

    // ── Class data types ──
    private class ClassJson
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public string strata { get; set; } = "";
        public string town { get; set; } = "";
        public string description { get; set; } = "";
        public string blurb { get; set; } = "";
        public List<string> core_cards { get; set; } = new();
    }

    public class ClassDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public Strata Strata { get; set; }
        public string Town { get; set; } = "";
        public string Description { get; set; } = "";
        public string Blurb { get; set; } = "";
        public List<string> CoreCardIds { get; set; } = new();
    }

    // ════════════════════════════════════════════════
    // _Ready
    // ════════════════════════════════════════════════

    public override void _Ready()
    {
        var vp = GetViewportRect().Size;
        _viewportW = vp.X;
        _viewportH = vp.Y;

        // Centre card must be >= 55% of viewport height.
        float targetH = _viewportH * 0.60f;
        _panelFullH = targetH;
        // Maintain aspect ratio from original 220:310 (w:h ≈ 0.71)
        _panelFullW = _panelFullH * 220f / 310f;
        _centerX = _viewportW / 2f;

        // Dark background — full rect
        var bg = new ColorRect { Color = BgDark, MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Hero art background — full anchors, KeepAspectCovered, subtle overlay
        var heroArt = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            // Uniform dark overlay across whole frame — no hard split = no pale band
            Modulate = new Color(0.62f, 0.62f, 0.62f, 0.60f)
        };
        heroArt.SetAnchorsPreset(LayoutPreset.FullRect);
        string heroPath = "res://content/art/title/hero_art.png";
        if (ResourceLoader.Exists(heroPath))
            heroArt.Texture = GD.Load<Texture2D>(heroPath);
        else
            GD.Print("[ART-MISSING] title/hero_art.png");
        AddChild(heroArt);

        // Subtle overall dark vignette (not a split gradient — no band)
        var vignette = new ColorRect
        {
            Color = new Color(0.04f, 0.03f, 0.02f, 0.45f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        vignette.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(vignette);

        // ═══ MAIN VBox CONTAINER — fills full viewport ═══
        _mainVBox = new VBoxContainer();
        _mainVBox.SetAnchorsPreset(LayoutPreset.FullRect);
        _mainVBox.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_mainVBox);

        // ── 1. Title block (fixed height) ──
        BuildTitleBlock();

        // ── 2. Carousel section (fills remaining space via size_flags_vertical=3) ──
        BuildCarouselSection();

        // ── 3. Core cards row (fixed <= 12% viewport height) ──
        BuildCoreSection();

        // ── 4. Begin button (fixed height, centered, with margin) ──
        BuildBeginButton();

        // Load classes
        LoadClasses();

        // Build carousel panels
        BuildCarouselPanels();

        // Initial carousel render
        UpdateCarousel();
        UpdateUI();
        RunVerify();

        // Soak mode: auto-select first class (warrior), auto-Begin
        // Runs regardless of AutoCaptureScreenshot — needed by LoopSmokeTest.
        if (CampaignContext.SoakActive)
        {
            _selectedIdx = 0;
            UpdateCarousel();
            UpdateUI();
            RunVerify();
            GD.Print("[ChooseYourPath] Soak mode — selected " + _classes[0].Name + ", auto-beginning");
            var soakBeginTimer = GetTree().CreateTimer(0.5f);
            soakBeginTimer.Timeout += OnBegin;
            return;
        }

        // Capture hook
        if (CampaignContext.AutoCaptureScreenshot)
        {
            _captureMode = true;

            if (CampaignContext.CaptureChoosePathScreenshot)
            {
                _selectedIdx = Mathf.Min(3, _classes.Count - 1);
                UpdateCarousel();
                UpdateUI();
                RunVerify();
            }

            var timer = GetTree().CreateTimer(1.0f);
            timer.Timeout += () =>
            {
                var image = GetViewport().GetTexture().GetImage();
                if (image != null)
                {
                    string path = CampaignContext.WideCaptureMode
                        ? "/home/fictive/runewake/artifacts/captures/choose_path_wide.png"
                        : "/home/fictive/runewake/artifacts/captures/choose_path.png";
                    image.SavePng(path);
                    string baseName = CampaignContext.WideCaptureMode ? "choose_path_wide" : "choose_path";
                    GD.Print($"[ChooseYourPath] Captured to {path}");

                    // TASK-UI-LINT-1: Dump layout JSON
                    DebugCapture.DumpLayoutJSON(baseName, this);
                }
                GetTree().Quit(0);
            };
        }
    }

    // ════════════════════════════════════════════════
    // Layout Builder — VBoxContainer children
    // ════════════════════════════════════════════════

    private void BuildTitleBlock()
    {
        // Compact title — proportional to viewport
        float titleH = Mathf.Max(24f, _viewportH * 0.040f);
        float subH = Mathf.Max(18f, _viewportH * 0.022f);

        var titleBlock = new VBoxContainer();
        titleBlock.CustomMinimumSize = new Vector2(0, titleH + subH + 8f);
        titleBlock.MouseFilter = MouseFilterEnum.Ignore;
        titleBlock.AddThemeConstantOverride("separation", 2);
        _mainVBox.AddChild(titleBlock);

        var title = new Label
        {
            Text = "CHOOSE YOUR PATH",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = (SizeFlags)3,
            SizeFlagsVertical = (SizeFlags)3
        };
        ApplyHeaderFont(title, (int)(FontSmall + 4));
        title.AddThemeColorOverride("font_color", Gold);
        title.AddThemeConstantOverride("outline_size", 2);
        title.AddThemeColorOverride("font_outline_color", Colors.Black);
        titleBlock.AddChild(title);

        var subtitle = new Label
        {
            Text = "Each path begins in its own town, with its own tale.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
            SizeFlagsHorizontal = (SizeFlags)3,
            SizeFlagsVertical = (SizeFlags)3
        };
        ApplyBodyFont(subtitle, FontSmall);
        subtitle.AddThemeColorOverride("font_color", Color.FromHtml("#C8B88A"));
        subtitle.AddThemeColorOverride("font_outline_color", Colors.Black);
        subtitle.AddThemeConstantOverride("outline_size", 1);
        titleBlock.AddChild(subtitle);
    }

    private void BuildCarouselSection()
    {
        // Container that fills the VBox — hosts ClipContainer + arrows + dots
        _carouselSection = new Control
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = (SizeFlags)3, // Fill | Expand
            SizeFlagsHorizontal = (SizeFlags)3
        };
        _mainVBox.AddChild(_carouselSection);

        // Clip container for panels — fills the section
        _carouselClipContainer = new Control
        {
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _carouselClipContainer.SetAnchorsPreset(LayoutPreset.FullRect);
        _carouselSection.AddChild(_carouselClipContainer);

        // Left arrow — positioned absolutely in LayoutCarouselChildren
        _leftArrow = new Button
        {
            Text = "\u25C0",
            Flat = true,
            CustomMinimumSize = new Vector2(44, 44),
            MouseFilter = MouseFilterEnum.Stop
        };
        _leftArrow.AddThemeFontSizeOverride("font_size", 20);
        _leftArrow.AddThemeColorOverride("font_color", Color.FromHtml("#C8B88A"));
        _leftArrow.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            _selectedIdx = (_selectedIdx - 1 + _classes.Count) % _classes.Count;
            UpdateCarousel();
            UpdateUI();
        };
        _carouselSection.AddChild(_leftArrow);

        // Right arrow
        _rightArrow = new Button
        {
            Text = "\u25B6",
            Flat = true,
            CustomMinimumSize = new Vector2(44, 44),
            MouseFilter = MouseFilterEnum.Stop
        };
        _rightArrow.AddThemeFontSizeOverride("font_size", 20);
        _rightArrow.AddThemeColorOverride("font_color", Color.FromHtml("#C8B88A"));
        _rightArrow.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            _selectedIdx = (_selectedIdx + 1) % _classes.Count;
            UpdateCarousel();
            UpdateUI();
        };
        _carouselSection.AddChild(_rightArrow);

        // Dots indicator area — positioned at bottom of carouselSection
        _dotsArea = new ColorRect
        {
            // Slightly dark backing to block the background from showing through as a pale band
            Color = new Color(0.08f, 0.065f, 0.05f, 0.70f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _carouselSection.AddChild(_dotsArea);
    }

    private void BuildCarouselPanels()
    {
        for (int i = 0; i < _classes.Count; i++)
        {
            var panel = MakeCarouselPanel(_classes[i], i);
            _carouselClipContainer.AddChild(panel);
            _panelNodes.Add(panel);
            int idx = i;
            panel.GuiInput += (@event) =>
            {
                if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                    SnapToIndex(idx);
            };
        }
    }

    private void BuildCoreSection()
    {
        // Core section — <= 12% viewport height, explicitly subordinate
        float coreSectionH = Mathf.Min(_viewportH * 0.12f, 140f);

        _coreCardsArea = new CenterContainer
        {
            CustomMinimumSize = new Vector2(0, coreSectionH),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _mainVBox.AddChild(_coreCardsArea);
    }

    private void BuildBeginButton()
    {
        // Begin with margin — >= 24px clearance
        var beginWrap = new VBoxContainer();
        beginWrap.Name = "Begin";
        beginWrap.MouseFilter = MouseFilterEnum.Pass;
        beginWrap.AddThemeConstantOverride("separation", 0);
        beginWrap.SizeFlagsVertical = (SizeFlags)0; // Shrink
        // Minimum height: button (46) + margin above (24) + margin below (12)
        beginWrap.CustomMinimumSize = new Vector2(0, 82f);
        _mainVBox.AddChild(beginWrap);

        // Spacer above the button for clearance
        var spacer = new ColorRect
        {
            Color = Colors.Transparent,
            SizeFlagsVertical = (SizeFlags)3, // Expand to fill any extra space
            MouseFilter = MouseFilterEnum.Ignore
        };
        beginWrap.AddChild(spacer);

        _beginButton = new PanelContainer();
        _beginButton.CustomMinimumSize = new Vector2(280, 46);
        _beginButton.MouseDefaultCursorShape = CursorShape.PointingHand;
        _beginButton.SizeFlagsHorizontal = (SizeFlags)4; // Center
        _beginButton.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = SurfaceStone,
            BorderColor = Gold,
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        });
        beginWrap.AddChild(_beginButton);

        _beginLabel = new Label
        {
            Text = "BEGIN",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ApplyHeaderFont(_beginLabel, FontBody);
        _beginLabel.AddThemeColorOverride("font_color", Gold);
        _beginLabel.SetAnchorsPreset(LayoutPreset.FullRect);
        _beginButton.AddChild(_beginLabel);

        var beginClick = new Button();
        beginClick.SetAnchorsPreset(LayoutPreset.FullRect);
        beginClick.MouseDefaultCursorShape = CursorShape.PointingHand;
        beginClick.Text = "Begin"; // needed by LoopSmokeTest.FindVisibleButton
        var btnTransparent = new StyleBoxFlat { BgColor = Colors.Transparent };
        beginClick.AddThemeStyleboxOverride("normal", btnTransparent);
        beginClick.AddThemeStyleboxOverride("hover", btnTransparent);
        beginClick.AddThemeStyleboxOverride("pressed", btnTransparent);
        beginClick.Pressed += OnBegin;
        _beginButton.AddChild(beginClick);
    }

    // ════════════════════════════════════════════════
    // Layout — positioning based on actual container sizes
    // ════════════════════════════════════════════════

    private void LayoutCarouselChildren()
    {
        if (_carouselSection == null) return;

        var vp = GetViewportRect().Size;
        _viewportW = vp.X;
        _viewportH = vp.Y;
        _centerX = _viewportW / 2f;

        // Recalc panel size — centre card >= 55% vh
        float targetH = _viewportH * 0.60f;
        _panelFullH = targetH;
        _panelFullW = _panelFullH * 220f / 310f;

        float sectionH = _carouselSection.Size.Y;

        // Arrows vertically centred in carousel section
        float arrowY = Mathf.Max(8f, sectionH / 2f - 22f);
        _leftArrow.Position = new Vector2(8, arrowY);
        _rightArrow.Position = new Vector2(_viewportW - 52, arrowY);

        // Dots at bottom of carousel section — slightly taller band blocks pale background
        float dotsY = Mathf.Max(0, sectionH - 32f);
        _dotsArea.Position = new Vector2(0, dotsY);
        _dotsArea.Size = new Vector2(_viewportW, 26);

        // Re-position dots
        float dotSpacing = 14f;
        float dotsTotal = (_classes.Count - 1) * dotSpacing;
        float dotsStartX = (_viewportW - dotsTotal) / 2f;
        for (int i = 0; i < _dotIndicators.Count && i < _dotsArea.GetChildCount(); i++)
        {
            var dot = _dotsArea.GetChild<ColorRect>(i);
            if (dot != null)
            {
                dot.Position = new Vector2(dotsStartX + i * dotSpacing, 3);
            }
        }

        // Clip container fills the section
        _carouselClipContainer.Size = new Vector2(_viewportW, sectionH);
    }

    private void RefreshLayout()
    {
        LayoutCarouselChildren();
        UpdateCarousel();
    }
    public override void _Process(double delta)
        {
            // On first frame after sizing, lay out the arrows/dots AND reposition cards
            if (!_layoutDone && _carouselSection != null && _carouselSection.Size.Y > 0)
            {
                LayoutCarouselChildren();
                UpdateCarousel(); // Recalculate card positions now that container is sized
                RunVerify();
                _layoutDone = true;
            }
        }

    // ════════════════════════════════════════════════
    // Carousel
    // ════════════════════════════════════════════════

    private void UpdateCarousel()
    {
        int total = _panelNodes.Count;
        if (total == 0) return;

        // Recompute layout constants from current viewport
        float targetH = _viewportH * 0.60f;
        _panelFullH = targetH;
        _panelFullW = _panelFullH * 220f / 310f;
        _centerX = _viewportW / 2f;

        float spacing = _panelFullW * SpacingRatio;
        float overlapMargin = _panelFullW * OverlapMarginRatio;
        float textMargin = _panelFullW * TextMarginRatio;
        float containerH = _carouselClipContainer.Size.Y;
        if (containerH < 1f) containerH = _panelFullH + 12f;

        for (int i = 0; i < total; i++)
        {
            var panel = _panelNodes[i];
            int rawDist = i - _selectedIdx;
            int dist = rawDist;
            if (dist > total / 2) dist -= total;
            else if (dist < -total / 2) dist += total;

            float absDist = Mathf.Abs(dist);

            // Scale: centre card is full size, neighbours scale down
            float scale = 1f - absDist * ScaleStep;
            if (scale < MinScale) scale = MinScale;

            // Brightness: centre card full bright, neighbours dimmed but art always visible
            float bright = 1f - absDist * BrightStep;
            if (bright < MinBright) bright = MinBright;

            panel.ZIndex = (int)(total - absDist);

            float panelW = _panelFullW * scale;
            float panelH = _panelFullH * scale;

            // Fan the cards: centre is centred, neighbours spread with slight offset for visual overlap
            float xPos = _centerX - panelW / 2f + dist * spacing;
            if (dist > 0) xPos += overlapMargin;
            else if (dist < 0) xPos -= overlapMargin;

            // Vertical centre within the container
            float yPos = (containerH - panelH) / 2f;
            // Slight vertical stagger for neighbours — cards at greater distance sit slightly lower
            // to keep the orbital feel (they look like they are behind and below)
            float yOffset = absDist * _panelFullH * 0.03f;
            yPos += yOffset;

            panel.Position = new Vector2(xPos, yPos);
            panel.Size = new Vector2(panelW, panelH);
            panel.Scale = Vector2.One;

            panel.Modulate = new Color(bright, bright, bright, 1f);

            // Only show panels within viewport bounds + margin
            panel.Visible = xPos + panelW > -100 && xPos < _viewportW + 100;
        }

        // Update dots
        for (int i = 0; i < _dotIndicators.Count; i++)
            _dotIndicators[i].Color = i == _selectedIdx ? Gold : TextInactive;
    }

    private void RunVerify()
    {
        int failed = 0;
        var containerGlobal = _carouselClipContainer.GetRect();

        if (containerGlobal.Size.X < 1 || containerGlobal.Size.Y < 1)
        {
            GD.Print("[VERIFY] carousel: skipped (container not yet laid out)");
            return;
        }

        for (int i = 0; i < _panelNodes.Count; i++)
        {
            var panel = _panelNodes[i];
            var panelGlobal = new Rect2(
                containerGlobal.Position + panel.Position,
                panel.Size
            );

            if (!containerGlobal.Encloses(panelGlobal))
            {
                GD.Print($"[VERIFY] Panel {i}: global_pos={panelGlobal.Position}, " +
                         $"size={panel.Size}, container={containerGlobal} — outside");
                failed++;
            }
        }

        // Check centre panel is full size
        if (_selectedIdx >= 0 && _selectedIdx < _panelNodes.Count)
        {
            var centerPanel = _panelNodes[_selectedIdx];
            if (Mathf.Abs(centerPanel.Size.X - _panelFullW) > 2f ||
                Mathf.Abs(centerPanel.Size.Y - _panelFullH) > 2f)
            {
                GD.Print($"[VERIFY] Center panel size mismatch: actual={centerPanel.Size}, " +
                         $"expected=({_panelFullW},{_panelFullH})");
                failed++;
            }
        }

        GD.Print($"[VERIFY] carousel: {failed} failed");
    }

    private void SnapToIndex(int idx)
    {
        _selectedIdx = idx;
        UpdateCarousel();
        UpdateUI();
    }

    // ════════════════════════════════════════════════
    // Drag handling
    // ════════════════════════════════════════════════

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    _dragging = true;
                    _dragStartX = mb.Position.X;
                    _dragOffset = 0;
                }
                else if (_dragging)
                {
                    _dragging = false;
                    float threshold = _panelFullW * 0.25f;
                    int dir = _dragOffset > threshold ? 1 : (_dragOffset < -threshold ? -1 : 0);
                    if (dir != 0)
                    {
                        _selectedIdx = (_selectedIdx + dir + _classes.Count) % _classes.Count;
                    }
                    UpdateCarousel();
                    UpdateUI();
                }
            }
            return;
        }

        if (_dragging && @event is InputEventMouseMotion mm)
        {
            _dragOffset += mm.Relative.X;
        }
    }

    // ════════════════════════════════════════════════
    // UI updates
    // ════════════════════════════════════════════════

    private void UpdateUI()
    {
        if (_selectedIdx < 0 || _selectedIdx >= _classes.Count) return;
        var cls = _classes[_selectedIdx];

        // Update dots
        for (int i = 0; i < _dotIndicators.Count; i++)
            _dotIndicators[i].Color = i == _selectedIdx ? Gold : TextInactive;

        // Update core cards
        foreach (var child in _coreCardsArea.GetChildren())
            child.QueueFree();

        float coreSectionH = _coreCardsArea.Size.Y;
        float coreAvailH = coreSectionH > 0 ? coreSectionH : Mathf.Min(_viewportH * 0.12f, 140f);

        var strataColor = StrataColor(cls.Strata);

        _coreLabel = new Label
        {
            Text = $"CLASS CORE \u2014 four sworn cards every {cls.Name} carries",
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = (SizeFlags)3
        };
        ApplyBodyFont(_coreLabel, FontSmall);
        _coreLabel.AddThemeColorOverride("font_color", TextSecondary);

        // Core cards as a vertical section
        var coreVBox = new VBoxContainer();
        coreVBox.SizeFlagsHorizontal = (SizeFlags)3; // Fill width
        coreVBox.SizeFlagsVertical = (SizeFlags)3; // Fill height
        coreVBox.AddThemeConstantOverride("separation", 2);
        coreVBox.MouseFilter = MouseFilterEnum.Ignore;
        _coreCardsArea.AddChild(coreVBox);

        // Label row
        coreVBox.AddChild(_coreLabel);

        // Core card row — centred HBox
        _coreCardRow = new HBoxContainer();
        _coreCardRow.Alignment = BoxContainer.AlignmentMode.Center;
        _coreCardRow.AddThemeConstantOverride("separation", 8);
        _coreCardRow.SizeFlagsHorizontal = (SizeFlags)3;
        _coreCardRow.SizeFlagsVertical = (SizeFlags)3;
        coreVBox.AddChild(_coreCardRow);

        // Calculate mini card size — smaller than before, <= 12% vh for the whole section
        float labelH = 20f;
        float separationH = 2f;
        float availForCards = coreAvailH - labelH - separationH - 4f;
        float miniCardRatio = 152f / 104f;
        float miniW = _viewportW * 0.055f; // ~127px at 2316
        if (miniW * miniCardRatio > availForCards)
            miniW = availForCards / miniCardRatio;
        if (miniW > 130f) miniW = 130f;
        if (miniW < 80f) miniW = 80f;
        float miniH = miniW * miniCardRatio;

        if (miniH < availForCards * 0.80f)
        {
            miniH = availForCards * 0.80f;
            miniW = miniH / miniCardRatio;
        }

        foreach (var cardId in cls.CoreCardIds)
        {
            var def = CardRegistry.Get(cardId);
            if (def == null) continue;

            var miniCard = BuildCoreMiniCard(def, miniW, miniH, strataColor);
            _coreCardRow.AddChild(miniCard);
        }
    }

    private Control BuildCoreMiniCard(CardDef def, float miniW, float miniH, Color strataColor)
    {
        var miniCard = new PanelContainer();
        miniCard.CustomMinimumSize = new Vector2(miniW, miniH);
        miniCard.Size = new Vector2(miniW, miniH);
        miniCard.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#332E28"),
            BorderColor = strataColor.Darkened(0.4f),
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 0, ContentMarginTop = 0,
            ContentMarginRight = 0, ContentMarginBottom = 0
        });

        var content = new Control();
        content.SetAnchorsPreset(LayoutPreset.FullRect);
        content.MouseFilter = MouseFilterEnum.Pass;
        miniCard.AddChild(content);

        // Mini card art
        var miniArt = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize
        };
        miniArt.SetAnchorsPreset(LayoutPreset.FullRect);
        string miniArtPath = $"res://content/art/{def.Id}.webp";
        if (ResourceLoader.Exists(miniArtPath))
        {
            var tex = ResourceLoader.Load<Texture2D>(miniArtPath);
            if (tex != null)
                miniArt.Texture = tex;
        }
        else
        {
            miniArt.Modulate = CardArtColors.Parchment;
            GD.Print($"[ART-MISSING] {def.Id} (core mini)");
        }
        content.AddChild(miniArt);

        // CardPlate overlay — name band, stat chips
        var plate = new CardPlate();
        content.AddChild(plate);
        plate.Setup(def.Name, def.Attack, def.Vigor, def.Strata, miniW, miniH);

        return miniCard;
    }

    // ════════════════════════════════════════════════
    // Carousel panel builder
    // ════════════════════════════════════════════════

    private Control MakeCarouselPanel(ClassDef cls, int index)
    {
        var panel = new Control
        {
            MouseFilter = MouseFilterEnum.Pass,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            ClipContents = true
        };

        // Background — warm dark, not pure black, so neighbour art shows on it
        var panelRect = new ColorRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Color = new Color(0.14f, 0.12f, 0.10f, 0.90f)
        };
        panelRect.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.AddChild(panelRect);

        // Border — thin keyline
        var border = new ColorRect
        {
            Color = Colors.Transparent,
            MouseFilter = MouseFilterEnum.Ignore
        };
        border.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.AddChild(border);

        // ── Main layout VBox — fills entire panel ──
        // Portrait expands to fill all space; text block shrinks to content.
        var mainLayout = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = (SizeFlags)3,
            SizeFlagsHorizontal = (SizeFlags)3
        };
        mainLayout.SetAnchorsPreset(LayoutPreset.FullRect);
        mainLayout.AddThemeConstantOverride("separation", 0);
        panel.AddChild(mainLayout);

        // ── Portrait — expands to fill all space the text block doesn't use ──
        var artRect = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            SizeFlagsVertical = (SizeFlags)3,
            SizeFlagsHorizontal = (SizeFlags)3
        };
        string artPath = $"res://content/art/classes/{cls.Id}.png";
        if (ResourceLoader.Exists(artPath))
        {
            var tex = ResourceLoader.Load<Texture2D>(artPath);
            if (tex != null)
                artRect.Texture = tex;
            else
                SetFallbackPortrait(artRect, cls);
        }
        else
        {
            SetFallbackPortrait(artRect, cls);
        }
        mainLayout.AddChild(artRect);

        // ── Text block — sits below the portrait, sized to its content ──
        float margin = Mathf.Max(6f, _panelFullW * TextMarginRatio);
        var textBlock = new MarginContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = (SizeFlags)4, // Shrink Center — minimum height
            SizeFlagsHorizontal = (SizeFlags)3
        };
        textBlock.AddThemeConstantOverride("margin_left", (int)margin);
        textBlock.AddThemeConstantOverride("margin_right", (int)margin);
        textBlock.AddThemeConstantOverride("margin_top", 2);
        textBlock.AddThemeConstantOverride("margin_bottom", 2);
        mainLayout.AddChild(textBlock);

        var vbox = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        vbox.AddThemeConstantOverride("separation", 1);
        textBlock.AddChild(vbox);

        // Class name — font scales with panel size
        var nameLabel = new Label
        {
            Text = cls.Name,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ApplyHeaderFont(nameLabel, FontBody);
        nameLabel.AddThemeColorOverride("font_color", Color.FromHtml("#E8DCC8"));
        nameLabel.AddThemeConstantOverride("outline_size", 1);
        nameLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        vbox.AddChild(nameLabel);

        // Blurb
        var blurbLabel = new Label
        {
            Text = cls.Blurb,
            AutowrapMode = TextServer.AutowrapMode.Word,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        ApplyBodyFont(blurbLabel, FontTiny);
        blurbLabel.AddThemeColorOverride("font_color", Color.FromHtml("#C8B88A"));
        blurbLabel.AddThemeConstantOverride("outline_size", 1);
        blurbLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.4f));
        vbox.AddChild(blurbLabel);

        // Origin
        var originLabel = new Label
        {
            Text = $"Origin \u00b7 {cls.Town}",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ApplyBodyFont(originLabel, FontTiny);
        originLabel.AddThemeColorOverride("font_color", TextMuted);
        originLabel.AddThemeConstantOverride("outline_size", 1);
        originLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.4f));
        vbox.AddChild(originLabel);

        return panel;
    }

    /// <summary>
    /// Create a visible coloured fallback for classes without portrait art.
    /// Generates a visible texture so neighbour cards never read as empty dark plates.
    /// </summary>
    private void SetFallbackPortrait(TextureRect target, ClassDef cls)
    {
        // Bright warm hue with visible stratum tint — must read as "a class" not an empty slot
        var strataColor = StrataColor(cls.Strata).Lightened(0.7f);
        target.Modulate = strataColor;
        // Create a 16x16 coloured texture
        var img = Godot.Image.CreateEmpty(16, 16, false, Godot.Image.Format.Rgba8);
        // Fill with a visible warm tint based on stratum
        Color fillColor = cls.Strata switch
        {
            Strata.TIDE => new Color(0.3f, 0.6f, 0.7f, 1.0f),   // blue-green
            Strata.HOLLOW => new Color(0.5f, 0.4f, 0.3f, 1.0f),  // earthy
            Strata.DAWN => new Color(0.8f, 0.7f, 0.5f, 1.0f),    // gold
            _ => new Color(0.6f, 0.5f, 0.4f, 1.0f)               // neutral warm
        };
        img.Fill(fillColor);
        target.Texture = Godot.ImageTexture.CreateFromImage(img);
        // Remove dim self-modulate — let the strata colour show clearly
        target.SelfModulate = Colors.White;
    }

    // ════════════════════════════════════════════════
    // Data loading
    // ════════════════════════════════════════════════

    private void LoadClasses()
    {
        string json = Godot.FileAccess.GetFileAsString("res://content/classes.json");
        var data = System.Text.Json.JsonSerializer.Deserialize<List<ClassJson>>(json);
        if (data == null) return;

        foreach (var c in data)
        {
            _classes.Add(new ClassDef
            {
                Id = c.id,
                Name = c.name,
                Strata = Enum.Parse<Strata>(c.strata, ignoreCase: true),
                Town = c.town,
                Description = c.description,
                Blurb = c.blurb,
                CoreCardIds = c.core_cards
            });
        }

        // Build dot indicators
        for (int i = 0; i < _classes.Count; i++)
        {
            var dot = new ColorRect
            {
                Color = i == _selectedIdx ? Gold : TextInactive,
                Size = new Vector2(8, 8),
                MouseFilter = MouseFilterEnum.Ignore
            };
            _dotsArea.AddChild(dot);
            _dotIndicators.Add(dot);
        }
    }

    // ════════════════════════════════════════════════
    // Begin
    // ════════════════════════════════════════════════

    private void OnBegin()
    {
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
        if (_selectedIdx < 0 || _selectedIdx >= _classes.Count) return;
        var cls = _classes[_selectedIdx];

        CampaignContext.ChosenClass = cls.Id;
        CampaignContext.ChosenTown = cls.Town;
        CampaignContext.CoreCardIds = new List<string>(cls.CoreCardIds);
        CampaignContext.AddOrUpdateProfile(cls.Id, cls.Town);

        CampaignContext.EnsureStarterDeck(cls.Id);
        GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// "CHOOSE YOUR PATH" screen — campaign entry point with looping class carousel.
/// VBoxContainer layout: title block (fixed) → carousel (fills) → class-core row (~22% vh, fixed) → Begin button (fixed).
/// Bulletproof geometry: every TextureRect has ExpandMode=IgnoreSize + full anchors,
/// portrait fills upper ~62% of panel, text below it, ClipContents everywhere.
/// Each core card owns its own attack/vigor chips (via CardPlate), anchored to its bottom corners.
/// </summary>
public partial class ChooseYourPathScene : Control
{
    // ── Data ──
    private readonly List<ClassDef> _classes = new();
    private int _selectedIdx = 0; // center = selected
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

    // Layout constants
    private float _panelFullW = 220f;
    private float _panelFullH = 310f;
    private float _centerX;
    private float _viewportW;
    private float _viewportH;

    // Container references for layout
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

        _panelFullW = Mathf.Min(240f, _viewportW * 0.14f);
        _panelFullH = _panelFullW * 310f / 220f;
        _centerX = _viewportW / 2f;

        // Dark background
        var bg = new ColorRect { Color = BgDark, MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Hero art background — full anchors, ExpandMode=IgnoreSize
        var heroArt = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Modulate = new Color(0.62f, 0.62f, 0.62f, 0.75f)
        };
        heroArt.SetAnchorsPreset(LayoutPreset.FullRect);
        string heroPath = "res://content/art/title/hero_art.png";
        if (ResourceLoader.Exists(heroPath))
            heroArt.Texture = GD.Load<Texture2D>(heroPath);
        else
            GD.Print("[ART-MISSING] title/hero_art.png");
        AddChild(heroArt);

        // Dark gradient (bottom half)
        var gradient = new ColorRect
        {
            Color = new Color(0.04f, 0.03f, 0.02f, 0.55f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        gradient.AnchorLeft = 0; gradient.AnchorRight = 1;
        gradient.AnchorTop = 0.5f; gradient.AnchorBottom = 1;
        AddChild(gradient);

        // ═══ MAIN VBox CONTAINER — fills full viewport ═══
        _mainVBox = new VBoxContainer();
        _mainVBox.SetAnchorsPreset(LayoutPreset.FullRect);
        _mainVBox.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_mainVBox);

        // ── 1. Title block (fixed height) ──
        BuildTitleBlock();

        // ── 2. Carousel section (fills remaining space via size_flags_vertical=3) ──
        BuildCarouselSection();

        // ── 3. Core cards row (fixed ~22% of viewport height) ──
        BuildCoreSection();

        // ── 4. Begin button (fixed height, centered) ──
        BuildBeginButton();

        // Load classes — must happen after layout building so _dotsArea exists
        LoadClasses();

        // Build carousel panels now that classes are loaded
        BuildCarouselPanels();

        // Initial carousel render
        UpdateCarousel();
        UpdateUI();
        RunVerify();

        // Capture hook
        if (CampaignContext.AutoCaptureScreenshot)
        {
            _captureMode = true;

            // If choose-path specific capture, auto-select index 3 (Tidecaller/Battlemage) for proof
            if (CampaignContext.CaptureChoosePathScreenshot)
            {
                _selectedIdx = Mathf.Min(3, _classes.Count - 1);
                UpdateCarousel();
                UpdateUI();
                RunVerify();
            }

            // Soak mode: auto-select first class (warrior), auto-Begin after short delay
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
                    DebugCapture.WriteLayoutJson(this, baseName);
                    GD.Print($"[ChooseYourPath] Captured to {path}");

                    // TASK-UI-LINT-1: Dump layout JSON
                    string cypBasename = CampaignContext.WideCaptureMode ? "choose_path_wide" : "choose_path";
                    DebugCapture.DumpLayoutJSON(cypBasename, this);
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
        float titleH = 48f * _viewportH / 1080f;
        float subH = 26f;

        var titleBlock = new VBoxContainer();
        titleBlock.CustomMinimumSize = new Vector2(0, titleH + subH + 12f);
        titleBlock.MouseFilter = MouseFilterEnum.Ignore;
        titleBlock.AddThemeConstantOverride("separation", 4);
        _mainVBox.AddChild(titleBlock);

        // Title
        var title = new Label
        {
            Text = "CHOOSE YOUR PATH",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = (SizeFlags)3,
            SizeFlagsVertical = (SizeFlags)3
        };
        ApplyHeaderFont(title, FontSmall + 4);
        title.AddThemeColorOverride("font_color", Gold);
        title.AddThemeConstantOverride("outline_size", 2);
        title.AddThemeColorOverride("font_outline_color", Colors.Black);
        titleBlock.AddChild(title);

        // Subtitle
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

        // The actual clip container for panels — positioned to fill the section
        _carouselClipContainer = new Control
        {
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _carouselClipContainer.SetAnchorsPreset(LayoutPreset.FullRect);
        _carouselSection.AddChild(_carouselClipContainer);

        // Arrow buttons — positioned absolutely within carouselSection (above clip container for z-order)
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

        // Dots indicator — positioned at bottom of carouselSection
        _dotsArea = new ColorRect
        {
            Color = Colors.Transparent,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _carouselSection.AddChild(_dotsArea);
    }

    private void BuildCarouselPanels()
    {
        // Build panels inside clip container
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
        // Fixed ~22% of viewport height
        float coreSectionH = _viewportH * 0.22f;

        _coreCardsArea = new Control();
        _coreCardsArea.CustomMinimumSize = new Vector2(0, coreSectionH);
        _coreCardsArea.MouseFilter = MouseFilterEnum.Ignore;
        _mainVBox.AddChild(_coreCardsArea);
    }

    private void BuildBeginButton()
    {
        // Fixed ~50px with margin
        var beginWrapper = new VBoxContainer();
        beginWrapper.Name = "Begin";
        beginWrapper.CustomMinimumSize = new Vector2(0, 60f);
        beginWrapper.MouseFilter = MouseFilterEnum.Pass;
        beginWrapper.AddThemeConstantOverride("separation", 0);
        _mainVBox.AddChild(beginWrapper);

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
        beginWrapper.AddChild(_beginButton);

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
        var btnTransparent = new StyleBoxFlat { BgColor = Colors.Transparent };
        beginClick.AddThemeStyleboxOverride("normal", btnTransparent);
        beginClick.AddThemeStyleboxOverride("hover", btnTransparent);
        beginClick.AddThemeStyleboxOverride("pressed", btnTransparent);
        beginClick.Pressed += OnBegin;
        _beginButton.AddChild(beginClick);
    }

    // ════════════════════════════════════════════════
    // Layout — post-_Ready positioning based on actual container sizes
    // ════════════════════════════════════════════════

    /// <summary>
    /// Called after the layout pass to position arrows and dots relative to the carousel section.
    /// Also re-measures viewport and panel sizes.
    /// </summary>
    private void LayoutCarouselChildren()
    {
        if (_carouselSection == null) return;

        var vp = GetViewportRect().Size;
        _viewportW = vp.X;
        _viewportH = vp.Y;
        _centerX = _viewportW / 2f;

        _panelFullW = Mathf.Min(240f, _viewportW * 0.14f);
        _panelFullH = _panelFullW * 310f / 220f;

        float sectionH = _carouselSection.Size.Y;

        // Arrows vertically centered in carousel section
        float arrowY = Mathf.Max(0, sectionH / 2f - 22f);
        _leftArrow.Position = new Vector2(8, arrowY);
        _rightArrow.Position = new Vector2(_viewportW - 52, arrowY);

        // Update dots area size and position
        float dotsY = Mathf.Max(0, sectionH - 20f);
        _dotsArea.Position = new Vector2(0, dotsY);
        _dotsArea.Size = new Vector2(_viewportW, 14);

        // Re-position dot indicators inside dots area
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

        // Carousel clip container fills the section
        _carouselClipContainer.Size = new Vector2(_viewportW, sectionH);
    }

    /// <summary>
    /// Post-layout positioning. Called after all container sizing has been resolved.
    /// </summary>
    private void RefreshLayout()
    {
        LayoutCarouselChildren();
        UpdateCarousel();
    }

    /// <summary>Override to position layout-dependent elements after VBoxContainer sizing.</summary>
    public override void _Process(double delta)
    {
        // On first frame after sizing, lay out the arrows/dots
        if (!_layoutDone && _carouselSection != null && _carouselSection.Size.Y > 0)
        {
            LayoutCarouselChildren();
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
        float spacing = _panelFullW * 0.55f;
        float overlapMargin = _panelFullW * 0.22f;
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

            float scale = 1f - absDist * 0.22f;
            if (scale < 0.5f) scale = 0.5f;

            float bright = 1f - absDist * 0.45f;
            if (bright < 0.3f) bright = 0.3f;

            panel.ZIndex = (int)(total - absDist);

            float panelW = _panelFullW * scale;
            float panelH = _panelFullH * scale;

            float xPos = _centerX - panelW / 2f + dist * spacing;
            if (dist > 0) xPos += overlapMargin;
            else if (dist < 0) xPos -= overlapMargin;

            panel.Position = new Vector2(xPos, (containerH - panelH) / 2f);
            panel.Size = new Vector2(panelW, panelH);
            panel.Scale = Vector2.One;
            panel.Modulate = new Color(bright, bright, bright, 1f);

            panel.Visible = xPos + panelW > -50 && xPos < _centerX * 2 + 50;
        }

        // Update dots
        for (int i = 0; i < _dotIndicators.Count; i++)
            _dotIndicators[i].Color = i == _selectedIdx ? Gold : TextInactive;
    }

    /// <summary>
    /// Layout self-check: every panel rect fully inside container, center panel full size.
    /// Uses global coordinates for proper containment check.
    /// </summary>
    private void RunVerify()
    {
        int failed = 0;
        var containerGlobal = _carouselClipContainer.GetRect();
        
        // Skip if container hasn't been laid out yet (VBox sizing happens after _Ready)
        if (containerGlobal.Size.X < 1 || containerGlobal.Size.Y < 1)
        {
            GD.Print("[VERIFY] carousel: skipped (container not yet laid out)");
            return;
        }

        for (int i = 0; i < _panelNodes.Count; i++)
        {
            var panel = _panelNodes[i];
            // Panel's global rect = container global position + panel local position
            var panelGlobal = new Rect2(
                containerGlobal.Position + panel.Position,
                panel.Size
            );

            // Check containment in carousel container
            if (!containerGlobal.Encloses(panelGlobal))
            {
                GD.Print($"[VERIFY] Panel {i}: global_pos={panelGlobal.Position}, " +
                         $"size={panel.Size}, container={containerGlobal} — outside");
                failed++;
            }
            else
            {
                GD.Print($"[VERIFY] Panel {i}: OK (pos={panel.Position}, size={panel.Size})");
            }
        }

        // Check center panel is full size
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
        float coreAvailH = coreSectionH > 0 ? coreSectionH : _viewportH * 0.22f;

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
        coreVBox.AddThemeConstantOverride("separation", 4);
        coreVBox.MouseFilter = MouseFilterEnum.Ignore;
        _coreCardsArea.AddChild(coreVBox);

        // Label row
        coreVBox.AddChild(_coreLabel);

        // Core card row — centered HBox with proper sizing
        _coreCardRow = new HBoxContainer();
        _coreCardRow.Alignment = BoxContainer.AlignmentMode.Center;
        _coreCardRow.AddThemeConstantOverride("separation", 12);
        _coreCardRow.SizeFlagsHorizontal = (SizeFlags)3; // Fill width
        _coreCardRow.SizeFlagsVertical = (SizeFlags)3; // Fill height
        coreVBox.AddChild(_coreCardRow);

        // Calculate mini card size based on available height
        float labelH = 22f;
        float separationH = 4f;
        float availForCards = coreAvailH - labelH - separationH - 4f; // 4px padding
        float miniCardRatio = 152f / 104f; // aspect ratio of cards
        float miniW = _viewportW * 0.065f; // ~150px at 2316
        if (miniW * miniCardRatio > availForCards)
            miniW = availForCards / miniCardRatio;
        if (miniW > 160f) miniW = 160f;
        if (miniW < 100f) miniW = 100f;
        float miniH = miniW * miniCardRatio;

        // Ensure each mini card is large enough that names are readable at arm's length
        if (miniH < availForCards * 0.85f)
        {
            miniH = availForCards * 0.85f;
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

        // Mini card art — ExpandMode=IgnoreSize + full anchors
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

        // CardPlate overlay — provides name band, stat chips at bottom corners
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

        // Background
        var panelRect = new ColorRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Color = new Color(0.12f, 0.10f, 0.08f, 0.95f)
        };
        panelRect.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.AddChild(panelRect);

        // Border
        var border = new ColorRect
        {
            Color = Colors.Transparent,
            MouseFilter = MouseFilterEnum.Ignore
        };
        border.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.AddChild(border);

        // ── Portrait — fills upper ~62% of panel ──
        // TextureRect: ExpandMode=IgnoreSize, full anchors (not inheriting texture size)
        var artRect = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            AnchorLeft = 0, AnchorRight = 1,
            AnchorTop = 0, AnchorBottom = 0.62f
        };
        string artPath = $"res://content/art/classes/{cls.Id}.png";
        if (ResourceLoader.Exists(artPath))
        {
            var tex = ResourceLoader.Load<Texture2D>(artPath);
            if (tex != null)
                artRect.Texture = tex;
        }
        else
        {
            artRect.Modulate = StrataColor(cls.Strata).Darkened(0.5f);
        }
        panel.AddChild(artRect);

        // ── Text block — sits BELOW the portrait, lower ~38% ──
        var vbox = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 0, AnchorRight = 1,
            AnchorTop = 0.62f, AnchorBottom = 1,
            OffsetLeft = 4, OffsetRight = -4,
            OffsetTop = 2, OffsetBottom = -2,
            SizeFlagsVertical = (SizeFlags)3
        };
        vbox.AddThemeConstantOverride("separation", 1);
        panel.AddChild(vbox);

        // Class name
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

        // Build dot indicators now that we know how many classes
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

        // Every class ships with a prebuilt starter deck — new players go
        // straight to the map and start playing. The Forge stays available
        // from the map/title for whenever they want to customize.
        CampaignContext.EnsureStarterDeck(cls.Id);
        GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
    }
}
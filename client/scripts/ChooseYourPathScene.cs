using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// "CHOOSE YOUR PATH" screen — campaign entry point with looping class carousel.
/// Bulletproof geometry: every TextureRect has ExpandMode=IgnoreSize + full anchors,
/// portrait fills upper ~62% of panel, text below it, ClipContents everywhere.
/// </summary>
public partial class ChooseYourPathScene : Control
{
    // ── Data ──
    private readonly List<ClassDef> _classes = new();
    private int _selectedIdx = 0; // center = selected
    private Control _beginButton;
    private Label _beginLabel;
    private ColorRect _dotsArea;
    private readonly List<ColorRect> _dotIndicators = new();
    private Control _coreCardsArea;
    private Label _coreLabel;
    private HBoxContainer _coreCardRow;
    private Control _carouselPanelContainer;
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

        LoadClasses();

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

        // Dark gradient
        var gradient = new ColorRect
        {
            Color = new Color(0.04f, 0.03f, 0.02f, 0.55f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        gradient.AnchorLeft = 0; gradient.AnchorRight = 1;
        gradient.AnchorTop = 0.5f; gradient.AnchorBottom = 1;
        AddChild(gradient);

        // ── Title ──
        float titleH = 48f * _viewportH / 1080f;
        float titleY = 18f * _viewportH / 1080f;
        var title = new Label
        {
            Text = "CHOOSE YOUR PATH",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ApplyHeaderFont(title, FontLarge);
        title.AddThemeColorOverride("font_color", Gold);
        title.AddThemeConstantOverride("outline_size", 2);
        title.AddThemeColorOverride("font_outline_color", Colors.Black);
        title.Position = new Vector2(0, titleY);
        title.Size = new Vector2(_viewportW, titleH);
        AddChild(title);

        // ── Subtitle ──
        float subY = titleY + titleH + 8f * _viewportH / 1080f;
        var subtitle = new Label
        {
            Text = "Each path begins in its own town, with its own tale.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word
        };
        ApplyBodyFont(subtitle, FontBody);
        subtitle.AddThemeColorOverride("font_color", Color.FromHtml("#C8B88A"));
        subtitle.AddThemeColorOverride("font_outline_color", Colors.Black);
        subtitle.AddThemeConstantOverride("outline_size", 1);
        subtitle.Position = new Vector2(40, subY);
        subtitle.Size = new Vector2(_viewportW - 80, 26);
        AddChild(subtitle);

        // ── Carousel container — proportional height ──
        float carouselY = subY + 34f * _viewportH / 1080f;
        float carouselH = _panelFullH + 12f;
        _carouselPanelContainer = new Control
        {
            ClipContents = true,
            Position = new Vector2(0, carouselY),
            Size = new Vector2(_viewportW, carouselH)
        };
        AddChild(_carouselPanelContainer);

        // Build panels
        for (int i = 0; i < _classes.Count; i++)
        {
            var panel = MakeCarouselPanel(_classes[i], i);
            _carouselPanelContainer.AddChild(panel);
            _panelNodes.Add(panel);
            int idx = i;
            panel.GuiInput += (@event) =>
            {
                if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                    SnapToIndex(idx);
            };
        }

        // Arrow buttons (sized from carousel container position, not panel positions)
        float arrowY = carouselY + carouselH / 2f - 22f;
        _leftArrow = new Button
        {
            Text = "\u25C0",
            Flat = true,
            CustomMinimumSize = new Vector2(44, 44),
            Position = new Vector2(8, arrowY)
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
        AddChild(_leftArrow);

        _rightArrow = new Button
        {
            Text = "\u25B6",
            Flat = true,
            CustomMinimumSize = new Vector2(44, 44),
            Position = new Vector2(_viewportW - 52, arrowY)
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
        AddChild(_rightArrow);

        // ── Dot indicators ──
        float dotsY = carouselY + carouselH + 6;
        _dotsArea = new ColorRect
        {
            Color = Colors.Transparent,
            MouseFilter = MouseFilterEnum.Ignore,
            Position = new Vector2(0, dotsY),
            Size = new Vector2(_viewportW, 12)
        };
        AddChild(_dotsArea);

        float dotSpacing = 14f;
        float dotsTotal = (_classes.Count - 1) * dotSpacing;
        float dotsStartX = (_viewportW - dotsTotal) / 2f;
        for (int i = 0; i < _classes.Count; i++)
        {
            var dot = new ColorRect
            {
                Color = i == _selectedIdx ? Gold : TextInactive,
                Size = new Vector2(8, 8),
                Position = new Vector2(dotsStartX + i * dotSpacing, 2),
                MouseFilter = MouseFilterEnum.Ignore
            };
            _dotsArea.AddChild(dot);
            _dotIndicators.Add(dot);
        }

        // ── Core cards area ──
        float coreY = dotsY + 16f * _viewportH / 1080f;
        float coreH = 160f * _viewportH / 1080f;
        _coreCardsArea = new Control();
        _coreCardsArea.Position = new Vector2(0, coreY);
        _coreCardsArea.Size = new Vector2(_viewportW, coreH);
        AddChild(_coreCardsArea);

        // ── BEGIN button ──
        float beginY = coreY + coreH + 4f * _viewportH / 1080f;
        _beginButton = new PanelContainer();
        _beginButton.Position = new Vector2(_viewportW / 2f - 140, beginY);
        _beginButton.Size = new Vector2(280, 46);
        _beginButton.CustomMinimumSize = new Vector2(280, 46);
        _beginButton.MouseDefaultCursorShape = CursorShape.PointingHand;
        _beginButton.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = SurfaceStone,
            BorderColor = Gold,
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        });

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
        AddChild(_beginButton);

        // Initial carousel render
        UpdateCarousel();
        UpdateUI();
        RunVerify();

        // Capture hook
        if (CampaignContext.AutoCaptureScreenshot)
        {
            _captureMode = true;

            // If choose-path specific capture, auto-select Tidecaller for proof
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
                }
                GetTree().Quit(0);
            };
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
        float containerH = _carouselPanelContainer.Size.Y;

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
        var containerGlobal = _carouselPanelContainer.GetRect();

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

        var strataColor = StrataColor(cls.Strata);

        _coreLabel = new Label
        {
            Text = $"CLASS CORE \u2014 four sworn cards every {cls.Name} carries",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        ApplyBodyFont(_coreLabel, FontSmall);
        _coreLabel.AddThemeColorOverride("font_color", TextSecondary);
        _coreLabel.Position = new Vector2(0, 0);
        _coreLabel.Size = new Vector2(_viewportW, 22);
        _coreCardsArea.AddChild(_coreLabel);

        // Show 4 mini cards
        _coreCardRow = new HBoxContainer();
        _coreCardRow.AddThemeConstantOverride("separation", 12);
        float miniW = 120f * _viewportH / 1080f;
        float minisTotal = cls.CoreCardIds.Count * miniW + (cls.CoreCardIds.Count - 1) * 12f;
        _coreCardRow.Position = new Vector2(_viewportW / 2f - minisTotal / 2f, 26);
        _coreCardRow.Size = new Vector2(minisTotal, 90);
        _coreCardsArea.AddChild(_coreCardRow);

        foreach (var cardId in cls.CoreCardIds)
        {
            var def = CardRegistry.Get(cardId);
            if (def == null) continue;

            float miniH = miniW * 152f / 104f;

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
            miniCard.AddChild(content);

            // Mini card art — ExpandMode=IgnoreSize + full anchors
            var miniArt = new TextureRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize
            };
            miniArt.SetAnchorsPreset(LayoutPreset.FullRect);
            string miniArtPath = $"res://content/art/{cardId}.webp";
            if (ResourceLoader.Exists(miniArtPath))
            {
                var tex = ResourceLoader.Load<Texture2D>(miniArtPath);
                if (tex != null)
                    miniArt.Texture = tex;
            }
            else
            {
                miniArt.Modulate = CardArtColors.Parchment;
                GD.Print($"[ART-MISSING] {cardId} (core mini)");
            }
            content.AddChild(miniArt);

            var plate = new CardPlate();
            content.AddChild(plate);
            plate.Setup(def.Name, def.Attack, def.Vigor, def.Strata, miniW, miniH);

            _coreCardRow.AddChild(miniCard);
        }

        // BEGIN button
        _beginLabel.Text = $"BEGIN IN {cls.Town}";
        _beginLabel.AddThemeColorOverride("font_color", Gold);
        _beginButton.Modulate = Colors.White;

        var clickArea = _beginButton.GetChild<Button>(_beginButton.GetChildCount() - 1);
        if (clickArea != null)
            clickArea.Disabled = false;
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
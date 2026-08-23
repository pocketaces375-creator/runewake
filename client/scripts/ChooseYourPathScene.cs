using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// "CHOOSE YOUR PATH" screen — campaign entry point with looping class carousel.
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
    private float _dragOffset; // accumulated offset from dragging

    // Arrow buttons
    private Button _leftArrow;
    private Button _rightArrow;

    // Layout constants (set in _Ready based on viewport)
    private float _panelFullW = 220f;
    private float _panelFullH = 310f;
    private float _centerX;

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
        float w = vp.X;
        float h = vp.Y;

        // Sizing: panels scale with viewport
        _panelFullW = Mathf.Min(240f, w * 0.14f);
        _panelFullH = _panelFullW * 310f / 220f; // ~1.4 aspect
        _centerX = w / 2f;

        // Dark background
        var bg = new ColorRect
        {
            Color = BgDark,
            MouseFilter = MouseFilterEnum.Ignore
        };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Load class data
        LoadClasses();

        // Hero art background
        var heroArt = new TextureRect();
        heroArt.SetAnchorsPreset(LayoutPreset.FullRect);
        heroArt.MouseFilter = MouseFilterEnum.Ignore;
        heroArt.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
        string heroPath = "res://content/art/title/hero_art.png";
        if (ResourceLoader.Exists(heroPath))
            heroArt.Texture = GD.Load<Texture2D>(heroPath);
        else
            GD.Print("[ART-MISSING] title/hero_art.png");
        heroArt.Modulate = new Color(0.62f, 0.62f, 0.62f, 0.75f);
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
        title.Position = new Vector2(0, 20);
        title.Size = new Vector2(w, 48);
        AddChild(title);

        // ── Subtitle ──
        float subY = 72;
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
        subtitle.Size = new Vector2(w - 80, 28);
        AddChild(subtitle);

        // ── Carousel area ──
        float carouselY = subY + 36;
        float carouselH = _panelFullH + 20;
        _carouselPanelContainer = new Control();
        _carouselPanelContainer.Position = new Vector2(0, carouselY);
        _carouselPanelContainer.Size = new Vector2(w, carouselH);
        AddChild(_carouselPanelContainer);

        // Build panels
        for (int i = 0; i < _classes.Count; i++)
        {
            var panel = MakeCarouselPanel(_classes[i], i);
            _carouselPanelContainer.AddChild(panel);
            _panelNodes.Add(panel);
            // Click handler on the panel itself
            int idx = i;
            panel.GuiInput += (@event) =>
            {
                if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                    SnapToIndex(idx);
            };
        }

        // Arrow buttons
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
        _leftArrow.Pressed += () => { _selectedIdx = (_selectedIdx - 1 + _classes.Count) % _classes.Count; UpdateCarousel(); UpdateUI(); };
        AddChild(_leftArrow);

        _rightArrow = new Button
        {
            Text = "\u25B6",
            Flat = true,
            CustomMinimumSize = new Vector2(44, 44),
            Position = new Vector2(w - 52, arrowY)
        };
        _rightArrow.AddThemeFontSizeOverride("font_size", 20);
        _rightArrow.AddThemeColorOverride("font_color", Color.FromHtml("#C8B88A"));
        _rightArrow.Pressed += () => { _selectedIdx = (_selectedIdx + 1) % _classes.Count; UpdateCarousel(); UpdateUI(); };
        AddChild(_rightArrow);

        // ── Dot indicators ──
        float dotsY = carouselY + carouselH + 4;
        _dotsArea = new ColorRect
        {
            Color = Colors.Transparent,
            MouseFilter = MouseFilterEnum.Ignore,
            Position = new Vector2(0, dotsY),
            Size = new Vector2(w, 12)
        };
        AddChild(_dotsArea);

        float dotSpacing = 14f;
        float dotsTotal = (_classes.Count - 1) * dotSpacing;
        float dotsStartX = (w - dotsTotal) / 2f;
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
        float coreY = dotsY + 16;
        float coreH = 120f;
        _coreCardsArea = new Control();
        _coreCardsArea.Position = new Vector2(0, coreY);
        _coreCardsArea.Size = new Vector2(w, coreH);
        AddChild(_coreCardsArea);

        // ── BEGIN button ──
        float beginY = coreY + coreH + 4;
        _beginButton = new PanelContainer();
        _beginButton.Position = new Vector2(w / 2f - 140, beginY);
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

        // Capture hook
        if (CampaignContext.AutoCaptureScreenshot)
        {
            _captureMode = true;
            var timer = GetTree().CreateTimer(0.8f);
            timer.Timeout += () =>
            {
                var image = GetViewport().GetTexture().GetImage();
                if (image != null)
                {
                    string path = CampaignContext.WideCaptureMode
                        ? "/home/fictive/runewake/artifacts/captures/choose_path_wide.png"
                        : "/home/fictive/runewake/artifacts/captures/choose_path.png";
                    image.SavePng(path);
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

        for (int i = 0; i < total; i++)
        {
            var panel = _panelNodes[i];
            // Distance from center (wrapping both directions, take shortest)
            int rawDist = i - _selectedIdx;
            int dist = rawDist;
            if (dist > total / 2) dist -= total;
            else if (dist < -total / 2) dist += total;

            float absDist = Mathf.Abs(dist);

            // Scale: center=1.0, neighbors=0.78, others shrink further
            float scale = 1f - absDist * 0.22f;
            if (scale < 0.5f) scale = 0.5f;

            // Brightness: center=1.0, neighbors=0.55, further dim
            float bright = 1f - absDist * 0.45f;
            if (bright < 0.3f) bright = 0.3f;

            // Z index: center on top
            panel.ZIndex = (int)(total - absDist);

            float panelW = _panelFullW * scale;
            float panelH = _panelFullH * scale;

            // X position: center plus offset for this panel's slot
            float xPos = _centerX - panelW / 2f + dist * spacing;
            if (dist > 0) xPos += overlapMargin; // right neighbors shifted right a bit more
            else if (dist < 0) xPos -= overlapMargin; // left neighbors shifted left

            panel.Position = new Vector2(xPos, _carouselPanelContainer.Size.Y / 2f - panelH / 2f);
            panel.Size = new Vector2(panelW, panelH);
            panel.Scale = Vector2.One;
            panel.Modulate = new Color(bright, bright, bright, 1f);

            // Visible if reasonably on screen
            panel.Visible = xPos + panelW > -50 && xPos < _centerX * 2 + 50;
        }

        // Update dots
        for (int i = 0; i < _dotIndicators.Count; i++)
        {
            _dotIndicators[i].Color = i == _selectedIdx ? Gold : TextInactive;
        }
    }

    private void SnapToIndex(int idx)
    {
        _selectedIdx = idx;
        UpdateCarousel();
        UpdateUI();
    }

    // ════════════════════════════════════════════════
    // Drag handling (GuiInput on carousel area)
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
                    // Snap: if dragged more than 1/4 panel width, advance
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
            float dx = mm.Relative.X;
            _dragOffset += dx;
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
        _coreLabel.Size = new Vector2(GetViewportRect().Size.X, 22);
        _coreCardsArea.AddChild(_coreLabel);

        // Show 4 mini cards
        _coreCardRow = new HBoxContainer();
        _coreCardRow.AddThemeConstantOverride("separation", 10);
        float w = GetViewportRect().Size.X;
        float minisTotal = cls.CoreCardIds.Count * 90f + (cls.CoreCardIds.Count - 1) * 10f;
        _coreCardRow.Position = new Vector2(w / 2f - minisTotal / 2f, 26);
        _coreCardRow.Size = new Vector2(minisTotal, 90);
        _coreCardsArea.AddChild(_coreCardRow);

        foreach (var cardId in cls.CoreCardIds)
        {
            var def = CardRegistry.Get(cardId);
            if (def == null) continue;

            float miniW = 80f;
            float miniH = 80f * 152f / 104f;

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

            // Mini card art
            var miniArt = new TextureRect();
            miniArt.SetAnchorsPreset(LayoutPreset.FullRect);
            miniArt.MouseFilter = MouseFilterEnum.Ignore;
            miniArt.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
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
        var panel = new Control();
        panel.MouseFilter = MouseFilterEnum.Pass;
        panel.MouseDefaultCursorShape = CursorShape.PointingHand;

        // Background with border
        var panelRect = new ColorRect();
        panelRect.SetAnchorsPreset(LayoutPreset.FullRect);
        panelRect.MouseFilter = MouseFilterEnum.Ignore;
        panelRect.Color = new Color(0.12f, 0.10f, 0.08f, 0.95f);
        panel.AddChild(panelRect);

        // Border (gold if selected, strata otherwise)
        var strataColor = StrataColor(cls.Strata);
        var border = new ColorRect
        {
            Color = Colors.Transparent,
            MouseFilter = MouseFilterEnum.Ignore
        };
        border.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.AddChild(border);

        // Art area (upper portion)
        var artRect = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            AnchorLeft = 0, AnchorRight = 1,
            AnchorTop = 0,
            SizeFlagsVertical = (SizeFlags)3
        };
        string artPath = $"res://content/art/classes/{cls.Id}.png";
        if (ResourceLoader.Exists(artPath))
        {
            var tex = ResourceLoader.Load<Texture2D>(artPath);
            if (tex != null)
                artRect.Texture = tex;
        }
        panel.AddChild(artRect);

        // Inner VBox for text content (overlaid on lower portion)
        var vbox = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 0, AnchorRight = 1,
            AnchorBottom = 1,
            OffsetLeft = 6, OffsetRight = -6,
            OffsetBottom = -6
        };
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
        if (_selectedIdx < 0 || _selectedIdx >= _classes.Count) return;
        var cls = _classes[_selectedIdx];

        CampaignContext.ChosenClass = cls.Id;
        CampaignContext.ChosenTown = cls.Town;
        CampaignContext.CoreCardIds = new List<string>(cls.CoreCardIds);

        GetTree().ChangeSceneToFile("res://scenes/deck/DeckBuilderScene.tscn");
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// "CHOOSE YOUR PATH" screen — campaign entry point.
/// Displays class panels with art, blurb, and core cards.
/// Selection feeds into the deck builder with core cards pre-slotted and locked.
/// </summary>
public partial class ChooseYourPathScene : Control
{
    // ── Data ──
    private readonly List<ClassDef> _classes = new();
    private int _selectedIdx = -1;
    private Control _beginButton;
    private Control _classPanelRow;
    private Control _coreCardsArea;
    private Label _beginLabel;

    // Capture
    private bool _captureMode;

    public override void _Ready()
    {
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

        // Hero art background (darkened) — real path: content/art/title/hero_art.png
        var heroArt = new TextureRect();
        heroArt.SetAnchorsPreset(LayoutPreset.FullRect);
        heroArt.MouseFilter = MouseFilterEnum.Ignore;
        heroArt.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
        string heroPath = "res://content/art/title/hero_art.png";
        if (ResourceLoader.Exists(heroPath))
            heroArt.Texture = GD.Load<Texture2D>(heroPath);
        else
            GD.Print("[ART-MISSING] title/hero_art.png");
        heroArt.Modulate = new Color(0.62f, 0.62f, 0.62f, 0.8f); // ~38% darken
        AddChild(heroArt);

        // Dark gradient toward bottom
        var gradient = new ColorRect
        {
            Color = new Color(0.04f, 0.03f, 0.02f, 0.6f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        gradient.AnchorLeft = 0; gradient.AnchorRight = 1;
        gradient.AnchorTop = 0.6f; gradient.AnchorBottom = 1;
        AddChild(gradient);

        // Title
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
        title.Position = new Vector2(0, 30);
        title.Size = new Vector2(GetViewportRect().Size.X, 48);
        AddChild(title);

        // Subtitle
        var subtitle = new Label
        {
            Text = "Each path begins in its own town, with its own tale — all roads may yet cross.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word
        };
        ApplyBodyFont(subtitle, FontBody);
        subtitle.AddThemeColorOverride("font_color", Color.FromHtml("#C8B88A"));
        subtitle.AddThemeColorOverride("font_outline_color", Colors.Black);
        subtitle.AddThemeConstantOverride("outline_size", 1);
        subtitle.Position = new Vector2(60, 80);
        subtitle.Size = new Vector2(GetViewportRect().Size.X - 120, 30);
        AddChild(subtitle);

        // Class panel row
        _classPanelRow = new HBoxContainer();
        _classPanelRow.AddThemeConstantOverride("separation", 24);
        float rowY = 130f;
        _classPanelRow.Position = new Vector2(60, rowY);
        _classPanelRow.Size = new Vector2(GetViewportRect().Size.X - 120, 300);
        AddChild(_classPanelRow);

        for (int i = 0; i < _classes.Count; i++)
        {
            int idx = i;
            var panel = MakeClassPanel(_classes[i]);
            // Add click overlay to the panel
            var clickOverlay = new Button();
            clickOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
            clickOverlay.MouseDefaultCursorShape = CursorShape.PointingHand;
            var transStyle = new StyleBoxFlat { BgColor = Colors.Transparent };
            clickOverlay.AddThemeStyleboxOverride("normal", transStyle);
            clickOverlay.AddThemeStyleboxOverride("hover", transStyle);
            clickOverlay.AddThemeStyleboxOverride("pressed", transStyle);
            clickOverlay.Pressed += () => SelectClass(idx);
            panel.AddChild(clickOverlay);
            _classPanelRow.AddChild(panel);
        }

        // Core cards area
        _coreCardsArea = new Control();
        _coreCardsArea.Position = new Vector2(0, rowY + 310);
        _coreCardsArea.Size = new Vector2(GetViewportRect().Size.X, 120);
        AddChild(_coreCardsArea);

        // Core cards label
        var coreLabel = new Label
        {
            Text = "CLASS CORE — four sworn cards every class carries",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        ApplyBodyFont(coreLabel, FontSmall);
        coreLabel.AddThemeColorOverride("font_color", TextSecondary);
        coreLabel.Position = new Vector2(0, 0);
        coreLabel.Size = new Vector2(GetViewportRect().Size.X, 24);
        _coreCardsArea.AddChild(coreLabel);

        // BEGIN button
        _beginButton = new PanelContainer();
        _beginButton.Position = new Vector2(GetViewportRect().Size.X / 2f - 120, rowY + 440);
        _beginButton.Size = new Vector2(240, 44);
        _beginButton.CustomMinimumSize = new Vector2(240, 44);
        _beginButton.MouseDefaultCursorShape = CursorShape.PointingHand;
        _beginButton.Modulate = new Color(1, 1, 1, 0.4f); // disabled until selection
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
        _beginLabel.AddThemeColorOverride("font_color", TextMuted);
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
        beginClick.Disabled = true; // disabled until selection
        _beginButton.AddChild(beginClick);

        AddChild(_beginButton);

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

    /// <summary>
    /// Create a selectable class panel.
    /// </summary>
    private PanelContainer MakeClassPanel(ClassDef cls)
    {
        var panel = new PanelContainer();
        panel.CustomMinimumSize = new Vector2(280, 280);
        panel.SizeFlagsHorizontal = (SizeFlags)3;
        panel.SizeFlagsVertical = (SizeFlags)3;
        panel.MouseDefaultCursorShape = CursorShape.PointingHand;

        var strataColor = StrataColor(cls.Strata);
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.15f, 0.13f, 0.10f, 0.9f),
            BorderColor = BorderStandard,
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8
        });

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(LayoutPreset.FullRect);
        vbox.OffsetLeft = 8; vbox.OffsetRight = -8;
        vbox.OffsetTop = 8; vbox.OffsetBottom = -8;
        vbox.AddThemeConstantOverride("separation", 6);
        panel.AddChild(vbox);

        // Class portrait art (full-bleed, cover-cropped)
        var classArt = new TextureRect
        {
            CustomMinimumSize = new Vector2(0, 100),
            SizeFlagsHorizontal = (SizeFlags)3,
            SizeFlagsVertical = (SizeFlags)3,
            MouseFilter = MouseFilterEnum.Ignore,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered
        };
        string classArtPath = $"res://content/art/classes/{cls.Id}.png";
        if (ResourceLoader.Exists(classArtPath))
        {
            var tex = ResourceLoader.Load<Texture2D>(classArtPath);
            if (tex != null)
                classArt.Texture = tex;
        }
        else
        {
            // Fallback: strata-colored placeholder
            classArt.Modulate = strataColor.Darkened(0.5f);
            GD.Print($"[ART-MISSING] classes/{cls.Id}.png");
        }
        vbox.AddChild(classArt);

        // Class name
        var nameLabel = new Label
        {
            Text = cls.Name,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        ApplyHeaderFont(nameLabel, FontSubtitle);
        nameLabel.AddThemeColorOverride("font_color", Gold);
        vbox.AddChild(nameLabel);

        // Blurb
        var blurbLabel = new Label
        {
            Text = cls.Blurb,
            AutowrapMode = TextServer.AutowrapMode.Word,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        ApplyBodyFont(blurbLabel, FontSmall);
        blurbLabel.AddThemeColorOverride("font_color", Color.FromHtml("#C8B88A"));
        blurbLabel.CustomMinimumSize = new Vector2(0, 40);
        vbox.AddChild(blurbLabel);

        // Origin line
        var originLabel = new Label
        {
            Text = $"Origin \u00b7 {cls.Town}",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        ApplyBodyFont(originLabel, FontTiny);
        originLabel.AddThemeColorOverride("font_color", TextMuted);
        vbox.AddChild(originLabel);

        return panel;
    }

    private void SelectClass(int idx)
    {
        _selectedIdx = idx;
        var cls = _classes[idx];
        var strataColor = StrataColor(cls.Strata);

        // Update all panels
        int childIdx = 0;
        foreach (var child in _classPanelRow.GetChildren())
        {
            if (child is PanelContainer panel)
            {
                bool selected = childIdx == idx;
                panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
                {
                    BgColor = new Color(0.15f, 0.13f, 0.10f, 0.9f),
                    BorderColor = selected ? Gold : BorderStandard,
                    BorderWidthLeft = selected ? 2 : 1,
                    BorderWidthTop = selected ? 2 : 1,
                    BorderWidthRight = selected ? 2 : 1,
                    BorderWidthBottom = selected ? 2 : 1,
                    CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
                    CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8
                });

                if (selected)
                    panel.Scale = new Vector2(1.05f, 1.05f);
                else
                    panel.Scale = Vector2.One;
            }
            childIdx++;
        }

        // Update core cards area
        foreach (var child in _coreCardsArea.GetChildren())
            child.QueueFree();

        var coreLabel = new Label
        {
            Text = $"CLASS CORE \u2014 four sworn cards every {cls.Name} carries",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        ApplyBodyFont(coreLabel, FontSmall);
        coreLabel.AddThemeColorOverride("font_color", TextSecondary);
        coreLabel.Position = new Vector2(0, 0);
        coreLabel.Size = new Vector2(GetViewportRect().Size.X, 24);
        _coreCardsArea.AddChild(coreLabel);

        // Show 4 mini cards
        var cardRow = new HBoxContainer();
        cardRow.AddThemeConstantOverride("separation", 12);
        cardRow.Position = new Vector2(GetViewportRect().Size.X / 2f - 4 * 55, 28);
        cardRow.Size = new Vector2(8 * 55, 80);
        _coreCardsArea.AddChild(cardRow);

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

            // Mini card art (full-bleed)
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

            cardRow.AddChild(miniCard);
        }

        // Enable BEGIN button
        _beginLabel.Text = $"BEGIN IN {cls.Town}";
        _beginLabel.AddThemeColorOverride("font_color", Gold);
        _beginButton.Modulate = Colors.White;

        // Re-enable the click area
        var clickArea = _beginButton.GetChild<Button>(_beginButton.GetChildCount() - 1);
        if (clickArea != null)
            clickArea.Disabled = false;
    }

    private void OnBegin()
    {
        if (_selectedIdx < 0) return;
        var cls = _classes[_selectedIdx];

        // Save chosen class and core cards to CampaignContext
        CampaignContext.ChosenClass = cls.Id;
        CampaignContext.ChosenTown = cls.Town;
        CampaignContext.CoreCardIds = new List<string>(cls.CoreCardIds);

        // Navigate to deck builder — it reads CampaignContext.CoreCardIds in _Ready
        GetTree().ChangeSceneToFile("res://scenes/deck/DeckBuilderScene.tscn");
    }

    // ── Data types ──

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
}
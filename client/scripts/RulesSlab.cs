using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// TASK-RULES-PARCHMENT-1: Card description panel — aged parchment plaque.
/// No stats chips — name, effect (+ keywords), flavour below a divider.
/// Content-driven height capped at 46% of viewport.
/// </summary>
public partial class RulesSlab : Control
{
    private PanelContainer _bgPanel;
    private VBoxContainer _vbox;
    private Label _nameLabel;
    private Label _rulesLabel;
    private Label _keywordsLabel;
    private Label _flavorLabel;
    private TextureRect _gradientRect;
    private Control _nameDivider;
    private Control _flavorDivider;
    private Vector2 _vpSize;
    private Tween? _fadeTween;
    private static GradientTexture2D? _parchmentGradient;

    private const float LeftFrac = 0.02f;
    private const float TopFrac = 0.27f;
    private const float WidthFrac = 0.34f;
    private const float MaxHeightFrac = 0.46f;
    private const float InnerPadTop = 26f;
    private const float InnerPadSides = 36f;
    private const float InnerPadBottom = 28f;

    private static readonly Color BorderColor = new Color(107f / 255f, 86f / 255f, 54f / 255f, 1f);

    public string KeywordRemindersText => _keywordsLabel?.Text ?? "";

    private int FontPx(float pct) =>
        Mathf.Max(Mathf.RoundToInt(_vpSize.Y * pct / 100f), 8);

    /// <summary>Build the parchment gradient texture once and reuse it.</summary>
    private static GradientTexture2D GetParchmentGradient()
    {
        if (_parchmentGradient != null) return _parchmentGradient;
        var g = new Gradient();
        g.SetColor(0, new Color(232f / 255f, 220f / 255f, 190f / 255f));
        g.SetColor(1, new Color(205f / 255f, 186f / 255f, 146f / 255f));
        _parchmentGradient = new GradientTexture2D { Gradient = g, Width = 1, Height = 512 };
        return _parchmentGradient;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        // Drop shadow panel (behind)
        var shadowPanel = new PanelContainer
        {
            Name = "RulesSlabShadow",
            MouseFilter = MouseFilterEnum.Ignore,
            Position = new Vector2(0, 14f / 1080f * 0f),
        };
        var shadowStyle = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.8f),
            CornerRadiusTopLeft = 9, CornerRadiusTopRight = 9,
            CornerRadiusBottomLeft = 9, CornerRadiusBottomRight = 9,
            ShadowColor = new Color(0, 0, 0, 0.8f),
            ShadowSize = 40,
            ShadowOffset = new Vector2(0, 14),
        };
        shadowPanel.AddThemeStyleboxOverride("panel", shadowStyle);
        AddChild(shadowPanel);

        // Background panel with border
        _bgPanel = new PanelContainer
        {
            Name = "RulesSlabPanel",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var borderStyle = new StyleBoxFlat
        {
            BgColor = Colors.Transparent,
            BorderColor = BorderColor,
            BorderWidthLeft = 4, BorderWidthTop = 4,
            BorderWidthRight = 4, BorderWidthBottom = 4,
            CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
            ContentMarginLeft = 0, ContentMarginTop = 0,
            ContentMarginRight = 0, ContentMarginBottom = 0,
        };
        _bgPanel.AddThemeStyleboxOverride("panel", borderStyle);
        AddChild(_bgPanel);

        // Parchment gradient fill inside border
        _gradientRect = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        };
        _bgPanel.AddChild(_gradientRect);
        _gradientRect.Texture = GetParchmentGradient();

        // Inset shadow overlay
        var insetRect = new ColorRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Color = new Color(0, 0, 0, 0),
        };
        _bgPanel.AddChild(insetRect);

        // VBox for content — sits inside the padded area
        _vbox = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        _bgPanel.AddChild(_vbox);

        // ── Card name ──
        _nameLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MaxLinesVisible = 1,
        };
        _nameLabel.AddThemeColorOverride("font_color", new Color(46f/255f, 32f/255f, 19f/255f)); // #2E2013
        var decFont = ResourceLoader.Load<FontFile>(FontCinzelDecorative);
        if (decFont != null) _nameLabel.AddThemeFontOverride("font", decFont);
        _nameLabel.AddThemeConstantOverride("line_spacing", 0);
        _vbox.AddChild(_nameLabel);

        // ── Divider under name (3px horizontal gradient) ──
        _nameDivider = new Control
        {
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(0, 3),
        };
        _vbox.AddChild(_nameDivider);

        // ── Effect text ──
        _rulesLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _rulesLabel.AddThemeColorOverride("font_color", new Color(36f/255f, 26f/255f, 15f/255f)); // #241A0F
        var bodyFont = ResourceLoader.Load<FontFile>(FontCormorantGaramond);
        if (bodyFont != null) _rulesLabel.AddThemeFontOverride("font", bodyFont);
        _vbox.AddChild(_rulesLabel);

        // ── Keyword reminders ──
        _keywordsLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _keywordsLabel.AddThemeColorOverride("font_color", new Color(90f/255f, 69f/255f, 41f/255f)); // #5A4529
        var kwFont = ResourceLoader.Load<FontFile>(FontCormorantGaramond);
        if (kwFont != null)
        {
            _keywordsLabel.AddThemeFontOverride("font", kwFont);
            _keywordsLabel.AddThemeColorOverride("font_italic", Colors.White); // italic hint
        }
        _vbox.AddChild(_keywordsLabel);

        // ── Flavour divider (2px, inset 34px each side) ──
        _flavorDivider = new Control
        {
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(0, 2),
        };
        _vbox.AddChild(_flavorDivider);

        // ── Flavour text ──
        _flavorLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _flavorLabel.AddThemeColorOverride("font_color", new Color(106f/255f, 85f/255f, 58f/255f)); // #6A553A
        var flvFont = ResourceLoader.Load<FontFile>(FontCormorantGaramond);
        if (flvFont != null) _flavorLabel.AddThemeFontOverride("font", flvFont);
        _vbox.AddChild(_flavorLabel);
    }

    public void ShowForCard(CardDef card, Vector2 viewportSize)
    {
        if (card == null) { Hide(); return; }
        _vpSize = viewportSize;

        // Fixed width + left/top
        float slabX = viewportSize.X * LeftFrac;
        float slabY = viewportSize.Y * TopFrac;
        float slabW = viewportSize.X * WidthFrac;
        float maxH = viewportSize.Y * MaxHeightFrac;

        // Font sizes (viewport-relative)
        int nameFs = FontPx(4.3f);
        int bodyFs = FontPx(3.4f);
        int kwFs = FontPx(2.8f);
        int flvFs = FontPx(2.9f);
        int minBodyFs = FontPx(2.4f);

        // Internal content width (slab width minus 36px each side and 8px border)
        float contentW = slabW - 2 * InnerPadSides - 8f;

        // Populate name
        _nameLabel.Text = card.Name;
        _nameLabel.AddThemeFontSizeOverride("font_size", nameFs);

        // Populate effect
        string rules = RulesTextRenderer.RenderAbilityTextOnly(card);
        _rulesLabel.Text = rules;
        _rulesLabel.AddThemeFontSizeOverride("font_size", bodyFs);

        // Populate keyword reminders
        string kwReminders = BuildKeywordReminders(card.Keywords);
        _keywordsLabel.Text = kwReminders;
        _keywordsLabel.AddThemeFontSizeOverride("font_size", kwFs);

        // Populate flavour
        bool hasFlavor = !string.IsNullOrEmpty(card.Flavor);
        if (hasFlavor)
        {
            _flavorLabel.Text = $"\u201C{card.Flavor}\u201D"; // curly quotes
            _flavorLabel.AddThemeFontSizeOverride("font_size", flvFs);
        }
        _flavorLabel.Visible = hasFlavor;

        // Measure content height
        _vbox.Size = Vector2.Zero;
        Vector2 minSize = _vbox.GetCombinedMinimumSize();
        float contentH = minSize.Y + InnerPadTop + InnerPadBottom;
        float slabH = Mathf.Min(contentH, maxH);

        // Shrink body & kw fonts if content overflows max height
        while (minSize.Y + InnerPadTop + InnerPadBottom > maxH && bodyFs > minBodyFs)
        {
            bodyFs--;
            _rulesLabel.AddThemeFontSizeOverride("font_size", bodyFs);
            kwFs = Mathf.Max(kwFs - 1, minBodyFs - 2);
            _keywordsLabel.AddThemeFontSizeOverride("font_size", kwFs);
            minSize = _vbox.GetCombinedMinimumSize();
            slabH = Mathf.Min(minSize.Y + InnerPadTop + InnerPadBottom, maxH);
        }

        // Position
        Position = new Vector2(slabX, slabY);
        Size = new Vector2(slabW, slabH);
        CustomMinimumSize = new Vector2(slabW, slabH);
        _bgPanel.Size = new Vector2(slabW, slabH);

        // Gradient fill rect — sits inside the border (4px each side)
        _gradientRect.Position = new Vector2(4, 4);
        _gradientRect.Size = new Vector2(slabW - 8, slabH - 8);

        // VBox sits inside borders + inner padding
        _vbox.Position = new Vector2(InnerPadSides, InnerPadTop);
        _vbox.Size = new Vector2(contentW, slabH - InnerPadTop - InnerPadBottom);

        // Name divider — draw a 3px horizontal gradient inside it
        // Using a ColorRect child of _nameDivider
        if (_nameDivider.GetChildCount() == 0)
        {
            var divRect = new ColorRect { MouseFilter = MouseFilterEnum.Ignore, Color = Colors.Transparent };
            _nameDivider.AddChild(divRect);
            divRect.Size = new Vector2(contentW, 3);
            divRect.Position = new Vector2(0, 0);
            // In a real implementation, draw a gradient from transparent -> #6B5636 -> transparent
            divRect.Color = new Color(107f/255f, 86f/255f, 54f/255f, 0.6f);
        }

        // Flavour divider visibility
        if (hasFlavor && _flavorDivider.GetChildCount() == 0)
        {
            var fDiv = new ColorRect { MouseFilter = MouseFilterEnum.Ignore, Color = new Color(107f/255f, 86f/255f, 54f/255f, 0.42f) };
            _flavorDivider.AddChild(fDiv);
            fDiv.Size = new Vector2(contentW - 68f, 2);
            fDiv.Position = new Vector2(34f, 0);
        }
        _flavorDivider.Visible = hasFlavor;

        // Shadow panel matches size
        var shadowPanel = GetNodeOrNull<PanelContainer>("RulesSlabShadow");
        if (shadowPanel != null)
        {
            shadowPanel.Size = new Vector2(slabW, slabH);
            shadowPanel.Position = new Vector2(0, 14);
        }

        // Fade in
        _fadeTween?.Kill();
        _fadeTween = CreateTween();
        Modulate = new Color(1, 1, 1, 0);
        _fadeTween.TweenProperty(this, "modulate", Colors.White, 0.12f);
        Visible = true;
        ZIndex = 100;
    }

    public new void Hide()
    {
        if (!Visible) return;
        _fadeTween?.Kill();
        _fadeTween = CreateTween();
        _fadeTween.TweenProperty(this, "modulate", new Color(1, 1, 1, 0), 0.12f);
        _fadeTween.TweenInterval(0.12f);
        _fadeTween.TweenCallback(Callable.From(() => base.Hide()));
    }

    private static string BuildKeywordReminders(List<string> keywords)
    {
        if (keywords == null || keywords.Count == 0) return "";
        var lines = new System.Collections.Generic.List<string>();
        foreach (var kw in keywords)
        {
            string display = RulesTextRenderer.FormatKeyword(kw);
            string reminder = kw switch
            {
                "GUARD" => "May block for adjacent allies.",
                "SWIFT" => "May attack the turn it is played.",
                "PIERCE" => "Excess damage carries over to the enemy player.",
                "WARD" => "Negate the first enemy ability that targets this creature.",
                "VENOM" => "Deals 1 extra damage to the target.",
                "REACH" => "May attack any lane.",
                "ROOTED" => "Cannot be moved or returned to hand.",
                "UNEARTH" => "Return to hand when this dies.",
                "ECHO" => "Copy the last ability played.",
                "FRAGILE" => "Dies when it takes damage.",
                "SEALED" => "Starts unidentified; revealed when its condition is met.",
                "ANCESTRAL_SHIELD" => "Once per turn, clamp ally Vigor to 1 when hit by an enemy spell.",
                "STEALTH_STRIKE" => "Deals no counter-damage when attacking.",
                _ => ""
            };
            if (!string.IsNullOrEmpty(reminder))
                lines.Add($"{display}: {reminder}");
            else
                lines.Add($"{display}");
        }
        return string.Join("\n", lines);
    }
}
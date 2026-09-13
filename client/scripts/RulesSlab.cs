using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// TASK-SLAB-HOLD-1: Description plaque — ONE fixed size, hold-to-peek.
/// No content-driven height, no font-shrink loop.
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

    // Fixed geometry — identical for every card
    private const float LeftFrac = 0.02f;
    private const float TopFrac = 0.27f;
    private const float WidthFrac = 0.34f;
    private const float HeightFrac = 0.40f;
    private const float InnerPadTop = 26f;
    private const float InnerPadSides = 36f;
    private const float InnerPadBottom = 28f;

    // Fixed typography at 1080p reference, scaled by vh/1080
    private const float NameSize1080 = 46f;
    private const float BodySize1080 = 37f;
    private const float KwSize1080 = 30f;
    private const float FlavorSize1080 = 31f;
    private const float FlavorMin1080 = 26f;
    private const float KwMin1080 = 24f;
    private const float RefVh = 1080f;

    private static readonly Color BorderColor = new Color(107f / 255f, 86f / 255f, 54f / 255f, 1f);

    public string KeywordRemindersText => _keywordsLabel?.Text ?? "";

    private int ScalePx(float px) =>
        Mathf.Max(Mathf.RoundToInt(_vpSize.Y * px / RefVh), 6);

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

        var shadowPanel = new PanelContainer
        {
            Name = "RulesSlabShadow",
            MouseFilter = MouseFilterEnum.Ignore,
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

        _gradientRect = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        };
        _bgPanel.AddChild(_gradientRect);
        _gradientRect.Texture = GetParchmentGradient();

        _vbox = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
        _bgPanel.AddChild(_vbox);

        _nameLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MaxLinesVisible = 1,
        };
        _nameLabel.AddThemeColorOverride("font_color", new Color(46f/255f, 32f/255f, 19f/255f));
        var decFont = ResourceLoader.Load<FontFile>(FontCinzelDecorative);
        if (decFont != null) _nameLabel.AddThemeFontOverride("font", decFont);
        _vbox.AddChild(_nameLabel);

        _nameDivider = new Control
        {
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(0, 3),
        };
        _vbox.AddChild(_nameDivider);

        _rulesLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _rulesLabel.AddThemeColorOverride("font_color", new Color(36f/255f, 26f/255f, 15f/255f));
        var bodyFont = ResourceLoader.Load<FontFile>(FontCormorantGaramond);
        if (bodyFont != null) _rulesLabel.AddThemeFontOverride("font", bodyFont);
        _vbox.AddChild(_rulesLabel);

        _keywordsLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _keywordsLabel.AddThemeColorOverride("font_color", new Color(90f/255f, 69f/255f, 41f/255f));
        var kwFont = ResourceLoader.Load<FontFile>(FontCormorantGaramond);
        if (kwFont != null)
        {
            _keywordsLabel.AddThemeFontOverride("font", kwFont);
            _keywordsLabel.AddThemeColorOverride("font_italic", Colors.White);
        }
        _vbox.AddChild(_keywordsLabel);

        _flavorDivider = new Control
        {
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(0, 2),
        };
        _vbox.AddChild(_flavorDivider);

        _flavorLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _flavorLabel.AddThemeColorOverride("font_color", new Color(106f/255f, 85f/255f, 58f/255f));
        var flvFont = ResourceLoader.Load<FontFile>(FontCormorantGaramond);
        if (flvFont != null) _flavorLabel.AddThemeFontOverride("font", flvFont);
        _vbox.AddChild(_flavorLabel);
    }

    public void ShowForCard(CardDef card, Vector2 viewportSize)
    {
        if (card == null) { Hide(); return; }
        _vpSize = viewportSize;

        float slabX = viewportSize.X * LeftFrac;
        float slabY = viewportSize.Y * TopFrac;
        float slabW = viewportSize.X * WidthFrac;
        float slabH = viewportSize.Y * HeightFrac;
        float padTop = ScalePx(InnerPadTop);
        float padSide = ScalePx(InnerPadSides);
        float contentW = slabW - 2 * padSide - 8f;

        int nameFs = ScalePx(NameSize1080);
        int bodyFs = ScalePx(BodySize1080);
        int kwFs = ScalePx(KwSize1080);
        int flvFs = ScalePx(FlavorSize1080);

        _nameLabel.Text = card.Name;
        _nameLabel.AddThemeFontSizeOverride("font_size", nameFs);

        string rules = RulesTextRenderer.RenderAbilityTextOnly(card);
        _rulesLabel.Text = rules;
        _rulesLabel.AddThemeFontSizeOverride("font_size", bodyFs);

        string kwReminders = BuildKeywordReminders(card.Keywords);
        _keywordsLabel.Text = kwReminders;
        _keywordsLabel.AddThemeFontSizeOverride("font_size", kwFs);

        bool hasFlavor = !string.IsNullOrEmpty(card.Flavor);
        if (hasFlavor)
        {
            _flavorLabel.Text = $"\u201C{card.Flavor}\u201D";
            _flavorLabel.AddThemeFontSizeOverride("font_size", flvFs);
        }
        _flavorLabel.Visible = hasFlavor;
        _flavorDivider.Visible = hasFlavor;

        // Check overflow and shrink flavour first, then keywords
        _vbox.Size = Vector2.Zero;
        Vector2 minSize = _vbox.GetCombinedMinimumSize();
        float availH = slabH - padTop - ScalePx(InnerPadBottom);
        int flvMin = ScalePx(FlavorMin1080);
        int kwMin = ScalePx(KwMin1080);
        if (minSize.Y > availH)
        {
            GD.Print($"[SLAB] overflow {card.Id}");
            while (minSize.Y > availH && flvFs > flvMin && hasFlavor)
            {
                flvFs--;
                _flavorLabel.AddThemeFontSizeOverride("font_size", flvFs);
                minSize = _vbox.GetCombinedMinimumSize();
            }
            while (minSize.Y > availH && kwFs > kwMin && !string.IsNullOrEmpty(kwReminders))
            {
                kwFs--;
                _keywordsLabel.AddThemeFontSizeOverride("font_size", kwFs);
                minSize = _vbox.GetCombinedMinimumSize();
            }
        }

        Position = new Vector2(slabX, slabY);
        Size = new Vector2(slabW, slabH);
        CustomMinimumSize = new Vector2(slabW, slabH);
        _bgPanel.Size = new Vector2(slabW, slabH);

        _gradientRect.Position = new Vector2(4, 4);
        _gradientRect.Size = new Vector2(slabW - 8, slabH - 8);

        _vbox.Position = new Vector2(padSide, padTop);
        _vbox.Size = new Vector2(contentW, availH);

        if (_nameDivider.GetChildCount() == 0)
        {
            var divRect = new ColorRect { MouseFilter = MouseFilterEnum.Ignore, Color = new Color(107f/255f, 86f/255f, 54f/255f, 0.6f) };
            _nameDivider.AddChild(divRect);
            divRect.Size = new Vector2(contentW, 3);
        }

        if (hasFlavor && _flavorDivider.GetChildCount() == 0)
        {
            var fDiv = new ColorRect { MouseFilter = MouseFilterEnum.Ignore, Color = new Color(107f/255f, 86f/255f, 54f/255f, 0.42f) };
            _flavorDivider.AddChild(fDiv);
            fDiv.Size = new Vector2(contentW - 68f, 2);
            fDiv.Position = new Vector2(34f, 0);
        }

        var shadowPanel = GetNodeOrNull<PanelContainer>("RulesSlabShadow");
        if (shadowPanel != null)
        {
            shadowPanel.Size = new Vector2(slabW, slabH);
            shadowPanel.Position = new Vector2(0, 14);
        }

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
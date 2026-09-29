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
    
    private const float InnerPadTop = 26f;
    private const float InnerPadSides = 36f;
    private const float InnerPadBottom = 28f;

    // Fixed typography at 1080p reference, scaled by vh/1080
    // FABLE-048: bigger and the SAME for every card — Trikzos: "small and hard to read … much more
    // uniform as far as presentation; these look a little wonky from one another on shapes and sizes".
    private const float NameSize1080 = 36f;
    private const float BodySize1080 = 35f;
    private const float KwSize1080 = 27f;
    private const float FlavorSize1080 = 28f;
    private const float FlavorMin1080 = 21f;
    private const float KwMin1080 = 21f;
    private const float BodyMin1080 = 26f;
    private const float SlabW1080 = 520f;
    private const float SlabH1080 = 470f;
    private const float RefVh = 1080f;

    private static readonly Color BorderColor = new Color(107f / 255f, 86f / 255f, 54f / 255f, 1f);

    // FABLE-031: the slab is dark glass like the tutorial boxes, not parchment. Two palettes:
    // gold for cards, violet for Artifacts — Trikzos: "something to distinguish the importance
    // of artifacts".
    private sealed record Palette(Color Rim, Color Heading, Color Kicker, Color Body, Color Reminder, Color Flavor, Color Face);
    private static readonly Palette CardPalette = new(
        Rim: new Color(0.80f, 0.60f, 0.20f, 0.85f), Heading: new Color(0.93f, 0.78f, 0.36f), Kicker: new Color(0.72f, 0.62f, 0.38f),
        Body: new Color(0.95f, 0.92f, 0.86f), Reminder: new Color(0.78f, 0.74f, 0.64f), Flavor: new Color(0.66f, 0.62f, 0.54f),
        Face: new Color(0.06f, 0.06f, 0.15f, 0.95f));
    private static readonly Palette ArtifactPalette = new(
        Rim: new Color(0.62f, 0.42f, 0.92f, 0.90f), Heading: new Color(0.80f, 0.66f, 1.0f), Kicker: new Color(0.62f, 0.52f, 0.82f),
        Body: new Color(0.93f, 0.90f, 0.98f), Reminder: new Color(0.76f, 0.70f, 0.88f), Flavor: new Color(0.64f, 0.58f, 0.78f),
        Face: new Color(0.08f, 0.05f, 0.14f, 0.95f));
    private Label _kickerLabel = default!;
    private StyleBoxFlat _faceStyle = default!;

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
        _faceStyle = new StyleBoxFlat
        {
            BgColor = CardPalette.Face,
            BorderColor = CardPalette.Rim,
            BorderWidthLeft = 3, BorderWidthTop = 3,
            BorderWidthRight = 3, BorderWidthBottom = 3,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 0, ContentMarginTop = 0,
            ContentMarginRight = 0, ContentMarginBottom = 0,
            AntiAliasing = true,
        };
        _bgPanel.AddThemeStyleboxOverride("panel", _faceStyle);
        AddChild(_bgPanel);

        // Kept for layout compatibility (the parchment is gone; this rect is now invisible).
        _gradientRect = new TextureRect { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _bgPanel.AddChild(_gradientRect);

        _vbox = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            SizeFlagsVertical = Control.SizeFlags.Fill,   // FABLE-048: fill the fixed box so the flavour sits at its foot
        };
        _bgPanel.AddChild(_vbox);

        _kickerLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            Text = "",
        };
        var kickFont = ThemeTokens.GetHeaderFont(18);
        if (kickFont != null) _kickerLabel.AddThemeFontOverride("font", kickFont);
        _vbox.AddChild(_kickerLabel);

        _nameLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MaxLinesVisible = 1,
        };
        _nameLabel.AddThemeColorOverride("font_color", CardPalette.Heading);
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
        _rulesLabel.AddThemeColorOverride("font_color", CardPalette.Body);
        var bodyFont = ResourceLoader.Load<FontFile>(FontCormorantGaramond);
        if (bodyFont != null) _rulesLabel.AddThemeFontOverride("font", bodyFont);
        _vbox.AddChild(_rulesLabel);

        _keywordsLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _keywordsLabel.AddThemeColorOverride("font_color", CardPalette.Reminder);
        var kwFont = ResourceLoader.Load<FontFile>(FontCormorantGaramond);
        if (kwFont != null)
        {
            _keywordsLabel.AddThemeFontOverride("font", kwFont);
            _keywordsLabel.AddThemeColorOverride("font_italic", Colors.White);
        }
        _vbox.AddChild(_keywordsLabel);

        // FABLE-048: pushes the flavour to the foot of the (fixed-size) box
        _vbox.AddChild(new Control { Name = "FlavorSpacer", MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ExpandFill });

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
        _flavorLabel.AddThemeColorOverride("font_color", CardPalette.Flavor);
        var flvFont = ResourceLoader.Load<FontFile>(FontCormorantGaramond);
        if (flvFont != null) _flavorLabel.AddThemeFontOverride("font", flvFont);
        _vbox.AddChild(_flavorLabel);
    }

    /// <summary>Height the column needs at width <paramref name="w"/>, from the fonts (not from layout).</summary>
    private float MeasureColumn(float w)
    {
        float sep = _vbox.GetThemeConstant("separation");
        float total = 0f; int n = 0;
        foreach (var node in _vbox.GetChildren())
        {
            if (node is not Control c || !c.Visible) continue;
            n++;
            if (c is Label l && !string.IsNullOrEmpty(l.Text))
            {
                var f = l.GetThemeFont("font");
                int fs = l.GetThemeFontSize("font_size");
                float lh = f.GetHeight(fs) + l.GetThemeConstant("line_spacing");
                int lines = l.AutowrapMode == TextServer.AutowrapMode.Off ? 1
                    : Mathf.Max(1, Mathf.RoundToInt(f.GetMultilineStringSize(l.Text, HorizontalAlignment.Left, w, fs).Y / f.GetHeight(fs)));
                total += lines * lh;
            }
            else total += c.CustomMinimumSize.Y;
        }
        return total + sep * Mathf.Max(0, n - 1);
    }

    public void ShowForCard(CardDef card, Vector2 viewportSize)
    {
        if (card == null) { Hide(); return; }
        _vpSize = viewportSize;

        float k = viewportSize.Y / RefVh;
        float slabW = SlabW1080 * k;
        float slabH = SlabH1080 * k;
        float slabX = 24f * k;
        float slabY = Mathf.Round((viewportSize.Y - slabH) / 2f);   // FABLE-048: centred on the left edge, between the artifact pairs
        float padTop = ScalePx(InnerPadTop);
        float padSide = ScalePx(InnerPadSides);
        float contentW = slabW - 2 * padSide - 8f;

        int nameFs = ScalePx(NameSize1080);
        // C1: autofit name to inner width
        float innerW = slabW - 2f * ScalePx(InnerPadSides);
        _nameLabel.AddThemeFontSizeOverride("font_size", nameFs);
        _nameLabel.Text = card.Name;
        while (_nameLabel.GetCombinedMinimumSize().X > innerW && nameFs > ScalePx(28f))
        {
            nameFs -= ScalePx(2f);
            _nameLabel.AddThemeFontSizeOverride("font_size", nameFs);
        }
        int bodyFs = ScalePx(BodySize1080);
        int kwFs = ScalePx(KwSize1080);
        int flvFs = ScalePx(FlavorSize1080);

        _nameLabel.Text = card.Name;
        _nameLabel.AddThemeFontSizeOverride("font_size", nameFs);

        // FABLE-031: violet for Artifacts, gold for everything else.
        bool isArtifact = card.Type == CardType.ARTIFACT;
        var pal = isArtifact ? ArtifactPalette : CardPalette;
        _faceStyle.BgColor = pal.Face;
        _faceStyle.BorderColor = pal.Rim;
        _nameLabel.AddThemeColorOverride("font_color", pal.Heading);
        _rulesLabel.AddThemeColorOverride("font_color", pal.Body);
        _keywordsLabel.AddThemeColorOverride("font_color", pal.Reminder);
        _flavorLabel.AddThemeColorOverride("font_color", pal.Flavor);
        _kickerLabel.AddThemeColorOverride("font_color", pal.Kicker);
        _kickerLabel.AddThemeFontSizeOverride("font_size", ScalePx(20f));
        _kickerLabel.Text = isArtifact ? "ARTIFACT" : card.Type switch
        {
            CardType.RITUAL => "RITUAL", CardType.RELIC => "RELIC", _ => "CREATURE",
        };

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

        foreach (var l in new[] { _rulesLabel, _keywordsLabel, _flavorLabel, _kickerLabel, _nameLabel })
            l.CustomMinimumSize = new Vector2(contentW, 0);
        // Check overflow and shrink flavour first, then keywords
        // FABLE-048: measure the text itself at the column width. The VBox minimum size used to report
        // ~780px for a two-line card (autowrap labels measured before they had a width), so every card
        // was shrunk to the floor sizes.
        Vector2 minSize = new Vector2(contentW, MeasureColumn(contentW));
        float availH = slabH - padTop - ScalePx(InnerPadBottom);
        int flvMin = ScalePx(FlavorMin1080);
        int kwMin = ScalePx(KwMin1080);
        if (minSize.Y > availH)
        {
            GD.Print($"[SLAB] overflow {card.Id} need={minSize.Y:0} avail={availH:0}");
            // FABLE-048: shrink the three text tiers TOGETHER so a wordy card keeps the same proportions
            // as every other card (it used to crush flavour and keywords to the floor while the body stayed big).
            int bodyMin = ScalePx(BodyMin1080);
            while (minSize.Y > availH && (bodyFs > bodyMin || kwFs > kwMin || flvFs > flvMin))
            {
                if (bodyFs > bodyMin) bodyFs--;
                if (kwFs > kwMin) kwFs--;
                if (flvFs > flvMin) flvFs--;
                _rulesLabel.AddThemeFontSizeOverride("font_size", bodyFs);
                _keywordsLabel.AddThemeFontSizeOverride("font_size", kwFs);
                _flavorLabel.AddThemeFontSizeOverride("font_size", flvFs);
                minSize.Y = MeasureColumn(contentW);
            }
        }

        Position = new Vector2(slabX, slabY);
        Size = new Vector2(slabW, slabH);
        CustomMinimumSize = new Vector2(slabW, slabH);
        // C1: height fits content, measured per-line
        float H(Label l) => l.Visible ? l.GetLineCount() * l.GetLineHeight() : 0f;
        float ruleH = _rulesLabel.Visible ? _rulesLabel.GetLineHeight() * 0.5f : 0f;
        float kwGap = _keywordsLabel.Visible ? 4f : 0f;
        float flavorRuleH = _flavorLabel.Visible ? _rulesLabel.GetLineHeight() * 0.5f : 0f;
        float flavorGap = _flavorLabel.Visible ? 4f : 0f;
        float contentH = H(_nameLabel) + ruleH + H(_rulesLabel) + kwGap + H(_keywordsLabel) + flavorRuleH + flavorGap + H(_flavorLabel);
        float minH = 160f * (viewportSize.Y / RefVh);
        // FABLE-048: every card gets the same box — no more hugging the content
        float actualH = slabH;
        Size = new Vector2(slabW, actualH);
        CustomMinimumSize = new Vector2(slabW, actualH);
        // FABLE-031: the box hugs its content (it used to be the full 440px whatever it held).
        _bgPanel.Size = new Vector2(slabW, actualH);

        _gradientRect.Position = new Vector2(4, 4);
        _gradientRect.Size = new Vector2(slabW - 8, slabH - 8);

        // FABLE-048: the panel lays the column out — give it the padding as content margins
        _faceStyle.ContentMarginLeft = padSide; _faceStyle.ContentMarginRight = padSide;
        _faceStyle.ContentMarginTop = padTop; _faceStyle.ContentMarginBottom = ScalePx(InnerPadBottom);
        _vbox.Position = new Vector2(padSide, padTop);
        _vbox.Size = new Vector2(contentW, availH);
        // FABLE-031: wrap at the slab's width, not at the longest word (an artifact's short lines
        // used to collapse the column to a few characters wide).
        foreach (var l in new[] { _rulesLabel, _keywordsLabel, _flavorLabel, _kickerLabel, _nameLabel })
            l.CustomMinimumSize = new Vector2(contentW, 0);

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
            shadowPanel.Size = new Vector2(slabW, actualH);   // FABLE-031: shadow hugs the box too
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
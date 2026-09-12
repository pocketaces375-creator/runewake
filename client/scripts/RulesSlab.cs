using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// TASK-RULES-SLAB-SYSTEM-1: Rules slab — ONE fixed rect, ONE gesture (long-press).
/// Content shrinks to fit the box; box position is always identical.
/// </summary>
public partial class RulesSlab : Control
{
    private PanelContainer _rootPanel;
    private VBoxContainer _vbox;
    private Label _nameLabel;
    private Label _costChip;
    private Label _attackChip;
    private Label _vigorChip;
    private Label _rulesLabel;
    private Label _keywordsLabel;
    private Control _statRow;
    private Vector2 _vpSize;
    private Tween? _fadeTween;

    private const float LeftFrac = 0.02f;
    private const float TopFrac = 0.30f;
    private const float WidthFrac = 0.34f;
    private const float HeightFrac = 0.26f;

    public string KeywordRemindersText => _keywordsLabel?.Text ?? "";

    private int FontPx(float pct) =>
        Mathf.Max(Mathf.RoundToInt(_vpSize.Y * pct / 100f), 8);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        _rootPanel = new PanelContainer
        {
            Name = "RulesSlabPanel",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_rootPanel);

        var slabStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.22f, 0.19f, 0.16f, 0.96f),
            BorderColor = new Color(0.35f, 0.30f, 0.25f, 1.0f),
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 12, ContentMarginTop = 8,
            ContentMarginRight = 12, ContentMarginBottom = 8,
        };
        _rootPanel.AddThemeStyleboxOverride("panel", slabStyle);

        var rootBound = new RootBoundBorder { Name = "RulesSlabBorder" };
        _rootPanel.AddChild(rootBound);

        _vbox = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        _rootPanel.AddChild(_vbox);

        _nameLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MaxLinesVisible = 1,
        };
        _nameLabel.AddThemeColorOverride("font_color", FrameNameText);
        // Cinzel Decorative for the card name in the slab
        var decFont = ResourceLoader.Load<FontFile>(FontCinzelDecorative);
        if (decFont != null)
            _nameLabel.AddThemeFontOverride("font", decFont);
        _vbox.AddChild(_nameLabel);

        _statRow = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        _vbox.AddChild(_statRow);

        _costChip = MakeInfoChip("0");
        _attackChip = MakeInfoChip("0");
        _vigorChip = MakeInfoChip("0");
        _statRow.AddChild(_costChip);
        _statRow.AddChild(_attackChip);
        _statRow.AddChild(_vigorChip);

        _rulesLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _rulesLabel.AddThemeColorOverride("font_color", TextPrimary);
        _vbox.AddChild(_rulesLabel);

        _keywordsLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _keywordsLabel.AddThemeColorOverride("font_color", TextMuted);
        _vbox.AddChild(_keywordsLabel);
    }

    public void ShowForCard(CardDef card, Vector2 viewportSize)
    {
        if (card == null) { Hide(); return; }
        _vpSize = viewportSize;

        // Fixed rect — identical for every card
        float slabX = viewportSize.X * LeftFrac;
        float slabY = viewportSize.Y * TopFrac;
        float slabW = viewportSize.X * WidthFrac;
        float slabH = viewportSize.Y * HeightFrac;

        // Set starting font sizes (viewport-relative, from TASK-RULES-READABLE-1)
        int nameFs = FontPx(3.0f);
        int chipFs = FontPx(2.2f);
        int bodyFs = FontPx(2.4f);
        int kwFs = FontPx(2.0f);
        int minBodyFs = FontPx(2.0f); // floor at 2.0% viewport height

        // Populate
        _nameLabel.Text = card.Name;
        _nameLabel.AddThemeFontSizeOverride("font_size", nameFs);

        _costChip.Text = $"Cost {card.Cost}";
        _costChip.AddThemeFontSizeOverride("font_size", chipFs);

        bool hasStats = card.Type is CardType.CREATURE or CardType.TOKEN;
        _attackChip.Visible = hasStats;
        _vigorChip.Visible = hasStats;
        if (hasStats)
        {
            _attackChip.Text = $"ATK {card.Attack ?? 0}";
            _attackChip.AddThemeFontSizeOverride("font_size", chipFs);
            _vigorChip.Text = $"VIG {card.Vigor ?? 0}";
            _vigorChip.AddThemeFontSizeOverride("font_size", chipFs);
        }

        string rules = RulesTextRenderer.RenderAbilityTextOnly(card);
        _rulesLabel.Text = rules;
        _rulesLabel.AddThemeFontSizeOverride("font_size", bodyFs);

        string kwReminders = BuildKeywordReminders(card.Keywords);
        _keywordsLabel.Text = kwReminders;
        _keywordsLabel.AddThemeFontSizeOverride("font_size", kwFs);

        // Content area: fixed rect minus panel padding (12px each side, 8px top/bottom)
        float contentW = slabW - 24f;
        float contentH = slabH - 16f;

        // Shrink body + kw fonts until content fits or hits the floor
        _vbox.Size = Vector2.Zero;
        Vector2 minSize = _vbox.GetCombinedMinimumSize();
        while (minSize.Y > contentH && bodyFs > minBodyFs)
        {
            bodyFs--;
            _rulesLabel.AddThemeFontSizeOverride("font_size", bodyFs);
            kwFs = Mathf.Max(kwFs - 1, minBodyFs - 2);
            _keywordsLabel.AddThemeFontSizeOverride("font_size", kwFs);
            minSize = _vbox.GetCombinedMinimumSize();
        }

        // Apply the fixed rect position
        Position = new Vector2(slabX, slabY);
        Size = new Vector2(slabW, slabH);
        CustomMinimumSize = new Vector2(slabW, slabH);
        _rootPanel.Size = new Vector2(slabW, slabH);

        var rootBound = _rootPanel.GetNodeOrNull<RootBoundBorder>("RulesSlabBorder");
        if (rootBound != null) rootBound.Setup(slabW, slabH);

        // Fade in over 0.12s
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
        // Fade out over 0.12s, then actually hide
        _fadeTween?.Kill();
        _fadeTween = CreateTween();
        _fadeTween.TweenProperty(this, "modulate", new Color(1, 1, 1, 0), 0.12f);
        _fadeTween.TweenInterval(0.12f);
        _fadeTween.TweenCallback(Callable.From(() => base.Hide()));
    }

    // ——— Helpers ———

    private static Label MakeInfoChip(string text)
    {
        var label = new Label
        {
            Text = text,
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeColorOverride("font_color", FrameNameText);
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.15f, 0.13f, 0.11f, 0.9f),
            BorderColor = new Color(0.40f, 0.35f, 0.28f, 0.7f),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            ContentMarginLeft = 4, ContentMarginTop = 1,
            ContentMarginRight = 4, ContentMarginBottom = 1,
        };
        label.AddThemeStyleboxOverride("normal", style);
        return label;
    }

    private static string BuildKeywordReminders(List<string> keywords)
    {
        if (keywords == null || keywords.Count == 0)
            return "";

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
using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// TASK-CARD-TEXT-1: Rules slab — panel showing card details on press-and-hold.
/// Content-driven height, derived font sizes, left-anchored so it never
/// covers board lanes.
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
    private const float MaxWidthFrac = 0.40f;
    private const float MaxHeightFrac = 0.45f;

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
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 8, ContentMarginTop = 6,
            ContentMarginRight = 8, ContentMarginBottom = 6,
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

        float slabW = viewportSize.X * MaxWidthFrac;
        slabW = Mathf.Max(200f, slabW);

        // Set font sizes from viewport height (FontPx)
        int nameFs = FontPx(3.0f);
        int chipFs = FontPx(2.2f);
        int bodyFs = FontPx(2.4f);
        int kwFs = FontPx(2.0f);

        // Populate
        _nameLabel.Text = card.Name;
        _nameLabel.AddThemeFontSizeOverride("font_size", nameFs);
        _nameLabel.Visible = true;

        _costChip.Text = $"Cost {card.Cost}";
        _costChip.AddThemeFontSizeOverride("font_size", chipFs);
        _costChip.Visible = true;

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
        _rulesLabel.Visible = !string.IsNullOrEmpty(rules);

        string kwReminders = BuildKeywordReminders(card.Keywords);
        _keywordsLabel.Text = kwReminders;
        _keywordsLabel.AddThemeFontSizeOverride("font_size", kwFs);
        _keywordsLabel.Visible = !string.IsNullOrEmpty(kwReminders);

        // Let the VBox measure its content height, then size slab to it
        _vbox.Size = Vector2.Zero; // force relayout
        Vector2 contentMin = _vbox.GetCombinedMinimumSize();
        float contentH = contentMin.Y;
        float maxH = viewportSize.Y * MaxHeightFrac;
        if (contentH > maxH)
        {
            // Shrink body & kw fonts if content overflows
            while (contentH > maxH && bodyFs > 10)
            {
                bodyFs--;
                _rulesLabel.AddThemeFontSizeOverride("font_size", bodyFs);
                kwFs = Mathf.Max(kwFs - 1, 8);
                _keywordsLabel.AddThemeFontSizeOverride("font_size", kwFs);
                contentMin = _vbox.GetCombinedMinimumSize();
                contentH = contentMin.Y;
            }
            contentH = maxH;
        }
        float slabH = contentH + 12f; // padding

        // Left-anchored, vertically centred
        float slabX = viewportSize.X * 0.02f;
        float slabY = (viewportSize.Y - slabH) / 2f;
        // Keep away from the very bottom (hand area) and very top (enemy nameplate)
        slabY = Mathf.Max(slabY, 40f);
        slabY = Mathf.Min(slabY, viewportSize.Y - slabH - 60f);

        Position = new Vector2(slabX, slabY);
        Size = new Vector2(slabW, slabH);
        CustomMinimumSize = new Vector2(slabW, slabH);
        _rootPanel.Size = new Vector2(slabW, slabH);

        var rootBound = _rootPanel.GetNodeOrNull<RootBoundBorder>("RulesSlabBorder");
        if (rootBound != null) rootBound.Setup(slabW, slabH);

        Visible = true;
        ZIndex = 100;
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

    /// <summary>
    /// Build keyword reminder text — one line per keyword, in a dimmer engraved tone.
    /// Reminders come from the keyword table so they stay correct.
    /// </summary>
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
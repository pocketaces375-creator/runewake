using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// TASK-CARD-TEXT-1: Rules slab — fixed panel in the top-centre band of the duel screen.
/// Shows card name, cost/ATK/VIG chips, rules text (via RulesTextRenderer), and
/// keyword reminders on press-and-hold of any card (hand, lane, artifact).
///
/// LOOK: pale carved stone (same as card name plaque), Root-Bound edge, no glow,
/// no flat rectangle of colour.
/// </summary>
public partial class RulesSlab : Control
{
    private PanelContainer _rootPanel;
    private Label _nameLabel;
    private Label _costChip;
    private Label _attackChip;
    private Label _vigorChip;
    private Label _rulesLabel;
    private Label _keywordsLabel;
    private Control _statRow;

    private const float SlabWidthFraction = 0.40f;
    private const float SlabHeightFraction = 0.14f;

    /// <summary>Which slab-local keyword reminders are currently shown, for unit-test scruitiny.</summary>
    public string KeywordRemindersText => _keywordsLabel?.Text ?? "";

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        // ── Root panel: pale carved stone with Root-Bound edge ──
        _rootPanel = new PanelContainer
        {
            Name = "RulesSlabPanel",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_rootPanel);

        // Pale carved stone background — same colour family as the card name plaque
        var slabStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.22f, 0.19f, 0.16f, 0.96f), // pale carved stone
            BorderColor = new Color(0.35f, 0.30f, 0.25f, 1.0f), // stone rim
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 0, ContentMarginTop = 0,
            ContentMarginRight = 0, ContentMarginBottom = 0,
        };
        _rootPanel.AddThemeStyleboxOverride("panel", slabStyle);

        // Root-Bound 9-slice border overlay
        var rootBound = new RootBoundBorder { Name = "RulesSlabBorder" };
        _rootPanel.AddChild(rootBound);

        // ── Inner VBox for content ──
        var vbox = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = (Control.SizeFlags)3, // Fill
            SizeFlagsVertical = (Control.SizeFlags)3,   // Fill
        };
        _rootPanel.AddChild(vbox);

        // ── Name label (Cinzel small caps) ──
        _nameLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MaxLinesVisible = 1,
        };
        _nameLabel.AddThemeColorOverride("font_color", FrameNameText);
        ApplyHeaderFont(_nameLabel, 16);
        vbox.AddChild(_nameLabel);

        // ── Stat row: cost / attack / vigor chips ──
        _statRow = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        vbox.AddChild(_statRow);

        _costChip = MakeInfoChip("0");
        _attackChip = MakeInfoChip("0");
        _vigorChip = MakeInfoChip("0");
        _statRow.AddChild(_costChip);
        _statRow.AddChild(_attackChip);
        _statRow.AddChild(_vigorChip);

        // ── Rules text (Cormorant Garamond body) ──
        _rulesLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _rulesLabel.AddThemeColorOverride("font_color", TextPrimary);
        ApplyBodyFont(_rulesLabel, 11);
        vbox.AddChild(_rulesLabel);

        // ── Keyword reminders (dimmer engraved tone) ──
        _keywordsLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _keywordsLabel.AddThemeColorOverride("font_color", TextMuted);
        ApplyBodyFont(_keywordsLabel, 10);
        vbox.AddChild(_keywordsLabel);
    }

    /// <summary>
    /// Position the slab in the top-centre band of the given viewport rect,
    /// then show it populated with the given card's data.
    /// </summary>
    public void ShowForCard(CardDef card, Vector2 viewportSize)
    {
        if (card == null) { Hide(); return; }

        // Compute slab geometry: ~40% wide × ~14% tall, centred in the top band
        float slabW = viewportSize.X * SlabWidthFraction;
        float slabH = viewportSize.Y * SlabHeightFraction;
        slabW = Mathf.Max(200f, slabW);
        slabH = Mathf.Max(100f, slabH);
        float slabX = (viewportSize.X - slabW) / 2f;

        // Top-centre band: ~45px from top, grows downward within the band
        // (below turn label/enemy nameplate area, above the battlefield)
        float bandTop = 40f;
        float slabY = bandTop;

        Position = new Vector2(slabX, slabY);
        Size = new Vector2(slabW, slabH);
        CustomMinimumSize = new Vector2(slabW, slabH);

        // Update Root-Bound border for current slab size
        var rootBound = _rootPanel.GetNodeOrNull<RootBoundBorder>("RulesSlabBorder");
        if (rootBound != null)
        {
            rootBound.Setup(slabW, slabH);
        }

        // ── Populate ──

        // Name
        _nameLabel.Text = card.Name;
        int nameFontSize = Mathf.Max(12, Mathf.RoundToInt(slabH * 0.13f));
        ApplyHeaderFont(_nameLabel, nameFontSize);
        _nameLabel.Visible = true;

        // Cost chip
        _costChip.Text = $"Cost {card.Cost}";
        _costChip.Visible = true;

        // Attack / Vigor chips — only for creatures and tokens
        bool hasStats = card.Type is CardType.CREATURE or CardType.TOKEN;
        _attackChip.Visible = hasStats;
        _vigorChip.Visible = hasStats;
        if (hasStats)
        {
            _attackChip.Text = $"ATK {card.Attack ?? 0}";
            _vigorChip.Text = $"VIG {card.Vigor ?? 0}";
        }

        // ── Rules text via RulesTextRenderer ──
        string rules = RulesTextRenderer.RenderAbilityTextOnly(card);
        _rulesLabel.Text = rules;
        _rulesLabel.Visible = !string.IsNullOrEmpty(rules);
        int rulesFontSize = Mathf.Max(10, Mathf.RoundToInt(slabH * 0.10f));
        ApplyBodyFont(_rulesLabel, rulesFontSize);

        // ── Keyword reminders ──
        string kwReminders = BuildKeywordReminders(card.Keywords);
        _keywordsLabel.Text = kwReminders;
        _keywordsLabel.Visible = !string.IsNullOrEmpty(kwReminders);
        int kwFontSize = Mathf.Max(9, Mathf.RoundToInt(slabH * 0.09f));
        ApplyBodyFont(_keywordsLabel, kwFontSize);

        _rootPanel.Size = new Vector2(slabW, slabH);

        Visible = true;
        ZIndex = 100; // above hand, lanes, and End Turn
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
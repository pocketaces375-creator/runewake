using Godot;
using Runewake.Engine.Cards;

namespace Runewake.Client;

/// <summary>
/// Standalone test scene for CardPlate — shows shortest and longest card names
/// at hand size and board size. Auto-captures and exits.
/// Run via: Godot_v4.3-stable_mono_linux.x86_64 --path client --scene res://scenes/test/CardPlateTest.tscn
/// </summary>
public partial class CardPlateTest : Control
{
    private const string ShortName = "Deep One";
    private const string LongName = "Cinderstorm Elemental";
    private const string ShortName2 = "Deep One";
    private const string LongName2 = "Forgeguard Berserker";

    public override void _Ready()
    {
        // Dark background
        var bg = new ColorRect
        {
            Color = Color.FromHtml("#1A1816"),
            MouseFilter = MouseFilterEnum.Ignore
        };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);

        float viewW = GetViewportRect().Size.X;

        // Title
        var title = new Label
        {
            Text = "CardPlate — Shortest vs Longest Names",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        ThemeTokens.ApplyHeaderFont(title, ThemeTokens.FontSubtitle);
        title.AddThemeColorOverride("font_color", Color.FromHtml("#C9A84C"));
        title.Position = new Vector2(0, 8);
        title.Size = new Vector2(viewW, 32);
        AddChild(title);

        float y = 50f;

        // ── Row 1: Hand size (130x190 per spec) ──
        y = AddCardPlateRow(y, "HAND SIZE (130x190)", 130f, 190f);

        // ── Row 2: Board size (124x181 per spec) ──
        y = AddCardPlateRow(y, "BOARD SIZE (124x181)", 124f, 181f);

        // Auto-capture after 1s — use resolution in filename
        var timer = GetTree().CreateTimer(1.0f);
        timer.Timeout += () =>
        {
            var image = GetViewport().GetTexture().GetImage();
            if (image != null)
            {
                string resolution = $"{(int)GetViewportRect().Size.X}x{(int)GetViewportRect().Size.Y}";
                string path = $"/home/fictive/runewake/artifacts/captures/cardplate_test_{resolution}.png";
                var dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    System.IO.Directory.CreateDirectory(dir);
                image.SavePng(path);
                DebugCapture.WriteLayoutJson(this, $"cardplate_test_{resolution}");
                GD.Print($"[CardPlateTest] Captured to {path}");

                // TASK-UI-LINT-1: Dump layout JSON
                DebugCapture.DumpLayoutJSON($"cardplate_test_{resolution.Replace('x', '_')}", this);
            }
            // Keep window open for scrot to capture, then exit
            var exitTimer = GetTree().CreateTimer(3.0f);
            exitTimer.Timeout += () => GetTree().Quit(0);
        };
    }

    private float AddCardPlateRow(float y, string label, float cardW, float cardH)
    {
        float viewW = GetViewportRect().Size.X;

        // Row label
        var rowLabel = new Label
        {
            Text = label,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        ThemeTokens.ApplyBodyFont(rowLabel, ThemeTokens.FontSmall);
        rowLabel.AddThemeColorOverride("font_color", Color.FromHtml("#B8A88A"));
        rowLabel.Position = new Vector2(0, y);
        rowLabel.Size = new Vector2(viewW, 20);
        AddChild(rowLabel);
        y += 24f;

        float spacing = 60f;
        float startX = (viewW - (cardW * 2 + spacing)) / 2f;

        // Left: Short name - "Deep One" (Tide, 1/2)
        AddPlateCard(startX, y, cardW, cardH, ShortName, 1, 2, Strata.TIDE);

        // Right: Long name - "Cinderstorm Elemental" (Ember, 7/5)
        AddPlateCard(startX + cardW + spacing, y, cardW, cardH, LongName, 7, 5, Strata.EMBER);

        // Label below the short name card
        var shortLabel = new Label
        {
            Text = $"\"{ShortName}\" — 8 chars (shortest)",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        ThemeTokens.ApplyBodyFont(shortLabel, ThemeTokens.FontTiny);
        shortLabel.AddThemeColorOverride("font_color", Color.FromHtml("#8A7D6B"));
        shortLabel.Position = new Vector2(startX, y + cardH + 2);
        shortLabel.Size = new Vector2(cardW, 16);
        AddChild(shortLabel);

        // Label below the long name card
        var longLabel = new Label
        {
            Text = $"\"{LongName}\" — 21 chars (longest)",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        ThemeTokens.ApplyBodyFont(longLabel, ThemeTokens.FontTiny);
        longLabel.AddThemeColorOverride("font_color", Color.FromHtml("#8A7D6B"));
        longLabel.Position = new Vector2(startX + cardW + spacing, y + cardH + 2);
        longLabel.Size = new Vector2(cardW, 16);
        AddChild(longLabel);

        y += cardH + 20f;
        return y;
    }

    private void AddPlateCard(float x, float y, float cardW, float cardH,
        string name, int attack, int vigor, Strata strata)
    {
        var card = new PanelContainer();
        card.Position = new Vector2(x, y);
        card.Size = new Vector2(cardW, cardH);
        card.CustomMinimumSize = new Vector2(cardW, cardH);
        var style = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#332E28"),
            BorderColor = new Color(0.35f, 0.32f, 0.28f),
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 0, ContentMarginTop = 0,
            ContentMarginRight = 0, ContentMarginBottom = 0
        };
        card.AddThemeStyleboxOverride("panel", style);

        var content = new Control();
        content.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        card.AddChild(content);

        // Cost badge (top-left)
        var costBadge = new PanelContainer();
        costBadge.Position = new Vector2(0, 0);
        costBadge.Size = new Vector2(Mathf.Max(20, cardW * 0.17f), Mathf.Max(18, cardW * 0.17f * 0.85f));
        var costStyle = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#1C1610"),
            BorderColor = Color.FromHtml("#C9A84C"),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            ContentMarginLeft = 2, ContentMarginTop = 1,
            ContentMarginRight = 2, ContentMarginBottom = 1
        };
        costBadge.AddThemeStyleboxOverride("panel", costStyle);
        var costLabel = new Label
        {
            Text = "3",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ThemeTokens.ApplyHeaderFont(costLabel, ThemeTokens.FontSmall);
        costLabel.AddThemeColorOverride("font_color", Color.FromHtml("#C9A84C"));
        costLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        costBadge.AddChild(costLabel);
        content.AddChild(costBadge);

        // CardPlate
        var plate = new CardPlate();
        content.AddChild(plate);
        plate.Setup(name, attack, vigor, strata, cardW, cardH);

        AddChild(card);
    }
}
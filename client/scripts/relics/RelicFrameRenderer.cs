using Godot;
using Runewake.Engine.Cards;

namespace Runewake.Client;

/// <summary>
/// Generates the visual engraving overlay text for Lost Relic card frames.
/// Produces a Godot Control node that can be layered on top of a card render.
/// </summary>
public static class RelicFrameRenderer
{
    /// <summary>
    /// Create an engraving overlay Control for a Lost Relic instance.
    /// Returns a ColorRect containing the engraving banner at the bottom of the card.
    /// </summary>
    public static Control CreateEngravingOverlay(LostRelicInstance relic, Vector2 cardSize)
    {
        var overlay = new ColorRect
        {
            Size = cardSize,
            Color = new Color(0f, 0f, 0f, 0f) // transparent base
        };

        // Engraving banner at the bottom 15% of the card
        float bannerHeight = cardSize.Y * 0.15f;
        var banner = new ColorRect
        {
            Color = GetBannerColor(relic.EngravingStyle),
            Size = new Vector2(cardSize.X, bannerHeight),
            Position = new Vector2(0f, cardSize.Y - bannerHeight)
        };
        overlay.AddChild(banner);

        // Engraving text
        var engravingLabel = new Label
        {
            Text = relic.GetEngravingText(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Size = new Vector2(cardSize.X - 8f, bannerHeight),
            Position = new Vector2(4f, cardSize.Y - bannerHeight + 2f),
            AutowrapMode = TextServer.AutowrapMode.Word
        };
        engravingLabel.AddThemeFontSizeOverride("font_size", (int)(bannerHeight * 0.35f));
        engravingLabel.Modulate = GetTextColor(relic.EngravingStyle);
        overlay.AddChild(engravingLabel);

        // Discovery index badge in the top-right corner
        var indexLabel = new Label
        {
            Text = $"#{relic.DiscoveryIndex}",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Size = new Vector2(40f, 24f),
            Position = new Vector2(cardSize.X - 44f, 4f)
        };
        indexLabel.AddThemeFontSizeOverride("font_size", 12);
        indexLabel.Modulate = new Color(1f, 1f, 1f, 0.7f);
        indexLabel.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.5f));
        overlay.AddChild(indexLabel);

        return overlay;
    }

    /// <summary>
    /// Generate the engraving text string without creating UI nodes (for preview/logging).
    /// </summary>
    public static string GetEngravingText(LostRelicInstance relic) => relic.GetEngravingText();

    private static Color GetBannerColor(string style) => style switch
    {
        "verdant_gold" => new Color(0.1f, 0.08f, 0.02f, 0.85f),
        "ember_iron" => new Color(0.15f, 0.05f, 0.02f, 0.85f),
        "tide_silver" => new Color(0.02f, 0.05f, 0.12f, 0.85f),
        "hollow_onyx" => new Color(0.05f, 0.02f, 0.08f, 0.85f),
        _ => new Color(0.08f, 0.06f, 0.04f, 0.85f) // default dark brown
    };

    private static Color GetTextColor(string style) => style switch
    {
        "verdant_gold" => new Color(0.95f, 0.85f, 0.3f),
        "ember_iron" => new Color(0.95f, 0.5f, 0.2f),
        "tide_silver" => new Color(0.7f, 0.8f, 0.95f),
        "hollow_onyx" => new Color(0.6f, 0.5f, 0.7f),
        _ => new Color(0.9f, 0.85f, 0.8f) // default warm white
    };
}
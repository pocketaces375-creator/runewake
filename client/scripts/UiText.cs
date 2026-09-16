using System;
using Godot;

namespace Runewake.Client;

/// <summary>
/// FABLE-003: deterministic text fitting for hand-laid-out panels.
///
/// Godot's autowrap Label reports its minimum height from its CURRENT width,
/// which is 0 on the frame it is created — so a PanelContainer built around it
/// sizes itself for one-word-per-line and never shrinks back. That is exactly
/// how the first tutorial card ended up the height of the screen.
///
/// This measures the wrapped text with the font metrics directly and gives the
/// label an explicit rect, so a panel can be sized to its content on the same
/// frame it is filled, with no container in the loop.
/// </summary>
public static class UiText
{
    /// <summary>Height the label's text needs when wrapped to <paramref name="width"/>.</summary>
    public static float MeasureHeight(Label label, float width)
    {
        var font = label.GetThemeFont("font");
        int fontSize = label.GetThemeFontSize("font_size");
        if (font == null || fontSize <= 0 || string.IsNullOrEmpty(label.Text))
            return 0f;
        int spacing = label.GetThemeConstant("line_spacing");
        var sz = font.GetMultilineStringSize(label.Text, label.HorizontalAlignment, width, fontSize);
        float lineH = Math.Max(1f, font.GetHeight(fontSize));
        int lines = Math.Max(1, Mathf.RoundToInt(sz.Y / lineH));
        return sz.Y + spacing * (lines - 1);
    }

    /// <summary>
    /// Place the label at (x, y) with the given width and exactly the height its
    /// wrapped text needs. Returns that height (0 if the label is hidden or empty).
    /// </summary>
    public static float Fit(Label label, float x, float y, float width)
    {
        if (!label.Visible || string.IsNullOrEmpty(label.Text))
            return 0f;
        float h = MeasureHeight(label, width);
        label.Position = new Vector2(x, y);
        label.Size = new Vector2(width, h);
        label.CustomMinimumSize = new Vector2(width, h);
        return h;
    }
}

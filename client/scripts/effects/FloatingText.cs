using Godot;

namespace Runewake.Client;

/// <summary>
/// A floating text label that animates upward, fades out, and frees itself.
/// Used for damage numbers (red), heal numbers (green), and face damage.
/// Two size modes: 22px for lane-level numbers, 26px for face/important hits.
/// </summary>
public partial class FloatingText : Label
{
    /// <summary>
    /// Show a floating damage/heal number at the given global position.
    /// Uses 22px font (lane-level).
    /// </summary>
    public void ShowAt(string text, Color color, Vector2 position)
    {
        ShowText(text, color, position, 22);
    }

    /// <summary>
    /// Show a large floating number for face damage or important events.
    /// Uses 26px font with bolder appearance.
    /// </summary>
    public void ShowLargeAt(string text, Color color, Vector2 position)
    {
        ShowText(text, color, position, 28);
    }

    private void ShowText(string text, Color color, Vector2 position, int fontSize)
    {
        Text = text;
        Modulate = color;
        AddThemeFontSizeOverride("font_size", fontSize);
        AddThemeConstantOverride("outline_size", 2);
        Position = position;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;

        var tween = CreateTween();
        tween.SetParallel();
        tween.TweenProperty(this, "position", position + new Vector2(0, -50), 0.9f);
        tween.TweenProperty(this, "modulate:a", 0.0f, 0.9f);
        tween.SetParallel(false);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
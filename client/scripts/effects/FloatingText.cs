using Godot;

namespace Runewake.Client;

/// <summary>
/// A floating text label that animates upward, fades out, and frees itself.
/// Used for damage numbers (red) and heal numbers (green).
/// </summary>
public partial class FloatingText : Label
{
    /// <summary>
    /// Show a floating damage/heal number at the given global position.
    /// </summary>
    public void ShowAt(string text, Color color, Vector2 position)
    {
        Text = text;
        Modulate = color;
        AddThemeFontSizeOverride("font_size", 18);
        Position = position;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;

        var tween = CreateTween();
        tween.SetParallel();
        tween.TweenProperty(this, "position", position + new Vector2(0, -40), 0.8f);
        tween.TweenProperty(this, "modulate:a", 0.0f, 0.8f);
        tween.SetParallel(false);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
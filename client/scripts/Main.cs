using Godot;

namespace Runewake.Client;

/// <summary>
/// Entry point for the Runewake client. Displays a smoke-test label
/// confirming the Godot .NET toolchain works with the engine reference.
/// </summary>
public partial class Main : Control
{
    public override void _Ready()
    {
        var label = new Label
        {
            Text = "RUNEWAKE",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 1f
        };

        // Apply a theme-style font override via code
        label.AddThemeFontSizeOverride("font_size", 48);
        AddChild(label);
    }
}

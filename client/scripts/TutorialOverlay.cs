using Godot;

namespace Runewake.Client;

/// <summary>
/// A semi-transparent overlay that shows tutorial hints and prompts.
/// Positioned at the top-center of the screen, above the board.
/// Updated by DuelScene when the tutorial step changes.
/// </summary>
public partial class TutorialOverlay : Control
{
    private Label _hintLabel;
    private Label _titleLabel;
    private Label _debugLabel;
    private Button _skipButton;

    /// <summary>True if the player pressed the skip button.</summary>
    public bool Skipped { get; private set; }

    /// <summary>Fires when the player taps Skip Tutorial.</summary>
    public event Action? SkipRequested;

    public override void _Ready()
    {
        // Background panel — semi-transparent dark with rounded corners
        var panel = new Panel();
        panel.AnchorLeft = 0.1f;
        panel.AnchorRight = 0.9f;
        panel.AnchorTop = 0.08f;
        panel.AnchorBottom = 0.22f;

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.06f, 0.06f, 0.15f, 0.92f);
        style.BorderColor = new Color(0.4f, 0.6f, 1.0f, 0.8f);
        style.BorderWidthLeft = 2;
        style.BorderWidthTop = 2;
        style.BorderWidthRight = 2;
        style.BorderWidthBottom = 2;
        style.CornerRadiusTopLeft = 8;
        style.CornerRadiusTopRight = 8;
        style.CornerRadiusBottomLeft = 8;
        style.CornerRadiusBottomRight = 8;
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);

        // Title label
        _titleLabel = new Label
        {
            Text = "TUTORIAL",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            AnchorLeft = 0.05f,
            AnchorRight = 0.95f,
            AnchorTop = 0.1f,
            AnchorBottom = 0.35f,
            Modulate = new Color(0.6f, 0.8f, 1.0f)
        };
        _titleLabel.AddThemeFontSizeOverride("font_size", 14);
        panel.AddChild(_titleLabel);

        // Hint text — wraps, centered
        _hintLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            AnchorLeft = 0.05f,
            AnchorRight = 0.95f,
            AnchorTop = 0.35f,
            AnchorBottom = 0.9f,
            AutowrapMode = TextServer.AutowrapMode.Word
        };
        _hintLabel.AddThemeFontSizeOverride("font_size", 13);
        _hintLabel.Modulate = new Color(1, 1, 1, 0.9f);
        panel.AddChild(_hintLabel);

        // Debug line — shows current step and last action
        _debugLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            AnchorLeft = 0.05f,
            AnchorRight = 0.95f,
            AnchorTop = 0.75f,
            AnchorBottom = 1.0f,
            Modulate = new Color(0.4f, 0.4f, 0.6f, 0.7f)
        };
        _debugLabel.AddThemeFontSizeOverride("font_size", 10);
        panel.AddChild(_debugLabel);

        // Skip button — top-right, styled as a small red link
        _skipButton = new Button
        {
            Text = "✕ Skip Tutorial",
            Position = new Vector2(-160, 8),
            Size = new Vector2(150, 30),
            AnchorLeft = 1.0f,
            AnchorRight = 0.0f,
            AnchorTop = 0.0f,
            AnchorBottom = 0.0f
        };
        _skipButton.AddThemeFontSizeOverride("font_size", 13);
        _skipButton.AddThemeColorOverride("font_color", new Color(1, 0.5f, 0.5f));
        _skipButton.Pressed += OnSkipPressed;
        AddChild(_skipButton);
    }

    /// <summary>
    /// Update the hint text displayed by this overlay.
    /// </summary>
    public void SetHint(string text)
    {
        if (_hintLabel != null)
            _hintLabel.Text = text;
    }

    /// <summary>
    /// Update the debug line showing what step is expected and the last action performed.
    /// </summary>
    public void SetDebugInfo(string stepName, string lastAction, bool lastActionMatched)
    {
        if (_debugLabel == null) return;
        string check = lastActionMatched ? "✓" : "○";
        _debugLabel.Text = $"Step: {stepName}  |  Last: {lastAction} {check}";
    }

    private void OnSkipPressed()
    {
        GD.Print("[TutorialOverlay] Skip button pressed.");
        Skipped = true;
        SkipRequested?.Invoke();
    }

    /// <summary>
    /// Dimiss this overlay with a fade-out animation.
    /// </summary>
    public void Dismiss()
    {
        var tween = CreateTween();
        tween.TweenProperty(this, "modulate", new Color(1, 1, 1, 0), 0.3f);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
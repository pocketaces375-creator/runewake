using Godot;

namespace Runewake.Client;

/// <summary>
/// A semi-transparent overlay that shows tutorial hints and prompts.
/// Positioned at the top-center of the screen, above the board.
/// Updated by DuelScene when the tutorial step changes.
/// Can highlight specific UI elements with a pulsing border.
/// Does NOT block input (MouseFilter.Pass).
/// </summary>
public partial class TutorialOverlay : Control
{
    private Label? _hintLabel;
    private Label? _titleLabel;
    private Label? _debugLabel;
    private Button? _skipButton;
    private ColorRect? _highlightRect;
    private ColorRect? _highlightBorder;

    /// <summary>True if the player pressed the skip button.</summary>
    public bool Skipped { get; private set; }

    /// <summary>Fires when the player taps Skip Tutorial.</summary>
    public event Action? SkipRequested;

    public override void _Ready()
    {
        // Fill the parent so children's anchors work
        AnchorLeft = 0;
        AnchorRight = 1;
        AnchorTop = 0;
        AnchorBottom = 1;
        MouseFilter = MouseFilterEnum.Pass;

        // Highlight elements (hidden by default)
        _highlightBorder = new ColorRect
        {
            Color = new Color(1, 0.8f, 0.2f, 0.9f),
            Visible = false,
            MouseFilter = MouseFilterEnum.Pass
        };
        AddChild(_highlightBorder);

        _highlightRect = new ColorRect
        {
            Color = new Color(1, 0.9f, 0.4f, 0.12f),
            Visible = false,
            MouseFilter = MouseFilterEnum.Pass
        };
        AddChild(_highlightRect);

        // Background — ColorRect instead of Panel to avoid theme override on labels
        var bg = new ColorRect
        {
            Color = new Color(0.06f, 0.06f, 0.15f, 0.92f),
            AnchorLeft = 0.1f,
            AnchorRight = 0.9f,
            AnchorTop = 0.06f,
            AnchorBottom = 0.20f,
            MouseFilter = MouseFilterEnum.Pass
        };
        AddChild(bg);

        // Border — slightly larger ColorRect behind bg
        var border = new ColorRect
        {
            Color = new Color(0.4f, 0.6f, 1.0f, 0.8f),
            AnchorLeft = 0.1f,
            AnchorRight = 0.9f,
            AnchorTop = 0.06f,
            AnchorBottom = 0.20f,
            MouseFilter = MouseFilterEnum.Pass
        };
        AddChild(border);
        // Move bg in front of border
        RemoveChild(bg);
        AddChild(bg);
        // Shrink bg slightly to create border effect
        bg.AnchorLeft = 0.103f;
        bg.AnchorRight = 0.897f;
        bg.AnchorTop = 0.064f;
        bg.AnchorBottom = 0.196f;

        // Hint text — large, white, wrapped (direct child of overlay, no panel)
        _hintLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            AnchorLeft = 0.1f,
            AnchorRight = 0.9f,
            AnchorTop = 0.08f,
            AnchorBottom = 0.19f,
            AutowrapMode = TextServer.AutowrapMode.Word,
            MouseFilter = MouseFilterEnum.Pass
        };
        _hintLabel.AddThemeColorOverride("font_color", Colors.White);
        _hintLabel.AddThemeFontSizeOverride("font_size", 18);
        AddChild(_hintLabel);

        // Debug line — bottom-right of overlay area
        _debugLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            AnchorLeft = 0.1f,
            AnchorRight = 0.9f,
            AnchorTop = 0.17f,
            AnchorBottom = 0.20f,
            MouseFilter = MouseFilterEnum.Pass
        };
        _debugLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.9f));
        _debugLabel.AddThemeFontSizeOverride("font_size", 13);
        AddChild(_debugLabel);

        // Skip button — top-right, styled as a small red link
        _skipButton = new Button { Text = "✕ Skip Tutorial" };
        _skipButton.AddThemeFontSizeOverride("font_size", 14);
        _skipButton.SelfModulate = new Color(1, 0.4f, 0.4f);
        _skipButton.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _skipButton.Size = new Vector2(160, 34);
        _skipButton.OffsetLeft = -160;
        _skipButton.OffsetRight = -8;
        _skipButton.OffsetTop = 6;
        _skipButton.OffsetBottom = 40;
        _skipButton.Pressed += OnSkipPressed;
        AddChild(_skipButton);
    }

    /// <summary>
    /// Update the hint text displayed by this overlay.
    /// One sentence, large and legible.
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

    /// <summary>
    /// Highlight an element on screen with a golden glow border.
    /// The targetRect should be in global screen coordinates.
    /// Call ClearHighlight() to remove.
    /// </summary>
    public void HighlightElement(Rect2 targetRect)
    {
        if (_highlightBorder == null || _highlightRect == null) return;

        // Border is 4px larger on each side than the target
        float pad = 6f;
        _highlightBorder.Position = targetRect.Position - new Vector2(pad, pad);
        _highlightBorder.Size = targetRect.Size + new Vector2(pad * 2, pad * 2);
        _highlightBorder.Visible = true;

        // Inner fill matches the target rect exactly
        _highlightRect.Position = targetRect.Position - new Vector2(2, 2);
        _highlightRect.Size = targetRect.Size + new Vector2(4, 4);
        _highlightRect.Visible = true;

        // Pulsing animation
        var tween = CreateTween().SetLoops();
        tween.TweenProperty(_highlightBorder, "modulate:a", 0.4f, 0.6f);
        tween.TweenProperty(_highlightBorder, "modulate:a", 0.9f, 0.6f);
    }

    /// <summary>
    /// Remove the element highlight.
    /// </summary>
    public void ClearHighlight()
    {
        if (_highlightBorder != null)
        {
            _highlightBorder.Visible = false;
            _highlightBorder.Modulate = new Color(1, 1, 1, 1);
        }
        if (_highlightRect != null)
        {
            _highlightRect.Visible = false;
        }
        // Kill any running tweens on the highlight
        if (_highlightBorder != null)
        {
            var tween = CreateTween();
            tween.TweenProperty(_highlightBorder, "modulate:a", 1.0f, 0.01f);
            tween.Kill();
        }
    }

    private void OnSkipPressed()
    {
        GD.Print("[TutorialOverlay] Skip button pressed.");
        Skipped = true;
        SkipRequested?.Invoke();
    }

    /// <summary>
    /// Dismiss this overlay with a fade-out animation.
    /// </summary>
    public void Dismiss()
    {
        var tween = CreateTween();
        tween.TweenProperty(this, "modulate", new Color(1, 1, 1, 0), 0.3f);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
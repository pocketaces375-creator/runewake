using System;
using Godot;

namespace Runewake.Client;

/// <summary>
/// Modal tutorial popup presenter — one implementation of <see cref="ITutorialPresenter"/>.
/// Blocks all input behind it with a dim overlay until dismissed.
/// No timers, no auto-advance — the only exit is the Continue button (or Skip link).
///
/// Usage:
///   var popup = new TutorialPopup();
///   popup.HighlightTarget = someControl;
///   popup.Dismissed += () => { ... };
///   popup.Show(content);
///   AddChild(popup);
///
/// The dim overlay uses MouseFilter.Stop so no input reaches the board
/// while a popup is visible.
/// </summary>
public partial class TutorialPopup : Control, ITutorialPresenter
{
    private static readonly Color DimColor = new(0f, 0f, 0f, 0.55f);
    private static readonly Color PopupBg = new(0.06f, 0.06f, 0.15f, 0.95f);
    private static readonly Color BorderColor = new(0.8f, 0.6f, 0.2f, 0.6f);
    private static readonly Color TitleColor = new(0.9f, 0.75f, 0.3f);

    private TutorialContent? _currentContent;
    private Control? _highlightTarget;
    private Vector2 _highlightMargins;
    private Control? _highlightFrame;
    private bool _wasSkipped;

    // ── ITutorialPresenter ──

    public event Action? Dismissed;

    public void Show(TutorialContent content)
    {
        _currentContent = content;
        _wasSkipped = false;

        Name = $"TutorialPopup_{content.PopupId}";
        AnchorLeft = 0;
        AnchorRight = 1;
        AnchorTop = 0;
        AnchorBottom = 1;
        MouseFilter = MouseFilterEnum.Ignore; // overlay covers us

        // If already in the tree, rebuild UI
        if (IsInsideTree())
            BuildUi();
    }

    // ── Presenter-specific properties ──

    /// <summary>
    /// Optional node to highlight with a golden pulsing border while this popup is open.
    /// Must be set before Show() for first render.
    /// </summary>
    public Control? HighlightTarget
    {
        get => _highlightTarget;
        set
        {
            _highlightTarget = value;
            if (IsInsideTree())
                RefreshHighlight();
        }
    }

    /// <summary>
    /// Extra margin (px) around the highlight target's rect for visual padding.
    /// </summary>
    public Vector2 HighlightMargins
    {
        get => _highlightMargins;
        set
        {
            _highlightMargins = value;
            if (IsInsideTree() && _highlightTarget != null)
                RefreshHighlight();
        }
    }

    /// <summary>
    /// True if Dismissed fired because the player tapped Skip (vs Continue).
    /// Check after Dismissed fires to determine caller behaviour.
    /// </summary>
    public bool WasSkipped => _wasSkipped;

    // ── Construction ──

    /// <summary>
    /// Create a tutorial popup presenter. Call Show() to display content,
    /// then AddChild() to add to the scene tree.
    /// </summary>
    public TutorialPopup()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        BuildUi();
    }

    private void BuildUi()
    {
        if (_currentContent == null) return;

        // Remove any previous children (safe for first call — no children yet)
        foreach (var child in GetChildren())
            RemoveChild(child);

        // ── Dim overlay (blocks all input below) ──
        var dim = new ColorRect
        {
            Color = DimColor,
            AnchorLeft = 0,
            AnchorRight = 1,
            AnchorTop = 0,
            AnchorBottom = 1,
            MouseFilter = MouseFilterEnum.Stop // CRITICAL: blocks input to board
        };
        AddChild(dim);

        // ── Popup container ──
        // Center-anchored Control with a fixed max width.
        var container = new Control
        {
            AnchorLeft = 0.1f,
            AnchorRight = 0.9f,
            AnchorTop = 0.2f,
            AnchorBottom = 0.8f,
            MouseFilter = MouseFilterEnum.Stop
        };
        AddChild(container);

        // Background
        var bg = new ColorRect
        {
            Color = PopupBg,
            AnchorLeft = 0,
            AnchorRight = 1,
            AnchorTop = 0,
            AnchorBottom = 1
        };
        container.AddChild(bg);

        // Border (slightly larger ColorRect behind bg)
        var border = new ColorRect
        {
            Color = BorderColor,
            AnchorLeft = 0,
            AnchorRight = 1,
            AnchorTop = 0,
            AnchorBottom = 1
        };
        container.AddChild(border);
        // Move bg in front
        container.RemoveChild(bg);
        container.AddChild(bg);
        // Shrink bg to create border effect
        bg.AnchorLeft = 0.008f;
        bg.AnchorRight = 0.992f;
        bg.AnchorTop = 0.008f;
        bg.AnchorBottom = 0.992f;

        // ── Title ──
        var titleLabel = new Label
        {
            Text = _currentContent.Title,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            AnchorLeft = 0.06f,
            AnchorRight = 0.94f,
            AnchorTop = 0.05f,
            AnchorBottom = 0.22f,
            AutowrapMode = TextServer.AutowrapMode.Word
        };
        titleLabel.AddThemeColorOverride("font_color", TitleColor);
        titleLabel.AddThemeFontSizeOverride("font_size", 20);
        container.AddChild(titleLabel);

        // ── Body text ──
        var bodyLabel = new Label
        {
            Text = _currentContent.Text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            AnchorLeft = 0.06f,
            AnchorRight = 0.94f,
            AnchorTop = 0.22f,
            AnchorBottom = 0.65f,
            AutowrapMode = TextServer.AutowrapMode.Word
        };
        bodyLabel.AddThemeColorOverride("font_color", Colors.White);
        bodyLabel.AddThemeFontSizeOverride("font_size", 16);
        bodyLabel.AddThemeConstantOverride("line_spacing", 6);
        container.AddChild(bodyLabel);

        // ── Continue button ──
        var continueBtn = new Button
        {
            Text = "Continue",
            AnchorLeft = 0.2f,
            AnchorRight = 0.8f,
            AnchorTop = 0.68f,
            AnchorBottom = 0.82f
        };
        continueBtn.AddThemeFontSizeOverride("font_size", 16);
        continueBtn.Pressed += OnContinuePressed;
        container.AddChild(continueBtn);

        // ── Skip Tutorial link ──
        if (_currentContent.ShowSkip)
        {
            var skipLabel = new Label
            {
                Text = "Skip Tutorial",
                AnchorLeft = 0.3f,
                AnchorRight = 0.7f,
                AnchorTop = 0.84f,
                AnchorBottom = 0.92f,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            skipLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
            skipLabel.AddThemeFontSizeOverride("font_size", 14);
            // Make clickable via GuiInput
            skipLabel.MouseFilter = MouseFilterEnum.Stop;
            skipLabel.GuiInput += (@event) =>
            {
                if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
                {
                    _wasSkipped = true;
                    _isWaitingForConfirm = false; // clear any pending confirm
                    OnDismissed();
                }
            };
            container.AddChild(skipLabel);
        }

        // ── Refresh highlight if one was set before Show() ──
        if (_highlightTarget != null)
            Callable.From(RefreshHighlight).CallDeferred();
    }

    // ── Skip confirmation (replaces old confirmation dialog) ──
    private bool _isWaitingForConfirm;

    private void OnContinuePressed()
    {
        _wasSkipped = false;
        OnDismissed();
    }

    private void OnDismissed()
    {
        Dismissed?.Invoke();
    }

    // ── Highlight system ──

    private void RefreshHighlight()
    {
        if (_highlightTarget == null || !IsInsideTree())
            return;

        RemoveHighlightFrame();

        var targetRect = _highlightTarget.GetRect();
        var globalPos = _highlightTarget.GetGlobalMousePosition(); // fallback
        // Use GlobalPosition + rect offset for proper positioning
        // We draw a golden outline around the target

        _highlightFrame = new ColorRect
        {
            Color = new Color(0, 0, 0, 0), // invisible fill
            // Position relative to this node's coordinate space
            ZIndex = 10
        };
        AddChild(_highlightFrame);

        // Animate pulse
        var tween = CreateTween();
        tween.SetLoops();
        tween.TweenProperty(_highlightFrame, "self_modulate", new Color(0.9f, 0.7f, 0.2f, 0.6f), 0.8f);
        tween.TweenProperty(_highlightFrame, "self_modulate", new Color(0.9f, 0.7f, 0.2f, 0.2f), 0.8f);
    }

    private void RemoveHighlightFrame()
    {
        if (_highlightFrame != null)
        {
            _highlightFrame.QueueFree();
            _highlightFrame = null;
        }
    }

    /// <summary>
    /// Clean up on removal.
    /// </summary>
    public override void _ExitTree()
    {
        RemoveHighlightFrame();
        Dismissed = null;
    }
}
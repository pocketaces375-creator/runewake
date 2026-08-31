using System;
using System.Collections.Generic;
using Godot;

namespace Runewake.Client;

/// <summary>
/// Modal tutorial popup presenter — one implementation of <see cref="ITutorialPresenter"/>.
/// Blocks all input behind it with a dim overlay until dismissed.
/// Supports spotlight highlights: resolves target Control nodes from the live duel layout
/// and draws golden pulsing frames around their global rects.
///
/// Usage:
///   var popup = new TutorialPopup();
///   popup.SetHighlightTargets(new List<Control> { handCard, laneSlot, endTurnBtn });
///   popup.Dismissed += () => { ... };
///   popup.Show(content);
///   AddChild(popup);
///
/// The dim overlay uses MouseFilter.Stop so no input reaches the board
/// while a popup is visible. Highlight frames are drawn above the dim,
/// making the highlighted elements pop through as the only interactive targets.
/// </summary>
public partial class TutorialPopup : Control, ITutorialPresenter
{
    private static readonly Color DimColor = new(0f, 0f, 0f, 0.55f);
    private static readonly Color PopupBg = new(0.06f, 0.06f, 0.15f, 0.95f);
    private static readonly Color BorderColor = new(0.8f, 0.6f, 0.2f, 0.6f);
    private static readonly Color TitleColor = new(0.9f, 0.75f, 0.3f);

    // Highlight visual constants
    private static readonly Color HighlightPulseA = new(0.9f, 0.7f, 0.2f, 0.7f); // peak
    private static readonly Color HighlightPulseB = new(0.9f, 0.7f, 0.2f, 0.2f); // trough
    private const float HighlightThickness = 3f;

    private TutorialContent? _currentContent;
    private Vector2 _highlightMargins;
    private bool _wasSkipped;

    // Multi-highlight support: each target gets its own pulse-animated outline frame
    private readonly List<Control> _highlightTargets = new();
    private readonly List<PanelContainer> _highlightFrames = new();

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
    /// Set the highlight target nodes BEFORE calling Show() for first render,
    /// or call at any time to update which elements are highlighted.
    /// Targets are resolved from the live layout — their global rects are
    /// captured at render time, so elements that haven't had their layout
    /// finalized yet will work after a call_deferred refresh.
    /// </summary>
    public void SetHighlightTargets(List<Control> targets)
    {
        _highlightTargets.Clear();
        _highlightTargets.AddRange(targets);

        if (IsInsideTree())
            RefreshHighlights();
    }

    /// <summary>
    /// Extra margin (px) around each highlight target's rect for visual padding.
    /// Applied as a uniform inset expansion on all four sides.
    /// </summary>
    public Vector2 HighlightMargins
    {
        get => _highlightMargins;
        set
        {
            _highlightMargins = value;
            if (IsInsideTree() && _highlightTargets.Count > 0)
                RefreshHighlights();
        }
    }

    /// <summary>
    /// True if Dismissed fired because the player tapped Skip (vs Continue).
    /// Check after Dismissed fires to determine caller behaviour.
    /// </summary>
    public bool WasSkipped => _wasSkipped;

    // ── Construction ──

    /// <summary>
    /// Create a tutorial popup presenter. Call SetHighlightTargets() before Show()
    /// for initial highlights, then Show() to display content.
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
            skipLabel.MouseFilter = MouseFilterEnum.Stop;
            skipLabel.GuiInput += (@event) =>
            {
                if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
                {
                    _wasSkipped = true;
                    _isWaitingForConfirm = false;
                    OnDismissed();
                }
            };
            container.AddChild(skipLabel);
        }

        // ── Refresh highlights ──
        // Must be deferred so that newly-created child nodes have their final layout
        if (_highlightTargets.Count > 0)
            Callable.From(RefreshHighlights).CallDeferred();
    }

    // ── Skip confirmation ──
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

    // ── Multi-highlight system ──

    /// <summary>
    /// Rebuild all highlight frames to match the current target list.
    /// Each target gets a pulsing golden outline drawn around its global rect,
    /// with margins applied for visual breathing room.
    /// </summary>
    private void RefreshHighlights()
    {
        RemoveAllHighlightFrames();

        if (_highlightTargets.Count == 0 || !IsInsideTree())
            return;

        foreach (var target in _highlightTargets)
        {
            if (target == null || !GodotObject.IsInstanceValid(target))
                continue;

            // Compute global rect of the target element
            // Since this popup covers the entire viewport, GlobalPosition is effectively (0,0).
            Rect2 targetRect = target.GetGlobalRect();

            if (targetRect.Size.X <= 0 || targetRect.Size.Y <= 0)
                continue; // element not laid out yet

            // Expand by margins
            float mx = _highlightMargins.X;
            float my = _highlightMargins.Y;
            var framePos = new Vector2(targetRect.Position.X - mx, targetRect.Position.Y - my);
            var frameSize = new Vector2(targetRect.Size.X + mx * 2, targetRect.Size.Y + my * 2);

            // Create highlight outline as a PanelContainer with transparent fill
            // and a colored border via StyleBoxFlat
            var panel = new PanelContainer
            {
                Position = framePos,
                Size = frameSize,
                ZIndex = 10,
                MouseFilter = MouseFilterEnum.Ignore // highlight doesn't block input to the element
            };
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0, 0, 0, 0), // transparent fill
                BorderWidthLeft = Math.Max(1, (int)HighlightThickness),
                BorderWidthRight = Math.Max(1, (int)HighlightThickness),
                BorderWidthTop = Math.Max(1, (int)HighlightThickness),
                BorderWidthBottom = Math.Max(1, (int)HighlightThickness),
                BorderColor = HighlightPulseA,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
            };
            panel.AddThemeStyleboxOverride("panel", style);

            AddChild(panel);
            _highlightFrames.Add(panel);

            // Start pulse animation via tween on the border alpha
            var tween = CreateTween();
            tween.SetLoops();
            tween.TweenMethod(Callable.From((float t) =>
            {
                if (!IsInstanceValid(panel)) return;
                // Triangle wave: peak → trough → peak over 0.8s interval
                float alpha = HighlightPulseA.A + (HighlightPulseB.A - HighlightPulseA.A)
                    * (1f - Mathf.Abs((t * 2f) - 1f));
                var modColor = style.BorderColor;
                modColor.A = alpha;
                style.BorderColor = modColor;
                panel.AddThemeStyleboxOverride("panel", style);
            }), 0f, 1f, 0.8f);
        }

        // Force redraw to ensure new children render immediately
        QueueRedraw();
    }

    private void RemoveAllHighlightFrames()
    {
        foreach (var frame in _highlightFrames)
        {
            if (GodotObject.IsInstanceValid(frame))
                frame.QueueFree();
        }
        _highlightFrames.Clear();
    }

    /// <summary>
    /// Clean up on removal.
    /// </summary>
    public override void _ExitTree()
    {
        RemoveAllHighlightFrames();
        Dismissed = null;
    }
}
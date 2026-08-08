using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// CanvasLayer overlay that shows tutorial speech bubbles and highlights.
/// Always on top (Layer=10). Subscribes to TutorialController.StepChanged.
/// </summary>
public partial class TutorialOverlay : CanvasLayer
{
    private PanelContainer _bubble = default!;
    private Label _messageLabel = default!;
    private Button _gotItButton = default!;
    private ColorRect? _highlight;

    public override void _Ready()
    {
        Layer = 10;

        // Semi-transparent background blocker
        var blocker = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.4f),
            AnchorLeft = 0, AnchorRight = 1,
            AnchorTop = 0, AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        AddChild(blocker);

        // Speech bubble at bottom
        _bubble = new PanelContainer();
        _bubble.AnchorLeft = 0.05f;
        _bubble.AnchorRight = 0.95f;
        _bubble.AnchorTop = 0.65f;
        _bubble.AnchorBottom = 0.88f;
        var bubbleStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.12f, 0.16f),
            BorderColor = new Color(0.3f, 0.5f, 0.8f),
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        };
        _bubble.AddThemeStyleboxOverride("panel", bubbleStyle);
        AddChild(_bubble);

        // Message label
        _messageLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0, AnchorRight = 1,
            AnchorTop = 0, AnchorBottom = 1,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _messageLabel.AddThemeFontSizeOverride("font_size", 13);
        _bubble.AddChild(_messageLabel);

        // "Got it" button
        _gotItButton = new Button
        {
            Text = "Got it",
            AnchorLeft = 0.4f, AnchorRight = 0.6f,
            AnchorTop = 0.9f, AnchorBottom = 0.96f,
        };
        _gotItButton.Pressed += OnGotIt;
        AddChild(_gotItButton);

        // Subscribe to tutorial controller
        var ctrl = GetNodeOrNull<TutorialController>("/root/TutorialController");
        if (ctrl != null)
        {
            ctrl.StepChanged += OnStepChanged;
            if (ctrl.IsActive)
                OnStepChanged();
        }

        Hide();
    }

    private void OnStepChanged()
    {
        var ctrl = GetNodeOrNull<TutorialController>("/root/TutorialController");
        var def = ctrl?.GetCurrentDef();
        if (def == null)
        {
            Hide();
            ClearHighlight();
            return;
        }

        Show();
        _messageLabel.Text = def.Message;
        SetHighlight(def.Highlight);
    }

    private void OnGotIt()
    {
        var ctrl = GetNodeOrNull<TutorialController>("/root/TutorialController");
        if (ctrl != null && ctrl.IsActive)
        {
            ctrl.Advance();
        }
    }

    /// <summary>
    /// Show a pulsing highlight overlay on the named UI region.
    /// </summary>
    private void SetHighlight(string region)
    {
        ClearHighlight();

        if (region == "none")
            return;

        // For now, use a simple positional highlight based on region name.
        // In a full implementation, these would be anchored to named nodes.
        var rect = new ColorRect
        {
            Color = new Color(0.3f, 0.6f, 1.0f, 0.25f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        switch (region)
        {
            case "hand":
                rect.AnchorLeft = 0.05f;
                rect.AnchorRight = 0.95f;
                rect.AnchorTop = 0.8f;
                rect.AnchorBottom = 1.0f;
                break;
            case "lane":
                rect.AnchorLeft = 0.1f;
                rect.AnchorRight = 0.9f;
                rect.AnchorTop = 0.35f;
                rect.AnchorBottom = 0.65f;
                break;
            case "endturn":
                // Bottom-right corner
                rect.AnchorLeft = 0.75f;
                rect.AnchorRight = 0.98f;
                rect.AnchorTop = 0.82f;
                rect.AnchorBottom = 0.96f;
                break;
            case "barrow":
                // Top-left corner (barrow counter area)
                rect.AnchorLeft = 0.02f;
                rect.AnchorRight = 0.2f;
                rect.AnchorTop = 0.02f;
                rect.AnchorBottom = 0.15f;
                break;
            case "runebtn":
                // Center of screen (rune button on map)
                rect.AnchorLeft = 0.3f;
                rect.AnchorRight = 0.7f;
                rect.AnchorTop = 0.4f;
                rect.AnchorBottom = 0.6f;
                break;
            case "runeslot":
                // Rune page center area
                rect.AnchorLeft = 0.1f;
                rect.AnchorRight = 0.9f;
                rect.AnchorTop = 0.2f;
                rect.AnchorBottom = 0.5f;
                break;
            default:
                rect.AnchorLeft = 0.3f;
                rect.AnchorRight = 0.7f;
                rect.AnchorTop = 0.35f;
                rect.AnchorBottom = 0.65f;
                break;
        }

        _highlight = rect;
        AddChild(rect);
    }

    private void ClearHighlight()
    {
        if (_highlight != null)
        {
            _highlight.QueueFree();
            _highlight = null;
        }
    }
}
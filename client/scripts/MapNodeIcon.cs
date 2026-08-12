using Godot;

namespace Runewake.Client;

/// <summary>
/// A single icon on the campaign map. Shows node type, lock state, and name.
/// Clickable — emits NodeSelected when tapped.
/// Three distinct visual states: locked (dark grey + lock icon),
/// available (full color + glow), cleared (faded + checkmark).
/// </summary>
public partial class MapNodeIcon : Button
{
    private Label _nameLabel;
    private ColorRect _iconCircle;
    private Label _typeChar;
    private ColorRect _glowBorder;
    private ColorRect _lockOverlay;
    private Label _clearMark;

    // Padlock parts (drawn in code to avoid font dependency)
    private ColorRect _lockShackleTop;
    private ColorRect _lockShackleLeft;
    private ColorRect _lockShackleRight;
    private ColorRect _lockBody;

    /// <summary>Node ID from the map region JSON.</summary>
    public string NodeId { get; private set; } = string.Empty;

    /// <summary>Display name for this node.</summary>
    public string NodeName { get; private set; } = string.Empty;

    /// <summary>Whether this node is currently locked.</summary>
    public bool IsLocked { get; private set; } = true;

    private bool _isCleared;

    /// <summary>Emits the node ID when the icon is clicked.</summary>
    [Signal]
    public delegate void NodeSelectedEventHandler(string nodeId);

    private static readonly Dictionary<string, (string symbol, Color color)> TypeConfig = new()
    {
        ["Duel"] = ("\u2694", new Color(0.3f, 0.6f, 0.3f)),       // crossed swords
        ["Elite"] = ("\u26A1", new Color(0.8f, 0.4f, 0.2f)),      // lightning
        ["Warden"] = ("\u265B", new Color(0.9f, 0.7f, 0.1f)),     // chess queen (crown)
        ["WardenBoss"] = ("\u2620", new Color(0.9f, 0.2f, 0.1f)), // skull
        ["Dig"] = ("\u26CF", new Color(0.5f, 0.3f, 0.1f)),        // pick
        ["Shrine"] = ("\u2726", new Color(0.3f, 0.5f, 0.8f)),     // four-pointed star
        ["Cache"] = ("?", new Color(0.7f, 0.4f, 0.7f)),           // question mark
        ["Merchant"] = ("$", new Color(0.8f, 0.7f, 0.3f)),        // dollar sign
    };

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(96, 96);
        _nameLabel = GetNode<Label>("NameLabel");
        _iconCircle = GetNode<ColorRect>("IconCircle");
        _typeChar = GetNode<Label>("TypeChar");
        _glowBorder = GetNode<ColorRect>("GlowBorder");
        _lockOverlay = GetNode<ColorRect>("LockOverlay");
        _clearMark = GetNode<Label>("ClearMark");

        // Build padlock from ColorRects (reliable, no font dependency)
        Color lockColor = new Color(1, 1, 1, 0.9f);
        _lockShackleTop = new ColorRect
        {
            Color = lockColor,
            Position = new Vector2(43, 24),
            Size = new Vector2(10, 4),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _lockShackleLeft = new ColorRect
        {
            Color = lockColor,
            Position = new Vector2(43, 24),
            Size = new Vector2(3, 12),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _lockShackleRight = new ColorRect
        {
            Color = lockColor,
            Position = new Vector2(50, 24),
            Size = new Vector2(3, 12),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _lockBody = new ColorRect
        {
            Color = lockColor,
            Position = new Vector2(41, 36),
            Size = new Vector2(14, 12),
            MouseFilter = MouseFilterEnum.Ignore
        };

        // Group padlock parts under a hidden container (shown when locked)
        var lockGroup = new Control();
        lockGroup.Name = "LockGroup";
        lockGroup.AddChild(_lockShackleTop);
        lockGroup.AddChild(_lockShackleLeft);
        lockGroup.AddChild(_lockShackleRight);
        lockGroup.AddChild(_lockBody);
        AddChild(lockGroup);

        // Hide lock group initially (will be shown by ApplyLockState)
        lockGroup.Hide();
    }

    /// <summary>
    /// Suppress button-level click — map container handles all taps
    /// to keep touch targets correct at any zoom level.
    /// </summary>
    public override void _GuiInput(InputEvent @event)
    {
        // Block button clicks (handled by MapScene._Input at container level)
        if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
        {
            GetViewport().SetInputAsHandled();
            return;
        }
    }

    /// <summary>
    /// Configure this icon from a map node definition.
    /// </summary>
    public void Setup(string nodeId, string displayName, string nodeType, bool locked)
    {
        NodeId = nodeId;
        NodeName = displayName;
        IsLocked = locked;
        _nameLabel.Text = TruncateName(displayName);

        // Look up type config
        if (TypeConfig.TryGetValue(nodeType, out var cfg))
        {
            _typeChar.Text = cfg.symbol;
            _iconCircle.Color = cfg.color;
        }
        else
        {
            _typeChar.Text = "?";
            _iconCircle.Color = new Color(0.5f, 0.5f, 0.5f);
        }

        ApplyLockState(locked);
    }

    /// <summary>
    /// Update the lock state without re-creating the icon.
    /// </summary>
    public void SetLocked(bool locked)
    {
        IsLocked = locked;
        ApplyLockState(locked);
    }

    /// <summary>
    /// Mark this node as cleared (completed). Shows a green checkmark.
    /// </summary>
    public void SetCleared()
    {
        _isCleared = true;
        IsLocked = false;

        // Desaturate the icon circle
        Color baseColor = _iconCircle.Color;
        float gray = baseColor.R * 0.3f + baseColor.G * 0.59f + baseColor.B * 0.11f;
        _iconCircle.Color = new Color(gray, gray, gray, 0.6f);

        // Hide lock overlay
        _lockOverlay.Hide();
        HideLockGroup();

        // Show checkmark
        _clearMark.Show();

        // Remove glow border
        _glowBorder.Color = new Color(0, 0, 0, 0);
        _glowBorder.Modulate = new Color(1, 1, 1, 0.2f);

        // Dim name label
        _nameLabel.Modulate = new Color(0.6f, 0.6f, 0.6f, 0.8f);

        // Dim type char
        _typeChar.Modulate = new Color(1, 1, 1, 0.4f);
    }

    private void ApplyLockState(bool locked)
    {
        if (locked)
        {
            // Locked state: dimmed icon, strong dark overlay, padlock, very dim label
            _iconCircle.Modulate = new Color(0.25f, 0.25f, 0.25f, 0.6f);
            _typeChar.Modulate = new Color(0.4f, 0.4f, 0.4f, 0.3f);
            _lockOverlay.Show();
            _lockOverlay.Color = new Color(0, 0, 0, 0.7f);
            ShowLockGroup();
            _clearMark.Hide();
            _glowBorder.Color = new Color(0, 0, 0, 0);
            _nameLabel.Modulate = new Color(0.4f, 0.38f, 0.35f, 0.5f);
        }
        else
        {
            // Available state: full saturated color, bright white glow, white type char
            _iconCircle.Modulate = new Color(1, 1, 1, 1);
            _typeChar.Modulate = new Color(1, 1, 1, 1);
            _lockOverlay.Hide();
            HideLockGroup();
            _clearMark.Hide();
            _glowBorder.Color = new Color(1f, 0.85f, 0.3f, 0.4f); // gold glow
            _glowBorder.Modulate = new Color(1, 1, 1, 1);
            _nameLabel.Modulate = new Color(1, 1, 0.9f, 1); // bright white-gold text
        }
    }

    private void ShowLockGroup()
    {
        var g = GetNodeOrNull<Control>("LockGroup");
        if (g != null) g.Show();
    }

    private void HideLockGroup()
    {
        var g = GetNodeOrNull<Control>("LockGroup");
        if (g != null) g.Hide();
    }

    /// <summary>
    /// Truncate a name to fit the icon width, adding ellipsis if needed.
    /// </summary>
    private static string TruncateName(string name, int maxLen = 10)
    {
        if (name.Length <= maxLen) return name;
        return name[..(maxLen - 1)] + "\u2026";
    }
}
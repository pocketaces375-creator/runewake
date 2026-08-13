using Godot;

namespace Runewake.Client;

/// <summary>
/// A single icon on the campaign map. Shows node type, lock state, and name.
/// Clickable — emits NodeSelected when tapped.
/// States follow the global UI rule: NEVER communicate state through darkness
/// alone. Locked = icon stays visible + padlock marker + mild desaturation.
/// Selected = persistent gold glow ring.
/// </summary>
public partial class MapNodeIcon : Button
{
    private Label _nameLabel;
    private ColorRect _iconCircle;
    private Label _typeChar;
    private ColorRect _glowBorder;
    private ColorRect _selectedGlow;
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
    private bool _isSelected;

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
        CustomMinimumSize = new Vector2(140, 150);
        _nameLabel = GetNode<Label>("NameLabel");
        _iconCircle = GetNode<ColorRect>("IconCircle");
        _typeChar = GetNode<Label>("TypeChar");
        _glowBorder = GetNode<ColorRect>("GlowBorder");
        _selectedGlow = GetNode<ColorRect>("SelectedGlow");
        _lockOverlay = GetNode<ColorRect>("LockOverlay");
        _clearMark = GetNode<Label>("ClearMark");

        // Build padlock from ColorRects (reliable, no font dependency)
        Color lockColor = new Color(1, 1, 1, 0.95f);
        _lockShackleTop = new ColorRect
        {
            Color = lockColor,
            Position = new Vector2(52, 28),
            Size = new Vector2(12, 5),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _lockShackleLeft = new ColorRect
        {
            Color = lockColor,
            Position = new Vector2(52, 28),
            Size = new Vector2(4, 14),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _lockShackleRight = new ColorRect
        {
            Color = lockColor,
            Position = new Vector2(60, 28),
            Size = new Vector2(4, 14),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _lockBody = new ColorRect
        {
            Color = lockColor,
            Position = new Vector2(50, 42),
            Size = new Vector2(16, 14),
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

        // Hover: brighten the whole icon (button hover style handles the frame)
        MouseEntered += OnHoverEntered;
        MouseExited += OnHoverExited;
    }

    private void OnHoverEntered()
    {
        if (IsLocked || _isCleared) return;
        _iconCircle.Modulate = new Color(1.15f, 1.15f, 1.15f, 1f);
    }

    private void OnHoverExited()
    {
        if (_isSelected)
        {
            _iconCircle.Modulate = new Color(1.08f, 1.08f, 1.08f, 1f);
            return;
        }
        if (IsLocked)
        {
            _iconCircle.Modulate = new Color(0.72f, 0.72f, 0.72f, 1f);
            return;
        }
        _iconCircle.Modulate = new Color(1, 1, 1, 1);
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
    /// Icon stays fully visible — cleared is communicated by the check + dimmed name, not a blackout.
    /// </summary>
    public void SetCleared()
    {
        _isCleared = true;
        IsLocked = false;
        _isSelected = false;

        // Slight desaturation only — icon remains clearly visible
        Color baseColor = _iconCircle.Color;
        float gray = baseColor.R * 0.3f + baseColor.G * 0.59f + baseColor.B * 0.11f;
        _iconCircle.Color = new Color(
            Mathf.Lerp(baseColor.R, gray, 0.45f),
            Mathf.Lerp(baseColor.G, gray, 0.45f),
            Mathf.Lerp(baseColor.B, gray, 0.45f),
            0.85f);

        // Hide lock overlay
        _lockOverlay.Hide();
        HideLockGroup();

        // Show checkmark
        _clearMark.Show();

        // Remove glow border and selection ring
        _glowBorder.Color = new Color(0, 0, 0, 0);
        _glowBorder.Modulate = new Color(1, 1, 1, 0.2f);
        _selectedGlow.Visible = false;

        // Dim name label slightly
        _nameLabel.Modulate = new Color(0.75f, 0.75f, 0.7f, 0.9f);

        // Type char stays visible
        _typeChar.Modulate = new Color(1, 1, 1, 0.8f);
    }

    /// <summary>
    /// Persistent selection ring — gold glow around the icon. First click selects;
    /// the ring stays until another node is selected.
    /// </summary>
    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        if (_isCleared) return;

        if (selected)
        {
            _selectedGlow.Visible = true;
            _selectedGlow.Color = new Color(1f, 0.85f, 0.2f, 0.55f);
            _iconCircle.Modulate = new Color(1.1f, 1.1f, 1.1f, 1f);
            // Also raise the availability glow
            _glowBorder.Color = new Color(1f, 0.85f, 0.3f, 0.5f);
            _glowBorder.Modulate = new Color(1, 1, 1, 1);
        }
        else
        {
            _selectedGlow.Visible = false;
            _iconCircle.Modulate = IsLocked ? new Color(0.72f, 0.72f, 0.72f, 1f) : new Color(1, 1, 1, 1);
        }
    }

    private void ApplyLockState(bool locked)
    {
        if (locked)
        {
            // LOCKED: icon stays visible (global rule — no blackout), padlock badge
            // marks the state. Mild desaturation keeps the type readable.
            _iconCircle.Modulate = new Color(0.72f, 0.72f, 0.72f, 1f);
            _typeChar.Modulate = new Color(1, 1, 1, 0.9f);
            _lockOverlay.Hide(); // no dark veil — padlock is the marker
            ShowLockGroup();
            _clearMark.Hide();
            _glowBorder.Color = new Color(0, 0, 0, 0);
            _selectedGlow.Visible = false;
            _nameLabel.Modulate = new Color(0.55f, 0.52f, 0.48f, 0.95f);
        }
        else
        {
            // Available state: full saturated color, gold glow, white type char
            _iconCircle.Modulate = new Color(1, 1, 1, 1);
            _typeChar.Modulate = new Color(1, 1, 1, 1);
            _lockOverlay.Hide();
            HideLockGroup();
            _clearMark.Hide();
            _glowBorder.Color = new Color(1f, 0.85f, 0.3f, 0.4f); // gold glow
            _glowBorder.Modulate = new Color(1, 1, 1, 1);
            _selectedGlow.Visible = false;
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

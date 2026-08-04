using Godot;

namespace Runewake.Client;

/// <summary>
/// A single icon on the campaign map. Shows node type, lock state, and name.
/// Clickable — emits NodeSelected when tapped.
/// </summary>
public partial class MapNodeIcon : Button
{
    private Label _nameLabel;
    private ColorRect _iconRect;
    private Label _typeLabel;
    private ColorRect _lockOverlay;

    /// <summary>Node ID from the map region JSON.</summary>
    public string NodeId { get; private set; } = string.Empty;

    /// <summary>Display name for this node.</summary>
    public string NodeName { get; private set; } = string.Empty;

    /// <summary>Whether this node is currently locked.</summary>
    public bool IsLocked { get; private set; } = true;

    /// <summary>Emits the node ID when the icon is clicked.</summary>
    [Signal]
    public delegate void NodeSelectedEventHandler(string nodeId);

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(72, 72);
        _nameLabel = GetNode<Label>("NameLabel");
        _iconRect = GetNode<ColorRect>("IconRect");
        _typeLabel = GetNode<Label>("TypeLabel");
        _lockOverlay = GetNode<ColorRect>("LockOverlay");
        Pressed += () => EmitSignal(SignalName.NodeSelected, NodeId);
    }

    /// <summary>
    /// Configure this icon from a map node definition.
    /// </summary>
    public void Setup(string nodeId, string displayName, string nodeType, bool locked)
    {
        NodeId = nodeId;
        NodeName = displayName;
        IsLocked = locked;
        _nameLabel.Text = displayName;
        _typeLabel.Text = nodeType;

        // Color by type
        Color baseColor = nodeType switch
        {
            "Duel" => new Color(0.3f, 0.6f, 0.3f),
            "Elite" => new Color(0.8f, 0.4f, 0.2f),
            "Warden" => new Color(0.9f, 0.7f, 0.1f),
            "WardenBoss" => new Color(0.9f, 0.2f, 0.1f),
            "Dig" => new Color(0.5f, 0.3f, 0.1f),
            "Shrine" => new Color(0.3f, 0.5f, 0.8f),
            "Cache" => new Color(0.7f, 0.4f, 0.7f),
            "Merchant" => new Color(0.8f, 0.7f, 0.3f),
            _ => new Color(0.5f, 0.5f, 0.5f)
        };
        _iconRect.Color = baseColor;

        // Lock state
        if (locked)
        {
            Modulate = new Color(0.4f, 0.4f, 0.4f, 0.7f);
            _lockOverlay.Show();
        }
        else
        {
            Modulate = new Color(1, 1, 1, 1);
            _lockOverlay.Hide();
        }
    }

    /// <summary>
    /// Update the lock state without re-creating the icon.
    /// </summary>
    public void SetLocked(bool locked)
    {
        IsLocked = locked;
        if (locked)
        {
            Modulate = new Color(0.4f, 0.4f, 0.4f, 0.7f);
            _lockOverlay.Show();
        }
        else
        {
            Modulate = new Color(1, 1, 1, 1);
            _lockOverlay.Hide();
        }
    }
}
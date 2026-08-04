using Godot;

namespace Runewake.Client;

/// <summary>
/// A single lane slot on the board. Shows card name and stats when occupied,
/// or remains empty with a subtle border. Supports drag-and-drop for playing
/// cards from hand, and tap selection for attack targeting.
/// </summary>
public partial class LaneSlot : PanelContainer
{
    private Label _cardName;
    private Label _stats;
    private NodeState _state = NodeState.Empty;
    private InputController? _input;

    /// <summary>
    /// Emitted when a card is dropped onto this lane slot.
    /// Parameters: cardId, laneIndex (this slot's index).
    /// </summary>
    [Signal]
    public delegate void CardDroppedEventHandler(string cardId, int laneIndex);

    /// <summary>
    /// Emitted when the player taps this lane slot.
    /// Parameters: laneIndex, isEmpty.
    /// </summary>
    [Signal]
    public delegate void LaneTappedEventHandler(int laneIndex, bool isEmpty);

    public enum NodeState { Empty, Occupied }

    /// <summary>Which row this lane belongs to: 0 = enemy, 1 = player.</summary>
    public int Row { get; set; }

    /// <summary>Lane index (0–4).</summary>
    public int LaneIndex { get; set; }

    public override void _Ready()
    {
        _cardName = GetNode<Label>("VBox/CardName");
        _stats = GetNode<Label>("VBox/Stats");
        SetEmpty();
    }

    /// <summary>
    /// Set this lane slot to show card info.
    /// </summary>
    public void SetCard(string name, int attack, int vigor)
    {
        _cardName.Text = name;
        _stats.Text = $"{attack}/{vigor}";
        _state = NodeState.Occupied;
        _cardName.Show();
        _stats.Show();
    }

    /// <summary>
    /// Clear this lane slot back to empty.
    /// </summary>
    public void SetEmpty()
    {
        _cardName.Text = "";
        _stats.Text = "";
        _state = NodeState.Empty;
        _cardName.Hide();
        _stats.Hide();
    }

    /// <summary>
    /// Show visual feedback for being a valid attack target (highlight border).
    /// </summary>
    public void Highlight()
    {
        Modulate = new Color(1, 1, 0.8f, 1);
    }

    /// <summary>
    /// Remove highlight effect.
    /// </summary>
    public void Unhighlight()
    {
        Modulate = new Color(1, 1, 1, 1);
    }

    // ——— Drag-and-drop target ———

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        // Only accept drops on player lane slots (row 1) that are empty
        if (Row != 1 || _state == NodeState.Occupied)
            return false;

        if (data.VariantType != Variant.Type.Dictionary)
            return false;

        var dict = data.AsGodotDictionary();
        return dict.ContainsKey("type") && dict["type"].AsString() == "hand_card";
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var dict = data.AsGodotDictionary();
        string cardId = dict["card_id"].AsString();
        EmitSignal(SignalName.CardDropped, cardId, LaneIndex);
    }

    // ——— Tap handling ———

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
        {
            EmitSignal(SignalName.LaneTapped, LaneIndex, _state == NodeState.Empty);
            GetViewport().SetInputAsHandled();
        }
    }
}
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
    /// Previous vigor value for computing damage/heal diffs.
    /// </summary>
    public int PreviousVigor { get; set; } = -1;

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

    // ——— Animation effects ———

    /// <summary>
    /// Play a summon animation: scale from 0 to 1 with a brief strata-colored flash.
    /// State update happens before this, so animations never block gameplay.
    /// </summary>
    public void PlaySummonEffect()
    {
        Scale = new Vector2(0, 0);
        Modulate = new Color(2, 2, 2, 1); // brief bright flash

        var tween = CreateTween();
        tween.SetParallel();
        tween.TweenProperty(this, "scale", new Vector2(1, 1), 0.3f)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(this, "modulate", new Color(1, 1, 1, 1), 0.2f);
    }

    /// <summary>
    /// Play a death animation: flash red, then fade out and shrink.
    /// Resets scale and alpha for reuse when the lane slot is re-populated.
    /// State update happens before this — the visual is purely cosmetic.
    /// </summary>
    public void PlayDeathEffect()
    {
        // Flash red
        Modulate = new Color(1, 0.2f, 0.2f, 1);

        var tween = CreateTween();
        tween.TweenInterval(0.1f); // hold red flash
        tween.SetParallel();
        tween.TweenProperty(this, "modulate:a", 0.0f, 0.4f);
        tween.TweenProperty(this, "scale", new Vector2(0, 0), 0.4f)
            .SetEase(Tween.EaseType.In)
            .SetTrans(Tween.TransitionType.Back);
        tween.SetParallel(false);
        tween.TweenCallback(Callable.From(() =>
        {
            // Reset for reuse when a new creature is summoned
            Modulate = new Color(1, 1, 1, 1);
            Scale = new Vector2(1, 1);
        }));
    }

    /// <summary>
    /// Show a floating damage number (red) at this lane's position.
    /// </summary>
    public void ShowDamageNumber(int amount)
    {
        if (amount <= 0) return;
        var ftScene = GD.Load<PackedScene>("res://scenes/effects/FloatingText.tscn");
        var ft = ftScene.Instantiate<FloatingText>();
        GetParent().AddChild(ft);
        ft.ShowAt($"-{amount}", new Color(1, 0.2f, 0.2f), GlobalPosition + new Vector2(32, 0));
    }

    /// <summary>
    /// Show a floating heal number (green) at this lane's position.
    /// </summary>
    public void ShowHealNumber(int amount)
    {
        if (amount <= 0) return;
        var ftScene = GD.Load<PackedScene>("res://scenes/effects/FloatingText.tscn");
        var ft = ftScene.Instantiate<FloatingText>();
        GetParent().AddChild(ft);
        ft.ShowAt($"+{amount}", new Color(0.2f, 1, 0.2f), GlobalPosition + new Vector2(32, 0));
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
using Godot;

namespace Runewake.Client;

/// <summary>
/// A single lane slot on the board. Shows card name and stats when occupied,
/// or remains empty with a subtle border. Contains helpers for setting state
/// from a card definition later.
/// </summary>
public partial class LaneSlot : PanelContainer
{
    private Label _cardName;
    private Label _stats;
    private NodeState _state = NodeState.Empty;

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
}
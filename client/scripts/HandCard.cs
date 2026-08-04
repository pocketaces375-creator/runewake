using Godot;

namespace Runewake.Client;

/// <summary>
/// A card in the player's hand, rendered as a small tappable button.
/// Will later be wired to drag-to-lane gameplay.
/// </summary>
public partial class HandCard : Button
{
    private Label _cardName;
    private Label _costLabel;

    /// <summary>Card's unique identifier from the engine.</summary>
    public string CardId { get; private set; } = "";

    public override void _Ready()
    {
        _cardName = GetNode<Label>("Margin/VBox/CardName");
        _costLabel = GetNode<Label>("Margin/VBox/CostLabel");
    }

    /// <summary>
    /// Configure this hand card widget with card data.
    /// </summary>
    public void SetCard(string cardId, string name, int cost)
    {
        CardId = cardId;
        _cardName.Text = name;
        _costLabel.Text = cost.ToString();
    }
}
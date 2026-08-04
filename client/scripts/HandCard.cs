using Godot;

namespace Runewake.Client;

/// <summary>
/// A card in the player's hand, rendered as a small tappable button.
/// Supports drag-and-drop to lane slots for playing cards, and tap
/// for entering attack/card-selection mode.
/// </summary>
public partial class HandCard : Button
{
    private Label _cardName;
    private Label _costLabel;

    /// <summary>Card's unique identifier from the engine.</summary>
    public string CardId { get; private set; } = "";

    /// <summary>Card's display name.</summary>
    public string CardName { get; private set; } = "";

    /// <summary>Attunement cost to play this card.</summary>
    public int CardCost { get; private set; }

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
        CardName = name;
        CardCost = cost;
        _cardName.Text = name;
        _costLabel.Text = cost.ToString();
    }

    // ——— Drag-and-drop support ———

    public override Variant _GetDragData(Vector2 atPosition)
    {
        // Create drag preview — a semi-transparent copy of this card
        var preview = new Label();
        preview.Text = CardName;
        preview.Size = new Vector2(80, 24);
        preview.Modulate = new Color(1, 1, 1, 0.7f);
        SetDragPreview(preview);

        // Return card data for the drop target
        var data = new Godot.Collections.Dictionary
        {
            ["type"] = "hand_card",
            ["card_id"] = CardId,
            ["card_name"] = CardName,
            ["card_cost"] = CardCost
        };
        return data;
    }
}
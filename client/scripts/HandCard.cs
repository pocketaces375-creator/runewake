using Godot;
using Runewake.Engine.Cards;

namespace Runewake.Client;

/// <summary>
/// A card in the player's hand, rendered as a tappable card thumbnail.
/// Shows card name, cost badge, and a strata-colored top stripe.
/// Supports drag-and-drop to lane slots for playing cards.
/// </summary>
public partial class HandCard : Button
{
    private Label _cardName;
    private Label _costLabel;
    private ColorRect _strataStrip;
    private ColorRect _cardBg;

    /// <summary>Card's unique identifier from the engine.</summary>
    public string CardId { get; private set; } = "";

    /// <summary>Card's display name.</summary>
    public string CardName { get; private set; } = "";

    /// <summary>Attunement cost to play this card.</summary>
    public int CardCost { get; private set; }

    /// <summary>Card's strata for color coding.</summary>
    public Strata CardStrata { get; private set; }

    public override void _Ready()
    {
        _cardName = GetNode<Label>("Margin/VBox/CardName");
        _costLabel = GetNode<Label>("Margin/VBox/Header/CostBadge/CostLabel");
        _strataStrip = GetNode<ColorRect>("Margin/VBox/StrataStrip");
        _cardBg = GetNode<ColorRect>("CardBg");
    }

    /// <summary>
    /// Configure this hand card widget with card data.
    /// </summary>
    public void SetCard(string cardId, string name, int cost, Strata strata)
    {
        CardId = cardId;
        CardName = name;
        CardCost = cost;
        CardStrata = strata;

        _cardName.Text = name;
        _costLabel.Text = cost.ToString();

        // Set strata color
        var color = GetStrataColor(strata);
        _strataStrip.Color = color;
    }

    /// <summary>
    /// Get the strata color for display.
    /// </summary>
    private static Color GetStrataColor(Strata strata) => strata switch
    {
        Strata.VERDANT => new Color(0.2f, 0.7f, 0.3f),
        Strata.EMBER => new Color(0.9f, 0.3f, 0.1f),
        Strata.TIDE => new Color(0.2f, 0.5f, 0.8f),
        Strata.HOLLOW => new Color(0.5f, 0.2f, 0.5f),
        Strata.DAWN => new Color(0.9f, 0.8f, 0.2f),
        _ => new Color(0.5f, 0.5f, 0.5f)
    };

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
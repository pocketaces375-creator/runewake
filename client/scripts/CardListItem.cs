using Godot;

namespace Runewake.Client;

/// <summary>
/// Compact card list item for deck builder collection/deck lists.
/// Shows cost badge, name, strata color, and quantity badge.
/// Emits signals for click and add/remove.
/// </summary>
public partial class CardListItem : Button
{
    private Label _costLabel;
    private Label _nameLabel;
    private Label _typeLabel;
    private Label _countLabel;
    private ColorRect _strataColor;

    /// <summary>Card ID this item represents.</summary>
    public string CardId { get; private set; } = string.Empty;

    /// <summary>Whether this item is in the player's deck currently.</summary>
    public bool InDeck { get; set; }

    [Signal]
    public delegate void ItemClickedEventHandler(string cardId);

    [Signal]
    public delegate void AddRequestedEventHandler(string cardId);

    [Signal]
    public delegate void RemoveRequestedEventHandler(string cardId);

    public override void _Ready()
    {
        _costLabel = GetNode<Label>("HBox/CostLabel");
        _nameLabel = GetNode<Label>("HBox/NameLabel");
        _typeLabel = GetNode<Label>("HBox/TypeLabel");
        _countLabel = GetNode<Label>("CountLabel");
        _strataColor = GetNode<ColorRect>("StrataColor");
        Pressed += () => EmitSignal(SignalName.ItemClicked, CardId);
    }

    public void Setup(string cardId, string name, int cost, string typeStr, string strata,
        int ownedCount, int inDeckCount, bool isInDeckList)
    {
        CardId = cardId;
        InDeck = isInDeckList;
        _costLabel.Text = cost.ToString();
        _nameLabel.Text = name;
        _typeLabel.Text = typeStr;

        // Strata color
        _strataColor.Color = strata.ToUpperInvariant() switch
        {
            "VERDANT" => new Color(0.2f, 0.6f, 0.2f),
            "EMBER" => new Color(0.8f, 0.3f, 0.1f),
            "TIDE" => new Color(0.2f, 0.4f, 0.7f),
            "HOLLOW" => new Color(0.5f, 0.2f, 0.5f),
            "DAWN" => new Color(0.8f, 0.7f, 0.2f),
            _ => new Color(0.5f, 0.5f, 0.5f)
        };

        // Show count badge for collection view
        int remaining = ownedCount - inDeckCount;
        if (isInDeckList)
        {
            _countLabel.Text = inDeckCount > 0 ? $"×{inDeckCount}" : "";
            _countLabel.Show();
        }
        else
        {
            _countLabel.Text = remaining > 0 ? $"{remaining}" : "";
            if (remaining <= 0)
                Modulate = new Color(0.4f, 0.4f, 0.4f, 0.5f);
            else
                Modulate = new Color(1, 1, 1, 1);
            _countLabel.Show();
        }
    }
}
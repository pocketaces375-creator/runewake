using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Deck builder screen — browse card collection, filter by strata/type/cost,
/// build a 30-card deck, and save it.
/// </summary>
public partial class DeckBuilderScene : Control
{
    // Node references
    private Button _backButton;
    private Button _saveButton;
    private Label _deckCountLabel;
    private LineEdit _searchBar;
    private OptionButton _strataFilter;
    private OptionButton _typeFilter;
    private SpinBox _costMin;
    private SpinBox _costMax;
    private ScrollContainer _collectionScroll;
    private VBoxContainer _collectionList;
    private ScrollContainer _deckScroll;
    private VBoxContainer _deckList;
    private CardView _cardDetail;
    private PanelContainer _detailPanel;

    // Data
    private readonly List<CardDef> _allCards = new();
    private readonly List<string> _deckCardIds = new();
    private ProgressionState? _saveState;
    private string? _selectedCardId;

    // Available strata for filter
    private static readonly string[] StrataOptions = { "All", "VERDANT", "EMBER", "TIDE", "HOLLOW", "DAWN" };
    private static readonly string[] TypeOptions = { "All", "CREATURE", "RITUAL", "RELIC" };

    public override void _Ready()
    {
        // Wire nodes
        _backButton = GetNode<Button>("TopBar/BackButton");
        _saveButton = GetNode<Button>("BottomBar/SaveButton");
        _deckCountLabel = GetNode<Label>("DeckPanel/DeckHeader/DeckCountLabel");
        _searchBar = GetNode<LineEdit>("CollectionPanel/Filters/SearchBar");
        _strataFilter = GetNode<OptionButton>("CollectionPanel/Filters/StrataFilter");
        _typeFilter = GetNode<OptionButton>("CollectionPanel/Filters/TypeFilter");
        _costMin = GetNode<SpinBox>("CollectionPanel/Filters/CostRange/CostMin");
        _costMax = GetNode<SpinBox>("CollectionPanel/Filters/CostRange/CostMax");
        _collectionScroll = GetNode<ScrollContainer>("CollectionPanel/CollectionScroll");
        _collectionList = GetNode<VBoxContainer>("CollectionPanel/CollectionScroll/CollectionList");
        _deckScroll = GetNode<ScrollContainer>("DeckPanel/DeckScroll");
        _deckList = GetNode<VBoxContainer>("DeckPanel/DeckScroll/DeckList");
        _cardDetail = GetNode<CardView>("CardDetail/DetailCardView");
        _detailPanel = GetNode<PanelContainer>("CardDetail");

        // Setup filters
        foreach (var s in StrataOptions) _strataFilter.AddItem(s);
        foreach (var t in TypeOptions) _typeFilter.AddItem(t);
        _strataFilter.Select(0);
        _typeFilter.Select(0);
        _costMin.Value = 0;
        _costMax.Value = 10;

        // Wire signals
        _backButton.Pressed += () => GD.Print("[DeckBuilder] Back pressed");
        _saveButton.Pressed += OnSaveDeck;
        _searchBar.TextChanged += _ => RefreshCollection();
        _strataFilter.ItemSelected += _ => RefreshCollection();
        _typeFilter.ItemSelected += _ => RefreshCollection();
        _costMin.ValueChanged += _ => RefreshCollection();
        _costMax.ValueChanged += _ => RefreshCollection();

        _detailPanel.Hide();

        // Load cards
        LoadCards();
        Refresh();
    }

    /// <summary>Set the save state for collection data.</summary>
    public void SetSaveState(ProgressionState state)
    {
        _saveState = state;
        RefreshCollection();
    }

    /// <summary>Get the current deck card IDs.</summary>
    public List<string> GetDeckCardIds() => new(_deckCardIds);

    private void LoadCards()
    {
        _allCards.Clear();
        // Load from all 5 pack files
        string contentDir = ProjectSettings.GlobalizePath("res://content/cards");
        var packs = new[]
        {
            "verdant.json", "ember.json", "tide.json", "hollow.json", "dawn.json"
        };
        foreach (var pack in packs)
        {
            var cards = CardLoader.LoadPack($"{contentDir}/{pack}");
            _allCards.AddRange(cards);
        }
    }

    public void Refresh()
    {
        RefreshCollection();
        RefreshDeck();
    }

    // ——— Collection ———

    private void RefreshCollection()
    {
        // Clear existing items
        foreach (var child in _collectionList.GetChildren())
            child.QueueFree();

        // Apply filters
        string search = _searchBar.Text.Trim().ToLowerInvariant();
        string strata = StrataOptions[_strataFilter.Selected];
        string type = TypeOptions[_typeFilter.Selected];
        int minCost = (int)_costMin.Value;
        int maxCost = (int)_costMax.Value;

        var filtered = _allCards
            .Where(c => search.Length == 0 || c.Name.ToLowerInvariant().Contains(search))
            .Where(c => strata == "All" || c.Strata.ToString() == strata)
            .Where(c => type == "All" || c.Type.ToString() == type)
            .Where(c => c.Cost >= minCost && c.Cost <= maxCost)
            .OrderBy(c => c.Cost)
            .ThenBy(c => c.Name)
            .ToList();

        int ownedCount = 0;
        foreach (var card in filtered)
        {
            int owned = _saveState?.Collection.GetValueOrDefault(card.Id, 0) ?? 0;
            int inDeck = _deckCardIds.Count(id => id == card.Id);
            ownedCount += owned;

            var itemScene = GD.Load<PackedScene>("res://scenes/components/CardListItem.tscn");
            var item = itemScene.Instantiate<CardListItem>();
            string typeStr = card.Type == Engine.Cards.CardType.CREATURE ? "CREATURE" : card.Type.ToString();
            item.Setup(card.Id, card.Name, card.Cost, typeStr,
                card.Strata.ToString(), owned, inDeck, false);
            item.ItemClicked += (id) => ShowCardDetail(id);
            item.AddRequested += (id) => AddToDeck(id);
            _collectionList.AddChild(item);

            // Double-click / right-click to add
            item.GuiInput += (@event) =>
            {
                if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Right)
                    AddToDeck(card.Id);
            };
        }

        // Empty state
        if (filtered.Count == 0)
        {
            var empty = new Label();
            empty.Text = "No cards match your filters.";
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.AddThemeFontSizeOverride("font_size", 12);
            empty.Modulate = new Color(0.6f, 0.6f, 0.6f);
            _collectionList.AddChild(empty);
        }
    }

    // ——— Deck ———

    private void RefreshDeck()
    {
        foreach (var child in _deckList.GetChildren())
            child.QueueFree();

        // Group deck cards by ID for count display
        var grouped = _deckCardIds
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var (cardId, count) in grouped)
        {
            var def = _allCards.FirstOrDefault(c => c.Id == cardId);
            if (def == null) continue;

            var itemScene = GD.Load<PackedScene>("res://scenes/components/CardListItem.tscn");
            var item = itemScene.Instantiate<CardListItem>();
            string typeStr = def.Type == Engine.Cards.CardType.CREATURE ? "CREATURE" : def.Type.ToString();
            int owned = _saveState?.Collection.GetValueOrDefault(cardId, 0) ?? 0;
            item.Setup(cardId, def.Name, def.Cost, typeStr, def.Strata.ToString(), owned, count, true);
            item.ItemClicked += (id) => ShowCardDetail(id);
            item.RemoveRequested += (id) => RemoveFromDeck(id);
            _deckList.AddChild(item);

            item.GuiInput += (@event) =>
            {
                if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Right)
                    RemoveFromDeck(cardId);
            };
        }

        _deckCountLabel.Text = $"Deck ({_deckCardIds.Count}/30)";
        _saveButton.Disabled = _deckCardIds.Count != 30;
    }

    // ——— Add / Remove ———

    private void AddToDeck(string cardId)
    {
        if (_deckCardIds.Count >= 30) return;

        // Check if player has enough copies
        int owned = _saveState?.Collection.GetValueOrDefault(cardId, 0) ?? 0;
        int inDeck = _deckCardIds.Count(id => id == cardId);
        if (inDeck >= owned) return;

        _deckCardIds.Add(cardId);
        RefreshCollection();
        RefreshDeck();
    }

    private void RemoveFromDeck(string cardId)
    {
        int idx = _deckCardIds.LastIndexOf(cardId);
        if (idx < 0) return;
        _deckCardIds.RemoveAt(idx);
        RefreshCollection();
        RefreshDeck();
    }

    // ——— Card detail ———

    private void ShowCardDetail(string cardId)
    {
        _selectedCardId = cardId;
        var def = _allCards.FirstOrDefault(c => c.Id == cardId);
        if (def == null) return;

        _cardDetail.SetCard(def);
        _detailPanel.Show();
    }

    // ——— Save ———

    private void OnSaveDeck()
    {
        if (_deckCardIds.Count != 30) return;
        GD.Print($"[DeckBuilder] Saved deck with {_deckCardIds.Count} cards");
        // Future: persist with ProgressionState reference
        _saveButton.Text = "Saved!";
        var timer = GetTree().CreateTimer(1.5f);
        timer.Timeout += () => _saveButton.Text = "Save Deck";
    }
}
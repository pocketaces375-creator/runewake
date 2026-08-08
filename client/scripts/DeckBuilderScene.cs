using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Deck builder screen — all UI constructed in code (no tscn export traps).
/// Browse card collection, filter by strata/type/cost/rarity, build a 30-card
/// deck validated by the engine, and save through the persistence layer.
/// </summary>
public partial class DeckBuilderScene : Control
{
    // Node references
    private VBoxContainer _collectionList;
    private VBoxContainer _deckList;
    private LineEdit _searchBar;
    private OptionButton _strataFilter;
    private OptionButton _typeFilter;
    private OptionButton _rarityFilter;
    private SpinBox _costMin;
    private SpinBox _costMax;
    private Label _deckCountLabel;
    private Label _validationStatus;
    private Button _saveButton;
    private PanelContainer _detailPanel;
    private Label _detailCard;

    // Data
    private readonly List<CardDef> _allCards = new();
    private readonly List<string> _deckCardIds = new();
    private ProgressionState? _saveState;
    private string? _selectedCardId;

    // Filter options
    private static readonly string[] StrataOptions = { "All", "VERDANT", "EMBER", "TIDE", "HOLLOW", "DAWN" };
    private static readonly string[] TypeOptions = { "All", "CREATURE", "RITUAL", "RELIC" };
    private static readonly string[] RarityOptions = { "All", "COMMON", "UNCOMMON", "RARE", "RELIC" };
    private const int ValidDeckSize = 30;

    public override void _Ready()
    {
        // Ensure campaign data is loaded (standalone export guard)
        if (!CampaignContext.SaveManager.IsLoaded)
            CampaignContext.SaveManager.Initialize();
        if (CampaignContext.EncounterIndex.Count == 0)
        {
            CampaignContext.LoadEncounters();
            CampaignContext.LoadDigSites();
        }

        BuildUI();
        LoadCards();
        if (CampaignContext.Progression.DeckCardIds.Count == ValidDeckSize)
            _deckCardIds.AddRange(CampaignContext.Progression.DeckCardIds);
        Refresh();
    }

    public void SetSaveState(ProgressionState state) { _saveState = state; RefreshCollection(); }
    public List<string> GetDeckCardIds() => new(_deckCardIds);

    // ——— UI Construction ——— //

    private void BuildUI()
    {
        // Background
        AddChild(new ColorRect
        {
            Color = new Color(0.08f, 0.08f, 0.12f),
            AnchorLeft = 0, AnchorRight = 1,
            AnchorTop = 0, AnchorBottom = 1
        });

        // Top bar
        var topBar = new HBoxContainer();
        topBar.AnchorLeft = 0; topBar.AnchorRight = 1;
        topBar.AnchorTop = 0; topBar.AnchorBottom = 0;
        topBar.OffsetBottom = 36;
        topBar.AddThemeConstantOverride("separation", 8);
        AddChild(topBar);

        var backBtn = new Button { Text = "\u2190 Back" };
        backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn");
        topBar.AddChild(backBtn);

        var title = new Label { Text = "Deck Builder" };
        title.SizeFlagsHorizontal = (Control.SizeFlags)3;
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 16);
        topBar.AddChild(title);

        // Bottom bar
        var bottomBar = new HBoxContainer();
        bottomBar.AnchorLeft = 0; bottomBar.AnchorRight = 1;
        bottomBar.AnchorTop = 1; bottomBar.AnchorBottom = 1;
        bottomBar.OffsetTop = -36;
        bottomBar.AddThemeConstantOverride("separation", 8);
        AddChild(bottomBar);

        _saveButton = new Button { Text = "Save Deck (0/30)", Disabled = true };
        _saveButton.SizeFlagsHorizontal = (Control.SizeFlags)3;
        _saveButton.Pressed += OnSaveDeck;
        bottomBar.AddChild(_saveButton);

        // Main area: HSplitContainer (spans between topbar and bottom bar)
        var main = new HSplitContainer();
        main.AnchorLeft = 0; main.AnchorRight = 1;
        main.AnchorTop = 0; main.AnchorBottom = 1;
        main.OffsetTop = 36;
        main.OffsetBottom = -36;
        AddChild(main);

        // ——— Left: Collection Panel ——— //
        var colPanel = MakePanel();
        colPanel.SizeFlagsHorizontal = (Control.SizeFlags)3;
        colPanel.SizeFlagsVertical = (Control.SizeFlags)3;
        main.AddChild(colPanel);

        var colInner = new VBoxContainer();
        colInner.AnchorLeft = 0; colInner.AnchorRight = 1;
        colInner.AnchorTop = 0; colInner.AnchorBottom = 1;
        colInner.AddThemeConstantOverride("separation", 4);
        colPanel.AddChild(colInner);

        _searchBar = new LineEdit { PlaceholderText = "Search cards..." };
        _searchBar.SizeFlagsHorizontal = (Control.SizeFlags)3;
        _searchBar.TextChanged += _ => RefreshCollection();
        colInner.AddChild(_searchBar);

        _strataFilter = MakeFilterDropdown(StrataOptions, "Strata");
        _strataFilter.ItemSelected += _ => RefreshCollection();
        colInner.AddChild(_strataFilter);

        _typeFilter = MakeFilterDropdown(TypeOptions, "Type");
        _typeFilter.ItemSelected += _ => RefreshCollection();
        colInner.AddChild(_typeFilter);

        _rarityFilter = MakeFilterDropdown(RarityOptions, "Rarity");
        _rarityFilter.ItemSelected += _ => RefreshCollection();
        colInner.AddChild(_rarityFilter);

        var costRow = new HBoxContainer();
        costRow.SizeFlagsHorizontal = (Control.SizeFlags)3;
        colInner.AddChild(costRow);

        costRow.AddChild(new Label { Text = "Cost:" });
        _costMin = new SpinBox { MinValue = 0, MaxValue = 10, Value = 0 };
        _costMin.SizeFlagsHorizontal = (Control.SizeFlags)3;
        _costMin.ValueChanged += _ => RefreshCollection();
        costRow.AddChild(_costMin);

        costRow.AddChild(new Label { Text = "\u2014" });
        _costMax = new SpinBox { MinValue = 0, MaxValue = 10, Value = 10 };
        _costMax.SizeFlagsHorizontal = (Control.SizeFlags)3;
        _costMax.ValueChanged += _ => RefreshCollection();
        costRow.AddChild(_costMax);

        var colScroll = new ScrollContainer();
        colScroll.SizeFlagsVertical = (Control.SizeFlags)3;
        colInner.AddChild(colScroll);

        _collectionList = new VBoxContainer();
        _collectionList.SizeFlagsHorizontal = (Control.SizeFlags)3;
        colScroll.AddChild(_collectionList);

        // ——— Right: Deck Panel ——— //
        var deckPanel = MakePanel();
        deckPanel.SizeFlagsHorizontal = (Control.SizeFlags)2;
        deckPanel.SizeFlagsVertical = (Control.SizeFlags)3;
        main.AddChild(deckPanel);

        var deckInner = new VBoxContainer();
        deckInner.AnchorLeft = 0; deckInner.AnchorRight = 1;
        deckInner.AnchorTop = 0; deckInner.AnchorBottom = 1;
        deckInner.AddThemeConstantOverride("separation", 4);
        deckPanel.AddChild(deckInner);

        var deckHeader = new HBoxContainer();
        deckHeader.SizeFlagsHorizontal = (Control.SizeFlags)3;
        deckInner.AddChild(deckHeader);

        _deckCountLabel = new Label { Text = "Deck (0/30)" };
        _deckCountLabel.SizeFlagsHorizontal = (Control.SizeFlags)3;
        _deckCountLabel.AddThemeFontSizeOverride("font_size", 12);
        deckHeader.AddChild(_deckCountLabel);

        _validationStatus = new Label();
        _validationStatus.SizeFlagsHorizontal = (Control.SizeFlags)3;
        deckInner.AddChild(_validationStatus);

        var deckScroll = new ScrollContainer();
        deckScroll.SizeFlagsVertical = (Control.SizeFlags)3;
        deckInner.AddChild(deckScroll);

        _deckList = new VBoxContainer();
        _deckList.SizeFlagsHorizontal = (Control.SizeFlags)3;
        deckScroll.AddChild(_deckList);

        // Card detail popup (hidden)
        _detailPanel = new PanelContainer();
        _detailPanel.AnchorLeft = 0.15f; _detailPanel.AnchorRight = 0.55f;
        _detailPanel.AnchorTop = 0.2f; _detailPanel.AnchorBottom = 0.7f;
        _detailPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_detailPanel);

        _detailCard = new Label();
        _detailCard.SizeFlagsHorizontal = (Control.SizeFlags)3;
        _detailCard.SizeFlagsVertical = (Control.SizeFlags)3;
        _detailCard.HorizontalAlignment = HorizontalAlignment.Center;
        _detailCard.VerticalAlignment = VerticalAlignment.Center;
        _detailCard.AddThemeFontSizeOverride("font_size", 14);
        _detailPanel.AddChild(_detailCard);
        _detailPanel.Hide();
    }

    private static Panel MakePanel()
    {
        var p = new Panel();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.1f, 0.1f, 0.14f),
            BorderColor = new Color(0.2f, 0.2f, 0.25f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1
        };
        p.AddThemeStyleboxOverride("panel", style);
        return p;
    }

    private static OptionButton MakeFilterDropdown(string[] options, string defaultText)
    {
        var btn = new OptionButton { Text = defaultText };
        btn.SizeFlagsHorizontal = (Control.SizeFlags)3;
        foreach (var o in options) btn.AddItem(o);
        btn.Select(0);
        return btn;
    }

    // ——— Card Loading ——— //

    private void LoadCards()
    {
        _allCards.Clear();
        var packs = new[] {
            "res://content/cards/verdant.json", "res://content/cards/ember.json",
            "res://content/cards/tide.json", "res://content/cards/hollow.json",
            "res://content/cards/dawn.json"
        };
        foreach (var pack in packs)
        {
            string json = Godot.FileAccess.GetFileAsString(pack);
            _allCards.AddRange(CardLoader.LoadPackFromString(json));
        }
    }

    // ——— Refresh ——— //

    public void Refresh()
    {
        RefreshCollection();
        RefreshDeck();
        RefreshValidation();
    }

    private void RefreshCollection()
    {
        foreach (var child in _collectionList.GetChildren())
            child.QueueFree();

        string search = _searchBar.Text.Trim().ToLowerInvariant();
        string strata = StrataOptions[_strataFilter.Selected];
        string type = TypeOptions[_typeFilter.Selected];
        string rarity = RarityOptions[_rarityFilter.Selected];
        int minCost = (int)_costMin.Value;
        int maxCost = (int)_costMax.Value;

        var filtered = _allCards
            .Where(c => search.Length == 0 || c.Name.ToLowerInvariant().Contains(search))
            .Where(c => strata == "All" || c.Strata.ToString() == strata)
            .Where(c => type == "All" || c.Type.ToString() == type)
            .Where(c => rarity == "All" || c.Rarity.ToString() == rarity)
            .Where(c => c.Cost >= minCost && c.Cost <= maxCost)
            .OrderBy(c => c.Cost)
            .ThenBy(c => c.Name)
            .ToList();

        foreach (var card in filtered)
        {
            int owned = _saveState?.Collection.GetValueOrDefault(card.Id, 0) ?? 0;
            int inDeck = _deckCardIds.Count(id => id == card.Id);

            var item = MakeCardListItem(card.Id, card.Name, card.Cost,
                card.Type.ToString(), card.Strata.ToString(), card.Rarity.ToString(),
                owned, inDeck, false, AddToDeck);
            _collectionList.AddChild(item);
        }

        if (filtered.Count == 0)
        {
            _collectionList.AddChild(new Label
            {
                Text = "No cards match your filters.",
                HorizontalAlignment = HorizontalAlignment.Center,
                Modulate = new Color(0.6f, 0.6f, 0.6f)
            });
        }
    }

    private void RefreshDeck()
    {
        foreach (var child in _deckList.GetChildren())
            child.QueueFree();

        var grouped = _deckCardIds
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var (cardId, count) in grouped)
        {
            var def = _allCards.FirstOrDefault(c => c.Id == cardId);
            if (def == null) continue;

            var item = MakeCardListItem(cardId, def.Name, def.Cost,
                def.Type.ToString(), def.Strata.ToString(), def.Rarity.ToString(),
                _saveState?.Collection.GetValueOrDefault(cardId, 0) ?? 0, count, true,
                RemoveFromDeck);
            _deckList.AddChild(item);
        }

        _deckCountLabel.Text = $"Deck ({_deckCardIds.Count}/30)";
    }

    // ——— CardListItem (code-only, no tscn dependency) ——— //

    private Panel MakeCardListItem(string id, string name, int cost, string typeStr,
        string strata, string rarity, int ownedCount, int inDeckCount, bool isDeckList,
        Action<string>? onAction = null)
    {
        var panel = new Panel();
        panel.CustomMinimumSize = new Vector2(0, 34);
        panel.SizeFlagsHorizontal = (Control.SizeFlags)3;
        panel.MouseDefaultCursorShape = CursorShape.PointingHand;

        int remaining = ownedCount - inDeckCount;
        bool isUnowned = ownedCount == 0 && !isDeckList;

        // Background style
        var bgStyle = new StyleBoxFlat { BgColor = new Color(0.12f, 0.12f, 0.16f) };
        bgStyle.BorderWidthLeft = 4;
        bgStyle.BorderColor = strata.ToUpperInvariant() switch
        {
            "VERDANT" => new Color(0.2f, 0.6f, 0.2f),
            "EMBER" => new Color(0.8f, 0.3f, 0.1f),
            "TIDE" => new Color(0.2f, 0.4f, 0.7f),
            "HOLLOW" => new Color(0.5f, 0.2f, 0.5f),
            "DAWN" => new Color(0.8f, 0.7f, 0.2f),
            _ => new Color(0.5f, 0.5f, 0.5f)
        };
        panel.AddThemeStyleboxOverride("panel", bgStyle);

        // Row text
        var hbox = new HBoxContainer();
        hbox.AnchorLeft = 0; hbox.AnchorRight = 1;
        hbox.AnchorTop = 0; hbox.AnchorBottom = 1;
        hbox.OffsetLeft = 8;
        hbox.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.AddChild(hbox);

        hbox.AddChild(MakeSmallLabel(cost.ToString(), 12, 20));
        hbox.AddChild(MakeSmallLabel(name, 11, 0));
        hbox.AddChild(MakeSmallLabel(typeStr, 9, 0));

        // Rarity badge
        string rarityChar = rarity switch
        {
            "COMMON" => "C", "UNCOMMON" => "U",
            "RARE" => "R", "RELIC" => "L",
            _ => "?"
        };
        var rl = MakeSmallLabel(rarityChar, 9, 14);
        rl.HorizontalAlignment = HorizontalAlignment.Right;
        hbox.AddChild(rl);

        // Count badge
        var countText = isDeckList
            ? (inDeckCount > 0 ? $"\u00d7{inDeckCount}" : "")
            : (isUnowned ? "\u2716" : (remaining > 0 ? $"{remaining}" : ""));
        var cl = MakeSmallLabel(countText, 9, 20);
        cl.HorizontalAlignment = HorizontalAlignment.Right;
        cl.AnchorLeft = 1; cl.AnchorRight = 1;
        cl.AnchorTop = 0; cl.AnchorBottom = 1;
        cl.OffsetLeft = -22;
        panel.AddChild(cl);

        // Click handling via transparent button overlay
        var clickArea = new Button();
        clickArea.AnchorLeft = 0; clickArea.AnchorRight = 1;
        clickArea.AnchorTop = 0; clickArea.AnchorBottom = 1;
        clickArea.MouseDefaultCursorShape = CursorShape.PointingHand;
        var transparentStyle = new StyleBoxFlat { BgColor = Colors.Transparent };
        clickArea.AddThemeStyleboxOverride("normal", transparentStyle);
        clickArea.AddThemeStyleboxOverride("hover", transparentStyle);
        clickArea.AddThemeStyleboxOverride("pressed", transparentStyle);
        clickArea.AddThemeStyleboxOverride("disabled", transparentStyle);
        panel.AddChild(clickArea);

        // Dim unowned / out-of-copies
        if (isUnowned)
            panel.Modulate = new Color(0.4f, 0.4f, 0.4f, 0.5f);
        else if (remaining <= 0 && !isDeckList)
            panel.Modulate = new Color(0.5f, 0.5f, 0.5f, 0.7f);

        // Engine grey-out for illegal adds
        string? reason = CanAddCard(id);
        if (!isDeckList)
        {
            clickArea.Disabled = reason != null;
            if (reason != null) clickArea.TooltipText = reason;
        }

        if (onAction != null)
            clickArea.Pressed += () => onAction(id);
        else
            clickArea.Pressed += () => ShowCardDetail(id);

        return panel;
    }

    private static Label MakeSmallLabel(string text, int fontSize, int minWidth)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", fontSize);
        if (minWidth > 0) l.CustomMinimumSize = new Vector2(minWidth, 0);
        return l;
    }

    // ——— Validation ——— //

    private void RefreshValidation()
    {
        var result = DeckValidator.Validate(_deckCardIds, LookupCard);
        _validationStatus.Text = string.Join("\n", result.Errors);
        if (result.Errors.Count > 0)
            _validationStatus.Modulate = new Color(1f, 0.5f, 0.5f);
        else if (_deckCardIds.Count == ValidDeckSize)
        {
            _validationStatus.Text = "Deck is legal.";
            _validationStatus.Modulate = new Color(0.3f, 1f, 0.3f);
        }
        else
            _validationStatus.Modulate = new Color(0.8f, 0.8f, 0.7f);

        _saveButton.Disabled = !result.IsValid;
        _saveButton.Text = result.IsValid ? "Save Deck" : $"Save ({_deckCardIds.Count}/30)";
    }

    private string? CanAddCard(string cardId)
    {
        var result = DeckValidator.CanAdd(_deckCardIds, cardId, LookupCard);
        if (!result.IsValid && result.PerCardErrors.ContainsKey(cardId))
            return result.PerCardErrors[cardId];
        return null;
    }

    private CardDef? LookupCard(string id) => _allCards.FirstOrDefault(c => c.Id == id);

    // ——— Add / Remove ——— //

    private void AddToDeck(string cardId)
    {
        if (CanAddCard(cardId) != null) return;
        _deckCardIds.Add(cardId);
        Refresh();
    }

    private void RemoveFromDeck(string cardId)
    {
        int idx = _deckCardIds.LastIndexOf(cardId);
        if (idx < 0) return;
        _deckCardIds.RemoveAt(idx);
        Refresh();
    }

    private void ShowCardDetail(string cardId)
    {
        _selectedCardId = cardId;
        var def = LookupCard(cardId);
        if (def == null) return;
        _detailCard.Text = $"{def.Name}\nCost: {def.Cost}  {def.Strata} {def.Type}\nRarity: {def.Rarity}";
        _detailPanel.Show();
    }

    // ——— Save ——— //

    private void OnSaveDeck()
    {
        var validation = DeckValidator.Validate(_deckCardIds, LookupCard);
        if (!validation.IsValid) return;

        var prog = CampaignContext.Progression;
        prog.DeckCardIds.Clear();
        prog.DeckCardIds.AddRange(_deckCardIds);
        CampaignContext.PlayerDeckIds.Clear();
        CampaignContext.PlayerDeckIds.AddRange(_deckCardIds);
        CampaignContext.SaveManager.Save();

        _saveButton.Text = "Saved!";
        var timer = GetTree().CreateTimer(1.5f);
        timer.Timeout += () => { _saveButton.Text = "Save Deck"; _saveButton.Disabled = false; };
    }
}
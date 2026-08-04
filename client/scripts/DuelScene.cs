using System.Collections.Generic;
using Godot;

namespace Runewake.Client;

/// <summary>
/// Main duel scene — lays out 5+5 lanes, HUD, hand area, and
/// manages all player input via the InputController state machine.
/// </summary>
public partial class DuelScene : Control
{
    // Node references
    private HBoxContainer _enemyLanes;
    private HBoxContainer _playerLanes;
    private HFlowContainer _handArea;

    private Label _enemyName;
    private Label _enemyVigorValue;
    private Label _enemyAttuneValue;
    private Label _playerVigorValue;
    private Label _playerAttuneValue;

    private readonly List<LaneSlot> _enemySlots = new(5);
    private readonly List<LaneSlot> _playerSlots = new(5);
    private readonly List<HandCard> _handCards = new();

    private InputController _input = default!;

    public override void _Ready()
    {
        _enemyName = GetNode<Label>("EnemyHUD/EnemyName");
        _enemyVigorValue = GetNode<Label>("EnemyHUD/EnemyVigorValue");
        _enemyAttuneValue = GetNode<Label>("EnemyHUD/EnemyAttuneValue");
        _playerVigorValue = GetNode<Label>("PlayerHUD/PlayerVigorValue");
        _playerAttuneValue = GetNode<Label>("PlayerHUD/PlayerAttuneValue");

        // Create and register input controller
        _input = new InputController();
        AddChild(_input);
        _input.PlayCardRequested += OnPlayCardRequested;
        _input.AttackRequested += OnAttackRequested;
        _input.SelectionCancelled += OnSelectionCancelled;

        // Set up lane slots and test data
        var laneContainer = GetNode("Board");
        _enemyLanes = laneContainer.GetNode<HBoxContainer>("EnemyLanes");
        _playerLanes = laneContainer.GetNode<HBoxContainer>("PlayerLanes");
        _handArea = GetNode<HFlowContainer>("HandArea");

        PopulateLanes();
        SetTestData();
    }

    /// <summary>
    /// Creates 5 lane slot instances for each row.
    /// </summary>
    private void PopulateLanes()
    {
        var laneScene = GD.Load<PackedScene>("res://scenes/components/LaneSlot.tscn");

        for (int i = 0; i < 5; i++)
        {
            // Enemy lane (row 0)
            var enemySlot = laneScene.Instantiate<LaneSlot>();
            enemySlot.Row = 0;
            enemySlot.LaneIndex = i;
            enemySlot.LaneTapped += OnLaneTapped;
            _enemyLanes.AddChild(enemySlot);
            _enemySlots.Add(enemySlot);

            // Player lane (row 1)
            var playerSlot = laneScene.Instantiate<LaneSlot>();
            playerSlot.Row = 1;
            playerSlot.LaneIndex = i;
            playerSlot.LaneTapped += OnLaneTapped;
            playerSlot.CardDropped += OnCardDropped;
            _playerLanes.AddChild(playerSlot);
            _playerSlots.Add(playerSlot);
        }
    }

    /// <summary>
    /// Populate with test data to verify layout and input visually.
    /// </summary>
    private void SetTestData()
    {
        _enemyName.Text = "Warden Ash";
        SetEnemyVigor(25);
        SetEnemyAttunement(3);
        SetPlayerVigor(25);
        SetPlayerAttunement(2);

        // Place a few test creatures on lanes
        _enemySlots[0].SetCard("Ember Hound", 2, 1);
        _enemySlots[2].SetCard("Phoenix Ash", 4, 4);
        _playerSlots[1].SetCard("Root Warden", 2, 4);
        _playerSlots[3].SetCard("Bloomweaver", 1, 4);
        _playerSlots[4].SetCard("Tidal Scholar", 1, 3);

        // Add some test hand cards
        AddHandCard("emb_c_flame_javelin", "Flame Javelin", 1);
        AddHandCard("emb_c_cinder_runner", "Cinder Runner", 2);
        AddHandCard("vrd_c_root_warden", "Root Warden", 3);
        AddHandCard("dwn_c_purifying_light", "Purifying Light", 1);
    }

    /// <summary>
    /// Add a card to the hand-fan display.
    /// </summary>
    private void AddHandCard(string cardId, string name, int cost)
    {
        var handScene = GD.Load<PackedScene>("res://scenes/components/HandCard.tscn");
        var card = handScene.Instantiate<HandCard>();
        card.SetCard(cardId, name, cost);
        card.Pressed += () => OnHandCardPressed(card);
        _handArea.AddChild(card);
        _handCards.Add(card);
    }

    // ——— Input event handlers ———

    /// <summary>
    /// Called when a lane slot is tapped (clicked).
    /// </summary>
    private void OnLaneTapped(int laneIndex, bool isEmpty)
    {
        // Determine which row this lane is in
        bool isPlayerLane = _playerSlots.Exists(s => s.LaneIndex == laneIndex);
        bool isEnemyLane = _enemySlots.Exists(s => s.LaneIndex == laneIndex);

        if (_input.State == InputController.InputState.SelectingAttacker)
        {
            // Player is selecting an attack target
            if (isEnemyLane)
            {
                _input.SelectAttackTarget(laneIndex);
            }
            else if (isPlayerLane && isEmpty)
            {
                // Tapped empty self lane → cancel
                _input.CancelSelection();
            }
            else if (isPlayerLane && !isEmpty)
            {
                // Tapped another friendly creature → re-select attacker
                _input.SelectAttacker(laneIndex);
            }
        }
        else
        {
            // Idle state: tapping a friendly occupied lane enters attack mode
            if (isPlayerLane && !isEmpty)
            {
                _input.SelectAttacker(laneIndex);
            }
        }
    }

    /// <summary>
    /// Called when a card is dragged and dropped onto a player lane slot.
    /// </summary>
    private void OnCardDropped(string cardId, int laneIndex)
    {
        _input.TryPlayCard(cardId, laneIndex);
    }

    /// <summary>
    /// Called when a hand card button is pressed (tap, not drag).
    /// In attack selection mode, tapping a hand card cancels.
    /// </summary>
    private void OnHandCardPressed(HandCard card)
    {
        if (_input.State == InputController.InputState.SelectingAttacker)
        {
            _input.CancelSelection();
        }
    }

    /// <summary>
    /// Called by the input controller when the player wants to play a card.
    /// </summary>
    private void OnPlayCardRequested(string cardId, int laneIndex)
    {
        GD.Print($"[DuelScene] Play card: {cardId} → lane {laneIndex}");
        // P3-04: Look up card data, validate, call Engine.Apply and update visuals
    }

    /// <summary>
    /// Called by the input controller when the player confirms an attack.
    /// </summary>
    private void OnAttackRequested(int attackerLane, int targetLane)
    {
        string targetDesc = targetLane == -1 ? "face" : $"lane {targetLane}";
        GD.Print($"[DuelScene] Attack: lane {attackerLane} → {targetDesc}");
        // P3-04: Look up game state, call Engine.Apply and update visuals
    }

    /// <summary>
    /// Called when the player cancels their current selection.
    /// Clears all highlights.
    /// </summary>
    private void OnSelectionCancelled()
    {
        foreach (var slot in _enemySlots) slot.Unhighlight();
        foreach (var slot in _playerSlots) slot.Unhighlight();
    }

    // ——— Public update methods ———

    public void SetEnemyName(string name) => _enemyName.Text = name;
    public void SetEnemyVigor(int vigor) => _enemyVigorValue.Text = vigor.ToString();
    public void SetEnemyAttunement(int attune) => _enemyAttuneValue.Text = attune.ToString();
    public void SetPlayerVigor(int vigor) => _playerVigorValue.Text = vigor.ToString();
    public void SetPlayerAttunement(int attune) => _playerAttuneValue.Text = attune.ToString();

    public void ClearBoard()
    {
        foreach (var slot in _enemySlots) slot.SetEmpty();
        foreach (var slot in _playerSlots) slot.SetEmpty();
    }

    public void ClearHand()
    {
        foreach (var card in _handCards) card.QueueFree();
        _handCards.Clear();
    }
}
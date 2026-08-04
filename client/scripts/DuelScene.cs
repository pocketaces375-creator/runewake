using System.Collections.Generic;
using Godot;
using Runewake.Engine.Cards;

namespace Runewake.Client;

/// <summary>
/// Main duel scene — holds a GameState, dispatches actions via the engine,
/// and re-renders all visual elements from the current state.
/// This is the final wiring connecting input → engine → visuals.
/// </summary>
public partial class DuelScene : Control
{
    // Node references
    private Label _enemyName;
    private Label _enemyVigorValue;
    private Label _enemyAttuneValue;
    private Label _playerVigorValue;
    private Label _playerAttuneValue;
    private Label _turnLabel;
    private HBoxContainer _enemyLanes;
    private HBoxContainer _playerLanes;
    private HFlowContainer _handArea;

    private readonly List<LaneSlot> _enemySlots = new(5);
    private readonly List<LaneSlot> _playerSlots = new(5);
    private readonly List<HandCard> _handCards = new();

    private InputController _input = default!;
    private GameStateManager _gsm = default!;

    public override void _Ready()
    {
        // Wire HUD nodes
        _enemyName = GetNode<Label>("EnemyHUD/EnemyName");
        _enemyVigorValue = GetNode<Label>("EnemyHUD/EnemyVigorValue");
        _enemyAttuneValue = GetNode<Label>("EnemyHUD/EnemyAttuneValue");
        _playerVigorValue = GetNode<Label>("PlayerHUD/PlayerVigorValue");
        _playerAttuneValue = GetNode<Label>("PlayerHUD/PlayerAttuneValue");
        _turnLabel = GetNode<Label>("TurnLabel");
        _handArea = GetNode<HFlowContainer>("HandArea");

        var board = GetNode("Board");
        _enemyLanes = board.GetNode<HBoxContainer>("EnemyLanes");
        _playerLanes = board.GetNode<HBoxContainer>("PlayerLanes");

        // Create input controller
        _input = new InputController();
        AddChild(_input);
        _input.PlayCardRequested += OnPlayCardRequested;
        _input.AttackRequested += OnAttackRequested;
        _input.SelectionCancelled += OnSelectionCancelled;

        // Create game state manager
        _gsm = new GameStateManager();
        AddChild(_gsm);
        _gsm.StateChanged += OnStateChanged;
        _gsm.GameOver += OnGameOver;

        // Populate lane slots
        PopulateLanes();

        // Load card packs and start the game
        LoadCardPacks();
        _gsm.InitializeTestGame();
    }

    /// <summary>
    /// Load all card packs into the global CardRegistry.
    /// </summary>
    private static void LoadCardPacks()
    {
        string contentDir = ProjectSettings.GlobalizePath("res://") + "../content/cards";
        var packs = new[]
        {
            $"{contentDir}/verdant.json",
            $"{contentDir}/ember.json",
            $"{contentDir}/tide.json",
            $"{contentDir}/hollow.json",
            $"{contentDir}/dawn.json"
        };

        foreach (var pack in packs)
        {
            var cards = CardLoader.LoadPack(pack);
            CardRegistry.RegisterRange(cards);
        }
    }

    /// <summary>
    /// Create 5 lane slot instances for each row.
    /// </summary>
    private void PopulateLanes()
    {
        var laneScene = GD.Load<PackedScene>("res://scenes/components/LaneSlot.tscn");

        for (int i = 0; i < 5; i++)
        {
            var enemySlot = laneScene.Instantiate<LaneSlot>();
            enemySlot.Row = 0;
            enemySlot.LaneIndex = i;
            enemySlot.LaneTapped += OnLaneTapped;
            _enemyLanes.AddChild(enemySlot);
            _enemySlots.Add(enemySlot);

            var playerSlot = laneScene.Instantiate<LaneSlot>();
            playerSlot.Row = 1;
            playerSlot.LaneIndex = i;
            playerSlot.LaneTapped += OnLaneTapped;
            playerSlot.CardDropped += OnCardDropped;
            _playerLanes.AddChild(playerSlot);
            _playerSlots.Add(playerSlot);
        }
    }

    // ——— State-driven rendering ———

    /// <summary>
    /// Called whenever the GameState changes. Rebuilds all UI from scratch.
    /// </summary>
    private void OnStateChanged()
    {
        RenderHud();
        RenderBoard();
        RenderHand();
    }

    private void RenderHud()
    {
        _enemyName.Text = "Enemy";

        var enemyHud = _gsm.GetPlayerHud(1);
        var playerHud = _gsm.GetPlayerHud(0);

        SetEnemyVigor(enemyHud.Vigor);
        SetEnemyAttunement(enemyHud.Attunement);
        SetPlayerVigor(playerHud.Vigor);
        SetPlayerAttunement(playerHud.Attunement);

        _turnLabel.Text = $"Turn {_gsm.TurnNumber} — {( _gsm.CurrentPlayerIndex == 0 ? "Your" : "Enemy" )} Turn";
    }

    private void RenderBoard()
    {
        // Enemy lanes
        var enemyLanes = _gsm.GetLanes(1);
        for (int i = 0; i < 5; i++)
        {
            var info = enemyLanes[i];
            if (info.IsEmpty)
                _enemySlots[i].SetEmpty();
            else
                _enemySlots[i].SetCard(info.Name, info.Attack, info.Vigor);
        }

        // Player lanes
        var playerLanes = _gsm.GetLanes(0);
        for (int i = 0; i < 5; i++)
        {
            var info = playerLanes[i];
            if (info.IsEmpty)
                _playerSlots[i].SetEmpty();
            else
                _playerSlots[i].SetCard(info.Name, info.Attack, info.Vigor);
        }
    }

    private void RenderHand()
    {
        // Remove old hand cards
        foreach (var card in _handCards)
            card.QueueFree();
        _handCards.Clear();

        // Rebuild from state
        var handScene = GD.Load<PackedScene>("res://scenes/components/HandCard.tscn");
        var hand = _gsm.GetHand(0); // Current player is always human for now

        foreach (var info in hand)
        {
            var card = handScene.Instantiate<HandCard>();
            card.SetCard(info.CardDefId, info.Name, info.Cost);
            card.Pressed += () => OnHandCardPressed(card);
            _handArea.AddChild(card);
            _handCards.Add(card);
        }
    }

    // ——— Input event handlers ———

    private void OnLaneTapped(int laneIndex, bool isEmpty)
    {
        bool isPlayerLane = _playerSlots.Exists(s => s.LaneIndex == laneIndex);
        bool isEnemyLane = _enemySlots.Exists(s => s.LaneIndex == laneIndex);

        if (_input.State == InputController.InputState.SelectingAttacker)
        {
            if (isEnemyLane)
            {
                _input.SelectAttackTarget(laneIndex);
            }
            else if (isPlayerLane && isEmpty)
            {
                _input.CancelSelection();
            }
            else if (isPlayerLane && !isEmpty)
            {
                _input.SelectAttacker(laneIndex);
            }
        }
        else
        {
            if (isPlayerLane && !isEmpty)
            {
                _input.SelectAttacker(laneIndex);
            }
        }
    }

    private void OnCardDropped(string cardId, int laneIndex)
    {
        _input.TryPlayCard(cardId, laneIndex);
    }

    private void OnHandCardPressed(HandCard card)
    {
        if (_input.State == InputController.InputState.SelectingAttacker)
        {
            _input.CancelSelection();
        }
    }

    // ——— Action callbacks from InputController ———

    private void OnPlayCardRequested(string cardId, int laneIndex)
    {
        _gsm.TryPlayCard(0, cardId, laneIndex);
    }

    private void OnAttackRequested(int attackerLane, int targetLane)
    {
        _gsm.TryAttack(0, attackerLane, targetLane);
    }

    private void OnSelectionCancelled()
    {
        foreach (var slot in _enemySlots) slot.Unhighlight();
        foreach (var slot in _playerSlots) slot.Unhighlight();
    }

    private void OnGameOver(int winnerIndex)
    {
        _turnLabel.Text = winnerIndex == 0 ? "You Win!" : "You Lose!";
    }

    // ——— Public update methods ———

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
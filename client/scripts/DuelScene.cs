using System.Collections.Generic;
using Godot;

namespace Runewake.Client;

/// <summary>
/// Main duel scene — lays out 5+5 lanes, HUD, hand area.
/// This is the visual shell that will later be data-driven by GameState.
/// For now, creates the static layout structure as a test fixture.
/// </summary>
public partial class DuelScene : Control
{
    // Node references
    private HBoxContainer _enemyLanes;
    private HBoxContainer _playerLanes;
    private HBoxContainer _enemyHud;
    private HBoxContainer _playerHud;
    private HFlowContainer _handArea;

    private Label _enemyName;
    private Label _enemyVigorValue;
    private Label _enemyAttuneValue;
    private Label _playerVigorValue;
    private Label _playerAttuneValue;

    private readonly List<LaneSlot> _enemySlots = new(5);
    private readonly List<LaneSlot> _playerSlots = new(5);
    private readonly List<HandCard> _handCards = new();

    public override void _Ready()
    {
        _enemyName = GetNode<Label>("EnemyHUD/EnemyName");
        _enemyVigorValue = GetNode<Label>("EnemyHUD/EnemyVigorValue");
        _enemyAttuneValue = GetNode<Label>("EnemyHUD/EnemyAttuneValue");
        _playerVigorValue = GetNode<Label>("PlayerHUD/PlayerVigorValue");
        _playerAttuneValue = GetNode<Label>("PlayerHUD/PlayerAttuneValue");
        _enemyLanes = GetNode<HBoxContainer>("Board/EnemyLanes");
        _playerLanes = GetNode<HBoxContainer>("Board/PlayerLanes");
        _handArea = GetNode<HFlowContainer>("HandArea");

        PopulateLanes();
        SetTestData();
    }

    /// <summary>
    /// Creates 5 lane slot instances for each row.
    /// </summary>
    private void PopulateLanes()
    {
        // Load the lane slot scene
        var laneScene = GD.Load<PackedScene>("res://scenes/components/LaneSlot.tscn");

        for (int i = 0; i < 5; i++)
        {
            // Enemy lane (row 0)
            var enemySlot = laneScene.Instantiate<LaneSlot>();
            enemySlot.Row = 0;
            enemySlot.LaneIndex = i;
            _enemyLanes.AddChild(enemySlot);
            _enemySlots.Add(enemySlot);

            // Player lane (row 1)
            var playerSlot = laneScene.Instantiate<LaneSlot>();
            playerSlot.Row = 1;
            playerSlot.LaneIndex = i;
            _playerLanes.AddChild(playerSlot);
            _playerSlots.Add(playerSlot);
        }
    }

    /// <summary>
    /// Populate with test data to verify layout visually.
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
        _handArea.AddChild(card);
        _handCards.Add(card);
    }

    // ——— Public update methods (for future engine binding) ———

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
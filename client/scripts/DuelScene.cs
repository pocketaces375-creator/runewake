using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Main duel scene — holds a GameState, dispatches actions via the engine,
/// and re-renders all visual elements from the current state.
/// Computes diffs between state renders to trigger animations.
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
    private MarginContainer _handArea;
    private HBoxContainer _handFlow;
    private Button? _endTurnButton;

    private readonly List<LaneSlot> _enemySlots = new(5);
    private readonly List<LaneSlot> _playerSlots = new(5);
    private readonly List<HandCard> _handCards = new();

    private InputController _input = default!;
    private GameStateManager _gsm = default!;
    private BotController _bot = default!;
    private CardView _cardDetail = default!;
    private bool _cardDetailVisible;

    // State snapshot for diff-based animation
    private struct BoardSnapshot
    {
        public bool IsEmpty;
        public string Name;
        public int Attack;
        public int Vigor;
    }
    private BoardSnapshot[] _prevEnemyBoard = new BoardSnapshot[5];
    private BoardSnapshot[] _prevPlayerBoard = new BoardSnapshot[5];
    private int _prevEnemyVigor = -1;
    private int _prevPlayerVigor = -1;
    private bool _firstRender = true;

    private bool _isCampaignEncounter;
    private bool _isGameOverHandled;
    private TutorialController? _tutorialCtrl;
    private int _prevBuryCount;
    private int _prevExcavateCardCount;

    public override void _Ready()
    {
        // Wire HUD nodes
        _enemyName = GetNode<Label>("EnemyHUD/EnemyName");
        _enemyVigorValue = GetNode<Label>("EnemyHUD/EnemyVigorValue");
        _enemyAttuneValue = GetNode<Label>("EnemyHUD/EnemyAttuneValue");
        _playerVigorValue = GetNode<Label>("PlayerHUD/PlayerHudRow/PlayerVigorValue");
        _playerAttuneValue = GetNode<Label>("PlayerHUD/PlayerHudRow/PlayerAttuneValue");
        _turnLabel = GetNode<Label>("TurnLabel");
        _handArea = GetNode<MarginContainer>("HandArea");
        _handFlow = GetNode<HBoxContainer>("HandArea/HandFlow");

        var board = GetNode("Board");
        _enemyLanes = board.GetNode<HBoxContainer>("EnemyLaneMargin/EnemyLanes");
        _playerLanes = board.GetNode<HBoxContainer>("PlayerLaneMargin/PlayerLanes");

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

        // Create bot controller
        _bot = new BotController();
        AddChild(_bot);
        _bot.BotTurnStarted += OnBotTurnStarted;
        _bot.BotTurnEnded += OnBotTurnEnded;

        // Populate lane slots
        PopulateLanes();

        // Load card packs
        LoadCardPacks();
        _bot.Initialize(_gsm);

        // Create card detail popup (hidden until tapped)
        var cardViewScene = GD.Load<PackedScene>("res://scenes/components/CardView.tscn");
        _cardDetail = cardViewScene.Instantiate<CardView>();
        _cardDetail.Visible = false;
        _cardDetailVisible = false;
        AddChild(_cardDetail);

        // Create End Turn button if not in scene
        var existingEndBtn = GetNodeOrNull<Button>("EndTurnButton");
        if (existingEndBtn != null)
        {
            _endTurnButton = existingEndBtn;
        }
        else
        {
            _endTurnButton = new Button();
            _endTurnButton.Text = "End Turn";
            _endTurnButton.ActionMode = Button.ActionModeEnum.Press;
            _endTurnButton.AddThemeFontSizeOverride("font_size", 14);
            _endTurnButton.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
            _endTurnButton.OffsetRight = -10;
            _endTurnButton.OffsetLeft = -100;
            _endTurnButton.OffsetBottom = -70;
            _endTurnButton.OffsetTop = -106;
            AddChild(_endTurnButton);
        }
        _endTurnButton.Pressed += OnEndTurnPressed;

        // Check if this is a campaign encounter or test game
        var encounter = CampaignContext.CurrentEncounter;
        _isCampaignEncounter = encounter != null;

        // Record duel start for telemetry
        if (encounter != null)
        {
            CampaignContext.Telemetry?.RecordDuelStart(
                encounter.Id, CampaignContext.PlayerDeckIds.Count);
        }

        // Check for tutorial override
        var tutorialCtrl = GetNodeOrNull<TutorialController>("/root/TutorialController");
        var tutorialConfig = tutorialCtrl?.GetCurrentTutorialConfig();
        if (tutorialConfig != null)
        {
            _isCampaignEncounter = false;
            GD.Print("[DuelScene] Tutorial duel — using tutorial config.");
            _gsm.Initialize(tutorialConfig);
            // Suspend bot during tutorial duels — player doesn't face an opponent
            _bot.Suspend();
        }
        else if (_isCampaignEncounter && encounter != null)
        {
            // Campaign mode: enemy uses encounter deck, player uses saved deck
            _enemyName.Text = encounter.Name;

            var config = new GameConfig
            {
                Seed = (ulong)GD.Randi(),
                ContentVersion = 1,
                Player0DeckIds = CampaignContext.PlayerDeckIds,
                Player1DeckIds = encounter.Deck
            };
            _gsm.Initialize(config);
        }
        else
        {
            _gsm.InitializeTestGame();
        }

        // Initialize tutorial controller if this is a tutorial duel
        _tutorialCtrl = GetNodeOrNull<TutorialController>("/root/TutorialController");
        if (_tutorialCtrl != null && _tutorialCtrl.IsActive)
        {
            var step = _tutorialCtrl.CurrentStep;
            bool isTutorialDuel = step == Engine.State.TutorialStep.Lanes_SummonCreature
                || step == Engine.State.TutorialStep.Lanes_Attack
                || step == Engine.State.TutorialStep.Lanes_EndTurn
                || step == Engine.State.TutorialStep.Excavate_PlayExcavate
                || step == Engine.State.TutorialStep.Excavate_BuryResolved;

            if (isTutorialDuel)
            {
                // Add tutorial overlay
                var overlay = new TutorialOverlay();
                AddChild(overlay);
                GD.Print($"[DuelScene] Tutorial duel active, step={step}");
            }
        }

        // Position card detail centered using CallDeferred (direct SetPosition post-tree-attach)
        Callable.From(() =>
        {
            _cardDetail.Position = new Vector2(
                (GetViewportRect().Size.X - 280) / 2f,
                (GetViewportRect().Size.Y - 400) / 2f
            );
        }).CallDeferred();

        // Enable background tap to cancel selection
        GuiInput += OnBackgroundGuiInput;
    }

    /// <summary>
    /// Handle taps on the background (empty space) to cancel selection.
    /// </summary>
    private void OnBackgroundGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
        {
            if (_input.State != InputController.InputState.Idle)
            {
                _input.CancelSelection();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    /// <summary>
    /// Load all card packs into the global CardRegistry.
    /// </summary>
    private static void LoadCardPacks()
        {
            // Use Godot's FileAccess to read from the embedded PCK (works in both editor and export)
            var packs = new[]
            {
                "res://content/cards/verdant.json",
                "res://content/cards/ember.json",
                "res://content/cards/tide.json",
                "res://content/cards/hollow.json",
                "res://content/cards/dawn.json"
            };

            foreach (var pack in packs)
            {
                string json = Godot.FileAccess.GetFileAsString(pack);
                var cards = CardLoader.LoadPackFromString(json);
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
    /// Count cards in a player's hand that have at least one EXCAVATE effect.
    /// </summary>
    private static int CountExcavateCards(GameState state, int playerIndex)
    {
        if (state == null || state.Players.Length <= playerIndex)
            return 0;
        return state.Players[playerIndex].Hand.Count(c =>
        {
            var def = CardRegistry.Get(c.CardDefId);
            return def != null && def.Abilities.Any(a => a.Effects.Any(e => e.Op == Op.EXCAVATE));
        });
    }

    /// <summary>
    /// Called whenever the GameState changes. Snapshot old state, render new,
    /// then compute diffs and trigger animations.
    /// </summary>
    private void OnStateChanged()
    {
        // Dismiss card detail popup on state change
        if (_cardDetailVisible)
        {
            _cardDetail.Visible = false;
            _cardDetailVisible = false;
        }

        // Get the current state from GSM
        var state = _gsm.State;

        // Compute excavate card count BEFORE render (hand state before update)
        int excavateCount = 0;
        if (state != null && state.Players.Length > 0)
            excavateCount = CountExcavateCards(state, 0);

        // Capture the new state for comparison
        var newEnemyBoard = CaptureBoard(1);
        var newPlayerBoard = CaptureBoard(0);

        RenderHud();
        RenderBoard();
        RenderHand();

        // Compute diffs and trigger animations using the previous snapshot
        if (!_firstRender)
        {
            AnimateBoardDiffs(_prevEnemyBoard, _prevPlayerBoard, newEnemyBoard, newPlayerBoard);
            AnimateVigorDiffs();
        }

        // Tutorial auto-advance: Excavate card played (hand lost an excavate card)
        if (_tutorialCtrl != null && _tutorialCtrl.CurrentStep == TutorialStep.Excavate_PlayExcavate
            && excavateCount < _prevExcavateCardCount)
        {
            _tutorialCtrl.Advance();
        }

        // Tutorial auto-advance: barrow count increased
        if (_tutorialCtrl != null && _tutorialCtrl.CurrentStep == TutorialStep.Excavate_BuryResolved
            && state != null && state.Players.Length > 0
            && state.Players[0].Barrow.Count > _prevBuryCount)
        {
            _tutorialCtrl.Advance();
        }

        // Save for next render
        _prevEnemyBoard = newEnemyBoard;
        _prevPlayerBoard = newPlayerBoard;
        _prevExcavateCardCount = excavateCount;
        if (state != null && state.Players.Length > 0)
            _prevBuryCount = state.Players[0].Barrow.Count;
        _firstRender = false;
    }

    private BoardSnapshot[] CaptureBoard(int playerIndex)
    {
        var lanes = _gsm.GetLanes(playerIndex);
        var result = new BoardSnapshot[5];
        for (int i = 0; i < 5; i++)
        {
            result[i] = new BoardSnapshot
            {
                IsEmpty = lanes[i].IsEmpty,
                Name = lanes[i].Name,
                Attack = lanes[i].Attack,
                Vigor = lanes[i].Vigor
            };
        }
        return result;
    }

    private void AnimateBoardDiffs(BoardSnapshot[] oldEnemy, BoardSnapshot[] oldPlayer,
        BoardSnapshot[] newEnemy, BoardSnapshot[] newPlayer)
    {
        for (int i = 0; i < 5; i++)
        {
            var slot = _enemySlots[i];
            var prev = oldEnemy[i];
            var cur = newEnemy[i];

            if (prev.IsEmpty && !cur.IsEmpty)
                slot.PlaySummonEffect();
            else if (!prev.IsEmpty && cur.IsEmpty)
                slot.PlayDeathEffect();
            else if (!prev.IsEmpty && !cur.IsEmpty)
            {
                int dmg = prev.Vigor - cur.Vigor;
                if (dmg > 0 && !CampaignContext.ReduceMotion) slot.ShowDamageNumber(dmg);
                else if (dmg < 0 && !CampaignContext.ReduceMotion) slot.ShowHealNumber(-dmg);
            }
        }

        for (int i = 0; i < 5; i++)
        {
            var slot = _playerSlots[i];
            var prev = oldPlayer[i];
            var cur = newPlayer[i];

            if (prev.IsEmpty && !cur.IsEmpty)
                slot.PlaySummonEffect();
            else if (!prev.IsEmpty && cur.IsEmpty)
                slot.PlayDeathEffect();
            else if (!prev.IsEmpty && !cur.IsEmpty)
            {
                int dmg = prev.Vigor - cur.Vigor;
                if (dmg > 0 && !CampaignContext.ReduceMotion) slot.ShowDamageNumber(dmg);
                else if (dmg < 0 && !CampaignContext.ReduceMotion) slot.ShowHealNumber(-dmg);
            }
        }
    }

    private void AnimateVigorDiffs()
    {
        var enemyHud = _gsm.GetPlayerHud(1);
        var playerHud = _gsm.GetPlayerHud(0);

        if (_prevEnemyVigor >= 0 && enemyHud.Vigor != _prevEnemyVigor)
        {
            int diff = _prevEnemyVigor - enemyHud.Vigor;
            ShowFaceDamage(true, diff);
        }

        if (_prevPlayerVigor >= 0 && playerHud.Vigor != _prevPlayerVigor)
        {
            int diff = _prevPlayerVigor - playerHud.Vigor;
            ShowFaceDamage(false, diff);
        }

        _prevEnemyVigor = enemyHud.Vigor;
        _prevPlayerVigor = playerHud.Vigor;
    }

    private void ShowFaceDamage(bool isEnemy, int amount)
    {
        var ftScene = GD.Load<PackedScene>("res://scenes/effects/FloatingText.tscn");
        var ft = ftScene.Instantiate<FloatingText>();
        AddChild(ft);

        Vector2 pos;
        Color color;
        string prefixAndAmount;

        if (amount > 0) // damage
        {
            color = new Color(1, 0.15f, 0.15f);
            prefixAndAmount = $"-{amount}";
        }
        else // heal
        {
            color = new Color(0.15f, 1, 0.15f);
            prefixAndAmount = $"+{-amount}";
            amount = -amount;
        }

        if (isEnemy)
            pos = _enemyVigorValue.GlobalPosition + new Vector2(40, -10);
        else
            pos = _playerVigorValue.GlobalPosition + new Vector2(40, -20);

        ft.ShowLargeAt(prefixAndAmount, color, pos);
    }

    private void RenderHud()
    {
        if (!_isCampaignEncounter)
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
            _handFlow.AddChild(card);
            // AddChild triggers _Ready, so GetNode inside HandCard._Ready() works
            card.SetCard(info.CardDefId, info.Name, info.Cost, info.Strata);
            var capturedCard = card;
            card.Pressed += () => OnHandCardPressed(capturedCard);
            _handCards.Add(card);
        }
    }

    // ——— Bot turn callbacks ———

    private void OnBotTurnStarted()
    {
        _turnLabel.Text = $"Turn {_gsm.TurnNumber} — Enemy Thinking...";
    }

    private void OnBotTurnEnded()
    {
    }

    // ——— Input event handlers ———

    private void OnLaneTapped(int laneIndex, bool isEmpty)
    {
        if (_bot.IsThinking) return;

        // Dismiss card detail popup on any lane interaction
        if (_cardDetailVisible)
        {
            _cardDetail.Visible = false;
            _cardDetailVisible = false;
        }

        bool isPlayerLane = _playerSlots.Exists(s => s.LaneIndex == laneIndex);
        bool isEnemyLane = _enemySlots.Exists(s => s.LaneIndex == laneIndex);

        if (_input.State == InputController.InputState.SelectingLane)
        {
            // Tap-to-summon: player tapped a lane after selecting a card
            if (isPlayerLane && isEmpty)
            {
                _input.SelectTargetLane(laneIndex);
            }
            else if (isPlayerLane && !isEmpty)
            {
                // Tapped occupied lane while selecting a card — cancel and show feedback
                ShowToast("That lane is already occupied.", new Color(1, 0.7f, 0.2f));
                _input.CancelSelection();
            }
            else
            {
                // Tapped enemy lane or empty space — cancel
                _input.CancelSelection();
            }
        }
        else if (_input.State == InputController.InputState.SelectingAttacker)
        {
            if (isEnemyLane)
            {
                _input.SelectAttackTarget(laneIndex);
            }
            else if (isPlayerLane && isEmpty)
            {
                // Tapped own empty lane while selecting attacker — cancel
                _input.CancelSelection();
            }
            else if (isPlayerLane && !isEmpty)
            {
                // Switch attacker to this creature instead
                _input.SelectAttacker(laneIndex);
                UpdateAttackHighlights();
            }
        }
        else
        {
            // Idle state
            if (isPlayerLane && !isEmpty)
            {
                _input.SelectAttacker(laneIndex);
                UpdateAttackHighlights();
            }
        }
    }

    private void OnCardDropped(string cardId, int laneIndex)
    {
        if (_bot.IsThinking) return;
        var result = _input.TryPlayCard(cardId, laneIndex);
        // The TryPlayCard emits PlayCardRequested, which is handled in OnPlayCardRequested
    }

    private void OnHandCardPressed(HandCard card)
    {
        if (_bot.IsThinking) return;

        if (_input.State == InputController.InputState.SelectingAttacker)
        {
            // Cancel attacker selection and show this card's detail
            _input.CancelSelection();
            ShowCardDetail(card);
        }
        else if (_input.State == InputController.InputState.SelectingLane)
        {
            // Already in lane-selection mode — switch to this card
            _input.CancelSelection();
            _input.SelectCardForPlay(card.CardId);
            ShowToast($"Select a lane to summon {card.CardName} (cost {card.CardCost})",
                new Color(0.5f, 1, 0.5f));
            UpdatePlayHighlights();
            ShowCardDetail(card);
        }
        else
        {
            // Idle — show detail and enter lane-selection mode (tap-to-summon)
            _input.SelectCardForPlay(card.CardId);
            ShowToast($"Select a lane to summon {card.CardName} (cost {card.CardCost})",
                new Color(0.5f, 1, 0.5f));
            UpdatePlayHighlights();
            ShowCardDetail(card);
        }
    }

    /// <summary>
    /// Show the card detail popup for a hand card.
    /// </summary>
    private void ShowCardDetail(HandCard card)
    {
        // Toggle card detail popup
        if (_cardDetailVisible && _cardDetail.CurrentCard?.Name == card.CardName)
        {
            // Same card tapped again — dismiss
            _cardDetail.Visible = false;
            _cardDetailVisible = false;
        }
        else
        {
            // Show this card's detail view
            var def = CardRegistry.Get(card.CardId);
            if (def != null)
            {
                _cardDetail.SetCard(def);
                _cardDetail.Visible = true;
                _cardDetailVisible = true;
            }
        }
    }

    // ——— Action callbacks from InputController ———

    private void OnPlayCardRequested(string cardId, int laneIndex)
    {
        var result = _gsm.TryPlayCard(0, cardId, laneIndex);
        if (!result.Success)
        {
            ShowToast(result.ErrorMessage ?? "Cannot play that card.",
                new Color(1, 0.7f, 0.2f));
        }
        else
        {
            // Advance tutorial if waiting for summon
            if (_tutorialCtrl?.CurrentStep == Engine.State.TutorialStep.Lanes_SummonCreature)
                _tutorialCtrl.Advance();
        }
    }

    private void OnAttackRequested(int attackerLane, int targetLane)
    {
        var result = _gsm.TryAttack(0, attackerLane, targetLane);
        if (!result.Success)
        {
            ShowToast(result.ErrorMessage ?? "Cannot attack.",
                new Color(1, 0.3f, 0.3f));
        }
        else
        {
            // Advance tutorial if waiting for attack
            if (_tutorialCtrl?.CurrentStep == Engine.State.TutorialStep.Lanes_Attack)
                _tutorialCtrl.Advance();
        }
    }

    private void OnSelectionCancelled()
    {
        foreach (var slot in _enemySlots) slot.Unhighlight();
        foreach (var slot in _playerSlots) slot.Unhighlight();
    }

    /// <summary>
    /// Highlight friendly occupied lanes (attackers available) when in selecting-attacker mode.
    /// </summary>
    private void UpdateAttackHighlights()
    {
        foreach (var slot in _playerSlots)
        {
            if (slot.Row == 1 && slot.LaneIndex == _input.SelectedAttackerLane)
                slot.Highlight();
            else
                slot.Unhighlight();
        }

        // Highlight all enemy lanes as potential targets when attacker is selected
        if (_input.State == InputController.InputState.SelectingAttacker)
        {
            foreach (var slot in _enemySlots)
                slot.Highlight();
        }
        else
        {
            foreach (var slot in _enemySlots)
                slot.Unhighlight();
        }
    }

    /// <summary>
    /// Highlight empty player lanes when in lane-selection mode (tap-to-summon).
    /// </summary>
    private void UpdatePlayHighlights()
    {
        if (_input.State == InputController.InputState.SelectingLane)
        {
            // Highlight all empty player lanes as valid summon targets
            foreach (var slot in _playerSlots)
            {
                if (slot.LaneIndex < 5)
                {
                    // Check if lane is empty via GSM
                    var lanes = _gsm.GetLanes(0);
                    var info = lanes[slot.LaneIndex];
                    if (info.IsEmpty)
                        slot.Highlight();
                    else
                        slot.Unhighlight();
                }
            }
        }
        else
        {
            foreach (var slot in _playerSlots)
                slot.Unhighlight();
        }
    }

    /// <summary>
    /// Handle End Turn button press.
    /// </summary>
    private void OnEndTurnPressed()
    {
        if (_bot.IsThinking) return;
        if (_gsm.CurrentPlayerIndex != 0) return;

        var result = _gsm.TryEndTurn();
        if (!result.Success)
        {
            ShowToast(result.ErrorMessage ?? "Cannot end turn.",
                new Color(1, 0.3f, 0.3f));
        }
        else
        {
            // Advance tutorial if waiting for end turn
            if (_tutorialCtrl?.CurrentStep == Engine.State.TutorialStep.Lanes_EndTurn)
                _tutorialCtrl.Advance();
        }
    }

    /// <summary>
    /// Show a floating toast message near the center of the screen.
    /// Auto-fades and removes itself. Used for rejection feedback.
    /// </summary>
    private void ShowToast(string message, Color color)
    {
        var toast = new Label();
        toast.Text = message;
        toast.HorizontalAlignment = HorizontalAlignment.Center;
        toast.VerticalAlignment = VerticalAlignment.Center;
        toast.AddThemeFontSizeOverride("font_size", 16);
        toast.Modulate = color;
        toast.AutowrapMode = TextServer.AutowrapMode.Word;
        toast.Position = new Vector2(
            GetViewportRect().Size.X / 2f - 150,
            GetViewportRect().Size.Y / 2f - 30
        );
        toast.Size = new Vector2(300, 60);
        AddChild(toast);

        var tween = CreateTween();
        tween.SetParallel();
        tween.TweenProperty(toast, "position", toast.Position + new Vector2(0, -30), 1.2f);
        tween.TweenProperty(toast, "modulate:a", 0.0f, 1.2f);
        tween.SetParallel(false);
        tween.TweenCallback(Callable.From(toast.QueueFree));
    }

    private void OnGameOver(int winnerIndex)
    {
        _turnLabel.Text = winnerIndex == 0 ? "You Win!" : "You Lose!";
        _turnLabel.Modulate = winnerIndex == 0
            ? new Color(1, 0.8f, 0.4f)
            : new Color(1, 0.3f, 0.3f);

        if (_isCampaignEncounter && !_isGameOverHandled)
        {
            _isGameOverHandled = true;

            // Record duel end for telemetry
            CampaignContext.Telemetry?.RecordDuelEnd(
                CampaignContext.CurrentEncounter?.Id ?? "unknown",
                winnerIndex == 0,
                _gsm.TurnNumber);

        if (winnerIndex == 0 && CampaignContext.CurrentEncounter != null)
        {
            // Player won — apply rewards
            var enc = CampaignContext.CurrentEncounter;
            var prog = CampaignContext.Progression;

            prog.Shards += enc.ShardReward;
            if (enc.DigChargeReward > 0)
                prog.DigCharges += enc.DigChargeReward;
            if (enc.FragmentReward != null)
            {
                var parts = enc.FragmentReward.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out int fragCount))
                    prog.AddFragments(parts[0], fragCount);
            }

            // Mint Lost Relic if this encounter qualifies (WARDEN_BOSS or rare find)
            if (CampaignContext.CurrentEncounter != null)
            {
                string encId = CampaignContext.CurrentEncounter.Id;
                if (CampaignContext.LostRelicIndex.TryGetValue(encId, out var relicDef))
                {
                    // Only mint if the player hasn't already collected this relic card
                    if (!prog.Collection.ContainsKey(relicDef.CardId))
                    {
                        var relic = LostRelicMinter.Mint(
                            encId,
                            CampaignContext.LostRelicIndex,
                            "Adventurer",
                            prog.GlobalDiscoveryIndex + 1
                        );
                        if (relic != null)
                        {
                            prog.AddRelic(relic);
                            prog.AddCard(relic.CardId);

                            // Sync the newly minted relic to Supabase (fire-and-forget)
                            if (CampaignContext.SyncManager != null)
                                _ = CampaignContext.SyncManager.SyncOnRelicMint(relic);

                            // Record telemetry for relic mint
                            CampaignContext.Telemetry?.RecordRelicMinted(relic.CardId);
                        }
                    }
                }
            }

            // Mark node cleared
            if (CampaignContext.CurrentNodeId != null)
                prog.MarkNodeCleared(CampaignContext.CurrentNodeId);

            // Record telemetry for node clear
            CampaignContext.Telemetry?.RecordNodeCleared(
                CampaignContext.CurrentNodeId ?? "unknown");

            // Grant the player one copy of each card in the encounter deck
            // that they don't already own
            foreach (var cardId in enc.Deck)
            {
                if (!prog.Collection.ContainsKey(cardId))
                    prog.AddCard(cardId);
            }

            CampaignContext.SaveManager.Save();

            // Show outro dialogue if available
            if (enc.DialogueOutro is { Count: > 0 })
            {
                var outroLabel = new Label
                {
                    Text = string.Join("\n", enc.DialogueOutro),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    AnchorLeft = 0.1f, AnchorRight = 0.9f,
                    AnchorTop = 0.3f, AnchorBottom = 0.7f,
                    AutowrapMode = TextServer.AutowrapMode.Word
                };
                outroLabel.AddThemeFontSizeOverride("font_size", 16);
                outroLabel.Modulate = new Color(1, 1, 1, 0.95f);
                AddChild(outroLabel);
            }

            // Show reward summary
            var rewardLabel = new Label
            {
                Text = $"+{enc.ShardReward} shards" +
                       (enc.DigChargeReward > 0 ? $"\n+{enc.DigChargeReward} dig charge(s)" : "") +
                       (enc.FragmentReward != null ? $"\n+{enc.FragmentReward} fragments" : "") +
                       $"\n+{enc.Deck.Count} new card(s) unlocked to collection",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AnchorLeft = 0.1f, AnchorRight = 0.9f,
                AnchorTop = 0.72f, AnchorBottom = 0.85f,
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            rewardLabel.AddThemeFontSizeOverride("font_size", 14);
            rewardLabel.Modulate = new Color(0.4f, 1, 0.4f);
            AddChild(rewardLabel);
        }

        // Navigate back to map after delay
        var timer = new Godot.Timer();
        timer.OneShot = true;
        timer.WaitTime = 4.0;
        timer.Timeout += () => GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
        AddChild(timer);
        timer.Start();
        }
        else
        {
            // Non-campaign (test/free-play) — show game-over overlay
            ShowGameOverOverlay(winnerIndex);
        }
    }

    private void ShowGameOverOverlay(int winnerIndex)
    {
        var panel = new Panel();
        panel.AnchorLeft = 0.2f;
        panel.AnchorRight = 0.8f;
        panel.AnchorTop = 0.25f;
        panel.AnchorBottom = 0.55f;

        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.06f, 0.06f, 0.1f, 0.95f);
        style.BorderColor = winnerIndex == 0 ? new Color(1, 0.8f, 0.4f) : new Color(1, 0.3f, 0.3f);
        style.BorderWidthLeft = 2;
        style.BorderWidthTop = 2;
        style.BorderWidthRight = 2;
        style.BorderWidthBottom = 2;
        style.CornerRadiusTopLeft = 8;
        style.CornerRadiusTopRight = 8;
        style.CornerRadiusBottomLeft = 8;
        style.CornerRadiusBottomRight = 8;
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vbox.AnchorLeft = 0.1f;
        vbox.AnchorRight = 0.9f;
        vbox.AnchorTop = 0.1f;
        vbox.AnchorBottom = 0.9f;
        panel.AddChild(vbox);

        var title = new Label();
        title.Text = winnerIndex == 0 ? "You Win!" : "You Lose!";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 28);
        title.Modulate = winnerIndex == 0 ? new Color(1, 0.8f, 0.4f) : new Color(1, 0.3f, 0.3f);
        vbox.AddChild(title);

        vbox.AddChild(new Control { SizeFlagsVertical = (Control.SizeFlags)3 }); // Spacer

        var turnInfo = new Label();
        turnInfo.Text = $"Game ended on turn {_gsm.TurnNumber}";
        turnInfo.HorizontalAlignment = HorizontalAlignment.Center;
        turnInfo.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(turnInfo);

        vbox.AddChild(new Control { SizeFlagsVertical = (Control.SizeFlags)3 }); // Spacer

        var btnHBox = new HBoxContainer();
        btnHBox.Alignment = BoxContainer.AlignmentMode.Center;
        btnHBox.SizeFlagsHorizontal = (Control.SizeFlags)3;
        vbox.AddChild(btnHBox);

        var playAgain = new Button();
        playAgain.Text = "Play Again";
        playAgain.CustomMinimumSize = new Vector2(130, 40);
        playAgain.Pressed += () => GetTree().ReloadCurrentScene();
        btnHBox.AddChild(playAgain);

        // Spacer between buttons
        btnHBox.AddChild(new Control { CustomMinimumSize = new Vector2(20, 0) });

        var backToTitle = new Button();
        backToTitle.Text = "Back to Title";
        backToTitle.CustomMinimumSize = new Vector2(130, 40);
        backToTitle.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
        btnHBox.AddChild(backToTitle);
    }

    // ——— Public update methods ———

    public void SetEnemyVigor(int vigor) => _enemyVigorValue.Text = vigor.ToString();
    public void SetEnemyAttunement(int attune) => _enemyAttuneValue.Text = attune.ToString();
    public void SetPlayerVigor(int vigor) => _playerVigorValue.Text = vigor.ToString();
    public void SetPlayerAttunement(int attune)
    {
        GD.Print($"SetPlayerAttunement({attune}) — assigning to _playerAttuneValue.Text");
        _playerAttuneValue.Text = attune.ToString();
    }

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
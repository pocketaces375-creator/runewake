using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using static ThemeTokens;

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

    // Health bar ColorRects
    private ColorRect _enemyHealthBar = default!;
    private ColorRect _playerHealthBar = default!;

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
    private TutorialPopup? _tutorialPopup;
    private bool _tutorialSummonedThisDuel;
    private bool _tutorialAwaitingCreatureSelect;
    private int _prevBuryCount;
    private int _prevExcavateCardCount;

    // Mulligan state
    private Control? _mulliganPanel;
    private readonly HashSet<int> _mulliganSelection = new();

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

        // Step 1: Typography — apply display font (Cinzel) to headers, body font (Inter) to data
        ApplyHeaderFont(_enemyName, FontTitle);
        ApplyHeaderFont(_turnLabel, FontSmall);
        ApplyBodyFont(_enemyVigorValue, FontLargeBody);
        ApplyBodyFont(_enemyAttuneValue, FontLargeBody);
        ApplyBodyFont(_playerVigorValue, FontLargeBody);
        ApplyBodyFont(_playerAttuneValue, FontLargeBody);

        var board = GetNode("Board");

        // ── Stone-slab board surface ──
        // Seamless tileable stone texture behind the board lanes
        var boardBg = GetNode<TextureRect>("BoardBg");
        var stoneTex = GD.Load<Texture2D>("res://assets/stone_board.png");
        if (stoneTex != null)
        {
            boardBg.Texture = stoneTex;
            boardBg.Modulate = new Color(0.38f, 0.36f, 0.33f, 1.0f); // darkened, desaturated — recedes behind cards
        }
        else
        {
            GD.PrintErr("[DuelScene] Failed to load stone_board.png — board background will be empty.");
        }

        // Health bar tracks (dark background)
        var enemyTrack = new ColorRect { Name = "EnemyHealthTrack", Color = new Color(0.06f, 0.05f, 0.04f, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        enemyTrack.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        enemyTrack.Size = new Vector2(0, 40);
        AddChild(enemyTrack);

        // Health bar ColorRects (the fill, resized by RenderHud)
        _enemyHealthBar = new ColorRect { Name = "EnemyHealthBar", Color = Colors.Transparent, MouseFilter = MouseFilterEnum.Ignore };
        _enemyHealthBar.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        AddChild(_enemyHealthBar);

        var playerTrack = new ColorRect { Name = "PlayerHealthTrack", Color = new Color(0.06f, 0.05f, 0.04f, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        playerTrack.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        playerTrack.OffsetTop = -36;
        AddChild(playerTrack);

        _playerHealthBar = new ColorRect { Name = "PlayerHealthBar", Color = Colors.Transparent, MouseFilter = MouseFilterEnum.Ignore };
        _playerHealthBar.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _playerHealthBar.OffsetTop = -36;
        AddChild(_playerHealthBar);

        // Move HUD text nodes in front of health bars
        var enemyHud = GetNode<HBoxContainer>("EnemyHUD");
        RemoveChild(enemyHud);
        AddChild(enemyHud);
        var playerHud = GetNode<CenterContainer>("PlayerHUD");
        RemoveChild(playerHud);
        AddChild(playerHud);

        _enemyLanes = board.GetNode<HBoxContainer>("EnemyLaneMargin/EnemyLanes");
        _playerLanes = board.GetNode<HBoxContainer>("PlayerLaneMargin/PlayerLanes");

        // Create input controller
        _input = new InputController();
        AddChild(_input);
        _input.PlayCardRequested += OnPlayCardRequested;
        _input.AttackRequested += OnAttackRequested;
        _input.CreatureSelectedForAttack += OnCreatureSelectedForAttack;
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
        _cardDetail.Dismissed += () => { _cardDetailVisible = false; };
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
            _endTurnButton.AddThemeFontSizeOverride("font_size", FontLargeBody);
            _endTurnButton.AddThemeColorOverride("font_color", TextPrimary);
            _endTurnButton.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
            _endTurnButton.OffsetRight = -10;
            _endTurnButton.OffsetLeft = -100;
            _endTurnButton.OffsetBottom = -70;
            _endTurnButton.OffsetTop = -106;
            AddChild(_endTurnButton);
        }
        _endTurnButton.Pressed += OnEndTurnPressed;

        var encounter = CampaignContext.CurrentEncounter;
        _isCampaignEncounter = encounter != null;

        // Record duel start for telemetry
        if (encounter != null)
        {
            CampaignContext.Telemetry?.RecordDuelStart(
                encounter.Id, CampaignContext.PlayerDeckIds.Count);
        }

        // Check if this is a tutorial encounter (uses campaign encounter with is_tutorial flag)
        bool isTutorialEncounter = _isCampaignEncounter && encounter != null && encounter.IsTutorial;
        if (isTutorialEncounter)
        {
            _tutorialPopup = new TutorialPopup();
            AddChild(_tutorialPopup);
            _tutorialCtrl = new TutorialController();
            AddChild(_tutorialCtrl);
            _tutorialCtrl.Initialize(this, _tutorialPopup);
            GD.Print("[DuelScene] Tutorial encounter detected — activating popup system.");
        }

        if (_isCampaignEncounter && encounter != null)
        {
            // Campaign mode: enemy uses encounter deck, player uses saved deck
            _enemyName.Text = encounter.Name;

            var config = new GameConfig
            {
                Seed = (ulong)GD.Randi(),
                ContentVersion = 1,
                Player0DeckIds = CampaignContext.PlayerDeckIds,
                Player1DeckIds = encounter.Deck,
                RunePage = CampaignContext.CurrentRunePage
            };
            _gsm.Initialize(config);
        }
        else
        {
            _gsm.InitializeTestGame();
        }

        // Speed up bot during tutorial so enemy turns are near-instant
        if (isTutorialEncounter)
        {
            _bot.ThinkDelay = 0.1f;
            _bot.ActionInterval = 0.1f;
        }

        // Show mulligan overlay if not in tutorial mode
        if (_tutorialCtrl == null || !_tutorialCtrl.IsActive)
        {
            Callable.From(ShowMulliganIfNeeded).CallDeferred();
        }

        // Position card detail centered using CallDeferred (direct SetPosition post-tree-attach)
        Callable.From(() =>
        {
            _cardDetail.Position = new Vector2(
                (GetViewportRect().Size.X - 280) / 2f,
                (GetViewportRect().Size.Y - 400) / 2f
            );
        }).CallDeferred();

        // Style the turn label for readability
        _turnLabel.AddThemeFontSizeOverride("font_size", FontSmall);
        _turnLabel.AddThemeColorOverride("font_color", TextSecondary);

        // Enable background tap to cancel selection
        GuiInput += OnBackgroundGuiInput;

        // Start tutorial popup sequence if this is a tutorial encounter
        if (_tutorialCtrl != null && _tutorialCtrl.IsActive)
        {
            Callable.From(ShowPopup1_Goal).CallDeferred();
        }

        // ═══ CAPTURE HOOK: auto-dismiss mulligan, wait, capture ═══
        if (CampaignContext.AutoCaptureScreenshot)
        {
            var capTimer = new Godot.Timer();
            capTimer.OneShot = true;
            capTimer.WaitTime = 1.0f; // wait for _Ready + mulligan overlay to render
            capTimer.Timeout += () =>
            {
                // Skip mulligan for both players
                if (_gsm != null && _gsm.State != null && !_gsm.State.Players[0].HasMulliganed)
                {
                    _gsm.PerformMulligan(0, new System.Collections.Generic.List<int>());
                    _gsm.PerformMulligan(1, new System.Collections.Generic.List<int>());
                    DismissMulligan();
                    // Force render of the board
                    Callable.From(OnStateChanged).CallDeferred();
                }

                // Capture after board renders
                var snapTimer = new Godot.Timer();
                snapTimer.OneShot = true;
                snapTimer.WaitTime = 1.0f;
                snapTimer.Timeout += () =>
                {
                    var img = GetViewport().GetTexture().GetImage();
                    if (img != null)
                        img.SavePng("/home/fictive/runewake/screenshots/board_art_v2.png");
                    GD.Print("[CAPTURE] board_art_v2.png saved");
                };
                AddChild(snapTimer);
                snapTimer.Start();
            };
            AddChild(capTimer);
            capTimer.Start();
        }
        // ═══ END CAPTURE HOOK ═══
    }

    // ═══════════════════════════════════════════════════
    // Tutorial popup sequence — content loaded from JSON,
    // presenter handles visual form. This class only
    // decides when to show and what to highlight.
    // ═══════════════════════════════════════════════════

    /// <summary>Popup 1: YOUR GOAL</summary>
    private void ShowPopup1_Goal()
    {
        if (_tutorialCtrl == null || !_tutorialCtrl.IsActive || _tutorialPopup == null) return;

        _tutorialPopup.HighlightTarget = _enemyVigorValue;
        _tutorialCtrl.ShowPopup("p1_goal",
            onContinue: ShowPopup2_Attunement,
            onSkip: () =>
            {
                _tutorialCtrl?.EndTutorial();
            }
        );
    }

    /// <summary>Popup 2: ATTUNEMENT</summary>
    private void ShowPopup2_Attunement()
    {
        if (_tutorialCtrl == null || !_tutorialCtrl.IsActive || _tutorialPopup == null) return;

        _tutorialPopup.HighlightTarget = _playerAttuneValue;
        _tutorialCtrl.ShowPopup("p2_attunement",
            onContinue: ShowPopup3_Summoning
        );
    }

    /// <summary>Popup 3: SUMMONING</summary>
    private void ShowPopup3_Summoning()
    {
        if (_tutorialCtrl == null || !_tutorialCtrl.IsActive) return;

        _tutorialCtrl.ShowPopup("p3_summoning",
            onContinue: () => { _tutorialSummonedThisDuel = false; }
        );
    }

    /// <summary>Popup 4a: ATTACKING — YOUR TURN (fires after creature summoned)</summary>
    private void ShowPopup4a_AttackingYourTurn()
    {
        if (_tutorialCtrl == null || !_tutorialCtrl.IsActive || _tutorialPopup == null) return;

        _tutorialPopup.HighlightTarget = FindPlayerCreatureNode() as Control;
        _tutorialCtrl.ShowPopup("p4a_attacking",
            onContinue: () => { _tutorialAwaitingCreatureSelect = true; }
        );
    }

    /// <summary>Popup 4b: ATTACKING — CHOOSING A TARGET (fires when player selects creature)</summary>
    private void ShowPopup4b_ChoosingTarget()
    {
        if (_tutorialCtrl == null || !_tutorialCtrl.IsActive) return;

        _tutorialCtrl.ShowPopup("p4b_choosing",
            onContinue: () => { _tutorialAwaitingCreatureSelect = false; }
        );
    }

    /// <summary>Popup 5: FACE HIT (fires after attack resolves)</summary>
    private void ShowPopup5_FaceHit()
    {
        if (_tutorialCtrl == null || !_tutorialCtrl.IsActive || _tutorialPopup == null) return;

        _tutorialPopup.HighlightTarget = _enemyVigorValue;
        _tutorialCtrl.ShowPopup("p5_facehit",
            onContinue: ShowPopup6_TurnCycle
        );
    }

    /// <summary>Popup 6: THE TURN CYCLE (final popup — end tutorial)</summary>
    private void ShowPopup6_TurnCycle()
    {
        if (_tutorialCtrl == null || !_tutorialCtrl.IsActive || _tutorialPopup == null) return;

        _tutorialPopup.HighlightTarget = _endTurnButton;
        _tutorialCtrl.ShowPopup("p6_turncycle",
            onContinue: () =>
            {
                _tutorialCtrl?.EndTutorial();
                GD.Print("[DuelScene] Tutorial popup sequence complete — free play.");
            }
        );
    }

    /// <summary>
    /// Find a player creature node on the board for highlight purposes.
    /// Returns the first occupied player lane slot.
    /// </summary>
    private Node? FindPlayerCreatureNode()
    {
        foreach (var slot in _playerSlots)
        {
            if (slot.GetChildCount() > 0)
            {
                foreach (var child in slot.GetChildren())
                {
                    if (child is Control c)
                        return c;
                }
            }
        }
        return null;
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
                "res://content/cards/dawn.json",
                "res://content/cards/tutorial_pack.json"
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

        // Tutorial: detect first summon to trigger Popup 4a
        if (_tutorialCtrl != null && _tutorialCtrl.IsActive
            && !_tutorialSummonedThisDuel && state != null && state.Players.Length > 0)
        {
            // Check if any player lane is now occupied (first summon of the duel)
            for (int i = 0; i < 5; i++)
            {
                if (state.Players[0].Lanes[i].Occupant != null)
                {
                    _tutorialSummonedThisDuel = true;
                    GD.Print("[DuelScene] Player summoned creature — triggering Popup 4a.");
                    Callable.From(ShowPopup4a_AttackingYourTurn).CallDeferred();
                    break;
                }
            }
        }

        // Tutorial: detect face hit to trigger Popup 5
        if (_tutorialCtrl != null && _tutorialCtrl.IsActive
            && _tutorialSummonedThisDuel && state != null && _prevEnemyVigor >= 0)
        {
            int currentEnemyVigor = state.Players[1].Vigor;
            if (currentEnemyVigor < _prevEnemyVigor)
            {
                int damage = _prevEnemyVigor - currentEnemyVigor;
                if (damage > 0)
                {
                    GD.Print($"[DuelScene] Face hit detected ({damage} dmg) — triggering Popup 5.");
                    Callable.From(ShowPopup5_FaceHit).CallDeferred();
                }
            }
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
        if (amount <= 0) return;

        var ftScene = GD.Load<PackedScene>("res://scenes/effects/FloatingText.tscn");
        var ft = ftScene.Instantiate<FloatingText>();
        AddChild(ft);

        Vector2 pos;
        Color color;
        string prefixAndAmount;

        // damage
        color = Ember;
        prefixAndAmount = $"-{amount}";

        // Shake the health bar track
        var bar = isEnemy ? _enemyHealthBar : _playerHealthBar;
        if (bar != null && IsInsideTree())
        {
            var origPos = bar.Position;
            var shake = CreateTween();
            shake.TweenProperty(bar, "position", origPos + new Vector2(8, 0), 0.04f);
            shake.TweenProperty(bar, "position", origPos - new Vector2(8, 0), 0.04f);
            shake.TweenProperty(bar, "position", origPos + new Vector2(4, 0), 0.04f);
            shake.TweenProperty(bar, "position", origPos, 0.04f);
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

        // Health bars — full-width bar behind HUD
        float enemyRatio = (float)enemyHud.Vigor / enemyHud.MaxVigor;
        float playerRatio = (float)playerHud.Vigor / playerHud.MaxVigor;
        float fullWidth = GetViewportRect().Size.X;
        _enemyHealthBar.Size = new Vector2(fullWidth * Math.Clamp(enemyRatio, 0, 1), 40);
        _enemyHealthBar.Color = HealthBarColor(enemyRatio);
        _playerHealthBar.Size = new Vector2(fullWidth * Math.Clamp(playerRatio, 0, 1), 36);
        _playerHealthBar.Color = HealthBarColor(playerRatio);

        SetEnemyVigor(enemyHud.Vigor);
        SetEnemyAttunement($"{enemyHud.Attunement}/{enemyHud.AttunementMax}");
        SetPlayerVigor(playerHud.Vigor);
        SetPlayerAttunement($"{playerHud.Attunement}/{playerHud.AttunementMax}");

        // Turn indicator
        bool isMyTurn = _gsm.CurrentPlayerIndex == 0;
        _turnLabel.Text = isMyTurn
            ? $"YOUR TURN {_gsm.TurnNumber}"
            : $"ENEMY TURN {_gsm.TurnNumber}";
        _turnLabel.Modulate = isMyTurn
            ? Gold
            : Ember;
    }

    /// <summary>Health bar color: moss > gold > ember as vigor drops.</summary>
    private static Color HealthBarColor(float ratio) => ratio switch
    {
        > 0.6f => new Color(Moss.R, Moss.G, Moss.B, 0.4f),
        > 0.3f => new Color(Gold.R, Gold.G, Gold.B, 0.4f),
        _ => new Color(Ember.R, Ember.G, Ember.B, 0.4f)
    };

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
                _enemySlots[i].SetCard(info.CardDefId, info.Name, info.Attack, info.Vigor, info.IsExhausted);
        }

        // Player lanes
        var playerLanes = _gsm.GetLanes(0);
        for (int i = 0; i < 5; i++)
        {
            var info = playerLanes[i];
            if (info.IsEmpty)
                _playerSlots[i].SetEmpty();
            else
                _playerSlots[i].SetCard(info.CardDefId, info.Name, info.Attack, info.Vigor, info.IsExhausted);
        }
    }

    private void RenderHand()
    {
        // Remove old hand cards
        foreach (var card in _handCards)
            card.QueueFree();
        _handCards.Clear();

        // Rebuild from state using HBoxContainer layout
        var handScene = GD.Load<PackedScene>("res://scenes/components/HandCard.tscn");
        var hand = _gsm.GetHand(0);
        int currentAttune = _gsm.GetPlayerHud(0).Attunement;

        foreach (var info in hand)
        {
            var card = handScene.Instantiate<HandCard>();
            _handFlow.AddChild(card);
            card.SetCard(info.CardDefId, info.Name, info.Cost, info.Strata);

            // Grey out cards the player can't afford
            card.Modulate = info.Cost > currentAttune
                ? new Color(TextInactive.R, TextInactive.G, TextInactive.B, 0.6f)
                : Colors.White;

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
        // No-op — bot turn completion is visually silent
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
                ShowToast("That lane is already occupied.", Gold);
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
            // Cancel attacker selection and start playing this card instead
            _input.CancelSelection();
            _input.SelectCardForPlay(card.CardId);
            ShowToast($"Select a lane to summon {card.CardName} (cost {card.CardCost})",
                Moss);
            UpdatePlayHighlights();
        }
        else if (_input.State == InputController.InputState.SelectingLane)
        {
            // Already in lane-selection mode — switch to this card
            _input.CancelSelection();
            _input.SelectCardForPlay(card.CardId);
            ShowToast($"Select a lane to summon {card.CardName} (cost {card.CardCost})",
                Moss);
            UpdatePlayHighlights();
        }
        else
        {
            // Idle — enter lane-selection mode (tap-to-summon), no detail popup
            _input.SelectCardForPlay(card.CardId);
            ShowToast($"Select a lane to summon {card.CardName} (cost {card.CardCost})",
                Moss);
            UpdatePlayHighlights();
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
                Gold);
        }
    }

    private void OnCreatureSelectedForAttack(int attackerLane)
    {
        // Fire Popup 4b if we're waiting for it (after Popup 4a was dismissed)
        if (_tutorialCtrl != null && _tutorialCtrl.IsActive && _tutorialAwaitingCreatureSelect)
        {
            Callable.From(ShowPopup4b_ChoosingTarget).CallDeferred();
        }
    }

    private void OnAttackRequested(int attackerLane, int targetLane)
    {
        var result = _gsm.TryAttack(0, attackerLane, targetLane);
        if (!result.Success)
        {
            ShowToast(result.ErrorMessage ?? "Cannot attack.",
                Ember);
        }
        // Success — face hit detection happens in OnStateChanged
    }

    private void OnSelectionCancelled()
    {
        foreach (var slot in _enemySlots) slot.Unhighlight();
        foreach (var slot in _playerSlots) slot.Unhighlight();
    }

    /// <summary>
    /// Highlight friendly occupied lanes (attackers available) when in selecting-attacker mode.
    /// Empty enemy lanes show "→ FACE" as valid targets.
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

        // Highlight enemy lanes as potential targets when attacker is selected
        if (_input.State == InputController.InputState.SelectingAttacker)
        {
            var enemyLanes = _gsm.GetLanes(1);
            foreach (var slot in _enemySlots)
            {
                var info = enemyLanes[slot.LaneIndex];
                if (info.IsEmpty)
                    slot.HighlightAsFaceTarget(); // empty lane = attack face
                else
                    slot.Highlight(); // occupied lane = fight creature
            }
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
                Ember);
        }
        // Success — nothing special needed for new tutorial system
    }

    private Label _toastLabel = default!;

    /// <summary>
    /// Show a floating toast message near the center of the screen.
    /// Persists for 4s visible, then fades over 1s — readable on a phone.
    /// Replaces any existing toast so they never overlap.
    /// </summary>
    private void ShowToast(string message, Color color)
    {
        // Remove any existing toast before creating a new one
        if (_toastLabel != null && IsInstanceValid(_toastLabel))
        {
            _toastLabel.QueueFree();
        }

        _toastLabel = new Label();
        _toastLabel.Text = message;
        _toastLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _toastLabel.VerticalAlignment = VerticalAlignment.Center;
        _toastLabel.AddThemeFontSizeOverride("font_size", 16);
        _toastLabel.Modulate = color;
        _toastLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        _toastLabel.Position = new Vector2(
            GetViewportRect().Size.X / 2f - 150,
            GetViewportRect().Size.Y / 2f - 30
        );
        _toastLabel.Size = new Vector2(300, 60);
        AddChild(_toastLabel);

        // Hold visible for 4s, then fade over 1s
        var tween = CreateTween();
        tween.TweenInterval(4.0);
        tween.TweenProperty(_toastLabel, "modulate:a", 0.0f, 1.0f);
        tween.TweenCallback(Callable.From(() =>
        {
            if (_toastLabel != null && IsInstanceValid(_toastLabel))
                _toastLabel.QueueFree();
        }));
    }

    // ——— Mulligan phase ———

    /// <summary>
    /// Show the mulligan overlay if neither player has mulliganed yet.
    /// The overlay displays the player's opening hand and lets them select
    /// cards to shuffle back and redraw. The bot auto-mulligans.
    /// </summary>
    private void ShowMulliganIfNeeded()
    {
        if (_gsm == null || !_gsm.IsInitialized) return;
        if (_gsm.State.Players[0].HasMulliganed) return;

        _mulliganSelection.Clear();
        _mulliganPanel = BuildMulliganOverlay();
        AddChild(_mulliganPanel);

        // Bot auto-mulligan: redraw any card costing 4 or more
        var botHand = _gsm.GetHand(1);
        var botRedraw = new List<int>();
        for (int i = 0; i < botHand.Count; i++)
        {
            if (botHand[i].Cost >= 4)
                botRedraw.Add(i);
        }
        if (botRedraw.Count > 0)
        {
            _gsm.PerformMulligan(1, botRedraw);
            GD.Print($"[DuelScene] Bot mulliganed {botRedraw.Count} card(s)");
        }
    }

    private Control BuildMulliganOverlay()
    {
        var panel = new Panel();
        panel.AnchorLeft = 0.02f;
        panel.AnchorRight = 0.98f;
        panel.AnchorTop = 0.1f;
        panel.AnchorBottom = 0.9f;

        var style = new StyleBoxFlat();
        style.BgColor = new Color(BgDark.R, BgDark.G, BgDark.B, 0.98f);
        style.BorderColor = BorderStandard;
        style.BorderWidthLeft = 2;
        style.BorderWidthTop = 2;
        style.BorderWidthRight = 2;
        style.BorderWidthBottom = 2;
        panel.AddThemeStyleboxOverride("panel", style);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vbox.AnchorLeft = 0.03f;
        vbox.AnchorRight = 0.97f;
        vbox.AnchorTop = 0.03f;
        vbox.AnchorBottom = 0.85f;
        panel.AddChild(vbox);

        // Title
        var title = new Label
        {
            Text = "Mulligan — Tap cards to redraw",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutoTranslate = false
        };
        title.AddThemeFontSizeOverride("font_size", 18);
        vbox.AddChild(title);

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        // Hand cards in a horizontal row
        var handRow = new HFlowContainer();
        handRow.SizeFlagsHorizontal = (Control.SizeFlags)3; // expand
        vbox.AddChild(handRow);

        // Card label
        var hint = new Label
        {
            Text = "Your Hand:",
            AutoTranslate = false
        };
        hint.AddThemeFontSizeOverride("font_size", 13);
        hint.Modulate = TextMuted;
        vbox.AddChild(hint);

        var handInfo = _gsm.GetHand(0);
        for (int i = 0; i < handInfo.Count; i++)
        {
            int idx = i;
            var info = handInfo[i];

            var cardBtn = new Button
            {
                Text = $"[{info.Cost}] {info.Name}",
                CustomMinimumSize = new Vector2(120, 40),
                SizeFlagsHorizontal = (Control.SizeFlags)3
            };
            cardBtn.AddThemeFontSizeOverride("font_size", 13);

            // Toggle selection
            cardBtn.Pressed += () => ToggleMulliganCard(idx, cardBtn);
            handRow.AddChild(cardBtn);
        }

        // Bottom buttons
        var btnRow = new HBoxContainer();
        btnRow.Alignment = BoxContainer.AlignmentMode.Center;
        btnRow.SizeFlagsHorizontal = (Control.SizeFlags)3; // expand
        btnRow.CustomMinimumSize = new Vector2(0, 50);
        vbox.AddChild(btnRow);

        var confirmBtn = new Button
        {
            Text = "Confirm Redraw",
            CustomMinimumSize = new Vector2(140, 44)
        };
        confirmBtn.Pressed += OnMulliganConfirm;
        btnRow.AddChild(confirmBtn);

        btnRow.AddChild(new Control { CustomMinimumSize = new Vector2(16, 0) });

        var keepBtn = new Button
        {
            Text = "Keep Hand",
            CustomMinimumSize = new Vector2(140, 44)
        };
        keepBtn.Pressed += OnMulliganKeep;
        btnRow.AddChild(keepBtn);

        return panel;
    }

    private void ToggleMulliganCard(int index, Button btn)
    {
        if (_mulliganSelection.Contains(index))
        {
            _mulliganSelection.Remove(index);
            btn.Modulate = TextPrimary;
        }
        else
        {
            _mulliganSelection.Add(index);
            btn.Modulate = Gold; // selected highlight
        }
    }

    private void OnMulliganConfirm()
    {
        var indices = _mulliganSelection.OrderBy(i => i).ToList();
        var result = _gsm.PerformMulligan(0, indices);
        if (result.Success)
        {
            var count = indices.Count;
            ShowToast(count > 0
                ? $"Mulligan: redrew {count} card(s)"
                : "No cards redrawn — hand kept", Moss);
        }
        else
        {
            ShowToast(result.ErrorMessage ?? "Mulligan failed", Gold);
        }

        DismissMulligan();
    }

    private void OnMulliganKeep()
    {
        _gsm.PerformMulligan(0, new List<int>()); // decline, just mark used
        ShowToast("Hand kept — good luck!", TextSecondary);
        DismissMulligan();
    }

    private void DismissMulligan()
    {
        if (_mulliganPanel != null)
        {
            _mulliganPanel.QueueFree();
            _mulliganPanel = null;
        }
    }

    private void OnGameOver(int winnerIndex)
    {
        _turnLabel.Text = winnerIndex == 0 ? "You Win!" : "You Lose!";
        _turnLabel.Modulate = winnerIndex == 0
            ? Gold
            : Ember;

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
                outroLabel.Modulate = new Color(TextPrimary.R, TextPrimary.G, TextPrimary.B, 0.95f);
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
            rewardLabel.Modulate = Moss;
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
        style.BgColor = new Color(BgDark.R, BgDark.G, BgDark.B, 0.95f);
        style.BorderColor = winnerIndex == 0 ? Gold : Ember;
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
        title.Modulate = winnerIndex == 0 ? Gold : Ember;
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

    // ——— Tutorial helpers ———

    // ——— Public update methods ———

    public void SetEnemyVigor(int vigor) => _enemyVigorValue.Text = vigor.ToString();
        public void SetEnemyAttunement(string text) => _enemyAttuneValue.Text = text;
        public void SetPlayerVigor(int vigor) => _playerVigorValue.Text = vigor.ToString();
        public void SetPlayerAttunement(string text) => _playerAttuneValue.Text = text;

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
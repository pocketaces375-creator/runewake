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
    private MarginContainer _handArea;
    private HBoxContainer _handFlow;
    private Button? _endTurnButton;

    // Health bar ColorRects
    private ColorRect _enemyHealthBar = default!;
    private ColorRect _playerHealthBar = default!;

    // Deck + Artifact group rects (TASK-H)
    private Control _playerGroupRect = default!;
    private Control _enemyGroupRect = default!;

    // TASK-UI3b: Altar battlefield container and slots
    private AltarField _altarField = default!;
    private Control _altarContainer = default!;

    private readonly List<LaneSlot> _enemySlots = new(5);
    private readonly List<LaneSlot> _playerSlots = new(5);
    private readonly List<HandCard> _handCards = new();

    // Card sizes computed from viewport height (FIX 3a)
    private float _handCardHeight = 180f;
    private float _boardCardHeight = 200f;

    // TASK-UI3a: Enemy top bar (replaces arsenal group + portrait + old EnemyHUD)
    private Control _enemyTopBar = default!;
    private Label _enemyDeckValue = default!;
    private Label _enemyBarrowValue = default!;
    private Label _enemyNameLabel = default!;
    private Label _enemySubtitleLabel = default!;
    private readonly Control[] _enemyArtifactMinis = new Control[2];
    private readonly Label[] _enemyArtifactNameLabels = new Label[2];
    private readonly Label[] _enemyArtifactChargeLabels = new Label[2];

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
        // Wire HUD nodes (TASK-UI3a: enemy HUD replaced by programmatic top bar)
        _playerVigorValue = GetNode<Label>("PlayerHUD/PlayerHudRow/PlayerVigorValue");
        _playerAttuneValue = GetNode<Label>("PlayerHUD/PlayerHudRow/PlayerAttuneValue");
        _turnLabel = GetNode<Label>("TurnLabel");
        _handArea = GetNode<MarginContainer>("HandArea");
        _handFlow = GetNode<HBoxContainer>("HandArea/HandFlow");

        // Card sizing from viewport height — proportional to screen size
        ScaleCardSizes(GetViewportRect().Size.Y);

        // Step 1: Typography — apply display font (Cinzel) to headers, body font (Inter) to data
        ApplyHeaderFont(_turnLabel, FontSmall);
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
            // Floor must be darker than lanes and cards so it recedes.
            // Texture is baked dark (~39 lum); this keeps a warm cast without lifting it.
            boardBg.Modulate = new Color(0.52f, 0.50f, 0.46f, 1.0f);
        }
        else
        {
            GD.PrintErr("[DuelScene] Failed to load stone_board.png — board background will be empty.");
        }

        // Health bar track (player only — TASK-UI3a: enemy uses vigor chip instead)
        var playerTrack = new ColorRect { Name = "PlayerHealthTrack", Color = new Color(0.06f, 0.05f, 0.04f, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        playerTrack.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        playerTrack.OffsetTop = -36;
        AddChild(playerTrack);

        _playerHealthBar = new ColorRect { Name = "PlayerHealthBar", Color = Colors.Transparent, MouseFilter = MouseFilterEnum.Ignore };
        _playerHealthBar.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _playerHealthBar.OffsetTop = -36;
        AddChild(_playerHealthBar);

        // TASK-UI3a: Build enemy top bar (74px, replaces old EnemyHUD + arsenal group + portrait)
        BuildEnemyTopBar();

        // Move player HUD in front of health bar
        var playerHud = GetNode<CenterContainer>("PlayerHUD");
        RemoveChild(playerHud);
        AddChild(playerHud);

        // TASK-UI3b: Build altar battlefield (replaces straight HBox lanes)
        BuildAltarField();

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
            _enemyNameLabel.Text = encounter.Name;

            var config = new GameConfig
            {
                Seed = CampaignContext.DebugSeed ?? (ulong)GD.Randi(),
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

        // ═══ TASK-H/UI3a: Player arsenal group (portrait + deck + artifact frames) ═══
        AddArtifactSlotFrames();
        // ═══ END TASK-H ═══

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

                // ═══ TASK-F4B: Pre-place 3 creatures per side before capture ═══
                PrePlaceCreatures();

                // Capture after board renders
                var snapTimer = new Godot.Timer();
                snapTimer.OneShot = true;
                snapTimer.WaitTime = 1.0f;
                snapTimer.Timeout += () =>
                {
                    var img = GetViewport().GetTexture().GetImage();
                    if (img != null)
                        img.SavePng("/home/fictive/runewake/artifacts/captures/duel_test.png");
                    GD.Print("[CAPTURE] duel_test.png saved");

                    // Write meta.json with screen-space card rects
                    var meta = new System.Text.StringBuilder();
                    meta.Append("{\n");

                    // Capture hand card info from _handCards
                    meta.Append("  \"expected_hand_card_count\": 4,\n");
                    meta.Append("  \"expected_board_card_count\": 10,\n");
                    meta.Append("  \"hand_cards\": [\n");
                    for (int ci = 0; ci < _handCards.Count; ci++)
                    {
                        var hc = _handCards[ci];
                        var r = hc.GetRect();
                        // GetRect returns local coords; to get global need GlobalPosition
                        var gp = hc.GetScreenTransform().Origin;
                        var nameR = hc.GetNodeOrNull<Label>("Content/CardName");
                        var nameRect = new Rect2();
                        if (nameR != null)
                        {
                            var np = nameR.GetScreenTransform().Origin;
                            nameRect = new Rect2(np.X, np.Y, nameR.Size.X, nameR.Size.Y);
                        }

                        meta.Append("    {\n");
                        meta.Append($"      \"card_id\": \"{hc.CardId}\",\n");
                        meta.Append($"      \"name\": \"{hc.CardName}\",\n");
                        meta.Append($"      \"rect\": {{ \"x\": {gp.X:F1}, \"y\": {gp.Y:F1}, \"w\": {r.Size.X:F1}, \"h\": {r.Size.Y:F1} }},\n");
                        meta.Append($"      \"name_rect\": {{ \"x\": {nameRect.Position.X:F1}, \"y\": {nameRect.Position.Y:F1}, \"w\": {nameRect.Size.X:F1}, \"h\": {nameRect.Size.Y:F1} }}\n");
                        meta.Append("    }");
                        if (ci < _handCards.Count - 1)
                            meta.Append(",");
                        meta.Append("\n");
                    }
                    meta.Append("  ],\n");

                    // Capture arsenal group rects (TASK-H: deck + artifact groups)
                    meta.Append("  \"groups\": [\n");
                    var playerGp = _playerGroupRect.GetScreenTransform().Origin;
                    var playerGpSize = _playerGroupRect.Size;
                    meta.Append("    {\n");
                    meta.Append("      \"side\": \"player\",\n");
                    meta.Append($"      \"rect\": {{ \"x\": {playerGp.X:F1}, \"y\": {playerGp.Y:F1}, \"w\": {playerGpSize.X:F1}, \"h\": {playerGpSize.Y:F1} }}\n");
                    meta.Append("    },\n");
                    var enemyGp = _enemyGroupRect.GetScreenTransform().Origin;
                    var enemyGpSize = _enemyGroupRect.Size;
                    meta.Append("    {\n");
                    meta.Append("      \"side\": \"enemy\",\n");
                    meta.Append($"      \"rect\": {{ \"x\": {enemyGp.X:F1}, \"y\": {enemyGp.Y:F1}, \"w\": {enemyGpSize.X:F1}, \"h\": {enemyGpSize.Y:F1} }}\n");
                    meta.Append("    }\n");
                    meta.Append("  ],\n");

                    // Capture board card info from player and enemy slots
                    meta.Append("  \"board_cards\": [\n");
                    int bi = 0;
                    foreach (var slot in _playerSlots)
                    {
                        var r = slot.GetRect();
                        var gp = slot.GetScreenTransform().Origin;
                        var nameR = slot.GetNodeOrNull<Label>("Content/CardName");
                        var nameRect = new Rect2();
                        if (nameR != null)
                        {
                            var np = nameR.GetScreenTransform().Origin;
                            nameRect = new Rect2(np.X, np.Y, nameR.Size.X, nameR.Size.Y);
                        }

                        meta.Append("    {\n");
                        meta.Append($"      \"slot\": \"player_{slot.LaneIndex}\",\n");
                        meta.Append($"      \"rect\": {{ \"x\": {gp.X:F1}, \"y\": {gp.Y:F1}, \"w\": {r.Size.X:F1}, \"h\": {r.Size.Y:F1} }},\n");
                        meta.Append($"      \"name_rect\": {{ \"x\": {nameRect.Position.X:F1}, \"y\": {nameRect.Position.Y:F1}, \"w\": {nameRect.Size.X:F1}, \"h\": {nameRect.Size.Y:F1} }},\n");
                        meta.Append($"      \"state\": \"{(_gsm.State.Players[0].Lanes[slot.LaneIndex].Occupant != null ? "occupied" : "empty")}\"\n");
                        meta.Append("    },");
                        meta.Append("\n");
                        bi++;
                    }
                    foreach (var slot in _enemySlots)
                    {
                        var r = slot.GetRect();
                        var gp = slot.GetScreenTransform().Origin;
                        var nameR = slot.GetNodeOrNull<Label>("Content/CardName");
                        var nameRect = new Rect2();
                        if (nameR != null)
                        {
                            var np = nameR.GetScreenTransform().Origin;
                            nameRect = new Rect2(np.X, np.Y, nameR.Size.X, nameR.Size.Y);
                        }

                        meta.Append("    {\n");
                        meta.Append($"      \"slot\": \"enemy_{slot.LaneIndex}\",\n");
                        meta.Append($"      \"rect\": {{ \"x\": {gp.X:F1}, \"y\": {gp.Y:F1}, \"w\": {r.Size.X:F1}, \"h\": {r.Size.Y:F1} }},\n");
                        meta.Append($"      \"name_rect\": {{ \"x\": {nameRect.Position.X:F1}, \"y\": {nameRect.Position.Y:F1}, \"w\": {nameRect.Size.X:F1}, \"h\": {nameRect.Size.Y:F1} }},\n");
                        meta.Append($"      \"state\": \"{(_gsm.State.Players[1].Lanes[slot.LaneIndex].Occupant != null ? "occupied" : "empty")}\"\n");
                        meta.Append("    }");
                        if (bi < (_playerSlots.Count + _enemySlots.Count) - 1)
                            meta.Append(",");
                        meta.Append("\n");
                        bi++;
                    }
                    meta.Append("\n  ]\n");
                    meta.Append("}\n");

                    var metaPath = "/home/fictive/runewake/artifacts/captures/duel_test.meta.json";
                    using (var writer = new System.IO.StreamWriter(metaPath))
                    {
                        writer.Write(meta.ToString());
                    }
                    GD.Print("[CAPTURE] duel_test.meta.json saved");

                    // Run layout verification
                    int failed = RunLayoutVerification();
                    GD.Print($"[VERIFY] Layout checks: {failed} failed");
                    if (failed > 0)
                        GetTree().Quit(1);
                    else
                        GetTree().Quit(0);
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
    /// TASK-UI3a: Build the enemy top bar — 74px full-width bar replacing old EnemyHUD, arsenal, and portrait.
    /// LEFT: portrait chip + stat chips (vigor, attune, deck, barrow — 50x50 rounded, label under value)
    /// CENTER: enemy name (small-caps ~23px) over subtitle line
    /// RIGHT: two Artifact mini-cards (92x56: glyph + one-word name + charge pips bottom-right)
    /// All values are live-bound through fields set here and updated in RenderHud().
    /// _enemyGroupRect is set to this bar for capture meta.json compatibility.
    /// </summary>
    private void BuildEnemyTopBar()
    {
        float barH = 74f;
        float vw = GetViewportRect().Size.X;

        // Root container
        _enemyTopBar = new Control { Name = "EnemyTopBar" };
        _enemyTopBar.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _enemyTopBar.Size = new Vector2(0, barH);
        _enemyTopBar.Position = new Vector2(0, 0);
        AddChild(_enemyTopBar);

        // Outer HBox: [LEFT_CLUSTER][CENTER][RIGHT_CLUSTER]
        var barRow = new HBoxContainer();
        barRow.SizeFlagsHorizontal = (Control.SizeFlags)3; // expand fill
        barRow.SizeFlagsVertical = (Control.SizeFlags)3;
        barRow.AnchorLeft = 0f;
        barRow.AnchorRight = 1f;
        barRow.AnchorTop = 0f;
        barRow.AnchorBottom = 1f;
        barRow.AddThemeConstantOverride("separation", 6);
        _enemyTopBar.AddChild(barRow);

        // ═══ LEFT CLUSTER ═══
        var leftCluster = new HBoxContainer();
        leftCluster.SizeFlagsHorizontal = 0; // no expand
        leftCluster.AddThemeConstantOverride("separation", 4);
        barRow.AddChild(leftCluster);

        // Portrait chip (52x56)
        var portraitChip = new PanelContainer();
        portraitChip.CustomMinimumSize = new Vector2(52, 56);
        portraitChip.MouseFilter = MouseFilterEnum.Ignore;
        var portraitStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.15f, 0.12f, 0.09f, 0.85f),
            BorderColor = new Color(0.6f, 0.5f, 0.25f, 0.5f),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6
        };
        portraitChip.AddThemeStyleboxOverride("panel", portraitStyle);
        var pLabel = new Label { Text = "?", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        pLabel.AddThemeFontSizeOverride("font_size", 20);
        pLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.5f, 0.25f, 0.5f));
        portraitChip.AddChild(pLabel);
        leftCluster.AddChild(portraitChip);

        // Stat chips: Vigor (red-tinted), Attune, Deck, Barrow — 50x50 rounded, label under value
        var chipDefs = new[] {
            // field: value label setter, name
            ("vigor", new Color(0.6f, 0.25f, 0.15f, 0.3f), "VIGOR"),
            ("attune", new Color(0.6f, 0.5f, 0.2f, 0.3f), "ATTUNE"),
            ("deck", new Color(0.4f, 0.4f, 0.35f, 0.25f), "DECK"),
            ("barrow", new Color(0.35f, 0.3f, 0.4f, 0.25f), "BARROW")
        };

        Label vigorValue = null!, attuneValue = null!, deckValue = null!, barrowValue = null!;

        foreach (var (field, tint, labelText) in chipDefs)
        {
            var chip = new PanelContainer();
            chip.CustomMinimumSize = new Vector2(50, 50);
            chip.MouseFilter = MouseFilterEnum.Ignore;
            var chipStyle = new StyleBoxFlat
            {
                BgColor = tint,
                BorderColor = new Color(0.5f, 0.45f, 0.35f, 0.5f),
                BorderWidthLeft = 1, BorderWidthTop = 1,
                BorderWidthRight = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
                CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6
            };
            chip.AddThemeStyleboxOverride("panel", chipStyle);

            var vbox = new VBoxContainer();
            vbox.MouseFilter = MouseFilterEnum.Ignore;
            chip.AddChild(vbox);

            var valLabel = new Label { Text = "0", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, MouseFilter = MouseFilterEnum.Ignore };
            valLabel.AddThemeFontSizeOverride("font_size", FontBody);
            valLabel.AddThemeColorOverride("font_color", TextPrimary);
            vbox.AddChild(valLabel);

            var nameLabel = new Label { Text = labelText, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, MouseFilter = MouseFilterEnum.Ignore };
            nameLabel.AddThemeFontSizeOverride("font_size", FontTiny);
            nameLabel.AddThemeColorOverride("font_color", TextMuted);
            vbox.AddChild(nameLabel);

            leftCluster.AddChild(chip);

            // Store references for live binding
            switch (field)
            {
                case "vigor": vigorValue = valLabel; _enemyVigorValue = valLabel; break;
                case "attune": attuneValue = valLabel; _enemyAttuneValue = valLabel; break;
                case "deck": deckValue = valLabel; break;
                case "barrow": barrowValue = valLabel; break;
            }
        }
        _enemyDeckValue = deckValue!;
        _enemyBarrowValue = barrowValue!;

        // ═══ CENTER: enemy name over subtitle ═══
        var centerVbox = new VBoxContainer();
        centerVbox.SizeFlagsHorizontal = (Control.SizeFlags)3; // expand
        centerVbox.SizeFlagsVertical = (Control.SizeFlags)4; // center
        centerVbox.MouseFilter = MouseFilterEnum.Ignore;
        barRow.AddChild(centerVbox);

        _enemyNameLabel = new Label { Text = "Enemy", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, MouseFilter = MouseFilterEnum.Ignore };
        _enemyNameLabel.AddThemeFontSizeOverride("font_size", 23);
        _enemyNameLabel.AddThemeColorOverride("font_color", TextPrimary);
        _enemyNameLabel.AddThemeConstantOverride("line_spacing", -2);
        ApplyHeaderFont(_enemyNameLabel, 23);
        centerVbox.AddChild(_enemyNameLabel);

        _enemySubtitleLabel = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, MouseFilter = MouseFilterEnum.Ignore };
        _enemySubtitleLabel.AddThemeFontSizeOverride("font_size", FontSmall);
        _enemySubtitleLabel.AddThemeColorOverride("font_color", TextMuted);
        ApplyBodyFont(_enemySubtitleLabel, FontSmall);
        centerVbox.AddChild(_enemySubtitleLabel);

        // ═══ RIGHT CLUSTER: Artifact mini-cards (92x56 each) ═══
        var rightCluster = new HBoxContainer();
        rightCluster.SizeFlagsHorizontal = 0; // no expand
        rightCluster.AddThemeConstantOverride("separation", 4);
        barRow.AddChild(rightCluster);

        for (int i = 0; i < 2; i++)
        {
            var mini = new PanelContainer();
            mini.CustomMinimumSize = new Vector2(92, 56);
            mini.MouseFilter = MouseFilterEnum.Ignore;
            mini.SizeFlagsHorizontal = 0;
            var miniStyle = new StyleBoxFlat
            {
                BgColor = new Color(0.12f, 0.10f, 0.08f, 0.8f),
                BorderColor = new Color(0.6f, 0.5f, 0.25f, 0.4f),
                BorderWidthLeft = 1, BorderWidthTop = 1,
                BorderWidthRight = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
            };
            mini.AddThemeStyleboxOverride("panel", miniStyle);

            var miniInner = new HBoxContainer();
            miniInner.MouseFilter = MouseFilterEnum.Ignore;
            mini.AddChild(miniInner);

            // Glyph placeholder
            var glyph = new Label { Text = "◇", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            glyph.AddThemeFontSizeOverride("font_size", FontSubtitle);
            glyph.AddThemeColorOverride("font_color", new Color(0.6f, 0.5f, 0.25f, 0.5f));
            glyph.CustomMinimumSize = new Vector2(28, 0);
            miniInner.AddChild(glyph);

            // Name + charges
            var nameChargeVbox = new VBoxContainer();
            nameChargeVbox.MouseFilter = MouseFilterEnum.Ignore;
            nameChargeVbox.SizeFlagsHorizontal = (Control.SizeFlags)3;
            miniInner.AddChild(nameChargeVbox);

            var artName = new Label { Text = "—", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, MouseFilter = MouseFilterEnum.Ignore };
            artName.AddThemeFontSizeOverride("font_size", FontTiny);
            artName.AddThemeColorOverride("font_color", TextMuted);
            nameChargeVbox.AddChild(artName);

            // Charge pips (bottom-right area within mini)
            var chargeLabel = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, MouseFilter = MouseFilterEnum.Ignore };
            chargeLabel.AddThemeFontSizeOverride("font_size", 7);
            chargeLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.5f, 0.25f, 0.7f));
            nameChargeVbox.AddChild(chargeLabel);

            _enemyArtifactMinis[i] = mini;
            _enemyArtifactNameLabels[i] = artName;
            _enemyArtifactChargeLabels[i] = chargeLabel;
            rightCluster.AddChild(mini);
        }

        // Set _enemyGroupRect to the top bar for capture meta.json compatibility
        _enemyGroupRect = _enemyTopBar;

        // Point legacy fields to new bar elements (tutorial popup uses _enemyName/VigorValue/AttuneValue)
        _enemyName = _enemyNameLabel;

        GD.Print("[DUEL] TASK-UI3a: Enemy top bar built (74px)");
    }

    /// <summary>
    /// TASK-H: Deck + Artifact side-group layout (DECISION CHANGE, supersedes FIX-5 portrait-flanking).
    /// Each player's deck pile + TWO Artifact frames form one visual group ("this is my sword and shield,
    /// next to my arsenal"). Player's group in the lower-left area, opponent's mirrored upper-right.
    /// Portraits stay; the Artifacts anchor to the DECK group. Placeholder frames with faint "Artifact" labels.
    /// The group rects are stored in _playerGroupRect/_enemyGroupRect and written to duel_test.meta.json.
    /// TASK-UI3a: Enemy side removed — replaced by top bar.
    /// </summary>
    private void AddArtifactSlotFrames()
    {
        float vw = GetViewportRect().Size.X;
        float vh = GetViewportRect().Size.Y;
        float frameSize = vh * 0.10f; // 10% of viewport height — compact but visible

        // Sizes
        float portraitW = frameSize * 1.2f;
        float portraitH = frameSize * 1.4f;
        float deckW = frameSize * 0.85f;
        float deckH = frameSize * 1.05f;
        float artW = frameSize * 0.8f;
        float artH = frameSize * 0.95f;
        float gap = 5f;
        float pad = 6f;

        // ═══ PLAYER: lower-left area ═══
        // Portrait stays at the left edge; the arsenal group sits beside it.
        float playerY = vh - portraitH - 8f;
        float leftX = 8f;

        var playerPortrait = MakePortraitFrame(new Vector2(leftX, playerY), frameSize);
        AddChild(playerPortrait);

        // Arsenal group: [Deck Pile][Artifact][Artifact]
        float groupX = leftX + portraitW + 10f;
        float groupY = playerY + (portraitH - deckH) * 0.5f;
        _playerGroupRect = BuildArsenalGroup(groupX, groupY, deckW, deckH, artW, artH, gap, pad, isPlayer: true);
        AddChild(_playerGroupRect);

        // ═══ TASK-UI3a: Enemy arsenal group removed — replaced by top bar ═══
        // _enemyGroupRect is now set in BuildEnemyTopBar() to point to the top bar.

        GD.Print($"[DUEL] TASK-H deck+artifact groups: player @({groupX:F0},{groupY:F0}) (enemy group moved to top bar)");
    }

    /// <summary>Portrait placeholder frame (kept from FIX-5, artifacts no longer flank it).</summary>
    private Control MakePortraitFrame(Vector2 pos, float frameSize)
    {
        var portrait = new PanelContainer();
        portrait.CustomMinimumSize = new Vector2(frameSize * 1.2f, frameSize * 1.4f);
        portrait.Position = pos;
        var pStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.15f, 0.12f, 0.09f, 0.85f),
            BorderColor = new Color(0.6f, 0.5f, 0.25f, 0.5f),
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8
        };
        portrait.AddThemeStyleboxOverride("panel", pStyle);
        var pLabel = new Label { Text = "?", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        pLabel.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(frameSize * 0.5f));
        pLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.5f, 0.25f, 0.4f));
        portrait.AddChild(pLabel);
        return portrait;
    }

    /// <summary>
    /// Build one arsenal group: a subtle container rect holding the deck pile and two artifact frames.
    /// Player order (left→right): [Deck][Art][Art]; enemy order is mirrored: [Art][Art][Deck].
    /// </summary>
    private Control BuildArsenalGroup(float x, float y, float deckW, float deckH, float artW, float artH, float gap, float pad, bool isPlayer)
    {
        var group = new PanelContainer { Name = isPlayer ? "PlayerArsenalGroup" : "EnemyArsenalGroup" };
        group.Position = new Vector2(x, y);
        group.CustomMinimumSize = new Vector2(pad * 2 + deckW + artW * 2 + gap * 2, pad * 2 + deckH);
        var groupStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.07f, 0.06f, 0.45f),
            BorderColor = new Color(0.6f, 0.5f, 0.25f, 0.25f),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6
        };
        group.AddThemeStyleboxOverride("panel", groupStyle);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", (int)gap);
        group.AddChild(row);

        var deck = MakeDeckPile(deckW, deckH, isPlayer);
        var art1 = MakeArtifactFrame(artW, artH);
        var art2 = MakeArtifactFrame(artW, artH);

        if (isPlayer)
        {
            row.AddChild(deck);
            row.AddChild(art1);
            row.AddChild(art2);
        }
        else
        {
            row.AddChild(art1);
            row.AddChild(art2);
            row.AddChild(deck);
        }

        return group;
    }

    /// <summary>Deck pile visual: stacked card backs + live deck count.</summary>
    private Control MakeDeckPile(float w, float h, bool isPlayer)
    {
        var pile = new Control { CustomMinimumSize = new Vector2(w, h) };
        pile.MouseFilter = MouseFilterEnum.Ignore;

        // Stacked card backs (3 layers, offset to suggest a pile)
        for (int i = 2; i >= 0; i--)
        {
            var back = new PanelContainer
            {
                Position = new Vector2(i * 3f, i * 3f),
                CustomMinimumSize = new Vector2(w, h),
                MouseFilter = MouseFilterEnum.Ignore
            };
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.10f, 0.08f, 0.06f, 0.95f),
                BorderColor = new Color(0.6f, 0.5f, 0.25f, 0.35f),
                BorderWidthLeft = 1, BorderWidthTop = 1,
                BorderWidthRight = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
                CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3
            };
            back.AddThemeStyleboxOverride("panel", style);
            pile.AddChild(back);
        }

        // Deck count label (live from game state)
        var count = 0;
        if (_gsm != null && _gsm.State != null)
        {
            var p = isPlayer ? _gsm.State.Players[0] : _gsm.State.Players[1];
            count = p.Deck.Count;
        }
        var label = new Label
        {
            Text = count.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(h * 0.35f));
        label.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.45f, 0.9f));
        pile.AddChild(label);

        return pile;
    }

    /// <summary>Placeholder artifact card frame with a faint "Artifact" label.</summary>
    private Control MakeArtifactFrame(float w, float h)
    {
        var frame = new PanelContainer { CustomMinimumSize = new Vector2(w, h) };
        frame.MouseFilter = MouseFilterEnum.Ignore;
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.10f, 0.08f, 0.8f),
            BorderColor = new Color(0.6f, 0.5f, 0.25f, 0.4f),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        };
        frame.AddThemeStyleboxOverride("panel", style);

        var label = new Label
        {
            Text = "Artifact",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.AddThemeFontSizeOverride("font_size", Mathf.Max(8, Mathf.RoundToInt(h * 0.16f)));
        label.AddThemeColorOverride("font_color", new Color(0.6f, 0.5f, 0.25f, 0.45f));
        frame.AddChild(label);
        return frame;
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
    /// Compute card sizes from viewport height (FIX 3a): hand ~180px at 1080p,
    /// board ~200px at 1080p. Scales proportionally on smaller viewports.
    /// </summary>
    private void ScaleCardSizes(float viewportHeight)
    {
        // Reference: 1080p design height = 648 viewport (canvas_items stretch).
        // 180px hand / 200px board at that reference; scale linearly with height.
        float reference = 648f;
        float scale = viewportHeight / reference;

        _handCardHeight = Mathf.Max(130f, 180f * scale);   // never below usable size
        _boardCardHeight = Mathf.Max(150f, 200f * scale);

        // Grow the hand area to fit larger cards (was 200px tall)
        _handArea.OffsetTop = -(_handCardHeight + 40f);

        GD.Print($"[DUEL] viewport height {viewportHeight:F0} → hand {_handCardHeight:F0}px, board {_boardCardHeight:F0}px");
    }

    /// <summary>
    /// TASK-UI3b: Build the altar battlefield — ellipse background, arc-positioned slots, rune glyphs.
    /// Replaces the old straight HBoxContainer lanes with facing arcs inside an altar ellipse.
    /// Ellipse ~1240x418 design units centered under the top bar, with border, dashed ring, glow.
    /// </summary>
    private void BuildAltarField()
    {
        var board = GetNode("Board");
        float vw = GetViewportRect().Size.X;
        float vh = GetViewportRect().Size.Y;
        float scale = vh / 648f;

        // ── Altar ellipse background ──
        _altarField = new AltarField { Name = "AltarField" };
        _altarField.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _altarField.MouseFilter = Control.MouseFilterEnum.Ignore;
        board.AddChild(_altarField);

        // ── Container for arc-positioned slots ──
        _altarContainer = new Control { Name = "AltarContainer" };
        _altarContainer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _altarContainer.MouseFilter = Control.MouseFilterEnum.Ignore;
        board.AddChild(_altarContainer);

        // ── Rune glyphs around ellipse edge (6 unicode runic characters) ──
        string runes = "ᚠᚢᚦᚨᚱᚲ"; // Fehu, Uruz, Thurisaz, Ansuz, Raidho, Kenaz
        float ellipseRx = 620f * scale;
        float ellipseRy = 209f * scale;
        Vector2 ellipseCenter = new Vector2(vw / 2f, vh * 0.39f);

        // 6 positions around the ellipse (top, top-right, right, bottom, bottom-left, left)
        float[] runeAngles = { -Mathf.Pi / 2f, -Mathf.Pi / 6f, Mathf.Pi / 6f, Mathf.Pi / 2f, 5f * Mathf.Pi / 6f, -5f * Mathf.Pi / 6f };
        for (int i = 0; i < 6 && i < runes.Length; i++)
        {
            float angle = runeAngles[i];
            float rx = 630f * scale;
            float ry = 215f * scale;
            float gx = ellipseCenter.X + rx * Mathf.Cos(angle) - 8f;
            float gy = ellipseCenter.Y + ry * Mathf.Sin(angle) - 8f;

            var rune = new Label
            {
                Text = runes[i].ToString(),
                Position = new Vector2(gx, gy),
                CustomMinimumSize = new Vector2(16, 16),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            rune.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(12 * scale));
            rune.AddThemeColorOverride("font_color", new Color(0.34f, 0.29f, 0.17f, 0.3f));
            _altarContainer.AddChild(rune);
        }

        // Populate arc slots
        PopulateLanes();

        GD.Print("[DUEL] TASK-UI3b: Altar field built");
    }

    /// <summary>
    /// TASK-UI3b: Create 5 lane slot instances for each side, positioned on facing arcs
    /// inside the altar ellipse. Outer slots get vertical offset + rotation for arc curvature.
    /// </summary>
    private void PopulateLanes()
    {
        var laneScene = GD.Load<PackedScene>("res://scenes/components/LaneSlot.tscn");
        float vw = GetViewportRect().Size.X;
        float vh = GetViewportRect().Size.Y;
        float scale = vh / 648f;
        float slotH = 176f * scale;
        float slotW = 206f * scale;

        // Arc geometry: X positions (centers) spread across the ellipse
        float centerX = vw / 2f;
        float spacing = 230f * scale;

        // Enemy baseline Y: top arc, centered within ellipse top half
        float enemyBaseY = GetViewportRect().Size.Y * 0.195f;
        // Player baseline Y: bottom arc
        float playerBaseY = GetViewportRect().Size.Y * 0.59f;

        for (int i = 0; i < 5; i++)
        {
            float xCenter = centerX + (i - 2) * spacing;
            float x = xCenter - slotW / 2f;

            // ── Enemy slot (top arc, bowing downward) ──
            float enemyYOffset = i switch { 0 or 4 => 34f, 1 or 3 => 8f, _ => 0f } * scale;
            float enemyY = enemyBaseY + enemyYOffset;
            var enemySlot = laneScene.Instantiate<LaneSlot>();
            enemySlot.Row = 0;
            enemySlot.LaneIndex = i;
            enemySlot.LaneTapped += OnLaneTapped;
            _altarContainer.AddChild(enemySlot);
            // Font sizing via ScaleTo (needs _Ready first, so call after AddChild)
            enemySlot.ScaleTo(slotH);
            // Override to arc slot proportions
            enemySlot.CustomMinimumSize = new Vector2(slotW, slotH);
            enemySlot.Size = new Vector2(slotW, slotH);
            enemySlot.Position = new Vector2(x, enemyY);
            enemySlot.PivotOffset = new Vector2(slotW / 2f, slotH / 2f);
            enemySlot.Rotation = i switch { 0 => Mathf.DegToRad(4f), 1 => Mathf.DegToRad(2f), 3 => Mathf.DegToRad(-2f), 4 => Mathf.DegToRad(-4f), _ => 0f };
            _enemySlots.Add(enemySlot);

            // ── Player slot (bottom arc, bowing upward) ──
            float playerYOffset = i switch { 0 or 4 => 34f, 1 or 3 => 8f, _ => 0f } * scale;
            float playerY = playerBaseY - playerYOffset;
            var playerSlot = laneScene.Instantiate<LaneSlot>();
            playerSlot.Row = 1;
            playerSlot.LaneIndex = i;
            playerSlot.LaneTapped += OnLaneTapped;
            playerSlot.CardDropped += OnCardDropped;
            _altarContainer.AddChild(playerSlot);
            playerSlot.ScaleTo(slotH);
            playerSlot.CustomMinimumSize = new Vector2(slotW, slotH);
            playerSlot.Size = new Vector2(slotW, slotH);
            playerSlot.Position = new Vector2(x, playerY);
            playerSlot.PivotOffset = new Vector2(slotW / 2f, slotH / 2f);
            playerSlot.Rotation = i switch { 0 => Mathf.DegToRad(-4f), 1 => Mathf.DegToRad(-2f), 3 => Mathf.DegToRad(2f), 4 => Mathf.DegToRad(4f), _ => 0f };
            _playerSlots.Add(playerSlot);
        }

        GD.Print($"[DUEL] TASK-UI3b: Populated {_enemySlots.Count} enemy + {_playerSlots.Count} player arc slots");
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

        // Shake the health bar (player) or vigor chip (enemy — TASK-UI3a)
        if (isEnemy)
        {
            // Shake the enemy vigor value label (in the top bar chip)
            if (_enemyVigorValue != null && IsInsideTree())
            {
                var origPos = _enemyVigorValue.Position;
                var shake = CreateTween();
                shake.TweenProperty(_enemyVigorValue, "position", origPos + new Vector2(4, 0), 0.04f);
                shake.TweenProperty(_enemyVigorValue, "position", origPos - new Vector2(4, 0), 0.04f);
                shake.TweenProperty(_enemyVigorValue, "position", origPos, 0.04f);
            }
        }
        else
        {
            if (_playerHealthBar != null && IsInsideTree())
            {
                var origPos = _playerHealthBar.Position;
                var shake = CreateTween();
                shake.TweenProperty(_playerHealthBar, "position", origPos + new Vector2(8, 0), 0.04f);
                shake.TweenProperty(_playerHealthBar, "position", origPos - new Vector2(8, 0), 0.04f);
                shake.TweenProperty(_playerHealthBar, "position", origPos + new Vector2(4, 0), 0.04f);
                shake.TweenProperty(_playerHealthBar, "position", origPos, 0.04f);
            }
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
        {
            _enemyName.Text = "Enemy";
            _enemyNameLabel.Text = "Enemy";
        }

        var enemyHud = _gsm.GetPlayerHud(1);
        var playerHud = _gsm.GetPlayerHud(0);
        var state = _gsm.State;

        // TASK-UI3a: Enemy stats via top bar chips (no full-width health bar)
        SetEnemyVigor(enemyHud.Vigor);
        SetEnemyAttunement($"{enemyHud.Attunement}/{enemyHud.AttunementMax}");

        // Deck and barrow counts
        if (state != null && state.Players.Length > 1)
        {
            var p1 = state.Players[1];
            _enemyDeckValue.Text = p1.Deck.Count.ToString();
            _enemyBarrowValue.Text = p1.Barrow.Count.ToString();

            // Artifact mini-cards: display name + charges (via stored label refs)
            int artSlots = p1.ArtifactSlots?.Length ?? 0;
            for (int i = 0; i < 2; i++)
            {
                if (i < artSlots && p1.ArtifactSlots[i]?.Occupant != null)
                {
                    var slot = p1.ArtifactSlots[i];
                    var occ = slot.Occupant;
                    var artDef = ArtifactRegistry.Get(occ.CardDefId);
                    _enemyArtifactNameLabels[i].Text = artDef?.Name ?? "?";
                    int ch = slot.Charges;
                    int maxCh = slot.MaxCharges;
                    _enemyArtifactChargeLabels[i].Text = maxCh > 0
                        ? new string('•', System.Math.Min(ch, maxCh)) + new string('∘', maxCh - System.Math.Min(ch, maxCh))
                        : "";
                }
                else
                {
                    _enemyArtifactNameLabels[i].Text = "—";
                    _enemyArtifactChargeLabels[i].Text = "";
                }
            }
        }

        // Player health bar
        float playerRatio = (float)playerHud.Vigor / playerHud.MaxVigor;
        float fullWidth = GetViewportRect().Size.X;
        _playerHealthBar.Size = new Vector2(fullWidth * Math.Clamp(playerRatio, 0, 1), 36);
        _playerHealthBar.Color = HealthBarColor(playerRatio);

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

        // Compute dynamic card sizing so all cards fit with consistent spacing
        // (FIX: hand overlaps — auto-shrink when hand is full)
        float handSep = 34f;
        _handFlow.AddThemeConstantOverride("separation", (int)handSep);
        float availWidth = GetViewportRect().Size.X - 40f; // margin 20 each side; use viewport not _handArea (pre-layout)
        float aspect = 110f / 168f;
        int n = hand.Count;
        float cardW = _handCardHeight * aspect;
        float required = n * cardW + (n - 1) * handSep;
        float fitHeight = _handCardHeight;
        if (required > availWidth && n > 1)
        {
            float shrink = (availWidth - (n - 1) * handSep) / (n * aspect);
            fitHeight = Mathf.Max(110f, shrink); // floor at 110px for readability
        }

        foreach (var info in hand)
        {
            var card = handScene.Instantiate<HandCard>();
            _handFlow.AddChild(card);
            card.ScaleTo(fitHeight);
            card.SetCard(info.CardDefId, info.Name, info.Cost, info.Strata);

            // Playability: full brightness + gold badge when affordable;
            // ≤30% desaturation + red badge when not. NEVER dim to black.
            card.SetPlayable(info.Cost <= currentAttune);

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

    // ——— Layout verification gate ———

    /// <summary>
    /// Run layout verification checks and return failure count.
    /// Called after capture in AutoCaptureScreenshot mode.
    /// </summary>
    private int RunLayoutVerification()
    {
        int fails = 0;
        var viewportSize = GetViewportRect().Size;
        int artCount = 0;

        GD.Print("[VERIFY] === Layout Verification Report ===");

        float minW = float.MaxValue, maxW = float.MinValue;
        float minH = float.MaxValue, maxH = float.MinValue;

        // — Hand card checks —
        if (_handCards.Count == 0)
        {
            GD.PrintErr("[VERIFY] FAIL: No hand cards to verify");
            fails++;
        }
        else
        {

            for (int i = 0; i < _handCards.Count; i++)
            {
                var card = _handCards[i];
                var s = card.Size;
                var pos = card.Position;

                // Track size extremes
                if (s.X < minW) minW = s.X;
                if (s.X > maxW) maxW = s.X;
                if (s.Y < minH) minH = s.Y;
                if (s.Y > maxH) maxH = s.Y;

                // Viewport bounds
                if (pos.X < 0 || pos.Y < 0 || pos.X + s.X > viewportSize.X || pos.Y + s.Y > viewportSize.Y)
                {
                    GD.PrintErr($"[VERIFY] FAIL: Card {i} ({card.CardName}) at {pos} size {s} exceeds viewport {viewportSize}");
                    fails++;
                }

                // ArtRect checks
                var artRect = card.ArtRectNode;
                if (artRect != null)
                {
                    var artSize = artRect.Size;
                    var artPos = artRect.Position;

                    // Art has non-null texture and non-zero size
                    if (artSize.X > 10 && artSize.Y > 10)
                    {
                        artCount++;
                        // ArtPresence: check if FixedArtRect has a texture
                        if (artRect is FixedArtRect far && far.Texture == null)
                            GD.Print($"[VERIFY] WARN: Card {i} ({card.CardName}) ArtRect has non-zero size but no texture");
                    }

                    // Containment: ArtRect inside parent (VBox)
                    var parent = artRect.GetParent() as Control;
                    if (parent != null)
                    {
                        var parentSize = parent.Size;
                        var artEnd = artPos + artSize;
                        if (artEnd.X > parentSize.X + 2 || artEnd.Y > parentSize.Y + 2)
                        {
                            GD.PrintErr($"[VERIFY] FAIL: Card {i} ({card.CardName}) ArtRect end {artEnd} > parent size {parentSize}");
                            fails++;
                        }
                    }
                }
            }

            // Uniformity: all hand cards within 5px of same size
            float wDiff = maxW - minW;
            float hDiff = maxH - minH;
            if (wDiff > 5 || hDiff > 5)
            {
                GD.PrintErr($"[VERIFY] FAIL: Hand card sizes differ: min=({minW},{minH}) max=({maxW},{maxH})");
                fails++;
            }
            else
            {
                GD.Print($"[VERIFY] OK: All {_handCards.Count} hand cards uniform ({minW:F0}x{minH:F0})");
            }

            // Overlap: no two sibling hand cards overlap in X
            var sortedCards = _handCards.OrderBy(c => c.Position.X).ToList();
            for (int i = 1; i < sortedCards.Count; i++)
            {
                var prev = sortedCards[i - 1];
                var cur = sortedCards[i];
                float prevRight = prev.Position.X + prev.Size.X;
                float curLeft = cur.Position.X;
                if (prevRight > curLeft + 2)
                {
                    GD.PrintErr($"[VERIFY] FAIL: Hand cards overlap: \"{prev.CardName}\" right={prevRight:F0} > \"{cur.CardName}\" left={curLeft:F0}");
                    fails++;
                }
            }
        }

        // — Board slot checks —
        int laneArtCount = 0;
        foreach (var slot in _enemySlots)
        {
            if (slot.Size.X > 50 && slot.Size.Y > 50)
                laneArtCount++;
        }
        foreach (var slot in _playerSlots)
        {
            if (slot.Size.X > 50 && slot.Size.Y > 50)
                laneArtCount++;
        }

        GD.Print($"[VERIFY] Hand art textures active: {artCount}/{_handCards.Count}");
        GD.Print($"[VERIFY] Lane slots with visible art: {laneArtCount}/{_enemySlots.Count + _playerSlots.Count}");
        GD.Print($"[VERIFY] Largest hand card: ({maxW:F0}x{maxH:F0})");
        GD.Print($"[VERIFY] Smallest hand card: ({minW:F0}x{minH:F0})");

        // — TASK-H: Deck + Artifact group checks —
        GD.Print("[VERIFY] === Arsenal group checks (TASK-H) ===");
        if (_playerGroupRect == null || _enemyGroupRect == null)
        {
            GD.PrintErr("[VERIFY] FAIL: Arsenal group rects not created");
            fails++;
        }
        else
        {
            var playerRect = new Rect2(_playerGroupRect.GetScreenTransform().Origin, _playerGroupRect.Size);
            var enemyRect = new Rect2(_enemyGroupRect.GetScreenTransform().Origin, _enemyGroupRect.Size);

            // Groups are inside the viewport
            if (playerRect.Position.X < 0 || playerRect.Position.Y < 0 ||
                playerRect.End.X > viewportSize.X + 2 || playerRect.End.Y > viewportSize.Y + 2)
            {
                GD.PrintErr($"[VERIFY] FAIL: Player arsenal group {playerRect} exceeds viewport {viewportSize}");
                fails++;
            }
            else
            {
                GD.Print($"[VERIFY] OK: Player arsenal group {playerRect}");
            }

            if (enemyRect.Position.X < 0 || enemyRect.Position.Y < 0 ||
                enemyRect.End.X > viewportSize.X + 2 || enemyRect.End.Y > viewportSize.Y + 2)
            {
                GD.PrintErr($"[VERIFY] FAIL: Enemy arsenal group {enemyRect} exceeds viewport {viewportSize}");
                fails++;
            }
            else
            {
                GD.Print($"[VERIFY] OK: Enemy arsenal group {enemyRect}");
            }

            // TASK-UI3a: Player group is lower-left (below vertical center); enemy group is the top bar
            float midX = viewportSize.X * 0.5f;
            float midY = viewportSize.Y * 0.5f;
            bool playerLowerLeft = playerRect.Position.X < midX && playerRect.Position.Y > midY;
            if (!playerLowerLeft)
            {
                GD.PrintErr($"[VERIFY] FAIL: Player arsenal group at {playerRect.Position} is not lower-left (mid {midX},{midY})");
                fails++;
            }

            // Enemy top bar: at top of screen, full-width or right-side
            bool enemyAtTop = enemyRect.Position.Y < midY;
            if (!enemyAtTop)
            {
                GD.PrintErr($"[VERIFY] FAIL: Enemy top bar at {enemyRect.Position} is not in top half (midY {midY})");
                fails++;
            }
            else
            {
                GD.Print($"[VERIFY] OK: Enemy top bar at ({enemyRect.Position.X:F0},{enemyRect.Position.Y:F0}) size ({enemyRect.Size.X:F0}x{enemyRect.Size.Y:F0})");
            }
        }

        GD.Print($"[VERIFY] === {fails} check(s) failed ===");

        return fails;
    }

    /// <summary>
    /// TASK-F4B: Pre-place 3 creatures per side on the board for the capture screenshot.
    /// Places card instances directly in the lane state with a mix of art/no-art cards.
    /// </summary>
    private void PrePlaceCreatures()
    {
        var state = _gsm.State;
        if (state == null)
        {
            GD.PrintErr("[CAPTURE] Cannot pre-place creatures: game state is null");
            return;
        }

        GD.Print("[CAPTURE] Pre-placing 3 creatures per side on lanes 0-2");

        // Cards with art: emb_c_cinder_runner (cost 2), dwn_r_sealing_light (cost 4)
        // Card without art: vrd_x_heartwood_relic (cost 4)
        var placements = new (int PlayerIdx, int Lane, string CardId)[]
        {
            // Player (side 0): lane 0=HAS art, lane 1=NO art, lane 2=HAS art
            (0, 0, "emb_c_cinder_runner"),
            (0, 1, "vrd_x_heartwood_relic"),
            (0, 2, "dwn_r_sealing_light"),
            // Enemy (side 1): lane 0=HAS art, lane 1=NO art, lane 2=HAS art
            (1, 0, "emb_c_cinder_runner"),
            (1, 1, "vrd_x_heartwood_relic"),
            (1, 2, "dwn_r_sealing_light"),
        };

        int nextId = state.NextInstanceId;
        foreach (var (playerIdx, laneIdx, cardId) in placements)
        {
            var def = CardRegistry.Get(cardId);
            if (def == null)
            {
                GD.PrintErr($"[CAPTURE] Card def not found: {cardId}");
                continue;
            }

            var instance = new CardInstance(nextId++, cardId, playerIdx)
            {
                CardType = CardType.CREATURE,
                Cost = def.Cost,
                Strata = def.Strata,
                BaseAttack = def.Attack ?? 0,
                BaseVigor = def.Vigor ?? 0,
                Zone = Zone.Lane,
                LaneIndex = laneIdx,
            };
            instance.Keywords.AddRange(def.Keywords);
            instance.Abilities.AddRange(def.Abilities.Select(a => new AbilityDef
            {
                Trigger = a.Trigger, Condition = a.Condition, ActivationCost = a.ActivationCost,
                Effects = a.Effects.Select(e => new EffectDef
                {
                    Op = e.Op, Target = e.Target, Amount = e.Amount,
                    Attack = e.Attack, Vigor = e.Vigor, Keyword = e.Keyword,
                    TokenId = e.TokenId, Duration = e.Duration
                }).ToList()
            }));

            state.Players[playerIdx].Lanes[laneIdx].Occupant = instance;
        }
        state.NextInstanceId = nextId;

        GD.Print("[CAPTURE] Pre-placed 3 creatures per side on the board");
    }
}
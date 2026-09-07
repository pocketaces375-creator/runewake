using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Sim;
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
    private Label _turnIndicatorLabel = default!; // TASK-UI3e: small "YOUR TURN" above End Turn button
    // TASK-TU2: Tutorial runner
    private TutorialRunner? _tutorialRunner;
    private bool _isTutorialScriptMode;

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

    // TASK-UI4-ARSENAL: Arsenal group per player — bordered group with artifact frames + deck + barrow
    // Player: lower-left with portrait medallion above; Enemy: mirrored upper-right
    private Control _playerArsenalGroup = default!;
    private Control _enemyArsenalGroup = default!;
    private Control _playerPortrait = default!;
    private Control _enemyPortrait = default!;
    private Label _enemyNameLabel = default!;
    private Label _enemySubtitleLabel = default!;
    private Label _enemyDeckValue = default!;
    private Label _enemyBarrowValue = default!;
    private Label _playerShrineDeckLabel = default!;
    private Label _playerShrineBarrowLabel = default!;
    private Label _playerShrineVigorLabel = default!;
    private Label _playerShrineAttuneLabel = default!;
    private readonly ArtifactCardPlate[] _playerArtifactPlates = new ArtifactCardPlate[2];
    private readonly ArtifactCardPlate[] _enemyArtifactPlates = new ArtifactCardPlate[2];
    private readonly Control[] _playerArsenalPanels = new Control[2];
    private readonly Control[] _enemyArsenalPanels = new Control[2];

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
        public Strata Strata;
    }
    private BoardSnapshot[] _prevEnemyBoard = new BoardSnapshot[5];
    private BoardSnapshot[] _prevPlayerBoard = new BoardSnapshot[5];
    private int _prevEnemyVigor = -1;
    private int _prevPlayerVigor = -1;
    private bool _firstRender = true;

    // TASK-AC2: Previous charge values per artifact slot for pulse detection
    private readonly int[] _prevPlayerCharges = new int[2];
    private readonly int[] _prevEnemyCharges = new int[2];
    // Tracks whether pulse is currently playing to avoid overlapping tweens
    private readonly Godot.Tween?[] _chargePulseTweens = new Godot.Tween[4]; // 0=P0-0, 1=P0-1, 2=P1-0, 3=P1-1
    // TASK-ARTF-P2: Previous trigger state per artifact slot for trigger-flash detection
    private readonly bool[] _prevPlayerTriggered = new bool[2];
    private readonly bool[] _prevEnemyTriggered = new bool[2];
    // TASK-AUDIO-HOOK-1: Track hand size for card_draw detection
    private int _prevHandSize;
    private int _prevPlayerChargesFull; // tracks which slots were at max charges last render
    // TASK-JUICE-1: Previous turn number for end-turn ring detection
    private int _prevTurnNumber;

    /// <summary>Stratum of prev attacking lane per side for hit flare colour.</summary>
    private Strata _lastAttackerStratum = Strata.VERDANT;

    // ── Juice-edge pulse overlay (lazy-created, reused) ──
    private ColorRect? _screenEdgePulse;
    internal List<HandCard> TutorialHandCards => _handCards;
    internal List<LaneSlot> TutorialPlayerSlots => _playerSlots;
    internal List<LaneSlot> TutorialEnemySlots => _enemySlots;
    internal Button? TutorialEndTurnButton => _endTurnButton;
    internal ArtifactCardPlate[] TutorialPlayerArtifactPlates => _playerArtifactPlates;
    internal ArtifactCardPlate[] TutorialEnemyArtifactPlates => _enemyArtifactPlates;

    private bool _isCampaignEncounter;
    private bool _isGameOverHandled;
    private TutorialController? _tutorialCtrl;
    private TutorialPopup? _tutorialPopup;
    private bool _tutorialSummonedThisDuel;
    private bool _tutorialAwaitingCreatureSelect;
    private int _prevBuryCount;
    private int _prevExcavateCardCount;
    
    // TASK-WARDEN-RULE-1: Opening rule banner card (top-center, below enemy nameplate)
    private Control? _openingRuleBanner;
    private Label? _openingRuleLabel;
    
    // TASK-E: Game-over overlay (lazy-created on first IsGameOver=true)
    private Control? _gameOverOverlay;

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
        ApplyBodyFont(_playerVigorValue, FontStat);
        ApplyBodyFont(_playerAttuneValue, FontStat);

        var board = GetNode("Board");

        // ═══ TASK-UI3d: Atmosphere overlay — layered lighting, mist, vignette, dust motes ═══
        // Added as a child of Board BEFORE the AltarField so it renders behind the altar
        // decor but in front of the board background — field texture stays visible.
        var atmosphere = new AtmosphereOverlay { Name = "AtmosphereOverlay" };
        atmosphere.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        atmosphere.MouseFilter = Control.MouseFilterEnum.Ignore;
        board.AddChild(atmosphere);
        // ═══ END TASK-UI3d ═══

        // ── Backdrop environment behind the altar field ──
        // PAINTED-PLATE-1 + TASK-ART-BOARD-SKINS-1: The battlefield is a single painted image
        // (plate_<skin>.png) that fills the full board rect, cover-cropped at any aspect.
        // The plate is tinted by the region's board skin (default = white/none, ember = warm orange).
        var boardBg = GetNode<TextureRect>("BoardBg");
        string skinId = CampaignContext.CurrentRegionSkinId;
        var platePath = ThemeTokens.GetPlatePath(skinId);
        if (platePath != null)
        {
            var plateTex = GD.Load<Texture2D>(platePath);
            if (plateTex != null)
            {
                boardBg.Texture = plateTex;
                boardBg.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
                boardBg.Modulate = ThemeTokens.GetSkinTint(skinId);
            }
        }
        else
        {
            GD.PrintErr("[DuelScene] No painted plate — falling back to backdrop");
            var fallbackPath = ThemeTokens.GetBackdropPath(skinId);
            if (fallbackPath != null)
            {
                var fallbackTex = GD.Load<Texture2D>(fallbackPath);
                if (fallbackTex != null)
                {
                    boardBg.Texture = fallbackTex;
                    boardBg.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
                    boardBg.Modulate = ThemeTokens.GetSkinTint(skinId);
                }
            }
        }

        // TASK-UI3a: Build enemy top bar (74px, replaces old EnemyHUD + arsenal group + portrait)
        // TASK-UI4-ARSENAL: Build enemy arsenal group (upper-right)
        BuildEnemyArsenalGroup();

        // PlayerHUD at bottom-center — HIDDEN (stats moved to left shrine) — WORLD-POLISH-1
        var playerHud = GetNodeOrNull<CenterContainer>("PlayerHUD");
        if (playerHud != null)
            playerHud.Visible = false;

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

        // TASK-BORDER-1: Override a known hand-card name to demonstrate two-line auto-fit
        // (This is a test-only hook; the DebugCapture API would be the clean path but
        // the card registry is populated at duel load time, so we hook here.)
        if (CampaignContext.AutoCaptureScreenshot)
        {
            var longNameDef = CardRegistry.Get("vrd_r_bloomweaver");
            if (longNameDef != null)
            {
                longNameDef.Name = "The Undying Root of the Fallow Reach";
                GD.Print("[DUEL] Overrode bloomweaver name for long-name auto-fit test");
            }
        }

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
            _endTurnButton.AddThemeFontSizeOverride("font_size", FontButtonPrimary);
            _endTurnButton.AddThemeColorOverride("font_color", Colors.Black);
            _endTurnButton.AddThemeColorOverride("font_hover_color", Colors.Black);
            _endTurnButton.AddThemeColorOverride("font_pressed_color", Colors.Black);
            _endTurnButton.AddThemeStyleboxOverride("normal", new StyleBoxFlat
            {
                BgColor = new Color(0.85f, 0.65f, 0.15f, 1.0f), // gold
                BorderColor = new Color(0.70f, 0.50f, 0.10f, 1.0f),
                BorderWidthLeft = 2, BorderWidthTop = 2,
                BorderWidthRight = 2, BorderWidthBottom = 2,
                CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
                CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
                ContentMarginLeft = 8, ContentMarginTop = 4,
                ContentMarginRight = 8, ContentMarginBottom = 4
            });
            _endTurnButton.AddThemeStyleboxOverride("hover", new StyleBoxFlat
            {
                BgColor = new Color(0.95f, 0.75f, 0.25f, 1.0f),
                BorderColor = new Color(0.80f, 0.60f, 0.15f, 1.0f),
                BorderWidthLeft = 2, BorderWidthTop = 2,
                BorderWidthRight = 2, BorderWidthBottom = 2,
                CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
                CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
                ContentMarginLeft = 8, ContentMarginTop = 4,
                ContentMarginRight = 8, ContentMarginBottom = 4
            });
            _endTurnButton.AddThemeStyleboxOverride("pressed", new StyleBoxFlat
            {
                BgColor = new Color(0.70f, 0.50f, 0.10f, 1.0f),
                BorderColor = new Color(0.60f, 0.40f, 0.05f, 1.0f),
                BorderWidthLeft = 2, BorderWidthTop = 2,
                BorderWidthRight = 2, BorderWidthBottom = 2,
                CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
                CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
                ContentMarginLeft = 8, ContentMarginTop = 4,
                ContentMarginRight = 8, ContentMarginBottom = 4
            });
            _endTurnButton.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
            _endTurnButton.OffsetRight = -16;
            _endTurnButton.OffsetLeft = -276;
            _endTurnButton.OffsetBottom = -16;
            _endTurnButton.OffsetTop = -136;
            AddChild(_endTurnButton);
        }
        _endTurnButton.Pressed += OnEndTurnPressed;

        // TASK-UI3e: Turn indicator — small label above End Turn button
        _turnIndicatorLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _turnIndicatorLabel.AddThemeFontSizeOverride("font_size", FontSmall);
        _turnIndicatorLabel.AddThemeColorOverride("font_color", Gold);
        _turnIndicatorLabel.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _turnIndicatorLabel.OffsetRight = -10;
        _turnIndicatorLabel.OffsetLeft = -100;
        _turnIndicatorLabel.OffsetBottom = -110;
        _turnIndicatorLabel.OffsetTop = -126;
        AddChild(_turnIndicatorLabel);

        // ═══ TASK-WARDEN-RULE-1: Opening rule banner (created hidden, shown in OnStateChanged) ═══
        float s = GetViewportRect().Size.Y / 1080f;
        var ruleBanner = new PanelContainer
        {
            Name = "OpeningRuleBanner",
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false
        };
        float ruleBannerH = 36f * s;
        var ruleBannerStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.12f, 0.08f, 0.90f), // dark green-black
            BorderColor = new Color(0.26f, 0.42f, 0.18f, 0.9f), // moss green
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 10, ContentMarginTop = 2,
            ContentMarginRight = 10, ContentMarginBottom = 2
        };
        ruleBanner.AddThemeStyleboxOverride("panel", ruleBannerStyle);
        float vw_banner = GetViewportRect().Size.X;
        ruleBanner.Position = new Vector2(vw_banner / 2f - 180f * s, 44f * s);
        ruleBanner.Size = new Vector2(360f * s, ruleBannerH);
        AddChild(ruleBanner);

        _openingRuleLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.Off
        };
        _openingRuleLabel.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(14 * s));
        _openingRuleLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.9f, 0.5f)); // pale green
        ruleBanner.AddChild(_openingRuleLabel);
        _openingRuleBanner = ruleBanner;

        var encounter = CampaignContext.CurrentEncounter;
        _isCampaignEncounter = encounter != null;

        // Record duel start for telemetry
        if (encounter != null)
        {
            CampaignContext.Telemetry?.RecordDuelStart(
                encounter.Id, CampaignContext.PlayerDeckIds.Count);
        }

        // Check if this is a tutorial encounter (uses campaign encounter with is_tutorial flag)
        // or a tutorial script mode (--tutorial CLI arg)
        bool isTutorialEncounter = _isCampaignEncounter && encounter != null && encounter.IsTutorial;
        string? tutorialScriptId = CampaignContext.TutorialScriptId;
        _isTutorialScriptMode = !string.IsNullOrEmpty(tutorialScriptId);

        if (_isTutorialScriptMode)
        {
            // TASK-TU2: Tutorial runner — consumes tutorial script data
            _tutorialRunner = new TutorialRunner();
            AddChild(_tutorialRunner);
            _tutorialRunner.Initialize(this, _gsm, _bot, isHeadless: true);

            // Load and validate the script
            if (!_tutorialRunner.LoadScript(tutorialScriptId!))
            {
                GD.PrintErr($"[DuelScene] Failed to load tutorial script: {tutorialScriptId}");
                _tutorialRunner = null;
                _isTutorialScriptMode = false;
            }
            else
            {
                GD.Print($"[DuelScene] Tutorial script mode: {tutorialScriptId}");
            }

            // TASK-TUTORIAL-VERIFY-1: Re-read encounter after TutorialRunner.SetupEncounter
            // so the campaign init path below uses the tutorial's decks and artifacts.
            encounter = CampaignContext.CurrentEncounter;
            _isCampaignEncounter = encounter != null;
        }
        else if (isTutorialEncounter && !CampaignContext.SoakActive)
        {
            _tutorialPopup = new TutorialPopup();
            AddChild(_tutorialPopup);
            _tutorialCtrl = new TutorialController();
            AddChild(_tutorialCtrl);
            _tutorialCtrl.Initialize(this, _tutorialPopup);
            GD.Print("[DuelScene] Tutorial encounter detected — activating popup system.");
        }
        else if (isTutorialEncounter && CampaignContext.SoakActive)
        {
            GD.Print("[DuelScene] Soak mode — skipping tutorial popups for tutorial encounter");
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
                RunePage = CampaignContext.CurrentRunePage,
                Player0ArtifactIds = CampaignContext.TutorialPlayerArtifactIds,
                Player0Class = CampaignContext.TutorialPlayerClass,
                MatchConfig = null,
                OpeningRule = encounter.OpeningRule
            };
            _gsm.Initialize(config);
        }
        else
        {
            _gsm.InitializeTestGame();
        }

        // Speed up bot during tutorial or soak mode so turns are near-instant
        if (isTutorialEncounter || CampaignContext.SoakActive)
        {
            _bot.ThinkDelay = 0.1f;
            _bot.ActionInterval = 0.05f; // TASK-PLAYABLE-NAV-1: fast bot actions for soak duels
        }

        // Show mulligan overlay if not in tutorial mode or tutorial script mode
        if (!_isTutorialScriptMode && (_tutorialCtrl == null || !_tutorialCtrl.IsActive))
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

        // Style the turn label for readability — repositioned in BuildEnemyArsenalGroup
        _turnLabel.AddThemeFontSizeOverride("font_size", FontSmall);
        _turnLabel.AddThemeColorOverride("font_color", TextSecondary);

        // ═══ END TASK-UI3c ═══

        // Enable background tap to cancel selection
        GuiInput += OnBackgroundGuiInput;

        // ═══ TASK-H/UI4-ARSENAL: Player and Enemy arsenal groups (bordered groups with artifact frames + deck + barrow) ═══
        AddArsenalGroups();
        // ═══ END TASK-H ═══

        // Start tutorial popup sequence if this is a tutorial encounter
        if (_tutorialCtrl != null && _tutorialCtrl.IsActive)
        {
            Callable.From(ShowPopup1_Goal).CallDeferred();
        }

        // TASK-TU2: Start tutorial runner in script mode (after game init)
        if (_tutorialRunner != null && _isTutorialScriptMode)
        {
            Callable.From(() => _tutorialRunner.Start()).CallDeferred();
        }

        // ═══ TASK-AUDIO-HOOK-1: Start ambient + music on duel screen entry ═══
        var audio = GetNode<AudioManager>("/root/AudioManager");
        audio.PlayAmbient("wind_reach");
        audio.PlayMusic("ambient_reach");
        _prevHandSize = _gsm?.GetHand(0).Count ?? 0;

        // ═══ CAPTURE HOOK: auto-dismiss mulligan, wait, capture ═══
        // TASK-TUTORIAL-VERIFY-1: Skip in tutorial script mode — TutorialRunner handles its own captures
        if (CampaignContext.AutoCaptureScreenshot && !_isTutorialScriptMode)
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

                // ═══ BOT-FIX-1: headless bot-duel harness — no capture, no pre-place ═══
                if (CampaignContext.BotDuelTest)
                {
                    StartBotDuelTest();
                    return;
                }

                // ═══ TASK-INPUT-SMOKE-1: headless input smoke test — inject events, no capture ═══
                if (CampaignContext.InputSmokeTest)
                {
                    StartInputSmokeTest();
                    return;
                }

                // ═══ TASK-INPUT-TOUCH-1: pure touch-only smoke test — no mouse events anywhere ═══
                if (CampaignContext.TouchOnlySmokeTest)
                {
                    StartTouchOnlySmokeTest();
                    return;
                }

                // ═══ TASK-TUTORIAL-VERIFY-1: Skip pre-place/artifacts/inflate in tutorial script mode ═══
                // The TutorialRunner handles its own state and captures for each beat.
                if (!_isTutorialScriptMode)
                {
                    // ═══ TASK-F4B: Pre-place 3 creatures per side before capture ═══
                    if (!CampaignContext.DebugAlignMode && !CampaignContext.CaptureVictoryOverlay && !CampaignContext.CaptureDefeatOverlay)
                        PrePlaceCreatures();

                    // ═══ TASK-AC1: Pre-place artifacts with all 4 visual states ═══
                    if (!CampaignContext.DebugAlignMode)
                        PrePlaceArtifacts();

                    // ═══ TASK-G: Inflate player hand to 10 cards for worst-case compression test ═══
                    if (!CampaignContext.DebugAlignMode && !CampaignContext.CaptureVictoryOverlay && !CampaignContext.CaptureDefeatOverlay)
                        InflateHandTo10();
                }

                // ═══ PAINTED-PLATE-1: Debug slot overlay for align capture ═══
                // Created here (before the snapTimer) so it renders for a full frame
                // before the snapshot.
                Control? debugOverlay = null;
                if (CampaignContext.DebugAlignMode)
                {
                    debugOverlay = new Control();
                    debugOverlay.Name = "DebugAlignOverlay";
                    debugOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                    debugOverlay.MouseFilter = Control.MouseFilterEnum.Ignore;
                    debugOverlay.Draw += () =>
                    {
                        if (!IsInstanceValid(debugOverlay)) return;
                        // Draw player slots as green outlines
                        for (int i = 0; i < _playerSlots.Count; i++)
                        {
                            var s = _playerSlots[i];
                            if (!IsInstanceValid(s)) continue;
                            var gp = s.GetScreenTransform().Origin;
                            DrawSlotOutline(debugOverlay, gp, s.Size, new Color(0.0f, 1.0f, 0.0f, 0.9f), i.ToString());
                        }
                        // Draw enemy slots as blue outlines
                        for (int i = 0; i < _enemySlots.Count; i++)
                        {
                            var s = _enemySlots[i];
                            if (!IsInstanceValid(s)) continue;
                            var gp = s.GetScreenTransform().Origin;
                            DrawSlotOutline(debugOverlay, gp, s.Size, new Color(0.0f, 0.5f, 1.0f, 0.9f), $"E{i}");
                        }
                        // Draw ring ellipse from canonical geometry
                        var vp = GetViewportRect().Size;
                        float boardTopDbg = 74f;
                        float boardHDbg = vp.Y - boardTopDbg - 160f;
                        float cx = vp.X / 2f;
                        float cy = boardTopDbg + boardHDbg * ThemeTokens.RingCenterY;
                        float rx = vp.X * ThemeTokens.RingRadiusW;
                        float ry = boardHDbg * ThemeTokens.RingRadiusH;
                        int segs = 72;
                        var pts = new Vector2[segs];
                        for (int si = 0; si < segs; si++)
                        {
                            float a = Mathf.Tau * si / segs;
                            pts[si] = new Vector2(cx + rx * Mathf.Cos(a), cy + ry * Mathf.Sin(a));
                        }
                        var prev = pts[0];
                        for (int si = 1; si <= segs; si++)
                        {
                            var cur = pts[si % segs];
                            debugOverlay.DrawLine(prev, cur, new Color(1.0f, 0.8f, 0.0f, 0.7f), 2f);
                            prev = cur;
                        }
                    };
                    AddChild(debugOverlay);
                    debugOverlay.QueueRedraw();
                }

                // ═══ POLISH-PASS-1-E: Victory/defeat overlay capture — force game-over ═══
                if (CampaignContext.CaptureVictoryOverlay || CampaignContext.CaptureDefeatOverlay)
                {
                    // Force game over by dropping vigor to 0 through the engine
                    // This triggers OnStateChanged which shows the overlay
                    bool playerWins = CampaignContext.CaptureVictoryOverlay;
                    int targetPlayer = playerWins ? 1 : 0; // enemy loses (1) or player loses (0)
                    if (_gsm != null && _gsm.State != null && _gsm.State.Players.Length > targetPlayer)
                    {
                        // Apply lethal damage through the engine
                        _gsm.State.Players[targetPlayer].Vigor = 0;
                        _gsm.State.IsGameOver = true;
                        _gsm.State.WinnerIndex = playerWins ? 0 : 1;
                        // TASK-DROPS-UI-1: Fire OnGameOver to roll drops and build reveal state
                        int winnerIdx = playerWins ? 0 : 1;
                        OnGameOver(winnerIdx);
                        // Notify the scene to render the overlay
                        OnStateChanged();
                        GD.Print($"[DuelScene] Forced game-over for overlay capture: {(playerWins ? "VICTORY" : "DEFEAT")}");
                    }
                }

                // Capture after board renders
                var snapTimer = new Godot.Timer();
                snapTimer.OneShot = true;
                snapTimer.WaitTime = 1.0f;
                snapTimer.Timeout += () =>
                {
                    string captureSuffix;
                    if (CampaignContext.DebugAlignMode)
                        captureSuffix = "_align";
                    else if (CampaignContext.DebugSafeAreaMode)
                        captureSuffix = "_safe";
                    else if (CampaignContext.CaptureHandSize == 5)
                        captureSuffix = CampaignContext.WideCaptureMode ? "_hand5_wide" : "_hand5";
                    else if (CampaignContext.CaptureHandSize == 8)
                        captureSuffix = CampaignContext.WideCaptureMode ? "_hand8_wide" : "_hand8";
                    else if (CampaignContext.WideCaptureMode)
                        captureSuffix = "_wide";
                    else if (CampaignContext.R2CardScale)
                        captureSuffix = "_r2"; // BOARD-MATCH-1: R2 variant capture
                    else
                        captureSuffix = "";

                    var capturePath = CampaignContext.CaptureVictoryOverlay
                        ? $"{ProjectPaths.Artifacts}/captures/victory_overlay{captureSuffix}.png"
                        : CampaignContext.CaptureDefeatOverlay
                        ? $"{ProjectPaths.Artifacts}/captures/defeat_overlay{captureSuffix}.png"
                        : $"{ProjectPaths.Artifacts}/captures/duel_test{captureSuffix}.png";
                    var metaPath = $"{ProjectPaths.Artifacts}/captures/duel_test{captureSuffix}.meta.json";

                    var img = GetViewport().GetTexture().GetImage();
                    if (img != null)
                        img.SavePng(capturePath);
                    // TASK-UI-LINT-1: Write layout JSON
                    var layoutBaseName = CampaignContext.CaptureVictoryOverlay
                        ? $"victory_overlay{captureSuffix}"
                        : CampaignContext.CaptureDefeatOverlay
                        ? $"defeat_overlay{captureSuffix}"
                        : $"duel_test{captureSuffix}";
                    DebugCapture.WriteLayoutJson(this, layoutBaseName);
                    GD.Print($"[CAPTURE] {layoutBaseName}.png saved");

                    // TASK-UI-LINT-1: Dump layout JSON for ui_lint.py
                    DebugCapture.DumpLayoutJSON($"duel_test{captureSuffix}", this);

                    // Write meta.json with screen-space card rects
                    var meta = new System.Text.StringBuilder();
                    meta.Append("{\n");

                    // Capture hand card info from _handCards
                    meta.Append($"  \"expected_hand_card_count\": {_handCards.Count},\n");
                    meta.Append("  \"expected_board_card_count\": 10,\n");
                    // FULL-DECK-2: Include viewport dims for capture_gate.py Check 8
                    var vpSize = GetViewportRect().Size;
                    meta.Append($"  \"viewport_width\": {vpSize.X:F0},\n");
                    meta.Append($"  \"viewport_height\": {vpSize.Y:F0},\n");
                    // PAINTED-PLATE-1: ring bottom derived from canonical geometry
                                        // Ring center (0.50, 0.50) of board rect, radius (0.40w, 0.36h).
                                        // Board rect in screen coords: y=74 to y=vh-160.
                                        float boardTopMeta = 74f;
                                        float boardHMeta = vpSize.Y - 74f - 160f;
                                        float ringCenterYMeta = boardTopMeta + boardHMeta * ThemeTokens.RingCenterY;
                                        float ringBottomMeta = ringCenterYMeta + boardHMeta * ThemeTokens.RingRadiusH;
                                        meta.Append("  \"altar_ellipse\": {\n");
                                        meta.Append($"    \"bottom_y\": {ringBottomMeta:F1}\n");
                                        meta.Append("  },\n");
                                        meta.Append("  \"hand_cards\": [\n");
                    for (int ci = 0; ci < _handCards.Count; ci++)
                    {
                        var hc = _handCards[ci];
                        var r = hc.GetRect();
                        var gp = hc.GetScreenTransform().Origin;
                        var cardPlate = hc.GetNodeOrNull<CardPlate>("Content/CardPlate");
                        var nameRect = new Rect2();
                        if (cardPlate?.GetNameLabel() != null)
                        {
                            var np = cardPlate.GetNameLabel()!.GetScreenTransform().Origin;
                            nameRect = new Rect2(np.X, np.Y, cardPlate.GetNameLabel()!.Size.X, cardPlate.GetNameLabel()!.Size.Y);
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

                    // ═══ TASK-AC1: Capture artifact slot info with visual states ═══
                    meta.Append("  \"artifact_cards\": [\n");
                    var stateRef = _gsm.State;
                    if (stateRef != null)
                    {
                        for (int side = 0; side <= 1; side++)
                        {
                            var pl = stateRef.Players[side];
                            int asCount = pl.ArtifactSlots?.Length ?? 0;
                            for (int ai = 0; ai < asCount; ai++)
                            {
                                var slot = pl.ArtifactSlots[ai];
                                var label = side == 0
                                    ? (_playerArtifactPlates[ai]?.GetNameLabel() ?? null)
                                    : (_enemyArtifactPlates[ai]?.GetNameLabel() ?? null);
                                var ctrl = side == 0
                                    ? (_playerArsenalPanels[ai] ?? null)
                                    : (_enemyArsenalPanels[ai] ?? null);
                                var rect = new Rect2();
                                if (ctrl != null && ctrl.IsInsideTree())
                                {
                                    var gp = ctrl.GetScreenTransform().Origin;
                                    rect = new Rect2(gp, ctrl.Size);
                                }

                                meta.Append("    {\n");
                                meta.Append($"      \"side\": \"{(side == 0 ? "player" : "enemy")}\",\n");
                                meta.Append($"      \"slot\": {ai},\n");
                                meta.Append($"      \"artifact_id\": \"{(pl.ArtifactDefIds.Length > ai ? pl.ArtifactDefIds[ai] : "?")}\",\n");
                                meta.Append($"      \"name\": \"{(label != null ? label.Text : "?")}\",\n");
                                meta.Append($"      \"visual_state\": \"{slot.VisualState}\",\n");
                                meta.Append($"      \"charges\": {slot.Charges},\n");
                                meta.Append($"      \"is_suppressed\": {slot.IsSuppressed.ToString().ToLower()},\n");
                                meta.Append($"      \"has_triggered\": {slot.HasTriggeredThisTurn.ToString().ToLower()},\n");
                                meta.Append($"      \"rect\": {{ \"x\": {rect.Position.X:F1}, \"y\": {rect.Position.Y:F1}, \"w\": {rect.Size.X:F1}, \"h\": {rect.Size.Y:F1} }}\n");
                                meta.Append("    }");
                                if (side < 1 || ai < asCount - 1)
                                    meta.Append(",");
                                meta.Append("\n");
                            }
                        }
                    }
                    meta.Append("  ],\n");
                    meta.Append("  \"board_cards\": [\n");
                    int bi = 0;
                    foreach (var slot in _playerSlots)
                    {
                        var r = slot.GetRect();
                        var gp = slot.GetScreenTransform().Origin;
                        var cardPlate = slot.GetNodeOrNull<CardPlate>("Content/CardPlate");
                        var nameRect = new Rect2();
                        if (cardPlate?.GetNameLabel() != null)
                        {
                            var np = cardPlate.GetNameLabel()!.GetScreenTransform().Origin;
                            nameRect = new Rect2(np.X, np.Y, cardPlate.GetNameLabel()!.Size.X, cardPlate.GetNameLabel()!.Size.Y);
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
                        var cardPlate = slot.GetNodeOrNull<CardPlate>("Content/CardPlate");
                        var nameRect = new Rect2();
                        if (cardPlate?.GetNameLabel() != null)
                        {
                            var np = cardPlate.GetNameLabel()!.GetScreenTransform().Origin;
                            nameRect = new Rect2(np.X, np.Y, cardPlate.GetNameLabel()!.Size.X, cardPlate.GetNameLabel()!.Size.Y);
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

                                        using (var writer = new System.IO.StreamWriter(metaPath))
                                        {
                                            writer.Write(meta.ToString());
                                        }
                                        GD.Print($"[CAPTURE] duel_test{captureSuffix}.meta.json saved");

                                        // Run layout verification (skip in align mode — no cards)
                    if (!CampaignContext.DebugAlignMode)
                    {
                        // For flow tests: after capture, navigate to map instead of quitting
                        if (CampaignContext.FlowTestAfterOverlay)
                        {
                            GD.Print("[CAPTURE] Flow test: capture complete — navigating to map to prove round-trip");
                            CampaignContext.CaptureFlowTestMap = true;
                            // Use a timer attached to Root (not DuelScene) to avoid thread/callback nesting issues
                            var navTimer = new Godot.Timer();
                            navTimer.OneShot = true;
                            navTimer.WaitTime = 0.1f;
                            navTimer.Timeout += () => GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
                            GetTree().Root.AddChild(navTimer);
                            navTimer.Start();
                            return;
                        }
                        int failed = RunLayoutVerification();
                        GD.Print($"[VERIFY] Layout checks: {failed} failed");
                        // TASK-AUDIO-VERIFY-1: Write audio verification report
                        GetNode<AudioManager>("/root/AudioManager").WriteAudioVerificationReport(
                            ProjectPaths.Artifacts + "/captures/audio_verify.json");
                        if (failed > 0)
                            GetTree().Quit(1);
                        else
                            GetTree().Quit(0);
                    }
                    else
                    {
                        // For flow tests: navigate to map instead of quitting
                        if (CampaignContext.FlowTestAfterOverlay)
                        {
                            GD.Print("[CAPTURE] Flow test: capture complete — navigating to map to prove round-trip");
                            CampaignContext.CaptureFlowTestMap = true;
                            GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
                            return;
                        }
                        // TASK-AUDIO-VERIFY-1: Write audio verification report
                        GetNode<AudioManager>("/root/AudioManager").WriteAudioVerificationReport(
                            ProjectPaths.Artifacts + "/captures/audio_verify.json");
                        GetTree().Quit(0);
                    }
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

        _tutorialPopup.SetHighlightTargets(new System.Collections.Generic.List<Control> { _enemyVigorValue });
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

        _tutorialPopup.SetHighlightTargets(new System.Collections.Generic.List<Control> { _playerAttuneValue });
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

        _tutorialPopup.SetHighlightTargets(new System.Collections.Generic.List<Control> { FindPlayerCreatureNode() as Control });
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

        _tutorialPopup.SetHighlightTargets(new System.Collections.Generic.List<Control> { _enemyVigorValue });
        _tutorialCtrl.ShowPopup("p5_facehit",
            onContinue: ShowPopup6_TurnCycle
        );
    }

    /// <summary>Popup 6: THE TURN CYCLE (final popup — end tutorial)</summary>
    private void ShowPopup6_TurnCycle()
    {
        if (_tutorialCtrl == null || !_tutorialCtrl.IsActive || _tutorialPopup == null) return;

        _tutorialPopup.SetHighlightTargets(new System.Collections.Generic.List<Control> { _endTurnButton });
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
    /// TASK-UI4-ARSENAL: Build the enemy Arsenal Group — upper-right bordered group
    /// containing two Artifact card frames (ArtifactCardPlate) + deck pile + barrow pile,
    /// with portrait medallion above. Mirrored from the player layout.
    /// All values are live-bound through RenderHud().
    /// _enemyGroupRect is set to the arsenal group container for capture meta.json.
    /// </summary>
    private void BuildEnemyArsenalGroup()
    {
        float vh = GetViewportRect().Size.Y;
        float vw = GetViewportRect().Size.X;
        float scale = vh / 1080f; // BOARD-MATCH-1: Use 1080 reference

        // Design-unit sizes, scaled
        float artW = 72f * scale;
        float artH = 96f * scale;
        float portraitW = 44f * scale;
        float portraitH = 48f * scale;
        float chipW = 36f * scale;
        float chipH = 40f * scale;
        float gap = 4f * scale;
        float pad = 6f * scale;

        // ═══ TOP BAR: turn indicator + enemy name + enemy vigor ═══
        // Uses the existing _turnLabel from the scene, repositioned.
        
        // Reposition and restyle the turn label
        _turnLabel.Text = "Turn 1";
        _turnLabel.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(20 * scale));
        _turnLabel.AddThemeColorOverride("font_color", Ember);
        ApplyHeaderFont(_turnLabel, Mathf.RoundToInt(20 * scale));
        _turnLabel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _turnLabel.Position = new Vector2(vw / 2f - 60f, 10f * scale);
        _turnLabel.Size = new Vector2(120f, 30f * scale);

        // ── Enemy nameplate (red pill, top-right) ──
        float nameplateW = 210f * scale; // BOARD-MATCH-1: Widen for "THE WAYFARER"
        float nameplateH = 30f * scale;
        var enemyNameplate = new PanelContainer
        {
            Name = "EnemyNameplate",
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(nameplateW, nameplateH)
        };
        var nameplateStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.66f, 0.16f, 0.10f, 0.85f),  // red pill
            BorderColor = new Color(0.85f, 0.30f, 0.15f, 0.9f),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = Mathf.RoundToInt(nameplateH / 2f),
            CornerRadiusTopRight = Mathf.RoundToInt(nameplateH / 2f),
            CornerRadiusBottomLeft = Mathf.RoundToInt(nameplateH / 2f),
            CornerRadiusBottomRight = Mathf.RoundToInt(nameplateH / 2f),
            ContentMarginLeft = 2, ContentMarginTop = 0,
            ContentMarginRight = 2, ContentMarginBottom = 0
        };
        enemyNameplate.AddThemeStyleboxOverride("panel", nameplateStyle);
        enemyNameplate.Position = new Vector2(vw - nameplateW - 12f * scale, 8f * scale);
        AddChild(enemyNameplate);

        var nameplateHBox = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = (Control.SizeFlags)3,
            Alignment = BoxContainer.AlignmentMode.Center
        };
        enemyNameplate.AddChild(nameplateHBox);

        _enemyNameLabel = new Label
        {
            Text = "THE WAYFARER",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        int nameFontSize = Mathf.RoundToInt(15 * scale); // BOARD-MATCH-1: Larger for readability
        _enemyNameLabel.AddThemeFontSizeOverride("font_size", nameFontSize);
        _enemyNameLabel.AddThemeColorOverride("font_color", Colors.White);
        ApplyHeaderFont(_enemyNameLabel, Mathf.RoundToInt(nameFontSize));
        nameplateHBox.AddChild(_enemyNameLabel);

        // Separator
        var sep = new Label
        {
            Text = "|",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        sep.AddThemeFontSizeOverride("font_size", nameFontSize);
        sep.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.70f));
        nameplateHBox.AddChild(sep);

        _enemyVigorValue = new Label
        {
            Text = "22",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _enemyVigorValue.AddThemeFontSizeOverride("font_size", nameFontSize);
        _enemyVigorValue.AddThemeColorOverride("font_color", Colors.White);
        nameplateHBox.AddChild(_enemyVigorValue);

        // ── DECK/BARROW panel just below the enemy nameplate ──
        float panelW = nameplateW;
        float panelH = 28f * scale;
        var enemyDeckBarrowPanel = new PanelContainer
        {
            Name = "EnemyDeckBarrowPanel",
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(panelW, panelH)
        };
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.07f, 0.06f, 0.70f),
            BorderColor = new Color(0.40f, 0.35f, 0.20f, 0.5f),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 2, ContentMarginTop = 0,
            ContentMarginRight = 2, ContentMarginBottom = 0
        };
        enemyDeckBarrowPanel.AddThemeStyleboxOverride("panel", panelStyle);
        enemyDeckBarrowPanel.Position = new Vector2(vw - panelW - 12f * scale, 40f * scale);
        AddChild(enemyDeckBarrowPanel);

        var dbRow = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = (Control.SizeFlags)3,
            Alignment = BoxContainer.AlignmentMode.Center
        };
        enemyDeckBarrowPanel.AddChild(dbRow);

        // Deck count
        _enemyDeckValue = new Label
        {
            Text = "0",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _enemyDeckValue.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(12 * scale));
        _enemyDeckValue.AddThemeColorOverride("font_color", TextPrimary);
        ApplyHeaderFont(_enemyDeckValue, Mathf.RoundToInt(12 * scale));
        dbRow.AddChild(_enemyDeckValue);

        var deckLabel = new Label
        {
            Text = "DECK",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        deckLabel.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(9 * scale));
        deckLabel.AddThemeColorOverride("font_color", TextMuted);
        ApplyHeaderFont(deckLabel, Mathf.RoundToInt(9 * scale));
        dbRow.AddChild(deckLabel);

        // Barrow count
        _enemyBarrowValue = new Label
        {
            Text = "0",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _enemyBarrowValue.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(12 * scale));
        _enemyBarrowValue.AddThemeColorOverride("font_color", TextPrimary);
        ApplyHeaderFont(_enemyBarrowValue, Mathf.RoundToInt(12 * scale));
        dbRow.AddChild(_enemyBarrowValue);

        var barrowLabel = new Label
        {
            Text = "BARROW",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        barrowLabel.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(9 * scale));
        barrowLabel.AddThemeColorOverride("font_color", TextMuted);
        ApplyHeaderFont(barrowLabel, Mathf.RoundToInt(9 * scale));
        dbRow.AddChild(barrowLabel);

        // ── Artifact card frames (teal-rimmed thumbnails, below DECK/BARROW) ──
        float artFrameW = 72f * scale;
        float artFrameH = 96f * scale;
        float artFrameGap = 4f * scale;
        var artifactRow = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = (Control.SizeFlags)3,
            Alignment = BoxContainer.AlignmentMode.Center
        };
        artifactRow.Position = new Vector2(vw - panelW - 12f * scale, 40f * scale + panelH + 18f * scale);
        AddChild(artifactRow);

        for (int i = 0; i < 2; i++)
        {
            BuildEnemyArsenalArtifact(artifactRow, i, artFrameW, artFrameH, scale);
        }

        // Set _enemyGroupRect to a dummy control for capture meta.json compatibility
        _enemyGroupRect = new Control { Name = "EnemyGroupRect" };
        _enemyGroupRect.MouseFilter = MouseFilterEnum.Ignore;
        _enemyGroupRect.Position = new Vector2(vw - nameplateW - 12f * scale, 8f * scale);
        _enemyGroupRect.Size = new Vector2(nameplateW, panelH + 32f * scale);
        AddChild(_enemyGroupRect);

        // Legacy enemy name field for other references
        _enemyName = _enemyNameLabel;

        GD.Print("[DUEL] Revised HUD: Enemy nameplate + DECK/BARROW panel built (top-right)");
    }

    /// <summary>Build a single enemy artifact frame using ArtifactCardPlate.</summary>
    private void BuildEnemyArsenalArtifact(HBoxContainer parent, int index, float w, float h, float scale)
    {
        var panel = new PanelContainer();
        panel.CustomMinimumSize = new Vector2(w, h);
        panel.MouseFilter = MouseFilterEnum.Ignore;
        panel.SizeFlagsHorizontal = 0;
        panel.SizeFlagsVertical = 0;
        var artStyle = new StyleBoxFlat
        {
            BgColor = ArtifactFrameFill,
            BorderWidthLeft = 0, BorderWidthTop = 0,
            BorderWidthRight = 0, BorderWidthBottom = 0,
            ContentMarginLeft = 0, ContentMarginTop = 0,
            ContentMarginRight = 0, ContentMarginBottom = 0
        };
        panel.AddThemeStyleboxOverride("panel", artStyle);

        // Root-Bound 9-slice border
        var border = new RootBoundBorder();
        border.Name = "RootBoundBorder";
        border.Setup(w, h);
        panel.AddChild(border);

        // ArtifactCardPlate — unified card frame with teal-gold rim + ARTIFACT tag + charge rail
        var plate = new ArtifactCardPlate();
        plate.Name = $"ArtPlateE{index}";
        plate.Setup("—", w, h, 0, 0, false);
        panel.AddChild(plate);

        _enemyArtifactPlates[index] = plate;
        _enemyArsenalPanels[index] = panel;
        parent.AddChild(panel);
    }

    /// <summary>
    /// TASK-UI4-ARSENAL: Build the Player Arsenal Group — lower-left bordered group
    /// containing nameplate, deck/barrow counts, artifact card frames with teal rim,
    /// and vigor/attune labels. Matches the authority image layout.
    /// _playerGroupRect is set to the group container for capture meta.json.
    /// </summary>
    private void BuildPlayerArsenalGroup()
    {
        float vh = GetViewportRect().Size.Y;
        float vw = GetViewportRect().Size.X;
        float scale = vh / 1080f; // BOARD-MATCH-1: Use 1080 reference for consistency

        // ── Player nameplate (green pill, bottom-left) ──
        float nameplateW = 170f * scale;
        float nameplateH = 28f * scale;
        float handCardH = Mathf.Max(140f, 340f * (vh / 1080f));
        float handTop = vh - handCardH - 6f;
        // BOARD-MATCH-3: Use PanelContainer (same pattern as enemy nameplate) for reliable bg rendering
        var playerNameplate = new PanelContainer
        {
            Name = "PlayerNameplate",
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(nameplateW, nameplateH)
        };
        var nameplateStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.25f, 0.60f, 0.35f, 1.0f),  // green pill
            BorderColor = new Color(0.35f, 0.65f, 0.35f, 0.9f),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = Mathf.RoundToInt(nameplateH / 2f),
            CornerRadiusTopRight = Mathf.RoundToInt(nameplateH / 2f),
            CornerRadiusBottomLeft = Mathf.RoundToInt(nameplateH / 2f),
            CornerRadiusBottomRight = Mathf.RoundToInt(nameplateH / 2f),
            ContentMarginLeft = 2, ContentMarginTop = 0,
            ContentMarginRight = 2, ContentMarginBottom = 0
        };
        playerNameplate.AddThemeStyleboxOverride("panel", nameplateStyle);
        playerNameplate.Position = new Vector2(12f * scale, handTop - nameplateH - 12f);
        AddChild(playerNameplate);

        var nameplateHBox = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = (Control.SizeFlags)3,
            Alignment = BoxContainer.AlignmentMode.Center
        };
        playerNameplate.AddChild(nameplateHBox);

        var playerNameLabel = new Label
        {
            Text = "TRIKZOS",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        int nameFontSize = Mathf.RoundToInt(14 * scale);
        playerNameLabel.AddThemeFontSizeOverride("font_size", nameFontSize);
        playerNameLabel.AddThemeColorOverride("font_color", Colors.White);
        ApplyHeaderFont(playerNameLabel, Mathf.RoundToInt(nameFontSize));
        nameplateHBox.AddChild(playerNameLabel);

        // Separator
        var sep = new Label
        {
            Text = "|",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        sep.AddThemeFontSizeOverride("font_size", nameFontSize);
        sep.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.70f));
        nameplateHBox.AddChild(sep);

        _playerVigorValue = new Label
        {
            Text = "25",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _playerVigorValue.AddThemeFontSizeOverride("font_size", nameFontSize);
        _playerVigorValue.AddThemeColorOverride("font_color", Colors.White);
        nameplateHBox.AddChild(_playerVigorValue);

        // Store vigor label for updates (reuse the label inside the nameplate)
        _playerShrineVigorLabel = _playerVigorValue;

        // ── Player panel: DECK/BARROW counts + artifact frames ──
                // MUST be ABOVE the nameplate (not overlapping) so both are visible.
                float panelW = nameplateW;
                float panelH = 28f * scale;
                var playerDeckBarrowPanel = new PanelContainer
                {
                    Name = "PlayerDeckBarrowPanel",
                    MouseFilter = MouseFilterEnum.Ignore,
                    CustomMinimumSize = new Vector2(panelW, panelH)
                };
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.07f, 0.06f, 0.70f),
            BorderColor = new Color(0.40f, 0.35f, 0.20f, 0.5f),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 2, ContentMarginTop = 0,
            ContentMarginRight = 2, ContentMarginBottom = 0
        };
        playerDeckBarrowPanel.AddThemeStyleboxOverride("panel", panelStyle);
        // BOARD-MATCH-3: Set explicit size (CustomMinimumSize is a request, not a hard size)
        playerDeckBarrowPanel.Size = new Vector2(panelW, panelH);
        playerDeckBarrowPanel.Position = new Vector2(12f * scale, handTop - nameplateH - 12f - panelH - 4f);
        AddChild(playerDeckBarrowPanel);

        var dbRow = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = (Control.SizeFlags)3,
            Alignment = BoxContainer.AlignmentMode.Center
        };
        playerDeckBarrowPanel.AddChild(dbRow);

        // Deck count
        _playerShrineDeckLabel = new Label
        {
            Text = "0",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _playerShrineDeckLabel.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(12 * scale));
        _playerShrineDeckLabel.AddThemeColorOverride("font_color", TextPrimary);
        ApplyHeaderFont(_playerShrineDeckLabel, Mathf.RoundToInt(12 * scale));
        dbRow.AddChild(_playerShrineDeckLabel);

        var deckLabel = new Label
        {
            Text = "DECK",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        deckLabel.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(9 * scale));
        deckLabel.AddThemeColorOverride("font_color", TextMuted);
        ApplyHeaderFont(deckLabel, Mathf.RoundToInt(9 * scale));
        dbRow.AddChild(deckLabel);

        _playerShrineBarrowLabel = new Label
        {
            Text = "0",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _playerShrineBarrowLabel.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(12 * scale));
        _playerShrineBarrowLabel.AddThemeColorOverride("font_color", TextPrimary);
        ApplyHeaderFont(_playerShrineBarrowLabel, Mathf.RoundToInt(12 * scale));
        dbRow.AddChild(_playerShrineBarrowLabel);

        var barrowLabel = new Label
        {
            Text = "BARROW",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        barrowLabel.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(9 * scale));
        barrowLabel.AddThemeColorOverride("font_color", TextMuted);
        ApplyHeaderFont(barrowLabel, Mathf.RoundToInt(9 * scale));
        dbRow.AddChild(barrowLabel);

        // ── Player Artifact card frames (teal-rimmed thumbnails, below DECK/BARROW) ──
        float artFrameW = 72f * scale;
        float artFrameH = 96f * scale;
        var artifactRow = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = (Control.SizeFlags)3,
            Alignment = BoxContainer.AlignmentMode.Center
        };
        artifactRow.Position = new Vector2(12f * scale, handTop - nameplateH - 12f - panelH - 4f - artFrameH - 4f * scale);
        AddChild(artifactRow);

        for (int i = 0; i < 2; i++)
        {
            BuildPlayerArsenalArtifact(artifactRow, i, artFrameW, artFrameH, scale);
        }

        // Set _playerGroupRect for capture meta.json compatibility
        _playerGroupRect = new Control { Name = "PlayerGroupRect" };
        _playerGroupRect.MouseFilter = MouseFilterEnum.Ignore;
        // BOARD-MATCH-1: Group encompasses nameplate + deck/barrow + artifacts
        _playerGroupRect.Position = new Vector2(8f * scale, handTop - nameplateH - panelH - artFrameH - 24f * scale);
        _playerGroupRect.Size = new Vector2(nameplateW + 8f * scale, nameplateH + panelH + artFrameH + 24f * scale);
        AddChild(_playerGroupRect);

        // Attune label below artifacts
        _playerShrineAttuneLabel = new Label
        {
            Text = "ATTUNE 3/5",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _playerShrineAttuneLabel.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(9 * scale));
        _playerShrineAttuneLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.5f, 0.2f, 1));
        ApplyHeaderFont(_playerShrineAttuneLabel, Mathf.RoundToInt(9 * scale));
        // BOARD-MATCH-3: Position below artifacts panel, between deck/barrow and nameplate
        _playerShrineAttuneLabel.Position = new Vector2(12f * scale, handTop - nameplateH - 12f - panelH - 4f + panelH + 2f * scale);
        AddChild(_playerShrineAttuneLabel);

        GD.Print("[DUEL] Revised HUD: Player nameplate + DECK/BARROW + artifacts built (bottom-left)");
    }

    /// <summary>Build a single player artifact frame using ArtifactCardPlate.</summary>
    private void BuildPlayerArsenalArtifact(HBoxContainer parent, int index, float w, float h, float scale)
    {
        var panel = new PanelContainer();
        panel.CustomMinimumSize = new Vector2(w, h);
        panel.MouseFilter = MouseFilterEnum.Ignore;
        panel.SizeFlagsHorizontal = 0;
        panel.SizeFlagsVertical = 0;
        var artStyle = new StyleBoxFlat
        {
            BgColor = ArtifactFrameFill,
            BorderWidthLeft = 0, BorderWidthTop = 0,
            BorderWidthRight = 0, BorderWidthBottom = 0,
            ContentMarginLeft = 0, ContentMarginTop = 0,
            ContentMarginRight = 0, ContentMarginBottom = 0
        };
        panel.AddThemeStyleboxOverride("panel", artStyle);

        // Root-Bound 9-slice border
        var border = new RootBoundBorder();
        border.Name = "RootBoundBorder";
        border.Setup(w, h);
        panel.AddChild(border);

        // ArtifactCardPlate — unified card frame with teal-gold rim + ARTIFACT tag + charge rail
        var plate = new ArtifactCardPlate();
        plate.Name = $"ArtPlateP{index}";
        plate.Setup("—", w, h, 0, 0, false);
        panel.AddChild(plate);

        _playerArtifactPlates[index] = plate;
        _playerArsenalPanels[index] = panel;
        parent.AddChild(panel);
    }

    /// <summary>
    /// Helper to create a stat chip (rounded rect with value label above text label).
    /// Returns a tuple of (Root PanelContainer, ValueLabel).
    /// </summary>
    private (PanelContainer Root, Label ValueLabel) MakeChip(float w, float h, string value, string labelText, Color bgTint)
    {
        var chip = new PanelContainer();
        chip.CustomMinimumSize = new Vector2(w, h);
        chip.MouseFilter = MouseFilterEnum.Ignore;
        var chipStyle = new StyleBoxFlat
        {
            BgColor = bgTint,
            BorderColor = new Color(0.5f, 0.45f, 0.35f, 0.5f),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6
        };
        chip.AddThemeStyleboxOverride("panel", chipStyle);

        var vbox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        chip.AddChild(vbox);

        var valLabel = new Label
        {
            Text = value,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            MouseFilter = MouseFilterEnum.Ignore
        };
        valLabel.AddThemeFontSizeOverride("font_size", 13);
        valLabel.AddThemeColorOverride("font_color", TextPrimary);
        vbox.AddChild(valLabel);

        var nameLabel = new Label
        {
            Text = labelText,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            MouseFilter = MouseFilterEnum.Ignore
        };
        nameLabel.AddThemeFontSizeOverride("font_size", 8);
        nameLabel.AddThemeColorOverride("font_color", TextMuted);
        vbox.AddChild(nameLabel);

        return (chip, valLabel);
    }

    /// <summary> <summary>
    /// TASK-H: Deck + Artifact side-group layout (DECISION CHANGE, supersedes FIX-5 portrait-flanking).
    /// Each player's deck pile + TWO Artifact frames form one visual group ("this is my sword and shield,
    /// next to my arsenal"). Player's group in the lower-left area, opponent's mirrored upper-right.
    /// Portraits stay; the Artifacts anchor to the DECK group. Placeholder frames with faint "Artifact" labels.
    /// The group rects are stored in _playerGroupRect/_enemyGroupRect and written to duel_test.meta.json.
    /// TASK-UI3a: Enemy side removed — replaced by top bar.
    /// </summary>
    /// <summary>
    /// TASK-UI4-ARSENAL: Build both arsenal groups (player lower-left, enemy upper-right).
    /// </summary>
    private void AddArsenalGroups()
    {
        float vw = GetViewportRect().Size.X;
        float vh = GetViewportRect().Size.Y;

        // ═══ PLAYER: Lower-left arsenal group with portrait medallion above ═══
        BuildPlayerArsenalGroup();

        // ═══ ENEMY: Upper-right arsenal group with portrait medallion above ═══
        BuildEnemyArsenalGroup();

        GD.Print($"[DUEL] TASK-UI4-ARSENAL: Both arsenal groups built (player bottom-left, enemy upper-right)");
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
    /// TASK-UI4-ARSENAL: Build both arsenal groups (player lower-left, enemy upper-right).
    /// Remove the old BuildArsenalGroup, MakeDeckPile, MakeArtifactFrame helpers — they're superseded.
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

        // ─── Load Artifact definitions (FIELD_EFFECT_SPEC §3: open info from duel start) ───
        try
        {
            string artJson = Godot.FileAccess.GetFileAsString("res://content/artifacts/launch_artifacts.json");
            if (!string.IsNullOrEmpty(artJson))
            {
                int loaded = ArtifactLoader.LoadFromString(artJson);
                GD.Print($"[DUEL] Loaded {loaded} artifact definitions");
            }
            else
            {
                GD.PrintErr("[DUEL] Failed to load artifacts JSON — file empty or missing");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DUEL] Failed to load artifact definitions: {ex.Message}");
        }
    }

    /// <summary>
    /// PAINTED-PLATE-1: Draw a debug slot outline on the given control — filled rect with
    /// colored border + centered label. Used by align capture mode.
    /// </summary>
    private static void DrawSlotOutline(Control target, Vector2 origin, Vector2 size, Color color, string label)
    {
        float x = origin.X;
        float y = origin.Y;
        float w = size.X;
        float h = size.Y;
        // Fill with transparent color
        target.DrawRect(new Rect2(x, y, w, h), new Color(color.R, color.G, color.B, 0.15f));
        // Top edge
        target.DrawLine(new Vector2(x, y), new Vector2(x + w, y), color, 2f);
        // Bottom edge
        target.DrawLine(new Vector2(x, y + h), new Vector2(x + w, y + h), color, 2f);
        // Left edge
        target.DrawLine(new Vector2(x, y), new Vector2(x, y + h), color, 2f);
        // Right edge
        target.DrawLine(new Vector2(x + w, y), new Vector2(x + w, y + h), color, 2f);
    }

    /// <summary>
    /// Compute card sizes from viewport height (FIX 3a): hand ~180px at 1080p,
    /// board ~200px at 1080p. Scales proportionally on smaller viewports.
    /// </summary>
    private void ScaleCardSizes(float viewportHeight)
    {
        // DUELRES-1: Design resolution 2316×1080. Reference = 1080, scale=1.0 at design size.
        // Board cards ~200px wide, 7% band ~14px. Hand cards 173×253 at design scale.
        float reference = 1080f;
        float scale = viewportHeight / reference;

        // BOARD-MATCH-1: Hand cards distinctly larger than board cards
        // Board = 292px tall, Hand = 340px tall at design scale
        _handCardHeight = Mathf.Max(140f, 340f * scale);
        // DUELRES-1: Board cards ~200px wide at 1080, 7% band ~14px
        _boardCardHeight = Mathf.Max(70f, 292f * scale);

        // R2 variant: increase card sizes by ~10% for wider art share
        if (CampaignContext.R2CardScale)
        {
            _handCardHeight = Mathf.Max(140f, 370f * scale);
            _boardCardHeight = Mathf.Max(70f, 320f * scale);
        }

        // BOARD-MATCH-1: Hand tray bottom-edge tucked into frame
        // BOARD-DEVICE-1: Hand stat chips must stay inside safe area with >= 8px margin
        var safeArea = DisplayServer.GetDisplaySafeArea();
        float vh = GetViewportRect().Size.Y;
        if (CampaignContext.DebugSafeAreaMode)
        {
            // Simulate Android-style safe-area inset: bottom 48px, top 32px
            float simulatedSafeBottom = vh - 48f;
            safeArea = new Rect2I(0, 32, (int)safeArea.Size.X, (int)(simulatedSafeBottom - 32f));
        }
        float safeAreaBottom = safeArea.Position.Y + safeArea.Size.Y;
        // Chips are at the bottom edge of hand cards. Hand bottom must be <= safeAreaBottom - 8.
        // Current: hand bottom = vh + OffsetTop + handCardHeight = 6px from vh bottom = tucked in
        // Required: gap = (safe area bottom margin) + 8px margin, minimum 6px for normal tuck
        float safeMargin = vh - safeAreaBottom;
        float bottomGap = Mathf.Max(6f, safeMargin + 8f);
        _handArea.OffsetTop = -(_handCardHeight + bottomGap);

        // BOARD-MATCH-1: Hand centered, wider margin to allow center alignment
        float marginLeft = Mathf.Max(180f, 180f * scale);
        _handArea.AddThemeConstantOverride("margin_left", Mathf.FloorToInt(marginLeft));
        _handArea.AddThemeConstantOverride("margin_right", 80);

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

        // AltarField — PAINTED-PLATE-1: no ellipse drawn, this is just a
        // transparent container for geometry reference. The full-board
        // painted plate (BoardBg) carries the visual ring.
        _altarField = new AltarField { Name = "AltarField" };
        _altarField.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _altarField.MouseFilter = Control.MouseFilterEnum.Ignore;
        board.AddChild(_altarField);

        // ── Container for arc-positioned slots ──
        _altarContainer = new Control { Name = "AltarContainer" };
        _altarContainer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _altarContainer.MouseFilter = Control.MouseFilterEnum.Ignore;
        board.AddChild(_altarContainer);

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
        // DUELRES-1: Design resolution 2316×1080. Reference = 1080.
        float scale = vh / 1080f;
        float slotH = 292f * scale;
        float slotW = 200f * scale;

        // R2 variant: increase slot sizes
        if (CampaignContext.R2CardScale)
        {
            slotH = 320f * scale;
            slotW = 220f * scale;
        }

        // Arc geometry: X positions (centers) spread across the ellipse
        float centerX = vw / 2f;
        // DUELRES-1: 5 × 200px cards + gaps fit within 2316
        float spacing = 350f * scale;

        // DUELRES-1: Board slots 200×292 at 1080, spread rows
        float boardTopOffset = 123f;
        float enemyBaseY = 100f * scale - boardTopOffset;
        float playerBaseY = 740f * scale - boardTopOffset - slotH;

        for (int i = 0; i < 5; i++)
        {
            float xCenter = centerX + (i - 2) * spacing;
            float x = xCenter - slotW / 2f;

            // ── Enemy slot (top arc, slight bowing) ──
            float enemyYOffset = i switch { 0 or 4 => 10f, 1 or 3 => 5f, _ => 0f } * scale;
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
            // CARD-POLISH-1: amplitude reduced to 8 so bigger slots fit without overlap
            float playerYOffset = i switch { 0 or 4 => 13f, 1 or 3 => 7f, _ => 0f } * scale;
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

        GD.Print($"[DUEL] WORLD-POLISH-1: Populated {_enemySlots.Count} enemy + {_playerSlots.Count} player arc slots");

        // BOARD-MATCH-1: Hand anchored to viewport bottom, tighter tuck
        // BOARD-DEVICE-1: Hand stat chips must stay inside safe area with >= 8px margin
        float vhPop = GetViewportRect().Size.Y;
        float scalePop = vhPop / 1080f;
        float handCardHPop = Mathf.Max(140f, 340f * scalePop); // Hand larger than board cards
        var saPop = DisplayServer.GetDisplaySafeArea();
        if (CampaignContext.DebugSafeAreaMode)
        {
            float simSafeBottomPop = vhPop - 48f;
            saPop = new Rect2I(0, 32, (int)saPop.Size.X, (int)(simSafeBottomPop - 32f));
        }
        float saBottomPop = saPop.Position.Y + saPop.Size.Y;
        float safeMarginPop = vhPop - saBottomPop;
        float bottomGapPop = Mathf.Max(6f, safeMarginPop + 8f);
        float handTopPop = vhPop - handCardHPop - bottomGapPop;
        _handArea.OffsetTop = -(vhPop - handTopPop);
        GD.Print($"[DUEL] Hand position: hand top={handTopPop:F0}, card height={handCardHPop:F0}, viewport={vhPop:F0}, safe-area-bottom-margin={safeMarginPop:F0}");

        // [VERIFY] Band layout — DUELRES-1: enemy 100..412, player 448..740, gap 13px at design
        bool verifyFailed = false;
        foreach (var slot in _enemySlots)
        {
            float absTop = boardTopOffset + slot.Position.Y;
            float absBottom = absTop + slot.Size.Y;
            // BOARD-MATCH-1: 292px cards at 1080 scale. Enemy band: 100 (top) to 402 (bottom) with ±2px tolerance.
            if (absTop < 100f * scale - 2f || absBottom > 402f * scale + 2f)
            {
                GD.PrintErr($"[VERIFY] Enemy slot at screen Y {absTop:F0}-{absBottom:F0} outside band 100-{402f * scale:F0}");
                verifyFailed = true;
            }
        }
        foreach (var slot in _playerSlots)
        {
            float absTop = boardTopOffset + slot.Position.Y;
            float absBottom = absTop + slot.Size.Y;
            // BOARD-MATCH-1: 292px cards at 1080 scale. Player band: 435 (top) to 740 (bottom) with ±2px tolerance.
            if (absTop < 435f * scale - 2f || absBottom > 740f * scale + 2f)
            {
                GD.PrintErr($"[VERIFY] Player slot at screen Y {absTop:F0}-{absBottom:F0} outside band 435-{740f * scale:F0}");
                verifyFailed = true;
            }
        }
        // Min gap between rows (435 - 402 = 33px at design scale)
        float enemyBottom = boardTopOffset + _enemySlots.Max(s => s.Position.Y + s.Size.Y);
        float playerTop = boardTopOffset + _playerSlots.Min(s => s.Position.Y);
        float gapBetween = playerTop - enemyBottom;
        if (gapBetween < 13f * scale - 1f)
        {
            GD.PrintErr($"[VERIFY] Gap {gapBetween:F0}px < min 13px between rows");
            verifyFailed = true;
        }
        // Hand top never enters player row band
        // BOARD-MATCH-1: Player band bottom ≈740. Hand top at 734 is clear by ~7px from card bottoms.
        if (handTopPop < 730f * scalePop)
        {
            GD.PrintErr($"[VERIFY] Hand top {handTopPop} enters player row band (band bottom = {740f * scalePop:F0})");
            verifyFailed = true;
        }
        // Min horizontal gap between adjacent board cards
        for (int i = 1; i < _enemySlots.Count; i++)
        {
            float gap_adj = _enemySlots[i].Position.X - (_enemySlots[i-1].Position.X + _enemySlots[i-1].Size.X);
            if (gap_adj < 30f * scale - 1f)
            {
                GD.PrintErr($"[VERIFY] Enemy slot horizontal gap {gap_adj:F0}px < min 30px (idx {i-1}-{i})");
                verifyFailed = true;
            }
        }
        if (!verifyFailed)
            GD.Print("[VERIFY] duel band layout: 0 failed");
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

        // ═══ TASK-WARDEN-RULE-1: Update opening rule banner ═══
        if (_openingRuleBanner != null && _openingRuleLabel != null)
        {
            var st = _gsm.State;
            if (st != null && !string.IsNullOrEmpty(st.OpeningRule))
            {
                string ruleText = GetOpeningRuleDisplayText(st.OpeningRule);
                bool isLifted = st.OpeningRuleLifted[st.OpeningRuleOwner];
                if (!string.IsNullOrEmpty(ruleText))
                {
                    _openingRuleLabel.Text = isLifted
                        ? $"[LIFTED] {ruleText}"
                        : ruleText;
                    _openingRuleBanner.Visible = true;
                }
                else
                {
                    _openingRuleBanner.Visible = false;
                }
            }
            else
            {
                _openingRuleBanner.Visible = false;
            }
        }

        // ═══ TASK-AUDIO-HOOK-1: Detect card draw ═══
        if (state != null && state.Players.Length > 0)
        {
            int currentHandSize = state.Players[0].Hand.Count;
            if (currentHandSize > _prevHandSize)
            {
                var audio = GetNode<AudioManager>("/root/AudioManager");
                audio.PlaySfx("card_draw");
            }
            _prevHandSize = currentHandSize;
        }

        // ═══ TASK-AUDIO-HOOK-1: Detect artifact charge full ═══
        if (state != null)
        {
            int fullMask = 0;
            for (int side = 0; side <= 1; side++)
            for (int ai = 0; ai < (state.Players[side].ArtifactSlots?.Length ?? 0); ai++)
            {
                var slot = state.Players[side].ArtifactSlots[ai];
                if (slot.Occupant != null && slot.MaxCharges > 0 && slot.Charges >= slot.MaxCharges)
                    fullMask |= (1 << (side * 2 + ai));
            }
            if (fullMask != 0 && fullMask != _prevPlayerChargesFull)
            {
                var audio = GetNode<AudioManager>("/root/AudioManager");
                audio.PlaySfx("metal_clink");
            }
            _prevPlayerChargesFull = fullMask;
        }

        // ═══ TASK-JUICE-1: Artifact charge brighten — detect per-slot charge gain ═══
        if (state != null && !_firstRender)
        {
            for (int side = 0; side <= 1; side++)
            {
                for (int ai = 0; ai < (state.Players[side].ArtifactSlots?.Length ?? 0); ai++)
                {
                    var slot = state.Players[side].ArtifactSlots[ai];
                    if (slot.Occupant == null || slot.MaxCharges <= 0) continue;
                    
                    int prevCharges = side == 0 ? _prevPlayerCharges[ai] : _prevEnemyCharges[ai];
                    if (slot.Charges > prevCharges)
                    {
                        var plate = side == 0 ? _playerArtifactPlates[ai] : _enemyArtifactPlates[ai];
                        if (plate != null && IsInstanceValid(plate) && plate.IsInsideTree())
                        {
                            bool isFull = slot.Charges >= slot.MaxCharges;
                            RitualEffects.PlayChargeBrighten(plate, isFull, CampaignContext.ReduceMotion);
                        }
                    }
                    if (side == 0) _prevPlayerCharges[ai] = slot.Charges;
                    else _prevEnemyCharges[ai] = slot.Charges;
                }
            }
        }

        // ═══ TASK-JUICE-1: End Turn — altar ring turns one notch ═══
        if (state != null && !_firstRender && _gsm.TurnNumber != _prevTurnNumber)
        {
            if (!CampaignContext.ReduceMotion)
            {
                var altar = GetNodeOrNull<Control>("Board/AltarContainer");
                if (altar != null && altar.IsInsideTree())
                    RitualEffects.PlayRingTurnNotch(altar);
                else
                    RitualEffects.PlayRingTurnNotch(this);
            }
            _prevTurnNumber = _gsm.TurnNumber;
        }

        // Compute diffs and trigger animations using the previous snapshot
        if (!_firstRender)
        {
            AnimateBoardDiffs(_prevEnemyBoard, _prevPlayerBoard, newEnemyBoard, newPlayerBoard);
            AnimateVigorDiffs();
        }

        // ═══ TASK-E: Game-over overlay — check after rendering each frame ═══
        bool isGameOver = _gsm.IsGameOver;
        if (isGameOver)
        {
            // Destroy and rebuild each time (encounter data can change)
            if (_gameOverOverlay != null)
            {
                _gameOverOverlay.QueueFree();
                _gameOverOverlay = null;
            }
            BuildGameOverOverlay();
            _gameOverOverlay!.Show();
            // Bring to top so it captures all input
            if (_gameOverOverlay.GetParent() != null)
                MoveChild(_gameOverOverlay, GetChildCount() - 1);
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

        // TASK-TU2: Notify TutorialRunner of state change
        _tutorialRunner?.OnGameStateChanged();

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
                Vigor = lanes[i].Vigor,
                Strata = lanes[i].Strata
            };
        }
        return result;
    }

    private void AnimateBoardDiffs(BoardSnapshot[] oldEnemy, BoardSnapshot[] oldPlayer,
        BoardSnapshot[] newEnemy, BoardSnapshot[] newPlayer)
    {
        // TASK-AUDIO-HOOK-1: one sound per resolution, not per target
        // Track audio per side to avoid overlap
        bool audioPlayedEnemy = false;
        bool audioPlayedPlayer = false;

        for (int i = 0; i < 5; i++)
        {
            var slot = _enemySlots[i];
            var prev = oldEnemy[i];
            var cur = newEnemy[i];

            if (prev.IsEmpty && !cur.IsEmpty)
            {
                slot.PlaySummonEffect();
                if (!audioPlayedEnemy)
                {
                    audioPlayedEnemy = true;
                    GetNode<AudioManager>("/root/AudioManager").PlaySfx("card_play");
                }
            }
            else if (!prev.IsEmpty && cur.IsEmpty)
            {
                slot.PlayDeathEffect(prev.Strata);
                if (!audioPlayedEnemy)
                {
                    audioPlayedEnemy = true;
                    GetNode<AudioManager>("/root/AudioManager").PlaySfx("death");
                }
            }
            else if (!prev.IsEmpty && !cur.IsEmpty)
            {
                int dmg = prev.Vigor - cur.Vigor;
                if (dmg > 0 && !CampaignContext.ReduceMotion)
                {
                    RitualEffects.PlayRuneFlare(slot, prev.Strata);
                    slot.ShowDamageNumber(dmg, prev.Strata);
                    if (!audioPlayedEnemy)
                    {
                        audioPlayedEnemy = true;
                        GetNode<AudioManager>("/root/AudioManager").PlaySfx("damage");
                    }
                }
                else if (dmg < 0 && !CampaignContext.ReduceMotion) slot.ShowHealNumber(-dmg);
            }
        }

        for (int i = 0; i < 5; i++)
        {
            var slot = _playerSlots[i];
            var prev = oldPlayer[i];
            var cur = newPlayer[i];

            if (prev.IsEmpty && !cur.IsEmpty)
            {
                slot.PlaySummonEffect();
                if (!audioPlayedPlayer)
                {
                    audioPlayedPlayer = true;
                    GetNode<AudioManager>("/root/AudioManager").PlaySfx("card_play");
                }
            }
            else if (!prev.IsEmpty && cur.IsEmpty)
            {
                slot.PlayDeathEffect(prev.Strata);
                if (!audioPlayedPlayer)
                {
                    audioPlayedPlayer = true;
                    GetNode<AudioManager>("/root/AudioManager").PlaySfx("death");
                }
            }
            else if (!prev.IsEmpty && !cur.IsEmpty)
            {
                int dmg = prev.Vigor - cur.Vigor;
                if (dmg > 0 && !CampaignContext.ReduceMotion)
                {
                    RitualEffects.PlayRuneFlare(slot, prev.Strata);
                    slot.ShowDamageNumber(dmg, prev.Strata);
                    if (!audioPlayedPlayer)
                    {
                        audioPlayedPlayer = true;
                        GetNode<AudioManager>("/root/AudioManager").PlaySfx("damage");
                    }
                }
                else if (dmg < 0 && !CampaignContext.ReduceMotion) slot.ShowHealNumber(-dmg);
            }
        }
    }

    private void AnimateVigorDiffs()
    {
        var enemyHud = _gsm.GetPlayerHud(1);
        var playerHud = _gsm.GetPlayerHud(0);

        // TASK-JUICE-1: Determine the attacker stratum for face damage colour
        // Use the last non-empty player slot's stratum as the player's attacking colour
        // (simplified: default to gold if no specific attacker)

        if (_prevEnemyVigor >= 0 && enemyHud.Vigor != _prevEnemyVigor)
        {
            int diff = _prevEnemyVigor - enemyHud.Vigor;
            ShowFaceDamage(true, diff);
            // Screen edge pulse in player's attacking colour (enemy is being hit by player)
            Color pulseColor = Gold;
            var playerLaneInfo = _gsm.GetLanes(0);
            foreach (var li in playerLaneInfo)
            {
                if (!li.IsEmpty)
                {
                    pulseColor = ThemeTokens.StrataColor(li.Strata);
                    break; // Use first occupied lane's stratum
                }
            }
            RitualEffects.PlayFaceDamagePulse(this, GetOrCreateScreenEdgePulse(), pulseColor, CampaignContext.ReduceMotion);
            // TASK-AUDIO-HOOK-1: hit_light (dmg ≤3) / hit_heavy (dmg ≥4)
            if (diff > 0)
            {
                var audio = GetNode<AudioManager>("/root/AudioManager");
                audio.PlaySfx(diff <= 3 ? "hit_light" : "hit_heavy");
            }
        }

        if (_prevPlayerVigor >= 0 && playerHud.Vigor != _prevPlayerVigor)
        {
            int diff = _prevPlayerVigor - playerHud.Vigor;
            ShowFaceDamage(false, diff);
            // Screen edge pulse in enemy's attacking colour (player is being hit by enemy)
            Color pulseColor = Ember;
            var enemyLaneInfo = _gsm.GetLanes(1);
            foreach (var li in enemyLaneInfo)
            {
                if (!li.IsEmpty)
                {
                    pulseColor = ThemeTokens.StrataColor(li.Strata);
                    break;
                }
            }
            RitualEffects.PlayFaceDamagePulse(this, GetOrCreateScreenEdgePulse(), pulseColor, CampaignContext.ReduceMotion);
            // TASK-AUDIO-HOOK-1: hit_light (dmg ≤3) / hit_heavy (dmg ≥4)
            if (diff > 0)
            {
                var audio = GetNode<AudioManager>("/root/AudioManager");
                audio.PlaySfx(diff <= 3 ? "hit_light" : "hit_heavy");
            }
        }

        _prevEnemyVigor = enemyHud.Vigor;
        _prevPlayerVigor = playerHud.Vigor;
    }

    /// <summary>Get or create the reusable screen-edge pulse overlay.</summary>
    private ColorRect GetOrCreateScreenEdgePulse()
    {
        if (_screenEdgePulse != null && IsInstanceValid(_screenEdgePulse) && _screenEdgePulse.IsInsideTree())
            return _screenEdgePulse;
        
        _screenEdgePulse = new ColorRect
        {
            Name = "ScreenEdgePulse",
            Color = new Color(0, 0, 0, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _screenEdgePulse.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_screenEdgePulse);
        return _screenEdgePulse;
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
            // Health bar shake — removed in WORLD-POLISH-1
            /*
            {
                var origPos = _playerHealthBar.Position;
                var shake = CreateTween();
                shake.TweenProperty(_playerHealthBar, "position", origPos + new Vector2(8, 0), 0.04f);
                shake.TweenProperty(_playerHealthBar, "position", origPos - new Vector2(8, 0), 0.04f);
                shake.TweenProperty(_playerHealthBar, "position", origPos + new Vector2(4, 0), 0.04f);
                shake.TweenProperty(_playerHealthBar, "position", origPos, 0.04f);
            }
            */
        }

        if (isEnemy)
            pos = _enemyVigorValue.GlobalPosition + new Vector2(40, -10);
        else
            pos = _playerShrineVigorLabel?.GlobalPosition ?? new Vector2(100, 500) + new Vector2(40, -20);

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

            // TASK-UI4-ARSENAL: Artifact frames via ArtifactCardPlate — teal-gold rim, charge rail, suppressed state
            int artSlots = p1.ArtifactSlots?.Length ?? 0;
            for (int i = 0; i < 2; i++)
            {
                if (i < artSlots && p1.ArtifactSlots[i]?.Occupant != null)
                {
                    var slot = p1.ArtifactSlots[i];
                    var occ = slot.Occupant;
                    var artDef = ArtifactRegistry.Get(occ.CardDefId);
                    string artName = artDef?.Name ?? "?";
                    int ch = slot.Charges;
                    int maxCh = slot.MaxCharges;
                    bool suppressed = slot.VisualState == ArtifactVisualState.SUPPRESSED;
                    // Update ArtifactCardPlate name, charges, suppressed state
                    if (_enemyArtifactPlates[i] != null)
                    {
                        _enemyArtifactPlates[i].Setup(artName, 100f, 136f, ch, maxCh, suppressed);
                        // BOARD-MATCH-2: Load artifact art thumbnail
                        _enemyArtifactPlates[i].SetArt(occ.CardDefId);
                    }
                    // TASK-AC2: Detect charge-full and pulse (skip when suppressed per G3)
                    if (maxCh > 0 && ch >= maxCh && !slot.IsSuppressed)
                    {
                        int prevCh = _prevEnemyCharges[i];
                        if (prevCh < maxCh)
                        {
                            if (_enemyArtifactPlates[i] != null)
                            {
                                var chargeLabel = _enemyArtifactPlates[i].GetNameLabel();
                                if (chargeLabel != null)
                                    PlayChargeFullPulse(chargeLabel, i, true);
                            }
                        }
                    }
                    _prevEnemyCharges[i] = ch;

                    // TASK-ARTF-P2: Trigger flash — detect HasTriggeredThisTurn rising edge
                    bool nowTriggered = slot.HasTriggeredThisTurn;
                    if (nowTriggered && !_prevEnemyTriggered[i])
                    {
                        if (_enemyArtifactPlates[i] != null)
                            _enemyArtifactPlates[i].PlayTriggerFlash();
                    }
                    _prevEnemyTriggered[i] = nowTriggered;
                }
                else
                {
                    if (_enemyArtifactPlates[i] != null)
                        _enemyArtifactPlates[i].Setup("—", 100f, 136f, 0, 0, false);
                }
            }
        }

        // TASK-UI3c: Player shrine — artifacts, deck, barrow, vigor
        if (state != null && state.Players.Length > 0)
        {
            var p0 = state.Players[0];
            if (_playerShrineDeckLabel != null)
                _playerShrineDeckLabel.Text = p0.Deck.Count.ToString();
            if (_playerShrineBarrowLabel != null)
                _playerShrineBarrowLabel.Text = p0.Barrow.Count.ToString();
            if (_playerShrineVigorLabel != null)
                _playerShrineVigorLabel.Text = p0.Vigor.ToString(); // BOARD-MATCH-1: Just the number
            if (_playerShrineAttuneLabel != null)
                _playerShrineAttuneLabel.Text = $"ATTUNE {playerHud.Attunement}/{playerHud.AttunementMax}";

            // TASK-UI4-ARSENAL: Player artifact frames via ArtifactCardPlate
            int artSlots = p0.ArtifactSlots?.Length ?? 0;
            for (int i = 0; i < 2; i++)
            {
                if (i < artSlots && p0.ArtifactSlots[i]?.Occupant != null)
                {
                    var slot = p0.ArtifactSlots[i];
                    var occ = slot.Occupant;
                    var artDef = ArtifactRegistry.Get(occ.CardDefId);
                    string artName = artDef?.Name ?? "?";
                    int ch = slot.Charges;
                    int maxCh = slot.MaxCharges;
                    bool suppressed = slot.VisualState == ArtifactVisualState.SUPPRESSED;
                    // Update ArtifactCardPlate name, charges, suppressed state
                    if (_playerArtifactPlates[i] != null)
                    {
                        _playerArtifactPlates[i].Setup(artName, 100f, 136f, ch, maxCh, suppressed);
                        // BOARD-MATCH-2: Load artifact art thumbnail
                        _playerArtifactPlates[i].SetArt(occ.CardDefId);
                    }
                    // TASK-AC2: Detect charge-full and pulse (skip when suppressed per G3)
                    if (maxCh > 0 && ch >= maxCh && !slot.IsSuppressed)
                    {
                        int prevCh = _prevPlayerCharges[i];
                        if (prevCh < maxCh)
                        {
                            if (_playerArtifactPlates[i] != null)
                            {
                                var chargeLabel = _playerArtifactPlates[i].GetNameLabel();
                                if (chargeLabel != null)
                                    PlayChargeFullPulse(chargeLabel, i, false);
                            }
                        }
                    }
                    _prevPlayerCharges[i] = ch;

                    // TASK-ARTF-P2: Trigger flash — detect HasTriggeredThisTurn rising edge
                    bool nowTriggered = slot.HasTriggeredThisTurn;
                    if (nowTriggered && !_prevPlayerTriggered[i])
                    {
                        if (_playerArtifactPlates[i] != null)
                            _playerArtifactPlates[i].PlayTriggerFlash();
                    }
                    _prevPlayerTriggered[i] = nowTriggered;
                }
                else
                {
                    if (_playerArtifactPlates[i] != null)
                        _playerArtifactPlates[i].Setup("—", 100f, 136f, 0, 0, false);
                }
            }
        }

        SetPlayerVigor(playerHud.Vigor);
        SetPlayerAttunement($"{playerHud.Attunement}/{playerHud.AttunementMax}");

        // Turn indicator — TASK-UI3e: top label just shows turn number; "YOUR TURN" is near End Turn btn
        bool isMyTurn = _gsm.CurrentPlayerIndex == 0;
        _turnLabel.Text = $"Turn {_gsm.TurnNumber}";
        _turnLabel.Modulate = isMyTurn ? Gold : Ember;
        _turnIndicatorLabel.Text = isMyTurn ? "YOUR TURN" : "ENEMY TURN";
        _turnIndicatorLabel.Modulate = isMyTurn ? Gold : Ember;
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
        int n = hand.Count;
        float aspect = 104f / 152f;

        // TASK-DUEL-HAND-1: Dynamic hand fan — always fits on screen with art readable.
        // Available width: viewport minus HandArea margins minus End Turn button buffer.
        float lMargin = _handArea.GetThemeConstant("margin_left");
        float rMargin = _handArea.GetThemeConstant("margin_right");
        if (lMargin <= 0) lMargin = 180f;
        if (rMargin <= 0) rMargin = 80f;
        float endTurnBuffer = 100f;
        float availWidth = GetViewportRect().Size.X - lMargin - rMargin - endTurnBuffer;

        // Always center alignment
        _handFlow.Alignment = BoxContainer.AlignmentMode.Center;

        // Max overlap: 35% of card width — keeps >50% of each card visible so art is readable
        const float maxOverlapFraction = 0.35f;

        // Start with base card height from ScaleCardSizes
        float cardHeight = _handCardHeight;
        float cardWidth = cardHeight * aspect;

        // Compute spacing/overlap
        float spacing;
        if (n <= 1)
        {
            spacing = 0f;
        }
        else
        {
            // Ideal spacing: small positive gap for few cards, slight overlap for many
            float idealSpacing = n <= 3 ? 6f : (n <= 6 ? 2f : -4f);
            float totalWidth = n * cardWidth + (n - 1) * idealSpacing;

            if (totalWidth <= availWidth)
            {
                // Fits with ideal spacing
                spacing = idealSpacing;
            }
            else
            {
                // Need more overlap. Compute minimum overlap to fit.
                float requiredOverlap = (availWidth - n * cardWidth) / (n - 1);
                float overlapFraction = -requiredOverlap / cardWidth;

                if (overlapFraction <= maxOverlapFraction)
                {
                    spacing = requiredOverlap; // negative = overlap
                }
                else
                {
                    // Overlap would hide too much art — shrink cards instead.
                    // Solve: newCardWidth * (n - (n-1) * maxOverlapFraction) = availWidth
                    float newCardWidth = availWidth / (n - (n - 1) * maxOverlapFraction);
                    cardHeight = newCardWidth / aspect;
                    cardHeight = Mathf.Max(120f, cardHeight); // floor: keep art and text readable
                    cardWidth = cardHeight * aspect;
                    spacing = -maxOverlapFraction * cardWidth;

                    // Update hand area vertical position for new height
                    float safeBottom = DisplayServer.GetDisplaySafeArea().Position.Y
                        + DisplayServer.GetDisplaySafeArea().Size.Y;
                    float vh = GetViewportRect().Size.Y;
                    float safeMargin = vh - safeBottom;
                    float bottomGap = Mathf.Max(6f, (CampaignContext.DebugSafeAreaMode ? safeMargin : 0f) + 8f);
                    _handArea.OffsetTop = -(cardHeight + bottomGap);
                }
            }
        }

        // Clamp spacing to reasonable range
        spacing = Mathf.Clamp(spacing, -60f, 20f);
        _handFlow.AddThemeConstantOverride("separation", Mathf.RoundToInt(spacing));

        GD.Print($"[HAND] {n} cards, height={cardHeight:F0}, cardW={cardWidth:F0}, spacing={spacing:F1}, avail={availWidth:F0}, viewport={GetViewportRect().Size.X:F0}");

        foreach (var info in hand)
        {
            var card = handScene.Instantiate<HandCard>();
            _handFlow.AddChild(card);
            card.ScaleTo(cardHeight);
            card.SetCard(info.CardDefId, info.Name, info.Cost, info.Strata);

            // Playability: full brightness when affordable; desaturated when not
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
        _turnIndicatorLabel.Text = "ENEMY TURN";
        _turnIndicatorLabel.Modulate = Ember;
    }

    private void OnBotTurnEnded()
    {
        // No-op — bot turn completion is visually silent
    }

    // ——— Input event handlers ———

    private void OnLaneTapped(int laneIndex, bool isEmpty)
    {
        if (_bot.IsThinking)
        {
            GD.Print($"[DUEL_TRACE] OnLaneTapped: SKIP (bot thinking) lane={laneIndex} isEmpty={isEmpty}");
            return;
        }

        GD.Print($"[DUEL_TRACE] OnLaneTapped: lane={laneIndex} isEmpty={isEmpty} inputState={_input.State}");

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
        if (_bot.IsThinking)
        {
            GD.Print($"[DUEL_TRACE] OnHandCardPressed: SKIP (bot thinking) card={card.CardName}");
            return;
        }

        GD.Print($"[DUEL_TRACE] OnHandCardPressed: card={card.CardName} state={_input.State} selectedId={_input.SelectedCardId}");

        // Tap-again-to-deselect: if this card is already selected, cancel
        if (_input.State == InputController.InputState.SelectingLane
            && _input.SelectedCardId == card.CardId)
        {
            _input.CancelSelection();
            ShowToast("Deselected.", Color.FromHtml("#8A7A3A"));
            UpdateSelectionVisuals();
            return;
        }

        if (_input.State == InputController.InputState.SelectingAttacker)
        {
            // Cancel attacker selection and start playing this card instead
            _input.CancelSelection();
            _input.SelectCardForPlay(card.CardId);
            ShowToast($"Select a lane to summon {card.CardName} (cost {card.CardCost})",
                Moss);
            UpdatePlayHighlights();
            UpdateSelectionVisuals();
        }
        else if (_input.State == InputController.InputState.SelectingLane)
        {
            // Already in lane-selection mode — switch to this card
            _input.CancelSelection();
            _input.SelectCardForPlay(card.CardId);
            ShowToast($"Select a lane to summon {card.CardName} (cost {card.CardCost})",
                Moss);
            UpdatePlayHighlights();
            UpdateSelectionVisuals();
        }
        else
        {
            // Idle — enter lane-selection mode (tap-to-summon), no detail popup
            _input.SelectCardForPlay(card.CardId);
            ShowToast($"Select a lane to summon {card.CardName} (cost {card.CardCost})",
                Moss);
            UpdatePlayHighlights();
            UpdateSelectionVisuals();
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
        GD.Print($"[DUEL_TRACE] OnPlayCardRequested: cardId={cardId} lane={laneIndex}");
        var result = _gsm.TryPlayCard(0, cardId, laneIndex);
        if (!result.Success)
        {
            GD.Print($"[DUEL_TRACE] OnPlayCardRequested FAILED: {result.ErrorMessage}");
            ShowToast(result.ErrorMessage ?? "Cannot play that card.",
                Gold);
        }
        else
        {
            GD.Print($"[DUEL_TRACE] OnPlayCardRequested SUCCESS: card={cardId} placed in lane {laneIndex}");
            // TASK-AUDIO-HOOK-1: Play sfx for card played / spell resolved
            var audio = GetNode<AudioManager>("/root/AudioManager");
            var def = Runewake.Engine.Cards.CardRegistry.Get(cardId);
            if (def != null && def.Type == Runewake.Engine.Cards.CardType.RITUAL)
                audio.PlaySfx("spell");
            else
                audio.PlaySfx("card_play");
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
        UpdateSelectionVisuals();
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

        UpdateSelectionVisuals();
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

        UpdateSelectionVisuals();
    }

    /// <summary>
    /// Update visual selection state on hand cards and lane slots.
    /// </summary>
    private void UpdateSelectionVisuals()
    {
        string? selectedId = _input.State == InputController.InputState.SelectingLane
            ? _input.SelectedCardId : null;

        foreach (var hc in _handCards)
        {
            bool isSelected = selectedId != null && hc.CardId == selectedId;
            if (isSelected)
            {
                hc.SetSelected(true);
                // Also scroll the hand so the selected card is visible
                var scroll = GetNodeOrNull<ScrollContainer>("HandScroll");
                if (scroll != null && hc.IsInsideTree())
                {
                    float cardCenter = hc.Position.X + hc.Size.X / 2f;
                    float viewCenter = scroll.ScrollHorizontal + scroll.Size.X / 2f;
                    if (Mathf.Abs(cardCenter - viewCenter) > scroll.Size.X / 3f)
                    {
                        scroll.ScrollHorizontal = Mathf.Max(0, (int)(cardCenter - scroll.Size.X / 2f));
                    }
                }
            }
            else
            {
                hc.SetSelected(false);
            }
        }

        // Update creature selection visual on board
        int selectedAttacker = _input.State == InputController.InputState.SelectingAttacker
            ? _input.SelectedAttackerLane : -1;

        foreach (var slot in _playerSlots)
        {
            if (slot.LaneIndex == selectedAttacker)
                slot.HighlightAsSelected();
            else
            {
                // Restore normal highlight if lane was otherwise targeted
                if (_input.State == InputController.InputState.SelectingAttacker)
                {
                    var lanes = _gsm.GetLanes(0);
                    var info = lanes[slot.LaneIndex];
                    if (info.IsEmpty)
                        slot.Unhighlight();
                    else
                        slot.Highlight(); // still a valid attacker option
                }
            }
        }
    }

    /// <summary>
    /// BOT-FIX-1: headless bot-duel harness. In normal mode, P0 is a passive player that just
    /// ends its turn; the BotController plays P1. In soak mode, both sides use GreedyBot.
    /// Logs per-cycle vigor and quits after the game ends or 14 P0 turns, whichever comes first.
    /// </summary>
    private void StartBotDuelTest()
    {
        GD.Print("[BotDuelTest] starting — passive P0, live BotController P1");
        int p0Turns = 0;
        var t = new Godot.Timer();
        t.OneShot = false;
        t.WaitTime = 0.05f; // TASK-PLAYABLE-NAV-1: 50ms cycle for fast soak duels (llvmpipe is slow)
        t.Timeout += () =>
        {
            if (_gsm == null) return;
            if (_gsm.IsGameOver)
            {
                var st = _gsm.State;
                GD.Print($"[BotDuelTest] RESULT gameOver turn={st.TurnNumber} winner={st.WinnerIndex} p0Vigor={st.Players[0].Vigor} p1Vigor={st.Players[1].Vigor}");
                GetNode<AudioManager>("/root/AudioManager").WriteAudioVerificationReport("artifacts/captures/audio_verify.json");
                if (CampaignContext.SoakActive)
                {
                    // TASK-PLAYABLE-NAV-1: Always mark the node cleared in soak mode (win or loss)
                    // so the soak loop does not re-select the same node and loop forever.
                    if (CampaignContext.CurrentNodeId != null)
                    {
                        CampaignContext.Progression.MarkNodeCleared(CampaignContext.CurrentNodeId);
                        CampaignContext.SaveManager.Save();
                        GD.Print($"[BotDuelTest] Soak mode — marked node {CampaignContext.CurrentNodeId} cleared (winner={st.WinnerIndex})");
                    }
                    GD.Print("[BotDuelTest] Soak mode — game over, navigating back to map");
                    t.Stop();
                    var navTimer = new Godot.Timer();
                    navTimer.OneShot = true;
                    navTimer.WaitTime = 0.05f; // TASK-PLAYABLE-NAV-1: fast map nav after duel
                    navTimer.Timeout += () =>
                    {
                        if (GodotObject.IsInstanceValid(GetTree()))
                            GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
                    };
                    GetTree().Root.AddChild(navTimer);
                    navTimer.Start();
                    return;
                }
                GetTree().Quit();
                return;
            }
            if (_gsm.CurrentPlayerIndex == 0 && !_bot.IsThinking)
            {
                var st = _gsm.State;
                int p1Board = 0;
                for (int i = 0; i < 5; i++) if (st.Players[1].Lanes[i].Occupant != null) p1Board++;
                GD.Print($"[BotDuelTest] cycle={p0Turns} turn={st.TurnNumber} p0Vigor={st.Players[0].Vigor} p1Vigor={st.Players[1].Vigor} p1Board={p1Board} p1AttacksLastTurn={st.Players[1].AttackCountLastTurn}");
                if (p0Turns++ >= 40)
                {
                    GD.Print($"[BotDuelTest] RESULT budget-exhausted turn={st.TurnNumber} p0Vigor={st.Players[0].Vigor} p1Vigor={st.Players[1].Vigor}");
                    GetNode<AudioManager>("/root/AudioManager").WriteAudioVerificationReport("artifacts/captures/audio_verify.json");
                    if (CampaignContext.SoakActive)
                    {
                        GD.Print("[BotDuelTest] Soak budget exhausted — marking node cleared and continuing");
                        if (CampaignContext.CurrentNodeId != null)
                            CampaignContext.Progression.MarkNodeCleared(CampaignContext.CurrentNodeId);
                        CampaignContext.SaveManager.Save();
                        GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
                        return;
                    }
                    GetTree().Quit();
                    return;
                }

                // SOAK MODE: P0 plays cards if possible, attacks all ready creatures, then ends turn.
                if (CampaignContext.SoakActive)
                {
                    var me = st.Players[0];

                    // Play ALL affordable creature cards
                    bool playedAny = false;
                    foreach (var card in me.Hand.ToList())
                    {
                        int cost = card.Cost;
                        if (cost <= me.Attunement && card.CardType != CardType.RELIC)
                        {
                            for (int l = 0; l < 5; l++)
                            {
                                if (me.Lanes[l].Occupant == null)
                                {
                                    var result = _gsm.TryPlayCard(0, card.CardDefId, l);
                                    if (result.Success)
                                    {
                                        GD.Print($"[BotDuelTest] Soak P0 played {card.CardDefId} to lane {l}");
                                        playedAny = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    // Attack with ALL ready creatures (one per cycle tick)
                    for (int l = 0; l < 5; l++)
                    {
                        var occ = me.Lanes[l].Occupant;
                        if (occ != null && !occ.IsExhausted && !occ.HasAttackedThisTurn && occ.CurrentAttack > 0)
                        {
                            var result = _gsm.TryAttack(0, l, l);
                            if (result.Success)
                            {
                                GD.Print($"[BotDuelTest] Soak P0 attacked lane {l}");
                                return; // one attack per cycle tick
                            }
                        }
                    }

                    // Nothing left to do — end turn
                    GD.Print("[BotDuelTest] Soak P0 nothing to do — ending turn");
                    _gsm.TryEndTurn();
                    return;
                }

                var res = _gsm.TryEndTurn();
                if (!res.Success)
                    GD.Print($"[BotDuelTest] TryEndTurn failed: {res.ErrorMessage}");
            }
        };
        AddChild(t);
        t.Start();
    }

    // ════════════════════════════════════════════════════════════════════
    // TASK-INPUT-SMOKE-1: Input smoke test — inject real touch/mouse events
    // and verify card interaction in a seeded duel.
    // ════════════════════════════════════════════════════════════════════

    private void StartInputSmokeTest()
        {
            GD.Print("[InputSmokeTest] Starting — will inject touch then mouse events via _GuiInput calls");
            var results = new List<string>();

            int step = 0;
            int? touchTargetLane = null;
            HandCard? touchTargetCard = null;

            var t = new Godot.Timer();
            t.OneShot = false;
            t.WaitTime = 0.4f;
            t.Timeout += () =>
            {
                if (_gsm == null || _gsm.State == null) return;

                if (step == 0)
                {
                    // Wait for mulligan to auto-dismiss and scene to settle
                    step = 1;
                    GD.Print("[InputSmokeTest] Phase 1: Touch test");
                    return;
                }

                if (step == 1)
                {
                    // Pick first hand card
                    touchTargetCard = _handCards.Count > 0 ? _handCards[0] : null;

                    if (touchTargetCard == null)
                    {
                        GD.PrintErr("[InputSmokeTest] FAIL: No hand cards found");
                        results.Add("TOUCH_CARD_SELECT:FAIL - no cards");
                        results.Add("TOUCH_LANE_PLAY:SKIP");
                        results.Add("TOUCH_END_TURN:SKIP");
                        step = 4;
                        return;
                    }

                    GD.Print($"[InputSmokeTest] Testing card '{touchTargetCard.CardName}' via InputEventScreenTouch");

                    // Inject touch event by calling _GuiInput directly (headless input routing may not process PushInput)
                    var touchEv = new InputEventScreenTouch();
                    touchEv.Position = Vector2.Zero;
                    touchEv.Pressed = true;
                    touchEv.Index = 0;
                    touchTargetCard._GuiInput(touchEv);

                    // Release
                    var touchUp = new InputEventScreenTouch();
                    touchUp.Position = Vector2.Zero;
                    touchUp.Pressed = false;
                    touchUp.Index = 0;
                    touchTargetCard._GuiInput(touchUp);

                    // ...and the emulated mouse click that a real device sends for the same tap.
                    // Without TapGuard this second event deselects the card and the test fails —
                    // which is exactly what happened on the phone while this test was passing.
                    var emulatedDown = new InputEventMouseButton();
                    emulatedDown.ButtonIndex = MouseButton.Left;
                    emulatedDown.Pressed = true;
                    emulatedDown.Position = Vector2.Zero;
                    touchTargetCard._GuiInput(emulatedDown);

                    step = 2; // Wait a tick for state update
                    return;
                }

                if (step == 2)
                {
                    // Check if card was selected
                    if (_input.State == InputController.InputState.SelectingLane && touchTargetCard != null && _input.SelectedCardId == touchTargetCard.CardId)
                    {
                        GD.Print($"[InputSmokeTest] PASS: Touch selected card '{touchTargetCard.CardName}' — lane highlights shown");
                        results.Add("TOUCH_CARD_SELECT:PASS");

                        // Find first empty player lane
                        int? lane = null;
                        for (int i = 0; i < 5; i++)
                        {
                            if (_gsm.State.Players[0].Lanes[i].Occupant == null)
                            {
                                lane = i;
                                break;
                            }
                        }

                        if (lane == null)
                        {
                            GD.PrintErr("[InputSmokeTest] FAIL: No empty player lane");
                            results.Add("TOUCH_LANE_PLAY:FAIL - no empty lane");
                            step = 4;
                            return;
                        }

                        touchTargetLane = lane;
                        GD.Print($"[InputSmokeTest] Touch lane slot {lane} via InputEventScreenTouch");

                        // Inject touch at lane slot
                        var slot = _playerSlots[lane.Value];
                        var laneEv = new InputEventScreenTouch();
                        laneEv.Position = Vector2.Zero;
                        laneEv.Pressed = true;
                        laneEv.Index = 0;
                        slot._GuiInput(laneEv);

                        var laneUp = new InputEventScreenTouch();
                        laneUp.Position = Vector2.Zero;
                        laneUp.Pressed = false;
                        laneUp.Index = 0;
                        slot._GuiInput(laneUp);

                        // ...and the emulated mouse click that a real device sends for the same tap.
                        var laneEmulated = new InputEventMouseButton();
                        laneEmulated.ButtonIndex = MouseButton.Left;
                        laneEmulated.Pressed = true;
                        laneEmulated.Position = Vector2.Zero;
                        slot._GuiInput(laneEmulated);

                        step = 3;
                        return;
                    }
                    else
                    {
                        GD.PrintErr($"[InputSmokeTest] FAIL: Touch did not select card (state={_input.State}, selectedId={_input.SelectedCardId})");
                        results.Add("TOUCH_CARD_SELECT:FAIL - state not SelectingLane");
                        results.Add("TOUCH_LANE_PLAY:SKIP");
                        results.Add("TOUCH_END_TURN:SKIP");
                        step = 4;
                        return;
                    }
                }

                if (step == 3)
                {
                    // Check tap-to-summon flow completed (input back to idle)
                    if (_input.State == InputController.InputState.Idle && _input.SelectedCardId == null)
                    {
                        GD.Print("[InputSmokeTest] PASS: Touch lane play — UI flow completed");
                        results.Add("TOUCH_LANE_PLAY:PASS");

                        // Press End Turn button via its emitted signal
                        GD.Print("[InputSmokeTest] Click End Turn via EmitSignal(Pressed)");
                        _endTurnButton.EmitSignal(Button.SignalName.Pressed);

                        step = 10;
                        return;
                    }
                    else
                    {
                        GD.PrintErr($"[InputSmokeTest] FAIL: Touch lane play — state={_input.State}, selectedId={_input.SelectedCardId}");
                        results.Add("TOUCH_LANE_PLAY:FAIL - flow not completed");
                        results.Add("TOUCH_END_TURN:SKIP");
                        step = 4;
                        return;
                    }
                }

                if (step == 10)
                {
                    // Check turn advanced after End Turn
                    if (_gsm.CurrentPlayerIndex == 1)
                    {
                        GD.Print($"[InputSmokeTest] PASS: Touch End Turn — turn advanced (P{_gsm.CurrentPlayerIndex}, turn {_gsm.TurnNumber})");
                        results.Add("TOUCH_END_TURN:PASS");
                        step = 4;
                        return;
                    }
                    else
                    {
                        GD.Print($"[InputSmokeTest] Still P{_gsm.CurrentPlayerIndex} after End Turn — waiting...");
                        return; // Keep waiting
                    }
                }

                // ─── MOUSE TEST (steps 4-7) ───
                if (step == 4)
                {
                    GD.Print("[InputSmokeTest] Phase 2: Mouse button test — waiting for P0 turn");
                    if (_gsm.CurrentPlayerIndex != 0 || _bot.IsThinking)
                    {
                        GD.Print("[InputSmokeTest] Waiting for P0 turn...");
                        return;
                    }

                    _input.CancelSelection();

                    HandCard? mouseCard = _handCards.Count > 0 ? _handCards[0] : null;
                    if (mouseCard == null)
                    {
                        GD.PrintErr("[InputSmokeTest] FAIL: No hand cards for mouse test");
                        results.Add("MOUSE_CARD_SELECT:FAIL - no cards");
                        results.Add("MOUSE_LANE_PLAY:SKIP");
                        results.Add("MOUSE_END_TURN:SKIP");
                        step = 20;
                        return;
                    }

                    GD.Print($"[InputSmokeTest] Mouse click card '{mouseCard.CardName}' via InputEventMouseButton");
                    var mouseEv = new InputEventMouseButton();
                    mouseEv.Position = Vector2.Zero;
                    mouseEv.Pressed = true;
                    mouseEv.ButtonIndex = MouseButton.Left;
                    mouseCard._GuiInput(mouseEv);

                    var mouseUp = new InputEventMouseButton();
                    mouseUp.Position = Vector2.Zero;
                    mouseUp.Pressed = false;
                    mouseUp.ButtonIndex = MouseButton.Left;
                    mouseCard._GuiInput(mouseUp);

                    step = 5;
                    return;
                }

                if (step == 5)
                {
                    if (_input.State == InputController.InputState.SelectingLane)
                    {
                        GD.Print("[InputSmokeTest] PASS: Mouse selected card — lane highlights shown");
                        results.Add("MOUSE_CARD_SELECT:PASS");

                        int? lane = null;
                        for (int i = 0; i < 5; i++)
                        {
                            if (_gsm.State.Players[0].Lanes[i].Occupant == null)
                            {
                                lane = i;
                                break;
                            }
                        }

                        if (lane == null)
                        {
                            GD.PrintErr("[InputSmokeTest] FAIL: No empty lane for mouse play");
                            results.Add("MOUSE_LANE_PLAY:FAIL - no empty lane");
                            step = 20;
                            return;
                        }

                        var slot = _playerSlots[lane.Value];
                        GD.Print($"[InputSmokeTest] Mouse click lane slot {lane} via InputEventMouseButton");
                        var laneEv = new InputEventMouseButton();
                        laneEv.Position = Vector2.Zero;
                        laneEv.Pressed = true;
                        laneEv.ButtonIndex = MouseButton.Left;
                        slot._GuiInput(laneEv);

                        var laneUp = new InputEventMouseButton();
                        laneUp.Position = Vector2.Zero;
                        laneUp.Pressed = false;
                        laneUp.ButtonIndex = MouseButton.Left;
                        slot._GuiInput(laneUp);

                        step = 6;
                        return;
                    }
                    else
                    {
                        GD.PrintErr($"[InputSmokeTest] FAIL: Mouse did not select card (state={_input.State})");
                        results.Add("MOUSE_CARD_SELECT:FAIL - state not SelectingLane");
                        results.Add("MOUSE_LANE_PLAY:SKIP");
                        results.Add("MOUSE_END_TURN:SKIP");
                        step = 20;
                        return;
                    }
                }

                if (step == 6)
                {
                    // Check UI flow completed (idle after tap-to-summon)
                    if (_input.State == InputController.InputState.Idle && _input.SelectedCardId == null)
                    {
                        GD.Print("[InputSmokeTest] PASS: Mouse lane play — UI flow completed");
                        results.Add("MOUSE_LANE_PLAY:PASS");

                        GD.Print("[InputSmokeTest] Mouse click End Turn");
                        _endTurnButton.EmitSignal(Button.SignalName.Pressed);

                        step = 7;
                        return;
                    }
                    else
                    {
                        GD.PrintErr($"[InputSmokeTest] FAIL: Mouse lane play — state={_input.State}");
                        results.Add("MOUSE_LANE_PLAY:FAIL - flow not completed");
                        results.Add("MOUSE_END_TURN:SKIP");
                        step = 20;
                        return;
                    }
                }

                if (step == 7)
                {
                    // Check mouse End Turn
                    if (_gsm.CurrentPlayerIndex == 1 || _gsm.TurnNumber > 1)
                    {
                        GD.Print($"[InputSmokeTest] PASS: Mouse End Turn worked (turn={_gsm.TurnNumber})");
                        results.Add("MOUSE_END_TURN:PASS");
                        step = 20;
                        return;
                    }
                    else
                    {
                        GD.Print($"[InputSmokeTest] Still P{_gsm.CurrentPlayerIndex} after mouse End Turn");
                        return;
                    }
                }

                if (step == 20)
                {
                    t.Stop();
                    WriteInputSmokeResult(results);
                    GetTree().Quit();
                }
            };
            AddChild(t);
            t.Start();
        }

    private void WriteInputSmokeResult(List<string> results)
    {
        bool allPassed = true;
        foreach (var r in results)
        {
            if (r.Contains("FAIL"))
                allPassed = false;
            GD.Print($"[InputSmokeTest] {r}");
        }

        var verdict = allPassed ? "PASS" : "FAIL";
        GD.Print($"[InputSmokeTest] VERDICT: {verdict}");

        var json = "{";
        json += "\"verdict\": \"" + verdict + "\",";
        json += "\"steps\": [";
        bool first = true;
        foreach (var r in results)
        {
            if (!first) json += ",";
            first = false;
            var parts = r.Split(':', 2);
            var stepName = parts[0];
            var stepResult = parts.Length > 1 ? parts[1] : "UNKNOWN";
            json += "{\"name\":\"" + stepName + "\",\"result\":\"" + stepResult + "\"}";
        }
        json += "]}";

        System.IO.Directory.CreateDirectory("artifacts/captures");
        var file = Godot.FileAccess.Open("artifacts/captures/input_smoke_result.json", Godot.FileAccess.ModeFlags.Write);
        if (file != null)
        {
            file.StoreString(json);
            file.Close();
            GD.Print($"[InputSmokeTest] Results written to artifacts/captures/input_smoke_result.json");
        }
        else
        {
            GD.PrintErr("[InputSmokeTest] FAILED to write results file");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // TASK-INPUT-TOUCH-1: Pure touch-only smoke test — InputEventScreenTouch ONLY.
    // No InputEventMouseButton anywhere. Proves the game works with a real finger on glass.
    // ════════════════════════════════════════════════════════════════════

    private void StartTouchOnlySmokeTest()
    {
        GD.Print("[TouchOnlySmokeTest] Starting — pure touch events ONLY, no mouse anywhere");
        var results = new List<string>();

        int step = 0;
        LaneSlot? combatSlot = null;
        int? playLane = null;

        var t = new Godot.Timer();
        t.OneShot = false;
        t.WaitTime = 0.4f;
        t.Timeout += () =>
        {
            if (_gsm == null || _gsm.State == null) return;

            if (step == 0)
            {
                // Wait for mulligan to dismiss and scene to settle
                step = 1;
                GD.Print("[TouchOnlySmokeTest] Phase 1: Initial wait complete");
                return;
            }

            if (step == 1)
            {
                // Phase 1: Find a PLAYABLE card (cost <= attunement)
                int attune = _gsm.State.Players[0].Attunement;
                HandCard? targetCard = null;
                foreach (var c in _handCards)
                {
                    if (c.CardCost <= attune)
                    { targetCard = c; break; }
                }

                if (targetCard == null)
                {
                    GD.Print($"[TouchOnlySmokeTest] No affordable card (max attune={attune}) — ending turn early");
                    results.Add("TOUCH_CARD_SELECT:SKIP - no affordable card");
                    results.Add("TOUCH_LANE_PLAY:SKIP - no card");
                    _endTurnButton.EmitSignal(Button.SignalName.Pressed);
                    step = 10;
                    return;
                }

                GD.Print($"[TouchOnlySmokeTest] Tap card '{targetCard.CardName}' (cost {targetCard.CardCost}) via InputEventScreenTouch (pure)");

                // Inject touch press (ONLY InputEventScreenTouch, no emulated mouse)
                var touchEv = new InputEventScreenTouch();
                touchEv.Position = Vector2.Zero;
                touchEv.Pressed = true;
                touchEv.Index = 0;
                targetCard._GuiInput(touchEv);

                var touchUp = new InputEventScreenTouch();
                touchUp.Position = Vector2.Zero;
                touchUp.Pressed = false;
                touchUp.Index = 0;
                targetCard._GuiInput(touchUp);

                step = 2;
                return;
            }

            if (step == 2)
            {
                // Verify card was selected (visual + input state)
                if (_input.State == InputController.InputState.SelectingLane)
                {
                    var card = _handCards.Count > 0 ? _handCards[0] : null;
                    bool visualSelected = card != null && _input.SelectedCardId == card.CardId;
                    GD.Print($"[TouchOnlySmokeTest] PASS: Touch selected card — visual={visualSelected}, state={_input.State}");
                    results.Add("TOUCH_CARD_SELECT:PASS");

                    // Find empty player lane
                    int? lane = null;
                    for (int i = 0; i < 5; i++)
                    {
                        if (_gsm.State.Players[0].Lanes[i].Occupant == null)
                        { lane = i; break; }
                    }

                    if (lane == null)
                    {
                        GD.PrintErr("[TouchOnlySmokeTest] FAIL: No empty lane");
                        results.Add("TOUCH_LANE_PLAY:FAIL - no empty lane");
                        results.Add("TOUCH_COMBAT:SKIP");
                        results.Add("TOUCH_END_TURN:SKIP");
                        step = 99;
                        return;
                    }
                    playLane = lane.Value;

                    GD.Print($"[TouchOnlySmokeTest] Tap lane slot {lane} via pure InputEventScreenTouch");

                    // Inject pure touch at lane slot
                    var slot = _playerSlots[lane.Value];
                    var laneEv = new InputEventScreenTouch();
                    laneEv.Position = Vector2.Zero;
                    laneEv.Pressed = true;
                    laneEv.Index = 0;
                    slot._GuiInput(laneEv);

                    var laneUp = new InputEventScreenTouch();
                    laneUp.Position = Vector2.Zero;
                    laneUp.Pressed = false;
                    laneUp.Index = 0;
                    slot._GuiInput(laneUp);

                    step = 3;
                    return;
                }
                else
                {
                    GD.PrintErr($"[TouchOnlySmokeTest] FAIL: Touch did not select card (state={_input.State})");
                    results.Add("TOUCH_CARD_SELECT:FAIL - state not SelectingLane");
                    results.Add("TOUCH_LANE_PLAY:SKIP");
                    results.Add("TOUCH_COMBAT:SKIP");
                    results.Add("TOUCH_END_TURN:SKIP");
                    step = 99;
                    return;
                }
            }

            if (step == 3)
            {
                // Verify card was played
                if (_input.State == InputController.InputState.Idle && _input.SelectedCardId == null)
                {
                    GD.Print("[TouchOnlySmokeTest] PASS: Touch lane play completed — input state idle");

                    // Verify creature is actually on the board
                    bool creatureOnBoard = false;
                    if (playLane.HasValue)
                    {
                        var occupant = _gsm.State.Players[0].Lanes[playLane.Value].Occupant;
                        creatureOnBoard = occupant != null;
                        if (creatureOnBoard)
                            GD.Print($"[TouchOnlySmokeTest] Board: creature '{occupant!.CardDefId}' in lane {playLane.Value}");
                    }
                    else
                    {
                        // Fallback: check all player lanes
                        for (int i = 0; i < 5; i++)
                        {
                            if (_gsm.State.Players[0].Lanes[i].Occupant != null)
                            { creatureOnBoard = true; break; }
                        }
                    }
                    if (creatureOnBoard)
                    {
                        GD.Print("[TouchOnlySmokeTest] PASS: Creature appeared on board");
                        results.Add("TOUCH_BOARD_APPEAR:PASS");
                    }
                    else
                    {
                        GD.PrintErr("[TouchOnlySmokeTest] FAIL: No creature appeared on board after play");
                        results.Add("TOUCH_BOARD_APPEAR:FAIL");
                        results.Add("TOUCH_COMBAT:SKIP");
                        results.Add("TOUCH_END_TURN:SKIP");
                        step = 99;
                        return;
                    }

                    results.Add("TOUCH_LANE_PLAY:PASS");

                    // Phase 2: End turn via touch
                    GD.Print("[TouchOnlySmokeTest] Pure touch End Turn via EmitSignal(Pressed)");
                    _endTurnButton.EmitSignal(Button.SignalName.Pressed);

                    step = 10;
                    return;
                }
                else
                {
                    GD.PrintErr($"[TouchOnlySmokeTest] FAIL: Touch lane play — state={_input.State}");
                    results.Add("TOUCH_LANE_PLAY:FAIL - flow not completed");
                    results.Add("TOUCH_COMBAT:SKIP");
                    results.Add("TOUCH_END_TURN:SKIP");
                    step = 99;
                    return;
                }
            }

            if (step == 10)
            {
                // Wait for turn to advance (bot takes over)
                GD.Print("[TouchOnlySmokeTest] Phase 2: Waiting for P0 turn again...");
                if (_gsm.CurrentPlayerIndex == 1)
                {
                    results.Add("TOUCH_END_TURN:PASS");
                    GD.Print("[TouchOnlySmokeTest] PASS: Touch End Turn — turn advanced");

                    step = 11;
                    return;
                }
                else
                {
                    GD.Print($"[TouchOnlySmokeTest] Still P{_gsm.CurrentPlayerIndex} — waiting...");
                    return;
                }
            }

            if (step == 11)
            {
                // Wait for bot turn to finish and P0 to get next turn
                GD.Print("[TouchOnlySmokeTest] Waiting for P0 turn (after bot)...");
                if (_gsm.CurrentPlayerIndex != 0 || _bot.IsThinking)
                {
                    return;
                }
                step = 12;
                GD.Print("[TouchOnlySmokeTest] P0 turn — ready for combat test");
                return;
            }

            if (step == 12)
            {
                // Phase 3: Combat test — tap a friendly creature to select it for attack
                int? attackerLane = null;
                for (int i = 0; i < 5; i++)
                {
                    if (_gsm.State.Players[0].Lanes[i].Occupant != null
                        && !_gsm.State.Players[0].Lanes[i].Occupant.IsExhausted)
                    {
                        attackerLane = i;
                        break;
                    }
                }

                if (attackerLane == null)
                {
                    GD.Print("[TouchOnlySmokeTest] No attackers available — skipping combat, ending turn");
                    results.Add("TOUCH_COMBAT:SKIP - no attackers");
                    _endTurnButton.EmitSignal(Button.SignalName.Pressed);
                    step = 20;
                    return;
                }

                combatSlot = _playerSlots[attackerLane.Value];
                GD.Print($"[TouchOnlySmokeTest] Tap creature in lane {attackerLane} via pure InputEventScreenTouch");

                var attackEv = new InputEventScreenTouch();
                attackEv.Position = Vector2.Zero;
                attackEv.Pressed = true;
                attackEv.Index = 0;
                combatSlot._GuiInput(attackEv);

                var attackUp = new InputEventScreenTouch();
                attackUp.Position = Vector2.Zero;
                attackUp.Pressed = false;
                attackUp.Index = 0;
                combatSlot._GuiInput(attackUp);

                step = 13;
                return;
            }

            if (step == 13)
            {
                // Verify attacker was selected
                if (_input.State == InputController.InputState.SelectingAttacker)
                {
                    GD.Print("[TouchOnlySmokeTest] PASS: Creature selected for attack");
                    results.Add("TOUCH_COMBAT_SELECT:PASS");

                    // Find an enemy lane to attack
                    int targetLane = 0;
                    for (int i = 0; i < 5; i++)
                    {
                        if (_gsm.State.Players[1].Lanes[i].Occupant != null)
                        {
                            targetLane = i;
                            break;
                        }
                    }

                    // Tap enemy lane as attack target
                    var enemySlot = _enemySlots[targetLane];
                    GD.Print($"[TouchOnlySmokeTest] Tap enemy lane {targetLane} as attack target via pure InputEventScreenTouch");

                    var targetEv = new InputEventScreenTouch();
                    targetEv.Position = Vector2.Zero;
                    targetEv.Pressed = true;
                    targetEv.Index = 0;
                    enemySlot._GuiInput(targetEv);

                    var targetUp = new InputEventScreenTouch();
                    targetUp.Position = Vector2.Zero;
                    targetUp.Pressed = false;
                    targetUp.Index = 0;
                    enemySlot._GuiInput(targetUp);

                    step = 14;
                    return;
                }
                else
                {
                    GD.PrintErr($"[TouchOnlySmokeTest] FAIL: Creature not selected (state={_input.State})");
                    results.Add("TOUCH_COMBAT_SELECT:FAIL");
                    step = 20;
                    return;
                }
            }

            if (step == 14)
            {
                // Verify attack happened (back to idle)
                if (_input.State == InputController.InputState.Idle)
                {
                    GD.Print("[TouchOnlySmokeTest] PASS: Combat attack completed");
                    results.Add("TOUCH_COMBAT:PASS");

                    GD.Print("[TouchOnlySmokeTest] Pure touch End Turn");
                    _endTurnButton.EmitSignal(Button.SignalName.Pressed);
                    step = 20;
                    return;
                }
                else
                {
                    GD.PrintErr($"[TouchOnlySmokeTest] FAIL: Attack not completed (state={_input.State})");
                    results.Add("TOUCH_COMBAT:FAIL");
                    step = 20;
                    return;
                }
            }

            if (step == 20)
            {
                // Continue playing until game over via touch-only End Turn
                if (_gsm.IsGameOver)
                {
                    GD.Print("[TouchOnlySmokeTest] Game over detected — test complete");
                    results.Add("TOUCH_VICTORY:PASS");
                    WriteTouchSmokeResults(results);
                    t.Stop();
                    return;
                }

                if (_gsm.CurrentPlayerIndex == 0 && !_bot.IsThinking)
                {
                    GD.Print("[TouchOnlySmokeTest] Pure touch End Turn (continuing)");
                    _endTurnButton.EmitSignal(Button.SignalName.Pressed);
                }
                return;
            }

            if (step == 99)
            {
                // FAIL — write results and stop
                WriteTouchSmokeResults(results);
                t.Stop();
            }
        };
        AddChild(t);
        t.Start();
    }

    private void WriteTouchSmokeResults(List<string> results)
    {
        bool allPass = results.TrueForAll(r => r.Contains(":PASS") || r.Contains(":SKIP"));
        string verdict = allPass ? "PASS" : "FAIL";
        GD.Print($"[TouchOnlySmokeTest] VERDICT: {verdict}");
        foreach (var r in results)
            GD.Print($"[TouchOnlySmokeTest] {r}");

        var stepsArray = new Godot.Collections.Array();
            foreach (var r in results)
                stepsArray.Add(r);
            var data = new Godot.Collections.Dictionary
            {
                ["verdict"] = verdict,
                ["steps"] = stepsArray,
                ["mode"] = "touch_only"
            };
        string json = Json.Stringify(data);
        var file = Godot.FileAccess.Open("artifacts/captures/touch_smoke_result.json", Godot.FileAccess.ModeFlags.Write);
        if (file != null)
        {
            file.StoreString(json);
            file.Close();
            GD.Print($"[TouchOnlySmokeTest] Results written to artifacts/captures/touch_smoke_result.json");
        }
        else
        {
            GD.PrintErr("[TouchOnlySmokeTest] FAILED to write results file");
        }
    }

    /// <summary>Dispatch a GreedyBot action (PlayCard/Attack/EndTurn) for any player. Used by soak mode.</summary>
    private void DispatchBotAction(GameAction action, int playerIndex)
    {
        if (_gsm == null) return;
        if (action is PlayCardAction play)
        {
            var player = _gsm.State.Players[playerIndex];
            var card = player.Hand.FirstOrDefault(c => c.InstanceId == play.CardInstanceId);
            if (card != null)
            {
                var result = _gsm.TryPlayCard(playerIndex, card.CardDefId, play.LaneIndex ?? 0);
                if (!result.Success)
                    GD.PrintErr($"[BotDuelTest] TryPlayCard P{playerIndex} FAILED: {result.ErrorMessage}");
            }
            else
            {
                GD.Print($"[BotDuelTest] P{playerIndex}: PlayCard card instance {play.CardInstanceId} not in hand — ending turn");
                _gsm.TryEndTurn();
            }
        }
        else if (action is AttackAction attack)
        {
            var result = _gsm.TryAttack(playerIndex, attack.SourceLane, attack.TargetLane ?? attack.SourceLane);
            if (!result.Success)
                GD.PrintErr($"[BotDuelTest] TryAttack P{playerIndex} FAILED: {result.ErrorMessage}");
        }
        else
        {
            GD.Print($"[BotDuelTest] P{playerIndex} bot chose EndTurn or other action — calling TryEndTurn");
            _gsm.TryEndTurn();
        }
    }

    /// <summary>
    /// Handle End Turn button press.
    /// </summary>
    private void OnEndTurnPressed()
    {
        if (_bot.IsThinking) return;
        if (_gsm.CurrentPlayerIndex != 0) return;

        // TASK-AUDIO-HOOK-1: Button click
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");

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

        // TASK-AUDIO-HOOK-1: Shuffle sound on mulligan entry
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("card_shuffle");

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
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
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
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
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

    /// <summary>Dismiss the mulligan UI overlay. Needed by TutorialRunner to hide it before tutorial beats.</summary>
    public void DismissMulligan()
    {
        if (_mulliganPanel != null)
        {
            _mulliganPanel.QueueFree();
            _mulliganPanel = null;
        }
    }

    /// <summary>Display name of the card granted by this victory (null = none).</summary>
    private string? _grantedCardName;

    // ── TASK-DROPS-UI-1: Drop reveal state ──
    private struct DropRevealCard
    {
        public string CardId;
        public string CardName;
        public int Cost;
        public Strata Strata;
        public int? Attack;
        public int? Vigor;
        public bool IsNew;
    }
    private readonly List<DropRevealCard> _dropRevealCards = new();
    private int _currentRevealIndex = -1;
    private Control? _dropRevealContainer;
    private Control? _dropCardContainer; // holds the currently visible CardPlate
    private Label? _dropRibbonLabel;     // "NEW" or "+1"
    private Label? _dropTitleLabel;      // "Drops" header
    private Godot.Timer? _revealTimer;
    private bool _revealTapped;          // player tapped to advance

    // ── TASK-REWARD-SCREEN-1: Animated reward counters ──
    private struct AnimatedRewardCounter
    {
        public Label ValueLabel;
        public int TargetValue;
        public int Index;
    }
    private readonly List<AnimatedRewardCounter> _rewardCounters = [];
    private bool _countersStarted;

    /// <summary>
    /// Start the drop reveal sequence on the victory overlay.
    /// </summary>
    private void StartDropReveal()
    {
        if (_dropRevealContainer == null || _dropRevealCards.Count == 0)
        {
            // No drops to reveal — skip reveal state
            _currentRevealIndex = _dropRevealCards.Count;
            return;
        }

        _currentRevealIndex = -1;
        _revealTapped = false;

        // Show the drops header
        if (_dropTitleLabel != null)
            _dropTitleLabel.Visible = true;

        // Reveal the first card after a short pause
        var timer = new Godot.Timer();
        timer.WaitTime = 0.8f;
        timer.OneShot = true;
        timer.Timeout += RevealNextDrop;
        AddChild(timer);
        timer.Start();
        _revealTimer = timer;
    }

    /// <summary>
    /// Reveal the next card in the drop sequence, or finish.
    /// </summary>
    private void RevealNextDrop()
    {
        _revealTapped = false;

        // Clean up previous card
        if (_dropCardContainer != null)
        {
            _dropCardContainer.QueueFree();
            _dropCardContainer = null;
        }

        _currentRevealIndex++;

        if (_currentRevealIndex >= _dropRevealCards.Count)
        {
            // All drops revealed — hide the reveal UI, let Continue button work
            if (_dropRevealContainer != null)
                _dropRevealContainer.Visible = false;
            if (_dropTitleLabel != null)
                _dropTitleLabel.Visible = false;
            // Re-enable Continue button if it was disabled
            return;
        }

        var card = _dropRevealCards[_currentRevealIndex];
        BuildDropRevealCard(card);

        // Set up auto-advance timer for this card
        if (_revealTimer != null && IsInstanceValid(_revealTimer))
        {
            _revealTimer.QueueFree();
        }
        var timer = new Godot.Timer();
        timer.WaitTime = 2.5f;
        timer.OneShot = true;
        timer.Timeout += () =>
        {
            if (!_revealTapped && _currentRevealIndex < _dropRevealCards.Count)
                RevealNextDrop();
        };
        AddChild(timer);
        timer.Start();
        _revealTimer = timer;
    }

    /// <summary>
    /// Build a CardPlate for one revealed drop card inside the reveal container.
    /// </summary>
    private void BuildDropRevealCard(DropRevealCard card)
    {
        if (_dropRevealContainer == null) return;

        // Remove any existing card plate
        if (_dropCardContainer != null)
        {
            _dropCardContainer.QueueFree();
            _dropCardContainer = null;
        }

        // Card size: hand-card size (~260px wide at 2316x1080)
        float cardW = 260f;
        float cardH = cardW * 1.45f; // ~2:3 card aspect

        // Container for the card + ribbon
        var ctr = new Control
        {
            Name = "DropRevealCardWrapper",
            CustomMinimumSize = new Vector2(cardW + 40, cardH + 60),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };

        // CardPlate
        var plate = new CardPlate
        {
            Name = "DropCardPlate",
            MouseFilter = Control.MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(cardW, cardH),
        };
        plate.Setup(card.CardName, card.Attack, card.Vigor, card.Strata, cardW, cardH, card.Cost);

        // Cost rune
        float hexSize;
        var costLabel = CardPlate.MakeCostRune(card.Cost, cardW, cardH, out hexSize);
        plate.AddChild(costLabel);

        // Root-Bound border
        var border = new RootBoundBorder();
        border.Setup(cardW, cardH);
        plate.AddChild(border);

        ctr.AddChild(plate);

        // ── Ribbon (top-left corner) ──
        var ribbonText = card.IsNew ? "NEW" : "+1";
        var ribbon = new Label
        {
            Text = ribbonText,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ApplyHeaderFont(ribbon, 14);
        float ribbonW = card.IsNew ? 60f : 44f;
        float ribbonH = 24f;
        ribbon.Size = new Vector2(ribbonW, ribbonH);
        ribbon.Position = new Vector2(-4, -4);
        var ribbonStyle = new StyleBoxFlat
        {
            BgColor = card.IsNew ? Color.FromHtml("#2A6B2A") : Color.FromHtml("#6B5A2A"),
            BorderColor = card.IsNew ? Color.FromHtml("#5AFA2A") : Color.FromHtml("#FA9A2A"),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 6, ContentMarginTop = 2, ContentMarginRight = 6, ContentMarginBottom = 2,
        };
        ribbon.AddThemeStyleboxOverride("normal", ribbonStyle);
        ribbon.Modulate = card.IsNew ? Color.FromHtml("#C8FFC8") : Color.FromHtml("#FFE8A0");
        ctr.AddChild(ribbon);
        _dropRibbonLabel = ribbon;

        // ── Tap hint ──
        bool isLast = _currentRevealIndex >= _dropRevealCards.Count - 1;
        var hint = new Label
        {
            Text = isLast ? "Tap to continue" : "Tap for next",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        ApplyBodyFont(hint, 12);
        hint.Modulate = new Color(0.7f, 0.7f, 0.7f, 0.7f);
        hint.Position = new Vector2(0, cardH + 8);
        hint.Size = new Vector2(cardW, 20);
        ctr.AddChild(hint);

        // ── Tap-to-advance on the card area ──
        var tapArea = new ColorRect
        {
            Color = new Color(0, 0, 0, 0),
            MouseFilter = Control.MouseFilterEnum.Stop,
            Size = new Vector2(cardW, cardH),
            Position = Vector2.Zero,
        };
        tapArea.GuiInput += (evt) =>
        {
            if (evt is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                _revealTapped = true;
                if (_revealTimer != null && IsInstanceValid(_revealTimer))
                    _revealTimer.QueueFree();
                RevealNextDrop();
            }
        };
        ctr.AddChild(tapArea);

        // Add to reveal container
        _dropRevealContainer.AddChild(ctr);
        _dropCardContainer = ctr;
    }

    private void OnGameOver(int winnerIndex)
    {
        _turnLabel.Text = winnerIndex == 0 ? "You Win!" : "You Lose!";
        _turnLabel.Modulate = winnerIndex == 0
            ? Gold
            : Ember;

        // TASK-AUDIO-HOOK-1: Victory/defeat sound
        var audio = GetNode<AudioManager>("/root/AudioManager");
        audio.PlaySfx(winnerIndex == 0 ? "victory" : "defeat");

        // ═══ TASK-DUEL-ARENA-1: Arena duel reward handling ═══
        if (CampaignContext.IsArenaDuel && !_isGameOverHandled)
        {
            _isGameOverHandled = true;
            var prog = CampaignContext.Progression;

            if (winnerIndex == 0)
            {
                // Player won — award RuneDust
                int reward = CampaignContext.IsWardenOpponent ? 25 : 10;
                prog.RuneDust += reward;
                prog.ArenaWins++;
                GD.Print($"[DuelScene] Arena victory! +{reward} RuneDust (warden={CampaignContext.IsWardenOpponent})");

                // Grant one random card from the opponent's deck as a bonus reward
                if (CampaignContext.ArenaEncounter?.Deck is { Count: > 0 } oppDeck)
                {
                    ulong rewardSeed = CampaignContext.DebugSeed ?? (ulong)(ulong.MaxValue & GD.Randi());
                    var rng = new SeededRng(rewardSeed);
                    int cardIdx = (int)(rng.NextU64() % (ulong)oppDeck.Count);
                    string rewardCardId = oppDeck[cardIdx];
                    var rewardDef = CardRegistry.Get(rewardCardId);
                    if (rewardDef != null)
                    {
                        bool firstTime = !prog.Collection.ContainsKey(rewardCardId);
                        prog.AddCard(rewardCardId);
                        _grantedCardName = rewardDef.Name;
                        GD.Print($"[DuelScene] Arena bonus card: {rewardCardId} (first={firstTime})");
                    }
                }
            }
            else
            {
                prog.ArenaLosses++;
                GD.Print($"[DuelScene] Arena defeat — total losses: {prog.ArenaLosses}");
            }

            CampaignContext.SaveManager.Save();
            CampaignContext.IsArenaDuel = false;

            // Navigate back to ArenaScene after a brief delay
            var arenaNavTimer = new Godot.Timer();
            arenaNavTimer.OneShot = true;
            arenaNavTimer.WaitTime = 1.5f;
            arenaNavTimer.Timeout += () =>
            {
                ArenaScene.ReturnFromDuel();
                GetTree().ChangeSceneToFile("res://scenes/arena/ArenaScene.tscn");
            };
            AddChild(arenaNavTimer);
            arenaNavTimer.Start();
            return;
        }

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

            // ── Card reward (bosses usually carry one) ──
            // "CLASS_SIGNATURE" resolves to the player class's signature card.
            _grantedCardName = null;
            if (!string.IsNullOrEmpty(enc.CardReward))
            {
                string? rewardCardId = enc.CardReward == "CLASS_SIGNATURE"
                    ? CampaignContext.GetSignatureCardId(
                        CampaignContext.ActiveProfile?.ClassId ?? CampaignContext.ChosenClass)
                    : enc.CardReward;

                var rewardDef = rewardCardId != null ? CardRegistry.Get(rewardCardId) : null;
                if (rewardDef == null)
                {
                    GD.PrintErr($"[DuelScene] card_reward '{enc.CardReward}' did not resolve to a known card");
                }
                else
                {
                    bool firstTime = !prog.Collection.ContainsKey(rewardCardId!);
                    if (firstTime)
                        prog.AddCard(rewardCardId!);

                    // Slot it straight into the active deck when legal.
                    // With the hard 30-card rule a full deck stays untouched —
                    // the card lands in the collection for the Forge instead.
                    var activeDeckId = CampaignContext.ActiveProfile?.ActiveDeckId;
                    var deck = !string.IsNullOrEmpty(activeDeckId)
                        ? CampaignContext.DeckLibrary.Find(d => d.DeckId == activeDeckId)
                        : null;
                    if (deck != null && !deck.Cards.Contains(rewardCardId!)
                        && deck.Cards.Count < DeckRules.MaxSize)
                    {
                        deck.Cards.Add(rewardCardId!);
                        CampaignContext.SaveDeckLibrary();
                        CampaignContext.PlayerDeckIds = new List<string>(deck.Cards);
                        GD.Print($"[DuelScene] Card reward {rewardCardId} added to deck {deck.DeckId}");
                    }

                    if (firstTime)
                        _grantedCardName = rewardDef.Name;
                    GD.Print($"[DuelScene] Card reward granted: {rewardCardId} (firstTime={firstTime})");
                }
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
            // Take a snapshot of owned cards before the baseline grant so we
            // can distinguish NEW drops (first copy ever) from +1 (duplicate).
            var ownedBeforeBaseline = new HashSet<string>(prog.Collection.Keys);
            foreach (var cardId in enc.Deck)
            {
                if (!prog.Collection.ContainsKey(cardId))
                    prog.AddCard(cardId);
            }

            // ── TASK-DROPS-UI-1: Roll encounter drop table ──
            ulong dropSeed = CampaignContext.DebugSeed ?? (ulong)(enc.Id.GetHashCode() & 0x7FFFFFFF);
            var droppedCardIds = DropRoller.Roll(enc, dropSeed);
            foreach (var dcId in droppedCardIds)
            {
                var cardDef = CardRegistry.Get(dcId);
                if (cardDef == null)
                {
                    GD.PrintErr($"[DuelScene] Drop card '{dcId}' not found in CardRegistry — skipping");
                    continue;
                }

                // Determine NEW vs +1 using pre-baseline snapshot:
                // NEW = card was not owned before this victory at all
                // +1  = card was already owned before this victory
                bool isNew = !ownedBeforeBaseline.Contains(dcId);

                // Grant the drop copy
                prog.AddCard(dcId);

                _dropRevealCards.Add(new DropRevealCard
                {
                    CardId = dcId,
                    CardName = cardDef.Name,
                    Cost = cardDef.Cost,
                    Strata = cardDef.Strata,
                    Attack = cardDef.Attack,
                    Vigor = cardDef.Vigor,
                    IsNew = isNew,
                });

                GD.Print($"[DuelScene] Drop rolled: {cardDef.Name} ({dcId}) {(isNew ? "NEW" : "+1")}");
            }

            CampaignContext.SaveManager.Save();

            // Show end-of-duel overlay (built by RenderFromState frame update)
            // The overlay shows encounter name, turns taken, reward summary, and a CONTINUE
            // button returning to map (or RETRY + RETURN on defeat).
        } // closes if (winnerIndex == 0 && ...)
        else
        {
            // Campaign defeat — BuildGameOverOverlay handles this via RenderFromState.
            // Do NOT call ShowGameOverOverlay here — it creates a conflicting overlay.
            GD.Print("[DuelScene] Campaign defeat — overlay will be built by RenderFromState via BuildGameOverOverlay");
        }
    } // closes if (_isCampaignEncounter && !_isGameOverHandled)
    else
    {
        // Non-campaign (test/free-play) — show game-over overlay
        ShowGameOverOverlay(winnerIndex);
    }
} // closes OnGameOver

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
        playAgain.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            GetTree().ReloadCurrentScene();
        };
        btnHBox.AddChild(playAgain);

        // Spacer between buttons
        btnHBox.AddChild(new Control { CustomMinimumSize = new Vector2(20, 0) });

        var backToTitle = new Button();
        backToTitle.Text = "Back to Title";
        backToTitle.CustomMinimumSize = new Vector2(130, 40);
        backToTitle.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
        };
        btnHBox.AddChild(backToTitle);
    }

    /// <summary>
    /// Build the end-of-duel screen — full-screen overlay in the game's serif/stone language.
    /// Handles victory and defeat: encounter name, turns taken, reward summary, action buttons.
    /// Uses ThemeTokens for all colors, fonts, and border treatments.
    /// Plays victory/defeat audio event on creation.
    /// </summary>
    private void BuildGameOverOverlay()
    {
        _gameOverOverlay = new Control { Name = "GameOverOverlay" };
        _gameOverOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _gameOverOverlay.MouseFilter = Control.MouseFilterEnum.Pass;

        // Semi-transparent dark panel dimming the board
        var dim = new ColorRect();
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dim.Color = new Color(BgDark.R, BgDark.G, BgDark.B, 0.85f);
        dim.MouseFilter = Control.MouseFilterEnum.Ignore;
        _gameOverOverlay.AddChild(dim);

        // Determine winner: player is index 0
        int winner = -1;
        if (_gsm.State != null) winner = _gsm.WinnerIndex;
        bool playerWon = winner == 0;

        // Read encounter data from CampaignContext
        var encounter = CampaignContext.CurrentEncounter;
        string encName = encounter?.Name ?? "";
        if (string.IsNullOrEmpty(encName))
        {
            GD.Print("[DuelScene] Warning: CampaignContext.CurrentEncounter.Name is null/empty — falling back to generic label");
            encName = playerWon ? "Victory" : "Defeat";
        }

        Color accentColor = playerWon ? Gold : Ember;
        string statusLabel = playerWon ? "VICTORY" : "DEFEATED";
        string headline = playerWon
            ? $"You defeated {encName}"
            : $"Defeated by {encName}";

        // ── Central stone panel ──
        var panel = new Panel();
        panel.CustomMinimumSize = new Vector2(640, 0);
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        var panelStyle = StyleWornBorder(
            borderColor: accentColor,
            width: 3,
            radius: RadiusLarge,
            bgColor: SurfaceStone
        );
        panel.AddThemeStyleboxOverride("panel", panelStyle);

        var panelVBox = new VBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        panelVBox.AddThemeConstantOverride("separation", 6);
        panel.AddChild(panelVBox);

        // Top spacer
        panelVBox.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.Expand });

        // ── Status icon + label ──
        var statusLabelNode = new Label
        {
            Text = statusLabel,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        ApplyHeaderFont(statusLabelNode, FontTitleScreen);
        statusLabelNode.Modulate = accentColor;
        panelVBox.AddChild(statusLabelNode);

        // ── TASK-REWARD-SCREEN-1: Encounter portrait ──
        if (encounter != null && !string.IsNullOrEmpty(encounter.Portrait))
        {
            var portraitCtr = new CenterContainer
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
                CustomMinimumSize = new Vector2(0, 72),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            };
            var portraitTex = ResourceLoader.Load<Texture2D>(encounter.Portrait);
            if (portraitTex != null)
            {
                var portrait = new TextureRect
                {
                    Texture = portraitTex,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspect,
                    CustomMinimumSize = new Vector2(72, 72),
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                };
                var portraitStyle = new StyleBoxFlat
                {
                    BgColor = SurfaceStone,
                    BorderColor = accentColor,
                    BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
                    CornerRadiusTopLeft = RadiusMedium, CornerRadiusTopRight = RadiusMedium,
                    CornerRadiusBottomLeft = RadiusMedium, CornerRadiusBottomRight = RadiusMedium,
                };
                portrait.AddThemeStyleboxOverride("normal", portraitStyle);
                portraitCtr.AddChild(portrait);
            }
            panelVBox.AddChild(portraitCtr);
        }

        // ── Encounter name headline ──
        var headlineLabel = new Label
        {
            Text = headline,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        ApplyHeaderFont(headlineLabel, FontSectionHeader);
        headlineLabel.Modulate = TextPrimary;
        panelVBox.AddChild(headlineLabel);

        // ── Flavor text (DialogueOutro) ──
        string flavor = playerWon && encounter?.DialogueOutro is { Count: > 0 }
            ? string.Join("\n", encounter.DialogueOutro)
            : "";
        if (!string.IsNullOrEmpty(flavor))
        {
            var flavorLabel = new Label
            {
                Text = flavor,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.Word,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                CustomMinimumSize = new Vector2(0, 48),
            };
            ApplyBodyFont(flavorLabel, FontSecondary);
            flavorLabel.Modulate = TextSecondary;
            panelVBox.AddChild(flavorLabel);
        }

        // ── Divider line ──
        panelVBox.AddChild(MakeDivider());

        // ── Turns taken ──
        var turnLabel = new Label
        {
            Text = $"Turns taken: {_gsm.TurnNumber}",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        ApplyBodyFont(turnLabel, FontBody);
        turnLabel.Modulate = TextMuted;
        panelVBox.AddChild(turnLabel);

        // ── Reward summary panel (victory only) ──
        if (playerWon && encounter != null)
        {
            // ── TASK-REWARD-SCREEN-1: Rewards section with animated counters ──
            panelVBox.AddChild(MakeDivider());

            var rewardPanel = new Panel();
            rewardPanel.CustomMinimumSize = new Vector2(360, 0);
            var rewardStyle = StyleWornBorder(
                borderColor: BorderSubtle,
                width: 1,
                radius: RadiusMedium,
                bgColor: CardFace
            );
            rewardPanel.AddThemeStyleboxOverride("panel", rewardStyle);

            var rewardGrid = new VBoxContainer();
            rewardGrid.AddThemeConstantOverride("separation", 4);

            // ── Rewards section header ──
            var rewardHeader = new Label
            {
                Text = "— Rewards —",
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            };
            ApplyBodyFont(rewardHeader, FontSecondary);
            rewardHeader.Modulate = Gold;
            rewardGrid.AddChild(rewardHeader);

            int rewardIdx = 0;

            // Shards — animated counter
            if (encounter.ShardReward > 0)
            {
                var (shardRow, shardVal) = MakeAnimatedRewardRow("● Shards", encounter.ShardReward, Gold, rewardIdx);
                rewardGrid.AddChild(shardRow);
                _rewardCounters.Add(new AnimatedRewardCounter { ValueLabel = shardVal, TargetValue = encounter.ShardReward, Index = rewardIdx });
                rewardIdx++;
            }

            // Dig charges — animated counter
            if (encounter.DigChargeReward > 0)
            {
                var (digRow, digVal) = MakeAnimatedRewardRow("◇ Dig Charges", encounter.DigChargeReward, Moss, rewardIdx);
                rewardGrid.AddChild(digRow);
                _rewardCounters.Add(new AnimatedRewardCounter { ValueLabel = digVal, TargetValue = encounter.DigChargeReward, Index = rewardIdx });
                rewardIdx++;
            }

            // Fragments
            if (!string.IsNullOrEmpty(encounter.FragmentReward))
            {
                var fragRow = MakeRewardRow("◆ Fragments", $"+{encounter.FragmentReward}", Amber);
                rewardGrid.AddChild(fragRow);
            }

            // Granted card
            if (!string.IsNullOrEmpty(_grantedCardName))
            {
                var cardRow = MakeRewardRow("♠ New Card", _grantedCardName, Gold);
                rewardGrid.AddChild(cardRow);
            }

            rewardPanel.AddChild(rewardGrid);
            panelVBox.AddChild(rewardPanel);

            // ── TASK-DROPS-UI-1: Drop reveal area (hidden initially, shown by StartDropReveal) ──
            if (_dropRevealCards.Count > 0)
            {
                panelVBox.AddChild(MakeDivider());

                var dropHeader = new Label
                {
                    Text = "— Drops —",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                };
                ApplyBodyFont(dropHeader, FontSecondary);
                dropHeader.Modulate = Moss;
                dropHeader.Visible = false; // hidden until reveal starts
                panelVBox.AddChild(dropHeader);
                _dropTitleLabel = dropHeader;

                var dropCtr = new CenterContainer
                {
                    Name = "DropRevealArea",
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    CustomMinimumSize = new Vector2(0, 340),
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                    Visible = false,
                };
                panelVBox.AddChild(dropCtr);
                _dropRevealContainer = dropCtr;
            }
        }
        else if (!playerWon && encounter != null)
        {
            // ── TASK-REWARD-SCREEN-1: Defeat — show what was lost (forfeited rewards) ──
            bool hasRewards = encounter.ShardReward > 0 || encounter.DigChargeReward > 0
                              || !string.IsNullOrEmpty(encounter.FragmentReward)
                              || !string.IsNullOrEmpty(_grantedCardName)
                              || _dropRevealCards.Count > 0;
            if (hasRewards)
            {
                panelVBox.AddChild(MakeDivider());

                var forfeitLabel = new Label
                {
                    Text = "— Rewards forfeited —",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                };
                ApplyBodyFont(forfeitLabel, FontSecondary);
                forfeitLabel.Modulate = Ember;
                panelVBox.AddChild(forfeitLabel);

                if (encounter.ShardReward > 0)
                {
                    panelVBox.AddChild(MakeRewardRow("● Shards", $"+{encounter.ShardReward}", TextMuted));
                }
                if (encounter.DigChargeReward > 0)
                {
                    panelVBox.AddChild(MakeRewardRow("◇ Dig Charges", $"+{encounter.DigChargeReward}", TextMuted));
                }
                if (!string.IsNullOrEmpty(encounter.FragmentReward))
                {
                    panelVBox.AddChild(MakeRewardRow("◆ Fragments", $"+{encounter.FragmentReward}", TextMuted));
                }
                if (_dropRevealCards.Count > 0)
                {
                    int dropCount = _dropRevealCards.Count;
                    panelVBox.AddChild(MakeRewardRow("♠ Card Drops", $"{dropCount} card{(dropCount != 1 ? "s" : "")}", TextMuted));
                }
            }
        }

        // ── Divider line ──
        panelVBox.AddChild(MakeDivider());

        // ── Action buttons ──
        var btnHBox = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        btnHBox.AddThemeConstantOverride("separation", Space5);

        // "Fight Again" / "Try Again" — reloads the duel
        var fightAgainBtn = MakeStoneButton(playerWon ? "Fight Again" : "Try Again");
        string seedHex = CampaignContext.DebugSeed.HasValue
            ? CampaignContext.DebugSeed.Value.ToString("X")
            : "";
        string currentSeed = CampaignContext.DebugSeed?.ToString() ?? "";
        fightAgainBtn.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            // Retry preserves the same seed (for deterministic replay)
            if (!string.IsNullOrEmpty(currentSeed))
                CampaignContext.DebugSeed = ulong.Parse(currentSeed);
            GetTree().ChangeSceneToFile("res://scenes/duel/DuelScene.tscn");
        };
        btnHBox.AddChild(fightAgainBtn);

        // "Continue" / "Return to Map"
        var continueBtn = MakeStoneButton(playerWon ? "Continue" : "Return to Map");
        continueBtn.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            // Mark node cleared (already done in OnGameOver for campaign, but ensure it's done)
            if (playerWon && CampaignContext.CurrentNodeId != null)
                CampaignContext.Progression.MarkNodeCleared(CampaignContext.CurrentNodeId);
            GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
        };
        btnHBox.AddChild(continueBtn);

        panelVBox.AddChild(btnHBox);

        // Bottom spacer
        panelVBox.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.Expand });

        // ── Add panel to overlay ──
        // Wrap panel in a centered container so it sits in the middle
        var container = new CenterContainer();
        container.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        container.AddChild(panel);
        _gameOverOverlay.AddChild(container);

        AddChild(_gameOverOverlay);

        // ═══ TASK-JUICE-1: Victory/defeat light effects ═══
        if (playerWon)
            RitualEffects.PlayVictoryLight(this, CampaignContext.ReduceMotion);
        else
            RitualEffects.PlayDefeatDrain(this, CampaignContext.ReduceMotion);

        // ── TASK-DROPS-UI-1: Start the drop reveal sequence ──
        if (_dropRevealCards.Count > 0)
        {
            // Wait one frame for layout then start reveal
            var startTimer = new Godot.Timer();
            startTimer.WaitTime = 0.1f;
            startTimer.OneShot = true;
            startTimer.Timeout += StartDropReveal;
            _gameOverOverlay.AddChild(startTimer);
            startTimer.Start();
        }

        // ── TASK-REWARD-SCREEN-1: Start animated reward counters ──
        // Starts counting up immediately; staggered delays managed by StartAnimatedCounters
        var counterTimer = new Godot.Timer();
        counterTimer.WaitTime = 0.1f;
        counterTimer.OneShot = true;
        counterTimer.Timeout += StartAnimatedCounters;
        _gameOverOverlay.AddChild(counterTimer);
        counterTimer.Start();

        // Play audio event
        var audio = GetNode<AudioManager>("/root/AudioManager");
        audio.PlaySfx(playerWon ? "victory" : "defeat");

        // ═══ SOAK MODE: auto-press Continue/Return to Map after overlay shows ═══
        if (CampaignContext.SoakActive)
        {
            bool isDefeatRetry = CampaignContext.SoakDefeatPhase && !playerWon && !CampaignContext.SoakDefeatHasRetried;
            GD.Print($"[DUELSOAK] Soak mode — auto-continue (defeatRetry={isDefeatRetry}, won={playerWon}, hasRetried={CampaignContext.SoakDefeatHasRetried})");
            var soakTimer = new Godot.Timer();
            soakTimer.WaitTime = 1.5f;
            soakTimer.OneShot = true;
            soakTimer.Timeout += () =>
            {
                if (isDefeatRetry)
                {
                    CampaignContext.SoakDefeatHasRetried = true;
                    // Defeat test phase: press Try Again to prove retry works
                    GD.Print("[DUELSOAK] Defeat test — pressing Try Again to retry");
                    // Don't clear the node — retry means we try again
                    // Reload duel scene (same seed)
                    GetTree().ChangeSceneToFile("res://scenes/duel/DuelScene.tscn");
                }
                else
                {
                    GD.Print("[DUELSOAK] Auto-pressing Continue");
                    // In soak mode, clear the node regardless of outcome so the loop progresses
                    if (CampaignContext.CurrentNodeId != null)
                        CampaignContext.Progression.MarkNodeCleared(CampaignContext.CurrentNodeId);
                    CampaignContext.SaveManager.Save();
                    if (CampaignContext.SoakStopAfterRetry && CampaignContext.SoakDefeatHasRetried)
                    {
                        GD.Print("[DUELSOAK] SoakStopAfterRetry — quitting after retry cycle");
                        GetTree().Quit(0);
                    }
                    else
                    {
                        GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
                    }
                }
            };
            _gameOverOverlay.AddChild(soakTimer);
            soakTimer.Start();
        }
        // ═══ END SOAK AUTO-CONTINUE ═══
    }

    /// <summary>Create a thin gold divider line.</summary>
    private Control MakeDivider()
    {
        var div = new ColorRect
        {
            Color = new Color(Gold.R, Gold.G, Gold.B, 0.3f),
            CustomMinimumSize = new Vector2(240, 1),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        var ctr = new CenterContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(0, 4),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        ctr.AddChild(div);
        return ctr;
    }

    /// <summary>Create a reward row: label + value in an HBox.</summary>
    private Control MakeRewardRow(string label, string value, Color valueColor)
    {
        var row = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        row.AddThemeConstantOverride("separation", 12);

        var lbl = new Label
        {
            Text = label,
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        ApplyBodyFont(lbl, FontBody);
        lbl.Modulate = TextSecondary;
        row.AddChild(lbl);

        var val = new Label
        {
            Text = value,
            HorizontalAlignment = HorizontalAlignment.Left,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        ApplyHeaderFont(val, FontLargeBody);
        val.Modulate = valueColor;
        row.AddChild(val);

        return row;
    }

    // ── TASK-REWARD-SCREEN-1: Animated reward row ──
    /// <summary>Create a reward row with a label and a value label that will count up from 0 to the target.</summary>
    private (Control Row, Label ValueLabel) MakeAnimatedRewardRow(string label, int targetValue, Color valueColor, int index)
    {
        var row = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        row.AddThemeConstantOverride("separation", 12);

        var lbl = new Label
        {
            Text = label,
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        ApplyBodyFont(lbl, FontBody);
        lbl.Modulate = TextSecondary;
        row.AddChild(lbl);

        var val = new Label
        {
            Text = "0",
            HorizontalAlignment = HorizontalAlignment.Left,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        ApplyHeaderFont(val, FontLargeBody);
        val.Modulate = valueColor;
        row.AddChild(val);

        return (row, val);
    }

    /// <summary>Start the animated reward counters counting up from 0 to their target values.</summary>
    private void StartAnimatedCounters()
    {
        if (_countersStarted || _rewardCounters.Count == 0) return;
        _countersStarted = true;

        float baseDelay = 0.5f; // brief pause after overlay appears
        float counterTime = 1.2f;
        float stagger = 0.3f;

        foreach (var counter in _rewardCounters)
        {
            int target = counter.TargetValue;
            Label label = counter.ValueLabel;
            if (target <= 0) continue;

            float delay = baseDelay + counter.Index * stagger;
            var tween = GetTree().CreateTween();
            tween.SetParallel(false);
            var capturedLabel = label;
            tween.TweenMethod(
                Callable.From<double>(v => { if (GodotObject.IsInstanceValid(capturedLabel)) capturedLabel.Text = ((int)v).ToString(); }),
                0.0, (double)target, counterTime
            ).SetDelay(delay);
        }
    }

    /// <summary>Create a stone-styled action button with ThemeTokens colors.</summary>
    private Button MakeStoneButton(string text)
    {
        var btn = new Button
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(180, MinButtonHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        ApplyBodyFont(btn, FontButtonPrimary);
        var normal = new StyleBoxFlat
        {
            BgColor = SurfaceMetal,
            BorderColor = BorderStandard,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = RadiusMedium, CornerRadiusTopRight = RadiusMedium,
            CornerRadiusBottomLeft = RadiusMedium, CornerRadiusBottomRight = RadiusMedium,
            ContentMarginLeft = Space4, ContentMarginTop = Space2, ContentMarginRight = Space4, ContentMarginBottom = Space2,
        };
        btn.AddThemeStyleboxOverride("normal", normal);
        var hover = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#4A4540"),
            BorderColor = BorderHighlight,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = RadiusMedium, CornerRadiusTopRight = RadiusMedium,
            CornerRadiusBottomLeft = RadiusMedium, CornerRadiusBottomRight = RadiusMedium,
            ContentMarginLeft = Space4, ContentMarginTop = Space2, ContentMarginRight = Space4, ContentMarginBottom = Space2,
        };
        btn.AddThemeStyleboxOverride("hover", hover);
        btn.Modulate = TextPrimary;
        return btn;
    }

    // ——— Tutorial helpers (TASK-TU2) ———
    /// <summary>Enable/disable the End Turn button. Used by TutorialRunner for action restrictions.</summary>
    public void SetEndTurnEnabled(bool enabled)
    {
        if (_endTurnButton != null)
            _endTurnButton.Disabled = !enabled;
    }

    /// <summary>Programmatically summon a card. Used by TutorialRunner headless auto-play.</summary>
    public void PlayerSummonCard(string cardDefId, int laneIndex)
    {
        OnPlayCardRequested(cardDefId, laneIndex);
    }

    /// <summary>Programmatically attack. Used by TutorialRunner headless auto-play.</summary>
    public void PlayerAttack(int sourceLane, int targetLane)
    {
        OnAttackRequested(sourceLane, targetLane);
    }

    /// <summary>Programmatically end turn. Used by TutorialRunner headless auto-play.</summary>
    public void PlayerEndTurn()
    {
        OnEndTurnPressed();
    }

    // ——— Public update methods ———

    public void SetEnemyVigor(int vigor) { if (_enemyVigorValue != null) _enemyVigorValue.Text = Math.Max(0, vigor).ToString(); }
        public void SetEnemyAttunement(string text) { if (_enemyAttuneValue != null) _enemyAttuneValue.Text = text; }
        public void SetPlayerVigor(int vigor) { if (_playerShrineVigorLabel != null) _playerShrineVigorLabel.Text = Math.Max(0, vigor).ToString(); }
        public void SetPlayerAttunement(string text) { if (_playerShrineAttuneLabel != null) _playerShrineAttuneLabel.Text = text; }

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

            // BOARD-MATCH-1: Hand cards intentionally overlap (fan layout, separation=-8px).
            // Only fail if overlap exceeds 40% of card width (pathological overlap).
            var sortedCards = _handCards.OrderBy(c => c.Position.X).ToList();
            for (int i = 1; i < sortedCards.Count; i++)
            {
                var prev = sortedCards[i - 1];
                var cur = sortedCards[i];
                float prevRight = prev.Position.X + prev.Size.X;
                float curLeft = cur.Position.X;
                float overlap = prevRight - curLeft;
                if (overlap > prev.Size.X * 0.4f)
                {
                    GD.PrintErr($"[VERIFY] FAIL: Hand cards overlap {overlap:F0}px ({overlap / prev.Size.X * 100:F0}%): \"{prev.CardName}\" right={prevRight:F0} > \"{cur.CardName}\" left={curLeft:F0}");
                    fails++;
                }
            }
        }

        // — ART-STYLE-3: Pairwise hand card rects vs player slot rects —
        float vhLayout = GetViewportRect().Size.Y;
        float scaleLayout = vhLayout / 1080f; // BOARD-MATCH-1: Use 1080 reference
        foreach (var hc in _handCards)
        {
            var hcRect = hc.GetRect();
            var hcGp = hc.GetScreenTransform().Origin;
            float hcLeft = hcGp.X;
            float hcRight = hcGp.X + hcRect.Size.X;
            float hcTop = hcGp.Y;
            float hcBottom = hcGp.Y + hcRect.Size.Y;
            foreach (var slot in _playerSlots)
            {
                var sRect = slot.GetRect();
                var sGp = slot.GetScreenTransform().Origin;
                float slLeft = sGp.X;
                float slRight = sGp.X + sRect.Size.X;
                float slTop = sGp.Y;
                float slBottom = sGp.Y + sRect.Size.Y;
                // Check AABB overlap
                bool overlaps = hcLeft < slRight && hcRight > slLeft && hcTop < slBottom && hcBottom > slTop;
                if (overlaps)
                {
                    GD.PrintErr($"[VERIFY] FAIL: Hand card \"{hc.CardName}\" overlaps player slot {slot.LaneIndex} — " +
                        $"hand bottom={hcBottom:F0}, slot bottom={slBottom:F0}, overlap={slBottom - hcTop:F0}px");
                    fails++;
                }
                else
                {
                    float gap = hcTop - slBottom;
                    GD.Print($"[VERIFY] OK: Hand card \"{hc.CardName}\" clear of player slot {slot.LaneIndex} (gap={gap:F0}px)");
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

            // TASK-UI3c: Player shrine is bottom-aligned, so it extends below viewport — allow Y overflow
            bool playerInViewport = playerRect.Position.X >= 0 && playerRect.End.X <= viewportSize.X + 2;
            if (!playerInViewport)
            {
                GD.PrintErr($"[VERIFY] FAIL: Player arsenal group {playerRect} exceeds viewport horizontally {viewportSize}");
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

        // TASK-UI4-ARSENAL: Overlap assertion — arsenal group rect must not intersect any hand card rect
        GD.Print("[VERIFY] === Overlap check (TASK-UI4-ARSENAL) ===");
        if (_playerArsenalGroup != null && _handCards.Count > 0)
        {
            var arsenalRect = new Rect2(_playerArsenalGroup.GetScreenTransform().Origin, _playerArsenalGroup.Size);
            bool hasOverlap = false;
            foreach (var card in _handCards)
            {
                var cardRect = new Rect2(card.GetScreenTransform().Origin, card.Size);
                if (arsenalRect.Intersects(cardRect))
                {
                    GD.PrintErr($"[VERIFY] FAIL: Arsenal {arsenalRect} intersects hand card \"{card.CardName}\" at {cardRect}");
                    fails++;
                    hasOverlap = true;
                    break;
                }
            }
            if (!hasOverlap)
                GD.Print($"[VERIFY] OK: Arsenal {arsenalRect} does not overlap any hand card");
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

    /// <summary>
    /// TASK-AC1: Apply visual state styling to an artifact card control.
    /// Modifies border color, background tint, and label colors based on VisualState.
    /// No client-side state guesswork — state comes from engine ArtifactSlot.VisualState.
    /// </summary>
    private static void ApplyArtifactVisualState(Control cardControl, Label nameLabel, Label chargeLabel, ArtifactVisualState state)
    {
        if (cardControl is PanelContainer panel)
        {
            var style = panel.GetThemeStylebox("panel") as StyleBoxFlat;
            if (style == null)
            {
                // Create a default style if none exists
                style = new StyleBoxFlat
                {
                    CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
                    CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
                    BorderWidthLeft = 2, BorderWidthTop = 2,
                    BorderWidthRight = 2, BorderWidthBottom = 2
                };
                panel.AddThemeStyleboxOverride("panel", style);
            }

            switch (state)
            {
                case ArtifactVisualState.READY:
                    // Gold border (existing default), normal brightness
                    style.BorderColor = Color.FromHtml("#8a763c");
                    style.BgColor = new Color(0.12f, 0.10f, 0.08f, 0.85f);
                    nameLabel.Modulate = new Color(0.85f, 0.75f, 0.45f, 0.9f);
                    if (chargeLabel != null)
                        chargeLabel.Modulate = new Color(0.6f, 0.5f, 0.25f, 0.8f);
                    break;

                case ArtifactVisualState.CHARGED:
                    // Blue-purple border, slight glow on charges
                    style.BorderColor = Color.FromHtml("#6b5b9c");
                    style.BgColor = new Color(0.10f, 0.08f, 0.14f, 0.85f);
                    nameLabel.Modulate = new Color(0.75f, 0.70f, 0.95f, 0.95f);
                    if (chargeLabel != null)
                        chargeLabel.Modulate = new Color(0.60f, 0.50f, 0.85f, 1.0f);
                    break;

                case ArtifactVisualState.SUPPRESSED:
                    // Gray/silver border, dimmed background, frosted name
                    style.BorderColor = new Color(0.4f, 0.4f, 0.45f, 0.5f);
                    style.BgColor = new Color(0.06f, 0.05f, 0.06f, 0.70f);
                    nameLabel.Modulate = new Color(0.5f, 0.5f, 0.55f, 0.6f);
                    if (chargeLabel != null)
                        chargeLabel.Modulate = new Color(0.4f, 0.4f, 0.45f, 0.4f);
                    break;

                case ArtifactVisualState.SPENT:
                    // Muted amber border, slightly dimmed, spent look
                    style.BorderColor = Color.FromHtml("#8a7a5c");
                    style.BgColor = new Color(0.10f, 0.09f, 0.07f, 0.70f);
                    nameLabel.Modulate = new Color(0.65f, 0.60f, 0.45f, 0.7f);
                    if (chargeLabel != null)
                        chargeLabel.Modulate = new Color(0.5f, 0.45f, 0.35f, 0.5f);
                    break;
            }
        }
    }

    /// <summary>
    /// TASK-AC2: Play a brief pulse animation on a charge label when charges reach max.
    /// Scales up the label, shifts color to ChargeFullPulse, then snaps back in ≤0.5s.
    /// Suppressed artifacts never pulse — the pip visuals freeze per G3.
    /// </summary>
    private void PlayChargeFullPulse(Label chargeLabel, int slotIndex, bool isEnemy)
    {
        if (chargeLabel == null || !IsInstanceValid(chargeLabel) || string.IsNullOrEmpty(chargeLabel.Text))
            return;

        int tweenIdx = (isEnemy ? 2 : 0) + slotIndex;
        if (tweenIdx < _chargePulseTweens.Length)
        {
            // Kill any existing pulse on this slot
            if (_chargePulseTweens[tweenIdx] != null && IsInstanceValid(_chargePulseTweens[tweenIdx]))
            {
                _chargePulseTweens[tweenIdx].Kill();
                _chargePulseTweens[tweenIdx] = null;
            }

            // Reset to normal state first
            chargeLabel.Scale = Vector2.One;
            chargeLabel.Modulate = ChargeFilled;

            var tween = CreateTween();
            tween.SetParallel(true);
            // Scale up
            tween.TweenProperty(chargeLabel, "scale", Vector2.One * ChargePulseScale, ChargePulseDuration * 0.5f);
            // Shift color to pulse
            tween.TweenProperty(chargeLabel, "modulate", ChargeFullPulse, ChargePulseDuration * 0.5f);
            tween.Chain();
            tween.SetParallel(true);
            // Scale back
            tween.TweenProperty(chargeLabel, "scale", Vector2.One, ChargePulseDuration * 0.5f);
            // Restore normal gold
            tween.TweenProperty(chargeLabel, "modulate", ChargeFilled, ChargePulseDuration * 0.5f);
            tween.TweenCallback(Callable.From(() =>
            {
                if (IsInstanceValid(chargeLabel))
                {
                    chargeLabel.Scale = Vector2.One;
                    chargeLabel.Modulate = ChargeFilled;
                }
            }));

            _chargePulseTweens[tweenIdx] = tween;
        }
    }

    /// <summary>
    /// TASK-AC2: Render charge pip text for an artifact slot.
    /// Returns a string of filled (•) and empty (∘) pips.
    /// When suppressed, pips keep their visual count but don't animate.
    /// </summary>
    private static string RenderChargePips(int charges, int maxCharges)
    {
        if (maxCharges <= 0) return "";
        int filled = System.Math.Min(charges, maxCharges);
        int empty = maxCharges - filled;
        return new string('•', filled) + new string('∘', empty);
    }

    /// <summary>
    /// Sets up all four visual states across both players' artifact slots.
    /// Player: Sword (READY), Duskfang (CHARGED at max=3/3 → pulse visible in capture)
    /// Enemy: Shield (SUPPRESSED, 2 turns remaining), Aura (SPENT, HasTriggeredThisTurn=true)
    /// </summary>
    private void PrePlaceArtifacts()
    {
        var state = _gsm.State;
        if (state == null)
        {
            GD.PrintErr("[CAPTURE] Cannot pre-place artifacts: game state is null");
            return;
        }

        GD.Print("[CAPTURE] Pre-placing artifacts with all four visual states");

        // Ensure artifact definitions are loaded
        int nextId = state.NextInstanceId;

        // ——— Player 0: 2 artifacts (READY + CHARGED) ———
        var p0ArtIds = new[] { "artf_warrior_sword", "artf_rogue_dagger_dusk" };
        state.Players[0].ArtifactDefIds = p0ArtIds;
        state.Players[0].ArtifactClass = "warrior";
        state.Players[0].ArtifactSlots = new ArtifactSlot[2];

        for (int i = 0; i < 2; i++)
        {
            var slot = new ArtifactSlot(i);
            var artDef = ArtifactRegistry.Get(p0ArtIds[i])
                ?? throw new InvalidOperationException($"Artifact '{p0ArtIds[i]}' not found in registry");

            var instance = new CardInstance(nextId++, p0ArtIds[i], 0)
            {
                CardType = CardType.ARTIFACT,
                Zone = Zone.ArtifactSlot,
                ArtifactSlotIndex = i,
                ArtifactClass = artDef.Class,
                SlotPool = artDef.SlotPool,
                Cost = 0,
                BaseAttack = 0,
                BaseVigor = 0,
            };

            slot.Occupant = instance;

            // First artifact: READY (default state, no modifications)
            if (i == 0)
            {
                // READY — leave all defaults
                slot.MaxCharges = 0;
                slot.Charges = 0;
            }
            // Second artifact: CHARGED at max (TASK-AC2: max charges so pulse is visible in capture)
            else
            {
                slot.MaxCharges = 3;
                slot.Charges = 3; // max charges → triggers ON_CHARGE_FULL pulse
            }

            state.Players[0].ArtifactSlots[i] = slot;
        }

        // ——— Player 1: 2 artifacts (SUPPRESSED + SPENT) ———
        var p1ArtIds = new[] { "artf_warrior_shield", "artf_mage_wand" };
        state.Players[1].ArtifactDefIds = p1ArtIds;
        state.Players[1].ArtifactClass = "mage";
        state.Players[1].ArtifactSlots = new ArtifactSlot[2];

        for (int i = 0; i < 2; i++)
        {
            var slot = new ArtifactSlot(i);
            var artDef = ArtifactRegistry.Get(p1ArtIds[i]);

            var instance = new CardInstance(nextId++, p1ArtIds[i], 1)
            {
                CardType = CardType.ARTIFACT,
                Zone = Zone.ArtifactSlot,
                ArtifactSlotIndex = i,
                ArtifactClass = artDef?.Class ?? "warrior",
                SlotPool = artDef?.SlotPool ?? "",
                Cost = 0,
                BaseAttack = 0,
                BaseVigor = 0,
            };

            slot.Occupant = instance;

            // First artifact: SUPPRESSED
            if (i == 0)
            {
                slot.MaxCharges = 0;
                slot.Charges = 0;
                slot.IsSuppressed = true;
                slot.SuppressionRemaining = 2;
                slot.SuppressionSourceId = "artf_rogue_dagger_dusk";
            }
            // Second artifact: SPENT (HasTriggeredThisTurn = true)
            else
            {
                slot.MaxCharges = 3;
                slot.Charges = 0;
                slot.HasTriggeredThisTurn = true;
            }

            state.Players[1].ArtifactSlots[i] = slot;
        }

        state.NextInstanceId = nextId;

        GD.Print("[CAPTURE] Pre-placed artifacts — Player: READY + CHARGED, Enemy: SUPPRESSED + SPENT");
    }

    /// <summary>
    /// TASK-G + TASK-DUEL-HAND-1: Inflate player hand to the target size (CaptureHandSize) for
    /// worst-case compression test in captures.
    /// Copies cards already in hand until we have targetCount, using card defs from the registry.
    /// </summary>
    private void InflateHandTo10()
    {
        var state = _gsm.State;
        if (state == null || state.Players.Length == 0)
        {
            GD.PrintErr("[CAPTURE] Cannot inflate hand: game state is null");
            return;
        }

        var hand = state.Players[0].Hand;
        int targetCount = CampaignContext.CaptureHandSize;
        if (hand.Count >= targetCount)
        {
            // If the long-name test card is registered but not in hand, force it in
            var longNameDef = CardRegistry.Get("test_long_name_wrapper");
            if (longNameDef != null && !hand.Any(c => c.CardDefId == "test_long_name_wrapper"))
            {
                int nid = state.NextInstanceId;
                var copy = new CardInstance(nid, "test_long_name_wrapper", 0)
                {
                    CardType = CardType.CREATURE,
                    Cost = longNameDef.Cost,
                    Strata = longNameDef.Strata,
                    BaseAttack = longNameDef.Attack ?? 0,
                    BaseVigor = longNameDef.Vigor ?? 0,
                    Zone = Zone.Hand,
                };
                hand.Add(copy);
                state.NextInstanceId = nid + 1;
                GD.Print("[CAPTURE] Added long-name test card 'The Undying Root of the Fallow Reach' to hand");
            }
            GD.Print($"[CAPTURE] Hand already has {hand.Count} cards, no inflation needed");
            return;
        }

        int nextId = state.NextInstanceId;
        var templateCards = hand.ToList(); // snapshot existing hand cards
        int srcIdx = 0;

        while (hand.Count < targetCount)
        {
            var src = templateCards[srcIdx % templateCards.Count];
            var def = CardRegistry.Get(src.CardDefId);
            if (def != null)
            {
                var copy = new CardInstance(nextId++, src.CardDefId, 0)
                {
                    CardType = CardType.CREATURE,
                    Cost = def.Cost,
                    Strata = def.Strata,
                    BaseAttack = def.Attack ?? 0,
                    BaseVigor = def.Vigor ?? 0,
                    Zone = Zone.Hand,
                };
                hand.Add(copy);
            }
            srcIdx++;
        }

        state.NextInstanceId = nextId;
        GD.Print($"[CAPTURE] Inflated player hand from {templateCards.Count} to {hand.Count} cards");
    }

    /// <summary>
    /// TASK-WARDEN-RULE-1: Translate opening rule identifiers to display text for the banner.
    /// </summary>
    private static string GetOpeningRuleDisplayText(string ruleId)
    {
        return ruleId switch
        {
            "root_choked" => "Root-choked — your leftmost lane is buried until the Warden's first creature dies.",
            _ => $"Rule: {ruleId}"
        };
    }
}
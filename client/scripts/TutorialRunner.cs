using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// TutorialRunner — consumes TU1's tutorial script data and drives the duel.
///
/// State machine:
///   Idle → SetupDuel → PlayerTurn (per-beat loop) → OpponentTurn → PlayerTurn → ... → Finished
///
/// On each player beat:
///   1. Apply hand/attunement overrides (first beat of a player turn only)
///   2. Set highlights and action restrictions from the beat
///   3. Wait for the player to perform an action matching trigger_event + condition
///   4. Show popup explaining the consequence
///   5. Capture screenshot at the beat boundary
///   6. Wait for popup dismiss → advance to next beat
///
/// For opponent turns: execute scripted actions via timer, replacing the AI bot.
///
/// Headless mode: auto-plays expected actions at each beat with a short delay,
/// captures each beat, then exits.
/// </summary>
public partial class TutorialRunner : Node
{
    private enum RunnerState
    {
        Idle,
        SetupDuel,
        PlayerTurn,
        ShowingPopup,
        OpponentTurn,
        OpponentEndTurn,
        Finished
    }

    // ── Dependencies ──
    private DuelScene _duelScene = default!;
    private GameStateManager _gsm = default!;
    private BotController _bot = default!;
    private bool _isHeadless;

    // ── Script data ──
    private TutorialScript? _script;
    private int _currentTurnIndex; // index into _script.Turns
    private int _currentBeatIndex; // index into current turn's player_beats

    // ── State tracking ──
    private RunnerState _state = RunnerState.Idle;
    private int _opponentActionIndex;
    private bool _awaitingDismiss;
    private TutorialPopup? _popup;
    private int _prevAttackCount; // player's AttackCountThisTurn from previous frame
    private int _prevSummonCount; // number of occupied player lanes from previous frame
    private int _prevTurnNumber;
    private int _prevCurrentPlayer;
    private bool _duelInitialized;

    // ── Override tracking ──
    private bool _handOverriddenThisTurn;
    private bool _attunementOverriddenThisTurn;

    // ── Timers for opponent actions and headless auto-play ──
    private Godot.Timer? _actionTimer;
    private Godot.Timer? _headlessTimer;

    // ── Beat capture paths (headless auto-play logic) ──
    private int _headlessSummonCount;
    private int _headlessAttackCount;
    // Track which player lane indices we've already summoned to
    private readonly List<int> _headlessSummonedLanes = new();

    // ── Capture directory ──
    private string _captureDir = "/home/fictive/runewake/artifacts/captures";
    private string _tutorialCapturePrefix = "tutorial_";

    // ── Events ──

    /// <summary>Raised when the tutorial finishes (completed or skipped).</summary>
    public event Action? TutorialFinished;

    // ── Initialization ──

    /// <summary>
    /// Initialize the tutorial runner with scene dependencies.
    /// Call after adding to the scene tree, before the duel starts.
    /// Sets up the CampaignContext encounter immediately so DuelScene
    /// reads the correct deck config.
    /// </summary>
    public void Initialize(DuelScene duelScene, GameStateManager gsm, BotController bot, bool isHeadless = false)
    {
        _duelScene = duelScene;
        _gsm = gsm;
        _bot = bot;
        _isHeadless = isHeadless;
        _popup = null;
        _state = RunnerState.Idle;
        _currentTurnIndex = 0;
        _currentBeatIndex = 0;
        _opponentActionIndex = 0;
        _awaitingDismiss = false;
        _duelInitialized = false;
        _prevAttackCount = 0;
        _prevSummonCount = 0;
        _prevTurnNumber = 1;
        _prevCurrentPlayer = 0;
        _headlessSummonCount = 0;
        _headlessAttackCount = 0;
        _headlessSummonedLanes.Clear();

        // Set up the encounter in CampaignContext immediately so DuelScene
        // reads the correct decks and artifacts when building GameConfig
        SetupEncounter();

        // Create action timer for opponent scripted plays and headless auto-play
        _actionTimer = new Godot.Timer();
        _actionTimer.OneShot = true;
        _actionTimer.Timeout += OnActionTimerTimeout;
        AddChild(_actionTimer);

        if (_isHeadless)
        {
            _headlessTimer = new Godot.Timer();
            _headlessTimer.OneShot = true;
            _headlessTimer.Timeout += OnHeadlessTimerTimeout;
            AddChild(_headlessTimer);
        }
    }

    /// <summary>
    /// Load a tutorial script by ID from content/tutorial/scripts/.
    /// </summary>
    public bool LoadScript(string tutorialId)
    {
        string path = $"res://content/tutorial/scripts/{tutorialId}.json";
        string json = Godot.FileAccess.GetFileAsString(path);
        if (string.IsNullOrEmpty(json))
        {
            GD.PrintErr($"[TutorialRunner] Script not found: {path}");
            return false;
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        _script = JsonSerializer.Deserialize<TutorialScript>(json, options);
        if (_script == null)
        {
            GD.PrintErr($"[TutorialRunner] Failed to parse script: {path}");
            return false;
        }

        GD.Print($"[TutorialRunner] Loaded script '{_script.TutorialId}': {_script.Title} ({_script.Turns.Count} turns)");
        return true;
    }

    /// <summary>
    /// Set up the duel state based on the tutorial script.
    /// Returns a GameConfig for the DuelScene to use.
    /// </summary>
    public void SetupEncounter()
    {
        if (_script == null) return;

        // Build encounter def for campaign context
        var deck = _script.PlayerDeck.ToList();
        var oppDeck = _script.OpponentDeck.ToList();

        CampaignContext.PlayerDeckIds = deck;
        CampaignContext.CurrentEncounter = new EncounterDef
        {
            Id = _script.TutorialId,
            Name = _script.Title,
            IsTutorial = true,
            Deck = oppDeck,
            Portrait = "",
            DialogueIntro = [],
            DialogueOutro = [],
            ShardReward = 0,
            DigChargeReward = 0
        };

        CampaignContext.DebugSeed = 42;

        // Set artifact IDs and class for the player's artifacts
        CampaignContext.TutorialPlayerArtifactIds = _script.Artifacts.ToArray();
        CampaignContext.TutorialPlayerClass = _script.Class;

        GD.Print($"[TutorialRunner] Setup encounter: player deck size={deck.Count}, opponent deck size={oppDeck.Count}");
        GD.Print($"[TutorialRunner] Artifacts: [{string.Join(", ", _script.Artifacts)}], class={_script.Class}");
    }

    /// <summary>
    /// Get the script for the current turn.
    /// </summary>
    private TurnScript? CurrentTurn => _script != null && _currentTurnIndex < _script.Turns.Count
        ? _script.Turns[_currentTurnIndex]
        : null;

    /// <summary>
    /// Get the current beat for player turns.
    /// </summary>
    private TutorialBeat? CurrentBeat
    {
        get
        {
            var turn = CurrentTurn;
            if (turn?.PlayerBeats == null || _currentBeatIndex >= turn.PlayerBeats.Count)
                return null;
            return turn.PlayerBeats[_currentBeatIndex];
        }
    }

    // ── Start / State Change Hook ──

    /// <summary>
    /// Start the tutorial after the duel scene has been set up and the game state initialized.
    /// Called from DuelScene once the game is ready.
    /// </summary>
    public void Start()
    {
        if (_script == null || _gsm.State == null)
        {
            GD.PrintErr("[TutorialRunner] Cannot start: script or game state not ready");
            return;
        }

        _duelInitialized = true;
        _state = RunnerState.PlayerTurn;

        // Disable the bot — we handle opponent turns
        _bot.Suspend();

        // Skip mulligan for both players
        SkipMulligan();

        // TASK-TUTORIAL-VERIFY-1: Dismiss the mulligan UI overlay so it doesn't
        // appear behind tutorial beat popups and captures.
        _duelScene.DismissMulligan();

        // Apply first-turn overrides
        ApplyTurnOverrides();

        // Set first beat
        _currentBeatIndex = 0;
        EnterBeat();

        GD.Print($"[TutorialRunner] Tutorial started: turn {_gsm.TurnNumber}, player turn");
    }

    private bool _pendingAdvance;
    
    public override void _Process(double delta)
    {
        if (_pendingAdvance)
        {
            _pendingAdvance = false;
            GD.Print("[TutorialRunner] _Process: executing pending AdvanceToNextTurn");
            AdvanceToNextTurn();
        }
    }

    /// <summary>
    /// Called from DuelScene's OnStateChanged after every state mutation.
    /// The runner checks if it needs to advance the state machine.
    /// All turn transitions are deferred via _pendingAdvance to avoid
    /// re-entrant NotifyStateChanged calls during render cycles.
    /// </summary>
    public void OnGameStateChanged()
    {
        if (!_duelInitialized || _script == null || _gsm.State == null) return;

        var state = _gsm.State;

        // Detect turn changes
        bool turnChanged = state.TurnNumber != _prevTurnNumber || state.CurrentPlayerIndex != _prevCurrentPlayer;
        _prevTurnNumber = state.TurnNumber;
        _prevCurrentPlayer = state.CurrentPlayerIndex;

        if (_state == RunnerState.OpponentEndTurn && state.CurrentPlayerIndex == 0)
        {
            // Opponent finished their turn, it's the player's turn now
            GD.Print($"[TutorialRunner] Turn {state.TurnNumber}: Opponent turn ended → player turn");
            _pendingAdvance = true;
            return;
        }

        // TASK-TUTORIAL-VERIFY-1: Player ended their turn — advance to opponent turn
        if (_state == RunnerState.PlayerTurn && turnChanged && state.CurrentPlayerIndex != 0)
        {
            GD.Print($"[TutorialRunner] Turn {state.TurnNumber}: Player turn ended → opponent turn");
            _pendingAdvance = true;
            return;
        }

        // TASK-TUTORIAL-VERIFY-1: Opponent executed END_TURN action — advance to player turn
        if (_state == RunnerState.OpponentTurn && turnChanged && state.CurrentPlayerIndex == 0)
        {
            GD.Print($"[TutorialRunner] Turn {state.TurnNumber}: Opponent END_TURN → player turn");
            _pendingAdvance = true;
            return;
        }

        if (_state == RunnerState.ShowingPopup && _awaitingDismiss)
        {
            // Waiting for popup dismiss — don't react to state changes
            return;
        }

        if (_state == RunnerState.PlayerTurn && state.CurrentPlayerIndex == 0)
        {
            // Check what the player just did by comparing state
            CheckPlayerAction(state);
        }
    }

    // ── Turn / Beat Flow ──

    private void AdvanceToNextTurn()
    {
        // Move to the next turn in the script
        _currentTurnIndex++;
        if (_currentTurnIndex >= (_script?.Turns.Count ?? 0))
        {
            EndTutorial();
            return;
        }

        var turn = CurrentTurn;
        if (turn == null) { EndTutorial(); return; }

        if (turn.Type == "opponent")
        {
            _state = RunnerState.OpponentTurn;
            _opponentActionIndex = 0;
            _handOverriddenThisTurn = false;
            _attunementOverriddenThisTurn = false;

            // Apply opponent hand override if specified
            ApplyTurnOverrides();

            // Start executing opponent actions
            ScheduleNextOpponentAction();
        }
        else
        {
            _state = RunnerState.PlayerTurn;
            _currentBeatIndex = 0;
            _handOverriddenThisTurn = false;
            _attunementOverriddenThisTurn = false;

            ApplyTurnOverrides();
            EnterBeat();
        }
    }

    private void EnterBeat()
    {
        var beat = CurrentBeat;
        if (beat == null)
        {
            // All beats completed — let player end turn naturally
            GD.Print("[TutorialRunner] All beats completed for this turn");
            return;
        }

        GD.Print($"[TutorialRunner] Entering beat '{beat.Id}' (trigger={beat.TriggerEvent})");

        // Reset tracking for this beat
        _prevAttackCount = _gsm.State.Players[0].AttackCountThisTurn;
        _prevSummonCount = _gsm.State.Players[0].Lanes.Count(l => l.Occupant != null);

        // Set up action restrictions if specified
        if (beat.RestrictActionsTo is { Count: > 0 })
        {
            ApplyActionRestrictions(beat.RestrictActionsTo);
        }

        // In headless mode, auto-play after a short delay
        if (_isHeadless && _headlessTimer != null)
        {
            _headlessTimer.Start(0.8f);
        }
    }

    private void CheckPlayerAction(GameState state)
    {
        var beat = CurrentBeat;
        if (beat == null) return;

        var player = state.Players[0];
        int currentAttackCount = player.AttackCountThisTurn;
        int currentSummonCount = state.Players[0].Lanes.Count(l => l.Occupant != null);

        bool matched = false;

        switch (beat.TriggerEvent)
        {
            case "SUMMON_CREATURE":
                if (currentSummonCount > _prevSummonCount)
                    matched = true;
                break;

            case "ATTACK_WITH_CREATURE":
                if (currentAttackCount > _prevAttackCount)
                    matched = true;
                break;

            case "END_TURN":
                if (state.CurrentPlayerIndex != 0)
                    matched = true;
                break;

            case "NO_ATTACK_END_TURN":
                // Player ended the turn without attacking this turn
                if (state.CurrentPlayerIndex != 0)
                {
                    // Check not_attacked_this_turn condition
                    bool noAttack = player.AttackCountThisTurn == 0;
                    if (noAttack && (beat.Condition?.NotAttackedThisTurn ?? false))
                        matched = true;
                    else if (noAttack && beat.Condition == null)
                        matched = true;
                }
                break;

            case "PLAY_SPELL":
                int currentSpellsCast = player.SpellCastCountThisTurn;
                if (currentSpellsCast > _prevSpellCastCount)
                    matched = true;
                break;

            case "ANY":
                matched = true;
                break;
        }

        // Check additional conditions
        if (matched && beat.Condition != null)
        {
            if (beat.Condition.NotAttackedThisTurn && player.AttackCountThisTurn > 0)
                matched = false;
            if (beat.Condition.AttackedCountGte.HasValue && player.AttackCountThisTurn < beat.Condition.AttackedCountGte.Value)
                matched = false;
            if (beat.Condition.CreaturesSummonedGte.HasValue && currentSummonCount < beat.Condition.CreaturesSummonedGte.Value)
                matched = false;
        }

        if (matched)
        {
            GD.Print($"[TutorialRunner] Beat '{beat.Id}' matched by player action ({beat.TriggerEvent})");
            OnBeatMatched(beat, state);
        }

        // Update tracking for next check
        _prevAttackCount = currentAttackCount;
        _prevSummonCount = currentSummonCount;
    }

    private int _prevSpellCastCount;

    private void OnBeatMatched(TutorialBeat beat, GameState state)
    {
        // Show popup if one exists
        if (!string.IsNullOrEmpty(beat.Popup))
        {
            _state = RunnerState.ShowingPopup;
            _awaitingDismiss = true;

            ShowPopup(beat.Popup, beat.Highlight);

            // Headless mode: auto-dismiss the popup after a short delay so
            // the tutorial can proceed without human interaction.
            if (_isHeadless && _headlessTimer != null)
            {
                // Use a 2-second delay: enough for the engine to render the popup
                // and capture to be written, then dismiss and advance.
                _headlessTimer.OneShot = true;
                _headlessTimer.Timeout -= OnHeadlessTimerTimeout;
                _headlessTimer.Timeout += AutoDismissPopup;
                _headlessTimer.Start(2.0f);
            }
        }
        else
        {
            // No popup — advance immediately
            AdvanceBeat();
        }
    }

    /// <summary>Headless auto-dismiss: dismiss the popup and re-arm the headless timer.</summary>
    private void AutoDismissPopup()
    {
        if (!_isHeadless) return;
        _headlessTimer.Timeout -= AutoDismissPopup;
        _headlessTimer.Timeout += OnHeadlessTimerTimeout;

        if (_state != RunnerState.ShowingPopup) return;

        // IMPORTANT: Use CallDeferred to avoid Godot crash (propagate_notification)
        // when removing the popup from the scene tree during a timer callback.
        // OnPopupDismissed handles capture + advance after the dismiss.
        var popup = _popup;
        if (popup != null && GodotObject.IsInstanceValid(popup))
        {
            Callable.From(() => popup.Dismiss()).CallDeferred();
        }
    }

    // ── Popup Display ──

    private void ShowPopup(string text, List<string>? highlights)
    {
        if (_popup == null)
        {
            _popup = new TutorialPopup();
            _duelScene.AddChild(_popup);
        }

        _popup.Dismissed -= OnPopupDismissed;
        _popup.Dismissed += OnPopupDismissed;

        var content = new TutorialContent
        {
            PopupId = $"tutorial_{_script?.TutorialId}_b{CurrentBeat?.Id ?? "?"}",
            Title = "Tutorial",
            Text = text,
            ShowSkip = false
        };

        // Resolve highlight string IDs to actual Control nodes from the live layout
        var resolvedHighlights = new List<Control>();
        if (highlights is { Count: > 0 })
        {
            // "all_creatures_highlight" is a magic ID — we handle it first, expanding
            // to all owned player slots, then treat the rest as individual IDs
            bool expandAllCreatures = highlights.Contains("all_creatures_highlight");
            foreach (var id in highlights)
            {
                if (id == "all_creatures_highlight")
                    continue; // handled below
                var ctrl = ResolveHighlight(id);
                if (ctrl != null)
                    resolvedHighlights.Add(ctrl);
            }
            if (expandAllCreatures && _duelScene.TutorialPlayerSlots is { Count: 5 })
            {
                // Add all player lane slots as separate highlights
                foreach (var slot in _duelScene.TutorialPlayerSlots)
                {
                    if (slot != null && GodotObject.IsInstanceValid(slot) && !resolvedHighlights.Contains(slot))
                        resolvedHighlights.Add(slot);
                }
            }
        }

        _popup.HighlightMargins = new Vector2(8, 8);

        // Set highlights BEFORE Show so the popup can render them immediately
        _popup.SetHighlightTargets(resolvedHighlights);
        _popup.Show(content);

        GD.Print($"[TutorialRunner] Popup shown: \"{text}\" ({resolvedHighlights.Count} highlights resolved)");
    }

    /// <summary>
    /// Resolve a highlight string ID to the actual Godot Control node,
    /// using the live duel layout (post-BORDER-1 positions).
    /// </summary>
    private Control? ResolveHighlight(string id)
    {
        // Hand cards: "hand_card_0" through "hand_card_9"
        if (id.StartsWith("hand_card_") && int.TryParse(id.AsSpan("hand_card_".Length), out int handIdx))
        {
            var handCards = _duelScene.TutorialHandCards;
            if (handIdx >= 0 && handIdx < handCards.Count && handCards[handIdx] != null && GodotObject.IsInstanceValid(handCards[handIdx]))
                return handCards[handIdx];
            GD.PrintErr($"[TutorialRunner] Highlight ID '{id}': hand card index {handIdx} out of range ({handCards.Count} cards)");
            return null;
        }

        // Player lane slots: "lane_0" through "lane_4"
        if (id.StartsWith("lane_") && int.TryParse(id.AsSpan("lane_".Length), out int laneIdx))
        {
            var slots = _duelScene.TutorialPlayerSlots;
            if (laneIdx >= 0 && laneIdx < slots.Count && slots[laneIdx] != null && GodotObject.IsInstanceValid(slots[laneIdx]))
                return slots[laneIdx];
            GD.PrintErr($"[TutorialRunner] Highlight ID '{id}': player lane index {laneIdx} out of range");
            return null;
        }

        // Enemy lane slots: "enemy_lane_0" through "enemy_lane_4"
        if (id.StartsWith("enemy_lane_") && int.TryParse(id.AsSpan("enemy_lane_".Length), out int enemyLaneIdx))
        {
            var slots = _duelScene.TutorialEnemySlots;
            if (enemyLaneIdx >= 0 && enemyLaneIdx < slots.Count && slots[enemyLaneIdx] != null && GodotObject.IsInstanceValid(slots[enemyLaneIdx]))
                return slots[enemyLaneIdx];
            return null;
        }

        // End Turn button
        if (id == "end_turn_button")
        {
            var btn = _duelScene.TutorialEndTurnButton;
            if (btn != null && GodotObject.IsInstanceValid(btn))
                return btn;
            GD.PrintErr("[TutorialRunner] Highlight 'end_turn_button': not available");
            return null;
        }

        // Artifact plates: "artifact_sword" → player plate 0, "artifact_shield" → player plate 1
        if (id.StartsWith("artifact_"))
        {
            var plates = _duelScene.TutorialPlayerArtifactPlates;
            int artIdx = id switch
            {
                "artifact_sword" => 0,
                "artifact_shield" => 1,
                "artifact_player_0" => 0,
                "artifact_player_1" => 1,
                "artifact_enemy_0" => 2,
                "artifact_enemy_1" => 3,
                _ => -1
            };
            if (artIdx >= 0 && artIdx < plates.Length)
            {
                if (plates[artIdx] != null && GodotObject.IsInstanceValid(plates[artIdx]))
                    return plates[artIdx];
                // For indices 2-3, enemy plates
                if (artIdx >= 2)
                {
                    var enemyPlates = _duelScene.TutorialEnemyArtifactPlates;
                    int ei = artIdx - 2;
                    if (ei >= 0 && ei < enemyPlates.Length && enemyPlates[ei] != null && GodotObject.IsInstanceValid(enemyPlates[ei]))
                        return enemyPlates[ei];
                }
            }
            GD.PrintErr($"[TutorialRunner] Highlight '{id}': artifact index {artIdx} not available");
            return null;
        }

        // Enemy portrait
        if (id == "enemy_portrait")
        {
            // Find the enemy name label or portrait control in DuelScene
            var duelScene = _duelScene;
            var enemyPortrait = duelScene.GetNodeOrNull<Control>("EnemyPortrait");
            if (enemyPortrait != null && GodotObject.IsInstanceValid(enemyPortrait))
                return enemyPortrait;
            return null;
        }

        // Player portrait
        if (id == "player_portrait")
        {
            var duelScene = _duelScene;
            var playerPortrait = duelScene.GetNodeOrNull<Control>("PlayerPortrait");
            if (playerPortrait != null && GodotObject.IsInstanceValid(playerPortrait))
                return playerPortrait;
            return null;
        }

        GD.PrintErr($"[TutorialRunner] Unknown highlight ID: '{id}'");
        return null;
    }

    private void OnPopupDismissed()
    {
        _awaitingDismiss = false;

        // Capture screenshot at this beat boundary
        CaptureCurrentBeat();

        if (_state == RunnerState.ShowingPopup)
        {
            AdvanceBeat();
        }
    }

    private void AdvanceBeat()
    {
        _currentBeatIndex++;
        var turn = CurrentTurn;
        if (turn?.PlayerBeats == null || _currentBeatIndex >= turn.PlayerBeats.Count)
        {
            // All beats for this turn complete — resume normal play
            _state = RunnerState.PlayerTurn;
            ClearActionRestrictions();
            GD.Print("[TutorialRunner] All beats complete for this turn — free play until end turn");
        }
        else
        {
            // Reset state to PlayerTurn so the headless timer can fire for the next beat
            _state = RunnerState.PlayerTurn;
            ClearActionRestrictions();
            EnterBeat();
        }
    }

    // ── Action Restrictions ──

    private void ApplyActionRestrictions(List<string> allowedActions)
    {
        // Disable End Turn button if END_TURN not allowed
        if (_duelScene != null && !allowedActions.Contains("END_TURN") && !allowedActions.Contains("ANY"))
        {
            _duelScene.SetEndTurnEnabled(false);
        }

        GD.Print($"[TutorialRunner] Action restrictions: [{string.Join(", ", allowedActions)}]");
    }

    private void ClearActionRestrictions()
    {
        if (_duelScene != null)
            _duelScene.SetEndTurnEnabled(true);
    }

    // ── Opponent Scripted Actions ──

    private void ScheduleNextOpponentAction()
    {
        var turn = CurrentTurn;
        if (turn?.OpponentActions == null || _opponentActionIndex >= turn.OpponentActions.Count)
        {
            // All actions done — end opponent turn
            _state = RunnerState.OpponentEndTurn;
            GD.Print("[TutorialRunner] Opponent actions complete — letting bot finish");
            return;
        }

        var action = turn.OpponentActions[_opponentActionIndex];
        int delay = action.DelayMs ?? 1200;
        _actionTimer?.Start(delay / 1000f);
    }

    private void OnActionTimerTimeout()
    {
        var turn = CurrentTurn;
        if (turn?.OpponentActions == null) return;

        var action = turn.OpponentActions[_opponentActionIndex];
        GD.Print($"[TutorialRunner] Opponent action: {action.Action}");

        try
        {
            ExecuteScriptedAction(action);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TutorialRunner] Error executing opponent action: {ex.Message}");
        }

        _opponentActionIndex++;
        ScheduleNextOpponentAction();
    }

    private void ExecuteScriptedAction(ScriptedAction action)
    {
        var state = _gsm.State;
        if (state == null) return;

        switch (action.Action)
        {
            case "SUMMON":
                if (action.CardId != null && action.Lane.HasValue)
                {
                    _gsm.TryPlayCard(1, action.CardId, action.Lane.Value);
                }
                break;

            case "ATTACK":
                if (action.Lane.HasValue && action.TargetLane.HasValue)
                {
                    _gsm.TryAttack(1, action.Lane.Value, action.TargetLane.Value);
                }
                break;

            case "PLAY_SPELL":
                if (action.CardId != null && action.Lane.HasValue)
                {
                    _gsm.TryPlayCard(1, action.CardId, action.Lane.Value);
                }
                else if (action.CardId != null)
                {
                    // Find playable spell in opponent's hand
                    var hand = state.Players[1].Hand;
                    var card = hand.FirstOrDefault(c => c.CardDefId == action.CardId);
                    if (card != null)
                    {
                        // Auto-target lane 0 if not specified
                        _gsm.TryPlayCard(1, action.CardId, action.Lane ?? 0);
                    }
                }
                break;

            case "END_TURN":
                _gsm.TryEndTurn();
                break;

            case "NO_OP":
                break;
        }
    }

    // ── Headless Auto-play ──

    private void OnHeadlessTimerTimeout()
    {
        if (!_isHeadless || _state != RunnerState.PlayerTurn) return;

        var beat = CurrentBeat;
        if (beat == null) return;

        GD.Print($"[TutorialRunner] Headless auto-play: beat '{beat.Id}' ({beat.TriggerEvent})");

        switch (beat.TriggerEvent)
        {
            case "SUMMON_CREATURE":
                AutoPlaySummon();
                break;

            case "ATTACK_WITH_CREATURE":
                AutoPlayAttack();
                break;

            case "END_TURN":
            case "NO_ATTACK_END_TURN":
                AutoPlayEndTurn();
                break;

            default:
                AutoPlayEndTurn();
                break;
        }
    }

    private void AutoPlaySummon()
    {
        var state = _gsm.State;
        if (state == null) return;

        var hand = state.Players[0].Hand;
        var player = state.Players[0];

        // Find the first playable card (cost ≤ attunement) and an open lane
        for (int li = 0; li < 5; li++)
        {
            if (player.Lanes[li].Occupant != null) continue;
            if (_headlessSummonedLanes.Contains(li)) continue;

            // Find cheapest card we can play
            CardInstance? best = null;
            foreach (var card in hand)
            {
                if (card.Cost <= player.Attunement)
                {
                    if (best == null || card.Cost < best.Cost)
                        best = card;
                }
            }

            if (best != null)
            {
                _headlessSummonedLanes.Add(li);
                _duelScene.PlayerSummonCard(best.CardDefId, li);
                return;
            }
        }

        // Can't summon anything — end turn instead
        AutoPlayEndTurn();
    }

    private void AutoPlayAttack()
    {
        var state = _gsm.State;
        if (state == null) return;

        var player = state.Players[0];
        var enemy = state.Players[1];

        // Find first unexhausted creature that hasn't attacked
        for (int li = 0; li < 5; li++)
        {
            var occ = player.Lanes[li].Occupant;
            if (occ != null && !occ.IsExhausted && !occ.HasAttackedThisTurn)
            {
                // In the altar arc layout, lane N attacks lane N (opposing lane).
                // If there's a creature at the same lane index, fight it.
                // Otherwise, hit face at that lane index.
                _duelScene.PlayerAttack(li, li);
                return;
            }
        }

        // No attacker available — end turn
        AutoPlayEndTurn();
    }

    private void AutoPlayEndTurn()
    {
        // Call TryEndTurn directly. Although this is called from a timer callback,
        // the headless tutorial's opponent and player turns are driven by TutorialRunner
        // via _pendingAdvance / _Process, which handles re-entrancy safely.
        _gsm.TryEndTurn();
    }

    // ── Overrides ──

    private void ApplyTurnOverrides()
    {
        var turn = CurrentTurn;
        if (turn == null) return;

        // Hand overrides
        if (turn.PlayerHandOverride is { Count: > 0 } && !_handOverriddenThisTurn)
        {
            ApplyHandOverride(0, turn.PlayerHandOverride);
            _handOverriddenThisTurn = true;
        }
        if (turn.OpponentHandOverride is { Count: > 0 })
        {
            ApplyHandOverride(1, turn.OpponentHandOverride);
        }

        // Attunement override
        if (turn.PlayerAttunementOverride.HasValue && !_attunementOverriddenThisTurn)
        {
            ApplyAttunementOverride(0, turn.PlayerAttunementOverride.Value);
            _attunementOverriddenThisTurn = true;
        }
    }

    private void ApplyHandOverride(int playerIndex, List<string> cardIds)
    {
        var state = _gsm.State;
        if (state == null) return;

        var player = state.Players[playerIndex];

        // Move all current hand cards to deck
        foreach (var card in player.Hand.ToList())
        {
            card.Zone = Zone.Deck;
            player.Deck.Add(card);
        }
        player.Hand.Clear();

        // Create new card instances for the override
        foreach (var cardId in cardIds)
        {
            var def = CardRegistry.Get(cardId);
            if (def == null)
            {
                GD.PrintErr($"[TutorialRunner] Unknown card in override: {cardId}");
                continue;
            }

            var instance = new CardInstance(state.NextInstanceId++, cardId, playerIndex)
            {
                CardType = def.Type,
                Cost = def.Cost,
                Strata = def.Strata,
                BaseAttack = def.Attack ?? 0,
                BaseVigor = def.Vigor ?? 0,
                Zone = Zone.Hand,
            };
            instance.Keywords.AddRange(def.Keywords);

            player.Hand.Add(instance);
        }

        // Remove the overridden cards from the deck (they were just created fresh)
        // But keep deck balanced — remove extras beyond override amount
        GD.Print($"[TutorialRunner] Hand override: P{playerIndex} set to {cardIds.Count} cards");

        // Force a re-render
        _gsm.NotifyStateChanged();
    }

    private void ApplyAttunementOverride(int playerIndex, int attunement)
    {
        var state = _gsm.State;
        if (state == null) return;

        var player = state.Players[playerIndex];
        player.Attunement = attunement;
        player.AttunementMax = Math.Max(player.AttunementMax, attunement);

        GD.Print($"[TutorialRunner] Attunement override: P{playerIndex} set to {attunement}");
        _gsm.NotifyStateChanged();
    }

    private void SkipMulligan()
    {
        if (_gsm.State == null) return;
        if (!_gsm.State.Players[0].HasMulliganed)
            _gsm.PerformMulligan(0, new List<int>());
        if (!_gsm.State.Players[1].HasMulliganed)
            _gsm.PerformMulligan(1, new List<int>());
    }

    // ── Capture ──

    private void CaptureCurrentBeat()
    {
        if (_gsm.State == null) return;

        var beat = CurrentBeat;
        string beatId = beat?.Id ?? "unknown";
        string turnStr = _gsm.State.TurnNumber.ToString();
        string filename = $"{_tutorialCapturePrefix}{_script?.TutorialId ?? "unknown"}_t{turnStr}_{beatId}";

        try
        {
            var img = _duelScene.GetViewport().GetTexture().GetImage();
            if (img != null)
            {
                string pngPath = $"{_captureDir}/{filename}.png";
                img.SavePng(pngPath);
                GD.Print($"[TutorialRunner] Capture saved: {pngPath}");

                // Write companion meta
                WriteCaptureMeta(filename, beatId);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TutorialRunner] Capture failed: {ex.Message}");
        }
    }

    private void WriteCaptureMeta(string filename, string beatId)
    {
        if (_gsm.State == null) return;

        var meta = new StringBuilder();
        meta.Append("{\n");
        meta.Append($"  \"tutorial_id\": \"{_script?.TutorialId ?? "?"}\",\n");
        meta.Append($"  \"beat_id\": \"{beatId}\",\n");
        meta.Append($"  \"turn\": {_gsm.State.TurnNumber},\n");
        meta.Append($"  \"title\": \"{_script?.Title ?? "?"}\"\n");
        meta.Append("}\n");

        string metaPath = $"{_captureDir}/{filename}.meta.json";
        using (var writer = new System.IO.StreamWriter(metaPath))
        {
            writer.Write(meta.ToString());
        }
        GD.Print($"[TutorialRunner] Meta saved: {metaPath}");
    }

    // ── End ──

    private void EndTutorial()
    {
        GD.Print("[TutorialRunner] Tutorial complete");
        _state = RunnerState.Finished;
        _duelInitialized = false;

        ClearActionRestrictions();

        // Capture the final state as the gate-named capture (tutorial_warrior_intro.png)
        if (_isHeadless)
        {
            CaptureGateCapture();
        }

        // Re-enable the bot for any remaining play
        _bot.Resume();

        TutorialFinished?.Invoke();

        // In headless mode, quit the application after tutorial finishes
        if (_isHeadless)
        {
            GD.Print("[TutorialRunner] Headless tutorial complete — quitting.");
            Callable.From(() => _duelScene.GetTree().Quit()).CallDeferred();
        }
    }

    /// <summary>Capture a gate-named screenshot (eg tutorial_warrior_intro.png) for the gate validator.</summary>
    private void CaptureGateCapture()
    {
        try
        {
            var img = _duelScene.GetViewport().GetTexture().GetImage();
            if (img != null)
            {
                string id = _script?.TutorialId ?? "unknown";
                string gatePrefix = $"tutorial_{id}";
                string pngPath = $"{_captureDir}/{gatePrefix}.png";
                img.SavePng(pngPath);
                GD.Print($"[TutorialRunner] Gate capture saved: {pngPath}");

                var meta = new StringBuilder();
                meta.Append("{\n");
                meta.Append($"  \"tutorial_id\": \"{_script?.TutorialId ?? "?"}\",\n");
                meta.Append($"  \"beat_id\": \"complete\",\n");
                meta.Append($"  \"turn\": {_gsm.State.TurnNumber},\n");
                meta.Append($"  \"title\": \"{_script?.Title ?? "?"}\",\n");
                meta.Append($"  \"capture_type\": \"{gatePrefix}\"\n");
                meta.Append("}\n");

                string metaPath = $"{_captureDir}/{gatePrefix}.meta.json";
                using (var writer = new System.IO.StreamWriter(metaPath))
                {
                    writer.Write(meta.ToString());
                }
                GD.Print($"[TutorialRunner] Gate meta saved: {metaPath}");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[TutorialRunner] Gate capture failed: {ex.Message}");
        }
    }
}
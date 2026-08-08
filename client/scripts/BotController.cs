using System.Linq;
using Godot;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Sim;

namespace Runewake.Client;

/// <summary>
/// Controls the AI opponent's turns with a think-delay.
/// When triggered, uses GreedyBot to choose and apply actions
/// one at a time with visual spacing between each action.
/// </summary>
public partial class BotController : Node
{
    private GameStateManager? _gsm;
    private Godot.Timer? _timer;
    private readonly GreedyBot _bot = new();

    /// <summary>Delay before the bot makes its first action each turn (seconds).</summary>
    public float ThinkDelay { get; set; } = 1.5f;

    /// <summary>Delay between consecutive bot actions in the same turn (seconds).</summary>
    public float ActionInterval { get; set; } = 0.6f;

    /// <summary>True while the bot is actively taking its turn.</summary>
    public bool IsThinking { get; private set; }

    /// <summary>
    /// Suspends the bot's turn processing. Stops any pending timer and prevents
    /// the bot from reacting to StateChanged. Call Resume() to re-enable.
    /// </summary>
    public void Suspend()
    {
        IsThinking = true;
        _pendingAction = false;
        _timer?.Stop();
    }

    /// <summary>
    /// Resumes normal bot turn processing.
    /// </summary>
    public void Resume()
    {
        IsThinking = false;
    }

    private bool _pendingAction;

    /// <summary>Raised when the bot's turn begins.</summary>
    public event System.Action? BotTurnStarted;

    /// <summary>Raised when the bot's turn ends.</summary>
    public event System.Action? BotTurnEnded;

    public override void _Ready()
    {
        _timer = new Godot.Timer();
        _timer.OneShot = true;
        _timer.Timeout += OnTimerTimeout;
        AddChild(_timer);
    }

    public override void _ExitTree()
    {
        // Stop the timer and unsubscribe from all events to prevent
        // the timer callback from firing after the bot is freed (signal 11).
        _timer?.Stop();
        _timer = null;
        if (_gsm != null)
        {
            _gsm.StateChanged -= OnStateChanged;
            _gsm = null;
        }
    }

    /// <summary>
    /// Initialize with a GameStateManager to dispatch actions through.
    /// </summary>
    public void Initialize(GameStateManager gsm)
    {
        _gsm = gsm;
        _gsm.StateChanged += OnStateChanged;
    }

    private void OnStateChanged()
    {
        if (_gsm == null || _gsm.IsGameOver) return;

        // If it's the enemy's turn, start the bot thinking
        if (_gsm.CurrentPlayerIndex == 1 && !IsThinking)
        {
            GD.Print($"[BotController] Turn {_gsm.TurnNumber}: StateChanged → P1 turn detected, starting bot turn");
            StartBotTurn();
        }
    }

    private void StartBotTurn()
    {
        GD.Print($"[BotController] Turn {_gsm.TurnNumber}: Bot turn STARTING (ThinkDelay={ThinkDelay}s)");
        IsThinking = true;
        _pendingAction = true;
        BotTurnStarted?.Invoke();
        _timer?.Start(ThinkDelay);
    }

    private void OnTimerTimeout()
    {
        try
        {
            if (_gsm == null || !_pendingAction) return;

            var state = _gsm.State;
            if (state.IsGameOver || state.CurrentPlayerIndex != 1)
            {
                GD.Print($"[BotController] Turn ended or not P1 (P={state.CurrentPlayerIndex}), ending bot turn");
                EndBotTurn();
                return;
            }

            // Bot chooses an action
            var action = _bot.ChooseAction(state, 1);
            if (action == null)
            {
                GD.Print($"[BotController] WARNING: _bot.ChooseAction returned null — ending bot turn without calling TryEndTurn!");
                EndBotTurn();
                return;
            }

            // Dispatch the chosen action
            if (action is EndTurnAction)
            {
                GD.Print($"[BotController] Bot chose EndTurnAction — calling TryEndTurn()");
                var result = _gsm.TryEndTurn();
                if (!result.Success)
                {
                    GD.PrintErr($"[BotController] TryEndTurn FAILED: {result.ErrorMessage}");
                    // If end turn fails, force-invoke state unchanged so the game
                    // loops back and tries again. Don't call EndBotTurn — the engine
                    // still thinks it's P1's turn.
                    return;
                }
                EndBotTurn();
            }
            else if (action is PlayCardAction play)
            {
                var player = state.Players[1];
                var card = player.Hand.FirstOrDefault(c => c.InstanceId == play.CardInstanceId);
                if (card != null)
                {
                    GD.Print($"[BotController] Bot plays card '{card.CardDefId}' to lane {play.LaneIndex}");
                    var result = _gsm.TryPlayCard(1, card.CardDefId, play.LaneIndex ?? 0);
                    if (!result.Success)
                        GD.PrintErr($"[BotController] TryPlayCard FAILED: {result.ErrorMessage}");
                }
                else
                {
                    GD.Print($"[BotController] WARNING: Bot tried to play card instance {play.CardInstanceId} but not found in hand");
                }
                ScheduleNext();
            }
            else if (action is AttackAction attack)
            {
                GD.Print($"[BotController] Bot attacks: lane {attack.SourceLane} → target {attack.TargetLane}");
                var result = _gsm.TryAttack(1, attack.SourceLane, attack.TargetLane ?? attack.SourceLane);
                if (!result.Success)
                    GD.PrintErr($"[BotController] TryAttack FAILED: {result.ErrorMessage}");
                ScheduleNext();
            }
            else
            {
                GD.Print($"[BotController] Unknown action type {action.GetType().Name} — ending turn");
                EndBotTurn();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[BotController] CRASH in OnTimerTimeout: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            // Don't leave the bot hanging — end its turn so the game can progress
            EndBotTurn();
        }
    }

    private void ScheduleNext()
    {
        if (_gsm.State.CurrentPlayerIndex == 1 && !_gsm.IsGameOver)
        {
            GD.Print($"[BotController] Scheduling next bot action in {ActionInterval}s");
            _timer?.Start(ActionInterval);
        }
        else
        {
            GD.Print($"[BotController] Bot turn done (P={_gsm.State.CurrentPlayerIndex}), ending bot turn");
            EndBotTurn();
        }
    }

    private void EndBotTurn()
    {
        GD.Print($"[BotController] Bot turn ENDED (IsThinking=false)");
        IsThinking = false;
        _pendingAction = false;
        _timer?.Stop();
        BotTurnEnded?.Invoke();
    }
}
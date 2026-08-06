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
            StartBotTurn();
        }
    }

    private void StartBotTurn()
    {
        IsThinking = true;
        _pendingAction = true;
        BotTurnStarted?.Invoke();
        _timer?.Start(ThinkDelay);
    }

    private void OnTimerTimeout()
    {
        if (_gsm == null || !_pendingAction) return;

        var state = _gsm.State;
        if (state.IsGameOver || state.CurrentPlayerIndex != 1)
        {
            EndBotTurn();
            return;
        }

        // Bot chooses an action
        var action = _bot.ChooseAction(state, 1);
        if (action == null)
        {
            EndBotTurn();
            return;
        }

        // Dispatch the chosen action
        if (action is EndTurnAction)
        {
            _gsm.TryEndTurn();
            EndBotTurn();
        }
        else if (action is PlayCardAction play)
        {
            var player = state.Players[1];
            var card = player.Hand.FirstOrDefault(c => c.InstanceId == play.CardInstanceId);
            if (card != null)
            {
                _gsm.TryPlayCard(1, card.CardDefId, play.LaneIndex ?? 0);
            }
            ScheduleNext();
        }
        else if (action is AttackAction attack)
        {
            _gsm.TryAttack(1, attack.SourceLane, attack.TargetLane ?? attack.SourceLane);
            ScheduleNext();
        }
        else
        {
            EndBotTurn();
        }
    }

    private void ScheduleNext()
    {
        if (_gsm.State.CurrentPlayerIndex == 1 && !_gsm.IsGameOver)
            _timer?.Start(ActionInterval);
        else
            EndBotTurn();
    }

    private void EndBotTurn()
    {
        IsThinking = false;
        _pendingAction = false;
        _timer?.Stop();
        BotTurnEnded?.Invoke();
    }
}
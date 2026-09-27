using Godot;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// FABLE-038: binds a DuelScene to the online match this phone is in.
///
/// PvP: the bot is detached, the GameStateManager is told which SEAT this phone plays (so
/// "me" is drawn at the bottom whichever seat that is), the player's moves go to the match
/// instead of straight into the engine, and moves that arrive from the other phone are pushed
/// into the GameStateManager as new state. A strip at the top says whose turn it is.
///
/// Co-op: the existing CoopOverlay does the board work (each player has their own board);
/// this overlay only pumps the wire.
/// </summary>
public partial class OnlineOverlay : Control
{
    private OnlineMatch _m = null!;
    private GameStateManager _gsm = null!;
    private Label _strip = null!;
    private bool _finished;

    public static OnlineOverlay? Attach(Control duel, GameStateManager gsm, BotController bot)
    {
        var m = OnlineMatch.Current;
        if (m == null) return null;
        var o = new OnlineOverlay { _m = m, _gsm = gsm, Name = "OnlineOverlay", MouseFilter = MouseFilterEnum.Ignore, ZIndex = 55 };
        o.SetAnchorsPreset(LayoutPreset.FullRect);
        if (m.Pvp != null)
        {
            bot.Detach();
            gsm.LocalSeat = m.LocalSeat;
            gsm.ActionSink = a => m.SubmitLocal(a);
            gsm.ConcedeSink = () => m.ConcedeLocal();
            m.Pvp.RemoteApplied += () => o.CallDeferred(nameof(OnlineOverlay.PushState));
            gsm.SetState(m.Pvp.State);
        }
        duel.AddChild(o);
        DuelScene.ExitTrace($"online attached: {m.Kind} seat {m.LocalSeat} vs {m.OpponentName}");
        return o;
    }

    public override void _Ready()
    {
        var vp = GetViewportRect().Size;
        float s = vp.Y / 1080f;
        _strip = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            Position = new Vector2(vp.X / 2f - 330f * s, 118f * s), Size = new Vector2(660f * s, 44f * s),
        };
        _strip.AddThemeFontOverride("font", ThemeTokens.GetBodyFont((int)(26 * s)));
        _strip.AddThemeFontSizeOverride("font_size", (int)(26 * s));
        _strip.AddThemeColorOverride("font_color", ThemeTokens.Gold);
        _strip.AddThemeConstantOverride("outline_size", (int)(6 * s));
        _strip.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        AddChild(_strip);
    }

    /// <summary>Main thread: the remote side changed the duel.</summary>
    private void PushState()
    {
        if (_m.Pvp == null || !IsInstanceValid(_gsm)) return;
        _gsm.SetState(_m.Pvp.State);
    }

    public override void _Process(double delta)
    {
        if (_finished) return;
        _m.Pump(delta);
        _strip.Text = _m.Status;
        _strip.Modulate = _m.Pvp?.IsMyTurn == true ? ThemeTokens.Gold : new Color(0.85f, 0.82f, 0.74f);
        if (_m.Pvp != null && _m.Pvp.State.IsGameOver)
        {
            _finished = true;
            _gsm.ActionSink = null;
            _gsm.ConcedeSink = null;
            _m.ReportResult(_m.Pvp.State.WinnerIndex == _m.LocalSeat);
        }
    }

    public override void _ExitTree()
    {
        // Leaving the duel scene by any road is leaving the match.
        if (OnlineMatch.Current == _m) _m.LeaveMatch();
    }
}

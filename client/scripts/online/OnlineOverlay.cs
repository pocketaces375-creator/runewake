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
    private Control? _brokenPanel;

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
        if (_m.Broken && _brokenPanel == null) ShowBroken();
        if (_m.Pvp != null && _m.Pvp.State.IsGameOver)
        {
            _finished = true;
            _gsm.ActionSink = null;
            _gsm.ConcedeSink = null;
            _m.ReportResult(_m.Pvp.State.WinnerIndex == _m.LocalSeat);
        }
    }

    /// <summary>
    /// FABLE-042: the two games disagree — nothing either player does can fix it, so say so
    /// plainly and offer the way out, instead of leaving a board that silently stops answering.
    /// </summary>
    private void ShowBroken()
    {
        DuelScene.ExitTrace($"online: match broken — {(_m.Pvp!.VersionMismatch ? "version mismatch at hello" : "desync")} after {_m.Pvp.MovesApplied} moves");
        var vp = GetViewportRect().Size;
        float s = vp.Y / 1080f;
        var root = new Control { MouseFilter = MouseFilterEnum.Stop, ZIndex = 200 };
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.70f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddChild(dim);
        float pw = 1100f * s, ph = 460f * s;
        var panel = new PanelContainer { Position = new Vector2((vp.X - pw) / 2, (vp.Y - ph) / 2), Size = new Vector2(pw, ph) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.082f, 0.072f, 0.061f, 0.97f), BorderColor = new Color(0.88f, 0.45f, 0.35f, 0.8f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 18, CornerRadiusTopRight = 18, CornerRadiusBottomLeft = 18, CornerRadiusBottomRight = 18,
            ContentMarginLeft = 48 * s, ContentMarginRight = 48 * s, ContentMarginTop = 40 * s, ContentMarginBottom = 40 * s,
        });
        root.AddChild(panel);
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        col.AddThemeConstantOverride("separation", (int)(22 * s));
        panel.AddChild(col);
        Label L(string t, Font f, int size, Color c)
        {
            var l = new Label { Text = t, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            l.AddThemeFontOverride("font", f); l.AddThemeFontSizeOverride("font_size", size); l.AddThemeColorOverride("font_color", c);
            col.AddChild(l); return l;
        }
        L(_m.Pvp.VersionMismatch ? "THESE GAMES DON'T MATCH" : "THE MATCH FELL OUT OF STEP", ThemeTokens.GetHeaderFont((int)(40 * s)), (int)(40 * s), Color.FromHtml("#E0865A"));
        L(_m.BrokenReason, ThemeTokens.GetBodyFont((int)(30 * s)), (int)(30 * s), new Color(0.91f, 0.86f, 0.78f));
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        col.AddChild(row);
        var leave = new Button { Text = "Leave match", CustomMinimumSize = new Vector2(380 * s, 88 * s), FocusMode = FocusModeEnum.None };
        leave.AddThemeFontOverride("font", ThemeTokens.GetButtonFont((int)(32 * s))); leave.AddThemeFontSizeOverride("font_size", (int)(32 * s));
        leave.AddThemeColorOverride("font_color", Color.FromHtml("#F2DFA6"));
        leave.AddThemeStyleboxOverride("normal", MenuButtons.PrimaryNormal());
        leave.AddThemeStyleboxOverride("hover", MenuButtons.PrimaryHover());
        leave.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed());
        leave.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        leave.Pressed += () => GetTree().ChangeSceneToFile(OnlineLobbyScene.ScenePath);
        row.AddChild(leave);
        AddChild(root);
        _brokenPanel = root;
    }

    public override void _ExitTree()
    {
        // Leaving the duel scene by any road is leaving the match.
        if (OnlineMatch.Current == _m) _m.LeaveMatch();
    }
}

using System;
using System.Linq;
using Godot;
using Runewake.Engine.Coop;

namespace Runewake.Client;

/// <summary>
/// FABLE-020: the co-op layer on top of a duel — Trikzos's TFT-style tabs.
///
///   * A tab per Delver down the left edge: YOU, then each ally, with their Vigor and whether
///     they're still acting, have ended their turn (✓), are knocked out (☠) or gave up (⚑).
///   * Tap an ally's tab to watch their board; tap YOUR tab (or the banner) to come back.
///   * Your moves go to the expedition, not straight into the engine; when everyone has ended
///     their turn, every board's enemy takes its turn and (in a raid) the shared pool settles.
///   * If you're knocked out, your team fights on — the view moves to an ally and the result
///     comes when the expedition decides.
///
/// FABLE-COOP-1 (Trikzos: "Players names are floating in space if artifacts … When I opened my
/// friends board the game crashed … make this look more like our current board … No concede
/// button"):
///   * The tabs live in the empty band of the left edge BETWEEN the two artifact pairs, sized so
///     up to five fit above the Concede plate. Nothing sits on the artifacts any more.
///   * Watching an ally is drawn by the duel scene itself (DuelScene.Spectate): the real lanes and
///     card faces, their artifacts and the enemy's, the Crescent Dial with their Vigor and
///     Attunement, and their hand. The old stand-in panel that rebuilt ~40 nodes every frame is gone.
///   * Concede gives up your board through the expedition (every phone applies it in the same
///     place), then shows you the loss; your allies fight on.
///   * Every frame is guarded: anything that throws is written to the exit trace (the "Screenshot
///     this for Fable" panel) instead of taking the fight down.
///
/// Attached by DuelScene when CoopSession.Current is set. Touches DuelScene only through
/// GameStateManager (state + action sink), BotController.Detach and DuelScene.Spectate.
/// </summary>
public partial class CoopOverlay : Control
{
    private CoopSession _s = null!;
    private GameStateManager _gsm = null!;
    private DuelScene? _duel;
    private VBoxContainer _tabs = null!;
    private Control _blocker = null!;
    private Button _watchBanner = null!;
    private int _viewing;
    private int _lastRound;
    private double _aiClock;
    private bool _finished;
    private bool _faulted;
    private Label _roundLabel = null!;
    private Label? _banner;
    private float _k = 1f;   // viewport height / 1080

    // ── the left band between the artifact pairs (design px at 1080) ─────────────────
    // Enemy artifacts: 37..259 (+ the slot word above). Yours: 834..1056 (+ the word at ~820).
    private const float BandTop = 272f;
    private const float BandFoot = 34f + 222f + 24f;   // from the bottom: artifacts + word + gap
    private const float BandX = 22f, BandW = 290f;

    /// <summary>Bottom of the tab band in screen px — DuelScene sits the co-op Concede plate on it.</summary>
    public static float BandBottom(float viewportHeight, float k) => viewportHeight - BandFoot * k;

    /// <summary>The fight is still on and this phone hasn't given up yet.</summary>
    public bool CanConcede => !_finished && _s.Expedition.Outcome == ExpeditionOutcome.Running && !_s.Expedition.HasConceded(_s.LocalSeat);

    public static CoopOverlay? Attach(Control duel, GameStateManager gsm, BotController bot)
    {
        var s = CoopSession.Current;
        if (s == null) return null;
        bot.Detach();
        gsm.DeferGameOver = true;
        gsm.ActionSink = a => s.SubmitLocal(a);
        gsm.SetState(s.Local.State);
        var o = new CoopOverlay { _s = s, _gsm = gsm, _duel = duel as DuelScene, Name = "CoopOverlay", MouseFilter = MouseFilterEnum.Ignore, ZIndex = 50 };
        o.SetAnchorsPreset(LayoutPreset.FullRect);
        duel.AddChild(o);
        DuelScene.ExitTrace($"co-op attached: {s.Title}, {s.Expedition.Boards.Count} Delvers");
        return o;
    }

    public override void _Ready()
    {
        _viewing = _s.LocalSeat;
        _lastRound = _s.Expedition.Round;
        var vp = GetViewportRect().Size;
        _k = vp.Y / 1080f;

        // Swallows every tap on the board while you watch an ally (nothing of theirs is yours to play).
        _blocker = new Control { Name = "WatchBlocker", MouseFilter = MouseFilterEnum.Stop, Visible = false };
        _blocker.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_blocker);

        float top = BandTop * _k, bottom = BandBottom(vp.Y, _k) - 60f * _k - 10f * _k;   // Concede plate below
        _roundLabel = new Label
        {
            Position = new Vector2(BandX * _k, top), Size = new Vector2(BandW * _k, 34f * _k),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _roundLabel.AddThemeFontOverride("font", ThemeTokens.GetHeaderFont((int)(24 * _k)));
        _roundLabel.AddThemeFontSizeOverride("font_size", (int)(24 * _k));
        _roundLabel.AddThemeColorOverride("font_color", ThemeTokens.Gold);
        _roundLabel.AddThemeConstantOverride("outline_size", (int)(6 * _k));
        _roundLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        AddChild(_roundLabel);

        int n = Math.Max(1, _s.Expedition.Boards.Count);
        float tabsTop = top + 40f * _k;
        float gap = 8f * _k;
        float tabH = Math.Min(96f * _k, (bottom - tabsTop - (n - 1) * gap) / n);
        _tabs = new VBoxContainer { Position = new Vector2(BandX * _k, tabsTop), Size = new Vector2(BandW * _k, bottom - tabsTop), MouseFilter = MouseFilterEnum.Ignore };
        _tabs.AddThemeConstantOverride("separation", (int)gap);
        AddChild(_tabs);
        foreach (var b in _s.Expedition.Boards) _tabs.AddChild(MakeTab(b.Seat.Seat, tabH));

        // "Watching X — back to my board": across the top, a big target.
        // ZIndex: above the enemy's hand fan, which it covers while you watch (the Dial still counts it).
        _watchBanner = new Button { Name = "WatchBanner", FocusMode = FocusModeEnum.None, Visible = false, MouseFilter = MouseFilterEnum.Stop, ZIndex = 300 };
        float bw = 900f * _k, bh = 70f * _k;
        _watchBanner.Position = new Vector2((vp.X - bw) / 2f, 14f * _k);
        _watchBanner.Size = new Vector2(bw, bh);
        _watchBanner.CustomMinimumSize = new Vector2(bw, bh);
        _watchBanner.AddThemeFontOverride("font", ThemeTokens.GetButtonFont((int)(28 * _k)));
        _watchBanner.AddThemeFontSizeOverride("font_size", (int)(28 * _k));
        _watchBanner.AddThemeColorOverride("font_color", Color.FromHtml("#F2DFA6"));
        _watchBanner.AddThemeColorOverride("font_hover_color", Colors.White);
        var bannerBox = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.07f, 0.09f, 0.92f), BorderColor = new Color(ThemeTokens.Gold.R, ThemeTokens.Gold.G, ThemeTokens.Gold.B, 0.9f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
        };
        _watchBanner.AddThemeStyleboxOverride("normal", bannerBox);
        _watchBanner.AddThemeStyleboxOverride("hover", bannerBox);
        _watchBanner.AddThemeStyleboxOverride("pressed", bannerBox);
        _watchBanner.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        _watchBanner.Pressed += () => Guard("banner", CloseViewer);
        AddChild(_watchBanner);

        RefreshTabs();
    }

    public override void _ExitTree()
    {
        if (CoopSession.Current == _s && _finished) CoopSession.Current = null;
    }

    public override void _Process(double delta)
    {
        if (_finished || _faulted) return;
        Guard("frame", () => Tick(delta));
    }

    /// <summary>Runs <paramref name="a"/>; if it throws, the trace says where and the watch view stands down.</summary>
    private void Guard(string where, Action a)
    {
        try { a(); }
        catch (Exception ex)
        {
            GD.PrintErr($"[Coop] {where} threw: {ex}");
            DuelScene.ExitTrace($"co-op {where} THREW: {ex.GetType().Name}: {ex.Message} @ {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
            try { CloseViewer(); } catch { /* already reported */ }
            if (where == "frame") _faulted = true;   // one report, then stop ticking rather than flood the trace
        }
    }

    private void Tick(double delta)
    {
        var x = _s.Expedition;

        // AI allies act a move at a time so their tabs visibly play out.
        _aiClock += delta;
        if (_aiClock > 0.18)
        {
            _aiClock = 0;
            _s.StepAi();
        }

        if (x.Round != _lastRound || x.Outcome != ExpeditionOutcome.Running)
        {
            _lastRound = x.Round;
            // The round resolved: the enemy acted on every board. Show ours.
            _gsm.SetState(_s.Local.State);
            if (_s.Local.IsOut && x.Outcome == ExpeditionOutcome.Running && _viewing == _s.LocalSeat)
            {
                Banner("You're down — your team fights on.");
                var ally = x.Boards.FirstOrDefault(b => b.Active);
                if (ally != null) View(ally.Seat.Seat);
            }
        }

        if (x.Outcome != ExpeditionOutcome.Running)
        {
            Finish(x.Outcome == ExpeditionOutcome.Victory, $"co-op finished: {x.Outcome} in round {x.Round}, pool {x.PoolRemaining}");
            return;
        }
        RefreshTabs();
        if (_viewing != _s.LocalSeat)
        {
            var b = x.Board(_viewing);
            _duel?.Spectate(b.State, b.Seat.DisplayName);
        }
    }

    private void Finish(bool won, string trace)
    {
        _finished = true;
        CloseViewer();
        RefreshTabs();
        DuelScene.ExitTrace(trace);
        // FABLE-COOP-1: the end screen is built from the GameStateManager's own state, so hand it a
        // COPY of this board that says how the fight ended for you — the team's win when an ally
        // won it, your loss when you gave up. (Before, a win on someone else's board, or a concede,
        // raised game-over on a board that wasn't over, and no end screen came.) A copy, because
        // the expedition's boards must never be edited here.
        _gsm.ActionSink = null;
        _gsm.DeferGameOver = true;
        var mine = _s.Local.State.Clone();
        mine.IsGameOver = true;
        mine.WinnerIndex = won ? 0 : 1;
        _gsm.SetState(mine);
        _gsm.DeferGameOver = false;
        _gsm.RaiseGameOver(won ? 0 : 1);
    }

    /// <summary>
    /// FABLE-COOP-1: the player gave up (DuelScene's Concede). Their board drops out of the
    /// expedition — over the network the concede goes to every phone in their move order — and
    /// they see the loss now; their allies fight on.
    /// </summary>
    public void ConcedeLocal()
    {
        if (!CanConcede) return;
        Guard("concede", () =>
        {
            _s.Concede();
            OnlineMatch.Current?.FlushOutbound();
            Finish(false, $"co-op: {_s.Local.Seat.DisplayName} conceded in round {_s.Expedition.Round} (expedition {_s.Expedition.Outcome})");
        });
    }

    // ── Tabs ─────────────────────────────────────────────────────────────

    private Button MakeTab(int seat, float h)
    {
        var tab = new Button { Name = $"Tab{seat}", CustomMinimumSize = new Vector2(BandW * _k, h), FocusMode = FocusModeEnum.None, MouseFilter = MouseFilterEnum.Stop, ClipContents = true };
        tab.Pressed += () => Guard("tab", () => { if (seat == _s.LocalSeat) CloseViewer(); else View(seat); });
        var name = new Label
        {
            Name = "L", MouseFilter = MouseFilterEnum.Ignore, ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            Position = new Vector2(14f * _k, 6f * _k), Size = new Vector2((BandW - 24f) * _k, h * 0.5f - 4f * _k),
            VerticalAlignment = VerticalAlignment.Center,
        };
        name.AddThemeFontOverride("font", ThemeTokens.GetHeaderFont((int)(22 * _k)));
        name.AddThemeFontSizeOverride("font_size", (int)(22 * _k));
        tab.AddChild(name);
        var line = new Label
        {
            Name = "S", MouseFilter = MouseFilterEnum.Ignore, ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            Position = new Vector2(14f * _k, h * 0.5f), Size = new Vector2((BandW - 24f) * _k, h * 0.5f - 6f * _k),
            VerticalAlignment = VerticalAlignment.Center,
        };
        line.AddThemeFontOverride("font", ThemeTokens.GetBodyFont((int)(19 * _k)));
        line.AddThemeFontSizeOverride("font_size", (int)(19 * _k));
        tab.AddChild(line);
        return tab;
    }

    private void RefreshTabs()
    {
        var x = _s.Expedition;
        _roundLabel.Text = x.PoolRemaining is int p ? $"ROUND {x.Round}  ·  POOL {p}/{x.PoolMax}" : $"ROUND {x.Round}";
        for (int i = 0; i < x.Boards.Count && i < _tabs.GetChildCount(); i++)
        {
            var b = x.Boards[i];
            var tab = (Button)_tabs.GetChild(i);
            bool mine = b.Seat.Seat == _s.LocalSeat;
            bool viewing = b.Seat.Seat == _viewing;
            bool gaveUp = x.HasConceded(b.Seat.Seat);
            string state = gaveUp ? "⚑ gave up" : b.IsOut ? "☠ knocked out" : b.Won ? "★ won" : b.EndedTurn ? "✓ turn ended" : mine ? "your move" : "acting…";
            var p0 = b.State.Players[0];
            var name = (Label)tab.GetNode("L");
            var line = (Label)tab.GetNode("S");
            name.Text = mine ? "YOU" : b.Seat.DisplayName;
            line.Text = $"♥ {Math.Max(0, p0.Vigor)}/{p0.MaxVigor}   {state}";
            bool dim = b.IsOut || gaveUp;
            name.AddThemeColorOverride("font_color", dim ? new Color(0.6f, 0.45f, 0.45f) : new Color(0.95f, 0.90f, 0.80f));
            line.AddThemeColorOverride("font_color", dim ? new Color(0.55f, 0.42f, 0.42f) : new Color(0.80f, 0.76f, 0.66f));
            var tint = mine ? new Color(0.62f, 0.52f, 0.30f) : new Color(0.40f, 0.46f, 0.52f);
            tab.AddThemeStyleboxOverride("normal", TabBox(tint, viewing));
            tab.AddThemeStyleboxOverride("hover", TabBox(tint, true));
            tab.AddThemeStyleboxOverride("pressed", TabBox(tint, true));
            tab.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        }
    }

    private StyleBoxFlat TabBox(Color tint, bool lit) => new()
    {
        BgColor = new Color(tint.R * 0.22f, tint.G * 0.22f, tint.B * 0.22f, lit ? 0.97f : 0.88f),
        BorderColor = lit ? ThemeTokens.Gold : new Color(tint.R, tint.G, tint.B, 0.8f),
        BorderWidthLeft = lit ? (int)(6 * _k) : 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 10, CornerRadiusBottomLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomRight = 10,
    };

    // ── Watching an ally ────────────────────────────────────────────────

    private void View(int seat)
    {
        if (_finished || seat == _s.LocalSeat) { CloseViewer(); return; }
        _viewing = seat;
        var b = _s.Expedition.Board(seat);
        _blocker.Visible = true;
        _watchBanner.Text = $"Watching {b.Seat.DisplayName}   ·   ◀ back to my board";
        _watchBanner.Visible = true;
        _duel?.Spectate(b.State, b.Seat.DisplayName);
        RefreshTabs();
    }

    private void CloseViewer()
    {
        _viewing = _s.LocalSeat;
        _blocker.Visible = false;
        _watchBanner.Visible = false;
        _duel?.EndSpectate();
        if (!_finished) RefreshTabs();
    }

    private void Banner(string text)
    {
        _banner?.QueueFree();
        var vp = GetViewportRect().Size;
        _banner = new Label { Text = text, Position = new Vector2(320f * _k, vp.Y * 0.44f), Size = new Vector2(vp.X - 320f * _k, 80f * _k), HorizontalAlignment = HorizontalAlignment.Center, ZIndex = 60, MouseFilter = MouseFilterEnum.Ignore };
        _banner.AddThemeFontSizeOverride("font_size", (int)(44 * _k));
        _banner.AddThemeColorOverride("font_color", new Color(1, 0.6f, 0.5f));
        _banner.AddThemeConstantOverride("outline_size", (int)(6 * _k));
        _banner.AddThemeColorOverride("font_outline_color", Colors.Black);
        AddChild(_banner);
        var t = _banner.CreateTween();
        t.TweenInterval(3.0);
        t.TweenProperty(_banner, "modulate:a", 0f, 0.6f);
    }
}

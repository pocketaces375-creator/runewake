using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.Coop;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// FABLE-020: the co-op layer on top of a duel — Trikzos's TFT-style tabs.
///
///   * Up to five tabs down the right edge: YOU, then each ally. Each tab shows
///     the Delver's name, class, Vigor, and whether they're still acting, have
///     ended their turn (✓), or are knocked out (☠).
///   * Tap an ally's tab to watch their field — their five lanes and their copy
///     of the enemy, live. Tap YOUR tab to go back to your own board.
///   * Your moves go to the expedition, not straight into the engine; when
///     everyone has ended their turn, every board's enemy takes its turn and
///     (in a raid) the shared health pool settles. The enemy nameplate on your
///     board IS the shared pool.
///   * If you're knocked out, your team fights on — the view moves to an ally
///     and the result comes when the expedition decides.
///
/// Attached by DuelScene when CoopSession.Current is set. Touches DuelScene
/// only through GameStateManager (state + action sink) and BotController.Detach.
/// </summary>
public partial class CoopOverlay : Control
{
    private CoopSession _s = null!;
    private GameStateManager _gsm = null!;
    private VBoxContainer _tabs = null!;
    private Control? _viewer;
    private int _viewing;
    private int _lastRound;
    private double _aiClock;
    private bool _finished;
    private Label _roundLabel = null!;
    private Label? _banner;

    public static CoopOverlay? Attach(Control duel, GameStateManager gsm, BotController bot)
    {
        var s = CoopSession.Current;
        if (s == null) return null;
        bot.Detach();
        gsm.DeferGameOver = true;
        gsm.ActionSink = a => s.SubmitLocal(a);
        gsm.SetState(s.Local.State);
        var o = new CoopOverlay { _s = s, _gsm = gsm, Name = "CoopOverlay", MouseFilter = MouseFilterEnum.Ignore, ZIndex = 50 };
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
        // Left edge: the board starts ~310px in, and the right edge holds the enemy HUD.
        _tabs = new VBoxContainer { Position = new Vector2(14, 150), Size = new Vector2(284, 600), MouseFilter = MouseFilterEnum.Ignore };
        _tabs.AddThemeConstantOverride("separation", 10);
        AddChild(_tabs);
        _roundLabel = new Label { Position = new Vector2(14, 104), Size = new Vector2(284, 40), HorizontalAlignment = HorizontalAlignment.Center };
        _roundLabel.AddThemeFontSizeOverride("font_size", 24);
        _roundLabel.AddThemeColorOverride("font_color", ThemeTokens.Gold);
        _roundLabel.AddThemeConstantOverride("outline_size", 6);
        _roundLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        AddChild(_roundLabel);
        RefreshTabs();
    }

    public override void _ExitTree()
    {
        if (CoopSession.Current == _s && _finished) CoopSession.Current = null;
    }

    public override void _Process(double delta)
    {
        if (_finished) return;
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
            _finished = true;
            CloseViewer();
            bool won = x.Outcome == ExpeditionOutcome.Victory;
            DuelScene.ExitTrace($"co-op finished: {x.Outcome} in round {x.Round}, pool {x.PoolRemaining}");
            _gsm.DeferGameOver = false;
            _gsm.ActionSink = null;
            _gsm.RaiseGameOver(won ? 0 : 1);
        }
        RefreshTabs();
        if (_viewer != null && _viewing != _s.LocalSeat) RenderViewer();
    }

    // ── Tabs ─────────────────────────────────────────────────────────────

    private void RefreshTabs()
    {
        var x = _s.Expedition;
        _roundLabel.Text = x.PoolRemaining is int p ? $"Round {x.Round} · Pool {p}/{x.PoolMax}" : $"Round {x.Round}";
        while (_tabs.GetChildCount() < x.Boards.Count)
        {
            var b = x.Boards[_tabs.GetChildCount()];
            int seat = b.Seat.Seat;
            var tab = new Button { CustomMinimumSize = new Vector2(284, 104), FocusMode = FocusModeEnum.None, MouseFilter = MouseFilterEnum.Stop };
            tab.Pressed += () => { if (seat == _s.LocalSeat) CloseViewer(); else View(seat); };
            var lbl = new Label { Name = "L", AutowrapMode = TextServer.AutowrapMode.Off, MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(14, 8), Size = new Vector2(258, 90), ClipText = true };
            lbl.AddThemeFontSizeOverride("font_size", 21);
            tab.AddChild(lbl);
            _tabs.AddChild(tab);
        }
        for (int i = 0; i < x.Boards.Count; i++)
        {
            var b = x.Boards[i];
            var tab = (Button)_tabs.GetChild(i);
            bool mine = b.Seat.Seat == _s.LocalSeat;
            bool viewing = b.Seat.Seat == _viewing;
            string state = b.IsOut ? "☠ knocked out" : b.Won ? "★ won" : b.EndedTurn ? "✓ turn ended" : mine ? "your move" : "acting…";
            var p0 = b.State.Players[0];
            ((Label)tab.GetNode("L")).Text = $"{(mine ? "YOU" : b.Seat.DisplayName)}\nVigor {Math.Max(0, p0.Vigor)}/{p0.MaxVigor}\n{state}";
            ((Label)tab.GetNode("L")).AddThemeColorOverride("font_color", b.IsOut ? new Color(0.6f, 0.45f, 0.45f) : new Color(0.93f, 0.88f, 0.8f));
            var tint = mine ? new Color(0.62f, 0.52f, 0.30f) : new Color(0.40f, 0.46f, 0.52f);
            tab.AddThemeStyleboxOverride("normal", TabBox(tint, viewing));
            tab.AddThemeStyleboxOverride("hover", TabBox(tint, true));
            tab.AddThemeStyleboxOverride("pressed", TabBox(tint, true));
            tab.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        }
    }

    private static StyleBoxFlat TabBox(Color tint, bool lit) => new()
    {
        BgColor = new Color(tint.R * 0.25f, tint.G * 0.25f, tint.B * 0.25f, lit ? 0.97f : 0.85f),
        BorderColor = lit ? ThemeTokens.Gold : new Color(tint.R, tint.G, tint.B, 0.8f),
        BorderWidthLeft = lit ? 6 : 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 10, CornerRadiusBottomLeft = 10, CornerRadiusTopRight = 4, CornerRadiusBottomRight = 4,
    };

    // ── Watching an ally ────────────────────────────────────────────────

    private void View(int seat)
    {
        _viewing = seat;
        if (_viewer == null)
        {
            var vp = GetViewportRect().Size;
            _viewer = new Panel { Position = new Vector2(312, 90), Size = new Vector2(vp.X - 332, vp.Y - 110), MouseFilter = MouseFilterEnum.Stop };
            _viewer.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.05f, 0.05f, 0.06f, 0.96f), BorderColor = new Color(0.40f, 0.46f, 0.52f, 0.9f),
                BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
                CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12, CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
            });
            AddChild(_viewer);
            MoveChild(_viewer, 0);
        }
        RenderViewer();
    }

    private void CloseViewer()
    {
        _viewing = _s.LocalSeat;
        _viewer?.QueueFree();
        _viewer = null;
    }

    private void RenderViewer()
    {
        if (_viewer == null) return;
        foreach (var c in _viewer.GetChildren()) { _viewer.RemoveChild(c); c.QueueFree(); }
        var b = _s.Expedition.Board(_viewing);
        var st = b.State;
        var size = _viewer.Size;

        Label L(string text, Vector2 pos, Vector2 sz, int fs, Color c, HorizontalAlignment align = HorizontalAlignment.Center)
        {
            var l = new Label { Text = text, Position = pos, Size = sz, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            l.AddThemeFontSizeOverride("font_size", fs);
            l.AddThemeColorOverride("font_color", c);
            _viewer.AddChild(l);
            return l;
        }

        L($"Watching {b.Seat.DisplayName}'s field", new Vector2(0, 14), new Vector2(size.X, 50), 34, ThemeTokens.Gold);
        L("tap YOU on the left to go back to your board", new Vector2(0, 58), new Vector2(size.X, 34), 22, new Color(0.62f, 0.58f, 0.5f));
        var enemyName = _s.Expedition.Config.Encounter.Name;
        L($"{enemyName} — Vigor {Math.Max(0, st.Players[1].Vigor)}   ·   hand {st.Players[1].Hand.Count}", new Vector2(0, 100), new Vector2(size.X, 40), 26, new Color(0.92f, 0.5f, 0.42f));
        Lanes(st.Players[1], 150);
        Lanes(st.Players[0], 150 + (size.Y - 330) / 2 + 40);
        var p0 = st.Players[0];
        L($"{b.Seat.DisplayName} — Vigor {Math.Max(0, p0.Vigor)}/{p0.MaxVigor}   ·   Attunement {p0.Attunement}/{p0.AttunementMax}   ·   hand {p0.Hand.Count}   ·   deck {p0.Deck.Count}",
          new Vector2(0, size.Y - 70), new Vector2(size.X, 44), 26, new Color(0.55f, 0.85f, 0.55f));

        void Lanes(PlayerState p, float y)
        {
            float laneW = (size.X - 120) / 5f, laneH = (size.Y - 330) / 2f;
            for (int i = 0; i < 5; i++)
            {
                var occ = p.Lanes[i].Occupant;
                var box = new Panel { Position = new Vector2(60 + i * laneW + 8, y), Size = new Vector2(laneW - 16, laneH) };
                box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
                {
                    BgColor = occ == null ? new Color(1, 1, 1, 0.03f) : new Color(0.16f, 0.13f, 0.10f, 0.95f),
                    BorderColor = occ == null ? new Color(1, 1, 1, 0.12f) : new Color(0.62f, 0.52f, 0.30f, 0.9f),
                    BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
                    CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
                });
                _viewer!.AddChild(box);
                if (occ == null) continue;
                var name = CardRegistry.Get(occ.CardDefId)?.Name ?? PrettyId(occ.CardDefId);
                var n = new Label { Text = name, Position = new Vector2(8, 8), Size = new Vector2(laneW - 32, laneH - 70), HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
                n.AddThemeFontSizeOverride("font_size", 24);
                n.AddThemeColorOverride("font_color", new Color(0.93f, 0.88f, 0.8f));
                box.AddChild(n);
                var stats = new Label { Text = $"{occ.CurrentAttack}  /  {occ.CurrentVigor}", Position = new Vector2(0, laneH - 58), Size = new Vector2(laneW - 16, 44), HorizontalAlignment = HorizontalAlignment.Center };
                stats.AddThemeFontSizeOverride("font_size", 32);
                stats.AddThemeColorOverride("font_color", occ.IsExhausted ? new Color(0.6f, 0.58f, 0.5f) : ThemeTokens.Gold);
                box.AddChild(stats);
            }
        }
    }

    private void Banner(string text)
    {
        _banner?.QueueFree();
        var vp = GetViewportRect().Size;
        _banner = new Label { Text = text, Position = new Vector2(300, vp.Y * 0.44f), Size = new Vector2(vp.X - 300, 80), HorizontalAlignment = HorizontalAlignment.Center, ZIndex = 60 };
        _banner.AddThemeFontSizeOverride("font_size", 44);
        _banner.AddThemeColorOverride("font_color", new Color(1, 0.6f, 0.5f));
        _banner.AddThemeConstantOverride("outline_size", 6);
        _banner.AddThemeColorOverride("font_outline_color", Colors.Black);
        AddChild(_banner);
        var t = _banner.CreateTween();
        t.TweenInterval(3.0);
        t.TweenProperty(_banner, "modulate:a", 0f, 0.6f);
    }

    /// <summary>Tokens (tok_familiar) aren't in the card registry: show "Familiar".</summary>
    private static string PrettyId(string id)
    {
        var t = id.StartsWith("tok_") ? id[4..] : id;
        return string.Join(' ', t.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.Supabase;

namespace Runewake.Client;

/// <summary>
/// FABLE-038: "Play with friends".
///
///   Host a duel     → a six-letter code appears; the friend types it in; when they have
///                     joined, the host presses Start and both phones open the duel.
///   Join with code  → type the code, land in the host's lobby, wait for Start.
///   Host a co-op    → same lobby, up to five, everyone fights their own copy of a boss;
///                     the first to win wins for all (engine/Coop Expedition).
///
/// The lobby is one Supabase call (lobby) polled every 1.5 s. When it says "running" and
/// carries the seed, both phones build the same match (OnlineMatch.FromLobby) and go.
/// Threading: HTTP replies are applied on the main thread through CallDeferred.
/// </summary>
public partial class OnlineLobbyScene : Control
{
    public const string ScenePath = "res://scenes/online/OnlineLobbyScene.tscn";
    private static readonly Color Parchment = new(0.91f, 0.86f, 0.78f);
    private const string NameFile = "user://online_name.txt";

    private enum View { Menu, Lobby }
    private View _view = View.Menu;
    private SyncManager? _sm;
    private OnlinePlaySync? _sync;
    private string _id = "";
    private string _code = "";
    private bool _host;
    private string _kind = "pvp1v1";
    private double _poll;
    private bool _polling;
    private bool _started;
    private OnlinePlaySync.Lobby? _lobby;

    // widgets
    private Control _menu = null!, _lobbyPane = null!;
    private Label _title = null!, _status = null!, _codeLabel = null!, _players = null!, _kicker = null!;
    private LineEdit _nameEdit = null!, _codeEdit = null!;
    private Button _startBtn = null!, _readyBtn = null!, _leaveBtn = null!;
    private string _coopTarget = "";
    private string _coopTitle = "";

    public override void _Ready() => SceneGuard.Build(this, "OnlineLobbyScene", Build, "res://scenes/main/Main.tscn", "Back to title");

    private void Build()
    {
        var vp = GetViewportRect().Size;
        _sm = CampaignContext.SyncManager;
        if (_sm != null && _sm.Config.IsConfigured) _sync = new OnlinePlaySync(_sm.Config, Http.Create(15));

        var bg = new ColorRect { Color = new Color(0.05f, 0.045f, 0.035f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        var glow = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = Grad(new Color(0.42f, 0.78f, 0.72f, 0.22f), new Color(0.42f, 0.78f, 0.72f, 0f)),
                Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.3f), FillTo = new Vector2(0.5f, 1f), Width = 64, Height = 64,
            },
            StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore,
        };
        glow.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(glow);

        _kicker = Text("PLAY WITH FRIENDS", 26, 28, ThemeTokens.GetHeaderFont(28), new Color(0.75f, 0.68f, 0.52f));
        _title = Text("Online", 60, 84, ThemeTokens.GetCardNameFont(84), ThemeTokens.Gold);
        _status = Text("", vp.Y - 150, 30, ThemeTokens.GetBodyFont(30), new Color(0.86f, 0.76f, 0.56f));

        // ── menu ──
        _menu = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _menu.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_menu);

        var nameRow = new HBoxContainer { Position = new Vector2(vp.X / 2f - 380, 190), Size = new Vector2(760, 70), Alignment = BoxContainer.AlignmentMode.Center };
        nameRow.AddThemeConstantOverride("separation", 20);
        _menu.AddChild(nameRow);
        var nameLbl = new Label { Text = "Your name", VerticalAlignment = VerticalAlignment.Center };
        nameLbl.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(32)); nameLbl.AddThemeFontSizeOverride("font_size", 32);
        nameLbl.AddThemeColorOverride("font_color", Parchment);
        nameRow.AddChild(nameLbl);
        _nameEdit = new LineEdit { Text = LoadName(), CustomMinimumSize = new Vector2(440, 64), MaxLength = 18, PlaceholderText = "Delver" };
        StyleEdit(_nameEdit);
        nameRow.AddChild(_nameEdit);

        var col = new VBoxContainer { Position = new Vector2(vp.X / 2f - 300, 290), Size = new Vector2(600, 500), Alignment = BoxContainer.AlignmentMode.Begin };
        col.AddThemeConstantOverride("separation", 22);
        _menu.AddChild(col);

        var hostBtn = Plate("Host a duel  ·  1v1", true);
        hostBtn.Pressed += () => _ = Host("pvp1v1", "pvp");
        col.AddChild(hostBtn);

        PickCoopTarget();
        var coopBtn = Plate(string.IsNullOrEmpty(_coopTarget) ? "Host a co-op fight" : $"Host a co-op fight  ·  {_coopTitle}", false);
        coopBtn.Disabled = string.IsNullOrEmpty(_coopTarget);
        coopBtn.Pressed += () => _ = Host("coop", _coopTarget);
        col.AddChild(coopBtn);

        var joinRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        joinRow.AddThemeConstantOverride("separation", 16);
        col.AddChild(joinRow);
        _codeEdit = new LineEdit { CustomMinimumSize = new Vector2(300, 84), MaxLength = 6, PlaceholderText = "CODE", Alignment = HorizontalAlignment.Center };
        StyleEdit(_codeEdit, 40);
        _codeEdit.TextChanged += t => { string u = t.ToUpperInvariant(); if (u != t) { _codeEdit.Text = u; _codeEdit.CaretColumn = u.Length; } };
        joinRow.AddChild(_codeEdit);
        var joinBtn = Plate("Join", false, 280);
        joinBtn.Pressed += () => _ = Join(_codeEdit.Text.Trim());
        joinRow.AddChild(joinBtn);

        var back = new Button { Text = "◀  Back to title", Position = new Vector2(80, vp.Y - 120), Size = new Vector2(360, 84) };
        WorldMapScene.StyleButton(back);
        back.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn");
        AddChild(back);

        // ── lobby ──
        _lobbyPane = new Control { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _lobbyPane.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_lobbyPane);
        _codeLabel = new Label
        {
            Position = new Vector2(0, 190), Size = new Vector2(vp.X, 130), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore,
        };
        _codeLabel.AddThemeFontOverride("font", ThemeTokens.GetCardNameFont(110)); _codeLabel.AddThemeFontSizeOverride("font_size", 110);
        _codeLabel.AddThemeColorOverride("font_color", new Color(0.77f, 0.96f, 0.92f));
        _codeLabel.AddThemeConstantOverride("outline_size", 8); _codeLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0.1f, 0.09f));
        _lobbyPane.AddChild(_codeLabel);
        _players = new Label
        {
            Position = new Vector2(vp.X / 2f - 400, 350), Size = new Vector2(800, 300), HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _players.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(36)); _players.AddThemeFontSizeOverride("font_size", 36);
        _players.AddThemeColorOverride("font_color", Parchment);
        _lobbyPane.AddChild(_players);

        var row = new HBoxContainer { Position = new Vector2(vp.X / 2f - 520, vp.Y - 260), Size = new Vector2(1040, 100), Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 30);
        _lobbyPane.AddChild(row);
        _leaveBtn = Plate("Leave", false, 300);
        _leaveBtn.Pressed += () => _ = Leave();
        row.AddChild(_leaveBtn);
        _readyBtn = Plate("I'm ready", false, 320);
        _readyBtn.Pressed += () => _ = ToggleReady();
        row.AddChild(_readyBtn);
        _startBtn = Plate("Start", true, 320);
        _startBtn.Pressed += () => _ = Start();
        row.AddChild(_startBtn);

        MenuButtons.BreatheTitle(_title);
        MenuButtons.RevealStagger(new Control[] { _kicker, _title, nameRow, col });

        if (_sync == null) SetStatus("Online play needs the cloud connection — it isn't set up on this build.");
        else if (_sm?.Session?.IsValid != true) SetStatus("Not signed in yet. Give it a moment, or check your connection.");
        else SetStatus($"Signed in as {_sm.Session.DisplayLabel()}");
    }

    public override void _Process(double delta)
    {
        if (_view != View.Lobby || _started || string.IsNullOrEmpty(_id)) return;
        _poll += delta;
        if (_poll < 1.5 || _polling) return;
        _poll = 0;
        _ = PollLobby();
    }

    // ── actions ─────────────────────────────────────────────────────────────

    private (string name, string cls, List<string> deck)? MyLoadout()
    {
        string name = _nameEdit.Text.Trim();
        if (string.IsNullOrEmpty(name)) name = _sm?.Session?.DisplayLabel() ?? "Delver";
        SaveName(name);
        string cls = CampaignContext.ChosenClass;
        var deck = CampaignContext.PlayerDeckIds;
        if (string.IsNullOrEmpty(cls) || deck == null || deck.Count == 0)
        {
            SetStatus("Start a campaign first so you have a class and a deck to bring.");
            return null;
        }
        return (name, cls, new List<string>(deck));
    }

    private async Task Host(string kind, string target)
    {
        if (_sync == null || _sm?.Session == null) return;
        var me = MyLoadout(); if (me == null) return;
        SetStatus("Opening a lobby…");
        _kind = kind;
        var r = await _sync.Create(_sm.Session, kind, target, kind == "coop" ? 5 : 2, me.Value.name, me.Value.cls, me.Value.deck).ConfigureAwait(false);
        CallDeferred(nameof(AfterHost), r.ok, r.id, r.code, r.error);
    }

    private void AfterHost(bool ok, string id, string code, string error)
    {
        if (!ok) { SetStatus("Couldn't open a lobby: " + error); return; }
        _id = id; _code = code; _host = true;
        ShowLobby();
        SetStatus(_kind == "coop" ? "Give friends the code. Start when everyone is in." : "Give your friend the code. Start when they're in.");
    }

    private async Task Join(string code)
    {
        if (_sync == null || _sm?.Session == null) return;
        if (code.Length != 6) { SetStatus("A code is six characters."); return; }
        var me = MyLoadout(); if (me == null) return;
        SetStatus("Looking for that lobby…");
        var f = await _sync.Find(_sm.Session, code).ConfigureAwait(false);
        if (!f.ok || f.found == null)
        {
            CallDeferred(nameof(SetStatus), f.ok ? "No open lobby has that code." : "Couldn't look it up: " + f.error);
            return;
        }
        if (f.found.State != "open")
        {
            CallDeferred(nameof(SetStatus), "That match has already started.");
            return;
        }
        var j = await _sync.Join(_sm.Session, f.found.Id, me.Value.name, me.Value.cls, me.Value.deck).ConfigureAwait(false);
        CallDeferred(nameof(AfterJoin), j.ok, f.found.Id, code.ToUpperInvariant(), f.found.Kind, f.found.HostName, j.error);
    }

    private void AfterJoin(bool ok, string id, string code, string kind, string hostName, string error)
    {
        if (!ok) { SetStatus("Couldn't join: " + error); return; }
        _id = id; _code = code; _host = false; _kind = kind;
        ShowLobby();
        SetStatus($"In {hostName}'s lobby. Waiting for them to start.");
    }

    private async Task ToggleReady()
    {
        if (_sync == null || _sm?.Session == null || _lobby?.Mine == null) return;
        bool ready = _lobby.Mine.Status != "ready";
        await _sync.SetReady(_sm.Session, _id, ready).ConfigureAwait(false);
    }

    private async Task Start()
    {
        if (_sync == null || _sm?.Session == null || !_host) return;
        int present = _lobby?.Members.Count(m => m.Status != "left") ?? 0;
        if (present < 2) { SetStatus("Nobody has joined yet."); return; }
        SetStatus("Starting…");
        var r = await _sync.Start(_sm.Session, _id).ConfigureAwait(false);
        if (!r.ok) CallDeferred(nameof(SetStatus), "Couldn't start: " + r.error);
        // the poll sees "running" and launches, same as the guest's phone
    }

    private async Task Leave()
    {
        if (_sync != null && _sm?.Session != null && !string.IsNullOrEmpty(_id))
            await _sync.Leave(_sm.Session, _id).ConfigureAwait(false);
        CallDeferred(nameof(ShowMenu));
    }

    private async Task PollLobby()
    {
        if (_sync == null || _sm?.Session == null) return;
        _polling = true;
        var r = await _sync.GetLobby(_sm.Session, _id).ConfigureAwait(false);
        _polling = false;
        if (r.ok && r.lobby != null) CallDeferred(nameof(ApplyLobby), Godot.Variant.From(0));
        _lobby = r.ok ? r.lobby : _lobby;
        if (!r.ok) CallDeferred(nameof(SetStatus), "Connection hiccup: " + r.error);
    }

    private void ApplyLobby(Variant _)
    {
        var l = _lobby;
        if (l == null) return;
        var lines = new List<string>();
        foreach (var m in l.Members.OrderBy(m => m.Seat))
        {
            string tag = m.Status == "left" ? "left" : m.Status == "ready" ? "ready" : m.Seat == 0 ? "host" : "joined";
            string cls = string.IsNullOrEmpty(m.ClassId) ? "" : $"  ·  {char.ToUpperInvariant(m.ClassId[0])}{m.ClassId[1..]}";
            lines.Add($"{(m.Me ? "▸ " : "")}{m.DisplayName}{cls}   ({tag})");
        }
        _players.Text = string.Join("\n", lines);
        int present = l.Members.Count(m => m.Status != "left");
        _startBtn.Visible = _host;
        _startBtn.Disabled = present < 2;
        _readyBtn.Visible = !_host;
        _readyBtn.Text = l.Mine?.Status == "ready" ? "Not ready" : "I'm ready";

        if (l.State == "abandoned")
        {
            SetStatus("The host closed the lobby.");
            ShowMenu();
            return;
        }
        if (l.State == "running" && l.Seed != null && !_started)
        {
            var match = OnlineMatch.FromLobby(l, _sync!, _sm!.Session!);
            if (match == null) { SetStatus("Couldn't build the match from the lobby."); return; }
            _started = true;
            OnlineMatch.Current = match;
            CampaignContext.CurrentEncounter = null;
            CampaignContext.IsArenaDuel = false;
            CampaignContext.DebugSeed = null;
            DuelScene.ExitTrace($"online: {l.Kind} {_id} seat {match.LocalSeat} → DuelScene");
            GetTree().ChangeSceneToFile(CampaignRun.DuelScenePath);
        }
    }

    // ── views ───────────────────────────────────────────────────────────────

    private void ShowLobby()
    {
        _view = View.Lobby;
        _menu.Visible = false;
        _lobbyPane.Visible = true;
        _codeLabel.Text = _code;
        _players.Text = "…";
        _title.Text = _kind == "coop" ? "Co-op lobby" : "Duel lobby";
        _kicker.Text = _host ? "YOUR LOBBY  ·  SHARE THIS CODE" : "LOBBY CODE";
        _poll = 10;   // poll at once
        _started = false;
    }

    private void ShowMenu()
    {
        _view = View.Menu;
        _id = ""; _code = ""; _lobby = null; _started = false;
        _menu.Visible = true;
        _lobbyPane.Visible = false;
        _title.Text = "Online";
        _kicker.Text = "PLAY WITH FRIENDS";
    }

    private void SetStatus(string s) { if (_status != null) _status.Text = s; }

    private void PickCoopTarget()
    {
        // The host's next campaign fight, or the first boss it knows: something everyone can face.
        var region = CampaignRun.LoadCurrentRegion();
        var next = CampaignRun.FindNextDuelNode(region);
        var enc = next?.Encounter != null && CampaignContext.EncounterIndex.TryGetValue(next.Encounter, out var e) ? e : null;
        enc ??= CampaignContext.EncounterIndex.Values.FirstOrDefault(x => !x.IsTutorial);
        if (enc != null) { _coopTarget = enc.Id; _coopTitle = enc.Name; }
    }

    // ── widgets ─────────────────────────────────────────────────────────────

    private Label Text(string text, float y, int size, Font font, Color color)
    {
        var l = new Label
        {
            Text = text, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Position = new Vector2(0, y), Size = new Vector2(GetViewportRect().Size.X, size + 16), MouseFilter = MouseFilterEnum.Ignore,
        };
        l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        AddChild(l);
        return l;
    }

    private static Button Plate(string text, bool primary, float w = 600)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(w, 96), FocusMode = FocusModeEnum.None };
        b.AddThemeFontOverride("font", ThemeTokens.GetButtonFont(36)); b.AddThemeFontSizeOverride("font_size", 36);
        b.AddThemeColorOverride("font_color", primary ? Color.FromHtml("#F2DFA6") : Color.FromHtml("#D8CBB0"));
        b.AddThemeColorOverride("font_disabled_color", new Color(0.45f, 0.42f, 0.36f));
        b.AddThemeStyleboxOverride("normal", primary ? MenuButtons.PrimaryNormal() : MenuButtons.QuietNormal());
        b.AddThemeStyleboxOverride("hover", primary ? MenuButtons.PrimaryHover() : MenuButtons.Hover());
        b.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed());
        b.AddThemeStyleboxOverride("disabled", MenuButtons.QuietNormal());
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        MenuButtons.Animate(b);
        return b;
    }

    private static void StyleEdit(LineEdit e, int size = 32)
    {
        e.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(size)); e.AddThemeFontSizeOverride("font_size", size);
        e.AddThemeColorOverride("font_color", new Color(0.95f, 0.92f, 0.85f));
        e.AddThemeColorOverride("font_placeholder_color", new Color(0.5f, 0.47f, 0.4f));
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.09f, 0.08f), BorderColor = new Color(0.5f, 0.42f, 0.24f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 16, ContentMarginRight = 16,
        };
        e.AddThemeStyleboxOverride("normal", box);
        e.AddThemeStyleboxOverride("focus", box);
    }

    private static string LoadName()
    {
        try { using var f = Godot.FileAccess.Open(NameFile, Godot.FileAccess.ModeFlags.Read); return f?.GetAsText().Trim() ?? ""; }
        catch { return ""; }
    }

    private static void SaveName(string name)
    {
        try { using var f = Godot.FileAccess.Open(NameFile, Godot.FileAccess.ModeFlags.Write); f?.StoreString(name); }
        catch (Exception ex) { GD.PrintErr($"[Online] name not saved: {ex.Message}"); }
    }

    private static Gradient Grad(Color a, Color b)
    {
        var g = new Gradient(); g.SetColor(0, a); g.SetColor(1, b); return g;
    }
}

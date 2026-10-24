using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.Coop;
using Runewake.Engine.Supabase;

namespace Runewake.Client;

/// <summary>
/// FABLE-038: "Play with friends".
///
///   Host a duel     → a six-letter code appears; the friend types it in; when they have
///                     joined, the host presses Start and both phones open the duel.
///   Join with code  → type the code, land in the host's lobby, wait for Start.
///   Host a co-op    → same lobby, up to five, everyone keeps their own board, all facing ONE boss scaled to the team (FABLE-062);
///                     when it falls the team wins (engine/Coop Expedition).
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
    // FABLE-054: challenge by username
    private LineEdit _challengeEdit = null!;
    private Control _challengeRow = null!, _inviteBanner = null!;
    private Label _inviteText = null!;
    private ProfileSync.Challenge? _invite;
    private double _invitePoll = 3;
    private bool _invitePolling;
    private string _coopTarget = "";
    private string _coopTitle = "";
    // FABLE-103: the war-table painting (OnlineLobbyScene_words.webp) - the lobby laid out on its stones, thrones and scrolls
    private static bool Painted => ScreenSkin.Words("OnlineLobbyScene");
    private Label _seatL = null!, _seatR = null!;
    private Button? _chBtn, _copyBtn;
    // FABLE-104: one lobby action at a time (Host / Join / Challenge). A double tap used to open two lobbies, or join twice,
    // and whichever answer landed last decided which lobby the screen polled.
    private bool _busy;
    private bool _leaving;
    private Button? _hostBtn, _coopBtn, _joinBtn;

    public override void _Ready() => SceneGuard.Build(this, "OnlineLobbyScene", Build, "res://scenes/main/Main.tscn", "Back to title");

    // FABLE-091: Play Online died on a phone with "TypeLoadException" while the runtime prepared this screen's one big
    // build method. The screen is now built in parts, each prepared and run on its own: a part that cannot load is
    // written to the trace with its full error and named on screen, and the rest of the lobby (and Back) still works.
    private void Build()
    {
        var vp = GetViewportRect().Size;
        var failed = new System.Collections.Generic.List<string>();
        try { BuildBackdrop(vp); } catch (Exception ex) { failed.Add(PartFailed("backdrop", ex)); }
        try { BuildBack(vp); } catch (Exception ex) { failed.Add(PartFailed("back", ex)); }
        try { BuildSync(); } catch (Exception ex) { failed.Add(PartFailed("cloud", ex)); }
        if (Painted)
        {
            try { BuildPaintedMenu(vp); } catch (Exception ex) { failed.Add(PartFailed("menu", ex)); }
            try { BuildPaintedLobby(vp); } catch (Exception ex) { failed.Add(PartFailed("lobby", ex)); }
        }
        else
        {
            try { BuildMenu(vp); } catch (Exception ex) { failed.Add(PartFailed("menu", ex)); }
            try { BuildLobbyPane(vp); } catch (Exception ex) { failed.Add(PartFailed("lobby", ex)); }
        }
        try { FinishBuild(); } catch (Exception ex) { failed.Add(PartFailed("status", ex)); }
        if (failed.Count > 0) SetStatus("Part of this screen could not load (" + string.Join("; ", failed) + "). Screenshot this for Fable.");
    }

    private static string PartFailed(string part, Exception ex)
    {
        DuelScene.ExitTrace($"OnlineLobby part '{part}' THREW: {ex}");
        string type = ex is TypeLoadException tl && !string.IsNullOrEmpty(tl.TypeName) ? " [" + tl.TypeName + "]" : "";
        string inner = ex.InnerException != null ? " / " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message : "";
        return $"{part}: {ex.GetType().Name}{type} {ex.Message}{inner}";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BuildSync()
    {
        _sm = CampaignContext.SyncManager;
        if (_sm != null && _sm.Config.IsConfigured) _sync = new OnlinePlaySync(_sm.Config, Http.Create(15));

    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BuildBackdrop(Vector2 vp)
    {
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
        // FABLE-098: with a painting behind the lobby (the war table), a soft veil keeps the menu readable over it.
        // (a Control holding the veil, so ScreenSkin's sweep of flat background layers leaves it alone)
        if (!Painted && ResourceLoader.Exists("res://content/art/screens/OnlineLobbyScene.webp"))
        {
            var veil = new Control { MouseFilter = MouseFilterEnum.Ignore };
            veil.SetAnchorsPreset(LayoutPreset.FullRect);
            var dim = new ColorRect { Color = new Color(0.02f, 0.02f, 0.03f, 0.38f), MouseFilter = MouseFilterEnum.Ignore };
            dim.SetAnchorsPreset(LayoutPreset.FullRect);
            veil.AddChild(dim);
            AddChild(veil);
        }

        _kicker = Text("PLAY WITH FRIENDS", 26, 28, ThemeTokens.GetHeaderFont(28), new Color(0.75f, 0.68f, 0.52f));
        _title = Text("Online", 60, 84, ThemeTokens.GetCardNameFont(84), ThemeTokens.Gold);
        _status = Text("", vp.Y - 150, 30, ThemeTokens.GetBodyFont(30), new Color(0.86f, 0.76f, 0.56f));
        if (Painted)   // FABLE-103: the title is painted on the stone; the line under it and the bottom scroll are live
        {
            _title.Visible = false;
            Place(_kicker, P(vp, 470, 133), P(vp, 905, 166));
            _kicker.AddThemeFontSizeOverride("font_size", Px(vp, 19));
            _kicker.AddThemeColorOverride("font_color", new Color(0.86f, 0.74f, 0.45f));
            _kicker.AutowrapMode = TextServer.AutowrapMode.Off; _kicker.VerticalAlignment = VerticalAlignment.Center;
            Place(_status, P(vp, 440, 693), P(vp, 945, 736));
            _status.AddThemeFontSizeOverride("font_size", Px(vp, 15));
            _status.AddThemeColorOverride("font_color", new Color(0.22f, 0.13f, 0.05f));   // dark ink on the scroll
            _status.AddThemeConstantOverride("line_spacing", -3);
            _status.VerticalAlignment = VerticalAlignment.Center;
            _status.SetMeta("skinned", true);
        }

    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BuildMenu(Vector2 vp)
    {
        // ── menu ──
        _menu = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _menu.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_menu);

        var nameRow = new HBoxContainer { Position = new Vector2(vp.X / 2f - 380, 190), Size = new Vector2(760, 70), Alignment = BoxContainer.AlignmentMode.Center };
        nameRow.AddThemeConstantOverride("separation", 20);
        _menu.AddChild(nameRow);
        bool hasUsername = !string.IsNullOrEmpty(_sm?.Username);
        var nameLbl = new Label { Text = hasUsername ? "Playing as" : "Your name", VerticalAlignment = VerticalAlignment.Center };
        nameLbl.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(32)); nameLbl.AddThemeFontSizeOverride("font_size", 32);
        nameLbl.AddThemeColorOverride("font_color", Parchment);
        nameRow.AddChild(nameLbl);
        // FABLE-054: your username when you have one (others never see an email); otherwise a filtered name
        _nameEdit = new LineEdit { Text = hasUsername ? _sm!.Username! : LoadName(), CustomMinimumSize = new Vector2(440, 64), MaxLength = Usernames.MaxLength, PlaceholderText = "Delver", Editable = !hasUsername };
        StyleEdit(_nameEdit);
        nameRow.AddChild(_nameEdit);

        // an invitation from a friend, by username
        _inviteBanner = new PanelContainer { Position = new Vector2(vp.X / 2f - 560, vp.Y - 270), Size = new Vector2(1120, 84), Visible = false, ZIndex = 5 };
        _inviteBanner.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.10f, 0.16f, 0.15f, 0.96f), BorderColor = ThemeTokens.Gold, BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2, CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14, ContentMarginLeft = 24, ContentMarginRight = 12 });
        AddChild(_inviteBanner);
        var inv = new HBoxContainer(); inv.AddThemeConstantOverride("separation", 16);
        _inviteBanner.AddChild(inv);
        _inviteText = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center, ClipText = true };
        _inviteText.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(32)); _inviteText.AddThemeFontSizeOverride("font_size", 32);
        _inviteText.AddThemeColorOverride("font_color", new Color(0.95f, 0.88f, 0.66f));
        inv.AddChild(_inviteText);
        var accept = Plate("Accept", true, 220); accept.CustomMinimumSize = new Vector2(220, 64);
        accept.Pressed += () => _ = AnswerInvite(true);
        inv.AddChild(accept);
        var decline = Plate("Decline", false, 220); decline.CustomMinimumSize = new Vector2(220, 64);
        decline.Pressed += () => _ = AnswerInvite(false);
        inv.AddChild(decline);

        var col = new VBoxContainer { Position = new Vector2(vp.X / 2f - 300, 290), Size = new Vector2(600, 500), Alignment = BoxContainer.AlignmentMode.Begin };
        col.AddThemeConstantOverride("separation", 22);
        _menu.AddChild(col);

        var hostBtn = Plate("Host a duel  ·  1v1", true);
        hostBtn.Pressed += () => _ = Host("pvp1v1", "pvp");
        col.AddChild(hostBtn);
        _hostBtn = hostBtn;

        try { PickCoopTarget(); } catch (Exception ex) { DuelScene.ExitTrace("OnlineLobby: no co-op target: " + ex); }
        var coopBtn = Plate(string.IsNullOrEmpty(_coopTarget) ? "Host a co-op fight" : $"Host a co-op fight  ·  {_coopTitle}", false);
        coopBtn.Disabled = string.IsNullOrEmpty(_coopTarget);
        coopBtn.Pressed += () => _ = Host("coop", _coopTarget);
        col.AddChild(coopBtn);
        _coopBtn = coopBtn;

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
        _joinBtn = joinBtn;

        MenuButtons.RevealStagger(new Control[] { _kicker, _title, nameRow, col });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BuildBack(Vector2 vp)
    {
        var back = Plate("◀  Back to title", false, 380);   // FABLE-091: its own plate (it borrowed the old world map's style)
        back.Position = new Vector2(80, vp.Y - 120); back.Size = new Vector2(380, 84); back.CustomMinimumSize = new Vector2(380, 84);
        if (Painted)   // FABLE-103: the painted "Back to title" stone - its word is the painting's
        {
            back.CustomMinimumSize = Vector2.Zero; Place(back, P(vp, 934, 607), P(vp, 1142, 680));
            foreach (var k in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_disabled_color", "font_focus_color" }) back.AddThemeColorOverride(k, new Color(0, 0, 0, 0));
        }
        back.Pressed += () =>
        {
            // FABLE-095: leaving from inside a lobby tells the server, so the other player isn't left waiting on a ghost
            if (_view == View.Lobby && _sync != null && _sm != null && _sm.Session != null && !string.IsNullOrEmpty(_id)) { _leaving = true; string leaveId = _id; _ = _sm.Authed(s => _sync.Leave(s, leaveId), x => x.error); }
            LoadScreen.Go(GetTree(), "res://scenes/main/Main.tscn");
        };
        AddChild(back);

    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BuildLobbyPane(Vector2 vp)
    {
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

        // FABLE-054: the host can challenge a friend by username
        var cr = new HBoxContainer { Position = new Vector2(vp.X / 2f - 460, vp.Y - 400), Size = new Vector2(920, 90), Alignment = BoxContainer.AlignmentMode.Center };
        cr.AddThemeConstantOverride("separation", 16);
        _challengeRow = cr;
        _lobbyPane.AddChild(cr);
        _challengeEdit = new LineEdit { CustomMinimumSize = new Vector2(520, 80), MaxLength = Usernames.MaxLength, PlaceholderText = "Friend's username", Alignment = HorizontalAlignment.Center };
        StyleEdit(_challengeEdit, 34);
        cr.AddChild(_challengeEdit);
        var chBtn = Plate("Challenge", false, 300);
        chBtn.Pressed += () => _ = Challenge(_challengeEdit.Text.Trim());
        _challengeEdit.TextSubmitted += t => _ = Challenge(t.Trim());
        cr.AddChild(chBtn);
        _chBtn = chBtn;

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

    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void FinishBuild()
    {
        MenuButtons.BreatheTitle(_title);

        if (_sync == null) SetStatus("Online play needs the cloud connection — it isn't set up on this build.");
        else if (_sm?.Session?.IsValid != true) SetStatus("Not signed in yet. Give it a moment, or check your connection.");
        else SetStatus($"Signed in as {_sm.PublicLabel}");
        }

    public override void _Process(double delta)
    {
        // FABLE-054: on the menu, look for challenges every few seconds
        if (_view == View.Menu && _sm?.HasAccount == true && !_invitePolling)
        {
            _invitePoll += delta;
            if (_invitePoll >= 4) { _invitePoll = 0; _ = PollInvites(); }
        }
        if (_view != View.Lobby || _started || string.IsNullOrEmpty(_id)) return;
        _poll += delta;
        if (_poll < 1.5 || _polling) return;
        _poll = 0;
        _ = PollLobby();
    }

    // ── actions ─────────────────────────────────────────────────────────────

    private (string name, string cls, List<string> deck)? MyLoadout()
    {
        // FABLE-054: never an email — your username, or a name that passes the filter, or "Delver"
        string name = !string.IsNullOrEmpty(_sm?.Username) ? _sm!.Username! : _nameEdit.Text.Trim();
        if (string.IsNullOrEmpty(name)) name = "Delver";
        if (Usernames.Problem(name) is string bad && name != "Delver") { SetStatus("Your name: " + bad); return null; }
        SaveName(name);
        string cls = CampaignContext.ChosenClass;
        var deck = CampaignContext.PlayerDeckIds;
        if (string.IsNullOrEmpty(cls) || deck == null || deck.Count == 0)
        {
            SetStatus("Start a campaign first so you have a class and a deck to bring.");
            return null;
        }
        // FABLE-054: the deck's two artifacts travel with it (see SeatConfig.FromPosted)
        var arts = ArtifactRegistry.PlayerLoadout(cls, CampaignContext.Progression?.ActiveArtifacts);
        return (name, cls, SeatConfig.ToPosted(deck, arts));
    }

    // ── FABLE-054: challenges by username ───────────────────────────────────

    private async Task Challenge(string username)
    {
        if (_busy || _sm == null || string.IsNullOrEmpty(_id)) return;
        if (username.Length == 0) { SetStatus("Type your friend's username."); return; }
        SetBusy(true);
        SetStatus($"Challenging {username}…");
        try
        {
            var err = await _sm.ChallengePlayer(username, _id).ConfigureAwait(false);
            Later(nameof(EndBusy), err ?? $"Challenge sent. {username} sees it on their Online screen.");
        }
        catch (Exception ex) { Later(nameof(EndBusy), "Couldn't send the challenge: " + ex.Message); }
    }

    /// <summary>FABLE-104: lock the lobby actions (main thread only).</summary>
    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (_hostBtn != null && IsInstanceValid(_hostBtn)) _hostBtn.Disabled = busy;
        if (_coopBtn != null && IsInstanceValid(_coopBtn)) _coopBtn.Disabled = busy || string.IsNullOrEmpty(_coopTarget);
        if (_joinBtn != null && IsInstanceValid(_joinBtn)) _joinBtn.Disabled = busy;
        if (_chBtn != null && IsInstanceValid(_chBtn)) _chBtn.Disabled = busy;
    }

    /// <summary>FABLE-104: an action finished (deferred to the main thread): unlock, and say how it went.</summary>
    private void EndBusy(string status)
    {
        SetBusy(false);
        if (!string.IsNullOrEmpty(status)) SetStatus(status);
    }

    private async Task PollInvites()
    {
        if (_sm == null) return;
        _invitePolling = true;
        var list = await _sm.MyChallenges().ConfigureAwait(false);
        _invitePolling = false;
        _invite = list.FirstOrDefault();
        Later(nameof(ShowInvite));
    }

    private void ShowInvite()
    {
        if (_inviteBanner == null) return;
        _inviteBanner.Visible = _view == View.Menu && _invite != null;
        if (_invite != null) _inviteText.Text = $"{_invite.FromName} challenges you to a duel";
    }

    private async Task AnswerInvite(bool accept)
    {
        var inv = _invite;
        if (_sm == null || inv == null) return;
        _invite = null;
        Later(nameof(ShowInvite));
        var code = await _sm.AnswerChallenge(inv.Id, accept).ConfigureAwait(false);
        if (!accept) return;
        if (string.IsNullOrEmpty(code)) { Later(nameof(SetStatus), "That lobby has closed."); return; }
        Later(nameof(JoinDeferred), code);
    }

    private void JoinDeferred(string code) => _ = Join(code);

    private async Task Host(string kind, string target)
    {
        if (_busy || _sync == null || _sm?.Session == null) return;
        var me = MyLoadout(); if (me == null) return;
        SetBusy(true);
        SetStatus("Opening a lobby…");
        _kind = kind;
        try
        {
            var m = me.Value;
            var r = await _sm.Authed(s => _sync.Create(s, kind, target, kind == "coop" ? 5 : 2, m.name, m.cls, m.deck), x => x.error).ConfigureAwait(false);
            Later(nameof(AfterHost), r.ok, r.id, r.code, r.error);
        }
        catch (Exception ex) { Later(nameof(EndBusy), "Couldn't open a lobby: " + ex.Message); }
    }

    private void AfterHost(bool ok, string id, string code, string error)
    {
        SetBusy(false);
        if (!ok) { SetStatus("Couldn't open a lobby: " + error); return; }
        _id = id; _code = code; _host = true;
        ShowLobby();
        SetStatus(_kind == "coop" ? "Give friends the code. Start when everyone is in." : "Give your friend the code. Start when they're in.");
    }

    private async Task Join(string code)
    {
        if (_busy || _sync == null || _sm?.Session == null) return;
        if (code.Length != 6) { SetStatus("A code is six characters."); return; }
        var me = MyLoadout(); if (me == null) return;
        SetBusy(true);
        SetStatus("Looking for that lobby…");
        try
        {
            var m = me.Value;
            var f = await _sm.Authed(s => _sync.Find(s, code), x => x.error).ConfigureAwait(false);
            if (!f.ok || f.found == null)
            {
                Later(nameof(EndBusy), f.ok ? "No open lobby has that code." : "Couldn't look it up: " + f.error);
                return;
            }
            if (f.found.State != "open")
            {
                Later(nameof(EndBusy), "That match has already started.");
                return;
            }
            var found = f.found;
            var j = await _sm.Authed(s => _sync.Join(s, found.Id, m.name, m.cls, m.deck), x => x.error).ConfigureAwait(false);
            Later(nameof(AfterJoin), j.ok, found.Id, code.ToUpperInvariant(), found.Kind, found.HostName, j.error);
        }
        catch (Exception ex) { Later(nameof(EndBusy), "Couldn't join: " + ex.Message); }
    }

    private void AfterJoin(bool ok, string id, string code, string kind, string hostName, string error)
    {
        SetBusy(false);
        if (!ok) { SetStatus("Couldn't join: " + error); return; }
        _id = id; _code = code; _host = false; _kind = kind;
        ShowLobby();
        SetStatus($"In {hostName}'s lobby. Waiting for them to start.");
    }

    private async Task ToggleReady()
    {
        if (_sync == null || _sm?.Session == null || _lobby?.Mine == null) return;
        bool ready = _lobby.Mine.Status != "ready";
        string id = _id;
        await _sm.Authed(s => _sync.SetReady(s, id, ready), x => x.error).ConfigureAwait(false);
    }

    private async Task Start()
    {
        if (_sync == null || _sm?.Session == null || !_host) return;
        int present = _lobby?.Members.Count(m => m.Status != "left") ?? 0;
        if (present < 2) { SetStatus("Nobody has joined yet."); return; }
        SetStatus("Starting…");
        string id = _id;
        var r = await _sm.Authed(s => _sync.Start(s, id), x => x.error).ConfigureAwait(false);
        if (!r.ok) Later(nameof(SetStatus), "Couldn't start: " + r.error);
        // the poll sees "running" and launches, same as the guest's phone
    }

    private async Task Leave()
    {
        _leaving = true;   // FABLE-104: a poll answer still in flight must not start the match we are leaving
        string id = _id;
        if (_sync != null && _sm?.Session != null && !string.IsNullOrEmpty(id))
            await _sm.Authed(s => _sync.Leave(s, id), x => x.error).ConfigureAwait(false);
        Later(nameof(ShowMenu));
    }

    private async Task PollLobby()
    {
        if (_sync == null || _sm?.Session == null) return;
        _polling = true;
        string id = _id;
        OnlinePlaySync.Lobby? lobby = null; bool ok = false; string error = "";
        try
        {
            var r = await _sm.Authed(s => _sync.GetLobby(s, id), x => x.error).ConfigureAwait(false);
            ok = r.ok; lobby = r.lobby; error = r.error;
        }
        catch (Exception ex) { error = ex.Message; }
        finally { _polling = false; }
        // FABLE-104: the answer is for the lobby we asked about; if we have left it (or moved to another) since, drop it
        if (_view != View.Lobby || _leaving || !string.Equals(id, _id, StringComparison.OrdinalIgnoreCase)) return;
        _lobby = ok ? lobby : _lobby;   // FABLE-095: set before the screen is told, so it never reads the old lobby
        if (ok && lobby != null) Later(nameof(ApplyLobby), Godot.Variant.From(0));
        if (!ok) Later(nameof(SetStatus), "Connection hiccup: " + error);
    }

    private void ApplyLobby(Variant _)
    {
        var l = _lobby;
        if (l == null) return;
        if (_view != View.Lobby || _leaving || !string.Equals(l.Id, _id, StringComparison.OrdinalIgnoreCase)) return;   // FABLE-104: a late answer for a lobby we left
        var lines = new List<string>();
        foreach (var m in l.Members.OrderBy(m => m.Seat))
        {
            string tag = m.Status == "left" ? "left" : m.Status == "ready" ? "ready" : m.Seat == 0 ? "host" : "joined";
            string cls = string.IsNullOrEmpty(m.ClassId) ? "" : $"  ·  {char.ToUpperInvariant(m.ClassId[0])}{m.ClassId[1..]}";
            lines.Add($"{(m.Me ? "▸ " : "")}{m.DisplayName}{cls}   ({tag})");
        }
        _players.Text = string.Join("\n", lines);
        FillSeats(l);
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
            var match = OnlineMatch.FromLobby(l, _sync!, _sm!);
            if (match == null) { SetStatus("Couldn't build the match from the lobby."); return; }
            _started = true;
            OnlineMatch.Current = match;
            CampaignContext.CurrentEncounter = null;
            CampaignContext.IsArenaDuel = false;
            CampaignContext.DebugSeed = null;
            DuelScene.ExitTrace($"online: {l.Kind} {_id} seat {match.LocalSeat} → DuelScene");
            LoadScreen.Go(GetTree(), CampaignRun.DuelScenePath);
        }
    }

    // ── views ───────────────────────────────────────────────────────────────

    private void ShowLobby()
    {
        _view = View.Lobby;
        _leaving = false;
        _menu.Visible = false;
        _lobbyPane.Visible = true;
        _codeLabel.Text = _code;
        _players.Text = "…";
        _challengeRow.Visible = _host && _kind == "pvp1v1";
        if (_inviteBanner != null) _inviteBanner.Visible = false;
        _title.Text = _kind == "coop" ? "Co-op lobby" : "Duel lobby";
        _kicker.Text = _host ? "YOUR LOBBY  ·  SHARE THIS CODE" : "LOBBY CODE";
        if (Painted)
        {
            if (_kind == "coop") _kicker.Text = _host ? "CO-OP LOBBY  ·  SHARE THIS CODE" : "CO-OP LOBBY  ·  CODE";
            if (_chBtn != null) _chBtn.Visible = _host && _kind == "pvp1v1";
            if (_copyBtn != null) _copyBtn.Visible = !(_host && _kind == "pvp1v1");
            _startBtn.Visible = _host; _readyBtn.Visible = !_host;
            FillSeats(null);
        }
        _poll = 10;   // poll at once
        _started = false;
    }

    private void ShowMenu()
    {
        _view = View.Menu;
        _leaving = false;
        _id = ""; _code = ""; _lobby = null; _started = false;
        _menu.Visible = true;
        _lobbyPane.Visible = false;
        _title.Text = "Online";
        _kicker.Text = "PLAY WITH FRIENDS";
    }

    private void SetStatus(string s) { if (_status != null && IsInstanceValid(_status)) _status.Text = s; }

    /// <summary>FABLE-095: a reply that lands after this screen was closed is dropped - it used to throw inside a background task.</summary>
    private void Later(string method, params Variant[] args)
    {
        if (!IsInstanceValid(this)) return;
        try { CallDeferred(method, args); } catch (ObjectDisposedException) { }
    }

    private void PickCoopTarget()
    {
        // The host's next campaign fight, or the first boss it knows: something everyone can face.
        var region = CampaignRun.LoadCurrentRegion();
        var next = CampaignRun.FindNextDuelNode(region);
        var enc = next?.Encounter != null && CampaignContext.EncounterIndex.TryGetValue(next.Encounter, out var e) ? e : null;
        enc ??= CampaignContext.EncounterIndex.Values.FirstOrDefault(x => !x.IsTutorial);
        if (enc != null) { _coopTarget = enc.Id; _coopTitle = enc.Name; }
    }

    // ── FABLE-103: the lobby on the war-table painting ──────────────────────
    // painting pixels (1376x768, stretched to the screen): the code stone, the two thrones' parchments, the scroll under
    // the table, the four stone buttons and the long scroll at the foot. Buttons turn see-through over their stones.

    private static Vector2 P(Vector2 vp, float x, float y) => new(x / 1376f * vp.X, y / 768f * vp.Y);
    private static int Px(Vector2 vp, float px) => Mathf.RoundToInt(px / 768f * vp.Y * 1.40f);
    private static void Place(Control c, Vector2 a, Vector2 b) { c.Position = a; c.Size = b - a; }
    private static readonly float[] StoneX = { 236, 468, 700, 934 };

    private Button Stone(Control parent, Vector2 vp, int i, string text, bool primary)
    {
        var b = Plate(text, primary, 0);
        b.CustomMinimumSize = Vector2.Zero;
        Place(b, P(vp, StoneX[i] + 4, 609), P(vp, StoneX[i] + 204, 678));
        b.AddThemeFontSizeOverride("font_size", Px(vp, 24));
        b.AddThemeColorOverride("font_color", Color.FromHtml("#F0D89A"));
        b.AddThemeColorOverride("font_disabled_color", new Color(0.55f, 0.52f, 0.46f));
        b.AddThemeConstantOverride("outline_size", 5); b.AddThemeColorOverride("font_outline_color", new Color(0.03f, 0.03f, 0.03f, 0.85f));
        parent.AddChild(b);
        return b;
    }

    private Label ParchmentText(Control parent, Vector2 a, Vector2 b, int px)
    {
        var l = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, ClipText = true, MouseFilter = MouseFilterEnum.Ignore };
        Place(l, a, b);
        l.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(px)); l.AddThemeFontSizeOverride("font_size", px);
        l.AddThemeColorOverride("font_color", new Color(0.20f, 0.12f, 0.05f));
        l.AddThemeConstantOverride("line_spacing", -2);
        l.SetMeta("skinned", true);
        parent.AddChild(l);
        return l;
    }

    private static void StyleOnPainting(LineEdit e, int px, Color ink, Color hint)
    {
        e.AddThemeFontSizeOverride("font_size", px);
        e.AddThemeColorOverride("font_color", ink);
        e.AddThemeColorOverride("font_placeholder_color", hint);
        e.AddThemeColorOverride("caret_color", ink);
        var box = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = new Color(ink, 0.35f), BorderWidthBottom = 2, ContentMarginLeft = 8, ContentMarginRight = 8 };
        e.AddThemeStyleboxOverride("normal", box);
        e.AddThemeStyleboxOverride("focus", box);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BuildPaintedMenu(Vector2 vp)
    {
        _menu = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _menu.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_menu);
        var ink = new Color(0.20f, 0.12f, 0.05f);

        // the scroll under the table: your name (fixed when you have a username)
        bool hasUsername = !string.IsNullOrEmpty(_sm?.Username);
        _nameEdit = new LineEdit { Text = hasUsername ? _sm!.Username! : LoadName(), MaxLength = Usernames.MaxLength, PlaceholderText = "Your name", Editable = !hasUsername, Alignment = HorizontalAlignment.Center };
        StyleEdit(_nameEdit);
        StyleOnPainting(_nameEdit, Px(vp, 20), ink, new Color(0.42f, 0.32f, 0.20f));
        if (hasUsername) _nameEdit.Text = "Playing as " + _sm!.Username!;
        Place(_nameEdit, P(vp, 545, 540), P(vp, 828, 580));
        _menu.AddChild(_nameEdit);

        // the code stone: type a friend's code into it
        _codeEdit = new LineEdit { MaxLength = 6, PlaceholderText = "CODE", Alignment = HorizontalAlignment.Center };
        StyleEdit(_codeEdit, 40);
        _codeEdit.AddThemeFontOverride("font", ThemeTokens.GetCardNameFont(80));
        StyleOnPainting(_codeEdit, Px(vp, 52), new Color(0.03f, 0.17f, 0.17f), new Color(0.10f, 0.36f, 0.35f, 0.85f));
        _codeEdit.AddThemeConstantOverride("outline_size", 6); _codeEdit.AddThemeColorOverride("font_outline_color", new Color(0.75f, 1f, 0.95f, 0.35f));
        _codeEdit.TextChanged += t => { string u = t.ToUpperInvariant(); if (u != t) { _codeEdit.Text = u; _codeEdit.CaretColumn = u.Length; } };
        _codeEdit.TextSubmitted += t => _ = Join(t.Trim());
        Place(_codeEdit, P(vp, 530, 262), P(vp, 850, 345));
        _menu.AddChild(_codeEdit);

        // the thrones: you on the left, the empty seat on the right
        var me = ParchmentText(_menu, P(vp, 324, 368), P(vp, 424, 462), Px(vp, 15));
        string cls = CampaignContext.ChosenClass;
        me.Text = (hasUsername ? _sm!.Username! : (LoadName() is { Length: > 0 } n ? n : "You")) + (string.IsNullOrEmpty(cls) ? "" : $"\n{char.ToUpperInvariant(cls[0])}{cls[1..]}");
        var seat = ParchmentText(_menu, P(vp, 962, 368), P(vp, 1054, 466), Px(vp, 15));
        seat.Text = "Host a duel, or type a friend's code";

        var hostBtn = Stone(_menu, vp, 0, "Host a duel", true);
        _hostBtn = hostBtn;
        hostBtn.Pressed += () => _ = Host("pvp1v1", "pvp");
        try { PickCoopTarget(); } catch (Exception ex) { DuelScene.ExitTrace("OnlineLobby: no co-op target: " + ex); }
        var coopBtn = Stone(_menu, vp, 1, "Host co-op", false);
        coopBtn.Disabled = string.IsNullOrEmpty(_coopTarget);
        coopBtn.TooltipText = _coopTitle;
        _coopBtn = coopBtn;
        coopBtn.Pressed += () => _ = Host("coop", _coopTarget);
        var joinBtn = Stone(_menu, vp, 2, "Join", false);
        _joinBtn = joinBtn;
        joinBtn.Pressed += () =>
        {
            string c = _codeEdit.Text.Trim();
            if (c.Length == 0) { _codeEdit.GrabFocus(); SetStatus("Type the six-letter code into the glowing stone, then tap Join."); return; }
            _ = Join(c);
        };

        // an invitation from a friend, by username: a banner over the table (keeps its own glass on the painting)
        _inviteBanner = new PanelContainer { Visible = false, ZIndex = 5 };
        Place(_inviteBanner, P(vp, 300, 470), P(vp, 1076, 530));
        _inviteBanner.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.06f, 0.12f, 0.12f, 0.96f), BorderColor = new Color(0.45f, 0.92f, 0.86f), BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2, CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14, ContentMarginLeft = 24, ContentMarginRight = 12, ShadowColor = new Color(0.3f, 0.95f, 0.88f, 0.35f), ShadowSize = 12 });
        _inviteBanner.SetMeta("skinned", true);
        AddChild(_inviteBanner);
        var inv = new HBoxContainer(); inv.AddThemeConstantOverride("separation", 16);
        _inviteBanner.AddChild(inv);
        _inviteText = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center, ClipText = true };
        _inviteText.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(30)); _inviteText.AddThemeFontSizeOverride("font_size", 30);
        _inviteText.AddThemeColorOverride("font_color", new Color(0.95f, 0.88f, 0.66f));
        _inviteText.SetMeta("skinned", true);
        inv.AddChild(_inviteText);
        var accept = Plate("Accept", true, 200); accept.CustomMinimumSize = new Vector2(200, 60); accept.SetMeta("skinned", true);
        accept.Pressed += () => _ = AnswerInvite(true);
        inv.AddChild(accept);
        var decline = Plate("Decline", false, 200); decline.CustomMinimumSize = new Vector2(200, 60); decline.SetMeta("skinned", true);
        decline.Pressed += () => _ = AnswerInvite(false);
        inv.AddChild(decline);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void BuildPaintedLobby(Vector2 vp)
    {
        _lobbyPane = new Control { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _lobbyPane.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_lobbyPane);
        // the code, carved into the glowing stone
        _codeLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        Place(_codeLabel, P(vp, 520, 255), P(vp, 860, 352));
        _codeLabel.AddThemeFontOverride("font", ThemeTokens.GetCardNameFont(90)); _codeLabel.AddThemeFontSizeOverride("font_size", Px(vp, 60));
        _codeLabel.AddThemeColorOverride("font_color", new Color(0.03f, 0.17f, 0.17f));
        _codeLabel.AddThemeConstantOverride("outline_size", 8); _codeLabel.AddThemeColorOverride("font_outline_color", new Color(0.75f, 1f, 0.95f, 0.40f));
        _codeLabel.SetMeta("skinned", true);
        _lobbyPane.AddChild(_codeLabel);
        _players = new Label { Visible = false, MouseFilter = MouseFilterEnum.Ignore };   // the thrones show the players instead
        _lobbyPane.AddChild(_players);
        _seatL = ParchmentText(_lobbyPane, P(vp, 324, 368), P(vp, 424, 462), Px(vp, 15));
        _seatR = ParchmentText(_lobbyPane, P(vp, 962, 368), P(vp, 1054, 466), Px(vp, 15));

        // the scroll: a friend's username, for the host of a duel
        _challengeEdit = new LineEdit { MaxLength = Usernames.MaxLength, PlaceholderText = "Friend's username", Alignment = HorizontalAlignment.Center };
        StyleEdit(_challengeEdit, 34);
        StyleOnPainting(_challengeEdit, Px(vp, 20), new Color(0.20f, 0.12f, 0.05f), new Color(0.42f, 0.32f, 0.20f));
        Place(_challengeEdit, P(vp, 545, 540), P(vp, 828, 580));
        _challengeEdit.TextSubmitted += t => _ = Challenge(t.Trim());
        _challengeRow = _challengeEdit;
        _lobbyPane.AddChild(_challengeEdit);

        _chBtn = Stone(_lobbyPane, vp, 0, "Challenge", false);
        _chBtn.Pressed += () => _ = Challenge(_challengeEdit.Text.Trim());
        _copyBtn = Stone(_lobbyPane, vp, 0, "Copy code", false);
        _copyBtn.Pressed += () => { DisplayServer.ClipboardSet(_code); SetStatus($"Code {_code} copied - send it to your friends."); };
        _leaveBtn = Stone(_lobbyPane, vp, 1, "Leave", false);
        _leaveBtn.Pressed += () => _ = Leave();
        _readyBtn = Stone(_lobbyPane, vp, 2, "I'm ready", false);
        _readyBtn.Pressed += () => _ = ToggleReady();
        _startBtn = Stone(_lobbyPane, vp, 2, "Start", true);
        _startBtn.Pressed += () => _ = Start();
    }

    /// <summary>FABLE-103: the host on the left throne, everyone else on the right (or the empty seat's line).</summary>
    private void FillSeats(OnlinePlaySync.Lobby? l)
    {
        if (!Painted || _seatL == null) return;
        string Who(OnlinePlaySync.Member m)
        {
            string cls = string.IsNullOrEmpty(m.ClassId) ? "" : $" · {char.ToUpperInvariant(m.ClassId[0])}{m.ClassId[1..]}";
            string tag = m.Status == "left" ? "left" : m.Status == "ready" ? "ready" : m.Seat == 0 ? "host" : "joined";
            return $"{m.DisplayName}{cls}\n({tag})";
        }
        if (l == null) { _seatL.Text = "…"; _seatR.Text = _kind == "coop" ? "Waiting for allies" : "Waiting for a challenger"; return; }
        var ms = l.Members.OrderBy(m => m.Seat).ToList();
        var host = ms.FirstOrDefault(m => m.Seat == 0);
        _seatL.Text = host != null ? Who(host) : "…";
        var rest = ms.Where(m => m.Seat != 0 && m.Status != "left").ToList();
        _seatR.Text = rest.Count == 0 ? (_kind == "coop" ? "Waiting for allies" : "Waiting for a challenger") : string.Join("\n", rest.Select(Who));
        int px = Px(GetViewportRect().Size, rest.Count > 1 ? 12 : 15);
        _seatR.AddThemeFontSizeOverride("font_size", px);
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

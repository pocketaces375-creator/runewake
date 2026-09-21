using System;
using System.Threading.Tasks;
using Godot;
using Runewake.Engine.Supabase;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// The account panel. FABLE-018. Opened from the title screen's Account plate,
/// and opened FOR the player when a save conflict needs a decision.
///
/// Three states, one panel:
///
///   GUEST      "Guest 3F2A · Backed up". Progress is in the cloud under an
///              anonymous user. One button: Link an email — so a lost phone
///              is not a lost account.
///
///   LINKED     "adam@…  · Backed up". Sign out available.
///
///   CONFLICT   two cards, "This phone" and "Cloud", each summarised
///              (level, nodes, shards, relics, when, which device). Pick one.
///              Nothing is discarded until they pick.
///
/// Plus, from any state, "Sign in on another phone" for the reverse trip:
/// email → 6-digit code → this phone adopts that account's save.
///
/// The whole thing is code-built (no .tscn), single file, and every network
/// call goes through SyncManager, which returns results rather than throwing
/// — so the worst a bad network does here is print a red line.
/// </summary>
public partial class AccountPanel : Control
{
    private SyncManager _sync = null!;
    private VBoxContainer _body = null!;
    private Label _statusLine = null!;
    private Label _errorLine = null!;

    private const float PanelW = 760f;

    /// <summary>Open (or focus) the panel over <paramref name="host"/>.</summary>
    public static AccountPanel Open(Control host, SyncManager sync)
    {
        var existing = host.GetNodeOrNull<AccountPanel>("AccountPanel");
        if (existing != null) { existing.Rebuild(); return existing; }
        var p = new AccountPanel { Name = "AccountPanel" };
        p._sync = sync;
        host.AddChild(p);
        host.MoveChild(p, host.GetChildCount() - 1);
        return p;
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        ZIndex = 900;
        MouseFilter = MouseFilterEnum.Stop;

        var dim = new ColorRect { Color = new Color(BgDark.R, BgDark.G, BgDark.B, 0.82f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        dim.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true }) Close(); };

        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(centre);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(PanelW, 0), MouseFilter = MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", StyleWornBorder(borderColor: Gold, width: 2, radius: RadiusMedium, bgColor: CardFace));
        centre.AddChild(panel);

        var pad = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            pad.AddThemeConstantOverride(side, 28);
        panel.AddChild(pad);

        _body = new VBoxContainer();
        _body.AddThemeConstantOverride("separation", 12);
        pad.AddChild(_body);

        _sync.StatusChanged += OnStatusChanged;
        Rebuild();
    }

    public override void _ExitTree()
    {
        if (_sync != null) _sync.StatusChanged -= OnStatusChanged;
    }

    private void OnStatusChanged(string s)
    {
        if (_statusLine != null && IsInstanceValid(_statusLine)) _statusLine.Text = s;
    }

    private void Close()
    {
        // A conflict is not dismissible by tapping outside — a decision is the
        // only way through, otherwise the next launch asks again anyway.
        if (_sync.PendingConflict != null) return;
        QueueFree();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Screens
    // ═════════════════════════════════════════════════════════════════════

    private void Rebuild()
    {
        ClearBody();

        Header(_sync.PendingConflict != null ? "TWO SAVES FOUND" : "ACCOUNT");

        _statusLine = Body(_sync.Status, TextSecondary, FontBody);
        _errorLine = Body("", Ember, FontBody);
        _errorLine.Visible = false;

        if (!_sync.IsConfigured)
        {
            Body("This build was made without account settings, so progress lives on this phone only.", TextMuted, FontBody);
            Buttons(("Close", () => QueueFree()));
            return;
        }

        if (_sync.PendingConflict != null) { ConflictScreen(_sync.PendingConflict); return; }

        if (!_sync.IsSignedIn)
        {
            Body("Not signed in. Usually this means no connection — the game keeps working, and will back up when it can.", TextMuted, FontBody);
            Buttons(("Try again", () => _ = Run(() => _sync.SyncNow())),
                    ("Sign in on another phone…", SignInScreen),
                    ("Close", () => QueueFree()));
            return;
        }

        if (_sync.IsLinked)
        {
            Body("Your progress is tied to this email. Sign in with it on any phone to pick up where you left off.", TextMuted, FontBody);
            Buttons(("Sync now", () => _ = Run(() => _sync.SyncNow())),
                    ("Sign out of this phone", () => _ = Run(async () => { await _sync.SignOut(); Rebuild(); })),
                    ("Close", () => QueueFree()));
            return;
        }

        // Guest
        Body("You're playing as a guest. Progress is backed up — but only this phone knows which account is yours. Link an email so a new phone can find it.", TextMuted, FontBody);
        Buttons(("Link an email", LinkScreen),
                ("Sign in on another phone…", SignInScreen),
                ("Close", () => QueueFree()));
    }

    private void LinkScreen()
    {
        ClearBody();
        Header("LINK AN EMAIL");
        Body("We'll send a 6-digit code. Your progress and account stay exactly as they are — this just makes them recoverable.", TextMuted, FontBody);
        _errorLine = Body("", Ember, FontBody); _errorLine.Visible = false;

        var email = Field("you@example.com");
        Buttons(("Send code", () => _ = Run(async () =>
                {
                    var r = await _sync.LinkEmail(email.Text);
                    if (!r.Ok) { ShowError(r.Error); return; }
                    CodeScreen(email.Text, isLink: true);
                })),
                ("Back", Rebuild));
    }

    private void SignInScreen()
    {
        ClearBody();
        Header("SIGN IN");
        Body("Enter the email you linked on your other phone. We'll send a code there. This phone will then load THAT account's progress.", TextMuted, FontBody);
        _errorLine = Body("", Ember, FontBody); _errorLine.Visible = false;

        var email = Field("you@example.com");
        Buttons(("Send code", () => _ = Run(async () =>
                {
                    var r = await _sync.SendSignInCode(email.Text);
                    if (!r.Ok) { ShowError(r.Error); return; }
                    CodeScreen(email.Text, isLink: false);
                })),
                ("Back", Rebuild));
    }

    private void CodeScreen(string email, bool isLink)
    {
        ClearBody();
        Header("ENTER THE CODE");
        Body($"Sent to {email}. Check spam if it's slow.", TextMuted, FontBody);
        _errorLine = Body("", Ember, FontBody); _errorLine.Visible = false;

        var code = Field("123456");
        code.MaxLength = 8;
        Buttons(("Confirm", () => _ = Run(async () =>
                {
                    var r = isLink ? await _sync.ConfirmLinkEmail(email, code.Text)
                                   : await _sync.ConfirmSignInCode(email, code.Text);
                    if (!r.Ok) { ShowError(r.Error); return; }
                    Rebuild();
                })),
                ("Back", Rebuild));
    }

    private void ConflictScreen(SyncManager.ConflictInfo c)
    {
        Body("This phone and the cloud both have progress that the other doesn't. Pick the one to keep. The other is replaced.", TextMuted, FontBody);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 20);
        _body.AddChild(row);

        row.AddChild(SaveCard("THIS PHONE", c.LocalSummary, () => _ = Run(async () => { await _sync.ResolveConflict(useCloud: false); QueueFree(); })));
        row.AddChild(SaveCard("CLOUD", c.CloudSummary, () => _ = Run(async () => { await _sync.ResolveConflict(useCloud: true); QueueFree(); })));
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Widgets
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Remove now, free later: a QueueFree'd child still occupies the VBox until end of frame.</summary>
    private void ClearBody()
    {
        foreach (var c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }
    }

    private void Header(string text)
    {
        var h = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        ApplyHeaderFont(h, FontSubtitle);
        h.Modulate = Gold;
        _body.AddChild(h);
        _body.AddChild(new ColorRect { Color = new Color(Gold.R, Gold.G, Gold.B, 0.3f), CustomMinimumSize = new Vector2(0, 1) });
    }

    private Label Body(string text, Color colour, int size)
    {
        var l = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(PanelW - 56, 0),
        };
        ApplyBodyFont(l, size);
        l.Modulate = colour;
        _body.AddChild(l);
        return l;
    }

    private LineEdit Field(string placeholder)
    {
        var f = new LineEdit
        {
            PlaceholderText = placeholder,
            CustomMinimumSize = new Vector2(PanelW - 56, MinButtonHeight),
            Alignment = HorizontalAlignment.Center,
        };
        f.AddThemeFontSizeOverride("font_size", FontLargeBody);
        _body.AddChild(f);
        f.CallDeferred(Control.MethodName.GrabFocus);
        return f;
    }

    private void Buttons(params (string label, Action act)[] items)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        _body.AddChild(col);
        foreach (var (label, act) in items)
        {
            var b = new Button { Text = label, CustomMinimumSize = new Vector2(PanelW - 56, MinButtonHeight) };
            b.AddThemeFontSizeOverride("font_size", FontLargeBody);
            b.AddThemeStyleboxOverride("normal", MenuButtons.Normal());
            b.AddThemeStyleboxOverride("hover", MenuButtons.Hover());
            b.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed());
            b.AddThemeColorOverride("font_color", Color.FromHtml("#E8DCC8"));
            var captured = act;
            b.Pressed += () =>
            {
                GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("click");
                captured();
            };
            col.AddChild(b);
        }
    }

    private Control SaveCard(string title, string summary, Action choose)
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2((PanelW - 76) / 2, 0) };
        card.AddThemeStyleboxOverride("panel", StyleWornBorder(borderColor: BorderSubtle, width: 1, radius: RadiusMedium, bgColor: SurfaceStone));
        var pad = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) pad.AddThemeConstantOverride(side, 16);
        card.AddChild(pad);
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 8); pad.AddChild(v);

        var t = new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center };
        ApplyHeaderFont(t, FontLargeBody); t.Modulate = Gold; v.AddChild(t);
        var s = new Label { Text = summary, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        ApplyBodyFont(s, FontBody); s.Modulate = TextSecondary; v.AddChild(s);

        var b = new Button { Text = "Keep this one", CustomMinimumSize = new Vector2(0, MinButtonHeight) };
        b.AddThemeFontSizeOverride("font_size", FontLargeBody);
        b.AddThemeStyleboxOverride("normal", MenuButtons.Normal());
        b.AddThemeStyleboxOverride("hover", MenuButtons.Hover());
        b.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed());
        b.Pressed += () => { GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("click"); choose(); };
        v.AddChild(b);
        return card;
    }

    private void ShowError(string msg)
    {
        if (_errorLine == null || !IsInstanceValid(_errorLine)) return;
        _errorLine.Text = msg;
        _errorLine.Visible = true;
    }

    /// <summary>
    /// Run an async action from a button. Godot installs a
    /// SynchronizationContext on the main thread, so an await HERE (no
    /// ConfigureAwait) resumes on the main thread even though SyncManager's
    /// internals hop to the pool — which is why the lambdas above may touch
    /// nodes after awaiting. The catch path is deferred anyway, belt and
    /// braces.
    /// </summary>
    private async Task Run(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AccountPanel] {ex}");
            Callable.From(() => ShowError(ex.GetType().Name + ": " + ex.Message)).CallDeferred();
        }
    }
}

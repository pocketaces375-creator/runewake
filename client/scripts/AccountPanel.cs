using System;
using System.Threading.Tasks;
using Godot;
using Runewake.Engine.Supabase;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// The account panel. FABLE-018, reworked for FABLE-ACCOUNTS-1 (email +
/// password accounts). Opened from the start screen's Create account / Sign
/// in buttons, from the title screen's account label, and opened FOR the
/// player when a save conflict needs a decision.
///
///   NO ACCOUNT   (guest, or not signed in) → Create an account / Sign in.
///                Creating an account moves this phone's progress into it.
///   ACCOUNT      email shown · Sync now · Change password · Sign out.
///   CONFLICT     two cards, "This phone" and "Account", each summarised
///                (level, nodes, shards, relics, when). Pick one. Nothing is
///                discarded until they pick.
///
/// Sign-up with the dashboard's "Confirm email" on: a "Check your email"
/// screen, then "I've confirmed — sign in". Forgot password: an emailed
/// 6-digit code signs in, then a new password is chosen.
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
    private Action? _firstScreen;
    private bool _busy;
    /// <summary>FABLE-ACCOUNTS-2: what Android's Back does on the current screen (null = close).</summary>
    private Action? _back;
    /// <summary>FABLE-ACCOUNTS-2: set while "Check your email" is showing — a quiet sign-in attempt.</summary>
    private Func<Task>? _autoCheck;
    private Godot.Timer? _autoTimer;
    private int _autoTries;

    /// <summary>FABLE-ACCOUNTS-1: raised once the phone is signed in to an account (created, or signed in).</summary>
    public event Action? AccountReady;

    private const float PanelW = 1040f;
    private const float Inner = PanelW - 56f;
    private const int HeadSize = 44;
    private const int TextSize = 30;
    private const int ControlSize = 32;
    private const int FieldHeight = 104;
    private const string Working = "Working…";

    /// <summary>Open (or focus) the panel over <paramref name="host"/>.</summary>
    public static AccountPanel Open(Control host, SyncManager sync) => Open(host, sync, null);

    /// <summary>Open straight onto "Create account".</summary>
    public static AccountPanel OpenCreate(Control host, SyncManager sync) => Open(host, sync, p => p.CreateScreen());

    /// <summary>Open straight onto "Sign in".</summary>
    public static AccountPanel OpenSignIn(Control host, SyncManager sync) => Open(host, sync, p => p.SignInScreen(sync.PendingSignupEmail ?? ""));

    private static AccountPanel Open(Control host, SyncManager sync, Action<AccountPanel>? first)
    {
        var existing = host.GetNodeOrNull<AccountPanel>("AccountPanel");
        if (existing != null)
        {
            if (first != null && existing._sync.PendingConflict == null) first(existing); else existing.Rebuild();
            return existing;
        }
        var p = new AccountPanel { Name = "AccountPanel" };
        p._sync = sync;
        if (first != null) p._firstScreen = () => first(p);
        host.AddChild(p);
        host.MoveChild(p, host.GetChildCount() - 1);
        return p;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ZIndex = 900;
        MouseFilter = MouseFilterEnum.Stop;

        var dim = new ColorRect { Color = new Color(BgDark.R, BgDark.G, BgDark.B, 0.86f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        // FABLE-ACCOUNTS-2: tapping outside the panel no longer closes it. On a phone that tap is
        // usually someone dismissing the keyboard, and it threw away the half-filled form or the
        // "Check your email" screen. Every screen has its own Back / Close button.

        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
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

        _autoTimer = new Godot.Timer { WaitTime = 15, OneShot = false, Autostart = true };
        _autoTimer.Timeout += () => _ = AutoCheck();
        AddChild(_autoTimer);

        _sync.StatusChanged += OnStatusChanged;
        if (_firstScreen != null && _sync.PendingConflict == null) _firstScreen();
        else Rebuild();
        _firstScreen = null;
    }

    public override void _ExitTree()
    {
        if (_sync != null) _sync.StatusChanged -= OnStatusChanged;
    }

    private void OnStatusChanged(string s)
    {
        if (_statusLine != null && IsInstanceValid(_statusLine)) _statusLine.Text = s;
    }

    public override void _Notification(int what)
    {
        // FABLE-ACCOUNTS-2: back from the email app → see if the link was tapped, at once.
        if (what == NotificationApplicationFocusIn) _ = AutoCheck();
        // Android Back (button or edge swipe) steps back inside the panel instead of quitting the game.
        if (what == NotificationWMGoBackRequest && !_busy)
        {
            if (_back != null) _back();
            else Close();
        }
    }

    /// <summary>Quietly try to finish a pending sign-up. Not yet confirmed = say nothing and wait.</summary>
    private async Task AutoCheck()
    {
        if (_autoCheck == null || _busy || !IsInstanceValid(this)) return;
        if (++_autoTries > 40) return;      // ~10 minutes of 15 s checks, plus every return to the game
        _busy = true;
        try { await _autoCheck(); }
        catch (Exception ex) { GD.PrintErr($"[AccountPanel] auto-check: {ex.Message}"); }
        finally { _busy = false; }
    }

    private void Close()
    {
        // A conflict is not dismissible by tapping outside — a decision is the
        // only way through, otherwise the next launch asks again anyway.
        if (_sync.PendingConflict != null || _busy) return;
        QueueFree();
    }

    /// <summary>Signed in to an account: tell the start screen, then close (or show the conflict).</summary>
    private void Done()
    {
        if (!IsInstanceValid(this)) return;
        AccountReady?.Invoke();
        if (_sync.PendingConflict != null) Rebuild();
        else QueueFree();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Screens
    // ═════════════════════════════════════════════════════════════════════

    private void Rebuild()
    {
        ClearBody();

        Header(_sync.PendingConflict != null ? "TWO SAVES FOUND" : "ACCOUNT");

        _statusLine = Body(_sync.Status, TextSecondary);
        _statusLine.Visible = _sync.PendingConflict == null;   // the header already says it
        ErrorLine();

        if (!_sync.IsConfigured)
        {
            Body("This build was made without account settings, so progress lives on this phone only.", TextMuted);
            Buttons(("Close", () => QueueFree()));
            return;
        }

        if (_sync.PendingConflict != null) { ConflictScreen(_sync.PendingConflict); return; }

        if (_sync.HasAccount)
        {
            Body("Your progress is saved to this account. Sign in with it on any phone to pick up where you left off.", TextMuted);
            Buttons(("Sync now", () => _ = Run(() => _sync.SyncNow())),
                    ("Change password", NewPasswordScreen));
            Row(("Sign out", () => _ = Run(async () => { await _sync.SignOut(); if (IsInstanceValid(this)) Rebuild(); })),
                ("Close", () => QueueFree()));
            return;
        }

        Body(_sync.IsSignedIn
                ? "You're playing as a guest. Make an account to keep your progress safe — everything on this phone moves into it."
                : "Not signed in. Make an account or sign in to save your progress to it.", TextMuted);
        Buttons(("Create an account", CreateScreen),
                ("Sign in", () => SignInScreen(_sync.PendingSignupEmail ?? "")));
        if (_sync.IsSignedIn) Row(("Close", () => QueueFree()));
        else Row(("Play as guest", () => { _sync.ContinueAsGuest(); QueueFree(); }), ("Close", () => QueueFree()));
    }

    private void CreateScreen()
    {
        ClearBody();
        Header("CREATE ACCOUNT");
        Body("Your progress is saved to your account, so it's safe on any phone. Anything you've played on this phone comes with you.", TextMuted);
        ErrorLine();

        var email = Field("Email", LineEdit.VirtualKeyboardTypeEnum.EmailAddress);
        var pass = Field($"Password ({SupabaseAuth.MinPasswordLength}+ characters)", LineEdit.VirtualKeyboardTypeEnum.Password, secret: true);
        void Submit() => _ = Run(async () =>
        {
            var r = await _sync.CreateAccount(email.Text, pass.Text);
            if (!IsInstanceValid(this)) return;
            if (!r.Ok) { ShowError(r.Error); return; }
            if (r.NeedsConfirmation) { ConfirmEmailScreen(email.Text.Trim(), pass.Text); return; }
            Done();
        });
        email.TextSubmitted += _ => pass.GrabFocus();
        pass.TextSubmitted += _ => Submit();
        Buttons(("Create account", Submit));
        Row(("I have an account", () => SignInScreen(email.Text)), ("Back", Rebuild));
        _back = Rebuild;
    }

    private void SignInScreen() => SignInScreen("");

    private void SignInScreen(string prefill)
    {
        ClearBody();
        Header("SIGN IN");
        var pending = _sync.PendingSignupEmail;
        Body(pending != null && !_sync.HasAccount
                ? $"Almost done: tap the link we emailed to {pending}, then sign in here with your password."
                : "Sign in to load your account's progress on this phone.", TextMuted);
        ErrorLine();

        var email = Field("Email", LineEdit.VirtualKeyboardTypeEnum.EmailAddress);
        email.Text = prefill ?? "";
        var pass = Field("Password", LineEdit.VirtualKeyboardTypeEnum.Password, secret: true);
        void Submit() => _ = Run(async () =>
        {
            var r = await _sync.SignInWithPassword(email.Text, pass.Text);
            if (!IsInstanceValid(this)) return;
            if (!r.Ok) { ShowError(r.Error); return; }
            Done();
        });
        email.TextSubmitted += _ => pass.GrabFocus();
        pass.TextSubmitted += _ => Submit();
        Buttons(("Sign in", Submit));
        Row(("Forgot password?", () => ForgotScreen(email.Text)), ("Back", Rebuild));
        _back = Rebuild;
    }

    private void ConfirmEmailScreen(string email, string password)
    {
        ClearBody();
        Header("CHECK YOUR EMAIL");
        Body($"We sent a link to {email}. Open your email, tap the link, then come back to the game — it signs you in by itself. (If the page the link opens shows an error, that's fine.)", TextMuted);
        ErrorLine();

        async Task Attempt(bool quiet)
        {
            var r = await _sync.SignInWithPassword(email, password);
            if (!IsInstanceValid(this)) return;
            if (r.Ok) { Done(); return; }
            bool notYet = r.Error.StartsWith("Confirm your email first");
            if (!quiet || !notYet) ShowError(notYet ? "Not confirmed yet — tap the link in the email first." : r.Error);
        }
        _autoTries = 0;
        _autoCheck = () => Attempt(quiet: true);

        Buttons(("I've confirmed — sign in", () => _ = Run(() => Attempt(quiet: false))));
        Row(("Send it again", () => _ = Run(async () =>
                {
                    var r = await _sync.ResendConfirmation(email);
                    if (!IsInstanceValid(this)) return;
                    ShowError(r.Ok ? "Sent. Check spam if it's slow." : r.Error, good: r.Ok);
                })),
            ("Back", CreateScreen));
        _back = CreateScreen;
    }

    private void ForgotScreen(string prefill)
    {
        ClearBody();
        Header("FORGOT PASSWORD");
        Body("We'll email you a 6-digit code. It signs you in, then you choose a new password.", TextMuted);
        ErrorLine();

        var email = Field("Email", LineEdit.VirtualKeyboardTypeEnum.EmailAddress);
        email.Text = prefill ?? "";
        void Submit() => _ = Run(async () =>
        {
            var r = await _sync.SendSignInCode(email.Text);
            if (!IsInstanceValid(this)) return;
            if (!r.Ok) { ShowError(r.Error); return; }
            CodeScreen(email.Text.Trim());
        });
        email.TextSubmitted += _ => Submit();
        Buttons(("Send code", Submit));
        Row(("Back", () => SignInScreen(email.Text)));
        _back = () => SignInScreen(email.Text);
    }

    private void CodeScreen(string email)
    {
        ClearBody();
        Header("ENTER THE CODE");
        Body($"Sent to {email}. Check spam if it's slow.", TextMuted);
        ErrorLine();

        var code = Field("123456", LineEdit.VirtualKeyboardTypeEnum.Number);
        code.MaxLength = 8;
        void Submit() => _ = Run(async () =>
        {
            var r = await _sync.ConfirmSignInCode(email, code.Text);
            if (!IsInstanceValid(this)) return;
            if (!r.Ok) { ShowError(r.Error); return; }
            AccountReady?.Invoke();
            NewPasswordScreen();
        });
        code.TextSubmitted += _ => Submit();
        Buttons(("Confirm", Submit));
        Row(("Back", () => ForgotScreen(email)));
        _back = () => ForgotScreen(email);
    }

    private void NewPasswordScreen()
    {
        ClearBody();
        Header("NEW PASSWORD");
        Body("Choose the password you'll sign in with from now on.", TextMuted);
        ErrorLine();

        var pass = Field($"New password ({SupabaseAuth.MinPasswordLength}+ characters)", LineEdit.VirtualKeyboardTypeEnum.Password, secret: true);
        void Submit() => _ = Run(async () =>
        {
            var r = await _sync.SetPassword(pass.Text);
            if (!IsInstanceValid(this)) return;
            if (!r.Ok) { ShowError(r.Error); return; }
            Done();
        });
        pass.TextSubmitted += _ => Submit();
        Buttons(("Save password", Submit));
        Row(("Not now", Done));
        _back = Done;
    }

    private void ConflictScreen(SyncManager.ConflictInfo c)
    {
        Body("This phone and your account both have progress. Pick the one to keep. The other is replaced.", TextMuted);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 20);
        _body.AddChild(row);

        row.AddChild(SaveCard("THIS PHONE", c.LocalSummary, () => _ = Run(async () => { await _sync.ResolveConflict(useCloud: false); if (IsInstanceValid(this)) QueueFree(); })));
        row.AddChild(SaveCard("ACCOUNT", c.CloudSummary, () => _ = Run(async () => { await _sync.ResolveConflict(useCloud: true); if (IsInstanceValid(this)) QueueFree(); })));
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Widgets
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Remove now, free later: a QueueFree'd child still occupies the VBox until end of frame.</summary>
    private void ClearBody()
    {
        _back = null;
        _autoCheck = null;
        foreach (var c in _body.GetChildren()) { _body.RemoveChild(c); c.QueueFree(); }
    }

    private void Header(string text)
    {
        var h = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        ApplyHeaderFont(h, HeadSize);
        h.Modulate = Gold;
        _body.AddChild(h);
        _body.AddChild(new ColorRect { Color = new Color(Gold.R, Gold.G, Gold.B, 0.3f), CustomMinimumSize = new Vector2(0, 1) });
    }

    private Label Body(string text, Color colour)
    {
        var l = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(Inner, 0),
        };
        ApplyBodyFont(l, TextSize);
        l.Modulate = colour;
        _body.AddChild(l);
        return l;
    }

    private void ErrorLine()
    {
        _errorLine = Body("", Ember);
        _errorLine.Visible = false;
    }

    private LineEdit Field(string placeholder, LineEdit.VirtualKeyboardTypeEnum keyboard, bool secret = false)
    {
        bool first = true;
        foreach (var c in _body.GetChildren()) if (c is LineEdit) first = false;
        var f = new LineEdit
        {
            PlaceholderText = placeholder,
            CustomMinimumSize = new Vector2(Inner, FieldHeight),
            Alignment = HorizontalAlignment.Center,
            Secret = secret,
            VirtualKeyboardType = keyboard,
        };
        f.AddThemeFontSizeOverride("font_size", ControlSize);
        _body.AddChild(f);
        if (first) f.CallDeferred(Control.MethodName.GrabFocus);
        return f;
    }

    private Button MakeButton(string label, Action act, float width)
    {
        var b = new Button { Text = label, CustomMinimumSize = new Vector2(width, MinButtonHeight) };
        b.AddThemeFontSizeOverride("font_size", ControlSize);
        b.AddThemeStyleboxOverride("normal", MenuButtons.Normal());
        b.AddThemeStyleboxOverride("hover", MenuButtons.Hover());
        b.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed());
        b.AddThemeColorOverride("font_color", Color.FromHtml("#E8DCC8"));
        var captured = act;
        b.Pressed += () =>
        {
            if (_busy) return;
            GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("click");
            captured();
        };
        return b;
    }

    /// <summary>Full-width buttons, one per line — the main actions.</summary>
    private void Buttons(params (string label, Action act)[] items)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        _body.AddChild(col);
        foreach (var (label, act) in items) col.AddChild(MakeButton(label, act, Inner));
    }

    /// <summary>Side-by-side buttons — the secondary actions (Back, Close, …).</summary>
    private void Row(params (string label, Action act)[] items)
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 10);
        _body.AddChild(row);
        float w = (Inner - 10 * (items.Length - 1)) / items.Length;
        foreach (var (label, act) in items) row.AddChild(MakeButton(label, act, w));
    }

    private Control SaveCard(string title, string summary, Action choose)
    {
        var card = new PanelContainer { CustomMinimumSize = new Vector2((Inner - 20) / 2, 0) };
        card.AddThemeStyleboxOverride("panel", StyleWornBorder(borderColor: BorderSubtle, width: 1, radius: RadiusMedium, bgColor: SurfaceStone));
        var pad = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) pad.AddThemeConstantOverride(side, 16);
        card.AddChild(pad);
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 8); pad.AddChild(v);

        var t = new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center };
        ApplyHeaderFont(t, ControlSize); t.Modulate = Gold; v.AddChild(t);
        var s = new Label { Text = summary, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        ApplyBodyFont(s, TextSize - 2); s.Modulate = TextSecondary; v.AddChild(s);

        v.AddChild(MakeButton("Keep this one", choose, 0));
        return card;
    }

    private void ShowError(string msg, bool good = false)
    {
        if (_errorLine == null || !IsInstanceValid(_errorLine)) return;
        _errorLine.Text = msg;
        _errorLine.Modulate = good ? Moss : Ember;
        _errorLine.Visible = !string.IsNullOrEmpty(msg);
    }

    /// <summary>
    /// Run an async action from a button. Godot installs a
    /// SynchronizationContext on the main thread, so an await HERE (no
    /// ConfigureAwait) resumes on the main thread even though SyncManager's
    /// internals hop to the pool — which is why the lambdas above may touch
    /// nodes after awaiting (after checking the panel still exists: a sign-in
    /// that loads the account's save reloads the title, panel and all).
    /// One request at a time: a second tap while one is in flight is ignored.
    /// </summary>
    private async Task Run(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        ShowError(Working, good: true);
        try
        {
            await action();
            if (IsInstanceValid(this) && _errorLine != null && IsInstanceValid(_errorLine) && _errorLine.Text == Working)
                ShowError("");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AccountPanel] {ex}");
            Callable.From(() => { if (IsInstanceValid(this)) ShowError(ex.GetType().Name + ": " + ex.Message); }).CallDeferred();
        }
        finally { _busy = false; }
    }
}

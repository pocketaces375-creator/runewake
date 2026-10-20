using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// FABLE-059: the Rewards page — everything daily in one place (Trikzos: "everything in one area/page so
/// it's easily accessible … a couple different claim buttons … playing a certain number of matches will
/// give you daily bonuses").
///
///   ┌ Daily Login ─ 7 day tiles ─────────────────────────── [Claim Day N] ┐
///   ├ Today's Matches ─ 4 goals, each its own Claim ─┬ Daily Chest ─ [Open] ┤
///   └ ◀ Back            new rewards in 5h 12m                              ┘
///
/// The rules live in the engine (DailyRewards); this page only shows them and calls Claim*.
/// </summary>
public partial class RewardsScene : Control
{
    public const string ScenePath = "res://scenes/rewards/RewardsScene.tscn";

    private static readonly Color Gold = ThemeTokens.Gold;
    private static readonly Color Parchment = new(0.91f, 0.86f, 0.78f);
    private static readonly Color Muted = new(0.62f, 0.57f, 0.47f);
    private static readonly Color Rule = new(0.79f, 0.66f, 0.30f, 0.22f);
    private static readonly Color ReadyGlow = new(0.95f, 0.80f, 0.40f);

    private Control _content = null!;
    private Label _balances = null!;
    private Label _countdown = null!;
    private Label _status = null!;
    private float _statusFade;
    private DateOnly _shownDay;
    private double _tick;
    private readonly Random _rng = new();

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
    private static ProgressionState Prog => CampaignContext.Progression;

    public override void _Ready() => SceneGuard.Build(this, "RewardsScene", Build, "res://scenes/main/Main.tscn", "Back to title");

    private void Build()
    {
        if (!CampaignContext.SaveManager.IsLoaded) CampaignContext.SaveManager.Initialize();
        var vp = GetViewportRect().Size;

        var bg = new ColorRect { Color = new Color(0.05f, 0.045f, 0.035f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        var glow = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = Grad(new Color(0.95f, 0.78f, 0.40f, 0.22f), new Color(0.95f, 0.78f, 0.40f, 0f)),
                Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.2f), FillTo = new Vector2(0.5f, 1f), Width = 64, Height = 64,
            },
            StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore,
        };
        glow.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(glow);

        var kicker = Text("COME BACK EACH DAY", 0, 22, vp.X, 26, ThemeTokens.GetHeaderFont(26), new Color(0.75f, 0.68f, 0.52f));
        var title = Text("Daily Rewards", 0, 52, vp.X, 80, ThemeTokens.GetCardNameFont(80), Gold);

        _balances = Text("", vp.X - 900, 46, 820, 30, ThemeTokens.GetBodyFont(30), new Color(0.86f, 0.76f, 0.56f));
        _balances.HorizontalAlignment = HorizontalAlignment.Right;

        _content = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _content.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_content);

        var back = Plate("◀  Back", false, 300, 80);
        back.Position = new Vector2(120, vp.Y - 104);
        back.Pressed += GoBack;
        AddChild(back);

        _countdown = Text("", vp.X / 2 - 600, vp.Y - 90, 1200, 30, ThemeTokens.GetBodyFont(30), Muted);
        _status = Text("", vp.X / 2 - 700, vp.Y - 134, 1400, 30, ThemeTokens.GetBodyFont(32), Gold);

        Render();
        MenuButtons.BreatheTitle(title);
        MenuButtons.RevealStagger(new Control[] { kicker, title, _content });
    }

    public override void _Process(double delta)
    {
        if (_statusFade > 0)
        {
            _statusFade -= (float)delta;
            if (_statusFade < 1f && _status != null) _status.Modulate = new Color(1, 1, 1, Math.Max(0, _statusFade));
        }
        _tick += delta;
        if (_tick < 1.0 || _countdown == null) return;
        _tick = 0;
        if (Today != _shownDay) Render();          // midnight passed while the page was open
        else UpdateCountdown();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMGoBackRequest) GoBack();
    }

    private void GoBack()
    {
        Click();
        LoadScreen.Go(GetTree(), "res://scenes/main/Main.tscn");
    }

    // ── the page ─────────────────────────────────────────────────────────

    private void Render()
    {
        var p = Prog;
        _shownDay = DailyRewards.Touch(p, Today);
        foreach (var c in _content.GetChildren()) c.QueueFree();
        var vp = GetViewportRect().Size;
        const float left = 120f, top = 160f, gap = 24f, loginH = 268f, bottomReserve = 150f;
        float width = vp.X - 2 * left;

        BuildLogin(p, new Vector2(left, top), new Vector2(width, loginH));

        float rowTop = top + loginH + gap;
        float h = vp.Y - bottomReserve - rowTop;
        float chestW = 620f;
        BuildMatches(p, new Vector2(left, rowTop), new Vector2(width - chestW - gap, h));
        BuildChest(p, new Vector2(left + width - chestW, rowTop), new Vector2(chestW, h));

        _balances.Text = $"{p.Shards} Shards";   // FABLE-075: one coin
        UpdateCountdown();
    }

    private void BuildLogin(ProgressionState p, Vector2 pos, Vector2 size)
    {
        var panel = Glass(pos, size);
        _content.AddChild(panel);
        var (ticked, ready) = DailyRewards.LoginCalendarView(p, Today);
        bool claimedToday = ready < 0;
        int shownDay = claimedToday ? ticked : ready + 1;

        Text(panel, "DAILY LOGIN", 44, 22, 600, 24, ThemeTokens.GetHeaderFont(24), new Color(0.80f, 0.72f, 0.54f));
        Text(panel, claimedToday ? $"Day {shownDay} of 7 claimed — see you tomorrow" : $"Day {shownDay} of 7 is waiting",
            44, 48, 1100, 38, ThemeTokens.GetCardNameFont(38), claimedToday ? Parchment : Gold);
        var note = Text(panel, "Missed a day? Your week waits — it never resets.", size.X - 744, 22, 700, 24,
            ThemeTokens.GetBodyFont(24), Muted);
        note.HorizontalAlignment = HorizontalAlignment.Right;

        const float tileW = 196f, tileH = 140f, tileGap = 18f;
        float tilesX = 44f, tilesY = 106f;
        for (int i = 0; i < DailyRewards.LoginCalendar.Count; i++)
        {
            var state = i < ticked ? RewardStatus.Claimed : i == ready ? RewardStatus.Ready : RewardStatus.Locked;
            panel.AddChild(DayTile(i, DailyRewards.LoginCalendar[i], state, new Vector2(tilesX + i * (tileW + tileGap), tilesY), new Vector2(tileW, tileH)));
        }

        float bx = tilesX + 7 * (tileW + tileGap) + 24;
        var claim = Plate(claimedToday ? "Claimed" : $"Claim Day {shownDay}", !claimedToday, size.X - bx - 44, 110);
        claim.Position = new Vector2(bx, tilesY + (tileH - 110) / 2);
        claim.Disabled = claimedToday;
        claim.Name = "ClaimLogin";
        claim.Pressed += () => Claim(() => DailyRewards.ClaimLogin(Prog, Today, PickCard), $"Day {shownDay}");
        panel.AddChild(claim);
        if (!claimedToday) Pulse(claim);
        _ = note;
    }

    private Control DayTile(int i, DailyReward reward, RewardStatus state, Vector2 pos, Vector2 size)
    {
        bool lastDay = i == DailyRewards.LoginCalendar.Count - 1;
        var tint = state == RewardStatus.Ready ? ReadyGlow : lastDay ? new Color(0.70f, 0.52f, 0.90f) : Gold;
        var box = new Panel { Position = pos, Size = size, MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = state == RewardStatus.Ready ? new Color(0.35f, 0.27f, 0.10f, 0.92f)
                    : state == RewardStatus.Claimed ? new Color(0.10f, 0.12f, 0.08f, 0.85f) : new Color(1, 1, 1, 0.035f),
            BorderColor = new Color(tint.R, tint.G, tint.B, state == RewardStatus.Locked ? 0.30f : 0.95f),
            BorderWidthLeft = state == RewardStatus.Ready ? 3 : 2, BorderWidthRight = state == RewardStatus.Ready ? 3 : 2,
            BorderWidthTop = state == RewardStatus.Ready ? 3 : 2, BorderWidthBottom = state == RewardStatus.Ready ? 3 : 2,
            CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
            ShadowColor = state == RewardStatus.Ready ? new Color(0.95f, 0.75f, 0.30f, 0.35f) : new Color(0, 0, 0, 0.4f),
            ShadowSize = state == RewardStatus.Ready ? 16 : 6,
        });
        var dim = state == RewardStatus.Locked ? new Color(0.62f, 0.57f, 0.47f) : Parchment;
        Text(box, $"DAY {i + 1}", 0, 12, size.X, 22, ThemeTokens.GetHeaderFont(22), state == RewardStatus.Ready ? ReadyGlow : Muted, center: true);
        var (big, small) = Headline(reward);
        Text(box, big, 0, 34, size.X, 44, ThemeTokens.GetCardNameFont(44), state == RewardStatus.Locked ? Muted : tint, center: true);
        var what = Text(box, small, 8, 84, size.X - 16, 22, ThemeTokens.GetBodyFont(22), dim, center: true);
        what.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        what.Size = new Vector2(size.X - 16, 54);
        if (state == RewardStatus.Claimed)
        {
            var tick = Text(box, "✓", size.X - 44, 6, 36, 30, Inter(30), Moss, center: true);
            box.Modulate = new Color(1, 1, 1, 0.78f);
            _ = tick;
        }
        return box;
    }

    private void BuildMatches(ProgressionState p, Vector2 pos, Vector2 size)
    {
        var panel = Glass(pos, size);
        _content.AddChild(panel);
        Text(panel, "TODAY'S MATCHES", 44, 22, 600, 24, ThemeTokens.GetHeaderFont(24), new Color(0.80f, 0.72f, 0.54f));
        Text(panel, $"{p.Daily.MatchesToday} played  ·  {p.Daily.WinsToday} won today", 44, 48, 700, 34,
            ThemeTokens.GetCardNameFont(34), Parchment);
        var any = Text(panel, "Any duel counts — campaign or online.", size.X - 744, 22, 700, 22, ThemeTokens.GetBodyFont(22), Muted);
        any.HorizontalAlignment = HorizontalAlignment.Right;

        float rowY = 100f, rowH = (size.Y - rowY - 18) / DailyRewards.MatchTiers.Count;
        foreach (var t in DailyRewards.MatchTiers)
        {
            var st = DailyRewards.TierStatus(p, t, Today);
            int prog = DailyRewards.Progress(p, t, Today);
            var row = new Panel { Position = new Vector2(32, rowY), Size = new Vector2(size.X - 64, rowH - 10), MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeStyleboxOverride("panel", RowBox(st == RewardStatus.Ready));
            panel.AddChild(row);
            float rh = rowH - 10;

            Text(row, t.Title, 24, rh / 2 - 34, 420, 30, ThemeTokens.GetCardNameFont(30), st == RewardStatus.Locked ? Parchment : Gold);
            Text(row, t.Reward.Describe(), 24, rh / 2 + 4, 520, 24, ThemeTokens.GetBodyFont(24), Muted);

            // progress bar
            float barX = 560, barW = row.Size.X - barX - 300;
            var barBg = new Panel { Position = new Vector2(barX, rh / 2 - 9), Size = new Vector2(barW, 18), MouseFilter = MouseFilterEnum.Ignore };
            barBg.AddThemeStyleboxOverride("panel", Rounded(new Color(1, 1, 1, 0.07f), 9));
            row.AddChild(barBg);
            float frac = t.Goal == 0 ? 1 : (float)prog / t.Goal;
            if (frac > 0)
            {
                var fill = new Panel { Position = barBg.Position, Size = new Vector2(Math.Max(18, barW * frac), 18), MouseFilter = MouseFilterEnum.Ignore };
                fill.AddThemeStyleboxOverride("panel", Rounded(st == RewardStatus.Claimed ? Moss : Gold, 9));
                row.AddChild(fill);
            }
            Text(row, $"{prog} / {t.Goal}", barX, rh / 2 + 12, barW, 22, ThemeTokens.GetBodyFont(22), Muted, center: true);

            var btn = Plate(st == RewardStatus.Claimed ? "Claimed" : st == RewardStatus.Ready ? "Claim" : $"{t.Goal - prog} to go",
                st == RewardStatus.Ready, 250, Math.Min(78, rh - 18));
            btn.Position = new Vector2(row.Size.X - 270, (rh - btn.Size.Y) / 2);
            btn.Disabled = st != RewardStatus.Ready;
            btn.Name = "Claim_" + t.Id;
            var id = t.Id;
            var title = t.Title;
            btn.Pressed += () => Claim(() => DailyRewards.ClaimTier(Prog, id, Today, PickCard), title);
            row.AddChild(btn);
            if (st == RewardStatus.Ready) Pulse(btn);
            rowY += rowH;
        }
    }

    private void BuildChest(ProgressionState p, Vector2 pos, Vector2 size)
    {
        var panel = Glass(pos, size);
        _content.AddChild(panel);
        var st = DailyRewards.ChestStatus(p, Today);
        int total = 1 + DailyRewards.MatchTiers.Count;
        int done = (DailyRewards.LoginStatus(p, Today) == RewardStatus.Claimed ? 1 : 0)
                   + DailyRewards.MatchTiers.Count(t => DailyRewards.TierStatus(p, t, Today) == RewardStatus.Claimed);

        Text(panel, "DAILY CHEST", 0, 22, size.X, 24, ThemeTokens.GetHeaderFont(24), new Color(0.80f, 0.72f, 0.54f), center: true);
        panel.AddChild(ChestArt(st, new Vector2(size.X / 2, 156)));
        Text(panel, DailyRewards.Chest.Describe(), 0, 236, size.X, 30, ThemeTokens.GetCardNameFont(30), Parchment, center: true);
        var why = Text(panel, st == RewardStatus.Claimed ? "Opened. A new chest arrives tomorrow."
                : st == RewardStatus.Ready ? "Everything claimed — open it!"
                : $"Claim everything else today to open it  ({done} of {total})",
            40, 278, size.X - 80, 24, ThemeTokens.GetBodyFont(24), Muted, center: true);
        why.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        why.Size = new Vector2(size.X - 80, 60);

        var btn = Plate(st == RewardStatus.Claimed ? "Opened" : "Open Chest", st == RewardStatus.Ready, size.X - 120, 84);
        btn.Position = new Vector2(60, size.Y - 84 - 24);
        btn.Disabled = st != RewardStatus.Ready;
        btn.Name = "ClaimChest";
        btn.Pressed += () => Claim(() => DailyRewards.ClaimChest(Prog, Today, PickCard), "Daily Chest");
        panel.AddChild(btn);
    }

    // ── claiming ─────────────────────────────────────────────────────────

    private string? PickCard(Rarity r) => DailyRewards.PickCard(Prog, r, _rng);

    private void Claim(Func<ClaimResult?> claim, string what)
    {
        var result = claim();
        if (result == null) { Render(); return; }
        Prog.MergeRuneDustIntoShards();   // FABLE-075: one coin
        try { CampaignContext.SaveManager.Save(); }
        catch (Exception ex) { GD.PrintErr($"[Rewards] save failed: {ex.Message}"); }
        GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("unlock");
        GD.Print($"[Rewards] claimed {what}: {result.Describe()}");
        Render();
        ShowReward(what, result);
    }

    /// <summary>The moment of getting it: a centred card with what you got; tap anywhere to close.</summary>
    private void ShowReward(string what, ClaimResult result)
    {
        var vp = GetViewportRect().Size;
        var veil = new Control { Name = "RewardPopup", ZIndex = 800, MouseFilter = MouseFilterEnum.Stop };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.62f), MouseFilter = MouseFilterEnum.Ignore };
        shade.SetAnchorsPreset(LayoutPreset.FullRect);
        veil.AddChild(shade);

        bool hasCard = result.CardId != null && ResourceLoader.Exists($"res://content/art/cards_baked/{result.CardId}.webp");
        var size = new Vector2(hasCard ? 1100 : 900, hasCard ? 640 : 420);
        var box = Glass((vp - size) / 2, size);
        veil.AddChild(box);
        Text(box, what.ToUpperInvariant(), 0, 30, size.X, 26, ThemeTokens.GetHeaderFont(26), new Color(0.80f, 0.72f, 0.54f), center: true);
        Text(box, "You got", 0, 70, size.X, 50, ThemeTokens.GetCardNameFont(50), Gold, center: true);
        float textX = 40, textW = size.X - 80, textY = 150;
        if (hasCard)
        {
            var art = new TextureRect
            {
                Texture = GD.Load<Texture2D>($"res://content/art/cards_baked/{result.CardId}.webp"),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Position = new Vector2(60, 140), Size = new Vector2(320, 440), MouseFilter = MouseFilterEnum.Ignore,
            };
            box.AddChild(art);
            textX = 420; textW = size.X - 460; textY = 250;
        }
        var got = Text(box, result.Describe(), textX, textY, textW, 40, ThemeTokens.GetCardNameFont(40), Parchment, center: !hasCard);
        got.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        got.Size = new Vector2(textW, 160);
        Text(box, "tap anywhere to continue", 0, size.Y - 60, size.X, 24, ThemeTokens.GetBodyFont(24), Muted, center: true);

        veil.Modulate = new Color(1, 1, 1, 0);
        veil.CreateTween().TweenProperty(veil, "modulate:a", 1f, 0.25f);
        box.PivotOffset = size / 2;
        box.Scale = new Vector2(0.9f, 0.9f);
        box.CreateTween().SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out).TweenProperty(box, "scale", Vector2.One, 0.3f);
        veil.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true } || e is InputEventScreenTouch { Pressed: true })
                veil.QueueFree();
        };
        AddChild(veil);
        Say($"{what}: {result.Describe()}");
    }

    private void UpdateCountdown()
    {
        var now = DateTime.Now;
        var left = now.Date.AddDays(1) - now;
        int ready = DailyRewards.ReadyCount(Prog, Today);
        string head = ready > 0 ? $"{ready} reward{(ready == 1 ? "" : "s")} ready to claim  ·  " : "";
        _countdown.Text = $"{head}New daily rewards in {(int)left.TotalHours}h {left.Minutes:00}m";
    }

    // ── small helpers (same visual language as the Duel Arena) ───────────

    private static readonly Color Moss = new(0.48f, 0.74f, 0.42f);

    /// <summary>A day tile's two lines: the big word/number and what it is ("50" / "Shards").</summary>
    private static (string big, string small) Headline(DailyReward r)
    {
        // FABLE-075: one coin — Runes are shown as the Shards they become
        if (r.Card is Rarity rr) return ("CARD", r.TotalShards > 0 ? $"{DailyReward.RarityName(rr)} + {r.TotalShards} Shards" : DailyReward.RarityName(rr));
        return (r.TotalShards.ToString(), "Shards");
    }

    private static Font Inter(int size)
    {
        var f = ResourceLoader.Load<FontFile>(ThemeTokens.FontInter);
        return f ?? ThemeTokens.GetBodyFont(size);
    }

    /// <summary>A small chest drawn from panels: body, lid, clasp. Lit when it can be opened.</summary>
    private static Control ChestArt(RewardStatus st, Vector2 center)
    {
        var root = new Control { Position = center - new Vector2(110, 70), Size = new Vector2(220, 140), MouseFilter = MouseFilterEnum.Ignore };
        Color wood = st == RewardStatus.Locked ? new Color(0.30f, 0.24f, 0.18f) : new Color(0.52f, 0.34f, 0.18f);
        Color band = st == RewardStatus.Locked ? new Color(0.42f, 0.38f, 0.30f) : ThemeTokens.Gold;
        Panel Box(Vector2 pos, Vector2 size, Color c, int r, Color? border = null)
        {
            var b = new Panel { Position = pos, Size = size, MouseFilter = MouseFilterEnum.Ignore };
            var sb = Rounded(c, r);
            if (border is Color bc) { sb.BorderColor = bc; sb.BorderWidthLeft = sb.BorderWidthRight = sb.BorderWidthTop = sb.BorderWidthBottom = 4; }
            b.AddThemeStyleboxOverride("panel", sb);
            root.AddChild(b);
            return b;
        }
        if (st == RewardStatus.Ready)
        {
            var halo = Box(new Vector2(-30, -26), new Vector2(280, 190), new Color(0.95f, 0.78f, 0.40f, 0.18f), 90);
            Pulse(halo);
        }
        bool open = st == RewardStatus.Claimed;                         // an opened chest: lid lifted, gold showing
        if (open) Box(new Vector2(12, 30), new Vector2(196, 34), new Color(1f, 0.86f, 0.45f, 0.9f), 8);
        Box(new Vector2(0, 56), new Vector2(220, 84), wood, 10, band);
        Box(new Vector2(0, open ? -24 : 0), new Vector2(220, 62), wood.Lightened(0.08f), 26, band);
        Box(new Vector2(96, 44), new Vector2(28, 36), band, 6);
        Box(new Vector2(28, 56), new Vector2(14, 84), band.Darkened(0.15f), 2);
        Box(new Vector2(178, 56), new Vector2(14, 84), band.Darkened(0.15f), 2);
        return root;
    }

    private static void Pulse(Control c)
    {
        var t = c.CreateTween().SetLoops();
        t.TweenProperty(c, "modulate", new Color(1.18f, 1.10f, 0.92f), 0.8f).SetTrans(Tween.TransitionType.Sine);
        t.TweenProperty(c, "modulate", Colors.White, 0.8f).SetTrans(Tween.TransitionType.Sine);
    }

    private static Panel Glass(Vector2 pos, Vector2 size)
    {
        var p = new Panel { Position = pos, Size = size, MouseFilter = MouseFilterEnum.Pass };
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.082f, 0.072f, 0.061f, 0.86f),
            BorderColor = new Color(0.79f, 0.66f, 0.30f, 0.38f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 18, CornerRadiusTopRight = 18, CornerRadiusBottomLeft = 18, CornerRadiusBottomRight = 18,
            ShadowColor = new Color(0, 0, 0, 0.55f), ShadowSize = 28,
        });
        return p;
    }

    private static StyleBoxFlat RowBox(bool ready) => new()
    {
        BgColor = ready ? new Color(0.79f, 0.66f, 0.30f, 0.12f) : new Color(1, 1, 1, 0.03f),
        BorderColor = new Color(0.79f, 0.66f, 0.30f, ready ? 0.65f : 0.20f),
        BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12, CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
    };

    private static StyleBoxFlat Rounded(Color c, int r) => new()
    {
        BgColor = c, CornerRadiusTopLeft = r, CornerRadiusTopRight = r, CornerRadiusBottomLeft = r, CornerRadiusBottomRight = r,
    };

    private Label Text(string text, float x, float y, float w, int size, Font font, Color colour) =>
        Text(this, text, x, y, w, size, font, colour, center: true);

    private static Label Text(Control parent, string text, float x, float y, float w, int size, Font font, Color colour, bool center = false)
    {
        var l = new Label
        {
            Text = text, HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, Position = new Vector2(x, y), Size = new Vector2(w, size + 16),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if (font != null) l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", colour);
        parent.AddChild(l);
        return l;
    }

    private static Button Plate(string text, bool primary, float w, float h)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(w, h), Size = new Vector2(w, h), FocusMode = FocusModeEnum.None };
        b.AddThemeFontOverride("font", ThemeTokens.GetButtonFont(32)); b.AddThemeFontSizeOverride("font_size", 32);
        b.AddThemeColorOverride("font_color", primary ? Color.FromHtml("#F2DFA6") : Color.FromHtml("#D8CBB0"));
        b.AddThemeColorOverride("font_disabled_color", new Color(0.58f, 0.54f, 0.46f));
        b.AddThemeStyleboxOverride("normal", primary ? MenuButtons.PrimaryNormal() : MenuButtons.QuietNormal());
        b.AddThemeStyleboxOverride("hover", primary ? MenuButtons.PrimaryHover() : MenuButtons.Hover());
        b.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed());
        b.AddThemeStyleboxOverride("disabled", MenuButtons.QuietNormal());
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        MenuButtons.Animate(b);
        return b;
    }

    private static Gradient Grad(Color a, Color b) { var g = new Gradient(); g.SetColor(0, a); g.SetColor(1, b); return g; }
    private void Click() => GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("click");
    private void Say(string t) { _status.Text = t; _status.Modulate = Colors.White; _statusFade = 3.5f; }
}

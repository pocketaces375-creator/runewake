using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// FABLE-061: the Card Packs page. Buy (Google Play), see exactly what a pack holds (Google Play requires
/// the odds of random items to be shown before purchase), and open the packs you own.
///
///   ┌ 1 Pack  $0.25 ┬ 5 Packs  $0.99 ┬ Your packs: N  [Open a Pack]       ┐
///   │  [Buy]        │  [Buy]         │ WHAT'S INSIDE · WHICH CARDS         │
///   └ ◀ Back                    status line                               ┘
/// </summary>
public partial class PackShopScene : Control
{
    public const string ScenePath = "res://scenes/shop/PackShopScene.tscn";

    private static readonly Color Gold = ThemeTokens.Gold;
    private static readonly Color Parchment = new(0.91f, 0.86f, 0.78f);
    private static readonly Color Muted = new(0.62f, 0.57f, 0.47f);
    private static readonly Color Kicker = new(0.80f, 0.72f, 0.54f);

    private Control _content = null!;
    private Label _status = null!;
    private float _statusFade;
    private Control? _opening;

    public override void _Ready() => SceneGuard.Build(this, "PackShopScene", Build, "res://scenes/main/Main.tscn", "Back to title");

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
                Gradient = Grad(new Color(0.62f, 0.45f, 0.95f, 0.20f), new Color(0.62f, 0.45f, 0.95f, 0f)),
                Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.3f, 0.35f), FillTo = new Vector2(0.3f, 1f), Width = 64, Height = 64,
            },
            StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore,
        };
        glow.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(glow);

        var kicker = Lbl(this, "SEALED IN THE OLD AGE", 0, 22, vp.X, 26, ThemeTokens.GetHeaderFont(26), new Color(0.75f, 0.68f, 0.52f), true);
        var title = Lbl(this, "Card Packs", 0, 52, vp.X, 80, ThemeTokens.GetCardNameFont(80), Gold, true);

        _content = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _content.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_content);

        var back = Plate("◀  Back", false, 300, 80);
        back.Position = new Vector2(120, vp.Y - 104);
        back.Pressed += GoBack;
        AddChild(back);
        _status = Lbl(this, "", vp.X / 2 - 700, vp.Y - 96, 1400, 30, ThemeTokens.GetBodyFont(30), Gold, true);

        PackStore.Init();
        PackStore.Changed += OnChanged;
        PackStore.Notice += Say;
        PackStore.PacksArrived += OnArrived;
        Render();
        _ = PackStore.Refresh();
        MenuButtons.BreatheTitle(title);
        // FABLE-067: --packrite = open a free preview pack straight away (debug builds only; for tuning the rite)
        if (System.Array.IndexOf(OS.GetCmdlineArgs(), "--packrite") >= 0 && PackStore.PreviewMode)
        {
            if (CardRegistry.GetAll().Count == 0)
                foreach (var set in new[] { "verdant", "ember", "tide", "hollow", "dawn" })
                    CardRegistry.RegisterRange(CardLoader.LoadPackFromString(Godot.FileAccess.GetFileAsString($"res://content/cards/{set}.json")));
            Callable.From(() => { PackStore.Buy("rw_pack_1"); OpenNext(); }).CallDeferred();
        }
        MenuButtons.RevealStagger(new Control[] { kicker, title, _content });
    }

    public override void _ExitTree()
    {
        PackStore.Changed -= OnChanged;
        PackStore.Notice -= Say;
        PackStore.PacksArrived -= OnArrived;
    }

    public override void _Process(double delta)
    {
        if (_statusFade <= 0 || _status == null) return;
        _statusFade -= (float)delta;
        if (_statusFade < 1f) _status.Modulate = new Color(1, 1, 1, Math.Max(0, _statusFade));
    }

    public override void _Notification(int what)
    {
        if (what != NotificationWMGoBackRequest) return;
        if (_opening != null) CloseOpening(); else GoBack();
    }

    private void GoBack()
    {
        Click();
        LoadScreen.Go(GetTree(), "res://scenes/main/Main.tscn");
    }

    private void OnChanged() { if (IsInsideTree() && _opening == null) Render(); }

    private void OnArrived(List<PendingPack> packs)
    {
        if (!IsInsideTree()) return;
        GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("unlock");
        Say($"{packs.Count} pack{(packs.Count == 1 ? "" : "s")} added — tap Open a Pack!");
        Render();
    }

    // ── the page ─────────────────────────────────────────────────────────

    private void Render()
    {
        foreach (var c in _content.GetChildren()) c.QueueFree();
        var vp = GetViewportRect().Size;
        const float top = 168f, left = 120f, gap = 28f;
        float h = vp.Y - 150 - top;
        float offerW = 520f;
        var why = PackStore.CannotBuyReason();
        for (int i = 0; i < Packs.Products.Count; i++)
            BuildOffer(Packs.Products[i], new Vector2(left + i * (offerW + gap), top), new Vector2(offerW, h), why, i == 1);
        float rx = left + 2 * (offerW + gap);
        BuildYours(new Vector2(rx, top), new Vector2(vp.X - left - rx, h), why);
    }

    private void BuildOffer(PackProduct p, Vector2 pos, Vector2 size, string? why, bool bestValue)
    {
        var panel = Glass(pos, size);
        _content.AddChild(panel);
        if (bestValue)
        {
            var tag = new Panel { Position = new Vector2(size.X - 190, -18), Size = new Vector2(170, 40), MouseFilter = MouseFilterEnum.Ignore };
            tag.AddThemeStyleboxOverride("panel", Rounded(new Color(0.62f, 0.45f, 0.95f), 20));
            panel.AddChild(tag);
            Lbl(tag, "BEST VALUE", 0, 2, 170, 22, ThemeTokens.GetHeaderFont(22), new Color(0.12f, 0.08f, 0.2f), true);
        }
        panel.AddChild(PackArt(new Vector2(size.X / 2, 210), p.Packs));
        Lbl(panel, p.Packs == 1 ? "1 Pack" : $"{p.Packs} Packs", 0, 380, size.X, 46, ThemeTokens.GetCardNameFont(46), Gold, true);
        Lbl(panel, $"{p.Packs * Packs.CardsPerPack} cards  ·  {p.Packs} guaranteed Rare{(p.Packs == 1 ? "" : "s")}", 0, 440, size.X, 26,
            ThemeTokens.GetBodyFont(26), Parchment, true);
        string label = PackStore.PreviewMode ? $"Get (preview)" : $"Buy  {PackStore.PriceOf(p)}";
        var buy = Plate(label, why == null, size.X - 100, 92);
        buy.Position = new Vector2(50, size.Y - 92 - 34);
        buy.Disabled = why != null;
        buy.Name = "Buy_" + p.Id;
        var id = p.Id;
        buy.Pressed += () => { Click(); PackStore.Buy(id); };
        panel.AddChild(buy);
        if (!PackStore.PreviewMode)
            Lbl(panel, PackStore.PriceOf(p), 0, 488, size.X, 34, ThemeTokens.GetCardNameFont(34), new Color(0.80f, 0.70f, 1f), true);
    }

    private void BuildYours(Vector2 pos, Vector2 size, string? why)
    {
        var panel = Glass(pos, size);
        _content.AddChild(panel);
        var prog = CampaignContext.Progression;
        int n = prog.PendingPacks.Count;
        Lbl(panel, "YOUR PACKS", 44, 24, 500, 24, ThemeTokens.GetHeaderFont(24), Kicker);
        Lbl(panel, n == 0 ? "No unopened packs" : $"{n} unopened pack{(n == 1 ? "" : "s")}", 44, 54, size.X - 88 - 360, 42,
            ThemeTokens.GetCardNameFont(42), n > 0 ? Gold : Parchment);
        var open = Plate(n > 0 ? "Open a Pack" : "Open a Pack", n > 0, 320, 84);
        open.Position = new Vector2(size.X - 364, 34);
        open.Disabled = n == 0;
        open.Name = "OpenPack";
        open.Pressed += () => { Click(); OpenNext(); };
        panel.AddChild(open);
        if (n > 0) Pulse(open);

        var rule = new Panel { Position = new Vector2(44, 146), Size = new Vector2(size.X - 88, 2), MouseFilter = MouseFilterEnum.Ignore };
        rule.AddThemeStyleboxOverride("panel", Rounded(new Color(0.79f, 0.66f, 0.30f, 0.25f), 1));
        panel.AddChild(rule);

        var st = PackStore.Status;
        double relic = st?.RelicChance ?? Packs.RelicChance;
        float y = 172;
        Lbl(panel, "WHAT'S INSIDE EVERY PACK", 44, y, 700, 24, ThemeTokens.GetHeaderFont(24), Kicker); y += 40;
        foreach (var (name, odds, col) in new[]
        {
            ("1 Common", "100%", new Color(0.82f, 0.80f, 0.74f)),
            ("1 Uncommon", "100%", new Color(0.45f, 0.75f, 0.95f)),
            ("1 Rare", $"{Math.Round((1 - relic) * 100)}%", Gold),
            ("   … or a Relic in its place", $"{Math.Round(relic * 100)}%", new Color(0.80f, 0.62f, 1f)),
        })
        {
            Lbl(panel, name, 60, y, 600, 30, ThemeTokens.GetCardNameFont(30), col);
            var o = Lbl(panel, odds, size.X - 244, y, 200, 30, ThemeTokens.GetCardNameFont(30), col);
            o.HorizontalAlignment = HorizontalAlignment.Right;
            y += 44;
        }
        Lbl(panel, "No card twice in one pack.", 60, y, size.X - 120, 24, ThemeTokens.GetBodyFont(24), Muted); y += 50;

        Lbl(panel, "WHICH CARDS", 44, y, 700, 24, ThemeTokens.GetHeaderFont(24), Kicker); y += 38;
        Para(panel, "Packs only hold older cards: a card joins packs 90 days after it is first discovered on the map. " +
                    "Cards from the Tower never come from packs, and neither do Lost Relics.",
            60, y, size.X - 120, 90, 24, Parchment);
        y += 100;
        string pool = st == null
            ? (PackStore.PreviewMode ? "Preview build: packs are rolled from every card, free, for testing." : "Connecting to the shop…")
            : $"In packs now: {st.Total} cards ({st.Commons} Common · {st.Uncommons} Uncommon · {st.Rares} Rare · {st.Relics} Relic)" +
              (st.Waiting > 0 && st.NextCardAt is DateTimeOffset at ? $"\n{st.Waiting} more on their way — the next joins on {at.ToLocalTime():MMMM d}." : "");
        Para(panel, pool, 60, y, size.X - 120, 80, 24, Muted);

        if (why != null)
        {
            Para(panel, why, 44, size.Y - 80, size.X - 88, 70, 26, new Color(0.92f, 0.70f, 0.45f), true);
        }
    }

    /// <summary>A sealed pack drawn from panels: foil body, wax seal, the number of packs.</summary>
    private static Control PackArt(Vector2 center, int count)
    {
        var root = new Control { Position = center - new Vector2(150, 150), Size = new Vector2(300, 300), MouseFilter = MouseFilterEnum.Ignore };
        int shown = Math.Min(count, 3);
        for (int i = shown - 1; i >= 0; i--)
        {
            var pack = new Panel { Position = new Vector2(70 + i * 26 - (shown - 1) * 13, 10 + i * 8), Size = new Vector2(160, 240), MouseFilter = MouseFilterEnum.Ignore };
            pack.RotationDegrees = (i - (shown - 1) / 2f) * 6;
            pack.PivotOffset = new Vector2(80, 240);
            var sb = new StyleBoxFlat
            {
                BgColor = new Color(0.22f, 0.15f, 0.36f), BorderColor = ThemeTokens.Gold,
                BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4,
                CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12, CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
                ShadowColor = new Color(0.55f, 0.35f, 0.95f, 0.35f), ShadowSize = 18,
            };
            pack.AddThemeStyleboxOverride("panel", sb);
            var band = new Panel { Position = new Vector2(0, 92), Size = new Vector2(160, 56), MouseFilter = MouseFilterEnum.Ignore };
            band.AddThemeStyleboxOverride("panel", Rounded(new Color(0.79f, 0.66f, 0.30f, 0.85f), 0));
            pack.AddChild(band);
            var seal = new Panel { Position = new Vector2(52, 90), Size = new Vector2(56, 60), MouseFilter = MouseFilterEnum.Ignore };
            seal.AddThemeStyleboxOverride("panel", Rounded(new Color(0.58f, 0.13f, 0.12f), 28));
            pack.AddChild(seal);
            Lbl(pack, "R", 52, 96, 56, 34, ThemeTokens.GetCardNameFont(34), new Color(0.95f, 0.82f, 0.6f), true);
            root.AddChild(pack);
        }
        if (count > 3) Lbl(root, $"×{count}", 250, 250, 90, 40, ThemeTokens.GetCardNameFont(40), ThemeTokens.Gold, true);
        return root;
    }

    // ── opening ──────────────────────────────────────────────────────────

    private void OpenNext()
    {
        var prog = CampaignContext.Progression;
        var next = prog.PendingPacks.FirstOrDefault();
        if (next == null) { CloseOpening(); return; }
        var before = next.Cards.ToDictionary(c => c, c => prog.Collection.TryGetValue(c, out var k) ? k : 0);
        var pack = PackStore.Open(next.Id);
        if (pack == null) return;
        ShowOpening(pack, before);
    }

    /// <summary>FABLE-067: the Drachma Rite — flick a coin into the deep, the runes rise with your cards.</summary>
    private void ShowOpening(PendingPack pack, Dictionary<string, int> ownedBefore)
    {
        _opening?.QueueFree();
        int left = CampaignContext.Progression.PendingPacks.Count;
        _opening = DrachmaRite.Play(this, pack, ownedBefore, left, onDone: CloseOpening, onNext: left > 0 ? OpenNext : null);
    }

    private static void AddGlow(Control slot, Color col, float w, float h)
    {
        var g = new Panel { Position = new Vector2(-10, -10), Size = new Vector2(w + 20, h + 20), MouseFilter = MouseFilterEnum.Ignore, ShowBehindParent = true };
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0), BorderColor = col,
            BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4,
            CornerRadiusTopLeft = 18, CornerRadiusTopRight = 18, CornerRadiusBottomLeft = 18, CornerRadiusBottomRight = 18,
            ShadowColor = new Color(col.R, col.G, col.B, 0.6f), ShadowSize = 30,
        };
        g.AddThemeStyleboxOverride("panel", sb);
        slot.AddChild(g);
        Pulse(g);
    }

    private void CloseOpening()
    {
        _opening?.QueueFree();
        _opening = null;
        Render();
    }

    // ── helpers (the Rewards / Duel Arena visual language) ───────────────

    private static Color RarityColor(string r) => r switch
    {
        "UNCOMMON" => new Color(0.45f, 0.75f, 0.95f),
        "RARE" => ThemeTokens.Gold,
        "RELIC" => new Color(0.80f, 0.62f, 1f),
        _ => new Color(0.82f, 0.80f, 0.74f),
    };

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
            BgColor = new Color(0.082f, 0.072f, 0.061f, 0.86f), BorderColor = new Color(0.79f, 0.66f, 0.30f, 0.38f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 18, CornerRadiusTopRight = 18, CornerRadiusBottomLeft = 18, CornerRadiusBottomRight = 18,
            ShadowColor = new Color(0, 0, 0, 0.55f), ShadowSize = 28,
        });
        return p;
    }

    private static StyleBoxFlat Rounded(Color c, int r) => new()
    {
        BgColor = c, CornerRadiusTopLeft = r, CornerRadiusTopRight = r, CornerRadiusBottomLeft = r, CornerRadiusBottomRight = r,
    };

    private static Label Para(Control parent, string text, float x, float y, float w, float h, int size, Color colour, bool center = false)
    {
        var l = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(w, 0),
            HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Position = new Vector2(x, y), Size = new Vector2(w, h), MouseFilter = MouseFilterEnum.Ignore,
        };
        l.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(size));
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", colour);
        l.Text = text;
        parent.AddChild(l);
        return l;
    }

    private static Label Lbl(Control parent, string text, float x, float y, float w, int size, Font font, Color colour, bool center = false)
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
    private void Say(string t) { if (_status == null || !IsInsideTree()) return; _status.Text = t; _status.Modulate = Colors.White; _statusFade = 4f; }
}

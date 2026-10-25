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
/// FABLE-069 (Trikzos: "reskin this to have the drachma flipped over a cliff … this is the screen to do
/// that … thematic and epic, Greek/Nordic"): the page IS the cliff. A pack is a drachma. Your drachmae rest
/// on the edge beside a broken portico and a rune stone; swipe the coin (or tap it) and it flies out over
/// the deep — the rite picks up mid-flight and the runes rise with your cards. The offers stand over the
/// chasm as carved stelae, and a third stele tells what the deep returns (the odds).
/// </summary>
public partial class PackShopScene : Control
{
    public const string ScenePath = "res://scenes/shop/PackShopScene.tscn";

    private static readonly Color Gold = ThemeTokens.Gold;
    private static readonly Color Parchment = new(0.91f, 0.86f, 0.78f);
    private static readonly Color Muted = new(0.70f, 0.66f, 0.58f);
    private static readonly Color Kicker = new(0.80f, 0.72f, 0.54f);
    private static readonly Color Silver = new(0.80f, 0.86f, 0.96f);

    private Control _content = null!;
    private Label _status = null!;
    private float _statusFade;
    private Control? _opening;
    private float _s = 1f;
    private Vector2 _vp;
    private DrachmaRite.Drachma? _coin;
    private Tween? _coinBob;
    private Vector2 _pressAt;
    private bool _pressing;

    public override void _Ready() => SceneGuard.Build(this, "PackShopScene", Build, "res://scenes/main/Main.tscn", "Back to title");

    private void Build()
    {
        if (!CampaignContext.SaveManager.IsLoaded) CampaignContext.SaveManager.Initialize();
        _vp = GetViewportRect().Size;
        _s = _vp.Y / 1080f;
        MouseFilter = MouseFilterEnum.Stop;

        AddChild(new DrachmaRite.Backdrop { Size = _vp, MouseFilter = MouseFilterEnum.Ignore, MoonAt = new Vector2(0.06f, 0.115f) });
        if (StyxArt.Backdrop != null) AddChild(new CosmosFx { Size = _vp, MouseFilter = MouseFilterEnum.Ignore });   // FABLE-088: a living sky over the cliffs
        if (StyxArt.Backdrop == null)   // FABLE-084: the painted cliffs carry their own frame
        {
            AddChild(new Meander { Position = Vector2.Zero, Size = new Vector2(_vp.X, 30 * _s), MouseFilter = MouseFilterEnum.Ignore });
            AddChild(new Meander { Position = new Vector2(0, _vp.Y - 30 * _s), Size = new Vector2(_vp.X, 30 * _s), MouseFilter = MouseFilterEnum.Ignore });
        }

        int kick = (int)((StyxArt.Backdrop != null ? 30 : 24) * _s);   // FABLE-105: larger over the starlight, and shadowed
        var kicker = Lbl(this, "CARD PACKS  ·  A DRACHMA FOR THE GODS OF OLD", 0, (StyxArt.Backdrop != null ? 36 : 42) * _s, _vp.X, kick, ThemeTokens.GetHeaderFont(kick), new Color(0.92f, 0.86f, 0.70f), true);
        var title = Lbl(this, "Offering to the Ancients", 0, 70 * _s, _vp.X, (int)(80 * _s), ThemeTokens.GetCardNameFont((int)(80 * _s)), Gold, true);
        Outline(kicker); Outline(title);
        if (StyxArt.Backdrop != null) Halo(Outline2(kicker));   // FABLE-105b: the kicker floats on the stars with a soft halo

        _content = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _content.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_content);

        var back = Plate("◀  Back", false, 260 * _s, 76 * _s);
        back.Position = new Vector2(36 * _s, _vp.Y - 120 * _s);
        back.Pressed += GoBack;
        AddChild(back);
        _status = Lbl(this, "", _vp.X * 0.36f, _vp.Y - 112 * _s, _vp.X * 0.62f, (int)(28 * _s), ThemeTokens.GetBodyFont((int)(28 * _s)), Gold, true);
        Outline(_status);
        if (StyxArt.Backdrop != null) { _status.Position = new Vector2(_vp.X * 0.19f, _vp.Y * 0.655f); _status.Size = new Vector2(_vp.X * 0.62f, 34 * _s); }   // FABLE-105: notices land under the scroll, over the chasm

        if (CardRegistry.GetAll().Count == 0)
            foreach (var set in new[] { "verdant", "ember", "tide", "hollow", "dawn" })
                CardRegistry.RegisterRange(CardLoader.LoadPackFromString(Godot.FileAccess.GetFileAsString($"res://content/cards/{set}.json")));
        PackStore.Init();
        PackStore.Changed += OnChanged;
        PackStore.Notice += Say;
        PackStore.PacksArrived += OnArrived;
        Render();
        _ = PackStore.Refresh();
        MenuButtons.BreatheTitle(title);
        if (StyxArt.Backdrop != null)   // FABLE-088: the title burns with a slow golden glow
        {
            title.AddThemeColorOverride("font_shadow_color", new Color(1f, 0.78f, 0.35f, 0.55f));
            title.AddThemeConstantOverride("shadow_outline_size", (int)(18 * _s));
            title.AddThemeConstantOverride("shadow_offset_x", 0); title.AddThemeConstantOverride("shadow_offset_y", 0);
            if (!CampaignContext.Settings.ReduceMotion)
            {
                var g = title.CreateTween().SetLoops();
                g.TweenMethod(Callable.From<float>(a => title.AddThemeColorOverride("font_shadow_color", new Color(1f, 0.78f, 0.35f, a))), 0.25f, 0.65f, 2.2f).SetTrans(Tween.TransitionType.Sine);
                g.TweenMethod(Callable.From<float>(a => title.AddThemeColorOverride("font_shadow_color", new Color(1f, 0.78f, 0.35f, a))), 0.65f, 0.25f, 2.2f).SetTrans(Tween.TransitionType.Sine);
            }
        }
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
        if (packs.Count == 0) { Render(); return; }
        GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("unlock");
        Say(packs.Count == 1 ? "A drachma is yours — make your offering!" : $"{packs.Count} drachmae are yours — make your offering!");
        Render();
    }

    // ── the flick, on the page itself ────────────────────────────────────

    public override void _GuiInput(InputEvent e)
    {
        if (_opening != null || _coin == null) return;
        Vector2? pos = e switch
        {
            InputEventScreenTouch t => t.Position,
            InputEventMouseButton m when m.ButtonIndex == MouseButton.Left => m.Position,
            _ => null,
        };
        if (pos is not Vector2 p) return;
        bool down = e is InputEventScreenTouch td ? td.Pressed : e is InputEventMouseButton md && md.Pressed;
        bool nearCoin = (p - _coin.Position).Length() < 170f * _s;
        if (down) { _pressing = nearCoin; _pressAt = p; return; }
        if (!_pressing) return;
        _pressing = false;
        var swipe = p - _pressAt;
        if (CampaignContext.Progression.PendingPacks.Count == 0)
        {
            Say("You hold no drachmae yet — take one from a stele to make an offering.");
            return;
        }
        Click();
        OpenNext(swipe.Length() > 60f * _s ? swipe.Normalized() : new Vector2(0.6f, -0.8f), Mathf.Clamp(swipe.Length() / (500f * _s), 0.6f, 1.4f));
        AcceptEvent();
    }

    // ── the page ─────────────────────────────────────────────────────────

    private void Render()
    {
        foreach (var c in _content.GetChildren()) c.QueueFree();
        _coinBob?.Kill();
        var why = PackStore.CannotBuyReason();
        int n = CampaignContext.Progression.PendingPacks.Count;
        if (StyxArt.Backdrop != null) { RenderCliffs(why, n); return; }

        // your drachmae, on the cliff
        float px = _vp.X * 0.015f, pw = _vp.X * 0.31f;
        Outline(Lbl(_content, "YOUR DRACHMAE", px, _vp.Y * 0.205f, pw, (int)(24 * _s), ThemeTokens.GetHeaderFont((int)(24 * _s)), Kicker, true));
        Outline(Lbl(_content, n == 0 ? "None yet" : $"× {n}", px, _vp.Y * 0.245f, pw, (int)(64 * _s), ThemeTokens.GetCardNameFont((int)(64 * _s)), n > 0 ? Gold : Parchment, true));
        _coin = new DrachmaRite.Drachma { Radius = 58f * _s, Position = DrachmaRite.CoinSpot(_vp), MouseFilter = MouseFilterEnum.Ignore };
        _coin.Modulate = n > 0 ? Colors.White : new Color(1, 1, 1, 0.28f);
        _content.AddChild(_coin);
        if (n > 1)   // the rest of the purse, stacked behind
            for (int i = 1; i < Math.Min(n, 4); i++)
            {
                var spare = new DrachmaRite.Drachma { Radius = 40f * _s, Position = _coin.Position + new Vector2(-70f * _s - i * 18f * _s, 30f * _s - i * 6f * _s), Modulate = new Color(0.85f, 0.85f, 0.85f), MouseFilter = MouseFilterEnum.Ignore };
                spare.Spin = 0.9f + i * 0.4f;
                _content.AddChild(spare);
                _content.MoveChild(spare, _coin.GetIndex());
            }
        _coinBob = _coin.CreateTween().SetLoops();
        _coinBob.TweenProperty(_coin, "position:y", _coin.Position.Y - 6f * _s, 0.9f).SetTrans(Tween.TransitionType.Sine);
        _coinBob.TweenProperty(_coin, "position:y", _coin.Position.Y, 0.9f).SetTrans(Tween.TransitionType.Sine);
        var hint = Lbl(_content, n > 0 ? "Pay the ferryman — flick a drachma" : "Take a drachma from a stele", _vp.X * 0.0f, _vp.Y * 0.775f, _vp.X * 0.34f,
            (int)(32 * _s), ThemeTokens.GetHeaderFont((int)(32 * _s)), Gold, true);
        Outline(hint);
        Outline(Lbl(_content, n > 0 ? "swipe the coin into the Styx — or tap it" : "then cast it into the Styx", 0, _vp.Y * 0.775f + 44 * _s, _vp.X * 0.34f,
            (int)(24 * _s), ThemeTokens.GetBodyFont((int)(24 * _s)), Kicker, true));
        if (n > 0)
        {
            var pulse = hint.CreateTween().SetLoops();
            pulse.TweenProperty(hint, "modulate:a", 0.45f, 0.8f).SetTrans(Tween.TransitionType.Sine);
            pulse.TweenProperty(hint, "modulate:a", 1f, 0.8f).SetTrans(Tween.TransitionType.Sine);
        }

        // the offers and the odds, standing over the chasm
        float top = _vp.Y * 0.205f, h = _vp.Y * 0.56f;
        float x = _vp.X * 0.375f, w = _vp.X * 0.17f, gap = _vp.X * 0.02f;
        for (int i = 0; i < Packs.Products.Count && i < 2; i++)
            BuildOffer(Packs.Products[i], new Vector2(x + i * (w + gap), top), new Vector2(w, h), why, i == 1);
        float ox = x + 2 * (w + gap);
        BuildOdds(new Vector2(ox, top), new Vector2(_vp.X - ox - _vp.X * 0.015f, h));
        if (why != null)
        {
            var l = Para(_content, why, x, top + h + 16 * _s, _vp.X - x - _vp.X * 0.015f, 64 * _s, (int)(24 * _s), new Color(0.95f, 0.78f, 0.52f), true);
            Outline(l);
        }
    }

    /// <summary>FABLE-084: Trikzos's cliff design — the offers carved on two altars at the cliff edges, the purse and
    /// the flick in the middle over the chasm, the odds under the title.</summary>
    // FABLE-105 (Trikzos): the words on the cliffs could not be read - gold on starlight, parchment on mist. The painting
    // stays exactly as it is; the words now sit where words belong on this screen: each offer on a bronze-framed wood
    // panel standing on its cliff top, and everything the player needs to know (what a drachma returns, how to offer
    // it, where packs are sold) on one scroll in the mist between them. The Buy plaques are always bright and always
    // answer a tap - when the store cannot sell yet, the tap says why instead of showing a dead grey button.
    private void RenderCliffs(string? why, int n)
    {
        var st = PackStore.Status;
        double relic = st?.RelicChance ?? Packs.RelicChance;
        float cx = _vp.X * 0.30f, cw = _vp.X * 0.40f;
        Outline(Lbl(_content, "YOUR DRACHMAE", cx, _vp.Y * 0.835f, cw, (int)(26 * _s), ThemeTokens.GetHeaderFont((int)(26 * _s)), Kicker, true));
        Outline(Lbl(_content, n == 0 ? "None yet" : $"× {n}", cx, _vp.Y * 0.87f, cw, (int)(56 * _s), ThemeTokens.GetCardNameFont((int)(56 * _s)), n > 0 ? Gold : Parchment, true));

        // FABLE-105b (Trikzos): no banner in the middle - the words float on the painting again, as they did before,
        // made readable with a soft dark halo (a wide blurred shadow) instead of a box. The odds sit under the title.
        Halo(Outline2(Lbl(_content, $"Each drachma returns 1 Common · 1 Uncommon · 1 Rare ({Math.Round((1 - relic) * 100)}%) or a Relic ({Math.Round(relic * 100)}%)",
            0, _vp.Y * 0.165f, _vp.X, (int)(28 * _s), ThemeTokens.GetBodyFont((int)(28 * _s)), new Color(0.97f, 0.93f, 0.84f), true)));
        var hint = Lbl(_content, n > 0 ? "Make your offering — flick a drachma into the mist" : "Take a drachma, then offer it to the Ancients", cx - _vp.X * 0.04f, _vp.Y * 0.46f, cw + _vp.X * 0.08f,
            (int)(34 * _s), ThemeTokens.GetHeaderFont((int)(34 * _s)), Gold, true);
        Halo(Outline2(hint));
        if (why != null) Halo(Outline2(Para(_content, why, cx, _vp.Y * 0.535f, cw, 70 * _s, (int)(25 * _s), new Color(0.98f, 0.86f, 0.64f), true)));
        if (n > 0)
        {
            var pulse = hint.CreateTween().SetLoops();
            pulse.TweenProperty(hint, "modulate:a", 0.45f, 0.8f).SetTrans(Tween.TransitionType.Sine);
            pulse.TweenProperty(hint, "modulate:a", 1f, 0.8f).SetTrans(Tween.TransitionType.Sine);
        }

        _coin = new DrachmaRite.Drachma { Radius = 58f * _s, Position = DrachmaRite.CoinSpot(_vp), MouseFilter = MouseFilterEnum.Ignore };
        _coin.Modulate = n > 0 ? Colors.White : new Color(1, 1, 1, 0.28f);
        _content.AddChild(_coin);
        _coinBob = _coin.CreateTween().SetLoops();
        _coinBob.TweenProperty(_coin, "position:y", _coin.Position.Y - 6f * _s, 0.9f).SetTrans(Tween.TransitionType.Sine);
        _coinBob.TweenProperty(_coin, "position:y", _coin.Position.Y, 0.9f).SetTrans(Tween.TransitionType.Sine);

        // the offers: a small wood panel standing on each cliff top (FABLE-105b: about a third smaller)
        var size = new Vector2(_vp.X * 0.165f, _vp.Y * 0.36f);
        for (int i = 0; i < Packs.Products.Count && i < 2; i++)
            BuildOffer(Packs.Products[i], new Vector2(i == 0 ? _vp.X * 0.07f : _vp.X * 0.765f, _vp.Y * 0.36f), size, why, i == 1);
    }

    private void BuildOffer(PackProduct p, Vector2 pos, Vector2 size, string? why, bool bestValue)
    {
        if (StyxArt.Backdrop != null) { BuildOfferPanel(p, pos, size, why, bestValue); return; }
        var stele = new Stele { Position = pos, Size = size, MouseFilter = MouseFilterEnum.Pass, Accent = bestValue ? new Color(0.98f, 0.80f, 0.35f) : new Color(0.80f, 0.70f, 0.48f) };
        _content.AddChild(stele);
        float pedH = size.X * 0.30f;
        // the coins this buys, in the pediment
        int shown = Math.Min(p.Packs, 5);
        float cr = (p.Packs == 1 ? 36f : 26f) * _s;
        for (int i = 0; i < shown; i++)
        {
            var c = new DrachmaRite.Drachma { Radius = cr, MouseFilter = MouseFilterEnum.Ignore };
            float spread = (shown - 1) * cr * 0.9f;
            c.Position = new Vector2(size.X / 2 - spread / 2 + i * cr * 0.9f, pedH * 0.70f + Math.Abs(i - (shown - 1) / 2f) * 4f * _s);
            c.Spin = i % 2 == 0 ? 0f : 0.35f;
            stele.AddChild(c);
        }
        if (bestValue)
        {
            var tag = new Panel { Position = new Vector2(size.X / 2 - 95 * _s, -20 * _s), Size = new Vector2(190 * _s, 40 * _s), MouseFilter = MouseFilterEnum.Ignore };
            tag.AddThemeStyleboxOverride("panel", Rounded(new Color(0.62f, 0.13f, 0.12f), (int)(8 * _s), new Color(0.98f, 0.80f, 0.35f)));
            stele.AddChild(tag);
            Lbl(tag, "BEST VALUE", 0, 0, 190 * _s, (int)(22 * _s), ThemeTokens.GetHeaderFont((int)(22 * _s)), new Color(1f, 0.90f, 0.70f), true);
        }
        float y = pedH + 22 * _s;
        Lbl(stele, p.Packs == 1 ? "1 Drachma" : $"{p.Packs} Drachmae", 0, y, size.X, (int)(40 * _s), ThemeTokens.GetCardNameFont((int)(40 * _s)), Gold, true);
        y += 60 * _s;
        Para(stele, $"{p.Packs * Packs.CardsPerPack} cards\n{p.Packs} Rare{(p.Packs == 1 ? "" : "s")} guaranteed", 12 * _s, y, size.X - 24 * _s, 70 * _s, (int)(24 * _s), Parchment, true);
        y += 82 * _s;
        Lbl(stele, PackStore.PreviewMode ? "free (preview)" : PackStore.PriceOf(p), 0, y, size.X, (int)(38 * _s), ThemeTokens.GetCardNameFont((int)(38 * _s)), Silver, true);
        string label = PackStore.PreviewMode ? "Take it" : $"Buy  {PackStore.PriceOf(p)}";
        var buy = Plate(label, why == null, size.X - 40 * _s, 78 * _s);
        buy.Position = new Vector2(20 * _s, size.Y - 78 * _s - 24 * _s);
        buy.Disabled = why != null;
        buy.Name = "Buy_" + p.Id;
        var id = p.Id;
        buy.Pressed += () => { Click(); PackStore.Buy(id); };
        stele.AddChild(buy);
    }

    /// <summary>FABLE-105: an offer on the cliffs - a bronze-framed wood panel with the coins, the count, the price and a bright Buy plaque.</summary>
    private void BuildOfferPanel(PackProduct p, Vector2 pos, Vector2 size, string? why, bool bestValue)
    {
        var panel = new Panel { Position = pos, Size = size, MouseFilter = MouseFilterEnum.Pass };
        if (!ScreenSkin.Wood(panel)) panel.AddThemeStyleboxOverride("panel", Glass(Vector2.Zero, size).GetThemeStylebox("panel"));
        _content.AddChild(panel);
        // the coins this buys, resting along the top rail
        int shown = Math.Min(p.Packs, 5);
        float cr = (p.Packs == 1 ? 26f : 19f) * _s;
        for (int i = 0; i < shown; i++)
        {
            var c = new DrachmaRite.Drachma { Radius = cr, MouseFilter = MouseFilterEnum.Ignore };
            float spread = (shown - 1) * cr * 0.95f;
            c.Position = new Vector2(size.X / 2 - spread / 2 + i * cr * 0.95f, 44 * _s + Math.Abs(i - (shown - 1) / 2f) * 3f * _s);
            c.Spin = i % 2 == 0 ? 0f : 0.35f;
            panel.AddChild(c);
        }
        if (bestValue)
        {
            var tag = new Panel { Position = new Vector2(size.X / 2 - 80 * _s, -16 * _s), Size = new Vector2(160 * _s, 34 * _s), MouseFilter = MouseFilterEnum.Ignore };
            tag.AddThemeStyleboxOverride("panel", Rounded(new Color(0.62f, 0.13f, 0.12f), (int)(8 * _s), new Color(0.98f, 0.80f, 0.35f)));
            tag.SetMeta("skinned", true);
            panel.AddChild(tag);
            Lbl(tag, "BEST VALUE", 0, 0, 160 * _s, (int)(19 * _s), ThemeTokens.GetHeaderFont((int)(19 * _s)), new Color(1f, 0.90f, 0.70f), true);
        }
        float y = 76 * _s;
        Lbl(panel, p.Packs == 1 ? "1 Drachma" : $"{p.Packs} Drachmae", 0, y, size.X, (int)(34 * _s), ThemeTokens.GetCardNameFont((int)(34 * _s)), Gold, true);
        y += 46 * _s;
        Para(panel, $"{p.Packs * Packs.CardsPerPack} cards  ·  {p.Packs} Rare{(p.Packs == 1 ? "" : "s")} guaranteed", 12 * _s, y, size.X - 24 * _s, 56 * _s, (int)(21 * _s), Parchment, true);
        y += 54 * _s;
        Lbl(panel, PackStore.PreviewMode ? "free (preview)" : PackStore.PriceOf(p), 0, y, size.X, (int)(36 * _s), ThemeTokens.GetCardNameFont((int)(36 * _s)), Silver, true);
        string label = PackStore.PreviewMode ? "Take it" : $"Buy  {PackStore.PriceOf(p)}";
        var buy = Plate(label, true, size.X - 36 * _s, 72 * _s);
        buy.Position = new Vector2(18 * _s, size.Y - 72 * _s - 20 * _s);
        buy.AddThemeFontSizeOverride("font_size", (int)(28 * _s));
        buy.Name = "Buy_" + p.Id;
        ScreenSkin.Plaque(buy);   // the bronze plaque, bright - never a dead grey button
        var id = p.Id;
        if (why != null) buy.Pressed += () => { Click(); Say(why); };
        else buy.Pressed += () => { Click(); PackStore.Buy(id); };
        panel.AddChild(buy);
    }

    private void BuildOdds(Vector2 pos, Vector2 size)
    {
        var stele = new Stele { Position = pos, Size = size, MouseFilter = MouseFilterEnum.Pass, Accent = new Color(0.55f, 0.85f, 0.95f), Runic = true };
        _content.AddChild(stele);
        var st = PackStore.Status;
        double relic = st?.RelicChance ?? Packs.RelicChance;
        float pad = 46 * _s, ped = size.X * 0.30f, y = ped * 0.52f;
        Lbl(stele, "WHAT THE STYX", pad, y - 14 * _s, size.X - 2 * pad, (int)(22 * _s), ThemeTokens.GetHeaderFont((int)(22 * _s)), Kicker, true);
        Lbl(stele, "RETURNS", pad, y + 14 * _s, size.X - 2 * pad, (int)(22 * _s), ThemeTokens.GetHeaderFont((int)(22 * _s)), Kicker, true);
        y = ped + 22 * _s;
        foreach (var (name, odds, col) in new[]
        {
            ("1 Common", "100%", new Color(0.86f, 0.84f, 0.78f)),
            ("1 Uncommon", "100%", new Color(0.45f, 0.75f, 0.95f)),
            ("1 Rare", $"{Math.Round((1 - relic) * 100)}%", Gold),
            ("  … or a Relic", $"{Math.Round(relic * 100)}%", new Color(0.80f, 0.62f, 1f)),
        })
        {
            Lbl(stele, name, pad, y, size.X - 2 * pad, (int)(27 * _s), ThemeTokens.GetCardNameFont((int)(27 * _s)), col);
            var o = Lbl(stele, odds, pad, y, size.X - 2 * pad, (int)(27 * _s), ThemeTokens.GetCardNameFont((int)(27 * _s)), col);
            o.HorizontalAlignment = HorizontalAlignment.Right;
            y += 40 * _s;
        }
        y += 6 * _s;
        Para(stele, "Every drachma the Styx accepts returns three cards — never the same card twice.", pad, y, size.X - 2 * pad, 60 * _s, (int)(21 * _s), Muted);
        y += 66 * _s;
        Para(stele, "Only older cards rise: a card joins 90 days after it is first found on the map. Tower cards and Lost Relics never do.",
            pad, y, size.X - 2 * pad, 100 * _s, (int)(21 * _s), Muted);
        y += 104 * _s;
        string pool = st == null
            ? (PackStore.PreviewMode ? "Preview build: drachmae are free and draw from every card, for testing." : "Reaching the shop…")
            : $"Beneath the Styx now: {st.Total} cards ({st.Commons} C · {st.Uncommons} U · {st.Rares} R · {st.Relics} Relic)" +
              (st.Waiting > 0 && st.NextCardAt is DateTimeOffset at ? $" — {st.Waiting} more from {at.ToLocalTime():MMM d}." : "");
        if (y < size.Y - 40 * _s) Para(stele, pool, pad, y, size.X - 2 * pad, size.Y - y - 16 * _s, (int)(20 * _s), new Color(0.60f, 0.78f, 0.84f));
    }

    // ── opening ──────────────────────────────────────────────────────────

    private void OpenNext() => OpenNext(null, 1f);

    private void OpenNext(Vector2? flick, float power)
    {
        var prog = CampaignContext.Progression;
        var next = prog.PendingPacks.FirstOrDefault();
        if (next == null) { CloseOpening(); return; }
        var before = next.Cards.ToDictionary(c => c, c => prog.Collection.TryGetValue(c, out var k) ? k : 0);
        var pack = PackStore.Open(next.Id);
        if (pack == null) return;
        ShowOpening(pack, before, flick, power);
    }

    /// <summary>FABLE-067: the Drachma Rite — flick a coin into the deep, the runes rise with your cards.</summary>
    private void ShowOpening(PendingPack pack, Dictionary<string, int> ownedBefore, Vector2? flick = null, float power = 1f)
    {
        _opening?.QueueFree();
        int left = CampaignContext.Progression.PendingPacks.Count;
        if (_coin != null && IsInstanceValid(_coin)) _coin.Visible = false;   // the rite's own coin takes off from the same spot
        _opening = DrachmaRite.Play(this, pack, ownedBefore, left, onDone: CloseOpening, onNext: left > 0 ? OpenNext : null, flick: flick, power: power);
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

    private static StyleBoxFlat Rounded(Color c, int r, Color? border = null)
    {
        var sb = new StyleBoxFlat { BgColor = c, CornerRadiusTopLeft = r, CornerRadiusTopRight = r, CornerRadiusBottomLeft = r, CornerRadiusBottomRight = r };
        if (border is Color b) { sb.BorderColor = b; sb.BorderWidthLeft = sb.BorderWidthRight = sb.BorderWidthTop = sb.BorderWidthBottom = 2; }
        return sb;
    }

    /// <summary>FABLE-105b: a heavier ink line for words floating on the cliffs.</summary>
    private static Label Outline2(Label l)
    {
        l.AddThemeColorOverride("font_outline_color", new Color(0.02f, 0.02f, 0.04f, 0.95f));
        l.AddThemeConstantOverride("outline_size", 9);
        return l;
    }

    /// <summary>FABLE-105b: a soft dark halo behind floating words - a wide blurred shadow, no box. Reads over starlight and mist alike.</summary>
    private static Label Halo(Label l)
    {
        l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.62f));
        l.AddThemeConstantOverride("shadow_outline_size", 22);
        l.AddThemeConstantOverride("shadow_offset_x", 0); l.AddThemeConstantOverride("shadow_offset_y", 0);
        return l;
    }

    private static void Outline(Label l)
    {
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        l.AddThemeConstantOverride("outline_size", StyxArt.Backdrop != null ? 8 : 5);   // FABLE-105: a heavier ink line over the painting
        if (StyxArt.Backdrop != null) { l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f)); l.AddThemeConstantOverride("shadow_offset_y", 2); }
    }

    /// <summary>FABLE-088: the cosmos over the cliffs comes alive - stars twinkle, mist drifts through the chasm and rune sparks
    /// rise from the deep where the offering falls. Still (drawn once) when Reduce motion is on.</summary>
    private partial class CosmosFx : Control
    {
        private readonly float[] _stars;
        private readonly float[] _motes;
        private float _t;
        private readonly bool _still = CampaignContext.Settings.ReduceMotion;
        public CosmosFx()
        {
            var rng = new RandomNumberGenerator { Seed = 4242 };
            _stars = new float[180 * 4];
            for (int i = 0; i < _stars.Length; i += 4) { _stars[i] = rng.Randf(); _stars[i + 1] = rng.Randf() * 0.62f; _stars[i + 2] = rng.RandfRange(0.6f, 2.2f); _stars[i + 3] = rng.Randf() * 6.28f; }
            _motes = new float[46 * 4];
            for (int i = 0; i < _motes.Length; i += 4) { _motes[i] = rng.RandfRange(0.36f, 0.64f); _motes[i + 1] = rng.Randf(); _motes[i + 2] = rng.RandfRange(0.04f, 0.10f); _motes[i + 3] = rng.Randf() * 6.28f; }
        }
        public override void _Process(double delta) { if (_still) return; _t += (float)delta; QueueRedraw(); }
        public override void _Draw()
        {
            float W = Size.X, H = Size.Y;
            for (int i = 0; i < _stars.Length; i += 4)   // twinkling stars
            {
                float tw = 0.45f + 0.55f * Mathf.Sin(_t * (0.8f + _stars[i + 2]) + _stars[i + 3]);
                var p = new Vector2(_stars[i] * W, _stars[i + 1] * H);
                DrawCircle(p, _stars[i + 2] * 0.9f, new Color(0.92f, 0.94f, 1f, 0.18f + 0.55f * tw * tw));
                if (_stars[i + 2] > 1.9f) DrawCircle(p, _stars[i + 2] * 3.2f, new Color(0.75f, 0.82f, 1f, 0.05f * tw));
            }
            for (int k = 0; k < 7; k++)   // mist banks drifting through the chasm
            {
                float x = ((k * 0.19f + _t * (0.006f + k * 0.0015f)) % 1.3f - 0.15f) * W, y = H * (0.62f + 0.05f * (k % 4));
                for (int r = 5; r >= 1; r--)
                    DrawCircle(new Vector2(x, y), H * (0.05f + 0.025f * r), new Color(0.78f, 0.82f, 0.9f, 0.012f));
            }
            for (int i = 0; i < _motes.Length; i += 4)   // rune sparks rising from the deep
            {
                float life = (_motes[i + 1] + _t * _motes[i + 2]) % 1f;
                float x = _motes[i] * W + Mathf.Sin(_t * 0.9f + _motes[i + 3]) * W * 0.012f, y = H * (0.95f - 0.75f * life);
                float a = Mathf.Sin(life * Mathf.Pi);
                var c = (i / 4) % 3 == 0 ? new Color(1f, 0.82f, 0.45f) : new Color(0.62f, 0.86f, 1f);
                DrawCircle(new Vector2(x, y), 2.2f + 1.6f * a, new Color(c.R, c.G, c.B, 0.75f * a));
                DrawCircle(new Vector2(x, y), 7f + 4f * a, new Color(c.R, c.G, c.B, 0.10f * a));
            }
        }
    }

    /// <summary>FABLE-069: a carved stone stele — a Greek pediment on top, a gold rim, and (Runic) a Norse
    /// knot-line border with glowing rune ticks. Children are its carved text.</summary>
    private partial class Stele : Control
    {
        public Color Accent = new(0.80f, 0.70f, 0.48f);
        public bool Runic;
        public override void _Draw()
        {
            float W = Size.X, H = Size.Y, ped = W * 0.30f;
            if (StyxArt.Backdrop != null && StyxArt.Stele == null)   // FABLE-084: on the cliffs the offer floats - no stone, just a pool of shadow under the words
            {
                for (int i = 6; i >= 1; i--)
                    DrawRect(new Rect2(W * (0.5f - 0.08f * i), ped * 0.5f + H * 0.03f * (6 - i), W * 0.16f * i, H - ped * 0.5f - H * 0.06f * (6 - i)), new Color(0.02f, 0.02f, 0.05f, 0.07f));
                return;
            }
            if (StyxArt.Stele is Texture2D painted)       // FABLE-070: the painted stele, its rim lit in the offer's colour
            {
                DrawTextureRect(painted, new Rect2(0, 0, W, H), false);
                DrawRect(new Rect2(W * 0.16f, ped + 6, W * 0.68f, H - ped - 30), new Color(0.05f, 0.04f, 0.04f, 0.38f));   // so carved text stays legible
                return;
            }
            var body = new[] { new Vector2(0, ped), new Vector2(W / 2, 0), new Vector2(W, ped), new Vector2(W, H), new Vector2(0, H) };
            // shadow, stone, a lighter face, the rim
            DrawColoredPolygon(body.Select(v => v + new Vector2(10, 14)).ToArray(), new Color(0, 0, 0, 0.45f));
            DrawColoredPolygon(body, new Color(0.11f, 0.10f, 0.12f, 0.96f));
            var face = new[] { new Vector2(14, ped + 8), new Vector2(W / 2, 16), new Vector2(W - 14, ped + 8), new Vector2(W - 14, H - 14), new Vector2(14, H - 14) };
            DrawColoredPolygon(face, new Color(0.17f, 0.155f, 0.17f, 0.96f));
            var rim = body.Append(body[0]).ToArray();
            DrawPolyline(rim, Accent, 3f, true);
            DrawPolyline(face.Append(face[0]).ToArray(), new Color(Accent, 0.35f), 1.5f, true);
            // the cornice line under the pediment
            DrawLine(new Vector2(0, ped), new Vector2(W, ped), new Color(Accent, 0.8f), 2f, true);
            if (Runic)
            {
                var glow = new Color(Accent, 0.55f);
                for (float y = ped + 30; y < H - 30; y += 46)
                {
                    DrawLine(new Vector2(22, y), new Vector2(22, y + 22), glow, 2f);
                    DrawLine(new Vector2(22, y + 6), new Vector2(30, y + 14), glow, 2f);
                    DrawLine(new Vector2(W - 22, y), new Vector2(W - 22, y + 22), glow, 2f);
                    DrawLine(new Vector2(W - 22, y + 10), new Vector2(W - 30, y + 2), glow, 2f);
                }
            }
            else
            {
                // a meander along the foot
                float step = 10f;
                for (float x = 24; x + step * 2 < W - 24; x += step * 2)
                {
                    float y0 = H - 40;
                    DrawPolyline(new[] { new Vector2(x, y0 + step), new Vector2(x, y0), new Vector2(x + step, y0), new Vector2(x + step, y0 + step * 0.6f), new Vector2(x + step * 0.5f, y0 + step * 0.6f) }, new Color(Accent, 0.45f), 1.5f);
                }
            }
        }
    }

    /// <summary>FABLE-069: a gold Greek key frieze — the page's top and bottom edge.</summary>
    private partial class Meander : Control
    {
        public override void _Draw()
        {
            float H = Size.Y, W = Size.X, u = H / 5f;
            DrawRect(new Rect2(0, 0, W, H), new Color(0.06f, 0.045f, 0.03f, 0.92f));
            var gold = new Color(0.86f, 0.70f, 0.36f, 0.85f);
            DrawLine(new Vector2(0, u * 0.5f), new Vector2(W, u * 0.5f), gold, 2f);
            DrawLine(new Vector2(0, H - u * 0.5f), new Vector2(W, H - u * 0.5f), gold, 2f);
            for (float x = 0; x < W; x += u * 4)
            {
                DrawPolyline(new[]
                {
                    new Vector2(x, H - u * 1.2f), new Vector2(x, u * 1.2f), new Vector2(x + u * 3, u * 1.2f), new Vector2(x + u * 3, H - u * 1.8f),
                    new Vector2(x + u * 1.2f, H - u * 1.8f), new Vector2(x + u * 1.2f, u * 2.2f), new Vector2(x + u * 2, u * 2.2f),
                }, gold, 2f);
            }
        }
    }

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

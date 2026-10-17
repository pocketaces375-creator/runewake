using System;
using System.Collections.Generic;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// FABLE-067: opening a pack is a rite, not a menu (Trikzos: "flicking a drachma over a cliff and the runes
/// rise up from there and tell you what you got … so we don't have any immersion breaking here").
///
///   1  Night. A cliff edge, a chasm full of mist. A silver-gold drachma rests on the edge.
///      "Flick the drachma into the deep" — swipe it (or tap it).
///   2  It spins up and out over the chasm, glinting, and falls away into the mist. A breath of silence.
///   3  Light wells up from the deep: one rune stone per card rises out of the chasm on a trail of embers,
///      glowing its rarity (pale for Common, blue Uncommon, gold Rare, violet Relic — the rarest rises last,
///      with a flash).
///   4  Tap a rune (or wait) and it turns over into the card, with its rarity and NEW if you never had it.
/// Everything is drawn here — no new art.
/// </summary>
public partial class DrachmaRite : Control
{
    private PendingPack _pack = null!;
    private Dictionary<string, int> _ownedBefore = null!;
    private Action? _onDone, _onNext;
    private int _left;
    private float _s = 1f;
    private Vector2 _vp;
    private Drachma _coin = null!;
    private Label _hint = null!;
    private bool _flicked;
    private Vector2 _pressAt;
    private bool _pressing;
    private readonly List<RuneStone> _runes = new();
    private int _revealed;
    private readonly RandomNumberGenerator _rng = new();
    private Tween? _bob, _hintPulse;

    private static readonly Color Parchment = new(0.91f, 0.86f, 0.78f);
    private static readonly Color Gold = new(0.95f, 0.80f, 0.42f);

    private Vector2? _autoFlick;
    private float _autoPower = 1f;

    /// <summary>The coin rests on the cliff here (the Card Packs page puts its own coin on the same spot).</summary>
    public static Vector2 CoinSpot(Vector2 vp) => new(vp.X * 0.235f, vp.Y * 0.665f);

    public static DrachmaRite Play(Node parent, PendingPack pack, Dictionary<string, int> ownedBefore, int left, Action onDone, Action? onNext,
                                   Vector2? flick = null, float power = 1f)
    {
        var r = new DrachmaRite { Name = "PackOpening", ZIndex = 800, MouseFilter = MouseFilterEnum.Stop };
        r._pack = pack; r._ownedBefore = ownedBefore; r._left = left; r._onDone = onDone; r._onNext = onNext;
        r._autoFlick = flick; r._autoPower = power;   // FABLE-069: flicked on the page itself — the rite picks up mid-flight
        parent.AddChild(r);
        return r;
    }

    public override void _Ready()
    {
        _vp = GetViewportRect().Size;
        _s = _vp.Y / 1080f;
        Position = Vector2.Zero;
        Size = _vp;

        AddChild(new Backdrop { Size = _vp, MouseFilter = MouseFilterEnum.Ignore, MoonAt = new Vector2(0.06f, 0.115f) });   // FABLE-069: same sky as the page

        var kicker = Text(_pack.Source == "preview" ? "PREVIEW OFFERING" : "AN OFFERING TO THE STYX", 0, 30 * _s, _vp.X, 26, Header(26), new Color(0.80f, 0.74f, 0.60f));
        kicker.Modulate = new Color(1, 1, 1, 0.85f);

        float coinR = 58f * _s;
        _coin = new Drachma { Radius = coinR, Position = CoinSpot(_vp), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_coin);
        _bob = _coin.CreateTween().SetLoops();
        _bob.TweenProperty(_coin, "position:y", _coin.Position.Y - 6f * _s, 0.9f).SetTrans(Tween.TransitionType.Sine);
        _bob.TweenProperty(_coin, "position:y", _coin.Position.Y, 0.9f).SetTrans(Tween.TransitionType.Sine);

        _hint = Text("Pay the ferryman — flick the drachma into the Styx", 0, _vp.Y * 0.84f, _vp.X * 0.5f, 34, Header(34), Gold);
        _hint.Position = new Vector2(_vp.X * 0.03f, _vp.Y * 0.80f);
        var sub = Text("swipe it — or tap it", _hint.Position.X, _hint.Position.Y + 48 * _s, _vp.X * 0.5f, 24, Body(26), new Color(0.78f, 0.72f, 0.60f));
        sub.Name = "Sub";
        _hintPulse = _hint.CreateTween().SetLoops();
        _hintPulse.TweenProperty(_hint, "modulate:a", 0.45f, 0.8f).SetTrans(Tween.TransitionType.Sine);
        _hintPulse.TweenProperty(_hint, "modulate:a", 1f, 0.8f).SetTrans(Tween.TransitionType.Sine);

        if (_autoFlick is Vector2 fd)
        {
            // the page underneath is the same cliff: no fade, the coin just keeps going
            foreach (var n in new[] { _hint, sub }) n.Visible = false;
            Callable.From(() => Flick(fd, _autoPower)).CallDeferred();
            return;
        }
        Modulate = new Color(1, 1, 1, 0);
        CreateTween().TweenProperty(this, "modulate:a", 1f, 0.35f);
        Sfx("unlock");
        if (System.Array.IndexOf(OS.GetCmdlineArgs(), "--packrite") >= 0)
            GetTree().CreateTimer(1.6).Timeout += () => { if (IsInstanceValid(this)) Flick(new Vector2(0.7f, -0.7f), 1.0f); };
        if (CampaignContext.ReduceMotion) { _flicked = true; GetTree().CreateTimer(0.4).Timeout += () => { if (IsInstanceValid(this)) RiseRunes(); }; _hint.Text = ""; }
    }

    // ── 1 · the flick ────────────────────────────────────────────────────

    public override void _GuiInput(InputEvent e)
    {
        if (_flicked) return;
        Vector2? pos = e switch
        {
            InputEventScreenTouch t => t.Position,
            InputEventMouseButton m when m.ButtonIndex == MouseButton.Left => m.Position,
            _ => null,
        };
        bool down = e is InputEventScreenTouch td ? td.Pressed : e is InputEventMouseButton md && md.Pressed;
        if (pos is Vector2 p)
        {
            if (down) { _pressAt = p; _pressing = true; }
            else if (_pressing)
            {
                _pressing = false;
                var swipe = p - _pressAt;
                bool nearCoin = (_pressAt - _coin.Position).Length() < 220f * _s;
                if (nearCoin || swipe.Length() > 60f * _s)
                    Flick(swipe.Length() > 60f * _s ? swipe.Normalized() : new Vector2(0.6f, -0.8f), Mathf.Clamp(swipe.Length() / (500f * _s), 0.6f, 1.4f));
            }
            AcceptEvent();
        }
    }

    private void Flick(Vector2 dir, float power)
    {
        if (_flicked) return;
        _flicked = true;
        Sfx("metal_clink");
        foreach (var n in new[] { _hint, GetNodeOrNull<Label>("Sub") }) if (n != null) n.CreateTween().TweenProperty(n, "modulate:a", 0f, 0.2f);
        _bob?.Kill();
        _hintPulse?.Kill();
        var start = _coin.Position;
        // over the chasm whatever way they swiped (a swipe "back" still flies out to the deep)
        float apexX = Mathf.Lerp(_vp.X * 0.40f, _vp.X * 0.52f, Mathf.Clamp(dir.X * 0.5f + 0.5f, 0f, 1f));
        var apex = new Vector2(apexX, _vp.Y * Mathf.Lerp(0.30f, 0.12f, (power - 0.6f) / 0.8f));
        var sink = new Vector2(_vp.X * 0.57f, _vp.Y * 0.98f);
        float up = 0.55f, down = 0.85f;
        var tw = CreateTween();
        tw.TweenMethod(Callable.From<float>(t =>
        {
            if (!IsInstanceValid(_coin)) return;
            Vector2 p = t < 1f
                ? Bezier(start, new Vector2(start.X + (apex.X - start.X) * 0.3f, apex.Y - 40f * _s), apex, Ease(t))
                : Bezier(apex, new Vector2(apex.X + (sink.X - apex.X) * 0.6f, apex.Y), sink, (t - 1f) * (t - 1f));
            _coin.Position = p;
            _coin.Spin = t * 9.5f;                               // the face turns: edge, face, edge…
            float sc = t < 1f ? 1f + 0.25f * t : Mathf.Lerp(1.25f, 0.18f, t - 1f);
            _coin.Scale = new Vector2(sc, sc);
            if (_rng.Randf() < 0.18f) Spark(p, Gold, 3f * _s);
        }), 0f, 2f, up + down);
        tw.TweenProperty(_coin, "modulate:a", 0f, 0.15f);
        tw.TweenInterval(0.45f);                                     // the breath of silence
        tw.TweenCallback(Callable.From(RiseRunes));
    }

    private static float Ease(float t) => 1f - (1f - t) * (1f - t);
    private static Vector2 Bezier(Vector2 a, Vector2 c, Vector2 b, float t) => (1 - t) * (1 - t) * a + 2 * (1 - t) * t * c + t * t * b;

    // ── 2 · the runes rise ───────────────────────────────────────────────

    private void RiseRunes()
    {
        if (!IsInstanceValid(this)) return;
        Sfx("spell");
        var deep = new Vector2(_vp.X * 0.57f, _vp.Y * 1.05f);
        // light wells up from the chasm
        var well = Glow(deep, new Color(0.75f, 0.65f, 1f, 0f), 700f * _s);
        well.CreateTween().TweenProperty(well, "modulate:a", 0.9f, 0.6f);

        int n = _pack.Cards.Count;
        float cw = 300f * _s, ch = 435f * _s, gap = 70f * _s;
        float x0 = (_vp.X - (n * cw + (n - 1) * gap)) / 2f;
        // the rarest rises last
        var order = new List<int>();
        for (int i = 0; i < n; i++) order.Add(i);
        order.Sort((a, b) => RarityRank(RarityOf(a)).CompareTo(RarityRank(RarityOf(b))));
        bool rm = CampaignContext.ReduceMotion;
        for (int k = 0; k < order.Count; k++)
        {
            int i = order[k];
            string rarity = RarityOf(i);
            var col = RarityColor(rarity);
            var stone = new RuneStone { Size = new Vector2(cw, ch), PivotOffset = new Vector2(cw, ch) / 2f, Color = col, Index = i, MouseFilter = MouseFilterEnum.Stop };
            var target = new Vector2(x0 + i * (cw + gap), _vp.Y * 0.11f);
            stone.Position = deep - stone.Size / 2f;
            stone.Scale = new Vector2(0.15f, 0.15f);
            stone.Modulate = new Color(1, 1, 1, 0);
            stone.GuiInput += ev =>
            {
                if ((ev is InputEventScreenTouch { Pressed: false } || ev is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }) && stone.Ready && !stone.Revealed)
                    Reveal(stone);
            };
            AddChild(stone);
            _runes.Add(stone);
            float delay = rm ? k * 0.15f : 0.35f + k * 0.65f + (k == order.Count - 1 && RarityRank(rarity) >= 2 ? 0.35f : 0f);
            float dur = rm ? 0.2f : 1.15f;
            var tw = stone.CreateTween();
            tw.TweenInterval(delay);
            tw.TweenCallback(Callable.From(() => { if (!rm) Sfx(RarityRank(rarity) >= 2 ? "spell" : "click"); }));
            tw.SetParallel();
            tw.TweenProperty(stone, "modulate:a", 1f, 0.25f);
            var from = stone.Position;
            var ctrl = new Vector2(Mathf.Lerp(from.X, target.X, 0.2f), _vp.Y * 0.55f);
            tw.TweenMethod(Callable.From<float>(t =>
            {
                if (!IsInstanceValid(stone)) return;
                float e = 1f - Mathf.Pow(1f - t, 3f);
                stone.Position = Bezier(from, ctrl, target, e);
                if (!rm && _rng.Randf() < 0.55f) Spark(stone.Position + stone.Size / 2f + new Vector2(_rng.RandfRange(-cw * 0.3f, cw * 0.3f), ch * 0.4f), col, 4f * _s);
            }), 0f, 1f, dur);
            tw.TweenProperty(stone, "scale", Vector2.One, dur).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
            tw.TweenProperty(stone, "rotation", 0f, dur).From(_rng.RandfRange(-0.6f, 0.6f));
            tw.SetParallel(false);
            tw.TweenCallback(Callable.From(() =>
            {
                stone.Ready = true;
                if (RarityRank(rarity) >= 2 && !rm) { Flash(col); Ring(target + stone.Size / 2f, col, 30f * _s, 380f * _s); }
                // nobody has to tap: each rune turns over by itself after a moment
                GetTree().CreateTimer(rm ? 0.2 : 0.9 + k * 0.35).Timeout += () => { if (IsInstanceValid(stone) && !stone.Revealed) Reveal(stone); };
            }));
        }
        var tapHint = Text("Tap a rune to read it", 0, _vp.Y * 0.11f + ch + 96f * _s, _vp.X, 26, Body(28), new Color(0.80f, 0.74f, 0.60f));
        tapHint.Name = "TapHint";
        tapHint.Modulate = new Color(1, 1, 1, 0);
        tapHint.CreateTween().TweenInterval(2.2f);
        tapHint.CreateTween().TweenProperty(tapHint, "modulate:a", 0.9f, 0.4f).SetDelay(2.2f);
    }

    private string RarityOf(int i) => i < _pack.Rarities.Count ? _pack.Rarities[i] : "COMMON";

    // ── 3 · the reveal ───────────────────────────────────────────────────

    private void Reveal(RuneStone stone)
    {
        if (stone.Revealed) return;
        stone.Revealed = true;
        int i = stone.Index;
        string id = _pack.Cards[i];
        string rarity = RarityOf(i);
        var col = RarityColor(rarity);
        var def = CardRegistry.Get(id);
        bool isNew = _ownedBefore.TryGetValue(id, out var had) && had == 0;
        float cw = stone.Size.X, ch = stone.Size.Y;
        var tw = stone.CreateTween();
        tw.TweenProperty(stone, "scale:x", 0.02f, 0.14f).SetTrans(Tween.TransitionType.Sine);
        tw.TweenCallback(Callable.From(() =>
        {
            stone.ShowFace = true;
            stone.QueueRedraw();
            string artPath = $"res://content/art/cards_baked/{id}.webp";
            if (def != null && ResourceLoader.Exists(artPath))
            {
                var plate = new CardPlate { MouseFilter = MouseFilterEnum.Ignore };
                stone.AddChild(plate);
                plate.Setup(id, def.Attack, def.Vigor, cw, ch, def.Cost);
            }
            else
            {
                var l = Text(def?.Name ?? id, 10, ch / 2 - 30, cw - 20, 32, Header(32), Parchment);
                l.Reparent(stone, false);
                l.AutowrapMode = TextServer.AutowrapMode.Word;
            }
            var under = Text(rarity + (isNew ? "  ·  NEW" : ""), stone.Position.X, stone.Position.Y + ch + 12f * _s, cw, 28, Header(28), col);
            var nm = Text(def?.Name ?? id, stone.Position.X, stone.Position.Y + ch + 52f * _s, cw, 26, Body(28), Parchment);
            foreach (var l in new[] { under, nm }) { l.Modulate = new Color(1, 1, 1, 0); l.CreateTween().TweenProperty(l, "modulate:a", 1f, 0.25f); }
            if (RarityRank(rarity) >= 2) stone.Glowing = true;
            if (isNew) Ring(stone.Position + stone.Size / 2f, col, 20f * _s, 260f * _s);
            Sfx("click");
        }));
        tw.TweenProperty(stone, "scale:x", 1f, 0.2f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        if (++_revealed == _runes.Count)
            tw.TweenCallback(Callable.From(ShowButtons));
    }

    private void ShowButtons()
    {
        var hint = GetNodeOrNull<Label>("TapHint");
        if (hint != null) hint.Visible = false;
        var done = Plate("Done", _left == 0, 320f * _s, 84f * _s);
        done.Position = new Vector2(_vp.X / 2 - (_left > 0 ? 340f * _s : 160f * _s), _vp.Y - 130f * _s);
        done.Pressed += () => { Sfx("click"); _onDone?.Invoke(); };
        AddChild(done);
        if (_left > 0 && _onNext != null)
        {
            var more = Plate($"Offer another ({_left} left)", true, 460f * _s, 84f * _s);
            more.Position = new Vector2(_vp.X / 2 + 20f * _s, _vp.Y - 130f * _s);
            more.Name = "OpenNext";
            more.Pressed += () => { Sfx("click"); _onNext?.Invoke(); };
            AddChild(more);
        }
        foreach (var b in new Control[] { done }) { b.Modulate = new Color(1, 1, 1, 0); b.CreateTween().TweenProperty(b, "modulate:a", 1f, 0.3f); }
    }

    // ── small effects ────────────────────────────────────────────────────

    private void Spark(Vector2 at, Color c, float r)
    {
        var d = new FxDot { Color = c.Lerp(Colors.White, 0.4f), Radius = r, Position = at, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(d);
        var tw = d.CreateTween().SetParallel();
        tw.TweenProperty(d, "position", at + new Vector2(_rng.RandfRange(-24f, 24f) * _s, _rng.RandfRange(-60f, -10f) * _s), 0.7f);
        tw.TweenProperty(d, "modulate:a", 0f, 0.7f);
        tw.SetParallel(false);
        tw.TweenCallback(Callable.From(d.QueueFree));
    }

    private TextureRect Glow(Vector2 at, Color c, float size)
    {
        var tex = new GradientTexture2D
        {
            Gradient = new Gradient { Offsets = new[] { 0f, 0.45f, 1f }, Colors = new[] { new Color(1, 1, 1, 0.8f), new Color(1, 1, 1, 0.25f), new Color(1, 1, 1, 0) } },
            Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f), Width = 64, Height = 64,
        };
        var g = new TextureRect { Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, Size = new Vector2(size, size), Position = at - new Vector2(size, size) / 2f, Modulate = c, MouseFilter = MouseFilterEnum.Ignore, Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
        AddChild(g);
        MoveChild(g, 1);   // above the backdrop, under everything else
        return g;
    }

    private void Ring(Vector2 at, Color c, float r0, float r1)
    {
        var ring = new FxRing { Color = c, Radius = r0, Width = 6f * _s, Position = at };
        AddChild(ring);
        var tw = ring.CreateTween().SetParallel();
        tw.TweenMethod(Callable.From<float>(r => { if (IsInstanceValid(ring)) ring.Radius = r; }), r0, r1, 0.6f).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(ring, "modulate:a", 0f, 0.6f);
        tw.SetParallel(false);
        tw.TweenCallback(Callable.From(ring.QueueFree));
    }

    private void Flash(Color c)
    {
        var f = new ColorRect { Color = c.Lerp(Colors.White, 0.5f), Size = _vp, Modulate = new Color(1, 1, 1, 0.3f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(f);
        var tw = f.CreateTween();
        tw.TweenProperty(f, "modulate:a", 0f, 0.35f);
        tw.TweenCallback(Callable.From(f.QueueFree));
    }

    private static int RarityRank(string r) => r switch { "UNCOMMON" => 1, "RARE" => 2, "RELIC" => 3, _ => 0 };

    public static Color RarityColor(string r) => r switch
    {
        "UNCOMMON" => new Color(0.45f, 0.75f, 0.95f),
        "RARE" => new Color(0.98f, 0.80f, 0.35f),
        "RELIC" => new Color(0.80f, 0.62f, 1f),
        _ => new Color(0.86f, 0.84f, 0.78f),
    };

    private void Sfx(string name) => GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx(name);

    private Font Header(int px) => ThemeTokens.GetHeaderFont((int)(px * _s));
    private Font Body(int px) => ThemeTokens.GetBodyFont((int)(px * _s));

    private Label Text(string text, float x, float y, float w, int size, Font font, Color colour)
    {
        var l = new Label
        {
            Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(x, y), Size = new Vector2(w, size * _s + 16), MouseFilter = MouseFilterEnum.Ignore,
        };
        if (font != null) l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", (int)(size * _s));
        l.AddThemeColorOverride("font_color", colour);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        l.AddThemeConstantOverride("outline_size", Mathf.Max(2, (int)(4 * _s)));
        AddChild(l);
        return l;
    }

    private Button Plate(string text, bool primary, float w, float h)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(w, h), Size = new Vector2(w, h), FocusMode = FocusModeEnum.None };
        b.AddThemeFontOverride("font", ThemeTokens.GetButtonFont((int)(32 * _s))); b.AddThemeFontSizeOverride("font_size", (int)(32 * _s));
        b.AddThemeColorOverride("font_color", primary ? Color.FromHtml("#F2DFA6") : Color.FromHtml("#D8CBB0"));
        b.AddThemeStyleboxOverride("normal", primary ? MenuButtons.PrimaryNormal() : MenuButtons.QuietNormal());
        b.AddThemeStyleboxOverride("hover", primary ? MenuButtons.PrimaryHover() : MenuButtons.Hover());
        b.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed());
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        MenuButtons.Animate(b);
        return b;
    }

    // ── drawn pieces ─────────────────────────────────────────────────────

    /// <summary>Night sky, distant peaks, the cliff you stand on, the far wall, mist in the chasm.</summary>
    public partial class Backdrop : Control
    {
        private readonly float[] _stars = new float[180];
        /// <summary>FABLE-069: where the moon hangs (fractions of the screen) — the Card Packs page moves it clear of the steles.</summary>
        public Vector2 MoonAt = new(0.80f, 0.18f);
        public override void _Ready()
        {
            var rng = new RandomNumberGenerator { Seed = 7 };
            for (int i = 0; i < _stars.Length; i++) _stars[i] = rng.Randf();
        }

        public override void _Draw()
        {
            var s = Size; float W = s.X, H = s.Y;
            // FABLE-070: the painted Styx, when it is installed
            if (StyxArt.Backdrop is Texture2D painting)
            {
                DrawTextureRectRegion(painting, new Rect2(Vector2.Zero, s), StyxArt.CoverSource(painting, s));
                return;
            }
            // sky: a vertical gradient in bands
            int bands = 24;
            for (int i = 0; i < bands; i++)
            {
                float t = i / (float)bands;
                var c = new Color(0.05f, 0.06f, 0.14f).Lerp(new Color(0.24f, 0.15f, 0.30f), t);
                DrawRect(new Rect2(0, H * t, W, H / bands + 1), c);
            }
            for (int i = 0; i + 2 < _stars.Length; i += 3)
            {
                var p = new Vector2(_stars[i] * W, _stars[i + 1] * H * 0.55f);
                DrawCircle(p, 0.6f + _stars[i + 2] * 1.6f, new Color(1, 1, 1, 0.25f + _stars[i + 2] * 0.5f));
            }
            // a pale moon
            DrawCircle(new Vector2(W * MoonAt.X, H * MoonAt.Y), H * 0.06f, new Color(0.92f, 0.90f, 0.82f, 0.9f));
            DrawCircle(new Vector2(W * MoonAt.X, H * MoonAt.Y), H * 0.09f, new Color(0.92f, 0.90f, 0.82f, 0.08f));
            // distant peaks
            DrawColoredPolygon(new[]
            {
                new Vector2(0, H * 0.62f), new Vector2(W * 0.10f, H * 0.50f), new Vector2(W * 0.18f, H * 0.57f), new Vector2(W * 0.30f, H * 0.44f),
                new Vector2(W * 0.42f, H * 0.56f), new Vector2(W * 0.55f, H * 0.47f), new Vector2(W * 0.66f, H * 0.58f), new Vector2(W * 0.78f, H * 0.46f),
                new Vector2(W * 0.90f, H * 0.55f), new Vector2(W, H * 0.50f), new Vector2(W, H), new Vector2(0, H),
            }, new Color(0.13f, 0.11f, 0.20f));
            DrawPolyline(new[]
            {
                new Vector2(0, H * 0.62f), new Vector2(W * 0.10f, H * 0.50f), new Vector2(W * 0.18f, H * 0.57f), new Vector2(W * 0.30f, H * 0.44f),
                new Vector2(W * 0.42f, H * 0.56f), new Vector2(W * 0.55f, H * 0.47f), new Vector2(W * 0.66f, H * 0.58f), new Vector2(W * 0.78f, H * 0.46f),
                new Vector2(W * 0.90f, H * 0.55f), new Vector2(W, H * 0.50f),
            }, new Color(0.75f, 0.72f, 0.85f, 0.25f), 2f, true);   // moonlight on the ridges
            // the chasm's glow and mist
            for (int i = 0; i < 10; i++)
            {
                float t = i / 10f;
                DrawRect(new Rect2(0, H * (0.70f + t * 0.30f), W, H * 0.035f), new Color(0.55f, 0.45f, 0.72f, 0.06f + t * 0.07f));
            }
            // the far wall
            DrawColoredPolygon(new[]
            {
                new Vector2(W, H), new Vector2(W, H * 0.58f), new Vector2(W * 0.93f, H * 0.60f), new Vector2(W * 0.87f, H * 0.67f),
                new Vector2(W * 0.82f, H * 0.80f), new Vector2(W * 0.79f, H),
            }, new Color(0.07f, 0.06f, 0.10f));
            // the cliff you stand on
            DrawColoredPolygon(new[]
            {
                new Vector2(0, H), new Vector2(0, H * 0.71f), new Vector2(W * 0.12f, H * 0.69f), new Vector2(W * 0.22f, H * 0.705f),
                new Vector2(W * 0.29f, H * 0.72f), new Vector2(W * 0.325f, H * 0.78f), new Vector2(W * 0.31f, H * 0.90f), new Vector2(W * 0.33f, H),
            }, new Color(0.05f, 0.04f, 0.06f));
            DrawPolyline(new[] { new Vector2(0, H * 0.71f), new Vector2(W * 0.12f, H * 0.69f), new Vector2(W * 0.22f, H * 0.705f), new Vector2(W * 0.29f, H * 0.72f), new Vector2(W * 0.325f, H * 0.78f) },
                new Color(0.80f, 0.74f, 0.90f, 0.55f), 3f, true);
            DrawShrine(W, H);
        }

        /// <summary>FABLE-069: on the cliff, a broken Greek portico (two columns, a cracked lintel) and a Norse
        /// rune stone whose carvings glow faintly — where the offerings are made.</summary>
        private void DrawShrine(float W, float H)
        {
            var stone = new Color(0.10f, 0.09f, 0.12f);
            var lit = new Color(0.62f, 0.58f, 0.74f, 0.55f);
            float baseY = H * 0.695f, colW = W * 0.022f, colH = H * 0.30f;
            foreach (float cx in new[] { W * 0.045f, W * 0.135f })
            {
                // shaft, capital, base — with a moonlit edge and three flutes
                DrawRect(new Rect2(cx - colW / 2, baseY - colH, colW, colH), stone);
                DrawRect(new Rect2(cx - colW * 0.85f, baseY - colH - H * 0.018f, colW * 1.7f, H * 0.018f), stone);
                DrawRect(new Rect2(cx - colW * 0.8f, baseY - H * 0.014f, colW * 1.6f, H * 0.014f), stone);
                DrawLine(new Vector2(cx + colW / 2, baseY - colH), new Vector2(cx + colW / 2, baseY), lit, 2f);
                for (int f = 1; f <= 3; f++)
                    DrawLine(new Vector2(cx - colW / 2 + colW * f / 4f, baseY - colH + 4), new Vector2(cx - colW / 2 + colW * f / 4f, baseY - 4), new Color(0, 0, 0, 0.35f), 1.5f);
            }
            // the lintel, broken off past the second column
            float ly = baseY - colH - H * 0.018f - H * 0.03f;
            DrawColoredPolygon(new[] { new Vector2(W * 0.02f, ly), new Vector2(W * 0.165f, ly), new Vector2(W * 0.172f, ly + H * 0.012f), new Vector2(W * 0.160f, ly + H * 0.03f), new Vector2(W * 0.02f, ly + H * 0.03f) }, stone);
            DrawLine(new Vector2(W * 0.02f, ly), new Vector2(W * 0.165f, ly), lit, 2f);
            // a meander carved along it
            float step = H * 0.012f;
            for (float x = W * 0.026f; x < W * 0.155f; x += step * 2f)
            {
                float y0 = ly + H * 0.009f;
                DrawPolyline(new[] { new Vector2(x, y0 + step), new Vector2(x, y0), new Vector2(x + step, y0), new Vector2(x + step, y0 + step * 0.6f), new Vector2(x + step * 0.5f, y0 + step * 0.6f) }, new Color(0.85f, 0.70f, 0.38f, 0.55f), 1.5f);
            }
            // the rune stone, leaning by the edge
            var rs = new[] { new Vector2(W * 0.265f, baseY + H * 0.03f), new Vector2(W * 0.262f, baseY - H * 0.13f), new Vector2(W * 0.278f, baseY - H * 0.165f), new Vector2(W * 0.298f, baseY - H * 0.14f), new Vector2(W * 0.302f, baseY + H * 0.035f) };
            DrawColoredPolygon(rs, new Color(0.09f, 0.085f, 0.11f));
            DrawPolyline(new[] { rs[1], rs[2], rs[3] }, lit, 2f);
            var glow = new Color(0.55f, 0.85f, 0.95f, 0.65f);
            float rx = W * 0.281f, ry = baseY - H * 0.12f, rh = H * 0.10f;
            DrawLine(new Vector2(rx, ry), new Vector2(rx, ry + rh), glow, 2.5f);
            DrawLine(new Vector2(rx, ry + rh * 0.15f), new Vector2(rx + W * 0.008f, ry + rh * 0.32f), glow, 2.5f);
            DrawLine(new Vector2(rx, ry + rh * 0.45f), new Vector2(rx - W * 0.008f, ry + rh * 0.62f), glow, 2.5f);
            DrawLine(new Vector2(rx, ry + rh * 0.62f), new Vector2(rx + W * 0.008f, ry + rh * 0.80f), glow, 2.5f);
        }
    }

    /// <summary>The coin: a gold disc, a rim, an owl-eye rune. Spin turns it edge-on and back.</summary>
    public partial class Drachma : Control
    {
        public float Radius = 50f;
        private float _spin;
        public float Spin { get => _spin; set { _spin = value; QueueRedraw(); } }
        public override void _Draw()
        {
            float squash = Mathf.Abs(Mathf.Cos(_spin));
            float rx = Mathf.Max(Radius * 0.12f, Radius * squash), ry = Radius;
            bool face = Mathf.Cos(_spin) >= 0;
            if (StyxArt.Coin is Texture2D painted)          // FABLE-070: the painted drachma, turning edge-on as it spins
            {
                DrawCircle(Vector2.Zero, Radius * 1.45f, new Color(1f, 0.85f, 0.45f, 0.10f));
                var tint = face ? Colors.White : new Color(0.80f, 0.80f, 0.86f);
                DrawTextureRect(painted, new Rect2(-rx, -ry, rx * 2, ry * 2), false, tint);
                return;
            }
            DrawSetTransform(Vector2.Zero, 0, new Vector2(rx / Radius, 1f));
            DrawCircle(Vector2.Zero, Radius * 1.35f, new Color(1f, 0.85f, 0.45f, 0.10f));
            DrawCircle(Vector2.Zero, Radius, face ? new Color(0.86f, 0.70f, 0.32f) : new Color(0.74f, 0.74f, 0.78f));
            DrawCircle(Vector2.Zero, Radius * 0.86f, face ? new Color(0.95f, 0.80f, 0.42f) : new Color(0.86f, 0.86f, 0.9f));
            DrawArc(Vector2.Zero, Radius * 0.93f, 0, Mathf.Tau, 48, new Color(0.55f, 0.42f, 0.18f), Radius * 0.06f, true);
            // the owl's eye — two arcs and a pupil (silver side: a rune star)
            var ink = face ? new Color(0.45f, 0.32f, 0.12f) : new Color(0.40f, 0.40f, 0.48f);
            if (face)
            {
                DrawArc(new Vector2(0, Radius * 0.25f), Radius * 0.55f, Mathf.Pi * 1.15f, Mathf.Pi * 1.85f, 24, ink, Radius * 0.07f, true);
                DrawArc(new Vector2(0, -Radius * 0.25f), Radius * 0.55f, Mathf.Pi * 0.15f, Mathf.Pi * 0.85f, 24, ink, Radius * 0.07f, true);
                DrawCircle(Vector2.Zero, Radius * 0.16f, ink);
            }
            else
            {
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.Tau / 6f;
                    DrawLine(Vector2.Zero, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Radius * 0.6f, ink, Radius * 0.06f, true);
                }
            }
            // a glint
            DrawCircle(new Vector2(-Radius * 0.35f, -Radius * 0.4f), Radius * 0.1f, new Color(1, 1, 1, 0.65f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }

    /// <summary>A rune stone: dark slate with a glowing rim and rune in its rarity's colour, until it turns over.</summary>
    private partial class RuneStone : Control
    {
        public Color Color = Colors.White;
        public int Index;
        public bool Ready, Revealed, ShowFace;
        private bool _glowing;
        private float _t;
        public bool Glowing { get => _glowing; set { _glowing = value; QueueRedraw(); } }
        public override void _Process(double delta) { _t += (float)delta; if (!ShowFace || _glowing) QueueRedraw(); }
        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            float pulse = 0.5f + 0.5f * Mathf.Sin(_t * 3f);
            // the halo
            for (int i = 6; i >= 1; i--)
            {
                var grow = new Rect2(r.Position - Vector2.One * i * 6f, r.Size + Vector2.One * i * 12f);
                DrawRect(grow, new Color(Color, (ShowFace && !_glowing ? 0.015f : 0.05f + 0.03f * pulse) * (7 - i) / 6f), true);
            }
            if (ShowFace) return;   // the card plate covers the stone now
            if (StyxArt.Rune is Texture2D painted)       // FABLE-070: the painted rune stone, lit in its rarity's colour
            {
                DrawTextureRect(painted, r, false, Colors.White.Lerp(Color, 0.35f));
                DrawRect(r, new Color(Color, 0.55f + 0.35f * pulse), false, 3f);
                return;
            }
            DrawRect(r, new Color(0.08f, 0.075f, 0.09f), true);
            DrawRect(r.Grow(-10f), new Color(0.12f, 0.11f, 0.13f), true);
            DrawRect(r, new Color(Color, 0.9f), false, 4f);
            DrawRect(r.Grow(-14f), new Color(Color, 0.35f), false, 2f);
            // the rune: a tall stave with branches
            var c = Size / 2f; float h = Size.Y * 0.28f;
            var col = new Color(Color.Lerp(Colors.White, 0.25f), 0.75f + 0.25f * pulse);
            float w = Size.X * 0.025f + 2f;
            DrawLine(c + new Vector2(0, -h), c + new Vector2(0, h), col, w, true);
            DrawLine(c + new Vector2(0, -h * 0.55f), c + new Vector2(h * 0.55f, -h), col, w, true);
            DrawLine(c + new Vector2(0, -h * 0.10f), c + new Vector2(-h * 0.55f, -h * 0.55f), col, w, true);
            DrawLine(c + new Vector2(0, h * 0.35f), c + new Vector2(h * 0.5f, h * 0.05f), col, w, true);
            DrawArc(c, h * 1.25f, 0, Mathf.Tau, 48, new Color(Color, 0.25f + 0.2f * pulse), w * 0.6f, true);
        }
    }
}

/// <summary>A small filled circle (sparks).</summary>
public partial class FxDot : Control
{
    public float Radius = 3f;
    public Color Color = Colors.White;
    public override void _Draw() => DrawCircle(Vector2.Zero, Radius, Color);
}

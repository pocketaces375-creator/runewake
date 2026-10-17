using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;

namespace Runewake.Client;

/// <summary>
/// FABLE-065: the ritual cast as a set piece (Trikzos: "epic, bigger and more dramatic so it's easier to see").
///
///   0.00  the board darkens, a vignette in the ritual's colour closes in
///         the card flies in from the caster's side and grows to half the screen;
///         behind it a glow, turning light rays and two rune circles open up
///   0.32  SLAM — shockwave, white flash, a burst of sparks, the screen shakes;
///         "YOU CAST" / "ENEMY CASTS", the ritual's name in big letters, its effect icons
///   1.25  the card collapses into a blazing orb; one bolt per target flies out on an arc
///         with a trail and lands with an impact that says what it did:
///           fire (damage) · heal · empower (buff) · curse · doom (destroy) · bind (stun/lock) ·
///           cards (draw/bury…) · summon · arcane
///   ~2.0  the darkness lifts; the damage / heal numbers follow on the targets
/// Countered: a Sigil slams onto the card, COUNTERED stamps down, the card shatters into pieces.
/// Reduce Motion keeps the plain one-line callout (PlayCast).
/// </summary>
public partial class CombatFx
{
    /// <summary>What the screen shakes (the duel's root). Null = no shake.</summary>
    public Func<Control?>? ShakeTarget;

    private Vector2? _shakeHome;
    private Tween? _shakeTween;
    private readonly RandomNumberGenerator _rng = new();

    private static CanvasItemMaterial? _add;
    private static Texture2D? _dotTex, _glowTex, _rayTex, _vignetteTex;
    private static CanvasItemMaterial Additive => _add ??= new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
    private static Texture2D DotTex => _dotTex ??= Radial(new[] { 0f, 0.35f, 1f }, new[] { Colors.White, new Color(1, 1, 1, 0.6f), new Color(1, 1, 1, 0) }, new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), 64, 64);
    private static Texture2D GlowTex => _glowTex ??= Radial(new[] { 0f, 0.4f, 1f }, new[] { new Color(1, 1, 1, 0.85f), new Color(1, 1, 1, 0.3f), new Color(1, 1, 1, 0) }, new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), 128, 128);
    private static Texture2D RayTex => _rayTex ??= Radial(new[] { 0f, 0.5f, 1f }, new[] { new Color(1, 1, 1, 0.9f), new Color(1, 1, 1, 0.25f), new Color(1, 1, 1, 0) }, new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), 32, 128);
    private static Texture2D VignetteTex => _vignetteTex ??= Radial(new[] { 0f, 0.62f, 1f }, new[] { new Color(1, 1, 1, 0), new Color(1, 1, 1, 0.08f), Colors.White }, new Vector2(0.5f, 0.5f), new Vector2(1.1f, 0.5f), 128, 128);

    private static GradientTexture2D Radial(float[] offsets, Color[] colors, Vector2 from, Vector2 to, int w, int h) => new()
    {
        Gradient = new Gradient { Offsets = offsets, Colors = colors },
        Fill = GradientTexture2D.FillEnum.Radial, FillFrom = from, FillTo = to, Width = w, Height = h,
    };

    private enum School { Arcane, Summon, Cards, Buff, Heal, Bind, Curse, Fire, Doom }

    private sealed record Hit(Vector2 At, School School, int TargetView);

    // ── the set piece ────────────────────────────────────────────────────

    private float PlayCastEpic(CombatEvent e, bool countered)
    {
        var def = CardRegistry.Get(e.SourceDefId);
        string name = def?.Name ?? e.SourceDefId;
        var color = StrataOf(e.SourceDefId);
        var hot = color.Lerp(Colors.White, 0.45f);
        int view = View(e.Side);
        var vs = GetViewportRect().Size;
        float H = vs.Y;
        var c = ToLocal(BoardCenter());
        var hits = countered ? new List<Hit>() : SpellTargets(e);
        Sfx?.Invoke("spell");

        var root = new Control { Name = "SpellCast", MouseFilter = MouseFilterEnum.Ignore };
        FullScreen(root);
        AddChild(root);

        // ── 1. darkness and a coloured vignette ──
        var dim = new ColorRect { Color = new Color(0.01f, 0.005f, 0.02f, 1f), Modulate = new Color(1, 1, 1, 0), MouseFilter = MouseFilterEnum.Ignore };
        FullScreen(dim);
        root.AddChild(dim);
        var vig = new TextureRect { Texture = VignetteTex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, Modulate = new Color(color, 0f), MouseFilter = MouseFilterEnum.Ignore };
        FullScreen(vig);
        root.AddChild(vig);

        // ── 2. the stage behind the card: glow, rays, rune circles ──
        float R = H * 0.42f;
        var stage = new Control { Position = c, MouseFilter = MouseFilterEnum.Ignore };
        root.AddChild(stage);
        var glow = Sprite(GlowTex, new Vector2(R * 3.0f, R * 3.0f), new Color(color.Lerp(Colors.White, 0.15f), 0f), additive: true);
        stage.AddChild(glow);
        var rays = new Control { MouseFilter = MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        stage.AddChild(rays);
        const int RayCount = 12;
        for (int i = 0; i < RayCount; i++)
        {
            float len = R * (i % 2 == 0 ? 1.7f : 1.25f), w = H * (i % 2 == 0 ? 0.075f : 0.05f);
            var ray = new TextureRect
            {
                Texture = RayTex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
                Size = new Vector2(w, len), Position = new Vector2(-w / 2f, -len), PivotOffset = new Vector2(w / 2f, len),
                Rotation = i * Mathf.Tau / RayCount, Modulate = new Color(hot, 0.5f), Material = Additive, MouseFilter = MouseFilterEnum.Ignore,
            };
            rays.AddChild(ray);
        }
        var outer = new FxRuneCircle { Radius = R * 0.92f, Color = new Color(color, 0.9f), Width = 4f * _s, Points = 6, Scale = new Vector2(0.2f, 0.2f), Modulate = new Color(1, 1, 1, 0) };
        var inner = new FxRuneCircle { Radius = R * 0.62f, Color = new Color(hot, 0.85f), Width = 3f * _s, Points = 5, Scale = new Vector2(0.2f, 0.2f), Modulate = new Color(1, 1, 1, 0) };
        stage.AddChild(outer);
        stage.AddChild(inner);

        // ── 3. the card ──
        float ch = H * 0.5f, cw = ch * 350f / 512f;
        var card = new Control { Size = new Vector2(cw, ch), PivotOffset = new Vector2(cw / 2f, ch / 2f), MouseFilter = MouseFilterEnum.Ignore };
        var art = ArtFor(e.SourceDefId);
        if (art != null)
            card.AddChild(new TextureRect { Texture = art, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Size = card.Size, MouseFilter = MouseFilterEnum.Ignore });
        else
        {
            card.AddChild(new ColorRect { Color = new Color(0.08f, 0.07f, 0.1f, 0.96f), Size = card.Size, MouseFilter = MouseFilterEnum.Ignore });
            card.AddChild(new ReferenceRect { EditorOnly = false, BorderColor = color, BorderWidth = 4f * _s, Size = card.Size, MouseFilter = MouseFilterEnum.Ignore });
            var nm = MakeLabel(name, hot, 34, header: true);
            nm.AutowrapMode = TextServer.AutowrapMode.Word;
            nm.Size = new Vector2(cw - 20f * _s, ch); nm.Position = new Vector2(10f * _s, 0);
            card.AddChild(nm);
        }
        var cardFlash = new ColorRect { Color = Colors.White, Size = card.Size, Modulate = new Color(1, 1, 1, 0), MouseFilter = MouseFilterEnum.Ignore };
        card.AddChild(cardFlash);
        var from = ToLocal(CastOrigin(view));
        card.Position = from - card.Size / 2f;
        card.Scale = new Vector2(0.22f, 0.22f);
        card.Rotation = view == 0 ? -0.35f : 0.35f;
        card.Modulate = new Color(1, 1, 1, 0);
        root.AddChild(card);

        // ── 4. the words ──
        var kicker = MakeLabel(view == 0 ? "YOU CAST" : "ENEMY CASTS", new Color(0.95f, 0.9f, 0.8f), 34, header: true);
        var title = MakeLabel(name.ToUpperInvariant(), color.Lerp(Colors.White, 0.7f), 72, header: true);
        title.AddThemeConstantOverride("outline_size", Mathf.Max(6, (int)(12 * _s)));
        title.AddThemeColorOverride("font_outline_color", color.Darkened(0.75f));
        kicker.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.95f));
        root.AddChild(kicker); root.AddChild(title);
        Fit(kicker); Fit(title);
        kicker.Position = new Vector2(c.X - kicker.Size.X / 2f, c.Y - ch / 2f - kicker.Size.Y - 14f * _s);
        title.Position = new Vector2(c.X - title.Size.X / 2f, c.Y + ch / 2f + 8f * _s);
        kicker.Modulate = new Color(kicker.Modulate, 0f);
        title.Modulate = new Color(title.Modulate, 0f);
        var glyphRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        glyphRow.AddThemeConstantOverride("separation", (int)(18 * _s));
        var glyphs = new List<Control>();
        foreach (var g in OpGlyphs(def))
        {
            var t = Glyph(g);
            if (t == null) continue;
            var gi = new TextureRect { Texture = t, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(54f * _s, 54f * _s), Modulate = new Color(hot, 0f), MouseFilter = MouseFilterEnum.Ignore };
            glyphRow.AddChild(gi);
            glyphs.Add(gi);
        }
        root.AddChild(glyphRow);
        Fit(glyphRow);
        glyphRow.Position = new Vector2(c.X - glyphRow.Size.X / 2f, title.Position.Y + title.Size.Y + 4f * _s);

        // ── timeline ──
        var open = root.CreateTween().SetParallel();
        open.TweenProperty(dim, "modulate:a", 0.68f, 0.22f);
        open.TweenProperty(vig, "modulate:a", 0.42f, 0.3f);
        open.TweenProperty(glow, "modulate:a", 1f, 0.35f);
        open.TweenProperty(rays, "modulate:a", 1f, 0.35f);
        foreach (var circle in new Control[] { outer, inner })
        {
            open.TweenProperty(circle, "modulate:a", 1f, 0.3f);
            open.TweenProperty(circle, "scale", Vector2.One, 0.45f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        }
        open.TweenProperty(card, "position", c - card.Size / 2f, 0.32f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        open.TweenProperty(card, "scale", new Vector2(1.08f, 1.08f), 0.32f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        open.TweenProperty(card, "rotation", 0f, 0.32f).SetEase(Tween.EaseType.Out);
        open.TweenProperty(card, "modulate:a", 1f, 0.12f);

        // the circles and rays keep turning for the whole cast
        var spin = root.CreateTween().SetParallel();
        spin.TweenProperty(outer, "rotation", Mathf.Pi * 0.6f, 2.6f);
        spin.TweenProperty(inner, "rotation", -Mathf.Pi * 0.9f, 2.6f);
        spin.TweenProperty(rays, "rotation", Mathf.Pi * 0.25f, 2.6f);
        var pulse = root.CreateTween().SetLoops(4);
        glow.PivotOffset = glow.Size / 2f;
        pulse.TweenProperty(glow, "scale", new Vector2(1.12f, 1.12f), 0.3f).SetTrans(Tween.TransitionType.Sine);
        pulse.TweenProperty(glow, "scale", Vector2.One, 0.3f).SetTrans(Tween.TransitionType.Sine);

        At(root, 0.32f, () =>
        {
            // SLAM
            var settle = card.CreateTween();
            settle.TweenProperty(card, "scale", Vector2.One, 0.16f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
            cardFlash.Modulate = new Color(1, 1, 1, 0.55f);
            card.CreateTween().TweenProperty(cardFlash, "modulate:a", 0f, 0.2f);
            ScreenFlash(root, Colors.White, 0.22f, 0.2f);
            Shockwave(root, c, hot, R * 0.2f, H * 0.8f, 0.6f, 12f * _s);
            Shockwave(root, c, color, R * 0.1f, H * 0.55f, 0.75f, 6f * _s, delay: 0.08f);
            Burst(stage, Vector2.Zero, hot, 120, 1100f * _s, 0.9f, gravity: 240f * _s, scale: 0.55f);
            Embers(stage, Vector2.Zero, color, R * 0.8f, 1.6f);
            Shake(16f, 0.35f);
            Sfx?.Invoke("hit_heavy");

            PopIn(kicker, 0f, 1.25f);
            PopIn(title, 0.04f, 1.6f);
            for (int i = 0; i < glyphs.Count; i++) PopIn(glyphs[i], 0.14f + i * 0.07f, 1.8f);
        });

        float tLaunch = 1.25f;
        float total;
        if (countered)
        {
            At(root, 0.72f, () => CounterSlam(root, card, c, cw, ch, art));
            total = 1.9f;
            At(root, 1.35f, () => FadeOut(new Control[] { kicker, title, glyphRow }, 0.25f));
            Lift(root, dim, vig, glow, rays, outer, inner, 1.45f, 0.5f);
        }
        else
        {
            At(root, tLaunch, () =>
            {
                FadeOut(new Control[] { kicker, title, glyphRow }, 0.22f);
                // the card collapses into an orb of light
                var col = card.CreateTween().SetParallel();
                col.TweenProperty(card, "scale", new Vector2(0.1f, 0.1f), 0.2f).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Back);
                col.TweenProperty(card, "modulate", new Color(2.2f, 2.2f, 2.2f, 0f), 0.2f);
                var orb = Sprite(DotTex, new Vector2(H * 0.16f, H * 0.16f), hot, additive: true);
                orb.Position = c - orb.Size / 2f; orb.Scale = new Vector2(0.2f, 0.2f);
                root.AddChild(orb);
                var ot = orb.CreateTween();
                ot.TweenProperty(orb, "scale", new Vector2(1.4f, 1.4f), 0.18f).SetEase(Tween.EaseType.Out);
                ot.TweenProperty(orb, "modulate:a", 0f, 0.25f);
            });
            float tFly = tLaunch + 0.18f;
            if (hits.Count == 0)
            {
                At(root, tFly, () =>
                {
                    Impact(root, c, School.Arcane, hot, view, big: true);
                });
                total = tFly + 0.45f;
            }
            else
            {
                for (int i = 0; i < hits.Count; i++)
                {
                    var h = hits[i];
                    float delay = tFly + i * 0.07f;
                    At(root, delay, () => Bolt(root, c, h.At, SchoolColor(h.School, color), 0.3f, () => Impact(root, h.At, h.School, SchoolColor(h.School, color), h.TargetView, big: false)));
                }
                total = tFly + 0.3f + (hits.Count - 1) * 0.07f + 0.18f;
            }
            Lift(root, dim, vig, glow, rays, outer, inner, tLaunch + 0.12f, 0.55f);
        }
        At(root, total + 1.6f, root.QueueFree);
        return total;
    }

    /// <summary>The events this ritual causes, read ahead in the queue: one impact per target, strongest school wins.</summary>
    private List<Hit> SpellTargets(CombatEvent cast)
    {
        var map = new Dictionary<(int, int), Hit>();
        var order = new List<(int, int)>();
        int seen = 0;
        foreach (var ev in _queue)
        {
            if (++seen > 60) break;
            if (ev.Kind is FxKind.Cast or FxKind.Countered or FxKind.Attack) break;
            if (ev.Kind == FxKind.Summon && ev.Tag == "PLAY") break;
            bool mine = cast.SourceInstanceId >= 0 ? ev.SourceInstanceId == cast.SourceInstanceId : ev.SourceDefId == cast.SourceDefId;
            if (!mine || ev.TargetSide < 0) continue;
            var school = SchoolOf(ev);
            if (school is null) continue;
            var at = TargetPoint(ev);
            if (at is null) continue;
            var key = (ev.TargetSide, ev.TargetLane);
            var hit = new Hit(ToLocal(at.Value), school.Value, View(ev.TargetSide));
            if (map.TryGetValue(key, out var had)) { if (school.Value > had.School) map[key] = hit; }
            else { map[key] = hit; order.Add(key); }
        }
        return order.Select(k => map[k]).ToList();
    }

    private static School? SchoolOf(CombatEvent ev) => ev.Kind switch
    {
        FxKind.Damage => School.Fire,
        FxKind.Heal => School.Heal,
        FxKind.Buff => School.Buff,
        FxKind.Debuff => School.Curse,
        FxKind.Summon => School.Summon,
        FxKind.Effect => ev.Tag switch
        {
            "DESTROY" or "STEAL" => School.Doom,
            "BURN" => School.Fire,
            "STUN" or "LOCK_LANE" or "SILENCE" or "SUPPRESS" => School.Bind,
            "DRAIN" or "LOSE_VIGOR" or "REMOVE_KEY" => School.Curse,
            "HEAL_FULL" or "GAIN_VIGOR" => School.Heal,
            "DOUBLE_STATS" or "FORGE" or "GRANT_KEY" or "REFRESH" or "ATTUNE" or "ADD_CHARGE" => School.Buff,
            "DRAW" or "EXCAVATE" or "BURY" or "UNBURY" or "DISCARD" or "SCY" or "BOUNCE" => School.Cards,
            "REVIVE_TOKEN" or "UNEARTH_FROM_GRAVEYARD" or "SUMMON" => School.Summon,
            "SET_TRAP" => null,       // a Sigil is face down — don't point at it
            _ => School.Arcane,
        },
        _ => null,
    };

    private static Color SchoolColor(School s, Color spell) => s switch
    {
        School.Fire => spell.Lerp(new Color(1f, 0.45f, 0.15f), 0.55f),
        School.Heal => new Color(0.45f, 1f, 0.55f),
        School.Buff => new Color(1f, 0.85f, 0.4f),
        School.Curse => new Color(0.7f, 0.4f, 1f),
        School.Doom => new Color(0.75f, 0.25f, 0.9f),
        School.Bind => new Color(0.95f, 0.9f, 0.6f),
        School.Cards => new Color(0.5f, 0.8f, 1f),
        School.Summon => spell.Lerp(Colors.White, 0.3f),
        _ => spell.Lerp(Colors.White, 0.35f),
    };

    // ── countered ────────────────────────────────────────────────────────

    private void CounterSlam(Control root, Control card, Vector2 c, float cw, float ch, Texture2D? art)
    {
        if (!IsInstanceValid(card)) return;
        var tide = Tide;
        // the Sigil slams onto the card
        var sigil = Sprite(Glyph("sigil") ?? DotTex, new Vector2(ch * 0.7f, ch * 0.7f), new Color(tide.Lerp(Colors.White, 0.3f), 0f), additive: false);
        sigil.Position = c - sigil.Size / 2f; sigil.Scale = new Vector2(3f, 3f);
        root.AddChild(sigil);
        var st = sigil.CreateTween().SetParallel();
        st.TweenProperty(sigil, "scale", Vector2.One, 0.18f).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Quad);
        st.TweenProperty(sigil, "modulate:a", 0.95f, 0.12f);
        At(root, 0.18f, () =>
        {
            Shake(20f, 0.4f);
            Sfx?.Invoke("hit_heavy");
            ScreenFlash(root, new Color(1f, 0.25f, 0.2f), 0.35f, 0.3f);
            Shockwave(root, c, tide, ch * 0.3f, ch * 1.4f, 0.45f, 10f * _s);
            var stamp = MakeLabel("COUNTERED", new Color(1f, 0.32f, 0.28f), 84, header: true);
            stamp.AddThemeConstantOverride("outline_size", Mathf.Max(5, (int)(10 * _s)));
            root.AddChild(stamp); Fit(stamp);
            stamp.Position = c - stamp.Size / 2f; stamp.Rotation = -0.18f; stamp.Scale = new Vector2(2.6f, 2.6f);
            var tw = stamp.CreateTween();
            tw.TweenProperty(stamp, "scale", Vector2.One, 0.14f).SetEase(Tween.EaseType.In);
            tw.TweenInterval(0.75f);
            tw.TweenProperty(stamp, "modulate:a", 0f, 0.3f);
            card.CreateTween().TweenProperty(card, "modulate", new Color(0.45f, 0.45f, 0.5f, 1f), 0.25f);
            var sf = sigil.CreateTween();
            sf.TweenInterval(0.25f);
            sf.TweenProperty(sigil, "modulate:a", 0f, 0.35f);
        });
        // and the card shatters
        At(root, 0.55f, () => Shatter(root, card, art, cw, ch));
    }

    private void Shatter(Control root, Control card, Texture2D? art, float cw, float ch)
    {
        if (!IsInstanceValid(card)) return;
        var origin = card.Position;
        card.Visible = false;
        const int Cols = 3, Rows = 4;
        Vector2 piece = new(cw / Cols, ch / Rows);
        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
            {
                Control shard;
                if (art != null)
                {
                    var ts = art.GetSize();
                    var region = new Rect2(ts.X * x / Cols, ts.Y * y / Rows, ts.X / Cols, ts.Y / Rows);
                    shard = new TextureRect { Texture = new AtlasTexture { Atlas = art, Region = region }, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, Size = piece };
                }
                else shard = new ColorRect { Color = new Color(0.12f, 0.1f, 0.14f), Size = piece };
                shard.MouseFilter = MouseFilterEnum.Ignore;
                shard.Modulate = new Color(0.55f, 0.55f, 0.6f, 1f);
                shard.Position = origin + new Vector2(piece.X * x, piece.Y * y);
                shard.PivotOffset = piece / 2f;
                root.AddChild(shard);
                var dir = (shard.Position + piece / 2f - (origin + new Vector2(cw, ch) / 2f)).Normalized();
                var tw = shard.CreateTween().SetParallel();
                tw.TweenProperty(shard, "position", shard.Position + dir * _rng.RandfRange(160f, 320f) * _s + new Vector2(0, _rng.RandfRange(60f, 200f) * _s), 0.7f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
                tw.TweenProperty(shard, "rotation", _rng.RandfRange(-2.2f, 2.2f), 0.7f);
                tw.TweenProperty(shard, "modulate:a", 0f, 0.6f).SetDelay(0.15f);
            }
        Burst(root, origin + new Vector2(cw, ch) / 2f, new Color(0.6f, 0.6f, 0.7f), 50, 600f * _s, 0.7f, gravity: 500f * _s, scale: 0.4f);
    }

    // ── bolts and impacts ────────────────────────────────────────────────

    private void Bolt(Control root, Vector2 a, Vector2 b, Color color, float dur, Action onArrive)
    {
        var orb = Sprite(DotTex, new Vector2(70f * _s, 70f * _s), color.Lerp(Colors.White, 0.3f), additive: true);
        root.AddChild(orb);
        var trail = new Line2D { Width = 22f * _s, DefaultColor = color, Antialiased = true, BeginCapMode = Line2D.LineCapMode.Round, EndCapMode = Line2D.LineCapMode.Round, Material = Additive };
        trail.WidthCurve = new Curve();
        trail.WidthCurve.AddPoint(new Vector2(0, 0));
        trail.WidthCurve.AddPoint(new Vector2(1, 1));
        trail.Gradient = new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { new Color(color, 0f), new Color(color.Lerp(Colors.White, 0.4f), 0.95f) } };
        root.AddChild(trail);
        // arc up and away from the board's middle, a different bend for each target
        var mid = (a + b) / 2f + new Vector2((b.X - a.X) * 0.15f, -Mathf.Clamp((b - a).Length() * 0.45f, 80f * _s, 320f * _s));
        var points = new List<Vector2>();
        var tw = root.CreateTween();
        tw.TweenMethod(Callable.From<float>(t =>
        {
            if (!IsInstanceValid(orb)) return;
            float u = t;
            var p = (1 - u) * (1 - u) * a + 2 * (1 - u) * u * mid + u * u * b;
            orb.Position = p - orb.Size / 2f;
            points.Add(p);
            if (points.Count > 14) points.RemoveAt(0);
            trail.Points = points.ToArray();
        }), 0f, 1f, dur).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Quad);
        tw.TweenCallback(Callable.From(() =>
        {
            if (IsInstanceValid(orb)) orb.QueueFree();
            if (IsInstanceValid(trail))
            {
                var tf = trail.CreateTween();
                tf.TweenProperty(trail, "modulate:a", 0f, 0.15f);
                tf.TweenCallback(Callable.From(trail.QueueFree));
            }
            onArrive();
        }));
    }

    private void Impact(Control root, Vector2 at, School school, Color color, int targetView, bool big)
    {
        float k = big ? 1.6f : 1f;
        float H = GetViewportRect().Size.Y;
        switch (school)
        {
            case School.Fire:
                Flare(root, at, color, H * 0.3f * k, 0.3f);
                Shockwave(root, at, color.Lerp(Colors.White, 0.3f), 10f * _s, H * 0.2f * k, 0.4f, 10f * _s);
                Burst(root, at, color, (int)(70 * k), 750f * _s * k, 0.65f, gravity: 500f * _s, scale: 0.5f);
                Burst(root, at, new Color(1f, 0.9f, 0.6f), (int)(25 * k), 400f * _s, 0.4f, scale: 0.35f);
                Shake(12f * k, 0.28f);
                Sfx?.Invoke("hit_heavy");
                break;
            case School.Heal:
                Flare(root, at, color, H * 0.24f * k, 0.45f);
                Column(root, at, color, k);
                Shockwave(root, at, color, 10f * _s, H * 0.14f * k, 0.5f, 5f * _s);
                break;
            case School.Buff:
                Flare(root, at, color, H * 0.22f * k, 0.4f);
                UpRays(root, at, color, k);
                Burst(root, at, color, (int)(40 * k), 380f * _s, 0.8f, gravity: -380f * _s, scale: 0.35f, spread: 40f);
                break;
            case School.Curse:
                Implode(root, at, color, H * 0.18f * k, 0.35f);
                Burst(root, at, color, (int)(45 * k), 260f * _s, 0.9f, gravity: 480f * _s, scale: 0.4f);
                Shake(6f, 0.2f);
                break;
            case School.Doom:
                Implode(root, at, color, H * 0.26f * k, 0.3f);
                At(root, 0.3f, () =>
                {
                    Flare(root, at, new Color(0.2f, 0.05f, 0.25f), H * 0.3f * k, 0.4f, additive: false);
                    Burst(root, at, color, (int)(80 * k), 820f * _s, 0.7f, gravity: 200f * _s, scale: 0.5f);
                    Stamp(root, at, "skull", Colors.White, 90f * _s * k);
                    Shake(18f * k, 0.35f);
                    Sfx?.Invoke("hit_heavy");
                });
                break;
            case School.Bind:
                Shockwave(root, at, color, H * 0.18f * k, 14f * _s, 0.3f, 8f * _s);
                Stamp(root, at, "lock", color, 80f * _s * k);
                Shake(8f, 0.18f);
                Sfx?.Invoke("hit_light");
                break;
            case School.Cards:
                Flare(root, at, color, H * 0.18f * k, 0.35f);
                FlyCards(root, at, color, targetView);
                break;
            case School.Summon:
                Shockwave(root, at, color, 10f * _s, H * 0.18f * k, 0.5f, 6f * _s);
                Stamp(root, at, "rune", color, 90f * _s * k);
                Burst(root, at, color, (int)(40 * k), 420f * _s, 0.8f, gravity: -200f * _s, scale: 0.35f);
                break;
            default:
                Flare(root, at, color, H * 0.26f * k, 0.4f);
                Shockwave(root, at, color, 10f * _s, H * 0.22f * k, 0.5f, 8f * _s);
                Burst(root, at, color, (int)(60 * k), 600f * _s * k, 0.7f, scale: 0.45f);
                Shake(8f * k, 0.22f);
                break;
        }
    }

    private void Column(Control root, Vector2 at, Color color, float k)
    {
        var p = MakeParticles(color, (int)(60 * k), 1.1f, 0.35f);
        p.Position = at;
        p.EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle;
        p.EmissionRectExtents = new Vector2(60f * _s * k, 20f * _s);
        p.Direction = Vector2.Up; p.Spread = 12f;
        p.InitialVelocityMin = 160f * _s; p.InitialVelocityMax = 360f * _s;
        p.Gravity = new Vector2(0, -140f * _s);
        root.AddChild(p);
        p.Emitting = true;
        FreeLater(p, 1.8f);
    }

    private void UpRays(Control root, Vector2 at, Color color, float k)
    {
        float H = GetViewportRect().Size.Y;
        for (int i = -1; i <= 1; i++)
        {
            float len = H * 0.32f * k, w = 46f * _s;
            var ray = new TextureRect
            {
                Texture = RayTex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
                Size = new Vector2(w, len), Position = at + new Vector2(-w / 2f, -len), PivotOffset = new Vector2(w / 2f, len),
                Rotation = i * 0.32f, Scale = new Vector2(1, 0), Modulate = new Color(color, 0.9f), Material = Additive, MouseFilter = MouseFilterEnum.Ignore,
            };
            root.AddChild(ray);
            var tw = ray.CreateTween();
            tw.TweenProperty(ray, "scale", Vector2.One, 0.22f).SetEase(Tween.EaseType.Out).SetDelay(Mathf.Abs(i) * 0.05f);
            tw.TweenProperty(ray, "modulate:a", 0f, 0.4f);
            tw.TweenCallback(Callable.From(ray.QueueFree));
        }
        Stamp(root, at, "up", color, 70f * _s * k);
    }

    private void FlyCards(Control root, Vector2 at, Color color, int targetView)
    {
        var dest = ToLocal(CastOrigin(targetView));
        for (int i = 0; i < 3; i++)
        {
            var g = Sprite(Glyph("card") ?? DotTex, new Vector2(64f * _s, 64f * _s), color, additive: false);
            g.Position = at - g.Size / 2f; g.Scale = new Vector2(0.3f, 0.3f);
            root.AddChild(g);
            var tw = g.CreateTween();
            tw.TweenInterval(i * 0.08f);
            tw.TweenProperty(g, "scale", new Vector2(1.2f, 1.2f), 0.14f).SetEase(Tween.EaseType.Out);
            tw.SetParallel();
            tw.TweenProperty(g, "position", dest - g.Size / 2f + new Vector2((i - 1) * 40f * _s, 0), 0.4f).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Cubic);
            tw.TweenProperty(g, "rotation", (i - 1) * 0.6f, 0.4f);
            tw.TweenProperty(g, "modulate:a", 0f, 0.15f).SetDelay(0.3f);
            tw.SetParallel(false);
            tw.TweenCallback(Callable.From(g.QueueFree));
        }
    }

    // ── primitives (all parented to the cast's root, so they go with it) ──

    private TextureRect Sprite(Texture2D tex, Vector2 size, Color modulate, bool additive)
    {
        var t = new TextureRect
        {
            Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Size = size, Position = -size / 2f, PivotOffset = size / 2f, Modulate = modulate, MouseFilter = MouseFilterEnum.Ignore,
        };
        if (additive) t.Material = Additive;
        return t;
    }

    private void Flare(Control root, Vector2 at, Color color, float size, float dur, bool additive = true)
    {
        var f = Sprite(additive ? DotTex : GlowTex, new Vector2(size, size), new Color(color, additive ? 1f : 0.9f), additive);
        f.Position = at - f.Size / 2f; f.Scale = new Vector2(0.3f, 0.3f);
        root.AddChild(f);
        var tw = f.CreateTween().SetParallel();
        tw.TweenProperty(f, "scale", Vector2.One, dur * 0.4f).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(f, "modulate:a", 0f, dur).SetEase(Tween.EaseType.In);
        tw.SetParallel(false);
        tw.TweenCallback(Callable.From(f.QueueFree));
    }

    private void Implode(Control root, Vector2 at, Color color, float r0, float dur)
    {
        var ring = new FxRing { Color = color, Radius = r0, Width = 10f * _s, Position = at, Modulate = new Color(1, 1, 1, 0) };
        root.AddChild(ring);
        var tw = ring.CreateTween().SetParallel();
        tw.TweenProperty(ring, "modulate:a", 1f, dur * 0.3f);
        tw.TweenMethod(Callable.From<float>(r => { if (IsInstanceValid(ring)) ring.Radius = r; }), r0, 4f * _s, dur).SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Cubic);
        tw.SetParallel(false);
        tw.TweenCallback(Callable.From(ring.QueueFree));
        var inner = Sprite(GlowTex, new Vector2(r0 * 2f, r0 * 2f), new Color(color, 0f), additive: true);
        inner.Position = at - inner.Size / 2f;
        root.AddChild(inner);
        var t2 = inner.CreateTween().SetParallel();
        t2.TweenProperty(inner, "modulate:a", 0.8f, dur * 0.5f);
        t2.TweenProperty(inner, "scale", new Vector2(0.15f, 0.15f), dur).SetEase(Tween.EaseType.In);
        t2.SetParallel(false);
        t2.TweenCallback(Callable.From(inner.QueueFree));
    }

    private void Stamp(Control root, Vector2 at, string glyph, Color color, float size)
    {
        var tex = Glyph(glyph);
        if (tex == null) return;
        var s = Sprite(tex, new Vector2(size, size), new Color(color, 0f), additive: false);
        s.Position = at - s.Size / 2f; s.Scale = new Vector2(2.4f, 2.4f);
        root.AddChild(s);
        var tw = s.CreateTween();
        tw.SetParallel();
        tw.TweenProperty(s, "scale", Vector2.One, 0.16f).SetEase(Tween.EaseType.In);
        tw.TweenProperty(s, "modulate:a", 1f, 0.1f);
        tw.SetParallel(false);
        tw.TweenInterval(0.45f);
        tw.SetParallel();
        tw.TweenProperty(s, "modulate:a", 0f, 0.3f);
        tw.TweenProperty(s, "position", s.Position + new Vector2(0, -40f * _s), 0.3f);
        tw.SetParallel(false);
        tw.TweenCallback(Callable.From(s.QueueFree));
    }

    private void Shockwave(Control root, Vector2 at, Color color, float r0, float r1, float dur, float width, float delay = 0f)
    {
        var ring = new FxRing { Color = color, Radius = r0, Width = width, Position = at };
        if (delay > 0) ring.Modulate = new Color(1, 1, 1, 0);
        root.AddChild(ring);
        var tw = ring.CreateTween();
        if (delay > 0) { tw.TweenInterval(delay); tw.TweenProperty(ring, "modulate:a", 1f, 0.01f); }
        tw.SetParallel();
        tw.TweenMethod(Callable.From<float>(r => { if (IsInstanceValid(ring)) ring.Radius = r; }), r0, r1, dur).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tw.TweenMethod(Callable.From<float>(w => { if (IsInstanceValid(ring)) ring.Width = w; }), width, width * 0.2f, dur);
        tw.TweenProperty(ring, "modulate:a", 0f, dur).SetEase(Tween.EaseType.In);
        tw.SetParallel(false);
        tw.TweenCallback(Callable.From(ring.QueueFree));
    }

    private void ScreenFlash(Control root, Color color, float alpha, float dur)
    {
        var f = new ColorRect { Color = color, Modulate = new Color(1, 1, 1, alpha), MouseFilter = MouseFilterEnum.Ignore };
        FullScreen(f);
        root.AddChild(f);
        var tw = f.CreateTween();
        tw.TweenProperty(f, "modulate:a", 0f, dur).SetEase(Tween.EaseType.Out);
        tw.TweenCallback(Callable.From(f.QueueFree));
    }

    private CpuParticles2D MakeParticles(Color color, int amount, float life, float scale)
    {
        var bright = color.Lerp(Colors.White, 0.35f);
        return new CpuParticles2D
        {
            Emitting = false, OneShot = true, Amount = Math.Max(1, amount), Lifetime = life, Explosiveness = 0.9f,
            Texture = DotTex, Material = Additive, LocalCoords = false,
            ScaleAmountMin = scale * 0.35f * _s * 1.6f, ScaleAmountMax = scale * _s * 1.6f,
            ColorRamp = new Gradient { Offsets = new[] { 0f, 0.6f, 1f }, Colors = new[] { bright, new Color(color, 0.8f), new Color(color, 0f) } },
            DampingMin = 20f, DampingMax = 60f,
        };
    }

    private void Burst(Control root, Vector2 at, Color color, int amount, float speed, float life, float gravity = 0f, float scale = 0.45f, float spread = 180f)
    {
        var p = MakeParticles(color, amount, life, scale);
        p.Position = at;
        p.Direction = Vector2.Up; p.Spread = spread;
        p.InitialVelocityMin = speed * 0.35f; p.InitialVelocityMax = speed;
        p.Gravity = new Vector2(0, gravity);
        root.AddChild(p);
        p.Emitting = true;
        FreeLater(p, life + 0.6f);
    }

    private void Embers(Control root, Vector2 at, Color color, float radius, float dur)
    {
        var p = MakeParticles(color, 50, 1.3f, 0.3f);
        p.OneShot = false; p.Explosiveness = 0f;
        p.Position = at;
        p.EmissionShape = CpuParticles2D.EmissionShapeEnum.Sphere;
        p.EmissionSphereRadius = radius;
        p.Direction = Vector2.Up; p.Spread = 30f;
        p.InitialVelocityMin = 40f * _s; p.InitialVelocityMax = 140f * _s;
        p.Gravity = new Vector2(0, -90f * _s);
        root.AddChild(p);
        p.Emitting = true;
        At(root, dur, () => { if (IsInstanceValid(p)) p.Emitting = false; });
        FreeLater(p, dur + 1.5f);
    }

    private void PopIn(Control c, float delay, float from)
    {
        c.PivotOffset = c.Size / 2f;
        c.Scale = new Vector2(from, from);
        var tw = c.CreateTween();
        if (delay > 0) tw.TweenInterval(delay);
        tw.SetParallel();
        tw.TweenProperty(c, "scale", Vector2.One, 0.2f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        tw.TweenProperty(c, "modulate:a", 1f, 0.14f);
    }

    private static void FadeOut(IEnumerable<Control> cs, float dur)
    {
        foreach (var c in cs)
            if (IsInstanceValid(c)) c.CreateTween().TweenProperty(c, "modulate:a", 0f, dur);
    }

    /// <summary>The darkness lifts: dim, vignette, glow, rays and circles fade together.</summary>
    private void Lift(Control root, ColorRect dim, Control vig, Control glow, Control rays, Control outer, Control inner, float at, float dur)
    {
        At(root, at, () =>
        {
            var tw = root.CreateTween().SetParallel();
            tw.TweenProperty(dim, "modulate:a", 0f, dur);
            foreach (var c in new[] { vig, glow, rays, outer, inner })
                if (IsInstanceValid(c)) tw.TweenProperty(c, "modulate:a", 0f, dur);
            foreach (var c in new[] { outer, inner })
                if (IsInstanceValid(c)) tw.TweenProperty(c, "scale", new Vector2(1.3f, 1.3f), dur).SetEase(Tween.EaseType.Out);
        });
    }

    private void At(Node owner, float t, Action a)
    {
        var tw = owner.CreateTween();
        tw.TweenInterval(Math.Max(0.001f, t));
        tw.TweenCallback(Callable.From(a));
    }

    private void FreeLater(Node n, float t)
    {
        var tw = n.CreateTween();
        tw.TweenInterval(t);
        tw.TweenCallback(Callable.From(n.QueueFree));
    }

    /// <summary>Shake the duel (not the effects): strength in px at 1080p, fading out over the duration.</summary>
    private void Shake(float strength, float dur)
    {
        if (CampaignContext.ReduceMotion) return;
        var t = ShakeTarget?.Invoke();
        if (t == null || !IsInstanceValid(t)) return;
        if (_shakeTween != null && _shakeTween.IsValid()) _shakeTween.Kill();
        if (_shakeHome is Vector2 h0) t.Position = h0;
        var home = t.Position;
        _shakeHome = home;
        _shakeTween = CreateTween();
        _shakeTween.TweenMethod(Callable.From<float>(k =>
        {
            if (!IsInstanceValid(t)) return;
            float a = strength * _s * (1f - k);
            t.Position = home + new Vector2(_rng.RandfRange(-a, a), _rng.RandfRange(-a, a));
        }), 0f, 1f, dur);
        _shakeTween.TweenCallback(Callable.From(() =>
        {
            if (IsInstanceValid(t)) t.Position = home;
            _shakeHome = null;
        }));
    }
}

/// <summary>An arcane circle: two rings, tick marks and a star polygon (5 = pentagram, 6 = hexagram).</summary>
public partial class FxRuneCircle : Control
{
    public float Radius = 100f;
    public float Width = 3f;
    public Color Color = Colors.White;
    public int Points = 6;

    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        float r = Radius, ri = Radius * 0.86f;
        DrawArc(Vector2.Zero, r, 0, Mathf.Tau, 96, Color, Width, true);
        DrawArc(Vector2.Zero, ri, 0, Mathf.Tau, 96, new Color(Color, Color.A * 0.7f), Width * 0.6f, true);
        for (int i = 0; i < 36; i++)
        {
            float a = i * Mathf.Tau / 36f;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            float inner = i % 3 == 0 ? ri * 0.94f : ri;
            DrawLine(dir * inner, dir * r, new Color(Color, Color.A * 0.8f), Width * 0.5f, true);
        }
        int n = Math.Max(3, Points), k = 2;
        var v = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = -Mathf.Pi / 2f + i * Mathf.Tau / n;
            v[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ri * 0.97f;
        }
        for (int i = 0; i < n; i++)
            DrawLine(v[i], v[(i + k) % n], Color, Width * 0.8f, true);
        foreach (var p in v)
            DrawArc(p, Width * 3.2f, 0, Mathf.Tau, 16, Color, Width * 0.6f, true);
    }
}

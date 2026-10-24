using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// FABLE-064: plays the engine's combat events (<see cref="GameState.Events"/>) as animations, in order, with a
/// little air between them, so every hit, spell, relic trigger and keyword is shown and named. Sits over the
/// duel as a full-screen layer that ignores input. Everything is drawn with controls and tweens; the only
/// assets are the small glyphs in <c>assets/fx</c> (the same ones the Symbol Key screen shows).
///
///   Damage   → floating −N in the source's stratum colour at the target (+ flare), face hits jolt the crest
///   Attack   → a strike beam from attacker to target and a scale punch
///   Death    → a rising skull over the lane, named by cause
///   Cast     → the ritual's card is shown large in the middle of the board with its effect glyphs
///   Trigger  → creature ability: a rune bloom over the lane · artifact: plate flash + arc to its target ·
///              relic: a full-screen veil that fades (opaque → clear) with the relic's name
///   Keyword  → a callout at the lane ("GUARD", "WARD", "DODGE" …) with the keyword's glyph
///   Buff/Debuff/Effect → short labels ("+1/+1", "STUNNED", "DRAW 2" …)
/// With Reduce Motion on, numbers and words still appear (briefly, no movement); beams, veils and showcases are skipped.
/// </summary>
public partial class CombatFx : Control
{
    // ── wiring from DuelScene ────────────────────────────────────────────
    public Func<int, int> View = v => v;                       // engine side → 0 = me (bottom), 1 = foe (top)
    public Func<int, int, Control?> SlotOf = (_, _) => null;    // (view, lane) → LaneSlot
    public Func<int, int, Control?> PlateOf = (_, _) => null;   // (view, artifact slot) → ArtifactCardPlate
    public Func<int, Control?> CrestOf = _ => null;             // view → the Vigor crest on the Crescent
    public Func<int, Vector2> CastOrigin = _ => Vector2.Zero;   // view → where that side's rituals fly in from
    public Action<int, int>? FaceHit;                           // (view, amount): the big face number + jolt + edge pulse
    public Action<string>? Sfx;
    public Action<string>? Log;

    /// <summary>Only one lane number per side per hit burst gets a sound.</summary>
    private double _lastDamageSfx, _lastClock;

    private readonly Queue<CombatEvent> _queue = new();
    private float _wait;
    private int _lastSeq;
    private float _s = 1f;   // UI scale (viewport height / 1080)
    private static readonly Dictionary<string, Texture2D?> _glyphs = new();

    /// <summary>True while anything is queued or the last beat is still playing (the bot waits for this).</summary>
    public bool Driving => _queue.Count > 0 || _wait > 0.05f;

    public override void _Ready()
    {
        // FABLE-065: explicit size — anchors under a CanvasLayer left this (and every full-screen child) at 0×0,
        // so the veil and the flashes never covered anything
        Position = Vector2.Zero;
        Size = GetViewportRect().Size;
        MouseFilter = MouseFilterEnum.Ignore;
        GetViewport().SizeChanged += () => { if (IsInstanceValid(this)) Size = GetViewportRect().Size; };
        _s = Mathf.Max(0.5f, GetViewportRect().Size.Y / 1080f);
    }

    /// <summary>Take the events a state change brought; the ones already shown are skipped.</summary>
    public int Ingest(GameState? state)
    {
        if (state == null) return 0;
        if (state.EventSeq < _lastSeq) _lastSeq = 0;   // a new game started
        int n = 0;
        foreach (var e in state.Events)
            if (e.Seq > _lastSeq) { _queue.Enqueue(e); _lastSeq = e.Seq; n++; }
        return n;
    }

    /// <summary>A scripted parade of every effect kind (for tuning: run the game with --fxdemo).</summary>
    public void Demo()
    {
        var q = new List<CombatEvent>();
        CombatEvent E(FxKind k, string tag, int side, string src, int srcLane, int tside, string tgt, int tlane, int n = 0, int n2 = 0, CardType st = CardType.CREATURE, int slot = -1) =>
            new() { Kind = k, Tag = tag, Side = side, SourceDefId = src, SourceLane = srcLane, SourceType = st, SourceSlot = slot, TargetSide = tside, TargetDefId = tgt, TargetLane = tlane, Amount = n, Amount2 = n2 };
        q.Add(E(FxKind.Cast, "", 0, "emb_r_inferno_burst", -1, -1, "", -1, st: CardType.RITUAL));
        q.Add(E(FxKind.Damage, "SPELL", 0, "emb_r_inferno_burst", -1, 1, "", -1, 3, st: CardType.RITUAL));
        q.Add(E(FxKind.Damage, "SPELL", 0, "emb_r_inferno_burst", -1, 1, "tid_c_tidal_scholar", 1, 3, st: CardType.RITUAL));
        q.Add(E(FxKind.Cast, "", 1, "hol_r_kingsbane", -1, -1, "", -1, st: CardType.RITUAL));
        q.Add(E(FxKind.Effect, "DESTROY", 1, "hol_r_kingsbane", -1, 0, "vrd_c_root_warden", 2, st: CardType.RITUAL));
        q.Add(E(FxKind.Death, "DESTROY", -1, "", -1, 0, "vrd_c_root_warden", 2));
        q.Add(E(FxKind.Cast, "", 0, "dwn_u_consecrate", -1, -1, "", -1, st: CardType.RITUAL));
        q.Add(E(FxKind.Heal, "", 0, "dwn_u_consecrate", -1, 0, "vrd_c_root_warden", 1, 2, st: CardType.RITUAL));
        q.Add(E(FxKind.Buff, "", 0, "dwn_u_consecrate", -1, 0, "vrd_c_root_warden", 3, 1, 1, st: CardType.RITUAL));
        q.Add(E(FxKind.Cast, "", 0, "tid_u_memory_tides", -1, -1, "", -1, st: CardType.RITUAL));
        q.Add(E(FxKind.Effect, "EXCAVATE", 0, "tid_u_memory_tides", -1, 0, "", -1, 2, st: CardType.RITUAL));
        q.Add(E(FxKind.Effect, "DISCARD", 0, "tid_u_memory_tides", -1, 1, "", -1, 1, st: CardType.RITUAL));
        q.Add(E(FxKind.Attack, "", 0, "vrd_c_root_warden", 1, 1, "tid_c_tidal_scholar", 1, 3));
        q.Add(E(FxKind.Keyword, "GUARD", 0, "vrd_c_root_warden", 1, 1, "vrd_c_verdant_sproutling", 0));
        q.Add(E(FxKind.Keyword, "WARD", 0, "vrd_c_root_warden", 1, 1, "vrd_c_verdant_sproutling", 0, 3));
        q.Add(E(FxKind.Attack, "", 0, "vrd_c_root_warden", 2, 1, "tid_c_tidal_scholar", 2, 3));
        q.Add(E(FxKind.Damage, "ATTACK", 0, "vrd_c_root_warden", 2, 1, "tid_c_tidal_scholar", 2, 3));
        q.Add(E(FxKind.Damage, "CLASH", 1, "tid_c_tidal_scholar", 2, 0, "vrd_c_root_warden", 2, 1));
        q.Add(E(FxKind.Keyword, "VENOM", 0, "vrd_c_root_warden", 2, 1, "tid_c_tidal_scholar", 2));
        q.Add(E(FxKind.Death, "VENOM", -1, "", -1, 1, "tid_c_tidal_scholar", 2));
        q.Add(E(FxKind.Trigger, "ON_DEATH", 1, "tid_c_tidal_scholar", 2, 1, "tid_c_tidal_scholar", 2));
        q.Add(E(FxKind.Effect, "DRAW", 1, "tid_c_tidal_scholar", 2, 1, "", -1, 1));
        q.Add(E(FxKind.Trigger, "ON_CREATURE_DIES", 0, "artf_paladin_aegis_of_sunspire", -1, 0, "", -1, st: CardType.ARTIFACT, slot: 1));
        q.Add(E(FxKind.Buff, "", 0, "artf_paladin_aegis_of_sunspire", -1, 0, "vrd_c_root_warden", 2, 1, 1, st: CardType.ARTIFACT, slot: 1));
        q.Add(E(FxKind.Attack, "", 1, "vrd_c_verdant_sproutling", 0, 0, "", -1, 2));
        q.Add(E(FxKind.Keyword, "SHIELD", 1, "vrd_c_verdant_sproutling", 0, 0, "", -1, 1));
        q.Add(E(FxKind.Damage, "ATTACK", 1, "vrd_c_verdant_sproutling", 0, 0, "", -1, 1));
        q.Add(E(FxKind.Keyword, "DODGE", 1, "tid_c_tidal_scholar", 3, 0, "vrd_c_root_warden", 3, 2));
        q.Add(E(FxKind.Keyword, "PIERCE", 0, "vrd_c_root_warden", 4, 1, "tid_c_tidal_scholar", 4, 2));
        q.Add(E(FxKind.Damage, "PIERCE", 0, "vrd_c_root_warden", 4, 1, "", -1, 2));
        q.Add(E(FxKind.Trigger, "ON_RELIC_IDENTIFY", 0, "tid_x_tidal_seal", 3, 0, "tid_x_tidal_seal", 3, st: CardType.RELIC));
        q.Add(E(FxKind.Heal, "", 0, "tid_x_tidal_seal", 3, 0, "", -1, 4, st: CardType.RELIC));
        q.Add(E(FxKind.Effect, "STUN", 1, "tid_u_brine_witch", 3, 0, "vrd_c_root_warden", 1, st: CardType.CREATURE));
        q.Add(E(FxKind.Damage, "BURN", -1, "", -1, 0, "vrd_c_root_warden", 1, 2));
        q.Add(E(FxKind.Countered, "tid_r_flood_of_secrets", 1, "tid_c_abyssal_gaze", -1, -1, "", -1, st: CardType.RITUAL));
        q.Add(E(FxKind.Sigil, "COUNTER_RITUAL", 0, "tid_r_flood_of_secrets", -1, 1, "", -1, st: CardType.RITUAL));
        foreach (var e in q) _queue.Enqueue(e);
    }

    /// <summary>The first render of a duel: nothing to replay.</summary>
    public void Catchup(GameState? state)
    {
        if (state != null) _lastSeq = state.EventSeq;
        _queue.Clear();
    }

    public override void _Process(double delta)
    {
        _lastClock += delta;
        if (_queue.Count == 0) { if (_wait > 0f) _wait = Mathf.Max(0f, _wait - (float)delta); return; }
        _wait -= (float)delta;
        int guard = 0;
        while (_wait <= 0f && _queue.Count > 0 && guard++ < 8)
        {
            var e = _queue.Dequeue();
            float gap;
            try { gap = Play(e); }
            catch (Exception ex) { GD.PrintErr($"[FX] {e}: {ex.Message}"); gap = 0f; }
            // a ritual is a set piece: it is never hurried (everything after it waits its turn)
            float hurry = e.Kind is FxKind.Cast or FxKind.Countered ? 1f : _queue.Count > 20 ? 0.25f : _queue.Count > 8 ? 0.5f : 1f;
            _wait += gap * hurry;
        }
    }

    // ── one event → one animation; returns how long to wait before the next ──

    private float Play(CombatEvent e)
    {
        Log?.Invoke(e.ToString());
        bool rm = CampaignContext.ReduceMotion;
        switch (e.Kind)
        {
            case FxKind.Attack: return PlayAttack(e, rm);
            case FxKind.Damage: return PlayDamage(e, rm);
            case FxKind.Heal: return PlayHeal(e, rm);
            case FxKind.Death: return PlayDeath(e, rm);
            case FxKind.Summon: return e.Tag == "TOKEN" ? Callout(TargetPoint(e), "SUMMONED", Gold, "rune", rm, 0.05f)
                                    : e.Tag == "PLAY" && View(e.Side) == 1 ? PlayEnemySummon(e, rm) : 0f;
            case FxKind.TurnStart: return PlayTurnBanner(e, rm);
            case FxKind.Cast: return PlayCast(e, false, rm);
            case FxKind.Countered: return PlayCast(e, true, rm);
            case FxKind.Trigger: return PlayTrigger(e, rm);
            case FxKind.Keyword: return PlayKeyword(e, rm);
            case FxKind.Buff: return Callout(TargetPoint(e), Stat(e.Amount, e.Amount2, true), Verdant, "up", rm, 0.08f);
            case FxKind.Debuff: return Callout(TargetPoint(e), Stat(-e.Amount, -e.Amount2, false), Hollow, "down", rm, 0.08f);
            case FxKind.Effect: return PlayEffect(e, rm);
            case FxKind.Sigil: return PlaySigil(e, rm);
        }
        return 0f;
    }

    private float PlayAttack(CombatEvent e, bool rm)
    {
        var from = SourcePoint(e);
        var to = TargetPoint(e);
        var src = SlotOf(View(e.Side), e.SourceLane);
        bool enemy = View(e.Side) == 1;
        if (rm) return 0.05f;
        // FABLE-067: a wind-up you can read — the attacker rears toward its target, then strikes
        if (src != null && from is Vector2 a0 && to is Vector2 b0)
        {
            var dir = (b0 - a0).Normalized();
            var home = src.Position;
            src.PivotOffset = src.Size / 2f;
            var tw = src.CreateTween();
            tw.TweenProperty(src, "scale", new Vector2(1.12f, 1.12f), enemy ? 0.16f : 0.1f).SetEase(Tween.EaseType.Out);
            tw.Parallel().TweenProperty(src, "position", home - dir * 10f * _s, enemy ? 0.16f : 0.1f);
            tw.TweenProperty(src, "position", home + dir * 26f * _s, 0.08f).SetEase(Tween.EaseType.In);
            tw.TweenProperty(src, "position", home, 0.14f).SetEase(Tween.EaseType.Out);
            tw.Parallel().TweenProperty(src, "scale", Vector2.One, 0.14f);
        }
        if (from is Vector2 a && to is Vector2 b)
        {
            float delay = enemy ? 0.16f : 0.1f;
            var tw = CreateTween();
            tw.TweenInterval(delay);
            tw.TweenCallback(Callable.From(() => { if (IsInsideTree()) Beam(a, b, StrataOf(e.SourceDefId), 8f * _s, 0.3f); }));
        }
        return enemy ? 0.36f : 0.22f;
    }

    /// <summary>FABLE-067: the enemy's creature is shown arriving — its card flies from the enemy to the lane.</summary>
    private float PlayEnemySummon(CombatEvent e, bool rm)
    {
        var slot = SlotOf(1, e.TargetLane);
        if (slot == null || rm) return 0.05f;
        var art = ArtFor(e.SourceDefId);
        if (art == null) return 0.05f;
        var dest = Center(slot);
        var size = slot.Size * slot.Scale;
        var card = new TextureRect { Texture = art, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Size = size, PivotOffset = size / 2f, MouseFilter = MouseFilterEnum.Ignore };
        var from = ToLocal(CastOrigin(1));
        card.Position = from - size / 2f;
        card.Scale = new Vector2(0.5f, 0.5f);
        card.Rotation = 0.4f;
        AddChild(card);
        slot.Modulate = new Color(1, 1, 1, 0);   // the board already shows it; reveal it when the card lands
        var tw = CreateTween().SetParallel();
        tw.TweenProperty(card, "position", ToLocal(dest) - size / 2f, 0.34f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tw.TweenProperty(card, "scale", new Vector2(1.18f, 1.18f), 0.34f).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(card, "rotation", 0f, 0.34f);
        tw.SetParallel(false);
        tw.TweenProperty(card, "scale", Vector2.One, 0.08f);
        tw.TweenCallback(Callable.From(() =>
        {
            if (IsInstanceValid(slot)) { slot.Modulate = Colors.White; if (slot is LaneSlot ls) ls.PlaySummonEffect(); }
            card.QueueFree();
            Sfx?.Invoke("card_play");
        }));
        return 0.5f;
    }

    /// <summary>FABLE-067: "ENEMY TURN" / "YOUR TURN" sweeps across the board, so the change of turn reads.</summary>
    private float PlayTurnBanner(CombatEvent e, bool rm)
    {
        bool mine = View(e.TargetSide) == 0;
        string text = mine ? "YOUR TURN" : "ENEMY TURN";
        var color = mine ? new Color(0.55f, 0.95f, 0.85f) : new Color(1f, 0.5f, 0.4f);
        var vs = GetViewportRect().Size;
        var c = ToLocal(BoardCenter());
        float h = 120f * _s;
        var band = new ColorRect { Color = new Color(0.02f, 0.015f, 0.02f, 0.78f), Size = new Vector2(vs.X, h), Position = new Vector2(0, c.Y - h / 2f), MouseFilter = MouseFilterEnum.Ignore };
        var edgeTop = new ColorRect { Color = new Color(color, 0.85f), Size = new Vector2(vs.X, 3f * _s), Position = Vector2.Zero, MouseFilter = MouseFilterEnum.Ignore };
        var edgeBot = new ColorRect { Color = new Color(color, 0.85f), Size = new Vector2(vs.X, 3f * _s), Position = new Vector2(0, h - 3f * _s), MouseFilter = MouseFilterEnum.Ignore };
        band.AddChild(edgeTop); band.AddChild(edgeBot);
        var label = MakeLabel(text, color, 64, header: true);
        label.AddThemeConstantOverride("outline_size", Mathf.Max(5, (int)(9 * _s)));
        band.AddChild(label);
        AddChild(band);
        Fit(label);
        label.Position = new Vector2(vs.X / 2f - label.Size.X / 2f, h / 2f - label.Size.Y / 2f);
        if (rm)
        {
            var t0 = band.CreateTween();
            t0.TweenInterval(0.6f);
            t0.TweenProperty(band, "modulate:a", 0f, 0.2f);
            t0.TweenCallback(Callable.From(band.QueueFree));
            return 0.5f;
        }
        band.Scale = new Vector2(1f, 0f);
        band.PivotOffset = new Vector2(vs.X / 2f, h / 2f);
        label.Position += new Vector2(mine ? -260f * _s : 260f * _s, 0);
        var tw = band.CreateTween();
        tw.TweenProperty(band, "scale", Vector2.One, 0.14f).SetEase(Tween.EaseType.Out);
        tw.Parallel().TweenProperty(label, "position:x", vs.X / 2f - label.Size.X / 2f, 0.22f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        tw.TweenInterval(0.42f);
        tw.TweenProperty(band, "modulate:a", 0f, 0.2f);
        tw.TweenCallback(Callable.From(band.QueueFree));
        Sfx?.Invoke(mine ? "card_draw" : "metal_clink");
        return 0.62f;
    }

    private float PlayDamage(CombatEvent e, bool rm)
    {
        var at = TargetPoint(e);
        if (at is null) return 0f;
        var color = e.Side >= 0 ? StrataOf(e.SourceDefId) : Ember;
        string cause = e.Tag switch { "BURN" => "burn", "PIERCE" => "pierce", "FATIGUE" => "fatigue", "AMBUSH" => "ambush", "CLASH" => "", _ => "" };
        if (e.TargetsFace)
        {
            FaceHit?.Invoke(View(e.TargetSide), e.Amount);
            if (cause != "") Callout(at.Value + new Vector2(0, 34f * _s), cause.ToUpperInvariant(), color, GlyphFor(cause), rm, 0f, small: true);
            return rm ? 0.05f : 0.12f;
        }
        var slot = SlotOf(View(e.TargetSide), e.TargetLane);
        if (slot != null && !rm) RitualEffects.PlayRuneFlare(slot, StrataEnum(e.SourceDefId, e.TargetDefId));
        Number($"-{e.Amount}", color, at.Value, rm, cause == "" ? null : cause);
        if (_lastClock - _lastDamageSfx > 0.12) { Sfx?.Invoke("damage"); _lastDamageSfx = _lastClock; }
        return rm ? 0.04f : 0.09f;
    }

    private float PlayHeal(CombatEvent e, bool rm)
    {
        var at = TargetPoint(e);
        if (at is null) return 0f;
        Number($"+{e.Amount}", Verdant, at.Value + (e.TargetsFace ? new Vector2(0, -10f * _s) : Vector2.Zero), rm, "heal");
        return rm ? 0.04f : 0.08f;
    }

    private float PlayDeath(CombatEvent e, bool rm)
    {
        var at = TargetPoint(e);
        if (at is null) return 0f;
        string why = e.Tag switch
        {
            "VENOM" => "VENOM", "FRAGILE" => "FRAGILE", "TRIBUTE" => "TRIBUTE", "BURN" => "BURNED",
            "DESTROY" => "DESTROYED", "WITHER" => "WITHERED", "SPELL" => "SLAIN", "AMBUSH" => "AMBUSHED", _ => ""
        };
        Rise(at.Value, "skull", why, Colors.White, rm, size: 54f);
        Sfx?.Invoke("death");
        return rm ? 0.05f : 0.14f;
    }

    private float PlayKeyword(CombatEvent e, bool rm)
    {
        var at = TargetPoint(e) ?? SourcePoint(e);
        if (at is null) return 0f;
        (string text, Color c, string glyph) = e.Tag switch
        {
            "GUARD" => ("GUARD", Dawn, "shield"),
            "WARD" => ("WARD", Tide, "ward"),
            "DODGE" => ("DODGE", Tide, "dodge"),
            "ARMOR" => ($"ARMOR −{e.Amount}", Dawn, "armor"),
            "SHIELD" => ($"SHIELDED −{e.Amount}", Dawn, "shieldface"),
            "VENOM" => ("VENOM", Hollow, "venom"),
            "PIERCE" => (e.Amount > 0 ? $"PIERCE {e.Amount}" : "PIERCE", Ember, "pierce"),
            "SWIFT" => ("SWIFT", Dawn, "swift"),
            "EXALTED" => ($"EXALTED +{e.Amount}", Gold, "exalted"),
            "UNEARTH" => ("UNEARTH", Verdant, "rune"),
            "REDIRECT" => ("REDIRECTED", Tide, "pierce"),
            _ => (e.Tag, Gold, "spell")
        };
        if (e.Tag == "GUARD" && !rm && SourcePoint(e) is Vector2 a) Beam(a, at.Value, Dawn, 5f * _s, 0.25f);
        if (e.Tag is "WARD" or "DODGE" or "SHIELD" && !rm) Ring(at.Value, c, 18f * _s, 62f * _s, 0.35f);
        return Callout(at, text, c, glyph, rm, 0.12f);
    }

    private float PlayEffect(CombatEvent e, bool rm)
    {
        // things the screen already shows some other way, or that would give away a face-down card
        if (e.Tag is "SET_TRAP" or "PREVENT_DAMAGE" or "COST_MOD" or "IDENTIFY" or "SET_PREY" or "MOVE_LANE" or "COPY" or "DESTROY") return 0f;
        var at = TargetPoint(e);
        if (at is null) return 0f;
        int n = e.Amount;
        // FABLE-104: GRANT_KEY / REMOVE_KEY carry the keyword after a colon ("GRANT_KEY:WARD")
        string tag = e.Tag, kw = "Keyword";
        int colon = tag.IndexOf(':');
        if (colon >= 0) { kw = tag[(colon + 1)..]; tag = tag[..colon]; }
        if (kw.Length > 1) kw = kw[0] + kw[1..].ToLowerInvariant();
        (string text, Color c, string glyph) = tag switch
        {
            "DRAW" => ($"DRAW {Math.Max(1, n)}", Tide, "card"),
            "DISCARD" => ($"DISCARD {Math.Max(1, n)}", Hollow, "card"),
            "EXCAVATE" => ("EXCAVATE", Dawn, "card"),
            "BURY" => ($"BURY {Math.Max(1, n)}", Hollow, "card"),
            "UNBURY" => ("UNBURIED", Verdant, "card"),
            "STUN" => ("STUNNED", Dawn, "stun"),
            "BURN" => ($"BURN {Math.Max(1, n)}", Ember, "burn"),
            "DRAIN" => ($"DRAIN {Math.Max(1, n)}", Hollow, "down"),
            "DRAIN_TICK" => ($"−{n} ATTUNE", Hollow, "down"),
            "LOCK_LANE" => ("LOCKED", Hollow, "lock"),
            "SILENCE" => ("SILENCED", Hollow, "lock"),
            "BOUNCE" => ("RETURNED", Tide, "card"),
            "ATTUNE" => ($"+{Math.Max(1, n)} ATTUNE", Tide, "spell"),
            "GAIN_VIGOR" => ($"+{n} MAX VIGOR", Verdant, "heart"),
            "LOSE_VIGOR" => ($"−{n} MAX VIGOR", Hollow, "heart"),
            "SWAP_STATS" => ("SWAPPED", Tide, "spell"),
            "DOUBLE_STATS" => ("DOUBLED", Gold, "up"),
            "STEAL" => ("STOLEN", Hollow, "sword"),
            "REFRESH" => ("READIED", Dawn, "swift"),
            "SCY" => ("SCRY", Tide, "card"),
            "SUPPRESS" => ($"SUPPRESSED {Math.Max(1, n)}", Hollow, "lock"),
            "ADD_CHARGE" => ($"+{Math.Max(1, n)} CHARGE", Gold, "charge"),
            "RESET_CHARGES" => ("CHARGES SPENT", Gold, "charge"),
            "FORGE" => ("FORGED", Ember, "up"),
            "HEAL_FULL" => ("RESTORED", Verdant, "heart"),
            "REDIRECT" => ("REDIRECT", Tide, "pierce"),
            "GRANT_KEY" => ($"+{kw}", Gold, "spell"),
            "REMOVE_KEY" => ($"−{kw}", Hollow, "spell"),
            "SET_STAT" => ("SET", Tide, "spell"),
            "REVIVE_TOKEN" => ("REVIVED", Verdant, "rune"),
            "UNEARTH_FROM_GRAVEYARD" => ("UNEARTHED", Verdant, "rune"),
            "SUMMON" => ("SUMMON", Gold, "rune"),
            _ => (tag.Replace('_', ' '), Gold, "spell")
        };
        if (tag is "ADD_CHARGE" or "RESET_CHARGES" && e.TargetsFace)
        {
            // charges live on the artifact plates, not the face
            var plate = PlateOf(View(e.TargetSide), 0) ?? PlateOf(View(e.TargetSide), 1);
            if (plate != null) at = Center(plate);
        }
        return Callout(at, text, c, glyph, rm, 0.1f, small: true);
    }

    private float PlaySigil(CombatEvent e, bool rm)
    {
        // a counter-Sigil is shown by the COUNTERED set piece that follows it
        if (e.Tag == "COUNTER_RITUAL" && !rm) return 0f;
        var at = TargetPoint(e);
        if (at is null) return 0f;
        if (!rm) Ring(at.Value, Tide, 10f * _s, 70f * _s, 0.4f);
        return Callout(at, e.Tag == "AMBUSH" ? "AMBUSH!" : "COUNTERED!", Tide, "sigil", rm, 0.3f);
    }

    // ── spells ───────────────────────────────────────────────────────────

    private float PlayCast(CombatEvent e, bool countered, bool rm)
    {
        var def = CardRegistry.Get(e.SourceDefId);
        string name = def?.Name ?? e.SourceDefId;
        var color = StrataOf(e.SourceDefId);
        int view = View(e.Side);
        if (rm)
        {
            Sfx?.Invoke("spell");
            Callout(BoardCenter(), (view == 0 ? "YOU CAST " : "ENEMY CAST ") + name.ToUpperInvariant() + (countered ? " — COUNTERED" : ""), color, "spell", true, 0.5f);
            return 0.5f;
        }
        // FABLE-065: the full set piece (CombatFx.Spell.cs)
        return PlayCastEpic(e, countered);
    }

    private static IEnumerable<string> OpGlyphs(CardDef? def)
    {
        if (def == null) yield break;
        var seen = new HashSet<string>();
        foreach (var ab in def.Abilities)
            foreach (var ef in ab.Effects)
            {
                string g = GlyphForOp(ef.Op);
                if (seen.Add(g)) yield return g;
            }
    }

    public static string GlyphForOp(Op op) => op switch
    {
        Op.DAMAGE or Op.LOSE_VIGOR => "sword",
        Op.HEAL or Op.HEAL_FULL or Op.GAIN_VIGOR => "heart",
        Op.BUFF or Op.DOUBLE_STATS or Op.FORGE => "up",
        Op.DEBUFF or Op.DRAIN => "down",
        Op.DESTROY => "skull",
        Op.DRAW or Op.EXCAVATE or Op.UNBURY or Op.BOUNCE or Op.SCY or Op.DISCARD or Op.BURY => "card",
        Op.SUMMON or Op.REVIVE_TOKEN or Op.UNEARTH_FROM_GRAVEYARD => "rune",
        Op.STUN => "stun",
        Op.BURN => "burn",
        Op.LOCK_LANE or Op.SILENCE or Op.SUPPRESS => "lock",
        Op.SET_TRAP => "sigil",
        Op.ADD_CHARGE or Op.RESET_CHARGES => "charge",
        Op.STEAL => "sword",
        Op.PREVENT_DAMAGE => "shieldface",
        Op.REDIRECT => "pierce",
        _ => "spell"
    };

    // ── triggers: three renditions ───────────────────────────────────────

    private float PlayTrigger(CombatEvent e, bool rm)
    {
        string trig = e.Tag switch
        {
            "ON_SUMMON" => "ON SUMMON", "ON_DEATH" => "ON DEATH", "ON_TURN_START" => "TURN START", "ON_TURN_END" => "TURN END",
            "ON_ATTACK" => "ON ATTACK", "ON_DAMAGED" => "WHEN HURT", "ON_ALLY_SUMMON" => "ALLY SUMMONED", "ON_ALLY_DEATH" => "ALLY FELL",
            "ON_CAST_RITUAL" or "ON_SPELL_CAST" => "ON RITUAL", "ON_HEAL" => "ON HEAL", "ON_CREATURE_DIES" => "A CREATURE DIED",
            "ON_ALLY_ATTACKED" => "ALLY ATTACKED", "ON_CHARGE_FULL" => "CHARGES FULL", "ON_CHARGE_GAINED" => "CHARGE",
            "ON_RELIC_IDENTIFY" => "AWAKENED", "ON_EXCAVATE" => "ON EXCAVATE", "ON_PREY_MARKED" => "PREY MARKED",
            "ON_PREY_DESTROYED" => "PREY SLAIN", "ON_CREATURE_ATTACKS" => "ON ATTACK",
            _ => e.Tag.Replace("ON_", "").Replace('_', ' ')
        };
        int view = View(e.Side);
        if (e.SourceType == CardType.ARTIFACT)
        {
            var plate = PlateOf(view, e.SourceSlot) ?? PlateOf(view, 0);
            var name = ArtifactRegistry.Get(e.SourceDefId)?.Name ?? e.SourceDefId;
            Sfx?.Invoke("metal_clink");
            if (plate == null) return Callout(BoardCenter(), $"{name.ToUpperInvariant()} · {trig}", Gold, "charge", rm, 0.4f);
            if (plate is ArtifactCardPlate acp) acp.PlayTriggerFlash();
            var at = Center(plate);
            // the arc goes to whatever the artifact does next (its first effect's target)
            var next = _queue.FirstOrDefault(n => n.Kind != FxKind.Trigger);
            if (!rm && next != null && TargetPoint(next) is Vector2 tp && (tp - at).Length() > 40f) Arc(at, tp, Gold, 0.45f);
            if (!rm) Ring(at, Gold, 10f * _s, 80f * _s, 0.4f);
            Callout(at + new Vector2(0, -8f * _s), name.ToUpperInvariant(), Gold, "charge", rm, 0f, small: true);
            return rm ? 0.2f : 0.45f;
        }
        var def = CardRegistry.Get(e.SourceDefId);
        string cname = def?.Name ?? e.SourceDefId;
        var color = StrataOf(e.SourceDefId);
        if (e.SourceType == CardType.RELIC || e.Tag == "ON_RELIC_IDENTIFY")
        {
            // rendition 3: the veil — opaque, then it clears, the relic's name across the screen
            var src = SlotOf(view, e.SourceLane);
            if (src != null && !rm) Ring(Center(src), color, 20f * _s, 120f * _s, 0.6f, width: 5f * _s);
            return Veil(cname.ToUpperInvariant(), trig, color, rm);
        }
        // rendition 1: rune bloom over the creature
        var slot = SlotOf(view, e.SourceLane);
        var p = slot != null ? Center(slot) : SourcePoint(e) ?? BoardCenter();
        if (!rm)
        {
            Bloom(p, color);
            if (slot != null) Punch(slot, 1.06f, 0.2f);
        }
        Callout(p + new Vector2(0, -22f * _s), trig, color, "rune", rm, 0f, small: true);
        return rm ? 0.2f : 0.5f;
    }

    private float Veil(string title, string sub, Color color, bool rm)
    {
        if (rm) return Callout(BoardCenter(), $"{title} · {sub}", color, "rune", true, 0.5f);
        var veil = new ColorRect { Color = new Color(0.02f, 0.015f, 0.03f, 0.82f), MouseFilter = MouseFilterEnum.Ignore };
        FullScreen(veil);
        AddChild(veil);
        var vs = GetViewportRect().Size;
        var icon = Glyph("rune");
        if (icon != null)
        {
            var ic = new TextureRect { Texture = icon, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Size = new Vector2(110f * _s, 110f * _s), Modulate = color, MouseFilter = MouseFilterEnum.Ignore };
            ic.Position = new Vector2(vs.X / 2f - 55f * _s, vs.Y * 0.30f); ic.PivotOffset = ic.Size / 2f;
            veil.AddChild(ic);
            var spin = CreateTween(); spin.TweenProperty(ic, "rotation", Mathf.Pi * 0.5f, 1.1f).SetEase(Tween.EaseType.Out);
        }
        var t = MakeLabel(title, color, 46, header: true);
        t.Size = new Vector2(vs.X, 70f * _s); t.Position = new Vector2(0, vs.Y * 0.30f + 120f * _s);
        veil.AddChild(t);
        var s2 = MakeLabel(sub, new Color(0.85f, 0.8f, 0.7f), 22, header: true);
        s2.Size = new Vector2(vs.X, 36f * _s); s2.Position = new Vector2(0, t.Position.Y + 74f * _s);
        veil.AddChild(s2);
        var tw = CreateTween();
        tw.TweenInterval(0.45f);
        tw.TweenProperty(veil, "modulate:a", 0f, 0.7f).SetEase(Tween.EaseType.In);
        tw.TweenCallback(Callable.From(veil.QueueFree));
        return 0.9f;
    }

    private void Bloom(Vector2 at, Color color)
    {
        Ring(at, color, 12f * _s, 72f * _s, 0.45f, width: 5f * _s);
        Ring(at, color, 4f * _s, 44f * _s, 0.55f, width: 3f * _s, delay: 0.08f);
        var g = Glyph("rune");
        if (g == null) return;
        var ic = new TextureRect { Texture = g, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Size = new Vector2(64f * _s, 64f * _s), Modulate = new Color(color, 0f), MouseFilter = MouseFilterEnum.Ignore };
        ic.Position = ToLocal(at) - ic.Size / 2f; ic.PivotOffset = ic.Size / 2f; ic.Scale = new Vector2(0.4f, 0.4f);
        AddChild(ic);
        var tw = CreateTween();
        tw.SetParallel();
        tw.TweenProperty(ic, "modulate:a", 1f, 0.15f);
        tw.TweenProperty(ic, "scale", new Vector2(1.1f, 1.1f), 0.3f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        tw.TweenProperty(ic, "rotation", Mathf.Pi / 3f, 0.6f);
        tw.SetParallel(false);
        tw.TweenProperty(ic, "modulate:a", 0f, 0.25f);
        tw.TweenCallback(Callable.From(ic.QueueFree));
    }

    // ── primitives ───────────────────────────────────────────────────────

    /// <summary>A number at a point: rises and fades (static for half a second with Reduce Motion).</summary>
    private void Number(string text, Color color, Vector2 at, bool rm, string? glyph = null)
    {
        var box = Pair(text, color, 38, glyph, color);
        AddChild(box);
        Fit(box);
        box.Position = ToLocal(at) - new Vector2(box.Size.X / 2f, box.Size.Y / 2f + 10f * _s);
        Drift(box, rm, rise: 56f * _s, dur: 0.95f, pop: true);
    }

    /// <summary>A short word with a glyph at a point. Returns the gap to wait.</summary>
    private float Callout(Vector2? at, string text, Color color, string? glyph, bool rm, float gap, bool small = false)
    {
        if (at is null) return 0f;
        var box = Pair(text, color, small ? 21 : 27, glyph, color);
        AddChild(box);
        Fit(box);
        box.Position = ToLocal(at.Value) - new Vector2(box.Size.X / 2f, box.Size.Y / 2f + (small ? 30f : 42f) * _s);
        Drift(box, rm, rise: small ? 26f * _s : 40f * _s, dur: small ? 1.0f : 1.25f, pop: !small);
        return gap;
    }

    /// <summary>A big glyph rising from a point with a word under it (the death skull).</summary>
    private void Rise(Vector2 at, string glyph, string caption, Color color, bool rm, float size)
    {
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        box.AddThemeConstantOverride("separation", (int)(2 * _s));
        var g = Glyph(glyph);
        if (g != null)
            box.AddChild(new TextureRect { Texture = g, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(size * _s, size * _s), Modulate = color, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ShrinkCenter });
        if (caption != "")
        {
            var l = MakeLabel(caption, new Color(0.9f, 0.85f, 0.8f), 15, header: true);
            l.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
            box.AddChild(l);
        }
        AddChild(box);
        Fit(box);
        box.Position = ToLocal(at) - box.Size / 2f;
        Drift(box, rm, rise: 70f * _s, dur: 1.4f, pop: true);
    }

    private Control Pair(string text, Color color, int fontPx, string? glyph, Color glyphColor)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", (int)(4 * _s));
        var g = glyph != null ? Glyph(glyph) : null;
        float gs = fontPx * 1.15f * _s;
        if (g != null)
            row.AddChild(new TextureRect { Texture = g, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(gs, gs), Modulate = glyphColor, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter });
        row.AddChild(MakeLabel(text, color, fontPx, header: true));
        return row;
    }

    /// <summary>Size a box to its content once it is in the tree (fonts resolve there), pivot in the middle.</summary>
    private static void Fit(Control box)
    {
        var sz = box.GetCombinedMinimumSize();
        box.Size = sz;
        box.PivotOffset = sz / 2f;
    }

    private Label MakeLabel(string text, Color color, int fontPx, bool header)
    {
        var l = new Label { Text = text, Modulate = color, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        int px = Mathf.RoundToInt(fontPx * _s);
        var f = header ? ThemeTokens.GetHeaderFont(px) : ThemeTokens.GetBodyFont(px);
        if (f != null) l.AddThemeFontOverride("font", f);
        l.AddThemeFontSizeOverride("font_size", px);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        l.AddThemeConstantOverride("outline_size", Mathf.Max(2, (int)(3 * _s)));
        return l;
    }

    private void Drift(Control box, bool rm, float rise, float dur, bool pop)
    {
        var tw = CreateTween();
        if (rm)
        {
            tw.TweenInterval(0.55f);
            tw.TweenProperty(box, "modulate:a", 0f, 0.25f);
            tw.TweenCallback(Callable.From(box.QueueFree));
            return;
        }
        if (pop)
        {
            box.Scale = new Vector2(0.6f, 0.6f);
            tw.TweenProperty(box, "scale", Vector2.One, 0.14f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        }
        tw.SetParallel();
        tw.TweenProperty(box, "position", box.Position + new Vector2(0, -rise), dur).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(box, "modulate:a", 0f, dur * 0.45f).SetDelay(dur * 0.55f);
        tw.SetParallel(false);
        tw.TweenCallback(Callable.From(box.QueueFree));
    }

    private void Beam(Vector2 a, Vector2 b, Color color, float width, float dur)
    {
        var line = new Line2D { Width = width, DefaultColor = new Color(color, 0.95f), Antialiased = true, BeginCapMode = Line2D.LineCapMode.Round, EndCapMode = Line2D.LineCapMode.Round };
        line.AddPoint(ToLocal(a)); line.AddPoint(ToLocal(a));
        AddChild(line);
        var tw = CreateTween();
        tw.TweenMethod(Callable.From<float>(t => { if (line.IsInsideTree()) line.SetPointPosition(1, ToLocal(a).Lerp(ToLocal(b), t)); }), 0f, 1f, dur * 0.4f).SetEase(Tween.EaseType.In);
        tw.SetParallel();
        tw.TweenProperty(line, "width", 0f, dur * 0.6f);
        tw.TweenProperty(line, "modulate:a", 0f, dur * 0.6f);
        tw.SetParallel(false);
        tw.TweenCallback(Callable.From(line.QueueFree));
        var ring = new FxRing { Color = color, Radius = width, Width = width * 0.6f };
        ring.Position = ToLocal(b);
        AddChild(ring);
        var tw2 = CreateTween();
        tw2.TweenInterval(dur * 0.35f);
        tw2.SetParallel();
        tw2.TweenMethod(Callable.From<float>(r => { if (ring.IsInsideTree()) ring.Radius = r; }), width, 34f * _s, 0.25f).SetEase(Tween.EaseType.Out);
        tw2.TweenProperty(ring, "modulate:a", 0f, 0.3f);
        tw2.SetParallel(false);
        tw2.TweenCallback(Callable.From(ring.QueueFree));
    }

    private void Arc(Vector2 a, Vector2 b, Color color, float dur)
    {
        var line = new Line2D { Width = 4f * _s, DefaultColor = new Color(color, 0.9f), Antialiased = true };
        var mid = (a + b) / 2f + new Vector2(0, -Mathf.Min(140f * _s, (b - a).Length() * 0.35f));
        const int N = 18;
        for (int i = 0; i <= N; i++) line.AddPoint(ToLocal(a));
        AddChild(line);
        var tw = CreateTween();
        tw.TweenMethod(Callable.From<float>(t =>
        {
            if (!line.IsInsideTree()) return;
            for (int i = 0; i <= N; i++)
            {
                float u = Mathf.Min(1f, (float)i / N) * t;
                var p = (1 - u) * (1 - u) * a + 2 * (1 - u) * u * mid + u * u * b;
                line.SetPointPosition(i, ToLocal(p));
            }
        }), 0f, 1f, dur * 0.6f).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(line, "modulate:a", 0f, dur * 0.4f);
        tw.TweenCallback(Callable.From(line.QueueFree));
    }

    private void Ring(Vector2 at, Color color, float r0, float r1, float dur, float width = -1f, float delay = 0f)
    {
        var ring = new FxRing { Color = color, Radius = r0, Width = width > 0 ? width : 4f * _s };
        ring.Position = ToLocal(at);
        AddChild(ring);
        var tw = CreateTween();
        if (delay > 0) tw.TweenInterval(delay);
        tw.SetParallel();
        tw.TweenMethod(Callable.From<float>(r => { if (ring.IsInsideTree()) ring.Radius = r; }), r0, r1, dur).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tw.TweenProperty(ring, "modulate:a", 0f, dur).SetEase(Tween.EaseType.In);
        tw.SetParallel(false);
        tw.TweenCallback(Callable.From(ring.QueueFree));
    }

    private void Punch(Control c, float by, float dur)
    {
        if (!IsInstanceValid(c) || !c.IsInsideTree()) return;
        c.PivotOffset = c.Size / 2f;
        var tw = c.CreateTween();
        tw.TweenProperty(c, "scale", new Vector2(by, by), dur * 0.4f).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(c, "scale", Vector2.One, dur * 0.6f).SetEase(Tween.EaseType.In);
    }

    // ── geometry and lookups ─────────────────────────────────────────────

    private Vector2 ToLocal(Vector2 global) => global - GlobalPosition;

    /// <summary>Cover the whole screen (explicit size: anchors don't resolve on these parents).</summary>
    private void FullScreen(Control c)
    {
        c.Position = Vector2.Zero;
        c.Size = GetViewportRect().Size;
    }

    private static Vector2 Center(Control c) => c.GlobalPosition + c.Size * c.Scale / 2f;

    private Vector2 BoardCenter()
    {
        var top = SlotOf(1, 2); var bot = SlotOf(0, 2);
        if (top != null && bot != null) return (Center(top) + Center(bot)) / 2f;
        var vs = GetViewportRect().Size;
        return GlobalPosition + new Vector2(vs.X / 2f, vs.Y * 0.45f);
    }

    private Vector2? TargetPoint(CombatEvent e)
    {
        if (e.TargetSide < 0) return null;
        int view = View(e.TargetSide);
        if (e.TargetLane >= 0) { var s = SlotOf(view, e.TargetLane); return s != null ? Center(s) : null; }
        var crest = CrestOf(view);
        return crest != null ? Center(crest) : null;
    }

    private Vector2? SourcePoint(CombatEvent e)
    {
        if (e.Side < 0) return null;
        int view = View(e.Side);
        if (e.SourceLane >= 0) { var s = SlotOf(view, e.SourceLane); if (s != null) return Center(s); }
        if (e.SourceSlot >= 0) { var p = PlateOf(view, e.SourceSlot); if (p != null) return Center(p); }
        return CastOrigin(view);
    }

    private static Strata StrataEnum(string sourceDef, string fallbackDef)
    {
        var d = CardRegistry.Get(sourceDef) ?? CardRegistry.Get(fallbackDef);
        return d?.Strata ?? Strata.DAWN;
    }

    private static Color StrataOf(string defId)
    {
        var d = CardRegistry.Get(defId);
        return d != null ? ThemeTokens.StrataColor(d.Strata) : Gold;
    }

    private static string GlyphFor(string cause) => cause switch
    {
        "burn" => "burn", "pierce" => "pierce", "fatigue" => "card", "ambush" => "sigil", "heal" => "heart", _ => "sword"
    };

    private static string Stat(int a, int v, bool up)
    {
        string sa = a == 0 ? "" : (a > 0 ? "+" : "−") + Math.Abs(a);
        string sv = v == 0 ? "" : (v > 0 ? "+" : "−") + Math.Abs(v);
        if (sa == "") return sv + " VIGOR";
        if (sv == "") return sa + " ATTACK";
        return $"{sa}/{sv}";
    }

    public static Texture2D? Glyph(string name)
    {
        if (_glyphs.TryGetValue(name, out var t)) return t;
        string path = $"res://assets/fx/{name}.svg";
        t = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        _glyphs[name] = t;
        return t;
    }

    private static Texture2D? ArtFor(string defId)
    {
        foreach (var p in new[] { $"res://content/art/cards_baked/{defId}.webp", $"res://content/art/{defId}.webp" })
            if (ResourceLoader.Exists(p)) return ResourceLoader.Load<Texture2D>(p);
        return null;
    }

    private static readonly Color Gold = new(0.86f, 0.72f, 0.38f);
    private static Color Ember => ThemeTokens.StrataColor(Strata.EMBER);
    private static Color Tide => ThemeTokens.StrataColor(Strata.TIDE);
    private static Color Verdant => ThemeTokens.StrataColor(Strata.VERDANT);
    private static Color Hollow => ThemeTokens.StrataColor(Strata.HOLLOW);
    private static Color Dawn => ThemeTokens.StrataColor(Strata.DAWN);
}

/// <summary>A ring drawn with an arc; Radius and Width are tweened.</summary>
public partial class FxRing : Control
{
    private float _radius = 10f, _width = 4f;
    public float Radius { get => _radius; set { _radius = value; QueueRedraw(); } }
    public float Width { get => _width; set { _width = value; QueueRedraw(); } }
    public Color Color = Colors.White;

    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        DrawArc(Vector2.Zero, _radius, 0f, Mathf.Tau, 48, Color, _width, true);
    }
}

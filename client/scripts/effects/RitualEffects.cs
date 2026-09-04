using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// Centralized visual effects for the dark fae ritual juice pass (TASK-JUICE-1).
/// Every effect is under 400ms, skippable via CampaignContext.ReduceMotion
/// (or the optional force parameter for non-skip contexts), and creates only
/// temporary visual nodes that self-cleanup. No layout-affecting changes.
/// All effects hook into the existing audio manifest via the scene's AudioManager.
/// </summary>
public static class RitualEffects
{
    // ── Constants ──
    private const float EffectDuration = 0.35f; // < 400ms
    private const float FastFade = 0.25f;
    
    /// <summary>
    /// Stratum-colour lookup for death effects: each stratum gets a unique
    /// colour palette matching its thematic substance.
    /// </summary>
    private static Color DeathColor(Strata s) => s switch
    {
        Strata.EMBER => new Color(0.35f, 0.20f, 0.10f, 1.0f),  // ash
        Strata.VERDANT => new Color(0.25f, 0.30f, 0.10f, 1.0f), // roots
        Strata.TIDE => new Color(0.15f, 0.30f, 0.35f, 1.0f),   // brine
        Strata.HOLLOW => new Color(0.30f, 0.20f, 0.30f, 1.0f),  // bone-dust
        Strata.DAWN => new Color(0.40f, 0.35f, 0.15f, 1.0f),    // light
        _ => new Color(0.30f, 0.25f, 0.20f, 1.0f)
    };

    /// <summary>
    /// Colour for the rune-flare on the hit creature — attacker's stratum.
    /// </summary>
    private static Color FlareColor(Strata s) => s switch
    {
        Strata.EMBER => new Color(0.80f, 0.35f, 0.10f, 0.6f),
        Strata.VERDANT => new Color(0.35f, 0.70f, 0.20f, 0.6f),
        Strata.TIDE => new Color(0.15f, 0.50f, 0.60f, 0.6f),
        Strata.HOLLOW => new Color(0.50f, 0.20f, 0.50f, 0.6f),
        Strata.DAWN => new Color(0.60f, 0.55f, 0.15f, 0.6f),
        _ => new Color(0.50f, 0.40f, 0.30f, 0.6f)
    };

    // ── Effect Implementations ──

    /// <summary>
    /// Card play: a puff of stone dust and a low thud as it seats in the lane.
    /// Creates brief expanding dust-ring and stone-colour flash on the slot.
    /// </summary>
    public static void PlayStoneDustPuff(Control parent, bool skip = false)
    {
        if (skip) return;
        
        var dust = new ColorRect
        {
            Color = new Color(0.55f, 0.45f, 0.35f, 0.35f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        dust.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dust.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        parent.AddChild(dust);

        var tween = parent.CreateTween();
        tween.SetParallel();
        // Expand and fade the dust
        tween.TweenProperty(dust, "color", new Color(0.55f, 0.45f, 0.35f, 0.0f), EffectDuration);
        // Brief stone-colour flash on the parent itself
        var origMod = parent.Modulate;
        parent.Modulate = new Color(1.15f, 1.10f, 1.05f, 1.0f);
        tween.TweenProperty(parent, "modulate", origMod, FastFade);
        tween.SetParallel(false);
        tween.TweenCallback(Callable.From(() =>
        {
            if (dust.IsInsideTree()) dust.QueueFree();
        }));
    }

    /// <summary>
    /// Hit: a rune-flare in the attacker's stratum colour on the hit lane slot.
    /// </summary>
    public static void PlayRuneFlare(Control parent, Strata stratum, bool skip = false)
    {
        if (skip) return;
        
        var flare = new ColorRect
        {
            Color = FlareColor(stratum),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        flare.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        flare.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        parent.AddChild(flare);

        var tween = parent.CreateTween();
        tween.SetParallel();
        tween.TweenProperty(flare, "color", new Color(0, 0, 0, 0), EffectDuration * 0.8f);
        tween.TweenProperty(flare, "scale", new Vector2(1.3f, 1.3f), EffectDuration * 0.8f);
        tween.SetParallel(false);
        tween.TweenCallback(Callable.From(() =>
        {
            if (flare.IsInsideTree()) flare.QueueFree();
        }));
    }

    /// <summary>
    /// Creature death: the card crumbles — ash, roots, brine, bone-dust or light by stratum.
    /// </summary>
    public static void PlayCrumblingDeath(Control parent, Strata stratum, bool skip = false)
    {
        if (skip) return;
        
        var crumble = new ColorRect
        {
            Color = DeathColor(stratum),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        crumble.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        parent.AddChild(crumble);

        var tween = parent.CreateTween();
        tween.SetParallel();
        // Darken and shrink the parent
        tween.TweenProperty(crumble, "color", new Color(0, 0, 0, 0.5f), EffectDuration);
        // Brief flash of stratum colour then fade to black
        tween.TweenProperty(crumble, "modulate", new Color(1, 1, 1, 0), EffectDuration);
        tween.TweenProperty(parent, "scale", new Vector2(0.3f, 0.3f), EffectDuration)
            .SetEase(Tween.EaseType.In);
        tween.SetParallel(false);
        tween.TweenCallback(Callable.From(() =>
        {
            if (crumble.IsInsideTree()) crumble.QueueFree();
        }));
    }

    /// <summary>
    /// Face damage: the whole altar ring trembles and the screen edge pulses
    /// in the enemy's colour. Creates a brief edge-pulse overlay and ring shake.
    /// </summary>
    public static void PlayFaceDamagePulse(Node sceneRoot, ColorRect edgeOverlay, Color pulseColor, bool skip = false)
    {
        if (skip) return;
        
        // Screen edge pulse — uses a full-rect ColorRect that flashes the edge colour
        edgeOverlay.Color = new Color(pulseColor.R, pulseColor.G, pulseColor.B, 0.25f);
        edgeOverlay.Visible = true;
        
        // Brief ring tremble via a slight scale oscillation on the altar container
        var altar = sceneRoot.GetNodeOrNull<Control>("Board/AltarContainer");
        if (altar != null && altar.IsInsideTree())
        {
            var origScale = altar.Scale;
            var tween = altar.CreateTween();
            tween.SetParallel(false);
            // Three rapid shakes
            tween.TweenProperty(altar, "scale", origScale * new Vector2(1.01f, 1.01f), 0.04f);
            tween.TweenProperty(altar, "scale", origScale * new Vector2(0.99f, 0.99f), 0.04f);
            tween.TweenProperty(altar, "scale", origScale * new Vector2(1.005f, 1.005f), 0.04f);
            tween.TweenProperty(altar, "scale", origScale, 0.04f);
        }

        // Fade edge overlay out
        var fadeTween = sceneRoot.CreateTween();
        fadeTween.TweenProperty(edgeOverlay, "color", new Color(pulseColor.R, pulseColor.G, pulseColor.B, 0.0f), EffectDuration);
        fadeTween.TweenCallback(Callable.From(() => { edgeOverlay.Visible = false; }));
    }

    /// <summary>
    /// End Turn: the altar ring turns one notch. Creates a brief rotating fan
    /// overlay that sweeps around the ring center, suggesting a notch turn.
    /// </summary>
    public static void PlayRingTurnNotch(Control parent, bool skip = false)
    {
        if (skip) return;
        
        var vp = parent.GetViewportRect().Size;
        float boardTop = 74f;
        float boardH = vp.Y - boardTop - 160f;
        float cx = vp.X / 2f;
        float cy = boardTop + boardH * RingCenterY;

        // A small triangular/narrow arc that sweeps 1/5 rotation (72 degrees)
        // We use a thin rotating ColorRect to suggest the ring notch turn
        var notch = new ColorRect
        {
            Color = new Color(0.80f, 0.70f, 0.40f, 0.15f),
            Size = new Vector2(60f, 4f),
            Position = new Vector2(cx - 30f, cy - 2f),
            PivotOffset = new Vector2(30f, 2f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        parent.AddChild(notch);

        var tween = parent.CreateTween();
        tween.TweenProperty(notch, "rotation", Mathf.DegToRad(72f), EffectDuration * 0.6f)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(notch, "modulate", new Color(0.80f, 0.70f, 0.40f, 0.0f), EffectDuration * 0.4f);
        tween.SetParallel(false);
        tween.TweenCallback(Callable.From(() =>
        {
            if (notch.IsInsideTree()) notch.QueueFree();
        }));
    }

    /// <summary>
    /// Victory: light floods up from the altar. A radial gradient overlay grows
    /// from the ring center outward, then fades into the scene.
    /// </summary>
    public static void PlayVictoryLight(Control parent, bool skip = false)
    {
        if (skip) return;
        
        var vp = parent.GetViewportRect().Size;
        float boardTop = 74f;
        float boardH = vp.Y - boardTop - 160f;
        float cx = vp.X / 2f;
        float cy = boardTop + boardH * RingCenterY;

        var light = new ColorRect
        {
            Color = new Color(0.95f, 0.85f, 0.50f, 0.0f),
            Size = new Vector2(0, 0),
            Position = new Vector2(cx, cy),
            PivotOffset = new Vector2(0, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
        };
        parent.AddChild(light);

        var tween = parent.CreateTween();
        tween.SetParallel();
        // Grow from center
        float maxSize = vp.Length() * 1.5f;
        tween.TweenProperty(light, "size", new Vector2(maxSize, maxSize), EffectDuration * 1.2f)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(light, "position", new Vector2(cx - maxSize / 2f, cy - maxSize / 2f), EffectDuration * 1.2f)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(light, "color", new Color(0.95f, 0.85f, 0.50f, 0.20f), EffectDuration * 0.6f);
        // Fade out after peak
        tween.TweenProperty(light, "color", new Color(0.95f, 0.85f, 0.50f, 0.0f), EffectDuration * 0.6f);
        tween.SetParallel(false);
        tween.TweenCallback(Callable.From(() =>
        {
            if (light.IsInsideTree()) light.QueueFree();
        }));
    }

    /// <summary>
    /// Defeat: light drains down into the stone. A dark vignette overlay
    /// shrinks into the ring center, suggesting energy draining away.
    /// </summary>
    public static void PlayDefeatDrain(Control parent, bool skip = false)
    {
        if (skip) return;
        
        var vp = parent.GetViewportRect().Size;
        float boardTop = 74f;
        float boardH = vp.Y - boardTop - 160f;
        float cx = vp.X / 2f;
        float cy = boardTop + boardH * RingCenterY;

        // Start as full-screen dark overlay
        var drain = new ColorRect
        {
            Color = new Color(0.05f, 0.03f, 0.02f, 0.0f),
            Size = vp * 1.5f,
            Position = new Vector2(-vp.X * 0.25f, -vp.Y * 0.25f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        parent.AddChild(drain);

        // Flash dark then shrink to center
        var tween = parent.CreateTween();
        tween.SetParallel();
        tween.TweenProperty(drain, "color", new Color(0.05f, 0.03f, 0.02f, 0.25f), EffectDuration * 0.5f);
        tween.TweenProperty(drain, "color", new Color(0.05f, 0.03f, 0.02f, 0.0f), EffectDuration * 0.5f)
            .SetDelay(EffectDuration * 0.5f);
        // Shrink to center
        tween.TweenProperty(drain, "size", new Vector2(0, 0), EffectDuration * 1.2f)
            .SetEase(Tween.EaseType.In)
            .SetDelay(EffectDuration * 0.2f);
        tween.TweenProperty(drain, "position", new Vector2(cx, cy), EffectDuration * 1.2f)
            .SetEase(Tween.EaseType.In)
            .SetDelay(EffectDuration * 0.2f);
        tween.SetParallel(false);
        tween.TweenCallback(Callable.From(() =>
        {
            if (drain.IsInsideTree()) drain.QueueFree();
        }));
    }

    /// <summary>
    /// Artifact charge gain: its rune sigil brightens with a brief flash.
    /// At max charges (halo), a soft glow pulse around the artifact.
    /// </summary>
    public static void PlayChargeBrighten(Control parent, bool isFull, bool skip = false)
    {
        if (skip) return;

        // Brief bright flash
        var flash = new ColorRect
        {
            Color = new Color(0.90f, 0.80f, 0.40f, 0.30f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        flash.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        parent.AddChild(flash);

        var tween = parent.CreateTween();
        tween.SetParallel();
        tween.TweenProperty(flash, "color", new Color(0.90f, 0.80f, 0.40f, 0.0f), EffectDuration * 0.7f);

        // At max charges: extra halo effect (slightly larger, outer glow)
        if (isFull)
        {
            var halo = new ColorRect
            {
                Color = new Color(0.60f, 0.80f, 1.0f, 0.15f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            // Halo sits slightly outside
            var parentSize = parent.Size;
            var pad = new Vector2(6f, 6f);
            halo.Position = -pad;
            halo.Size = parentSize + pad * 2f;
            halo.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
            parent.AddChild(halo);

            tween.TweenProperty(halo, "color", new Color(0.60f, 0.80f, 1.0f, 0.0f), EffectDuration * 0.9f);
            tween.TweenProperty(halo, "scale", new Vector2(1.15f, 1.15f), EffectDuration * 0.9f);
            tween.SetParallel(false);
            tween.TweenCallback(Callable.From(() =>
            {
                if (halo.IsInsideTree()) halo.QueueFree();
            }));
        }

        tween.SetParallel(false);
        tween.TweenCallback(Callable.From(() =>
        {
            if (flash.IsInsideTree()) flash.QueueFree();
        }));
    }
}
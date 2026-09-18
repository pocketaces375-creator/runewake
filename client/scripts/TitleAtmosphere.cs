using Godot;

namespace Runewake.Client;

/// <summary>
/// FABLE-007: the title screen, alive.
///
/// The brief was "a looped animation, epic, the same image or similar". The way
/// NOT to do that on this project is a video file: client/content/art is already
/// 201 MB of a 205 MB APK, and a looping decoder is the single most expensive
/// thing you can leave running on a phone's battery while somebody reads a menu.
///
/// So the painting stays a painting and the motion is generated on the GPU:
///
///   push-in    the hero art breathes between 1.02 and 1.075 over 48 seconds,
///              drifting a few pixels sideways so it never feels like a zoom.
///              It never returns to 1.0 — KeepAspectCovered fits exactly there,
///              so the drift would show bare background at the edge
///   shafts     god-rays raked across the hall, four bands at different speeds
///              so they never visibly repeat
///   shimmer    caustic light on the flooded floor, bottom third only
///   motes      drifting dust, real nodes rather than a per-pixel loop — forty
///              sprites cost nothing, forty iterations in a fragment shader cost
///              a fill-rate-bound phone a great deal
///   vignette   a slow breath of darkness at the edges, so the centre lifts
///
/// Nothing here is periodic on a common multiple, so the loop never lands on a
/// seam — it is endless rather than looped. Total cost: zero bytes of asset, two
/// small fragment shaders, forty sprites.
///
/// Honours CampaignContext.ReduceMotion: everything holds still, at a slightly
/// lower intensity, and the push-in does not run.
/// </summary>
public partial class TitleAtmosphere : Control
{
    private const int MoteCount = 40;

    private TextureRect? _hero;
    private ColorRect _shafts = default!;
    private ColorRect _shimmer = default!;
    private ColorRect _vignette = default!;
    private Node2D _moteLayer = default!;
    private readonly Sprite2D[] _motes = new Sprite2D[MoteCount];
    private readonly float[] _moteSpeed = new float[MoteCount];
    private readonly float[] _motePhase = new float[MoteCount];
    private readonly float[] _moteDrift = new float[MoteCount];
    private float _t;
    private bool _reduced;

    // ── Shaders ──────────────────────────────────────────────────────────────

    private const string ShaftShader = @"
shader_type canvas_item;
render_mode blend_add;

uniform vec4 ray_color : source_color = vec4(1.0, 0.93, 0.74, 1.0);
uniform float intensity : hint_range(0.0, 1.0) = 0.30;
uniform float speed : hint_range(0.0, 2.0) = 1.0;
uniform float tilt : hint_range(-1.0, 1.0) = 0.42;

void fragment() {
    vec2 uv = UV;
    float ca = cos(tilt);
    float sa = sin(tilt);
    float px = uv.x * ca - uv.y * sa;
    float t = TIME * 0.02 * speed;

    // Four bands, deliberately incommensurate widths and speeds so the pattern
    // never returns to where it started.
    float acc = 0.0;
    acc += smoothstep(0.62, 1.0, sin(px *  7.0 + t * 1.00)) * 0.50;
    acc += smoothstep(0.66, 1.0, sin(px * 11.3 - t * 1.37 + 1.7)) * 0.32;
    acc += smoothstep(0.70, 1.0, sin(px * 17.9 + t * 0.61 + 4.1)) * 0.20;
    acc += smoothstep(0.74, 1.0, sin(px * 26.4 - t * 2.11 + 2.3)) * 0.12;

    // Rays come from above and die out before the floor; they also fade at the
    // left and right edges so the effect has no visible boundary.
    float vert = smoothstep(0.95, 0.10, uv.y);
    float edge = smoothstep(0.0, 0.30, uv.x) * smoothstep(1.0, 0.70, uv.x);
    float breathe = 0.85 + 0.15 * sin(TIME * 0.11 * speed);

    COLOR = vec4(ray_color.rgb, acc * vert * edge * breathe * intensity);
}
";

    private const string ShimmerShader = @"
shader_type canvas_item;
render_mode blend_add;

uniform vec4 water_color : source_color = vec4(0.72, 0.88, 1.0, 1.0);
uniform float intensity : hint_range(0.0, 1.0) = 0.22;
uniform float speed : hint_range(0.0, 2.0) = 1.0;

void fragment() {
    vec2 uv = UV;
    float t = TIME * 0.35 * speed;

    // Crossed travelling waves read as caustics on a wet floor.
    float a = sin(uv.x * 26.0 + t * 1.00 + sin(uv.y * 13.0 - t * 0.7) * 1.6);
    float b = sin(uv.x * 17.0 - t * 1.31 + sin(uv.y *  9.0 + t * 0.5) * 1.2);
    float c = smoothstep(0.55, 1.0, a * 0.5 + b * 0.5 + 0.5);

    // Strongest at the bottom edge, gone by the top of this band.
    float depth = smoothstep(0.0, 0.85, uv.y);

    COLOR = vec4(water_color.rgb, c * depth * intensity);
}
";

    private const string VignetteShader = @"
shader_type canvas_item;

uniform float strength : hint_range(0.0, 1.0) = 0.42;
uniform float breathe : hint_range(0.0, 1.0) = 1.0;

void fragment() {
    vec2 d = UV - vec2(0.5);
    d.y *= 1.12;
    float r = length(d) * 1.42;
    float v = smoothstep(0.42, 1.0, r);
    float pulse = 1.0 + 0.10 * sin(TIME * 0.07) * breathe;
    COLOR = vec4(0.02, 0.02, 0.03, v * strength * pulse);
}
";

    // ── Construction ─────────────────────────────────────────────────────────

    /// <summary>
    /// Build the atmosphere as the next child of <paramref name="parent"/>.
    ///
    /// Call this AFTER the background layers (hero art, rune wheels) and BEFORE
    /// any UI — draw order is child order, so that puts the light in front of the
    /// painting and behind the buttons, which is where it belongs.
    /// </summary>
    public static TitleAtmosphere Attach(Node parent, TextureRect? hero)
    {
        var atmo = new TitleAtmosphere { _hero = hero };
        parent.AddChild(atmo);
        return atmo;
    }

    public override void _Ready()
    {
        Name = "TitleAtmosphere";
        // AndOffsets, not SetAnchorsPreset. _Ready runs when we are ALREADY in the
        // tree, and set_anchor with keep_offset=false preserves the current rect —
        // which for a freshly-new'd node is 0x0. The anchors would have been right
        // and the size would have stayed zero, taking all three shader rects with
        // it and rendering the entire effect invisible.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _reduced = CampaignContext.ReduceMotion;

        _shafts = MakeShaderRect(ShaftShader);
        _shafts.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        SetParam(_shafts, "intensity", _reduced ? 0.16f : 0.30f);
        SetParam(_shafts, "speed", _reduced ? 0.0f : 1.0f);
        AddChild(_shafts);

        _shimmer = MakeShaderRect(ShimmerShader);
        _shimmer.AnchorLeft = 0f; _shimmer.AnchorRight = 1f;
        _shimmer.AnchorTop = 0.62f; _shimmer.AnchorBottom = 1f;
        _shimmer.OffsetLeft = _shimmer.OffsetRight = _shimmer.OffsetTop = _shimmer.OffsetBottom = 0f;
        SetParam(_shimmer, "intensity", _reduced ? 0.10f : 0.22f);
        SetParam(_shimmer, "speed", _reduced ? 0.0f : 1.0f);
        AddChild(_shimmer);

        _moteLayer = new Node2D { Name = "Motes" };
        AddChild(_moteLayer);
        BuildMotes();

        _vignette = MakeShaderRect(VignetteShader);
        _vignette.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        SetParam(_vignette, "strength", 0.42f);
        SetParam(_vignette, "breathe", _reduced ? 0.0f : 1.0f);
        AddChild(_vignette);

        if (!_reduced) StartPushIn();

        GD.Print($"[TITLE-ATMO] shafts+shimmer+{MoteCount} motes+vignette "
                 + $"(reduce_motion={_reduced}, hero={(_hero != null ? "yes" : "none")})");
    }

    private static ColorRect MakeShaderRect(string code)
    {
        var mat = new ShaderMaterial { Shader = new Shader { Code = code } };
        return new ColorRect
        {
            Color = new Color(1, 1, 1, 1),
            MouseFilter = MouseFilterEnum.Ignore,
            Material = mat,
        };
    }

    private static void SetParam(CanvasItem node, string name, float value)
    {
        if (node.Material is ShaderMaterial m) m.SetShaderParameter(name, value);
    }

    // ── Dust ─────────────────────────────────────────────────────────────────

    /// <summary>A soft round dot, generated rather than shipped.</summary>
    private static ImageTexture MakeDotTexture(int size = 24)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float c = (size - 1) / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                float a = Mathf.Clamp(1f - d, 0f, 1f);
                a = a * a * a;                     // soft falloff, no hard rim
                img.SetPixel(x, y, new Color(1f, 0.97f, 0.88f, a));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    private void BuildMotes()
    {
        var tex = MakeDotTexture();
        var rng = new RandomNumberGenerator();
        rng.Seed = 20260918;
        var vp = GetViewportRect().Size;
        for (int i = 0; i < MoteCount; i++)
        {
            float scale = rng.RandfRange(0.10f, 0.42f);
            var s = new Sprite2D
            {
                Texture = tex,
                Position = new Vector2(rng.RandfRange(0, vp.X), rng.RandfRange(0, vp.Y)),
                Scale = new Vector2(scale, scale),
                // Nearer motes are brighter; the small ones read as distance.
                Modulate = new Color(1f, 0.98f, 0.90f, rng.RandfRange(0.10f, 0.42f) * (_reduced ? 0.6f : 1f)),
            };
            _moteLayer.AddChild(s);
            _motes[i] = s;
            _moteSpeed[i] = rng.RandfRange(4f, 15f) * scale;   // big = near = faster
            _motePhase[i] = rng.RandfRange(0f, Mathf.Tau);
            _moteDrift[i] = rng.RandfRange(6f, 22f);
        }
    }

    // ── Motion ───────────────────────────────────────────────────────────────

    private void StartPushIn()
    {
        if (_hero == null || !IsInstanceValid(_hero)) return;
        // Deferred: the hero's rect is only real after the first layout pass, and
        // scaling about the wrong pivot shows the background sliding off-centre.
        Callable.From(() =>
        {
            if (_hero == null || !IsInstanceValid(_hero)) return;
            _hero.PivotOffset = _hero.Size / 2f;
            // A permanent 2% overscan. KeepAspectCovered fits the viewport EXACTLY
            // at scale 1.0, so drifting sideways from there would show bare
            // background at the edge. 2% of 2316px is ~23px of margin each side —
            // comfortably more than the 9px the drift ever uses.
            _hero.Scale = new Vector2(1.02f, 1.02f);
            var tw = CreateTween().SetLoops();
            tw.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            tw.TweenProperty(_hero, "scale", new Vector2(1.075f, 1.075f), 24.0f);
            tw.TweenProperty(_hero, "scale", new Vector2(1.02f, 1.02f), 24.0f);

            var drift = CreateTween().SetLoops();
            drift.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            float x0 = _hero.Position.X;
            // 37s against the 48s zoom, so the two never line up.
            drift.TweenProperty(_hero, "position:x", x0 - 9f, 18.5f);
            drift.TweenProperty(_hero, "position:x", x0 + 9f, 18.5f);
        }).CallDeferred();
    }

    public override void _Process(double delta)
    {
        if (_reduced) return;
        _t += (float)delta;
        var vp = GetViewportRect().Size;
        if (vp.Y <= 0) return;

        for (int i = 0; i < MoteCount; i++)
        {
            var s = _motes[i];
            if (s == null || !IsInstanceValid(s)) continue;
            var p = s.Position;
            p.Y -= _moteSpeed[i] * (float)delta;                       // dust rises in the light
            p.X += Mathf.Sin(_t * 0.19f + _motePhase[i]) * _moteDrift[i] * (float)delta;
            if (p.Y < -20f)
            {
                p.Y = vp.Y + 20f;                                      // wrap, never respawn visibly
                p.X = (float)GD.RandRange(0.0, (double)vp.X);
            }
            if (p.X < -20f) p.X = vp.X + 20f;
            else if (p.X > vp.X + 20f) p.X = -20f;
            s.Position = p;

            // Slow twinkle, each mote on its own clock.
            var c = s.Modulate;
            float baseA = 0.26f;
            c.A = baseA + 0.16f * Mathf.Sin(_t * (0.5f + i * 0.037f) + _motePhase[i]);
            s.Modulate = c;
        }
    }
}

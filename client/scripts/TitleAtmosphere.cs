using Godot;

namespace Runewake.Client;

/// <summary>
/// FABLE-010: the title screen, alive — second attempt, and this time with no
/// hand-written GLSL anywhere in it.
///
/// WHY THE FIRST ATTEMPT SHOWED NOTHING
/// ------------------------------------
/// FABLE-007 built this out of three inline canvas_item shaders. Every check
/// said it worked: the node is in the tree, [TITLE-ATMO] prints, ux_gate is
/// green, and title_test.layout.json records TitleAtmosphere at a correct
/// 2316x1080 with all three ColorRects correctly sized and visible. The screen
/// still looked untouched.
///
/// Proof, from the stamped capture rather than from opinion: I re-implemented
/// the three shaders exactly — same constants, same smoothstep edges — and ran
/// them over hero_art.png. They should shift 71% of the frame by more than four
/// levels, add up to 22% warm light in the upper hall, and crush the corners by
/// 43%. Cropping the same patch of stone out of the real capture and out of that
/// render side by side: the render is visibly brighter and raked with light, the
/// capture is a touch DARKER than the bare painting and has no light in it at
/// all. So the vignette (normal blend) was landing and the two `render_mode
/// blend_add` shaders were producing nothing.
///
/// I am not going to spend another build cycle finding out which driver detail
/// swallowed them, because the project already has a blend mode that is known to
/// work on this device: RitualEffects uses
/// `new CanvasItemMaterial { BlendMode = BlendModeEnum.Add }` in five places and
/// those effects are visible in play. So this version is built out of ordinary
/// nodes, ordinary textures and that one proven material. Nothing here can
/// silently compile to a no-op.
///
/// WHAT IT DRAWS, back to front
/// ----------------------------
///   push-in    the painting breathes 1.045 ↔ 1.08 over 48s and drifts sideways
///              on a 37s cycle, so the two never line up. Never reaches 1.0,
///              where KeepAspectCovered fits exactly and drift would show the
///              bare edge. (This part already worked — the capture shows the
///              hero offset by -23.5,-10.9.)
///   vortex     spiral_core.png turning inside the rune arch, plus a wider,
///              fainter, counter-turning ghost for depth. This is also the fix
///              for the missing rune wheel: Main.cs asks for rune_wheel.png,
///              which does not exist in the repo, so the two rotating wheels it
///              wanted have been silently disabled this whole time.
///   mist       mist_veil.png scrolling across the flooded floor in two ribbons
///              at different speeds and opposite directions.
///   shafts     god-rays from a generated texture, drifting across the hall.
///   motes      forty drifting dust sprites, as before.
///   vignette   a generated radial darkening, normal blend.
///
/// spiral_core.png and mist_veil.png are already in the repo and already in the
/// APK. Nothing referenced them. This costs zero new asset bytes.
///
/// Honours CampaignContext.ReduceMotion: everything holds still at a lower
/// intensity and the push-in does not run.
/// </summary>
public partial class TitleAtmosphere : Control
{
    private const int MoteCount = 40;

    // Where the painted rune circle sits in the 1536x864 source image.
    private const float WheelSrcX = 722f;
    private const float WheelSrcY = 350f;
    private const float SrcW = 1536f;
    private const float SrcH = 864f;

    private TextureRect? _hero;
    private Node2D _fxLayer = default!;
    private Sprite2D? _vortexNear, _vortexFar;
    private Node2D? _mistLow, _mistMid;
    private Sprite2D? _shaftA, _shaftB;
    private readonly Sprite2D[] _motes = new Sprite2D[MoteCount];
    private readonly float[] _moteSpeed = new float[MoteCount];
    private readonly float[] _motePhase = new float[MoteCount];
    private readonly float[] _moteDrift = new float[MoteCount];
    private float _t;
    private bool _reduced;
    private float _vpW, _vpH;

    // ── Construction ─────────────────────────────────────────────────────────

    /// <summary>
    /// Build the atmosphere as the next child of <paramref name="parent"/>.
    /// Call AFTER the background layers and BEFORE any UI — draw order is child
    /// order, so that is what puts the light in front of the painting and behind
    /// the buttons.
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
        // AndOffsets, not SetAnchorsPreset: _Ready runs when we are ALREADY in
        // the tree, and a freshly-new'd node has a 0x0 rect.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _reduced = CampaignContext.ReduceMotion;

        var vp = GetViewportRect().Size;
        _vpW = vp.X > 0 ? vp.X : 2316f;
        _vpH = vp.Y > 0 ? vp.Y : 1080f;

        // One Node2D holds every sprite. Node2D children draw in child order and
        // ignore Control layout entirely, which is what we want for free-floating
        // light — no container can resize or reposition them behind our back.
        _fxLayer = new Node2D { Name = "Fx" };
        AddChild(_fxLayer);

        BuildVortex();
        BuildMist();
        BuildShafts();
        BuildMotes();
        BuildVignette();

        if (!_reduced) StartPushIn();

        GD.Print($"[TITLE-ATMO] v2 nodes-only: vortex={(_vortexNear != null)} "
                 + $"mist={(_mistLow != null)} shafts={(_shaftA != null)} motes={MoteCount} "
                 + $"(reduce_motion={_reduced}, hero={(_hero != null ? "yes" : "none")}, "
                 + $"viewport={_vpW:F0}x{_vpH:F0})");
    }

    /// <summary>The proven additive material — see the class comment.</summary>
    private static CanvasItemMaterial Additive() =>
        new() { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };

    private static Texture2D? TryLoad(string path)
    {
        if (!ResourceLoader.Exists(path))
        {
            GD.PrintErr($"[TITLE-ATMO] missing texture {path} — that layer is skipped");
            return null;
        }
        return ResourceLoader.Load<Texture2D>(path);
    }

    /// <summary>
    /// Where the painted rune circle lands on screen, given KeepAspectCovered.
    /// Same arithmetic Main.cs uses to place the (missing) rune wheel.
    /// </summary>
    private Vector2 ArchCentre()
    {
        float scale = Mathf.Max(_vpW / SrcW, _vpH / SrcH);
        float offX = (SrcW * scale - _vpW) / 2f;
        float offY = (SrcH * scale - _vpH) / 2f;
        return new Vector2(WheelSrcX * scale - offX, WheelSrcY * scale - offY);
    }

    // ── Layers ───────────────────────────────────────────────────────────────

    private void BuildVortex()
    {
        var tex = TryLoad("res://content/art/title/layers/spiral_core.png");
        if (tex == null) return;

        var centre = ArchCentre();
        float texW = tex.GetWidth();

        // Wide, faint, slow, counter-turning — reads as depth behind the arch.
        _vortexFar = new Sprite2D
        {
            Texture = tex,
            Centered = true,
            Position = centre,
            Scale = Vector2.One * (_vpW * 0.62f / texW),
            Modulate = new Color(0.52f, 0.80f, 0.84f, _reduced ? 0.10f : 0.20f),
            Material = Additive(),
        };
        _fxLayer.AddChild(_vortexFar);

        // Tight, brighter, the actual eye of the portal.
        _vortexNear = new Sprite2D
        {
            Texture = tex,
            Centered = true,
            Position = centre,
            Scale = Vector2.One * (_vpW * 0.34f / texW),
            Modulate = new Color(0.70f, 0.93f, 0.95f, _reduced ? 0.22f : 0.42f),
            Material = Additive(),
        };
        _fxLayer.AddChild(_vortexNear);
    }

    /// <summary>
    /// A scrolling ribbon. THREE copies, mirrored in the middle: normal, flipped,
    /// normal. _Process slides the strip left and wraps it every 2 screens.
    ///
    /// Two copies is the obvious build and it is wrong twice over. The strip only
    /// spans 2 screens, so by the time it has travelled 2 screens the tail has
    /// left a gap; and wrapping after ONE screen would snap the mirrored copy
    /// into the place of the unmirrored one, which is a visible jump because they
    /// are different pictures. With three, the screen is covered for the whole
    /// travel and the copy showing at the wrap point is identical to the copy
    /// showing at the start, so the reset is invisible.
    ///
    /// Also deliberately not a looping Tween: a Tween that targets an absolute
    /// position has nothing left to travel on its second lap.
    /// </summary>
    private Node2D? MakeScrollBand(Texture2D? tex, float centreY, float height,
                                   float alpha, Color tint)
    {
        if (tex == null) return null;
        var band = new Node2D();
        float sx = _vpW / tex.GetWidth();
        float sy = height / tex.GetHeight();
        for (int i = 0; i < 3; i++)
        {
            var s = new Sprite2D
            {
                Texture = tex,
                Centered = true,
                Position = new Vector2(_vpW * (0.5f + i), centreY),
                Scale = new Vector2(i == 1 ? -sx : sx, sy),   // mirror only the middle
                Modulate = new Color(tint.R, tint.G, tint.B, alpha),
                Material = Additive(),
            };
            band.AddChild(s);
        }
        _fxLayer.AddChild(band);
        return band;
    }

    private void BuildMist()
    {
        var tex = TryLoad("res://content/art/title/layers/mist_veil.png");
        if (tex == null) return;
        var cool = new Color(0.78f, 0.86f, 0.92f);
        _mistLow = MakeScrollBand(tex, _vpH * 0.84f, _vpH * 0.46f, _reduced ? 0.14f : 0.26f, cool);
        _mistMid = MakeScrollBand(tex, _vpH * 0.62f, _vpH * 0.34f, _reduced ? 0.08f : 0.15f, cool);
    }

    /// <summary>
    /// God-rays as a generated texture rather than a fragment shader. Small —
    /// 256x128, stretched over the hall — because it is all soft gradients and
    /// nobody can see the resolution once it is blurred across 2316px.
    /// </summary>
    private static ImageTexture MakeShaftTexture(int w = 256, int h = 128)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (int y = 0; y < h; y++)
        {
            float v = (float)y / (h - 1);
            float vert = Mathf.Clamp(1f - v * 1.15f, 0f, 1f);       // dies before the floor
            vert *= vert;
            for (int x = 0; x < w; x++)
            {
                float u = (float)x / (w - 1);
                float px = u * 0.913f - v * 0.408f;                  // the same 0.42rad rake
                float acc = 0f;
                acc += Band(Mathf.Sin(px * 7.0f), 0.62f) * 0.50f;
                acc += Band(Mathf.Sin(px * 11.3f + 1.7f), 0.66f) * 0.32f;
                acc += Band(Mathf.Sin(px * 17.9f + 4.1f), 0.70f) * 0.20f;
                acc += Band(Mathf.Sin(px * 26.4f + 2.3f), 0.74f) * 0.12f;
                float edge = Mathf.Clamp(u / 0.22f, 0f, 1f) * Mathf.Clamp((1f - u) / 0.22f, 0f, 1f);
                float a = Mathf.Clamp(acc * vert * edge, 0f, 1f);
                img.SetPixel(x, y, new Color(1f, 0.94f, 0.78f, a));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    private static float Band(float s, float edge)
    {
        float t = Mathf.Clamp((s - edge) / (1f - edge), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private void BuildShafts()
    {
        var tex = MakeShaftTexture();
        float w = _vpW * 1.35f, h = _vpH * 0.95f;

        _shaftA = new Sprite2D
        {
            Texture = tex,
            Centered = true,
            Position = new Vector2(_vpW * 0.5f, _vpH * 0.42f),
            Scale = new Vector2(w / tex.GetWidth(), h / tex.GetHeight()),
            Modulate = new Color(1f, 1f, 1f, _reduced ? 0.30f : 0.55f),
            Material = Additive(),
        };
        _fxLayer.AddChild(_shaftA);

        // A second pass at a different scale and speed so the rake never repeats.
        _shaftB = new Sprite2D
        {
            Texture = tex,
            Centered = true,
            Position = new Vector2(_vpW * 0.5f, _vpH * 0.38f),
            Scale = new Vector2(-w * 0.8f / tex.GetWidth(), h * 1.05f / tex.GetHeight()),
            Modulate = new Color(1f, 0.96f, 0.86f, _reduced ? 0.16f : 0.30f),
            Material = Additive(),
        };
        _fxLayer.AddChild(_shaftB);
    }

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
                a = a * a * a;
                img.SetPixel(x, y, new Color(1f, 0.97f, 0.88f, a));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    private void BuildMotes()
    {
        var tex = MakeDotTexture();
        var rng = new RandomNumberGenerator { Seed = 20260919 };
        for (int i = 0; i < MoteCount; i++)
        {
            float scale = rng.RandfRange(0.14f, 0.55f);
            var s = new Sprite2D
            {
                Texture = tex,
                Centered = true,
                Position = new Vector2(rng.RandfRange(0, _vpW), rng.RandfRange(0, _vpH)),
                Scale = new Vector2(scale, scale),
                Modulate = new Color(1f, 0.98f, 0.90f,
                                     rng.RandfRange(0.18f, 0.60f) * (_reduced ? 0.6f : 1f)),
                Material = Additive(),
            };
            _fxLayer.AddChild(s);
            _motes[i] = s;
            _moteSpeed[i] = rng.RandfRange(5f, 18f) * scale;
            _motePhase[i] = rng.RandfRange(0f, Mathf.Tau);
            _moteDrift[i] = rng.RandfRange(6f, 22f);
        }
    }

    /// <summary>
    /// Radial darkening as a generated texture on a normal-blend TextureRect.
    /// Small and stretched — it is one smooth gradient.
    /// </summary>
    private static ImageTexture MakeVignetteTexture(int w = 128, int h = 72)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (int y = 0; y < h; y++)
        {
            float v = (float)y / (h - 1) - 0.5f;
            for (int x = 0; x < w; x++)
            {
                float u = (float)x / (w - 1) - 0.5f;
                float r = Mathf.Sqrt(u * u + (v * 1.12f) * (v * 1.12f)) * 1.42f;
                float t = Mathf.Clamp((r - 0.40f) / 0.60f, 0f, 1f);
                img.SetPixel(x, y, new Color(0.02f, 0.02f, 0.03f, t * t * (3f - 2f * t) * 0.62f));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>
    /// The shade behind the wordmark: an elliptical pool that fades to nothing
    /// at every edge, so there is no bar ruled across the architecture. Public
    /// because Main.cs builds the title block itself.
    /// </summary>
    public static ImageTexture MakeTitleShadeTexture(int w = 160, int h = 80)
    {
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (int y = 0; y < h; y++)
        {
            float v = ((float)y / (h - 1) - 0.42f) * 2.05f;
            for (int x = 0; x < w; x++)
            {
                float u = ((float)x / (w - 1) - 0.5f) * 2.35f;
                float r = Mathf.Sqrt(u * u + v * v);
                float t = Mathf.Clamp(1f - r, 0f, 1f);
                t = t * t * (3f - 2f * t);
                img.SetPixel(x, y, new Color(0.04f, 0.03f, 0.02f, t * 0.72f));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    private void BuildVignette()
    {
        var rect = new TextureRect
        {
            Texture = MakeVignetteTexture(),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(rect);
        rect.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    // ── Motion ───────────────────────────────────────────────────────────────

    private void StartPushIn()
    {
        if (_hero == null || !IsInstanceValid(_hero)) return;
        Callable.From(() =>
        {
            if (_hero == null || !IsInstanceValid(_hero)) return;
            _hero.PivotOffset = _hero.Size / 2f;
            // A permanent overscan: KeepAspectCovered fits the viewport EXACTLY
            // at 1.0, so drifting from there would show bare background.
            _hero.Scale = new Vector2(1.045f, 1.045f);
            var zoom = CreateTween().SetLoops();
            zoom.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            zoom.TweenProperty(_hero, "scale", new Vector2(1.08f, 1.08f), 24.0f);
            zoom.TweenProperty(_hero, "scale", new Vector2(1.045f, 1.045f), 24.0f);

            var drift = CreateTween().SetLoops();
            drift.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            float x0 = _hero.Position.X;
            drift.TweenProperty(_hero, "position:x", x0 - 11f, 18.5f);
            drift.TweenProperty(_hero, "position:x", x0 + 11f, 18.5f);
        }).CallDeferred();
    }

    private static void Scroll(Node2D? band, float speed, float width, float delta)
    {
        if (band == null || !IsInstanceValid(band)) return;
        float x = band.Position.X + speed * delta;
        float span = width * 2f;
        // Keep it in (-span, 0] so the mirrored pair always covers the screen.
        while (x <= -span) x += span;
        while (x > 0f) x -= span;
        band.Position = new Vector2(x, band.Position.Y);
    }

    public override void _Process(double delta)
    {
        if (_reduced) return;
        float dt = (float)delta;
        _t += dt;

        if (_vortexNear != null && IsInstanceValid(_vortexNear))
        {
            _vortexNear.Rotation += 0.045f * dt;                       // ~140s a turn
            float pulse = 0.42f + 0.10f * Mathf.Sin(_t * 0.23f);
            var c = _vortexNear.Modulate; c.A = pulse; _vortexNear.Modulate = c;
        }
        if (_vortexFar != null && IsInstanceValid(_vortexFar))
        {
            _vortexFar.Rotation -= 0.021f * dt;                        // counter, slower
            float pulse = 0.20f + 0.05f * Mathf.Sin(_t * 0.17f + 1.3f);
            var c = _vortexFar.Modulate; c.A = pulse; _vortexFar.Modulate = c;
        }

        Scroll(_mistLow, -14f, _vpW, dt);
        Scroll(_mistMid, 9f, _vpW, dt);

        if (_shaftA != null && IsInstanceValid(_shaftA))
        {
            _shaftA.Position = new Vector2(
                _vpW * 0.5f + Mathf.Sin(_t * 0.045f) * _vpW * 0.055f, _shaftA.Position.Y);
            var c = _shaftA.Modulate; c.A = 0.55f + 0.10f * Mathf.Sin(_t * 0.11f); _shaftA.Modulate = c;
        }
        if (_shaftB != null && IsInstanceValid(_shaftB))
        {
            _shaftB.Position = new Vector2(
                _vpW * 0.5f + Mathf.Sin(_t * 0.031f + 2.1f) * _vpW * 0.075f, _shaftB.Position.Y);
            var c = _shaftB.Modulate; c.A = 0.30f + 0.08f * Mathf.Sin(_t * 0.07f + 0.9f); _shaftB.Modulate = c;
        }

        for (int i = 0; i < MoteCount; i++)
        {
            var s = _motes[i];
            if (s == null || !IsInstanceValid(s)) continue;
            var p = s.Position;
            p.Y -= _moteSpeed[i] * dt;                                  // dust rises in the light
            p.X += Mathf.Sin(_t * 0.19f + _motePhase[i]) * _moteDrift[i] * dt;
            if (p.Y < -20f)
            {
                p.Y = _vpH + 20f;
                p.X = (float)GD.RandRange(0.0, (double)_vpW);
            }
            if (p.X < -20f) p.X = _vpW + 20f;
            else if (p.X > _vpW + 20f) p.X = -20f;
            s.Position = p;

            var c = s.Modulate;
            c.A = 0.34f + 0.20f * Mathf.Sin(_t * (0.5f + i * 0.037f) + _motePhase[i]);
            s.Modulate = c;
        }
    }
}

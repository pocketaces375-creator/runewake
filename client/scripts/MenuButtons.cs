using System.Collections.Generic;
using Godot;

namespace Runewake.Client;

/// <summary>
/// FABLE-007: the menu's buttons, in the game's own language.
///
/// They were flat brown rectangles with a 1px grey edge and a 4px radius —
/// a debug UI sitting in front of a painting. These are carved plates: a dark
/// warm stone face, a bronze keyline that lifts to gold under the finger, a
/// dropped shadow so they sit ON the wall rather than in it, and a highlight
/// along the top edge where light would catch a cut stone.
///
/// The motion is deliberately small. A menu button that leaps is a toy; one that
/// settles a pixel and warms half a shade is a heavy thing being pressed.
/// </summary>
public static class MenuButtons
{
    // ── Palette (matches ThemeTokens' stone/gold family) ──
    private static readonly Color FaceNormal = Color.FromHtml("#332E29");
    private static readonly Color FaceHover = Color.FromHtml("#413A33");
    private static readonly Color FacePressed = Color.FromHtml("#241F1B");
    private static readonly Color EdgeNormal = Color.FromHtml("#6E5E3C");
    private static readonly Color EdgeHover = Color.FromHtml("#D9B75A");
    private static readonly Color EdgePressed = Color.FromHtml("#A98A33");

    private const int Radius = 7;
    private const int PadX = 18;
    private const int PadY = 16;

    private const int TexSize = 72;      // nine-patch source
    private const int TexMargin = 22;    // corner size kept unstretched

    /// <summary>
    /// FABLE-010: draw the plate rather than describe it.
    ///
    /// StyleBoxFlat can do a flat fill, one border colour and a drop shadow, and
    /// that is the whole vocabulary — so a "carved" plate made from one is really
    /// a rounded rectangle with a line round it. Cut stone reads as carved
    /// because of what happens in the first few pixels inside the edge: light
    /// catching along the top lip, shadow pooling along the bottom, the face
    /// falling off as it recedes. That needs a gradient and two inner bevels,
    /// which means a texture.
    ///
    /// So this paints a nine-patch by hand — 72x72, corners fixed, middle
    /// stretched — and hands it to a StyleBoxTexture. Generated at startup from
    /// a few hundred lines of arithmetic: no new asset bytes, and no shader that
    /// can quietly fail to compile the way FABLE-007's did.
    /// </summary>
    // FABLE-019c: the plates were opaque slabs sitting ON the hall. Now they are
    // smoked glass: the vortex and the mist read through the face, the keyline
    // and lip stay solid, so the menu belongs to the scene instead of covering it.
    private static ImageTexture PlateTexture(Color faceTop, Color faceBottom, Color rim, float rimWidth, float faceAlpha)
    {
        int n = TexSize;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        float half = n / 2f, radius = 13f;
        var rng = new RandomNumberGenerator { Seed = 7717 };

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                // Distance inside a rounded rectangle: positive inwards.
                float qx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - (half - radius), 0f);
                float qy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - (half - radius), 0f);
                float inside = radius - Mathf.Sqrt(qx * qx + qy * qy);

                if (inside <= -1f)
                {
                    img.SetPixel(x, y, new Color(0, 0, 0, 0));
                    continue;
                }

                float t = Mathf.Clamp(y / (float)(n - 1), 0f, 1f);
                var c = faceTop.Lerp(faceBottom, t * t * 0.85f + t * 0.15f);

                // Stone grain — just enough to stop the face reading as plastic.
                float grain = (rng.Randf() - 0.5f) * 0.022f;
                c = new Color(c.R + grain, c.G + grain, c.B + grain, 1f);

                // Light along the top lip, shadow pooling at the bottom.
                bool upper = y < n / 2;
                float lip = Mathf.Clamp((4.5f - inside) / 4.5f, 0f, 1f);
                if (inside < 4.5f && inside > 0f)
                {
                    if (upper) c = c.Lerp(new Color(0.80f, 0.70f, 0.46f), lip * 0.55f);
                    else c = c.Lerp(new Color(0.05f, 0.04f, 0.03f), lip * 0.55f);
                }

                // The keyline itself.
                if (inside < rimWidth)
                {
                    float k = Mathf.Clamp(inside / rimWidth, 0f, 1f);
                    c = c.Lerp(rim, 1f - k * 0.35f);
                }

                float alpha = Mathf.Clamp(inside + 1f, 0f, 1f);   // one soft pixel of AA
                // Face is glass; the rim and the lip are solid stone/gold.
                float solid = Mathf.Clamp((rimWidth + 3.5f - inside) / 3.5f, 0f, 1f);
                alpha *= Mathf.Lerp(faceAlpha, 1f, solid);
                img.SetPixel(x, y, new Color(c.R, c.G, c.B, alpha));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    private static StyleBox Plate(Color faceTop, Color faceBottom, Color rim, float rimWidth, float faceAlpha)
    {
        var box = new StyleBoxTexture
        {
            Texture = PlateTexture(faceTop, faceBottom, rim, rimWidth, faceAlpha),
            ContentMarginLeft = PadX,
            ContentMarginRight = PadX,
            ContentMarginTop = PadY,
            ContentMarginBottom = PadY,
        };
        box.SetTextureMargin(Side.Left, TexMargin);
        box.SetTextureMargin(Side.Right, TexMargin);
        box.SetTextureMargin(Side.Top, TexMargin);
        box.SetTextureMargin(Side.Bottom, TexMargin);
        return box;
    }

    /// <summary>FABLE-031: the one primary plate on the title — a warmer face and a gold rim.</summary>
    public static StyleBox PrimaryNormal() =>
        Plate(Color.FromHtml("#5A4A22"), Color.FromHtml("#2A2112"), Color.FromHtml("#D9B75A"), 3.0f, 0.62f);
    public static StyleBox PrimaryHover() =>
        Plate(Color.FromHtml("#6B5828"), Color.FromHtml("#332816"), Color.FromHtml("#F0D27A"), 3.4f, 0.82f);
    /// <summary>FABLE-031: a slimmer, quieter plate for the secondary row.</summary>
    public static StyleBox QuietNormal() =>
        Plate(Color.FromHtml("#2E2822"), Color.FromHtml("#16130F"), Color.FromHtml("#7E6A3C"), 1.8f, 0.46f);

    public static StyleBox Normal() =>
        Plate(Color.FromHtml("#3B342C"), Color.FromHtml("#1A1613"), Color.FromHtml("#9E8447"), 2.2f, 0.58f);   // FABLE-019d: a little more glass

    public static StyleBox Hover() =>
        Plate(Color.FromHtml("#4A4136"), Color.FromHtml("#2A241E"), EdgeHover, 3.0f, 0.80f);

    /// <summary>Pressed: the face darkens and the lip loses its light, so it sinks.</summary>
    public static StyleBox Pressed() =>
        Plate(Color.FromHtml("#1E1A16"), Color.FromHtml("#2C2621"), EdgePressed, 2.4f, 0.90f);

    /// <summary>
    /// Give a button its hover/press feel.
    ///
    /// Tint only — no geometry. These plates live inside a GridContainer and a
    /// VBoxContainer, and a container rewrites its children's position on every
    /// sort. An earlier version tweened position:y and cached a "rest" value on
    /// first hover; any re-sort after that (a status label changing length, a
    /// rotation, a resize) would have left a plate permanently displaced.
    ///
    /// It also uses SELF_modulate, not modulate, because RevealStagger owns
    /// modulate:a for the first second of the menu's life. Two tweens on one
    /// property is a flicker.
    /// </summary>
    public static void Animate(Button btn)
    {
        if (btn == null) return;
        Tween? tint = null;

        void To(Color c, float secs)
        {
            if (CampaignContext.ReduceMotion) { btn.SelfModulate = c; return; }
            tint?.Kill();                       // one tween per button, always
            tint = btn.CreateTween();
            tint.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
            tint.TweenProperty(btn, "self_modulate", c, secs);
        }

        btn.MouseEntered += () => To(new Color(1.12f, 1.08f, 1.00f), 0.14f);
        btn.MouseExited += () => To(Colors.White, 0.18f);
        btn.ButtonDown += () => { tint?.Kill(); btn.SelfModulate = new Color(0.88f, 0.86f, 0.82f); };
        btn.ButtonUp += () => To(Colors.White, 0.16f);
    }

    /// <summary>
    /// Entrance: each item fades up, one after another, so the screen assembles
    /// itself rather than appearing all at once.
    ///
    /// Alpha only, deliberately. These buttons live inside containers, and a
    /// container owns its children's position AND their offsets — it rewrites
    /// both on every layout pass. Anything that animated geometry here would be
    /// silently stamped out, or worse, fight the container for a few frames.
    /// Modulate is the one property no container touches.
    /// </summary>
    public static void RevealStagger(IEnumerable<Control> items, float step = 0.07f)
    {
        int i = 0;
        foreach (var c in items)
        {
            if (c == null || !GodotObject.IsInstanceValid(c)) continue;
            if (CampaignContext.ReduceMotion)
            {
                c.Modulate = Colors.White;
                i++;
                continue;
            }
            var faded = c.Modulate;
            faded.A = 0f;
            c.Modulate = faded;
            var t = c.CreateTween();
            t.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            t.TweenProperty(c, "modulate:a", 1f, 0.45f).SetDelay(0.10f + i * step);
            i++;
        }
    }

    /// <summary>
    /// The wordmark, breathing. A very slow warm pulse — visible over ten
    /// seconds, invisible over one.
    /// </summary>
    public static void BreatheTitle(Control title)
    {
        if (title == null || CampaignContext.ReduceMotion) return;
        var t = title.CreateTween().SetLoops();
        t.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        t.TweenProperty(title, "modulate", new Color(1.22f, 1.14f, 0.92f), 2.6f);   // FABLE-019c: visible breath
        t.TweenProperty(title, "modulate", Colors.White, 2.6f);
    }
}

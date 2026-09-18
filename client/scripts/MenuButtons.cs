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

    private static StyleBoxFlat Plate(Color face, Color edge, int border, int shadow, int lift)
    {
        return new StyleBoxFlat
        {
            BgColor = face,
            BorderColor = edge,
            BorderWidthLeft = border,
            BorderWidthRight = border,
            BorderWidthTop = border,
            BorderWidthBottom = border,
            CornerRadiusTopLeft = Radius,
            CornerRadiusTopRight = Radius,
            CornerRadiusBottomLeft = Radius,
            CornerRadiusBottomRight = Radius,
            ContentMarginLeft = PadX,
            ContentMarginRight = PadX,
            ContentMarginTop = PadY,
            ContentMarginBottom = PadY,
            ShadowColor = new Color(0f, 0f, 0f, 0.55f),
            ShadowSize = shadow,
            ShadowOffset = new Vector2(0, lift),
        };
    }

    public static StyleBoxFlat Normal() => Plate(FaceNormal, EdgeNormal, 2, 10, 4);
    public static StyleBoxFlat Hover() => Plate(FaceHover, EdgeHover, 2, 14, 5);

    /// <summary>Pressed: shadow almost gone and no offset, so the plate sinks in.</summary>
    public static StyleBoxFlat Pressed() => Plate(FacePressed, EdgePressed, 2, 3, 1);

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
        t.TweenProperty(title, "modulate", new Color(1.13f, 1.08f, 0.94f), 4.5f);
        t.TweenProperty(title, "modulate", Colors.White, 4.5f);
    }
}

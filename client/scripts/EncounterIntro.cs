using System;
using Godot;
using Runewake.Engine.Cards;

namespace Runewake.Client;

/// <summary>
/// FABLE-068: who you are about to fight, before the fight. Encounters carry a portrait and intro lines the
/// duel never showed; the class starting zones add a one-idea lesson per location. On those fights this card
/// shows (until the node is cleared): the foe's portrait and name, two lines of who they are, and the lesson
/// in its own box. Tap anywhere to fight.
/// </summary>
public partial class EncounterIntro : Control
{
    private Action? _onClosed;
    private bool _closing;

    private static readonly Color Parchment = new(0.92f, 0.86f, 0.74f);
    private static readonly Color Gold = new(0.95f, 0.80f, 0.42f);

    /// <summary>Should a fight against <paramref name="enc"/> on <paramref name="nodeId"/> open with the card?</summary>
    public static bool ShouldShow(EncounterDef? enc, string? nodeId)
    {
        if (enc == null) return false;
        foreach (var a in OS.GetCmdlineArgs()) if (a.StartsWith("--introtest=")) return true;
        if (CampaignContext.SoakActive || CampaignContext.AutoCaptureScreenshot || CampaignContext.LoopSmokeTest
            || CampaignContext.BotDuelTest || CampaignContext.UxWalk) return false;
        if (!string.IsNullOrEmpty(nodeId) && CampaignContext.Progression?.IsNodeCleared(nodeId!) == true) return false;
        // the class starting zones (each fight teaches one thing); later regions' dialogue is still placeholder text
        return enc.Lesson is { Count: >= 2 };
    }

    public static EncounterIntro Show(Node parent, EncounterDef enc, string? zoneName, Action? onClosed = null)
    {
        // its own layer, above the combat effects (layer 20) — the opening turn banner waits underneath
        var layer = new CanvasLayer { Name = "EncounterIntroLayer", Layer = 30 };
        parent.AddChild(layer);
        var intro = new EncounterIntro { Name = "EncounterIntro", _onClosed = onClosed };
        layer.AddChild(intro);
        intro.Build(enc, zoneName);
        return intro;
    }

    private void Build(EncounterDef enc, string? zoneName)
    {
        var vp = GetViewportRect().Size;
        float s = Mathf.Max(0.55f, vp.Y / 1080f);
        Position = Vector2.Zero;
        Size = vp;
        MouseFilter = MouseFilterEnum.Stop;

        var veil = new ColorRect { Color = new Color(0, 0, 0, 0.74f), Size = vp, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(veil);

        bool hasLesson = enc.Lesson is { Count: >= 2 };
        float pw = Mathf.Min(vp.X - 40 * s, 1240 * s);
        float ph = (hasLesson ? 640 : 480) * s;
        var panel = new Panel { Size = new Vector2(pw, ph), Position = (vp - new Vector2(pw, ph)) / 2f, MouseFilter = MouseFilterEnum.Ignore };
        panel.PivotOffset = panel.Size / 2f;
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.075f, 0.062f, 0.05f, 0.97f), BorderColor = Gold,
            BorderWidthLeft = (int)(3 * s), BorderWidthTop = (int)(3 * s), BorderWidthRight = (int)(3 * s), BorderWidthBottom = (int)(3 * s),
            CornerRadiusTopLeft = (int)(18 * s), CornerRadiusTopRight = (int)(18 * s), CornerRadiusBottomLeft = (int)(18 * s), CornerRadiusBottomRight = (int)(18 * s),
            ShadowColor = new Color(0, 0, 0, 0.7f), ShadowSize = (int)(30 * s),
        });
        AddChild(panel);

        // portrait (the foe's class art, a tall card), in a gold frame
        float pad = 30 * s, portW = 230 * s, portH = (hasLesson ? 330 : 400) * s;
        float port = portH;    // the text column is as tall as the portrait
        var frame = new Panel { Position = new Vector2(pad, pad), Size = new Vector2(portW + 10 * s, portH + 10 * s), MouseFilter = MouseFilterEnum.Ignore, ClipContents = true };
        frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.02f, 0.02f, 0.02f, 1f), BorderColor = Gold,
            BorderWidthLeft = (int)(3 * s), BorderWidthTop = (int)(3 * s), BorderWidthRight = (int)(3 * s), BorderWidthBottom = (int)(3 * s),
            CornerRadiusTopLeft = (int)(10 * s), CornerRadiusTopRight = (int)(10 * s), CornerRadiusBottomLeft = (int)(10 * s), CornerRadiusBottomRight = (int)(10 * s),
        });
        panel.AddChild(frame);
        var tex = PortraitFor(enc);
        if (tex != null)
        {
            var art = new TextureRect { MouseFilter = MouseFilterEnum.Ignore };
            art.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;          // before the size, or the texture's own size wins
            art.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
            art.Texture = tex;
            frame.AddChild(art);
            art.Position = new Vector2(5 * s, 5 * s);
            art.Size = new Vector2(portW, portH);
        }

        float tx = pad * 2 + portW + 10 * s, tw = pw - tx - pad;
        float y = pad;
        string kicker = enc.Modifier switch { "ELITE" => "ELITE", "WARDEN" => "WARDEN", _ => enc.OpeningRule != null ? "BOSS" : "CHALLENGER" };
        if (!string.IsNullOrEmpty(zoneName)) kicker += "  ·  " + zoneName!.ToUpperInvariant();
        y += Text(panel, kicker, tx, y, tw, ThemeTokens.GetHeaderFont((int)(22 * s)), 22 * s, new Color(0.75f, 0.68f, 0.52f)) + 4 * s;
        float nameSize = (enc.Name.Length > 24 ? 40 : enc.Name.Length > 18 ? 46 : 52) * s;
        y += Text(panel, enc.Name, tx, y, tw, ThemeTokens.GetCardNameFont((int)nameSize), nameSize, Gold) + 12 * s;
        if (enc.DialogueIntro is { Count: > 0 } lines)
        {
            string body = string.Join("\n", lines.Count > 2 ? lines.GetRange(lines.Count - 2, 2) : lines);
            Text(panel, body, tx, y, tw, ThemeTokens.GetBodyFont((int)(27 * s)), 27 * s, Parchment, wrap: true, height: port - (y - pad));
        }

        if (hasLesson)
        {
            float ly = pad + portH + 30 * s, lh = ph - ly - 70 * s;
            var box = new Panel { Position = new Vector2(pad, ly), Size = new Vector2(pw - pad * 2, lh), MouseFilter = MouseFilterEnum.Ignore };
            box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.16f, 0.13f, 0.08f, 0.9f), BorderColor = new Color(Gold, 0.7f),
                BorderWidthLeft = (int)(6 * s), BorderWidthTop = (int)(1 * s), BorderWidthRight = (int)(1 * s), BorderWidthBottom = (int)(1 * s),
                CornerRadiusTopLeft = (int)(10 * s), CornerRadiusTopRight = (int)(10 * s), CornerRadiusBottomLeft = (int)(10 * s), CornerRadiusBottomRight = (int)(10 * s),
            });
            panel.AddChild(box);
            float by = 14 * s;
            by += Text(box, "LESSON  ·  " + enc.Lesson![0], 26 * s, by, box.Size.X - 46 * s, ThemeTokens.GetHeaderFont((int)(26 * s)), 26 * s, Gold) + 6 * s;
            Text(box, enc.Lesson[1], 26 * s, by, box.Size.X - 46 * s, ThemeTokens.GetBodyFont((int)(25 * s)), 25 * s, Parchment, wrap: true, height: lh - by - 10 * s);
        }

        var tap = new Label { Text = "Tap to fight", HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        tap.AddThemeFontOverride("font", ThemeTokens.GetHeaderFont((int)(28 * s)));
        tap.AddThemeFontSizeOverride("font_size", (int)(28 * s));
        tap.AddThemeColorOverride("font_color", Gold);
        tap.Position = new Vector2(0, ph - 60 * s);
        tap.Size = new Vector2(pw, 40 * s);
        panel.AddChild(tap);
        var pulse = tap.CreateTween().SetLoops();
        pulse.TweenProperty(tap, "modulate:a", 0.35f, 0.8f).SetTrans(Tween.TransitionType.Sine);
        pulse.TweenProperty(tap, "modulate:a", 1f, 0.8f).SetTrans(Tween.TransitionType.Sine);

        // entrance: the veil fades in, the card rises and settles
        Modulate = new Color(1, 1, 1, 0);
        panel.Scale = new Vector2(0.92f, 0.92f);
        var tw2 = CreateTween().SetParallel();
        tw2.TweenProperty(this, "modulate:a", 1f, 0.25f);
        tw2.TweenProperty(panel, "scale", Vector2.One, 0.35f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    private static Texture2D? PortraitFor(EncounterDef enc)
    {
        foreach (var path in new[] { enc.Portrait, string.IsNullOrEmpty(enc.Class) ? null : $"res://content/art/classes/{enc.Class!.ToLowerInvariant()}.webp",
                                     "res://content/art/card_back.webp" })
            if (!string.IsNullOrEmpty(path) && ResourceLoader.Exists(path)) return GD.Load<Texture2D>(path);
        return null;
    }

    /// <summary>Add a label; returns the height it takes.</summary>
    private static float Text(Control parent, string text, float x, float y, float w, Font? font, float size, Color colour, bool wrap = false, float height = 0)
    {
        var l = new Label { Text = text, Position = new Vector2(x, y), MouseFilter = MouseFilterEnum.Ignore };
        if (font != null) l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", (int)size);
        l.AddThemeColorOverride("font_color", colour);
        if (wrap)
        {
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.Size = new Vector2(w, Mathf.Max(height, size * 1.4f));
            l.ClipText = true;
        }
        else
        {
            l.Size = new Vector2(w, size * 1.3f);
            l.ClipText = true;
            l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        }
        parent.AddChild(l);
        return wrap ? l.Size.Y : size * 1.3f;
    }

    public override void _GuiInput(InputEvent e)
    {
        bool tapped = e is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } || e is InputEventScreenTouch { Pressed: false };
        if (!tapped || _closing) return;
        AcceptEvent();
        Close();
    }

    public void Close()
    {
        if (_closing) return;
        _closing = true;
        GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("click");
        var tw = CreateTween();
        tw.TweenProperty(this, "modulate:a", 0f, 0.2f);
        tw.TweenCallback(Callable.From(() => { _onClosed?.Invoke(); (GetParent() as CanvasLayer ?? (Node)this).QueueFree(); }));
    }
}

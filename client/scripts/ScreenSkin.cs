using System.Collections.Generic;
using Godot;

namespace Runewake.Client;

/// <summary>
/// FABLE-084/088: Trikzos's picked screens in the game.
///  * Faithful screens (content/art/screens/&lt;Scene&gt;_ui.webp): the approved painting itself, its words scraped off, is
///    the screen. Panels and buttons drawn in code turn see-through so the painted ones show; words that sit on light
///    parchment switch to dark ink. Settings and Reliquary lay themselves out on the painting (Art()); Deck Forge and
///    Daily Rewards already line up with theirs, so their painting is stretched to the screen.
///  * Older plate skin (&lt;Scene&gt;.webp + panel.png / plaque.png) for the screens that have no faithful painting yet.
/// Anything missing is simply not applied.
/// </summary>
public partial class ScreenSkin : Node
{
    private const string Dir = "res://content/art/screens/";
    private static readonly string[] Skinned = { "SettingsScene", "RewardsScene", "DeckBuilderScene", "OnlineLobbyScene", "MapScene", "ReliquaryScene", "PackShopScene" };
    /// <summary>Faithful paintings shown whole (letterboxed with a dim copy of themselves), not stretched.</summary>
    private static readonly string[] FitScenes = { "SettingsScene", "ReliquaryScene" };
    private static readonly Dictionary<string, Texture2D?> Cache = new();
    private static readonly Dictionary<string, Image?> Pixels = new();
    private Node? _scene;
    private bool _on;
    // FABLE-095: nodes added this frame are walked together, once, after one short wait - one timer and one walk per
    // node made every Deck Forge tap (hundreds of nodes) a hitch of hundreds of timers and thousands of passes
    private readonly List<Node> _added = new();
    private bool _walkPending;

    public override void _Ready() => GetTree().NodeAdded += n =>
    {
        if (!_on || _scene == null || !IsInstanceValid(_scene) || n is not Control || !_scene.IsAncestorOf(n)) return;
        _added.Add(n);
        if (_walkPending) return;
        _walkPending = true;
        GetTree().CreateTimer(0.06).Timeout += () =>
        {
            _walkPending = false;
            var batch = new HashSet<Node>(_added); _added.Clear();
            if (_scene == null || !IsInstanceValid(_scene)) return;
            foreach (var a in batch)
            {
                if (!IsInstanceValid(a) || !a.IsInsideTree()) continue;
                bool covered = false;   // a node whose ancestor is also in the batch is walked with that ancestor
                for (var p = a.GetParent(); p != null && p != _scene; p = p.GetParent())
                    if (batch.Contains(p)) { covered = true; break; }
                if (!covered) Walk(a, _scene);
            }
        };
    };

    public override void _Process(double delta)
    {
        var s = GetTree().CurrentScene;
        if (s == _scene) return;
        _scene = s;
        _on = s != null && System.Array.IndexOf(Skinned, Key(s)) >= 0
              && (Faithful(Key(s)) || Tex("panel.png") != null || Tex("plaque.png") != null || Tex(Key(s) + ".webp") != null);
        if (_on) Callable.From(() => Apply(s!)).CallDeferred();
    }

    private static string Key(Node? s) => (s?.SceneFilePath ?? "").GetFile().GetBaseName();

    private static Texture2D? Tex(string f)
    {
        if (!Cache.TryGetValue(f, out var t)) Cache[f] = t = ResourceLoader.Exists(Dir + f) ? GD.Load<Texture2D>(Dir + f) : null;
        return t;
    }

    /// <summary>True when the scene has its approved painting installed.</summary>
    public static bool Faithful(string key) => FaithfulTex(key) != null;

    /// <summary>
    /// Which approved painting a scene has: "words" (FABLE-097: the painting with its own lettering kept - only what
    /// changes was erased, the game draws just the live values), "tree" (the World-Tree Settings), "ui" (every word
    /// erased, the game draws them all), or "" for none.
    /// </summary>
    public static string Variant(string key) => Tex(key + "_words.webp") != null ? "words" : Tex(key + "_tree.webp") != null ? "tree" : Tex(key + "_ui.webp") != null ? "ui" : "";

    /// <summary>FABLE-097: the painting carries its own static words; the game must not draw them again.</summary>
    public static bool Words(string key) => Variant(key) == "words";

    private static Texture2D? FaithfulTex(string key) => Tex(key + "_words.webp") ?? Tex(key + "_tree.webp") ?? Tex(key + "_ui.webp");

    /// <summary>Where the painting sits on a <paramref name="vp"/>-sized screen.</summary>
    public static Rect2 PlateRect(string key, Vector2 vp)
    {
        if (System.Array.IndexOf(FitScenes, key) < 0 || FaithfulTex(key) is not Texture2D t) return new Rect2(Vector2.Zero, vp);
        var s = t.GetSize(); float k = Mathf.Min(vp.X / s.X, vp.Y / s.Y);
        return new Rect2((vp - s * k) / 2f, s * k);
    }

    /// <summary>A point of the painting (0..1 across and down) in screen pixels.</summary>
    public static Vector2 Art(string key, Vector2 vp, float fx, float fy)
    {
        var r = PlateRect(key, vp);
        return r.Position + new Vector2(fx * r.Size.X, fy * r.Size.Y);
    }

    public static void Apply(Node scene)
    {
        if (!IsInstanceValid(scene) || scene is not Control root) return;
        string key = Key(scene);
        var plate = FaithfulTex(key) ?? Tex(key + ".webp");
        if (plate != null)
        {
            foreach (var c in root.GetChildren())   // the flat / hero background layers the screen starts with
            {
                if ((c is not ColorRect && c is not TextureRect) || ((Control)c).AnchorRight < 0.99f || ((Control)c).AnchorBottom < 0.99f) break;
                ((Control)c).Visible = false;
            }
            bool faithful = Faithful(key), fit = faithful && System.Array.IndexOf(FitScenes, key) >= 0;
            var bg = new TextureRect
            {
                Name = "ScreenSkinPlate", Texture = plate, MouseFilter = Control.MouseFilterEnum.Ignore, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = !faithful ? TextureRect.StretchModeEnum.KeepAspectCovered : fit ? TextureRect.StretchModeEnum.KeepAspectCentered : TextureRect.StretchModeEnum.Scale,
            };
            bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            root.AddChild(bg);
            root.MoveChild(bg, 0);
            if (fit)   // FABLE-097: the bands beside the painting are a soft, dark blur of it (a sharp dim copy doubled its corners)
            {
                var side = new TextureRect { Name = "ScreenSkinSides", Texture = Blurred(key, plate), MouseFilter = Control.MouseFilterEnum.Ignore, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.Scale, Modulate = new Color(0.30f, 0.29f, 0.28f) };
                side.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                root.AddChild(side);
                root.MoveChild(side, 0);
            }
        }
        scene.GetTree().CreateTimer(0.06).Timeout += () => { if (IsInstanceValid(scene)) Walk(scene, scene); };   // sizes are right after a layout pass
    }

    private static void Walk(Node n, Node scene)
    {
        if (!IsInstanceValid(n)) return;
        if (n is CanvasLayer || IsOverlay(n)) return;   // pop-ups keep their own glass
        if (n is Button && n.HasMeta("skinned")) return;   // already done, or styled by its own screen: keeps its look and its words
        SkinNode(n, scene);
        foreach (var c in n.GetChildren()) Walk(c, scene);
    }

    /// <summary>A pop-up: something laid over a full-screen dimmer.</summary>
    private static bool IsOverlay(Node n)
    {
        if (n.GetParent() is not Node p) return false;
        for (int i = 0; i < n.GetIndex(); i++)
            if (p.GetChild(i) is ColorRect dim && dim.Visible && dim.Color.A > 0.4f && dim.Color.A < 0.97f && dim.AnchorRight > 0.99f && dim.AnchorBottom > 0.99f && dim.Name != "ScreenSkinPlate") return true;
        return false;
    }

    private static void SkinNode(Node n, Node scene)
    {
        if (n.HasMeta("skinned")) return;
        string key = Key(scene);
        if (Faithful(key)) { SkinFaithful(n, key); return; }
        if (n is Button mb && key == "MapScene" && mb.GetGlobalRect().Position.X > mb.GetViewportRect().Size.X * 0.5f) { n.SetMeta("skinned", true); return; }   // the map's node panel (bottom right) keeps its own buttons
        if (n is Button b && Tex("plaque.png") is Texture2D plaque && b.Size.Y >= 48 && b.Size.X >= 110 && b.Icon == null
            && b.Text.Trim().Length > 1 && b.HasThemeStyleboxOverride("normal") && Drawn(b.GetThemeStylebox("normal")))
        {
            Plaque(b, plaque);
        }
        else if ((n is PanelContainer || n is Godot.Panel) && Tex("panel.png") is Texture2D panel && n is Control c
                 && c.Size.X >= 300 && c.Size.Y >= 150 && c.HasThemeStyleboxOverride("panel") && c.GetThemeStylebox("panel") is StyleBoxFlat)
        {
            c.AddThemeStyleboxOverride("panel", Box(panel, c.GetThemeStylebox("panel"), new Color(1, 1, 1, 0.97f)));
            c.SetMeta("skinned", true);
        }
    }

    /// <summary>FABLE-105: the bronze-framed wood panel (panel.png) under a block of words that a painting leaves unreadable.</summary>
    public static bool Wood(Control c, float alpha = 0.96f)
    {
        var tex = Tex("panel.png");
        if (tex == null) return false;
        var old = c.HasThemeStyleboxOverride("panel") ? c.GetThemeStylebox("panel") : new StyleBoxFlat();
        c.AddThemeStyleboxOverride("panel", Box(tex, old, new Color(1, 1, 1, alpha)));
        c.SetMeta("skinned", true);
        return true;
    }

    /// <summary>The older bronze plaque, for a button with no painted plaque under it (Settings' Back).</summary>
    public static void Plaque(Button b, Texture2D? tex = null)
    {
        tex ??= Tex("plaque.png");
        if (tex == null) return;
        var old = b.GetThemeStylebox("normal");
        b.AddThemeStyleboxOverride("normal", Box(tex, old, new Color(1, 1, 1)));
        b.AddThemeStyleboxOverride("hover", Box(tex, old, new Color(1.18f, 1.12f, 1.0f)));
        b.AddThemeStyleboxOverride("pressed", Box(tex, old, new Color(0.72f, 0.70f, 0.66f)));
        b.AddThemeStyleboxOverride("disabled", Box(tex, old, new Color(0.55f, 0.55f, 0.55f, 0.8f)));
        b.SetMeta("skinned", true);
    }

    private static void SkinFaithful(Node n, string key)
    {
        if (n is Button b && b.HasThemeStyleboxOverride("normal") && Drawn(b.GetThemeStylebox("normal")))
        {
            var m = b.GetThemeStylebox("normal");
            b.AddThemeStyleboxOverride("normal", Clear(m, new Color(0, 0, 0, 0)));
            b.AddThemeStyleboxOverride("hover", Clear(m, new Color(1f, 0.86f, 0.45f, 0.13f)));
            b.AddThemeStyleboxOverride("pressed", Clear(m, new Color(0, 0, 0, 0.22f)));
            b.AddThemeStyleboxOverride("disabled", Clear(m, new Color(0, 0, 0, 0)));
            b.SetMeta("skinned", true);
        }
        else if ((n is PanelContainer || n is Godot.Panel) && n is Control c && c.HasThemeStyleboxOverride("panel") && c.GetThemeStylebox("panel") is StyleBoxFlat f && f.BgColor.A > 0.05f)
        {
            c.AddThemeStyleboxOverride("panel", Clear(f, new Color(0, 0, 0, 0)));
            c.SetMeta("skinned", true);
        }
        if (n is Label l) Ink(l, key);
        else if (n is Button bt && bt.Text.Length > 0) Ink(bt, key);
    }

    /// <summary>Words on light parchment read in dark ink; on wood and stone they keep their own colour.</summary>
    private static void Ink(Control c, string key)
    {
        if (!c.IsVisibleInTree() || c.Size.X < 2) return;
        var img = PlateImage(key);
        if (img == null) return;
        var vp = c.GetViewportRect().Size; var plate = PlateRect(key, vp); var r = c.GetGlobalRect();
        float sum = 0; int k = 0;
        for (int i = 1; i <= 3; i++)
            for (int j = 1; j <= 3; j++)
            {
                var p = r.Position + r.Size * new Vector2(i / 4f, j / 4f);
                float u = (p.X - plate.Position.X) / plate.Size.X, v = (p.Y - plate.Position.Y) / plate.Size.Y;
                if (u < 0 || v < 0 || u >= 1 || v >= 1) continue;
                var px = img.GetPixel((int)(u * img.GetWidth()), (int)(v * img.GetHeight()));
                sum += px.R * 0.299f + px.G * 0.587f + px.B * 0.114f; k++;
            }
        if (k == 0 || sum / k < 0.56f) return;
        var ink = new Color(0.20f, 0.13f, 0.07f);
        c.AddThemeColorOverride("font_color", ink);
        if (c is Button) { c.AddThemeColorOverride("font_hover_color", ink); c.AddThemeColorOverride("font_pressed_color", ink); }
        c.AddThemeConstantOverride("outline_size", 0);
        c.SetMeta("inked", true);
    }

    private static readonly Dictionary<string, Texture2D> Blurs = new();

    /// <summary>The painting shrunk to a few dozen pixels and let the GPU smooth it back up: a heavy blur for free.</summary>
    private static Texture2D Blurred(string key, Texture2D plate)
    {
        if (Blurs.TryGetValue(key, out var t)) return t;
        var img = plate.GetImage();
        if (img.IsCompressed()) img.Decompress();
        img.Resize(48, 27, Image.Interpolation.Lanczos);
        img.Resize(192, 108, Image.Interpolation.Cubic);
        return Blurs[key] = ImageTexture.CreateFromImage(img);
    }

    private static Image? PlateImage(string key)
    {
        if (Pixels.TryGetValue(key, out var img)) return img;
        img = FaithfulTex(key)?.GetImage();
        if (img != null && img.IsCompressed()) img.Decompress();
        Pixels[key] = img;
        return img;
    }

    private static StyleBoxFlat Clear(StyleBox like, Color bg)
    {
        var s = new StyleBoxFlat { BgColor = bg, CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10 };
        foreach (Side side in new[] { Side.Left, Side.Right, Side.Top, Side.Bottom }) s.SetContentMargin(side, like.GetMargin(side));
        return s;
    }

    private static bool InsidePanel(Node n)
    {
        for (var p = n.GetParent(); p != null; p = p.GetParent()) if (p is PanelContainer || p is Godot.Panel) return true;
        return false;
    }

    /// <summary>Only boxes drawn in code are swapped; a box that shows art is left alone.</summary>
    private static bool Drawn(StyleBox sb) => sb is StyleBoxFlat || (sb is StyleBoxTexture t && t.Texture is ImageTexture);

    /// <summary>A 9-slice of the painting that keeps the old box's content margins, so nothing moves.</summary>
    private static StyleBoxTexture Box(Texture2D tex, StyleBox old, Color modulate)
    {
        float m = Mathf.Round(Mathf.Min(tex.GetSize().X, tex.GetSize().Y) * 0.22f);
        var box = new StyleBoxTexture { Texture = tex, ModulateColor = modulate };
        foreach (Side side in new[] { Side.Left, Side.Right, Side.Top, Side.Bottom })
        {
            box.SetTextureMargin(side, m);
            box.SetContentMargin(side, old.GetMargin(side));
        }
        return box;
    }
}

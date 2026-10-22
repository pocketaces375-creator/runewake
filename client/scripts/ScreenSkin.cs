using System.Collections.Generic;
using Godot;

namespace Runewake.Client;

/// <summary>FABLE-084: the picked redesigns under the screens as they are. content/art/screens/: Scene.webp = painted
/// backdrop (replaces the flat one), panel.png / plaque.png = painted 9-slices for big panels and buttons. Missing = not applied.</summary>
public partial class ScreenSkin : Node
{
    private const string Dir = "res://content/art/screens/";
    private static readonly string[] Skinned = { "SettingsScene", "RewardsScene", "DeckBuilderScene", "OnlineLobbyScene", "MapScene", "ReliquaryScene", "PackShopScene" };
    private static readonly Dictionary<string, Texture2D?> Cache = new();
    private Node? _scene;
    private bool _on;

    public override void _Ready() => GetTree().NodeAdded += n =>
    {
        if (_on && _scene != null && IsInstanceValid(_scene) && (n is Godot.Panel || n is PanelContainer || n is Button) && _scene.IsAncestorOf(n))
            GetTree().CreateTimer(0.05).Timeout += () => { if (IsInstanceValid(n)) SkinNode(n); };
    };

    public override void _Process(double delta)
    {
        var s = GetTree().CurrentScene;
        if (s == _scene) return;
        _scene = s;
        _on = s != null && System.Array.IndexOf(Skinned, Key(s)) >= 0 && (Tex("panel.png") != null || Tex("plaque.png") != null || Tex(Key(s) + ".webp") != null);
        if (_on) Callable.From(() => Apply(s!)).CallDeferred();
    }

    private static string Key(Node s) => (s.SceneFilePath ?? "").GetFile().GetBaseName();

    private static Texture2D? Tex(string f)
    {
        if (!Cache.TryGetValue(f, out var t)) Cache[f] = t = ResourceLoader.Exists(Dir + f) ? GD.Load<Texture2D>(Dir + f) : null;
        return t;
    }

    public static void Apply(Node scene)
    {
        if (!IsInstanceValid(scene)) return;
        if (Tex(Key(scene) + ".webp") is Texture2D plate && scene is Control root)
        {
            foreach (var c in root.GetChildren())   // the flat / hero background layers the screen starts with
            {
                if ((c is not ColorRect && c is not TextureRect) || ((Control)c).AnchorRight < 0.99f || ((Control)c).AnchorBottom < 0.99f) break;
                ((Control)c).Visible = false;
            }
            var bg = new TextureRect { Name = "ScreenSkinPlate", Texture = plate, MouseFilter = Control.MouseFilterEnum.Ignore,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize };
            bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            root.AddChild(bg);
            root.MoveChild(bg, 0);
        }
        scene.GetTree().CreateTimer(0.05).Timeout += () => Walk(scene);   // sizes are right after a layout pass
    }

    private static void Walk(Node n)
    {
        if (!IsInstanceValid(n)) return;
        SkinNode(n);
        foreach (var c in n.GetChildren()) Walk(c);
    }

    private static void SkinNode(Node n)
    {
        if (n.HasMeta("skinned")) return;
        if (n is Button b && Tex("plaque.png") is Texture2D plaque && b.Size.Y >= 48 && b.Size.X >= 110 && b.Icon == null
            && b.Text.Trim().Length > 1 && b.HasThemeStyleboxOverride("normal") && Drawn(b.GetThemeStylebox("normal")))
        {
            var old = b.GetThemeStylebox("normal");
            b.AddThemeStyleboxOverride("normal", Box(plaque, old, new Color(1, 1, 1)));
            b.AddThemeStyleboxOverride("hover", Box(plaque, old, new Color(1.18f, 1.12f, 1.0f)));
            b.AddThemeStyleboxOverride("pressed", Box(plaque, old, new Color(0.72f, 0.70f, 0.66f)));
            b.AddThemeStyleboxOverride("disabled", Box(plaque, old, new Color(0.55f, 0.55f, 0.55f, 0.8f)));
            b.SetMeta("skinned", true);
        }
        else if ((n is PanelContainer || n is Godot.Panel) && Tex("panel.png") is Texture2D panel && n is Control c
                 && c.Size.X >= 300 && c.Size.Y >= 150 && c.HasThemeStyleboxOverride("panel") && c.GetThemeStylebox("panel") is StyleBoxFlat)
        {
            c.AddThemeStyleboxOverride("panel", Box(panel, c.GetThemeStylebox("panel"), new Color(1, 1, 1, 0.97f)));
            c.SetMeta("skinned", true);
        }
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

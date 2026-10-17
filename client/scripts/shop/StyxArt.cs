using Godot;

namespace Runewake.Client;

/// <summary>
/// FABLE-070: the painted pieces of the Ferryman's Toll (tools/styx_art.py paints them; Trikzos picks).
/// Each is optional — anything missing falls back to the scene drawn in code.
/// </summary>
public static class StyxArt
{
    private const string Dir = "res://content/art/styx/";
    private static Texture2D? _backdrop, _coin, _stele, _rune;
    private static bool _loaded;

    private static Texture2D? Load(string file) => ResourceLoader.Exists(Dir + file) ? GD.Load<Texture2D>(Dir + file) : null;

    private static void Ensure()
    {
        if (_loaded) return;
        _loaded = true;
        _backdrop = Load("backdrop.webp");
        _coin = Load("coin.png");
        _stele = Load("stele.png");
        _rune = Load("rune.png");
    }

    public static Texture2D? Backdrop { get { Ensure(); return _backdrop; } }
    public static Texture2D? Coin { get { Ensure(); return _coin; } }
    public static Texture2D? Stele { get { Ensure(); return _stele; } }
    public static Texture2D? Rune { get { Ensure(); return _rune; } }

    /// <summary>The part of <paramref name="tex"/> that covers a <paramref name="target"/>-sized box (centre crop, no stretching).</summary>
    public static Rect2 CoverSource(Texture2D tex, Vector2 target)
    {
        var s = tex.GetSize();
        float scale = Mathf.Max(target.X / s.X, target.Y / s.Y);
        var view = target / scale;
        return new Rect2((s - view) / 2f, view);
    }
}

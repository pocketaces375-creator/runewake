using System;
using Godot;

namespace Runewake.Client;

/// <summary>
/// LOADSCREEN-1. Full-screen painted load screens on every scene change, instead
/// of the black gap. The FIRST load screen of a session is always the main
/// kingdom painting (load_main.webp); every later one is a random pick from the
/// pool, never repeating the last one shown.
///
/// The overlay lives on this autoload (a CanvasLayer at the root), so it
/// survives ChangeSceneToFile: the scene swap frees the current scene, not the
/// autoload. Show -> one frame for the overlay to render -> swap -> hide after
/// the new scene has had time to build.
///
/// TEST-SAFE: in headless, capture, loop-smoke, or UX-walk mode the overlay is
/// suppressed entirely and Go() is a plain scene change, so every automated
/// check and screenshot sees exactly what it saw before.
/// </summary>
public partial class LoadScreen : CanvasLayer
{
    /// <summary>The FIRST load screen — always shown first, then excluded from the random pool.</summary>
    public const string MainScreen = "res://content/art/load/load_main.webp";

    private static readonly string[] Pool =
    {
        "res://content/art/load/load_gold_dawn.webp",
        "res://content/art/load/load_teal_dusk.webp",
        "res://content/art/load/load_warrior.webp",
        "res://content/art/load/load_battlemage.webp",
        "res://content/art/load/load_necromancer.webp",
        "res://content/art/load/load_paladin.webp",
        "res://content/art/load/load_druid.webp",
        "res://content/art/load/load_rogue.webp",
        "res://content/art/load/load_astrologist.webp",
    };

    private static readonly Random Rng = new();
    private static bool _firstShown;
    private static int _lastPick = -1;
    private TextureRect? _art;

    /// <summary>True when an automated check is running and the overlay must stay out of the picture.</summary>
    public static bool Suppressed =>
        OS.HasFeature("headless")
        || CampaignContext.AutoCaptureScreenshot
        || CampaignContext.LoopSmokeTest
        || CampaignContext.UxWalk;

    public override void _Ready()
    {
        Layer = 100;
        _art = new TextureRect
        {
            AnchorLeft = 0, AnchorRight = 1, AnchorTop = 0, AnchorBottom = 1,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        AddChild(_art);
    }

    /// <summary>Scene-change entry point. Shows a load screen, swaps, then hides it.</summary>
    public static void Go(SceneTree tree, string path)
    {
        if (tree == null) return;
        if (Suppressed)
        {
            tree.ChangeSceneToFile(path);
            return;
        }
        try
        {
            var ls = tree.Root.GetNodeOrNull<LoadScreen>("LoadScreen");
            if (ls == null || ls._art == null)
            {
                tree.ChangeSceneToFile(path);
                return;
            }
            ls.ShowPick();
            // One short timer so the overlay actually renders before the swap frees the old scene.
            var t1 = tree.CreateTimer(0.05, processAlways: true, processInPhysics: false, ignoreTimeScale: true);
            t1.Timeout += () =>
            {
                try
                {
                    tree.ChangeSceneToFile(path);
                    var t2 = tree.CreateTimer(0.35, processAlways: true, processInPhysics: false, ignoreTimeScale: true);
                    t2.Timeout += ls.Hide;
                }
                catch (System.Exception ex)
                {
                    GD.PrintErr($"[LoadScreen] swap failed: {ex.Message}");
                    try { tree.ChangeSceneToFile(path); } catch { }
                }
            };
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[LoadScreen] Go failed: {ex.Message}");
            try { tree.ChangeSceneToFile(path); } catch { }
        }
    }

    /// <summary>Show a load screen WITHOUT swapping (used by DuelScene.DoSwap, which has its own swap machinery).</summary>
    public static void ShowForSwap(SceneTree tree)
    {
        if (Suppressed || tree == null) return;
        try
        {
            var ls = tree.Root.GetNodeOrNull<LoadScreen>("LoadScreen");
            ls?.ShowPick();
        }
        catch { }
    }

    /// <summary>Hide the overlay shortly from now (used by DuelScene.DoSwap after its own swap).</summary>
    public static void HideSoon(SceneTree tree)
    {
        if (Suppressed || tree == null) return;
        try
        {
            var ls = tree.Root.GetNodeOrNull<LoadScreen>("LoadScreen");
            if (ls == null) return;
            var t2 = tree.CreateTimer(0.35, processAlways: true, processInPhysics: false, ignoreTimeScale: true);
            t2.Timeout += ls.Hide;
        }
        catch { }
    }

    public void Hide()
    {
        if (_art != null) _art.Visible = false;
    }

    private void ShowPick()
    {
        if (_art == null) return;
        string texPath;
        if (!_firstShown)
        {
            texPath = MainScreen;
            _firstShown = true;
            _lastPick = -1;
        }
        else
        {
            int idx;
            do { idx = Rng.Next(Pool.Length); } while (idx == _lastPick);
            _lastPick = idx;
            texPath = Pool[idx];
        }
        var tex = ResourceLoader.Load<Texture2D>(texPath);
        if (tex == null)
        {
            GD.PrintErr($"[LoadScreen] missing texture: {texPath}");
            return;
        }
        _art.Texture = tex;
        _art.Visible = true;
    }
}

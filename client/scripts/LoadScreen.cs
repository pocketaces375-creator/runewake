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
/// autoload.
///
/// TIMING (FABLE-060 — Jade: "load screens are not lasting as long as they should"):
/// every load screen is ON SCREEN for DisplaySeconds of real time, the same everywhere.
///   1. Show the painting, then wait until it has actually been DRAWN before the
///      swap starts. The duel's exit used to show it and load the next scene in the
///      same frame, so the phone froze on the old screen during the load, and the
///      painting appeared only afterwards with most of its 3 s already gone.
///   2. The clock starts at that first drawn frame and is real (wall-clock) time,
///      so a frozen frame during a load counts — the painting was up for it.
///   3. It hides when DisplaySeconds have passed AND the new scene is in place
///      (a load slower than DisplaySeconds keeps it up until the scene is ready),
///      fading out over the last FadeSeconds. MaxSeconds is a safety net so a
///      failed swap can never leave it covering the game.
/// A newer show replaces an older one (generation counter): no stale hide.
///
/// TEST-SAFE: in headless, capture, loop-smoke, or UX-walk mode the overlay is
/// suppressed entirely and Go() is a plain scene change, so every automated
/// check and screenshot sees exactly what it saw before.
/// </summary>
public partial class LoadScreen : CanvasLayer
{
    /// <summary>The FIRST load screen — always shown first, then excluded from the random pool.</summary>
    public const string MainScreen = "res://content/art/load/load_main.webp";

    /// <summary>How long every load screen is on screen, start to finish (fade included).</summary>
    public const double DisplaySeconds = 3.0;

    /// <summary>The fade-out at the end of the hold.</summary>
    public const double FadeSeconds = 0.25;

    /// <summary>Never cover the game longer than this, whatever happened to the swap.</summary>
    public const double MaxSeconds = 8.0;

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
    private string? _nextPath;       // the next painting, already loading in the background

    // the one load screen currently up
    private long _gen;
    private ulong _shownAtMs;        // when ShowPick ran
    private ulong _drawnAtMs;        // first frame the painting was actually drawn (0 = not yet)
    private ulong _oldSceneId;       // the scene being left; "ready" = a different scene is current
    private ulong _readyAtMs;        // when the new scene was first seen in place (0 = not yet)
    private bool _fading;
    private Action? _afterDrawn;     // the swap, run once the painting is on screen

    /// <summary>True when an automated check is running and the overlay must stay out of the picture.</summary>
    public static bool Suppressed =>
        (OS.HasFeature("headless")
        || CampaignContext.AutoCaptureScreenshot
        || CampaignContext.LoopSmokeTest
        || CampaignContext.UxWalk)
        && System.Environment.GetEnvironmentVariable("LOADSCREEN_FORCE_SHOW") != "1";

    public override void _Ready()
    {
        Layer = 100;
        ProcessMode = ProcessModeEnum.Always;
        _art = new TextureRect
        {
            AnchorLeft = 0, AnchorRight = 1, AnchorTop = 0, AnchorBottom = 1,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Stop,      // taps during a load don't reach the scene behind it
            Visible = false,
        };
        AddChild(_art);
        RenderingServer.FramePostDraw += OnFramePostDraw;
        if (!Suppressed) PrepareNext();
    }

    /// <summary>
    /// Pick the next painting now and start loading it on a background thread, so a tap shows it at once
    /// instead of freezing while a full-screen image decodes. Only one is kept ready (memory on phones).
    /// </summary>
    private void PrepareNext()
    {
        if (!_firstShown) _nextPath = MainScreen;
        else
        {
            int idx;
            do { idx = Rng.Next(Pool.Length); } while (idx == _lastPick && Pool.Length > 1);
            _lastPick = idx;
            _nextPath = Pool[idx];
        }
        try { ResourceLoader.LoadThreadedRequest(_nextPath, "Texture2D"); }
        catch (Exception ex) { GD.PrintErr($"[LoadScreen] preload {_nextPath}: {ex.Message}"); }
    }

    private Texture2D? TakeNext()
    {
        if (_nextPath == null) PrepareNext();
        var path = _nextPath!;
        _nextPath = null;
        _firstShown = true;
        try
        {
            if (ResourceLoader.LoadThreadedGetStatus(path) != ResourceLoader.ThreadLoadStatus.InvalidResource)
                return ResourceLoader.LoadThreadedGet(path) as Texture2D;     // waits only if still decoding
        }
        catch (Exception ex) { GD.PrintErr($"[LoadScreen] threaded get {path}: {ex.Message}"); }
        return ResourceLoader.Load<Texture2D>(path);
    }

    public override void _ExitTree() => RenderingServer.FramePostDraw -= OnFramePostDraw;

    /// <summary>Scene-change entry point. Shows a load screen, swaps once it is drawn, hides it on time.</summary>
    public static Error Go(SceneTree tree, string path)
    {
        if (tree == null) return Error.Failed;
        if (Suppressed) return tree.ChangeSceneToFile(path);
        try
        {
            var ls = tree.Root.GetNodeOrNull<LoadScreen>("LoadScreen");
            if (ls == null || ls._art == null || !ls.ShowPick(tree)) return tree.ChangeSceneToFile(path);
            long gen = ls._gen;
            ls._afterDrawn = () =>
            {
                if (gen != ls._gen) return;          // a newer load screen took over
                Error err;
                try { err = tree.ChangeSceneToFile(path); }
                catch (Exception ex) { GD.PrintErr($"[LoadScreen] swap threw: {ex.Message}"); err = Error.Failed; }
                if (err != Error.Ok)
                {
                    GD.PrintErr($"[LoadScreen] swap to {path} failed ({err}) — hiding");
                    ls.Hide();
                }
            };
            return Error.Ok;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LoadScreen] Go failed: {ex.Message}");
            try { return tree.ChangeSceneToFile(path); } catch { return Error.Failed; }
        }
    }

    /// <summary>
    /// Show a load screen WITHOUT swapping — DuelScene has its own swap machinery. It is called BEFORE the
    /// frames DuelScene waits (SwapAfterOneFrame), so the painting is on screen before the load starts.
    /// </summary>
    public static void ShowForSwap(SceneTree tree)
    {
        if (Suppressed || tree == null) return;
        try { tree.Root.GetNodeOrNull<LoadScreen>("LoadScreen")?.ShowPick(tree); }
        catch { }
    }

    /// <summary>No-op: the hold is timed from the first drawn frame (see TIMING). Kept for DoSwap's call site.</summary>
    public static void HideSoon(SceneTree tree)
    {
    }

    public void Hide()
    {
        _gen++;
        _afterDrawn = null;
        _fading = false;
        if (_art == null) return;
        _art.Visible = false;
        _art.Modulate = Colors.White;
    }

    private bool ShowPick(SceneTree tree)
    {
        if (_art == null) return false;
        var tex = TakeNext();
        PrepareNext();                       // the one after this starts loading now
        if (tex == null)
        {
            GD.PrintErr("[LoadScreen] missing load-screen texture");
            return false;
        }
        _gen++;
        _afterDrawn = null;
        _fading = false;
        _art.Texture = tex;
        _art.Modulate = Colors.White;
        _art.Visible = true;
        _shownAtMs = Time.GetTicksMsec();
        _drawnAtMs = 0;
        _readyAtMs = 0;
        _oldSceneId = tree.CurrentScene?.GetInstanceId() ?? 0;
        return true;
    }

    /// <summary>After every rendered frame: the first one with the painting up starts its clock.</summary>
    private void OnFramePostDraw()
    {
        if (_art == null || !_art.Visible || _drawnAtMs != 0) return;
        _drawnAtMs = Time.GetTicksMsec();
    }

    public override void _Process(double delta)
    {
        if (_art == null || !_art.Visible || _fading) return;
        ulong now = Time.GetTicksMsec();

        // a renderer that never reports a drawn frame must not stall the swap
        if (_drawnAtMs == 0 && now - _shownAtMs > 500) _drawnAtMs = _shownAtMs;
        if (_drawnAtMs == 0) return;

        if (_afterDrawn != null)
        {
            var swap = _afterDrawn;
            _afterDrawn = null;
            Callable.From(swap).CallDeferred();
            return;
        }

        var cur = GetTree()?.CurrentScene;
        if (_readyAtMs == 0 && cur != null && cur.GetInstanceId() != _oldSceneId && cur.IsNodeReady())
            _readyAtMs = now;

        double shown = (now - _drawnAtMs) / 1000.0;
        bool ready = _readyAtMs != 0 && now - _readyAtMs >= 100;    // the new scene has had a few frames
        if ((ready && shown >= DisplaySeconds - FadeSeconds) || shown >= MaxSeconds)
        {
            _fading = true;
            long gen = _gen;
            double readyAfter = _readyAtMs == 0 ? -1 : (_readyAtMs - _drawnAtMs) / 1000.0;
            GD.Print($"[LoadScreen] on screen {shown + FadeSeconds:0.00}s (new scene ready after {readyAfter:0.00}s)");
            var tw = _art.CreateTween();
            tw.TweenProperty(_art, "modulate:a", 0f, FadeSeconds);
            tw.TweenCallback(Callable.From(() => { if (gen == _gen) Hide(); }));
        }
    }
}

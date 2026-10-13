using System;
using Godot;

namespace Runewake.Client;

/// <summary>
/// FABLE-024. A scene whose _Ready throws on the phone builds nothing, and the player sees the
/// clear colour: a grey screen with the music still going. DuelScene and MapScene already paint
/// their failure; every scene the Continue chain can land on now does the same through this
/// helper, and always leaves a way out.
/// </summary>
public static class SceneGuard
{
    /// <summary>Run the scene's build; on a throw, paint the error and a button to <paramref name="escapePath"/>.</summary>
    public static void Build(Control scene, string name, Action build, string escapePath, string escapeLabel)
    {
        DuelScene.ExitTrace($"arrived: {name} _Ready begin");
        try
        {
            build();
            DuelScene.ExitTrace($"arrived: {name} _Ready done ({scene.GetChildCount()} nodes)");
        }
        catch (Exception ex)
        {
            DuelScene.ExitTrace($"{name} _Ready THREW: {ex}");
            try
            {
                var bg = new ColorRect { Color = new Color(0.06f, 0.05f, 0.04f) };
                bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                scene.AddChild(bg);
                var msg = new Label
                {
                    Text = $"{name} failed to load: {ex.GetType().Name} — {ex.Message}\n{DuelScene.FirstFrame(ex)}\nScreenshot this for Fable.",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    AnchorLeft = 0.08f, AnchorRight = 0.92f, AnchorTop = 0.20f, AnchorBottom = 0.62f,
                };
                msg.AddThemeFontSizeOverride("font_size", 30);
                msg.AddThemeColorOverride("font_color", new Color(1.0f, 0.62f, 0.52f));
                scene.AddChild(msg);
                var btn = new Button { Text = escapeLabel, AnchorLeft = 0.35f, AnchorRight = 0.65f, AnchorTop = 0.68f, AnchorBottom = 0.78f };
                btn.AddThemeFontSizeOverride("font_size", 36);
                btn.Pressed += () =>
                {
                    DuelScene.ExitTrace($"{name}: escape → {escapePath.GetFile()}");
                    scene.GetTree().ChangeSceneToFile(escapePath);
                };
                scene.AddChild(btn);
            }
            catch (Exception ex2)
            {
                DuelScene.ExitTrace($"{name}: could not even paint the failure: {ex2.Message}");
            }
        }
    }
}

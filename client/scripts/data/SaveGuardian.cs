using Godot;

namespace Runewake.Client;

/// <summary>
/// FABLE-066: writes everything down when the app goes to the background or is closed. Android can stop a
/// backgrounded app without warning; before this nothing saved on the way out, so whatever happened since
/// the last explicit save (a deck tweak, a setting, a daily claim) could be gone on the next launch.
/// </summary>
public partial class SaveGuardian : Node
{
    private double _lastFlush = -10;

    /// <summary>Add the guardian to the root once per run.</summary>
    public static void Ensure(SceneTree? tree)
    {
        if (tree?.Root == null) return;
        if (tree.Root.GetNodeOrNull("SaveGuardian") != null) return;
        var g = new SaveGuardian { Name = "SaveGuardian", ProcessMode = ProcessModeEnum.Always };
        Callable.From(() => { if (tree.Root.GetNodeOrNull("SaveGuardian") == null) tree.Root.AddChild(g); }).CallDeferred();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationPaused || what == NotificationWMCloseRequest || what == NotificationApplicationFocusOut)
            Flush(what == NotificationApplicationPaused ? "pause" : what == NotificationWMCloseRequest ? "close" : "focus out");
    }

    /// <summary>Save the open campaign, the campaign list and the deck library — at most once a second.</summary>
    public void Flush(string why)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (now - _lastFlush < 1.0) return;
        _lastFlush = now;
        try
        {
            var sm = CampaignContext.SaveManager;
            if (sm != null && sm.IsLoaded && !sm.LastLoadFailed) sm.Save();
            if (CampaignContext.Profiles.Count > 0) CampaignContext.SaveCampaignProfile();
            if (CampaignContext.DeckLibrary.Count > 0) CampaignContext.SaveDeckLibrary();
            GD.Print($"[SaveGuardian] saved on {why}");
        }
        catch (System.Exception ex) { GD.PrintErr($"[SaveGuardian] flush failed: {ex.Message}"); }
    }
}

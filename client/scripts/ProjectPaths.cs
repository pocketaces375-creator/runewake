using Godot;

namespace Runewake.Client;

/// <summary>Project-relative locations. Never hardcode a lane directory — every lane is a different clone.</summary>
public static class ProjectPaths
{
    /// <summary>&lt;repo&gt;/artifacts, resolved from res:// so it is correct in every clone and in exports.</summary>
    public static string Artifacts =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "artifacts"));

    public static string Captures => System.IO.Path.Combine(Artifacts, "captures");
}

using System;
using Godot;

namespace Runewake.Client;

/// <summary>
/// FABLE-079: which build this is and what phone it runs on, for the stuck-screen trace and Settings. A friend's
/// Android 9 phone crashed on every match and the screenshot could not say which build or which phone it was.
/// tools/ship_apk.sh writes res://content/misc/build_info.json (not in git) just before each export.
/// </summary>
public static class BuildInfo
{
    private const string InfoPath = "res://content/misc/build_info.json";
    private static string? _stamp;

    /// <summary>"c164bb61 · 2026-10-20" — or "dev build" when it was not built by ship_apk.</summary>
    public static string Stamp => _stamp ??= Load();

    private static string Load()
    {
        try
        {
            if (!Godot.FileAccess.FileExists(InfoPath)) return "dev build";
            var d = Json.ParseString(Godot.FileAccess.GetFileAsString(InfoPath)).AsGodotDictionary();
            string commit = d.ContainsKey("commit") ? d["commit"].AsString() : "?";
            string date = d.ContainsKey("date") ? d["date"].AsString() : "";
            return string.IsNullOrEmpty(date) ? commit : $"{commit} · {date}";
        }
        catch { return "unknown build"; }
    }

    /// <summary>Phone, OS, GPU and the graphics API it got, and its memory.</summary>
    public static string Device()
    {
        try
        {
            string ram = "";
            try
            {
                var m = OS.GetMemoryInfo();
                if (m.ContainsKey("physical") && m["physical"].AsInt64() > 0)
                    ram = $" | RAM {m["physical"].AsInt64() / 1048576} MB" + (m.ContainsKey("free") && m["free"].AsInt64() > 0 ? $" ({m["free"].AsInt64() / 1048576} free)" : "");
            }
            catch { }
            return $"{OS.GetModelName()} | {OS.GetName()} {OS.GetVersion()} | {RenderingServer.GetVideoAdapterVendor()} {RenderingServer.GetVideoAdapterName()} | " +
                   $"{RenderingServer.GetVideoAdapterApiVersion()}{ram}";
        }
        catch (Exception ex) { return "device unknown: " + ex.Message; }
    }

    /// <summary>How much memory the game is holding right now (textures are what fill a small phone).</summary>
    public static string Memory()
    {
        try
        {
            double mb(Performance.Monitor m) => Performance.GetMonitor(m) / 1048576.0;
            return $"mem {mb(Performance.Monitor.MemoryStatic):F0} MB, textures {mb(Performance.Monitor.RenderTextureMemUsed):F0} MB, video {mb(Performance.Monitor.RenderVideoMemUsed):F0} MB";
        }
        catch { return "mem ?"; }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Runewake.Engine.Diagnostics;

namespace Runewake.Client;

/// <summary>
/// Godot Node singleton (autoload) that handles unhandled exceptions.
///
/// On <see cref="AppDomain.CurrentDomain.UnhandledException"/> or
/// <see cref="TaskScheduler.UnobservedTaskException"/>: builds a JSON crash
/// report via <see cref="CrashReportBuilder"/>, writes it to
/// <c>user://crash_reports/&lt;timestamp&gt;_crash.json</c>, appends the
/// full stack to <c>user://crash.log</c>, shows a full-screen recovery
/// overlay with "Something went wrong — returning to the title", then
/// navigates back to the title scene after a brief pause.
///
/// On startup (call <see cref="UploadPendingReports"/>), POSTs any unsent
/// reports to Supabase using Godot's <see cref="HttpRequest"/> node (works
/// in exported builds on Android/iOS). Each file gets its own one-shot
/// HttpRequest child; on 2xx the local file is deleted.
///
/// Debug-only: in <c>OS.IsDebugBuild()</c>, the game will respond to a
/// <c>--test-crash</c> CLI argument by throwing immediately (proving the
/// recovery path works).
///
/// The crash handler itself never throws.
/// </summary>
public partial class CrashReporter : Node
{
    private static CrashReporter? _instance;
    private static bool _hookInstalled;
    private static readonly object Lock = new();

    private const string CrashDir = "user://crash_reports";
    private const string CrashLogPath = "user://crash.log";

    // ——— Singleton lifecycle ———

    public override void _Ready()
    {
        _instance = this;
        InstallGlobalHook();
        GD.Print("[CrashReporter] Autoload ready.");

        // Check for debug crash trigger CLI arg
        try
        {
            var args = OS.GetCmdlineArgs();
            if (args != null)
            {
                foreach (var arg in args)
                {
                    if (arg == "--test-crash")
                    {
                        GD.Print("[CrashReporter] --test-crash flag detected — triggering crash recovery handler in 0.5s");
                        var crashTimer = new Godot.Timer();
                        crashTimer.OneShot = true;
                        crashTimer.WaitTime = 0.5f;
                        crashTimer.Timeout += () =>
                        {
                            GD.Print("[CrashReporter] Triggering crash via TriggerCrashRecovery...");
                            TriggerCrashRecovery(new InvalidOperationException(
                                "THIS IS A TEST CRASH — triggered by --test-crash flag. The game should show the recovery screen and return to the title."));
                        };
                        AddChild(crashTimer);
                        crashTimer.Start();
                    }
                }
            }
        }
        catch
        {
            // Never throw from _Ready
        }
    }

    public override void _ExitTree()
    {
        _instance = null;
    }

    /// <summary>
    /// Install the global AppDomain unhandled-exception hook and
    /// TaskScheduler unobserved-task handler. Idempotent.
    /// </summary>
    private static void InstallGlobalHook()
    {
        if (_hookInstalled)
            return;
        _hookInstalled = true;

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        GD.Print("[CrashReporter] Global hooks installed.");
    }

    // ——— Global exception handlers ———

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        var ex = args.ExceptionObject as Exception;
        if (ex == null)
            return;

        HandleCrash(ex);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        if (args.Exception == null)
            return;

        HandleCrash(args.Exception);
        args.SetObserved();
    }

    // ——— Core crash handling ———

    private static void HandleCrash(Exception ex)
    {
        lock (Lock)
        {
            TriggerCrashRecovery(ex);
        }
    }

    /// <summary>
    /// Append the full exception stack trace to <c>user://crash.log</c>.
    /// </summary>
    private static void WriteCrashLog(Exception ex, string timestamp)
    {
        try
        {
            string logPath = ProjectSettings.GlobalizePath(CrashLogPath);
            string entry = $"=== Crash at {timestamp} ===\n"
                         + $"Type: {ex.GetType().FullName}\n"
                         + $"Message: {ex.Message}\n"
                         + $"{ex}\n\n";
            System.IO.File.AppendAllText(logPath, entry);
            GD.PrintErr($"[CrashReporter] crash.log updated");
        }
        catch
        {
            // Never throw from crash handler
        }
    }

    // ——— Recovery screen (runs on main thread via CallDeferred) ———

    /// <summary>
    /// Public entry point for crash recovery. Called when a crash is detected
    /// (by the global handler, a diagnostics trigger, or a scene-level catch).
    /// Writes crash.log, writes JSON report, then defers the recovery overlay
    /// to the main thread.
    /// Safe to call from any thread.
    /// </summary>
    public static void TriggerCrashRecovery(Exception ex)
    {
        lock (Lock)
        {
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");

            // Write JSON report (best-effort)
            WriteJsonReport(ex, timestamp);

            // Write plain-text crash.log
            WriteCrashLog(ex, timestamp);

            // Show recovery overlay (deferred to main thread)
            string stackText = ex.ToString();
            if (_instance != null)
            {
                Callable.From(() => _instance!.ShowRecoveryScreen(stackText)).CallDeferred();
            }
        }
    }

    private static void WriteJsonReport(Exception ex, string timestamp)
    {
        try
        {
            string appVersion = ProjectSettings.GetSetting("application/config/version", "dev").AsString();
            string platform = OS.GetName();
            string godotVersion = "unknown";
            var vi = Godot.Engine.GetVersionInfo();
            if (vi.TryGetValue("string", out var v))
                godotVersion = v.AsString();

            var report = CrashReportBuilder.BuildReport(ex, appVersion, platform, godotVersion);
            string json = CrashReportBuilder.SerializeReport(report);
            string dir = ProjectSettings.GlobalizePath(CrashDir);
            string filePath = System.IO.Path.Combine(dir, $"{timestamp}_crash.json");
            CrashReportBuilder.WriteReportFile(filePath, json);
            GD.PrintErr($"[CrashReporter] Report written to {filePath}");
        }
        catch
        {
        }
    }

    private void ShowRecoveryScreen(string fullStack)
    {
        // Bail if scene tree is gone
        var tree = GetTree();
        if (tree == null)
        {
            GD.PrintErr("[CrashReporter] Scene tree is null — cannot show recovery screen.");
            return;
        }

        var root = tree.Root;
        if (root == null)
        {
            GD.PrintErr("[CrashReporter] Root is null — cannot show recovery screen.");
            return;
        }

        try
        {
            // Full-screen dark overlay that blocks all input
            var overlay = new ColorRect
            {
                Color = new Color(0.05f, 0.03f, 0.01f, 0.92f),
                MouseFilter = Control.MouseFilterEnum.Stop
            };
            overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);

            // Vertical layout
            var vbox = new VBoxContainer();
            vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            vbox.AddThemeConstantOverride("separation", 20);

            // Spacer at top so content is centred
            vbox.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.Expand });

            // "Something went wrong" message — large, warm gold
            var messageLabel = new Label
            {
                Text = "Something went wrong\n— returning to the title",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.Word,
                AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
            };
            messageLabel.AddThemeFontSizeOverride("font_size", 28);
            messageLabel.Modulate = new Color(0.9f, 0.7f, 0.3f); // warm gold
            vbox.AddChild(messageLabel);

            // Spacer
            vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

            // Stack trace — smaller, dimmer, scrollable
            var stackLabel = new Label
            {
                Text = fullStack,
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.Word,
                AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
                SizeFlagsHorizontal = Control.SizeFlags.Fill,
                SizeFlagsVertical = Control.SizeFlags.Expand,
                CustomMinimumSize = new Vector2(0, 60)
            };
            stackLabel.AddThemeFontSizeOverride("font_size", 10);
            stackLabel.Modulate = new Color(0.6f, 0.6f, 0.6f, 0.8f);
            vbox.AddChild(stackLabel);

            // Spacer at bottom
            vbox.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.Expand });

            overlay.AddChild(vbox);

            // Add overlay to scene root, on top of everything
            root.AddChild(overlay);
            root.MoveChild(overlay, root.GetChildCount() - 1);

            GD.Print("[CrashReporter] Recovery overlay shown.");

            // Schedule return to title after 2.5 seconds so the message is visible
            var timer = new Godot.Timer();
            timer.OneShot = true;
            timer.WaitTime = 2.5f;
            timer.Timeout += () =>
            {
                try
                {
                    GD.Print("[CrashReporter] Returning to title...");
                    Error err = tree.ChangeSceneToFile("res://scenes/main/Main.tscn");
                    if (err != Error.Ok)
                        GD.PrintErr($"[CrashReporter] Scene change failed: {err}");
                }
                catch (Exception navEx)
                {
                    GD.PrintErr($"[CrashReporter] Navigation exception: {navEx.Message}");
                    // Last resort: try quitting so the player doesn't hang
                    try { tree.Quit(); } catch { }
                }
            };
            AddChild(timer);
            timer.Start();
        }
        catch (Exception recoveryEx)
        {
            // Last resort — if even showing the recovery screen fails, log it
            GD.PrintErr($"[CrashReporter] Recovery screen failed: {recoveryEx.Message}");
            try
            {
                // Try to navigate directly
                var err = tree.ChangeSceneToFile("res://scenes/main/Main.tscn");
                if (err != Error.Ok)
                    tree.Quit();
            }
            catch
            {
                try { tree.Quit(); } catch { }
            }
        }
    }

    // ——— Pending report upload ———

    /// <summary>
    /// Upload all pending crash report JSON files to Supabase.
    /// Call once from Main._Ready() after save loading.
    /// Spawns one-shot HttpRequest children — fires and forgets.
    /// On 2xx response, the local file is deleted.
    /// Reports with errors are left on disk for the next session.
    /// </summary>
    public static void UploadPendingReports(string supabaseUrl, string anonKey)
    {
        if (_instance == null)
        {
            GD.PrintErr("[CrashReporter] Cannot upload — autoload not yet ready.");
            return;
        }

        if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(anonKey))
        {
            GD.Print("[CrashReporter] UploadPendingReports: no credentials configured.");
            return;
        }

        string? dir = null;
        try
        {
            dir = ProjectSettings.GlobalizePath(CrashDir);
        }
        catch
        {
            return;
        }

        var files = CrashReportBuilder.ListPendingReports(dir);
        if (files.Count == 0)
        {
            GD.Print("[CrashReporter] No pending reports to upload.");
            return;
        }

        GD.Print($"[CrashReporter] Uploading {files.Count} pending crash report(s)...");

        foreach (var filePath in files)
        {
            string json;
            try
            {
                json = System.IO.File.ReadAllText(filePath);
                if (string.IsNullOrEmpty(json))
                    continue;
            }
            catch
            {
                continue;
            }

            var http = new HttpRequest();
            http.UseThreads = true;
            http.Timeout = 10;

            string url = $"{supabaseUrl.TrimEnd('/')}/rest/v1/crash_reports";
            string[] headers = new[]
            {
                $"apikey: {anonKey}",
                $"Authorization: Bearer {anonKey}",
                "Content-Type: application/json",
                "Accept: application/json"
            };

            // Capture filePath in closure for the response handler
            string capturedPath = filePath;

            http.RequestCompleted += (long result, long responseCode, string[] responseHeaders, byte[] body) =>
            {
                if (responseCode == 200 || responseCode == 201)
                {
                    CrashReportBuilder.DeleteReportFile(capturedPath);
                    GD.Print($"[CrashReporter] Uploaded and removed: {capturedPath}");
                }
                else
                {
                    GD.PrintErr($"[CrashReporter] Upload failed (HTTP {responseCode}): {capturedPath}");
                }

                // Cleanup the temporary node
                if (_instance != null && http.GetParent() != null)
                    _instance.RemoveChild(http);
                http.QueueFree();
            };

            _instance.AddChild(http);
            Error err = http.Request(url, headers, Godot.HttpClient.Method.Post, json);
            if (err != Error.Ok)
            {
                GD.PrintErr($"[CrashReporter] Request error {err} for {filePath}");
                _instance.RemoveChild(http);
                http.QueueFree();
            }
        }
    }
}
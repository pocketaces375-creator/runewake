using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Godot;

namespace Runewake.Client;

/// <summary>FABLE-082 device sweep: invariant culture (Turkish/Thai/Arabic phones), Android back presses the screen's Back/Close, low-memory warnings traced.</summary>
public partial class DeviceCompat : Node
{
    [ModuleInitializer]
    internal static void LockCulture()
    {
        var c = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = c; CultureInfo.DefaultThreadCurrentUICulture = c;
        CultureInfo.CurrentCulture = c; CultureInfo.CurrentUICulture = c;
    }

    private static readonly string[] OwnBack = { "/main/", "/duel/", "/choose_path/", "/rewards/", "/symbolkey/", "PackShopScene" };
    private static readonly Regex Closer = new(@"^\W*(close|done|cancel|not now|ok)\W*$", RegexOptions.IgnoreCase);
    private static readonly Regex Backer = new(@"^\W*(back|main menu|back to .{1,24}|save & close)\W*$", RegexOptions.IgnoreCase);

    public override void _Notification(int what)
    {
        if (what == NotificationOsMemoryWarning) { DuelScene.ExitTrace("low memory warning from Android: " + BuildInfo.Memory()); return; }
        if (what != NotificationWMGoBackRequest) return;
        string path = GetTree().CurrentScene?.SceneFilePath ?? "/main/";
        foreach (var p in OwnBack) if (path.Contains(p)) return;
        var target = FindBackButton(GetTree().Root);
        if (target == null) return;
        DuelScene.ExitTrace($"android back -> '{target.Get("text")}' on {path}");
        target.EmitSignal(BaseButton.SignalName.Pressed);
    }

    public static BaseButton? FindBackButton(Node root)
    {
        BaseButton? closer = null, backer = null;
        int cl = int.MinValue, bl = int.MinValue;
        var stack = new Stack<(Node n, int layer)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            var (n, layer) = stack.Pop();
            if (n is CanvasLayer c) { if (!c.Visible) continue; layer = c.Layer; }
            if (n is CanvasItem ci && !ci.Visible) continue;
            if (n is BaseButton b && !b.Disabled && b.Get("text").VariantType == Variant.Type.String)
            {
                string t = b.Get("text").AsString();
                if (Closer.IsMatch(t) && layer >= cl) { closer = b; cl = layer; }
                else if (Backer.IsMatch(t) && layer >= bl) { backer = b; bl = layer; }
            }
            for (int i = n.GetChildCount() - 1; i >= 0; i--) stack.Push((n.GetChild(i), layer));
        }
        return closer != null && (backer == null || cl >= bl) ? closer : backer;
    }
}

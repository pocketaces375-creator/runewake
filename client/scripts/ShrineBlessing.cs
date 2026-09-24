using System.Linq;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// FABLE-030. A blessing taken at a Shrine, carried to the player's NEXT fight and spent there.
/// Stored as a tag in ProgressionState.ClearedNodes ("bless|vigor|3"), the same trick the
/// world and Tower use, so it rides in the existing save with no schema change.
/// </summary>
public static class ShrineBlessing
{
    private const string Prefix = "bless|";

    public static void Set(ProgressionState prog, string kind, int amount)
    {
        Clear(prog);
        prog.MarkNodeCleared($"{Prefix}{kind}|{amount}");
    }

    public static (string kind, int amount)? Peek(ProgressionState? prog)
    {
        var tag = prog?.ClearedNodes.FirstOrDefault(n => n.StartsWith(Prefix));
        if (tag == null) return null;
        var parts = tag.Split('|');
        return parts.Length == 3 && int.TryParse(parts[2], out int n) ? (parts[1], n) : null;
    }

    /// <summary>Take the blessing for the fight that is starting now. Null if there is none.</summary>
    public static (string kind, int amount)? Take(ProgressionState? prog)
    {
        var b = Peek(prog);
        if (b != null && prog != null) Clear(prog);
        return b;
    }

    public static void Clear(ProgressionState prog) => prog.ClearedNodes.RemoveWhere(n => n.StartsWith(Prefix));

    public static string Describe((string kind, int amount) b) => b.kind switch
    {
        "vigor" => $"Shrine blessing: +{b.amount} Vigor this fight",
        "attune" => $"Shrine blessing: +{b.amount} Attunement this fight",
        _ => "Shrine blessing",
    };
}

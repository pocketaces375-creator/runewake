using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Runewake.Tests.World;
using Xunit;

namespace Runewake.Tests.Client;

/// <summary>
/// FABLE-029 guard. A tween made with GetTree().CreateTween() belongs to the whole scene tree, so
/// it outlives the node it animates. If it also loops forever, every loop aborts in zero time once
/// that node is freed — and RELEASE builds have no infinite-loop check, so the main loop spins
/// forever: the grey screen with music after every duel. Debug builds hide it behind an error.
/// Tweens must be made from a node (`node.CreateTween()`) so they die with it.
/// </summary>
public class TweenSafetyTests
{
    [Fact]
    public void No_Scene_Tree_Owned_Tweens_In_Client_Scripts()
    {
        string root = Path.Combine(WorldGeneratorTests.Root(), "client", "scripts");
        var offenders = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith("Zz"))
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, i + 1, line)))
            .Where(t => !t.line.TrimStart().StartsWith("//"))
            .Where(t => Regex.IsMatch(t.line, @"GetTree\(\)\??\.CreateTween\(\)|\btree\.CreateTween\(\)"))
            .Select(t => $"{Path.GetRelativePath(root, t.f)}:{t.Item2}: {t.line.Trim()}")
            .ToList();
        Assert.True(offenders.Count == 0, "Scene-tree-owned tweens outlive their targets and can freeze release builds:\n" + string.Join("\n", offenders));
    }
}

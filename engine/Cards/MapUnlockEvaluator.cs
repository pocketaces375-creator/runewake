using System.Collections.Generic;
using System.Linq;

namespace Runewake.Engine.Cards;

/// <summary>
/// Evaluates map node unlock conditions against a set of cleared node IDs.
/// Separated from the data model so it is testable without Godot dependencies.
/// </summary>
public static class MapUnlockEvaluator
{
    /// <summary>
    /// Returns the set of unlocked node IDs for the given region, given the
    /// set of cleared node IDs. A node with no unlock condition is always unlocked.
    /// </summary>
    public static HashSet<string> GetUnlockedNodes(MapRegion region, IReadOnlySet<string> clearedNodeIds)
    {
        var unlocked = new HashSet<string>();
        foreach (var node in region.Nodes)
        {
            if (IsUnlocked(node, clearedNodeIds))
                unlocked.Add(node.Id);
        }
        return unlocked;
    }

    /// <summary>
    /// Returns true if the given node is unlocked given the set of cleared node IDs.
    /// <list type="bullet">
    ///   <item>No unlock condition → unlocked (the node is a starting point).</item>
    ///   <item>NODES_CLEARED → unlocked iff all prerequisite node IDs are cleared.</item>
    ///   <item>Unknown op → locked (conservative).</item>
    /// </list>
    /// </summary>
    public static bool IsUnlocked(MapNode node, IReadOnlySet<string> clearedNodeIds)
    {
        if (node.Unlock == null)
            return true;

        if (node.Unlock.Op != "NODES_CLEARED")
            return false;

        if (node.Unlock.Value.Count == 0)
            return false;

        return node.Unlock.Value.All(p => clearedNodeIds.Contains(p));
    }
}
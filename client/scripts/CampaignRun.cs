using System.Collections.Generic;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// FABLE-012: the campaign as a LOOP.
///
/// Winning a duel used to be a dead end — Continue went to the map at best, and
/// the player had to find the next node themselves. What the game actually wants
/// is: fight, win, go straight into the next challenge, repeat, and keep
/// repeating until the region runs out. Trikzos's words: "brings you to the
/// beach challenge. Same thing for that challenge and so on, this system needs
/// to be able to loop."
///
/// This is the one place that knows how to answer "what comes next?", so the
/// duel screen and the map cannot drift apart on it. The unlock rule itself
/// stays where it has always lived — MapUnlockEvaluator — and is not
/// reimplemented here.
/// </summary>
public static class CampaignRun
{
    /// <summary>What the loop decided to do next.</summary>
    public enum Step
    {
        /// <summary>Another duel is armed and ready; load the duel scene.</summary>
        NextDuel,
        /// <summary>Nothing is unlocked and uncleared here — send them to the map.</summary>
        BackToMap,
    }

    public const string DuelScenePath = "res://scenes/duel/DuelScene.tscn";
    public const string MapScenePath = "res://scenes/map/MapScene.tscn";

    /// <summary>The region graph the player is currently in, or null if unreadable.</summary>
    public static MapRegion? LoadCurrentRegion()
    {
        string regionId = CampaignContext.GetRegionIdForMap();
        string json = Godot.FileAccess.GetFileAsString($"res://content/map/{regionId}.json");
        if (string.IsNullOrEmpty(json))
        {
            GD.PrintErr($"[CampaignRun] could not read res://content/map/{regionId}.json");
            return null;
        }
        return MapLoader.LoadRegionFromString(json);
    }

    /// <summary>
    /// The next node worth playing: unlocked, not cleared, and something the duel
    /// scene can actually run. Dig sites are skipped — they are a different
    /// scene and a different kind of choice, so the loop hands those back to the
    /// map rather than guessing.
    /// </summary>
    public static MapNode? FindNextDuelNode(MapRegion? region)
    {
        if (region == null) return null;
        var cleared = CampaignContext.Progression?.ClearedNodes ?? (IReadOnlySet<string>)new HashSet<string>();
        foreach (var node in region.Nodes)
        {
            if (cleared.Contains(node.Id)) continue;
            if (!MapUnlockEvaluator.IsUnlocked(node, cleared)) continue;
            bool isDuel = node.Type is MapNodeType.Duel or MapNodeType.Elite
                                    or MapNodeType.Warden or MapNodeType.WardenBoss;
            if (!isDuel) continue;
            if (node.Encounter == null) continue;
            if (!CampaignContext.EncounterIndex.ContainsKey(node.Encounter)) continue;
            return node;
        }
        return null;
    }

    /// <summary>
    /// Close out the duel just won and arm whatever comes next.
    ///
    /// Returns the scene to load and what it means. The caller navigates —
    /// this deliberately does not touch the SceneTree, so it stays testable and
    /// so there is exactly one place doing scene changes.
    /// </summary>
    public static (Step step, string scenePath, string description) AdvanceAfterVictory()
    {
        // 1. Bank the win. MarkNodeCleared is a HashSet.Add, so doing it twice
        //    is harmless — OnGameOver may already have done it.
        var justCleared = CampaignContext.CurrentNodeId;
        if (!string.IsNullOrEmpty(justCleared))
        {
            CampaignContext.Progression?.MarkNodeCleared(justCleared!);
            GD.Print($"[CampaignRun] cleared {justCleared}");
        }
        try
        {
            CampaignContext.SaveManager?.Save();
        }
        catch (System.Exception ex)
        {
            // A failed save must not cost the player their forward motion.
            GD.PrintErr($"[CampaignRun] save failed, continuing anyway: {ex.Message}");
        }

        // 2. Ask the region what is next.
        var region = LoadCurrentRegion();
        var next = FindNextDuelNode(region);
        if (next == null)
        {
            GD.Print("[CampaignRun] nothing unlocked and uncleared — back to the map");
            return (Step.BackToMap, MapScenePath, "no further duel available");
        }

        // 3. Arm it exactly the way the map arms a duel, so the loop and the map
        //    produce identical state and the duel cannot tell which started it.
        if (!CampaignContext.EncounterIndex.TryGetValue(next.Encounter!, out var encounter))
        {
            GD.PrintErr($"[CampaignRun] unknown encounter '{next.Encounter}' on node {next.Id}");
            return (Step.BackToMap, MapScenePath, "next encounter is missing");
        }

        CampaignContext.CurrentNodeId = next.Id;
        CampaignContext.CurrentEncounter = encounter;
        CampaignContext.MatchConfig = new MatchConfig();
        if (region != null)
            CampaignContext.CurrentRegionSkinId = region.BoardSkin ?? "default";

        // A fresh duel must not inherit the last one's seed, or every fight in
        // the run plays out identically.
        CampaignContext.DebugSeed = null;

        GD.Print($"[CampaignRun] next duel: {next.Id} -> {encounter.Name}");
        return (Step.NextDuel, DuelScenePath, encounter.Name);
    }
}

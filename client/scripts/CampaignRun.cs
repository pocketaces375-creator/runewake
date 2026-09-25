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
        /// <summary>
        /// FABLE-020: this zone is finished and the next one's first duel is
        /// armed. Show the "new zone" screen, which offers to continue.
        /// </summary>
        NewZone,
    }

    public const string DuelScenePath = "res://scenes/duel/DuelScene.tscn";
    public const string MapScenePath = "res://scenes/map/MapScene.tscn";

    /// <summary>The region graph the player is currently in, or null if unreadable.</summary>
    public static MapRegion? LoadCurrentRegion() => LoadRegion(CampaignContext.GetRegionIdForMap());

    private static Dictionary<string, string>? _nodeRegion;

    /// <summary>Which region file a node id lives in (scans content/map once).</summary>
    public static string? RegionOfNode(string nodeId)
    {
        if (_nodeRegion == null)
        {
            _nodeRegion = new Dictionary<string, string>();
            using var dir = DirAccess.Open("res://content/map");
            if (dir != null)
            {
                foreach (var f in dir.GetFiles())
                {
                    var name = f.EndsWith(".remap") ? f[..^6] : f;
                    if (!name.EndsWith(".json")) continue;
                    var r = LoadRegion(name[..^5]);
                    if (r == null) continue;
                    foreach (var n in r.Nodes) _nodeRegion[n.Id] = r.Id;
                }
            }
        }
        return _nodeRegion.TryGetValue(nodeId, out var id) ? id : null;
    }

    /// <summary>FABLE-020: where "back to the map" goes for this node — the world map for world places.</summary>
    public static string MapPathFor(string? nodeId) =>
        WorldService.IsWorldBlip(nodeId) ? WorldService.WorldMapScenePath
        : TowerScene.IsTowerNode(nodeId) ? TowerScene.ScenePath
        : MapScenePath;

    /// <summary>A region graph by id, or null if unreadable.</summary>
    public static MapRegion? LoadRegion(string regionId)
    {
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
    /// FABLE-033: what Continue will do, as a line for the button — read-only, so
    /// the end screen can say "Next: Thornbark" before the player commits. Mirrors
    /// AdvanceAfterVictory's decisions without arming anything.
    /// </summary>
    public static string PeekAfterVictory()
    {
        var just = CampaignContext.CurrentNodeId;
        if (WorldService.IsWorldBlip(just)) return "Back to the world map";
        if (TowerScene.IsTowerNode(just)) return "Back to the Tower";
        var region = LoadCurrentRegion();
        var next = FindNextDuelNode(region);
        if (next == null)
        {
            bool crossroads = CampaignContext.Progression != null
                && !CampaignContext.Progression.ClearedNodes.Contains("wx|crossroads") && WorldService.CrossroadsEarned();
            return crossroads ? $"New zone: {WorldService.Atlas.HubName}" : "Back to the map";
        }
        string? zoneBefore = string.IsNullOrEmpty(just) ? null : RegionOfNode(just!);
        if (region != null && zoneBefore != null && region.Id != zoneBefore) return $"New zone: {region.Name}";
        return CampaignContext.EncounterIndex.TryGetValue(next.Encounter!, out var e) ? $"Next: {e.Name}" : "Back to the map";
    }

    /// <summary>
    /// FABLE-033: bank the win without arming the next fight — "Back to the map"
    /// from the victory screen. The Crossroads still opens if this was the win
    /// that earned it, so leaving by the map costs nothing.
    /// </summary>
    public static void BankVictory()
    {
        var just = CampaignContext.CurrentNodeId;
        if (!string.IsNullOrEmpty(just)) CampaignContext.Progression?.MarkNodeCleared(just!);
        if (CampaignContext.Progression != null && !CampaignContext.Progression.ClearedNodes.Contains("wx|crossroads") && WorldService.CrossroadsEarned())
        {
            WorldService.OpenCrossroads();
            CampaignContext.CrossroadsJustOpened = true;
            GD.Print("[CampaignRun] the Crossroads opened (via the map)");
        }
        try { CampaignContext.SaveManager?.Save(); }
        catch (System.Exception ex) { GD.PrintErr($"[CampaignRun] save failed, continuing anyway: {ex.Message}"); }
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
        // FABLE-020: the zone this fight belonged to. Not GetRegionIdForMap():
        // OnGameOver has usually marked the node cleared already, and clearing a
        // WardenBoss moves the region chain on, so by now that answers with the
        // NEXT zone.
        string? zoneBefore = string.IsNullOrEmpty(justCleared) ? null : RegionOfNode(justCleared!);
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

        // FABLE-020: a fight in the shared endless world goes back to the world
        // map (or announces the new page/area its guardian opened).
        if (WorldService.IsWorldBlip(justCleared))
            return WorldService.AfterWorldVictory(justCleared!);
        if (TowerScene.IsTowerNode(justCleared))
            return TowerScene.AfterTowerVictory(justCleared!);

        // FABLE-020: the first time the authored starting chain is finished,
        // the Crossroads — the way into the open world — opens.
        bool crossroadsJustOpened = false;
        if (!CampaignContext.Progression!.ClearedNodes.Contains("wx|crossroads") && WorldService.CrossroadsEarned())
        {
            WorldService.OpenCrossroads();
            crossroadsJustOpened = true;
            CampaignContext.CrossroadsJustOpened = true;
            GD.Print("[CampaignRun] the Crossroads opened");
        }

        // 2. Ask the region what is next.
        var region = LoadCurrentRegion();
        var next = FindNextDuelNode(region);
        if (next == null)
        {
            if (crossroadsJustOpened)
            {
                CampaignContext.PendingZoneInfo = WorldService.CrossroadsAnnouncement();
                return (Step.NewZone, ZoneTransitionScene.ScenePath, WorldService.Atlas.HubName);
            }
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

        // FABLE-020: the win finished the zone — announce the new one first.
        if (region != null && zoneBefore != null && region.Id != zoneBefore)
        {
            CampaignContext.CurrentRegionId = region.Id;
            CampaignContext.PendingZone = region;
            GD.Print($"[CampaignRun] zone complete: {zoneBefore} → {region.Id} ({region.Name}); first duel {next.Id} -> {encounter.Name}");
            return (Step.NewZone, ZoneTransitionScene.ScenePath, region.Name);
        }

        GD.Print($"[CampaignRun] next duel: {next.Id} -> {encounter.Name}");
        return (Step.NextDuel, DuelScenePath, encounter.Name);
    }
}

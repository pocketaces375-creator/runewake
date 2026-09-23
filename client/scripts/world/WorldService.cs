using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.Supabase;
using Runewake.Engine.World;

namespace Runewake.Client;

/// <summary>
/// FABLE-020: the client's door into the shared endless world (engine/World).
///
/// The world is generated, never stored. The player's place in it rides in the
/// ordinary save (ProgressionState.ClearedNodes — already saved to SQLite and
/// backed up to the cloud) as self-describing strings:
///   "w1:elvenwood:0:3:17"   a place beaten        (the blip id itself)
///   "wv|w1:elvenwood:0:3:17" a place visited
///   "wa|lost_forge.0"        an area opened by a Gateway
///   "wx|crossroads"          the Crossroads is open
///   "wp|elvenwood.0.3"       the page the player was last on
/// So nothing new needs saving, syncing or migrating.
///
/// Discoveries — who found what first — live in Supabase (world_tower_coop.sql)
/// and are fetched per page; offline, everything unfound just reads uncharted.
/// </summary>
public static class WorldService
{
    public const string WorldMapScenePath = "res://scenes/world/WorldMapScene.tscn";
    public const string CrossroadsScenePath = "res://scenes/world/CrossroadsScene.tscn";

    private static AtlasDef? _atlas;
    private static WorldGenerator? _gen;
    private static List<CardDef>? _pool;
    private static readonly Dictionary<string, HashSet<string>> _discovered = new();

    public static AtlasDef Atlas => _atlas ??= AtlasDef.FromJson(Godot.FileAccess.GetFileAsString("res://content/world/atlas.json"));
    public static WorldGenerator Generator => _gen ??= new WorldGenerator(Atlas);
    public static List<CardDef> Pool => _pool ??= EncounterForge.PlayablePool(CardRegistry.GetAll());

    /// <summary>The player's world progress, rebuilt from the save (cheap: it's a set scan).</summary>
    public static WorldProgress Progress
    {
        get
        {
            var p = new WorldProgress(Generator);
            var cleared = CampaignContext.Progression?.ClearedNodes;
            if (cleared != null)
            {
                p.LoadSaveEntries(cleared);
                if (!p.CrossroadsOpen && CrossroadsEarned()) p.CrossroadsOpen = true;
            }
            return p;
        }
    }

    /// <summary>True once the authored starting chain's final boss is beaten.</summary>
    public static bool CrossroadsEarned()
    {
        var region = CampaignRun.LoadRegion(Atlas.StartingChainEnd);
        var boss = region?.Nodes.LastOrDefault(n => n.Type == MapNodeType.WardenBoss);
        return boss != null && CampaignContext.Progression?.IsNodeCleared(boss.Id) == true;
    }

    public static bool IsWorldBlip(string? id) => id != null && WorldGenerator.TryParseBlipId(id, out _, out _, out _);

    // ── The page the player is looking at ──────────────────────────────

    public static PageAddress? LastPage
    {
        get
        {
            var e = CampaignContext.Progression?.ClearedNodes.FirstOrDefault(x => x.StartsWith("wp|"));
            if (e == null) return null;
            var parts = e[3..].Split('.');
            return parts.Length == 3 && int.TryParse(parts[1], out int i) && int.TryParse(parts[2], out int pg)
                ? new PageAddress(parts[0], i, pg) : null;
        }
        set
        {
            var set = CampaignContext.Progression?.ClearedNodes;
            if (set == null) return;
            set.RemoveWhere(x => x.StartsWith("wp|"));
            if (value is PageAddress a) set.Add($"wp|{a.Biome}.{a.Instance}.{a.Page}");
        }
    }

    /// <summary>The page to show when the world map opens.</summary>
    public static PageAddress CurrentPage
    {
        get => CampaignContext.WorldPage ?? LastPage ?? new PageAddress(Atlas.OpenBiomes.First().Id, 0, 0);
        set { CampaignContext.WorldPage = value; LastPage = value; }
    }

    // ── Mutations (all persisted through the ordinary save) ────────────

    public static void OpenCrossroads()
    {
        CampaignContext.Progression?.ClearedNodes.Add("wx|crossroads");
        Save();
    }

    public static void MarkVisited(Blip b)
    {
        if (CampaignContext.Progression == null) return;
        if (!CampaignContext.Progression.ClearedNodes.Contains(b.Id))
            CampaignContext.Progression.ClearedNodes.Add("wv|" + b.Id);
        Save();
    }

    /// <summary>Beat / complete a place. Returns what it opened.</summary>
    public static WorldUnlock MarkCleared(WorldPage page, Blip b)
    {
        var prog = CampaignContext.Progression;
        if (prog == null) return default;
        var wp = Progress;
        var unlock = wp.Clear(page, b);
        prog.ClearedNodes.Remove("wv|" + b.Id);
        prog.MarkNodeCleared(b.Id);
        if (unlock.Kind == WorldUnlockKind.HiddenArea && unlock.Page is PageAddress hp)
            prog.ClearedNodes.Add($"wa|{hp.Biome}.{hp.Instance}");
        Save();
        _ = ReportClear(b.Id);
        return unlock;
    }

    private static void Save()
    {
        try { CampaignContext.SaveManager?.Save(); }
        catch (Exception ex) { GD.PrintErr($"[World] save failed: {ex.Message}"); }
    }

    // ── Fights ─────────────────────────────────────────────────────────

    /// <summary>Arm a fight blip exactly the way the campaign map arms a duel.</summary>
    public static EncounterDef ArmFight(WorldPage page, Blip b)
    {
        var enc = EncounterForge.For(Atlas, page, b, Pool);
        CampaignContext.EncounterIndex[enc.Id] = enc;
        CampaignContext.CurrentNodeId = b.Id;
        CampaignContext.CurrentEncounter = enc;
        CampaignContext.MatchConfig = new Runewake.Engine.State.MatchConfig();
        CampaignContext.CurrentRegionSkinId = Atlas.Biome(page.Address.Biome).BoardSkin ?? "default";
        CampaignContext.DebugSeed = null;
        return enc;
    }

    /// <summary>
    /// Called by CampaignRun after a world fight is won. Records the clear and
    /// says where to go next: back to the world map, or — after a Warden or an
    /// area boss — the "new zone" screen for the page/area it opened.
    /// </summary>
    public static (CampaignRun.Step step, string scenePath, string what) AfterWorldVictory(string blipId)
    {
        if (!WorldGenerator.TryParseBlipId(blipId, out _, out var addr, out int idx))
            return (CampaignRun.Step.BackToMap, WorldMapScenePath, "world");
        var page = Generator.Page(addr);
        var b = page.Blips[idx];
        var unlock = MarkCleared(page, b);
        if (unlock.Page is PageAddress next && unlock.Kind is WorldUnlockKind.NextPage or WorldUnlockKind.NextArea)
        {
            CurrentPage = next;
            var np = Generator.Page(next);
            CampaignContext.PendingZoneInfo = new ZoneInfo
            {
                Kicker = unlock.Kind == WorldUnlockKind.NextArea ? "A NEW AREA" : "THE ROAD GOES ON",
                Title = unlock.Kind == WorldUnlockKind.NextArea ? $"{np.BiomeName} — Area {next.Instance + 1}" : $"{np.BiomeName} — Page {next.Page + 1}",
                Line = unlock.Kind == WorldUnlockKind.NextArea
                    ? "You've reached a new zone with new monsters and obstacles. It is harder here."
                    : "You've reached a new zone with new monsters and obstacles.",
                Strata = np.Strata,
                ContinueLabel = "Continue",
                ContinuePath = WorldMapScenePath,
            };
            return (CampaignRun.Step.NewZone, ZoneTransitionScene.ScenePath, CampaignContext.PendingZoneInfo.Title);
        }
        CurrentPage = addr;
        return (CampaignRun.Step.BackToMap, WorldMapScenePath, "back to the world map");
    }

    public static ZoneInfo CrossroadsAnnouncement() => new()
    {
        Kicker = "THE WORLD OPENS",
        Title = Atlas.HubName,
        Line = "Beyond the starting lands the roads run on forever. Every place you find, you find for everyone.",
        Strata = "DAWN",
        ContinueLabel = "Go to the Crossroads",
        ContinuePath = CrossroadsScenePath,
    };

    // ── Discoveries (Supabase) ─────────────────────────────────────────

    private static WorldDiscoverySync? Sync()
    {
        var sm = CampaignContext.SyncManager;
        if (sm == null || !GodotObject.IsInstanceValid(sm) || !sm.IsConfigured || sm.Session?.IsValid != true) return null;
        return new WorldDiscoverySync(sm.Config, Http.Create(12));
    }

    /// <summary>Ids on this page anyone has discovered. Cached; empty offline.</summary>
    public static HashSet<string> Discovered(WorldPage page) =>
        _discovered.TryGetValue(page.Key, out var s) ? s : new HashSet<string>();

    public static async Task RefreshDiscoveries(WorldPage page)
    {
        // Never throw: these run fire-and-forget, and an unobserved faulted
        // task trips the global crash handler ("Something went wrong").
        try
        {
            var sync = Sync();
            if (sync == null) return;
            var (ok, ids, err) = await sync.PageDiscoveries(CampaignContext.SyncManager!.Session!, page.Key);
            if (ok) _discovered[page.Key] = ids;
            else GD.Print($"[World] page discoveries unavailable: {err}");
        }
        catch (Exception ex) { GD.Print($"[World] page discoveries failed: {ex.Message}"); }
    }

    /// <summary>Tell the server we entered this place. True only if we are the first ever.</summary>
    public static async Task<bool> Discover(WorldPage page, Blip b)
    {
        if (!_discovered.TryGetValue(page.Key, out var set)) _discovered[page.Key] = set = new HashSet<string>();
        bool knownBefore = set.Contains(b.Id);
        set.Add(b.Id);
        try
        {
            var sync = Sync();
            if (sync == null || knownBefore) return false;
            var r = await sync.Discover(CampaignContext.SyncManager!.Session!, b.Id);
            if (!r.Ok) GD.Print($"[World] discover failed: {r.Error}");
            return r.Ok && r.First;
        }
        catch (Exception ex) { GD.Print($"[World] discover failed: {ex.Message}"); return false; }
    }

    private static async Task ReportClear(string blipId)
    {
        try
        {
            var sync = Sync();
            if (sync != null) await sync.Clear(CampaignContext.SyncManager!.Session!, blipId);
        }
        catch (Exception ex) { GD.Print($"[World] clear report failed: {ex.Message}"); }
    }
}

/// <summary>What the "new zone" screen announces (campaign regions and world pages alike).</summary>
public sealed class ZoneInfo
{
    public string Kicker { get; init; } = "A NEW ZONE";
    public string Title { get; init; } = "";
    public string Line { get; init; } = "You've reached a new zone with new monsters and obstacles.";
    public string? Strata { get; init; }
    public string ContinueLabel { get; init; } = "Continue";
    public string ContinuePath { get; init; } = CampaignRun.DuelScenePath;
    public string MapPath { get; init; } = CampaignRun.MapScenePath;
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Runewake.Engine.World;

/// <summary>How a blip looks to one player.</summary>
public enum BlipVisibility
{
    /// <summary>Not shown at all (more than one step beyond what you've beaten).</summary>
    Hidden,
    /// <summary>You can travel here, but nobody has EVER found it: the road runs out into nothing, no dot.</summary>
    Uncharted,
    /// <summary>You can travel here and someone has found it before: a dimmed dot.</summary>
    Known,
    /// <summary>You have been here (found it) but not beaten it yet.</summary>
    Visited,
    /// <summary>You have beaten / completed it.</summary>
    Cleared,
}

/// <summary>
/// FABLE-020: one player's position in the shared world, and the rules for
/// what they can see and where they can go. Pure — no I/O. The client fills
/// it from the save (cleared/visited ids) and from Supabase (which blips on
/// the current page anyone has ever discovered).
///
/// Visibility, exactly as specified:
///   "If someone has discovered that area, you see one more circle past where
///    you've beat and it's slightly dimmed out. If nobody has discovered it you
///    can't see the blip. The road will connect to nothing."
/// </summary>
public sealed class WorldProgress
{
    private readonly WorldGenerator _gen;

    /// <summary>Blip ids this player has beaten / completed.</summary>
    public HashSet<string> Cleared { get; } = new(StringComparer.Ordinal);
    /// <summary>Blip ids this player has entered (discovered for themselves).</summary>
    public HashSet<string> Visited { get; } = new(StringComparer.Ordinal);
    /// <summary>Areas opened by something other than the page chain: the Crossroads, a Gateway.</summary>
    public HashSet<AreaAddress> OpenedAreas { get; } = new();
    /// <summary>True once the authored starting chain is done and the Crossroads is open.</summary>
    public bool CrossroadsOpen { get; set; }

    public WorldProgress(WorldGenerator gen) { _gen = gen; }

    /// <summary>Areas you can start from the Crossroads: instance 0 of every open biome.</summary>
    public IEnumerable<AreaAddress> CrossroadsAreas =>
        CrossroadsOpen ? _gen.Atlas.OpenBiomes.Select(b => new AreaAddress(b.Id, 0)) : Enumerable.Empty<AreaAddress>();

    /// <summary>Can this player enter this page at all?</summary>
    public bool IsPageOpen(PageAddress p)
    {
        if (p.Page == 0 && (OpenedAreas.Contains(p.Area) || (p.Instance == 0 && CrossroadsOpen && !_gen.Atlas.Biome(p.Biome).Hidden)))
            return true;
        var prev = _gen.PreviousPage(p);
        if (prev == null) return false;
        return Cleared.Contains(_gen.Page(prev.Value).Guardian.Id);
    }

    /// <summary>Can this player travel to this blip right now?</summary>
    public bool IsReachable(WorldPage page, Blip b)
    {
        if (!IsPageOpen(page.Address)) return false;
        if (b.IsEntry) return true;
        foreach (var from in page.Blips)
            if (from.Next.Contains(b.Index) && Cleared.Contains(from.Id)) return true;
        return false;
    }

    /// <param name="globallyDiscovered">Ids on this page that ANY player has ever discovered (from Supabase).</param>
    public BlipVisibility VisibilityOf(WorldPage page, Blip b, ISet<string> globallyDiscovered)
    {
        if (Cleared.Contains(b.Id)) return BlipVisibility.Cleared;
        if (Visited.Contains(b.Id)) return BlipVisibility.Visited;
        if (!IsReachable(page, b)) return BlipVisibility.Hidden;
        return globallyDiscovered.Contains(b.Id) ? BlipVisibility.Known : BlipVisibility.Uncharted;
    }

    /// <summary>Everything a map screen needs for one page.</summary>
    public Dictionary<int, BlipVisibility> Visibility(WorldPage page, ISet<string> globallyDiscovered) =>
        page.Blips.ToDictionary(b => b.Index, b => VisibilityOf(page, b, globallyDiscovered));

    /// <summary>
    /// Mark a blip visited (you entered it). Returns false if it isn't reachable
    /// — the client must not let a player jump ahead.
    /// </summary>
    public bool Visit(WorldPage page, Blip b)
    {
        if (!Cleared.Contains(b.Id) && !Visited.Contains(b.Id) && !IsReachable(page, b)) return false;
        Visited.Add(b.Id);
        return true;
    }

    /// <summary>
    /// Mark a blip beaten. Opens whatever it opens: a Gateway opens its hidden
    /// biome; the page chain opens itself via the guardian check in IsPageOpen.
    /// Returns what opened, for the UI.
    /// </summary>
    public WorldUnlock Clear(WorldPage page, Blip b)
    {
        Visited.Add(b.Id);
        Cleared.Add(b.Id);
        if (b.Kind == BlipKind.Gateway && b.GatewayTo != null)
        {
            var area = new AreaAddress(b.GatewayTo, 0);
            OpenedAreas.Add(area);
            return new WorldUnlock(WorldUnlockKind.HiddenArea, new PageAddress(area.Biome, 0, 0));
        }
        if (b.Kind == BlipKind.AreaBoss)
            return new WorldUnlock(WorldUnlockKind.NextArea, _gen.NextPage(page.Address));
        if (b.Kind == BlipKind.Warden)
            return new WorldUnlock(WorldUnlockKind.NextPage, _gen.NextPage(page.Address));
        return new WorldUnlock(WorldUnlockKind.None, null);
    }

    /// <summary>Reachable places on a page you haven't cleared: the "where can I go" list.</summary>
    public IEnumerable<Blip> Frontier(WorldPage page) =>
        page.Blips.Where(b => !Cleared.Contains(b.Id) && IsReachable(page, b));

    // ── Save format ────────────────────────────────────────────────────
    // The world rides in ProgressionState.ClearedNodes (already saved and
    // cloud-synced) as prefixed strings, so no schema change is needed:
    //   "w1:elvenwood:0:3:17"     cleared blip (ids are self-describing)
    //   "wv|w1:elvenwood:0:3:17"  visited, not cleared
    //   "wa|deepforge.0"          area opened by a Gateway
    //   "wx|crossroads"           the Crossroads is open

    public IEnumerable<string> ToSaveEntries()
    {
        foreach (var id in Cleared) yield return id;
        foreach (var id in Visited) if (!Cleared.Contains(id)) yield return "wv|" + id;
        foreach (var a in OpenedAreas) yield return $"wa|{a.Biome}.{a.Instance}";
        if (CrossroadsOpen) yield return "wx|crossroads";
    }

    public void LoadSaveEntries(IEnumerable<string> entries)
    {
        foreach (var e in entries)
        {
            if (e.StartsWith("wv|")) Visited.Add(e[3..]);
            else if (e.StartsWith("wa|"))
            {
                var s = e[3..]; int dot = s.LastIndexOf('.');
                if (dot > 0 && int.TryParse(s[(dot + 1)..], out int inst)) OpenedAreas.Add(new AreaAddress(s[..dot], inst));
            }
            else if (e == "wx|crossroads") CrossroadsOpen = true;
            else if (WorldGenerator.TryParseBlipId(e, out _, out _, out _)) { Cleared.Add(e); Visited.Add(e); }
        }
    }
}

public enum WorldUnlockKind { None, NextPage, NextArea, HiddenArea }

public readonly record struct WorldUnlock(WorldUnlockKind Kind, PageAddress? Page);

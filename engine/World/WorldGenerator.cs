using System;
using System.Collections.Generic;
using System.Linq;
using Runewake.Engine.State;

namespace Runewake.Engine.World;

/// <summary>
/// Stable 64-bit string hashing (FNV-1a) for world seeds.
///
/// Deliberately NOT System.Security.Cryptography: on Android the .NET crypto
/// shim needs a system libssl the app cannot load and aborts the process
/// (see client/scripts/supabase/GodotHttpHandler.cs). FNV is plenty for
/// seeding, is identical on every platform, and never changes.
/// </summary>
public static class StableHash
{
    public static ulong Of(params object[] parts)
    {
        ulong h = 14695981039346656037UL;
        foreach (var p in parts)
        {
            var s = Convert.ToString(p, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            foreach (char c in s)
            {
                h ^= c;
                h *= 1099511628211UL;
            }
            h ^= 0x1F; // separator, so ("ab","c") != ("a","bc")
            h *= 1099511628211UL;
        }
        return h;
    }
}

/// <summary>
/// FABLE-020: generates the shared world, one page at a time, on demand.
/// Pure: the same atlas + address always yields the same page, on any device.
/// </summary>
public sealed class WorldGenerator
{
    private readonly AtlasDef _atlas;
    private readonly Dictionary<PageAddress, WorldPage> _cache = new();

    public WorldGenerator(AtlasDef atlas) { _atlas = atlas; }

    public AtlasDef Atlas => _atlas;

    private SeededRng Rng(params object[] parts) =>
        new(StableHash.Of(new object[] { _atlas.Seed, _atlas.Version }.Concat(parts).ToArray()));

    /// <summary>How many pages an area has (5–7 by default). Stable per area.</summary>
    public int PagesInArea(AreaAddress area)
    {
        var b = _atlas.Biome(area.Biome);
        return Rng("area-pages", area.Biome, area.Instance).NextInt(b.PagesMin, b.PagesMax + 1);
    }

    /// <summary>Difficulty in [0,1): rises with depth and never quite reaches 1 — endless, but bounded.</summary>
    public static double DepthDifficulty(double baseDifficulty, double depth) =>
        1.0 - (1.0 - Math.Clamp(baseDifficulty, 0, 0.95)) * Math.Exp(-DepthRate * depth);

    /// <summary>How fast difficulty climbs per page. ~0.47 by area 5, ~0.85 by area 20 (from a 0.20 base).</summary>
    public const double DepthRate = 0.012;

    public WorldPage Page(PageAddress addr)
    {
        if (_cache.TryGetValue(addr, out var cached)) return cached;
        var page = Generate(addr);
        if (_cache.Count > 64) _cache.Clear();
        _cache[addr] = page;
        return page;
    }

    private WorldPage Generate(PageAddress addr)
    {
        var biome = _atlas.Biome(addr.Biome);
        if (addr.Instance < 0 || addr.Page < 0) throw new ArgumentOutOfRangeException(nameof(addr));
        int pages = PagesInArea(addr.Area);
        if (addr.Page >= pages) throw new ArgumentOutOfRangeException(nameof(addr), $"{addr.Area} has {pages} pages");

        var rng = Rng("page", addr.Biome, addr.Instance, addr.Page);
        int n = rng.NextInt(biome.BlipsMin, biome.BlipsMax + 1);
        bool lastPage = addr.Page == pages - 1;

        // ── Columns ──────────────────────────────────────────────────────
        // The guardian sits alone in the last column. Everything else spreads
        // over 6–16 columns of 2–6 places, entries on the left.
        int regular = n - 1;
        int cols = Math.Clamp((int)Math.Round(regular / 4.2), 6, 16);
        while (cols > 2 && 2 * cols > regular) cols--;   // tiny pages: every column still gets 2+
        var counts = new int[cols];
        counts[0] = Math.Min(2 + rng.NextInt(2), regular - 2 * (cols - 1));   // 2–3 entries
        int left = regular - counts[0];
        for (int c = 1; c < cols; c++) counts[c] = 2;
        left -= 2 * (cols - 1);
        while (left > 0)
        {
            int c = 1 + rng.NextInt(cols - 1);
            if (counts[c] >= 7) { if (counts.Skip(1).All(x => x >= 7)) counts[c]++; else continue; }
            else counts[c]++;
            left--;
        }

        // ── Place the blips ──────────────────────────────────────────────
        var gateways = biome.Gateways;
        bool gatewayPlaced = false;
        bool gatewayEligiblePage = !(addr.Instance == 0 && addr.Page == 0);
        var usedNames = new HashSet<string>();
        var blips = new List<Blip>(n);
        var colStart = new int[cols + 1];
        int idx = 0;
        double pageDepth = addr.Instance * 7.0 + addr.Page;

        for (int c = 0; c < cols; c++)
        {
            colStart[c] = idx;
            int k = counts[c];
            for (int i = 0; i < k; i++)
            {
                int x = 70 + (int)Math.Round(c * (840.0 / cols)) + rng.NextInt(-14, 15);
                double slot = 520.0 / k;
                int jit = (int)Math.Min(20, slot / 4);
                int y = 40 + (int)Math.Round(slot * (i + 0.5)) + rng.NextInt(-jit, jit + 1);

                BlipKind kind = BlipKind.Duel;
                EventKind? ev = null;
                string? gateTo = null;
                if (c > 0)
                {
                    double roll = rng.NextU64() / (double)ulong.MaxValue;
                    if (roll < _atlas.LoreChance) kind = BlipKind.Lore;
                    else
                    {
                        if (!gatewayPlaced && gatewayEligiblePage && c >= 2)
                            foreach (var g in gateways)
                                if (rng.NextBool(g.Chance)) { kind = BlipKind.Gateway; gateTo = g.To; gatewayPlaced = true; break; }
                        if (kind == BlipKind.Duel)
                        {
                            double r = rng.NextU64() / (double)ulong.MaxValue;
                            if (c >= 2 && r < 0.10) kind = BlipKind.Elite;
                            else if (r < 0.24)
                            {
                                kind = BlipKind.Event;
                                double e = rng.NextU64() / (double)ulong.MaxValue;
                                ev = e < 0.30 ? EventKind.Shrine : e < 0.50 ? EventKind.Merchant : e < 0.80 ? EventKind.Dig : EventKind.Cache;
                            }
                        }
                    }
                }

                double depth = pageDepth + (double)c / cols;
                double diff = DepthDifficulty(biome.BaseDifficulty, depth) + (kind == BlipKind.Elite ? 0.05 : 0);
                blips.Add(new Blip
                {
                    Id = BlipId(addr, idx),
                    Index = idx,
                    Kind = kind,
                    Event = ev,
                    Name = UniqueName(rng, biome, usedNames, kind, gateTo),
                    X = Math.Clamp(x, 30, 900),
                    Y = Math.Clamp(y, 30, 570),
                    Column = c,
                    IsEntry = c == 0,
                    GatewayTo = gateTo,
                    Difficulty = Math.Min(0.999, diff),
                });
                idx++;
            }
        }
        colStart[cols] = idx;

        // The guardian.
        var gKind = lastPage ? BlipKind.AreaBoss : BlipKind.Warden;
        double gDiff = DepthDifficulty(biome.BaseDifficulty, pageDepth + 1) + (lastPage ? 0.12 : 0.06);
        blips.Add(new Blip
        {
            Id = BlipId(addr, idx),
            Index = idx,
            Kind = gKind,
            Name = lastPage ? Pick(rng, biome.BossNames, "The Heart of " + biome.Name) : Pick(rng, biome.WardenNames, "The Warden"),
            X = 955,
            Y = 300,
            Column = cols,
            Difficulty = Math.Min(0.999, gDiff),
        });

        // ── Roads ────────────────────────────────────────────────────────
        // Each place leads on to 1–2 places in the next column, chosen by
        // relative height so roads rarely cross; then every place in the next
        // column is guaranteed at least one road in.
        for (int c = 0; c < cols - 1; c++)
        {
            int a0 = colStart[c], an = counts[c], b0 = colStart[c + 1], bn = counts[c + 1];
            var hasIn = new bool[bn];
            for (int i = 0; i < an; i++)
            {
                int j = Math.Min(bn - 1, (int)((i + 0.5) / an * bn));
                Link(blips[a0 + i], b0 + j); hasIn[j] = true;
                if (rng.NextBool(0.45))
                {
                    int j2 = j + (rng.NextBool() ? 1 : -1);
                    if (j2 >= 0 && j2 < bn) { Link(blips[a0 + i], b0 + j2); hasIn[j2] = true; }
                }
            }
            for (int j = 0; j < bn; j++)
            {
                if (hasIn[j]) continue;
                int i = Math.Min(an - 1, (int)((j + 0.5) / bn * an));
                Link(blips[a0 + i], b0 + j);
            }
        }
        // Last regular column → guardian.
        for (int i = colStart[cols - 1]; i < colStart[cols]; i++) Link(blips[i], idx);

        return new WorldPage
        {
            Address = addr,
            GeneratorVersion = _atlas.Version,
            BiomeName = biome.Name,
            Strata = biome.Strata,
            Strata2 = biome.Strata2,
            PagesInArea = pages,
            Blips = blips,
        };

        static void Link(Blip from, int to) { if (!from.Next.Contains(to)) from.Next.Add(to); }
    }

    public string BlipId(PageAddress addr, int index) => $"{addr.Key(_atlas.Version)}:{index}";

    /// <summary>Parse "w1:elvenwood:0:3:17" back into an address + index.</summary>
    public static bool TryParseBlipId(string id, out int version, out PageAddress page, out int index)
    {
        version = 0; page = default; index = -1;
        var p = id.Split(':');
        if (p.Length != 5 || p[0].Length < 2 || p[0][0] != 'w') return false;
        if (!int.TryParse(p[0].AsSpan(1), out version)) return false;
        if (!int.TryParse(p[2], out int inst) || !int.TryParse(p[3], out int pg) || !int.TryParse(p[4], out index)) return false;
        page = new PageAddress(p[1], inst, pg);
        return true;
    }

    private static string Pick(SeededRng rng, List<string> bank, string fallback) =>
        bank.Count == 0 ? fallback : bank[rng.NextInt(bank.Count)];

    private static string UniqueName(SeededRng rng, BiomeDef b, HashSet<string> used, BlipKind kind, string? gateTo)
    {
        if (kind == BlipKind.Gateway) return "A Hidden Road";
        for (int tries = 0; tries < 12; tries++)
        {
            var name = $"{Pick(rng, b.PlaceAdjectives, "Quiet")} {Pick(rng, b.PlaceNouns, "Clearing")}";
            if (used.Add(name)) return name;
        }
        var fallback = $"{Pick(rng, b.PlaceAdjectives, "Quiet")} {Pick(rng, b.PlaceNouns, "Clearing")} {used.Count + 1}";
        used.Add(fallback);
        return fallback;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Navigation: what comes after what
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>The page reached by beating this page's guardian (next page, or the next area's first page).</summary>
    public PageAddress NextPage(PageAddress addr) =>
        addr.Page + 1 < PagesInArea(addr.Area)
            ? addr with { Page = addr.Page + 1 }
            : new PageAddress(addr.Biome, addr.Instance + 1, 0);

    /// <summary>The page whose guardian unlocks this one, or null for an area's first page of instance 0.</summary>
    public PageAddress? PreviousPage(PageAddress addr)
    {
        if (addr.Page > 0) return addr with { Page = addr.Page - 1 };
        if (addr.Instance > 0)
        {
            var prevArea = new AreaAddress(addr.Biome, addr.Instance - 1);
            return new PageAddress(addr.Biome, addr.Instance - 1, PagesInArea(prevArea) - 1);
        }
        return null;
    }
}

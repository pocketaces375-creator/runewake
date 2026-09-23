using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Runewake.Engine.World;

// ═══════════════════════════════════════════════════════════════════════════
// FABLE-020 — the shared, endless world.
//
//   Atlas   the fixed list of biomes (Elvenwood, Deepdelve Mines, ...) and how
//           they connect: which are open from the Crossroads, which are hidden
//           behind a rare Gateway.
//   Area    one instance of a biome: (biome, instance). Elvenwood #0 is the
//           first Elvenwood everyone reaches; past its final boss the road runs
//           on into Elvenwood #1, deeper and harder, forever.
//   Page    one screen of an area. 5–7 pages per area, 25–60 blips per page,
//           so an area holds a few hundred places without any one screen
//           being a wall of dots.
//   Blip    one place on a page: a fight, an event, a lore find, a gateway.
//
// Everything is a pure function of (atlas, generator version, address). The
// same address produces the same page on every phone, forever — that is what
// makes it ONE shared world and what makes "first to discover" meaningful.
// Nothing is stored for the world itself; only discoveries are (Supabase).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>What a blip is.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BlipKind
{
    /// <summary>An ordinary fight.</summary>
    Duel,
    /// <summary>A harder fight with a twist (more Vigor, better deck, better loot).</summary>
    Elite,
    /// <summary>The page's guardian. Beating it opens the road to the next page.</summary>
    Warden,
    /// <summary>The area's final boss (last page only). Beating it opens the next area.</summary>
    AreaBoss,
    /// <summary>A non-combat stop: shrine, merchant, dig, cache.</summary>
    Event,
    /// <summary>A rare lore find (about 1 in 100 places).</summary>
    Lore,
    /// <summary>A very rare road into a hidden biome (e.g. the Ancient Lost Forge).</summary>
    Gateway,
}

/// <summary>Flavour of an Event blip.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventKind { Shrine, Merchant, Dig, Cache }

/// <summary>An area address: which biome, which instance of it.</summary>
public readonly record struct AreaAddress(string Biome, int Instance)
{
    public override string ToString() => $"{Biome}.{Instance}";
}

/// <summary>A page address inside the world.</summary>
public readonly record struct PageAddress(string Biome, int Instance, int Page)
{
    public AreaAddress Area => new(Biome, Instance);
    /// <summary>Stable key, used as the Supabase page_key (e.g. "w1:elvenwood:0:3").</summary>
    public string Key(int version) => $"w{version}:{Biome}:{Instance}:{Page}";
    public override string ToString() => $"{Biome}.{Instance}.p{Page}";
}

/// <summary>One place on a page.</summary>
public sealed class Blip
{
    /// <summary>Globally unique, stable id: "w1:elvenwood:0:3:17".</summary>
    public string Id { get; init; } = "";
    /// <summary>Index within the page (0..n-1), in column order.</summary>
    public int Index { get; init; }
    public BlipKind Kind { get; init; }
    public EventKind? Event { get; init; }
    /// <summary>Display name ("Whispering Hollow").</summary>
    public string Name { get; init; } = "";
    /// <summary>Position on a 1000 x 600 page canvas.</summary>
    public int X { get; init; }
    public int Y { get; init; }
    /// <summary>Column (0 = page entry side).</summary>
    public int Column { get; init; }
    /// <summary>Indices (same page) this blip's roads lead to.</summary>
    public List<int> Next { get; init; } = new();
    /// <summary>True for blips the page is entered through.</summary>
    public bool IsEntry { get; init; }
    /// <summary>For Gateway blips: the hidden biome this road leads into.</summary>
    public string? GatewayTo { get; init; }
    /// <summary>0..1 difficulty within the whole world (depth-scaled).</summary>
    public double Difficulty { get; init; }
}

/// <summary>One generated page.</summary>
public sealed class WorldPage
{
    public PageAddress Address { get; init; }
    public int GeneratorVersion { get; init; }
    public string BiomeName { get; init; } = "";
    public string Strata { get; init; } = "";
    public string? Strata2 { get; init; }
    /// <summary>How many pages this page's area has.</summary>
    public int PagesInArea { get; init; }
    public bool IsLastPageOfArea => Address.Page == PagesInArea - 1;
    public List<Blip> Blips { get; init; } = new();
    public string Key => Address.Key(GeneratorVersion);

    /// <summary>The page's closing fight: Warden, or AreaBoss on the last page.</summary>
    public Blip Guardian => Blips[^1];
}

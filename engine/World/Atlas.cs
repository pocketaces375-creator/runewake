using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Runewake.Engine.World;

/// <summary>A biome: the kind of place an area is.</summary>
public sealed class BiomeDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("strata")] public string Strata { get; set; } = "VERDANT";
    [JsonPropertyName("strata2")] public string? Strata2 { get; set; }
    [JsonPropertyName("board_skin")] public string? BoardSkin { get; set; }
    /// <summary>True = reachable only through a Gateway blip, never listed at the Crossroads.</summary>
    [JsonPropertyName("hidden")] public bool Hidden { get; set; }
    [JsonPropertyName("pages_min")] public int PagesMin { get; set; } = 5;
    [JsonPropertyName("pages_max")] public int PagesMax { get; set; } = 7;
    [JsonPropertyName("blips_min")] public int BlipsMin { get; set; } = 40;
    [JsonPropertyName("blips_max")] public int BlipsMax { get; set; } = 60;
    /// <summary>Base difficulty of this biome's first area (0 = starter, 1 = very hard).</summary>
    [JsonPropertyName("base_difficulty")] public double BaseDifficulty { get; set; } = 0.15;
    /// <summary>Gateways that can appear here: hidden biome id + per-blip chance.</summary>
    [JsonPropertyName("gateways")] public List<GatewayDef> Gateways { get; set; } = new();

    // Name banks. Place names are "{adjective} {place}"; foes are "{title} {foe}".
    [JsonPropertyName("place_adjectives")] public List<string> PlaceAdjectives { get; set; } = new();
    [JsonPropertyName("place_nouns")] public List<string> PlaceNouns { get; set; } = new();
    [JsonPropertyName("foes")] public List<string> Foes { get; set; } = new();
    [JsonPropertyName("foe_titles")] public List<string> FoeTitles { get; set; } = new();
    [JsonPropertyName("warden_names")] public List<string> WardenNames { get; set; } = new();
    [JsonPropertyName("boss_names")] public List<string> BossNames { get; set; } = new();
    /// <summary>Lore finds for this biome's rare Lore places (about 1 in 100).</summary>
    [JsonPropertyName("lore")] public List<string> Lore { get; set; } = new();
    /// <summary>Wielder classes that fit this biome (artifact loadouts).</summary>
    [JsonPropertyName("classes")] public List<string> Classes { get; set; } = new();
}

public sealed class GatewayDef
{
    [JsonPropertyName("to")] public string To { get; set; } = "";
    /// <summary>Chance per eligible blip. At most one gateway per page.</summary>
    [JsonPropertyName("chance")] public double Chance { get; set; } = 0.004;
}

/// <summary>The whole world's fixed shape.</summary>
public sealed class AtlasDef
{
    /// <summary>
    /// Generator version. Part of every blip id. Bump ONLY for a deliberate new
    /// world: changing the generator under a live version would move every
    /// place and orphan every discovery.
    /// </summary>
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    /// <summary>World seed. Same seed + version = same world for everyone.</summary>
    [JsonPropertyName("seed")] public ulong Seed { get; set; } = 0x52554E4557414B45; // "RUNEWAKE"
    [JsonPropertyName("hub_name")] public string HubName { get; set; } = "The Crossroads of Seals";
    /// <summary>
    /// The authored campaign region whose boss opens the Crossroads (the way
    /// into the open world). Set to "region_01" for testing the endless world
    /// early; the launch value is the last starting region ("region_04").
    /// </summary>
    [JsonPropertyName("starting_chain_end")] public string StartingChainEnd { get; set; } = "region_04";
    [JsonPropertyName("lore_chance")] public double LoreChance { get; set; } = 0.01;
    [JsonPropertyName("biomes")] public List<BiomeDef> Biomes { get; set; } = new();

    public BiomeDef Biome(string id) =>
        Biomes.FirstOrDefault(b => b.Id == id) ?? throw new ArgumentException($"unknown biome '{id}'");

    /// <summary>Biomes offered at the Crossroads (not hidden).</summary>
    public IEnumerable<BiomeDef> OpenBiomes => Biomes.Where(b => !b.Hidden);

    public static AtlasDef FromJson(string json)
    {
        var atlas = JsonSerializer.Deserialize<AtlasDef>(json, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
            ?? throw new InvalidOperationException("atlas json is empty");
        atlas.Validate();
        return atlas;
    }

    /// <summary>Throws with a readable message if the atlas cannot generate a world.</summary>
    public void Validate()
    {
        if (Biomes.Count == 0) throw new InvalidOperationException("atlas has no biomes");
        var ids = new HashSet<string>();
        foreach (var b in Biomes)
        {
            if (string.IsNullOrWhiteSpace(b.Id) || b.Id.Contains(':') || b.Id.Contains('.'))
                throw new InvalidOperationException($"biome id '{b.Id}' must be non-empty and contain no ':' or '.'");
            if (!ids.Add(b.Id)) throw new InvalidOperationException($"duplicate biome id '{b.Id}'");
            if (b.PagesMin < 1 || b.PagesMax < b.PagesMin) throw new InvalidOperationException($"{b.Id}: bad page range");
            if (b.BlipsMin < 8 || b.BlipsMax < b.BlipsMin) throw new InvalidOperationException($"{b.Id}: blips per page must be >= 8");
            foreach (var bank in new[] { b.PlaceAdjectives, b.PlaceNouns, b.Foes })
                if (bank.Count == 0) throw new InvalidOperationException($"{b.Id}: empty name bank");
        }
        foreach (var b in Biomes)
            foreach (var g in b.Gateways)
                if (!ids.Contains(g.To)) throw new InvalidOperationException($"{b.Id}: gateway to unknown biome '{g.To}'");
        if (!OpenBiomes.Any()) throw new InvalidOperationException("atlas has no open biomes");
    }
}

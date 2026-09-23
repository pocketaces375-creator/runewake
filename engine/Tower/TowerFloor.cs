using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Runewake.Engine.Cards;

namespace Runewake.Engine.Tower;

// ═══════════════════════════════════════════════════════════════════════════
// FABLE-020 — the Tower.
//
// 100 floors, every one AUTHORED (never generated): a curated, themed area
// with many places to go, laid out like a map page. Each floor ends in a
// massive RAID boss for teams of up to five (engine/Coop, WinRule.SharedPool).
// A floor's boss must be beaten by `unlock_threshold` DIFFERENT players before
// the next floor opens — for everyone (supabase record_tower_boss_clear).
//
// Floor definitions live in content/tower/floor_NNN.json AND in Supabase
// (tower_floors.definition) so a floor can be retuned without an app update;
// the file is the source, tools/tower_seed.py turns it into SQL.
// ═══════════════════════════════════════════════════════════════════════════

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TowerPlaceKind { Duel, Elite, Keeper, Event, Lore, Boss }

public sealed class TowerEventDef
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "Shrine";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    /// <summary>Reward strings, same grammar as map node rewards ("shard:40", "dig_charge:1").</summary>
    [JsonPropertyName("rewards")] public List<string> Rewards { get; set; } = new();
}

public sealed class TowerPlaceDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("kind")] public TowerPlaceKind Kind { get; set; }
    [JsonPropertyName("wing")] public string? Wing { get; set; }
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("entry")] public bool Entry { get; set; }
    [JsonPropertyName("next")] public List<string> Next { get; set; } = new();
    /// <summary>All of these must be cleared before this place opens (the boss needs every Keeper).</summary>
    [JsonPropertyName("requires")] public List<string> Requires { get; set; } = new();
    [JsonPropertyName("encounter")] public EncounterDef? Encounter { get; set; }
    [JsonPropertyName("event")] public TowerEventDef? Event { get; set; }
    [JsonPropertyName("lore")] public string? Lore { get; set; }
}

public sealed class TowerRaidDef
{
    [JsonPropertyName("max_players")] public int MaxPlayers { get; set; } = 5;
    /// <summary>The boss's one health pool, shared by every raider's board.</summary>
    [JsonPropertyName("shared_pool")] public int SharedPool { get; set; } = 300;
    [JsonPropertyName("max_rounds")] public int MaxRounds { get; set; } = 30;
    [JsonPropertyName("class")] public string? BossClass { get; set; }
    /// <summary>
    /// FABLE-021: the pool for 1, 2, 3… raiders. A solo or duo raid still has a chance,
    /// it is just harder per player. Falls back to SharedPool when absent or short.
    /// </summary>
    [JsonPropertyName("shared_pool_by_players")] public List<int>? SharedPoolByPlayers { get; set; }

    public int PoolFor(int players)
    {
        if (SharedPoolByPlayers is { Count: > 0 } l && players >= 1)
            return l[Math.Min(players, l.Count) - 1];
        return SharedPool;
    }
}

public sealed class TowerWingDef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("blurb")] public string Blurb { get; set; } = "";
}

public sealed class TowerFloorDef
{
    [JsonPropertyName("floor")] public int Floor { get; set; }
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("theme")] public string Theme { get; set; } = "";
    [JsonPropertyName("strata")] public string Strata { get; set; } = "VERDANT";
    [JsonPropertyName("strata2")] public string? Strata2 { get; set; }
    [JsonPropertyName("board_skin")] public string? BoardSkin { get; set; }
    [JsonPropertyName("intro")] public List<string> Intro { get; set; } = new();
    [JsonPropertyName("unlock_threshold")] public int UnlockThreshold { get; set; } = 100;
    [JsonPropertyName("wings")] public List<TowerWingDef> Wings { get; set; } = new();
    [JsonPropertyName("places")] public List<TowerPlaceDef> Places { get; set; } = new();
    [JsonPropertyName("raid")] public TowerRaidDef Raid { get; set; } = new();

    public TowerPlaceDef Place(string id) => Places.First(p => p.Id == id);
    public TowerPlaceDef Boss => Places.Single(p => p.Kind == TowerPlaceKind.Boss);

    private static readonly JsonSerializerOptions Opts = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static TowerFloorDef FromJson(string json) =>
        JsonSerializer.Deserialize<TowerFloorDef>(json, Opts) ?? throw new InvalidOperationException("empty floor json");

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

    /// <summary>Every problem with this floor, in words. Empty = shippable.</summary>
    public List<string> Validate(Func<string, bool>? cardExists = null)
    {
        var errs = new List<string>();
        if (Floor is < 1 or > 100) errs.Add($"floor {Floor} is outside 1–100");
        var ids = new HashSet<string>();
        foreach (var p in Places)
        {
            if (string.IsNullOrWhiteSpace(p.Id)) errs.Add("a place has no id");
            else if (!ids.Add(p.Id)) errs.Add($"duplicate place id {p.Id}");
        }
        foreach (var p in Places)
        {
            foreach (var n in p.Next.Concat(p.Requires))
                if (!ids.Contains(n)) errs.Add($"{p.Id} points at unknown place {n}");
            bool fight = p.Kind is TowerPlaceKind.Duel or TowerPlaceKind.Elite or TowerPlaceKind.Keeper or TowerPlaceKind.Boss;
            if (fight)
            {
                if (p.Encounter == null) { errs.Add($"{p.Id} is a fight with no encounter"); continue; }
                if (p.Encounter.Deck.Count != 30) errs.Add($"{p.Id}: deck has {p.Encounter.Deck.Count} cards, needs 30");
                foreach (var r in p.Encounter.BossRules ?? new List<string>())
                    if (!Runewake.Engine.Engine.BossRules.IsValid(r)) errs.Add($"{p.Id}: unknown or malformed boss rule '{r}'");
                if (cardExists != null)
                    foreach (var c in p.Encounter.Deck.Where(c => !cardExists(c)).Distinct())
                        errs.Add($"{p.Id}: unknown card {c}");
            }
            if (p.Kind == TowerPlaceKind.Event && p.Event == null) errs.Add($"{p.Id} is an event with no event");
            if (p.Kind == TowerPlaceKind.Lore && string.IsNullOrWhiteSpace(p.Lore)) errs.Add($"{p.Id} is lore with no text");
        }
        if (Places.Count(p => p.Kind == TowerPlaceKind.Boss) != 1) errs.Add("a floor needs exactly one Boss");
        if (!Places.Any(p => p.Entry)) errs.Add("a floor needs at least one entry");
        if (errs.Count > 0) return errs;

        // Every place reachable from an entry (roads + requires both honoured).
        var cleared = new HashSet<string>();
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var p in Places)
                if (!cleared.Contains(p.Id) && TowerProgress.IsOpen(this, p, cleared)) { cleared.Add(p.Id); grew = true; }
        }
        foreach (var p in Places.Where(p => !cleared.Contains(p.Id)))
            errs.Add($"{p.Id} can never be reached");
        if (Raid.SharedPool <= 0) errs.Add("the raid needs a shared pool");
        if (Raid.MaxPlayers is < 1 or > 5) errs.Add("raids are 1–5 players");
        return errs;
    }
}

/// <summary>Which Tower places a player can go to. Pure.</summary>
public static class TowerProgress
{
    public static bool IsOpen(TowerFloorDef floor, TowerPlaceDef place, ISet<string> cleared)
    {
        if (!place.Requires.All(cleared.Contains)) return false;
        if (place.Entry) return true;
        return floor.Places.Any(p => cleared.Contains(p.Id) && p.Next.Contains(place.Id));
    }

    public static IEnumerable<TowerPlaceDef> Open(TowerFloorDef floor, ISet<string> cleared) =>
        floor.Places.Where(p => !cleared.Contains(p.Id) && IsOpen(floor, p, cleared));
}

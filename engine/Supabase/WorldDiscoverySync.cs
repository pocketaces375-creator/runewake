using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Runewake.Engine.Supabase;

/// <summary>
/// FABLE-020: the shared world's discoveries (supabase/world_tower_coop.sql §1).
///
///   Discover(id)        call when the player ENTERS a place. Returns First=true
///                       only if nobody, ever, had found it before — the one and
///                       only time the game says "you discovered this".
///   PageDiscoveries(k)  ids on one page that anyone has found: drives dimmed
///                       dots (someone found it) vs. roads into nothing.
///   Clear(id)           call when the player beats / completes a place.
///
/// All three fail soft: offline, the map treats every place as uncharted and
/// the save still records the player's own progress.
/// </summary>
public sealed class WorldDiscoverySync
{
    private readonly SupabaseRpc _rpc;
    public WorldDiscoverySync(SupabaseConfig config, HttpClient http) { _rpc = new SupabaseRpc(config, http); }

    public readonly record struct DiscoverResult(bool Ok, bool First, string Error);

    public async Task<DiscoverResult> Discover(SupabaseSession session, string blipId)
    {
        var r = await _rpc.Call(session, "discover_blip", new { p_blip_id = blipId }).ConfigureAwait(false);
        if (!r.Ok) return new DiscoverResult(false, false, r.Error);
        try
        {
            using var doc = JsonDocument.Parse(r.Body);
            var e = SupabaseRpc.SingleObject(doc.RootElement);
            if (e is null) return new DiscoverResult(false, false, "Unexpected reply shape");
            bool first = e.Value.TryGetProperty("first", out var f) && f.ValueKind == JsonValueKind.True;
            return new DiscoverResult(true, first, "");
        }
        catch (JsonException) { return new DiscoverResult(false, false, "Unreadable reply"); }
    }

    public async Task<(bool ok, HashSet<string> ids, string error)> PageDiscoveries(SupabaseSession session, string pageKey)
    {
        var r = await _rpc.Call(session, "page_discoveries", new { p_page_key = pageKey }).ConfigureAwait(false);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (!r.Ok) return (false, ids, r.Error);
        try
        {
            using var doc = JsonDocument.Parse(r.Body);
            foreach (var e in doc.RootElement.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String) ids.Add(e.GetString()!);
            return (true, ids, "");
        }
        catch (JsonException) { return (false, ids, "Unreadable reply"); }
    }

    public async Task<bool> Clear(SupabaseSession session, string blipId)
    {
        var r = await _rpc.Call(session, "clear_blip", new { p_blip_id = blipId }).ConfigureAwait(false);
        return r.Ok;
    }
}

/// <summary>FABLE-020: the Tower's server side (supabase/world_tower_coop.sql §2).</summary>
public sealed class TowerSync
{
    private readonly SupabaseRpc _rpc;
    public TowerSync(SupabaseConfig config, HttpClient http) { _rpc = new SupabaseRpc(config, http); }

    public sealed class FloorStatus
    {
        public int Floor { get; init; }
        public string Title { get; init; } = "";
        public bool IsOpen { get; init; }
        public long UniqueClears { get; init; }
        public int UnlockThreshold { get; init; }
    }

    public async Task<(bool ok, List<FloorStatus> floors, string error)> Status(SupabaseSession session)
    {
        var r = await _rpc.Call(session, "tower_status", new { }).ConfigureAwait(false);
        var list = new List<FloorStatus>();
        if (!r.Ok) return (false, list, r.Error);
        try
        {
            using var doc = JsonDocument.Parse(r.Body);
            foreach (var e in doc.RootElement.EnumerateArray())
                list.Add(new FloorStatus
                {
                    Floor = e.GetProperty("floor").GetInt32(),
                    Title = e.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    IsOpen = e.GetProperty("is_open").GetBoolean(),
                    UniqueClears = e.GetProperty("unique_clears").GetInt64(),
                    UnlockThreshold = e.GetProperty("unlock_threshold").GetInt32(),
                });
            return (true, list, "");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return (false, list, "Unreadable reply");
        }
    }

    /// <summary>The definition JSON of an OPEN floor (RLS hides locked floors), or null.</summary>
    public async Task<(bool ok, string? definitionJson, int version, string error)> FloorDefinition(SupabaseSession session, int floor)
    {
        var r = await _rpc.Get(session, $"/rest/v1/tower_floors?floor=eq.{floor}&select=definition,version").ConfigureAwait(false);
        if (!r.Ok) return (false, null, 0, r.Error);
        try
        {
            using var doc = JsonDocument.Parse(r.Body);
            if (doc.RootElement.GetArrayLength() == 0) return (true, null, 0, "");
            var row = doc.RootElement[0];
            return (true, row.GetProperty("definition").GetRawText(), row.GetProperty("version").GetInt32(), "");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return (false, null, 0, "Unreadable reply");
        }
    }

    public readonly record struct BossClearResult(bool Ok, long UniqueClears, int Threshold, bool NextFloorOpened, bool FirstEver, string Error);

    public async Task<BossClearResult> RecordBossClear(SupabaseSession session, int floor, string? raidId)
    {
        var r = await _rpc.Call(session, "record_tower_boss_clear", new { p_floor = floor, p_raid_id = raidId }).ConfigureAwait(false);
        if (!r.Ok) return new BossClearResult(false, 0, 0, false, false, r.Error);
        try
        {
            using var doc = JsonDocument.Parse(r.Body);
            var o = SupabaseRpc.SingleObject(doc.RootElement);
            if (o is null) return new BossClearResult(false, 0, 0, false, false, "Unexpected reply shape");
            var e = o.Value;
            return new BossClearResult(true, e.GetProperty("unique_clears").GetInt64(), e.GetProperty("threshold").GetInt32(),
                e.GetProperty("next_floor_opened").GetBoolean(), e.GetProperty("first_ever").GetBoolean(), "");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new BossClearResult(false, 0, 0, false, false, "Unreadable reply");
        }
    }
}

/// <summary>FABLE-020: co-op / raid / PvP lobbies (supabase/world_tower_coop.sql §3).</summary>
public sealed class ExpeditionSync
{
    private readonly SupabaseRpc _rpc;
    public ExpeditionSync(SupabaseConfig config, HttpClient http) { _rpc = new SupabaseRpc(config, http); }

    public async Task<(bool ok, string? id, string error)> Create(SupabaseSession s, string kind, string target, int maxPlayers,
                                                                  string displayName, string classId, IReadOnlyList<string> deck)
    {
        var r = await _rpc.Call(s, "create_expedition", new { p_kind = kind, p_target = target, p_max = maxPlayers, p_display_name = displayName, p_class = classId, p_deck = deck }).ConfigureAwait(false);
        return r.Ok ? (true, JsonSerializer.Deserialize<string>(r.Body), "") : (false, null, r.Error);
    }

    public async Task<(bool ok, int seat, string error)> Join(SupabaseSession s, string id, string displayName, string classId, IReadOnlyList<string> deck)
    {
        var r = await _rpc.Call(s, "join_expedition", new { p_id = id, p_display_name = displayName, p_class = classId, p_deck = deck }).ConfigureAwait(false);
        return r.Ok && int.TryParse(r.Body.Trim(), out int seat) ? (true, seat, "") : (false, -1, r.Ok ? "Unreadable reply" : r.Error);
    }

    public async Task<(bool ok, long seed, string error)> Start(SupabaseSession s, string id)
    {
        var r = await _rpc.Call(s, "start_expedition", new { p_id = id }).ConfigureAwait(false);
        return r.Ok && long.TryParse(r.Body.Trim(), out long seed) ? (true, seed, "") : (false, 0, r.Ok ? "Unreadable reply" : r.Error);
    }

    public async Task<bool> Finish(SupabaseSession s, string id, object result)
    {
        var r = await _rpc.Call(s, "finish_expedition", new { p_id = id, p_result = result }).ConfigureAwait(false);
        return r.Ok;
    }

    public sealed class Member
    {
        public int Seat { get; init; }
        public string UserId { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string ClassId { get; init; } = "";
        public List<string> Deck { get; init; } = new();
        public string Status { get; init; } = "";
    }

    public async Task<(bool ok, List<Member> members, string error)> Members(SupabaseSession s, string id)
    {
        var r = await _rpc.Get(s, $"/rest/v1/expedition_members?expedition_id=eq.{id}&select=seat,user_id,display_name,class_id,deck,status&order=seat").ConfigureAwait(false);
        var list = new List<Member>();
        if (!r.Ok) return (false, list, r.Error);
        try
        {
            using var doc = JsonDocument.Parse(r.Body);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var deck = new List<string>();
                if (e.TryGetProperty("deck", out var d) && d.ValueKind == JsonValueKind.Array)
                    foreach (var c in d.EnumerateArray()) if (c.ValueKind == JsonValueKind.String) deck.Add(c.GetString()!);
                list.Add(new Member
                {
                    Seat = e.GetProperty("seat").GetInt32(),
                    UserId = e.GetProperty("user_id").GetString() ?? "",
                    DisplayName = e.TryGetProperty("display_name", out var n) ? n.GetString() ?? "" : "",
                    ClassId = e.TryGetProperty("class_id", out var c2) ? c2.GetString() ?? "" : "",
                    Deck = deck,
                    Status = e.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "",
                });
            }
            return (true, list, "");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return (false, list, "Unreadable reply");
        }
    }
}

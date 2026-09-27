using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Runewake.Engine.Supabase;

/// <summary>
/// FABLE-038: the online-play lobby and move log (supabase/online_play.sql), over plain
/// HTTPS. Every call is one PostgREST RPC. Used by the phone (through Godot's HTTP) and by
/// the seat bot (tools/pvp-bot, plain .NET) alike — the bot is simply another client.
/// </summary>
public sealed class OnlinePlaySync
{
    private readonly SupabaseRpc _rpc;
    public OnlinePlaySync(SupabaseConfig config, HttpClient http) { _rpc = new SupabaseRpc(config, http); }

    public sealed class Found
    {
        public string Id { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Target { get; init; } = "";
        public string State { get; init; } = "";
        public int MaxPlayers { get; init; }
        public int Players { get; init; }
        public string HostName { get; init; } = "";
    }

    public sealed class Member
    {
        public int Seat { get; init; }
        public string UserId { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string ClassId { get; init; } = "";
        public List<string> Deck { get; init; } = new();
        public string Status { get; init; } = "";
        public int SecondsAgo { get; init; }
        public bool Me { get; init; }
    }

    public sealed class Lobby
    {
        public string Id { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Target { get; init; } = "";
        public string State { get; init; } = "";
        public long? Seed { get; init; }
        public string Code { get; init; } = "";
        public string Host { get; init; } = "";
        public int MaxPlayers { get; init; }
        public List<Member> Members { get; init; } = new();
        public JsonElement? Result { get; init; }
        public Member? Mine => Members.Find(m => m.Me);
    }

    public sealed class MoveRow
    {
        public long Id { get; init; }
        public int Seat { get; init; }
        public int Seq { get; init; }
        public string Payload { get; init; } = "";
    }

    public async Task<(bool ok, string id, string code, string error)> Create(SupabaseSession s, string kind, string target, int maxPlayers,
                                                                           string displayName, string classId, IReadOnlyList<string> deck)
    {
        var r = await _rpc.Call(s, "create_expedition_v2", new { p_kind = kind, p_target = target, p_max = maxPlayers, p_display_name = displayName, p_class = classId, p_deck = deck }).ConfigureAwait(false);
        if (!r.Ok) return (false, "", "", r.Error);
        try
        {
            using var doc = JsonDocument.Parse(r.Body);
            var o = SupabaseRpc.SingleObject(doc.RootElement) ?? throw new JsonException();
            return (true, o.GetProperty("id").GetString() ?? "", o.GetProperty("code").GetString() ?? "", "");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException) { return (false, "", "", "Unreadable reply"); }
    }

    public async Task<(bool ok, Found? found, string error)> Find(SupabaseSession s, string code)
    {
        var r = await _rpc.Call(s, "find_expedition", new { p_code = code }).ConfigureAwait(false);
        if (!r.Ok) return (false, null, r.Error);
        try
        {
            using var doc = JsonDocument.Parse(r.Body);
            var o = SupabaseRpc.SingleObject(doc.RootElement);
            if (o == null) return (true, null, "");
            var e = o.Value;
            return (true, new Found
            {
                Id = e.GetProperty("id").GetString() ?? "", Kind = Str(e, "kind"), Target = Str(e, "target"), State = Str(e, "state"),
                MaxPlayers = Int(e, "max_players"), Players = Int(e, "players"), HostName = Str(e, "host_name"),
            }, "");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException) { return (false, null, "Unreadable reply"); }
    }

    public async Task<(bool ok, int seat, string error)> Join(SupabaseSession s, string id, string displayName, string classId, IReadOnlyList<string> deck)
    {
        var r = await _rpc.Call(s, "join_expedition", new { p_id = id, p_display_name = displayName, p_class = classId, p_deck = deck }).ConfigureAwait(false);
        return r.Ok && int.TryParse(r.Body.Trim(), out int seat) ? (true, seat, "") : (false, -1, r.Ok ? "Unreadable reply" : r.Error);
    }

    public async Task<(bool ok, string error)> SetReady(SupabaseSession s, string id, bool ready)
    {
        var r = await _rpc.Call(s, "set_ready", new { p_id = id, p_ready = ready }).ConfigureAwait(false);
        return (r.Ok, r.Error);
    }

    public async Task<(bool ok, long seed, string error)> Start(SupabaseSession s, string id)
    {
        var r = await _rpc.Call(s, "start_expedition", new { p_id = id }).ConfigureAwait(false);
        return r.Ok && long.TryParse(r.Body.Trim(), out long seed) ? (true, seed, "") : (false, 0, r.Ok ? "Unreadable reply" : r.Error);
    }

    public async Task<(bool ok, string error)> Leave(SupabaseSession s, string id)
    {
        var r = await _rpc.Call(s, "leave_expedition", new { p_id = id }).ConfigureAwait(false);
        return (r.Ok, r.Error);
    }

    public async Task<bool> Finish(SupabaseSession s, string id, object result)
    {
        var r = await _rpc.Call(s, "finish_expedition", new { p_id = id, p_result = result }).ConfigureAwait(false);
        return r.Ok;
    }

    public async Task<(bool ok, Lobby? lobby, string error)> GetLobby(SupabaseSession s, string id)
    {
        var r = await _rpc.Call(s, "lobby", new { p_id = id }).ConfigureAwait(false);
        if (!r.Ok) return (false, null, r.Error);
        try
        {
            using var doc = JsonDocument.Parse(r.Body);
            var o = SupabaseRpc.SingleObject(doc.RootElement) ?? throw new JsonException();
            var members = new List<Member>();
            if (o.TryGetProperty("members", out var ms) && ms.ValueKind == JsonValueKind.Array)
                foreach (var m in ms.EnumerateArray())
                {
                    var deck = new List<string>();
                    if (m.TryGetProperty("deck", out var d) && d.ValueKind == JsonValueKind.Array)
                        foreach (var c in d.EnumerateArray()) if (c.ValueKind == JsonValueKind.String) deck.Add(c.GetString()!);
                    members.Add(new Member
                    {
                        Seat = Int(m, "seat"), UserId = Str(m, "user_id"), DisplayName = Str(m, "display_name"), ClassId = Str(m, "class_id"),
                        Deck = deck, Status = Str(m, "status"), SecondsAgo = Int(m, "seconds_ago"),
                        Me = m.TryGetProperty("me", out var me) && me.ValueKind == JsonValueKind.True,
                    });
                }
            long? seed = o.TryGetProperty("seed", out var sd) && sd.ValueKind == JsonValueKind.Number ? sd.GetInt64() : null;
            return (true, new Lobby
            {
                Id = Str(o, "id"), Kind = Str(o, "kind"), Target = Str(o, "target"), State = Str(o, "state"), Seed = seed,
                Code = Str(o, "code"), Host = Str(o, "host"), MaxPlayers = Int(o, "max_players"), Members = members,
                Result = o.TryGetProperty("result", out var res) && res.ValueKind == JsonValueKind.Object ? res.Clone() : null,
            }, "");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return (false, null, "Unreadable reply"); }
    }

    /// <summary>Post one NetMessage (already JSON). The server assigns the seat from the membership.</summary>
    public async Task<(bool ok, long id, string error)> PostMove(SupabaseSession s, string id, int seq, string payloadJson)
    {
        var r = await _rpc.Call(s, "post_move", new { p_id = id, p_seq = seq, p_payload = JsonDocument.Parse(payloadJson).RootElement }).ConfigureAwait(false);
        return r.Ok && long.TryParse(r.Body.Trim(), out long rowId) ? (true, rowId, "") : (false, 0, r.Ok ? "Unreadable reply" : r.Error);
    }

    public async Task<(bool ok, List<MoveRow> rows, string error)> MovesSince(SupabaseSession s, string id, long after)
    {
        var list = new List<MoveRow>();
        var r = await _rpc.Call(s, "moves_since", new { p_id = id, p_after = after }).ConfigureAwait(false);
        if (!r.Ok) return (false, list, r.Error);
        try
        {
            using var doc = JsonDocument.Parse(r.Body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return (true, list, "");
            foreach (var e in root.EnumerateArray())
                list.Add(new MoveRow
                {
                    Id = e.GetProperty("id").GetInt64(), Seat = Int(e, "seat"), Seq = Int(e, "seq"),
                    Payload = e.GetProperty("payload").GetRawText(),
                });
            return (true, list, "");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException) { return (false, list, "Unreadable reply"); }
    }

    private static string Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static int Int(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
}

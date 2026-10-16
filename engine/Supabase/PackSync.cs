using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Runewake.Engine.State;

namespace Runewake.Engine.Supabase;

/// <summary>
/// FABLE-061: the phone's side of paid packs (supabase/packs.sql + supabase/functions/verify-pack-purchase).
///
///   Status()            the shop's numbers: cards in packs per rarity, can packs be sold, next card's date
///   Discover(ids, src)  report cards found on the map ("map") or in the Tower ("tower") — first find counts
///   Verify(product, t)  hand a Google Play purchase token to the server; get the rolled packs back
///   Owed()              packs paid for that never reached this phone (crash, no signal, reinstall)
///   MarkReceived(ids)   this phone has stored them in its save
///
/// Every call fails soft (Ok = false + a sentence for the player); nothing here throws.
/// </summary>
public sealed class PackSync
{
    private readonly SupabaseConfig _config;
    private readonly HttpClient _http;
    private readonly SupabaseRpc _rpc;

    public PackSync(SupabaseConfig config, HttpClient http)
    {
        _config = config;
        _http = http;
        _rpc = new SupabaseRpc(config, http);
    }

    public bool IsConfigured => _config.IsConfigured;

    public sealed class PackStatus
    {
        public bool Ok { get; init; }
        public string Error { get; init; } = "";
        public int Commons { get; init; }
        public int Uncommons { get; init; }
        public int Rares { get; init; }
        public int Relics { get; init; }
        public bool CanSell { get; init; }
        public int Waiting { get; init; }
        public DateTimeOffset? NextCardAt { get; init; }
        public double RelicChance { get; init; } = Packs.RelicChance;
        public int Total => Commons + Uncommons + Rares + Relics;
    }

    public sealed class VerifyResult
    {
        public bool Ok { get; init; }
        public bool Pending { get; init; }
        public bool Already { get; init; }
        public bool Consumed { get; init; }
        public bool Retry { get; init; }
        public int Status { get; init; }
        public string Error { get; init; } = "";
        public List<PendingPack> Packs { get; init; } = new();
    }

    public async Task<PackStatus> Status(SupabaseSession session)
    {
        var r = await _rpc.Call(session, "pack_status", new { }).ConfigureAwait(false);
        if (!r.Ok) return new PackStatus { Ok = false, Error = r.Error };
        return ParseStatus(r.Body);
    }

    public static PackStatus ParseStatus(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var e = SupabaseRpc.SingleObject(doc.RootElement);
            if (e is null) return new PackStatus { Ok = false, Error = "Unexpected reply shape" };
            var o = e.Value;
            int I(string k) => o.TryGetProperty(k, out var v) && v.TryGetInt32(out var n) ? n : 0;
            DateTimeOffset? next = o.TryGetProperty("next_card_at", out var na) && na.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(na.GetString(), out var d) ? d : null;
            double chance = o.TryGetProperty("relic_chance", out var rc) && rc.TryGetDouble(out var c) ? c : Packs.RelicChance;
            return new PackStatus
            {
                Ok = true, Commons = I("commons"), Uncommons = I("uncommons"), Rares = I("rares"), Relics = I("relics"),
                CanSell = o.TryGetProperty("can_sell", out var cs) && cs.ValueKind == JsonValueKind.True,
                Waiting = I("waiting"), NextCardAt = next, RelicChance = chance,
            };
        }
        catch (JsonException) { return new PackStatus { Ok = false, Error = "Unreadable reply" }; }
    }

    public async Task<bool> Discover(SupabaseSession session, IReadOnlyCollection<string> cardIds, string source)
    {
        if (cardIds.Count == 0) return true;
        var r = await _rpc.Call(session, "discover_cards", new { p_card_ids = cardIds, p_source = source }).ConfigureAwait(false);
        return r.Ok;
    }

    public async Task<(bool ok, List<PendingPack> packs, string error)> Owed(SupabaseSession session)
    {
        var r = await _rpc.Call(session, "my_pending_packs", new { }).ConfigureAwait(false);
        if (!r.Ok) return (false, new(), r.Error);
        try
        {
            using var doc = JsonDocument.Parse(r.Body);
            return (true, ParseGrants(doc.RootElement), "");
        }
        catch (JsonException) { return (false, new(), "Unreadable reply"); }
    }

    public async Task<bool> MarkReceived(SupabaseSession session, IReadOnlyCollection<string> packIds)
    {
        if (packIds.Count == 0) return true;
        var r = await _rpc.Call(session, "mark_packs_applied", new { p_ids = packIds }).ConfigureAwait(false);
        return r.Ok;
    }

    /// <summary>POST the purchase to the edge function. Its reply decides everything.</summary>
    public async Task<VerifyResult> Verify(SupabaseSession session, string productId, string purchaseToken)
    {
        if (!_config.IsConfigured) return new VerifyResult { Error = "Accounts are not set up in this build" };
        if (!session.IsValid) return new VerifyResult { Error = "Sign in to buy packs" };
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, _config.Url.TrimEnd('/') + "/functions/v1/verify-pack-purchase");
            req.Headers.Add("apikey", _config.AnonKey);
            req.Headers.Add("Authorization", "Bearer " + session.AccessToken);
            req.Content = new StringContent(JsonSerializer.Serialize(new { product_id = productId, purchase_token = purchaseToken }),
                Encoding.UTF8, "application/json");
            var resp = await _http.SendAsync(req).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return ParseVerify((int)resp.StatusCode, text);
        }
        catch (Exception ex)
        {
            var inner = ex; while (inner.InnerException != null) inner = inner.InnerException;
            return new VerifyResult { Retry = true, Error = "No connection — your purchase is safe and will be delivered when you're back online." };
        }
    }

    public static VerifyResult ParseVerify(int status, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var o = doc.RootElement;
            bool B(string k) => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
            string err = o.ValueKind == JsonValueKind.Object && o.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString()! : "";
            var packs = o.ValueKind == JsonValueKind.Object && o.TryGetProperty("grants", out var g) ? ParseGrants(g) : new List<PendingPack>();
            bool ok = status == 200 && B("ok");
            return new VerifyResult
            {
                Ok = ok, Status = status, Pending = B("pending"), Already = B("already"), Consumed = B("consumed"),
                Retry = B("retry") || status == 0 || status >= 500 && status != 503,
                Error = ok ? "" : (err.Length > 0 ? err : $"The store said {status}"), Packs = packs,
            };
        }
        catch (JsonException)
        {
            return new VerifyResult { Status = status, Retry = true, Error = $"The store's reply was unreadable ({status})" };
        }
    }

    public static List<PendingPack> ParseGrants(JsonElement arr)
    {
        var list = new List<PendingPack>();
        if (arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var g in arr.EnumerateArray())
        {
            if (g.ValueKind != JsonValueKind.Object || !g.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String) continue;
            var p = new PendingPack { Id = id.GetString()!, Source = "play" };
            if (g.TryGetProperty("cards", out var cs) && cs.ValueKind == JsonValueKind.Array)
                foreach (var c in cs.EnumerateArray()) if (c.ValueKind == JsonValueKind.String) p.Cards.Add(c.GetString()!);
            if (g.TryGetProperty("rarities", out var rs) && rs.ValueKind == JsonValueKind.Array)
                foreach (var r in rs.EnumerateArray()) if (r.ValueKind == JsonValueKind.String) p.Rarities.Add(r.GetString()!);
            if (p.Cards.Count > 0) list.Add(p);
        }
        return list;
    }
}

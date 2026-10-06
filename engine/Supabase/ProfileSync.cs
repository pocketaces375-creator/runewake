using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Runewake.Engine.Supabase;

/// <summary>
/// FABLE-054: usernames and challenges by name, over the functions in supabase/usernames.sql.
/// A server without that migration answers 404 — <see cref="Missing"/> — and the phone keeps working
/// (the name is kept on the phone and offered again later).
/// </summary>
public sealed class ProfileSync
{
    private readonly SupabaseRpc _rpc;
    public ProfileSync(SupabaseConfig config, HttpClient http) { _rpc = new SupabaseRpc(config, http); }

    public enum Claim { Ok, Taken, Invalid, Missing, Failed }

    public sealed class Challenge
    {
        public string Id { get; init; } = "";
        public string FromName { get; init; } = "";
        public string Code { get; init; } = "";
    }

    /// <summary>A function the server doesn't have yet (the migration hasn't been run).</summary>
    private static bool IsMissing(SupabaseRpc.Result r) => r.Status == 404 || r.Body.Contains("PGRST202") || r.Body.Contains("Could not find the function");

    private static string? Scalar(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.String => doc.RootElement.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            };
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Take this username (it's checked here first: shape and the word filter).</summary>
    public async Task<(Claim result, string error)> ClaimUsername(SupabaseSession s, string name)
    {
        name = (name ?? "").Trim();
        if (Usernames.Problem(name) is string why) return (Claim.Invalid, why);
        var r = await _rpc.Call(s, "claim_username", new { p_name = name }).ConfigureAwait(false);
        if (!r.Ok) return IsMissing(r) ? (Claim.Missing, "Usernames aren't switched on for this game's server yet.") : (Claim.Failed, r.Error);
        return Scalar(r.Body) switch
        {
            "ok" => (Claim.Ok, ""),
            "taken" => (Claim.Taken, "Someone already has that name."),
            "invalid" => (Claim.Invalid, "Letters, numbers, spaces, _ and - only (3–16)."),
            _ => (Claim.Failed, "Unreadable reply"),
        };
    }

    /// <summary>This account's username, or null (none yet, or the server can't say).</summary>
    public async Task<string?> MyUsername(SupabaseSession s)
    {
        var r = await _rpc.Call(s, "my_username", new { }).ConfigureAwait(false);
        return r.Ok ? Scalar(r.Body) : null;
    }

    /// <summary>
    /// Invite a player into this open lobby by their username.
    /// Returns null on success, else what to tell the player.
    /// </summary>
    public async Task<string?> ChallengePlayer(SupabaseSession s, string username, string expeditionId)
    {
        var r = await _rpc.Call(s, "challenge_player", new { p_name = (username ?? "").Trim(), p_expedition = expeditionId }).ConfigureAwait(false);
        if (!r.Ok) return IsMissing(r) ? "Challenges aren't switched on for this game's server yet." : r.Error;
        return Scalar(r.Body) switch
        {
            "ok" => null,
            "no_such_player" => "No one has that username.",
            "self" => "That's you!",
            "no_lobby" => "Host a duel first, then challenge from its lobby.",
            "no_username" => "Pick a username first (Account) so they know who's calling.",
            _ => "Unreadable reply",
        };
    }

    /// <summary>Open challenges to me (newest first). Empty when there are none or the server can't say.</summary>
    public async Task<List<Challenge>> MyChallenges(SupabaseSession s)
    {
        var list = new List<Challenge>();
        var r = await _rpc.Call(s, "my_challenges", new { }).ConfigureAwait(false);
        if (!r.Ok) return list;
        try
        {
            using var doc = JsonDocument.Parse(r.Body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in doc.RootElement.EnumerateArray())
                list.Add(new Challenge
                {
                    Id = e.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "",
                    FromName = e.TryGetProperty("from_name", out var f) ? f.GetString() ?? "" : "",
                    Code = e.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "",
                });
        }
        catch (JsonException) { }
        return list;
    }

    /// <summary>Accept (returns the lobby code to join) or decline (returns null).</summary>
    public async Task<string?> AnswerChallenge(SupabaseSession s, string id, bool accept)
    {
        var r = await _rpc.Call(s, "answer_challenge", new { p_id = id, p_accept = accept }).ConfigureAwait(false);
        return r.Ok ? Scalar(r.Body) : null;
    }
}

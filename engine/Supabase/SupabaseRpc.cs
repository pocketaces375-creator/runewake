using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Runewake.Engine.Supabase;

/// <summary>
/// FABLE-020: calls a Postgres function through PostgREST
/// (<c>POST /rest/v1/rpc/&lt;name&gt;</c>) as the signed-in player, or reads a
/// table. Shared by the world, Tower and expedition clients.
///
/// On the phone the HttpClient passed in MUST come from Http.Create() (Godot
/// TLS) — .NET's own HTTPS aborts the app on Android. Tests pass a mock handler.
/// </summary>
public sealed class SupabaseRpc
{
    private readonly SupabaseConfig _config;
    private readonly HttpClient _http;

    public SupabaseRpc(SupabaseConfig config, HttpClient http)
    {
        _config = config;
        _http = http;
    }

    public bool IsConfigured => _config.IsConfigured;

    public readonly record struct Result(bool Ok, int Status, string Body, string Error);

    public Task<Result> Call(SupabaseSession session, string function, object args) =>
        Send(session, HttpMethod.Post, $"/rest/v1/rpc/{function}", JsonSerializer.Serialize(args));

    public Task<Result> Get(SupabaseSession session, string pathAndQuery) =>
        Send(session, HttpMethod.Get, pathAndQuery, null);

    private async Task<Result> Send(SupabaseSession session, HttpMethod method, string path, string? body)
    {
        if (!_config.IsConfigured) return new Result(false, 0, "", "Not configured");
        if (!session.IsValid) return new Result(false, 0, "", "Not signed in");
        try
        {
            var req = new HttpRequestMessage(method, _config.Url.TrimEnd('/') + path);
            req.Headers.Add("apikey", _config.AnonKey);
            req.Headers.Add("Authorization", "Bearer " + session.AccessToken);
            req.Headers.Add("Accept", "application/json");
            if (body != null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            var resp = await _http.SendAsync(req).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            int status = (int)resp.StatusCode;
            if (status < 200 || status >= 300) return new Result(false, status, text, SupabaseAuth.Describe(text, status));
            return new Result(true, status, text, "");
        }
        catch (Exception ex)
        {
            var inner = ex; while (inner.InnerException != null) inner = inner.InnerException;
            return new Result(false, 0, "", "No connection — " + inner.GetType().Name + ": " + inner.Message);
        }
    }

    /// <summary>A jsonb RPC reply is an object; some proxies/versions wrap it in a one-row array. Null if neither.</summary>
    public static System.Text.Json.JsonElement? SingleObject(System.Text.Json.JsonElement root) => root.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Object => root,
        System.Text.Json.JsonValueKind.Array when root.GetArrayLength() > 0 && root[0].ValueKind == System.Text.Json.JsonValueKind.Object => root[0],
        _ => null,
    };
}

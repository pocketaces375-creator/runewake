using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// FABLE-019b: every web request the game makes goes through THIS, never
/// through .NET's own networking.
///
/// Why: on Android, Godot's .NET runtime is the "linux-bionic" build. Its
/// HTTPS stack loads libSystem.Security.Cryptography.Native.OpenSsl.so, which
/// then looks for a system libssl. Android apps are not allowed to load the
/// system's libssl, the APK does not ship one, so the shim prints
/// "No usable version of libssl was found" and calls abort(). That is a native
/// crash: no try/catch can stop it. It fires on the FIRST https request.
///
/// Before FABLE-019 the APK had no INTERNET permission, so every request died
/// at DNS lookup (a normal, catchable exception) and never reached TLS. Turning
/// the permission on let requests reach TLS, and the game aborted on launch,
/// because SyncManager signs in the moment the title screen loads.
///
/// This handler plugs Godot's own HTTPClient (TLS via mbedTLS, compiled into
/// the engine, with Godot's bundled CA certificates) under System.Net.Http's
/// HttpClient, so the engine code (SupabaseAuth, CloudSaveSync,
/// RelicLedgerSync) is unchanged and its tests keep using mock handlers.
///
/// Godot's HTTPClient is a RefCounted, not a node: it is safe off the main
/// thread. Each request runs, blocking, on a thread-pool thread.
/// tools/network_transport_check.py fails the build if any game code creates
/// an HttpClient that does not go through Http.Create().
/// </summary>
public sealed class GodotHttpHandler : HttpMessageHandler
{
    private static readonly TimeSpan HardCap = TimeSpan.FromSeconds(30);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.Run(() => SendBlocking(request, ct), ct);

    private static HttpResponseMessage SendBlocking(HttpRequestMessage req, CancellationToken ct)
    {
        var uri = req.RequestUri ?? throw new HttpRequestException("no request URI");
        bool tls = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        var deadline = DateTime.UtcNow + HardCap;
        var client = new Godot.HttpClient();
        try
        {
            var err = client.ConnectToHost(uri.Host, uri.Port, tls ? TlsOptions.Client() : null);
            if (err != Error.Ok) throw new HttpRequestException($"connect failed ({err})");

            Pump(client, ct, deadline, Godot.HttpClient.Status.Resolving, Godot.HttpClient.Status.Connecting);
            var st = client.GetStatus();
            if (st != Godot.HttpClient.Status.Connected)
                throw new HttpRequestException(Describe(st, uri.Host));

            // Headers. Godot adds Host, Content-Length and User-Agent itself.
            var headers = new List<string>();
            void Add(IEnumerable<KeyValuePair<string, IEnumerable<string>>> hs)
            {
                foreach (var h in hs)
                {
                    if (h.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                    if (h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                    headers.Add(h.Key + ": " + string.Join(", ", h.Value));
                }
            }
            Add(req.Headers);
            byte[] body = Array.Empty<byte>();
            if (req.Content != null)
            {
                Add(req.Content.Headers);
                body = req.Content.ReadAsByteArrayAsync(ct).GetAwaiter().GetResult();
            }

            err = client.RequestRaw(MapMethod(req.Method), uri.PathAndQuery, headers.ToArray(), body);
            if (err != Error.Ok) throw new HttpRequestException($"request failed ({err})");

            Pump(client, ct, deadline, Godot.HttpClient.Status.Requesting);
            st = client.GetStatus();
            if (st != Godot.HttpClient.Status.Body && st != Godot.HttpClient.Status.Connected)
                throw new HttpRequestException(Describe(st, uri.Host));
            if (!client.HasResponse()) throw new HttpRequestException("no response");

            int code = client.GetResponseCode();
            var respHeaders = client.GetResponseHeaders();

            using var buf = new MemoryStream();
            while (client.GetStatus() == Godot.HttpClient.Status.Body)
            {
                Check(ct, deadline);
                client.Poll();
                var chunk = client.ReadResponseBodyChunk();
                if (chunk.Length == 0) Thread.Sleep(5);
                else buf.Write(chunk, 0, chunk.Length);
            }

            var resp = new HttpResponseMessage((HttpStatusCode)code)
            {
                RequestMessage = req,
                Content = new ByteArrayContent(buf.ToArray()),
            };
            foreach (var line in respHeaders)
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var name = line.Substring(0, colon).Trim();
                var value = line.Substring(colon + 1).Trim();
                if (!resp.Headers.TryAddWithoutValidation(name, value))
                    resp.Content.Headers.TryAddWithoutValidation(name, value);
            }
            return resp;
        }
        finally
        {
            client.Close();
        }
    }

    private static void Pump(Godot.HttpClient c, CancellationToken ct, DateTime deadline, params Godot.HttpClient.Status[] whileIn)
    {
        while (whileIn.Contains(c.GetStatus()))
        {
            Check(ct, deadline);
            c.Poll();
            Thread.Sleep(10);
        }
    }

    private static void Check(CancellationToken ct, DateTime deadline)
    {
        ct.ThrowIfCancellationRequested();
        if (DateTime.UtcNow > deadline) throw new TaskCanceledException("timed out");
    }

    private static string Describe(Godot.HttpClient.Status st, string host) => st switch
    {
        Godot.HttpClient.Status.CantResolve => $"can't resolve {host}",
        Godot.HttpClient.Status.CantConnect => $"can't connect to {host}",
        Godot.HttpClient.Status.TlsHandshakeError => $"TLS handshake with {host} failed",
        Godot.HttpClient.Status.ConnectionError => "connection dropped",
        _ => $"unexpected state {st}",
    };

    private static Godot.HttpClient.Method MapMethod(HttpMethod m)
    {
        if (m == HttpMethod.Get) return Godot.HttpClient.Method.Get;
        if (m == HttpMethod.Post) return Godot.HttpClient.Method.Post;
        if (m == HttpMethod.Put) return Godot.HttpClient.Method.Put;
        if (m == HttpMethod.Delete) return Godot.HttpClient.Method.Delete;
        if (m == HttpMethod.Patch) return Godot.HttpClient.Method.Patch;
        if (m == HttpMethod.Head) return Godot.HttpClient.Method.Head;
        if (m == HttpMethod.Options) return Godot.HttpClient.Method.Options;
        throw new HttpRequestException($"unsupported method {m}");
    }
}

/// <summary>The only place game code may get an HttpClient.</summary>
public static class Http
{
    public static System.Net.Http.HttpClient Create(double timeoutSeconds)
        => new(new GodotHttpHandler(), disposeHandler: true) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
}

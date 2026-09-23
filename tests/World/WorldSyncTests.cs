using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Runewake.Engine.Supabase;
using Xunit;

namespace Runewake.Tests.World;

/// <summary>FABLE-020: the world / Tower / expedition clients send the right RPCs and read the replies.</summary>
public class WorldSyncTests
{
    private static readonly SupabaseConfig Cfg = new() { Url = "https://proj.supabase.co", AnonKey = "anon" };
    private static readonly SupabaseSession Me = new() { UserId = "u1", AccessToken = "jwt-u1" };

    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<(string path, string body, string? auth)> Seen = new();
        private readonly Func<string, string, (int, string)> _reply;
        public Fake(Func<string, string, (int, string)> reply) { _reply = reply; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            string body = req.Content == null ? "" : await req.Content.ReadAsStringAsync(ct);
            Seen.Add((req.RequestUri!.PathAndQuery, body, req.Headers.Authorization?.ToString()));
            var (code, text) = _reply(req.RequestUri!.PathAndQuery, body);
            return new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task Discover_Reports_First_Only_When_The_Server_Says_So()
    {
        var fake = new Fake((p, b) => (200, b.Contains(":3\"") ? "{\"first\":true}" : "{\"first\":false}"));
        var w = new WorldDiscoverySync(Cfg, new HttpClient(fake));
        var a = await w.Discover(Me, "w1:elvenwood:0:0:3");
        var b = await w.Discover(Me, "w1:elvenwood:0:0:4");
        Assert.True(a.Ok && a.First, "first");
        Assert.True(b.Ok && !b.First, "not first");
        Assert.Equal("/rest/v1/rpc/discover_blip", fake.Seen[0].path);
        Assert.Contains("\"p_blip_id\":\"w1:elvenwood:0:0:3\"", fake.Seen[0].body);
        Assert.Equal("Bearer jwt-u1", fake.Seen[0].auth);
    }

    [Fact]
    public async Task Odd_Reply_Shapes_Never_Throw()
    {
        // A one-row array wrapping the object is read; an empty array or a bare value is a soft failure.
        var wrapped = await new WorldDiscoverySync(Cfg, new HttpClient(new Fake((p, b) => (200, "[{\"first\":true}]")))).Discover(Me, "w1:e:0:0:1");
        Assert.True(wrapped.Ok && wrapped.First, "wrapped object");
        foreach (var body in new[] { "[]", "42", "\"x\"", "not json" })
        {
            var r = await new WorldDiscoverySync(Cfg, new HttpClient(new Fake((p, b) => (200, body)))).Discover(Me, "w1:e:0:0:1");
            Assert.False(r.Ok, body);
            var t = await new TowerSync(Cfg, new HttpClient(new Fake((p, b) => (200, body)))).RecordBossClear(Me, 1, null);
            Assert.False(t.Ok, body);
        }
    }

    [Fact]
    public async Task Page_Discoveries_Parse_And_Fail_Soft()
    {
        var ok = new WorldDiscoverySync(Cfg, new HttpClient(new Fake((p, b) => (200, "[\"w1:e:0:0:1\",\"w1:e:0:0:7\"]"))));
        var r = await ok.PageDiscoveries(Me, "w1:e:0:0");
        Assert.True(r.ok);
        Assert.Equal(2, r.ids.Count);
        Assert.True(r.ids.Contains("w1:e:0:0:7"));
        var down = new WorldDiscoverySync(Cfg, new HttpClient(new Fake((p, b) => throw new HttpRequestException("offline"))));
        var r2 = await down.PageDiscoveries(Me, "w1:e:0:0");
        Assert.False(r2.ok);
        Assert.Empty(r2.ids);
        Assert.Contains("No connection", r2.error);
    }

    [Fact]
    public async Task Tower_Status_And_Boss_Clear()
    {
        var fake = new Fake((p, b) => p.EndsWith("tower_status")
            ? (200, "[{\"floor\":1,\"title\":\"The Rootgate\",\"is_open\":true,\"unique_clears\":37,\"unlock_threshold\":100,\"opened_at\":null},{\"floor\":2,\"title\":\"?\",\"is_open\":false,\"unique_clears\":0,\"unlock_threshold\":100,\"opened_at\":null}]")
            : (200, "{\"unique_clears\":100,\"threshold\":100,\"next_floor_opened\":true,\"first_ever\":false}"));
        var t = new TowerSync(Cfg, new HttpClient(fake));
        var s = await t.Status(Me);
        Assert.True(s.ok);
        Assert.Equal(2, s.floors.Count);
        Assert.Equal(37L, s.floors[0].UniqueClears);
        Assert.False(s.floors[1].IsOpen);
        var c = await t.RecordBossClear(Me, 1, null);
        Assert.True(c.Ok && c.NextFloorOpened && !c.FirstEver, "the 100th unique clear opened floor 2");
        Assert.Contains("\"p_floor\":1", fake.Seen[1].body);
    }

    [Fact]
    public async Task Expedition_Create_Join_Start()
    {
        var fake = new Fake((p, b) => p switch
        {
            "/rest/v1/rpc/create_expedition" => (200, "\"7c1e0000-0000-0000-0000-000000000001\""),
            "/rest/v1/rpc/join_expedition" => (200, "2"),
            "/rest/v1/rpc/start_expedition" => (200, "123456789"),
            _ => (404, "{}"),
        });
        var x = new ExpeditionSync(Cfg, new HttpClient(fake));
        var c = await x.Create(Me, "raid", "tower:1:boss", 5, "Trikzos", "warrior", new[] { "a", "b" });
        Assert.True(c.ok);
        Assert.Equal("7c1e0000-0000-0000-0000-000000000001", c.id);
        Assert.Contains("\"p_deck\":[\"a\",\"b\"]", fake.Seen[0].body);
        var j = await x.Join(Me, c.id!, "Trikzos", "warrior", new[] { "a" });
        Assert.Equal(2, j.seat);
        var s = await x.Start(Me, c.id!);
        Assert.Equal(123456789L, s.seed);
    }

    [Fact]
    public async Task Nothing_Is_Sent_When_Signed_Out()
    {
        var fake = new Fake((p, b) => (200, "{}"));
        var w = new WorldDiscoverySync(Cfg, new HttpClient(fake));
        var r = await w.Discover(new SupabaseSession(), "w1:e:0:0:1");
        Assert.False(r.Ok);
        Assert.Empty(fake.Seen);
    }
}

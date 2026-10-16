using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Runewake.Engine.Supabase;
using Runewake.Persistence;
using Xunit;

namespace Runewake.Tests.State;

/// <summary>FABLE-061: paid packs on the phone — storing, opening, persisting, and talking to the server.</summary>
public class PacksTests
{
    private static PendingPack Pack(string id) => new()
    {
        Id = id, Cards = new() { "vrd_c_root_warden", "vrd_u_canopy_archer", "vrd_r_bloomweaver" },
        Rarities = new() { "COMMON", "UNCOMMON", "RARE" },
    };

    [Fact]
    public void A_pack_is_stored_once_however_often_the_server_sends_it()
    {
        var p = new ProgressionState();
        Assert.Equal(2, Packs.Receive(p, new[] { Pack("a"), Pack("b") }).Count);
        Assert.Empty(Packs.Receive(p, new[] { Pack("a") }));
        Assert.Equal(2, p.PendingPacks.Count);
        Packs.Open(p, "a");
        Assert.Empty(Packs.Receive(p, new[] { Pack("a") }));     // already opened: never again
        Assert.Single(p.PendingPacks);
    }

    [Fact]
    public void Opening_a_pack_puts_its_three_cards_in_the_collection()
    {
        var p = new ProgressionState();
        p.AddCard("vrd_c_root_warden");
        Packs.Receive(p, new[] { Pack("a") });
        var opened = Packs.Open(p, "a");
        Assert.NotNull(opened);
        Assert.Equal(2, p.Collection["vrd_c_root_warden"]);
        Assert.Equal(1, p.Collection["vrd_r_bloomweaver"]);
        Assert.Empty(p.PendingPacks);
        Assert.Contains("a", p.OpenedPackIds);
        Assert.Null(Packs.Open(p, "a"));                          // gone
    }

    [Fact]
    public void Unopened_packs_survive_a_relaunch_and_the_cloud()
    {
        var p = new ProgressionState();
        Packs.Receive(p, new[] { Pack("a"), Pack("b") });
        Packs.Open(p, "a");
        var path = Path.Combine(Path.GetTempPath(), $"rw_packs_{Guid.NewGuid():N}.db");
        try
        {
            new SaveRepository(path).Save(p);
            var back = new SaveRepository(path).Load();
            Assert.Equal("b", Assert.Single(back.PendingPacks).Id);
            Assert.Equal(3, back.PendingPacks[0].Cards.Count);
            Assert.Contains("a", back.OpenedPackIds);

            var cloud = new ProgressionState();
            ProgressionSnapshot.FromJson(ProgressionSnapshot.FromState(back).ToJson())!.ApplyTo(cloud);
            Assert.Equal("b", Assert.Single(cloud.PendingPacks).Id);
            Assert.Contains("a", cloud.OpenedPackIds);
            Assert.True(ProgressionSnapshot.FromState(cloud).HasPlayed);    // signing in asks before replacing bought packs
        }
        finally { foreach (var ext in new[] { "", "-wal", "-shm" }) try { File.Delete(path + ext); } catch { } }
    }

    [Fact]
    public void The_preview_pack_has_the_same_shape_as_a_real_one()
    {
        var cards = new List<CardDef>
        {
            new() { Id = "c1", Rarity = Rarity.COMMON, Type = CardType.CREATURE, Set = "s" },
            new() { Id = "u1", Rarity = Rarity.UNCOMMON, Type = CardType.CREATURE, Set = "s" },
            new() { Id = "r1", Rarity = Rarity.RARE, Type = CardType.CREATURE, Set = "s" },
            new() { Id = "x1", Rarity = Rarity.RELIC, Type = CardType.RELIC, Set = "s" },
            new() { Id = "t1", Rarity = Rarity.COMMON, Type = CardType.TOKEN, Set = "s" },
            new() { Id = "lost", Rarity = Rarity.COMMON, Type = CardType.CREATURE, Set = "s" },
        };
        int relics = 0;
        var rng = new Random(7);
        for (int i = 0; i < 1000; i++)
        {
            var pk = Packs.PreviewRoll(cards, new HashSet<string> { "lost" }, rng);
            Assert.Equal(3, pk.Cards.Count);
            Assert.Equal("c1", pk.Cards[0]);
            Assert.Equal("u1", pk.Cards[1]);
            Assert.Contains(pk.Cards[2], new[] { "r1", "x1" });
            Assert.Equal("preview", pk.Source);
            if (pk.Rarities[2] == "RELIC") relics++;
        }
        Assert.InRange(relics, 60, 140);                          // ~10%
    }

    // ── the server conversation ──────────────────────────────────────────

    private sealed class Fake : HttpMessageHandler
    {
        public Func<HttpRequestMessage, string, (int, string)> Route = (_, __) => (404, "{}");
        public readonly List<(string url, string body, string auth)> Seen = new();
        public bool Offline;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (Offline) throw new HttpRequestException("no route");
            var body = req.Content == null ? "" : await req.Content.ReadAsStringAsync(ct);
            Seen.Add((req.RequestUri!.ToString(), body, req.Headers.Authorization?.ToString() ?? ""));
            var (s, t) = Route(req, body);
            return new HttpResponseMessage((HttpStatusCode)s) { Content = new StringContent(t, Encoding.UTF8, "application/json") };
        }
    }

    private static readonly SupabaseConfig Cfg = new() { Url = "https://x.supabase.co", AnonKey = "anon" };
    private static readonly SupabaseSession Me = new() { AccessToken = "jwt-me", UserId = "u-me", IsAnonymous = false };

    [Fact]
    public async Task Verify_sends_the_token_to_the_edge_function_and_returns_the_packs()
    {
        var f = new Fake
        {
            Route = (req, body) => (200, "{\"ok\":true,\"already\":false,\"consumed\":true,\"grants\":[" +
                "{\"id\":\"g1\",\"cards\":[\"a\",\"b\",\"c\"],\"rarities\":[\"COMMON\",\"UNCOMMON\",\"RARE\"]}," +
                "{\"id\":\"g2\",\"cards\":[\"d\",\"e\",\"f\"],\"rarities\":[\"COMMON\",\"UNCOMMON\",\"RELIC\"]}]}"),
        };
        var r = await new PackSync(Cfg, new HttpClient(f)).Verify(Me, "rw_pack_5", "tok-123");
        Assert.True(r.Ok);
        Assert.True(r.Consumed);
        Assert.Equal(new[] { "g1", "g2" }, r.Packs.Select(p => p.Id).ToArray());
        Assert.Equal("RELIC", r.Packs[1].Rarities[2]);
        Assert.Equal("https://x.supabase.co/functions/v1/verify-pack-purchase", f.Seen[0].url);
        Assert.Contains("\"purchase_token\":\"tok-123\"", f.Seen[0].body);
        Assert.Equal("Bearer jwt-me", f.Seen[0].auth);
    }

    [Fact]
    public void Verify_replies_are_read_the_way_the_shop_needs()
    {
        var pending = PackSync.ParseVerify(202, "{\"ok\":false,\"pending\":true,\"error\":\"payment pending\"}");
        Assert.False(pending.Ok); Assert.True(pending.Pending); Assert.False(pending.Retry);
        var google = PackSync.ParseVerify(502, "{\"ok\":false,\"error\":\"could not reach Google Play\",\"retry\":true}");
        Assert.True(google.Retry);
        var notReady = PackSync.ParseVerify(503, "{\"ok\":false,\"error\":\"packs are not available yet\",\"retry\":false}");
        Assert.False(notReady.Retry);
        Assert.Equal("packs are not available yet", notReady.Error);
        var junk = PackSync.ParseVerify(500, "<html>");
        Assert.True(junk.Retry);
    }

    [Fact]
    public async Task Offline_verify_says_the_purchase_is_safe()
    {
        var r = await new PackSync(Cfg, new HttpClient(new Fake { Offline = true })).Verify(Me, "rw_pack_1", "tok");
        Assert.False(r.Ok);
        Assert.True(r.Retry);
        Assert.Contains("safe", r.Error);
    }

    [Fact]
    public void The_shop_status_reads_the_server_numbers()
    {
        var s = PackSync.ParseStatus("{\"commons\":21,\"uncommons\":19,\"rares\":15,\"relics\":5,\"can_sell\":true,\"waiting\":334," +
                                     "\"next_card_at\":\"2026-11-09T00:00:00+00:00\",\"relic_chance\":0.1,\"products\":[]}");
        Assert.True(s.Ok); Assert.True(s.CanSell);
        Assert.Equal(60, s.Total);
        Assert.Equal(334, s.Waiting);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero), s.NextCardAt);
    }

    [Fact]
    public async Task Owed_packs_and_marking_them_received_use_the_rpcs()
    {
        var f = new Fake
        {
            Route = (req, body) => req.RequestUri!.AbsolutePath switch
            {
                "/rest/v1/rpc/my_pending_packs" => (200, "[{\"id\":\"g9\",\"cards\":[\"a\",\"b\",\"c\"],\"rarities\":[\"COMMON\",\"UNCOMMON\",\"RARE\"]}]"),
                "/rest/v1/rpc/mark_packs_applied" => (200, "1"),
                "/rest/v1/rpc/discover_cards" => (200, "2"),
                _ => (404, "{}"),
            },
        };
        var sync = new PackSync(Cfg, new HttpClient(f));
        var (ok, packs, _) = await sync.Owed(Me);
        Assert.True(ok);
        Assert.Equal("g9", Assert.Single(packs).Id);
        Assert.True(await sync.MarkReceived(Me, new[] { "g9" }));
        Assert.Contains("\"p_ids\":[\"g9\"]", f.Seen[1].body);
        Assert.True(await sync.Discover(Me, new[] { "vrd_c_root_warden" }, "map"));
        Assert.Contains("\"p_source\":\"map\"", f.Seen[2].body);
    }
}

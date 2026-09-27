using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Runewake.Engine.Coop;
using Runewake.Engine.Engine;
using Runewake.Engine.Supabase;
using Runewake.Sim;
using Runewake.Tests.World;
using Xunit;

namespace Runewake.Tests.Supabase;

/// <summary>
/// FABLE-038: the online-play client (OnlinePlaySync) against an in-memory stand-in for the
/// SQL in supabase/online_play.sql — same functions, same rules (seat from membership, per-seat
/// sequence, idempotent retries, members only). Then two players run a whole duel through it
/// the way the phone and the seat bot do: post my moves, poll for theirs, in lockstep.
/// </summary>
[Collection("NonParallel")]
public class OnlinePlayTests
{
    // ── a stand-in for PostgREST + online_play.sql ──────────────────────────
    private sealed class FakeServer : HttpMessageHandler
    {
        public sealed class Exp { public string Id = Guid.NewGuid().ToString(); public string Code = ""; public string Kind = ""; public string Target = ""; public string State = "open"; public long? Seed; public string Host = ""; public int Max; public List<Mem> Members = new(); public List<(long id, int seat, int seq, string payload)> Moves = new(); }
        public sealed class Mem { public string User = ""; public int Seat; public string Name = ""; public string Cls = ""; public List<string> Deck = new(); public string Status = "joined"; }
        public readonly List<Exp> Exps = new();
        private long _nextRow = 1;
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            string user = req.Headers.Authorization?.Parameter ?? "";      // the test's "JWT" is just the user id
            string fn = req.RequestUri!.AbsolutePath.Split('/').Last();
            var body = req.Content == null ? "{}" : req.Content.ReadAsStringAsync().Result;
            using var doc = JsonDocument.Parse(body);
            var a = doc.RootElement;
            try { return Task.FromResult(Ok(Handle(fn, a, user))); }
            catch (Exception ex) { return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent($"{{\"message\":\"{ex.Message}\"}}", Encoding.UTF8, "application/json") }); }
        }

        private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        private Exp Get(JsonElement a) => Exps.FirstOrDefault(e => e.Id == a.GetProperty("p_id").GetString()) ?? throw new Exception("no such expedition");
        private static Mem Member(Exp e, string user) => e.Members.FirstOrDefault(m => m.User == user) ?? throw new Exception("not a member");

        private string Handle(string fn, JsonElement a, string user)
        {
            lock (Exps)
            {
                switch (fn)
                {
                    case "create_expedition_v2":
                    {
                        foreach (var old in Exps.Where(e => e.Host == user && e.State == "open")) old.State = "abandoned";
                        var e = new Exp { Code = "CODE" + Exps.Count.ToString("00"), Kind = a.GetProperty("p_kind").GetString()!, Target = a.GetProperty("p_target").GetString()!, Host = user, Max = a.GetProperty("p_kind").GetString() == "pvp1v1" ? 2 : 5 };
                        e.Members.Add(new Mem { User = user, Seat = 0, Name = a.GetProperty("p_display_name").GetString()!, Cls = a.GetProperty("p_class").GetString()!, Deck = a.GetProperty("p_deck").EnumerateArray().Select(x => x.GetString()!).ToList() });
                        Exps.Add(e);
                        return JsonSerializer.Serialize(new { id = e.Id, code = e.Code });
                    }
                    case "find_expedition":
                    {
                        var e = Exps.FirstOrDefault(x => x.Code == a.GetProperty("p_code").GetString()!.ToUpperInvariant() && x.State is "open" or "running");
                        return e == null ? "null" : JsonSerializer.Serialize(new { id = e.Id, kind = e.Kind, target = e.Target, state = e.State, max_players = e.Max, players = e.Members.Count, host_name = e.Members[0].Name });
                    }
                    case "join_expedition":
                    {
                        var e = Get(a);
                        if (e.State != "open") throw new Exception("expedition is not open");
                        var mine = e.Members.FirstOrDefault(m => m.User == user);
                        if (mine != null) return mine.Seat.ToString();
                        if (e.Members.Count >= e.Max) throw new Exception("expedition is full");
                        int seat = Enumerable.Range(0, e.Max).First(s => e.Members.All(m => m.Seat != s));
                        e.Members.Add(new Mem { User = user, Seat = seat, Name = a.GetProperty("p_display_name").GetString()!, Cls = a.GetProperty("p_class").GetString()!, Deck = a.GetProperty("p_deck").EnumerateArray().Select(x => x.GetString()!).ToList() });
                        return seat.ToString();
                    }
                    case "set_ready": Member(Get(a), user).Status = a.GetProperty("p_ready").GetBoolean() ? "ready" : "joined"; return "";
                    case "start_expedition":
                    {
                        var e = Get(a);
                        if (e.Host != user || e.State != "open") throw new Exception("only the host can start an open expedition");
                        e.State = "running"; e.Seed = 424242;
                        return e.Seed.ToString()!;
                    }
                    case "leave_expedition": { var e = Get(a); var m = e.Members.FirstOrDefault(x => x.User == user); if (m != null) m.Status = "left"; return ""; }
                    case "finish_expedition": { var e = Get(a); Member(e, user); if (e.State == "running") e.State = "done"; return ""; }
                    case "lobby":
                    {
                        var e = Get(a); Member(e, user);
                        return JsonSerializer.Serialize(new
                        {
                            id = e.Id, kind = e.Kind, target = e.Target, state = e.State, seed = e.Seed, code = e.Code, host = e.Host, max_players = e.Max,
                            members = e.Members.OrderBy(m => m.Seat).Select(m => new { seat = m.Seat, user_id = m.User, display_name = m.Name, class_id = m.Cls, deck = m.Deck, status = m.Status, seconds_ago = 0, me = m.User == user }),
                        });
                    }
                    case "post_move":
                    {
                        var e = Get(a); var m = Member(e, user);
                        if (e.State != "running") throw new Exception($"expedition is {e.State}");
                        int seq = a.GetProperty("p_seq").GetInt32();
                        int next = e.Moves.Where(x => x.seat == m.Seat).Select(x => x.seq + 1).DefaultIfEmpty(0).Max();
                        if (seq < next) return e.Moves.First(x => x.seat == m.Seat && x.seq == seq).id.ToString();
                        if (seq > next) throw new Exception($"sequence gap: expected {next}, got {seq}");
                        long id = _nextRow++;
                        e.Moves.Add((id, m.Seat, seq, a.GetProperty("p_payload").GetRawText()));
                        return id.ToString();
                    }
                    case "moves_since":
                    {
                        var e = Get(a); Member(e, user);
                        long after = a.GetProperty("p_after").GetInt64();
                        var rows = e.Moves.Where(x => x.id > after).OrderBy(x => x.id).Take(200)
                            .Select(x => new { id = x.id, seat = x.seat, seq = x.seq, payload = JsonDocument.Parse(x.payload).RootElement });
                        return JsonSerializer.Serialize(rows);
                    }
                }
                throw new Exception("unknown function " + fn);
            }
        }
    }

    private static SupabaseSession Who(string id) => new() { AccessToken = id, RefreshToken = "r", UserId = id, ExpiresAtUnix = long.MaxValue };
    private static readonly SupabaseConfig Cfg = new() { Url = "https://fake.supabase.co", AnonKey = "anon" };

    [Fact]
    public async Task Host_Code_Join_Ready_Start_Lobby_Round_Trip()
    {
        var server = new FakeServer();
        var sync = new OnlinePlaySync(Cfg, new HttpClient(server));
        var alice = Who("alice"); var bob = Who("bob");

        var c = await sync.Create(alice, "pvp1v1", "pvp", 2, "Alice", "battlemage", new[] { "a", "b" });
        Assert.True(c.ok, c.error);
        Assert.Equal(6, c.code.Length);

        var f = await sync.Find(bob, c.code.ToLowerInvariant());
        Assert.True(f.ok && f.found != null);
        Assert.Equal("Alice", f.found!.HostName);
        Assert.Equal("open", f.found.State);

        var j = await sync.Join(bob, c.id, "Bob", "druid", new[] { "c" });
        Assert.True(j.ok, j.error);
        Assert.Equal(1, j.seat);
        Assert.True((await sync.SetReady(bob, c.id, true)).ok);

        var l = await sync.GetLobby(alice, c.id);
        Assert.True(l.ok, l.error);
        Assert.Equal(2, l.lobby!.Members.Count);
        Assert.Equal("ready", l.lobby.Members[1].Status);
        Assert.True(l.lobby.Mine!.Seat == 0 && l.lobby.Members[0].Me && !l.lobby.Members[1].Me);
        Assert.Null(l.lobby.Seed);

        var s = await sync.Start(alice, c.id);
        Assert.True(s.ok, s.error);
        var l2 = await sync.GetLobby(bob, c.id);
        Assert.Equal("running", l2.lobby!.State);
        Assert.Equal(s.seed, l2.lobby.Seed);
        Assert.Equal(1, l2.lobby.Mine!.Seat);
    }

    [Fact]
    public async Task The_Move_Log_Keeps_Order_Refuses_Gaps_And_Is_Idempotent()
    {
        var server = new FakeServer();
        var sync = new OnlinePlaySync(Cfg, new HttpClient(server));
        var alice = Who("alice"); var bob = Who("bob");
        var c = await sync.Create(alice, "pvp1v1", "pvp", 2, "Alice", "x", Array.Empty<string>());
        await sync.Join(bob, c.id, "Bob", "y", Array.Empty<string>());
        await sync.Start(alice, c.id);

        var m0 = await sync.PostMove(alice, c.id, 0, "{\"t\":\"move\",\"seat\":0,\"seq\":0}");
        Assert.True(m0.ok, m0.error);
        var m1 = await sync.PostMove(alice, c.id, 1, "{\"t\":\"hash\",\"seat\":0,\"round\":1,\"hash\":\"5\"}");
        var again = await sync.PostMove(alice, c.id, 1, "{\"t\":\"hash\",\"seat\":0,\"round\":1,\"hash\":\"5\"}");
        Assert.Equal(m1.id, again.id);
        var gap = await sync.PostMove(alice, c.id, 7, "{}");
        Assert.False(gap.ok); Assert.Contains("sequence gap", gap.error);

        var seen = await sync.MovesSince(bob, c.id, 0);
        Assert.True(seen.ok, seen.error);
        Assert.Equal(2, seen.rows.Count);
        Assert.All(seen.rows, r => Assert.Equal(0, r.Seat));
        Assert.Contains("\"t\":\"move\"", seen.rows[0].Payload);
        var newer = await sync.MovesSince(bob, c.id, seen.rows[0].Id);
        Assert.Single(newer.rows);
    }

    [Fact]
    public async Task Two_Players_Finish_A_Whole_Duel_Through_The_Wire()
    {
        var root = WorldGeneratorTests.Root();
        var cards = System.IO.Directory.GetFiles(System.IO.Path.Combine(root, "content", "cards"), "*.json").SelectMany(Runewake.Engine.Cards.CardLoader.LoadPack).ToList();
        Runewake.Engine.Cards.CardRegistry.Clear(); Runewake.Engine.Cards.CardRegistry.RegisterRange(cards);
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(root, "client", "content", "decks", "starter_decks.json")));
        var st = doc.RootElement.GetProperty("starters").EnumerateArray()
            .Select(s => (cls: s.GetProperty("class_id").GetString()!, deck: s.GetProperty("cards").EnumerateArray().Select(x => x.GetString()!).ToList())).ToList();

        var server = new FakeServer();
        var sync = new OnlinePlaySync(Cfg, new HttpClient(server));
        var alice = Who("alice"); var bob = Who("bob");
        var c = await sync.Create(alice, "pvp1v1", "pvp", 2, "Alice", st[0].cls, st[0].deck);
        await sync.Join(bob, c.id, "Bob", st[1].cls, st[1].deck);
        var started = await sync.Start(alice, c.id);
        Assert.True(started.ok);

        // both build the match from the lobby, exactly as OnlineMatch.FromLobby / PvpBot do
        PvpSession Build(SupabaseSession me)
        {
            var l = sync.GetLobby(me, c.id).Result.lobby!;
            var seats = l.Members.OrderBy(m => m.Seat).Select(m => new SeatConfig { Seat = m.Seat, DisplayName = m.DisplayName, ClassId = m.ClassId, Deck = m.Deck, Artifacts = Runewake.Engine.Cards.ArtifactRegistry.DefaultLoadoutFor(m.ClassId) }).ToList();
            return new PvpSession(new PvpDuel((ulong)l.Seed!.Value, seats[0], seats[1]), l.Mine!.Seat);
        }
        var pa = Build(alice); var pb = Build(bob);
        Assert.Equal(pa.Duel.Hash(), pb.Duel.Hash());

        var bot = new GreedyBot();
        var outA = new Queue<string>(); var outB = new Queue<string>();
        pa.Outbound += outA.Enqueue; pb.Outbound += outB.Enqueue;
        int seqA = 0, seqB = 0; long curA = 0, curB = 0;

        async Task Exchange(PvpSession p, SupabaseSession who, Queue<string> outbox, Func<int> seq, Action bumpSeq, Func<long> cur, Action<long> setCur)
        {
            while (outbox.Count > 0)
            {
                var r = await sync.PostMove(who, c.id, seq(), outbox.Peek());
                Assert.True(r.ok, r.error);
                outbox.Dequeue(); bumpSeq();
            }
            var m = await sync.MovesSince(who, c.id, cur());
            Assert.True(m.ok, m.error);
            foreach (var row in m.rows) { setCur(Math.Max(cur(), row.Id)); if (row.Seat != p.LocalSeat) p.Receive(row.Payload); }
        }

        int guard = 0;
        while (!(pa.State.IsGameOver && pb.State.IsGameOver) && guard++ < 600)
        {
            foreach (var (p, seat) in new[] { (pa, 0), (pb, 1) })
                if (p.IsMyTurn)
                {
                    var a = bot.ChooseAction(p.State, seat) ?? new EndTurnAction { PlayerIndex = seat };
                    Assert.True(p.Local(a, out var err), err);
                }
            await Exchange(pa, alice, outA, () => seqA, () => seqA++, () => curA, v => curA = v);
            await Exchange(pb, bob, outB, () => seqB, () => seqB++, () => curB, v => curB = v);
        }
        Assert.True(pa.State.IsGameOver && pb.State.IsGameOver, "both phones saw the end");
        Assert.Equal(pa.Duel.Hash(), pb.Duel.Hash());
        Assert.False(pa.IsDesynced || pb.IsDesynced);
        Assert.Equal(pa.State.WinnerIndex, pb.State.WinnerIndex);
        Assert.True(await sync.Finish(alice, c.id, new { winner_seat = pa.State.WinnerIndex }));
        Assert.Equal("done", (await sync.GetLobby(bob, c.id)).lobby!.State);
    }
}

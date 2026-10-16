using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.Coop;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Sim;
using Xunit;

namespace Runewake.Tests.World;

/// <summary>FABLE-020/062: co-op expeditions and raids — simultaneous turns, own boards against ONE shared enemy, lockstep in server order.</summary>
// The engine keeps cards in a static registry; these tests share it with every
// other suite, so they must not run in parallel with them (same rule as tests/Engine).
[Collection("NonParallel")]
public class ExpeditionTests
{
    private static readonly GreedyBot Bot = new();
    private static readonly object Gate = new();
    private static List<(string cls, List<string> deck)>? _starters;

    private static List<(string cls, List<string> deck)> Starters()
    {
        lock (Gate)
        {
            if (_starters != null) return _starters;
            var root = WorldGeneratorTests.Root();
            var cards = Directory.GetFiles(Path.Combine(root, "content", "cards"), "*.json").SelectMany(CardLoader.LoadPack).ToList();
            CardRegistry.Clear();
            CardRegistry.RegisterRange(cards);
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "client", "content", "decks", "starter_decks.json")));
            _starters = doc.RootElement.GetProperty("starters").EnumerateArray()
                .Select(s => (s.GetProperty("class_id").GetString()!, s.GetProperty("cards").EnumerateArray().Select(x => x.GetString()!).ToList()))
                .ToList();
            return _starters;
        }
    }

    private static EncounterDef Enemy(int vigor = 25) => new()
    {
        Id = "test:enemy", Name = "Test Foe", Deck = Starters()[2].deck.ToList(), EnemyVigor = vigor,
    };

    private static ExpeditionConfig Config(int seats, WinRule rule, int pool = 0, ulong seed = 42, int enemyVigor = 25) => new()
    {
        Kind = rule == WinRule.SharedPool ? ExpeditionKind.Raid : ExpeditionKind.Coop,
        WinRule = rule,
        Seed = seed,
        SharedPool = pool,
        Encounter = Enemy(enemyVigor),
        Seats = Enumerable.Range(0, seats).Select(i => new SeatConfig
        {
            Seat = i, DisplayName = "P" + i, ClassId = Starters()[i % 7].cls, Deck = Starters()[i % 7].deck.ToList(),
        }).ToList(),
    };

    private static GameAction? EnemyAi(GameState s, int p) => Bot.ChooseAction(s, p);

    /// <summary>Every seat plays its turn (as a bot standing in for the human). Returns the moves in order.</summary>
    private static List<(int seat, int round, GameAction a)> PlayRound(Expedition x)
    {
        var moves = new List<(int, int, GameAction)>();
        int round = x.Round;
        foreach (var b in x.Boards.ToList())
        {
            int guard = 0;
            while (x.Round == round && x.Outcome == ExpeditionOutcome.Running && b.Active && !b.EndedTurn && guard++ < 200)
            {
                var a = Bot.ChooseAction(b.State, 0) ?? new EndTurnAction { PlayerIndex = 0 };
                Assert.True(x.Submit(b.Seat.Seat, round, a, out var err), err);
                moves.Add((b.Seat.Seat, round, a));
            }
        }
        return moves;
    }

    [Fact]
    public void Everyone_Fights_Their_Own_Board_And_The_Round_Waits_For_All()
    {
        var x = new Expedition(Config(3, WinRule.AnySeatWins), EnemyAi);
        Assert.Equal(3, x.Boards.Count);
        Assert.True(x.Boards.Select(b => StateHash.Of(b.State)).Distinct().Count() == 3, "three different boards (own shuffles)");
        // Seat 0 ends its turn; the round must not resolve until seats 1 and 2 do.
        Assert.True(x.Submit(0, 1, new EndTurnAction { PlayerIndex = 0 }, out _));
        Assert.Equal(1, x.Round);
        Assert.False(x.Submit(0, 1, new EndTurnAction { PlayerIndex = 0 }, out var why), "no second End Turn");
        Assert.Contains("already ended", why);
        Assert.True(x.Submit(1, 1, new EndTurnAction { PlayerIndex = 0 }, out _));
        Assert.True(x.Submit(2, 1, new EndTurnAction { PlayerIndex = 0 }, out _));
        Assert.Equal(2, x.Round);
        Assert.True(x.Boards.All(b => b.State.CurrentPlayerIndex == 0 || b.State.IsGameOver), "enemies all took their turn");
        Assert.False(x.Submit(1, 1, new EndTurnAction { PlayerIndex = 0 }, out _), "stale round refused");
    }

    [Fact]
    public void An_Expedition_Plays_To_A_Result_Deterministically()
    {
        ulong Run(out ExpeditionOutcome o, out int rounds)
        {
            var x = new Expedition(Config(4, WinRule.AnySeatWins, seed: 777), EnemyAi);
            int guard = 0;
            while (x.Outcome == ExpeditionOutcome.Running && guard++ < 80) PlayRound(x);
            o = x.Outcome; rounds = x.Round;
            return x.Hash();
        }
        var h1 = Run(out var o1, out var r1);
        var h2 = Run(out var o2, out var r2);
        Assert.NotEqual(ExpeditionOutcome.Running, o1);
        Assert.Equal(o1, o2);
        Assert.Equal(r1, r2);
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void Raid_Shares_One_Health_Pool_Across_Every_Board()
    {
        var x = new Expedition(Config(5, WinRule.SharedPool, pool: 150), EnemyAi);
        Assert.True(x.Boards.All(b => b.State.Players[1].Vigor == 150), "every board starts at the pool");
        int lastPool = 150, guard = 0;
        while (x.Outcome == ExpeditionOutcome.Running && guard++ < 80)
        {
            PlayRound(x);
            Assert.True(x.PoolRemaining <= lastPool, "the pool only goes down");
            lastPool = x.PoolRemaining ?? 0;
            if (x.Outcome == ExpeditionOutcome.Running)
                foreach (var b in x.Boards.Where(b => b.Active))
                    Assert.Equal(x.PoolRemaining, b.State.Players[1].Vigor);
        }
        Assert.NotEqual(ExpeditionOutcome.Running, x.Outcome);
        Assert.True(x.PoolRemaining < 150, $"five players dealt damage to the shared pool (left {x.PoolRemaining})");
        if (x.Outcome == ExpeditionOutcome.Victory) Assert.Equal(0, x.PoolRemaining);
    }

    [Fact]
    public void A_Knocked_Out_Player_Stays_Out_And_The_Rest_Fight_On()
    {
        var cfg = Config(3, WinRule.AllSeatsMustWin, enemyVigor: 999);   // nobody can win: we watch knockouts
        var x = new Expedition(cfg, EnemyAi);
        int guard = 0;
        while (x.Outcome == ExpeditionOutcome.Running && guard++ < 70) PlayRound(x);
        Assert.Equal(ExpeditionOutcome.Defeat, x.Outcome);
        Assert.True(x.Boards.All(b => b.IsOut) || x.Round > cfg.MaxRounds, "defeat means everyone out (or the round cap)");
        var outBoard = x.Boards.FirstOrDefault(b => b.IsOut);
        if (outBoard != null)
            Assert.False(x.Submit(outBoard.Seat.Seat, x.Round, new EndTurnAction { PlayerIndex = 0 }, out _), "an out player cannot act");
    }

    /// <summary>
    /// FABLE-062: the server. Phones post moves in any interleaving; the server numbers them in the order
    /// they arrive and every phone (the sender too) reads them back in that one order.
    /// </summary>
    private sealed class FakeServer
    {
        private readonly List<string> _rows = new();
        private readonly Dictionary<LockstepSession, int> _cursor = new();
        public FakeServer(params LockstepSession[] phones)
        {
            foreach (var p in phones) { _cursor[p] = 0; p.Outbound += m => _rows.Add(m); }
        }
        /// <summary>One phone polls: everything after its cursor, in server order.</summary>
        public void Poll(LockstepSession p)
        {
            while (_cursor[p] < _rows.Count) p.Receive(_rows[_cursor[p]++]);
        }
        public void PollAll(Random? rng = null)
        {
            var order = _cursor.Keys.ToList();
            if (rng != null) order = order.OrderBy(_ => rng.Next()).ToList();
            foreach (var p in order) Poll(p);
        }
    }

    private static string EnemySide(GameState s)
    {
        var e = s.Players[1];
        var lanes = string.Join(",", e.Lanes.Select(l => l.Occupant == null ? "_" : $"{l.Occupant.InstanceId}:{l.Occupant.CurrentAttack}/{l.Occupant.CurrentVigor}"));
        return $"{e.Vigor}/{e.MaxVigor}|{lanes}|{string.Join(",", e.Hand.Select(c => c.InstanceId))}|{e.Deck.Count}|{e.Barrow.Count}";
    }

    private static void AssertOneEnemy(Expedition x, string when)
    {
        var sides = x.Boards.Where(b => b.Active).Select(b => EnemySide(b.State)).Distinct().ToList();
        Assert.True(sides.Count <= 1, $"every board faces the same enemy ({when}): {string.Join("  VS  ", sides)}");
    }

    [Fact]
    public void Every_Board_Faces_The_Same_One_Enemy_After_Every_Move()
    {
        var x = new Expedition(Config(3, WinRule.AnySeatWins, seed: 314), EnemyAi);
        AssertOneEnemy(x, "at the start");
        int guard = 0, moves = 0;
        while (x.Outcome == ExpeditionOutcome.Running && guard++ < 60)
        {
            int round = x.Round;
            foreach (var b in x.Boards.ToList())
            {
                int g = 0;
                while (x.Round == round && x.Outcome == ExpeditionOutcome.Running && b.Active && !b.EndedTurn && g++ < 200)
                {
                    var a = Bot.ChooseAction(b.State, 0) ?? new EndTurnAction { PlayerIndex = 0 };
                    Assert.True(x.Submit(b.Seat.Seat, round, a, out var err), err);
                    moves++;
                    AssertOneEnemy(x, $"after move {moves}, round {round}");
                }
            }
        }
        Assert.NotEqual(ExpeditionOutcome.Running, x.Outcome);
        Assert.True(moves > 20, "a real fight happened");
    }

    [Fact]
    public void A_Creature_One_Player_Kills_Is_Gone_For_Everyone()
    {
        var x = new Expedition(Config(2, WinRule.AnySeatWins, seed: 99), EnemyAi);
        int killsSeen = 0, guard = 0;
        while (x.Outcome == ExpeditionOutcome.Running && guard++ < 60 && killsSeen == 0)
        {
            int round = x.Round;
            foreach (var b in x.Boards.ToList())
            {
                int g = 0;
                while (x.Round == round && x.Outcome == ExpeditionOutcome.Running && b.Active && !b.EndedTurn && g++ < 200)
                {
                    var a = Bot.ChooseAction(b.State, 0) ?? new EndTurnAction { PlayerIndex = 0 };
                    var enemyBefore = b.State.Players[1].Lanes.Count(l => l.Occupant != null);
                    Assert.True(x.Submit(b.Seat.Seat, round, a, out var err), err);
                    var other = x.Boards.First(o => o != b);
                    int after = b.State.Players[1].Lanes.Count(l => l.Occupant != null);
                    if (a is AttackAction && after < enemyBefore)
                    {
                        killsSeen++;
                        Assert.Equal(after, other.State.Players[1].Lanes.Count(l => l.Occupant != null));
                    }
                }
            }
        }
        Assert.True(killsSeen > 0, "a player's attack killed an enemy creature, and the ally's board lost it too");
    }

    [Fact]
    public void The_Enemy_Is_Scaled_To_The_Team()
    {
        var solo = new Expedition(Config(1, WinRule.AnySeatWins, enemyVigor: 25), EnemyAi);
        var trio = new Expedition(Config(3, WinRule.AnySeatWins, enemyVigor: 25), EnemyAi);
        Assert.Equal(1.0, solo.EnemyScale, 3);
        Assert.Equal(2.5, trio.EnemyScale, 3);                        // 1 + 0.75 × 2
        Assert.Equal(25, solo.EnemyMaxVigor);
        Assert.Equal(63, trio.EnemyMaxVigor);                         // 25 × 2.5 = 62.5 → 63
        Assert.True(trio.Boards.All(b => b.State.Players[1].Vigor == 63), "every board shows the team-sized enemy");
        Assert.Equal(new[] { 1.0, 1.75, 2.5, 3.15, 3.6 }, Enumerable.Range(1, 5).Select(n => Math.Round(Expedition.ScaleFor(n, 0.75), 3)).ToArray());
        var soloCards = solo.Board(0).State.Players[1].Deck.Concat(solo.Board(0).State.Players[1].Hand).Where(c => c.BaseAttack > 0).ToList();
        var trioCards = trio.Board(0).State.Players[1].Deck.Concat(trio.Board(0).State.Players[1].Hand).ToDictionary(c => c.InstanceId);
        Assert.NotEmpty(soloCards);
        foreach (var c in soloCards)
        {
            var t = trioCards[c.InstanceId];                          // same seed, same enemy ids
            Assert.Equal(Math.Max(c.BaseAttack, (int)Math.Round(c.BaseAttack * 2.5, MidpointRounding.AwayFromZero)), t.BaseAttack);
            Assert.Equal(Math.Max(c.BaseVigor, (int)Math.Round(c.BaseVigor * 2.5, MidpointRounding.AwayFromZero)), t.BaseVigor);
        }
        var raid = new Expedition(Config(3, WinRule.SharedPool, pool: 150), EnemyAi);
        Assert.Equal(150, raid.EnemyMaxVigor);                        // a raid pool is already sized for the team
    }

    [Fact]
    public void The_Enemy_Turns_On_One_Player_A_Round_In_Rotation()
    {
        var x = new Expedition(Config(3, WinRule.AnySeatWins, seed: 5, enemyVigor: 999), EnemyAi);
        var focus = new List<int>();
        for (int r = 0; r < 4 && x.Outcome == ExpeditionOutcome.Running && x.Boards.All(b => b.Active); r++)
        {
            foreach (var b in x.Boards) Assert.True(x.Submit(b.Seat.Seat, x.Round, new EndTurnAction { PlayerIndex = 0 }, out var err), err);
            focus.Add(x.LastFocusSeat);
        }
        Assert.Equal(new[] { 0, 1, 2, 0 }.Take(focus.Count).ToArray(), focus.ToArray());
    }

    [Fact]
    public void Ending_Your_Turn_Shows_The_Enemy_Turn_While_Allies_Act()
    {
        var x = new Expedition(Config(2, WinRule.AnySeatWins, seed: 11), EnemyAi);
        Assert.True(x.Submit(0, 1, new EndTurnAction { PlayerIndex = 0 }, out _));
        Assert.Equal(1, x.ViewState(0).CurrentPlayerIndex);           // drawn as "enemy turn / waiting"
        Assert.Equal(0, x.Board(0).State.CurrentPlayerIndex);         // applied only when the round resolves
        Assert.Equal(0, x.ViewState(1).CurrentPlayerIndex);
    }

    [Fact]
    public void Two_Phones_Show_The_Same_Fight_When_Moves_Come_Back_In_Server_Order()
    {
        var cfg = Config(2, WinRule.SharedPool, pool: 70, seed: 99);
        var a = new LockstepSession(new Expedition(cfg, EnemyAi), 0);
        var b = new LockstepSession(new Expedition(cfg, EnemyAi), 1);
        var server = new FakeServer(a, b);
        bool desync = false; int rejected = 0;
        a.Desynced += (_, _) => desync = true; b.Desynced += (_, _) => desync = true;
        a.LocalMoveRejected += (_, _) => rejected++; b.LocalMoveRejected += (_, _) => rejected++;
        var rng = new Random(5);

        int guard = 0;
        while (a.Expedition.Outcome == ExpeditionOutcome.Running && guard++ < 4000)
        {
            // both players act on what their phone predicts, BEFORE seeing each other's latest moves
            foreach (var (s, seat) in new[] { (a, 0), (b, 1) }.OrderBy(_ => rng.Next()))
            {
                var board = s.Predicted.Board(seat);
                if (!board.Active || board.EndedTurn || board.State.CurrentPlayerIndex != 0) continue;
                var mv = Bot.ChooseAction(board.State, 0) ?? new EndTurnAction { PlayerIndex = 0 };
                Assert.True(s.Local(mv, out var err), err);
            }
            if (rng.Next(3) == 0) server.Poll(rng.Next(2) == 0 ? a : b);   // phones poll at different moments
            else server.PollAll(rng);
            Assert.Equal(EnemySide(a.Predicted.Board(0).State), EnemySide(a.Predicted.Board(1).State));
        }
        server.PollAll(); server.PollAll();
        Assert.NotEqual(ExpeditionOutcome.Running, a.Expedition.Outcome);
        Assert.Equal(a.Expedition.Outcome, b.Expedition.Outcome);
        Assert.Equal(a.Expedition.Hash(), b.Expedition.Hash());
        Assert.Equal(0, a.Unconfirmed + b.Unconfirmed);
        Assert.False(desync, $"no desync reported ({rejected} local move(s) were overtaken by an ally's)");
    }

    [Fact]
    public void A_Player_Who_Concedes_Is_Out_And_The_Team_Fights_On()
    {
        // FABLE-COOP-1: giving up in co-op knocks only your own board out.
        var x = new Expedition(Config(2, WinRule.AnySeatWins, seed: 7), EnemyAi);
        Assert.True(x.Concede(1, x.Round, out var err), err);
        Assert.True(x.Board(1).IsOut);
        Assert.True(x.Board(1).Conceded);
        Assert.Equal(ExpeditionOutcome.Running, x.Outcome);
        Assert.False(x.Submit(1, x.Round, new EndTurnAction { PlayerIndex = 0 }, out _), "a player who gave up cannot act");
        Assert.False(x.Concede(1, x.Round, out _), "cannot give up twice");
        int r = x.Round;
        PlayRound(x);                                     // the round no longer waits for seat 1
        Assert.True(x.Round > r || x.Outcome != ExpeditionOutcome.Running);
        Assert.True(x.Concede(0, x.Round, out err), err);  // the last one standing gives up: the team loses
        Assert.Equal(ExpeditionOutcome.Defeat, x.Outcome);
    }

    [Fact]
    public void A_Concede_While_Waiting_For_Allies_Resolves_The_Round()
    {
        // Seat 0 has ended their turn; seat 1 gives up mid-turn: the round resolves at once.
        var x = new Expedition(Config(2, WinRule.AnySeatWins, seed: 8), EnemyAi);
        Assert.True(x.Submit(0, 1, new EndTurnAction { PlayerIndex = 0 }, out var err), err);
        Assert.Equal(1, x.Round);
        Assert.True(x.Concede(1, 1, out err), err);
        Assert.Equal(2, x.Round);
    }

    [Fact]
    public void A_Concede_After_Ending_The_Turn_Lands_The_Same_On_Every_Phone()
    {
        // Seat 1 ends their turn, then gives up while seat 0 is still acting; the phones poll at different times.
        var cfg = Config(2, WinRule.SharedPool, pool: 70, seed: 21);
        var a = new LockstepSession(new Expedition(cfg, EnemyAi), 0);
        var b = new LockstepSession(new Expedition(cfg, EnemyAi), 1);
        var server = new FakeServer(a, b);
        bool desync = false;
        a.Desynced += (_, _) => desync = true; b.Desynced += (_, _) => desync = true;

        void Play(LockstepSession s, int seat, bool end)
        {
            int guard = 0;
            while (guard++ < 50)
            {
                var bd = s.Predicted.Board(seat);
                if (!bd.Active || bd.EndedTurn) break;
                var mv = Bot.ChooseAction(bd.State, 0) ?? new EndTurnAction { PlayerIndex = 0 };
                if (mv is EndTurnAction && !end) break;
                Assert.True(s.Local(mv, out var e), e);
            }
        }
        Play(b, 1, end: true);
        Play(a, 0, end: false);
        Assert.True(b.LocalConcede(out var err), err);
        server.Poll(b);                                                 // phone B sees its own moves come back
        Assert.True(b.Expedition.HasConceded(1));
        Assert.True(a.Local(new EndTurnAction { PlayerIndex = 0 }, out err), err);
        server.PollAll(); server.PollAll();
        Assert.Equal(a.Expedition.Round, b.Expedition.Round);
        Assert.True(a.Expedition.Board(1).IsOut);
        Assert.True(b.Expedition.Board(1).IsOut);
        Assert.Equal(a.Expedition.Hash(), b.Expedition.Hash());

        int guard2 = 0;
        while (a.Expedition.Outcome == ExpeditionOutcome.Running && guard2++ < 80) { Play(a, 0, end: true); server.PollAll(); }
        Assert.NotEqual(ExpeditionOutcome.Running, a.Expedition.Outcome);
        Assert.Equal(a.Expedition.Outcome, b.Expedition.Outcome);
        Assert.Equal(a.Expedition.Hash(), b.Expedition.Hash());
        Assert.False(desync, "no desync reported");
    }

    [Fact]
    public void Moves_Survive_The_Wire()
    {
        var moves = new GameAction[]
        {
            new EndTurnAction { PlayerIndex = 0 },
            new PlayCardAction { PlayerIndex = 0, CardInstanceId = 12, Cost = 3, LaneIndex = 2 },
            new AttackAction { PlayerIndex = 0, SourceLane = 1, TargetLane = null },
            new TapArtifactAction { PlayerIndex = 0, SlotIndex = 1 },
        };
        foreach (var m in moves)
        {
            var back = NetMessage.FromJson(NetMessage.Move(3, 7, 11, m).ToJson())!;
            Assert.Equal(3, back.Seat); Assert.Equal(7, back.Round); Assert.Equal(11, back.Seq);
            Assert.Equal(m, back.ReadAction());
        }
        Assert.Null(NetMessage.FromJson("not json"));
    }

    [Fact]
    public void Pvp_Only_The_Player_On_Turn_May_Move_And_Both_Phones_Agree()
    {
        var s0 = new SeatConfig { Seat = 0, ClassId = Starters()[0].cls, Deck = Starters()[0].deck.ToList() };
        var s1 = new SeatConfig { Seat = 1, ClassId = Starters()[3].cls, Deck = Starters()[3].deck.ToList() };
        var phoneA = new PvpDuel(2024, s0, s1);
        var phoneB = new PvpDuel(2024, s0, s1);
        Assert.Equal(phoneA.Hash(), phoneB.Hash());
        Assert.False(phoneA.Submit(1, new EndTurnAction { PlayerIndex = 1 }, out var why), "seat 1 cannot move on seat 0's turn");
        Assert.Contains("not your turn", why);
        int guard = 0;
        while (!phoneA.State.IsGameOver && guard++ < 3000)
        {
            int seat = phoneA.State.CurrentPlayerIndex;
            var mv = Bot.ChooseAction(phoneA.State, seat) ?? new EndTurnAction { PlayerIndex = seat };
            Assert.True(phoneA.Submit(seat, mv, out var e1), e1);
            Assert.True(phoneB.Submit(seat, NetMessage.FromJson(NetMessage.Move(seat, 0, 0, mv).ToJson())!.ReadAction()!, out var e2), e2);
        }
        Assert.True(phoneA.State.IsGameOver, "finished");
        Assert.Equal(phoneA.Hash(), phoneB.Hash());
    }
}

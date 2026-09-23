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

/// <summary>FABLE-020: co-op expeditions and raids — simultaneous turns, own boards, shared outcome, lockstep.</summary>
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
    public void Damage_On_Any_Board_Comes_Off_The_Pool_That_Round()
    {
        var x = new Expedition(Config(2, WinRule.SharedPool, pool: 200), EnemyAi);
        // Hand-deal damage on board 1 during the players' turn, then end the round.
        var b1 = x.Board(1);
        var s = b1.State.Clone(); s.Players[1].Vigor -= 17;
        typeof(SeatBoard).GetProperty("State")!.SetValue(b1, s);
        Assert.True(x.Submit(0, 1, new EndTurnAction { PlayerIndex = 0 }, out _));
        Assert.True(x.Submit(1, 1, new EndTurnAction { PlayerIndex = 0 }, out _));
        Assert.True(x.PoolRemaining <= 183, $"the 17 dealt on board 1 came off the pool ({x.PoolRemaining})");
        Assert.Equal(x.PoolRemaining, x.Board(0).State.Players[1].Vigor);
        Assert.Equal(x.PoolRemaining, x.Board(1).State.Players[1].Vigor);
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

    [Fact]
    public void Two_Phones_Stay_In_Lockstep_Over_A_Jumbled_Channel()
    {
        var cfg = Config(2, WinRule.SharedPool, pool: 70, seed: 99);
        var a = new LockstepSession(new Expedition(cfg, EnemyAi), 0);
        var b = new LockstepSession(new Expedition(cfg, EnemyAi), 1);
        var toA = new List<string>(); var toB = new List<string>();
        a.Outbound += m => toB.Add(m);
        b.Outbound += m => toA.Add(m);
        bool desync = false;
        a.Desynced += (_, _) => desync = true; b.Desynced += (_, _) => desync = true;
        var rng = new Random(5);

        void Deliver()
        {
            // Out of order, like a real network: shuffle what's in flight.
            var ba = toA.OrderBy(_ => rng.Next()).ToList(); toA.Clear(); foreach (var m in ba) a.Receive(m);
            var bb = toB.OrderBy(_ => rng.Next()).ToList(); toB.Clear(); foreach (var m in bb) b.Receive(m);
        }

        int guard = 0;
        while (a.Expedition.Outcome == ExpeditionOutcome.Running && guard++ < 3000)
        {
            foreach (var (s, seat) in new[] { (a, 0), (b, 1) })
            {
                var board = s.Expedition.Board(seat);
                if (!board.Active || board.EndedTurn || board.State.CurrentPlayerIndex != 0) continue;
                var mv = Bot.ChooseAction(board.State, 0) ?? new EndTurnAction { PlayerIndex = 0 };
                Assert.True(s.Local(mv, out var err), err);
            }
            Deliver();
        }
        Deliver(); Deliver();
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

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

/// <summary>
/// FABLE-038: two phones, one duel. Both build the same PvpDuel from the seed; each applies
/// its own moves at once and the other's as they arrive — in order, out of order, duplicated,
/// with hashes checked after every move. The "channel" here is a list of JSON strings, which
/// is exactly what the move log holds.
/// </summary>
[Collection("NonParallel")]
public class PvpSessionTests
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

    private static (SeatConfig a, SeatConfig b) Seats()
    {
        var st = Starters();
        return (new SeatConfig { Seat = 0, DisplayName = "Alice", ClassId = st[0].cls, Deck = st[0].deck },
                new SeatConfig { Seat = 1, DisplayName = "Bob", ClassId = st[1 % st.Count].cls, Deck = st[1 % st.Count].deck });
    }

    private static (PvpSession a, PvpSession b, List<string> wireA, List<string> wireB) TwoPhones(ulong seed = 7)
    {
        var (s0, s1) = Seats();
        var a = new PvpSession(new PvpDuel(seed, s0, s1), 0);
        var b = new PvpSession(new PvpDuel(seed, s0, s1), 1);
        var wireA = new List<string>(); var wireB = new List<string>();
        a.Outbound += wireA.Add;
        b.Outbound += wireB.Add;
        return (a, b, wireA, wireB);
    }

    [Fact]
    public void Both_Phones_Build_The_Same_Duel_From_The_Seed()
    {
        var (a, b, _, _) = TwoPhones();
        Assert.Equal(a.Duel.Hash(), b.Duel.Hash());
        Assert.True(a.IsMyTurn);
        Assert.False(b.IsMyTurn);
        Assert.True(a.State.Players[0].HasMulliganed && a.State.Players[1].HasMulliganed, "no mulligan online");
    }

    [Fact]
    public void Only_The_Seat_Whose_Turn_It_Is_May_Move_And_Only_Its_Own_Side()
    {
        var (a, b, _, _) = TwoPhones();
        Assert.False(b.Local(new EndTurnAction { PlayerIndex = 1 }, out var e1));
        Assert.Contains("not your turn", e1);
        Assert.False(a.Local(new EndTurnAction { PlayerIndex = 1 }, out var e2));
        Assert.Contains("own side", e2);
        Assert.True(a.Local(new EndTurnAction { PlayerIndex = 0 }, out _));
    }

    [Fact]
    public void A_Whole_Bot_Duel_Stays_In_Step_Over_A_Jumbled_Duplicated_Channel()
    {
        var (a, b, wireA, wireB) = TwoPhones();
        var rnd = new Random(3);
        int guard = 0;
        while (!a.State.IsGameOver && guard++ < 400)
        {
            var mover = a.IsMyTurn ? a : b;
            int seat = mover.LocalSeat;
            var action = Bot.ChooseAction(mover.State, seat) ?? new EndTurnAction { PlayerIndex = seat };
            Assert.True(mover.Local(action, out var err), err);

            // deliver: shuffled, every message twice, sometimes a message held back until later
            Deliver(wireA, b, rnd); Deliver(wireB, a, rnd);
        }
        Deliver(wireA, b, rnd); Deliver(wireB, a, rnd);
        Assert.True(a.State.IsGameOver, "the duel finished");
        Assert.Equal(a.Duel.Hash(), b.Duel.Hash());
        Assert.False(a.IsDesynced); Assert.False(b.IsDesynced);
        Assert.Equal(a.State.WinnerIndex, b.State.WinnerIndex);
        Assert.Equal(a.MovesApplied, b.MovesApplied);
    }

    private static void Deliver(List<string> wire, PvpSession to, Random rnd)
    {
        var batch = wire.ToList(); wire.Clear();
        // hold one back now and then; it arrives with the next batch
        if (batch.Count > 1 && rnd.NextDouble() < 0.3) { wire.Add(batch[^1]); batch.RemoveAt(batch.Count - 1); }
        foreach (var m in batch.OrderBy(_ => rnd.Next())) { to.Receive(m); to.Receive(m); }
    }

    [Fact]
    public void Concede_Ends_It_For_Both_And_Leave_Forfeits()
    {
        var (a, b, wireA, wireB) = TwoPhones();
        a.LocalConcede();
        foreach (var m in wireA) b.Receive(m);
        Assert.True(b.State.IsGameOver);
        Assert.Equal(1, b.State.WinnerIndex);
        Assert.Equal(a.State.WinnerIndex, b.State.WinnerIndex);

        var (c, d, wireC, _) = TwoPhones(11);
        c.LocalLeave();
        foreach (var m in wireC) d.Receive(m);
        Assert.True(d.OpponentLeft);
        Assert.Equal(1, d.State.WinnerIndex);

        var (e, f, _, _) = TwoPhones(12);
        f.OpponentAbandoned();
        Assert.True(f.State.IsGameOver);
        Assert.Equal(1, f.State.WinnerIndex);
        _ = e;
    }

    [Fact]
    public void A_Move_The_Engine_Refuses_Flags_A_Desync_Instead_Of_Crashing()
    {
        var (a, b, _, _) = TwoPhones();
        int flagged = -1;
        b.Desynced += (n, s) => flagged = s;
        // seat 0 "attacks" from an empty lane — a move phone A could only send if its state differed
        b.Receive(NetMessage.Move(0, 0, 0, new AttackAction { PlayerIndex = 0, SourceLane = 2, TargetLane = 2 }).ToJson());
        Assert.True(b.IsDesynced);
        Assert.Equal(0, flagged);
        _ = a;
    }

    [Fact]
    public void Seat_View_Translation_Is_Its_Own_Inverse()
    {
        // The client's GameStateManager shows every phone "0 = me"; seat = view ^ localSeat.
        foreach (int local in new[] { 0, 1 })
            foreach (int view in new[] { 0, 1 })
                Assert.Equal(view, (view ^ local) ^ local);
        Assert.Equal(1, 0 ^ 1);   // seat 1's phone: view 0 (me) is canonical seat 1
        Assert.Equal(0, 1 ^ 1);   // and view 1 (the opponent) is seat 0
    }
}

using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>FABLE-064: the engine tells the screen what happened, without changing what happens.</summary>
public class CombatEventTests
{
    private static GameState Fresh()
    {
        var s = new GameState(seed: 7);
        foreach (var p in s.Players)
        {
            p.AttunementMax = 10; p.Attunement = 10;
            for (int i = 0; i < 6; i++)
                p.Deck.Add(new CardInstance(s.NextInstanceId++, "tst_filler", p.Index) { Zone = Zone.Deck, CardType = CardType.CREATURE, Cost = 1, BaseAttack = 1, BaseVigor = 1 });
        }
        return s;
    }

    private static CardInstance Place(GameState s, int side, int lane, int atk, int vig, params string[] keywords)
    {
        var c = new CardInstance(s.NextInstanceId++, $"tst_{side}_{lane}", side)
        {
            Zone = Zone.Lane, LaneIndex = lane, CardType = CardType.CREATURE, Cost = 1,
            BaseAttack = atk, BaseVigor = vig, IsExhausted = false
        };
        foreach (var k in keywords) c.Keywords.Add(k);
        if (keywords.Contains("WARD")) c.WardRemaining = 1;
        s.Players[side].Lanes[lane].Occupant = c;
        return c;
    }

    [Fact]
    public void Attack_on_a_creature_records_attack_both_hits_and_the_death()
    {
        var s = Fresh();
        Place(s, 0, 1, 4, 5);
        Place(s, 1, 1, 2, 3);
        var after = DuelEngine.Apply(s, new AttackAction { PlayerIndex = 0, SourceLane = 1, TargetLane = 1 });

        var kinds = after.Events.Select(e => e.Kind).ToList();
        Assert.Equal(FxKind.Attack, kinds[0]);
        var hit = after.Events.First(e => e.Kind == FxKind.Damage && e.Tag == "ATTACK");
        Assert.Equal(4, hit.Amount);
        Assert.Equal(1, hit.TargetSide); Assert.Equal(1, hit.TargetLane); Assert.Equal(0, hit.Side); Assert.Equal(1, hit.SourceLane);
        var clash = after.Events.First(e => e.Kind == FxKind.Damage && e.Tag == "CLASH");
        Assert.Equal(2, clash.Amount); Assert.Equal(0, clash.TargetSide);
        var death = after.Events.Single(e => e.Kind == FxKind.Death);
        Assert.Equal("COMBAT", death.Tag); Assert.Equal("tst_1_1", death.TargetDefId);
        Assert.True(after.Events.Select(e => e.Seq).SequenceEqual(after.Events.Select(e => e.Seq).OrderBy(x => x)));
    }

    [Fact]
    public void Face_hit_guard_and_ward_are_named()
    {
        var s = Fresh();
        Place(s, 0, 0, 3, 3);
        var guard = Place(s, 1, 4, 1, 6, "GUARD", "WARD");
        var after = DuelEngine.Apply(s, new AttackAction { PlayerIndex = 0, SourceLane = 0, TargetLane = 0 });
        Assert.Contains(after.Events, e => e.Kind == FxKind.Keyword && e.Tag == "GUARD" && e.TargetInstanceId == guard.InstanceId);
        Assert.Contains(after.Events, e => e.Kind == FxKind.Keyword && e.Tag == "WARD" && e.Amount == 3);
        Assert.DoesNotContain(after.Events, e => e.Kind == FxKind.Damage && e.Tag == "ATTACK");   // the Ward ate it

        var s2 = Fresh();
        Place(s2, 0, 0, 3, 3);
        var face = DuelEngine.Apply(s2, new AttackAction { PlayerIndex = 0, SourceLane = 0, TargetLane = 0 });
        var hit = face.Events.Single(e => e.Kind == FxKind.Damage);
        Assert.True(hit.TargetsFace); Assert.Equal(1, hit.TargetSide); Assert.Equal(3, hit.Amount);
        Assert.Equal(22, face.Players[1].Vigor);
    }

    [Fact]
    public void Events_do_not_touch_the_hash_and_are_cleared_each_action()
    {
        var s = Fresh();
        Place(s, 0, 0, 3, 3);
        var a = DuelEngine.Apply(s, new AttackAction { PlayerIndex = 0, SourceLane = 0, TargetLane = 0 });
        ulong h = a.ComputeStateHash();
        a.Events.Clear();
        Assert.Equal(h, a.ComputeStateHash());

        var b = DuelEngine.Apply(a, new EndTurnAction { PlayerIndex = 0 });
        Assert.DoesNotContain(b.Events, e => e.Kind == FxKind.Attack);
        Assert.True(b.EventSeq >= a.EventSeq);
    }

    [Fact]
    public void Clone_keeps_events_and_apply_starts_fresh()
    {
        var s = Fresh();
        Place(s, 0, 0, 3, 3);
        var a = DuelEngine.Apply(s, new AttackAction { PlayerIndex = 0, SourceLane = 0, TargetLane = 0 });
        var c = a.Clone();
        Assert.Equal(a.Events.Count, c.Events.Count);
        Assert.NotEmpty(c.Events);
    }

    [Fact]
    public void Playing_a_creature_is_a_summon_event()
    {
        var s = Fresh();
        var card = new CardInstance(s.NextInstanceId++, "tst_play", 0) { Zone = Zone.Hand, CardType = CardType.CREATURE, Cost = 2, BaseAttack = 2, BaseVigor = 2 };
        s.Players[0].Hand.Add(card);
        var after = DuelEngine.Apply(s, new PlayCardAction { PlayerIndex = 0, CardInstanceId = card.InstanceId, Cost = 2, LaneIndex = 2 });
        var ev = after.Events.Single(e => e.Kind == FxKind.Summon);
        Assert.Equal("PLAY", ev.Tag); Assert.Equal(2, ev.TargetLane); Assert.Equal("tst_play", ev.SourceDefId);
    }

    [Fact]
    public void Ending_a_turn_announces_whose_turn_it_is_first()
    {
        var s = Fresh();
        var c = Place(s, 1, 0, 1, 5);
        c.Burn = 1;
        var after = DuelEngine.Apply(s, new EndTurnAction { PlayerIndex = 0 });
        var turn = after.Events.First(e => e.Kind == FxKind.TurnStart);
        Assert.Equal(1, turn.TargetSide);
        // the banner comes before that turn's start-of-turn effects (the burn tick)
        Assert.True(turn.Seq < after.Events.First(e => e.Kind == FxKind.Damage && e.Tag == "BURN").Seq);
    }

    [Fact]
    public void Burn_ticks_and_fatigue_are_damage_with_a_cause()
    {
        var s = Fresh();
        var c = Place(s, 1, 0, 1, 5);
        c.Burn = 2;
        s.Players[1].Deck.Clear();
        var after = DuelEngine.Apply(s, new EndTurnAction { PlayerIndex = 0 });
        Assert.Contains(after.Events, e => e.Kind == FxKind.Damage && e.Tag == "BURN" && e.TargetLane == 0 && e.Amount == 2);
        Assert.Contains(after.Events, e => e.Kind == FxKind.Damage && e.Tag == "FATIGUE" && e.TargetsFace);
    }
}

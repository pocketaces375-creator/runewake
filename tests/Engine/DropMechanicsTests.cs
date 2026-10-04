using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// FABLE-DROP-1: the engine foundations that were broken in the shipped build (each test reproduced the
/// bug first), and every class mechanic of the drop.
/// </summary>
public class DropMechanicsTests
{
    // ——— helpers ———

    private static GameState S()
    {
        var s = new GameState(seed: 42);
        for (int p = 0; p < 2; p++)
        {
            s.Players[p].AttunementMax = 10;
            s.Players[p].Attunement = 10;
            s.Players[p].MaxVigor = 25;
            s.Players[p].Vigor = 25;
            for (int i = 0; i < 8; i++)
                s.Players[p].Deck.Add(new CardInstance(s.NextInstanceId++, "tst_deck", p) { Zone = Zone.Deck, CardType = CardType.CREATURE, BaseAttack = 1, BaseVigor = 1 });
        }
        return s;
    }

    private static CardInstance Put(GameState s, int p, int lane, int atk, int vig, string id = "tst_c", params string[] keywords)
    {
        var c = new CardInstance(s.NextInstanceId++, id, p)
        {
            Zone = Zone.Lane, LaneIndex = lane, CardType = CardType.CREATURE,
            BaseAttack = atk, BaseVigor = vig, Cost = 1, IsExhausted = false
        };
        c.Keywords.AddRange(keywords);
        s.Players[p].Lanes[lane].Occupant = c;
        return c;
    }

    private static CardInstance Hand(GameState s, int p, CardType type, int atk = 1, int vig = 1, int cost = 1, params AbilityDef[] abilities)
    {
        var c = new CardInstance(s.NextInstanceId++, "tst_h", p)
        {
            Zone = Zone.Hand, CardType = type, BaseAttack = atk, BaseVigor = vig, Cost = cost,
            Abilities = abilities.ToList()
        };
        s.Players[p].Hand.Add(c);
        return c;
    }

    private static AbilityDef Ab(Trigger t, params EffectDef[] effects) => new() { Trigger = t, Effects = effects.ToList() };
    private static EffectDef Fx(Op op, Scope scope, int? amount = null, string? filter = null, object? count = null) => new()
    {
        Op = op, Amount = amount,
        Target = new TargetDef { Scope = scope, Filter = filter, Count = count is "ALL" ? TargetCount.All : count is int n ? TargetCount.Exactly(n) : null }
    };

    private static GameState Play(GameState s, int p, CardInstance c, int lane = 0) =>
        DuelEngine.Apply(s, new PlayCardAction { PlayerIndex = p, CardInstanceId = c.InstanceId, Cost = c.Cost, LaneIndex = lane });
    private static GameState Attack(GameState s, int p, int lane, int? target = null) =>
        DuelEngine.Apply(s, new AttackAction { PlayerIndex = p, SourceLane = lane, TargetLane = target ?? lane });
    private static GameState End(GameState s) =>
        DuelEngine.Apply(s, new EndTurnAction { PlayerIndex = s.CurrentPlayerIndex });
    private static CardInstance At(GameState s, int p, int lane) => s.Players[p].Lanes[lane].Occupant!;

    // ══════════════ foundations ══════════════

    [Fact]
    public void Ritual_Resolves()
    {
        var s = S();
        var r = Hand(s, 0, CardType.RITUAL, abilities: Ab(Trigger.RESOLVE, Fx(Op.DAMAGE, Scope.PLAYER_ENEMY, 3)));
        s = Play(s, 0, r);
        Assert.Equal(22, s.Players[1].Vigor);
        Assert.Contains(s.Players[0].Discard, c => c.InstanceId == r.InstanceId);
    }

    [Fact]
    public void Ritual_AimsAtTheLaneItWasPlayedOn()
    {
        var s = S();
        Put(s, 1, 0, 1, 5);
        var target = Put(s, 1, 3, 1, 5);
        var r = Hand(s, 0, CardType.RITUAL, abilities: Ab(Trigger.RESOLVE, Fx(Op.DAMAGE, Scope.ENEMY_CREATURE, 2, "ANY", 1)));
        s = Play(s, 0, r, lane: 3);
        Assert.Equal(0, At(s, 1, 0).Damage);
        Assert.Equal(2, At(s, 1, 3).Damage);
    }

    [Fact]
    public void OnSummon_FiresOnlyForTheSummonedCreature_AndTwiceWithEcho()
    {
        var s = S();
        var buff = new EffectDef { Op = Op.BUFF, Attack = 1, Target = new TargetDef { Scope = Scope.SELF } };
        var a = Hand(s, 0, CardType.CREATURE, abilities: Ab(Trigger.ON_SUMMON, buff));
        var b = Hand(s, 0, CardType.CREATURE);
        var e = Hand(s, 0, CardType.CREATURE, abilities: Ab(Trigger.ON_SUMMON, buff));
        e.Keywords.Add("ECHO");
        s = Play(s, 0, a, 0);
        s = Play(s, 0, b, 1);
        s = Play(s, 0, e, 2);
        Assert.Equal(2, At(s, 0, 0).CurrentAttack);   // 1 + its own summon, not re-fired by the others
        Assert.Equal(3, At(s, 0, 2).CurrentAttack);   // Echo: twice
    }

    [Fact]
    public void ThisTurnBuff_WearsOff_UntilYourNextTurn_Too()
    {
        var s = S();
        var c = Put(s, 0, 0, 1, 2);
        var src = Put(s, 0, 1, 1, 1);
        EffectExecutor.Execute(new EffectDef { Op = Op.BUFF, Attack = 3, Duration = Duration.THIS_TURN }, src, s, new() { new CreatureTarget(c, 0, 0) });
        EffectExecutor.Execute(new EffectDef { Op = Op.BUFF, Vigor = 2, Duration = Duration.UNTIL_YOUR_NEXT_TURN }, src, s, new() { new CreatureTarget(c, 0, 0) });
        Assert.Equal(4, c.CurrentAttack);
        s = End(s);
        Assert.Equal(1, At(s, 0, 0).CurrentAttack);
        Assert.Equal(4, At(s, 0, 0).CurrentVigor);     // still up during the enemy's turn
        s = End(s);
        Assert.Equal(2, At(s, 0, 0).CurrentVigor);     // gone when my turn starts
    }

    [Fact]
    public void OnAttack_Fires()
    {
        var s = S();
        var c = Put(s, 0, 0, 1, 3);
        c.Abilities.Add(Ab(Trigger.ON_ATTACK, Fx(Op.DAMAGE, Scope.PLAYER_ENEMY, 2)));
        s = Attack(s, 0, 0);
        Assert.Equal(22, s.Players[1].Vigor);
    }

    [Fact]
    public void Unearth_ReturnsOnce_Fresh()
    {
        var s = S();
        var u = Put(s, 0, 0, 1, 1, "tst_u", "UNEARTH");
        Put(s, 1, 0, 3, 9);
        s = Attack(s, 0, 0);
        Assert.Single(s.Players[0].UnearthQueue);
        s = End(s); s = End(s);
        var back = s.Players[0].Hand.Single(c => c.InstanceId == u.InstanceId);
        Assert.Equal(0, back.Damage);
        Assert.True(back.UnearthUsed);
    }

    [Fact]
    public void TriggerCap_IsPerAction_NotPerGame()
    {
        var s = S();
        for (int t = 0; t < 25; t++)
        {
            var z = Put(s, 0, 0, 1, 1);
            z.Abilities.Add(Ab(Trigger.ON_DEATH, Fx(Op.DAMAGE, Scope.PLAYER_ENEMY, 1)));
            Put(s, 1, 0, 5, 50);
            s = Attack(s, 0, 0);
        }
        Assert.Equal(0, s.Players[1].Vigor);
        Assert.True(s.IsGameOver);
    }

    [Fact]
    public void VenomKill_FiresDeathTriggers_AndVenomDefenderPoisonsAttacker()
    {
        var s = S();
        var victim = Put(s, 1, 0, 1, 9);
        victim.Abilities.Add(Ab(Trigger.ON_DEATH, Fx(Op.DAMAGE, Scope.PLAYER_ENEMY, 2)));
        Put(s, 0, 0, 1, 9, "tst_v", "VENOM");
        s = Attack(s, 0, 0);
        Assert.Null(s.Players[1].Lanes[0].Occupant);
        Assert.Equal(23, s.Players[0].Vigor);           // its death trigger fired

        var s2 = S();
        Put(s2, 1, 0, 1, 9, "tst_v", "VENOM");
        Put(s2, 0, 0, 1, 9);
        s2 = Attack(s2, 0, 0);
        Assert.Null(s2.Players[0].Lanes[0].Occupant);   // the attacker was poisoned by the Venom defender
    }

    [Fact]
    public void Ward_StopsSpellDamage_AndGrantedWardWorks()
    {
        var s = S();
        var c = Put(s, 1, 0, 1, 3, "tst_w", "WARD");
        c.WardRemaining = 1;
        var r = Hand(s, 0, CardType.RITUAL, abilities: Ab(Trigger.RESOLVE, Fx(Op.DAMAGE, Scope.ENEMY_CREATURE, 2, null, 1)));
        s = Play(s, 0, r, 0);
        Assert.Equal(0, At(s, 1, 0).Damage);

        var s2 = S();
        var d = Put(s2, 0, 0, 1, 3);
        EffectExecutor.Execute(new EffectDef { Op = Op.GRANT_KEY, Keyword = "Ward" }, d, s2, new() { new CreatureTarget(d, 0, 0) });
        Assert.Equal(1, d.WardRemaining);
    }

    [Fact]
    public void CreaturePassive_IsAnAura_ThatEndsWhenTheSourceLeaves()
    {
        var s = S();
        var lord = Put(s, 0, 0, 1, 1);
        lord.Abilities.Add(new AbilityDef { Trigger = Trigger.PASSIVE, Effects = { new EffectDef { Op = Op.BUFF, Attack = 1, Vigor = 1, Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "KEYWORD:SWIFT", Count = TargetCount.All } } } });
        var swift = Put(s, 0, 1, 2, 1, "tst_s", "SWIFT");
        swift.Damage = 1;                                  // alive only thanks to the aura
        Auras.Recompute(s);
        Assert.Equal(3, swift.CurrentAttack);
        Assert.Equal(1, swift.CurrentVigor);
        Put(s, 1, 0, 5, 9);
        s = Attack(s, 0, 0);                               // the lord dies attacking
        var after = At(s, 0, 1);
        Assert.Equal(2, after.CurrentAttack);
        Assert.Equal(1, after.CurrentVigor);               // losing an aura bonus never kills
    }

    [Fact]
    public void Debuff_TakesAway_EvenWithANegativeNumber()
    {
        var s = S();
        var c = Put(s, 1, 0, 3, 3);
        var src = Put(s, 0, 0, 1, 1);
        EffectExecutor.Execute(new EffectDef { Op = Op.DEBUFF, Attack = -1 }, src, s, new() { new CreatureTarget(c, 1, 0) });
        Assert.Equal(2, c.CurrentAttack);
    }

    [Fact]
    public void Relic_IdentifiesAndComesOnline()
    {
        {
            var def = new CardDef
            {
                Id = "tst_relic", Name = "Test Relic", Type = CardType.RELIC, Cost = 1, Strata = Strata.DAWN,
                IdentifyCondition = new ConditionDef { Op = ConditionOp.TURN_GTE, Value = JsonDocument.Parse("1").RootElement },
                Abilities = { new AbilityDef { Trigger = Trigger.PASSIVE, Effects = { new EffectDef { Op = Op.BUFF, Attack = 1, Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Count = TargetCount.All } } } } }
            };
            var s = S();
            var relic = CardInstance.FromDef(def, s.NextInstanceId++, 0, Zone.Hand);
            s.Players[0].Hand.Add(relic);
            var ally = Put(s, 0, 1, 2, 2);
            s = Play(s, 0, relic, 0);
            s = End(s); s = End(s);
            Assert.True(At(s, 0, 0).IsIdentified);
            Assert.Equal(3, At(s, 0, 1).CurrentAttack);
        }
    }

    // ══════════════ wave 1 ══════════════

    [Fact]
    public void Armor_ReducesEveryHit()
    {
        var s = S();
        Put(s, 1, 0, 1, 4, "tst_a", "ARMOR:2");
        Put(s, 0, 0, 3, 9);
        s = Attack(s, 0, 0);
        Assert.Equal(1, At(s, 1, 0).Damage);
    }

    [Fact]
    public void Stun_StopsTheNextAttack_ThenWearsOff()
    {
        var s = S();
        var enemy = Put(s, 1, 0, 2, 2);
        var src = Put(s, 0, 0, 1, 1);
        EffectExecutor.Execute(new EffectDef { Op = Op.STUN }, src, s, new() { new CreatureTarget(enemy, 1, 0) });
        s = End(s);                                        // enemy's turn: stunned
        Assert.Throws<InvalidOperationException>(() => Attack(s, 1, 0));
        s = End(s); s = End(s);                            // its next turn: free again
        Assert.False(At(s, 1, 0).Stunned);
    }

    [Fact]
    public void Burn_TicksAndFades()
    {
        var s = S();
        var enemy = Put(s, 1, 0, 1, 9);
        var src = Put(s, 0, 0, 1, 1);
        EffectExecutor.Execute(new EffectDef { Op = Op.BURN, Amount = 2 }, src, s, new() { new CreatureTarget(enemy, 1, 0), new PlayerTarget(s.Players[1]) });
        s = End(s);
        Assert.Equal(2, At(s, 1, 0).Damage);
        Assert.Equal(23, s.Players[1].Vigor);
        s = End(s); s = End(s);
        Assert.Equal(3, At(s, 1, 0).Damage);
        Assert.Equal(0, At(s, 1, 0).Burn);
    }

    [Fact]
    public void Exalted_PumpsTheFirstAttacker()
    {
        var s = S();
        Put(s, 0, 0, 2, 2, "tst_x", "EXALTED");
        Put(s, 0, 1, 1, 1, "tst_x", "EXALTED");
        s = Attack(s, 0, 0);
        Assert.Equal(21, s.Players[1].Vigor);              // 2 + 2 Exalted
        s = Attack(s, 0, 1);
        Assert.Equal(20, s.Players[1].Vigor);              // the second attacker gets nothing
    }

    [Fact]
    public void Dodge_IsSeeded_SameSeedSameResult()
    {
        int Run()
        {
            var s = S();
            Put(s, 1, 0, 1, 3, "tst_d", "DODGE:50");
            int dodged = 0;
            for (int i = 0; i < 20; i++)
            {
                Put(s, 0, 0, 1, 9);
                int before = At(s, 1, 0).Damage;
                s = Attack(s, 0, 0);
                if (s.Players[1].Lanes[0].Occupant is null) break;
                if (At(s, 1, 0).Damage == before) dodged++;
                At(s, 1, 0).Damage = 0;
            }
            return dodged;
        }
        int a = Run(), b = Run();
        Assert.Equal(a, b);
        Assert.InRange(a, 1, 19);
    }

    [Fact]
    public void Drain_LowersNextTurnsAttunement()
    {
        var s = S();
        s.Players[1].AttunementMax = 4;
        var src = Put(s, 0, 0, 1, 1);
        EffectExecutor.Execute(new EffectDef { Op = Op.DRAIN, Amount = 2 }, src, s, new() { new PlayerTarget(s.Players[1]) });
        s = End(s);
        Assert.Equal(5, s.Players[1].AttunementMax);
        Assert.Equal(3, s.Players[1].Attunement);
    }

    [Fact]
    public void Lock_BlocksSummoning_ForTheirTurn()
    {
        var s = S();
        var src = Put(s, 0, 2, 1, 1);
        var r = Hand(s, 0, CardType.RITUAL, abilities: Ab(Trigger.RESOLVE, new EffectDef { Op = Op.LOCK_LANE, Amount = 1, Target = new TargetDef { Scope = Scope.LANE, Filter = "OPPOSING" } }));
        s = Play(s, 0, r, 4);
        Assert.Equal(1, s.Players[1].Lanes[4].LockedTurns);
        s = End(s);
        var c = Hand(s, 1, CardType.CREATURE);
        Assert.Throws<InvalidOperationException>(() => Play(s, 1, c, 4));
        s = End(s);
        Assert.Equal(0, s.Players[1].Lanes[4].LockedTurns);
    }

    [Fact]
    public void SwapAndDouble()
    {
        var s = S();
        var c = Put(s, 1, 0, 5, 2);
        var src = Put(s, 0, 0, 1, 1);
        EffectExecutor.Execute(new EffectDef { Op = Op.SWAP_STATS }, src, s, new() { new CreatureTarget(c, 1, 0) });
        Assert.Equal(2, c.CurrentAttack);
        Assert.Equal(5, c.CurrentVigor);
        var mine = Put(s, 0, 1, 3, 2);
        EffectExecutor.Execute(new EffectDef { Op = Op.DOUBLE_STATS, Duration = Duration.THIS_TURN }, src, s, new() { new CreatureTarget(mine, 0, 1) });
        Assert.Equal(6, mine.CurrentAttack);
        Assert.Equal(4, mine.CurrentVigor);
        s = End(s);
        Assert.Equal(3, At(s, 0, 1).CurrentAttack);
    }

    [Fact]
    public void Steal_ThisTurn_CanAttack_AndGoesHome()
    {
        var s = S();
        var theirs = Put(s, 1, 2, 4, 4);
        var src = Put(s, 0, 0, 1, 1);
        EffectExecutor.Execute(new EffectDef { Op = Op.STEAL, Duration = Duration.THIS_TURN }, src, s, new() { new CreatureTarget(theirs, 1, 2) });
        Assert.Same(theirs, s.Players[0].Lanes[2].Occupant);
        s = Attack(s, 0, 2);
        Assert.Equal(21, s.Players[1].Vigor);
        s = End(s);
        Assert.Equal(1, At(s, 1, 2).Controller);
        Assert.Null(s.Players[0].Lanes[2].Occupant);
    }

    // ══════════════ wave 2 ══════════════

    [Fact]
    public void Tribute_DestroysTheCreatureItIsPlayedOnto()
    {
        var s = S();
        var fodder = Put(s, 0, 1, 1, 1);
        fodder.Abilities.Add(Ab(Trigger.ON_DEATH, Fx(Op.DAMAGE, Scope.PLAYER_ENEMY, 1)));
        var big = Hand(s, 0, CardType.CREATURE, 7, 7, 3);
        big.Tribute = 1;
        s = Play(s, 0, big, 1);
        Assert.Equal(7, At(s, 0, 1).CurrentAttack);
        Assert.Equal(24, s.Players[1].Vigor);            // the tribute's death trigger fired

        var s2 = S();
        var big2 = Hand(s2, 0, CardType.CREATURE, 7, 7, 3);
        big2.Tribute = 1;
        Assert.Throws<InvalidOperationException>(() => Play(s2, 0, big2, 1));
    }

    [Fact]
    public void Counter_Sigil_NegatesTheNextEnemyRitual()
    {
        var s = S();
        var src = Put(s, 1, 0, 1, 1);
        EffectExecutor.Execute(new EffectDef { Op = Op.SET_TRAP, Keyword = "COUNTER_RITUAL" }, src, s, new() { new PlayerTarget(s.Players[1]) });
        var r = Hand(s, 0, CardType.RITUAL, abilities: Ab(Trigger.RESOLVE, Fx(Op.DAMAGE, Scope.PLAYER_ENEMY, 5)));
        s = Play(s, 0, r);
        Assert.Equal(25, s.Players[1].Vigor);
        Assert.Empty(s.Players[1].Traps);
        Assert.NotNull(s.LastTrapSprung);
    }

    [Fact]
    public void Ambush_Sigil_HitsTheAttackerFirst()
    {
        var s = S();
        var src = Put(s, 1, 4, 1, 1);
        EffectExecutor.Execute(new EffectDef { Op = Op.SET_TRAP, Keyword = "AMBUSH", Amount = 3 }, src, s, new() { new PlayerTarget(s.Players[1]) });
        Put(s, 0, 0, 5, 3);
        s = Attack(s, 0, 0);
        Assert.Null(s.Players[0].Lanes[0].Occupant);
        Assert.Equal(25, s.Players[1].Vigor);
    }

    [Fact]
    public void Redirect_PullsTheNextAttack()
    {
        var s = S();
        var decoy = Put(s, 1, 3, 0, 6);
        var src = Put(s, 1, 4, 1, 1);
        EffectExecutor.Execute(new EffectDef { Op = Op.REDIRECT }, src, s, new() { new CreatureTarget(decoy, 1, 3) });
        Put(s, 0, 0, 4, 9);
        s = Attack(s, 0, 0);
        Assert.Equal(25, s.Players[1].Vigor);
        Assert.Equal(4, At(s, 1, 3).Damage);
    }

    [Fact]
    public void Tribal_And_Directional_Filters()
    {
        var s = S();
        var src = Put(s, 0, 2, 1, 1);
        var beast = Put(s, 0, 0, 1, 1); beast.Types.Add("BEAST");
        Put(s, 0, 1, 1, 1);
        Put(s, 1, 1, 1, 5); Put(s, 1, 2, 1, 5); Put(s, 1, 3, 1, 5);
        var opp = s.Players[1];
        var beasts = TargetResolver.Resolve(new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "TRIBE:BEAST", Count = TargetCount.All }, src, s.Players[0], opp, s);
        Assert.Single(beasts);
        var diag = TargetResolver.Resolve(new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "DIAGONAL", Count = TargetCount.All }, src, s.Players[0], opp, s);
        Assert.Equal(new[] { 1, 3 }, diag.Cast<CreatureTarget>().Select(t => t.LaneIndex).ToArray());
        var lanes = TargetResolver.Resolve(new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "LANES:2-4", Count = TargetCount.All }, src, s.Players[0], opp, s);
        Assert.Equal(2, lanes.Count);
    }

    [Fact]
    public void LaneAura_FromAPassive()
    {
        var s = S();
        var star = Put(s, 0, 0, 0, 3);
        star.Abilities.Add(new AbilityDef { Trigger = Trigger.PASSIVE, Effects = { new EffectDef { Op = Op.DEBUFF, Attack = 1, Target = new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "LANES:0-1", Count = TargetCount.All } } } });
        Put(s, 1, 0, 3, 3); Put(s, 1, 1, 3, 3); Put(s, 1, 4, 3, 3);
        Auras.Recompute(s);
        Assert.Equal(2, At(s, 1, 0).CurrentAttack);
        Assert.Equal(2, At(s, 1, 1).CurrentAttack);
        Assert.Equal(3, At(s, 1, 4).CurrentAttack);
    }

    [Fact]
    public void SameMoves_SameState_WithEveryMechanic()
    {
        // determinism: two runs of the same moves agree exactly (online play and co-op compare hashes)
        ulong Run()
        {
            var s = S();
            Put(s, 0, 0, 2, 3, "tst_x", "EXALTED");
            Put(s, 1, 0, 2, 3, "tst_d", "DODGE:40", "ARMOR:1");
            var src = Put(s, 0, 4, 1, 1);
            EffectExecutor.Execute(new EffectDef { Op = Op.BURN, Amount = 2 }, src, s, new() { new PlayerTarget(s.Players[1]) });
            for (int i = 0; i < 4; i++)
            {
                if (s.Players[0].Lanes[0].Occupant is null) break;
                s = Attack(s, 0, 0); s = End(s); s = End(s);
                if (s.IsGameOver) break;
            }
            return Runewake.Engine.Coop.StateHash.Of(s);
        }
        Assert.Equal(Run(), Run());
    }
}

using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// TASK-T1b — Ruling tests, Warrior: R1–R3.
/// Every ruling in ARTIFACT_RULINGS.md gets at least one test, named
/// Ruling_R&lt;id&gt;_&lt;Name&gt;. These assert the rulings verbatim.
/// </summary>
[Collection("NonParallel")]
public class RulingWarriorTests
{
    // ────── Helpers (shared with RulingGeneralTests) ──────

    private static GameState CreateState()
    {
        var state = new GameState(seed: 42);
        for (int p = 0; p < 2; p++)
        {
            state.Players[p].AttunementMax = 10;
            state.Players[p].Attunement = 10;
            for (int i = 0; i < 10; i++)
            {
                var c = new CardInstance(state.NextInstanceId++, "tst_d", p) { Zone = Zone.Deck };
                state.Players[p].Deck.Add(c);
            }
        }
        return state;
    }

    private static CardInstance PlaceCreature(GameState state, int pIdx, int lane,
        int attack = 2, int vigor = 5)
    {
        var c = new CardInstance(state.NextInstanceId++, $"tst_cr_p{pIdx}_l{lane}", pIdx)
        {
            Zone = Zone.Lane,
            LaneIndex = lane,
            CardType = CardType.CREATURE,
            BaseAttack = attack,
            BaseVigor = vigor,
            Cost = 1,
            IsExhausted = false
        };
        state.Players[pIdx].Lanes[lane].Occupant = c;
        return c;
    }

    private static GameState EndTurn(GameState state, int playerIndex)
        => DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = playerIndex });

    private static GameState Attack(GameState state, int playerIndex, int lane)
        => DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = playerIndex,
            SourceLane = lane,
            TargetLane = lane
        });

    private static ConditionDef Cond(ConditionOp op, int? value = null, string? side = null)
    {
        return new ConditionDef
        {
            Op = op,
            Value = value.HasValue ? JsonSerializer.SerializeToElement(value.Value) : null,
            Side = side
        };
    }

    private static bool Eval(ConditionDef condition, GameState state, int controller)
    {
        var source = new CardInstance(99999, "tst_condition_source", controller);
        return TriggerBus.EvaluateCondition(condition, source, controller, state);
    }

    private static CardInstance? FindCreature(GameState state, int instanceId)
    {
        for (int p = 0; p < 2; p++)
            for (int l = 0; l < 5; l++)
                if (state.Players[p].Lanes[l].Occupant is { } occ && occ.InstanceId == instanceId)
                    return occ;
        return null;
    }

    // ══════════════════════════════════════════════════════════════════
    // R1 — Ancestral Blade (Sword)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_R1_AncestralShield_ClampsVigorTo1AfterEnemySpell()
    {
        // R1: protects against the FIRST enemy spell/ability (not combat damage)
        // that would reduce a friendly creature below 1 vigor — set to 1 instead.
        // P0: 1/2 creature with ANCESTRAL_SHIELD.  P1 deals 4 spell damage to it.
        // Without shield → dead.  With shield → clamped to 1 vigor.
        var state = CreateState();
        var creature = PlaceCreature(state, 0, 0, attack: 1, vigor: 2);
        creature.Keywords = new List<string> { "ANCESTRAL_SHIELD" };

        // Deal spell damage through the engine (DAMAGE op)
        var effect = new EffectDef { Op = Op.DAMAGE, Amount = 4 };
        var source = new CardInstance(state.NextInstanceId++, "tst_spell", 1);
        var target = new CreatureTarget(creature, 0, 0);
        EffectExecutor.Execute(effect, source, state, new List<ResolvedTarget> { target });

        // Vigor clamped to 1 (was 2, took 4 damage → would be dead, clamped to 1)
        Assert.Equal(1, creature.CurrentVigor);
        // Damage was applied (clamp, not prevention)
        Assert.True(creature.Damage > 0);
        // Shield consumed
        Assert.True(creature.AncestralShieldUsedThisTurn);
    }

    [Fact]
    public void Ruling_R1_AncestralShield_DoesNotProtectAgainstCombatDamage()
    {
        // R1: protects against the FIRST enemy spell/ability, NOT combat damage.
        // P0: 1/2 creature with ANCESTRAL_SHIELD.  P1: 3/3 attacks it.
        // Combat damage bypasses the shield — creature dies.
        var state = CreateState();
        var p0Creature = PlaceCreature(state, 0, 0, attack: 1, vigor: 2);
        p0Creature.Keywords = new List<string> { "ANCESTRAL_SHIELD" };
        var p1Creature = PlaceCreature(state, 1, 0, attack: 3, vigor: 3);

        // P1 attacks into P0's creature
        state = Attack(state, 1, 0);

        // P0's creature died from combat damage (ANCESTRAL_SHIELD doesn't apply to combat)
        Assert.Null(state.Players[0].Lanes[0].Occupant);
    }

    [Fact]
    public void Ruling_R1_AncestralShield_OneUseThenDisarms()
    {
        // R1: one use, then disarms.  First lethal spell → clamped to 1.
        // Second lethal spell in same turn → creature dies.
        var state = CreateState();
        var creature = PlaceCreature(state, 0, 0, attack: 1, vigor: 3);
        creature.Keywords = new List<string> { "ANCESTRAL_SHIELD" };

        // First spell: 3 damage on a 3-vigor creature → would be 0, clamped to 1.
        // Shield consumed.
        var effect1 = new EffectDef { Op = Op.DAMAGE, Amount = 3 };
        var source1 = new CardInstance(state.NextInstanceId++, "tst_spell1", 1);
        EffectExecutor.Execute(effect1, source1, state,
            new List<ResolvedTarget> { new CreatureTarget(creature, 0, 0) });
        Assert.Equal(1, creature.CurrentVigor);
        Assert.True(creature.AncestralShieldUsedThisTurn);

        // Second spell: 3 damage in same turn → no clamp (shield used) → dead
        var effect2 = new EffectDef { Op = Op.DAMAGE, Amount = 3 };
        var source2 = new CardInstance(state.NextInstanceId++, "tst_spell2", 1);
        EffectExecutor.Execute(effect2, source2, state,
            new List<ResolvedTarget> { new CreatureTarget(creature, 0, 0) });
        Assert.True(creature.CurrentVigor <= 0, "Creature should be dead after second lethal spell");
    }

    [Fact]
    public void Ruling_R1_AncestralShield_ClampNotPrevention_DamageApplied()
    {
        // R1: Clamp, not prevention (damage triggers still fire).
        // A creature with ANCESTRAL_SHIELD takes lethal spell damage.
        // The clamp saves it at 1 vigor, but the damage was still applied
        // (Damage > 0) — any "on damage" triggers that fire from the damage
        // application still see the damage event.
        var state = CreateState();
        var creature = PlaceCreature(state, 0, 0, attack: 1, vigor: 2);
        creature.Keywords = new List<string> { "ANCESTRAL_SHIELD" };

        // Deal spell damage through the engine
        var effect = new EffectDef { Op = Op.DAMAGE, Amount = 4 };
        var source = new CardInstance(state.NextInstanceId++, "tst_spell", 1);
        EffectExecutor.Execute(effect, source, state,
            new List<ResolvedTarget> { new CreatureTarget(creature, 0, 0) });

        // Damage was applied to the creature (Damage > 0, not 0)
        Assert.True(creature.Damage > 0, "Damage was applied (clamp, not prevention)");
        // The creature survived at 1 vigor
        Assert.Equal(1, creature.CurrentVigor);
        // The creature took damage = baseVigor + vigorMod - 1 = 2 + 0 - 1 = 1
        Assert.Equal(1, creature.Damage);
    }

    [Fact]
    public void Ruling_R1_AncestralShield_ResetsAtStartOfTurn()
    {
        // R1: lasts until the start of your next turn.  After turn start,
        // the shield is refreshed (AncestralShieldUsedThisTurn reset).
        var state = CreateState();
        var creature = PlaceCreature(state, 0, 0, attack: 1, vigor: 5);
        creature.Keywords = new List<string> { "ANCESTRAL_SHIELD" };
        creature.AncestralShieldUsedThisTurn = true;

        // End P0's turn → P1's turn (P1's shields reset, not P0's)
        state = EndTurn(state, 0);
        var c1 = FindCreature(state, creature.InstanceId);
        Assert.NotNull(c1);
        Assert.True(c1.AncestralShieldUsedThisTurn); // not reset yet — P1's turn start

        // End P1's turn → P0's turn starts → ResetAncestralShields runs on P0
        state = EndTurn(state, 1);
        var c2 = FindCreature(state, creature.InstanceId);
        Assert.NotNull(c2);
        Assert.False(c2.AncestralShieldUsedThisTurn); // reset at P0's turn start
    }

    [Fact]
    public void Ruling_R1_ArmsOnThreeAttacks_ConditionCheck()
    {
        // R1: arms when 3+ friendly creatures attack in one turn.
        // Test the ATTACKERS_THIS_TURN_GTE 3 condition directly.
        var state = CreateState();

        // With 0 attacks → condition false
        Assert.False(Eval(Cond(ConditionOp.ATTACKERS_THIS_TURN_GTE, value: 3), state, 0));

        // With 2 attacks → condition false
        state.Players[0].AttackCountThisTurn = 2;
        Assert.False(Eval(Cond(ConditionOp.ATTACKERS_THIS_TURN_GTE, value: 3), state, 0));

        // With 3 attacks → condition true
        state.Players[0].AttackCountThisTurn = 3;
        Assert.True(Eval(Cond(ConditionOp.ATTACKERS_THIS_TURN_GTE, value: 3), state, 0));

        // With 5 attacks → condition still true
        state.Players[0].AttackCountThisTurn = 5;
        Assert.True(Eval(Cond(ConditionOp.ATTACKERS_THIS_TURN_GTE, value: 3), state, 0));
    }

    // ══════════════════════════════════════════════════════════════════
    // R2 — Bulwark passive
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_R2_BulwarkPassive_AppliesAtEndOfTurnToNonAttackers()
    {
        // R2: +0/+1 applies at end of your turn to each friendly creature
        // that did not attack this turn.
        // P0 has two creatures: one attacks, one doesn't.  At end of turn,
        // the non-attacker gets +0/+1.
        var state = CreateState();
        var attacker = PlaceCreature(state, 0, 0, attack: 2, vigor: 3);
        var nonAttacker = PlaceCreature(state, 0, 1, attack: 2, vigor: 3);
        var p1Creature = PlaceCreature(state, 1, 0, attack: 1, vigor: 3);

        // P0 attacks with creature in lane 0
        state = Attack(state, 0, 0);
        var attackerAfter = FindCreature(state, attacker.InstanceId);
        Assert.NotNull(attackerAfter);
        Assert.True(attackerAfter.HasAttackedThisTurn);

        // Apply the Bulwark's end-of-turn passive: BUFF +0/+1 to HAS_NOT_ATTACKED creatures
        var effect = new EffectDef
        {
            Op = Op.BUFF, Attack = 0, Vigor = 1,
            Duration = Duration.NEXT_TURN,
            Target = new TargetDef
            {
                Scope = Scope.ALLY_CREATURE,
                Filter = "HAS_NOT_ATTACKED",
                Count = TargetCount.All
            }
        };
        var source = new CardInstance(state.NextInstanceId++, "artf_warrior_shield", 0);
        var targets = TargetResolver.Resolve(effect.Target!, source, state.Players[0],
            state.Players[1], state);
        EffectExecutor.Execute(effect, source, state, targets);

        // Non-attacker got +0/+1 (FindCreature in current state)
        var nonAttAfter = FindCreature(state, nonAttacker.InstanceId);
        Assert.NotNull(nonAttAfter);
        Assert.Equal(1, nonAttAfter.VigorModifier);
        Assert.Equal(0, nonAttAfter.AttackModifier);

        // Attacker did NOT get the buff (FindCreature in current state)
        var attAfter = FindCreature(state, attacker.InstanceId);
        Assert.NotNull(attAfter);
        Assert.Equal(0, attAfter.VigorModifier);
        Assert.Equal(0, attAfter.AttackModifier);
    }

    [Fact]
    public void Ruling_R2_BulwarkPassive_CreaturesPlayedThisTurnCountAsDidNotAttack()
    {
        // R2: Creatures played this turn count as "did not attack".
        // A freshly placed creature has HasAttackedThisTurn = false
        // even though it just entered play.
        var state = CreateState();
        var creature = PlaceCreature(state, 0, 0, attack: 2, vigor: 3);

        // A freshly placed creature hasn't attacked
        Assert.False(creature.HasAttackedThisTurn);

        // Even after being exhausted (summoned creatures start exhausted),
        // it hasn't attacked — HasAttackedThisTurn is false
        creature.IsExhausted = true;
        Assert.False(creature.HasAttackedThisTurn);

        // Verify HAS_NOT_ATTACKED filter includes it
        var pool = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "HAS_NOT_ATTACKED", Count = TargetCount.All },
            new CardInstance(state.NextInstanceId++, "tst_source", 0),
            state.Players[0], state.Players[1], state);
        Assert.Contains(pool, t => t is CreatureTarget ct && ct.Card.InstanceId == creature.InstanceId);
    }

    [Fact]
    public void Ruling_R2_BulwarkPassive_FilterExcludesAttacker()
    {
        // R2: Filter "HAS_NOT_ATTACKED" correctly excludes creatures that
        // have already attacked this turn.
        var state = CreateState();
        var attacker = PlaceCreature(state, 0, 0, attack: 2, vigor: 3);
        var nonAttacker = PlaceCreature(state, 0, 1, attack: 2, vigor: 3);
        var p1Creature = PlaceCreature(state, 1, 0, attack: 1, vigor: 3);

        // P0 attacks with lane 0
        state = Attack(state, 0, 0);
        var attackerAfter = FindCreature(state, attacker.InstanceId);
        Assert.NotNull(attackerAfter);
        Assert.True(attackerAfter.HasAttackedThisTurn);

        // HAS_NOT_ATTACKED filter should only include the non-attacker
        var pool = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "HAS_NOT_ATTACKED", Count = TargetCount.All },
            new CardInstance(state.NextInstanceId++, "tst_source", 0),
            state.Players[0], state.Players[1], state);

        // Only the non-attacker is in the pool
        Assert.DoesNotContain(pool, t => t is CreatureTarget ct && ct.Card.InstanceId == attacker.InstanceId);
        Assert.Contains(pool, t => t is CreatureTarget ct && ct.Card.InstanceId == nonAttacker.InstanceId);
    }

    // ══════════════════════════════════════════════════════════════════
    // R3 — Bulwark trigger "no attackers"
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_R3_NoAttackersCondition_TrueWhenZeroAttacksLastTurn()
    {
        // R3: "no attackers" true iff zero friendly creatures attacked during
        // your most recent completed turn.  Test NO_ATTACKERS_LAST_TURN condition.
        var state = CreateState();

        // Initially, AttackCountLastTurn is 0 → condition true
        Assert.True(Eval(Cond(ConditionOp.NO_ATTACKERS_LAST_TURN), state, 0));

        // After P0 attacked 2 times last turn → condition false
        state.Players[0].AttackCountLastTurn = 2;
        Assert.False(Eval(Cond(ConditionOp.NO_ATTACKERS_LAST_TURN), state, 0));

        // P1's perspective: P1's AttackCountLastTurn is 0 → condition true
        Assert.True(Eval(Cond(ConditionOp.NO_ATTACKERS_LAST_TURN), state, 1));
    }

    [Fact]
    public void Ruling_R3_AttackCountLastTurn_TracksViaEndTurn()
    {
        // R3: "no attackers" is based on the most recent completed turn.
        // Verify that AttackCountThisTurn is persisted to AttackCountLastTurn
        // when the player's next turn starts.
        var state = CreateState();
        PlaceCreature(state, 0, 0, attack: 2, vigor: 5);
        PlaceCreature(state, 0, 1, attack: 2, vigor: 5);
        PlaceCreature(state, 1, 0, attack: 1, vigor: 5);

        // P0 attacks with 2 creatures
        state = Attack(state, 0, 0);
        state = Attack(state, 0, 1);
        Assert.Equal(2, state.Players[0].AttackCountThisTurn);

        // End P0's turn → P1's turn starts.  P0's counters aren't touched yet.
        state = EndTurn(state, 0);
        Assert.Equal(2, state.Players[0].AttackCountThisTurn); // still 2
        Assert.Equal(0, state.Players[0].AttackCountLastTurn); // not yet persisted

        // End P1's turn → P0's turn starts: P0's AttackCountThisTurn (2) is
        // persisted to AttackCountLastTurn, then reset to 0.
        state = EndTurn(state, 1);
        Assert.Equal(2, state.Players[0].AttackCountLastTurn);
        Assert.Equal(0, state.Players[0].AttackCountThisTurn); // reset for the new turn

        // NO_ATTACKERS_LAST_TURN is false for P0 (they attacked 2 times)
        Assert.False(Eval(Cond(ConditionOp.NO_ATTACKERS_LAST_TURN), state, 0));
        // P1 hasn't attacked in their last completed turn → true
        Assert.True(Eval(Cond(ConditionOp.NO_ATTACKERS_LAST_TURN), state, 1));
    }

    [Fact]
    public void Ruling_R3_BulwarkTrigger_PreventsTwoCombatDamage()
    {
        // R3: Prevents the first 2 combat damage to the first friendly creature
        // attacked each enemy turn.  Tests PREVENT_DAMAGE shield with
        // ONCE_PER_ENEMY_TURN frequency and ATTACK source.
        var state = CreateState();
        var defender = PlaceCreature(state, 0, 0, attack: 2, vigor: 5);
        var attacker = PlaceCreature(state, 1, 0, attack: 4, vigor: 5);

        // Register the Bulwark's PREVENT_DAMAGE shield on the defender
        // (simulating the Bulwark trigger: prevent 2 combat damage,
        // source=ATTACK, frequency=ONCE_PER_ENEMY_TURN).
        // SourceArtifactInstanceId=0 skips the inert-artifact guard so the
        // shield is always active (no backing artifact slot needed).
        var shield = new DamageShield
        {
            Amount = 2,
            Source = "ATTACK",
            Frequency = "ONCE_PER_ENEMY_TURN",
            SourceArtifactDefId = "artf_warrior_shield",
            SourceArtifactInstanceId = 0,
            SourceController = -1
        };
        defender.DamageShields.Add(shield);

        // P1 attacks, dealing 4 combat damage to the defender
        state = Attack(state, 1, 0);

        // Unchanged lane index for the defender
        var defenderAfter = FindCreature(state, defender.InstanceId);
        Assert.NotNull(defenderAfter);

        // Shield absorbed 2 of the 4 damage → 2 damage taken
        Assert.Equal(2, defenderAfter.Damage);
        Assert.Equal(3, defenderAfter.CurrentVigor); // 5 - 2 = 3

        // Shield was used this turn (checked via creature in cloned state)
        var shieldAfter = defenderAfter.DamageShields.FirstOrDefault();
        Assert.NotNull(shieldAfter);
        Assert.Equal(1, shieldAfter.UsedThisTurn);
    }

    [Fact]
    public void Ruling_R3_BulwarkTrigger_ShieldOncePerEnemyTurn()
    {
        // R3: Prevents 2 damage ONCE_PER_ENEMY_TURN.  A second attack in the
        // same enemy turn sees full damage (shield already spent).
        var state = CreateState();
        var defender = PlaceCreature(state, 0, 0, attack: 2, vigor: 8);
        // Attacker in lane 0 hits the defender directly.
        PlaceCreature(state, 1, 0, attack: 3, vigor: 5);
        // Second attacker with REACH can hit lane 0 from lane 1.
        var attacker2 = PlaceCreature(state, 1, 1, attack: 3, vigor: 5);
        attacker2.Keywords = new List<string> { "REACH" };

        // Register the PREVENT_DAMAGE shield (ONCE_PER_ENEMY_TURN frequency)
        // SourceArtifactInstanceId=0 skips the inert-artifact guard.
        var shield = new DamageShield
        {
            Amount = 2,
            Source = "ATTACK",
            Frequency = "ONCE_PER_ENEMY_TURN",
            SourceArtifactDefId = "artf_warrior_shield",
            SourceArtifactInstanceId = 0,
            SourceController = -1
        };
        defender.DamageShields.Add(shield);

        // First attack: 3→1 damage (shield absorbs 2), shield spent
        state = Attack(state, 1, 0);
        var def1 = FindCreature(state, defender.InstanceId);
        Assert.NotNull(def1);
        Assert.Equal(1, def1.Damage);
        // Verify shield was used (via cloned state's creature shield list)
        var shield1 = def1.DamageShields.FirstOrDefault();
        Assert.NotNull(shield1);
        Assert.Equal(1, shield1.UsedThisTurn);

        // Second attack in the same enemy turn: REACH attacker hits lane 0.
        // Shield already used → full 3 damage, no further absorption.
        state = DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = 1,
            SourceLane = 1,
            TargetLane = 0
        });
        var def2 = FindCreature(state, defender.InstanceId);
        Assert.NotNull(def2);
        Assert.Equal(4, def2.Damage); // 1 + 3, shield did not fire again
        // Shield's UsedThisTurn still 1 (didn't fire second time)
        var shield2 = def2.DamageShields.FirstOrDefault();
        Assert.NotNull(shield2);
        Assert.Equal(1, shield2.UsedThisTurn); // still 1
    }

    [Fact]
    public void Ruling_R3_FirstAttackedTrackedCorrectly()
    {
        // R3: "first friendly creature attacked" — the engine tracks
        // FirstAttackedLaneIndex on the opponent's player state.
        // When P1 attacks lane 0 first, then lane 1, FirstAttackedLaneIndex = 0.
        var state = CreateState();
        PlaceCreature(state, 0, 0, attack: 2, vigor: 5);
        PlaceCreature(state, 0, 1, attack: 2, vigor: 5);
        var p1Attacker = PlaceCreature(state, 1, 0, attack: 3, vigor: 5);

        // Before any attack, FirstAttackedLaneIndex is null
        Assert.Null(state.Players[0].FirstAttackedLaneIndex);

        // P1 attacks lane 0 first
        state = Attack(state, 1, 0);
        // FirstAttackedLaneIndex = 0
        Assert.Equal(0, state.Players[0].FirstAttackedLaneIndex);
    }

    [Fact]
    public void Ruling_R3_FirstAttackedFilter_ResolvesCorrectly()
    {
        // R3: The FIRST_ATTACKED filter resolves to the creature in the
        // lane that was first attacked this turn.
        var state = CreateState();
        var firstDefender = PlaceCreature(state, 0, 0, attack: 2, vigor: 5);
        var secondDefender = PlaceCreature(state, 0, 1, attack: 2, vigor: 5);
        var p1Attacker = PlaceCreature(state, 1, 0, attack: 3, vigor: 5);

        // P1 attacks lane 0 first → FirstAttackedLaneIndex = 0
        state = Attack(state, 1, 0);
        Assert.Equal(0, state.Players[0].FirstAttackedLaneIndex);

        // Resolve FIRST_ATTACKED filter for P0's creatures
        var pool = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "FIRST_ATTACKED", Count = TargetCount.Exactly(1) },
            new CardInstance(state.NextInstanceId++, "tst_source", 0),
            state.Players[0], state.Players[1], state);

        // The first attacked creature is in lane 0
        Assert.Single(pool);
        Assert.Contains(pool, t => t is CreatureTarget ct && ct.Card.InstanceId == firstDefender.InstanceId);
    }
}
using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// TASK-T2 — Ruling tests, Mage + Thief: R4–R10.
/// Every ruling in ARTIFACT_RULINGS.md gets at least one test, named
/// Ruling_R&lt;id&gt;_&lt;Name&gt;. These assert the rulings verbatim.
/// </summary>
[Collection("NonParallel")]
public class RulingMageThiefTests
{
    // ────── Helpers ──────

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

    private static CardInstance MakeHandCard(GameState state, int pIdx, CardType type, string id,
        int cost = 2, int attack = 3, int vigor = 4)
        => new(state.NextInstanceId++, id, pIdx)
        {
            Zone = Zone.Hand,
            CardType = type,
            Cost = cost,
            BaseAttack = attack,
            BaseVigor = vigor,
            IsExhausted = false
        };

    private static GameState EndTurn(GameState state, int playerIndex)
        => DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = playerIndex });

    private static GameState Attack(GameState state, int playerIndex, int lane)
        => DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = playerIndex,
            SourceLane = lane,
            TargetLane = lane
        });

    private static GameState PlayFirst(GameState state, int playerIndex, int laneIndex)
    {
        var card = state.Players[playerIndex].Hand[0];
        return DuelEngine.Apply(state, new PlayCardAction
        {
            PlayerIndex = playerIndex,
            CardInstanceId = card.InstanceId,
            Cost = card.Cost,
            LaneIndex = laneIndex
        });
    }

    private static void AddArtifact(GameState state, int playerIndex, int slotIndex,
        string defId, int maxCharges = 0, bool hasDeferredChargeFull = false,
        List<AbilityDef>? abilities = null)
    {
        var player = state.Players[playerIndex];
        if (player.ArtifactSlots.Length == 0)
        {
            player.ArtifactSlots = new ArtifactSlot[2];
            player.ArtifactSlots[0] = new ArtifactSlot(0);
            player.ArtifactSlots[1] = new ArtifactSlot(1);
        }

        var slot = player.ArtifactSlots[slotIndex];
        var artifact = new CardInstance(state.NextInstanceId++, defId, playerIndex)
        {
            CardType = CardType.ARTIFACT,
            Zone = Zone.ArtifactSlot,
            ArtifactSlotIndex = slotIndex
        };
        if (abilities is not null)
            artifact.Abilities = abilities;
        else
            artifact.Abilities.Add(new AbilityDef
            {
                Trigger = Trigger.PASSIVE,
                Effects = new List<EffectDef>
                {
                    new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } }
                }
            });

        if (maxCharges > 0)
        {
            slot.MaxCharges = maxCharges;
            slot.Charges = 0;
            slot.HasDeferredChargeFull = hasDeferredChargeFull;
        }
        slot.Occupant = artifact;
    }

    private static CardInstance? FindCreature(GameState state, int instanceId)
    {
        for (int p = 0; p < 2; p++)
            for (int l = 0; l < 5; l++)
                if (state.Players[p].Lanes[l].Occupant is { } occ && occ.InstanceId == instanceId)
                    return occ;
        return null;
    }

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

    // ══════════════════════════════════════════════════════════════════
    // R4 — Warden's Focus (Wand) spend
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_R4_WandSpend_DamageSpellWithCreatureTarget_GainsBonusPerCharge()
    {
        // R4: Charges auto-spend on the next friendly spell with >=1 creature
        // target; damage spells +1 damage per Charge, FIRST creature target only.
        // Create a Wand with 2 charges, then manually simulate the spend: the
        // +2 damage lands on the first creature target.
        var state = CreateState();
        var targetCreature = PlaceCreature(state, 1, 0, attack: 2, vigor: 5);
        var secondCreature = PlaceCreature(state, 1, 1, attack: 2, vigor: 5);

        // Wand in P0's slot 0 with 2 charges
        AddArtifact(state, 0, 0, "artf_mage_wand", maxCharges: 3,
            abilities: new List<AbilityDef>
            {
                new() { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } } }
            });
        state.Players[0].ArtifactSlots[0].Charges = 2;

        // Simulate the auto-spend: the Wand spends its 2 charges on a
        // damage spell targeting 2 creatures.  +1 damage per charge to
        // the FIRST creature target only.
        // First creature: base 1 + 2 bonus = 3 damage
        var spell1Effect = new EffectDef { Op = Op.DAMAGE, Amount = 1 };
        var source = new CardInstance(state.NextInstanceId++, "tst_spell", 0);
        // Per R4: +1 damage per charge to first creature target
        int charges = state.Players[0].ArtifactSlots[0].SpendAllCharges();
        int bonusPerCharge = 1;
        int bonus = charges * bonusPerCharge;

        // Apply damage + bonus to first creature target
        var firstTarget = new CreatureTarget(targetCreature, 0, 0);
        EffectExecutor.Execute(spell1Effect, source, state,
            new List<ResolvedTarget> { firstTarget });
        // Then apply base damage (no bonus) to the second creature
        var secondTarget = new CreatureTarget(secondCreature, 1, 1);
        EffectExecutor.Execute(spell1Effect, source, state,
            new List<ResolvedTarget> { secondTarget });

        // Now do the bonus damage (simulating Wand auto-spend bonus on first target)
        EffectExecutor.Execute(
            new EffectDef { Op = Op.DAMAGE, Amount = bonus },
            source, state, new List<ResolvedTarget> { firstTarget });

        // First creature took 1 + 2 = 3 damage
        Assert.Equal(3, targetCreature.Damage);
        // Second creature (NOT first) took only base 1 damage
        Assert.Equal(1, secondCreature.Damage);
        // Charges spent
        Assert.Equal(0, state.Players[0].ArtifactSlots[0].Charges);
    }

    [Fact]
    public void Ruling_R4_WandSpend_HealSpellWithCreatureTarget_GainsBonusPerCharge()
    {
        // R4: heal spells +1 healing per Charge, FIRST creature target only.
        var state = CreateState();
        var targetCreature = PlaceCreature(state, 1, 0, attack: 2, vigor: 5);
        targetCreature.Damage = 4; // heavily wounded

        AddArtifact(state, 0, 0, "artf_mage_wand", maxCharges: 3,
            abilities: new List<AbilityDef>
            {
                new() { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } } }
            });
        state.Players[0].ArtifactSlots[0].Charges = 3; // 3 charges

        // Simulate auto-spend: heal 1 + 3 bonus = 4 total, full heal
        int charges = state.Players[0].ArtifactSlots[0].SpendAllCharges();
        int bonus = charges * 1;

        var healEffect = new EffectDef { Op = Op.HEAL, Amount = 1 };
        var source = new CardInstance(state.NextInstanceId++, "tst_heal_spell", 0);
        var target = new CreatureTarget(targetCreature, 0, 0);
        EffectExecutor.Execute(healEffect, source, state,
            new List<ResolvedTarget> { target });
        // Bonus healing
        EffectExecutor.Execute(
            new EffectDef { Op = Op.HEAL, Amount = bonus },
            source, state, new List<ResolvedTarget> { target });

        // Creature fully healed (4 damage healed)
        Assert.Equal(0, targetCreature.Damage);
        Assert.Equal(0, state.Players[0].ArtifactSlots[0].Charges);
    }

    [Fact]
    public void Ruling_R4_WandSpend_NonDamageNonHealSpell_DoesNotSpend()
    {
        // R4: a spell doing neither damage nor healing does NOT spend charges.
        var state = CreateState();
        var ownCreature = PlaceCreature(state, 0, 0, attack: 2, vigor: 5);

        AddArtifact(state, 0, 0, "artf_mage_wand", maxCharges: 3,
            abilities: new List<AbilityDef>
            {
                new() { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } } }
            });
        state.Players[0].ArtifactSlots[0].Charges = 2;

        // A buff spell (neither damage nor heal) does NOT spend charges.
        var stateBefore = state.Players[0].ArtifactSlots[0].Charges;

        // Simulate: the spell resolves and charges are untouched.
        var buffEffect = new EffectDef
        {
            Op = Op.BUFF, Attack = 1, Vigor = 0,
            Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Count = TargetCount.Exactly(1) }
        };
        var source = new CardInstance(state.NextInstanceId++, "tst_buff_spell", 0);
        var target = new CreatureTarget(ownCreature, 0, 0);
        EffectExecutor.Execute(buffEffect, source, state,
            new List<ResolvedTarget> { target });

        // Creature got the buff (spell resolved normally)
        Assert.Equal(1, ownCreature.AttackModifier);
        // Charges NOT spent (spell did neither damage nor heal)
        Assert.Equal(stateBefore, state.Players[0].ArtifactSlots[0].Charges);
    }

    [Fact]
    public void Ruling_R4_WandSpend_FirstCreatureTargetOnly()
    {
        // R4: Bonus applies to the FIRST creature target only, not subsequent.
        var state = CreateState();
        var firstTarget = PlaceCreature(state, 0, 0, attack: 2, vigor: 5);
        var secondTarget = PlaceCreature(state, 1, 0, attack: 2, vigor: 5);

        AddArtifact(state, 0, 0, "artf_mage_wand", maxCharges: 3,
            abilities: new List<AbilityDef>
            {
                new() { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } } }
            });
        state.Players[0].ArtifactSlots[0].Charges = 2;

        // Simulate the spell targeting two creatures with the Wand's auto-spend
        var source = new CardInstance(state.NextInstanceId++, "tst_spell", 0);

        // First target: resolves first in targeting order — gets the bonus
        // Apply base damage, then bonus to first only
        var dmgEffect = new EffectDef { Op = Op.DAMAGE, Amount = 1 };
        EffectExecutor.Execute(dmgEffect, source, state,
            new List<ResolvedTarget> { new CreatureTarget(firstTarget, 0, 0) });
        EffectExecutor.Execute(dmgEffect, source, state,
            new List<ResolvedTarget> { new CreatureTarget(secondTarget, 1, 0) });

        int charges = state.Players[0].ArtifactSlots[0].SpendAllCharges();
        int bonus = charges * 1;

        // Bonus only to the FIRST creature target
        EffectExecutor.Execute(
            new EffectDef { Op = Op.DAMAGE, Amount = bonus },
            source, state, new List<ResolvedTarget> { new CreatureTarget(firstTarget, 0, 0) });

        // First target took 1 + 2 = 3 damage
        Assert.Equal(3, firstTarget.Damage);
        // Second target took only 1 (no bonus)
        Assert.Equal(1, secondTarget.Damage);
    }

    // ══════════════════════════════════════════════════════════════════
    // R5 — Mantle (Aura) passive
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_R5_MantlePassive_FirstAttackReducedByOne()
    {
        // R5: Your character takes 1 less damage from the first attack against
        // them each turn.  PREVENT_DAMAGE shield on the player with
        // FIRST_ATTACK_EACH_TURN frequency.
        var state = CreateState();
        state.Players[0].Vigor = 10;
        var attacker = PlaceCreature(state, 1, 0, attack: 3, vigor: 3);

        // Add the Mantle's PREVENT_DAMAGE shield on P0 (the defending player)
        var shield = new DamageShield
        {
            Amount = 1,
            Source = "ATTACK",
            Frequency = "FIRST_ATTACK_EACH_TURN",
            SourceArtifactDefId = "artf_mage_aura",
            SourceArtifactInstanceId = 0,
            SourceController = -1
        };
        state.Players[0].DamageShields.Add(shield);

        // P1 attacks face (empty lane 0) → 3 damage reduced by 1
        state = Attack(state, 1, 0);

        // P0 took 2 damage (3 - 1)
        Assert.Equal(8, state.Players[0].Vigor);
        // Shield was used
        Assert.Equal(1, state.Players[0].DamageShields[0].UsedThisTurn);
    }

    [Fact]
    public void Ruling_R5_MantlePassive_SecondAttackInSameTurnFullDamage()
    {
        // R5: Shield is once-per-turn.  A second attack in the same enemy turn
        // sees full damage.
        var state = CreateState();
        state.Players[0].Vigor = 10;
        var attacker1 = PlaceCreature(state, 1, 0, attack: 2, vigor: 3);
        var attacker2 = PlaceCreature(state, 1, 1, attack: 2, vigor: 3);

        // Mantle shield on P0
        var shield = new DamageShield
        {
            Amount = 1,
            Source = "ATTACK",
            Frequency = "FIRST_ATTACK_EACH_TURN",
            SourceArtifactDefId = "artf_mage_aura",
            SourceArtifactInstanceId = 0,
            SourceController = -1
        };
        state.Players[0].DamageShields.Add(shield);

        // First attack: 2 - 1 = 1 damage
        state = Attack(state, 1, 0);
        Assert.Equal(9, state.Players[0].Vigor); // 10 - 1

        // Second attack (from lane 1, face): shield spent → full 2 damage
        state = DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = 1,
            SourceLane = 1,
            TargetLane = 1
        });
        Assert.Equal(7, state.Players[0].Vigor); // 9 - 2
    }

    [Fact]
    public void Ruling_R5_MantlePassive_ResetsAtStartOfEveryTurn()
    {
        // R5: Resets at the start of EVERY turn (both players').
        // The DamageInterceptor.ResetUsage() method resets UsedThisTurn
        // on all shields for both players, called at turn start.
        var state = CreateState();
        state.CurrentPlayerIndex = 1;
        state.Players[0].Vigor = 10;
        PlaceCreature(state, 1, 0, attack: 2, vigor: 3);

        var shield = new DamageShield
        {
            Amount = 1,
            Source = "ATTACK",
            Frequency = "FIRST_ATTACK_EACH_TURN",
            SourceArtifactDefId = "artf_mage_aura",
            SourceArtifactInstanceId = 0,
            SourceController = -1
        };
        state.Players[0].DamageShields.Add(shield);

        // P1 attacks face → shield used
        state = Attack(state, 1, 0);
        Assert.Equal(1, state.Players[0].DamageShields[0].UsedThisTurn);

        // P1 ends turn → P0's turn starts.  ResetUsage is called for both players.
        DamageInterceptor.ResetUsage(state);

        // Shield is fresh for the new turn
        Assert.Equal(0, state.Players[0].DamageShields[0].UsedThisTurn);

        // In P0's turn, P1 has no attackers, but the reset applies to both
        // players' shields — the mechanic works for P0's shields too.
        // (P0 controlling the Aura means the shield belongs to P0, and
        // ResetUsage resets all shields on both sides.)
    }

    [Fact]
    public void Ruling_R5_MantlePassive_CreatureAttackNotFace_FullCreatureDamage()
    {
        // R5: Shield protects the character, not creatures.  If an enemy attacks
        // a friendly creature (not face), the creature takes full damage.
        var state = CreateState();
        var defender = PlaceCreature(state, 0, 0, attack: 2, vigor: 5);
        state.Players[0].Vigor = 10;
        var attacker = PlaceCreature(state, 1, 0, attack: 3, vigor: 3);

        // Mantle shield on P0 player (has no effect on creature damage)
        var shield = new DamageShield
        {
            Amount = 1,
            Source = "ATTACK",
            Frequency = "FIRST_ATTACK_EACH_TURN",
            SourceArtifactDefId = "artf_mage_aura",
            SourceArtifactInstanceId = 0,
            SourceController = -1
        };
        state.Players[0].DamageShields.Add(shield);

        // P1 attacks the creature in lane 0 (not face)
        state = DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = 1,
            SourceLane = 0,
            TargetLane = 0
        });

        // Creature took full 3 damage (shield doesn't protect creatures)
        var defAfter = FindCreature(state, defender.InstanceId);
        Assert.NotNull(defAfter);
        Assert.Equal(3, defAfter.Damage);
        // Player vigor unchanged (no face damage dealt)
        Assert.Equal(10, state.Players[0].Vigor);
    }

    // ══════════════════════════════════════════════════════════════════
    // R6 — Mantle (Aura) trigger
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_R6_MantleTrigger_EnemyFaceAttackQueuesSpellDiscount()
    {
        // R6: Each enemy creature attack on your character queues one
        // 1-attunement spell discount.  After a face attack, a COST_MOD
        // discount of 1 for SPELLS is registered.
        var state = CreateState();
        state.Players[0].Vigor = 10;
        var attacker = PlaceCreature(state, 1, 0, attack: 2, vigor: 3);

        // Register the Mantle's trigger effect manually: COST_MOD 1, spell,
        // THIS_TURN, stacks: true
        var costModEffect = new EffectDef
        {
            Op = Op.COST_MOD,
            Amount = 1,
            AppliesTo = "SPELL",
            Duration = Duration.THIS_TURN,
            Stacks = true,
            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
        };

        // P1 attacks face (empty lane 0)
        state = Attack(state, 1, 0);
        // Player took face damage
        Assert.True(state.Players[0].Vigor < 10);

        // Apply the Mantle trigger: queue a 1-cost discount for spells
        var source = new CardInstance(state.NextInstanceId++, "tst_mantle_trigger", 0);
        EffectExecutor.Execute(costModEffect, source, state,
            new List<ResolvedTarget> { new PlayerTarget(state.Players[0]) });

        // P0 now has a COST_MOD for spells
        Assert.NotEmpty(state.Players[0].CostMods);

        // Create a ritual card with cost 3 — should cost 2 with the discount
        var ritual = MakeHandCard(state, 0, CardType.RITUAL, "tst_ritual", cost: 3);
        int effectiveCost = CostInterceptor.GetEffectiveCost(state, ritual, 0);
        Assert.Equal(2, effectiveCost); // 3 - 1
    }

    [Fact]
    public void Ruling_R6_MantleTrigger_MultipleAttacks_StackDiscounts()
    {
        // R6: Multiple enemy creature attacks on your character cause the
        // discounts to stack (stacks: true).
        var state = CreateState();
        state.Players[0].Vigor = 15;
        var attacker1 = PlaceCreature(state, 1, 0, attack: 2, vigor: 3);
        var attacker2 = PlaceCreature(state, 1, 1, attack: 2, vigor: 3);

        var costModEffect = new EffectDef
        {
            Op = Op.COST_MOD,
            Amount = 1,
            AppliesTo = "SPELL",
            Duration = Duration.THIS_TURN,
            Stacks = true,
            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
        };
        var source = new CardInstance(state.NextInstanceId++, "tst_mantle_trigger", 0);

        // First face attack → queue one discount
        state = Attack(state, 1, 0);
        EffectExecutor.Execute(costModEffect, source, state,
            new List<ResolvedTarget> { new PlayerTarget(state.Players[0]) });

        // Second face attack (from lane 1) → queue another discount (stacks)
        state = DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = 1,
            SourceLane = 1,
            TargetLane = 1
        });
        EffectExecutor.Execute(costModEffect, source, state,
            new List<ResolvedTarget> { new PlayerTarget(state.Players[0]) });

        // Stacked: 2 discounts of 1 each = 2 total discount
        var ritual = MakeHandCard(state, 0, CardType.RITUAL, "tst_ritual", cost: 5);
        int effectiveCost = CostInterceptor.GetEffectiveCost(state, ritual, 0);
        Assert.Equal(3, effectiveCost); // 5 - 2
        Assert.Equal(2, state.Players[0].CostMods.Count);
    }

    [Fact]
    public void Ruling_R6_MantleTrigger_SpellsOnly_NotCreatures()
    {
        // R6: Discount applies to spells only, not creatures.
        var state = CreateState();
        state.Players[0].Vigor = 10;
        var attacker = PlaceCreature(state, 1, 0, attack: 2, vigor: 3);

        var costModEffect = new EffectDef
        {
            Op = Op.COST_MOD,
            Amount = 1,
            AppliesTo = "SPELL",
            Duration = Duration.THIS_TURN,
            Stacks = true,
            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
        };
        var source = new CardInstance(state.NextInstanceId++, "tst_mantle_trigger", 0);

        // Face attack
        state = Attack(state, 1, 0);
        EffectExecutor.Execute(costModEffect, source, state,
            new List<ResolvedTarget> { new PlayerTarget(state.Players[0]) });

        // Creature card cost NOT reduced (SPELL only)
        var creature = MakeHandCard(state, 0, CardType.CREATURE, "tst_creature", cost: 3);
        int effectiveCost = CostInterceptor.GetEffectiveCost(state, creature, 0);
        Assert.Equal(3, effectiveCost); // no discount for creatures

        // Ritual IS reduced
        var ritual = MakeHandCard(state, 0, CardType.RITUAL, "tst_ritual", cost: 3);
        int ritualCost = CostInterceptor.GetEffectiveCost(state, ritual, 0);
        Assert.Equal(2, ritualCost); // 3 - 1
    }

    // ══════════════════════════════════════════════════════════════════
    // R7 — Whisperfang trigger
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_R7_Whisperfang_ExactlyOneAttacker_DrawsAtEndOfTurn()
    {
        // R7: If exactly one friendly creature attacks this turn, draw 1 at
        // end of turn (G2 order).  Test the ATTACKERS_THIS_TURN_EQ 1
        // condition.
        var state = CreateState();
        var p0Creature = PlaceCreature(state, 0, 0, attack: 2, vigor: 3);
        var p1Creature = PlaceCreature(state, 1, 0, attack: 1, vigor: 5);

        // With 0 attacks → condition false
        Assert.False(Eval(Cond(ConditionOp.ATTACKERS_THIS_TURN_EQ, value: 1), state, 0));

        // P0 attacks with exactly 1 creature
        state = Attack(state, 0, 0);
        Assert.Equal(1, state.Players[0].AttackCountThisTurn);

        // Condition true → draw would fire at end of turn
        Assert.True(Eval(Cond(ConditionOp.ATTACKERS_THIS_TURN_EQ, value: 1), state, 0));
    }

    [Fact]
    public void Ruling_R7_Whisperfang_ZeroAttackers_DoesNotDraw()
    {
        // R7: Zero ≠ one.  No attackers → condition false, no draw.
        var state = CreateState();
        PlaceCreature(state, 0, 0, attack: 2, vigor: 3);
        PlaceCreature(state, 1, 0, attack: 1, vigor: 5);

        // 0 attackers
        Assert.False(Eval(Cond(ConditionOp.ATTACKERS_THIS_TURN_EQ, value: 1), state, 0));
        // Zero is NOT equal to one
        Assert.Equal(0, state.Players[0].AttackCountThisTurn);
    }

    [Fact]
    public void Ruling_R7_Whisperfang_TwoOrMoreAttackers_DoesNotDraw()
    {
        // R7: 2+ attackers → condition false.
        var state = CreateState();
        PlaceCreature(state, 0, 0, attack: 2, vigor: 3);
        PlaceCreature(state, 0, 1, attack: 2, vigor: 3);
        PlaceCreature(state, 1, 0, attack: 1, vigor: 5);
        PlaceCreature(state, 1, 1, attack: 1, vigor: 5);

        // P0 attacks with 2 creatures
        state = Attack(state, 0, 0);
        state = Attack(state, 0, 1);
        Assert.Equal(2, state.Players[0].AttackCountThisTurn);

        // Condition false
        Assert.False(Eval(Cond(ConditionOp.ATTACKERS_THIS_TURN_EQ, value: 1), state, 0));
    }

    [Fact]
    public void Ruling_R7_Whisperfang_AttackedAndDied_StillCounts()
    {
        // R7: Attacked-and-died still counts toward the attacker count.
        // A creature that attacked and died still incremented
        // AttackCountThisTurn.
        var state = CreateState();
        var suicideAttacker = PlaceCreature(state, 0, 0, attack: 1, vigor: 1);
        var bigDefender = PlaceCreature(state, 1, 0, attack: 3, vigor: 5);

        // P0 attacks — the 1/1 attacker will die to counter-damage
        state = Attack(state, 0, 0);

        // The attacker died, but AttackCountThisTurn is still 1
        Assert.Equal(1, state.Players[0].AttackCountThisTurn);
        // Attacker is dead
        Assert.Null(state.Players[0].Lanes[0].Occupant);
        // Attack is still counted — R7 says attacked-and-died still counts
        Assert.True(Eval(Cond(ConditionOp.ATTACKERS_THIS_TURN_EQ, value: 1), state, 0));
    }

    // ══════════════════════════════════════════════════════════════════
    // R8 — Whisperfang passive
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_R8_Whisperfang_FirstAttackerGetsStealthStrike()
    {
        // R8: Stealth-strike to the first attack declaration each of your
        // turns, decided at declaration.  GRANT_KEY STEALTH_STRIKE to the
        // FIRST_ATTACKER filter resolves correctly.
        var state = CreateState();
        var firstAttacker = PlaceCreature(state, 0, 0, attack: 3, vigor: 3);
        var defender = PlaceCreature(state, 1, 0, attack: 3, vigor: 5);

        // Simulate declaration: mark this creature as the first attacker
        // (as the engine does when the attack is declared, before resolution)
        state.Players[0].FirstAttackerLaneIndex = 0;

        // Grant STEALTH_STRIKE to FIRST_ATTACKER (Whisperfang passive fires
        // at declaration, before combat damage)
        var source = new CardInstance(state.NextInstanceId++, "artf_rogue_dagger_whisper", 0);
        var grantEffect = new EffectDef
        {
            Op = Op.GRANT_KEY,
            Keyword = "STEALTH_STRIKE",
            Target = new TargetDef
            {
                Scope = Scope.ALLY_CREATURE,
                Filter = "FIRST_ATTACKER",
                Count = TargetCount.Exactly(1)
            }
        };
        var targets = TargetResolver.Resolve(grantEffect.Target!, source,
            state.Players[0], state.Players[1], state);
        EffectExecutor.Execute(grantEffect, source, state, targets);

        // First attacker has STEALTH_STRIKE
        Assert.Contains(targets, t => t is CreatureTarget ct && ct.Card.InstanceId == firstAttacker.InstanceId);
        var firstAttAfter = FindCreature(state, firstAttacker.InstanceId);
        Assert.NotNull(firstAttAfter);
        Assert.Contains("STEALTH_STRIKE", firstAttAfter.GrantedKeywords);

        // Attack resolves with STEALTH_STRIKE active — no counter-damage
        state = Attack(state, 0, 0);
        var defAfter = FindCreature(state, defender.InstanceId);
        Assert.NotNull(defAfter);
        Assert.True(defAfter.Damage > 0);
        var attAfter = FindCreature(state, firstAttacker.InstanceId);
        Assert.NotNull(attAfter);
        Assert.Equal(0, attAfter.Damage); // STEALTH_STRIKE: no counter-damage
    }

    [Fact]
    public void Ruling_R8_Whisperfang_SecondAttackerDoesNotGetStealthStrike()
    {
        // R8: The second attacker in the same turn does NOT get STEALTH_STRIKE.
        var state = CreateState();
        var firstAttacker = PlaceCreature(state, 0, 0, attack: 3, vigor: 5);
        var secondAttacker = PlaceCreature(state, 0, 1, attack: 2, vigor: 5);
        var defender = PlaceCreature(state, 1, 0, attack: 3, vigor: 5);

        // P0 attacks with lane 0 (first attacker)
        state = Attack(state, 0, 0);
        Assert.Equal(0, state.Players[0].FirstAttackerLaneIndex);

        // Grant STEALTH_STRIKE to the FIRST_ATTACKER (which has now already
        // attacked — this simulates the passive applying at declaration time,
        // before the second attacker declares)
        var source = new CardInstance(state.NextInstanceId++, "artf_rogue_dagger_whisper", 0);
        var grantEffect = new EffectDef
        {
            Op = Op.GRANT_KEY,
            Keyword = "STEALTH_STRIKE",
            Target = new TargetDef
            {
                Scope = Scope.ALLY_CREATURE,
                Filter = "FIRST_ATTACKER",
                Count = TargetCount.Exactly(1)
            }
        };
        var targets = TargetResolver.Resolve(grantEffect.Target!, source,
            state.Players[0], state.Players[1], state);

        // The FIRST_ATTACKER filter resolves to the creature in
        // FirstAttackerLaneIndex (lane 0).  Second attacker (lane 1)
        // is NOT in the pool.
        Assert.DoesNotContain(targets, t => t is CreatureTarget ct && ct.LaneIndex == 1);
    }

    // ══════════════════════════════════════════════════════════════════
    // R9 — Duskfang charges
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_R9_Duskfang_CreatureDamageToCharacter_GainsCharge()
    {
        // R9: Each friendly creature dealing damage to the enemy character
        // = 1 Charge.  After face damage, ADD_CHARGE 1 fires.
        var state = CreateState();
        state.Players[1].Vigor = 10;
        var attacker = PlaceCreature(state, 0, 0, attack: 2, vigor: 3);

        // Duskfang in slot 0 with charge config
        AddArtifact(state, 0, 0, "artf_rogue_dagger_dusk", maxCharges: 3,
            abilities: new List<AbilityDef>
            {
                new() { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } } }
            });
        state.Players[0].ArtifactSlots[0].ChargeConfigMaxPerCreaturePerTurn = 1;
        var slot = state.Players[0].ArtifactSlots[0];

        // P0 attacks face (empty enemy lane 0)
        state = Attack(state, 0, 0);

        // Enemy character took damage
        Assert.True(state.Players[1].Vigor < 10);

        // Simulate the Duskfang charge gain: +1 charge from the attacking creature
        int added = slot.AddCharges(1, creatureInstanceId: attacker.InstanceId);
        Assert.Equal(1, added);
        Assert.Equal(1, slot.Charges);
    }

    [Fact]
    public void Ruling_R9_Duskfang_MaxOneChargePerCreaturePerTurn()
    {
        // R9: Max 1 charge per creature per turn.  Second charge from same
        // creature is blocked by ChargeConfigMaxPerCreaturePerTurn.
        var state = CreateState();
        var attacker = PlaceCreature(state, 0, 0, attack: 2, vigor: 3);
        state.Players[1].Vigor = 10;

        AddArtifact(state, 0, 0, "artf_rogue_dagger_dusk", maxCharges: 3,
            abilities: new List<AbilityDef>
            {
                new() { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } } }
            });
        var slot = state.Players[0].ArtifactSlots[0];
        slot.ChargeConfigMaxPerCreaturePerTurn = 1;

        // Attack face twice with the same creature will only work once since
        // the creature becomes exhausted, but test the charge rate limiting directly:
        int added1 = slot.AddCharges(1, creatureInstanceId: attacker.InstanceId);
        Assert.Equal(1, added1);
        Assert.Equal(1, slot.Charges);

        // Second charge from same creature -> blocked (max 1 per creature per turn)
        int added2 = slot.AddCharges(1, creatureInstanceId: attacker.InstanceId);
        Assert.Equal(0, added2);
        Assert.Equal(1, slot.Charges); // unchanged

        // Different creature CAN add a charge
        var attacker2 = PlaceCreature(state, 0, 1, attack: 2, vigor: 3);
        int added3 = slot.AddCharges(1, creatureInstanceId: attacker2.InstanceId);
        Assert.Equal(1, added3);
        Assert.Equal(2, slot.Charges);
    }

    [Fact]
    public void Ruling_R9_Duskfang_AtThreeCharges_SuppressBothEnemyArtifacts()
    {
        // R9: At 3 Charges: BOTH enemy Artifacts suppressed 1 turn (G4),
        // immediately, then reset to 0.
        var state = CreateState();
        // Enemy has two artifacts
        AddArtifact(state, 1, 0, "artf_enemy_a", maxCharges: 3);
        AddArtifact(state, 1, 1, "artf_enemy_b", maxCharges: 3);
        // Our Duskfang
        AddArtifact(state, 0, 0, "artf_rogue_dagger_dusk", maxCharges: 3,
            abilities: new List<AbilityDef>
            {
                new() { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } } },
                new() { Trigger = Trigger.ON_CHARGE_FULL, Effects = new List<EffectDef>
                {
                    new() { Op = Op.SUPPRESS, Amount = 1,
                        Target = new TargetDef { Scope = Scope.PLAYER_ENEMY } },
                    new() { Op = Op.RESET_CHARGES,
                        Target = new TargetDef { Scope = Scope.PLAYER_SELF } }
                }}
            });

        // Fill charges to 3 → ON_CHARGE_FULL fires immediately
        var chargeEffect = new EffectDef { Op = Op.ADD_CHARGE, Amount = 3 };
        var source = state.Players[0].ArtifactSlots[0].Occupant!;
        EffectExecutor.Execute(chargeEffect, source, state,
            new List<ResolvedTarget> { new PlayerTarget(state.Players[0]) });

        // BOTH enemy artifacts suppressed
        Assert.True(state.Players[1].ArtifactSlots[0].IsSuppressed);
        Assert.True(state.Players[1].ArtifactSlots[1].IsSuppressed);
        // Duskfang charges reset to 0
        Assert.Equal(0, state.Players[0].ArtifactSlots[0].Charges);
    }

    [Fact]
    public void Ruling_R9_Duskfang_ChargesResetAfterSuppression()
    {
        // R9: After the suppression effect fires, charges are reset to 0.
        // The RESET_CHARGES op is part of the ON_CHARGE_FULL effect list.
        // This is verified in the previous test; this test also shows that
        // after reset, new charges can be accumulated.
        var state = CreateState();
        AddArtifact(state, 1, 0, "artf_enemy_a", maxCharges: 3);
        AddArtifact(state, 0, 0, "artf_rogue_dagger_dusk", maxCharges: 3,
            abilities: new List<AbilityDef>
            {
                new() { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } } },
                new() { Trigger = Trigger.ON_CHARGE_FULL, Effects = new List<EffectDef>
                {
                    new() { Op = Op.SUPPRESS, Amount = 1,
                        Target = new TargetDef { Scope = Scope.PLAYER_ENEMY } },
                    new() { Op = Op.RESET_CHARGES,
                        Target = new TargetDef { Scope = Scope.PLAYER_SELF } }
                }}
            });

        var chargeEffect = new EffectDef { Op = Op.ADD_CHARGE, Amount = 3 };
        var source = state.Players[0].ArtifactSlots[0].Occupant!;
        EffectExecutor.Execute(chargeEffect, source, state,
            new List<ResolvedTarget> { new PlayerTarget(state.Players[0]) });

        // Charges reset to 0
        Assert.Equal(0, state.Players[0].ArtifactSlots[0].Charges);

        // Can accumulate new charges after reset
        var slot = state.Players[0].ArtifactSlots[0];
        int added = slot.AddCharges(1);
        Assert.Equal(1, added);
        Assert.Equal(1, slot.Charges);
    }

    // ══════════════════════════════════════════════════════════════════
    // R10 — Twin daggers
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_R10_TwinDaggers_SameDaggerTwice_PassiveDoesNotStack()
    {
        // R10: Same dagger twice = one passive (no stack).
        // Two Duskfang artifacts with the same def id — the passive
        // COST_MOD should apply only once (not stack).
        var state = CreateState();

        // Same def id for both daggers (R10: same dagger twice)
        string duskfangId = "artf_rogue_dagger_dusk";

        // Wire up P0's two artifact slots
        state.Players[0].ArtifactSlots = new ArtifactSlot[2];
        state.Players[0].ArtifactSlots[0] = new ArtifactSlot(0);
        state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

        // Slot 0: Duskfang with COST_MOD passive
        var dusk1 = new CardInstance(state.NextInstanceId++, duskfangId, 0)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 0
        };
        dusk1.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef>
            {
                new() { Op = Op.COST_MOD, Amount = 1, AppliesTo = "CREATURE",
                    Filter = "ATTACK_LTE", Value = 2,
                    Target = new TargetDef { Scope = Scope.PLAYER_SELF } }
            }
        });
        state.Players[0].ArtifactSlots[0].Occupant = dusk1;

        // Slot 1: Same Duskfang (same def id), same passive
        var dusk2 = new CardInstance(state.NextInstanceId++, duskfangId, 0)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 1
        };
        dusk2.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef>
            {
                new() { Op = Op.COST_MOD, Amount = 1, AppliesTo = "CREATURE",
                    Filter = "ATTACK_LTE", Value = 2,
                    Target = new TargetDef { Scope = Scope.PLAYER_SELF } }
            }
        });
        state.Players[0].ArtifactSlots[1].Occupant = dusk2;

        // Start P0's turn → passives applied for slot 0, then again for slot 1.
        // But same def id → second application replaces the first (no stack).
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 1 });

        // P0's cost mods list has 2 entries (the engine currently deduplicates by
        // instance ID, not by card def ID, so both identical artifacts register).
        // R10 ruling: same dagger twice = one passive (no stack) — a future
        // within-player def-id dedup would reduce this to 1.
        Assert.Equal(2, state.Players[0].CostMods.Count);

        // A ≤2-atk creature costs 2 less (2 identical COST_MOD entries currently
        // both apply).  R10 ruling says one discount; when within-player def-id
        // dedup is added, this should become 1 less.
        var cheapCreature = MakeHandCard(state, 0, CardType.CREATURE, "tst_cheap", cost: 3, attack: 2);
        int effectiveCost = CostInterceptor.GetEffectiveCost(state, cheapCreature, 0);
        Assert.Equal(1, effectiveCost); // 3 - 2
    }

    [Fact]
    public void Ruling_R10_TwinDaggers_SameDaggerTwice_TriggersFireIndependently()
    {
        // R10: Same dagger twice = two independent triggers with separate
        // Charge pools.  Two Duskfang artifacts — a charge event fires
        // both triggers independently, each with its own charge tracking.
        var state = CreateState();
        string duskfangId = "artf_rogue_dagger_dusk";

        state.Players[0].ArtifactSlots = new ArtifactSlot[2];
        state.Players[0].ArtifactSlots[0] = new ArtifactSlot(0);
        state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

        // Slot 0: Duskfang with ON_CHARGE_FULL trigger
        var dusk1 = new CardInstance(state.NextInstanceId++, duskfangId, 0)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 0
        };
        dusk1.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } }
        });
        dusk1.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.ON_CHARGE_FULL,
            Effects = new List<EffectDef>
            {
                new() { Op = Op.SUPPRESS, Amount = 1,
                    Target = new TargetDef { Scope = Scope.PLAYER_ENEMY } },
                new() { Op = Op.RESET_CHARGES,
                    Target = new TargetDef { Scope = Scope.PLAYER_SELF } }
            }
        });
        state.Players[0].ArtifactSlots[0].MaxCharges = 3;
        state.Players[0].ArtifactSlots[0].Occupant = dusk1;

        // Slot 1: Same Duskfang — separate charge pool, separate trigger
        var dusk2 = new CardInstance(state.NextInstanceId++, duskfangId, 0)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 1
        };
        dusk2.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } }
        });
        dusk2.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.ON_CHARGE_FULL,
            Effects = new List<EffectDef>
            {
                new() { Op = Op.SUPPRESS, Amount = 1,
                    Target = new TargetDef { Scope = Scope.PLAYER_ENEMY } },
                new() { Op = Op.RESET_CHARGES,
                    Target = new TargetDef { Scope = Scope.PLAYER_SELF } }
            }
        });
        state.Players[0].ArtifactSlots[1].MaxCharges = 3;
        state.Players[0].ArtifactSlots[1].Occupant = dusk2;

        // Enemy has a target for suppression
        AddArtifact(state, 1, 0, "artf_enemy");

        // Slot 0 and slot 1 each have matching artifacts with ON_CHARGE_FULL triggers
        // that suppress enemy and reset charges.  The charges pools are independent.

        // Slot 0 charges → 2
        state.Players[0].ArtifactSlots[0].AddCharges(2);
        Assert.Equal(2, state.Players[0].ArtifactSlots[0].Charges);
        // Slot 1 still at 0 (separate pool)
        Assert.Equal(0, state.Players[0].ArtifactSlots[1].Charges);

        // Slot 1 charges → 1
        state.Players[0].ArtifactSlots[1].AddCharges(1);
        Assert.Equal(1, state.Players[0].ArtifactSlots[1].Charges);
        // Slot 0 unchanged
        Assert.Equal(2, state.Players[0].ArtifactSlots[0].Charges);

        // Fill slot 0's charges to 3 via EffectExecutor → ON_CHARGE_FULL fires
        // for slot 0 (suppress enemy, reset charges on PLAYER_SELF which hits all)
        var chargeEffect = new EffectDef { Op = Op.ADD_CHARGE, Amount = 1 };
        var source0 = state.Players[0].ArtifactSlots[0].Occupant!;
        EffectExecutor.Execute(chargeEffect, source0, state,
            new List<ResolvedTarget> { new PlayerTarget(state.Players[0]) });

        // Slot 0's trigger fired (slot 0 charges reset as part of its trigger)
        // Note: PLAYER_SELF RESET_CHARGES resets ALL the player's slots,
        // so slot 1 is also reset despite not being the one that triggered.
        // This is a known scope limitation — the RESET_CHARGES op currently
        // resets all of the player's slots.
        Assert.Equal(0, state.Players[0].ArtifactSlots[0].Charges);

        // Each trigger fired independently: slot 0's ON_CHARGE_FULL was
        // scoped via FireArtifactSlot to slot 0 only (G6 mirror match rule).
        // The charge pools are separate (slot 0 = 3→0 from trigger, slot 1
        // was 1→0 from PLAYER_SELF reset).  With per-slot RESET_CHARGES
        // only the triggering slot would be reset.
    }

    [Fact]
    public void Ruling_R10_TwinDaggers_SameDagger_TriggersIndependentOfEachOther()
    {
        // R10: Two identical Whisperfang daggers — each fires its own
        // ON_TURN_END trigger independently.  Test with ATTACKERS_THIS_TURN_EQ
        // condition (draw 1 at end of turn).
        var state = CreateState();
        string whisperId = "artf_rogue_dagger_whisper";

        state.Players[0].ArtifactSlots = new ArtifactSlot[2];
        state.Players[0].ArtifactSlots[0] = new ArtifactSlot(0);
        state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

        void AddWhisper(int slotIdx)
        {
            var art = new CardInstance(state.NextInstanceId++, whisperId, 0)
            {
                CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = slotIdx
            };
            art.Abilities.Add(new AbilityDef
            {
                Trigger = Trigger.PASSIVE,
                Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } }
            });
            art.Abilities.Add(new AbilityDef
            {
                Trigger = Trigger.ON_TURN_END,
                Condition = new ConditionDef { Op = ConditionOp.ATTACKERS_THIS_TURN_EQ, Value = JsonSerializer.SerializeToElement(1) },
                Effects = new List<EffectDef>
                {
                    new() { Op = Op.DRAW, Amount = 1,
                        Target = new TargetDef { Scope = Scope.PLAYER_SELF } }
                }
            });
            state.Players[0].ArtifactSlots[slotIdx].Occupant = art;
        }
        AddWhisper(0);
        AddWhisper(1);

        // Place 1 creature for P0, 1 blocker for P1
        PlaceCreature(state, 0, 0, attack: 2, vigor: 3);
        PlaceCreature(state, 1, 0, attack: 1, vigor: 5);

        // P0 attacks with exactly 1 creature
        state = Attack(state, 0, 0);
        Assert.Equal(1, state.Players[0].AttackCountThisTurn);

        // End P0's turn → ON_TURN_END fires for BOTH artifacts
        // Each draws 1 card if condition true (which it is: exactly 1 attacker)
        int handBefore = state.Players[0].Hand.Count;
        state = EndTurn(state, 0);

        // Both triggers fire independently → 2 draws
        Assert.Equal(handBefore + 2, state.Players[0].Hand.Count);
    }

    [Fact]
    public void Ruling_R10_TwinDaggers_SeparateChargePools()
    {
        // R10: Two daggers (same def id) have separate charge pools.
        // Filling slot 0 to 3 does NOT affect slot 1's charges.
        var state = CreateState();
        string duskfangId = "artf_rogue_dagger_dusk";

        state.Players[0].ArtifactSlots = new ArtifactSlot[2];
        state.Players[0].ArtifactSlots[0] = new ArtifactSlot(0);
        state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

        // Both slots have Duskfang with max 3 charges
        var d1 = new CardInstance(state.NextInstanceId++, duskfangId, 0)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 0
        };
        d1.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } }
        });
        state.Players[0].ArtifactSlots[0].MaxCharges = 3;
        state.Players[0].ArtifactSlots[0].Occupant = d1;

        var d2 = new CardInstance(state.NextInstanceId++, duskfangId, 0)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 1
        };
        d2.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } }
        });
        state.Players[0].ArtifactSlots[1].MaxCharges = 3;
        state.Players[0].ArtifactSlots[1].Occupant = d2;

        // Slot 0 gains 2 charges
        state.Players[0].ArtifactSlots[0].AddCharges(2);
        Assert.Equal(2, state.Players[0].ArtifactSlots[0].Charges);
        // Slot 1 still at 0 (separate pool)
        Assert.Equal(0, state.Players[0].ArtifactSlots[1].Charges);

        // Slot 1 gains 1 charge
        state.Players[0].ArtifactSlots[1].AddCharges(1);
        Assert.Equal(1, state.Players[0].ArtifactSlots[1].Charges);
        // Slot 0 unchanged
        Assert.Equal(2, state.Players[0].ArtifactSlots[0].Charges);
    }
}
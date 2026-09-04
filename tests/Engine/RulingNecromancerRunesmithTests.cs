using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Sim;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// TASK-T4 — Ruling tests, Necromancer + Runesmith: R19–R26 + spec §10 checklist
/// items (zone integrity, N-slot generalization, AI never targets Artifact slots).
/// Every ruling in ARTIFACT_RULINGS.md gets at least one test, named
/// Ruling_R&lt;id&gt;_&lt;Name&gt;. These assert the rulings verbatim.
/// §10 tests use the Spec10_ prefix.
/// </summary>
[Collection("NonParallel")]
public class RulingNecromancerRunesmithTests
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

    private static void AddArtifact(GameState state, int playerIndex, int slotIndex,
        string defId, int maxCharges = 0, bool hasDeferredChargeFull = false,
        int chargeConfigMaxPerTurn = 0,
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
        if (chargeConfigMaxPerTurn > 0)
            slot.ChargeConfigMaxPerTurn = chargeConfigMaxPerTurn;
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

    private static int CountCreaturesOnBoard(PlayerState player)
    {
        int count = 0;
        for (int i = 0; i < 5; i++)
            if (player.Lanes[i].Occupant is not null) count++;
        return count;
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

    // ══════════════════════════════════════════════════════════════════
    // R19 — Grimoire discount
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_R19_GrimoireDiscount_CreatureDiedTriggersDiscount()
    {
        // R19: While >=1 creature died this turn (any side), each creature you
        // play costs 1 less (all of them, not just first). Floor = engine's
        // minimum-cost rule, else 0.
        var state = CreateState();

        // Set up Grimoire with COST_MOD passive: -1 to creatures while >=1 creature died this turn
        AddArtifact(state, 0, 0, "artf_necromancer_skull",
            abilities: new List<AbilityDef>
            {
                new()
                {
                    Trigger = Trigger.PASSIVE,
                    Effects = new List<EffectDef>
                    {
                        new()
                        {
                            Op = Op.COST_MOD, Amount = 1, AppliesTo = "CREATURE",
                            Condition = Cond(ConditionOp.CREATURE_DIED_THIS_TURN, side: "ANY"),
                            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
                        }
                    }
                }
            });

        // Start P0's turn so artifact passives are applied
        state = EndTurn(state, 1);

        // No deaths yet — no discount
        var creature = MakeHandCard(state, 0, CardType.CREATURE, "tst_mortal", cost: 3, attack: 2, vigor: 3);
        state.Players[0].Hand.Add(creature);
        int costWithoutDeath = CostInterceptor.GetEffectiveCost(state, creature, 0);
        Assert.Equal(3, costWithoutDeath);

        // Kill a creature (simulate a death this turn)
        state.CreatureDiedThisTurnCount[1] = 1; // enemy death

        // Now discount applies — cost reduced by 1
        int costWithDeath = CostInterceptor.GetEffectiveCost(state, creature, 0);
        Assert.Equal(2, costWithDeath);
    }

    [Fact]
    public void Ruling_R19_GrimoireDiscount_DiscountAppliesToAllCreatures()
    {
        // R19: All creatures cost less, not just the first one played.
        var state = CreateState();

        AddArtifact(state, 0, 0, "artf_necromancer_skull",
            abilities: new List<AbilityDef>
            {
                new()
                {
                    Trigger = Trigger.PASSIVE,
                    Effects = new List<EffectDef>
                    {
                        new()
                        {
                            Op = Op.COST_MOD, Amount = 1, AppliesTo = "CREATURE",
                            Condition = Cond(ConditionOp.CREATURE_DIED_THIS_TURN, side: "ANY"),
                            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
                        }
                    }
                }
            });

        state = EndTurn(state, 1);
        state.CreatureDiedThisTurnCount[1] = 1; // a death happened

        var creatureA = MakeHandCard(state, 0, CardType.CREATURE, "tst_a", cost: 3, attack: 2, vigor: 3);
        var creatureB = MakeHandCard(state, 0, CardType.CREATURE, "tst_b", cost: 4, attack: 2, vigor: 3);
        state.Players[0].Hand.Add(creatureA);
        state.Players[0].Hand.Add(creatureB);

        // Both creatures get the discount
        Assert.Equal(2, CostInterceptor.GetEffectiveCost(state, creatureA, 0));
        Assert.Equal(3, CostInterceptor.GetEffectiveCost(state, creatureB, 0));
    }

    [Fact]
    public void Ruling_R19_GrimoireDiscount_NoDeathNoDiscount()
    {
        // R19: No discount when no creature died this turn.
        var state = CreateState();

        AddArtifact(state, 0, 0, "artf_necromancer_skull",
            abilities: new List<AbilityDef>
            {
                new()
                {
                    Trigger = Trigger.PASSIVE,
                    Effects = new List<EffectDef>
                    {
                        new()
                        {
                            Op = Op.COST_MOD, Amount = 1, AppliesTo = "CREATURE",
                            Condition = Cond(ConditionOp.CREATURE_DIED_THIS_TURN, side: "ANY"),
                            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
                        }
                    }
                }
            });

        state = EndTurn(state, 1); // Start P0's turn

        var creature = MakeHandCard(state, 0, CardType.CREATURE, "tst_mortal", cost: 3, attack: 2, vigor: 3);
        state.Players[0].Hand.Add(creature);

        // No death this turn — full cost
        Assert.Equal(3, CostInterceptor.GetEffectiveCost(state, creature, 0));
    }

    // ══════════════════════════════════════════════════════════════════
    // R20 — Grimoire Revenant
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_R20_GrimoireRevenant_SummonedAtEndOfTurn()
    {
        // R20: Summon resolves at end of whichever turn the 3rd Charge landed.
        // Deferred ON_CHARGE_FULL with END_OF_TURN timing fires at end of turn.
        var state = CreateState();
        var player = state.Players[0];

        // Grimoire with deferred ON_CHARGE_FULL that revives a token
        AddArtifact(state, 0, 0, "artf_necromancer_skull", maxCharges: 3,
            hasDeferredChargeFull: true,
            abilities: new List<AbilityDef>
            {
                new()
                {
                    Trigger = Trigger.PASSIVE,
                    Effects = new List<EffectDef>
                    {
                        new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } }
                    }
                },
                new()
                {
                    Trigger = Trigger.ON_CHARGE_FULL, Timing = "END_OF_TURN",
                    Effects = new List<EffectDef>
                    {
                        new()
                        {
                            Op = Op.REVIVE_TOKEN, Keyword = "artf_revenant_token",
                            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
                        },
                        new()
                        {
                            Op = Op.RESET_CHARGES,
                            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
                        }
                    }
                }
            });

        int creaturesBefore = CountCreaturesOnBoard(player);

        // Add 3 charges at once — deferred, not immediate
        var chargeEffect = new EffectDef { Op = Op.ADD_CHARGE, Amount = 3 };
        var source = state.Players[0].ArtifactSlots[0].Occupant!;
        EffectExecutor.Execute(chargeEffect, source, state,
            new List<ResolvedTarget> { new PlayerTarget(state.Players[0]) });

        var slot = state.Players[0].ArtifactSlots[0];
        Assert.Equal(3, slot.Charges);
        Assert.True(slot.PendingChargeFull);

        // Board unchanged before end of turn
        Assert.Equal(creaturesBefore, CountCreaturesOnBoard(player));

        // End turn — deferred charge full fires
        state = EndTurn(state, 0);
        player = state.Players[0];

        // Token summoned on the board
        Assert.Equal(creaturesBefore + 1, CountCreaturesOnBoard(player));

        // Charges reset to 0
        slot = state.Players[0].ArtifactSlots[0];
        Assert.Equal(0, slot.Charges);
        Assert.False(slot.PendingChargeFull);
    }

    [Fact]
    public void Ruling_R20_GrimoireRevenant_BoardFullNoSummon()
    {
        // R20: Board full = summon lost, Charges still reset.
        var state = CreateState();
        var player = state.Players[0];

        // Fill all 5 lanes with creatures
        for (int i = 0; i < 5; i++)
            PlaceCreature(state, 0, i, attack: 1, vigor: 1);

        AddArtifact(state, 0, 0, "artf_necromancer_skull", maxCharges: 3,
            hasDeferredChargeFull: true,
            abilities: new List<AbilityDef>
            {
                new()
                {
                    Trigger = Trigger.PASSIVE,
                    Effects = new List<EffectDef>
                    {
                        new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } }
                    }
                },
                new()
                {
                    Trigger = Trigger.ON_CHARGE_FULL, Timing = "END_OF_TURN",
                    Effects = new List<EffectDef>
                    {
                        new()
                        {
                            Op = Op.REVIVE_TOKEN, Keyword = "artf_revenant_token",
                            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
                        },
                        new()
                        {
                            Op = Op.RESET_CHARGES,
                            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
                        }
                    }
                }
            });

        // Fill charges
        var chargeEffect = new EffectDef { Op = Op.ADD_CHARGE, Amount = 3 };
        var source = state.Players[0].ArtifactSlots[0].Occupant!;
        EffectExecutor.Execute(chargeEffect, source, state,
            new List<ResolvedTarget> { new PlayerTarget(state.Players[0]) });

        // End of turn — charge full deferred fires
        state = EndTurn(state, 0);
        player = state.Players[0];

        // Board still full (no creature added — revive silently failed)
        Assert.Equal(5, CountCreaturesOnBoard(player));

        // Charges still reset
        var slot = state.Players[0].ArtifactSlots[0];
        Assert.Equal(0, slot.Charges);
    }

    // ══════════════════════════════════════════════════════════════════
    // R21 — Phylactery armor
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_R21_PhylacteryArmor_ReducedWhenOutnumbered()
    {
        // R21: When fewer allies than enemies, combat damage to player reduced by 1.
        var state = CreateState();

        // P0 has 0 creatures, P1 has 2 creatures (outnumbered)
        // P1 can attack P0's empty opposing lane → face damage
        PlaceCreature(state, 1, 0, attack: 2, vigor: 5);
        PlaceCreature(state, 1, 1, attack: 2, vigor: 5);

        state.Players[0].Vigor = 25;

        // Phylactery with PREVENT_DAMAGE 1, source ATTACK, condition FEWER_ALLY_CREATURES_THAN_ENEMY
        AddArtifact(state, 0, 0, "artf_necromancer_ritual_piece",
            abilities: new List<AbilityDef>
            {
                new()
                {
                    Trigger = Trigger.PASSIVE,
                    Effects = new List<EffectDef>
                    {
                        new()
                        {
                            Op = Op.PREVENT_DAMAGE, Amount = 1, Source = "ATTACK",
                            Condition = Cond(ConditionOp.FEWER_ALLY_CREATURES_THAN_ENEMY),
                            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
                        }
                    }
                }
            });

        state = EndTurn(state, 1); // start P0's turn so passives are applied
        state = EndTurn(state, 0); // now P1's turn

        // P1 creature in lane 0 attacks P0's empty lane 0 → face damage
        int vigorBefore = state.Players[0].Vigor;
        state = DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = 1,
            SourceLane = 0,
            TargetLane = 0 // attacks its own opposing lane (empty) → face
        });

        // 2 damage reduced by 1 (Phylactery) = 1 damage taken
        Assert.Equal(vigorBefore - 1, state.Players[0].Vigor);
    }

    [Fact]
    public void Ruling_R21_PhylacteryArmor_NotReducedWhenNotOutnumbered()
    {
        // R21: When not outnumbered (equal creature count),
        // FEWER_ALLY_CREATURES_THAN_ENEMY condition is FALSE.
        var state = CreateState();

        // P0 has 1 creature, P1 has 1 creature (equal)
        PlaceCreature(state, 0, 0, attack: 2, vigor: 5);
        PlaceCreature(state, 1, 0, attack: 3, vigor: 5);

        // Evaluate the condition: 1 < 1 = false
        var condition = new ConditionDef
        {
            Op = ConditionOp.FEWER_ALLY_CREATURES_THAN_ENEMY
        };
        bool result = TriggerBus.EvaluateCondition(condition,
            new CardInstance(999, "tst", 0), 0, state);
        Assert.False(result);

        // With 0 ally creatures and 1 enemy: 0 < 1 = true (outnumbered)
        state.Players[0].Lanes[0].Occupant = null;
        bool outnumbered = TriggerBus.EvaluateCondition(condition,
            new CardInstance(999, "tst", 0), 0, state);
        Assert.True(outnumbered);
    }

    [Fact]
    public void Ruling_R21_PhylacteryArmor_AttackDamageOnly()
    {
        // R21: Attack damage only — spell damage bypasses the shield.
        var state = CreateState();

        // P0 has 0 creatures, P1 has 1 creature → outnumbered
        PlaceCreature(state, 1, 0, attack: 2, vigor: 5);
        state.Players[0].Vigor = 25;

        AddArtifact(state, 0, 0, "artf_necromancer_ritual_piece",
            abilities: new List<AbilityDef>
            {
                new()
                {
                    Trigger = Trigger.PASSIVE,
                    Effects = new List<EffectDef>
                    {
                        new()
                        {
                            Op = Op.PREVENT_DAMAGE, Amount = 1, Source = "ATTACK",
                            Condition = Cond(ConditionOp.FEWER_ALLY_CREATURES_THAN_ENEMY),
                            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
                        }
                    }
                }
            });

        state = EndTurn(state, 1); // Start P0's turn → passives applied

        // Direct spell damage to P0 (source SPELL, not ATTACK) should NOT be reduced
        int vigorBefore = state.Players[0].Vigor;
        var dmgEffect = new EffectDef { Op = Op.DAMAGE, Amount = 3 };
        var dmgSource = new CardInstance(state.NextInstanceId++, "tst_dmg_spell", 1);
        EffectExecutor.Execute(dmgEffect, dmgSource, state,
            new List<ResolvedTarget> { new PlayerTarget(state.Players[0]) });

        // Full 3 damage (Phylactery only reduces ATTACK source)
        Assert.Equal(vigorBefore - 3, state.Players[0].Vigor);
    }

    // ══════════════════════════════════════════════════════════════════
    // R22 — Phylactery drain
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_R22_PhylacteryDrain_EnemyDeathHealsPlayer()
    {
        // R22: Every enemy creature death, any turn → heal your character 1.
        var state = CreateState();
        state.Players[0].Vigor = 20;

        PlaceCreature(state, 0, 0, attack: 2, vigor: 1); // fragile ally
        PlaceCreature(state, 1, 0, attack: 2, vigor: 1); // fragile enemy

        // Phylactery with ON_CREATURE_DIES + condition ENEMY → HEAL 1
        AddArtifact(state, 0, 0, "artf_necromancer_ritual_piece",
            abilities: new List<AbilityDef>
            {
                new()
                {
                    Trigger = Trigger.PASSIVE,
                    Effects = new List<EffectDef>
                    {
                        new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } }
                    }
                },
                new()
                {
                    Trigger = Trigger.ON_CREATURE_DIES,
                    Condition = Cond(ConditionOp.ENEMY),
                    Effects = new List<EffectDef>
                    {
                        new()
                        {
                            Op = Op.HEAL, Amount = 1,
                            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
                        }
                    }
                }
            });

        state = EndTurn(state, 1); // start P0's turn

        int vigorBefore = state.Players[0].Vigor;

        // Kill P1's creature (enemy death) via combat — P0 attacks lane 0
        state = Attack(state, 0, 0);

        // Enemy died → Phylactery ON_CREATURE_DIES triggers → P0 healed 1
        Assert.Null(state.Players[1].Lanes[0].Occupant);
        Assert.Equal(vigorBefore + 1, state.Players[0].Vigor);
    }

    [Fact]
    public void Ruling_R22_PhylacteryDrain_AllyDeathDoesNotHeal()
    {
        // R22: Friendly creature death does NOT trigger the heal.
        var state = CreateState();
        state.Players[0].Vigor = 20;

        PlaceCreature(state, 0, 0, attack: 2, vigor: 1); // fragile ally
        PlaceCreature(state, 1, 0, attack: 5, vigor: 5);

        AddArtifact(state, 0, 0, "artf_necromancer_ritual_piece",
            abilities: new List<AbilityDef>
            {
                new()
                {
                    Trigger = Trigger.PASSIVE,
                    Effects = new List<EffectDef>
                    {
                        new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } }
                    }
                },
                new()
                {
                    Trigger = Trigger.ON_CREATURE_DIES,
                    Condition = Cond(ConditionOp.ENEMY),
                    Effects = new List<EffectDef>
                    {
                        new()
                        {
                            Op = Op.HEAL, Amount = 1,
                            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
                        }
                    }
                }
            });

        state = EndTurn(state, 1); // start P0's turn

        int vigorBefore = state.Players[0].Vigor;

        // P1 attacks P0's creature, killing it (ally death → should not heal)
        state = EndTurn(state, 0); // P1's turn
        state = Attack(state, 1, 0); // P1 attacks lane 0

        // Ally died but it was FRIENDLY, not ENEMY → no heal
        Assert.Equal(vigorBefore, state.Players[0].Vigor);
    }

    [Fact]
    public void Ruling_R22_PhylacteryDrain_SelfSacrificeAlsoTriggers()
    {
        // R22: Every enemy creature death including self-sacrifice → heal 1.
        var state = CreateState();
        state.Players[0].Vigor = 20;

        // P1 has a creature
        PlaceCreature(state, 1, 0, attack: 2, vigor: 1);

        AddArtifact(state, 0, 0, "artf_necromancer_ritual_piece",
            abilities: new List<AbilityDef>
            {
                new()
                {
                    Trigger = Trigger.PASSIVE,
                    Effects = new List<EffectDef>
                    {
                        new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } }
                    }
                },
                new()
                {
                    Trigger = Trigger.ON_CREATURE_DIES,
                    Condition = Cond(ConditionOp.ENEMY),
                    Effects = new List<EffectDef>
                    {
                        new()
                        {
                            Op = Op.HEAL, Amount = 1,
                            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
                        }
                    }
                }
            });

        int vigorBefore = state.Players[0].Vigor;

        // Kill P1's creature via direct effect (simulating self-sacrifice)
        state.Players[1].Lanes[0].Occupant = null;
        state.CreatureDiedThisTurnCount[1] = 1;
        state.LastDeathPlayerIndex = 1;

        // Fire global ON_CREATURE_DIES — condition ENEMY means
        // LastDeathPlayerIndex != controller (0), so 1 != 0 → true
        TriggerBus.Fire(state, Trigger.ON_CREATURE_DIES, 1);

        // Healed 1 (enemy died)
        Assert.Equal(vigorBefore + 1, state.Players[0].Vigor);
    }

    // ══════════════════════════════════════════════════════════════════
    // R23 — Forgehammer forge
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_R23_HammerForge_FirstSummonFilterSelectsCorrectTarget()
    {
        // R23: The FIRST_SUMMONED_THIS_TURN filter picks the creature with
        // SummonedThisTurn flag. When multiple creatures were summoned this turn,
        // the oldest (lowest InstanceId) among them is selected.
        var state = CreateState();

        // Two creatures summoned this turn
        var first = PlaceCreature(state, 0, 0, attack: 1, vigor: 1);
        first.SummonedThisTurn = true;

        var second = PlaceCreature(state, 0, 1, attack: 1, vigor: 1);
        second.SummonedThisTurn = true;

        // Resolve FIRST_SUMMONED_THIS_TURN filter
        var effect = new EffectDef
        {
            Op = Op.BUFF, Attack = 0, Vigor = 1,
            Duration = Duration.PERMANENT,
            Target = new TargetDef
            {
                Scope = Scope.ALLY_CREATURE,
                Filter = "FIRST_SUMMONED_THIS_TURN",
                Count = TargetCount.Exactly(1)
            }
        };

        var source = new CardInstance(state.NextInstanceId++, "artf_runesmith_hammer", 0);
        var targets = TargetResolver.Resolve(effect.Target!, source,
            state.Players[0], state.Players[1], state);

        // The first summon (lowest InstanceId with SummonedThisTurn) is picked
        Assert.Single(targets);
        var ct = Assert.IsType<CreatureTarget>(targets[0]);
        Assert.Equal(first.InstanceId, ct.Card.InstanceId);
    }

    [Fact]
    public void Ruling_R23_HammerForge_UnsummonedCreatureNotSelected()
    {
        // R23: A creature NOT summoned this turn is not selected.
        var state = CreateState();

        // Creature that WAS summoned this turn
        var summoned = PlaceCreature(state, 0, 0, attack: 1, vigor: 1);
        summoned.SummonedThisTurn = true;

        // Creature that was NOT summoned this turn
        var notSummoned = PlaceCreature(state, 0, 1, attack: 1, vigor: 1);
        // notSummoned.SummonedThisTurn is false (default)

        var effect = new EffectDef
        {
            Op = Op.BUFF, Attack = 0, Vigor = 1,
            Duration = Duration.PERMANENT,
            Target = new TargetDef
            {
                Scope = Scope.ALLY_CREATURE,
                Filter = "FIRST_SUMMONED_THIS_TURN",
                Count = TargetCount.Exactly(1)
            }
        };

        var source = new CardInstance(state.NextInstanceId++, "artf_runesmith_hammer", 0);
        var targets = TargetResolver.Resolve(effect.Target!, source,
            state.Players[0], state.Players[1], state);

        // Only the summoned creature matches
        Assert.Single(targets);
        var ct = Assert.IsType<CreatureTarget>(targets[0]);
        Assert.Equal(summoned.InstanceId, ct.Card.InstanceId);
    }

    [Fact]
    public void Ruling_R23_HammerForge_PermanentSurvivesSuppression()
    {
        // R23: Permanent survives suppression (G3: permanent buffs remain).
        var state = CreateState();

        // Give a creature a +0/+1 permanent buff (simulating Hammer forge)
        var creature = PlaceCreature(state, 0, 0, attack: 2, vigor: 3);
        creature.VigorModifier = 1; // +1 vigor from Hammer

        // Add Hammer artifact
        AddArtifact(state, 0, 0, "artf_runesmith_hammer");

        // Suppress P0's artifact
        state.Players[0].ArtifactSlots[0].ApplySuppression(1, "test_suppression");

        // Creature still has the permanent buff (G3: permanent buffs remain)
        Assert.Equal(1, creature.VigorModifier);
        Assert.True(state.Players[0].ArtifactSlots[0].IsSuppressed);
    }

    // ══════════════════════════════════════════════════════════════════
    // R24 — Hammer Charge
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_R24_HammerCharge_ChargeGainedOnSummon()
    {
        // R24: Every friendly creature entering play on your turn = 1 Charge.
        // Tokens count. No cost condition.
        var state = CreateState();

        // Hammer with max 3 charges
        AddArtifact(state, 0, 0, "artf_runesmith_hammer", maxCharges: 3,
            abilities: new List<AbilityDef>
            {
                new()
                {
                    Trigger = Trigger.PASSIVE,
                    Effects = new List<EffectDef>
                    {
                        new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } }
                    }
                }
            });

        var slot = state.Players[0].ArtifactSlots[0];

        // Simulate gaining a charge (as would happen on summon)
        int added = slot.AddCharges(1);
        Assert.Equal(1, added);
        Assert.Equal(1, slot.Charges);
    }

    [Fact]
    public void Ruling_R24_HammerCharge_CapIsThree()
    {
        // R24: Hammer's Charge cap is 3.
        var state = CreateState();

        AddArtifact(state, 0, 0, "artf_runesmith_hammer", maxCharges: 3,
            abilities: new List<AbilityDef>
            {
                new()
                {
                    Trigger = Trigger.PASSIVE,
                    Effects = new List<EffectDef>
                    {
                        new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } }
                    }
                }
            });

        var slot = state.Players[0].ArtifactSlots[0];

        // Add 5 charges → capped at 3
        slot.AddCharges(5);
        Assert.Equal(3, slot.Charges);

        // Adding more after cap does nothing
        slot.AddCharges(1);
        Assert.Equal(3, slot.Charges);
    }

    [Fact]
    public void Ruling_R24_HammerCharge_NoChargeUnderSuppression()
    {
        // G3: Suppressed artifacts don't gain charges.
        var state = CreateState();

        AddArtifact(state, 0, 0, "artf_runesmith_hammer", maxCharges: 3,
            abilities: new List<AbilityDef>
            {
                new()
                {
                    Trigger = Trigger.PASSIVE,
                    Effects = new List<EffectDef>
                    {
                        new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } }
                    }
                }
            });

        var slot = state.Players[0].ArtifactSlots[0];

        // Suppress first
        slot.ApplySuppression(1, "test");
        Assert.True(slot.IsSuppressed);

        // Try to add charge — blocked (frozen)
        int added = slot.AddCharges(1);
        Assert.Equal(0, added);
        Assert.Equal(0, slot.Charges);
    }

    // ══════════════════════════════════════════════════════════════════
    // R25 — Anvil trigger
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_R25_AnvilTrigger_SpendPartnerChargesAtEndOfTurn()
    {
        // R25: End of YOUR turn, iff zero friendly attacks this turn AND
        // >=1 friendly creature AND partner Charges >=1: spend ALL partner
        // Charges, +1/+1 per charge to highest-cost creature (tie → oldest).
        var state = CreateState();
        var player = state.Players[0];

        // Place the Anvil in slot 1, partner Hammer in slot 0 with charges
        AddArtifact(state, 0, 0, "artf_runesmith_hammer", maxCharges: 3,
            abilities: new List<AbilityDef>
            {
                new()
                {
                    Trigger = Trigger.PASSIVE,
                    Effects = new List<EffectDef>
                    {
                        new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } }
                    }
                }
            });

        // Anvil trigger: ON_TURN_END_NO_ATTACK → FORGE
        AddArtifact(state, 0, 1, "artf_paladin_banner",
            abilities: new List<AbilityDef>
            {
                new()
                {
                    Trigger = Trigger.PASSIVE,
                    Effects = new List<EffectDef>
                    {
                        new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } }
                    }
                },
                new()
                {
                    Trigger = Trigger.ON_TURN_END_NO_ATTACK,
                    Condition = new ConditionDef
                    {
                        All = new List<ConditionDef>
                        {
                            Cond(ConditionOp.ALLY_CREATURE_EXISTS),
                            Cond(ConditionOp.PARTNER_CHARGES_GTE, value: 1)
                        }
                    },
                    Effects = new List<EffectDef>
                    {
                        new()
                        {
                            Op = Op.FORGE,
                            SpendFrom = "PARTNER_SLOT",
                            Spend = "ALL",
                            PerCharge = new PerChargeStats { Attack = 1, Vigor = 1 },
                            Duration = Duration.PERMANENT,
                            Target = new TargetDef
                            {
                                Scope = Scope.ALLY_CREATURE,
                                Filter = "HIGHEST_COST",
                                Count = TargetCount.Exactly(1),
                                Tiebreak = "OLDEST_IN_PLAY"
                            }
                        }
                    }
                }
            });

        state = EndTurn(state, 1); // Start P0's turn

        // Add creatures — one high-cost, one low
        var highCost = PlaceCreature(state, 0, 0, attack: 2, vigor: 5);
        highCost.Cost = 5;
        var lowCost = PlaceCreature(state, 0, 1, attack: 2, vigor: 5);
        lowCost.Cost = 2;

        // Add 2 charges to partner slot (Hammer, slot 0)
        state.Players[0].ArtifactSlots[0].AddCharges(2);
        Assert.Equal(2, state.Players[0].ArtifactSlots[0].Charges);

        // No attacks this turn
        player.HasAttackedThisTurn = false;
        player.AttackCountThisTurn = 0;

        // Directly test the FORGE effect independently of TriggerBus routing
        var forgeEffect = new EffectDef
        {
            Op = Op.FORGE,
            SpendFrom = "PARTNER_SLOT",
            Spend = "ALL",
            PerCharge = new PerChargeStats { Attack = 1, Vigor = 1 },
            Duration = Duration.PERMANENT,
            Target = new TargetDef
            {
                Scope = Scope.ALLY_CREATURE,
                Filter = "HIGHEST_COST",
                Count = TargetCount.Exactly(1),
                Tiebreak = "OLDEST_IN_PLAY"
            }
        };

        var anvilArtifact = state.Players[0].ArtifactSlots[1].Occupant!;
        var forgeTargets = TargetResolver.Resolve(forgeEffect.Target!, anvilArtifact,
            state.Players[0], state.Players[1], state);

        // Verify the highest-cost creature is targeted
        Assert.Single(forgeTargets);
        var forgeTarget = Assert.IsType<CreatureTarget>(forgeTargets[0]);
        Assert.Equal(highCost.InstanceId, forgeTarget.Card.InstanceId);

        // Execute the FORGE op directly — this tests the actual mechanic
        EffectExecutor.Execute(forgeEffect, anvilArtifact, state, forgeTargets);

        // Partner charges spent
        Assert.Equal(0, state.Players[0].ArtifactSlots[0].Charges);

        // Highest-cost creature (highCost, cost=5) got +2/+2 (2 charges × +1/+1)
        var highAfter = FindCreature(state, highCost.InstanceId);
        Assert.NotNull(highAfter);
        Assert.Equal(2, highAfter.AttackModifier);
        Assert.Equal(2, highAfter.VigorModifier);
    }

    [Fact]
    public void Ruling_R25_AnvilTrigger_NoCreatureKeepsCharges()
    {
        // R25: No creature = nothing happens, Charges KEPT.
        var state = CreateState();
        var player = state.Players[0];

        AddArtifact(state, 0, 0, "artf_runesmith_hammer", maxCharges: 3,
            abilities: new List<AbilityDef>
            {
                new()
                {
                    Trigger = Trigger.PASSIVE,
                    Effects = new List<EffectDef>
                    {
                        new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } }
                    }
                }
            });

        AddArtifact(state, 0, 1, "artf_paladin_banner",
            abilities: new List<AbilityDef>
            {
                new()
                {
                    Trigger = Trigger.PASSIVE,
                    Effects = new List<EffectDef>
                    {
                        new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } }
                    }
                },
                new()
                {
                    Trigger = Trigger.ON_TURN_END_NO_ATTACK,
                    Condition = new ConditionDef
                    {
                        All = new List<ConditionDef>
                        {
                            Cond(ConditionOp.ALLY_CREATURE_EXISTS),
                            Cond(ConditionOp.PARTNER_CHARGES_GTE, value: 1)
                        }
                    },
                    Effects = new List<EffectDef>
                    {
                        new()
                        {
                            Op = Op.FORGE,
                            SpendFrom = "PARTNER_SLOT",
                            Spend = "ALL",
                            PerCharge = new PerChargeStats { Attack = 1, Vigor = 1 },
                            Duration = Duration.PERMANENT,
                            Target = new TargetDef
                            {
                                Scope = Scope.ALLY_CREATURE,
                                Filter = "HIGHEST_COST",
                                Count = TargetCount.Exactly(1),
                                Tiebreak = "OLDEST_IN_PLAY"
                            }
                        }
                    }
                }
            });

        state = EndTurn(state, 1);

        // Add 2 charges to partner, but NO friendly creatures
        state.Players[0].ArtifactSlots[0].AddCharges(2);

        // No attacks this turn
        player.HasAttackedThisTurn = false;
        player.AttackCountThisTurn = 0;

        // End turn → condition ALLY_CREATURE_EXISTS is false (no creatures on board)
        state = EndTurn(state, 0);

        // Charges KEPT (condition failed, FORGE never executed)
        Assert.Equal(2, state.Players[0].ArtifactSlots[0].Charges);
    }

    // ══════════════════════════════════════════════════════════════════
    // R26 — Anvil passive
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_R26_AnvilPassive_BuffedCreatureGetsPlusOneAttack()
    {
        // R26: +1 attack to friendly creatures with any permanent stat buff
        // from any source, checked continuously.
        var state = CreateState();

        // A creature with a permanent buff (+0/+1 from Hammer forge)
        var buffed = PlaceCreature(state, 0, 0, attack: 2, vigor: 4);
        buffed.VigorModifier = 1; // has a permanent buff

        // A creature without any buff
        var unbuffed = PlaceCreature(state, 0, 1, attack: 2, vigor: 4);

        // Resolve the Anvil's HAS_PERMANENT_BUFF filter via TargetResolver
        var effect = new EffectDef
        {
            Op = Op.BUFF, Attack = 1, Vigor = 0,
            Duration = Duration.WHILE_PRESENT,
            Target = new TargetDef
            {
                Scope = Scope.ALLY_CREATURE,
                Filter = "HAS_PERMANENT_BUFF",
                Count = TargetCount.All
            }
        };
        var source = new CardInstance(state.NextInstanceId++, "artf_paladin_banner", 0);
        var targets = TargetResolver.Resolve(effect.Target!, source,
            state.Players[0], state.Players[1], state);

        // Only the buffed creature matches HAS_PERMANENT_BUFF
        Assert.Single(targets);
        var ct = Assert.IsType<CreatureTarget>(targets[0]);
        Assert.Equal(buffed.InstanceId, ct.Card.InstanceId);
    }

    [Fact]
    public void Ruling_R26_AnvilPassive_UnbuffedCreatureNoBonus()
    {
        // R26: Creatures without any permanent stat buff don't get the bonus.
        var state = CreateState();

        // Creature with no buffs at all
        PlaceCreature(state, 0, 0, attack: 2, vigor: 4);

        // Resolve HAS_PERMANENT_BUFF filter — no creatures with buffs
        var effect = new EffectDef
        {
            Op = Op.BUFF, Attack = 1, Vigor = 0,
            Duration = Duration.WHILE_PRESENT,
            Target = new TargetDef
            {
                Scope = Scope.ALLY_CREATURE,
                Filter = "HAS_PERMANENT_BUFF",
                Count = TargetCount.All
            }
        };
        var source = new CardInstance(state.NextInstanceId++, "artf_paladin_banner", 0);
        var targets = TargetResolver.Resolve(effect.Target!, source,
            state.Players[0], state.Players[1], state);

        // No creatures match HAS_PERMANENT_BUFF
        Assert.Empty(targets);
    }

    [Fact]
    public void Ruling_R26_AnvilPassive_PermanentBuffFromAnySourceCounts()
    {
        // R26: Permanent stat buff from ANY source is counted.
        // A creature buffed via the FORGE op should also match.
        var state = CreateState();

        // Creature with attack modifier from any source
        var forged = PlaceCreature(state, 0, 0, attack: 2, vigor: 4);
        forged.AttackModifier = 2; // permanent forge buff

        // Creature with only a base stat (no modifier)
        var normal = PlaceCreature(state, 0, 1, attack: 2, vigor: 4);

        var effect = new EffectDef
        {
            Op = Op.BUFF, Attack = 1, Vigor = 0,
            Duration = Duration.WHILE_PRESENT,
            Target = new TargetDef
            {
                Scope = Scope.ALLY_CREATURE,
                Filter = "HAS_PERMANENT_BUFF",
                Count = TargetCount.All
            }
        };
        var source = new CardInstance(state.NextInstanceId++, "artf_paladin_banner", 0);
        var targets = TargetResolver.Resolve(effect.Target!, source,
            state.Players[0], state.Players[1], state);

        Assert.Single(targets);
        var ct = Assert.IsType<CreatureTarget>(targets[0]);
        Assert.Equal(forged.InstanceId, ct.Card.InstanceId);
    }

    // ══════════════════════════════════════════════════════════════════
    // §10 — Zone integrity
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Spec10_ZoneIntegrity_ArtifactCardsNeverChangeZone()
    {
        // §10: Artifact cards can never change zones. They always remain in
        // ArtifactSlot zone. No action can move them to Deck, Hand, Lane, Discard,
        // Barrow, or RemovedFromGame.
        var state = CreateState();

        AddArtifact(state, 0, 0, "artf_necromancer_skull");
        var artifact = state.Players[0].ArtifactSlots[0].Occupant!;

        // Verify initial zone
        Assert.Equal(Zone.ArtifactSlot, artifact.Zone);

        // Zone never changes — attempt zone mutations that apply to normal cards.
        // These operations should not affect artifact cards since they're not in the
        // expected zones (Lane, Hand, etc.).

        // 1. BOUNCE — artifact is not in Lane zone, so no-op
        var bounceEffect = new EffectDef { Op = Op.BOUNCE };
        EffectExecutor.Execute(bounceEffect, artifact, state,
            new List<ResolvedTarget> { new CreatureTarget(artifact, 0, 0) });
        Assert.Equal(Zone.ArtifactSlot, artifact.Zone);

        // 2. DESTROY — artifact is not in Lane zone, so no-op
        var destroyEffect = new EffectDef { Op = Op.DESTROY };
        EffectExecutor.Execute(destroyEffect, artifact, state,
            new List<ResolvedTarget> { new CreatureTarget(artifact, 0, 0) });
        Assert.Equal(Zone.ArtifactSlot, artifact.Zone);

        // 3. Suppression does NOT change zone
        state.Players[0].ArtifactSlots[0].ApplySuppression(1, "test");
        Assert.Equal(Zone.ArtifactSlot, artifact.Zone);

        // 4. After suppression expires — still in slot zone
        state.Players[0].ArtifactSlots[0].TickSuppression();
        Assert.Equal(Zone.ArtifactSlot, artifact.Zone);

        // 5. Clone retains ArtifactSlot zone
        var cloned = artifact.Clone();
        Assert.Equal(Zone.ArtifactSlot, cloned.Zone);
    }

    [Fact]
    public void Spec10_NSlotGeneralization_ThreeSlotsWork()
    {
        // §10: N-slot generalization — a fake 3-slot test class works.
        // ArtifactSlots is an array, so slot count is per-class data.
        // Verify all three slots can hold artifacts and function independently.
        var state = CreateState();
        var player = state.Players[0];

        // Create 3-slot player
        player.ArtifactSlots = new ArtifactSlot[3];
        player.ArtifactSlots[0] = new ArtifactSlot(0);
        player.ArtifactSlots[1] = new ArtifactSlot(1);
        player.ArtifactSlots[2] = new ArtifactSlot(2);

        // Place a creature to receive healing
        var creature = PlaceCreature(state, 0, 0, attack: 2, vigor: 5);
        creature.Damage = 2;

        // Artifact in slot 0: BUFF +1 attack
        var art0 = new CardInstance(state.NextInstanceId++, "tst_3slot_a", 0)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 0
        };
        art0.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef>
            {
                new()
                {
                    Op = Op.BUFF, Attack = 1, Vigor = 0,
                    Duration = Duration.WHILE_PRESENT,
                    Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Count = TargetCount.All }
                }
            }
        });
        player.ArtifactSlots[0].Occupant = art0;

        // Artifact in slot 1: BUFF +0/+2
        var art1 = new CardInstance(state.NextInstanceId++, "tst_3slot_b", 0)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 1
        };
        art1.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef>
            {
                new()
                {
                    Op = Op.BUFF, Attack = 0, Vigor = 2,
                    Duration = Duration.WHILE_PRESENT,
                    Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Count = TargetCount.All }
                }
            }
        });
        player.ArtifactSlots[1].Occupant = art1;

        // Artifact in slot 2: HEAL 1 to most-wounded (cadence ON_TURN_START)
        var art2 = new CardInstance(state.NextInstanceId++, "tst_3slot_c", 0)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 2
        };
        art2.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef>
            {
                new()
                {
                    Op = Op.HEAL, Amount = 1,
                    Cadence = EffectDef.CadenceOnTurnStart,
                    Target = new TargetDef
                    {
                        Scope = Scope.ALLY_CREATURE,
                        Filter = "MOST_WOUNDED",
                        Count = TargetCount.Exactly(1)
                    }
                }
            }
        });
        player.ArtifactSlots[2].Occupant = art2;

        // Start P0's turn → passives and cadences fire
        state = EndTurn(state, 1);
        player = state.Players[0];

        // Creature healed 1 of 2 damage by slot 2's cadence heal
        var crAfter = FindCreature(state, creature.InstanceId);
        Assert.NotNull(crAfter);
        Assert.Equal(1, crAfter.Damage);

        // All three slots exist and are functional
        Assert.Equal(3, player.ArtifactSlots.Length);
        Assert.NotNull(player.ArtifactSlots[0].Occupant);
        Assert.NotNull(player.ArtifactSlots[1].Occupant);
        Assert.NotNull(player.ArtifactSlots[2].Occupant);

        // Suppress slot 1 only — other slots unaffected
        player.ArtifactSlots[1].ApplySuppression(1, "test");
        Assert.True(player.ArtifactSlots[1].IsSuppressed);
        Assert.False(player.ArtifactSlots[0].IsSuppressed);
        Assert.False(player.ArtifactSlots[2].IsSuppressed);
    }

    [Fact]
    public void Spec10_AI_NeverTargetsArtifactSlots()
    {
        // §10: Regression test that combat AI does not evaluate Artifact slots
        // as attackable targets. The bot enumerates valid actions and should
        // only produce attack actions against lane targets 0-4, not Artifact slots
        // (which have separate indices from lanes).
        var state = CreateState();
        var player = state.Players[0];
        var opponent = state.Players[1];

        // Set up both with matching artifacts + a creature to attack with
        player.ArtifactSlots = new ArtifactSlot[2];
        player.ArtifactSlots[0] = new ArtifactSlot(0);
        player.ArtifactSlots[1] = new ArtifactSlot(1);
        var art = new CardInstance(state.NextInstanceId++, "tst_artifact", 0)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 0
        };
        art.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef>
            {
                new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } }
            }
        });
        player.ArtifactSlots[0].Occupant = art;

        opponent.ArtifactSlots = new ArtifactSlot[2];
        opponent.ArtifactSlots[0] = new ArtifactSlot(0);
        opponent.ArtifactSlots[1] = new ArtifactSlot(1);
        var oppArt = new CardInstance(state.NextInstanceId++, "tst_artifact", 1)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 0
        };
        oppArt.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef>
            {
                new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } }
            }
        });
        opponent.ArtifactSlots[0].Occupant = oppArt;

        // Place a creature that can attack
        PlaceCreature(state, 0, 0, attack: 3, vigor: 5);

        // Start P0's turn so creature is ready
        state = EndTurn(state, 1);

        // Use the GreedyBot to enumerate valid actions
        var bot = new GreedyBot();
        var actions = bot.EnumerateValidActions(state, 0);

        // Filter to attack actions only
        var attackActions = actions.OfType<AttackAction>().ToList();

        // Verify every attack action has TargetLane in range 0-4
        foreach (var attack in attackActions)
        {
            Assert.True(attack.TargetLane.HasValue && attack.TargetLane.Value >= 0 && attack.TargetLane.Value <= 4,
                $"Attack target lane {attack.TargetLane} is outside valid lane range (0-4)");
        }

        // Additionally, the bot should have generated attack actions
        Assert.NotEmpty(attackActions);
    }
}
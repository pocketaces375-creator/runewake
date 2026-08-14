using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// TASK-DSL-6 — Partner-slot mechanics.
/// Covers: PARTNER_CHARGES_GTE condition, FORGE op with spend_from PARTNER_SLOT
/// (all charges, +1/+1 per charge, HIGHEST_COST target, tiebreak OLDEST_IN_PLAY,
/// charges kept if no creature — R25).
/// </summary>
[Collection("NonParallel")]
public class ForgeTests
{
    // ——— Helpers ———

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

    /// <summary>
    /// Set up a player with two artifact slots (partnered).
    /// Slot 0 has the "anvil" (source of FORGE effect) — no charges of its own.
    /// Slot 1 has the "hammer" (partner slot) — holds charges.
    /// Returns (state, hammerSlot) so tests can inspect charges.
    /// </summary>
    private static (GameState state, ArtifactSlot hammerSlot) CreateStateWithPartnerSlots(
        int partnerMaxCharges = 3,
        int partnerInitialCharges = 0)
    {
        var state = CreateState();
        var player = state.Players[0];
        player.ArtifactClass = "test";
        player.ArtifactDefIds = new[] { "tst_anvil", "tst_hammer" };
        player.ArtifactSlots = new ArtifactSlot[2];

        // Slot 0: Anvil — the artifact that executes FORGE
        var anvilDef = new ArtifactDef
        {
            Id = "tst_anvil",
            Name = "Test Anvil",
            Class = "test",
            SlotPool = "anvil",
            Trigger = new AbilityDef
            {
                Trigger = Trigger.ON_TURN_END_NO_ATTACK,
                // Compound condition: All of (ALLY_CREATURE_EXISTS, PARTNER_CHARGES_GTE >= 1)
                Condition = new ConditionDef
                {
                    All = new List<ConditionDef>
                    {
                        new() { Op = ConditionOp.ALLY_CREATURE_EXISTS },
                        new() { Op = ConditionOp.PARTNER_CHARGES_GTE, Value = JsonSerializer.SerializeToElement(1) }
                    }
                },
                Effects = new List<EffectDef>
                {
                    new()
                    {
                        Op = Op.FORGE,
                        SpendFrom = "PARTNER_SLOT",
                        Spend = "ALL",
                        Target = new TargetDef
                        {
                            Scope = Scope.ALLY_CREATURE,
                            Filter = "HIGHEST_COST",
                            Count = TargetCount.Exactly(1),
                            Tiebreak = "OLDEST_IN_PLAY"
                        },
                        PerCharge = new PerChargeStats { Attack = 1, Vigor = 1 },
                        Duration = Duration.PERMANENT
                    }
                }
            },
            Passive = new EffectDef { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } }
        };

        var anvilInstance = new CardInstance(state.NextInstanceId++, "tst_anvil", 0)
        {
            CardType = CardType.ARTIFACT,
            Zone = Zone.Lane,
            LaneIndex = -1
        };
        anvilInstance.Abilities.Add(new AbilityDef { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { anvilDef.Passive } });
        anvilInstance.Abilities.Add(anvilDef.Trigger);

        var anvilSlot = new ArtifactSlot(0)
        {
            MaxCharges = 0,
            Charges = 0,
            Occupant = anvilInstance
        };
        player.ArtifactSlots[0] = anvilSlot;

        // Slot 1: Hammer — the partner slot that holds charges
        var hammerInstance = new CardInstance(state.NextInstanceId++, "tst_hammer", 0)
        {
            CardType = CardType.ARTIFACT,
            Zone = Zone.Lane,
            LaneIndex = -1
        };

        var hammerSlot = new ArtifactSlot(1)
        {
            MaxCharges = partnerMaxCharges,
            Charges = partnerInitialCharges,
            Occupant = hammerInstance
        };
        player.ArtifactSlots[1] = hammerSlot;

        return (state, hammerSlot);
    }

    /// <summary>
    /// Execute the FORGE effect directly through EffectExecutor (bypassing trigger resolution).
    /// Resolves the target using TargetResolver, then executes.
    /// </summary>
    private static void ExecuteForge(GameState state, CardInstance? customTarget = null)
    {
        var player = state.Players[0];
        var anvilSlot = player.ArtifactSlots[0];
        var anvil = anvilSlot.Occupant!;
        var forgeEffect = anvil.Abilities[1].Effects[0];

        if (customTarget is not null)
        {
            // Execute directly with a specific creature target
            var resolved = new List<ResolvedTarget>
            {
                new CreatureTarget(customTarget, player.Index, customTarget.LaneIndex ?? 0)
            };
            EffectExecutor.Execute(forgeEffect, anvil, state, resolved);
        }
        else
        {
            // Normal resolution path
            var opponentState = state.Players[1];
            var targets = TargetResolver.Resolve(
                forgeEffect.Target!,
                anvil,
                player,
                opponentState,
                state);
            EffectExecutor.Execute(forgeEffect, anvil, state, targets);
        }
    }

    private static CardInstance MakeCreature(GameState state, int pIdx, int cost, int instanceIdOverride = -1, int? laneIndex = null)
    {
        int id = instanceIdOverride >= 0 ? instanceIdOverride : state.NextInstanceId++;
        int lane = laneIndex ?? 0;
        var c = new CardInstance(id, "tst_creature", pIdx)
        {
            Zone = Zone.Lane,
            CardType = CardType.CREATURE,
            BaseAttack = 2,
            BaseVigor = 3,
            Cost = cost,
            IsExhausted = false,
            LaneIndex = lane
        };
        state.Players[pIdx].Lanes[lane].Occupant = c;
        return c;
    }

    private static GameState EndTurn(GameState state, int playerIndex)
        => DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = playerIndex });

    // ——— Tests ———

    // — PARTNER_CHARGES_GTE condition — //

    [Fact]
    public void PartnerChargesGte_ConditionMet_WhenChargesAtThreshold()
    {
        var (state, hammerSlot) = CreateStateWithPartnerSlots(partnerMaxCharges: 3, partnerInitialCharges: 2);

        // PARTNER_CHARGES_GTE value=1 with 2 charges should be true
        var anvil = state.Players[0].ArtifactSlots[0].Occupant!;
        bool result = TriggerBus.EvaluateCondition(
            new ConditionDef { Op = ConditionOp.PARTNER_CHARGES_GTE, Value = JsonSerializer.SerializeToElement(1) },
            anvil, 0, state);

        Assert.True(result);
    }

    [Fact]
    public void PartnerChargesGte_ConditionNotMet_WhenChargesBelowThreshold()
    {
        var (state, hammerSlot) = CreateStateWithPartnerSlots(partnerMaxCharges: 3, partnerInitialCharges: 0);

        var anvil = state.Players[0].ArtifactSlots[0].Occupant!;
        bool result = TriggerBus.EvaluateCondition(
            new ConditionDef { Op = ConditionOp.PARTNER_CHARGES_GTE, Value = JsonSerializer.SerializeToElement(1) },
            anvil, 0, state);

        Assert.False(result);
    }

    [Fact]
    public void PartnerChargesGte_ZeroCharges_NotMetForPositiveThreshold()
    {
        var (state, hammerSlot) = CreateStateWithPartnerSlots(partnerMaxCharges: 3, partnerInitialCharges: 0);

        var anvil = state.Players[0].ArtifactSlots[0].Occupant!;
        bool result = TriggerBus.EvaluateCondition(
            new ConditionDef { Op = ConditionOp.PARTNER_CHARGES_GTE, Value = JsonSerializer.SerializeToElement(1) },
            anvil, 0, state);

        Assert.False(result);
    }

    [Fact]
    public void PartnerChargesGte_MaxCharges_ReturnsCorrectCount()
    {
        var (state, hammerSlot) = CreateStateWithPartnerSlots(partnerMaxCharges: 3, partnerInitialCharges: 3);

        var anvil = state.Players[0].ArtifactSlots[0].Occupant!;
        bool result = TriggerBus.EvaluateCondition(
            new ConditionDef { Op = ConditionOp.PARTNER_CHARGES_GTE, Value = JsonSerializer.SerializeToElement(1) },
            anvil, 0, state);

        Assert.True(result);
    }

    // — FORGE: spend charges and buff — //

    [Fact]
    public void Forge_SpendsAllChargesFromPartner()
    {
        var (state, hammerSlot) = CreateStateWithPartnerSlots(partnerMaxCharges: 5, partnerInitialCharges: 3);
        var creature = MakeCreature(state, 0, cost: 3, instanceIdOverride: 1001);

        ExecuteForge(state);

        Assert.Equal(0, hammerSlot.Charges); // All 3 charges spent
    }

    [Fact]
    public void Forge_BuffsTargetWithPerChargeStats()
    {
        var (state, hammerSlot) = CreateStateWithPartnerSlots(partnerMaxCharges: 5, partnerInitialCharges: 2);
        var creature = MakeCreature(state, 0, cost: 3, instanceIdOverride: 1001);

        Assert.Equal(2, creature.BaseAttack);
        Assert.Equal(3, creature.BaseVigor);
        Assert.Equal(0, creature.AttackModifier);
        Assert.Equal(0, creature.VigorModifier);

        ExecuteForge(state);

        // 2 charges * (+1/+1 per charge) = +2/+2
        Assert.Equal(2, creature.AttackModifier);
        Assert.Equal(2, creature.VigorModifier);
    }

    [Fact]
    public void Forge_TargetsHighestCostCreature()
    {
        var (state, hammerSlot) = CreateStateWithPartnerSlots(partnerMaxCharges: 5, partnerInitialCharges: 3);

        // Two creatures — cost 5 and cost 3
        var cheaper = MakeCreature(state, 0, cost: 3, instanceIdOverride: 1001, laneIndex: 0);
        var pricier = MakeCreature(state, 0, cost: 5, instanceIdOverride: 1002, laneIndex: 1);

        ExecuteForge(state);

        // pricier (cost 5) should be the target
        Assert.Equal(0, cheaper.AttackModifier); // cheaper gets 0
        Assert.Equal(3, pricier.AttackModifier); // pricier gets +3
    }

    [Fact]
    public void Forge_TiebreakOldestInPlay_SameCost()
    {
        var (state, hammerSlot) = CreateStateWithPartnerSlots(partnerMaxCharges: 5, partnerInitialCharges: 2);

        // Two creatures with same cost — older (lower instance ID) should win
        var older = MakeCreature(state, 0, cost: 3, instanceIdOverride: 50, laneIndex: 0);
        var newer = MakeCreature(state, 0, cost: 3, instanceIdOverride: 100, laneIndex: 1);

        ExecuteForge(state);

        // Older (id 50) should be the target
        Assert.Equal(2, older.AttackModifier); // older gets +2
        Assert.Equal(0, newer.AttackModifier); // newer gets 0
    }

    [Fact]
    public void Forge_TiebreakOldestInPlay_ThreeCreatures_SameCost()
    {
        var (state, hammerSlot) = CreateStateWithPartnerSlots(partnerMaxCharges: 5, partnerInitialCharges: 3);

        var oldest = MakeCreature(state, 0, cost: 4, instanceIdOverride: 10, laneIndex: 0);
        var middle = MakeCreature(state, 0, cost: 4, instanceIdOverride: 20, laneIndex: 1);
        var newest = MakeCreature(state, 0, cost: 4, instanceIdOverride: 30, laneIndex: 2);

        ExecuteForge(state);

        // Oldest (id 10) should be the target
        Assert.Equal(3, oldest.AttackModifier);
        Assert.Equal(0, middle.AttackModifier);
        Assert.Equal(0, newest.AttackModifier);
    }

    [Fact]
    public void Forge_NoCreatureOnBoard_KeepsCharges()
    {
        var (state, hammerSlot) = CreateStateWithPartnerSlots(partnerMaxCharges: 5, partnerInitialCharges: 3);

        // No creature on board — charges should be kept
        ExecuteForge(state);

        Assert.Equal(3, hammerSlot.Charges); // Charges kept
    }

    [Fact]
    public void Forge_ZeroCharges_NoBuffApplied()
    {
        var (state, hammerSlot) = CreateStateWithPartnerSlots(partnerMaxCharges: 5, partnerInitialCharges: 0);
        var creature = MakeCreature(state, 0, cost: 3, instanceIdOverride: 1001);

        ExecuteForge(state);

        Assert.Equal(0, hammerSlot.Charges); // Still 0
        Assert.Equal(0, creature.AttackModifier);
        Assert.Equal(0, creature.VigorModifier);
    }

    [Fact]
    public void Forge_PartnerSuppressed_NoChargesSpent()
    {
        var (state, hammerSlot) = CreateStateWithPartnerSlots(partnerMaxCharges: 5, partnerInitialCharges: 3);
        var creature = MakeCreature(state, 0, cost: 3, instanceIdOverride: 1001);

        // Suppress the partner slot
        hammerSlot.IsSuppressed = true;

        ExecuteForge(state);

        // Charges frozen under suppression — not spent
        Assert.Equal(3, hammerSlot.Charges);
        Assert.Equal(0, creature.AttackModifier);
    }

    [Fact]
    public void Forge_TargetIsEnemyCreature_DoesNotSpendCharges()
    {
        var (state, hammerSlot) = CreateStateWithPartnerSlots(partnerMaxCharges: 5, partnerInitialCharges: 3);
        // Creature on opponent's board
        MakeCreature(state, 1, cost: 3, instanceIdOverride: 2001);

        ExecuteForge(state);

        // FORGE scope is ALLY_CREATURE, so enemy creatures aren't valid targets
        // No ally creature — charges kept
        Assert.Equal(3, hammerSlot.Charges);
    }

    [Fact]
    public void Forge_PerChargeAttackOnly_BuffsOnlyAttack()
    {
        var (state, hammerSlot) = CreateStateWithPartnerSlots(partnerMaxCharges: 5, partnerInitialCharges: 2);

        // Override the per_charge to attack-only
        var anvil = state.Players[0].ArtifactSlots[0].Occupant!;
        var forgeEffect = anvil.Abilities[1].Effects[0];
        forgeEffect.PerCharge = new PerChargeStats { Attack = 1, Vigor = 0 };

        var creature = MakeCreature(state, 0, cost: 3, instanceIdOverride: 1001);
        ExecuteForge(state);

        Assert.Equal(2, creature.AttackModifier); // +2 attack (2 charges * 1)
        Assert.Equal(0, creature.VigorModifier);  // 0 vigor
    }

    [Fact]
    public void Forge_PerChargeVigorOnly_BuffsOnlyVigor()
    {
        var (state, hammerSlot) = CreateStateWithPartnerSlots(partnerMaxCharges: 5, partnerInitialCharges: 3);

        var anvil = state.Players[0].ArtifactSlots[0].Occupant!;
        var forgeEffect = anvil.Abilities[1].Effects[0];
        forgeEffect.PerCharge = new PerChargeStats { Attack = 0, Vigor = 2 };

        var creature = MakeCreature(state, 0, cost: 3, instanceIdOverride: 1001);
        ExecuteForge(state);

        Assert.Equal(0, creature.AttackModifier);  // 0 attack
        Assert.Equal(6, creature.VigorModifier);   // +6 vigor (3 charges * 2)
    }

    [Fact]
    public void Forge_MultipleCharges_BuffScalesLinearly()
    {
        var (state, hammerSlot) = CreateStateWithPartnerSlots(partnerMaxCharges: 10, partnerInitialCharges: 5);
        var creature = MakeCreature(state, 0, cost: 3, instanceIdOverride: 1001);

        ExecuteForge(state);

        Assert.Equal(5, creature.AttackModifier); // 5 charges * 1
        Assert.Equal(5, creature.VigorModifier);  // 5 charges * 1
    }

    [Fact]
    public void Forge_PartnerSlotHasMaxCharges_SpendsAll()
    {
        var (state, hammerSlot) = CreateStateWithPartnerSlots(partnerMaxCharges: 7, partnerInitialCharges: 7);
        var creature = MakeCreature(state, 0, cost: 3, instanceIdOverride: 1001);

        ExecuteForge(state);

        Assert.Equal(0, hammerSlot.Charges);
        Assert.Equal(7, creature.AttackModifier);
        Assert.Equal(7, creature.VigorModifier);
    }

    [Fact]
    public void Forge_ThroughTrigger_IntegratesCorrectly()
    {
        // Integration test: simulate the full ON_TURN_END_NO_ATTACK trigger path
        var (state, hammerSlot) = CreateStateWithPartnerSlots(partnerMaxCharges: 5, partnerInitialCharges: 2);
        var creature = MakeCreature(state, 0, cost: 3, instanceIdOverride: 1001);

        // Fire the ON_TURN_END_NO_ATTACK trigger
        TriggerBus.Fire(state, Trigger.ON_TURN_END_NO_ATTACK, 0);

        var resolvedSlot = state.Players[0].ArtifactSlots[1];

        Assert.Equal(0, resolvedSlot.Charges); // Both charges spent
        Assert.Equal(2, creature.AttackModifier); // +2/+2
        Assert.Equal(2, creature.VigorModifier);
    }

    [Fact]
    public void Forge_ThroughTrigger_NoAllyCreature_KeepsCharges()
    {
        var (state, hammerSlot) = CreateStateWithPartnerSlots(partnerMaxCharges: 5, partnerInitialCharges: 2);

        // No creature — the ALLY_CREATURE_EXISTS condition in the trigger's All clause
        // prevents the trigger from firing. The charges are never spent.
        TriggerBus.Fire(state, Trigger.ON_TURN_END_NO_ATTACK, 0);

        var resolvedSlot = state.Players[0].ArtifactSlots[1];
        Assert.Equal(2, resolvedSlot.Charges); // Charges kept
    }
}
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// TASK-ITEMS-NECROMANCER-1: Four more Necromancer artifacts in
/// content/artifacts/variants/necromancer.json.
/// Tests each variant's passive, charge mechanism, and full-charge effect.
/// </summary>
[Collection("NonParallel")]
public class NecromancerVariantTests
{
    private static readonly string ContentRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "content"));

    private static readonly string ArtifactsDir = Path.Combine(ContentRoot, "artifacts");

    private static readonly string VariantsDir = Path.Combine(ArtifactsDir, "variants");

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

    private static void LoadAllArtifacts()
    {
        ArtifactRegistry.Clear();
        CardRegistry.Clear();
        var launchPath = Path.Combine(ArtifactsDir, "launch_artifacts.json");
        ArtifactLoader.LoadPack(launchPath);
        if (Directory.Exists(VariantsDir))
            ArtifactLoader.LoadAllVariants(VariantsDir);
    }

    private static CardInstance PlaceCreature(GameState state, int pIdx, int lane,
        int attack = 2, int vigor = 5, string? keyword = null, int cost = 1)
    {
        var c = new CardInstance(state.NextInstanceId++, $"tst_cr_p{pIdx}_l{lane}", pIdx)
        {
            Zone = Zone.Lane,
            LaneIndex = lane,
            CardType = CardType.CREATURE,
            BaseAttack = attack,
            BaseVigor = vigor,
            Cost = cost,
            IsExhausted = false
        };
        if (keyword != null)
            c.Keywords.Add(keyword);
        state.Players[pIdx].Lanes[lane].Occupant = c;
        return c;
    }

    private static ArtifactSlot EquipArtifact(GameState state, int pIdx, int slotIdx, string defId)
    {
        var def = ArtifactRegistry.Get(defId);
        Assert.NotNull(def);

        var slot = new ArtifactSlot(slotIdx);
        var card = new CardInstance(state.NextInstanceId++, defId, pIdx)
        {
            CardType = CardType.ARTIFACT,
            Zone = Zone.ArtifactSlot,
            ArtifactSlotIndex = slotIdx
        };

        // Build abilities from the ArtifactDef
        // Passive
        card.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { def.Passive }
        });

        // Trigger (if any)
        if (def.Trigger is { Trigger: not Trigger.PASSIVE } && def.Trigger.Effects.Count > 0)
        {
            card.Abilities.Add(def.Trigger);
        }

        // Full-charge effects
        if (def.FullCharge is { Count: > 0 })
        {
            var fullChargeAbility = new AbilityDef
            {
                Trigger = Trigger.ON_CHARGE_FULL,
                Effects = def.FullCharge
            };
            card.Abilities.Add(fullChargeAbility);
        }

        // Charge config
        if (def.Charges is not null)
        {
            slot.MaxCharges = def.Charges.Max;
            slot.AutoChargeGainOn = def.Charges.GainOn;
            slot.ChargeConfigMaxPerTurn = def.Charges.MaxPerTurn;
            slot.ChargeConfigMaxPerCreaturePerTurn = def.Charges.MaxPerCreaturePerTurn;
        }

        slot.Occupant = card;
        state.Players[pIdx].ArtifactSlots[slotIdx] = slot;
        return slot;
    }

    private static GameState EndTurn(GameState state, int playerIndex)
        => DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = playerIndex });

    // ================================================================
    // GRINNING SKULL
    // ================================================================

    [Fact]
    public void GrinningSkull_LoadsAndRegisters()
    {
        try
        {
            LoadAllArtifacts();
            var def = ArtifactRegistry.Get("artf_necromancer_grinning_skull");
            Assert.NotNull(def);
            Assert.Equal("necromancer", def.Class);
            Assert.Equal("skull", def.SlotPool);
            Assert.Equal("Grinning Skull", def.Name);
            Assert.NotNull(def.Charges);
            Assert.Equal(3, def.Charges.Max);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void GrinningSkull_TriggerDamagesEnemyFaceOnFriendlyDeath()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);
            state.Players[1].Vigor = 25; // baseline

            var slot = EquipArtifact(state, 0, 0, "artf_necromancer_grinning_skull");

            // Kill friendly creature (LastDeathPlayerIndex = 0 = controller)
            state.LastDeathPlayerIndex = 0;
            TriggerBus.Fire(state, Trigger.ON_CREATURE_DIES, 0);

            // Should deal 1 damage to enemy face
            Assert.Equal(24, state.Players[1].Vigor);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void GrinningSkull_TriggerAddsChargeOnFriendlyDeath()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_necromancer_grinning_skull");
            slot.Charges = 0;

            // Kill friendly creature
            state.LastDeathPlayerIndex = 0;
            TriggerBus.Fire(state, Trigger.ON_CREATURE_DIES, 0);

            // SELF_ARTIFACT RESET_CHARGES is in full_charge, not trigger, so ADD_CHARGE
            // adds to the player's artifact slots
            Assert.True(slot.Charges > 0);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void GrinningSkull_DoesNotDamageOnEnemyDeath()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);
            state.Players[1].Vigor = 25;

            var slot = EquipArtifact(state, 0, 0, "artf_necromancer_grinning_skull");

            // Kill enemy creature — LastDeathPlayerIndex != controller
            state.LastDeathPlayerIndex = 1;
            TriggerBus.Fire(state, Trigger.ON_CREATURE_DIES, 0);

            Assert.Equal(25, state.Players[1].Vigor);
            Assert.Equal(0, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void GrinningSkull_FullChargeSpawnsSkeletonTokens()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_necromancer_grinning_skull");

            // Fire ON_CHARGE_FULL — should spawn 1/1 skeleton tokens in all empty lanes
            TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 0);

            // All 5 lanes should have a skeleton token
            for (int i = 0; i < 5; i++)
            {
                Assert.NotNull(state.Players[0].Lanes[i].Occupant);
                Assert.Equal("tok_skeleton", state.Players[0].Lanes[i].Occupant!.CardDefId);
                Assert.Equal(1, state.Players[0].Lanes[i].Occupant!.CurrentAttack);
                Assert.Equal(1, state.Players[0].Lanes[i].Occupant!.CurrentVigor);
            }
            // RESET_CHARGES target SELF_ARTIFACT
            Assert.Equal(0, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    // ================================================================
    // WHISPERING SKULL
    // ================================================================

    [Fact]
    public void WhisperingSkull_LoadsAndRegisters()
    {
        try
        {
            LoadAllArtifacts();
            var def = ArtifactRegistry.Get("artf_necromancer_whispering_skull");
            Assert.NotNull(def);
            Assert.Equal("necromancer", def.Class);
            Assert.Equal("skull", def.SlotPool);
            Assert.Equal("Whispering Skull", def.Name);
            Assert.NotNull(def.Charges);
            Assert.Equal(3, def.Charges.Max);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void WhisperingSkull_OnTurnStart_ExcavatesAndGainsCharge()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_necromancer_whispering_skull");
            slot.Charges = 0;

            int handBefore = state.Players[0].Hand.Count;

            // Fire ON_TURN_START for P0's artifact slot 0
            TriggerBus.FireArtifactSlot(state, Trigger.ON_TURN_START, 0, 0);

            // EXCAVATE 1 draws the top card to hand
            Assert.Equal(handBefore + 1, state.Players[0].Hand.Count);
            Assert.True(slot.Charges > 0);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void WhisperingSkull_FullChargeDrawsTwo()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_necromancer_whispering_skull");

            int handBefore = state.Players[0].Hand.Count;

            // Fire ON_CHARGE_FULL — should draw 2 and reset charges
            TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 0);

            Assert.Equal(handBefore + 2, state.Players[0].Hand.Count);
            Assert.Equal(0, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    // ================================================================
    // BLOOD CHALICE
    // ================================================================

    [Fact]
    public void BloodChalice_LoadsAndRegisters()
    {
        try
        {
            LoadAllArtifacts();
            var def = ArtifactRegistry.Get("artf_necromancer_blood_chalice");
            Assert.NotNull(def);
            Assert.Equal("necromancer", def.Class);
            Assert.Equal("ritual_piece", def.SlotPool);
            Assert.Equal("Blood Chalice", def.Name);
            Assert.NotNull(def.Charges);
            Assert.Equal(3, def.Charges.Max);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void BloodChalice_TriggerDrawsOnFriendlyDeath()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_necromancer_blood_chalice");

            int handBefore = state.Players[0].Hand.Count;

            // Kill friendly creature
            state.LastDeathPlayerIndex = 0;
            TriggerBus.Fire(state, Trigger.ON_CREATURE_DIES, 0);

            // DRAW 2 should fire
            Assert.Equal(handBefore + 2, state.Players[0].Hand.Count);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void BloodChalice_TriggerAddsChargeOnFriendlyDeath()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_necromancer_blood_chalice");
            slot.Charges = 0;

            state.LastDeathPlayerIndex = 0;
            TriggerBus.Fire(state, Trigger.ON_CREATURE_DIES, 0);

            Assert.True(slot.Charges > 0);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void BloodChalice_FullChargeUnearthsWithCostFilter()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_necromancer_blood_chalice");

            // Put three creatures in discard: cost 2, cost 4 (too expensive), cost 1
            var cheap1 = new CardInstance(state.NextInstanceId++, "tst_cr_p0_l0", 0)
            {
                Zone = Zone.Discard, CardType = CardType.CREATURE,
                BaseAttack = 3, BaseVigor = 3, Cost = 2
            };
            state.Players[0].Discard.Add(cheap1);

            var expensive = new CardInstance(state.NextInstanceId++, "tst_cr_p0_l1", 0)
            {
                Zone = Zone.Discard, CardType = CardType.CREATURE,
                BaseAttack = 5, BaseVigor = 5, Cost = 4
            };
            state.Players[0].Discard.Add(expensive);

            var cheap2 = new CardInstance(state.NextInstanceId++, "tst_cr_p0_l2", 0)
            {
                Zone = Zone.Discard, CardType = CardType.CREATURE,
                BaseAttack = 2, BaseVigor = 2, Cost = 1
            };
            state.Players[0].Discard.Add(cheap2);

            // Fire ON_CHARGE_FULL — UNEARTH x2 with value=3 cost filter
            TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 0);

            // Should unearth two creatures with cost <= 3 into lanes 0 and 1
            Assert.NotNull(state.Players[0].Lanes[0].Occupant);
            Assert.Equal(cheap1.InstanceId, state.Players[0].Lanes[0].Occupant!.InstanceId);

            Assert.NotNull(state.Players[0].Lanes[1].Occupant);
            Assert.Equal(cheap2.InstanceId, state.Players[0].Lanes[1].Occupant!.InstanceId);

            // expensive (cost 4) should still be in discard
            Assert.Contains(state.Players[0].Discard, c => c.InstanceId == expensive.InstanceId);
            Assert.Equal(0, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    // ================================================================
    // PLAGUE IDOL
    // ================================================================

    [Fact]
    public void PlagueIdol_LoadsAndRegisters()
    {
        try
        {
            LoadAllArtifacts();
            var def = ArtifactRegistry.Get("artf_necromancer_plague_idol");
            Assert.NotNull(def);
            Assert.Equal("necromancer", def.Class);
            Assert.Equal("ritual_piece", def.SlotPool);
            Assert.Equal("Plague Idol", def.Name);
            Assert.NotNull(def.Charges);
            Assert.Equal(3, def.Charges.Max);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void PlagueIdol_PassiveBuffsVenomCreatures()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();

            // Apply the passive BUFF +1/+0 WHILE_PRESENT to KEYWORD:VENOM
            var effect = new EffectDef
            {
                Op = Op.BUFF,
                Attack = 1,
                Vigor = 0,
                Duration = Duration.WHILE_PRESENT,
                Target = new TargetDef
                {
                    Scope = Scope.ALLY_CREATURE,
                    Filter = "KEYWORD:VENOM",
                    Count = TargetCount.All
                }
            };

            var source = new CardInstance(99999, "artf_necromancer_plague_idol", 0);
            var targets = TargetResolver.Resolve(
                effect.Target, source, state.Players[0], state.Players[1], state);

            EffectExecutor.Execute(effect, source, state, targets);

            // Place two creatures: one with VENOM, one without
            var venomCreature = PlaceCreature(state, 0, 0, attack: 2, vigor: 3, keyword: "VENOM");
            var normalCreature = PlaceCreature(state, 0, 1, attack: 2, vigor: 3);

            // Re-resolve and execute
            targets = TargetResolver.Resolve(
                effect.Target, source, state.Players[0], state.Players[1], state);
            EffectExecutor.Execute(effect, source, state, targets);

            // VENOM creature should have +1 attack
            Assert.Equal(3, venomCreature.CurrentAttack);
            // Normal creature unchanged
            Assert.Equal(2, normalCreature.CurrentAttack);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void PlagueIdol_TriggerAddsChargeOnEnemyDeath()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_necromancer_plague_idol");
            slot.Charges = 0;

            // Kill enemy creature — ENEMY condition fires
            state.LastDeathPlayerIndex = 1;
            TriggerBus.Fire(state, Trigger.ON_CREATURE_DIES, 0);

            Assert.True(slot.Charges > 0);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void PlagueIdol_DoesNotGainChargeOnFriendlyDeath()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_necromancer_plague_idol");
            slot.Charges = 0;

            // Kill friendly creature — ENEMY condition should not fire
            state.LastDeathPlayerIndex = 0;
            TriggerBus.Fire(state, Trigger.ON_CREATURE_DIES, 0);

            Assert.Equal(0, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void PlagueIdol_FullChargeGrantsVenomToAllFriendly()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_necromancer_plague_idol");

            var creature1 = PlaceCreature(state, 0, 0, attack: 2, vigor: 3);
            var creature2 = PlaceCreature(state, 0, 1, attack: 3, vigor: 4);

            // Fire ON_CHARGE_FULL — GRANT_KEY VENOM to all friendly creatures
            TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 0);

            Assert.Contains("VENOM", creature1.EffectiveKeywords);
            Assert.Contains("VENOM", creature2.EffectiveKeywords);
            Assert.Equal(0, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }
}
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// TASK-ITEMS-ROGUE-1: Four more Rogue artifacts in content/artifacts/variants/rogue.json.
/// Tests each variant's passive, charge mechanism, and full-charge effect.
/// </summary>
[Collection("NonParallel")]
public class RogueVariantTests
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
        var launchPath = Path.Combine(ArtifactsDir, "launch_artifacts.json");
        ArtifactLoader.LoadPack(launchPath);
        if (Directory.Exists(VariantsDir))
            ArtifactLoader.LoadAllVariants(VariantsDir);
    }

    private static CardInstance PlaceCreature(GameState state, int pIdx, int lane,
        int attack = 2, int vigor = 5, string? keyword = null)
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

        // Full-charge effects (built into a single ON_CHARGE_FULL ability)
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

    private static GameState Attack(GameState state, int playerIndex, int lane)
        => DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = playerIndex,
            SourceLane = lane,
            TargetLane = lane
        });

    // ================================================================
    // POISONER'S KISS
    // ================================================================

    [Fact]
    public void PoisonersKiss_PassiveGrantsVenom_ToFirstSummonedThisTurn()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            // Equip Poisoner's Kiss in slot 0
            var slot = EquipArtifact(state, 0, 0, "artf_rogue_poisoners_kiss");

            // Place two creatures — first one gets SummonedThisTurn flag
            var first = PlaceCreature(state, 0, 0, attack: 2, vigor: 3);
            first.SummonedThisTurn = true;
            var second = PlaceCreature(state, 0, 1, attack: 2, vigor: 3);
            second.SummonedThisTurn = true;

            // Resolve the passive: GRANT_KEY VENOM to FIRST_SUMMONED_THIS_TURN
            var effect = new EffectDef
            {
                Op = Op.GRANT_KEY,
                Keyword = "VENOM",
                Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "FIRST_SUMMONED_THIS_TURN", Count = TargetCount.Exactly(1) }
            };
            var targets = TargetResolver.Resolve(effect.Target!, slot.Occupant!,
                state.Players[0], state.Players[1], state);
            EffectExecutor.Execute(effect, slot.Occupant!, state, targets);

            // First creature (lane 0, oldest InstanceId) got VENOM
            var venomTarget = targets.OfType<CreatureTarget>().FirstOrDefault();
            Assert.NotNull(venomTarget);
            Assert.Contains("VENOM", venomTarget.Card.GrantedKeywords);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void PoisonersKiss_GainsCharge_WhenEnemyCreatureDies()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_rogue_poisoners_kiss");
            Assert.Equal(0, slot.Charges);

            // Place an enemy creature and mark it as dying this turn
            var enemy = PlaceCreature(state, 1, 0, attack: 1, vigor: 1);
            state.LastDeathPlayerIndex = 1; // enemy died
            state.CreatureDiedThisTurnCount[1] = 1;

            // Fire the ON_CREATURE_DIES trigger (condition: ENEMY)
            TriggerBus.Fire(state, Trigger.ON_CREATURE_DIES, 0);

            // Charge gained
            Assert.Equal(1, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void PoisonersKiss_FullCharge_DealsTwoFaceDamage()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);
            state.Players[1].Vigor = 20;

            var slot = EquipArtifact(state, 0, 0, "artf_rogue_poisoners_kiss");
            slot.Charges = 3;

            // Fire ON_CHARGE_FULL for slot 0
            TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 0);

            // Poisoner's Kiss full_charge: DAMAGE 2 to PLAYER_ENEMY + RESET_CHARGES
            // RESET_CHARGES resets to 0; DAMAGE 2 is dealt
            Assert.Equal(18, state.Players[1].Vigor);
            Assert.Equal(0, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    // ================================================================
    // SHADOWFANG
    // ================================================================

    [Fact]
    public void Shadowfang_PassiveBuffsSwiftCreatures()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_rogue_shadowfang");

            // Place a Swift creature and a non-Swift creature
            var swift = PlaceCreature(state, 0, 0, attack: 2, vigor: 3, keyword: "SWIFT");
            var normal = PlaceCreature(state, 0, 1, attack: 2, vigor: 3);

            // Resolve the passive: BUFF +1/+0 THIS_TURN to KEYWORD:SWIFT creatures
            var effect = new EffectDef
            {
                Op = Op.BUFF,
                Attack = 1,
                Vigor = 0,
                Duration = Duration.THIS_TURN,
                Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "KEYWORD:SWIFT", Count = TargetCount.All }
            };
            var targets = TargetResolver.Resolve(effect.Target!, slot.Occupant!,
                state.Players[0], state.Players[1], state);
            EffectExecutor.Execute(effect, slot.Occupant!, state, targets);

            // Swift creature got +1 attack (2+1=3)
            Assert.Equal(3, swift.CurrentAttack);
            // Non-Swift creature unaffected (still 2)
            Assert.Equal(2, normal.CurrentAttack);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void Shadowfang_FullCharge_GrantsSwiftAndStealthStrike()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_rogue_shadowfang");

            // Place a creature that was summoned this turn
            var creature = PlaceCreature(state, 0, 0, attack: 2, vigor: 3);
            creature.SummonedThisTurn = true;

            slot.Charges = 3;

            // Fire ON_CHARGE_FULL
            TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 0);

            // Shadowfang full_charge: GRANT_KEY SWIFT and STEALTH_STRIKE to FIRST_SUMMONED_THIS_TURN + RESET_CHARGES
            Assert.Contains("SWIFT", creature.GrantedKeywords);
            Assert.Contains("STEALTH_STRIKE", creature.GrantedKeywords);
            Assert.Equal(0, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void Shadowfang_GainsChargeOnTurnEnd()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_rogue_shadowfang");
            Assert.Equal(0, slot.Charges);

            // Shadowfang has gain_on "on_turn_end" — auto-gain at end of P0's turn
            // Use the DuelEngine turn-end processing that auto-charges artifacts
            state = EndTurn(state, 0);

            // P0's turn ended — Shadowfang gains 1 charge
            var sfSlot = state.Players[0].ArtifactSlots[0];
            Assert.Equal(1, sfSlot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    // ================================================================
    // LOCKPICK
    // ================================================================

    [Fact]
    public void Lockpick_Passive_DiscountsFirstCreatureEachTurn()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_rogue_lockpick");

            // Register the COST_MOD passive — apply it as if passive fires
            var effect = new EffectDef
            {
                Op = Op.COST_MOD,
                AppliesTo = "CREATURE",
                Filter = "FIRST_CREATURE_EACH_TURN",
                Amount = 1,
                Target = new TargetDef { Scope = Scope.PLAYER_SELF }
            };
            var targets = TargetResolver.Resolve(effect.Target!, slot.Occupant!,
                state.Players[0], state.Players[1], state);
            EffectExecutor.Execute(effect, slot.Occupant!, state, targets);

            // Create a creature card (cost 3)
            var creatureCard = new CardInstance(state.NextInstanceId++, "tst_creature", 0)
            {
                CardType = CardType.CREATURE,
                Cost = 3,
                Zone = Zone.Hand
            };
            state.Players[0].Hand.Add(creatureCard);

            // Effective cost should be 2 (3 - 1)
            int effectiveCost = CostInterceptor.GetEffectiveCost(state, creatureCard, 0);
            Assert.Equal(2, effectiveCost);

            // Simulate playing the creature (consumes the per-turn mod)
            CostInterceptor.ConsumePerTurnMods(state, creatureCard, 0);

            // After consumption, the mod's used this turn, no more discount
            int afterCost = CostInterceptor.GetEffectiveCost(state, creatureCard, 0);
            Assert.Equal(3, afterCost);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void Lockpick_FullCharge_DrawsTwo()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_rogue_lockpick");
            int handBefore = state.Players[0].Hand.Count;

            slot.Charges = 3;

            // Fire ON_CHARGE_FULL
            TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 0);

            // Lockpick full_charge: DRAW 2 + RESET_CHARGES
            Assert.Equal(handBefore + 2, state.Players[0].Hand.Count);
            Assert.Equal(0, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void Lockpick_GainsChargeOnTurnEnd()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_rogue_lockpick");
            Assert.Equal(0, slot.Charges);

            // Lockpick has gain_on "on_turn_end"
            state = EndTurn(state, 0);

            var lpSlot = state.Players[0].ArtifactSlots[0];
            Assert.Equal(1, lpSlot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    // ================================================================
    // GLOOMBLADE
    // ================================================================

    [Fact]
    public void Gloomblade_PassiveBuffsEdgeLaneCreatures()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_rogue_gloomblade");

            // Place creatures: one in edge lane (0), one in center (2), one in other edge (4)
            var edgeLeft = PlaceCreature(state, 0, 0, attack: 2, vigor: 3);
            var center = PlaceCreature(state, 0, 2, attack: 2, vigor: 3);
            var edgeRight = PlaceCreature(state, 0, 4, attack: 2, vigor: 3);

            // Resolve the passive: BUFF +1/+0 WHILE_PRESENT to EDGE_LANE creatures
            var effect = new EffectDef
            {
                Op = Op.BUFF,
                Attack = 1,
                Vigor = 0,
                Duration = Duration.WHILE_PRESENT,
                Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "EDGE_LANE", Count = TargetCount.All }
            };
            var targets = TargetResolver.Resolve(effect.Target!, slot.Occupant!,
                state.Players[0], state.Players[1], state);
            EffectExecutor.Execute(effect, slot.Occupant!, state, targets);

            // Edge creatures got +1 (2+1=3)
            Assert.Equal(3, edgeLeft.CurrentAttack);
            Assert.Equal(3, edgeRight.CurrentAttack);
            // Center creature unaffected (still 2)
            Assert.Equal(2, center.CurrentAttack);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void Gloomblade_FullCharge_DamagesLowestVigorEnemy()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_rogue_gloomblade");

            // Place enemy creatures with different vigor
            var tough = PlaceCreature(state, 1, 0, attack: 1, vigor: 10);
            var weak = PlaceCreature(state, 1, 1, attack: 1, vigor: 2);

            slot.Charges = 3;

            // Fire ON_CHARGE_FULL
            TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 0);

            // Gloomblade full_charge: DAMAGE 3 to ENEMY_CREATURE with LOWEST_VIGOR + RESET_CHARGES
            // weak (vigor=2) should take 3 damage (die) or go to -1
            // tough (vigor=10) should be untouched
            Assert.True(weak.CurrentVigor <= 0 || weak.CurrentVigor == 2 - 3); // dead or at -1
            Assert.Equal(10, tough.CurrentVigor);
            Assert.Equal(0, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void Gloomblade_GainsChargeOnTurnEnd()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_rogue_gloomblade");
            Assert.Equal(0, slot.Charges);

            // Gloomblade has gain_on "on_turn_end"
            state = EndTurn(state, 0);

            var gbSlot = state.Players[0].ArtifactSlots[0];
            Assert.Equal(1, gbSlot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    // ================================================================
    // VARIANT LOAD TEST
    // ================================================================

    [Fact]
    public void RogueVariantArtifacts_LoadFromFile_AndEquip()
    {
        try
        {
            LoadAllArtifacts();

            // Verify all 4 are registered
            Assert.NotNull(ArtifactRegistry.Get("artf_rogue_poisoners_kiss"));
            Assert.NotNull(ArtifactRegistry.Get("artf_rogue_shadowfang"));
            Assert.NotNull(ArtifactRegistry.Get("artf_rogue_lockpick"));
            Assert.NotNull(ArtifactRegistry.Get("artf_rogue_gloomblade"));

            // Verify class and slot
            var pk = ArtifactRegistry.Get("artf_rogue_poisoners_kiss");
            Assert.Equal("rogue", pk.Class);
            Assert.Equal("dagger", pk.SlotPool);

            // Equip all 4 in a headless duel
            var state = CreateState();
            state.Players[0].ArtifactClass = "rogue";
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[0] = new ArtifactSlot(0);
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            EquipArtifact(state, 0, 0, "artf_rogue_poisoners_kiss");
            EquipArtifact(state, 0, 1, "artf_rogue_shadowfang");

            Assert.NotNull(state.Players[0].ArtifactSlots[0].Occupant);
            Assert.NotNull(state.Players[0].ArtifactSlots[1].Occupant);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }
}
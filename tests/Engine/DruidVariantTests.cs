using System.Collections.Generic;
using System.IO;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// TASK-ITEMS-DRUID-1: Four more Druid artifacts in
/// content/artifacts/variants/druid.json.
/// Tests each variant's passive, charge mechanism, and full-charge effect.
/// </summary>
[Collection("NonParallel")]
public class DruidVariantTests
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

    private static void FireOnChargeFull(GameState state, int pIdx, int slotIdx)
        => TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, pIdx, slotIdx);

    // ================================================================
    // GRIMOIRE OF THORNS
    // ================================================================

    [Fact]
    public void GrimoireOfThorns_OnAllyAttacked_DamagesEnemyAndGainsCharge()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_druid_grimoire_of_thorns");

            // Place a friendly creature and an enemy creature
            var ally = PlaceCreature(state, 0, 0, attack: 2, vigor: 5);
            var enemy = PlaceCreature(state, 1, 0, attack: 3, vigor: 3);

            Assert.Equal(0, slot.Charges);

            // Fire ON_ALLY_ATTACKED — Grimoire damages lowest-vigor enemy (enemy, vigor 3) and gains charge
            TriggerBus.Fire(state, Trigger.ON_ALLY_ATTACKED, 0);

            // Enemy took 1 damage (3 → 2 vigor remaining)
            Assert.Equal(2, enemy.CurrentVigor);
            // Gained 1 charge
            Assert.Equal(1, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void GrimoireOfThorns_FullCharge_BuffsRootedCreatures()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_druid_grimoire_of_thorns");

            // Place a Rooted friendly creature and a non-Rooted one
            var rooted = PlaceCreature(state, 0, 0, attack: 2, vigor: 5, keyword: "ROOTED");
            var normal = PlaceCreature(state, 0, 1, attack: 2, vigor: 5);

            slot.Charges = 3;
            FireOnChargeFull(state, 0, 0);

            // Rooted creature got +1/+1 permanently (2→3, 5→6)
            Assert.Equal(3, rooted.CurrentAttack);
            Assert.Equal(6, rooted.CurrentVigor);
            // Non-Rooted creature unaffected
            Assert.Equal(2, normal.CurrentAttack);
            Assert.Equal(5, normal.CurrentVigor);
            // Charges reset
            Assert.Equal(0, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    // ================================================================
    // SEEDBOOK
    // ================================================================

    [Fact]
    public void Seedbook_OnTurnEnd_SummonsSeedAndGainsCharge()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_druid_seedbook");
            Assert.Equal(0, slot.Charges);

            // Fire ON_TURN_END — Seedbook summons a 0/2 ROOTED Seed token and gains charge
            TriggerBus.FireArtifactSlot(state, Trigger.ON_TURN_END, 0, 0);

            // A token was summoned in an empty lane
            var token = state.Players[0].Lanes[0].Occupant;
            Assert.NotNull(token);
            Assert.Equal(0, token.CurrentAttack);
            Assert.Equal(2, token.CurrentVigor);
            Assert.Contains("ROOTED", token.Keywords);

            // Gained 1 charge
            Assert.Equal(1, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void Seedbook_FullCharge_BuffsTokensToTwoThreeWithReach()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_druid_seedbook");

            // Place a token creature and a non-token creature
            var token = PlaceCreature(state, 0, 0, attack: 0, vigor: 2);
            token.CardType = CardType.TOKEN; // mark as token
            var normal = PlaceCreature(state, 0, 1, attack: 2, vigor: 5);

            slot.Charges = 3;
            FireOnChargeFull(state, 0, 0);

            // Token became 2/3 with REACH
            Assert.Equal(2, token.CurrentAttack);
            Assert.Equal(3, token.CurrentVigor);
            Assert.Contains("REACH", token.GrantedKeywords);
            // Non-token unaffected
            Assert.Equal(2, normal.CurrentAttack);
            Assert.Equal(5, normal.CurrentVigor);
            // Charges reset
            Assert.Equal(0, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    // ================================================================
    // WILD PACT
    // ================================================================

    [Fact]
    public void WildPact_PassiveGrantsSwift_ToFirstSummonedCreature()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_druid_wild_pact");

            // Place a creature summoned this turn and one that wasn't
            var first = PlaceCreature(state, 0, 0, attack: 2, vigor: 5);
            first.SummonedThisTurn = true;
            var second = PlaceCreature(state, 0, 1, attack: 2, vigor: 5);
            second.SummonedThisTurn = false;

            // Resolve passive: GRANT_KEY SWIFT to FIRST_SUMMONED_THIS_TURN
            var effect = new EffectDef
            {
                Op = Op.GRANT_KEY,
                Keyword = "SWIFT",
                Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "FIRST_SUMMONED_THIS_TURN", Count = TargetCount.Exactly(1) }
            };
            var targets = TargetResolver.Resolve(effect.Target!, slot.Occupant!,
                state.Players[0], state.Players[1], state);
            EffectExecutor.Execute(effect, slot.Occupant!, state, targets);

            // First creature (summoned this turn) got SWIFT
            var swiftTarget = targets.OfType<CreatureTarget>().FirstOrDefault();
            Assert.NotNull(swiftTarget);
            Assert.Contains("SWIFT", swiftTarget.Card.GrantedKeywords);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void WildPact_GainsCharge_OnCreatureAttacksDuringYourTurn()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_druid_wild_pact");
            Assert.Equal(0, slot.Charges);

            // Fire ON_CREATURE_ATTACKS during P0's turn
            state.CurrentPlayerIndex = 0;
            TriggerBus.Fire(state, Trigger.ON_CREATURE_ATTACKS, 0);

            // Gained 1 charge
            Assert.Equal(1, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void WildPact_FullCharge_DrawsCards()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_druid_wild_pact");

            int handSizeBefore = state.Players[0].Hand.Count;

            slot.Charges = 3;
            FireOnChargeFull(state, 0, 0);

            // Wild Pact full_charge: DRAW 2 + RESET_CHARGES
            Assert.Equal(handSizeBefore + 2, state.Players[0].Hand.Count);
            Assert.Equal(0, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    // ================================================================
    // GROVE LINK
    // ================================================================

    [Fact]
    public void GroveLink_PassiveBuffsLowestVigorCreatures()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_druid_grove_link");

            // Place three creatures with different vigor values
            var weak = PlaceCreature(state, 0, 0, attack: 2, vigor: 2);
            var medium = PlaceCreature(state, 0, 1, attack: 2, vigor: 4);
            var strong = PlaceCreature(state, 0, 2, attack: 2, vigor: 6);

            // Resolve passive: BUFF +0/+1 WHILE_PRESENT to LOWEST_VIGOR 2 (oldest first)
            var effect = new EffectDef
            {
                Op = Op.BUFF,
                Attack = 0,
                Vigor = 1,
                Duration = Duration.WHILE_PRESENT,
                Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "LOWEST_VIGOR", Count = TargetCount.Exactly(2), Tiebreak = "OLDEST_IN_PLAY" }
            };
            var targets = TargetResolver.Resolve(effect.Target!, slot.Occupant!,
                state.Players[0], state.Players[1], state);
            EffectExecutor.Execute(effect, slot.Occupant!, state, targets);

            // The two lowest vigor creatures got +1 vigor
            // Weak (vigor 2 → 3), Medium (vigor 4 → 5), Strong (vigor 6, unaffacted)
            Assert.Equal(3, weak.CurrentVigor);
            Assert.Equal(5, medium.CurrentVigor);
            Assert.Equal(6, strong.CurrentVigor);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void GroveLink_GainsCharge_OnAllyAttacked()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_druid_grove_link");
            Assert.Equal(0, slot.Charges);

            // Fire ON_ALLY_ATTACKED
            TriggerBus.Fire(state, Trigger.ON_ALLY_ATTACKED, 0);

            // Gained 1 charge
            Assert.Equal(1, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void GroveLink_FullCharge_BuffsAndGrantsGuardToLowestVigorCreatures()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_druid_grove_link");

            // Place three creatures
            var weak = PlaceCreature(state, 0, 0, attack: 2, vigor: 2);
            var medium = PlaceCreature(state, 0, 1, attack: 2, vigor: 4);
            var strong = PlaceCreature(state, 0, 2, attack: 2, vigor: 6);

            slot.Charges = 3;
            FireOnChargeFull(state, 0, 0);

            // Two lowest-vigor (weak, medium) got +1/+1 PERMANENT and GUARD
            // weak: 2→3, 2→3
            Assert.Equal(3, weak.CurrentAttack);
            Assert.Equal(3, weak.CurrentVigor);
            Assert.Contains("GUARD", weak.GrantedKeywords);

            // medium: 2→3, 4→5
            Assert.Equal(3, medium.CurrentAttack);
            Assert.Equal(5, medium.CurrentVigor);
            Assert.Contains("GUARD", medium.GrantedKeywords);

            // strong: unaffected (not in lowest 2)
            Assert.Equal(2, strong.CurrentAttack);
            Assert.Equal(6, strong.CurrentVigor);

            // Charges reset
            Assert.Equal(0, slot.Charges);
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
    public void DruidVariantArtifacts_LoadFromFile_AndEquip()
    {
        try
        {
            LoadAllArtifacts();

            // Verify all 4 are registered
            Assert.NotNull(ArtifactRegistry.Get("artf_druid_grimoire_of_thorns"));
            Assert.NotNull(ArtifactRegistry.Get("artf_druid_seedbook"));
            Assert.NotNull(ArtifactRegistry.Get("artf_druid_wild_pact"));
            Assert.NotNull(ArtifactRegistry.Get("artf_druid_grove_link"));

            // Verify class and slot
            var thorns = ArtifactRegistry.Get("artf_druid_grimoire_of_thorns");
            Assert.Equal("druid", thorns.Class);
            Assert.Equal("book", thorns.SlotPool);

            var seed = ArtifactRegistry.Get("artf_druid_seedbook");
            Assert.Equal("druid", seed.Class);
            Assert.Equal("book", seed.SlotPool);

            var pact = ArtifactRegistry.Get("artf_druid_wild_pact");
            Assert.Equal("druid", pact.Class);
            Assert.Equal("totem", pact.SlotPool);

            var grove = ArtifactRegistry.Get("artf_druid_grove_link");
            Assert.Equal("druid", grove.Class);
            Assert.Equal("totem", grove.SlotPool);

            // Equip in a headless duel
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[0] = new ArtifactSlot(0);
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            EquipArtifact(state, 0, 0, "artf_druid_seedbook");
            EquipArtifact(state, 0, 1, "artf_druid_wild_pact");

            Assert.NotNull(state.Players[0].ArtifactSlots[0].Occupant);
            Assert.NotNull(state.Players[0].ArtifactSlots[1].Occupant);

            // Run a turn cycle to prove the duel functions
            state = EndTurn(state, 0);
            Assert.False(state.IsGameOver, "Game should not be over after one turn cycle");
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }
}
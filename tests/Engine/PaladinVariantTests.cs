using System.Collections.Generic;
using System.IO;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// TASK-ITEMS-PALADIN-1: Four more Paladin artifacts in
/// content/artifacts/variants/paladin.json.
/// Tests each variant's passive, charge mechanism, and full-charge effect.
/// </summary>
[Collection("NonParallel")]
public class PaladinVariantTests
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
    // JUDGEMENT MAUL
    // ================================================================

    [Fact]
    public void JudgementMaul_PassiveGrantsPierce_ToHighestVigorCreature()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_paladin_judgement_maul");

            // Place two creatures: one with higher vigor
            var strong = PlaceCreature(state, 0, 0, attack: 2, vigor: 7);
            var weak = PlaceCreature(state, 0, 1, attack: 2, vigor: 3);

            // Resolve the passive: GRANT_KEY PIERCE to HIGHEST_VIGOR
            var effect = new EffectDef
            {
                Op = Op.GRANT_KEY,
                Keyword = "PIERCE",
                Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "HIGHEST_VIGOR", Count = TargetCount.Exactly(1), Tiebreak = "OLDEST_IN_PLAY" }
            };
            var targets = TargetResolver.Resolve(effect.Target!, slot.Occupant!,
                state.Players[0], state.Players[1], state);
            EffectExecutor.Execute(effect, slot.Occupant!, state, targets);

            // Highest-vigor creature (lane 0, vigor 7) got PIERCE
            var pierceTarget = targets.OfType<CreatureTarget>().FirstOrDefault();
            Assert.NotNull(pierceTarget);
            Assert.Contains("PIERCE", pierceTarget.Card.GrantedKeywords);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void JudgementMaul_GainsCharge_OnHeal()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            // Equip Judgement Maul in slot 0 and something else in slot 1
            // so we can verify only slot 0 gains charge
            var slot0 = EquipArtifact(state, 0, 0, "artf_paladin_judgement_maul");
            var slot1 = EquipArtifact(state, 0, 1, "artf_paladin_judgement_maul");
            Assert.Equal(0, slot0.Charges);
            Assert.Equal(0, slot1.Charges);

            // Heal a friendly creature
            var ally = PlaceCreature(state, 0, 0, attack: 2, vigor: 5);
            ally.Damage = 2; // wounded
            var healEffect = new EffectDef { Op = Op.HEAL, Amount = 2 };
            EffectExecutor.Execute(healEffect, slot0.Occupant!, state,
                new List<ResolvedTarget> { new CreatureTarget(ally, 0, 0) });

            // Fire ON_HEAL for slot 0 — ADD_CHARGE 1
            // With SELF_ARTIFACT scope for ADD_CHARGE, it resolves to PLAYER_SELF
            // and adds charge to ALL non-suppressed artifacts
            TriggerBus.FireArtifactSlot(state, Trigger.ON_HEAL, 0, 0);

            // Both slots gained charge (ADD_CHARGE to SELF_ARTIFACT iterates all slots)
            Assert.Equal(1, slot0.Charges);
            Assert.Equal(1, slot1.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void JudgementMaul_FullCharge_DamagesHighestAttackEnemy()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_paladin_judgement_maul");

            // Place two enemy creatures
            var enemy1 = PlaceCreature(state, 1, 0, attack: 3, vigor: 5); // higher attack
            var enemy2 = PlaceCreature(state, 1, 1, attack: 1, vigor: 5);

            slot.Charges = 3;

            // Fire ON_CHARGE_FULL
            TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 0);

            // Judgement Maul full_charge: DAMAGE 3 to HIGHEST_ATTACK enemy + RESET_CHARGES
            // enemy1 (attack 3) takes 3 damage (5-3=2), enemy2 untouched (5)
            Assert.Equal(2, enemy1.CurrentVigor);
            Assert.Equal(5, enemy2.CurrentVigor);
            Assert.Equal(0, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    // ================================================================
    // WARDEN'S HAMMER
    // ================================================================

    [Fact]
    public void WardensHammer_PassiveBuffsGuardCreatures()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_paladin_wardens_hammer");

            // Place a Guard creature and a non-Guard creature
            var guard = PlaceCreature(state, 0, 0, attack: 2, vigor: 5, keyword: "GUARD");
            var normal = PlaceCreature(state, 0, 1, attack: 2, vigor: 5);

            // Resolve the passive: BUFF +1/+0 WHILE_PRESENT to KEYWORD:GUARD
            var effect = new EffectDef
            {
                Op = Op.BUFF,
                Attack = 1,
                Vigor = 0,
                Duration = Duration.WHILE_PRESENT,
                Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "KEYWORD:GUARD", Count = TargetCount.All }
            };
            var targets = TargetResolver.Resolve(effect.Target!, slot.Occupant!,
                state.Players[0], state.Players[1], state);
            EffectExecutor.Execute(effect, slot.Occupant!, state, targets);

            // Guard creature got +1 attack (2+1=3)
            Assert.Equal(3, guard.CurrentAttack);
            // Non-Guard creature unaffected (still 2)
            Assert.Equal(2, normal.CurrentAttack);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void WardensHammer_GainsCharge_OnAllyAttacked()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_paladin_wardens_hammer");
            Assert.Equal(0, slot.Charges);

            // Place a friendly creature
            var ally = PlaceCreature(state, 0, 0, attack: 2, vigor: 5);

            // Fire ON_ALLY_ATTACKED trigger
            TriggerBus.Fire(state, Trigger.ON_ALLY_ATTACKED, 0);

            // Warden's Hammer gains 1 charge per ally attacked
            Assert.Equal(1, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void WardensHammer_FullCharge_GrantsGuardAndHealsFriendly()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_paladin_wardens_hammer");

            // Place a damaged friendly creature
            var ally = PlaceCreature(state, 0, 0, attack: 2, vigor: 5);
            ally.Damage = 3; // wounded

            slot.Charges = 3;

            // Fire ON_CHARGE_FULL
            TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 0);

            // Warden's Hammer full_charge: GRANT_KEY GUARD + HEAL 99 + RESET_CHARGES
            // The damaged creature gained GUARD and was healed to full
            Assert.Contains("GUARD", ally.GrantedKeywords);
            Assert.Equal(0, ally.Damage); // healed to full (99 > 3)
            Assert.Equal(0, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    // ================================================================
    // RALLYING STANDARD
    // ================================================================

    [Fact]
    public void RallyingStandard_OnSummon_BuffsPlayedCreatureAndGainsCharge()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_paladin_rallying_standard");

            // Place a creature as if just summoned
            var ally = PlaceCreature(state, 0, 0, attack: 2, vigor: 5);
            ally.SummonedThisTurn = true;

            // Fire ON_SUMMON trigger for P0
            // Rallying Standard has condition DURING_YOUR_TURN
            state.CurrentPlayerIndex = 0; // ensure it's P0's turn
            TriggerBus.Fire(state, Trigger.ON_SUMMON, 0);

            // Rallying Standard trigger: BUFF +1/+0 THIS_TURN to FIRST_SUMMONED_THIS_TURN + ADD_CHARGE 1
            // The summoned creature got +1 attack
            Assert.Equal(3, ally.CurrentAttack); // 2 + 1 = 3
            Assert.Equal(1, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void RallyingStandard_FullCharge_BuffsAllFriendlyCreatures()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_paladin_rallying_standard");

            // Place two friendly creatures
            var ally1 = PlaceCreature(state, 0, 0, attack: 2, vigor: 3);
            var ally2 = PlaceCreature(state, 0, 1, attack: 1, vigor: 4);

            slot.Charges = 3;

            // Fire ON_CHARGE_FULL
            TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 0);

            // Rallying Standard full_charge: BUFF +1/+1 UNTIL_YOUR_NEXT_TURN to all + RESET_CHARGES
            Assert.Equal(3, ally1.CurrentAttack); // 2 + 1 = 3
            Assert.Equal(4, ally1.CurrentVigor);  // 3 + 1 = 4
            Assert.Equal(2, ally2.CurrentAttack); // 1 + 1 = 2
            Assert.Equal(5, ally2.CurrentVigor);  // 4 + 1 = 5
            Assert.Equal(0, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    // ================================================================
    // SUNSPIRE SIGIL
    // ================================================================

    [Fact]
    public void SunspireSigil_OnTurnEnd_HealsDamagedCreaturesAndGainsCharge()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_paladin_sunspire_sigil");

            // Place two friendly creatures — one damaged, one undamaged
            var wounded = PlaceCreature(state, 0, 0, attack: 2, vigor: 5);
            wounded.Damage = 2;
            var full = PlaceCreature(state, 0, 1, attack: 2, vigor: 5);

            // Fire ON_TURN_END for P0's artifact slot 0
            TriggerBus.FireArtifactSlot(state, Trigger.ON_TURN_END, 0, 0);

            // Sunspire Sigil trigger: HEAL 1 to DAMAGED all + ADD_CHARGE 1
            // wounded creature healed for 1 (2-1=1 damage remaining)
            Assert.Equal(1, wounded.Damage);
            // undamaged creature takes no damage from HEAL (overheal is no-op)
            Assert.Equal(0, full.Damage);
            // ADD_CHARGE to SELF_ARTIFACT adds charge to all slots (both get 1)
            Assert.Equal(1, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void SunspireSigil_FullCharge_GrantsWardToAllFriendlies()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_paladin_sunspire_sigil");

            // Place two friendly creatures
            var ally1 = PlaceCreature(state, 0, 0, attack: 2, vigor: 3);
            var ally2 = PlaceCreature(state, 0, 1, attack: 1, vigor: 4);

            slot.Charges = 3;

            // Fire ON_CHARGE_FULL
            TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 0);

            // Sunspire Sigil full_charge: GRANT_KEY WARD to all + RESET_CHARGES
            Assert.Contains("WARD", ally1.GrantedKeywords);
            Assert.Contains("WARD", ally2.GrantedKeywords);
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
    public void PaladinVariantArtifacts_LoadFromFile_AndEquip()
    {
        try
        {
            LoadAllArtifacts();

            // Verify all 4 are registered
            Assert.NotNull(ArtifactRegistry.Get("artf_paladin_judgement_maul"));
            Assert.NotNull(ArtifactRegistry.Get("artf_paladin_wardens_hammer"));
            Assert.NotNull(ArtifactRegistry.Get("artf_paladin_rallying_standard"));
            Assert.NotNull(ArtifactRegistry.Get("artf_paladin_sunspire_sigil"));

            // Verify class and slot
            var jm = ArtifactRegistry.Get("artf_paladin_judgement_maul");
            Assert.Equal("paladin", jm.Class);
            Assert.Equal("hammer", jm.SlotPool);

            var wh = ArtifactRegistry.Get("artf_paladin_wardens_hammer");
            Assert.Equal("paladin", wh.Class);
            Assert.Equal("hammer", wh.SlotPool);

            var rs = ArtifactRegistry.Get("artf_paladin_rallying_standard");
            Assert.Equal("paladin", rs.Class);
            Assert.Equal("banner", rs.SlotPool);

            var ss = ArtifactRegistry.Get("artf_paladin_sunspire_sigil");
            Assert.Equal("paladin", ss.Class);
            Assert.Equal("banner", ss.SlotPool);

            // Equip in a headless duel
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[0] = new ArtifactSlot(0);
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            EquipArtifact(state, 0, 0, "artf_paladin_judgement_maul");
            EquipArtifact(state, 0, 1, "artf_paladin_sunspire_sigil");

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
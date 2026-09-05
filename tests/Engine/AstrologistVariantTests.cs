using System.Collections.Generic;
using System.IO;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// TASK-ITEMS-ASTROLOGIST-1: Four more Astrologist artifacts in
/// content/artifacts/variants/astrologist.json.
/// Tests each variant's passive, charge mechanism, and full-charge effect.
/// </summary>
[Collection("NonParallel")]
public class AstrologistVariantTests
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
    // LUNAR LENS
    // ================================================================

    [Fact]
    public void LunarLens_OnTurnStart_ScrysAndDrawsAndGainsCharge()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_astrologist_lunar_lens");

            int deckBefore = state.Players[0].Deck.Count;
            int handBefore = state.Players[0].Hand.Count;

            // Fire ON_TURN_START trigger for P0's artifact slot 0
            TriggerBus.FireArtifactSlot(state, Trigger.ON_TURN_START, 0, 0);

            // Lunar Lens trigger: SCY 1 (top card stays on top), DRAW 1, ADD_CHARGE 1
            // Deck count decreased by 1 (draw)
            Assert.Equal(deckBefore - 1, state.Players[0].Deck.Count);
            Assert.Equal(handBefore + 1, state.Players[0].Hand.Count);
            Assert.Equal(1, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void LunarLens_FullCharge_DiscountsNextSpell()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_astrologist_lunar_lens");
            slot.Charges = 3;

            // Fire ON_CHARGE_FULL — applies COST_MOD for next spell
            TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 0);

            // Create a spell card (RITUAL) with cost 3
            var spellCard = new CardInstance(state.NextInstanceId++, "tst_spell", 0)
            {
                CardType = CardType.RITUAL,
                Cost = 3,
                Zone = Zone.Hand
            };
            state.Players[0].Hand.Add(spellCard);

            // Should cost 1 (3 - 2 discount)
            int effectiveCost = CostInterceptor.GetEffectiveCost(state, spellCard, 0);
            Assert.Equal(1, effectiveCost);

            // Charges reset
            Assert.Equal(0, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    // ================================================================
    // ECLIPSE SPHERE
    // ================================================================

    [Fact]
    public void EclipseSphere_GainsChargeOnTurnEnd()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_astrologist_eclipse_sphere");
            Assert.Equal(0, slot.Charges);

            // Eclipse Sphere has gain_on "on_turn_end"
            state = EndTurn(state, 0);

            var esSlot = state.Players[0].ArtifactSlots[0];
            Assert.Equal(1, esSlot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void EclipseSphere_FullCharge_DrawsTwo()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_astrologist_eclipse_sphere");
            int handBefore = state.Players[0].Hand.Count;

            slot.Charges = 3;

            // Fire ON_CHARGE_FULL
            TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 0);

            // Eclipse Sphere full_charge: DRAW 2 + RESET_CHARGES
            Assert.Equal(handBefore + 2, state.Players[0].Hand.Count);
            Assert.Equal(0, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    // ================================================================
    // METEOR SHOWER
    // ================================================================

    [Fact]
    public void MeteorShower_GainsChargeOnTurnEnd()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_astrologist_meteor_shower");
            Assert.Equal(0, slot.Charges);

            // Meteor Shower has gain_on "on_turn_end" with max=4
            state = EndTurn(state, 0);

            var msSlot = state.Players[0].ArtifactSlots[0];
            Assert.Equal(1, msSlot.Charges);
            Assert.Equal(4, msSlot.MaxCharges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void MeteorShower_FullCharge_DamagesAllEnemyCreatures()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_astrologist_meteor_shower");

            // Place two enemy creatures with vigor 3
            var enemy1 = PlaceCreature(state, 1, 0, attack: 1, vigor: 3);
            var enemy2 = PlaceCreature(state, 1, 1, attack: 1, vigor: 3);

            slot.Charges = 4;

            // Fire ON_CHARGE_FULL
            TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 0);

            // Meteor Shower full_charge: DAMAGE 2 to ALL enemy creatures + RESET_CHARGES
            Assert.Equal(1, enemy1.CurrentVigor);  // 3 - 2 = 1
            Assert.Equal(1, enemy2.CurrentVigor);  // 3 - 2 = 1
            Assert.Equal(0, slot.Charges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    // ================================================================
    // TWIN STARS
    // ================================================================

    [Fact]
    public void TwinStars_GainsChargeOnTurnEnd()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_astrologist_twin_stars");
            Assert.Equal(0, slot.Charges);

            // Twin Stars has gain_on "on_turn_end" with max=2
            state = EndTurn(state, 0);

            var tsSlot = state.Players[0].ArtifactSlots[0];
            Assert.Equal(1, tsSlot.Charges);
            Assert.Equal(2, tsSlot.MaxCharges);
        }
        finally
        {
            ArtifactRegistry.Clear();
        }
    }

    [Fact]
    public void TwinStars_FullCharge_DamagesOneEnemyCreature()
    {
        try
        {
            LoadAllArtifacts();
            var state = CreateState();
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            var slot = EquipArtifact(state, 0, 0, "artf_astrologist_twin_stars");

            // Place two enemy creatures — only one should be hit
            var enemy1 = PlaceCreature(state, 1, 0, attack: 1, vigor: 5);
            var enemy2 = PlaceCreature(state, 1, 1, attack: 1, vigor: 5);

            slot.Charges = 2;

            // Fire ON_CHARGE_FULL
            TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 0);

            // Twin Stars full_charge: DAMAGE 3 to one enemy creature + RESET_CHARGES
            // Exactly one creature should have taken 3 damage (5-3=2)
            int damagedCount = 0;
            foreach (var e in new[] { enemy1, enemy2 })
            {
                if (e.CurrentVigor == 2)
                    damagedCount++;
            }
            Assert.Equal(1, damagedCount);
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
    public void AstrologistVariantArtifacts_LoadFromFile_AndEquip()
    {
        try
        {
            LoadAllArtifacts();

            // Verify all 4 are registered
            Assert.NotNull(ArtifactRegistry.Get("artf_astrologist_lunar_lens"));
            Assert.NotNull(ArtifactRegistry.Get("artf_astrologist_eclipse_sphere"));
            Assert.NotNull(ArtifactRegistry.Get("artf_astrologist_meteor_shower"));
            Assert.NotNull(ArtifactRegistry.Get("artf_astrologist_twin_stars"));

            // Verify class and slot
            var ll = ArtifactRegistry.Get("artf_astrologist_lunar_lens");
            Assert.Equal("astrologist", ll.Class);
            Assert.Equal("orb", ll.SlotPool);

            var es = ArtifactRegistry.Get("artf_astrologist_eclipse_sphere");
            Assert.Equal("astrologist", es.Class);
            Assert.Equal("orb", es.SlotPool);

            var ms = ArtifactRegistry.Get("artf_astrologist_meteor_shower");
            Assert.Equal("astrologist", ms.Class);
            Assert.Equal("starlight", ms.SlotPool);

            var ts = ArtifactRegistry.Get("artf_astrologist_twin_stars");
            Assert.Equal("astrologist", ts.Class);
            Assert.Equal("starlight", ts.SlotPool);

            // Equip in a headless duel
            var state = CreateState();
            state.Players[0].ArtifactClass = "astrologist";
            state.Players[0].ArtifactSlots = new ArtifactSlot[2];
            state.Players[0].ArtifactSlots[0] = new ArtifactSlot(0);
            state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

            EquipArtifact(state, 0, 0, "artf_astrologist_lunar_lens");
            EquipArtifact(state, 0, 1, "artf_astrologist_meteor_shower");

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
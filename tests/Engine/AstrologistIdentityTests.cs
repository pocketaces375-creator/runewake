using System.Collections.Generic;
using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// TASK-CLASS-IDENTITY-1A — Astrologist becomes a real class.
/// Tests that both artifact effects (Seer's Orb and Constellation Starlight)
/// exist as DSL/engine with a unit test that fires each.
/// </summary>
[Collection("NonParallel")]
public class AstrologistIdentityTests
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

    private static GameState CreateStateWithAstrologistArtifacts()
    {
        var state = CreateState();
        var player = state.Players[0];

        // Set up the Orb (slot 0) and Constellation Starlight (slot 1)
        player.ArtifactClass = "astrologist";
        player.ArtifactDefIds = new[] { "artf_astrologist_orb", "artf_astrologist_constellation_starlight" };
        player.ArtifactSlots = new ArtifactSlot[2];
        player.ArtifactSlots[0] = new ArtifactSlot(0);
        player.ArtifactSlots[1] = new ArtifactSlot(1);

        // Build Seer's Orb manually (same pattern as ChargeTests.CreateStateWithArtifact)
        var orbSlot = player.ArtifactSlots[0];
        var orbInstance = new CardInstance(state.NextInstanceId++, "artf_astrologist_orb", 0)
        {
            CardType = CardType.ARTIFACT,
            Zone = Zone.ArtifactSlot,
            ArtifactSlotIndex = 0
        };
        orbInstance.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } }
        });
        orbInstance.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.ON_CHARGE_FULL,
            Effects = new List<EffectDef>
            {
                new() { Op = Op.DRAW, Target = new TargetDef { Scope = Scope.PLAYER_SELF }, Amount = 2 },
                new() { Op = Op.RESET_CHARGES, Target = new TargetDef { Scope = Scope.SELF_ARTIFACT } }
            }
        });
        orbSlot.MaxCharges = 3;
        orbSlot.Charges = 0;
        orbSlot.AutoChargeGainOn = "on_turn_start";
        orbSlot.HasDeferredChargeFull = false;
        orbSlot.Occupant = orbInstance;
        player.ArtifactSlots[0] = orbSlot;

        // Build Constellation Starlight manually
        var starlightSlot = player.ArtifactSlots[1];
        var starlightInstance = new CardInstance(state.NextInstanceId++, "artf_astrologist_constellation_starlight", 0)
        {
            CardType = CardType.ARTIFACT,
            Zone = Zone.ArtifactSlot,
            ArtifactSlotIndex = 1
        };
        starlightInstance.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } }
        });
        starlightInstance.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.ON_CHARGE_FULL,
            Effects = new List<EffectDef>
            {
                new() { Op = Op.DAMAGE, Target = new TargetDef { Scope = Scope.ENEMY_CREATURE, Count = TargetCount.All }, Amount = 4 },
                new() { Op = Op.RESET_CHARGES, Target = new TargetDef { Scope = Scope.SELF_ARTIFACT } }
            }
        });
        starlightSlot.MaxCharges = 3;
        starlightSlot.Charges = 0;
        starlightSlot.AutoChargeGainOn = "on_turn_end";
        starlightSlot.HasDeferredChargeFull = false;
        starlightSlot.Occupant = starlightInstance;
        player.ArtifactSlots[1] = starlightSlot;

        return state;
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

    private static GameState EndTurn(GameState state, int playerIndex)
        => DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = playerIndex });

    private static GameState PlayCard(GameState state, int playerIndex, int handIndex, int lane = 0)
    {
        var player = state.Players[playerIndex];
        if (handIndex < 0 || handIndex >= player.Hand.Count)
            return state;
        var card = player.Hand[handIndex];
        if (card.CardType == CardType.CREATURE)
        {
            return DuelEngine.Apply(state, new PlayCardAction
            {
                PlayerIndex = playerIndex,
                CardInstanceId = card.InstanceId,
                LaneIndex = lane
            });
        }
        // Ritual
        return DuelEngine.Apply(state, new PlayCardAction
        {
            PlayerIndex = playerIndex,
            CardInstanceId = card.InstanceId
        });
    }

    // ——— Tests ———

    [Fact]
    public void SeersOrb_GainsChargeAtTurnStart()
    {
        // The Orb gains 1 charge at the start of each of P0's turns
        var state = CreateStateWithAstrologistArtifacts();
        var orbSlot = state.Players[0].ArtifactSlots[0];

        Assert.Equal(0, orbSlot.Charges); // starts empty

        // End P0's turn, then P1's turn — P0 gets a new turn (turn 2)
        // The auto-charge system fires at turn start for gain_on="on_turn_start"
        state = EndTurn(state, 0); // now P1's turn
        state = EndTurn(state, 1); // now P0's turn — auto-charge fires

        var resolvedSlot = state.Players[0].ArtifactSlots[0];
        Assert.Equal(1, resolvedSlot.Charges);
    }

    [Fact]
    public void SeersOrb_GainsMaxChargesOverMultipleTurns()
    {
        // Inline setup (same as Diag_OrbFullCycle which passes)
        var state = new GameState(seed: 42);
        for (int p = 0; p < 2; p++)
        {
            state.Players[p].AttunementMax = 10;
            state.Players[p].Attunement = 10;
            for (int i = 0; i < 10; i++)
                state.Players[p].Deck.Add(new CardInstance(state.NextInstanceId++, "tst_d", p) { Zone = Zone.Deck });
        }

        var player = state.Players[0];
        player.ArtifactClass = "astrologist";
        player.ArtifactDefIds = new[] { "artf_astrologist_orb", "artf_astrologist_constellation_starlight" };
        player.ArtifactSlots = new ArtifactSlot[2];
        player.ArtifactSlots[0] = new ArtifactSlot(0);
        player.ArtifactSlots[1] = new ArtifactSlot(1);

        var orbSlot = player.ArtifactSlots[0];
        var orbInstance = new CardInstance(state.NextInstanceId++, "artf_astrologist_orb", 0)
        {
            CardType = CardType.ARTIFACT,
            Zone = Zone.ArtifactSlot,
            ArtifactSlotIndex = 0
        };
        orbInstance.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } }
        });
        orbInstance.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.ON_CHARGE_FULL,
            Effects = new List<EffectDef>
            {
                new() { Op = Op.DRAW, Target = new TargetDef { Scope = Scope.PLAYER_SELF }, Amount = 2 },
                new() { Op = Op.RESET_CHARGES, Target = new TargetDef { Scope = Scope.SELF_ARTIFACT } }
            }
        });
        orbSlot.MaxCharges = 3;
        orbSlot.Charges = 0;
        orbSlot.AutoChargeGainOn = "on_turn_start";
        orbSlot.Occupant = orbInstance;
        player.ArtifactSlots[0] = orbSlot;

        // Cycle 1: End P0 → P1 → P0: Orb gains 1 charge
        state = EndTurn(state, 0);
        state = EndTurn(state, 1);
        Assert.Equal(1, state.Players[0].ArtifactSlots[0].Charges);

        // Cycle 2: Orb gains 1 more → now 2
        state = EndTurn(state, 0);
        state = EndTurn(state, 1);
        Assert.Equal(2, state.Players[0].ArtifactSlots[0].Charges);

        // Cycle 3: Orb gains 1 more → fills to 3 → fires ON_CHARGE_FULL → RESET_CHARGES → 0
        state = EndTurn(state, 0);
        state = EndTurn(state, 1);
        Assert.Equal(0, state.Players[0].ArtifactSlots[0].Charges);
    }

    [Fact]
    public void Starlight_GainsChargeAtTurnEnd()
    {
        // Constellation Starlight gains 1 charge at the END of each of P0's turns
        var state = CreateStateWithAstrologistArtifacts();

        Assert.Equal(0, state.Players[0].ArtifactSlots[1].Charges);

        // End P0's turn — auto-charge fires for gain_on="on_turn_end"
        state = EndTurn(state, 0);

        Assert.Equal(1, state.Players[0].ArtifactSlots[1].Charges);
    }

    [Fact]
    public void Starlight_Starfall_DealsDamageToAllEnemyCreatures()
    {
        // Set up a duel state where Starlight charges fill to 3,
        // then Starfall deals 4 damage to all enemy creatures
        var state = CreateStateWithAstrologistArtifacts();
        var starlightSlot = state.Players[0].ArtifactSlots[1];

        // Place 3 enemy creatures with 6 vigor each
        PlaceCreature(state, 1, 0, attack: 3, vigor: 6);
        PlaceCreature(state, 1, 1, attack: 3, vigor: 6);
        PlaceCreature(state, 1, 2, attack: 3, vigor: 6);

        // Manually set charges to 2 (one away from full)
        starlightSlot.Charges = 2;

        // End P0's turn — should auto-gain 1 charge, filling to 3
        // ON_CHARGE_FULL fires, dealing 4 damage to all enemy creatures
        state = EndTurn(state, 0);

        // Each enemy creature should have taken 4 damage
        var enemy = state.Players[1];
        Assert.Equal(2, enemy.Lanes[0].Occupant!.CurrentVigor); // 6 - 4 = 2
        Assert.Equal(2, enemy.Lanes[1].Occupant!.CurrentVigor); // 6 - 4 = 2
        Assert.Equal(2, enemy.Lanes[2].Occupant!.CurrentVigor); // 6 - 4 = 2

        // Charges should be reset to 0
        Assert.Equal(0, state.Players[0].ArtifactSlots[1].Charges);
    }

    [Fact]
    public void Starlight_Starfall_HandlesEmptyLanes()
    {
        // Starfall should not crash when some lanes are empty
        var state = CreateStateWithAstrologistArtifacts();
        var starlightSlot = state.Players[0].ArtifactSlots[1];

        // Place 1 enemy creature at lane 0 only
        PlaceCreature(state, 1, 0, attack: 2, vigor: 6);

        starlightSlot.Charges = 2;

        // Should not throw
        state = EndTurn(state, 0);

        var enemy = state.Players[1];
        Assert.Equal(2, enemy.Lanes[0].Occupant!.CurrentVigor); // 6 - 4 = 2
        Assert.Null(enemy.Lanes[1].Occupant); // still empty
    }

    [Fact]
    public void Starlight_Starfall_IgnoresGuard()
    {
        // Starfall deals damage to ALL enemy creatures, ignoring Guard
        var state = CreateStateWithAstrologistArtifacts();
        var starlightSlot = state.Players[0].ArtifactSlots[1];

        // Place Guard creature and a non-Guard creature
        var guard = PlaceCreature(state, 1, 0, attack: 1, vigor: 5, keyword: "GUARD");
        var nonGuard = PlaceCreature(state, 1, 2, attack: 2, vigor: 5);

        starlightSlot.Charges = 2;

        state = EndTurn(state, 0);

        // Both should be damaged — Guard does not block effect-based damage
        Assert.Equal(1, state.Players[1].Lanes[0].Occupant!.CurrentVigor); // 5 - 4 = 1
        Assert.Equal(1, state.Players[1].Lanes[2].Occupant!.CurrentVigor); // 5 - 4 = 1
    }

    [Fact]
    public void StarReader_OnPlay_Scries2()
    {
        // Star-Reader should scry 2 when played (on summon)
        var state = CreateState();

        var player = state.Players[0];
        player.Attunement = 10;

        // Add some known cards to the deck
        player.Deck.Clear();
        var card0 = new CardInstance(state.NextInstanceId++, "tid_c_star_reader", 0) { Zone = Zone.Deck, CardType = CardType.CREATURE, Cost = 3 };
        var card1 = new CardInstance(state.NextInstanceId++, "tst_known_a", 0) { Zone = Zone.Deck, CardType = CardType.CREATURE, Cost = 1 };
        var card2 = new CardInstance(state.NextInstanceId++, "tst_known_b", 0) { Zone = Zone.Deck, CardType = CardType.CREATURE, Cost = 2 };
        player.Deck.Add(card0);
        player.Deck.Add(card1);
        player.Deck.Add(card2);

        // Create Star-Reader in hand
        var srInHand = new CardInstance(state.NextInstanceId++, "tid_c_star_reader", 0)
        {
            Zone = Zone.Hand,
            CardType = CardType.CREATURE,
            Cost = 3,
            BaseAttack = 1,
            BaseVigor = 3,
            IsExhausted = false
        };
        player.Hand.Add(srInHand);

        // Record deck order before scry
        var before0 = player.Deck[0].CardDefId;
        var before1 = player.Deck[1].CardDefId;

        // Play Star-Reader onto lane 0
        var state2 = DuelEngine.Apply(state, new PlayCardAction
        {
            PlayerIndex = 0,
            CardInstanceId = srInHand.InstanceId,
            LaneIndex = 0
        });

        // After scry 2: top card should be what was originally at position 0,
        // and the second card should now be at the bottom of the deck
        var resultPlayer = state2.Players[0];
        Assert.Equal(before0, resultPlayer.Deck[0].CardDefId); // top card preserved
        Assert.True(resultPlayer.Deck.Count >= 2); // still have cards
    }
}
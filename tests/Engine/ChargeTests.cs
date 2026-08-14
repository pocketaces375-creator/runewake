using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// TASK-DSL-5 — Charge plumbing.
/// Covers: RESET_CHARGES op, max_per_turn and max_per_creature_per_turn caps,
/// ON_CHARGE_FULL with timing END_OF_TURN, charge freeze under suppression (G3),
/// and deferred ON_CHARGE_FULL firing at end of turn.
/// </summary>
[Collection("NonParallel")]
public class ChargeTests
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
    /// Set up a player with an artifact that has a given ChargeConfig.
    /// Returns the slot so the test can inspect state.
    /// </summary>
    private static (GameState state, ArtifactSlot slot) CreateStateWithArtifact(
        int maxCharges,
        int maxPerTurn = 0,
        int maxPerCreaturePerTurn = 0,
        bool hasImmediateChargeFull = false,
        bool hasDeferredChargeFull = false)
    {
        var state = CreateState();
        var player = state.Players[0];

        // Build the artifact definition
        var artDef = new ArtifactDef
        {
            Id = "tst_charge_artifact",
            Name = "Test Charge Artifact",
            Class = "test",
            SlotPool = "test",
            Charges = new ChargeConfig
            {
                Max = maxCharges,
                MaxPerTurn = maxPerTurn,
                MaxPerCreaturePerTurn = maxPerCreaturePerTurn
            },
            Trigger = new AbilityDef
            {
                Trigger = hasDeferredChargeFull ? Trigger.ON_CHARGE_FULL : hasImmediateChargeFull ? Trigger.ON_CHARGE_FULL : Trigger.ON_CHARGE_GAINED,
                Timing = hasDeferredChargeFull ? "END_OF_TURN" : null,
                Effects = new List<EffectDef>
                {
                    new() { Op = Op.ADD_CHARGE, Target = new TargetDef { Scope = Scope.PLAYER_SELF }, Amount = 1 }
                }
            },
            Passive = new EffectDef { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } }
        };

        // Add the artifact
        player.ArtifactClass = "test";
        player.ArtifactDefIds = new[] { "tst_charge_artifact" };
        player.ArtifactSlots = new ArtifactSlot[1];
        var slot = new ArtifactSlot(0);
        var instance = new CardInstance(state.NextInstanceId++, "tst_charge_artifact", 0)
        {
            CardType = CardType.ARTIFACT,
            Zone = Zone.Lane,
            LaneIndex = -1
        };
        instance.Abilities.Add(new AbilityDef { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { artDef.Passive } });
        instance.Abilities.Add(artDef.Trigger);

        slot.MaxCharges = artDef.Charges.Max;
        slot.Charges = 0;
        slot.ChargeConfigMaxPerTurn = artDef.Charges.MaxPerTurn;
        slot.ChargeConfigMaxPerCreaturePerTurn = artDef.Charges.MaxPerCreaturePerTurn;
        slot.HasDeferredChargeFull = hasDeferredChargeFull;
        slot.Occupant = instance;
        player.ArtifactSlots[0] = slot;

        return (state, slot);
    }

    /// <summary>
    /// Execute an ADD_CHARGE effect with the given source card (to test per-creature tracking).
    /// </summary>
    private static void ApplyCharge(GameState state, int amount, CardInstance? source = null)
    {
        var effect = new EffectDef { Op = Op.ADD_CHARGE, Amount = amount };
        var targetResolved = new PlayerTarget(state.Players[0]);
        source ??= state.Players[0].ArtifactSlots[0].Occupant!;
        EffectExecutor.Execute(effect, source, state, new List<ResolvedTarget> { targetResolved });
    }

    /// <summary>
    /// Execute a RESET_CHARGES effect.
    /// </summary>
    private static void ApplyResetCharges(GameState state)
    {
        var effect = new EffectDef { Op = Op.RESET_CHARGES };
        var targetResolved = new PlayerTarget(state.Players[0]);
        var source = state.Players[0].ArtifactSlots[0].Occupant!;
        EffectExecutor.Execute(effect, source, state, new List<ResolvedTarget> { targetResolved });
    }

    private static CardInstance MakeCreature(GameState state, int pIdx, int instanceIdOverride = -1)
    {
        int id = instanceIdOverride >= 0 ? instanceIdOverride : state.NextInstanceId++;
        var c = new CardInstance(id, "tst_creature", pIdx)
        {
            Zone = Zone.Lane,
            CardType = CardType.CREATURE,
            BaseAttack = 2,
            BaseVigor = 3,
            Cost = 2,
            IsExhausted = false
        };
        state.Players[pIdx].Lanes[0].Occupant = c;
        return c;
    }

    private static GameState EndTurn(GameState state, int playerIndex)
        => DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = playerIndex });

    // ——— Tests ———

    // — RESET_CHARGES — //

    [Fact]
    public void ResetCharges_ClearsChargesToZero()
    {
        var (state, slot) = CreateStateWithArtifact(maxCharges: 5);
        slot.AddCharges(3);

        Assert.Equal(3, slot.Charges);

        ApplyResetCharges(state);

        Assert.Equal(0, slot.Charges);
    }

    [Fact]
    public void ResetCharges_OnSlotWithZeroCharges_StaysZero()
    {
        var (state, slot) = CreateStateWithArtifact(maxCharges: 5);
        Assert.Equal(0, slot.Charges);

        ApplyResetCharges(state);

        Assert.Equal(0, slot.Charges);
    }

    [Fact]
    public void ResetCharges_DoesNotAffectMaxCharges()
    {
        var (state, slot) = CreateStateWithArtifact(maxCharges: 5);
        slot.AddCharges(4);
        ApplyResetCharges(state);

        Assert.Equal(0, slot.Charges);
        Assert.Equal(5, slot.MaxCharges);
    }

    [Fact]
    public void ResetCharges_ThroughEffectExecutor_Works()
    {
        var (state, slot) = CreateStateWithArtifact(maxCharges: 4);
        slot.AddCharges(3);
        Assert.Equal(3, slot.Charges);

        // Execute RESET_CHARGES as an EffectDef
        var effect = new EffectDef { Op = Op.RESET_CHARGES };
        var targets = new List<ResolvedTarget> { new PlayerTarget(state.Players[0]) };
        EffectExecutor.Execute(effect, slot.Occupant!, state, targets);

        Assert.Equal(0, slot.Charges);
    }

    // — max_per_turn — //

    [Fact]
    public void MaxPerTurn_LimitsTotalChargeGains()
    {
        var (state, slot) = CreateStateWithArtifact(maxCharges: 10, maxPerTurn: 2);

        slot.AddCharges(1);
        Assert.Equal(1, slot.Charges);
        Assert.Equal(1, slot.ChargesGainedThisTurn);

        slot.AddCharges(1);
        Assert.Equal(2, slot.Charges);
        Assert.Equal(2, slot.ChargesGainedThisTurn);

        // Third charge same turn should be capped
        slot.AddCharges(1);
        Assert.Equal(2, slot.Charges); // not 3
        Assert.Equal(2, slot.ChargesGainedThisTurn);
    }

    [Fact]
    public void MaxPerTurn_ReturnsZeroWhenCapped()
    {
        var (state, slot) = CreateStateWithArtifact(maxCharges: 10, maxPerTurn: 1);

        int added = slot.AddCharges(1);
        Assert.Equal(1, added);
        Assert.Equal(1, slot.Charges);

        added = slot.AddCharges(1);
        Assert.Equal(0, added);
        Assert.Equal(1, slot.Charges);
    }

    [Fact]
    public void MaxPerTurn_ZeroMeansUnlimited()
    {
        var (state, slot) = CreateStateWithArtifact(maxCharges: 10, maxPerTurn: 0);

        slot.AddCharges(5);
        Assert.Equal(5, slot.Charges);
        Assert.Equal(5, slot.ChargesGainedThisTurn);

        slot.AddCharges(5);
        Assert.Equal(10, slot.Charges);
        Assert.Equal(10, slot.ChargesGainedThisTurn);
    }

    [Fact]
    public void MaxPerTurn_ResetsOnTurnStart()
    {
        var (state, slot) = CreateStateWithArtifact(maxCharges: 10, maxPerTurn: 2);

        slot.AddCharges(2);
        Assert.Equal(2, slot.Charges);
        Assert.Equal(2, slot.ChargesGainedThisTurn);

        // End P0's turn — P1 becomes active. P0's tracking does NOT reset yet.
        // Only the next player's (P1's) tracking resets on their turn start.
        state = EndTurn(state, 0);

        // After EndTurn(0), P0's slot tracking is still at 2 because
        // charge tracking resets for the next player (P1), not the ending player.
        // End P1's turn — P0 becomes active again, finally resetting P0's tracking.
        state = EndTurn(state, 1);

        // Re-resolve the slot from the new state after clone
        var p0 = state.Players[0];
        var resolvedSlot = p0.ArtifactSlots[0];

        Assert.Equal(0, resolvedSlot.ChargesGainedThisTurn);

        // Now we can gain charges again — tracking reset, so 2 more added
        resolvedSlot.AddCharges(2);
        Assert.Equal(4, resolvedSlot.Charges); // 2 original + 2 new
    }

    // — max_per_creature_per_turn — //

    [Fact]
    public void MaxPerCreaturePerTurn_LimitsPerCreature()
    {
        var (state, slot) = CreateStateWithArtifact(maxCharges: 10, maxPerCreaturePerTurn: 1);

        var creature1 = MakeCreature(state, 0, instanceIdOverride: 1001);
        var creature2 = MakeCreature(state, 0, instanceIdOverride: 1002);
        state.Players[0].Lanes[1].Occupant = creature2;

        // Creature 1 adds 1 charge
        int added = slot.AddCharges(1, creature1.InstanceId);
        Assert.Equal(1, added);
        Assert.Equal(1, slot.Charges);

        // Creature 1 tries again — should be capped
        added = slot.AddCharges(1, creature1.InstanceId);
        Assert.Equal(0, added);
        Assert.Equal(1, slot.Charges);

        // Creature 2 can still add (different creature)
        added = slot.AddCharges(1, creature2.InstanceId);
        Assert.Equal(1, added);
        Assert.Equal(2, slot.Charges);
    }

    [Fact]
    public void MaxPerCreaturePerTurn_ZeroMeansUnlimited()
    {
        var (state, slot) = CreateStateWithArtifact(maxCharges: 10, maxPerCreaturePerTurn: 0);

        var creature = MakeCreature(state, 0, instanceIdOverride: 2001);

        slot.AddCharges(3, creature.InstanceId);
        Assert.Equal(3, slot.Charges);

        slot.AddCharges(3, creature.InstanceId);
        Assert.Equal(6, slot.Charges);
    }

    [Fact]
    public void MaxPerCreaturePerTurn_WorksWithoutCreatureId()
    {
        // Without a creature ID, only max_per_turn applies (not per-creature)
        var (state, slot) = CreateStateWithArtifact(maxCharges: 10, maxPerCreaturePerTurn: 1);

        // No creature ID — per-creature limit doesn't apply
        int added = slot.AddCharges(5);
        Assert.Equal(5, added);
        Assert.Equal(5, slot.Charges);
    }

    [Fact]
    public void MaxPerCreaturePerTurn_CombinedWithMaxPerTurn()
    {
        var (state, slot) = CreateStateWithArtifact(maxCharges: 10, maxPerTurn: 3, maxPerCreaturePerTurn: 1);

        var creature1 = MakeCreature(state, 0, instanceIdOverride: 3001);
        var creature2 = MakeCreature(state, 0, instanceIdOverride: 3002);
        state.Players[0].Lanes[1].Occupant = creature2;
        var creature3 = MakeCreature(state, 0, instanceIdOverride: 3003);
        state.Players[0].Lanes[2].Occupant = creature3;

        // Creature 1: 1 charge (per-creature cap)
        slot.AddCharges(1, creature1.InstanceId);
        Assert.Equal(1, slot.Charges);

        // Creature 2: 1 charge (per-creature cap)
        slot.AddCharges(1, creature2.InstanceId);
        Assert.Equal(2, slot.Charges);

        // Creature 3: 1 charge (total per-turn cap of 3)
        slot.AddCharges(1, creature3.InstanceId);
        Assert.Equal(3, slot.Charges);

        // Creature 1 tries again — per-creature capped
        slot.AddCharges(1, creature1.InstanceId);
        Assert.Equal(3, slot.Charges);
    }

    // — Suppression freeze (G3) — //

    [Fact]
    public void ChargeGain_BlockedWhenSuppressed()
    {
        var (state, slot) = CreateStateWithArtifact(maxCharges: 5);
        slot.IsSuppressed = true;

        slot.AddCharges(3);

        Assert.Equal(0, slot.Charges); // No charges gained while suppressed
    }

    [Fact]
    public void ChargeGain_ResumesWhenUnsuppressed()
    {
        var (state, slot) = CreateStateWithArtifact(maxCharges: 5);

        // Suppress
        slot.IsSuppressed = true;
        slot.AddCharges(3);
        Assert.Equal(0, slot.Charges);

        // Unsuppress
        slot.IsSuppressed = false;
        slot.AddCharges(3);
        Assert.Equal(3, slot.Charges);
    }

    [Fact]
    public void ChargeGain_ResetsChargeTrackingOnTurnStart()
    {
        var (state, slot) = CreateStateWithArtifact(maxCharges: 10, maxPerTurn: 2);

        slot.AddCharges(2);
        Assert.Equal(2, slot.ChargesGainedThisTurn);

        // End P0's turn, then P1's turn — P0 becomes active and tracking resets
        state = EndTurn(state, 0);
        state = EndTurn(state, 1);
        var resolvedSlot = state.Players[0].ArtifactSlots[0];

        Assert.Equal(0, resolvedSlot.ChargesGainedThisTurn);
    }

    [Fact]
    public void SuppressionFreeze_WorksThroughEffectExecutor()
    {
        var (state, slot) = CreateStateWithArtifact(maxCharges: 5);
        slot.IsSuppressed = true;

        // Try ADD_CHARGE through EffectExecutor
        ApplyCharge(state, 3);

        Assert.Equal(0, slot.Charges);
    }

    // — ON_CHARGE_FULL with timing END_OF_TURN — //

    [Fact]
    public void DeferredChargeFull_DoesNotFireImmediately()
    {
        var (state, slot) = CreateStateWithArtifact(
            maxCharges: 3,
            hasDeferredChargeFull: true);

        // Go through EffectExecutor to set PendingChargeFull
        ApplyCharge(state, 3);
        slot = state.Players[0].ArtifactSlots[0];

        Assert.Equal(3, slot.Charges);
        Assert.True(slot.PendingChargeFull);
        Assert.True(slot.HasDeferredChargeFull);
    }

    [Fact]
    public void ImmediateChargeFull_FiresOnFill()
    {
        var (state, slot) = CreateStateWithArtifact(
            maxCharges: 3,
            hasImmediateChargeFull: true);

        // Add 3 charges — should fire ON_CHARGE_FULL immediately (no deferred)
        slot.AddCharges(3);

        Assert.Equal(3, slot.Charges);
        Assert.False(slot.PendingChargeFull);
    }

    [Fact]
    public void DeferredChargeFull_NoTiming_DoesNotSetPending()
    {
        var (state, slot) = CreateStateWithArtifact(
            maxCharges: 3,
            hasImmediateChargeFull: true);

        // Not deferred
        Assert.False(slot.HasDeferredChargeFull);

        slot.AddCharges(3);
        Assert.False(slot.PendingChargeFull);
    }

    [Fact]
    public void DeferredChargeFull_ClearsOnTurnEnd()
    {
        var (state, slot) = CreateStateWithArtifact(
            maxCharges: 3,
            hasDeferredChargeFull: true);

        // Fill charges through EffectExecutor
        ApplyCharge(state, 3);
        slot = state.Players[0].ArtifactSlots[0];
        Assert.True(slot.PendingChargeFull);

        // End P0's turn — should fire deferred ON_CHARGE_FULL and clear
        state = EndTurn(state, 0);
        slot = state.Players[0].ArtifactSlots[0];

        Assert.Equal(3, slot.Charges);
        Assert.False(slot.PendingChargeFull);
    }

    [Fact]
    public void DeferredChargeFull_ThroughEffectExecutor_SetsPending()
    {
        var (state, slot) = CreateStateWithArtifact(
            maxCharges: 3,
            hasDeferredChargeFull: true);

        // Apply charge through EffectExecutor
        ApplyCharge(state, 3);

        Assert.True(slot.PendingChargeFull);
        Assert.Equal(3, slot.Charges);
    }

    // — Edge cases — //

    [Fact]
    public void AddCharges_NegativeAmount_DoesNothing()
    {
        var (state, slot) = CreateStateWithArtifact(maxCharges: 5);
        slot.AddCharges(-1);
        Assert.Equal(0, slot.Charges);
    }

    [Fact]
    public void ArtifactWithNoCharges_CannotReceiveCharges()
    {
        var (state, slot) = CreateStateWithArtifact(maxCharges: 0);
        slot.AddCharges(3);
        Assert.Equal(0, slot.Charges);
    }

    [Fact]
    public void AddCharges_CapsAtMaxCharges()
    {
        var (state, slot) = CreateStateWithArtifact(maxCharges: 5);
        slot.AddCharges(10);
        Assert.Equal(5, slot.Charges);
    }

    [Fact]
    public void AddCharges_ReturnsActualAmountAdded()
    {
        var (state, slot) = CreateStateWithArtifact(maxCharges: 5);

        int added = slot.AddCharges(3);
        Assert.Equal(3, added);

        added = slot.AddCharges(3); // would exceed max — should cap at 2
        Assert.Equal(2, added);
        Assert.Equal(5, slot.Charges);
    }

    [Fact]
    public void ChargeFull_DeferredClearedOnResetChargeTracking()
    {
        var (state, slot) = CreateStateWithArtifact(
            maxCharges: 3,
            hasDeferredChargeFull: true);

        // Go through EffectExecutor so PendingChargeFull gets set
        ApplyCharge(state, 3);
        slot = state.Players[0].ArtifactSlots[0];

        Assert.True(slot.PendingChargeFull);

        slot.ResetChargeTracking();
        Assert.False(slot.PendingChargeFull);
        Assert.Equal(0, slot.ChargesGainedThisTurn);
    }

    [Fact]
    public void StateHash_IncludesChargeFields()
    {
        var (state1, slot1) = CreateStateWithArtifact(maxCharges: 5);
        var (state2, slot2) = CreateStateWithArtifact(maxCharges: 5);

        // Same state — same hash
        Assert.Equal(state1.ComputeStateHash(), state2.ComputeStateHash());

        // Different charges — different hash
        slot1.AddCharges(3);
        Assert.NotEqual(state1.ComputeStateHash(), state2.ComputeStateHash());
    }

    [Fact]
    public void Clone_PreservesChargeTracking()
    {
        var (state, slot) = CreateStateWithArtifact(maxCharges: 5, maxPerTurn: 3);
        slot.AddCharges(2, creatureInstanceId: 999);

        var cloned = slot.Clone();
        Assert.Equal(slot.Charges, cloned.Charges);
        Assert.Equal(slot.ChargesGainedThisTurn, cloned.ChargesGainedThisTurn);
        Assert.Equal(slot.ChargeConfigMaxPerTurn, cloned.ChargeConfigMaxPerTurn);
        Assert.Equal(slot.ChargeConfigMaxPerCreaturePerTurn, cloned.ChargeConfigMaxPerCreaturePerTurn);

        // Per-creature tracking preserved
        Assert.True(cloned.ChargesGainedThisTurnByCreature.ContainsKey(999));
        Assert.Equal(2, cloned.ChargesGainedThisTurnByCreature[999]);
    }
}
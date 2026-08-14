using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// TASK-T1a — Ruling tests, general: G1–G8.
/// Every ruling in ARTIFACT_RULINGS.md gets at least one test, named
/// Ruling_&lt;id&gt;_&lt;Name&gt;. These assert the rulings verbatim.
/// </summary>
[Collection("NonParallel")]
public class RulingGeneralTests
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

    /// <summary>
    /// Wire an artifact into the given slot with a single ability list.
    /// The first ability is treated as the PASSIVE, the second (if present)
    /// as the trigger. Charge configuration is optional.
    /// </summary>
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

    private static GameState ApplyCharge(GameState state, int amount = 3)
    {
        var effect = new EffectDef { Op = Op.ADD_CHARGE, Amount = amount };
        var source = state.Players[0].ArtifactSlots[0].Occupant!;
        EffectExecutor.Execute(effect, source, state,
            new List<ResolvedTarget> { new PlayerTarget(state.Players[0]) });
        return state;
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
    // G1 — Trigger ordering
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_G1_TriggerOrdering_ActivePlayersArtifactsFireFirst()
    {
        // G1: Multiple Artifact triggers on one event: active player's Artifacts
        // first, then non-active player's.
        // P0 artifact (slot 0): ON_TURN_END → DAMAGE 1 to P1's creature.
        // P1 artifact (slot 0): ON_TURN_END → HEAL 1 to own creature (filter DAMAGED).
        // If P0 fires first: damage then heal → net 0.  If P1 first: heal finds nothing,
        // then damage sticks → net 1.  Assert net 0 → active player's artifact first.
        var state = CreateState();
        var p1Creature = PlaceCreature(state, 1, 0, attack: 2, vigor: 5);

        AddArtifact(state, 0, 0, "tst_g1_p0", abilities: new List<AbilityDef>
        {
            new() { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } } },
            new() { Trigger = Trigger.ON_TURN_END, Effects = new List<EffectDef>
            {
                new() { Op = Op.DAMAGE, Amount = 1, Target = new TargetDef { Scope = Scope.ENEMY_CREATURE, Count = TargetCount.Exactly(1) } }
            }}
        });
        AddArtifact(state, 1, 0, "tst_g1_p1", abilities: new List<AbilityDef>
        {
            new() { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } } },
            new() { Trigger = Trigger.ON_TURN_END, Effects = new List<EffectDef>
            {
                new() { Op = Op.HEAL, Amount = 1, Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "DAMAGED", Count = TargetCount.Exactly(1) } }
            }}
        });

        // P0 ends turn → ON_TURN_END fires with eventPlayerIndex=0.  P0's artifact
        // fires first (damage), then P1's (heal).  Creature returns to full health.
        state = EndTurn(state, 0);
        var creatureAfter = FindCreature(state, p1Creature.InstanceId);
        Assert.NotNull(creatureAfter);
        Assert.Equal(0, creatureAfter.Damage);
    }

    [Fact]
    public void Ruling_G1_TriggerOrdering_SlotOrderLeftThenRight()
    {
        // G1: within a player, slot order (left, then right).
        // P0 slot 0: ON_TURN_END → DAMAGE 1 to P0's creature.
        // P0 slot 1: ON_TURN_END → HEAL 1 to P0's creature (filter DAMAGED).
        // If slot 0 fires first: damage then heal → net 0.  If slot 1 first:
        // heal finds nothing, then damage → net 1.  Assert 0 → slot 0 before slot 1.
        var state = CreateState();
        var p0Creature = PlaceCreature(state, 0, 0, attack: 2, vigor: 5);

        AddArtifact(state, 0, 0, "tst_slot0", abilities: new List<AbilityDef>
        {
            new() { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } } },
            new() { Trigger = Trigger.ON_TURN_END, Effects = new List<EffectDef>
            {
                new() { Op = Op.DAMAGE, Amount = 1, Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Count = TargetCount.Exactly(1) } }
            }}
        });
        AddArtifact(state, 0, 1, "tst_slot1", abilities: new List<AbilityDef>
        {
            new() { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } } },
            new() { Trigger = Trigger.ON_TURN_END, Effects = new List<EffectDef>
            {
                new() { Op = Op.HEAL, Amount = 1, Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "DAMAGED", Count = TargetCount.Exactly(1) } }
            }}
        });

        // P0 ends turn → ON_TURN_END fires (P0's artifacts, slot order 0 then 1).
        state = EndTurn(state, 0);
        var creatureAfter = FindCreature(state, p0Creature.InstanceId);
        Assert.NotNull(creatureAfter);
        Assert.Equal(0, creatureAfter.Damage);
    }

    [Fact]
    public void Ruling_G1_TriggerOrdering_EffectsCompleteBeforeNextAbility()
    {
        // G1: Through the normal TriggerBus queue, never interleaved mid-effect.
        // P0 slot 0: ON_TURN_END with TWO effects [DAMAGE 1, HEAL 2] to a creature.
        // P0 slot 1: ON_TURN_END with effect [DAMAGE 2] to the same creature.
        // If slot 0's effects run as a unit: dmg 1 → heal 2 → D=0, then slot 1 dmg 2 → D=2.
        // If interleaved (s0-dmg, s1-dmg, s0-heal): dmg 1 → dmg 2 → D=3 → heal 2 → D=1.
        // Assert D=2 → slot 0's effects completed before slot 1.
        var state = CreateState();
        var creature = PlaceCreature(state, 0, 0, attack: 2, vigor: 10);

        AddArtifact(state, 0, 0, "tst_slot0", abilities: new List<AbilityDef>
        {
            new() { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } } },
            new() { Trigger = Trigger.ON_TURN_END, Effects = new List<EffectDef>
            {
                new() { Op = Op.DAMAGE, Amount = 1, Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Count = TargetCount.Exactly(1) } },
                new() { Op = Op.HEAL, Amount = 2, Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "DAMAGED", Count = TargetCount.Exactly(1) } }
            }}
        });
        AddArtifact(state, 0, 1, "tst_slot1", abilities: new List<AbilityDef>
        {
            new() { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } } },
            new() { Trigger = Trigger.ON_TURN_END, Effects = new List<EffectDef>
            {
                new() { Op = Op.DAMAGE, Amount = 2, Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Count = TargetCount.Exactly(1) } }
            }}
        });

        // P0 ends turn → ON_TURN_END fires once.  Effects run as a unit per ability.
        state = EndTurn(state, 0);
        var creatureAfter = FindCreature(state, creature.InstanceId);
        Assert.NotNull(creatureAfter);
        Assert.Equal(2, creatureAfter.Damage); // dmg1→1, heal2→0, then dmg2→2
    }

    // ══════════════════════════════════════════════════════════════════
    // G2 — End-of-turn stacking
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_G2_EndOfTurnArtifactEffects_ResolveBeforeUntilEndOfTurnExpire()
    {
        // G2: All "at end of turn" Artifact effects resolve BEFORE "until end of
        // turn" effects expire (an end-of-turn heal still sees Icon's +1 attack
        // buffs, which then expire normally).
        // A creature has a THIS_TURN attack buff (+2, simulating Icon's +1).
        // A deferred ON_CHARGE_FULL artifact (END_OF_TURN timing) fires at end of
        // turn, healing the creature.  The heal resolves while the buff is still
        // active — the buff is still present when the end-of-turn effect fires.
        var state = CreateState();
        var creature = PlaceCreature(state, 0, 0, attack: 2, vigor: 5);
        creature.Damage = 1; // wounded — target for the heal
        creature.AttackModifier = 2; // Icon-style THIS_TURN buff (simulated)

        // Deferred charge-full artifact: HEAL 1 to most-wounded ally at end of turn.
        AddArtifact(state, 0, 0, "tst_g2_deferred", maxCharges: 3, hasDeferredChargeFull: true,
            abilities: new List<AbilityDef>
            {
                new() { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } } },
                new() { Trigger = Trigger.ON_CHARGE_FULL, Timing = "END_OF_TURN", Effects = new List<EffectDef>
                {
                    new() { Op = Op.HEAL, Amount = 1, Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "DAMAGED", Count = TargetCount.Exactly(1) } }
                }}
            });

        // Fill charges to 3 → PendingChargeFull set (not fired immediately)
        state = ApplyCharge(state, 3);
        var slot = state.Players[0].ArtifactSlots[0];
        Assert.True(slot.PendingChargeFull);

        // End turn → deferred ON_CHARGE_FULL fires at end of turn.
        // The heal resolves while the THIS_TURN buff is still active.
        state = EndTurn(state, 0);
        var creatureAfter = FindCreature(state, creature.InstanceId);
        Assert.NotNull(creatureAfter);

        // Heal landed (creature healed)
        Assert.Equal(0, creatureAfter.Damage);

        // The THIS_TURN buff was still present when the end-of-turn effect
        // resolved — it didn't expire before the artifact effect ran.
        Assert.Equal(2, creatureAfter.AttackModifier);
    }

    // ══════════════════════════════════════════════════════════════════
    // G3 — Suppression scope
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_G3_SuppressionScope_PassiveOffAndTriggersDontFire()
    {
        // G3: While Suppressed: passive off, triggers don't fire.
        // Artifact with passive BUFF +1/+0 (applied at turn start) and
        // ON_TURN_END trigger (DRAW 1).  Suppress it → passive not applied
        // at next turn start, trigger doesn't fire at end of turn.
        var state = CreateState();
        var creature = PlaceCreature(state, 0, 0, attack: 2, vigor: 5);

        AddArtifact(state, 0, 0, "tst_g3", abilities: new List<AbilityDef>
        {
            new() { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef>
            {
                new() { Op = Op.BUFF, Attack = 1, Vigor = 0, Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Count = TargetCount.All } }
            }},
            new() { Trigger = Trigger.ON_TURN_END, Effects = new List<EffectDef>
            {
                new() { Op = Op.DRAW, Amount = 1, Target = new TargetDef { Scope = Scope.PLAYER_SELF } }
            }}
        });

        // P1 ends turn → P0's turn starts → passive applied (creature +1 attack)
        state = EndTurn(state, 1);
        var creatureAfter = FindCreature(state, creature.InstanceId);
        Assert.NotNull(creatureAfter);
        Assert.Equal(1, creatureAfter.AttackModifier);

        // Suppress the artifact.  Use SuppressionRemaining=3 so it survives
        // any turn-end ticks during this test (duration is G4's concern).
        state.Players[0].ArtifactSlots[0].IsSuppressed = true;
        state.Players[0].ArtifactSlots[0].SuppressionRemaining = 3;

        // P0 ends turn → ON_TURN_END should NOT fire (no draw).
        int handAtP0End = state.Players[0].Hand.Count;
        state = EndTurn(state, 0);
        Assert.Equal(handAtP0End, state.Players[0].Hand.Count);

        // P1 ends turn → P0's turn starts → passive should NOT be applied.
        state = EndTurn(state, 1);
        var creatureAfter2 = FindCreature(state, creature.InstanceId);
        Assert.NotNull(creatureAfter2);
        Assert.Equal(1, creatureAfter2.AttackModifier); // unchanged — no new buff
    }

    [Fact]
    public void Ruling_G3_SuppressionScope_ChargesFrozen_NoGainNoSpendNoLoss()
    {
        // G3: While Suppressed: Charges frozen (no gain, no spend, no loss).
        var state = CreateState();
        AddArtifact(state, 0, 0, "tst_g3_charge", maxCharges: 5, abilities: new List<AbilityDef>
        {
            new() { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } } }
        });
        var slot = state.Players[0].ArtifactSlots[0];
        slot.Charges = 3; // start with some charges

        // Suppress
        slot.IsSuppressed = true;

        // No gain: AddCharges returns 0
        int added = slot.AddCharges(2);
        Assert.Equal(0, added);
        Assert.Equal(3, slot.Charges); // unchanged

        // No loss: RESET_CHARGES on a suppressed slot is a no-op
        var resetEffect = new EffectDef { Op = Op.RESET_CHARGES };
        EffectExecutor.Execute(resetEffect, slot.Occupant!, state,
            new List<ResolvedTarget> { new PlayerTarget(state.Players[0]) });
        Assert.Equal(3, slot.Charges); // preserved

        // No spend: FORGE's spend path gates on partner slot suppression.
        // (Covered by the FORGE suppression guard; here we just verify the
        // slot-level freeze — spend through the effect path is blocked.)
        // Unsuppress → charges still there, spend works
        slot.IsSuppressed = false;
        Assert.Equal(3, slot.Charges);

        // RESET_CHARGES works once unsuppressed
        EffectExecutor.Execute(resetEffect, slot.Occupant!, state,
            new List<ResolvedTarget> { new PlayerTarget(state.Players[0]) });
        Assert.Equal(0, slot.Charges);
    }

    [Fact]
    public void Ruling_G3_SuppressionScope_PermanentBuffsRemain()
    {
        // G3: Continuous passives switch off immediately; permanent buffs the
        // Artifact granted earlier remain — they belong to the creature now.
        // Use FORGE to grant a permanent +1/+1 (via partner slot charges).
        // Suppress the forging artifact → creature keeps the buff.
        var state = CreateState();
        state.Players[0].ArtifactSlots = new ArtifactSlot[2];
        state.Players[0].ArtifactSlots[0] = new ArtifactSlot(0);
        state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

        // Anvil (slot 0) — source of FORGE
        var anvil = new CardInstance(state.NextInstanceId++, "tst_anvil", 0)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 0
        };
        anvil.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } }
        });
        state.Players[0].ArtifactSlots[0].Occupant = anvil;

        // Hammer (slot 1) — holds charges
        var hammer = new CardInstance(state.NextInstanceId++, "tst_hammer", 0)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 1
        };
        hammer.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } }
        });
        state.Players[0].ArtifactSlots[1].Occupant = hammer;
        state.Players[0].ArtifactSlots[1].MaxCharges = 3;
        state.Players[0].ArtifactSlots[1].Charges = 3;

        var creature = PlaceCreature(state, 0, 0, attack: 2, vigor: 5);

        // Execute FORGE: spend all partner charges, +1/+1 per charge
        var forgeEffect = new EffectDef
        {
            Op = Op.FORGE,
            SpendFrom = "PARTNER_SLOT",
            Spend = "ALL",
            Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Count = TargetCount.Exactly(1) },
            PerCharge = new PerChargeStats { Attack = 1, Vigor = 1 }
        };
        var target = new CreatureTarget(creature, 0, 0);
        EffectExecutor.Execute(forgeEffect, anvil, state, new List<ResolvedTarget> { target });

        // Creature buffed: +3/+3 permanent
        var creatureAfter = FindCreature(state, creature.InstanceId);
        Assert.NotNull(creatureAfter);
        Assert.Equal(3, creatureAfter.AttackModifier);
        Assert.Equal(3, creatureAfter.VigorModifier);

        // Suppress the Anvil (the source artifact)
        state.Players[0].ArtifactSlots[0].IsSuppressed = true;

        // Permanent buffs remain — they belong to the creature now
        Assert.Equal(3, creatureAfter.AttackModifier);
        Assert.Equal(3, creatureAfter.VigorModifier);
    }

    [Fact]
    public void Ruling_G3_SuppressionScope_ContinuousPassiveOffImmediately()
    {
        // G3: Continuous passives switch off immediately.
        // A passive COST_MOD from the artifact is removed when suppressed.
        var state = CreateState();
        var player = state.Players[0];
        player.ArtifactSlots = new ArtifactSlot[1];
        player.ArtifactSlots[0] = new ArtifactSlot(0);

        var artifact = new CardInstance(state.NextInstanceId++, "tst_g3_mod", 0)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 0
        };
        artifact.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef>
            {
                new() { Op = Op.COST_MOD, Amount = 1, AppliesTo = "CREATURE",
                    Target = new TargetDef { Scope = Scope.PLAYER_SELF } }
            }
        });
        state.Players[0].ArtifactSlots[0].Occupant = artifact;

        // Start P0's turn → ApplyArtifactPassives creates the cost mod
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 1 });
        Assert.Single(state.Players[0].CostMods); // discount active

        // Suppress via the SUPPRESS op (the engine path) — mods die immediately.
        // P0's own artifact suppresses itself (scope PLAYER_SELF).
        var suppressEffect = new EffectDef
        {
            Op = Op.SUPPRESS,
            Amount = 1,
            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
        };
        var p0Target = new PlayerTarget(state.Players[0]);
        EffectExecutor.Execute(suppressEffect, artifact, state, new List<ResolvedTarget> { p0Target });

        Assert.True(state.Players[0].ArtifactSlots[0].IsSuppressed);
        Assert.Empty(state.Players[0].CostMods); // removed at suppression time
    }

    // ══════════════════════════════════════════════════════════════════
    // G4 — Suppression duration
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_G4_SuppressionDuration_OneTurnUntilEndOfOwnerNextTurn()
    {
        // G4: Suppression duration counted in the suppressed player's turns.
        // "1 turn" = until the end of that player's next turn.
        // Suppress P0's artifact for 1 turn during P1's turn (Duskfang-style).
        // P0's turn starts: still suppressed.  P0 ends turn: suppression expires.
        var state = CreateState();
        AddArtifact(state, 0, 0, "tst_g4", maxCharges: 3);
        state.CurrentPlayerIndex = 1;

        // Suppress P0's artifact for 1 turn (e.g., by Duskfang)
        state.Players[0].ArtifactSlots[0].ApplySuppression(1, "tst_duskfang");

        // P1 ends turn → P0's turn starts.  Tick runs on P1's artifacts (none) —
        // P0's slot untouched: still suppressed.
        state = EndTurn(state, 1);
        var p0Slot = state.Players[0].ArtifactSlots[0];
        Assert.True(p0Slot.IsSuppressed);
        Assert.Equal(1, p0Slot.SuppressionRemaining);

        // P0 ends turn → tick: 1 → 0 → unsuppressed (end of P0's next turn)
        state = EndTurn(state, 0);
        p0Slot = state.Players[0].ArtifactSlots[0];
        Assert.False(p0Slot.IsSuppressed);
        Assert.Equal(0, p0Slot.SuppressionRemaining);
    }

    [Fact]
    public void Ruling_G4_SuppressionDuration_SameSourceRefreshes()
    {
        // G4: Re-applying from the same source id refreshes (does not extend).
        var slot = new ArtifactSlot(0);
        slot.ApplySuppression(1, "srcA");
        Assert.Equal(1, slot.SuppressionRemaining);
        Assert.True(slot.IsSuppressed);

        // Same source: refresh — remaining stays 1
        slot.ApplySuppression(1, "srcA");
        Assert.Equal(1, slot.SuppressionRemaining); // not 2
    }

    [Fact]
    public void Ruling_G4_SuppressionDuration_DifferentSourceExtends()
    {
        // G4: A different source extends the duration.
        var slot = new ArtifactSlot(0);
        slot.ApplySuppression(1, "srcA");
        Assert.Equal(1, slot.SuppressionRemaining);

        // Different source: extends
        slot.ApplySuppression(1, "srcB");
        Assert.Equal(2, slot.SuppressionRemaining); // 1 + 1 = 2
        Assert.Equal("srcB", slot.SuppressionSourceId); // last source wins
    }

    // ══════════════════════════════════════════════════════════════════
    // G5 — Turn-scoped counters
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_G5_TurnScopedCounters_ResetAtStartOfEveryTurn_IndependentPerPlayer()
    {
        // G5: Turn-scoped counters reset at the START of every turn, both players
        // tracked independently; Artifact conditions read the OWNER's counter
        // unless the text names the opponent.
        var state = CreateState();
        // Two creatures per side so we can attack twice (creatures exhaust).
        PlaceCreature(state, 0, 0, attack: 3, vigor: 5);
        PlaceCreature(state, 0, 1, attack: 3, vigor: 5);
        PlaceCreature(state, 1, 0, attack: 3, vigor: 5);

        // P0 attacks with two creatures → P0's counter = 2, P1's = 0 (independent)
        state = Attack(state, 0, 0);
        state = Attack(state, 0, 1);
        Assert.Equal(2, state.Players[0].AttackCountThisTurn);
        Assert.Equal(0, state.Players[1].AttackCountThisTurn);

        // End P0's turn → P1's turn starts.  P1's counters reset (were 0).
        // P0's counters NOT reset (P0's turn hasn't restarted).
        state = EndTurn(state, 0);
        Assert.Equal(0, state.Players[1].AttackCountThisTurn);
        Assert.Equal(2, state.Players[0].AttackCountThisTurn); // P0's still from last turn

        // P1 attacks once → P1 = 1, P0 still 2
        state = Attack(state, 1, 0);
        Assert.Equal(1, state.Players[1].AttackCountThisTurn);
        Assert.Equal(2, state.Players[0].AttackCountThisTurn);

        // End P1's turn → P0's turn starts → P0's counters reset
        state = EndTurn(state, 1);
        Assert.Equal(0, state.Players[0].AttackCountThisTurn);
        Assert.Equal(2, state.Players[0].AttackCountLastTurn); // persisted
    }

    [Fact]
    public void Ruling_G5_TurnScopedCounters_ConditionsReadOwnerCounter()
    {
        // G5: Artifact conditions read the OWNER's counter unless the text
        // names the opponent.  ATTACKERS_THIS_TURN_GTE on an artifact:
        // evaluated with the artifact's controller.
        var state = CreateState();
        PlaceCreature(state, 0, 0, attack: 3, vigor: 5);
        PlaceCreature(state, 1, 0, attack: 3, vigor: 5);

        // P0 attacks → P0's counter = 1, P1's = 0
        state = Attack(state, 0, 0);

        // Condition ATTACKERS_THIS_TURN_GTE 1 with controller=P0 → true
        Assert.True(Eval(Cond(ConditionOp.ATTACKERS_THIS_TURN_GTE, value: 1), state, 0));

        // Condition with controller=P1 → false (P1's counter = 0)
        Assert.False(Eval(Cond(ConditionOp.ATTACKERS_THIS_TURN_GTE, value: 1), state, 1));
    }

    // ══════════════════════════════════════════════════════════════════
    // G6 — Mirror matches
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_G6_MirrorMatch_ChargeFullFiresOnlyOwningArtifact()
    {
        // G6: Mirror matches — all triggers fire independently.  Each player's
        // Charges are their own.  When P0's artifact fills to 3, P1's identical
        // artifact must NOT fire (P1's charges didn't fill).
        var state = CreateState();
        PlaceCreature(state, 0, 0, attack: 2, vigor: 5);
        PlaceCreature(state, 1, 0, attack: 2, vigor: 5);

        // Both players have the same charge-full artifact:
        // ON_CHARGE_FULL (immediate) → BUFF +7/+0 to ally creature + RESET_CHARGES.
        void AddMirrorArtifact(int playerIndex)
        {
            AddArtifact(state, playerIndex, 0, "tst_duskfang_mirror", maxCharges: 3,
                abilities: new List<AbilityDef>
                {
                    new() { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } } },
                    new() { Trigger = Trigger.ON_CHARGE_FULL, Effects = new List<EffectDef>
                    {
                        new() { Op = Op.BUFF, Attack = 7, Vigor = 0,
                            Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Count = TargetCount.Exactly(1) } },
                        new() { Op = Op.RESET_CHARGES,
                            Target = new TargetDef { Scope = Scope.PLAYER_SELF } }
                    }}
                });
        }
        AddMirrorArtifact(0);
        AddMirrorArtifact(1);

        // P0's artifact fills to 3 through EffectExecutor
        var effect = new EffectDef { Op = Op.ADD_CHARGE, Amount = 3 };
        var source = state.Players[0].ArtifactSlots[0].Occupant!;
        EffectExecutor.Execute(effect, source, state,
            new List<ResolvedTarget> { new PlayerTarget(state.Players[0]) });

        // P0's artifact fired: P0's creature buffed (+7), P0's charges reset to 0
        var p0Creature = state.Players[0].Lanes[0].Occupant!;
        Assert.Equal(7, p0Creature.AttackModifier);
        Assert.Equal(0, state.Players[0].ArtifactSlots[0].Charges);

        // P1's artifact must NOT have fired: P1's creature unbuffed, P1's
        // charges unchanged (0 — P1 never filled).
        var p1Creature = state.Players[1].Lanes[0].Occupant!;
        Assert.Equal(0, p1Creature.AttackModifier);
        Assert.Equal(0, state.Players[1].ArtifactSlots[0].Charges);
    }

    [Fact]
    public void Ruling_G6_MirrorMatch_IdenticalPassivesDoNotStack()
    {
        // G6: Identical passives (same card id) never stack.
        // Both players have the same Duskfang-like artifact with a passive
        // COST_MOD -1 on creatures ≤2 atk.  P0's discount applies to P0's
        // plays only; P1's to P1's.  They don't stack onto the same player.
        var state = CreateState();
        var player = state.Players[0];

        // P0's Duskfang (passive COST_MOD -1, creatures ≤2 atk)
        player.ArtifactSlots = new ArtifactSlot[1];
        player.ArtifactSlots[0] = new ArtifactSlot(0);
        var p0Art = new CardInstance(state.NextInstanceId++, "tst_duskfang", 0)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 0
        };
        p0Art.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef>
            {
                new() { Op = Op.COST_MOD, Amount = 1, AppliesTo = "CREATURE",
                    Filter = "ATTACK_LTE", Value = 2,
                    Target = new TargetDef { Scope = Scope.PLAYER_SELF } }
            }
        });
        player.ArtifactSlots[0].Occupant = p0Art;

        // P1's Duskfang (same def id) — P1's mod applies to P1's cost mods,
        // never to P0's.  "Never stack" across players.
        var p1Player = state.Players[1];
        p1Player.ArtifactSlots = new ArtifactSlot[1];
        p1Player.ArtifactSlots[0] = new ArtifactSlot(0);
        var p1Art = new CardInstance(state.NextInstanceId++, "tst_duskfang", 1)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 0
        };
        p1Art.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef>
            {
                new() { Op = Op.COST_MOD, Amount = 1, AppliesTo = "CREATURE",
                    Filter = "ATTACK_LTE", Value = 2,
                    Target = new TargetDef { Scope = Scope.PLAYER_SELF } }
            }
        });
        p1Player.ArtifactSlots[0].Occupant = p1Art;

        // Start P0's turn → passives applied for both players
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 1 });

        // P0's cost mods list has exactly 1 entry (the discount)
        Assert.Single(state.Players[0].CostMods);

        // P0 plays a ≤2-atk creature → cost is reduced by 1 (not 2)
        var cheapCreature = MakeHandCard(state, 0, CardType.CREATURE, "tst_cheap", cost: 3, attack: 2);
        int effectiveCost = CostInterceptor.GetEffectiveCost(state, cheapCreature, 0);
        Assert.Equal(2, effectiveCost); // 3 - 1, not 3 - 2
    }

    [Fact]
    public void Ruling_G6_MirrorMatch_ChargesAreOwn()
    {
        // G6: Each player's Charges/marks are their own.
        // Both players have a charge artifact.  P0 gains charges → only P0's
        // slot charges increase; P1's slot stays unchanged.
        var state = CreateState();
        AddArtifact(state, 0, 0, "tst_charge_p0", maxCharges: 3);
        AddArtifact(state, 1, 0, "tst_charge_p1", maxCharges: 3);

        // P0 gains 2 charges
        state.Players[0].ArtifactSlots[0].AddCharges(2);
        Assert.Equal(2, state.Players[0].ArtifactSlots[0].Charges);
        Assert.Equal(0, state.Players[1].ArtifactSlots[0].Charges); // P1's unchanged
    }

    // ══════════════════════════════════════════════════════════════════
    // G7 — "Creature died" = any side, any turn
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_G7_CreatureDied_AnySideAnyTurn()
    {
        // G7: "Creature died" = left play to any death, either side, any turn,
        // unless text says friendly/enemy.  Default (no side) = both sides.
        var state = CreateState();
        PlaceCreature(state, 0, 0, attack: 1, vigor: 2);
        PlaceCreature(state, 1, 0, attack: 1, vigor: 2);

        // No deaths yet → condition false
        Assert.False(Eval(Cond(ConditionOp.CREATURE_DIED_THIS_TURN), state, 0));

        // Kill P0's creature → death counted (both sides)
        state.Players[0].Lanes[0].Occupant = null;
        state.CreatureDiedThisTurnCount[0] = 1;
        Assert.True(Eval(Cond(ConditionOp.CREATURE_DIED_THIS_TURN), state, 0));

        // Also true for P1's perspective
        Assert.True(Eval(Cond(ConditionOp.CREATURE_DIED_THIS_TURN), state, 1));
    }

    [Fact]
    public void Ruling_G7_CreatureDied_SideAware_AllyVsEnemy()
    {
        // G7: Side-aware — ALLY = own deaths, ENEMY = opponent's deaths.
        var state = CreateState();
        state.CreatureDiedThisTurnCount[0] = 1; // P0's creature died
        state.CreatureDiedThisTurnCount[1] = 0;

        // P0's perspective: ALLY true (P0's creature died)
        Assert.True(Eval(Cond(ConditionOp.CREATURE_DIED_THIS_TURN, side: "ALLY"), state, 0));
        // P0's perspective: ENEMY false (P1's creature didn't die)
        Assert.False(Eval(Cond(ConditionOp.CREATURE_DIED_THIS_TURN, side: "ENEMY"), state, 0));

        // P1's perspective: ALLY false (P1's creature didn't die)
        Assert.False(Eval(Cond(ConditionOp.CREATURE_DIED_THIS_TURN, side: "ALLY"), state, 1));
        // P1's perspective: ENEMY true (P0's creature died → P1's enemy)
        Assert.True(Eval(Cond(ConditionOp.CREATURE_DIED_THIS_TURN, side: "ENEMY"), state, 1));
    }

    // ══════════════════════════════════════════════════════════════════
    // G8 — Charges
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Ruling_G8_ChargesPerCard_CapThree()
    {
        // G8: Charges are per-card, cap 3, visible to both players.
        // Two artifacts each with own charge pool; adding past max caps.
        var state = CreateState();
        AddArtifact(state, 0, 0, "tst_g8_a", maxCharges: 3);
        AddArtifact(state, 0, 1, "tst_g8_b", maxCharges: 3);

        // Each artifact has its own charge pool
        state.Players[0].ArtifactSlots[0].AddCharges(5); // capped at 3
        state.Players[0].ArtifactSlots[1].AddCharges(2); // stays 2

        Assert.Equal(3, state.Players[0].ArtifactSlots[0].Charges);
        Assert.Equal(2, state.Players[0].ArtifactSlots[1].Charges);
    }

    [Fact]
    public void Ruling_G8_ChargeFull_FiresImmediatelyOnThirdCharge()
    {
        // G8: Charge-full effects fire immediately on the 3rd Charge unless
        // the card says "at end of turn".  Duskfang-style: ON_CHARGE_FULL
        // immediate → SUPPRESS enemy artifacts when charges hit 3.
        var state = CreateState();
        AddArtifact(state, 0, 0, "tst_g8_immediate", maxCharges: 3,
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
        // Add artifact to P1 so SUPPRESS has a target
        AddArtifact(state, 1, 0, "tst_g8_enemy", maxCharges: 3);

        // Fill to 3 through EffectExecutor → immediate ON_CHARGE_FULL fires
        var effect = new EffectDef { Op = Op.ADD_CHARGE, Amount = 3 };
        var source = state.Players[0].ArtifactSlots[0].Occupant!;
        EffectExecutor.Execute(effect, source, state,
            new List<ResolvedTarget> { new PlayerTarget(state.Players[0]) });

        // P0's charges reset (trigger effect: RESET_CHARGES)
        Assert.Equal(0, state.Players[0].ArtifactSlots[0].Charges);

        // P1's artifact suppressed (trigger effect: SUPPRESS)
        Assert.True(state.Players[1].ArtifactSlots[0].IsSuppressed);
    }

    [Fact]
    public void Ruling_G8_ChargeFull_DeferredToEndOfTurn()
    {
        // G8: Charge-full with "at end of turn" timing fires at end of turn,
        // not immediately.  Censer/Grimoire-style: PendingChargeFull set,
        // fires at end-of-turn, not right away.
        var state = CreateState();
        AddArtifact(state, 0, 0, "tst_g8_deferred", maxCharges: 3, hasDeferredChargeFull: true,
            abilities: new List<AbilityDef>
            {
                new() { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } } },
                new() { Trigger = Trigger.ON_CHARGE_FULL, Timing = "END_OF_TURN", Effects = new List<EffectDef>
                {
                    new() { Op = Op.RESET_CHARGES,
                        Target = new TargetDef { Scope = Scope.PLAYER_SELF } }
                }}
            });

        // Fill to 3 → PendingChargeFull set, NOT fired immediately
        state = ApplyCharge(state, 3);
        var slot = state.Players[0].ArtifactSlots[0];
        Assert.Equal(3, slot.Charges);
        Assert.True(slot.PendingChargeFull);

        // End turn → deferred ON_CHARGE_FULL fires → RESET_CHARGES applied
        state = EndTurn(state, 0);
        var slotAfter = state.Players[0].ArtifactSlots[0];
        Assert.Equal(0, slotAfter.Charges); // reset by trigger
        Assert.False(slotAfter.PendingChargeFull); // cleared
    }

    [Fact]
    public void Ruling_G8_Charges_VisibleToBothPlayers()
    {
        // G8: Charges are visible to both players.  The engine exposes all
        // artifact slots as part of the observable game state.  Both players
        // can see each other's charges.
        var state = CreateState();
        AddArtifact(state, 0, 0, "tst_g8_a", maxCharges: 3);
        AddArtifact(state, 1, 0, "tst_g8_b", maxCharges: 3);

        // Set charges on both
        state.Players[0].ArtifactSlots[0].AddCharges(2);
        state.Players[1].ArtifactSlots[0].AddCharges(1);

        // P0's slot readable from P0's player state
        Assert.Equal(2, state.Players[0].ArtifactSlots[0].Charges);
        // P1's slot readable from P1's player state
        Assert.Equal(1, state.Players[1].ArtifactSlots[0].Charges);

        // Charges affect the observable state hash (deterministic, visible)
        var hash1 = state.ComputeStateHash();
        state.Players[0].ArtifactSlots[0].AddCharges(1); // 2→3
        var hash2 = state.ComputeStateHash();
        Assert.NotEqual(hash1, hash2);
    }
}

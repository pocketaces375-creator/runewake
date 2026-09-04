using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// TASK-CLASS-IDENTITY-1C — Rogue's twin daggers get their own feel,
/// and the whole item set is soaked and explained.
/// Tests: Dusk (first Swift attacker +1 STEALTH_STRIKE), Whisper (face dmg charge,
/// full-charge 3 face dmg + Venom), and the TWIN rule.
/// </summary>
[Collection("NonParallel")]
public class RogueIdentityTests
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
    /// Set up P0's two artifact slots with real Duskfang and Whisperfang definitions
    /// (matching the launch_artifacts.json DSL).
    /// </summary>
    private static GameState CreateStateWithDaggers()
    {
        var state = CreateState();
        var player = state.Players[0];

        player.ArtifactClass = "rogue";
        player.ArtifactDefIds = new[] { "artf_rogue_dagger_dusk", "artf_rogue_dagger_whisper" };
        player.ArtifactSlots = new ArtifactSlot[2];
        player.ArtifactSlots[0] = new ArtifactSlot(0);
        player.ArtifactSlots[1] = new ArtifactSlot(1);

        // Duskfang (slot 0)
        var duskSlot = player.ArtifactSlots[0];
        var dusk = new CardInstance(state.NextInstanceId++, "artf_rogue_dagger_dusk", 0)
        {
            CardType = CardType.ARTIFACT,
            Zone = Zone.ArtifactSlot,
            ArtifactSlotIndex = 0
        };
        // Passive: GRANT_KEY STEALTH_STRIKE to FIRST_ATTACKER
        dusk.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { new() { Op = Op.GRANT_KEY,
                Keyword = "STEALTH_STRIKE",
                Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "FIRST_ATTACKER", Count = TargetCount.Exactly(1) } } }
        });
        // Full-charge: BUFF +1 to FIRST_ATTACKER + GRANT_KEY STEALTH_STRIKE + RESET + ADD_CHARGE (TWIN)
        dusk.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.ON_CHARGE_FULL,
            Effects = new List<EffectDef>
            {
                new() { Op = Op.BUFF, Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "FIRST_ATTACKER", Count = TargetCount.Exactly(1) }, Attack = 1, Vigor = 0, Duration = Duration.THIS_TURN },
                new() { Op = Op.GRANT_KEY, Keyword = "STEALTH_STRIKE", Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "FIRST_ATTACKER", Count = TargetCount.Exactly(1) } },
                new() { Op = Op.RESET_CHARGES, Target = new TargetDef { Scope = Scope.SELF_ARTIFACT } },
                new() { Op = Op.ADD_CHARGE, Target = new TargetDef { Scope = Scope.PLAYER_SELF }, Amount = 1 }
            }
        });
        duskSlot.MaxCharges = 3;
        duskSlot.Charges = 0;
        duskSlot.AutoChargeGainOn = "on_turn_start";
        duskSlot.Occupant = dusk;
        player.ArtifactSlots[0] = duskSlot;

        // Whisperfang (slot 1)
        var whisperSlot = player.ArtifactSlots[1];
        var whisper = new CardInstance(state.NextInstanceId++, "artf_rogue_dagger_whisper", 0)
        {
            CardType = CardType.ARTIFACT,
            Zone = Zone.ArtifactSlot,
            ArtifactSlotIndex = 1
        };
        // Passive: HEAL NONE (placeholder)
        whisper.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } }
        });
        // Full-charge: DAMAGE 3 to enemy face + GRANT_KEY VENOM to highest attack enemy
        // + RESET_CHARGES + ADD_CHARGE (TWIN)
        whisper.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.ON_CHARGE_FULL,
            Effects = new List<EffectDef>
            {
                new() { Op = Op.DAMAGE, Target = new TargetDef { Scope = Scope.PLAYER_ENEMY }, Amount = 3 },
                new() { Op = Op.GRANT_KEY, Keyword = "VENOM",
                    Target = new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "HIGHEST_ATTACK", Count = TargetCount.Exactly(1) } },
                new() { Op = Op.RESET_CHARGES, Target = new TargetDef { Scope = Scope.SELF_ARTIFACT } },
                new() { Op = Op.ADD_CHARGE, Target = new TargetDef { Scope = Scope.PLAYER_SELF }, Amount = 1 }
            }
        });
        whisperSlot.MaxCharges = 3;
        whisperSlot.Charges = 0;
        whisperSlot.Occupant = whisper;
        player.ArtifactSlots[1] = whisperSlot;

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

    private static GameState Attack(GameState state, int playerIndex, int lane)
        => DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = playerIndex,
            SourceLane = lane,
            TargetLane = lane
        });

    // ================================================================
    // DUSK
    // ================================================================

    [Fact]
    public void Dusk_PassiveGrantsStealthStrike_ToFirstAttacker()
    {
        // Dusk's passive: GRANT_KEY STEALTH_STRIKE to FIRST_ATTACKER.
        // The first creature to attack each turn gains STEALTH_STRIKE.
        var state = CreateState();
        var firstAttacker = PlaceCreature(state, 0, 0, attack: 3, vigor: 3);
        PlaceCreature(state, 1, 0, attack: 3, vigor: 5);

        // Set up Dusk in slot 0
        state.Players[0].ArtifactSlots = new ArtifactSlot[2];
        state.Players[0].ArtifactSlots[0] = new ArtifactSlot(0);
        state.Players[0].ArtifactSlots[1] = new ArtifactSlot(1);

        var dusk = new CardInstance(state.NextInstanceId++, "artf_rogue_dagger_dusk", 0)
        {
            CardType = CardType.ARTIFACT,
            Zone = Zone.ArtifactSlot,
            ArtifactSlotIndex = 0
        };
        dusk.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef>
            {
                new() { Op = Op.GRANT_KEY, Keyword = "STEALTH_STRIKE",
                    Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "FIRST_ATTACKER", Count = TargetCount.Exactly(1) } }
            }
        });
        state.Players[0].ArtifactSlots[0].Occupant = dusk;

        // Slot 1 minimal
        var min1 = new CardInstance(state.NextInstanceId++, "tst_min", 0)
        {
            CardType = CardType.ARTIFACT,
            Zone = Zone.ArtifactSlot,
            ArtifactSlotIndex = 1
        };
        min1.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } }
        });
        state.Players[0].ArtifactSlots[1].Occupant = min1;

        // Simulate declaration: mark this creature as first attacker
        state.Players[0].FirstAttackerLaneIndex = 0;

        // Resolve the Dusk passive effect (GRANT_KEY STEALTH_STRIKE to FIRST_ATTACKER)
        var grantEffect = new EffectDef
        {
            Op = Op.GRANT_KEY,
            Keyword = "STEALTH_STRIKE",
            Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "FIRST_ATTACKER", Count = TargetCount.Exactly(1) }
        };
        var targets = TargetResolver.Resolve(grantEffect.Target!, dusk,
            state.Players[0], state.Players[1], state);
        EffectExecutor.Execute(grantEffect, dusk, state, targets);

        // First attacker gained STEALTH_STRIKE
        var firstAttAfter = state.Players[0].Lanes[0].Occupant!;
        Assert.Contains("STEALTH_STRIKE", firstAttAfter.GrantedKeywords);

        // Attack resolves with STEALTH_STRIKE — no counter-damage
        state = Attack(state, 0, 0);
        var attAfter = state.Players[0].Lanes[0].Occupant!;
        // Should have survived (no counter-damage from STEALTH_STRIKE)
        Assert.True(attAfter.CurrentVigor > 0);
    }

    [Fact]
    public void Dusk_GainsChargeEachTurnStart()
    {
        // Dusk gains +1 charge at the start of each of your turns.
        // Game starts with P0's turn already in progress (CurrentPlayerIndex=0).
        // End P0's turn first -> P1's turn starts (no on_turn_start for P0 yet
        // since P0's turn was already active when artifact was equipped).
        // Then end P1's turn -> P0's turn starts -> Dusk's on_turn_start fires.
        var state = CreateStateWithDaggers();
        var player = state.Players[0];

        Assert.Equal(0, player.ArtifactSlots[0].Charges);

        // End P0's turn -> P1's turn starts
        state = EndTurn(state, 0);
        // Dusk didn't gain because it was P1's turn start (different player)
        Assert.Equal(0, player.ArtifactSlots[0].Charges);

        // End P1's turn -> P0's turn starts -> Dusk gains 1 from on_turn_start
        state = EndTurn(state, 1);
        // After EndTurn, state is cloned, so re-get the slot from the new state
        var duskAfter = state.Players[0].ArtifactSlots[0];
        Assert.Equal(1, duskAfter.Charges);

        // Another cycle: End P0 -> End P1 -> P0 starts -> another gain
        state = EndTurn(state, 0);
        state = EndTurn(state, 1);
        duskAfter = state.Players[0].ArtifactSlots[0];
        Assert.Equal(2, duskAfter.Charges);
    }

    [Fact]
    public void Dusk_FullCharge_GrantsBuffAndStealthStrikeAndTriggersTwin()
    {
        // When Dusk reaches 3 charges: BUFF +1 + GRANT_KEY STEALTH_STRIKE to
        // FIRST_ATTACKER, RESET, then ADD_CHARGE (TWIN) gives both daggers +1
        var state = CreateStateWithDaggers();
        PlaceCreature(state, 0, 0, attack: 3, vigor: 3);
        PlaceCreature(state, 1, 0, attack: 1, vigor: 5);
        var duskSlot = state.Players[0].ArtifactSlots[0];
        var whisperSlot = state.Players[0].ArtifactSlots[1];

        // Set first attacker lane
        state.Players[0].FirstAttackerLaneIndex = 0;

        // Set Dusk to 3 charges, then fire ON_CHARGE_FULL for slot 0
        duskSlot.Charges = 3;
        TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 0);

        // Dusk: full-charge → RESET (→0) → ADD_CHARGE (TWIN →1)
        Assert.Equal(1, duskSlot.Charges);
        // Whisper also gets +1 from TWIN
        Assert.Equal(1, whisperSlot.Charges);
    }

    [Fact]
    public void Dusk_FullCharge_BuffsFirstAttacker()
    {
        // Dusk's full-charge also grants +1 attack to the first attacker.
        var state = CreateStateWithDaggers();
        var attacker = PlaceCreature(state, 0, 0, attack: 2, vigor: 4);
        PlaceCreature(state, 1, 0, attack: 1, vigor: 5);
        state.Players[0].FirstAttackerLaneIndex = 0;

        var duskSlot = state.Players[0].ArtifactSlots[0];
        // Set Dusk to 3, fire full-charge → BUFF +1 applies
        duskSlot.Charges = 3;
        TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 0);

        // The full-charge fired: BUFF +1 to FIRST_ATTACKER
        // First attacker should have +1 attack (2 + 1 = 3)
        var attAfter = state.Players[0].Lanes[0].Occupant!;
        Assert.Equal(3, attAfter.CurrentAttack);
    }

    // ================================================================
    // WHISPER
    // ================================================================

    [Fact]
    public void Whisper_FullCharge_DealsFaceDamage()
    {
        // Whisper's full-charge: DAMAGE 3 to PLAYER_ENEMY
        var state = CreateStateWithDaggers();
        state.Players[1].Vigor = 15; // enemy vigor

        var whisperSlot = state.Players[0].ArtifactSlots[1];

        // Fire Whisper's ON_CHARGE_FULL ability directly (simulate reaching 3 charges)
        TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 1);

        // Enemy should have taken 3 face damage
        Assert.Equal(12, state.Players[1].Vigor);
    }

    [Fact]
    public void Whisper_FullCharge_GrantsVenomToEnemyCreature()
    {
        // Whisper's full-charge grants VENOM to the HIGHEST_ATTACK enemy creature
        var state = CreateStateWithDaggers();
        var weakEnemy = PlaceCreature(state, 1, 0, attack: 1, vigor: 3);
        var strongEnemy = PlaceCreature(state, 1, 1, attack: 5, vigor: 5);

        var whisperSlot = state.Players[0].ArtifactSlots[1];
        var grantEffect = new EffectDef
        {
            Op = Op.GRANT_KEY,
            Keyword = "VENOM",
            Target = new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "HIGHEST_ATTACK", Count = TargetCount.Exactly(1) }
        };
        var targets = TargetResolver.Resolve(grantEffect.Target!, whisperSlot.Occupant!,
            state.Players[0], state.Players[1], state);
        EffectExecutor.Execute(grantEffect, whisperSlot.Occupant!, state, targets);

        // The HIGHEST_ATTACK enemy (strongEnemy with atk=5) should get VENOM
        var venomTarget = targets.OfType<CreatureTarget>().FirstOrDefault();
        Assert.NotNull(venomTarget);
        Assert.Equal(strongEnemy.InstanceId, venomTarget.Card.InstanceId);
        Assert.Contains("VENOM", venomTarget.Card.GrantedKeywords);
    }

    [Fact]
    public void Whisper_FullCharge_ResetsAndTriggersTwin()
    {
        // Whisper's full-charge resets its charges and the TWIN ADD_CHARGE
        // gives +1 to both daggers.
        var state = CreateStateWithDaggers();
        var whisperSlot = state.Players[0].ArtifactSlots[1];
        var duskSlot = state.Players[0].ArtifactSlots[0];

        // Set Whisper to 3 charges directly, then fire ON_CHARGE_FULL
        whisperSlot.Charges = 3;
        TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 1);

        // After full-charge: RESET_CHARGES (→ 0) then ADD_CHARGE TWIN (→ 1)
        Assert.Equal(1, whisperSlot.Charges);
        // Dusk also gets +1 from TWIN ADD_CHARGE
        Assert.Equal(1, duskSlot.Charges);
    }

    // ================================================================
    // TWIN RULE
    // ================================================================

    [Fact]
    public void TwinRule_WhenDuskFiresFullCharge_WhisperGainsCharge()
    {
        // TWIN: when Dusk's full-charge fires, Whisper gets +1 charge
        var state = CreateStateWithDaggers();
        var duskSlot = state.Players[0].ArtifactSlots[0];
        var whisperSlot = state.Players[0].ArtifactSlots[1];

        // Set Dusk to 3 charges, then fire ON_CHARGE_FULL for slot 0
        duskSlot.Charges = 3;
        TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 0);

        // Dusk fires full-charge: RESET → ADD_CHARGE TWIN
        // Dusk = 1, Whisper = 1
        Assert.Equal(1, duskSlot.Charges);
        Assert.Equal(1, whisperSlot.Charges);
    }

    [Fact]
    public void TwinRule_WhenWhisperFiresFullCharge_DuskGainsCharge()
    {
        // TWIN: when Whisper's full-charge fires, Dusk gets +1 charge
        var state = CreateStateWithDaggers();
        state.Players[1].Vigor = 15; // give enemy room for face damage
        var duskSlot = state.Players[0].ArtifactSlots[0];
        var whisperSlot = state.Players[0].ArtifactSlots[1];

        // Set Whisper to 3 charges, then fire ON_CHARGE_FULL for slot 1
        whisperSlot.Charges = 3;
        TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, 0, 1);

        // Whisper fires full-charge: RESET → ADD_CHARGE TWIN
        // Whisper = 1, Dusk = 1
        Assert.Equal(1, whisperSlot.Charges);
        Assert.Equal(1, duskSlot.Charges);
    }
}
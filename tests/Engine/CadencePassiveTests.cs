using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// TASK-DSL-4 — Cadenced passives: cadence ON_TURN_START with explicit ordering key.
/// Prey marking (Bow SET_PREY, order BEFORE_ALL_OTHER_TURN_START_EFFECTS) resolves
/// before all other turn-start effects (R15); Censer heal after; then draw (R11).
/// </summary>
[Collection("NonParallel")]
public class CadencePassiveTests
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
    /// Place a creature on the given player's lane.
    /// </summary>
    private static CardInstance PlaceCreature(GameState state, int playerIndex, int laneIndex, int attack, int vigor)
    {
        var c = new CardInstance(state.NextInstanceId++, "tst_cr", playerIndex)
        {
            Zone = Zone.Lane,
            LaneIndex = laneIndex,
            CardType = CardType.CREATURE,
            BaseAttack = attack,
            BaseVigor = vigor,
            IsExhausted = false
        };
        state.Players[playerIndex].Lanes[laneIndex].Occupant = c;
        return c;
    }

    /// <summary>
    /// Wire a test artifact into the given slot with a single PASSIVE ability
    /// carrying the given effect (cadence/order are on the effect).
    /// </summary>
    private static void AddArtifact(GameState state, int playerIndex, int slotIndex, string defId, EffectDef effect)
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
        artifact.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { effect }
        });
        slot.Occupant = artifact;
    }

    private static GameState EndTurn(GameState state, int playerIndex)
        => DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = playerIndex });

    /// <summary>
    /// Find a creature by instance id in the (possibly re-cloned) state.
    /// DuelEngine.Apply clones the state, so post-action reads must re-fetch.
    /// </summary>
    private static CardInstance? FindCreature(GameState state, int instanceId)
    {
        for (int p = 0; p < 2; p++)
            for (int l = 0; l < 5; l++)
                if (state.Players[p].Lanes[l].Occupant is { } occ && occ.InstanceId == instanceId)
                    return occ;
        return null;
    }

    private static EffectDef PreyMarkEffect()
        => new()
        {
            Op = Op.SET_PREY,
            Cadence = EffectDef.CadenceOnTurnStart,
            Order = EffectDef.OrderBeforeAllOtherTurnStartEffects,
            Target = new TargetDef
            {
                Scope = Scope.ENEMY_CREATURE,
                Filter = "HIGHEST_ATTACK",
                Count = TargetCount.Exactly(1)
            }
        };

    private static EffectDef CenserHealEffect()
        => new()
        {
            Op = Op.HEAL,
            Amount = 1,
            Cadence = EffectDef.CadenceOnTurnStart,
            Target = new TargetDef
            {
                Scope = Scope.ALLY_CREATURE,
                Filter = "MOST_WOUNDED",
                Count = TargetCount.Exactly(1)
            }
        };

    // ——— EffectDef deserialization ———

    [Fact]
    public void EffectDef_DeserializesCadenceAndOrder()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        var bow = JsonSerializer.Deserialize<EffectDef>(
            "{\"op\":\"SET_PREY\",\"cadence\":\"ON_TURN_START\",\"order\":\"BEFORE_ALL_OTHER_TURN_START_EFFECTS\",\"target\":{\"scope\":\"ENEMY_CREATURE\",\"filter\":\"HIGHEST_ATTACK\",\"count\":1}}",
            opts)!;
        Assert.Equal(Op.SET_PREY, bow.Op);
        Assert.Equal(EffectDef.CadenceOnTurnStart, bow.Cadence);
        Assert.Equal(EffectDef.OrderBeforeAllOtherTurnStartEffects, bow.Order);

        var censer = JsonSerializer.Deserialize<EffectDef>(
            "{\"op\":\"HEAL\",\"cadence\":\"ON_TURN_START\",\"target\":{\"scope\":\"ALLY_CREATURE\",\"filter\":\"MOST_WOUNDED\",\"count\":1},\"amount\":1}",
            opts)!;
        Assert.Equal(Op.HEAL, censer.Op);
        Assert.Equal(EffectDef.CadenceOnTurnStart, censer.Cadence);
        Assert.Null(censer.Order);
    }

    // ——— Ordering key: BEFORE_ALL_OTHER_TURN_START_EFFECTS first ———

    [Fact]
    public void OrderingKey_BeforeAllOtherResolvesBeforeDefaultOrder()
    {
        var state = CreateState();
        var ally = PlaceCreature(state, 0, 0, 1, 3); // 3 vigor, no damage

        // Artifact A (slot 0): order BEFORE_ALL_OTHER → DAMAGE 1 on first ally.
        AddArtifact(state, 0, 0, "tst_a", new EffectDef
        {
            Op = Op.DAMAGE,
            Amount = 1,
            Cadence = EffectDef.CadenceOnTurnStart,
            Order = EffectDef.OrderBeforeAllOtherTurnStartEffects,
            Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Count = TargetCount.Exactly(1) }
        });
        // Artifact B (slot 1): default order → HEAL 1 on a DAMAGED ally.
        AddArtifact(state, 0, 1, "tst_b", new EffectDef
        {
            Op = Op.HEAL,
            Amount = 1,
            Cadence = EffectDef.CadenceOnTurnStart,
            Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "DAMAGED", Count = TargetCount.Exactly(1) }
        });

        // P1 ends turn → P0's turn starts → cadence phase fires.
        state = EndTurn(state, 1);
        var allyAfter = FindCreature(state, ally.InstanceId);

        // A ran first (damage 1 → Damage=1), then B healed the damaged ally
        // (Damage=0). If B ran first there was no damaged ally to heal, and A
        // would leave the creature at Damage=1.
        Assert.NotNull(allyAfter);
        Assert.Equal(0, allyAfter.Damage);
    }

    [Fact]
    public void OrderingKey_ReversedOrderWouldLeaveDamage()
    {
        var state = CreateState();
        var ally = PlaceCreature(state, 0, 0, 1, 3);

        // Same two effects, but the DEFAULT-order DAMAGE is now in slot 0 and
        // the BEFORE_ALL_OTHER HEAL in slot 1 — the heal resolves first, so
        // there is no damaged creature to heal and the damage sticks.
        AddArtifact(state, 0, 0, "tst_b", new EffectDef
        {
            Op = Op.DAMAGE,
            Amount = 1,
            Cadence = EffectDef.CadenceOnTurnStart,
            Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Count = TargetCount.Exactly(1) }
        });
        AddArtifact(state, 0, 1, "tst_a", new EffectDef
        {
            Op = Op.HEAL,
            Amount = 1,
            Cadence = EffectDef.CadenceOnTurnStart,
            Order = EffectDef.OrderBeforeAllOtherTurnStartEffects,
            Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "DAMAGED", Count = TargetCount.Exactly(1) }
        });

        state = EndTurn(state, 1);
        var allyAfter = FindCreature(state, ally.InstanceId);

        // Heal first (no-op: nothing damaged), then damage sticks.
        Assert.NotNull(allyAfter);
        Assert.Equal(1, allyAfter.Damage);
    }

    // ——— Real artifact pair: Bow prey marking BEFORE Censer heal ———

    [Fact]
    public void PreyMarking_RunsBeforeCenserHeal_BothAtTurnStart()
    {
        var state = CreateState();

        // P0: Bow (SET_PREY, BEFORE_ALL_OTHER) + Censer (HEAL, default).
        AddArtifact(state, 0, 0, "artf_astrologist_orb", PreyMarkEffect());
        AddArtifact(state, 0, 1, "artf_cleric_censer", CenserHealEffect());

        // P1 enemy creature with the highest attack — prey target.
        var enemy = PlaceCreature(state, 1, 0, 5, 5);

        // P0 ally creature with 1 damage (wounded) — Censer heal target.
        var ally = PlaceCreature(state, 0, 1, 2, 3);
        ally.Damage = 1;

        // P1 ends turn → P0's turn starts.
        state = EndTurn(state, 1);

        // Prey marking ran at turn start (before draw, before any play).
        Assert.Equal(enemy.InstanceId, state.Players[0].PreyTargetId);

        // Censer heal ran after prey marking, still before draw — ally healed.
        var allyAfter = FindCreature(state, ally.InstanceId);
        Assert.NotNull(allyAfter);
        Assert.Equal(0, allyAfter.Damage);
    }

    [Fact]
    public void PreyMarking_NoEnemies_NoMark()
    {
        var state = CreateState();
        AddArtifact(state, 0, 0, "artf_astrologist_orb", PreyMarkEffect());

        // No enemy creatures → no valid prey target (R15).
        state = EndTurn(state, 1);

        Assert.Null(state.Players[0].PreyTargetId);
    }

    [Fact]
    public void CenserHeal_NoWoundedCreature_NoHeal()
    {
        var state = CreateState();
        AddArtifact(state, 0, 1, "artf_cleric_censer", CenserHealEffect());

        // Healthy ally creature — nothing to heal (R11: no wounded = no heal).
        var ally = PlaceCreature(state, 0, 1, 2, 3);

        state = EndTurn(state, 1);
        var allyAfter = FindCreature(state, ally.InstanceId);

        Assert.NotNull(allyAfter);
        Assert.Equal(0, allyAfter.Damage);
    }

    // ——— Cadence fires BEFORE the turn-start trigger phase (which follows draw) ———

    [Fact]
    public void Cadence_ResolvesBeforeTurnStartTriggerPhase()
    {
        var state = CreateState();
        state.Players[0].Hand.Clear();
        state.Players[0].Deck.Clear();
        for (int i = 0; i < 2; i++)
            state.Players[0].Deck.Add(new CardInstance(state.NextInstanceId++, "tst_d", 0) { Zone = Zone.Deck });

        // Cadenced artifact with DRAW 1 (default order).
        AddArtifact(state, 0, 0, "tst_draw", new EffectDef
        {
            Op = Op.DRAW,
            Amount = 1,
            Cadence = EffectDef.CadenceOnTurnStart,
            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
        });

        // Creature with ON_TURN_START that only fires when the hand already has
        // 2 cards — i.e. AFTER the cadence draw AND the normal draw both landed.
        var watcher = PlaceCreature(state, 0, 0, 1, 1);
        watcher.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.ON_TURN_START,
            Condition = new ConditionDef { Op = ConditionOp.HAND_COUNT_GTE, Value = JsonDocument.Parse("2").RootElement },
            Effects = new List<EffectDef>
            {
                new() { Op = Op.BUFF, Attack = 1, Vigor = 0, Duration = Duration.THIS_TURN, Target = new TargetDef { Scope = Scope.SELF } }
            }
        });

        // P1 ends turn → P0's turn starts.
        state = EndTurn(state, 1);

        // Cadence drew 1 + normal draw = 2 cards by the time the ON_TURN_START
        // trigger phase runs (which is after the draw phase). The watcher's
        // condition (hand ≥ 2) proves the cadence resolved before the trigger
        // phase — i.e. before/at the draw, never after the trigger phase.
        Assert.Equal(2, state.Players[0].Hand.Count);
        var watcherAfter = FindCreature(state, watcher.InstanceId);
        Assert.NotNull(watcherAfter);
        Assert.Equal(1, watcherAfter.AttackModifier);
    }

    // ——— Suppression (R18 / G3) ———

    [Fact]
    public void Cadence_SuppressedArtifactDoesNotFire()
    {
        var state = CreateState();

        // Bow in slot 0; enemy creature available as prey target.
        AddArtifact(state, 0, 0, "artf_astrologist_orb", PreyMarkEffect());
        PlaceCreature(state, 1, 0, 5, 5);

        // Suppress the Bow — cadence passive must NOT fire (R18).
        state.Players[0].ArtifactSlots[0].IsSuppressed = true;
        state.Players[0].ArtifactSlots[0].SuppressionRemaining = 1;

        state = EndTurn(state, 1);

        Assert.Null(state.Players[0].PreyTargetId);
    }

    // ——— launch_artifacts.json wiring ———

    [Fact]
    public void LaunchArtifacts_BowAndCenserCarryCadence()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "content", "artifacts", "launch_artifacts.json");
        var json = File.ReadAllText(path);

        using var doc = JsonDocument.Parse(json);
        var byId = doc.RootElement.EnumerateArray()
            .ToDictionary(a => a.GetProperty("id").GetString()!);

        var bow = byId["artf_astrologist_orb"].GetProperty("passive");
        Assert.Equal("SET_PREY", bow.GetProperty("op").GetString());
        Assert.Equal("ON_TURN_START", bow.GetProperty("cadence").GetString());
        Assert.Equal("BEFORE_ALL_OTHER_TURN_START_EFFECTS", bow.GetProperty("order").GetString());

        var eleBond = byId["artf_druid_elemental_bond"].GetProperty("passive");
        Assert.Equal("HEAL", eleBond.GetProperty("op").GetString());
        Assert.Equal("ON_TURN_START", eleBond.GetProperty("cadence").GetString());
    }
}

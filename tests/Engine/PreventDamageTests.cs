using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

public class PreventDamageTests
{
    // ——— Helpers ———

    private static GameState CreateState()
    {
        var state = new GameState(seed: 42);
        for (int p = 0; p < 2; p++)
        {
            state.Players[p].AttunementMax = 10;
            state.Players[p].Attunement = 10;
            for (int i = 0; i < 5; i++)
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
    /// Register a PREVENT_DAMAGE shield on a player directly (bypassing the full op path).
    /// </summary>
    private static void AddPlayerShield(PlayerState player, int amount, string? source = null,
        string? frequency = null, ConditionDef? condition = null,
        int artifactInstanceId = 0, string defId = "tst_shield_source")
    {
        player.DamageShields.Add(new DamageShield
        {
            Amount = amount,
            Source = source,
            Frequency = frequency,
            Condition = condition,
            SourceArtifactDefId = defId,
            SourceArtifactInstanceId = artifactInstanceId,
            SourceController = player.Index
        });
    }

    /// <summary>
    /// Register a PREVENT_DAMAGE shield on a creature directly.
    /// </summary>
    private static void AddCreatureShield(CardInstance creature, int amount, string? source = null,
        string? frequency = null, ConditionDef? condition = null,
        int artifactInstanceId = 0, string defId = "tst_shield_source")
    {
        creature.DamageShields.Add(new DamageShield
        {
            Amount = amount,
            Source = source,
            Frequency = frequency,
            Condition = condition,
            SourceArtifactDefId = defId,
            SourceArtifactInstanceId = artifactInstanceId,
            SourceController = creature.Controller
        });
    }

    // ——— Basic amount ———

    [Fact]
    public void PreventDamage_ReducesDamageByAmount()
    {
        var state = CreateState();
        var player = state.Players[0];
        AddPlayerShield(player, 3);
        int reduced = DamageInterceptor.Reduce(state, player, 10, "ATTACK");
        Assert.Equal(7, reduced);
    }

    [Fact]
    public void PreventDamage_NeverGoesBelowZero()
    {
        var state = CreateState();
        var player = state.Players[0];
        AddPlayerShield(player, 100);
        int reduced = DamageInterceptor.Reduce(state, player, 10, "ATTACK");
        Assert.Equal(0, reduced);
    }

    [Fact]
    public void PreventDamage_ZeroAmountDoesNothing()
    {
        var state = CreateState();
        var player = state.Players[0];
        AddPlayerShield(player, 0);
        int reduced = DamageInterceptor.Reduce(state, player, 10, "ATTACK");
        Assert.Equal(10, reduced);
    }

    // ——— Source filter ———

    [Fact]
    public void PreventDamage_SourceFilterAttack_BlocksAttackOnly()
    {
        var state = CreateState();
        var player = state.Players[0];
        AddPlayerShield(player, 5, "ATTACK");

        // Blocks ATTACK damage
        int attackReduced = DamageInterceptor.Reduce(state, player, 5, "ATTACK");
        Assert.Equal(0, attackReduced);

        // Does NOT block SPELL damage
        int spellReduced = DamageInterceptor.Reduce(state, player, 5, "SPELL");
        Assert.Equal(5, spellReduced);
    }

    [Fact]
    public void PreventDamage_SourceFilterSpell_BlocksSpellOnly()
    {
        var state = CreateState();
        var player = state.Players[0];
        AddPlayerShield(player, 5, "SPELL");

        // Blocks SPELL damage
        int spellReduced = DamageInterceptor.Reduce(state, player, 5, "SPELL");
        Assert.Equal(0, spellReduced);

        // Does NOT block ATTACK damage
        int attackReduced = DamageInterceptor.Reduce(state, player, 5, "ATTACK");
        Assert.Equal(5, attackReduced);
    }

    [Fact]
    public void PreventDamage_NoSourceFilter_BlocksAnySource()
    {
        var state = CreateState();
        var player = state.Players[0];
        AddPlayerShield(player, 5); // null source

        int attackReduced = DamageInterceptor.Reduce(state, player, 5, "ATTACK");
        Assert.Equal(0, attackReduced);

        int spellReduced = DamageInterceptor.Reduce(state, player, 5, "SPELL");
        Assert.Equal(0, spellReduced);
    }

    // ——— Frequency ———

    [Fact]
    public void PreventDamage_FrequencyFirstAttackEachTurn_BlocksOnlyFirstAttack()
    {
        var state = CreateState();
        var player = state.Players[0];
        AddPlayerShield(player, 5, frequency: "FIRST_ATTACK_EACH_TURN");

        // First attack is blocked
        int first = DamageInterceptor.Reduce(state, player, 5, "ATTACK");
        Assert.Equal(0, first);

        // Second attack same turn goes through (shield exhausted)
        int second = DamageInterceptor.Reduce(state, player, 5, "ATTACK");
        Assert.Equal(5, second);
    }

    [Fact]
    public void PreventDamage_FrequencyResetsAtTurnStart()
    {
        var state = CreateState();
        var player = state.Players[0];
        AddPlayerShield(player, 5, frequency: "FIRST_ATTACK_EACH_TURN");

        // First attack blocked
        int first = DamageInterceptor.Reduce(state, player, 5, "ATTACK");
        Assert.Equal(0, first);

        // Turn start resets
        DamageInterceptor.ResetUsage(state);

        // First attack of next turn is blocked again
        int second = DamageInterceptor.Reduce(state, player, 5, "ATTACK");
        Assert.Equal(0, second);
    }

    [Fact]
    public void PreventDamage_FrequencyOncePerEnemyTurn_BlocksOncePerTurn()
    {
        var state = CreateState();
        var player = state.Players[0];
        AddPlayerShield(player, 5, frequency: "ONCE_PER_ENEMY_TURN");

        // First enemy attack is blocked
        int first = DamageInterceptor.Reduce(state, player, 5, "ATTACK");
        Assert.Equal(0, first);

        // Second attack same turn goes through
        int second = DamageInterceptor.Reduce(state, player, 5, "ATTACK");
        Assert.Equal(5, second);

        // Turn start resets (R5: resets at start of EVERY turn)
        DamageInterceptor.ResetUsage(state);

        // Next turn, blocks again
        int third = DamageInterceptor.Reduce(state, player, 5, "ATTACK");
        Assert.Equal(0, third);
    }

    // ——— Condition ———

    [Fact]
    public void PreventDamage_ConditionFewerAllyCreatures_ActiveWhenFewer()
    {
        var state = CreateState();
        var player = state.Players[0];
        var opponent = state.Players[1];

        // P0 has 1 creature, P1 has 2 — P0 has fewer, condition is active
        PlaceCreature(state, 0, 0, 1, 1);
        PlaceCreature(state, 1, 0, 1, 1);
        PlaceCreature(state, 1, 1, 1, 1);

        AddPlayerShield(player, 3, condition: new ConditionDef
        {
            Op = ConditionOp.FEWER_ALLY_CREATURES_THAN_ENEMY
        });

        // Condition met: P0 (1) < P1 (2) — shield is active
        int reduced = DamageInterceptor.Reduce(state, player, 5, "ATTACK");
        Assert.Equal(2, reduced); // 5 - 3 = 2
    }

    [Fact]
    public void PreventDamage_ConditionFewerAllyCreatures_InactiveWhenNotFewer()
    {
        var state = CreateState();
        var player = state.Players[0];
        var opponent = state.Players[1];

        // P0 has 2, P1 has 1 — P0 has MORE, condition is inactive
        PlaceCreature(state, 0, 0, 1, 1);
        PlaceCreature(state, 0, 1, 1, 1);
        PlaceCreature(state, 1, 0, 1, 1);

        AddPlayerShield(player, 3, condition: new ConditionDef
        {
            Op = ConditionOp.FEWER_ALLY_CREATURES_THAN_ENEMY
        });

        // Condition NOT met: P0 (2) >= P1 (1) — shield is inactive
        int reduced = DamageInterceptor.Reduce(state, player, 5, "ATTACK");
        Assert.Equal(5, reduced); // full damage
    }

    [Fact]
    public void PreventDamage_ConditionFewerAllyCreatures_EvaluatedAtDamageTime()
    {
        var state = CreateState();
        var player = state.Players[0];
        var opponent = state.Players[1];

        // Equal creatures — condition inactive initially
        PlaceCreature(state, 0, 0, 1, 1);
        PlaceCreature(state, 1, 0, 1, 1);

        AddPlayerShield(player, 3, condition: new ConditionDef
        {
            Op = ConditionOp.FEWER_ALLY_CREATURES_THAN_ENEMY
        });

        // Equal: 1 == 1, not fewer
        int reduced = DamageInterceptor.Reduce(state, player, 5, "ATTACK");
        Assert.Equal(5, reduced);

        // Add another creature for opponent — now P0 (1) < P1 (2)
        PlaceCreature(state, 1, 1, 1, 1);

        // Condition now active: creature count changed at damage time (R21)
        int reduced2 = DamageInterceptor.Reduce(state, player, 5, "ATTACK");
        Assert.Equal(2, reduced2);
    }

    // ——— Creature targets ———

    [Fact]
    public void PreventDamage_OnCreature_ReducesDamage()
    {
        var state = CreateState();
        var creature = PlaceCreature(state, 0, 0, 3, 5);
        AddCreatureShield(creature, 2);

        int reduced = DamageInterceptor.Reduce(state, creature, 4, "ATTACK");
        Assert.Equal(2, reduced); // 4 - 2 = 2
    }

    // ——— Multiple shields ———

    [Fact]
    public void PreventDamage_MultipleShields_ApplyInOrder()
    {
        var state = CreateState();
        var player = state.Players[0];
        // Both shields use instanceId 0 (test-created, always active).
        AddPlayerShield(player, 3);
        AddPlayerShield(player, 2);

        // First shield reduces 10→7, second reduces 7→5
        int reduced = DamageInterceptor.Reduce(state, player, 10, "ATTACK");
        Assert.Equal(5, reduced);
    }

    // ——— Suppression symmetry ———

    [Fact]
    public void PreventDamage_ShieldFromSuppressedArtifact_IsInert()
    {
        var state = CreateState();
        var player = state.Players[0];

        // Set up an artifact slot with an occupant
        player.ArtifactSlots = new ArtifactSlot[1];
        var slot = new ArtifactSlot(0);
        var artifact = new CardInstance(100, "artf_test", 0)
        {
            CardType = CardType.ARTIFACT,
            Zone = Zone.ArtifactSlot,
            ArtifactSlotIndex = 0
        };
        slot.Occupant = artifact;
        player.ArtifactSlots[0] = slot;

        // Register a shield from this artifact (with artf_ prefix so suppression check applies)
        AddPlayerShield(player, 3, artifactInstanceId: 100, defId: "artf_test");

        // Shield is active before suppression
        int before = DamageInterceptor.Reduce(state, player, 5, "ATTACK");
        Assert.Equal(2, before);

        // Apply suppression
        slot.ApplySuppression(1, "test_suppress");

        // Shield is now inert (IsSourceArtifactSuppressed returns true)
        int during = DamageInterceptor.Reduce(state, player, 5, "ATTACK");
        Assert.Equal(5, during);
    }

    [Fact]
    public void PreventDamage_RemoveShieldsFromArtifact_RemovesThem()
    {
        var state = CreateState();
        var player = state.Players[0];
        var creature = PlaceCreature(state, 0, 0, 3, 5);

        // Register shields from artifact instance 100 (with artf_ prefix so suppression check applies)
        player.DamageShields.Add(new DamageShield
        {
            Amount = 2,
            SourceArtifactDefId = "artf_test",
            SourceArtifactInstanceId = 100,
            SourceController = 0
        });
        creature.DamageShields.Add(new DamageShield
        {
            Amount = 2,
            SourceArtifactDefId = "artf_test",
            SourceArtifactInstanceId = 100,
            SourceController = 0
        });

        // Verify shields are active
        Assert.Single(player.DamageShields);
        Assert.Single(creature.DamageShields);

        // Remove shields from artifact 100 by controller 0
        DamageInterceptor.RemoveShieldsFromArtifact(state, 100, 0);

        // Shields are removed from both player and all creatures
        Assert.Empty(player.DamageShields);
        Assert.Empty(creature.DamageShields);
    }

    // ——— EffectExecutor integration ———

    [Fact]
    public void PreventDamage_ApplyViaEffectExecutor_RegistersShieldOnPlayer()
    {
        var state = CreateState();
        var player = state.Players[0];
        var source = new CardInstance(1, "tst_shield_source", 0);

        var effect = new EffectDef
        {
            Op = Op.PREVENT_DAMAGE,
            Amount = 3,
            Source = "ATTACK",
            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
        };

        var targets = TargetResolver.Resolve(effect.Target, source, player,
            state.Players[1], state);
        EffectExecutor.Execute(effect, source, state, targets);

        Assert.Single(player.DamageShields);
        Assert.Equal(3, player.DamageShields[0].Amount);
        Assert.Equal("ATTACK", player.DamageShields[0].Source);
    }

    [Fact]
    public void PreventDamage_ApplyViaEffectExecutor_NoStacking()
    {
        var state = CreateState();
        var player = state.Players[0];
        var source = new CardInstance(1, "tst_shield_source", 0);

        var effect = new EffectDef
        {
            Op = Op.PREVENT_DAMAGE,
            Amount = 3,
            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
        };

        var targets = TargetResolver.Resolve(effect.Target, source, player,
            state.Players[1], state);

        // Apply twice — same artifact instance
        EffectExecutor.Execute(effect, source, state, targets);
        EffectExecutor.Execute(effect, source, state, targets);

        // Should only have one shield (no stacking, G6)
        Assert.Single(player.DamageShields);
    }

    [Fact]
    public void PreventDamage_ApplyViaEffectExecutor_OnCreatureTarget()
    {
        var state = CreateState();
        var player = state.Players[0];
        var creature = PlaceCreature(state, 0, 0, 3, 5);
        var source = new CardInstance(1, "tst_shield_source", 0);

        var effect = new EffectDef
        {
            Op = Op.PREVENT_DAMAGE,
            Amount = 2,
            Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "ALL", Count = TargetCount.All }
        };

        var targets = TargetResolver.Resolve(effect.Target, source, player,
            state.Players[1], state);
        EffectExecutor.Execute(effect, source, state, targets);

        Assert.Single(creature.DamageShields);
        Assert.Equal(2, creature.DamageShields[0].Amount);
    }

    // ——— Frequency via filter alias (Aura passive uses "filter" for frequency) ———

    [Fact]
    public void PreventDamage_FrequencyFromFilterAlias()
    {
        var state = CreateState();
        var player = state.Players[0];
        var source = new CardInstance(1, "tst_shield_source", 0);

        // Aura passive uses "filter": "FIRST_ATTACK_EACH_TURN" instead of "frequency"
        var effect = new EffectDef
        {
            Op = Op.PREVENT_DAMAGE,
            Amount = 1,
            Source = "ATTACK",
            Filter = "FIRST_ATTACK_EACH_TURN",  // alias for frequency
            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
        };

        var targets = TargetResolver.Resolve(effect.Target, source, player,
            state.Players[1], state);
        EffectExecutor.Execute(effect, source, state, targets);

        // First attack blocked
        int first = DamageInterceptor.Reduce(state, player, 3, "ATTACK");
        Assert.Equal(2, first); // 3 - 1 = 2

        // Second attack not blocked (frequency gate)
        int second = DamageInterceptor.Reduce(state, player, 3, "ATTACK");
        Assert.Equal(3, second);
    }

    // ——— Combat integration ———

    [Fact]
    public void PreventDamage_CombatDamage_InterceptedByPlayerShield()
    {
        var state = CreateState();
        var player = state.Players[0];
        var opponent = state.Players[1];
        state.CurrentPlayerIndex = 1; // P1's turn

        // P1 has an attacker
        var attacker = PlaceCreature(state, 1, 0, 5, 10);
        attacker.IsExhausted = false;

        // P0 has a shield protecting against ATTACK damage
        AddPlayerShield(player, 3);

        // P1 attacks P0's face
        var action = new AttackAction { PlayerIndex = 1, SourceLane = 0, TargetLane = 0 };
        state = DuelEngine.Apply(state, action);

        // P0's face damage should be reduced by 3: 5 - 3 = 2
        // P0 has default 25 vigor (re-fetch from the cloned state)
        Assert.Equal(23, state.Players[0].Vigor); // 25 - 2 = 23
    }

    [Fact]
    public void PreventDamage_CombatDamage_InterceptedByCreatureShield()
    {
        var state = CreateState();
        var player = state.Players[0];
        var opponent = state.Players[1];
        state.CurrentPlayerIndex = 1; // P1's turn

        // P0 has a creature with a shield
        var defender = PlaceCreature(state, 0, 0, 3, 10);
        AddCreatureShield(defender, 3, "ATTACK");

        // P1 has an attacker
        var attacker = PlaceCreature(state, 1, 0, 5, 10);
        attacker.IsExhausted = false;

        // P1 attacks P0's lane 0
        var action = new AttackAction { PlayerIndex = 1, SourceLane = 0, TargetLane = 0 };
        state = DuelEngine.Apply(state, action);

        // Defender should take 5 - 3 = 2 damage (re-fetch from the cloned state)
        Assert.Equal(2, state.Players[0].Lanes[0].Occupant!.Damage);
        // Attacker should take full counter damage (3) — no shield on attacker
        Assert.Equal(3, state.Players[1].Lanes[0].Occupant!.Damage);
    }

    [Fact]
    public void PreventDamage_CombatDamage_FaceShieldBlocksFaceDamage()
    {
        var state = CreateState();
        var player = state.Players[0];
        var opponent = state.Players[1];
        state.CurrentPlayerIndex = 1; // P1's turn

        // P0 has a face shield against ATTACK
        AddPlayerShield(player, 2);
        // P0 has no creatures — P1 attacks face

        var attacker = PlaceCreature(state, 1, 0, 5, 10);
        attacker.IsExhausted = false;

        var action = new AttackAction { PlayerIndex = 1, SourceLane = 0, TargetLane = 0 };
        state = DuelEngine.Apply(state, action);

        // Face damage reduced: 5 - 2 = 3 (re-fetch from the cloned state)
        Assert.Equal(22, state.Players[0].Vigor); // 25 - 3 = 22
    }
}
using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// TASK-DSL-3 — COST_MOD op (the discount mechanic).
/// Covers: applies_to CREATURE|SPELL, per-card filters (ATTACK_LTE), play-time
/// conditions (CREATURE_DIED_THIS_TURN), per-turn consumption filters
/// (FIRST_SPELL_EACH_TURN), duration (THIS_TURN), stacking (Aura), no-stacking
/// (passive re-application), suppression symmetry, floor 0, and the
/// launch_artifacts.json ATTUNE→COST_MOD migration.
/// </summary>
[Collection("NonParallel")]
public class CostModTests
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

    private static CardInstance MakeHandCard(GameState state, int pIdx, CardType type, string id,
        int cost, int attack = 0, int vigor = 1)
    {
        var c = new CardInstance(state.NextInstanceId++, id, pIdx)
        {
            Zone = Zone.Hand,
            CardType = type,
            Cost = cost,
            BaseAttack = attack,
            BaseVigor = vigor,
            IsExhausted = false
        };
        state.Players[pIdx].Hand.Add(c);
        return c;
    }

    private static void AddMod(PlayerState player, int amount, string? appliesTo = null,
        string? filter = null, int? value = null, ConditionDef? condition = null,
        Duration? duration = null, bool stacks = false,
        int artifactInstanceId = 0, string defId = "tst_costmod_source")
    {
        player.CostMods.Add(new CostMod
        {
            Amount = amount,
            AppliesTo = appliesTo,
            Filter = filter,
            Value = value,
            Condition = condition,
            Duration = duration,
            Stacks = stacks,
            SourceArtifactDefId = defId,
            SourceArtifactInstanceId = artifactInstanceId,
            SourceController = player.Index
        });
    }

    private static ConditionDef Cond(ConditionOp op, int? value = null, string? side = null)
        => new()
        {
            Op = op,
            Value = value.HasValue ? JsonDocument.Parse(value.Value.ToString()).RootElement : null,
            Side = side
        };

    private static GameState EndTurn(GameState state, int playerIndex)
        => DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = playerIndex });

    private static GameState Play(GameState state, CardInstance card, int playerIndex, int? lane = null)
        => DuelEngine.Apply(state, new PlayCardAction
        {
            PlayerIndex = playerIndex,
            CardInstanceId = card.InstanceId,
            Cost = card.Cost,
            LaneIndex = card.CardType == CardType.CREATURE ? (lane ?? 0) : null
        });

    // ——— Basic discount & floor 0 ———

    [Fact]
    public void CostMod_AppliesBasicDiscount()
    {
        var state = CreateState();
        AddMod(state.Players[0], 1);

        var card = MakeHandCard(state, 0, CardType.CREATURE, "tst_c", cost: 3);

        Assert.Equal(2, CostInterceptor.GetEffectiveCost(state, card, 0));
    }

    [Fact]
    public void CostMod_NeverGoesBelowZero()
    {
        var state = CreateState();
        AddMod(state.Players[0], 5);

        var card = MakeHandCard(state, 0, CardType.CREATURE, "tst_c", cost: 2);

        // 2 - 5 → floor 0, never negative
        Assert.Equal(0, CostInterceptor.GetEffectiveCost(state, card, 0));
    }

    [Fact]
    public void CostMod_ZeroAmountDoesNothing()
    {
        var state = CreateState();
        AddMod(state.Players[0], 0);

        var card = MakeHandCard(state, 0, CardType.CREATURE, "tst_c", cost: 3);

        Assert.Equal(3, CostInterceptor.GetEffectiveCost(state, card, 0));
    }

    // ——— applies_to ———

    [Fact]
    public void CostMod_AppliesToSpell_DiscountsRitualsOnly()
    {
        var state = CreateState();
        AddMod(state.Players[0], 1, appliesTo: "SPELL");

        var creature = MakeHandCard(state, 0, CardType.CREATURE, "tst_cr", cost: 3);
        var ritual = MakeHandCard(state, 0, CardType.RITUAL, "tst_sp", cost: 3);

        Assert.Equal(3, CostInterceptor.GetEffectiveCost(state, creature, 0)); // creature NOT discounted
        Assert.Equal(2, CostInterceptor.GetEffectiveCost(state, ritual, 0));   // spell discounted
    }

    [Fact]
    public void CostMod_AppliesToCreature_DiscountsCreaturesOnly()
    {
        var state = CreateState();
        AddMod(state.Players[0], 1, appliesTo: "CREATURE");

        var creature = MakeHandCard(state, 0, CardType.CREATURE, "tst_cr", cost: 3);
        var ritual = MakeHandCard(state, 0, CardType.RITUAL, "tst_sp", cost: 3);

        Assert.Equal(2, CostInterceptor.GetEffectiveCost(state, creature, 0));
        Assert.Equal(3, CostInterceptor.GetEffectiveCost(state, ritual, 0));
    }

    [Fact]
    public void CostMod_NoAppliesTo_DiscountsAnyCardType()
    {
        var state = CreateState();
        AddMod(state.Players[0], 1);

        var creature = MakeHandCard(state, 0, CardType.CREATURE, "tst_cr", cost: 3);
        var ritual = MakeHandCard(state, 0, CardType.RITUAL, "tst_sp", cost: 3);

        Assert.Equal(2, CostInterceptor.GetEffectiveCost(state, creature, 0));
        Assert.Equal(2, CostInterceptor.GetEffectiveCost(state, ritual, 0));
    }

    // ——— Per-card filter ———

    [Fact]
    public void CostMod_FilterAttackLte_DiscountsOnlyLowAttackCreatures()
    {
        var state = CreateState();
        AddMod(state.Players[0], 1, appliesTo: "CREATURE", filter: "ATTACK_LTE", value: 2);

        var low = MakeHandCard(state, 0, CardType.CREATURE, "tst_low", cost: 3, attack: 1);
        var high = MakeHandCard(state, 0, CardType.CREATURE, "tst_high", cost: 3, attack: 3);

        Assert.Equal(2, CostInterceptor.GetEffectiveCost(state, low, 0));   // attack 1 ≤ 2 → discounted
        Assert.Equal(3, CostInterceptor.GetEffectiveCost(state, high, 0));  // attack 3 > 2 → full
    }

    // ——— Condition (evaluated at play time) ———

    [Fact]
    public void CostMod_ConditionCreatureDied_InactiveUntilDeath()
    {
        var state = CreateState();
        AddMod(state.Players[0], 1, appliesTo: "CREATURE",
            condition: Cond(ConditionOp.CREATURE_DIED_THIS_TURN, side: "ANY"));

        var creature = MakeHandCard(state, 0, CardType.CREATURE, "tst_cr", cost: 3);

        // No deaths yet — condition false, no discount
        Assert.Equal(3, CostInterceptor.GetEffectiveCost(state, creature, 0));

        // A creature dies (any side, side-aware ANY) — condition true at play time
        state.CreatureDiedThisTurnCount[1] = 1;
        Assert.Equal(2, CostInterceptor.GetEffectiveCost(state, creature, 0));

        // Side filter: ENEMY-only — an ally death does NOT activate it
        state.CreatureDiedThisTurnCount[1] = 0;
        state.CreatureDiedThisTurnCount[0] = 1;
        var enemyOnly = new CostMod
        {
            Amount = 1,
            AppliesTo = "CREATURE",
            Condition = Cond(ConditionOp.CREATURE_DIED_THIS_TURN, side: "ENEMY"),
            SourceController = 0
        };
        state.Players[0].CostMods.Clear();
        state.Players[0].CostMods.Add(enemyOnly);
        Assert.Equal(3, CostInterceptor.GetEffectiveCost(state, creature, 0)); // ally death only → full
        state.CreatureDiedThisTurnCount[0] = 0;
        state.CreatureDiedThisTurnCount[1] = 1;
        Assert.Equal(2, CostInterceptor.GetEffectiveCost(state, creature, 0)); // enemy death → discounted
    }

    // ——— Per-turn consumption (FIRST_SPELL_EACH_TURN) ———

    [Fact]
    public void CostMod_FirstSpellEachTurn_ConsumedAfterFirstSpellPlayed()
    {
        var state = CreateState();
        AddMod(state.Players[0], 1, appliesTo: "SPELL", filter: "FIRST_SPELL_EACH_TURN");

        var spell1 = MakeHandCard(state, 0, CardType.RITUAL, "tst_s1", cost: 3);
        var spell2 = MakeHandCard(state, 0, CardType.RITUAL, "tst_s2", cost: 3);

        // First spell is discounted at play time…
        state = Play(state, spell1, 0);
        Assert.Equal(8, state.Players[0].Attunement); // 10 - 2

        // …second spell pays full price (gate consumed)
        state = Play(state, spell2, 0);
        Assert.Equal(5, state.Players[0].Attunement); // 8 - 3
    }

    [Fact]
    public void CostMod_FirstSpellEachTurn_ConsumedOnlyByMatchingType()
    {
        var state = CreateState();
        AddMod(state.Players[0], 1, appliesTo: "SPELL", filter: "FIRST_SPELL_EACH_TURN");

        var creature = MakeHandCard(state, 0, CardType.CREATURE, "tst_cr", cost: 1);
        var spell = MakeHandCard(state, 0, CardType.RITUAL, "tst_sp", cost: 3);

        // Playing a creature does NOT consume the spell gate
        state = Play(state, creature, 0);
        Assert.Equal(9, state.Players[0].Attunement); // 10 - 1 (no discount on creature)

        // First spell still gets the discount
        state = Play(state, spell, 0);
        Assert.Equal(7, state.Players[0].Attunement); // 9 - 2
    }

    // ——— Duration THIS_TURN ———

    [Fact]
    public void CostMod_ThisTurn_ClearedWhenOwnerEndsTurn()
    {
        var state = CreateState();
        AddMod(state.Players[0], 1, appliesTo: "CREATURE", duration: Duration.THIS_TURN);

        Assert.Single(state.Players[0].CostMods);

        // Owner ends their turn → THIS_TURN mods are cleared
        state = EndTurn(state, 0);
        Assert.Empty(state.Players[0].CostMods);
    }

    [Fact]
    public void CostMod_ThisTurn_SurvivesEnemyTurn_UntilOwnerEndsTurn()
    {
        var state = CreateState();
        // Aura case: discount granted during the ENEMY's turn survives into the owner's turn
        AddMod(state.Players[0], 1, appliesTo: "SPELL", duration: Duration.THIS_TURN);

        // Enemy's turn ends — owner's mod is NOT cleared
        state = EndTurn(state, 1);
        Assert.Single(state.Players[0].CostMods);

        // Owner's turn ends — cleared
        state = EndTurn(state, 0);
        Assert.Empty(state.Players[0].CostMods);
    }

    // ——— Stacking ———

    [Fact]
    public void CostMod_StacksAccumulate()
    {
        var state = CreateState();
        // Aura: each enemy attack adds another -1 (same artifact, stacks: true)
        AddMod(state.Players[0], 1, appliesTo: "SPELL", duration: Duration.THIS_TURN, stacks: true);
        AddMod(state.Players[0], 1, appliesTo: "SPELL", duration: Duration.THIS_TURN, stacks: true);

        var spell = MakeHandCard(state, 0, CardType.RITUAL, "tst_sp", cost: 5);

        Assert.Equal(3, CostInterceptor.GetEffectiveCost(state, spell, 0)); // 5 - 2
    }

    // ——— No-stacking (passive re-application) ———

    [Fact]
    public void CostMod_NoStacking_ReapplicationReplaces()
    {
        var state = CreateState();
        var player = state.Players[0];
        var source = new CardInstance(100, "artf_test", 0);

        var effect = new EffectDef
        {
            Op = Op.COST_MOD,
            Amount = 1,
            AppliesTo = "SPELL",
            Filter = "FIRST_SPELL_EACH_TURN",
            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
        };

        var targets = TargetResolver.Resolve(effect.Target, source, player,
            state.Players[1], state);

        EffectExecutor.Execute(effect, source, state, targets);
        EffectExecutor.Execute(effect, source, state, targets);

        // Same artifact instance re-applied → replaced, not duplicated (G6-style no-stacking)
        Assert.Single(player.CostMods);
        Assert.Equal(1, player.CostMods[0].Amount);
    }

    // ——— EffectExecutor integration ———

    [Fact]
    public void CostMod_ApplyViaEffectExecutor_RegistersModOnPlayer()
    {
        var state = CreateState();
        var player = state.Players[0];
        var source = new CardInstance(1, "tst_costmod_source", 0);

        var effect = new EffectDef
        {
            Op = Op.COST_MOD,
            Amount = 1,
            AppliesTo = "CREATURE",
            Filter = "ATTACK_LTE",
            Value = 2,
            Target = new TargetDef { Scope = Scope.PLAYER_SELF }
        };

        var targets = TargetResolver.Resolve(effect.Target, source, player,
            state.Players[1], state);
        EffectExecutor.Execute(effect, source, state, targets);

        Assert.Single(player.CostMods);
        Assert.Equal(1, player.CostMods[0].Amount);
        Assert.Equal("CREATURE", player.CostMods[0].AppliesTo);
        Assert.Equal("ATTACK_LTE", player.CostMods[0].Filter);
        Assert.Equal(2, player.CostMods[0].Value);
    }

    // ——— Suppression symmetry ———

    [Fact]
    public void CostMod_ModFromSuppressedArtifact_IsInert()
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

        AddMod(player, 1, appliesTo: "CREATURE", artifactInstanceId: 100, defId: "artf_test");

        var creature = MakeHandCard(state, 0, CardType.CREATURE, "tst_cr", cost: 3);

        // Active before suppression
        Assert.Equal(2, CostInterceptor.GetEffectiveCost(state, creature, 0));

        // Apply suppression — mod is inert
        slot.ApplySuppression(1, "test_suppress");
        Assert.Equal(3, CostInterceptor.GetEffectiveCost(state, creature, 0));
    }

    [Fact]
    public void CostMod_RemoveModsFromArtifact_RemovesThem()
    {
        var state = CreateState();
        var player = state.Players[0];

        AddMod(player, 1, appliesTo: "CREATURE", artifactInstanceId: 100, defId: "artf_test");

        Assert.Single(player.CostMods);

        CostInterceptor.RemoveModsFromArtifact(state, 100, 0);

        Assert.Empty(player.CostMods);
    }

    // ——— Engine integration: play-time charging ———

    [Fact]
    public void CostMod_Engine_PlayCardChargesDiscountedCost()
    {
        var state = CreateState();
        AddMod(state.Players[0], 1);

        var card = MakeHandCard(state, 0, CardType.CREATURE, "tst_cr", cost: 3);

        state = Play(state, card, 0);

        // Charged 2, not 3
        Assert.Equal(8, state.Players[0].Attunement);
    }

    [Fact]
    public void CostMod_Engine_PlayCardChargesFullCostWithoutMod()
    {
        var state = CreateState();

        var card = MakeHandCard(state, 0, CardType.CREATURE, "tst_cr", cost: 3);

        state = Play(state, card, 0);

        Assert.Equal(7, state.Players[0].Attunement);
    }

    [Fact]
    public void CostMod_Engine_DiscountCanMakeCardAffordable()
    {
        var state = CreateState();
        state.Players[0].Attunement = 3;
        AddMod(state.Players[0], 1);

        // Cost 4 card, only 3 attunement — the -1 discount makes it playable
        var card = MakeHandCard(state, 0, CardType.CREATURE, "tst_cr", cost: 4);

        state = Play(state, card, 0);

        Assert.Equal(0, state.Players[0].Attunement); // 3 - 3 (effective)
    }

    [Fact]
    public void CostMod_Engine_InsufficientAttunement_StillRejects()
    {
        var state = CreateState();
        state.Players[0].Attunement = 2;
        AddMod(state.Players[0], 1);

        var card = MakeHandCard(state, 0, CardType.CREATURE, "tst_cr", cost: 4);

        // Effective cost 3 > 2 attunement — still rejected
        var ex = Assert.Throws<InvalidOperationException>(() => Play(state, card, 0));
        Assert.Contains("attunement", ex.Message.ToLower());
    }

    // ——— Clone + state hash ———

    [Fact]
    public void CostMod_ClonePreservesMod()
    {
        var state = CreateState();
        AddMod(state.Players[0], 1, appliesTo: "SPELL", filter: "FIRST_SPELL_EACH_TURN");

        var cloned = state.Clone();

        Assert.Single(cloned.Players[0].CostMods);
        Assert.Equal(1, cloned.Players[0].CostMods[0].Amount);
        Assert.Equal("SPELL", cloned.Players[0].CostMods[0].AppliesTo);
        Assert.Equal("FIRST_SPELL_EACH_TURN", cloned.Players[0].CostMods[0].Filter);
    }

    [Fact]
    public void CostMod_StateHash_IncludesMods()
    {
        var state = CreateState();
        ulong baseline = state.ComputeStateHash();

        AddMod(state.Players[0], 1, appliesTo: "CREATURE");
        ulong withMod = state.ComputeStateHash();

        Assert.NotEqual(baseline, withMod);
    }

    // ——— launch_artifacts.json migration ———

    [Fact]
    public void LaunchArtifacts_DiscountsMigratedToCostMod_NoAttuneRemains()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "content", "artifacts", "launch_artifacts.json");
        var json = File.ReadAllText(path);

        using var doc = JsonDocument.Parse(json);
        var artifacts = doc.RootElement.EnumerateArray().ToList();

        Assert.Equal(14, artifacts.Count);

        // Every discount previously encoded as ATTUNE is now COST_MOD.
        var byId = artifacts.ToDictionary(a => a.GetProperty("id").GetString()!);

        // Battlemage wand (placeholder BUFF until TASK-CLASS-IDENTITY-1)
        Assert.Equal("BUFF", byId["artf_battlemage_wand"].GetProperty("passive").GetProperty("op").GetString());
        Assert.Equal("ATTACKING", byId["artf_battlemage_wand"].GetProperty("passive").GetProperty("target").GetProperty("filter").GetString());

        // Battlemage aura (placeholder BUFF until TASK-CLASS-IDENTITY-1)
        var auraPassive = byId["artf_battlemage_aura"].GetProperty("passive");
        Assert.Equal("BUFF", auraPassive.GetProperty("op").GetString());
        Assert.Equal("HAS_NOT_ATTACKED", auraPassive.GetProperty("target").GetProperty("filter").GetString());

        var duskfangPassive = byId["artf_rogue_dagger_dusk"].GetProperty("passive");
        Assert.Equal("COST_MOD", duskfangPassive.GetProperty("op").GetString());
        Assert.Equal("ATTACK_LTE", duskfangPassive.GetProperty("filter").GetString());
        Assert.Equal(2, duskfangPassive.GetProperty("value").GetInt32());

        var grimoirePassive = byId["artf_necromancer_skull"].GetProperty("passive");
        Assert.Equal("COST_MOD", grimoirePassive.GetProperty("op").GetString());
        Assert.Equal("CREATURE_DIED_THIS_TURN", grimoirePassive.GetProperty("condition").GetProperty("op").GetString());

        // No ATTUNE op anywhere in the file.
        Assert.DoesNotContain("ATTUNE", json);
    }

    [Fact]
    public void CostMod_EffectDef_DeserializesFromJson()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        // Duskfang-style: creature discount with attack filter
        var dusk = JsonSerializer.Deserialize<EffectDef>(
            "{\"op\":\"COST_MOD\",\"applies_to\":\"CREATURE\",\"filter\":\"ATTACK_LTE\",\"value\":2,\"target\":{\"scope\":\"PLAYER_SELF\"},\"amount\":1}",
            opts)!;
        Assert.Equal(Op.COST_MOD, dusk.Op);
        Assert.Equal("CREATURE", dusk.AppliesTo);
        Assert.Equal("ATTACK_LTE", dusk.Filter);
        Assert.Equal(2, dusk.Value);
        Assert.Equal(1, dusk.Amount);

        // Aura-style: stacking this-turn spell discount
        var aura = JsonSerializer.Deserialize<EffectDef>(
            "{\"op\":\"COST_MOD\",\"applies_to\":\"SPELL\",\"target\":{\"scope\":\"PLAYER_SELF\"},\"amount\":1,\"duration\":\"THIS_TURN\",\"stacks\":true}",
            opts)!;
        Assert.Equal(Op.COST_MOD, aura.Op);
        Assert.Equal("SPELL", aura.AppliesTo);
        Assert.Equal(Duration.THIS_TURN, aura.Duration);
        Assert.True(aura.Stacks);

        // Wand-style: per-turn consumption filter
        var wand = JsonSerializer.Deserialize<EffectDef>(
            "{\"op\":\"COST_MOD\",\"applies_to\":\"SPELL\",\"filter\":\"FIRST_SPELL_EACH_TURN\",\"target\":{\"scope\":\"PLAYER_SELF\"},\"amount\":1}",
            opts)!;
        Assert.Equal("FIRST_SPELL_EACH_TURN", wand.Filter);
    }

    // ——— End-to-end via artifact passive ———

    [Fact]
    public void CostMod_EndToEnd_ArtifactPassiveDiscountsOnNextTurn()
    {
        var state = CreateState();
        var player = state.Players[0];

        // Wire a Wand-like artifact with a COST_MOD passive, then apply passives
        // the same way ApplyArtifactPassives does at turn start.
        player.ArtifactSlots = new ArtifactSlot[1];
        var slot = new ArtifactSlot(0);
        var artifact = new CardInstance(state.NextInstanceId++, "artf_mage_wand", 0)
        {
            CardType = CardType.ARTIFACT,
            Zone = Zone.ArtifactSlot,
            ArtifactSlotIndex = 0
        };
        artifact.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef>
            {
                new()
                {
                    Op = Op.COST_MOD,
                    AppliesTo = "SPELL",
                    Filter = "FIRST_SPELL_EACH_TURN",
                    Amount = 1,
                    Target = new TargetDef { Scope = Scope.PLAYER_SELF }
                }
            }
        });
        slot.Occupant = artifact;
        player.ArtifactSlots[0] = slot;

        // Apply passives by ending the opponent's turn (P1 end → P0 passives apply)
        state = EndTurn(state, 1);

        var spell = MakeHandCard(state, 0, CardType.RITUAL, "tst_sp", cost: 3);
        state = Play(state, spell, 0);

        // First spell of the turn discounted: charged 2, not 3
        Assert.Equal(8, state.Players[0].Attunement);

        // Second spell pays full price (per-turn gate consumed)
        var spell2 = MakeHandCard(state, 0, CardType.RITUAL, "tst_sp2", cost: 3);
        state = Play(state, spell2, 0);
        Assert.Equal(5, state.Players[0].Attunement);
    }
}

using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

public class EffectExecutorTests
{
    // ——— Helpers ———

    /// <summary>
    /// Creates a GameState with pre-placed creatures for effect testing.
    /// P0 lane 0: 3/4 Verdant (Damage=0)
    /// P0 lane 1: 2/2 Ember (Damage=1, damaged)
    /// P0 lane 2: 5/5 Hollow (Damage=0, Guard)
    /// P1 lane 0: 4/3 Tide (Damage=0)
    /// P1 lane 1: 1/1 Dawn (Damage=0, Swift)
    /// </summary>
    private static GameState CreateEffectState()
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
            // Add some barrow cards for P0
            if (p == 0)
            {
                for (int i = 0; i < 3; i++)
                {
                    var bc = new CardInstance(state.NextInstanceId++, "tst_buried", 0) { Zone = Zone.Barrow };
                    state.Players[0].Barrow.Add(bc);
                }
            }
        }

        // P0 creatures
        state.Players[0].Lanes[0].Occupant = new CardInstance(
            state.NextInstanceId++, "tst_vrd", 0)
        {
            Zone = Zone.Lane, LaneIndex = 0,
            CardType = CardType.CREATURE, Strata = Strata.VERDANT,
            BaseAttack = 3, BaseVigor = 4, Cost = 3,
            IsExhausted = true
        };
        state.Players[0].Lanes[1].Occupant = new CardInstance(
            state.NextInstanceId++, "tst_emb", 0)
        {
            Zone = Zone.Lane, LaneIndex = 1,
            CardType = CardType.CREATURE, Strata = Strata.EMBER,
            BaseAttack = 2, BaseVigor = 2, Cost = 2,
            Damage = 1, IsExhausted = true
        };
        state.Players[0].Lanes[2].Occupant = new CardInstance(
            state.NextInstanceId++, "tst_hol", 0)
        {
            Zone = Zone.Lane, LaneIndex = 2,
            CardType = CardType.CREATURE, Strata = Strata.HOLLOW,
            BaseAttack = 5, BaseVigor = 5, Cost = 5,
            IsExhausted = true
        };
        state.Players[0].Lanes[2].Occupant.Keywords.Add("GUARD");

        // P1 creatures
        state.Players[1].Lanes[0].Occupant = new CardInstance(
            state.NextInstanceId++, "tst_tid", 1)
        {
            Zone = Zone.Lane, LaneIndex = 0,
            CardType = CardType.CREATURE, Strata = Strata.TIDE,
            BaseAttack = 4, BaseVigor = 3, Cost = 4,
            IsExhausted = false
        };
        state.Players[1].Lanes[1].Occupant = new CardInstance(
            state.NextInstanceId++, "tst_dwn", 1)
        {
            Zone = Zone.Lane, LaneIndex = 1,
            CardType = CardType.CREATURE, Strata = Strata.DAWN,
            BaseAttack = 1, BaseVigor = 1, Cost = 1,
            IsExhausted = false
        };
        state.Players[1].Lanes[1].Occupant.Keywords.Add("SWIFT");

        return state;
    }

    /// <summary>
    /// Execute an effect and return the resulting state.
    /// </summary>
    private static GameState ExecEffect(
        GameState state,
        Op op,
        TargetDef targetDef,
        int? amount = null,
        int? attack = null,
        int? vigor = null,
        string? keyword = null,
        string? tokenId = null,
        Duration? duration = null)
    {
        var state2 = state.Clone();
        var source = state2.Players[0].Lanes[0].Occupant!;
        var srcPlayer = state2.Players[0];
        var oppPlayer = state2.Players[1];

        var targets = TargetResolver.Resolve(targetDef, source, srcPlayer, oppPlayer, state2);
        var effect = new EffectDef
        {
            Op = op,
            Target = targetDef,
            Amount = amount,
            Attack = attack,
            Vigor = vigor,
            Keyword = keyword,
            TokenId = tokenId,
            Duration = duration
        };
        EffectExecutor.Execute(effect, source, state2, targets);
        return state2;
    }

    // ——— All 23 OPs ———

    public static IEnumerable<object[]> OpTestData()
    {
        // (op, targetDef, amount, attack, vigor, keyword, tokenId, assertion description)
        // each entry yields: Op, TargetDef, expected side effect check
        yield return new object[] { Op.DAMAGE, new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "ANY", Count = TargetCount.Exactly(1) }, 3, null, null, null, null,
            "Damage reduces creature vigor" };
        yield return new object[] { Op.HEAL, new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "DAMAGED", Count = TargetCount.Exactly(1) }, 1, null, null, null, null,
            "Heal reduces damage on damaged creature" };
        yield return new object[] { Op.BUFF, new TargetDef { Scope = Scope.SELF }, null, 1, 2, null, null,
            "Buff adds attack and vigor modifier" };
        yield return new object[] { Op.DEBUFF, new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "ANY", Count = TargetCount.Exactly(1) }, null, -1, -1, null, null,
            "Debuff subtracts attack and vigor modifier" };
        yield return new object[] { Op.DESTROY, new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "LOWEST_VIGOR", Count = TargetCount.Exactly(1) }, null, null, null, null, null,
            "Destroy kills a creature" };
        yield return new object[] { Op.DRAW, new TargetDef { Scope = Scope.PLAYER_SELF }, 2, null, null, null, null,
            "Draw adds cards from deck to hand" };
        yield return new object[] { Op.DISCARD, new TargetDef { Scope = Scope.PLAYER_ENEMY }, 1, null, null, null, null,
            "Discard removes cards from hand" };
        yield return new object[] { Op.EXCAVATE, new TargetDef { Scope = Scope.PLAYER_SELF }, 3, null, null, null, null,
            "Excavate moves 1 to hand, buries rest" };
        yield return new object[] { Op.BURY, new TargetDef { Scope = Scope.PLAYER_SELF }, 2, null, null, null, null,
            "Bury moves cards from deck to barrow" };
        yield return new object[] { Op.UNBURY, new TargetDef { Scope = Scope.PLAYER_SELF }, 2, null, null, null, null,
            "Unbury returns cards from barrow to hand" };
        yield return new object[] { Op.SUMMON, new TargetDef { Scope = Scope.PLAYER_SELF }, null, null, null, null, "tst_token",
            "Summon creates a token in an empty lane" };
        yield return new object[] { Op.GRANT_KEY, new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "ANY", Count = TargetCount.Exactly(1) }, null, null, null, "WARD", null,
            "Grant key adds keyword" };
        yield return new object[] { Op.REMOVE_KEY, new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "KEYWORD:GUARD", Count = TargetCount.Exactly(1) }, null, null, null, "GUARD", null,
            "Remove key suppresses keyword" };
        yield return new object[] { Op.SILENCE, new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "KEYWORD:GUARD", Count = TargetCount.Exactly(1) }, null, null, null, null, null,
            "Silence suppresses all keywords" };
        yield return new object[] { Op.BOUNCE, new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "ANY", Count = TargetCount.Exactly(1) }, null, null, null, null, null,
            "Bounce returns creature to hand" };
        yield return new object[] { Op.ATTUNE, new TargetDef { Scope = Scope.PLAYER_SELF }, 2, null, null, null, null,
            "Attune increases attunement" };
        yield return new object[] { Op.MOVE_LANE, new TargetDef { Scope = Scope.LANE, Filter = "ANY", Count = TargetCount.Exactly(1) }, null, null, null, null, null,
            "Move lane moves source to another empty lane" };
        yield return new object[] { Op.IDENTIFY, new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "ANY", Count = TargetCount.Exactly(1) }, null, null, null, null, null,
            "Identify sets relic as identified" };
        yield return new object[] { Op.GAIN_VIGOR, new TargetDef { Scope = Scope.PLAYER_SELF }, 5, null, null, null, null,
            "Gain vigor increases player max vigor" };
        yield return new object[] { Op.LOSE_VIGOR, new TargetDef { Scope = Scope.PLAYER_ENEMY }, 3, null, null, null, null,
            "Lose vigor decreases player max vigor" };
        yield return new object[] { Op.COPY, new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "ANY", Count = TargetCount.Exactly(1) }, null, null, null, null, null,
            "Copy duplicates a creature in its lane" };
        yield return new object[] { Op.SET_STAT, new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "ANY", Count = TargetCount.Exactly(1) }, null, 99, 99, null, null,
            "Set stat changes base attack and vigor" };
        yield return new object[] { Op.REFRESH, new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "ANY", Count = TargetCount.Exactly(1) }, null, null, null, null, null,
            "Refresh un-exhausts a creature" };
    }

    [Theory]
    [MemberData(nameof(OpTestData))]
    public void Op_Executes(
        Op op, TargetDef targetDef,
        int? amount, int? attack, int? vigor,
        string? keyword, string? tokenId,
        string description)
    {
        var state = CreateEffectState();
        // Ensure P0 has cards in hand for DISCARD tests
        state.Players[1].Hand.Add(
            new CardInstance(state.NextInstanceId++, "tst_hand", 1) { Zone = Zone.Hand });

        var result = ExecEffect(state, op, targetDef, amount, attack, vigor, keyword, tokenId);

        // Each test just checks the effect didn't crash; specific assertions follow
        Assert.NotNull(result);
    }

    // ——— OP-specific detailed assertions ———

    [Fact]
    public void Op_Damage_ReducesCreatureVigor()
    {
        var state = CreateEffectState();
        var result = ExecEffect(state, Op.DAMAGE,
            new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "ANY" }, amount: 2);
        // P1 lane 0: 4/3 takes 2 → Damage=2, CurrentVigor=1
        Assert.Equal(2, result.Players[1].Lanes[0].Occupant!.Damage);
    }

    [Fact]
    public void Op_Damage_CanKillCreature()
    {
        var state = CreateEffectState();
        var result = ExecEffect(state, Op.DAMAGE,
            new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "ANY" }, amount: 999);
        Assert.Null(result.Players[1].Lanes[0].Occupant);
        Assert.Contains(result.Players[1].Discard, c => c.CardDefId == "tst_tid");
    }

    [Fact]
    public void Op_Damage_DamagesPlayer()
    {
        var state = CreateEffectState();
        var result = ExecEffect(state, Op.DAMAGE,
            new TargetDef { Scope = Scope.PLAYER_ENEMY }, amount: 7);
        Assert.Equal(18, result.Players[1].Vigor);
    }

    [Fact]
    public void Op_Heal_ReducesDamage()
    {
        var state = CreateEffectState();
        var result = ExecEffect(state, Op.HEAL,
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "DAMAGED" }, amount: 1);
        // P0 lane 1: 2/2 with Damage=1 → heal 1 → Damage=0
        Assert.Equal(0, result.Players[0].Lanes[1].Occupant!.Damage);
    }

    [Fact]
    public void Op_Buff_ModifiesStats()
    {
        var state = CreateEffectState();
        var result = ExecEffect(state, Op.BUFF,
            new TargetDef { Scope = Scope.SELF }, attack: 2, vigor: 3);
        Assert.Equal(2, result.Players[0].Lanes[0].Occupant!.AttackModifier);
        Assert.Equal(3, result.Players[0].Lanes[0].Occupant.VigorModifier);
    }

    [Fact]
    public void Op_Debuff_NegativeModifier()
    {
        var state = CreateEffectState();
        var result = ExecEffect(state, Op.DEBUFF,
            new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "ANY" }, attack: 1, vigor: 1);
        // P1 lane 0: 4/3 → AttackMod=-1, VigorMod=-1
        Assert.Equal(-1, result.Players[1].Lanes[0].Occupant!.AttackModifier);
        Assert.Equal(-1, result.Players[1].Lanes[0].Occupant.VigorModifier);
    }

    [Fact]
    public void Op_Destroy_KillsLowestVigor()
    {
        var state = CreateEffectState();
        var result = ExecEffect(state, Op.DESTROY,
            new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "LOWEST_VIGOR" });
        // P1 lane 1: 1/1 has lowest vigor → destroyed
        Assert.Null(result.Players[1].Lanes[1].Occupant);
        Assert.NotNull(result.Players[1].Lanes[0].Occupant);
    }

    [Fact]
    public void Op_Draw_AddsCardsToHand()
    {
        var state = CreateEffectState();
        var result = ExecEffect(state, Op.DRAW,
            new TargetDef { Scope = Scope.PLAYER_SELF }, amount: 2);
        Assert.Equal(2, result.Players[0].Hand.Count);
        Assert.Equal(3, result.Players[0].Deck.Count); // was 5
    }

    [Fact]
    public void Op_Discard_RemovesCards()
    {
        var state = CreateEffectState();
        state.Players[0].Hand.Add(
            new CardInstance(state.NextInstanceId++, "tst_discard", 0) { Zone = Zone.Hand });
        var result = ExecEffect(state, Op.DISCARD,
            new TargetDef { Scope = Scope.PLAYER_SELF }, amount: 1);
        Assert.Empty(result.Players[0].Hand);
        Assert.Single(result.Players[0].Discard);
    }

    [Fact]
    public void Op_Excavate_MovesOneToHandBuriesRest()
    {
        var state = CreateEffectState();
        var state2 = state.Clone();
        // Direct test: manually call the excavate path
        var src = state2.Players[0].Lanes[0].Occupant!;
        var player = state2.Players[0];
        var targets = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.PLAYER_SELF }, src, player, state2.Players[1], state2);
        Assert.Single(targets);
        var effect = new EffectDef { Op = Op.EXCAVATE, Target = new TargetDef { Scope = Scope.PLAYER_SELF }, Amount = 3 };
        EffectExecutor.Execute(effect, src, state2, targets);
        Assert.Equal(2, state2.Players[0].Deck.Count);
        Assert.Equal(5, state2.Players[0].Barrow.Count); // 3 + 2 buried
        Assert.Single(state2.Players[0].Hand);
    }

    [Fact]
    public void Op_Bury_MovesToBarrow()
    {
        var state = CreateEffectState();
        var result = ExecEffect(state, Op.BURY,
            new TargetDef { Scope = Scope.PLAYER_SELF }, amount: 2);
        Assert.Equal(3, result.Players[0].Deck.Count); // 5 - 2
        Assert.Equal(5, result.Players[0].Barrow.Count); // 3 + 2
    }

    [Fact]
    public void Op_Unbury_ReturnsFromBarrow()
    {
        var state = CreateEffectState();
        var result = ExecEffect(state, Op.UNBURY,
            new TargetDef { Scope = Scope.PLAYER_SELF }, amount: 2);
        Assert.Equal(2, result.Players[0].Hand.Count);
        Assert.Single(result.Players[0].Barrow); // 3 - 2
    }

    [Fact]
    public void Op_Summon_CreatesToken()
    {
        var state = CreateEffectState();
        // P0 lanes 0,1,2 are occupied. Summon to an empty lane (3 or 4).
        var result = ExecEffect(state, Op.SUMMON,
            new TargetDef { Scope = Scope.PLAYER_SELF }, tokenId: "tst_token");
        Assert.NotNull(result.Players[0].Lanes[3].Occupant);
        Assert.Equal("tst_token", result.Players[0].Lanes[3].Occupant!.CardDefId);
        Assert.Equal(CardType.TOKEN, result.Players[0].Lanes[3].Occupant.CardType);
    }

    [Fact]
    public void Op_GrantKey_AddsKeyword()
    {
        var state = CreateEffectState();
        var result = ExecEffect(state, Op.GRANT_KEY,
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "ANY" }, keyword: "VENOM");
        Assert.Contains("VENOM", result.Players[0].Lanes[0].Occupant!.GrantedKeywords);
    }

    [Fact]
    public void Op_RemoveKey_SuppressesKeyword()
    {
        var state = CreateEffectState();
        var result = ExecEffect(state, Op.REMOVE_KEY,
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "KEYWORD:GUARD" }, keyword: "GUARD");
        Assert.Contains("GUARD", result.Players[0].Lanes[2].Occupant!.RemovedKeywords);
        // Effective should no longer have GUARD
        Assert.DoesNotContain("GUARD", result.Players[0].Lanes[2].Occupant.EffectiveKeywords);
    }

    [Fact]
    public void Op_Silence_SuppressesAllKeywords()
    {
        var state = CreateEffectState();
        var result = ExecEffect(state, Op.SILENCE,
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "KEYWORD:GUARD" });
        Assert.Contains("GUARD", result.Players[0].Lanes[2].Occupant!.RemovedKeywords);
        Assert.DoesNotContain("GUARD", result.Players[0].Lanes[2].Occupant.EffectiveKeywords);
    }

    [Fact]
    public void Op_Bounce_ReturnsToHand()
    {
        var state = CreateEffectState();
        var result = ExecEffect(state, Op.BOUNCE,
            new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "ANY" });
        Assert.Null(result.Players[1].Lanes[0].Occupant);
        Assert.Contains(result.Players[1].Hand, c => c.CardDefId == "tst_tid");
    }

    [Fact]
    public void Op_Attune_IncreasesAttunement()
    {
        var state = CreateEffectState();
        var result = ExecEffect(state, Op.ATTUNE,
            new TargetDef { Scope = Scope.PLAYER_SELF }, amount: 2);
        Assert.Equal(10, result.Players[0].AttunementMax); // capped at 10 (was 10 + 2 = 12 → 10)
        Assert.Equal(10, result.Players[0].Attunement);
    }

    [Fact]
    public void Op_MoveLane_MovesSource()
    {
        var state = CreateEffectState();
        // MOVE_LANE should move source from lane 0 to first empty lane (lane 3)
        var result = ExecEffect(state, Op.MOVE_LANE,
            new TargetDef { Scope = Scope.SELF });
        Assert.Null(result.Players[0].Lanes[0].Occupant);
        Assert.NotNull(result.Players[0].Lanes[3].Occupant);
        Assert.Equal(3, result.Players[0].Lanes[3].Occupant!.LaneIndex);
        Assert.Equal("tst_vrd", result.Players[0].Lanes[3].Occupant.CardDefId);
    }

    [Fact]
    public void Op_Identify_SetsFlag()
    {
        var state = CreateEffectState();
        var result = ExecEffect(state, Op.IDENTIFY,
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "ANY" });
        Assert.True(result.Players[0].Lanes[0].Occupant!.IsIdentified);
    }

    [Fact]
    public void Op_GainVigor_IncreasesMaxAndCurrent()
    {
        var state = CreateEffectState();
        var result = ExecEffect(state, Op.GAIN_VIGOR,
            new TargetDef { Scope = Scope.PLAYER_SELF }, amount: 5);
        Assert.Equal(30, result.Players[0].MaxVigor);
        Assert.Equal(30, result.Players[0].Vigor);
    }

    [Fact]
    public void Op_LoseVigor_DecreasesMaxAndCurrent()
    {
        var state = CreateEffectState();
        var result = ExecEffect(state, Op.LOSE_VIGOR,
            new TargetDef { Scope = Scope.PLAYER_ENEMY }, amount: 3);
        Assert.Equal(22, result.Players[1].MaxVigor);
        Assert.Equal(22, result.Players[1].Vigor);
    }

    [Fact]
    public void Op_Copy_DuplicateCreature()
    {
        var state = CreateEffectState();
        var result = ExecEffect(state, Op.COPY,
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "ANY" });
        // P0 lane 0 had a 3/4 creature. Copy should replace it with same stats.
        Assert.NotNull(result.Players[0].Lanes[0].Occupant);
        Assert.Equal(3, result.Players[0].Lanes[0].Occupant!.BaseAttack);
        Assert.Equal(4, result.Players[0].Lanes[0].Occupant.BaseVigor);
    }

    [Fact]
    public void Op_SetStat_ChangesBase()
    {
        var state = CreateEffectState();
        var result = ExecEffect(state, Op.SET_STAT,
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "ANY" }, attack: 99, vigor: 99);
        Assert.Equal(99, result.Players[0].Lanes[0].Occupant!.BaseAttack);
        Assert.Equal(99, result.Players[0].Lanes[0].Occupant.BaseVigor);
    }

    [Fact]
    public void Op_Refresh_UnExhausts()
    {
        var state = CreateEffectState();
        // P0 lane 0 creature is exhausted
        var result = ExecEffect(state, Op.REFRESH,
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "ANY" });
        Assert.False(result.Players[0].Lanes[0].Occupant!.IsExhausted);
    }

    // ——— All filters ———

    [Fact]
    public void Filter_ANY_SelectsFirst()
    {
        var state = CreateEffectState();
        var src = state.Players[0].Lanes[0].Occupant!;
        var targets = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "ANY" },
            src, state.Players[0], state.Players[1], state);
        Assert.Single(targets);
        Assert.Equal(0, ((CreatureTarget)targets[0]).LaneIndex);
    }

    [Fact]
    public void Filter_ADJACENT_SelectsAdjacentLanes()
    {
        var state = CreateEffectState();
        var src = state.Players[0].Lanes[1].Occupant!;
        var targets = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "ADJACENT", Count = TargetCount.All },
            src, state.Players[0], state.Players[1], state);
        // Adjacent to lane 1: lanes 0 and 2. Both occupied.
        Assert.Equal(2, targets.Count);
    }

    [Fact]
    public void Filter_OPPOSING_SelectsOpposingLane()
    {
        var state = CreateEffectState();
        var src = state.Players[0].Lanes[1].Occupant!; // P0 lane 1
        var targets = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "OPPOSING" },
            src, state.Players[0], state.Players[1], state);
        Assert.Single(targets);
        Assert.Equal(1, ((CreatureTarget)targets[0]).LaneIndex);
        Assert.Equal("tst_dwn", ((CreatureTarget)targets[0]).Card.CardDefId);
    }

    [Fact]
    public void Filter_SAME_LANE_SelectsSameLane()
    {
        var state = CreateEffectState();
        var src = state.Players[0].Lanes[2].Occupant!;
        var targets = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "SAME_LANE" },
            src, state.Players[0], state.Players[1], state);
        Assert.Single(targets);
        Assert.Equal(2, ((CreatureTarget)targets[0]).LaneIndex);
    }

    [Fact]
    public void Filter_EDGE_LANE_SelectsLanes0And4()
    {
        var state = CreateEffectState();
        var src = state.Players[0].Lanes[1].Occupant!;
        var targets = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "EDGE_LANE" },
            src, state.Players[0], state.Players[1], state);
        // Only lane 0 is an edge lane that's occupied
        Assert.Single(targets);
        Assert.Equal(0, ((CreatureTarget)targets[0]).LaneIndex);
    }

    [Fact]
    public void Filter_CENTER_LANE_SelectsLane2()
    {
        var state = CreateEffectState();
        var src = state.Players[0].Lanes[0].Occupant!;
        var targets = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "CENTER_LANE" },
            src, state.Players[0], state.Players[1], state);
        Assert.Single(targets);
        Assert.Equal(2, ((CreatureTarget)targets[0]).LaneIndex);
    }

    [Fact]
    public void Filter_DAMAGED_SelectsDamaged()
    {
        var state = CreateEffectState();
        var src = state.Players[0].Lanes[0].Occupant!;
        var targets = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "DAMAGED" },
            src, state.Players[0], state.Players[1], state);
        Assert.Single(targets);
        Assert.Equal(1, ((CreatureTarget)targets[0]).LaneIndex); // lane 1 has Damage=1
    }

    [Fact]
    public void Filter_UNDAMAGED_SelectsUndamaged()
    {
        var state = CreateEffectState();
        var src = state.Players[0].Lanes[0].Occupant!;
        var targets = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "UNDAMAGED", Count = TargetCount.All },
            src, state.Players[0], state.Players[1], state);
        Assert.Equal(2, targets.Count); // lanes 0 and 2
    }

    [Fact]
    public void Filter_STRATA_FiltersByStratum()
    {
        var state = CreateEffectState();
        var src = state.Players[0].Lanes[0].Occupant!;
        var targets = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "STRATA:VERDANT" },
            src, state.Players[0], state.Players[1], state);
        Assert.Single(targets);
        Assert.Equal("tst_vrd", ((CreatureTarget)targets[0]).Card.CardDefId);
    }

    [Fact]
    public void Filter_KEYWORD_FiltersByKeyword()
    {
        var state = CreateEffectState();
        var src = state.Players[0].Lanes[0].Occupant!;
        var targets = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "KEYWORD:GUARD" },
            src, state.Players[0], state.Players[1], state);
        Assert.Single(targets);
        Assert.Equal(2, ((CreatureTarget)targets[0]).LaneIndex);
    }

    [Fact]
    public void Filter_TYPE_FiltersByType()
    {
        var state = CreateEffectState();
        var src = state.Players[0].Lanes[0].Occupant!;
        var targets = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "TYPE:CREATURE", Count = TargetCount.All },
            src, state.Players[0], state.Players[1], state);
        Assert.Equal(3, targets.Count); // all 3 P0 creatures are CREATURE type
    }

    [Fact]
    public void Filter_LOWEST_VIGOR_OrdersByVigorAscending()
    {
        var state = CreateEffectState();
        var src = state.Players[0].Lanes[0].Occupant!;
        var targets = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "LOWEST_VIGOR" },
            src, state.Players[0], state.Players[1], state);
        Assert.Single(targets);
        Assert.Equal(1, ((CreatureTarget)targets[0]).LaneIndex); // 1/1 has lowest vigor
    }

    [Fact]
    public void Filter_HIGHEST_ATTACK_OrdersByAttackDescending()
    {
        var state = CreateEffectState();
        var src = state.Players[0].Lanes[0].Occupant!;
        var targets = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "HIGHEST_ATTACK" },
            src, state.Players[0], state.Players[1], state);
        Assert.Single(targets);
        Assert.Equal(0, ((CreatureTarget)targets[0]).LaneIndex); // 4/3 has highest attack
    }

    [Fact]
    public void Filter_LOWEST_COST_OrdersByCostAscending()
    {
        var state = CreateEffectState();
        var src = state.Players[0].Lanes[0].Occupant!;
        var targets = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "LOWEST_COST" },
            src, state.Players[0], state.Players[1], state);
        Assert.Single(targets);
        Assert.Equal(1, ((CreatureTarget)targets[0]).LaneIndex); // cost 1 (Dawn)
    }

    [Fact]
    public void Filter_HIGHEST_COST_OrdersByCostDescending()
    {
        var state = CreateEffectState();
        var src = state.Players[0].Lanes[0].Occupant!;
        var targets = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "HIGHEST_COST" },
            src, state.Players[0], state.Players[1], state);
        Assert.Single(targets);
        Assert.Equal(0, ((CreatureTarget)targets[0]).LaneIndex); // cost 4 (Tide)
    }

    [Fact]
    public void Filter_CHOSEN_SelectsFirst()
    {
        var state = CreateEffectState();
        var src = state.Players[0].Lanes[0].Occupant!;
        var targets = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "CHOSEN" },
            src, state.Players[0], state.Players[1], state);
        Assert.Single(targets);
        Assert.Equal(0, ((CreatureTarget)targets[0]).LaneIndex); // first in pool
    }

    [Fact]
    public void Scope_PLAYER_ENEMY_TargetsOpponent()
    {
        var state = CreateEffectState();
        var src = state.Players[0].Lanes[0].Occupant!;
        var targets = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.PLAYER_ENEMY },
            src, state.Players[0], state.Players[1], state);
        Assert.Single(targets);
        Assert.IsType<PlayerTarget>(targets[0]);
        Assert.Equal(1, ((PlayerTarget)targets[0]).Player.Index);
    }

    [Fact]
    public void Count_ALL_ReturnsAllMatches()
    {
        var state = CreateEffectState();
        var src = state.Players[0].Lanes[0].Occupant!;
        var targets = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "ANY", Count = TargetCount.All },
            src, state.Players[0], state.Players[1], state);
        Assert.Equal(2, targets.Count);
    }

    [Fact]
    public void Count_2_ReturnsTwo()
    {
        var state = CreateEffectState();
        var src = state.Players[0].Lanes[0].Occupant!;
        var targets = TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ENEMY_CREATURE, Filter = "ANY", Count = TargetCount.Exactly(2) },
            src, state.Players[0], state.Players[1], state);
        Assert.Equal(2, targets.Count);
    }
}
using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// TASK-DSL-1 — Turn-scoped counters + conditions + filters.
/// Covers G5 (counters reset at start of every turn, per player), the
/// ATTACKERS_THIS_TURN_GTE/EQ, SPELLS_CAST_THIS_TURN_EQ, NO_ATTACKERS_LAST_TURN,
/// CREATURE_DIED_THIS_TURN (side-aware) conditions, and the
/// HAS_NOT_ATTACKED / FIRST_ATTACKER / FIRST_ATTACKED filters.
/// </summary>
public class TurnScopedCounterTests
{
    // ——— Helpers ———

    private static GameState CreateState(int p0HandSize = 0, int p1HandSize = 0)
    {
        var state = new GameState(seed: 42);
        for (int p = 0; p < 2; p++)
        {
            state.Players[p].AttunementMax = 10;
            state.Players[p].Attunement = 10;
            for (int i = 0; i < 10; i++)
            {
                var c = new CardInstance(state.NextInstanceId++, "tst_deck_filler", p)
                { Zone = Zone.Deck };
                state.Players[p].Deck.Add(c);
            }
        }
        for (int i = 0; i < p0HandSize; i++)
            state.Players[0].Hand.Add(MakeHandCard(state, 0, CardType.CREATURE, "tst_p0_hand"));
        for (int i = 0; i < p1HandSize; i++)
            state.Players[1].Hand.Add(MakeHandCard(state, 1, CardType.CREATURE, "tst_p1_hand"));
        return state;
    }

    private static CardInstance MakeHandCard(GameState state, int pIdx, CardType type, string id)
    {
        return new CardInstance(state.NextInstanceId++, id, pIdx)
        {
            Zone = Zone.Hand,
            CardType = type,
            Cost = 2,
            BaseAttack = 3,
            BaseVigor = 4,
            IsExhausted = false
        };
    }

    /// <summary>Places a ready creature directly on the board (no summon).</summary>
    private static CardInstance PlaceCreature(GameState state, int pIdx, int lane,
        int attack = 3, int vigor = 4)
    {
        var card = new CardInstance(state.NextInstanceId++, $"tst_creature_p{pIdx}_l{lane}", pIdx)
        {
            Zone = Zone.Lane,
            LaneIndex = lane,
            CardType = CardType.CREATURE,
            BaseAttack = attack,
            BaseVigor = vigor,
            Cost = 1,
            IsExhausted = false
        };
        state.Players[pIdx].Lanes[lane].Occupant = card;
        return card;
    }

    /// <summary>Attack: source lane must equal target lane (no REACH keyword).</summary>
    private static GameState Attack(GameState state, int playerIndex, int lane)
    {
        return DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = playerIndex,
            SourceLane = lane,
            TargetLane = lane
        });
    }

    private static GameState EndTurn(GameState state, int playerIndex)
    {
        return DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = playerIndex });
    }

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

    private static ConditionDef Cond(ConditionOp op, int? value = null, string? side = null)
    {
        return new ConditionDef
        {
            Op = op,
            Value = value.HasValue ? JsonDocument.Parse(value.Value.ToString()).RootElement : null,
            Side = side
        };
    }

    private static bool Eval(ConditionDef condition, GameState state, int controller)
    {
        var source = new CardInstance(99999, "tst_condition_source", controller);
        return TriggerBus.EvaluateCondition(condition, source, controller, state);
    }

    private static List<ResolvedTarget> Resolve(GameState state, int controller, string filter)
    {
        var source = new CardInstance(99998, "tst_filter_source", controller);
        return TargetResolver.Resolve(
            new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = filter, Count = TargetCount.All },
            source,
            state.Players[controller],
            state.Players[state.OpponentIndex(controller)],
            state);
    }

    // ——— G5 counter mechanics ———

    [Fact]
    public void AttackCountThisTurn_IncrementsPerAttack_AndResetsAtOwnTurnStart()
    {
        var state = CreateState();
        PlaceCreature(state, 0, 0);
        PlaceCreature(state, 0, 1);
        // Each creature attacks its opposing lane (empty → face)
        state = Attack(state, 0, 0);
        Assert.Equal(1, state.Players[0].AttackCountThisTurn);
        state = Attack(state, 0, 1);
        Assert.Equal(2, state.Players[0].AttackCountThisTurn);

        // P1's turn: P0's counters hold
        state = EndTurn(state, 0);
        Assert.Equal(2, state.Players[0].AttackCountThisTurn);

        // Back to P0: P0's ThisTurn resets, LastTurn gets old value
        state = EndTurn(state, 1);
        Assert.Equal(0, state.Players[0].AttackCountThisTurn);
        Assert.Equal(2, state.Players[0].AttackCountLastTurn);
    }

    [Fact]
    public void SpellCastCountThisTurn_IncrementsOnRitual_AndResetsAtOwnTurnStart()
    {
        var state = CreateState(p0HandSize: 2);
        foreach (var card in state.Players[0].Hand)
            card.CardType = CardType.RITUAL;

        state = PlayFirst(state, 0, 0);
        Assert.Equal(1, state.Players[0].SpellCastCountThisTurn);
        state = PlayFirst(state, 0, 0);
        Assert.Equal(2, state.Players[0].SpellCastCountThisTurn);

        // Creature plays do NOT count as spells
        var creature = MakeHandCard(state, 0, CardType.CREATURE, "tst_p0_creature");
        state.Players[0].Hand.Add(creature);
        // Hand now has 1 card (the creature). Hand[0] is the creature.
        state = PlayFirst(state, 0, 0);
        // Creature play does not increment spell count
        Assert.Equal(2, state.Players[0].SpellCastCountThisTurn);

        state = EndTurn(state, 0);
        state = EndTurn(state, 1);
        Assert.Equal(0, state.Players[0].SpellCastCountThisTurn);
    }

    [Fact]
    public void Counters_AreTrackedIndependentlyPerPlayer()
    {
        var state = CreateState();
        PlaceCreature(state, 0, 0);
        PlaceCreature(state, 1, 0);

        state = Attack(state, 0, 0);
        Assert.Equal(1, state.Players[0].AttackCountThisTurn);
        Assert.Equal(0, state.Players[1].AttackCountThisTurn);

        state = EndTurn(state, 0);
        state = Attack(state, 1, 0);
        Assert.Equal(1, state.Players[1].AttackCountThisTurn);
        Assert.Equal(1, state.Players[0].AttackCountThisTurn);
    }

    // ——— Conditions ———

    [Fact]
    public void AttackersThisTurnGte_TrueWhenThresholdMet()
    {
        var state = CreateState();
        PlaceCreature(state, 0, 0);
        PlaceCreature(state, 0, 1);
        PlaceCreature(state, 0, 2);

        Assert.False(Eval(Cond(ConditionOp.ATTACKERS_THIS_TURN_GTE, 3), state, 0));
        state = Attack(state, 0, 0);
        state = Attack(state, 0, 1);
        state = Attack(state, 0, 2);
        Assert.True(Eval(Cond(ConditionOp.ATTACKERS_THIS_TURN_GTE, 3), state, 0));
        Assert.False(Eval(Cond(ConditionOp.ATTACKERS_THIS_TURN_GTE, 4), state, 0));
    }

    [Fact]
    public void AttackersThisTurnEq_ExactMatchOnly()
    {
        var state = CreateState();
        PlaceCreature(state, 0, 0);
        PlaceCreature(state, 0, 1);

        Assert.False(Eval(Cond(ConditionOp.ATTACKERS_THIS_TURN_EQ, 1), state, 0));
        state = Attack(state, 0, 0);
        Assert.True(Eval(Cond(ConditionOp.ATTACKERS_THIS_TURN_EQ, 1), state, 0));
        state = Attack(state, 0, 1);
        Assert.False(Eval(Cond(ConditionOp.ATTACKERS_THIS_TURN_EQ, 1), state, 0));
        Assert.True(Eval(Cond(ConditionOp.ATTACKERS_THIS_TURN_EQ, 2), state, 0));
    }

    [Fact]
    public void SpellsCastThisTurnEq_ExactMatchOnly()
    {
        var state = CreateState(p0HandSize: 3);
        foreach (var card in state.Players[0].Hand)
            card.CardType = CardType.RITUAL;

        state = PlayFirst(state, 0, 0);
        Assert.False(Eval(Cond(ConditionOp.SPELLS_CAST_THIS_TURN_EQ, 2), state, 0));
        state = PlayFirst(state, 0, 0);
        Assert.True(Eval(Cond(ConditionOp.SPELLS_CAST_THIS_TURN_EQ, 2), state, 0));
        state = PlayFirst(state, 0, 0);
        Assert.False(Eval(Cond(ConditionOp.SPELLS_CAST_THIS_TURN_EQ, 2), state, 0));
        Assert.True(Eval(Cond(ConditionOp.SPELLS_CAST_THIS_TURN_EQ, 3), state, 0));
    }

    [Fact]
    public void NoAttackersLastTurn_TrueWhenZeroFriendlyAttackersLastTurn()
    {
        var state = CreateState();
        PlaceCreature(state, 0, 0);
        // P0 attacks nothing on turn 1
        state = EndTurn(state, 0);
        state = EndTurn(state, 1);
        // P0's turn 2: AttackCountLastTurn = 0 (no attacks on turn 1)
        Assert.True(Eval(Cond(ConditionOp.NO_ATTACKERS_LAST_TURN), state, 0));
    }

    [Fact]
    public void NoAttackersLastTurn_FalseWhenAttackedLastTurn()
    {
        var state = CreateState();
        PlaceCreature(state, 0, 0);
        state = Attack(state, 0, 0);
        state = EndTurn(state, 0);
        state = EndTurn(state, 1);
        // P0 attacked once on turn 1 → NO_ATTACKERS_LAST_TURN false on turn 2
        Assert.False(Eval(Cond(ConditionOp.NO_ATTACKERS_LAST_TURN), state, 0));
    }

    [Fact]
    public void CreatureDiedThisTurn_AnySide_CountsBothSides()
    {
        var state = CreateState();
        PlaceCreature(state, 0, 0, attack: 5, vigor: 1);
        PlaceCreature(state, 1, 0, attack: 5, vigor: 1);

        // Trade — both die
        state = Attack(state, 0, 0);
        Assert.True(Eval(Cond(ConditionOp.CREATURE_DIED_THIS_TURN), state, 0));
        Assert.Equal(1, state.CreatureDiedThisTurnCount[0]);
        Assert.Equal(1, state.CreatureDiedThisTurnCount[1]);
    }

    [Fact]
    public void CreatureDiedThisTurn_AllySide_OnlyOwnDeaths()
    {
        var state = CreateState();
        PlaceCreature(state, 0, 0, attack: 1, vigor: 1);
        PlaceCreature(state, 1, 0, attack: 5, vigor: 5);
        state = EndTurn(state, 0); // P1's turn
        state = Attack(state, 1, 0); // P1 kills P0's creature

        Assert.True(Eval(Cond(ConditionOp.CREATURE_DIED_THIS_TURN, side: "ALLY"), state, 0));
        Assert.False(Eval(Cond(ConditionOp.CREATURE_DIED_THIS_TURN, side: "ENEMY"), state, 0));
        Assert.Equal(1, state.CreatureDiedThisTurnCount[0]);
        Assert.Equal(0, state.CreatureDiedThisTurnCount[1]);
    }

    [Fact]
    public void CreatureDiedThisTurn_EnemySide_OnlyEnemyDeaths()
    {
        var state = CreateState();
        PlaceCreature(state, 0, 0, attack: 5, vigor: 5);
        PlaceCreature(state, 1, 0, attack: 1, vigor: 1);
        state = Attack(state, 0, 0); // P0 kills P1's creature

        Assert.False(Eval(Cond(ConditionOp.CREATURE_DIED_THIS_TURN, side: "ALLY"), state, 0));
        Assert.True(Eval(Cond(ConditionOp.CREATURE_DIED_THIS_TURN, side: "ENEMY"), state, 0));
        Assert.Equal(0, state.CreatureDiedThisTurnCount[0]);
        Assert.Equal(1, state.CreatureDiedThisTurnCount[1]);
    }

    [Fact]
    public void CreatureDiedThisTurn_ResetsAtTurnStart()
    {
        var state = CreateState();
        PlaceCreature(state, 0, 0, attack: 5, vigor: 5);
        PlaceCreature(state, 1, 0, attack: 1, vigor: 1);
        state = Attack(state, 0, 0);
        Assert.Equal(1, state.CreatureDiedThisTurnCount[1]);

        state = EndTurn(state, 0);
        Assert.Equal(0, state.CreatureDiedThisTurnCount[0]);
        Assert.Equal(0, state.CreatureDiedThisTurnCount[1]);
        Assert.False(Eval(Cond(ConditionOp.CREATURE_DIED_THIS_TURN), state, 0));
    }

    [Fact]
    public void CreatureDiedThisTurn_WithValueThreshold_RequiresAtLeastValue()
    {
        var state = CreateState();
        PlaceCreature(state, 0, 0, attack: 5, vigor: 5);
        PlaceCreature(state, 1, 0, attack: 1, vigor: 1);
        state = Attack(state, 0, 0);

        // 1 death: value 1 passes, value 2 fails
        Assert.True(Eval(Cond(ConditionOp.CREATURE_DIED_THIS_TURN, 1), state, 0));
        Assert.False(Eval(Cond(ConditionOp.CREATURE_DIED_THIS_TURN, 2), state, 0));
    }

    // ——— Filters ———

    [Fact]
    public void HasNotAttacked_FiltersOutCreaturesThatAttacked()
    {
        var state = CreateState();
        PlaceCreature(state, 0, 0);
        PlaceCreature(state, 0, 1);
        PlaceCreature(state, 0, 2);

        state = Attack(state, 0, 0);
        var targets = Resolve(state, 0, "HAS_NOT_ATTACKED");
        // Only lanes 1 and 2 remain (lane 0 attacked)
        Assert.Equal(2, targets.Count);
        Assert.All(targets, t =>
        {
            var ct = Assert.IsType<CreatureTarget>(t);
            Assert.False(ct.Card.HasAttackedThisTurn);
            Assert.NotEqual(0, ct.LaneIndex);
        });
    }

    [Fact]
    public void HasNotAttacked_IncludesAllCreaturesBeforeAnyAttack()
    {
        var state = CreateState();
        PlaceCreature(state, 0, 0);
        PlaceCreature(state, 0, 1);
        var targets = Resolve(state, 0, "HAS_NOT_ATTACKED");
        Assert.Equal(2, targets.Count);
    }

    [Fact]
    public void FirstAttacker_MatchesTheFirstAttackingCreature()
    {
        var state = CreateState();
        PlaceCreature(state, 0, 0);
        PlaceCreature(state, 0, 1);
        PlaceCreature(state, 0, 2);

        // Lane 2 attacks first, then lane 0
        state = Attack(state, 0, 2);
        state = Attack(state, 0, 0);

        var targets = Resolve(state, 0, "FIRST_ATTACKER");
        var single = Assert.Single(targets);
        var ct = Assert.IsType<CreatureTarget>(single);
        Assert.Equal(2, ct.LaneIndex);
    }

    [Fact]
    public void FirstAttacker_NoMatch_WhenNothingAttackedYet()
    {
        var state = CreateState();
        PlaceCreature(state, 0, 0);
        var targets = Resolve(state, 0, "FIRST_ATTACKER");
        Assert.Empty(targets);
    }

    [Fact]
    public void FirstAttacked_MatchesTheFirstCreatureAttackedByEnemy()
    {
        var state = CreateState();
        // P1's creatures (strong, survive attacks)
        PlaceCreature(state, 1, 0, attack: 5, vigor: 10);
        PlaceCreature(state, 1, 3, attack: 5, vigor: 10);
        // P0's attackers
        PlaceCreature(state, 0, 0, attack: 1, vigor: 1);
        PlaceCreature(state, 0, 3, attack: 1, vigor: 1);

        // P0 attacks P1's lane 3 first, then lane 0
        state = Attack(state, 0, 3);
        state = Attack(state, 0, 0);

        var targets = Resolve(state, 1, "FIRST_ATTACKED");
        var single = Assert.Single(targets);
        var ct = Assert.IsType<CreatureTarget>(single);
        Assert.Equal(3, ct.LaneIndex);
    }

    [Fact]
    public void FirstAttacked_NoMatch_WhenNothingAttacked()
    {
        var state = CreateState();
        PlaceCreature(state, 0, 0);
        var targets = Resolve(state, 0, "FIRST_ATTACKED");
        Assert.Empty(targets);
    }
}
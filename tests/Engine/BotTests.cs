using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Engine.Cards;
using Runewake.Sim;
using Xunit;

namespace Runewake.Tests.Engine;

public class BotTests
{
    /// <summary>
    /// Creates a bare GameState with no deck, no hand, just empty lanes and 25 vigor each.
    /// Useful for testing score evaluation.
    /// </summary>
    private static GameState CreateEmptyState(ulong seed = 42)
    {
        var state = new GameState(seed);
        // No decks, no hands — just basic state
        return state;
    }

    /// <summary>
    /// Places a creature on the given player's lane.
    /// </summary>
    private static CardInstance PlaceCreature(PlayerState player, int laneIdx, int atk, int vig, int instanceId)
    {
        var card = new CardInstance(instanceId, "tst_test", player.Index)
        {
            CardType = CardType.CREATURE,
            BaseAttack = atk,
            BaseVigor = vig,
            Zone = Zone.Lane,
            LaneIndex = laneIdx,
            IsExhausted = false,
        };
        player.Lanes[laneIdx].Occupant = card;
        return card;
    }

    // ——— Evaluate ———

    [Fact]
    public void Evaluate_EmptyBoard_ReturnsVigorDifference()
    {
        var state = CreateEmptyState();
        var bot = new GreedyBot();

        // Both at 25 vigor, no creatures → 25 - 25 = 0
        int score = bot.Evaluate(state, playerIndex: 0);
        Assert.Equal(0, score);
    }

    [Fact]
    public void Evaluate_AllyHasCreature_ReturnsPositiveScore()
    {
        var state = CreateEmptyState();
        var p0 = state.Players[0];
        PlaceCreature(p0, laneIdx: 0, atk: 3, vig: 4, instanceId: 100);

        var bot = new GreedyBot();
        int score = bot.Evaluate(state, playerIndex: 0);

        // Ally: 3 atk + 4 vig = +7, Enemy: nothing = 0. Vigor diff: 25-25=0. Total: 7
        Assert.Equal(7, score);
    }

    [Fact]
    public void Evaluate_EnemyHasStrongerCreature_ReturnsNegativeScore()
    {
        var state = CreateEmptyState();
        var p0 = state.Players[0];
        var p1 = state.Players[1];
        PlaceCreature(p0, laneIdx: 0, atk: 2, vig: 2, instanceId: 100);
        PlaceCreature(p1, laneIdx: 2, atk: 5, vig: 5, instanceId: 200);

        var bot = new GreedyBot();
        int score = bot.Evaluate(state, playerIndex: 0);

        // Ally: 2+2=4, Enemy: 5+5=10. Vigor: 25-25=0. Total: 4 - 10 = -6
        Assert.Equal(-6, score);
    }

    // ——— EnumerateValidActions ———

    [Fact]
    public void EnumerateActions_EmptyBoardNoHand_OnlyEndTurn()
    {
        var state = CreateEmptyState();
        var bot = new GreedyBot();
        var actions = bot.EnumerateValidActions(state, playerIndex: 0);

        Assert.Single(actions);
        Assert.IsType<EndTurnAction>(actions[0]);
    }

    [Fact]
    public void EnumerateActions_CardsInHand_IncludesPlayActions()
    {
        var state = CreateEmptyState();
        var p0 = state.Players[0];
        p0.Attunement = 3;

        var card = new CardInstance(42, "tst_creature", 0)
        {
            CardType = CardType.CREATURE,
            Cost = 2,
            Zone = Zone.Hand,
        };
        p0.Hand.Add(card);

        var bot = new GreedyBot();
        var actions = bot.EnumerateValidActions(state, playerIndex: 0);

        // Should include 5 play actions (one per empty lane) + 1 end turn
        Assert.Equal(6, actions.Count);
        Assert.Contains(actions, a => a is PlayCardAction);
        Assert.Contains(actions, a => a is EndTurnAction);
    }

    // ——— ChooseAction ———

    [Fact]
    public void ChooseAction_PrefersPlayingCreatureOverEndTurn_WhenHandHasPlayableCard()
    {
        var state = CreateEmptyState();
        var p0 = state.Players[0];
        p0.Attunement = 3;

        var card = new CardInstance(42, "tst_creature", 0)
        {
            CardType = CardType.CREATURE,
            Cost = 2,
            BaseAttack = 4,
            BaseVigor = 4,
            Zone = Zone.Hand,
        };
        p0.Hand.Add(card);

        var bot = new GreedyBot();
        var action = bot.ChooseAction(state, playerIndex: 0);

        // Playing the 4/4 creature gives score boost, so bot should play it
        Assert.IsType<PlayCardAction>(action);
    }

    [Fact]
    public void ChooseAction_PrefersAttackingOverEndTurn_WhenCreatureIsReady()
    {
        var state = CreateEmptyState();
        var p0 = state.Players[0];
        var p1 = state.Players[1];

        // Place a ready 5/5 for P0, an empty lane for P1 (face damage better than nothing)
        PlaceCreature(p0, laneIdx: 0, atk: 5, vig: 5, instanceId: 100);
        // No enemy creatures in any lane

        var bot = new GreedyBot();
        var action = bot.ChooseAction(state, playerIndex: 0);

        // Attacking with a 5/5 against empty lane deals 5 face damage, gaining +5 in score.
        // Should prefer attack over end turn.
        Assert.IsType<AttackAction>(action);
    }

    [Fact]
    public void ChooseAction_PrefersAttackingGuardOverEndTurn_WhenCreatureIsReady()
    {
        var state = CreateEmptyState();
        var p0 = state.Players[0];
        var p1 = state.Players[1];

        // P0: ready 5/5
        PlaceCreature(p0, laneIdx: 0, atk: 5, vig: 5, instanceId: 100);
        // P1: 3/3 Guard — blocks face, bot must attack it
        var guard = PlaceCreature(p1, laneIdx: 2, atk: 3, vig: 3, instanceId: 200);
        guard.Keywords.Add("GUARD");

        var bot = new GreedyBot();
        var action = bot.ChooseAction(state, playerIndex: 0);

        // Must pick an attack (not EndTurn) because attacking improves score
        Assert.IsType<AttackAction>(action);
        var atk = (AttackAction)action;
        Assert.Equal(0, atk.SourceLane);
        // The bot should prefer attacking (any valid target) over ending turn
        // Guard redirect means any target lane hits the Guard
    }
}
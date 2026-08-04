using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Engine.Cards;

namespace Runewake.Sim;

/// <summary>
/// A greedy heuristic bot that evaluates each legal action one ply deep.
/// Score = own board stats + vigor − enemy board stats − enemy vigor.
/// Picks the action with the highest score.
/// </summary>
public class GreedyBot
{
    /// <summary>
    /// Chooses the best action for the current player by scoring the resulting
    /// state after each legal action. Returns null if no actions are available
    /// (should not happen — EndTurn is always legal).
    /// </summary>
    public GameAction? ChooseAction(GameState state, int playerIndex)
    {
        var actions = EnumerateValidActions(state, playerIndex);
        if (actions.Count == 0)
            return null;

        GameAction bestAction = actions[0];
        int bestScore = int.MinValue;

        foreach (var action in actions)
        {
            // Clone and simulate
            GameState next;
            try
            {
                next = DuelEngine.Apply(state, action);
            }
            catch
            {
                // Skip invalid actions
                continue;
            }

            int score = Evaluate(next, playerIndex);
            if (score > bestScore)
            {
                bestScore = score;
                bestAction = action;
            }
        }

        return bestAction;
    }

    /// <summary>
    /// Evaluates a game state from the perspective of <paramref name="playerIndex"/>.
    /// Higher score = better for the player.
    /// </summary>
    public int Evaluate(GameState state, int playerIndex)
    {
        var me = state.Player(playerIndex);
        var enemy = state.Player(state.OpponentIndex(playerIndex));

        int allyScore = me.Vigor;
        int enemyScore = enemy.Vigor;

        for (int i = 0; i < 5; i++)
        {
            // Ally creatures
            var allyCreature = me.Lanes[i].Occupant;
            if (allyCreature is not null && allyCreature.CurrentAttack > 0)
            {
                allyScore += allyCreature.CurrentAttack + allyCreature.CurrentVigor;
            }
            // Also count 0-attack creatures (relics, debuffed creatures) — their vigor still matters
            else if (allyCreature is not null)
            {
                allyScore += allyCreature.CurrentVigor;
            }

            // Enemy creatures
            var enemyCreature = enemy.Lanes[i].Occupant;
            if (enemyCreature is not null && enemyCreature.CurrentAttack > 0)
            {
                enemyScore += enemyCreature.CurrentAttack + enemyCreature.CurrentVigor;
            }
            else if (enemyCreature is not null)
            {
                enemyScore += enemyCreature.CurrentVigor;
            }
        }

        return allyScore - enemyScore;
    }

    /// <summary>
    /// Enumerates all legal actions the current player can take: play cards,
    /// attack with ready creatures, and end turn.
    /// </summary>
    public List<GameAction> EnumerateValidActions(GameState state, int playerIndex)
    {
        var actions = new List<GameAction>();
        var player = state.Player(playerIndex);
        var opponent = state.Player(state.OpponentIndex(playerIndex));

        // 1. Play card actions
        foreach (var card in player.Hand)
        {
            if (card.Cost <= player.Attunement)
            {
                if (card.CardType == CardType.CREATURE ||
                    card.CardType == CardType.RELIC)
                {
                    for (int l = 0; l < 5; l++)
                    {
                        if (player.Lanes[l].Occupant is null)
                        {
                            actions.Add(new PlayCardAction
                            {
                                PlayerIndex = playerIndex,
                                CardInstanceId = card.InstanceId,
                                Cost = card.Cost,
                                LaneIndex = l,
                            });
                        }
                    }
                }
                else
                {
                    // RITUAL — no lane target needed
                    actions.Add(new PlayCardAction
                    {
                        PlayerIndex = playerIndex,
                        CardInstanceId = card.InstanceId,
                        Cost = card.Cost,
                        LaneIndex = null,
                    });
                }
            }
        }

        // 2. Attack actions — ready creatures with any attack target
        for (int l = 0; l < 5; l++)
        {
            var occ = player.Lanes[l].Occupant;
            if (occ is not null && !occ.IsExhausted && !occ.HasAttackedThisTurn && occ.CurrentAttack > 0)
            {
                for (int tl = 0; tl < 5; tl++)
                {
                    actions.Add(new AttackAction
                    {
                        PlayerIndex = playerIndex,
                        SourceLane = l,
                        TargetLane = tl,
                    });
                }
            }
        }

        // 3. End turn — always legal
        if (!state.IsGameOver)
        {
            actions.Add(new EndTurnAction
            {
                PlayerIndex = playerIndex,
            });
        }

        return actions;
    }
}
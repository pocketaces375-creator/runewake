using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Engine.Cards;

namespace Runewake.Sim;

/// <summary>
/// Minimal interface for a bot that chooses the next action in a duel.
/// </summary>
public interface IGameBot
{
    /// <summary>Choose the best action for the current player, or null if none.</summary>
    GameAction? ChooseAction(GameState state, int playerIndex);
}

/// <summary>
/// A greedy heuristic bot that evaluates each legal action one ply deep.
/// Score = own board stats + vigor − enemy board stats − enemy vigor.
/// Picks the action with the highest score.
/// </summary>
public class GreedyBot : IGameBot
{
    /// <summary>
    /// Chooses the best action for the current player by scoring the resulting
    /// state after each legal action. Returns null if no actions are available
    /// (should not happen — EndTurn is always legal).
    /// TASK-FUN-SIM-1: Also evaluates TapArtifactAction for INVOKE mode.
    /// </summary>
    public GameAction? ChooseAction(GameState state, int playerIndex)
    {
        // 1. Best card play, one ply deep — must strictly beat doing nothing.
        var actions = EnumerateValidActions(state, playerIndex);
        if (actions.Count == 0)
            return null;

        int baseline = Evaluate(state, playerIndex);
        GameAction? bestPlay = null;
        int bestPlayScore = baseline;
        foreach (var action in actions)
        {
            if (action is not PlayCardAction && action is not TapArtifactAction) continue;
            GameState next;
            try { next = DuelEngine.Apply(state, action); }
            catch { continue; }
            int score = Evaluate(next, playerIndex);
            if (score > bestPlayScore)
            {
                bestPlayScore = score;
                bestPlay = action;
            }
        }
        if (bestPlay != null)
            return bestPlay;

        // 2. BOT-FIX-1: attack via turn-level planning. The old one-ply greedy
        // rejected every individually-unfavorable attack, so it could never see
        // that three 1/1s kill a 4/3 — with a full enemy board it went fully
        // passive. The planner evaluates whole missions (gang-kills, face rush)
        // and returns the first attack of the best one; it is recomputed from
        // the live state before every action, so multi-attack plans converge.
        var attack = PlanBestAttack(state, playerIndex);
        if (attack != null)
            return attack;

        // 3. Nothing worth doing — end turn.
        return actions.FirstOrDefault(a => a is EndTurnAction)
            ?? new EndTurnAction { PlayerIndex = playerIndex };
    }

    /// <summary>
    /// BOT-FIX-1: pick the best attack under lane-locked combat rules.
    /// Without REACH a creature may only attack its directly opposing lane;
    /// REACH adds the two neighbors. Attacking an empty lane hits the face
    /// unless the enemy has a Guard anywhere (empty-lane attacks redirect
    /// into the Guard). Each ready attacker's legal options are scored:
    ///   face      = attack damage (lethal check first),
    ///   creature  = damage dealt + removal bonus if it dies, minus own loss
    ///               to the simultaneous strike-back.
    /// Returns the single best non-negative option, else null (hold).
    /// </summary>
    public AttackAction? PlanBestAttack(GameState state, int playerIndex)
    {
        var me = state.Player(playerIndex);
        var enemy = state.Player(state.OpponentIndex(playerIndex));

        var ready = new List<(int lane, CardInstance c)>();
        for (int l = 0; l < 5; l++)
        {
            var occ = me.Lanes[l].Occupant;
            if (occ is not null && !occ.IsExhausted && !occ.HasAttackedThisTurn
                && occ.CurrentAttack > 0 && KeywordHandlers.CanAttack(occ))
                ready.Add((l, occ));
        }
        if (ready.Count == 0)
            return null;

        int? guardLane = null;
        for (int l = 0; l < 5; l++)
        {
            var occ = enemy.Lanes[l].Occupant;
            if (occ is not null && occ.EffectiveKeywords.Contains("GUARD")) { guardLane = l; break; }
        }

        // Lethal check: total face damage through open lanes (no Guard in the way).
        if (guardLane is null)
        {
            int faceDamage = 0;
            foreach (var (lane, c) in ready)
                if (LegalTargets(c, lane).Any(t => enemy.Lanes[t].Occupant is null))
                    faceDamage += c.CurrentAttack;
            if (faceDamage >= enemy.Vigor)
            {
                foreach (var (lane, c) in ready)
                {
                    int face = LegalTargets(c, lane).FirstOrDefault(t => enemy.Lanes[t].Occupant is null, -1);
                    if (face >= 0)
                        return new AttackAction { PlayerIndex = playerIndex, SourceLane = lane, TargetLane = face };
                }
            }
        }

        AttackAction? best = null;
        int bestScore = 0; // neutral-or-better only; worse than that we hold
        foreach (var (lane, c) in ready)
        {
            foreach (int t in LegalTargets(c, lane))
            {
                var d = enemy.Lanes[t].Occupant;
                // Empty lane redirects into the Guard when one exists.
                if (d is null && guardLane is int g) d = enemy.Lanes[g].Occupant;

                int score;
                if (d is null)
                {
                    score = c.CurrentAttack; // clean face hit
                }
                else
                {
                    bool kills = c.CurrentAttack >= d.CurrentVigor;
                    bool dies = d.CurrentAttack >= c.CurrentVigor;
                    score = System.Math.Min(c.CurrentAttack, d.CurrentVigor)
                          + (kills ? d.CurrentAttack + 1 : 0)      // removal is tempo
                          - (dies ? c.CurrentAttack + c.CurrentVigor : 0);
                }

                if (score > bestScore || (best is null && score == bestScore && d is not null && c.CurrentAttack >= d.CurrentVigor))
                {
                    bestScore = score;
                    best = new AttackAction { PlayerIndex = playerIndex, SourceLane = lane, TargetLane = t };
                }
            }
        }
        return best;
    }

    /// <summary>Legal attack target lanes for an attacker under lane-locked rules.</summary>
    internal static IEnumerable<int> LegalTargets(CardInstance attacker, int sourceLane)
    {
        if (attacker.EffectiveKeywords.Contains("REACH"))
        {
            for (int t = System.Math.Max(0, sourceLane - 1); t <= System.Math.Min(4, sourceLane + 1); t++)
                yield return t;
        }
        else
        {
            yield return sourceLane;
        }
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
            // COST_MOD discounts (the discount mechanic) reduce effective cost.
            int effectiveCost = CostInterceptor.GetEffectiveCost(state, card, playerIndex);

            if (effectiveCost <= player.Attunement)
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
                                Cost = effectiveCost,
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
                        Cost = effectiveCost,
                        LaneIndex = null,
                    });
                }
            }
        }

        // 2. Attack actions — ready creatures, lane-locked targets only (BOT-FIX-1)
        for (int l = 0; l < 5; l++)
        {
            var occ = player.Lanes[l].Occupant;
            if (occ is not null && !occ.IsExhausted && !occ.HasAttackedThisTurn
                && occ.CurrentAttack > 0 && KeywordHandlers.CanAttack(occ))
            {
                foreach (int tl in LegalTargets(occ, l))
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

        // 4. TASK-FUN-SIM-1: Tap artifact actions (INVOKE mode)
        if (state.InvokeMode)
        {
            var curPlayer = state.Player(playerIndex);
            for (int s = 0; s < curPlayer.ArtifactSlots.Length; s++)
            {
                if (curPlayer.ArtifactSlots[s].HasHeldChargeFull)
                {
                    actions.Add(new TapArtifactAction
                    {
                        PlayerIndex = playerIndex,
                        SlotIndex = s,
                    });
                }
            }
        }

        return actions;
    }
}
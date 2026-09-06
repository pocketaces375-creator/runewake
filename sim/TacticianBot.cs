using System;
using System.Collections.Generic;
using System.Linq;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Engine.Cards;

namespace Runewake.Sim;

/// <summary>
/// TacticianBot: a smarter one-ply bot that:
///   - Uses the same action structure as GreedyBot
///   - Has a richer board evaluation: keywords, lethal weighting, hand value
///   - Has smarter attack targeting: favors efficient trades
///   - Knows when to hold cards instead of dumping into a Guard wall
///   - Respects Guard lanes properly
///   - Values artifact charge progress
///
/// The key difference from GreedyBot: the Evaluate function includes card hand
/// value and keyword-aware creature valuation, which changes card-play ordering.
/// The attack planner rewards favorable trades more and penalizes suicide less
/// when the trade is meaningfully efficient.
/// </summary>
public class TacticianBot : IGameBot
{
    private readonly GreedyBot _greedy = new();

    public List<GameAction> EnumerateValidActions(GameState state, int playerIndex)
        => _greedy.EnumerateValidActions(state, playerIndex);

    private static IEnumerable<int> LegalTargets(CardInstance attacker, int sourceLane)
        => GreedyBot.LegalTargets(attacker, sourceLane);

    // ——— ChooseAction ——— //

    public GameAction? ChooseAction(GameState state, int playerIndex)
    {
        var actions = EnumerateValidActions(state, playerIndex);
        if (actions.Count == 0)
            return null;

        int baseline = Evaluate(state, playerIndex);
        GameAction? bestPlay = null;
        int bestPlayScore = baseline;

        // 1. Card play / artifact tap
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

        // 2. Attack
        var attack = PlanBestAttack(state, playerIndex);
        if (attack != null)
            return attack;

        // 3. End turn
        return actions.FirstOrDefault(a => a is EndTurnAction)
            ?? new EndTurnAction { PlayerIndex = playerIndex };
    }

    // ——— Attack Planning ——— //

    public AttackAction? PlanBestAttack(GameState state, int playerIndex)
    {
        var me = state.Player(playerIndex);
        var enemy = state.Player(state.OpponentIndex(playerIndex));

        // Ready attackers
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

        // Guard lane
        int? guardLane = null;
        for (int l = 0; l < 5; l++)
        {
            var occ = enemy.Lanes[l].Occupant;
            if (occ is not null && occ.EffectiveKeywords.Contains("GUARD"))
            { guardLane = l; break; }
        }

        int enemyVigor = enemy.Vigor;

        // --- Lethal first ---
        if (guardLane is null)
        {
            int faceDamage = 0;
            foreach (var (lane, c) in ready)
                if (LegalTargets(c, lane).Any(t => enemy.Lanes[t].Occupant is null))
                    faceDamage += c.CurrentAttack;
            if (faceDamage >= enemyVigor)
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
        int bestScore = 0;

        foreach (var (lane, c) in ready)
        {
            foreach (int t in LegalTargets(c, lane))
            {
                var d = enemy.Lanes[t].Occupant;
                if (d is null && guardLane is int g)
                    d = enemy.Lanes[g].Occupant;

                int score;
                if (d is null)
                {
                    // Face hit
                    score = c.CurrentAttack; // same as GreedyBot
                    // Bonus for finishing
                    if (enemyVigor - c.CurrentAttack <= 0)
                        score += 50;
                    else if (enemyVigor - c.CurrentAttack <= 5)
                        score += 5;
                }
                else
                {
                    // Combat trade
                    bool kills = c.CurrentAttack >= d.CurrentVigor;
                    bool dies = d.CurrentAttack >= c.CurrentVigor;

                    // GreedyBot's base formula:
                    int dmgDealt = Math.Min(c.CurrentAttack, d.CurrentVigor);
                    int greedyScore = dmgDealt
                        + (kills ? d.CurrentAttack + 1 : 0)
                        - (dies ? c.CurrentAttack + c.CurrentVigor : 0);

                    if (kills && !dies)
                    {
                        // Favorable trade — bonus for efficiency
                        score = greedyScore + CreatureValue(d) + 3;
                        // Guard clear bonus
                        if (guardLane == t)
                            score += 20;
                    }
                    else if (kills && dies)
                    {
                        // Mutual kill — net value
                        int net = CreatureValue(d) - CreatureValue(c);
                        score = greedyScore + net;
                        if (guardLane == t)
                            score += 15;
                        // Floor: at least as good as doing nothing
                        if (score < 0 && net > 0)
                            score = 0; // still acceptable if we remove a better creature
                    }
                    else if (!kills && dies)
                    {
                        // Suicidal — only for lethal Guard clear
                        if (guardLane == t)
                        {
                            int remaining = 0;
                            foreach (var (rl, rc) in ready)
                                if (rc.InstanceId != c.InstanceId)
                                    remaining += rc.CurrentAttack;
                            bool otherGuard = false;
                            for (int l2 = 0; l2 < 5; l2++)
                            {
                                var o = enemy.Lanes[l2].Occupant;
                                if (o is not null && o.InstanceId != d.InstanceId
                                    && o.EffectiveKeywords.Contains("GUARD"))
                                { otherGuard = true; break; }
                            }
                            bool clearsLethal = !otherGuard && remaining >= enemyVigor;
                            if (clearsLethal)
                                score = 50;
                            else
                                score = int.MinValue + 1;
                        }
                        else
                        {
                            score = int.MinValue + 1;
                        }
                    }
                    else
                    {
                        // Neither dies — partial damage trade
                        score = dmgDealt - Math.Min(d.CurrentAttack, c.CurrentVigor);
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = new AttackAction
                    {
                        PlayerIndex = playerIndex,
                        SourceLane = lane,
                        TargetLane = t,
                    };
                }
            }
        }

        return best;
    }

    // ——— Evaluate ——— //

    public int Evaluate(GameState state, int playerIndex)
    {
        var me = state.Player(playerIndex);
        var enemy = state.Player(state.OpponentIndex(playerIndex));

        int allyScore = me.Vigor;
        int enemyScore = enemy.Vigor;

        // Lethal-weight: bonus for winning
        if (enemy.Vigor <= 0) allyScore += 9999;
        if (me.Vigor <= 0) enemyScore += 9999;

        for (int i = 0; i < 5; i++)
        {
            var ally = me.Lanes[i].Occupant;
            if (ally is not null)
                allyScore += CreatureValue(ally);

            var en = enemy.Lanes[i].Occupant;
            if (en is not null)
                enemyScore += CreatureValue(en);
        }

        // Hand value — holding is better than dumping into Guard
        int affordable = 0;
        foreach (var card in me.Hand)
        {
            int ec = CostInterceptor.GetEffectiveCost(state, card, playerIndex);
            if (ec <= me.Attunement && card.CardType is CardType.CREATURE or CardType.RELIC)
                affordable++;
        }
        bool enemyHasGuard = false;
        for (int l = 0; l < 5; l++)
        {
            var occ = enemy.Lanes[l].Occupant;
            if (occ is not null && occ.EffectiveKeywords.Contains("GUARD"))
            { enemyHasGuard = true; break; }
        }
        if (enemyHasGuard && affordable > 0)
            allyScore += affordable * 2;

        // Artifact charges
        for (int s = 0; s < me.ArtifactSlots.Length; s++)
            if (me.ArtifactSlots[s].Occupant is not null && me.ArtifactSlots[s].MaxCharges > 0)
                allyScore += me.ArtifactSlots[s].Charges * 2;
        for (int s = 0; s < enemy.ArtifactSlots.Length; s++)
            if (enemy.ArtifactSlots[s].Occupant is not null && enemy.ArtifactSlots[s].MaxCharges > 0)
                enemyScore += enemy.ArtifactSlots[s].Charges * 2;

        return allyScore - enemyScore;
    }

    // ——— Helpers ——— //

    private static int CreatureValue(CardInstance c)
    {
        int val = c.CurrentAttack + c.CurrentVigor;
        if (c.EffectiveKeywords.Contains("GUARD"))   val += 3;
        if (c.EffectiveKeywords.Contains("SWIFT"))   val += 2;
        if (c.EffectiveKeywords.Contains("REACH"))   val += 1;
        if (c.EffectiveKeywords.Contains("WARD"))    val += 2;
        if (c.EffectiveKeywords.Contains("FRAGILE")) val -= 1;
        if (c.EffectiveKeywords.Contains("ROOTED"))  val -= 1;
        return val;
    }
}
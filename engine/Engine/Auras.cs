using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Engine.Engine;

/// <summary>
/// FABLE-DROP-1: continuous effects. A PASSIVE ability on a creature (or an identified relic) on the
/// board is an aura: "your Swift creatures have +1 Attack", "enemy creatures have -1 Attack". Before
/// this, creature and relic PASSIVE abilities did nothing at all.
///
/// Auras are never stored as permanent changes. After every action the engine clears every creature's
/// aura fields and recomputes them from the sources that are on the board right now, so an aura ends the
/// moment its source leaves. Losing an aura's Vigor never kills (the creature keeps at least 1 Vigor);
/// an aura that takes Vigor away (a penalty) can.
/// </summary>
public static class Auras
{
    public static void Recompute(GameState state)
    {
        for (int round = 0; round < 4; round++)
        {
            RecomputeOnce(state);
            // an aura PENALTY can kill; killing changes the board, which can change the auras
            var dead = new List<CardInstance>();
            foreach (var p in state.Players)
                for (int i = 0; i < 5; i++)
                {
                    var c = p.Lanes[i].Occupant;
                    if (c is null || c.CardType == CardType.RELIC || c.CurrentVigor > 0) continue;
                    if (c.AuraVigor < 0) dead.Add(c);
                    else c.Damage = Math.Max(0, c.MaxVigorNow - 1);   // a lost aura bonus never kills
                }
            if (dead.Count == 0 || state.IsGameOver) return;
            foreach (var c in dead)
                EffectExecutor.KillCreature(c, state);
        }
    }

    private static void RecomputeOnce(GameState state)
    {
        foreach (var p in state.Players)
            for (int i = 0; i < 5; i++)
            {
                var c = p.Lanes[i].Occupant;
                if (c is null) continue;
                c.AuraAttack = 0;
                c.AuraVigor = 0;
                c.AuraKeywords.Clear();
            }

        var sources = new List<(CardInstance src, int ctl, AbilityDef ability)>();
        foreach (var p in state.Players)
            for (int i = 0; i < 5; i++)
            {
                var c = p.Lanes[i].Occupant;
                if (c is null) continue;
                if (c.CardType == CardType.RELIC && !c.IsIdentified) continue;
                foreach (var a in c.Abilities)
                    if (a.Trigger == Trigger.PASSIVE && TriggerBus.EvaluateCondition(a.Condition, c, p.Index, state))
                        sources.Add((c, p.Index, a));
            }

        // keywords first, so "your Swift creatures get +1 Attack" sees a Swift granted by another aura
        foreach (bool keywordPass in new[] { true, false })
            foreach (var (src, ctl, ability) in sources)
                foreach (var e in ability.Effects)
                {
                    bool isKey = e.Op == Op.GRANT_KEY;
                    if (isKey != keywordPass) continue;
                    if (!isKey && e.Op != Op.BUFF && e.Op != Op.DEBUFF) continue;
                    var targets = TargetResolver.Resolve(e.Target ?? new TargetDef { Scope = Scope.NONE }, src,
                        state.Player(ctl), state.Player(state.OpponentIndex(ctl)), state);
                    foreach (var t in targets)
                    {
                        if (t is not CreatureTarget ct) continue;
                        if (isKey)
                        {
                            if (!string.IsNullOrEmpty(e.Keyword)) ct.Card.AuraKeywords.Add(e.Keyword.ToUpperInvariant());
                            continue;
                        }
                        int sign = e.Op == Op.DEBUFF ? -1 : 1;
                        // DEBUFF amounts are a size: "attack": -1 and "attack": 1 both mean -1
                        ct.Card.AuraAttack += sign * (e.Op == Op.DEBUFF ? Math.Abs(e.Attack ?? 0) : e.Attack ?? 0);
                        ct.Card.AuraVigor += sign * (e.Op == Op.DEBUFF ? Math.Abs(e.Vigor ?? 0) : e.Vigor ?? 0);
                    }
                }

    }
}

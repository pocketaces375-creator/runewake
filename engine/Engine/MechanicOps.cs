using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Engine.Engine;

/// <summary>
/// FABLE-DROP-1: the class mechanics (docs/CLASS_IDENTITY.md). Everything here is deterministic — the only
/// randomness (Dodge, random discard) draws from the seeded <see cref="GameState.Rng"/>, so online play and
/// co-op, which compare state hashes, stay in lockstep.
///
///   Keywords   ARMOR:N  damage dealt to this is reduced by N (every hit)
///              DODGE:N  N% chance to take no combat damage
///              EXALTED  your first attacker each turn gets +1/+1 this turn for each Exalted you control
///   Ops        STUN     can't attack; wears off at the end of its controller's next turn
///              BURN     N damage at the start of its controller's turn, then Burn drops by 1 (creature or player)
///              DRAIN    the enemy has N less Attunement at the start of their next turn
///              LOCK_LANE  nothing can be summoned into that enemy lane for N of their turns
///              SWAP_STATS   Attack and Vigor trade places
///              DOUBLE_STATS doubles Attack and Vigor (this turn, unless PERMANENT)
///              STEAL    take an enemy creature into one of your empty lanes (THIS_TURN: it can attack, and
///                       goes back at the end of the turn)
///              SET_TRAP a face-down Sigil: COUNTER_RITUAL (negate the next enemy ritual) or AMBUSH N
///                       (N damage to the next enemy creature that attacks, before it strikes)
///              REDIRECT the next enemy attack this side takes hits this creature instead
///   Card rules Tribute N, creature types (tribal) — DuelEngine / TargetResolver
/// </summary>
public static class MechanicOps
{
    public static readonly string[] NewKeywords = { "ARMOR", "DODGE", "EXALTED" };

    /// <summary>The N of a parametrized keyword ("ARMOR:2" → 2; several sources add up).</summary>
    public static int KeywordValue(CardInstance card, string keyword)
    {
        int total = 0;
        foreach (var k in card.EffectiveKeywords)
            if (k.StartsWith(keyword + ":") && int.TryParse(k.AsSpan(keyword.Length + 1), out int n))
                total += n;
        return total;
    }

    public static bool ChangesStats(Op op) => op is Op.SWAP_STATS or Op.DOUBLE_STATS;

    public static void Execute(EffectDef effect, CardInstance source, GameState state, ResolvedTarget target)
    {
        switch (effect.Op)
        {
            case Op.STUN:
                if (target is CreatureTarget st)
                {
                    st.Card.Stunned = true;
                    st.Card.StunSkipFirstEnd = state.CurrentPlayerIndex == st.Card.Controller;
                }
                break;
            case Op.BURN:
                int burn = Math.Max(0, effect.Amount ?? 1);
                if (target is CreatureTarget bc) bc.Card.Burn += burn;
                else if (target is PlayerTarget bp) bp.Player.Burn += burn;
                break;
            case Op.DRAIN:
                if (target is PlayerTarget dp) dp.Player.DrainNext += Math.Max(0, effect.Amount ?? 1);
                break;
            case Op.LOCK_LANE:
                if (target is LaneTarget lt)
                {
                    var lane = state.Player(lt.PlayerIndex).Lanes[lt.LaneIndex];
                    lane.LockedTurns = Math.Max(lane.LockedTurns, Math.Max(1, effect.Amount ?? 1));
                }
                break;
            case Op.SWAP_STATS:
                if (target is CreatureTarget sw) SwapStats(sw.Card);
                break;
            case Op.DOUBLE_STATS:
                if (target is CreatureTarget dc) DoubleStats(dc.Card, effect, source, state);
                break;
            case Op.STEAL:
                if (target is CreatureTarget sc) Steal(sc.Card, source, state, effect.Duration == Duration.THIS_TURN);
                break;
            case Op.SET_TRAP:
                if (target is PlayerTarget tp)
                    tp.Player.Traps.Add(new TrapInstance
                    {
                        Kind = string.IsNullOrEmpty(effect.Keyword) ? "COUNTER_RITUAL" : effect.Keyword.ToUpperInvariant(),
                        Amount = effect.Amount ?? 0,
                        SourceDefId = source.CardDefId
                    });
                break;
            case Op.HEAL_FULL:
                if (target is CreatureTarget hf) hf.Card.Damage = 0;
                else if (target is PlayerTarget hp) hp.Player.Vigor = hp.Player.MaxVigor;
                break;
            case Op.REDIRECT:
                if (target is CreatureTarget rc) rc.Card.RedirectCharges = Math.Max(rc.Card.RedirectCharges, Math.Max(1, effect.Amount ?? 1));
                break;
        }
    }

    // ——— Armor / Dodge ———

    public static int ReduceByArmor(CardInstance card, int amount)
    {
        if (amount <= 0) return amount;
        int armor = KeywordValue(card, "ARMOR");
        return armor > 0 ? Math.Max(0, amount - armor) : amount;
    }

    /// <summary>Combat damage about to be dealt to <paramref name="card"/>: Dodge may stop it.</summary>
    public static int CombatDamageTo(GameState state, CardInstance card, int amount)
    {
        if (amount <= 0) return amount;
        int dodge = Math.Min(90, KeywordValue(card, "DODGE"));
        if (dodge > 0 && state.Rng.NextInt(100) < dodge) return 0;
        return amount;
    }

    /// <summary>Both sides' combat damage has landed: a Venom DEFENDER poisons the attacker too (rules §8).</summary>
    public static void AfterCombatDamage(GameState state, CardInstance attacker, CardInstance defender, int toDefender, int toAttacker)
    {
        if (toAttacker > 0 && defender.EffectiveKeywords.Contains("VENOM"))
            attacker.IsVenomed = true;
    }

    // ——— Exalted ———

    public static int AttackPowerWithExalted(GameState state, PlayerState player, CardInstance attacker, int power)
    {
        if (player.AttackCountThisTurn != 1) return power;
        int n = 0;
        for (int i = 0; i < 5; i++)
            if (player.Lanes[i].Occupant?.EffectiveKeywords.Contains("EXALTED") == true) n++;
        if (n == 0) return power;
        attacker.AttackModifier += n;
        attacker.VigorModifier += n;
        attacker.TimedMods.Add(new TimedMod { Attack = n, Vigor = n, EndOfTurn = true });
        return power + n;
    }

    // ——— Stats ———

    private static void SwapStats(CardInstance c)
    {
        int atk = Math.Max(0, c.BaseAttack + c.AttackModifier);
        int vig = c.BaseVigor + c.VigorModifier - c.Damage;
        c.BaseAttack = Math.Max(0, vig);
        c.BaseVigor = atk;
        c.AttackModifier = 0;
        c.VigorModifier = 0;
        c.Damage = 0;
        c.TimedMods.RemoveAll(m => m.Keyword is null);   // the swap bakes in every stat change so far
    }

    private static void DoubleStats(CardInstance c, EffectDef effect, CardInstance source, GameState state)
    {
        int addA = c.CurrentAttack, addV = c.CurrentVigor;
        c.AttackModifier += addA;
        c.VigorModifier += addV;
        if (effect.Duration != Duration.PERMANENT)
            c.TimedMods.Add(new TimedMod { Attack = addA, Vigor = addV, EndOfTurn = true });
    }

    // ——— Steal ———

    private static void Steal(CardInstance c, CardInstance source, GameState state, bool thisTurnOnly)
    {
        if (c.Zone != Zone.Lane || c.LaneIndex is not int from || c.CardType == CardType.RELIC) return;
        int newOwner = source.Controller;
        if (c.Controller == newOwner) return;
        var me = state.Player(newOwner);
        var them = state.Player(c.Controller);
        int to = me.Lanes[from].Occupant is null && me.Lanes[from].LockedTurns == 0 && !me.Lanes[from].IsBuried ? from
            : Enumerable.Range(0, 5).FirstOrDefault(i => me.Lanes[i].Occupant is null && me.Lanes[i].LockedTurns == 0 && !me.Lanes[i].IsBuried, -1);
        if (to < 0) return;   // nowhere to put it: the steal fails
        them.Lanes[from].Occupant = null;
        me.Lanes[to].Occupant = c;
        c.StolenFrom = thisTurnOnly ? c.Controller : -1;
        c.StolenFromLane = thisTurnOnly ? from : -1;
        c.Controller = newOwner;
        c.LaneIndex = to;
        // a creature borrowed for the turn can attack this turn; one taken for good arrives exhausted
        c.IsExhausted = !thisTurnOnly;
        c.HasAttackedThisTurn = false;
        c.SummonedThisTurn = true;
        if (them.PreyTargetId == c.InstanceId) them.PreyTargetId = null;
    }

    // ——— Traps ———

    /// <summary>An enemy COUNTER_RITUAL Sigil negates this ritual (it is still spent). True = countered.</summary>
    public static bool TryCounter(GameState state, CardInstance ritual, int caster)
    {
        var enemy = state.Player(state.OpponentIndex(caster));
        var trap = enemy.Traps.FirstOrDefault(t => t.Kind == "COUNTER_RITUAL");
        if (trap is null) return false;
        enemy.Traps.Remove(trap);
        state.LastTrapSprung = $"{trap.SourceDefId}>{ritual.CardDefId}";
        return true;
    }

    /// <summary>An enemy AMBUSH Sigil hits the attacker before it strikes.</summary>
    public static void SpringAmbush(GameState state, PlayerState defender, CardInstance attacker)
    {
        var trap = defender.Traps.FirstOrDefault(t => t.Kind == "AMBUSH");
        if (trap is null) return;
        defender.Traps.Remove(trap);
        state.LastTrapSprung = $"{trap.SourceDefId}>{attacker.CardDefId}";
        int dmg = ReduceByArmor(attacker, KeywordHandlers.ApplyWard(attacker, Math.Max(0, trap.Amount)));
        attacker.Damage += dmg;
        if (attacker.CurrentVigor <= 0) EffectExecutor.KillCreature(attacker, state);
    }

    // ——— Redirect ———

    public static int RedirectAttack(GameState state, PlayerState defender, int targetLane)
    {
        for (int i = 0; i < 5; i++)
        {
            var c = defender.Lanes[i].Occupant;
            if (c is null || c.RedirectCharges <= 0 || i == targetLane) continue;
            c.RedirectCharges--;
            return i;
        }
        return targetLane;
    }

    // ——— Tokens ———

    /// <summary>A summoned token takes its printed stats, keywords, types and abilities when it has a card.</summary>
    public static void ApplyTokenDef(CardInstance token, string tokenId, EffectDef effect)
    {
        var def = CardRegistry.Get(tokenId);
        if (def is null) return;
        if (effect.Attack is null && def.Attack is int a) token.BaseAttack = a;
        if (effect.Vigor is null && def.Vigor is int v) token.BaseVigor = v;
        token.Strata = def.Strata;
        foreach (var k in def.Keywords)
            if (!token.Keywords.Contains(k.ToUpperInvariant())) token.Keywords.Add(k.ToUpperInvariant());
        token.Types.AddRange(def.Types.Select(t => t.ToUpperInvariant()));
        token.Abilities.AddRange(def.Abilities.Select(CardInstance.CloneAbility));
    }

    public static void ClearStatuses(CardInstance c)
    {
        c.Stunned = false;
        c.StunSkipFirstEnd = false;
        c.Burn = 0;
        c.RedirectCharges = 0;
        c.StolenFrom = -1;
        c.StolenFromLane = -1;
    }

    // ——— Turn hooks ———

    /// <summary>The end of <paramref name="playerIndex"/>'s turn: stuns wear off, locks tick, borrowed creatures go home.</summary>
    public static void OnTurnEnd(GameState state, int playerIndex)
    {
        var p = state.Player(playerIndex);
        for (int i = 0; i < 5; i++)
        {
            var c = p.Lanes[i].Occupant;
            if (c is not null && c.Stunned)
            {
                if (c.StunSkipFirstEnd) c.StunSkipFirstEnd = false;
                else c.Stunned = false;
            }
            if (p.Lanes[i].LockedTurns > 0) p.Lanes[i].LockedTurns--;
        }
        // borrowed creatures (STEAL … THIS_TURN) return
        for (int i = 0; i < 5; i++)
        {
            var c = p.Lanes[i].Occupant;
            if (c is null || c.StolenFrom < 0) continue;
            var home = state.Player(c.StolenFrom);
            int to = c.StolenFromLane is >= 0 and <= 4 && home.Lanes[c.StolenFromLane].Occupant is null ? c.StolenFromLane
                : Enumerable.Range(0, 5).FirstOrDefault(k => home.Lanes[k].Occupant is null, -1);
            if (to < 0)
            {
                // no room at home: it is destroyed (under its rightful owner)
                p.Lanes[i].Occupant = null;
                c.Controller = c.StolenFrom;
                c.StolenFrom = -1;
                c.Zone = Zone.Discard;
                home.Discard.Add(c);
                continue;
            }
            p.Lanes[i].Occupant = null;
            home.Lanes[to].Occupant = c;
            c.Controller = home.Index;
            c.LaneIndex = to;
            c.StolenFrom = -1;
            c.StolenFromLane = -1;
            c.IsExhausted = true;
        }
    }

    /// <summary>The start of a player's turn, after Attune: Drain, then Burn on their creatures and on them.</summary>
    public static void OnTurnStartBeforeDraw(GameState state, PlayerState p)
    {
        if (p.DrainNext > 0)
        {
            p.Attunement = Math.Max(0, p.Attunement - p.DrainNext);
            p.DrainNext = 0;
        }
        for (int i = 0; i < 5 && !state.IsGameOver; i++)
        {
            var c = p.Lanes[i].Occupant;
            if (c is null || c.Burn <= 0) continue;
            c.Damage += ReduceByArmor(c, c.Burn);
            c.Burn--;
            if (c.CurrentVigor <= 0) EffectExecutor.KillCreature(c, state);
        }
        if (p.Burn > 0 && !state.IsGameOver)
        {
            p.Vigor -= p.Burn;
            p.Burn--;
            if (p.Vigor <= 0)
            {
                state.IsGameOver = true;
                state.WinnerIndex = state.OpponentIndex(p.Index);
            }
        }
    }

    // ——— Play rules ———

    /// <summary>
    /// Tribute N: the creatures that will be destroyed to play <paramref name="card"/> into
    /// <paramref name="lane"/>. The creature in that lane (if it's yours) is the first; the rest are your
    /// weakest. Null = not enough creatures.
    /// </summary>
    public static List<CardInstance>? TributeVictims(PlayerState p, CardInstance card, int lane)
    {
        int n = card.Tribute;
        if (n <= 0) return new List<CardInstance>();
        var victims = new List<CardInstance>();
        var inLane = p.Lanes[lane].Occupant;
        if (inLane is not null)
        {
            if (inLane.CardType == CardType.RELIC) return null;
            victims.Add(inLane);
        }
        var rest = Enumerable.Range(0, 5).Select(i => p.Lanes[i].Occupant)
            .Where(c => c is not null && c.CardType != CardType.RELIC && !victims.Contains(c))
            .OrderBy(c => c!.Cost).ThenBy(c => c!.CurrentAttack + c!.CurrentVigor).ThenBy(c => c!.InstanceId)
            .Cast<CardInstance>();
        victims.AddRange(rest.Take(n - victims.Count));
        return victims.Count >= n ? victims.Take(n).ToList() : null;
    }

    /// <summary>Why <paramref name="card"/> can't go into <paramref name="lane"/> (null = it can).</summary>
    public static string? LaneProblem(PlayerState p, CardInstance card, int lane)
    {
        if (lane < 0 || lane > 4) return $"Invalid lane index: {lane}.";
        var l = p.Lanes[lane];
        if (l.IsBuried) return $"Lane {lane + 1} is buried.";
        if (l.LockedTurns > 0) return $"Lane {lane + 1} is locked.";
        if (card.Tribute > 0)
        {
            if (TributeVictims(p, card, lane) is null)
                return $"Needs {card.Tribute} creature{(card.Tribute == 1 ? "" : "s")} to tribute.";
            return null;
        }
        if (l.Occupant is not null) return $"Lane {lane + 1} is already occupied.";
        return null;
    }
}

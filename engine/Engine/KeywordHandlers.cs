using Runewake.Engine.State;

namespace Runewake.Engine.Engine;

/// <summary>
/// Pure static handlers for all 11 Runewake keywords.
/// Every handler is a pure function: takes state, returns modifications.
/// See <c>docs/01_GAME_RULES.md §8</c> for keyword definitions.
/// </summary>
public static class KeywordHandlers
{
    // ——— Entry points called from DuelEngine ———

    /// <summary>Apply effects when a creature is played to the field.</summary>
    public static void OnPlay(CardInstance card)
    {
        card.SummonedThisTurn = true;
        // Default: exhaust on summon
        card.IsExhausted = true;
        // Swift overrides: not exhausted
        if (card.EffectiveKeywords.Contains("SWIFT"))
            card.IsExhausted = false;
        if (card.EffectiveKeywords.Contains("WARD"))
            card.WardRemaining = 1;
    }

    /// <summary>Returns true if the creature is allowed to declare an attack.</summary>
    public static bool CanAttack(CardInstance card)
    {
        return !card.EffectiveKeywords.Contains("ROOTED");
    }

    /// <summary>
    /// Determines the resolved target lane for an attack.
    /// Returns the actual lane that will be attacked, or null if the target is invalid.
    /// </summary>
    public static int? ResolveTargetLane(CardInstance attacker, int sourceLane, int? requestedTarget)
    {
        int target = requestedTarget ?? sourceLane;

        if (attacker.EffectiveKeywords.Contains("REACH"))
        {
            // Reach allows attacking sourceLane-1, sourceLane, or sourceLane+1
            int diff = int.Abs(target - sourceLane);
            if (diff > 1 || target < 0 || target > 4)
                return null;
        }
        else
        {
            // Without Reach, only the opposing lane
            if (target != sourceLane)
                return null;
        }

        return target;
    }

    /// <summary>
    /// Apply Ward before taking damage. Returns the actual damage after ward absorption.
    /// </summary>
    public static int ApplyWard(CardInstance target, int incomingDamage)
    {
        if (target.WardRemaining > 0 && incomingDamage > 0)
        {
            target.WardRemaining--;
            return 0;
        }
        return incomingDamage;
    }

    /// <summary>
    /// Apply Venom marking when damage is dealt in combat.
    /// </summary>
    public static void OnCombatDamageDealt(CardInstance attacker, CardInstance defender, int actualDamage)
    {
        if (actualDamage > 0 && attacker.EffectiveKeywords.Contains("VENOM"))
        {
            defender.IsVenomed = true;
        }
    }

    /// <summary>
    /// After combat damage resolves, destroy all creatures marked by Venom
    /// and clear Venom flags for next combat.
    /// </summary>
    public static void ResolveVenom(GameState state, int attackerPlayerIndex)
    {
        var opponent = state.Player(state.OpponentIndex(attackerPlayerIndex));
        for (int i = 0; i < 5; i++)
        {
            var occ = opponent.Lanes[i].Occupant;
            if (occ is not null && occ.IsVenomed)
            {
                DestroyCreature(opponent.Lanes[i], occ, opponent, state);
                occ.IsVenomed = false;
            }
        }

        // Also check the attacker's own creatures (in case of self-damage reflection)
        var attacker = state.Player(attackerPlayerIndex);
        for (int i = 0; i < 5; i++)
        {
            var occ = attacker.Lanes[i].Occupant;
            if (occ is not null && occ.IsVenomed)
            {
                DestroyCreature(attacker.Lanes[i], occ, attacker, state);
                occ.IsVenomed = false;
            }
        }
    }

    /// <summary>
    /// Called when a creature is destroyed. Handles Unearth keyword.
    /// Returns true if the card was intercepted (will be unearthed instead of going to discard).
    /// </summary>
    public static bool OnDeath(CardInstance card, PlayerState owner)
    {
        if (card.UnearthCost > 0)
        {
            // Instead of going to discard, queue for Unearth
            card.Zone = Zone.RemovedFromGame;
            owner.UnearthQueue.Add(card);
            return true; // intercepted
        }
        return false;
    }

    /// <summary>
    /// Process Unearth queue at the start of the player's turn.
    /// Cards that can be afforded return to hand; others are discarded.
    /// </summary>
    public static void ProcessUnearth(PlayerState player)
    {
        var remaining = new List<CardInstance>();
        foreach (var card in player.UnearthQueue)
        {
            if (player.Attunement >= card.UnearthCost)
            {
                player.Attunement -= card.UnearthCost;
                card.Zone = Zone.Hand;
                player.Hand.Add(card);
            }
            else
            {
                // Cannot afford — go to discard
                card.Zone = Zone.Discard;
                player.Discard.Add(card);
            }
        }
        player.UnearthQueue.Clear();
    }

    /// <summary>
    /// Process Fragile at end of turn: destroy creatures summoned this turn
    /// that have the Fragile keyword. Also resets SummonedThisTurn flags.
    /// </summary>
    public static void ProcessFragile(PlayerState player)
    {
        for (int i = 0; i < 5; i++)
        {
            var occ = player.Lanes[i].Occupant;
            if (occ is not null && occ.SummonedThisTurn && occ.EffectiveKeywords.Contains("FRAGILE"))
            {
                DestroyCreature(player.Lanes[i], occ, player, null);
            }
        }

        // Reset SummonedThisTurn for remaining creatures
        for (int i = 0; i < 5; i++)
        {
            var occ = player.Lanes[i].Occupant;
            if (occ is not null)
                occ.SummonedThisTurn = false;
        }
    }

    /// <summary>
    /// Returns true if the given card cannot be targeted by enemy abilities (Sealed).
    /// </summary>
    public static bool IsSealed(CardInstance card)
    {
        return card.EffectiveKeywords.Contains("SEALED");
    }

    // ——— Internal helpers ———

    private static void DestroyCreature(LaneState lane, CardInstance card, PlayerState owner, GameState? state)
    {
        lane.Occupant = null;
        card.Zone = Zone.Discard;
        owner.Discard.Add(card);
        if (state is not null)
            CheckGameOver(state, owner);
    }

    private static void CheckGameOver(GameState state, PlayerState player)
    {
        if (player.Vigor <= 0)
        {
            state.IsGameOver = true;
            state.WinnerIndex = state.OpponentIndex(player.Index);
        }
    }
}
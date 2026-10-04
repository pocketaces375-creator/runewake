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
        return !card.EffectiveKeywords.Contains("ROOTED") && !card.Stunned;   // FABLE-DROP-1: Stun
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
        // FABLE-DROP-1: through the one death path, so a Venom kill fires death triggers and Unearth
        foreach (int p in new[] { state.OpponentIndex(attackerPlayerIndex), attackerPlayerIndex })
        {
            var player = state.Player(p);
            for (int i = 0; i < 5; i++)
            {
                var occ = player.Lanes[i].Occupant;
                if (occ is not null && occ.IsVenomed)
                {
                    occ.IsVenomed = false;
                    EffectExecutor.KillCreature(occ, state);
                }
            }
        }
    }

    /// <summary>
    /// Called when a creature is destroyed. Handles Unearth keyword.
    /// Returns true if the card was intercepted (will be unearthed instead of going to discard).
    /// </summary>
    public static bool OnDeath(CardInstance card, PlayerState owner)
    {
        // FABLE-DROP-1: the Unearth KEYWORD (rules §8) — six cards had it and it did nothing. It returns
        // the creature to your hand at the start of your next turn, once (the returned card has used it).
        if (card.UnearthCost <= 0 && card.EffectiveKeywords.Contains("UNEARTH") && !card.UnearthUsed
            && card.CardType == Cards.CardType.CREATURE)
        {
            card.Zone = Zone.RemovedFromGame;
            card.UnearthUsed = true;
            owner.UnearthQueue.Add(card);
            return true;
        }
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
            if (card.UnearthCost <= 0)
            {
                ResetForHand(card);
                card.UnearthUsed = true;
                if (player.Hand.Count < player.MaxHandSize) { card.Zone = Zone.Hand; player.Hand.Add(card); }
                else { card.Zone = Zone.Discard; player.Discard.Add(card); }
                continue;
            }
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
    public static void ProcessFragile(PlayerState player, GameState? state = null)
    {
        for (int i = 0; i < 5; i++)
        {
            var occ = player.Lanes[i].Occupant;
            if (occ is not null && occ.SummonedThisTurn && occ.EffectiveKeywords.Contains("FRAGILE"))
            {
                if (state is not null) EffectExecutor.KillCreature(occ, state);   // FABLE-DROP-1: death triggers fire
                else DestroyCreature(player.Lanes[i], occ, player, null);
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

    /// <summary>
    /// ANCESTRAL_SHIELD: after an enemy spell applies damage to a creature,
    /// clamp that creature's Vigor to at least 1 (clamp not prevention — damage
    /// triggers still fire). One use per turn, until the shield-owner's next turn.
    /// Scans all friendly creatures for an active ANCESTRAL_SHIELD.
    /// Returns true if the clamp was applied (shield consumed).
    /// </summary>
    public static bool TryAncestralShieldClamp(CardInstance damagedCreature, GameState state)
    {
        var owner = state.Player(damagedCreature.Controller);
        for (int i = 0; i < 5; i++)
        {
            var ally = owner.Lanes[i].Occupant;
            if (ally is null || !ally.EffectiveKeywords.Contains("ANCESTRAL_SHIELD"))
                continue;
            if (ally.AncestralShieldUsedThisTurn)
                continue;

            // Clamp: if CurrentVigor would be below 1, reduce Damage to achieve V=1
            if (damagedCreature.CurrentVigor < 1)
            {
                int vigTarget = damagedCreature.BaseVigor + damagedCreature.VigorModifier;
                damagedCreature.Damage = Math.Max(0, vigTarget - 1);
                ally.AncestralShieldUsedThisTurn = true;
                return true;
            }
            // No clamping needed — creature is still ≥1 V
            return false;
        }
        return false;
    }

    /// <summary>
    /// Reset ANCESTRAL_SHIELD usage flags at the start of the controlling player's turn.
    /// </summary>
    public static void ResetAncestralShields(PlayerState player)
    {
        for (int i = 0; i < 5; i++)
        {
            var ally = player.Lanes[i].Occupant;
            if (ally is not null)
                ally.AncestralShieldUsedThisTurn = false;
        }
    }

    /// <summary>
    /// FABLE-DROP-1: a card going back to a hand (Bounce, Unearth) comes back as printed — no damage, no
    /// buffs, no granted or silenced keywords, no statuses.
    /// </summary>
    public static void ResetForHand(CardInstance card)
    {
        card.LaneIndex = null;
        card.Damage = 0;
        card.AttackModifier = 0;
        card.VigorModifier = 0;
        card.AuraAttack = 0;
        card.AuraVigor = 0;
        card.AuraKeywords.Clear();
        card.GrantedKeywords.Clear();
        card.RemovedKeywords.Clear();
        card.TimedMods.Clear();
        card.DamageShields.Clear();
        card.WardRemaining = 0;
        card.IsVenomed = false;
        card.IsExhausted = false;
        card.HasAttackedThisTurn = false;
        card.SummonedThisTurn = false;
        MechanicOps.ClearStatuses(card);
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
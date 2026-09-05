using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Engine.Engine;

/// <summary>
/// Computes effective card-play costs under standing COST_MOD discounts
/// (the discount mechanic, TASK-DSL-3).
/// Called at card-play time: the engine charges the effective cost, and the
/// bot uses the same function when enumerating legal actions so discounted
/// cards are playable. Matching discounts never push a cost below 0 (floor 0).
/// </summary>
public static class CostInterceptor
{
    /// <summary>
    /// Per-turn consumption filter: the discount applies to the first spell
    /// cast each turn, then is spent (Wand passive).
    /// </summary>
    public const string FirstSpellEachTurn = "FIRST_SPELL_EACH_TURN";

    /// <summary>
    /// Per-turn consumption filter: the discount applies to the first creature
    /// played each turn, then is spent (Lockpick passive).
    /// </summary>
    public const string FirstCreatureEachTurn = "FIRST_CREATURE_EACH_TURN";

    /// <summary>
    /// Card filter: creature with CurrentAttack ≤ value (Duskfang passive).
    /// </summary>
    public const string AttackLte = "ATTACK_LTE";

    /// <summary>
    /// Compute the effective play cost of a card for a player under their
    /// active COST_MOD discounts. Pure — never mutates state (per-turn
    /// consumption is applied separately via <see cref="ConsumePerTurnMods"/>
    /// when the card is actually played).
    /// </summary>
    public static int GetEffectiveCost(GameState state, CardInstance card, int controller)
    {
        var player = state.Player(controller);
        int discount = 0;

        foreach (var mod in player.CostMods)
        {
            if (!MatchesCard(mod, card)) continue;
            if (!IsActive(state, mod)) continue;

            // Per-turn consumption gate: spent mods stop applying until re-applied.
            if (IsPerTurnFilter(mod.Filter) && mod.UsedThisTurn > 0) continue;

            discount += mod.Amount;
        }

        return Math.Max(0, card.Cost - discount);
    }

    /// <summary>
    /// Mark per-turn consumption filters as spent after a card is actually
    /// played (so the discount applies to at most one card per gate per turn).
    /// Called by the engine after a successful play.
    /// </summary>
    public static void ConsumePerTurnMods(GameState state, CardInstance card, int controller)
    {
        var player = state.Player(controller);

        foreach (var mod in player.CostMods)
        {
            if (mod.UsedThisTurn > 0) continue;
            if (!IsPerTurnFilter(mod.Filter)) continue;
            if (!AppliesToMatches(mod, card)) continue;
            if (!IsActive(state, mod)) continue;
            mod.UsedThisTurn++;
        }
    }

    /// <summary>
    /// Remove all cost mods created by a specific Artifact instance (used on
    /// suppression so a suppressed Artifact's discounts die immediately —
    /// suppression symmetry, G3).
    /// </summary>
    public static void RemoveModsFromArtifact(GameState state, int artifactInstanceId, int controller)
    {
        var player = state.Player(controller);
        player.CostMods.RemoveAll(m =>
            m.SourceArtifactInstanceId == artifactInstanceId && m.SourceController == controller);
    }

    // ——— Matching ———

    private static bool MatchesCard(CostMod mod, CardInstance card)
    {
        if (!AppliesToMatches(mod, card)) return false;

        string filter = mod.Filter?.ToUpperInvariant() ?? "";
        switch (filter)
        {
            case "" or "ALL" or "ANY":
                return true;
            case AttackLte:
                return card.CurrentAttack <= (mod.Value ?? 0);
            case FirstSpellEachTurn:
                // Per-turn gate — spend check handled by caller; the gate itself
                // never excludes by card beyond applies_to (SPELL).
                return true;
            case FirstCreatureEachTurn:
                // Per-turn gate — spend check handled by caller; the gate itself
                // never excludes by card beyond applies_to (CREATURE).
                return true;
            default:
                // Unknown filter: don't block (lenient forward-compat).
                return true;
        }
    }

    /// <summary>
    /// Card-type filter match: null/empty = any type; "CREATURE" = creatures
    /// (and tokens); "SPELL" = Rituals.
    /// </summary>
    private static bool AppliesToMatches(CostMod mod, CardInstance card)
    {
        string applies = mod.AppliesTo?.ToUpperInvariant() ?? "ANY";
        return applies switch
        {
            "CREATURE" => card.CardType is CardType.CREATURE or CardType.TOKEN,
            "SPELL" => card.CardType == CardType.RITUAL,
            _ => true
        };
    }

    private static bool IsPerTurnFilter(string? filter)
        => filter is not null && (filter.Equals(FirstSpellEachTurn, StringComparison.OrdinalIgnoreCase) || filter.Equals(FirstCreatureEachTurn, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when the mod currently applies: its condition (evaluated at
    /// play time) passes and its source Artifact is not suppressed or gone.
    /// </summary>
    private static bool IsActive(GameState state, CostMod mod)
    {
        // Suppression symmetry: a mod from a suppressed (or removed) Artifact is inert.
        if (IsSourceArtifactSuppressed(state, mod))
            return false;

        // Condition evaluated at card-play time (e.g. CREATURE_DIED_THIS_TURN).
        if (mod.Condition is not null &&
            !TriggerBus.EvaluateCondition(mod.Condition, MakeSourceCard(mod), mod.SourceController, state))
            return false;

        return true;
    }

    /// <summary>
    /// Build a lightweight source card for condition evaluation (reads side
    /// from the owning player's state).
    /// </summary>
    private static CardInstance MakeSourceCard(CostMod mod)
        => new(mod.SourceArtifactInstanceId, mod.SourceArtifactDefId, mod.SourceController);

    /// <summary>
    /// True when the Artifact that created the mod is currently suppressed or
    /// no longer present. Only cards with artifact def ids (prefixed "artf_")
    /// are checked; non-artifact sources are always active. Test-created mods
    /// without a backing artifact are always active.
    /// </summary>
    private static bool IsSourceArtifactSuppressed(GameState state, CostMod mod)
    {
        if (mod.SourceArtifactInstanceId <= 0 || mod.SourceController < 0)
            return false;
        if (!mod.SourceArtifactDefId.StartsWith("artf_", StringComparison.Ordinal))
            return false;

        var owner = state.Player(mod.SourceController);
        foreach (var slot in owner.ArtifactSlots)
        {
            if (slot.Occupant is { } occ && occ.InstanceId == mod.SourceArtifactInstanceId)
                return slot.IsSuppressed;
        }
        // Artifact no longer present — the mod is inert.
        return true;
    }
}
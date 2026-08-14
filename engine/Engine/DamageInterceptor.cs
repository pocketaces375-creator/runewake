using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Engine.Engine;

/// <summary>
/// Intercepts incoming damage and applies standing PREVENT_DAMAGE shields
/// (registered on players and creatures by the PREVENT_DAMAGE op).
/// Called at every damage-application point in the engine.
/// </summary>
public static class DamageInterceptor
{
    /// <summary>Damage-source classification for combat/attack damage.</summary>
    public const string SourceAttack = "ATTACK";

    /// <summary>Damage-source classification for spell/ability damage (DAMAGE op).</summary>
    public const string SourceSpell = "SPELL";

    /// <summary>
    /// Reduce damage to a player by active shields on that player.
    /// </summary>
    public static int Reduce(GameState state, PlayerState target, int amount, string sourceType)
        => ReduceShields(state, target.DamageShields, target, amount, sourceType);

    /// <summary>
    /// Reduce damage to a creature by active shields on that creature.
    /// </summary>
    public static int Reduce(GameState state, CardInstance target, int amount, string sourceType)
        => ReduceShields(state, target.DamageShields, target, amount, sourceType);

    /// <summary>
    /// Iterate the target's shields, applying each matching shield in order.
    /// Returns the damage that actually gets through (never negative).
    /// </summary>
    private static int ReduceShields(GameState state, List<DamageShield> shields, object target, int amount, string sourceType)
    {
        if (amount <= 0 || shields.Count == 0)
            return Math.Max(0, amount);

        int remaining = amount;
        foreach (var shield in shields)
        {
            if (remaining <= 0) break;

            if (!SourceMatches(shield.Source, sourceType))
                continue;

            // Frequency gate: gated shields fire at most once per turn (reset every turn start).
            if (shield.Frequency is not null && shield.UsedThisTurn > 0)
                continue;

            // Condition evaluated at damage-application time (R21).
            if (shield.Condition is not null &&
                !TriggerBus.EvaluateCondition(shield.Condition, MakeSourceCard(shield), shield.SourceController, state))
                continue;

            // Suppression symmetry: a shield from a suppressed (or removed) Artifact is inert.
            if (IsSourceArtifactSuppressed(state, shield))
                continue;

            int prevented = Math.Min(shield.Amount, remaining);
            remaining -= prevented;
            if (shield.Frequency is not null)
                shield.UsedThisTurn++;
        }

        return Math.Max(0, remaining);
    }

    /// <summary>
    /// Source filter match: null/empty = any source; "ATTACK"/"SPELL" = exact match.
    /// </summary>
    private static bool SourceMatches(string? filter, string sourceType)
    {
        if (string.IsNullOrEmpty(filter))
            return true;
        return string.Equals(filter, sourceType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Build a lightweight source card for condition evaluation (partner-charge etc. read from it).
    /// </summary>
    private static CardInstance MakeSourceCard(DamageShield shield)
        => new(shield.SourceArtifactInstanceId, shield.SourceArtifactDefId, shield.SourceController);

    /// <summary>
    /// True when the Artifact that created the shield is currently suppressed or no longer present.
    /// Only cards with artifact def ids (prefixed "artf_") are checked; non-artifact sources are
    /// always active. Test-created shields without a backing artifact are always active.
    /// </summary>
    private static bool IsSourceArtifactSuppressed(GameState state, DamageShield shield)
    {
        if (shield.SourceArtifactInstanceId <= 0 || shield.SourceController < 0)
            return false;
        // Only Artifact cards can be suppressed; everything else ignores suppression.
        if (!shield.SourceArtifactDefId.StartsWith("artf_", StringComparison.Ordinal))
            return false;

        var owner = state.Player(shield.SourceController);
        foreach (var slot in owner.ArtifactSlots)
        {
            if (slot.Occupant is { } occ && occ.InstanceId == shield.SourceArtifactInstanceId)
                return slot.IsSuppressed;
        }
        // Artifact no longer present — shield is inert.
        return true;
    }

    /// <summary>
    /// Reset frequency counters on all shields belonging to a player and their creatures.
    /// Called at the start of EVERY turn (R5: first-attack shields reset for both players).
    /// </summary>
    public static void ResetUsage(GameState state)
    {
        for (int p = 0; p < 2; p++)
        {
            var player = state.Player(p);
            foreach (var s in player.DamageShields)
                s.UsedThisTurn = 0;
            for (int i = 0; i < 5; i++)
            {
                if (player.Lanes[i].Occupant is { } occ)
                {
                    foreach (var s in occ.DamageShields)
                        s.UsedThisTurn = 0;
                }
            }
        }
    }

    /// <summary>
    /// Remove all shields created by a specific Artifact instance (used on suppression
    /// so a suppressed Artifact's shields die immediately — suppression symmetry).
    /// </summary>
    public static void RemoveShieldsFromArtifact(GameState state, int artifactInstanceId, int controller)
    {
        for (int p = 0; p < 2; p++)
        {
            var player = state.Player(p);
            player.DamageShields.RemoveAll(s =>
                s.SourceArtifactInstanceId == artifactInstanceId && s.SourceController == controller);
            for (int i = 0; i < 5; i++)
            {
                if (player.Lanes[i].Occupant is { } occ)
                {
                    occ.DamageShields.RemoveAll(s =>
                        s.SourceArtifactInstanceId == artifactInstanceId && s.SourceController == controller);
                }
            }
        }
    }
}

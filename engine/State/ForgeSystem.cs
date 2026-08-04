using System;
using System.Collections.Generic;
using System.Linq;
using Runewake.Engine.Cards;

namespace Runewake.Engine.State;

/// <summary>
/// Result of attempting to forge a rune.
/// </summary>
public enum ForgeResult
{
    Success,
    InsufficientFragments,
    AllRunesOwned,
    InvalidStrata
}

/// <summary>
/// Static forge system: 4 fragments of a strata forge 1 random unowned rune of that strata.
/// Duplicate runes are not allowed — if all runes in a strata are owned, forging is blocked.
/// Sigil/Mythic runes (no strata) are NOT forgeable — they come from Warden Bosses.
/// </summary>
public static class ForgeSystem
{
    /// <summary>Number of fragments required to forge one rune.</summary>
    public const int FragmentsPerForge = 4;

    /// <summary>
    /// Attempt to forge a rune of the given strata.
    /// </summary>
    /// <param name="strata">The strata key (lowercase, e.g. "verdant", "ember").</param>
    /// <param name="progression">The player's progression state.</param>
    /// <param name="runeIndex">All available runes keyed by ID.</param>
    /// <param name="forgeRecipes">Maps strata key → list of forgeable rune IDs.</param>
    /// <param name="random">Optional seeded RNG. Uses a new Random if null.</param>
    /// <returns>ForgeResult and the forged rune ID (null on failure).</returns>
    public static (ForgeResult Result, string? RuneId) Forge(
        string strata,
        ProgressionState progression,
        Dictionary<string, RuneDef> runeIndex,
        Dictionary<string, List<string>> forgeRecipes,
        Random? random = null)
    {
        // Validate strata
        if (!forgeRecipes.TryGetValue(strata, out var pool) || pool.Count == 0)
            return (ForgeResult.InvalidStrata, null);

        // Check fragment count
        if (!progression.Fragments.TryGetValue(strata, out var fragments) || fragments < FragmentsPerForge)
            return (ForgeResult.InsufficientFragments, null);

        // Find unowned runes in the pool
        var unowned = pool.Where(id => !progression.OwnedRuneIds.Contains(id)).ToList();
        if (unowned.Count == 0)
            return (ForgeResult.AllRunesOwned, null);

        // Pick one at random
        var rng = random ?? Random.Shared;
        var chosenId = unowned[rng.Next(unowned.Count)];

        // Deduct fragments and add rune
        progression.Fragments[strata] = fragments - FragmentsPerForge;
        progression.OwnedRuneIds.Add(chosenId);

        // Also add the rune as a card if it references one (runes don't add cards)
        // Runes are tracked via OwnedRuneIds, separate from the card Collection

        return (ForgeResult.Success, chosenId);
    }

    /// <summary>
    /// Check if the player can forge a rune of the given strata.
    /// </summary>
    public static bool CanForge(string strata, ProgressionState progression,
        Dictionary<string, List<string>> forgeRecipes)
    {
        if (!forgeRecipes.TryGetValue(strata, out var pool) || pool.Count == 0)
            return false;

        if (!progression.Fragments.TryGetValue(strata, out var fragments) || fragments < FragmentsPerForge)
            return false;

        return pool.Any(id => !progression.OwnedRuneIds.Contains(id));
    }
}
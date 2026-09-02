using System.Collections.Generic;
using Runewake.Engine.Cards;

namespace Runewake.Engine.Engine;

/// <summary>
/// Seeded deterministic drop-roll for encounter victory rewards.
/// Each DropEntry is evaluated independently against a seeded PRNG derived from
/// the duel seed + encounter ID hash, producing a list of card IDs that dropped.
/// </summary>
public static class DropRoller
{
    /// <summary>
    /// Roll an encounter's drop table against a duel seed.
    /// Returns the card IDs that successfully dropped.
    /// </summary>
    public static List<string> Roll(EncounterDef encounter, ulong duelSeed)
    {
        var result = new List<string>();
        if (encounter.Drops == null || encounter.Drops.Count == 0)
            return result;

        // Derive a per-encounter seed from the duel seed + encounter ID hash
        int idHash = encounter.Id.GetHashCode();
        ulong seed = duelSeed ^ (ulong)(idHash < 0 ? -(idHash + 1) : idHash);
        var rng = new System.Random((int)(seed & 0x7FFFFFFF) ^ (int)((seed >> 32) & 0x7FFFFFFF));

        foreach (var entry in encounter.Drops)
        {
            double roll = rng.NextDouble();
            if (roll < entry.Rate)
            {
                result.Add(entry.CardId);
            }
        }

        return result;
    }
}
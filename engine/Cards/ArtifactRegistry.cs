using System.Collections.Generic;
using System.Linq;
using Runewake.Engine.State;

namespace Runewake.Engine.Cards;

/// <summary>
/// Registry for Artifact definitions. Similar to <see cref="CardRegistry"/>
/// but for Artifact cards (field-effect cards that sit in permanent slots).
/// Artifacts are never drawn, discarded, or loaded from deck packs — they
/// are loaded from the class configuration at game start.
/// </summary>
public static class ArtifactRegistry
{
    private static readonly Dictionary<string, ArtifactDef> _artifacts = new();

    /// <summary>
    /// Register a single Artifact definition.
    /// </summary>
    public static void Register(ArtifactDef def)
    {
        _artifacts[def.Id] = def;
    }

    /// <summary>
    /// Register multiple Artifact definitions at once.
    /// </summary>
    public static void RegisterMany(IEnumerable<ArtifactDef> defs)
    {
        foreach (var def in defs)
            _artifacts[def.Id] = def;
    }

    /// <summary>
    /// Look up an Artifact definition by ID, or null if not found.
    /// </summary>
    public static ArtifactDef? Get(string id)
    {
        return _artifacts.TryGetValue(id, out var def) ? def : null;
    }

    /// <summary>
    /// Returns all registered Artifact definitions.
    /// </summary>
    public static IEnumerable<ArtifactDef> GetAll() => _artifacts.Values;

    /// <summary>
    /// Clears all registered Artifacts (for testing).
    /// </summary>
    public static void Clear()
    {
        _artifacts.Clear();
    }

    /// <summary>
    /// Returns the two launch-artifact IDs for the given class (one per slot_pool, stable order).
    /// Unknown/empty class defaults to "battlemage".
    /// </summary>
    public static string[] DefaultLoadoutFor(string classId)
    {
        if (string.IsNullOrEmpty(classId))
            classId = "battlemage";

        var artifacts = _artifacts.Values
            .Where(a => a.Class == classId)
            .OrderBy(a => a.SlotPool)
            .Take(2)
            .Select(a => a.Id)
            .ToArray();

        if (artifacts.Length < 2)
        {
            // Fallback to battlemage
            artifacts = _artifacts.Values
                .Where(a => a.Class == "battlemage")
                .OrderBy(a => a.SlotPool)
                .Take(2)
                .Select(a => a.Id)
                .ToArray();
        }

        return artifacts;
    }

    /// <summary>
    /// A hash of a string that is the same in every process and every build.
    ///
    /// string.GetHashCode() is NOT. .NET randomises string hashing per process by
    /// default, so anything seeded from it silently re-rolls on every launch.
    /// DuelScene used it to pick an opponent's class "stably" — the opponent's
    /// relics therefore changed every time the app started, and a seeded replay
    /// did not reproduce. FNV-1a, so the answer is fixed forever.
    /// </summary>
    public static ulong StableHash(string s)
    {
        ulong h = 14695981039346656037UL;          // FNV offset basis
        foreach (char c in s)
        {
            h ^= c;
            h *= 1099511628211UL;                   // FNV prime
        }
        return h;
    }

    /// <summary>
    /// Pick the class and the two Artifacts an AI opponent brings to a duel.
    ///
    /// Three things this guarantees that <see cref="DefaultLoadoutFor"/> did not:
    ///
    ///  1. NEVER THE PLAYER'S OWN PAIR. Opponent class used to come from
    ///     encounterId.GetHashCode() % 7, so roughly one duel in seven handed the
    ///     opponent the player's class — and DefaultLoadoutFor returns one fixed
    ///     pair per class, so that duel was an exact relic mirror. Worse for a
    ///     battlemage player: battlemage owns exactly one artifact per slot pool,
    ///     so the mirror was total and unavoidable.
    ///  2. VARIETY. DefaultLoadoutFor always takes the first entry of each pool,
    ///     so the whole game could only ever show seven opponent loadouts and the
    ///     40-odd variant artifacts never appeared on an opponent at all. This
    ///     picks a variant per pool from the duel seed.
    ///  3. DETERMINISM. Seeded from the duel seed and a stable hash of the
    ///     encounter id, so the same seed replays identically — which the old
    ///     GetHashCode path could not do.
    ///
    /// Returns the class actually used together with its artifacts, because the
    /// caller must set Player1Class to match or the two disagree.
    /// </summary>
    /// <param name="explicitClass">encounter.Class when the content sets one; honoured as-is.</param>
    /// <param name="playerClassId">The player's class, avoided when nothing forces it.</param>
    /// <param name="playerArtifactIds">The player's own pair — never reused.</param>
    /// <param name="encounterId">Identifies the encounter; mixed into the seed.</param>
    /// <param name="seed">The duel seed, so a replay is reproducible.</param>
    public static (string ClassId, string[] Artifacts) OpponentLoadout(
        string? explicitClass,
        string playerClassId,
        IReadOnlyList<string> playerArtifactIds,
        string encounterId,
        ulong seed)
    {
        var rng = new SeededRng(seed ^ StableHash(encounterId ?? string.Empty));
        var taken = new HashSet<string>(playerArtifactIds ?? System.Array.Empty<string>());

        // Content's explicit class wins; otherwise prefer a class that is not the
        // player's, so the duel reads as two different kits facing each other.
        string classId;
        if (!string.IsNullOrEmpty(explicitClass))
        {
            classId = explicitClass!;
        }
        else
        {
            var pool = _artifacts.Values.Select(a => a.Class)
                                        .Where(c => !string.IsNullOrEmpty(c))
                                        .Distinct().OrderBy(c => c, System.StringComparer.Ordinal)
                                        .ToList();
            var preferred = pool.Where(c => c != playerClassId).ToList();
            var choices = preferred.Count > 0 ? preferred : pool;
            classId = choices.Count > 0 ? choices[rng.NextInt(choices.Count)] : "battlemage";
        }

        var picked = PickPerPool(classId, taken, rng);

        // An explicit class with only one artifact per pool can still collide with
        // the player. Rather than hand back a mirror, step to another class — the
        // content asked for a flavour, not for an identical kit.
        if (picked.Length < 2 || picked.Any(taken.Contains))
        {
            foreach (var alt in _artifacts.Values.Select(a => a.Class)
                                                 .Where(c => !string.IsNullOrEmpty(c) && c != classId && c != playerClassId)
                                                 .Distinct().OrderBy(c => c, System.StringComparer.Ordinal))
            {
                var altPick = PickPerPool(alt, taken, rng);
                if (altPick.Length >= 2 && !altPick.Any(taken.Contains))
                {
                    classId = alt;
                    picked = altPick;
                    break;
                }
            }
        }

        if (picked.Length == 0)
            picked = DefaultLoadoutFor(classId);

        return (classId, picked);
    }

    /// <summary>
    /// One artifact from each of a class's slot pools, chosen with the given rng
    /// and skipping anything the player already holds. Pools are visited in a
    /// fixed order so the result depends only on the seed, never on dictionary
    /// enumeration order.
    /// </summary>
    private static string[] PickPerPool(string classId, HashSet<string> exclude, SeededRng rng)
    {
        var pools = _artifacts.Values
            .Where(a => a.Class == classId && !string.IsNullOrEmpty(a.SlotPool))
            .GroupBy(a => a.SlotPool)
            .OrderBy(g => g.Key, System.StringComparer.Ordinal)
            .ToList();

        var result = new List<string>(2);
        foreach (var pool in pools)
        {
            if (result.Count >= 2) break;
            var options = pool.Select(a => a.Id)
                              .OrderBy(id => id, System.StringComparer.Ordinal)
                              .ToList();
            var usable = options.Where(id => !exclude.Contains(id)).ToList();
            if (usable.Count == 0) continue;           // whole pool is the player's — skip it
            result.Add(usable[rng.NextInt(usable.Count)]);
        }

        // A class with a single deep pool (rogue: six daggers, one pool) still
        // deserves two artifacts — take a second, different entry from it.
        if (result.Count == 1 && pools.Count == 1)
        {
            var second = pools[0].Select(a => a.Id)
                                 .OrderBy(id => id, System.StringComparer.Ordinal)
                                 .Where(id => !exclude.Contains(id) && id != result[0])
                                 .ToList();
            if (second.Count > 0) result.Add(second[rng.NextInt(second.Count)]);
        }

        return result.ToArray();
    }
}
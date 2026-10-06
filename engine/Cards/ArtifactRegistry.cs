using System;
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

        var artifacts = LaunchPair(classId);
        if (artifacts.Length < 2)
            artifacts = LaunchPair("battlemage");   // fallback
        return artifacts;
    }

    /// <summary>
    /// FABLE-042: the class's two LAUNCH artifacts — the first two registered for it
    /// (launch_artifacts.json loads before any variant file), in slot-pool order as before.
    /// The old OrderBy(SlotPool).Take(2) looked at every registered artifact, so once variant
    /// files were loaded a druid could get two books — and because the phone and the PC bot
    /// load different variant files, the two sides of an online duel built different
    /// loadouts and fell out of step the first time an artifact mattered.
    /// (Rogues really do carry two daggers: their launch pair shares the "dagger" pool.)
    /// </summary>
    private static string[] LaunchPair(string classId) => _artifacts.Values
        .Where(a => a.Class == classId && !a.Forge)
        .Take(2)
        .OrderBy(a => a.SlotPool, StringComparer.Ordinal)
        .Select(a => a.Id)
        .ToArray();

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
            .Where(a => a.Class == classId && !a.Forge && !string.IsNullOrEmpty(a.SlotPool))
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

    // ══════════════════════════════════════════════════════════════════════
    //  FABLE-054: the Deck Forge pool — every deck picks two of its class's
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>How many artifacts a deck carries.</summary>
    public const int LoadoutSize = 2;

    /// <summary>The class's Deck Forge artifacts, in content order.</summary>
    public static IReadOnlyList<ArtifactDef> ForgePool(string? classId) => _artifacts.Values
        .Where(a => a.Forge && a.Class == (classId ?? ""))
        .ToList();

    /// <summary>The two a fresh deck of this class starts with (the pool's defaults, else its first two).</summary>
    public static string[] ForgeDefaults(string? classId)
    {
        var pool = ForgePool(classId);
        var picks = pool.Where(a => a.IsDefault).Select(a => a.Id).ToList();
        foreach (var a in pool) { if (picks.Count >= LoadoutSize) break; if (!picks.Contains(a.Id)) picks.Add(a.Id); }
        return picks.Take(LoadoutSize).ToArray();
    }

    /// <summary>
    /// True when these ids are a legal Deck Forge loadout for the class: exactly two, different,
    /// registered, in that class's pool.
    /// </summary>
    public static bool IsValidLoadout(string? classId, IReadOnlyList<string>? ids) =>
        ids is { Count: LoadoutSize } && ids.Distinct().Count() == LoadoutSize
        && ids.All(id => Get(id) is { Forge: true } a && a.Class == classId);

    /// <summary>
    /// What a player brings: their deck's picks when legal, else the class's Forge defaults, else
    /// (no Forge content loaded) the old launch pair. Never returns an illegal pair.
    /// </summary>
    public static string[] PlayerLoadout(string? classId, IReadOnlyList<string>? chosen)
    {
        if (IsValidLoadout(classId, chosen)) return chosen!.ToArray();
        var d = ForgeDefaults(classId);
        return d.Length == LoadoutSize ? d : DefaultLoadoutFor(classId ?? "");
    }

    /// <summary>
    /// An AI opponent's class and two Forge artifacts: a class other than the player's unless the content
    /// names one, two different artifacts from its pool, chosen from the duel seed so a replay matches.
    /// Falls back to <see cref="OpponentLoadout"/> when no Forge pool is loaded.
    /// </summary>
    public static (string ClassId, string[] Artifacts) OpponentForgeLoadout(
        string? explicitClass, string playerClassId, string encounterId, ulong seed)
    {
        var classes = _artifacts.Values.Where(a => a.Forge).Select(a => a.Class).Distinct()
            .OrderBy(c => c, System.StringComparer.Ordinal).ToList();
        if (classes.Count == 0)
            return OpponentLoadout(explicitClass, playerClassId, System.Array.Empty<string>(), encounterId, seed);

        var rng = new SeededRng(seed ^ StableHash("forge:" + (encounterId ?? string.Empty)));
        string classId;
        if (!string.IsNullOrEmpty(explicitClass) && classes.Contains(explicitClass!))
            classId = explicitClass!;
        else
        {
            var others = classes.Where(c => c != playerClassId).ToList();
            var from = others.Count > 0 ? others : classes;
            classId = from[rng.NextInt(from.Count)];
        }
        var pool = ForgePool(classId).Select(a => a.Id).ToList();
        if (pool.Count < LoadoutSize)
            return OpponentLoadout(classId, playerClassId, System.Array.Empty<string>(), encounterId, seed);
        int first = rng.NextInt(pool.Count);
        int second = rng.NextInt(pool.Count - 1);
        if (second >= first) second++;
        return (classId, new[] { pool[first], pool[second] });
    }
}

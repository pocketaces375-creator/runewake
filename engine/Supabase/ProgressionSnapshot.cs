using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Engine.Supabase;

/// <summary>
/// The wire form of one save slot's <see cref="ProgressionState"/>. FABLE-018.
///
/// This is a deliberately dumb, all-settable copy, NOT the state class itself,
/// for two reasons that matter more than the duplication:
///
///   1. ProgressionState's collections are get-only ({ get; } = new()). That is
///      correct for the game — nobody should be swapping the Collection
///      dictionary out from under it — and it means a JSON deserializer
///      cannot populate them without reflection tricks that behave
///      differently across serializer versions. Explicit copy in, explicit
///      copy out, no magic.
///
///   2. This is a FORMAT. Once a save has been uploaded, its shape is a
///      promise to every phone that will ever download it. The state class
///      changes whenever the game does; this one changes only with a
///      Version bump and a migration.
///
/// Every field of ProgressionState is here. If you add one there, add it
/// here or it silently does not survive a reinstall — ProgressionSnapshotTests
/// checks the round trip field by field for exactly that reason.
/// </summary>
public class ProgressionSnapshot
{
    /// <summary>Snapshot format version. Bump with a migration in FromJson.</summary>
    public const int CurrentVersion = 1;

    [JsonPropertyName("v")] public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("shards")] public int Shards { get; set; }
    [JsonPropertyName("dig_charges")] public int DigCharges { get; set; }
    [JsonPropertyName("rune_dust")] public int RuneDust { get; set; }
    [JsonPropertyName("cleared_nodes")] public List<string> ClearedNodes { get; set; } = new();
    [JsonPropertyName("collection")] public Dictionary<string, int> Collection { get; set; } = new();
    [JsonPropertyName("fragments")] public Dictionary<string, int> Fragments { get; set; } = new();
    [JsonPropertyName("owned_rune_ids")] public List<string> OwnedRuneIds { get; set; } = new();
    [JsonPropertyName("unlocked_tools")] public List<string> UnlockedTools { get; set; } = new();
    [JsonPropertyName("relics")] public List<RelicDto> DiscoveredRelics { get; set; } = new();
    [JsonPropertyName("deck_card_ids")] public List<string> DeckCardIds { get; set; } = new();
    [JsonPropertyName("saved_decks")] public Dictionary<string, List<string>> SavedDecks { get; set; } = new();
    [JsonPropertyName("global_discovery_index")] public int GlobalDiscoveryIndex { get; set; }
    [JsonPropertyName("has_completed_tutorial")] public bool HasCompletedTutorial { get; set; }
    [JsonPropertyName("delver_level")] public int DelverLevel { get; set; } = 1;
    [JsonPropertyName("delver_xp")] public int DelverXp { get; set; }
    [JsonPropertyName("rune_page_json")] public string? SavedRunePageJson { get; set; }
    [JsonPropertyName("tutorial_step")] public int? TutorialStep { get; set; }
    [JsonPropertyName("tutorial_complete")] public bool? TutorialComplete { get; set; }
    [JsonPropertyName("shop_rotation_day")] public int ShopRotationDay { get; set; }
    [JsonPropertyName("arena_wins")] public int ArenaWins { get; set; }
    [JsonPropertyName("arena_losses")] public int ArenaLosses { get; set; }
    [JsonPropertyName("seen_card_ids")] public List<string> SeenCardIds { get; set; } = new();

    public class RelicDto
    {
        [JsonPropertyName("id")] public string RelicInstanceId { get; set; } = string.Empty;
        [JsonPropertyName("card_id")] public string CardId { get; set; } = string.Empty;
        [JsonPropertyName("acquirer")] public string AcquirerName { get; set; } = string.Empty;
        [JsonPropertyName("acquired_at")] public string AcquiredAt { get; set; } = string.Empty;
        [JsonPropertyName("site")] public string Site { get; set; } = string.Empty;
        [JsonPropertyName("discovery_index")] public int DiscoveryIndex { get; set; }
        [JsonPropertyName("engraving")] public string EngravingStyle { get; set; } = string.Empty;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    // ── state ⇄ snapshot ──────────────────────────────────────────────────

    public static ProgressionSnapshot FromState(ProgressionState s)
    {
        return new ProgressionSnapshot
        {
            Version = CurrentVersion,
            Shards = s.Shards,
            DigCharges = s.DigCharges,
            RuneDust = s.RuneDust,
            ClearedNodes = s.ClearedNodes.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            Collection = new Dictionary<string, int>(s.Collection),
            Fragments = new Dictionary<string, int>(s.Fragments),
            OwnedRuneIds = s.OwnedRuneIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            UnlockedTools = s.UnlockedTools.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            DiscoveredRelics = s.DiscoveredRelics.Select(r => new RelicDto
            {
                RelicInstanceId = r.RelicInstanceId,
                CardId = r.CardId,
                AcquirerName = r.AcquirerName,
                AcquiredAt = r.AcquiredAt,
                Site = r.Site,
                DiscoveryIndex = r.DiscoveryIndex,
                EngravingStyle = r.EngravingStyle,
            }).ToList(),
            DeckCardIds = new List<string>(s.DeckCardIds),
            SavedDecks = s.SavedDecks.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value)),
            GlobalDiscoveryIndex = s.GlobalDiscoveryIndex,
            HasCompletedTutorial = s.HasCompletedTutorial,
            DelverLevel = s.DelverLevel,
            DelverXp = s.DelverXp,
            SavedRunePageJson = s.SavedRunePageJson,
            TutorialStep = s.Tutorial == null ? null : (int)s.Tutorial.CurrentStep,
            TutorialComplete = s.Tutorial?.IsComplete,
            ShopRotationDay = s.ShopRotationDay,
            ArenaWins = s.ArenaWins,
            ArenaLosses = s.ArenaLosses,
            SeenCardIds = s.SeenCardIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
        };
    }

    /// <summary>
    /// Overwrite <paramref name="s"/> with this snapshot. Total replacement,
    /// not a merge — the merge decision (whose save wins) is made by the caller
    /// BEFORE this is called, on updated_at. Collections are cleared then
    /// filled so the state object's own instances are kept.
    /// </summary>
    public void ApplyTo(ProgressionState s)
    {
        s.Shards = Shards;
        s.DigCharges = DigCharges;
        s.RuneDust = RuneDust;

        s.ClearedNodes.Clear(); foreach (var x in ClearedNodes) s.ClearedNodes.Add(x);
        s.Collection.Clear(); foreach (var kv in Collection) s.Collection[kv.Key] = kv.Value;
        s.Fragments.Clear(); foreach (var kv in Fragments) s.Fragments[kv.Key] = kv.Value;
        s.OwnedRuneIds.Clear(); foreach (var x in OwnedRuneIds) s.OwnedRuneIds.Add(x);
        s.UnlockedTools.Clear(); foreach (var x in UnlockedTools) s.UnlockedTools.Add(x);

        s.DiscoveredRelics.Clear();
        foreach (var r in DiscoveredRelics)
        {
            s.DiscoveredRelics.Add(new LostRelicInstance
            {
                RelicInstanceId = r.RelicInstanceId,
                CardId = r.CardId,
                AcquirerName = r.AcquirerName,
                AcquiredAt = r.AcquiredAt,
                Site = r.Site,
                DiscoveryIndex = r.DiscoveryIndex,
                EngravingStyle = r.EngravingStyle,
            });
        }

        s.DeckCardIds.Clear(); s.DeckCardIds.AddRange(DeckCardIds);
        s.SavedDecks.Clear(); foreach (var kv in SavedDecks) s.SavedDecks[kv.Key] = new List<string>(kv.Value);

        s.GlobalDiscoveryIndex = GlobalDiscoveryIndex;
        s.HasCompletedTutorial = HasCompletedTutorial;
        s.DelverLevel = DelverLevel;
        s.DelverXp = DelverXp;
        s.SavedRunePageJson = SavedRunePageJson;

        if (TutorialStep.HasValue || TutorialComplete.HasValue)
        {
            s.Tutorial = new TutorialState
            {
                CurrentStep = (TutorialStep)(TutorialStep ?? 0),
                IsComplete = TutorialComplete ?? false,
            };
        }
        else s.Tutorial = null;

        s.ShopRotationDay = ShopRotationDay;
        s.ArenaWins = ArenaWins;
        s.ArenaLosses = ArenaLosses;
        s.SeenCardIds.Clear(); foreach (var x in SeenCardIds) s.SeenCardIds.Add(x);
    }

    // ── json ──────────────────────────────────────────────────────────────

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    /// <summary>Null on unreadable input. Older versions are migrated here.</summary>
    public static ProgressionSnapshot? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        ProgressionSnapshot? snap;
        try { snap = JsonSerializer.Deserialize<ProgressionSnapshot>(json, JsonOpts); }
        catch { return null; }
        if (snap == null) return null;

        // Migrations go here, lowest version first:
        // if (snap.Version < 2) { ...; snap.Version = 2; }

        if (snap.Version > CurrentVersion)
            return null;   // written by a newer game than this one; do not guess

        // Null-proof every collection: a hand-edited or truncated blob must
        // not turn into a NullReferenceException in ApplyTo.
        snap.ClearedNodes ??= new();
        snap.Collection ??= new();
        snap.Fragments ??= new();
        snap.OwnedRuneIds ??= new();
        snap.UnlockedTools ??= new();
        snap.DiscoveredRelics ??= new();
        snap.DeckCardIds ??= new();
        snap.SavedDecks ??= new();
        snap.SeenCardIds ??= new();
        if (snap.DelverLevel < 1) snap.DelverLevel = 1;
        return snap;
    }

    /// <summary>
    /// A cheap "is there anything here worth keeping" — used to decide whether
    /// a fresh install should adopt the cloud save silently or ask.
    /// </summary>
    [JsonIgnore]
    public bool IsEmptyProgress =>
        Shards == 0 && ClearedNodes.Count == 0 && Collection.Count == 0
        && DiscoveredRelics.Count == 0 && DelverLevel <= 1 && DelverXp == 0
        && !HasCompletedTutorial;
}

/// <summary>
/// Everything one player owns, across all their local save slots — this is
/// the object that goes into player_saves.save_json.
///
/// The slot model is the client's (up to three campaign profiles, each with
/// its own SQLite file, plus shared profiles.json and decks.json). The engine
/// does not know those client types, so profiles and decks travel as opaque
/// JSON strings the client fills and reads back. Slots are keyed by slot
/// index as a string because JSON object keys are strings.
/// </summary>
public class CloudSaveBundle
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("v")] public int Version { get; set; } = CurrentVersion;
    [JsonPropertyName("profiles_json")] public string? ProfilesJson { get; set; }
    [JsonPropertyName("decks_json")] public string? DecksJson { get; set; }
    [JsonPropertyName("slots")] public Dictionary<string, ProgressionSnapshot> Slots { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    public static CloudSaveBundle? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        CloudSaveBundle? b;
        try { b = JsonSerializer.Deserialize<CloudSaveBundle>(json, JsonOpts); }
        catch { return null; }
        if (b == null || b.Version > CurrentVersion) return null;
        b.Slots ??= new();
        // Re-run the per-slot null-proofing by round-tripping through FromJson
        // once; cheap, and it keeps one set of rules.
        foreach (var key in b.Slots.Keys.ToList())
        {
            var fixedSlot = ProgressionSnapshot.FromJson(b.Slots[key].ToJson());
            if (fixedSlot != null) b.Slots[key] = fixedSlot; else b.Slots.Remove(key);
        }
        return b;
    }

    [JsonIgnore]
    public bool IsEmptyProgress => Slots.Count == 0 || Slots.Values.All(s => s.IsEmptyProgress);
}

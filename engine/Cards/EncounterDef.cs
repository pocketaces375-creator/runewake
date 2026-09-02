using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>A single drop entry in an encounter's drop table.</summary>
public class DropEntry
{
    [JsonPropertyName("card_id")]
    public string CardId { get; set; } = string.Empty;

    [JsonPropertyName("rate")]
    public double Rate { get; set; }
}

/// <summary>
/// Defines a duel encounter on the campaign map.
/// Includes wielder identity, deck, dialogue, rewards, and drops.
/// </summary>
public class EncounterDef
{
    /// <summary>Unique encounter ID matching the map node's encounter field (e.g. "r1_duel_wayfarer").</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name of the wielder (e.g. "The Wayfarer").</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Path to the wielder's portrait asset (e.g. "res://art/portraits/wayfarer.png").</summary>
    [JsonPropertyName("portrait")]
    public string? Portrait { get; set; }

    /// <summary>Card IDs in the wielder's deck (30 cards).</summary>
    [JsonPropertyName("deck")]
    public List<string> Deck { get; set; } = new();

    /// <summary>Dialogue lines shown before the duel.</summary>
    [JsonPropertyName("dialogue_intro")]
    public List<string>? DialogueIntro { get; set; }

    /// <summary>Dialogue lines shown after winning the duel.</summary>
    [JsonPropertyName("dialogue_outro")]
    public List<string>? DialogueOutro { get; set; }

    /// <summary>Base shard reward for winning.</summary>
    [JsonPropertyName("shard_reward")]
    public int ShardReward { get; set; }

    /// <summary>Dig charges rewarded for winning.</summary>
    [JsonPropertyName("dig_charge_reward")]
    public int DigChargeReward { get; set; }

    /// <summary>Fragment reward string (e.g. "verdant:2").</summary>
    [JsonPropertyName("fragment_reward")]
    public string? FragmentReward { get; set; }

    /// <summary>
    /// Card granted on first victory. Either a concrete card id, or the
    /// sentinel "CLASS_SIGNATURE" which resolves to the player class's
    /// signature card (defined in starter_decks.json). Bosses usually
    /// carry one of these.
    /// </summary>
    [JsonPropertyName("card_reward")]
    public string? CardReward { get; set; }

    /// <summary>Optional difficulty modifier for ELITE and WARDEN encounters.</summary>
    [JsonPropertyName("modifier")]
    public string? Modifier { get; set; }

    /// <summary>If true, this encounter fires tutorial popups instead of playing normally.</summary>
    [JsonPropertyName("is_tutorial")]
    public bool IsTutorial { get; set; }

    /// <summary>Drop table: card_id + probability entries, rolled on victory.</summary>
    [JsonPropertyName("drops")]
    public List<DropEntry> Drops { get; set; } = new();
}

/// <summary>
/// Container for a pack of encounter definitions (one file = one pack).
/// </summary>
public class EncounterPack
{
    [JsonPropertyName("encounters")]
    public List<EncounterDef> Encounters { get; set; } = new();
}
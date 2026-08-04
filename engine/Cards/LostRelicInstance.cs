using System;
using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// An instance of a minted Lost Relic — created when a player defeats a WARDEN_BOSS
/// or rare challenge encounter for the first time.
/// The engraving is permanent and cosmetic only.
/// </summary>
public class LostRelicInstance
{
    /// <summary>Unique identifier for this relic instance (UUID).</summary>
    [JsonPropertyName("relic_instance_id")]
    public string RelicInstanceId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The card ID that was minted (e.g. "relic_aelins_seal").</summary>
    [JsonPropertyName("card_id")]
    public string CardId { get; set; } = string.Empty;

    /// <summary>The player's display name (set at account creation).</summary>
    [JsonPropertyName("acquirer_name")]
    public string AcquirerName { get; set; } = "Adventurer";

    /// <summary>Date the relic was acquired (ISO 8601, e.g. "2026-08-04").</summary>
    [JsonPropertyName("acquired_at")]
    public string AcquiredAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-dd");

    /// <summary>Site description (e.g. "The Fallow Reach — Steward's Barrow").</summary>
    [JsonPropertyName("site")]
    public string Site { get; set; } = string.Empty;

    /// <summary>Global discovery index — which finder number this player is for this relic.</summary>
    [JsonPropertyName("discovery_index")]
    public int DiscoveryIndex { get; set; }

    /// <summary>Visual frame style for the engraving overlay (e.g. "verdant_gold", "ember_iron").</summary>
    [JsonPropertyName("engraving_style")]
    public string EngravingStyle { get; set; } = "default";

    /// <summary>
    /// Generates the engraving text line shown on the card frame.
    /// Format: "Unearthed by {AcquirerName} — {Site}, {AcquiredAt}."
    /// </summary>
    public string GetEngravingText()
    {
        // Parse the date into a friendlier format
        if (DateTime.TryParse(AcquiredAt, out var dt))
            return $"Unearthed by {AcquirerName} — {Site}, {dt:%d} {dt:MMMM} {dt.Year}.";
        return $"Unearthed by {AcquirerName} — {Site}, {AcquiredAt}.";
    }
}
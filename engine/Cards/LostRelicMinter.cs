using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// Definition of a Lost Relic that can be minted when certain conditions are met.
/// Maps encounter IDs or node types to specific relic cards.
/// </summary>
public class LostRelicDef
{
    /// <summary>The card ID that is minted as a Lost Relic.</summary>
    [JsonPropertyName("card_id")]
    public string CardId { get; set; } = string.Empty;

    /// <summary>Display name of the relic (e.g. "Aelin's Seal").</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Which map node encounter triggers minting (e.g. "r1_warden_boss").</summary>
    [JsonPropertyName("encounter_id")]
    public string EncounterId { get; set; } = string.Empty;

    /// <summary>Site description for the engraving line (e.g. "The Fallow Reach — Steward's Barrow").</summary>
    [JsonPropertyName("site")]
    public string Site { get; set; } = string.Empty;

    /// <summary>Visual frame style for the engraving overlay.</summary>
    [JsonPropertyName("engraving_style")]
    public string EngravingStyle { get; set; } = "default";
}

/// <summary>
/// Container for Lost Relic definitions.
/// </summary>
public class LostRelicPack
{
    [JsonPropertyName("relics")]
    public List<LostRelicDef> Relics { get; set; } = new();
}

/// <summary>
/// Static minter service that creates LostRelicInstance objects when a player
/// defeats a WARDEN_BOSS or qualifying encounter for the first time.
/// </summary>
public static class LostRelicMinter
{
    /// <summary>
    /// Attempt to mint a Lost Relic from the given encounter ID.
    /// Returns null if no relic definition matches the encounter.
    /// </summary>
    /// <param name="encounterId">The encounter ID that was defeated.</param>
    /// <param name="defs">All loaded Lost Relic definitions keyed by encounter ID.</param>
    /// <param name="acquirerName">Player's display name.</param>
    /// <param name="discoveryIndex">The global discovery index (increment per mint per card).</param>
    /// <param name="now">Optional override for the acquisition date (for testing).</param>
    public static LostRelicInstance? Mint(
        string encounterId,
        Dictionary<string, LostRelicDef> defs,
        string acquirerName,
        int discoveryIndex,
        DateTime? now = null)
    {
        if (!defs.TryGetValue(encounterId, out var def))
            return null;

        return new LostRelicInstance
        {
            RelicInstanceId = Guid.NewGuid().ToString(),
            CardId = def.CardId,
            AcquirerName = acquirerName,
            AcquiredAt = (now ?? DateTime.UtcNow).ToString("yyyy-MM-dd"),
            Site = def.Site,
            DiscoveryIndex = discoveryIndex,
            EngravingStyle = def.EngravingStyle
        };
    }
}
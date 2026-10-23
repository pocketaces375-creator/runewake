using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Runewake.Engine.Cards;

namespace Runewake.Engine.State;

/// <summary>
/// FABLE-061: one pack the player owns but has not opened. Its three cards were rolled ON THE SERVER when
/// the purchase was verified (supabase/packs.sql) — the phone only stores and reveals them.
/// </summary>
public class PendingPack
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("cards")] public List<string> Cards { get; set; } = new();
    [JsonPropertyName("rarities")] public List<string> Rarities { get; set; } = new();
    /// <summary>"play" (a Google Play purchase) or "preview" (a debug build's free test pack).</summary>
    [JsonPropertyName("source")] public string Source { get; set; } = "play";

    public PendingPack Clone() => new() { Id = Id ?? "", Cards = new(Cards ?? new()), Rarities = new(Rarities ?? new()), Source = Source ?? "play" };   // FABLE-095: null fields from a hand-edited file
}

/// <summary>A pack product in the shop. The price shown comes from Google Play when it is available.</summary>
public sealed record PackProduct(string Id, int Packs, string FallbackPrice);

/// <summary>
/// FABLE-061: paid card packs (Trikzos). What a pack holds, which cards may be in one, and how a phone
/// stores packs it bought. The server is the authority for the real thing; this mirrors its rules for the
/// shop's odds text, the debug preview, and tests.
///
///   • 3 cards: 1 Common, 1 Uncommon, 1 Rare — 1 in 10 of those Rares is a Relic instead.
///   • Only cards first discovered on the map 90+ days ago. Tower cards, Lost Relics and tokens: never.
///   • 1 pack $0.25 · 5 packs $0.99.
/// </summary>
public static class Packs
{
    public const int CardsPerPack = 3;
    public const double RelicChance = 0.10;
    public const int DaysUntilInPacks = 90;

    public static readonly IReadOnlyList<PackProduct> Products = new[]
    {
        new PackProduct("rw_pack_1", 1, "$0.25"),
        new PackProduct("rw_pack_5", 5, "$0.99"),
    };

    public static PackProduct? Product(string id) => Products.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// Store packs the server granted (a purchase, or ones owed from before). A pack already pending or
    /// already opened is never stored twice, however many times the server sends it.
    /// Returns the packs that were actually new.
    /// </summary>
    public static List<PendingPack> Receive(ProgressionState p, IEnumerable<PendingPack> grants)
    {
        var added = new List<PendingPack>();
        foreach (var g in grants)
        {
            if (string.IsNullOrEmpty(g.Id) || g.Cards.Count == 0) continue;
            if (p.PendingPacks.Any(x => x.Id == g.Id) || p.OpenedPackIds.Contains(g.Id)) continue;
            var copy = g.Clone();
            p.PendingPacks.Add(copy);
            added.Add(copy);
        }
        return added;
    }

    /// <summary>Open a pack: its cards go into the collection. Null if there is no such pack.</summary>
    public static PendingPack? Open(ProgressionState p, string packId)
    {
        var pack = p.PendingPacks.FirstOrDefault(x => x.Id == packId);
        if (pack == null) return null;
        foreach (var c in pack.Cards) p.AddCard(c);
        p.PendingPacks.Remove(pack);
        p.OpenedPackIds.Add(pack.Id);
        // a long-time buyer: keep the dedupe list bounded (the server stops resending once a pack is marked)
        while (p.OpenedPackIds.Count > 500) p.OpenedPackIds.RemoveAt(0);
        return pack;
    }

    /// <summary>
    /// The DEBUG preview's pack: the same shape as the server's roll, from whatever cards are loaded.
    /// Never used for a paid pack.
    /// </summary>
    public static PendingPack PreviewRoll(IEnumerable<CardDef> cards, ISet<string> neverInPacks, Random rng)
    {
        var pool = cards.Where(c => c.Type != CardType.TOKEN && !neverInPacks.Contains(c.Id)).ToList();
        string? Pick(Rarity r) { var of = pool.Where(c => c.Rarity == r).ToList(); return of.Count == 0 ? null : of[rng.Next(of.Count)].Id; }
        var top = rng.NextDouble() < RelicChance ? Rarity.RELIC : Rarity.RARE;
        var third = Pick(top);
        if (third == null) { top = top == Rarity.RELIC ? Rarity.RARE : Rarity.RELIC; third = Pick(top); }
        var ids = new[] { Pick(Rarity.COMMON), Pick(Rarity.UNCOMMON), third };
        return new PendingPack
        {
            Id = "preview-" + Guid.NewGuid().ToString("N")[..12],
            Cards = ids.Where(x => x != null).Select(x => x!).ToList(),
            Rarities = new() { "COMMON", "UNCOMMON", top.ToString() },
            Source = "preview",
        };
    }

    /// <summary>The odds as the shop states them (Google Play requires odds for random items).</summary>
    public static string OddsText(double relicChance = RelicChance) =>
        $"Every pack: 1 Common, 1 Uncommon and 1 Rare. {(int)Math.Round(relicChance * 100)}% of the time the Rare is a Relic instead.";
}

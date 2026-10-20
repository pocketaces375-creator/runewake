using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// FABLE-075: one plain sentence for every reward the game pays, shown when a reward is tapped
/// (Trikzos: "I don't know what tide fragments or dig shards are"). Rune fragments are called
/// RUNE PIECES on screen so they are never mistaken for Shards, the coin.
/// </summary>
public static class RewardHelp
{
    public const string Shards =
        "Shards are your coin. Spend them at the Merchant on the map to buy new cards.";

    public static string Stratum(string key) => key.ToLowerInvariant() switch
    {
        "ember" => "Ember", "tide" => "Tide", "dawn" => "Dawn", "verdant" => "Verdant", "hollow" => "Hollow",
        _ => key.Length > 0 ? char.ToUpperInvariant(key[0]) + key[1..].ToLowerInvariant() : key,
    };

    /// <summary>"3 of 4 for a rune" / "1 rune ready to forge".</summary>
    public static string PiecesProgress(int have)
    {
        int per = ForgeSystem.FragmentsPerForge;
        int ready = have / per;
        return ready > 0 ? (ready == 1 ? "1 rune ready to forge" : $"{ready} runes ready to forge") : $"{have} of {per} for a rune";
    }

    public static string RunePieces(string stratum, int have)
    {
        int per = ForgeSystem.FragmentsPerForge;
        string where = have >= per
            ? $"You have enough — open the Forge on the map to make a {stratum} rune."
            : $"You have {have} of {per}.";
        return $"Every {per} {stratum} rune pieces forge one {stratum} rune at the Forge (map, bottom left). " +
               $"Put runes on your Rune Page and they help you in every fight. {where}";
    }

    public static string Card(string tag, string? name)
    {
        string n = string.IsNullOrEmpty(name) ? "this card" : name!;
        return tag switch
        {
            "REWARD" => $"The prize for this fight: {n} is now in your collection. Add it to a deck in the Deck Builder.",
            "RELIC" => $"A lost relic: {n} is rare and now yours. Add it to a deck in the Deck Builder.",
            "NEW" => $"New card! {n} is now in your collection. Add it to a deck in the Deck Builder.",
            _ => $"Another copy of {n}. A deck holds one of each card — extra copies let it go in more than one deck at once.",
        };
    }
}

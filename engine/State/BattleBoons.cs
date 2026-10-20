using System.Collections.Generic;
using System.Linq;

namespace Runewake.Engine.State;

/// <summary>
/// FABLE-075: the Gods of Old answer before every campaign fight. The player picks ONE boon at the start of
/// the battle (this replaced the Shrine node, whose choices nobody could read on the map). Applied to a
/// freshly initialised duel, before the first action, on the player's own side only.
/// Tune the numbers here.
/// </summary>
public static class BattleBoons
{
    public sealed record Boon(string Id, string Name, string Effect, string Toast);

    public const int VigorAmount = 5;
    public const int InsightCards = 2;
    public const int AttunementAmount = 1;

    public static readonly IReadOnlyList<Boon> All = new[]
    {
        new Boon("vigor", "Endurance", $"Start with +{VigorAmount} Vigor",
            $"Endurance: +{VigorAmount} Vigor this fight"),
        new Boon("insight", "Insight", $"Draw {InsightCards} extra cards now",
            $"Insight: drew {InsightCards} extra cards"),
        new Boon("attune", "Attunement", $"+{AttunementAmount} Attunement every turn",
            $"Attunement: +{AttunementAmount} Attunement every turn this fight"),
    };

    public static Boon? Find(string? id) => All.FirstOrDefault(b => b.Id == id);

    /// <summary>Give <paramref name="player"/> the boon. False if the id is unknown.</summary>
    public static bool Apply(GameState state, int player, string id)
    {
        var p = state.Players[player];
        switch (id)
        {
            case "vigor":
                p.MaxVigor += VigorAmount;
                p.Vigor += VigorAmount;
                return true;
            case "insight":
                for (int i = 0; i < InsightCards && p.Deck.Count > 0 && p.Hand.Count < p.MaxHandSize; i++)
                {
                    var card = p.Deck[0];
                    p.Deck.RemoveAt(0);
                    card.Zone = Zone.Hand;
                    p.Hand.Add(card);
                }
                return true;
            case "attune":
                // the turn-1 Attune step has already run: lift this turn's pool and the ceiling it grows from
                p.AttunementMax += AttunementAmount;
                p.Attunement += AttunementAmount;
                return true;
            default:
                return false;
        }
    }
}

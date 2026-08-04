using System.Text.Json;
using System.Text.Json.Serialization;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Engine.Cards;

namespace Runewake.Sim;

/// <summary>
/// Result of a single simulated game.
/// </summary>
public sealed class GameResult
{
    [JsonPropertyName("game")]
    public int Game { get; init; }

    [JsonPropertyName("winner")]
    public int Winner { get; init; } // 0 or 1

    [JsonPropertyName("turns")]
    public int Turns { get; init; }

    [JsonPropertyName("p0_attunement_max")]
    public int P0AttunementMax { get; init; }

    [JsonPropertyName("p1_attunement_max")]
    public int P1AttunementMax { get; init; }

    [JsonPropertyName("p0_vigor")]
    public int P0Vigor { get; init; }

    [JsonPropertyName("p1_vigor")]
    public int P1Vigor { get; init; }
}

/// <summary>
/// Aggregated report for a batch simulation run.
/// </summary>
public sealed class BatchReport
{
    [JsonPropertyName("config")]
    public BatchConfig Config { get; init; } = new();

    [JsonPropertyName("results")]
    public List<GameResult> Results { get; init; } = new();

    [JsonPropertyName("p0_wins")]
    public int P0Wins => Results.Count(r => r.Winner == 0);

    [JsonPropertyName("p1_wins")]
    public int P1Wins => Results.Count(r => r.Winner == 1);

    [JsonPropertyName("total_games")]
    public int TotalGames => Results.Count;

    [JsonPropertyName("avg_turns")]
    public double AvgTurns => Results.Count > 0 ? Results.Average(r => r.Turns) : 0;

    [JsonPropertyName("win_rate_p0")]
    public double WinRateP0 => TotalGames > 0 ? (double)P0Wins / TotalGames : 0;

    /// <summary>
    /// Serializes this report to a compact JSON string.
    /// </summary>
    public string ToJson()
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };
        return JsonSerializer.Serialize(this, opts);
    }
}

/// <summary>
/// Configuration for a batch run, as parsed from CLI or test.
/// </summary>
public sealed class BatchConfig
{
    [JsonPropertyName("seed")]
    public ulong Seed { get; init; } = 42;

    [JsonPropertyName("games")]
    public int Games { get; init; } = 100;

    [JsonPropertyName("content_version")]
    public int ContentVersion { get; init; } = 1;

    [JsonPropertyName("deck_a")]
    public string DeckA { get; init; } = string.Empty;

    [JsonPropertyName("deck_b")]
    public string DeckB { get; init; } = string.Empty;

    /// <summary>
    /// Card definition IDs for deck A (loaded from JSON). Populated at runtime.
    /// </summary>
    [JsonIgnore]
    public List<string> DeckAIds { get; set; } = new();

    /// <summary>
    /// Card definition IDs for deck B (loaded from JSON). Populated at runtime.
    /// </summary>
    [JsonIgnore]
    public List<string> DeckBIds { get; set; } = new();
}

/// <summary>
/// Runs batch simulations between two bots and produces a report.
/// </summary>
public static class BatchRunner
{
    private static readonly GreedyBot Bot = new();

    /// <summary>
    /// Runs a batch of games. Each game gets a unique seed (baseSeed + gameIndex)
    /// so every game is a different random sequence.
    /// </summary>
    public static BatchReport Run(BatchConfig config)
    {
        var results = new List<GameResult>(config.Games);

        for (int i = 0; i < config.Games; i++)
        {
            ulong gameSeed = (ulong)((long)config.Seed + i * 100003);
            var gameConfig = new GameConfig
            {
                Seed = gameSeed,
                ContentVersion = config.ContentVersion,
                Player0DeckIds = new List<string>(config.DeckAIds),
                Player1DeckIds = new List<string>(config.DeckBIds),
            };

            var state = GameState.Initialize(gameConfig);
            int turns = 0;
            const int maxTurns = 200; // safety limit

            while (!state.IsGameOver && turns < maxTurns)
            {
                int playerIdx = state.CurrentPlayerIndex;
                var action = Bot.ChooseAction(state, playerIdx);
                if (action is null)
                    break;
                state = DuelEngine.Apply(state, action);
                turns++;

                // A turn is complete when both players have acted (back to P0)
                // TurnNumber in GameState increments after P1's EndTurn
            }

            // Determine winner from final state
            int winner = state.WinnerIndex ?? (state.Players[0].Vigor > state.Players[1].Vigor ? 0 : 1);
            int finalTurn = state.TurnNumber;

            results.Add(new GameResult
            {
                Game = i,
                Winner = winner,
                Turns = finalTurn,
                P0AttunementMax = state.Players[0].AttunementMax,
                P1AttunementMax = state.Players[1].AttunementMax,
                P0Vigor = state.Players[0].Vigor,
                P1Vigor = state.Players[1].Vigor,
            });
        }

        return new BatchReport
        {
            Config = config,
            Results = results,
        };
    }

    /// <summary>
    /// Loads a deck list from a JSON card pack file. Returns the list of card IDs.
    /// Each card in the pack is registered with CardRegistry.
    /// </summary>
    public static List<string> LoadDeckFromPack(string packPath)
    {
        var pack = CardLoader.LoadPack(packPath);
        var ids = new List<string>(pack.Count);

        foreach (var def in pack)
        {
            CardRegistry.Register(def);
            ids.Add(def.Id);
        }

        return ids;
    }
}
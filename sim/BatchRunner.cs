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

    [JsonPropertyName("combat_turns")]
    public int CombatTurns { get; init; }

    [JsonPropertyName("deviation_turns")]
    public int DeviationTurns { get; init; }

    [JsonPropertyName("p0_cards_in_hand")]
    public int P0CardsInHand { get; init; }

    [JsonPropertyName("p1_cards_in_hand")]
    public int P1CardsInHand { get; init; }
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

    [JsonPropertyName("total_combat_turns")]
    public int TotalCombatTurns => Results.Sum(r => r.CombatTurns);

    [JsonPropertyName("total_deviation_turns")]
    public int TotalDeviationTurns => Results.Sum(r => r.DeviationTurns);

    [JsonPropertyName("attack_deviation_rate")]
    public double AttackDeviationRate => TotalCombatTurns > 0 ? (double)TotalDeviationTurns / TotalCombatTurns : 0;

    [JsonPropertyName("avg_cards_in_hand_p0")]
    public double AvgCardsInHandP0 => Results.Count > 0 ? Results.Average(r => r.P0CardsInHand) : 0;

    [JsonPropertyName("avg_cards_in_hand_p1")]
    public double AvgCardsInHandP1 => Results.Count > 0 ? Results.Average(r => r.P1CardsInHand) : 0;

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

    [JsonPropertyName("player0_class")]
    public string Player0Class { get; init; } = string.Empty;

    [JsonPropertyName("player1_class")]
    public string Player1Class { get; init; } = string.Empty;
}

/// <summary>
/// Runs batch simulations between two bots and produces a report.
/// </summary>
public static class BatchRunner
{
    private static readonly GreedyBot Bot = new();

    /// <summary>
    /// Maps class name to artifact IDs (from launch_artifacts.json).
    /// </summary>
    private static readonly Dictionary<string, string[]> ClassArtifactMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["warrior"] = new[] { "artf_warrior_sword", "artf_warrior_shield" },
        ["mage"] = new[] { "artf_mage_wand", "artf_mage_aura" },
        ["thief"] = new[] { "artf_thief_dagger_whisper", "artf_thief_dagger_dusk" },
        ["cleric"] = new[] { "artf_cleric_censer", "artf_cleric_icon" },
        ["ranger"] = new[] { "artf_ranger_bow", "artf_ranger_quiver" },
        ["necromancer"] = new[] { "artf_necromancer_grimoire", "artf_necromancer_phylactery" },
        ["runesmith"] = new[] { "artf_runesmith_hammer", "artf_runesmith_anvil" },
    };

    /// <summary>
    /// Resolves artifact IDs for a given class name. Returns empty array if class not found.
    /// </summary>
    public static string[] GetArtifactIdsForClass(string className)
    {
        return ClassArtifactMap.TryGetValue(className, out var ids) ? ids : Array.Empty<string>();
    }

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

            // Resolve artifact IDs for each player
            var p0ArtifactIds = GetArtifactIdsForClass(config.Player0Class);
            var p1ArtifactIds = GetArtifactIdsForClass(config.Player1Class);

            var gameConfig = new GameConfig
            {
                Seed = gameSeed,
                ContentVersion = config.ContentVersion,
                Player0DeckIds = new List<string>(config.DeckAIds),
                Player1DeckIds = new List<string>(config.DeckBIds),
                Player0ArtifactIds = p0ArtifactIds,
                Player1ArtifactIds = p1ArtifactIds,
                Player0Class = config.Player0Class,
                Player1Class = config.Player1Class,
            };

            var state = GameState.Initialize(gameConfig);
            int turns = 0;
            const int maxTurns = 200; // safety limit

            // Attack deviation tracking
            int combatTurns = 0;
            int deviationTurns = 0;
            int lastActivePlayer = -1;
            bool turnHadAttack = false;
            int eligibleNotAttacked = 0;

            while (!state.IsGameOver && turns < maxTurns)
            {
                int playerIdx = state.CurrentPlayerIndex;

                // Track player turns for per-turn metrics
                if (playerIdx != lastActivePlayer)
                {
                    // Finalize previous turn metrics
                    if (lastActivePlayer >= 0 && turnHadAttack && eligibleNotAttacked > 0)
                    {
                        deviationTurns++;
                    }

                    // Start new turn tracking
                    lastActivePlayer = playerIdx;
                    turnHadAttack = false;
                    eligibleNotAttacked = CountEligibleNotAttacked(state, playerIdx);
                }

                var action = Bot.ChooseAction(state, playerIdx);
                if (action is null)
                    break;

                // Update tracking based on action type
                if (action is AttackAction)
                {
                    turnHadAttack = true;
                    // One attacker acted, decrement eligible-not-attacked
                    if (eligibleNotAttacked > 0)
                        eligibleNotAttacked--;
                }
                else if (action is EndTurnAction)
                {
                    // Finalize this turn
                    if (turnHadAttack && eligibleNotAttacked > 0)
                    {
                        deviationTurns++;
                    }
                    if (turnHadAttack)
                    {
                        combatTurns++;
                    }
                    turnHadAttack = false;
                    eligibleNotAttacked = 0;
                    lastActivePlayer = -1; // force fresh tracking on next player
                }
                // PlayCardAction — eligibleNotAttacked unchanged (creatures unchanged)

                state = DuelEngine.Apply(state, action);
                turns++;

                // A turn is complete when both players have acted (back to P0)
                // TurnNumber in GameState increments after P1's EndTurn
            }

            // Finalize last turn if game ended mid-turn
            if (turnHadAttack && eligibleNotAttacked > 0)
            {
                deviationTurns++;
            }
            if (turnHadAttack)
            {
                combatTurns++;
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
                CombatTurns = combatTurns,
                DeviationTurns = deviationTurns,
                P0CardsInHand = state.Players[0].Hand.Count,
                P1CardsInHand = state.Players[1].Hand.Count,
            });
        }

        return new BatchReport
        {
            Config = config,
            Results = results,
        };
    }

    /// <summary>
    /// Counts how many eligible (ready, non-exhausted, attack > 0) creatures
    /// for the given player have not yet attacked this turn.
    /// </summary>
    private static int CountEligibleNotAttacked(GameState state, int playerIndex)
    {
        var player = state.Player(playerIndex);
        int count = 0;
        for (int l = 0; l < 5; l++)
        {
            var occ = player.Lanes[l].Occupant;
            if (occ is not null && !occ.IsExhausted && !occ.HasAttackedThisTurn && occ.CurrentAttack > 0)
                count++;
        }
        return count;
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
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

    /// <summary>TASK-FUN-SIM-1: Turn of first creature death in the game.</summary>
    [JsonPropertyName("first_creature_death_turn")]
    public int FirstCreatureDeathTurn { get; init; }

    /// <summary>TASK-FUN-SIM-1: Final state vigor of player 0.</summary>
    [JsonPropertyName("final_p0_vigor")]
    public int FinalP0Vigor { get; init; }

    /// <summary>TASK-FUN-SIM-1: Final state vigor of player 1.</summary>
    [JsonPropertyName("final_p1_vigor")]
    public int FinalP1Vigor { get; init; }
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

    // ——— TASK-FUN-SIM-1 metrics ———

    [JsonPropertyName("avg_turns_first_creature_death")]
    public double AvgTurnsFirstCreatureDeath => Results.Count > 0
        ? Results.Where(r => r.FirstCreatureDeathTurn > 0).Select(r => (double)r.FirstCreatureDeathTurn).DefaultIfEmpty(0).Average()
        : 0;

    [JsonPropertyName("avg_final_p0_vigor")]
    public double AvgFinalP0Vigor => Results.Count > 0 ? Results.Average(r => r.FinalP0Vigor) : 0;

    [JsonPropertyName("avg_final_p1_vigor")]
    public double AvgFinalP1Vigor => Results.Count > 0 ? Results.Average(r => r.FinalP1Vigor) : 0;

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

    [JsonPropertyName("compensation_variant")]
    public int CompensationVariant { get; init; } = 0; // 0=baseline, 1=P1+1Attune, 2=P1+1Card, 3=both, 4=P0delay

    /// <summary>
    /// Optional opening rule to apply (e.g. "root_choked").
    /// When set, the sim applies the rule during GameConfig initialization.
    /// </summary>
    [JsonPropertyName("opening_rule")]
    public string? OpeningRule { get; init; }

    /// <summary>
    /// Player index (0 or 1) that owns the opening rule (the Warden).
    /// Defaults to 1 for backward compatibility.
    /// </summary>
    [JsonPropertyName("opening_rule_owner")]
    public int OpeningRuleOwner { get; init; } = 1;

    // ——— TASK-FUN-SIM-1 variant flags ———

    /// <summary>Variant (a): Starting Vigor 20.</summary>
    [JsonPropertyName("starting_vigor_20")]
    public bool StartingVigor20 { get; init; }

    /// <summary>Variant (b): INVOKE mode — artifact charges held until tapped.</summary>
    [JsonPropertyName("invoke_mode")]
    public bool InvokeMode { get; init; }

    /// <summary>Variant (c): ALTAR mode — lane 2 altar, edge lane hedge.</summary>
    [JsonPropertyName("altar_mode")]
    public bool AltarMode { get; init; }
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
        ["battlemage"] = new[] { "artf_battlemage_wand", "artf_battlemage_aura" },
        ["necromancer"] = new[] { "artf_necromancer_skull", "artf_necromancer_ritual_piece" },
        ["paladin"] = new[] { "artf_paladin_hammer", "artf_paladin_banner" },
        ["druid"] = new[] { "artf_druid_book_of_familiar", "artf_druid_elemental_bond" },
        ["rogue"] = new[] { "artf_rogue_dagger_dusk", "artf_rogue_dagger_whisper" },
        ["astrologist"] = new[] { "artf_astrologist_orb", "artf_astrologist_constellation_starlight" },
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
                OpeningRule = config.OpeningRule,
                OpeningRuleOwner = config.OpeningRuleOwner,
                MatchConfig = new MatchConfig
                {
                    StartingVigor20 = config.StartingVigor20,
                    InvokeMode = config.InvokeMode,
                    AltarMode = config.AltarMode,
                },
            };

            var state = GameState.Initialize(gameConfig);

            // Track first creature death turn
            int firstCreatureDeathTurn = 0;

            // Apply compensation variant after initialization (test harness only, not shipped code)
            ApplyCompensation(state, config.CompensationVariant);

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

                // Track first creature death turn
                if (firstCreatureDeathTurn == 0 && (state.TotalCreatureDiedCount[0] > 0 || state.TotalCreatureDiedCount[1] > 0))
                    firstCreatureDeathTurn = state.TurnNumber;

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
                FirstCreatureDeathTurn = firstCreatureDeathTurn,
                FinalP0Vigor = state.Players[0].Vigor,
                FinalP1Vigor = state.Players[1].Vigor,
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

    /// <summary>
    /// Applies a first-player-advantage compensation variant after game initialization.
    /// Test harness only — never changes shipped defaults.
    /// </summary>
    private static void ApplyCompensation(GameState state, int variant)
    {
        switch (variant)
        {
            case 0: // baseline — no changes
                break;

            case 1: // P1 gets +1 Attunement max on turn 1
                // Set P1's AttunementMax to 1 at Initialize; the normal Attune step
                // on P1's first turn adds +1, yielding AttunementMax=2.
                state.Players[1].AttunementMax = 1;
                state.Players[1].Attunement = 1;
                break;

            case 2: // P1 opening hand 6 instead of 5
                if (state.Players[1].Deck.Count > 0)
                {
                    var card = state.Players[1].Deck[0];
                    state.Players[1].Deck.RemoveAt(0);
                    card.Zone = Zone.Hand;
                    state.Players[1].Hand.Add(card);
                }
                break;

            case 3: // both b and c
                state.Players[1].AttunementMax = 1;
                state.Players[1].Attunement = 1;
                if (state.Players[1].Deck.Count > 0)
                {
                    var card = state.Players[1].Deck[0];
                    state.Players[1].Deck.RemoveAt(0);
                    card.Zone = Zone.Hand;
                    state.Players[1].Hand.Add(card);
                }
                break;

            case 4: // P0's turn-1 Attunement ramp delayed one turn
                // Undo the Attune step that GameState.Initialize applied to P0.
                state.Players[0].AttunementMax = 0;
                state.Players[0].Attunement = 0;
                break;
        }
    }
}
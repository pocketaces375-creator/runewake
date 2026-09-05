using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Runewake.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Runewake.Tests.Campaign;

/// <summary>
/// TASK-WARDEN-RULE-1: Sim the boss fight with/without opening rule.
/// TASK-ENGINE-GHOST-1: Seat-agnostic opening rules — same Warden from P0 or P1.
/// </summary>
[Collection("NonParallel")]
public class BossOpeningRuleSimulationTests : IDisposable
{
    private static readonly string ContentRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "content"));

    private static readonly string CardsDir = Path.Combine(ContentRoot, "cards");
    private static readonly string EncountersDir = Path.Combine(ContentRoot, "encounters");

    private readonly ITestOutputHelper _output;

    public BossOpeningRuleSimulationTests(ITestOutputHelper output)
    {
        _output = output;
        LoadAllCardsIntoRegistry();
    }

    public void Dispose()
    {
        CardRegistry.Clear();
    }

    private static void LoadAllCardsIntoRegistry()
    {
        CardRegistry.Clear();
        ArtifactRegistry.Clear();
        var allCards = new List<CardDef>();
        foreach (var file in Directory.GetFiles(CardsDir, "*.json"))
        {
            var cards = CardLoader.LoadPack(file);
            allCards.AddRange(cards);
        }
        CardRegistry.RegisterRange(allCards);

        // Load launch artifacts
        var artifactsPath = Path.Combine(ContentRoot, "artifacts", "launch_artifacts.json");
        if (File.Exists(artifactsPath))
            ArtifactLoader.LoadPack(artifactsPath);

        // Load variant artifacts
        var variantsDir = Path.Combine(ContentRoot, "artifacts", "variants");
        if (Directory.Exists(variantsDir))
        {
            int variantCount = ArtifactLoader.LoadAllVariants(variantsDir);
            if (variantCount > 0)
                System.Console.Error.WriteLine($"Loaded {variantCount} variant artifacts from {variantsDir}");
        }
    }

    private static List<string> LoadEncounterDeck(string encounterId)
    {
        var bossJson = File.ReadAllText(Path.Combine(EncountersDir, "region_01_boss.json"));
        var pack = EncounterLoader.LoadPackFromString(bossJson);
        var enc = pack.Encounters.FirstOrDefault(e => e.Id == encounterId)
            ?? throw new InvalidOperationException($"Encounter {encounterId} not found");
        return enc.Deck;
    }

    /// <summary>
    /// Build a player deck from all available common/cheap cards — representative mix.
    /// </summary>
    private static List<string> BuildPlayerDeck()
    {
        var allCardIds = CardRegistry.GetAll()
            .Where(d => d.Cost <= 3)
            .Select(d => d.Id)
            .Distinct()
            .ToList();

        // Shuffle and take 30 using a deterministic seed
        var rng = new Random(42);
        var shuffled = allCardIds.OrderBy(_ => rng.Next()).Take(DeckSize).ToList();

        // Fill to exactly 30 if we got fewer
        while (shuffled.Count < DeckSize)
        {
            shuffled.Add(allCardIds[rng.Next(allCardIds.Count)]);
        }

        return shuffled.Take(DeckSize).ToList();
    }

    private const int DeckSize = 30;
    private const int SimGames = 200;

    private BatchReport RunSim(string encounterId, string? openingRule, int openingRuleOwner = 1)
    {
        var bossDeck = LoadEncounterDeck(encounterId);
        var playerDeck = BuildPlayerDeck();

        var config = new BatchConfig
        {
            Seed = 42,
            Games = SimGames,
            ContentVersion = 1,
            DeckA = "player",
            DeckB = encounterId,
            Player0Class = "warrior",
            Player1Class = "warrior",
            OpeningRule = openingRule,
            OpeningRuleOwner = openingRuleOwner,
        };
        config.DeckAIds = playerDeck;
        config.DeckBIds = bossDeck;

        return BatchRunner.Run(config);
    }

    [Fact]
    public void BossDeck_WinPercent_WithOpeningRule()
    {
        var before = RunSim("r1_boss_warden_aelin", null);
        var after = RunSim("r1_boss_warden_aelin", "root_choked");

        _output.WriteLine("=== Boss Deck Win% BEFORE opening rule (no rule) ===");
        _output.WriteLine($"  P0 (player) wins: {before.P0Wins}/{before.TotalGames} = {before.WinRateP0:P2}");
        _output.WriteLine($"  P1 (boss)   wins: {before.P1Wins}/{before.TotalGames} = {1.0 - before.WinRateP0:P2}");
        _output.WriteLine($"  Avg turns: {before.AvgTurns:F1}");

        _output.WriteLine("");
        _output.WriteLine("=== Boss Deck Win% AFTER opening rule (root_choked) ===");
        _output.WriteLine($"  P0 (player) wins: {after.P0Wins}/{after.TotalGames} = {after.WinRateP0:P2}");
        _output.WriteLine($"  P1 (boss)   wins: {after.P1Wins}/{after.TotalGames} = {1.0 - after.WinRateP0:P2}");
        _output.WriteLine($"  Avg turns: {after.AvgTurns:F1}");

        double delta = after.WinRateP0 - before.WinRateP0;
        _output.WriteLine("");
        _output.WriteLine($"=== Delta (player win% after - before): {delta:P2} ===");

        // The rule should make the boss stronger (P1 win% up, P0 win% down).
        // No assertion on magnitude — this is an informational report.
        // But verify the sim ran correctly
        Assert.Equal(SimGames, before.TotalGames);
        Assert.Equal(SimGames, after.TotalGames);
    }

    [Fact]
    public void OpeningRule_SeatAgnostic_Symmetry()
    {
        // Run the same Warden rule with the boss in seat 0 (owner=0) and seat 1 (owner=1).
        // The opponent's leftmost lane should be buried in both cases — mirror outcome.
        // With deterministic seed, the mirrored state should have swapped player indexes.

        // Seat 1: Warden is P1 (owner=1), challenger is P0
        var seat1 = RunSim("r1_boss_warden_aelin", "root_choked", openingRuleOwner: 1);

        // Seat 0: Warden is P0 (owner=0), challenger is P1.
        // Swap decks so the boss deck is in P0 and the player deck is in P1.
        var bossDeck = LoadEncounterDeck("r1_boss_warden_aelin");
        var playerDeck = BuildPlayerDeck();

        var configSeat0 = new BatchConfig
        {
            Seed = 42,
            Games = SimGames,
            ContentVersion = 1,
            DeckA = "r1_boss_warden_aelin",
            DeckB = "player",
            Player0Class = "warrior",
            Player1Class = "warrior",
            OpeningRule = "root_choked",
            OpeningRuleOwner = 0,
        };
        configSeat0.DeckAIds = bossDeck;
        configSeat0.DeckBIds = playerDeck;

        var seat0 = BatchRunner.Run(configSeat0);

        _output.WriteLine("=== Seat-agnostic root_choked: Owner=1 (P1 boss) ===");
        _output.WriteLine($"  P0 (challenger) wins: {seat1.P0Wins}/{seat1.TotalGames} = {seat1.WinRateP0:P2}");
        _output.WriteLine($"  P1 (boss)       wins: {seat1.P1Wins}/{seat1.TotalGames} = {1.0 - seat1.WinRateP0:P2}");

        _output.WriteLine("");
        _output.WriteLine("=== Seat-agnostic root_choked: Owner=0 (P0 boss) ===");
        _output.WriteLine($"  P0 (boss)        wins: {seat0.P0Wins}/{seat0.TotalGames} = {seat0.WinRateP0:P2}");
        _output.WriteLine($"  P1 (challenger)  wins: {seat0.P1Wins}/{seat0.TotalGames} = {1.0 - seat0.WinRateP0:P2}");

        _output.WriteLine("");
        _output.WriteLine("=== Symmetry check ===");

        // The boss win rate should be equivalent regardless of seat:
        // seat1.P1Wins (boss=P1) should roughly equal seat0.P0Wins (boss=P0)
        double bossWinRateSeat1 = 1.0 - seat1.WinRateP0; // P1 wins when boss = P1
        double bossWinRateSeat0 = seat0.WinRateP0;        // P0 wins when boss = P0

        _output.WriteLine($"  Boss win% when seated P1: {bossWinRateSeat1:P2}");
        _output.WriteLine($"  Boss win% when seated P0: {bossWinRateSeat0:P2}");

        double diff = Math.Abs(bossWinRateSeat1 - bossWinRateSeat0);
        _output.WriteLine($"  Absolute difference: {diff:P2}");

        // The difference should be small — under 15% is evidence that seat
        // position does not dominate outcome (the same cards are played but
        // P0/P1 asymmetry from first-turn advantage still affects win rates).
        Assert.True(diff < 0.15,
            $"Boss win rate differs by {diff:P2} between seats — seat-agnostic rule broken.");

        Assert.Equal(SimGames, seat1.TotalGames);
        Assert.Equal(SimGames, seat0.TotalGames);
    }
}
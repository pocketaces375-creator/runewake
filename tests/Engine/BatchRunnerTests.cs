using Runewake.Engine.Cards;
using Runewake.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Runewake.Tests.Engine;

public class BatchRunnerTests
{
    private readonly ITestOutputHelper _output;

    public BatchRunnerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Registers a minimal test card pack in CardRegistry and returns deck IDs.
    /// </summary>
    private static List<string> RegisterTestPack()
    {
        CardRegistry.Clear();
        var defs = new[]
        {
            new CardDef
            {
                Id = "tst_soldier", Name = "Test Soldier", Set = "test",
                Type = CardType.CREATURE, Cost = 2, Attack = 2, Vigor = 2,
                Strata = Strata.VERDANT, Rarity = Rarity.COMMON,
            },
            new CardDef
            {
                Id = "tst_scout", Name = "Test Scout", Set = "test",
                Type = CardType.CREATURE, Cost = 1, Attack = 1, Vigor = 1,
                Strata = Strata.VERDANT, Rarity = Rarity.COMMON,
            },
            new CardDef
            {
                Id = "tst_tank", Name = "Test Tank", Set = "test",
                Type = CardType.CREATURE, Cost = 4, Attack = 3, Vigor = 5,
                Strata = Strata.VERDANT, Rarity = Rarity.UNCOMMON,
            },
        };
        CardRegistry.RegisterRange(defs);
        return defs.Select(d => d.Id).ToList();
    }

    [Fact]
    public void RunBatch_100Games_ProducesReport()
    {
        var allIds = RegisterTestPack();

        // Build 20-card decks: mix of soldier, scout, tank
        var deckA = new List<string>();
        var deckB = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            deckA.Add("tst_soldier");
            deckB.Add("tst_soldier");
        }
        for (int i = 0; i < 6; i++)
        {
            deckA.Add("tst_scout");
            deckB.Add("tst_scout");
        }
        for (int i = 0; i < 4; i++)
        {
            deckA.Add("tst_tank");
            deckB.Add("tst_tank");
        }

        var config = new BatchConfig
        {
            Seed = 42,
            Games = 100,
            DeckAIds = deckA,
            DeckBIds = deckB,
        };

        var report = BatchRunner.Run(config);

        // Verify report structure
        Assert.NotNull(report);
        Assert.Equal(100, report.TotalGames);
        Assert.Equal(100, report.Results.Count);
        Assert.True(report.P0Wins + report.P1Wins == report.TotalGames,
            $"Wins don't sum to total: P0={report.P0Wins} P1={report.P1Wins} total={report.TotalGames}");

        // All results should have valid data
        foreach (var result in report.Results)
        {
            Assert.InRange(result.Winner, 0, 1);
            Assert.InRange(result.Turns, 1, 200);
            Assert.InRange(result.P0AttunementMax, 0, 10);
            Assert.InRange(result.P1AttunementMax, 0, 10);
        }

        // Average turns should be reasonable (at least 1)
        Assert.True(report.AvgTurns > 0);

        _output.WriteLine($"Batch: {report.P0Wins}/{report.TotalGames} P0 wins ({report.WinRateP0:P1}), avg {report.AvgTurns:F1} turns");
    }

    [Fact]
    public void RunBatch_JSONSerialization_ProducesValidJson()
    {
        var allIds = RegisterTestPack();

        var config = new BatchConfig
        {
            Seed = 12345,
            Games = 10,
            DeckAIds = allIds.Take(5).ToList(),
            DeckBIds = allIds.Take(5).ToList(),
        };

        var report = BatchRunner.Run(config);
        var json = report.ToJson();

        Assert.NotNull(json);
        Assert.Contains("\"total_games\":10", json);
        Assert.Contains("\"p0_wins\"", json);
        Assert.Contains("\"p1_wins\"", json);
        Assert.Contains("\"avg_turns\"", json);
        Assert.Contains("\"results\"", json);

        _output.WriteLine(json);
    }

    [Fact]
    public void RunBatch_DifferentDecks_CanSeeImbalance()
    {
        var allIds = RegisterTestPack();

        // Deck A: all tanks (strong). Deck B: all scouts (weak)
        var deckA = new List<string>();
        var deckB = new List<string>();
        for (int i = 0; i < 20; i++)
        {
            deckA.Add("tst_tank");
            deckB.Add("tst_scout");
        }

        var config = new BatchConfig
        {
            Seed = 9999,
            Games = 50,
            DeckAIds = deckA,
            DeckBIds = deckB,
        };

        var report = BatchRunner.Run(config);

        // Tank deck (3/5) should win more than scout deck (1/1)
        Assert.True(report.P0Wins > report.P1Wins,
            $"Expected P0 (tanks) to win more, got P0={report.P0Wins} P1={report.P1Wins}");

        _output.WriteLine($"Tanks vs Scouts: P0={report.P0Wins}/{report.TotalGames} ({report.WinRateP0:P1})");
    }

    [Fact]
    public void RunBatch_SameSeed_ProducesDeterministicResults()
    {
        var allIds = RegisterTestPack();

        var deckIds = new List<string>();
        for (int i = 0; i < 20; i++)
            deckIds.Add("tst_soldier");

        var config = new BatchConfig
        {
            Seed = 7777,
            Games = 25,
            DeckAIds = deckIds,
            DeckBIds = deckIds,
        };

        var report1 = BatchRunner.Run(config);

        // Reset and run again with same config
        RegisterTestPack();
        var report2 = BatchRunner.Run(config);

        // Identical reports (all results should match)
        Assert.Equal(report1.TotalGames, report2.TotalGames);
        Assert.Equal(report1.P0Wins, report2.P0Wins);
        Assert.Equal(report1.P1Wins, report2.P1Wins);
        Assert.Equal(report1.AvgTurns, report2.AvgTurns);

        for (int i = 0; i < report1.Results.Count; i++)
        {
            Assert.Equal(report1.Results[i].Winner, report2.Results[i].Winner);
            Assert.Equal(report1.Results[i].Turns, report2.Results[i].Turns);
        }
    }
}
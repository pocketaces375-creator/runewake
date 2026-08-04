using Runewake.Engine.Cards;
using Runewake.Sim;

if (args.Length < 2 || args[1] != "run")
{
    Console.WriteLine("Usage: Runewake.Sim run --deck-a <path> --deck-b <path> [--games <N>] [--seed <N>]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --deck-a <path>       Path to JSON card pack for player A (required)");
    Console.WriteLine("  --deck-b <path>       Path to JSON card pack for player B (required)");
    Console.WriteLine("  --games <N>           Number of games to simulate (default: 100)");
    Console.WriteLine("  --seed <N>            Base seed for deterministic RNG (default: 42)");
    return;
}

// Parse arguments
string? deckA = null, deckB = null;
int games = 100;
ulong seed = 42;

for (int i = 2; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--deck-a" when i + 1 < args.Length:
            deckA = args[++i];
            break;
        case "--deck-b" when i + 1 < args.Length:
            deckB = args[++i];
            break;
        case "--games" when i + 1 < args.Length && int.TryParse(args[++i], out var g):
            games = g;
            break;
        case "--seed" when i + 1 < args.Length && ulong.TryParse(args[++i], out var s):
            seed = s;
            break;
    }
}

if (deckA is null || deckB is null)
{
    Console.Error.WriteLine("Error: both --deck-a and --deck-b are required.");
    Environment.Exit(1);
}

if (!File.Exists(deckA))
{
    Console.Error.WriteLine($"Error: deck-a file not found: {deckA}");
    Environment.Exit(1);
}

if (!File.Exists(deckB))
{
    Console.Error.WriteLine($"Error: deck-b file not found: {deckB}");
    Environment.Exit(1);
}

// Load decks
var deckAIds = BatchRunner.LoadDeckFromPack(deckA);
var deckBIds = BatchRunner.LoadDeckFromPack(deckB);

Console.Error.WriteLine($"Loaded deck A: {deckA} ({deckAIds.Count} cards)");
Console.Error.WriteLine($"Loaded deck B: {deckB} ({deckBIds.Count} cards)");
Console.Error.WriteLine($"Running {games} games with seed {seed}...");

var config = new BatchConfig
{
    Seed = seed,
    Games = games,
    DeckA = deckA,
    DeckB = deckB,
    DeckAIds = deckAIds,
    DeckBIds = deckBIds,
};

var report = BatchRunner.Run(config);

// Output report as JSON to stdout
Console.WriteLine(report.ToJson());

Console.Error.WriteLine($"Done. P0 wins: {report.P0Wins}/{report.TotalGames} ({report.WinRateP0:P1}), avg turns: {report.AvgTurns:F1}");
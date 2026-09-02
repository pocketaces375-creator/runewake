using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Sim;

if (args.Length < 1)
{
    PrintUsage();
    return;
}

string command = args[0];

switch (command)
{
    case "run":
        RunCommand(args);
        break;
    case "validate-card":
        ValidateCardCommand(args);
        break;
    default:
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        break;
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  Runewake.Sim run --deck-a <path> --deck-b <path> [--games <N>] [--seed <N>]");
    Console.WriteLine("                    [--artifacts-path <path>] [--class-a <name>] [--class-b <name>]");
    Console.WriteLine("                    [--compensation <0|1|2|3|4>]");
    Console.WriteLine("  Runewake.Sim validate-card <card-file>");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  run              Run a batch simulation between two bots");
    Console.WriteLine("  validate-card    Validate a JSON card pack file");
}

static void RunCommand(string[] args)
{
    string? deckA = null, deckB = null;
    int games = 100;
    ulong seed = 42;
    string? artifactsPath = null;
    string? classA = null;
    string? classB = null;
    int compensationVariant = 0;

    for (int i = 1; i < args.Length; i++)
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
            case "--artifacts-path" when i + 1 < args.Length:
                artifactsPath = args[++i];
                break;
            case "--class-a" when i + 1 < args.Length:
                classA = args[++i];
                break;
            case "--class-b" when i + 1 < args.Length:
                classB = args[++i];
                break;
            case "--compensation" when i + 1 < args.Length && int.TryParse(args[++i], out var cv):
                compensationVariant = cv;
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

    // Load artifacts if path provided
    if (artifactsPath is not null)
    {
        if (!File.Exists(artifactsPath))
        {
            Console.Error.WriteLine($"Error: artifacts file not found: {artifactsPath}");
            Environment.Exit(1);
        }
        int count = ArtifactLoader.LoadPack(artifactsPath);
        Console.Error.WriteLine($"Loaded {count} artifact definitions.");
    }

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
        Player0Class = classA ?? "",
        Player1Class = classB ?? "",
        CompensationVariant = compensationVariant,
    };

    var report = BatchRunner.Run(config);
    Console.WriteLine(report.ToJson());
    Console.Error.WriteLine($"Done. P0 wins: {report.P0Wins}/{report.TotalGames} ({report.WinRateP0:P1}), avg turns: {report.AvgTurns:F1}, combat turns deviating: {report.TotalDeviationTurns}/{report.TotalCombatTurns} ({report.AttackDeviationRate:P1})");
}

static void ValidateCardCommand(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: Runewake.Sim validate-card <card-file>");
        Environment.Exit(1);
    }

    string path = args[1];

    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"Error: file not found: {path}");
        Environment.Exit(1);
    }

    string json = File.ReadAllText(path);
    List<CardDef>? cards;
    try
    {
        cards = JsonSerializer.Deserialize<List<CardDef>>(json, CardLoader.JsonOptions);
    }
    catch (JsonException ex)
    {
        Console.Error.WriteLine($"Error: invalid JSON: {ex.Message}");
        Environment.Exit(1);
        return;
    }

    if (cards is null || cards.Count == 0)
    {
        Console.Error.WriteLine("Error: card pack is empty or null.");
        Environment.Exit(1);
    }

    int totalErrors = 0;
    foreach (var card in cards)
    {
        var errors = CardValidator.Validate(card);
        string status = errors.Count == 0 ? "✓" : "✗";
        Console.Out.WriteLine($"[{status}] {card.Id} ({card.Name})");

        foreach (var err in errors)
        {
            Console.Out.WriteLine($"    - {err}");
            totalErrors++;
        }
    }

    Console.Error.WriteLine($"Validated {cards.Count} cards, {totalErrors} error(s).");
    if (totalErrors > 0)
        Environment.Exit(1);
}
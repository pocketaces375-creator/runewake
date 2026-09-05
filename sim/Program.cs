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
    Console.WriteLine("                    [--starting-vigor-20] [--invoke-mode] [--altar-mode]");
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
    bool startingVigor20 = false;
    bool invokeMode = false;
    bool altarMode = false;

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
            case "--starting-vigor-20":
                startingVigor20 = true;
                break;
            case "--invoke-mode":
                invokeMode = true;
                break;
            case "--altar-mode":
                altarMode = true;
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
        Console.Error.WriteLine($"Loaded {count} artifact definitions from {artifactsPath}");

        // Load variant artifacts from sibling variants/ directory
        var artifactsDir = Path.GetDirectoryName(artifactsPath);
        if (artifactsDir is not null)
        {
            var variantsDir = Path.Combine(artifactsDir, "variants");
            if (Directory.Exists(variantsDir))
            {
                int variantCount = ArtifactLoader.LoadAllVariants(variantsDir);
                if (variantCount > 0)
                    Console.Error.WriteLine($"Loaded {variantCount} variant artifacts from {variantsDir}");
            }
        }
    }

    var deckAIds = BatchRunner.LoadDeckFromPack(deckA);
    var deckBIds = BatchRunner.LoadDeckFromPack(deckB);

    // Load the full stratum card packs so CardRegistry has real definitions
    // (starter decks are ID-only references, not full CardDefs).
    // Stratum packs must load AFTER starter decks so they overwrite partial defs.
    var contentDir = Path.GetDirectoryName(Path.GetFullPath(deckA ?? "."));
    string contentCardsDir;
    if (contentDir is not null && Directory.Exists(Path.Combine(contentDir, "..", "content", "cards")))
        contentCardsDir = Path.GetFullPath(Path.Combine(contentDir, "..", "content", "cards"));
    else if (Directory.Exists("content/cards"))
        contentCardsDir = Path.GetFullPath("content/cards");
    else
        contentCardsDir = "/home/fictive/runewake/content/cards";

    if (Directory.Exists(contentCardsDir))
    {
        foreach (var packFile in Directory.GetFiles(contentCardsDir, "*.json"))
        {
            // LoadPack registers full CardDefs, overwriting the partial ID-only defs
            var pack = CardLoader.LoadPack(packFile);
            CardRegistry.RegisterRange(pack);
            Console.Error.WriteLine($"Loaded card definitions from {packFile}");
        }
    }
    else
    {
        Console.Error.WriteLine($"Warning: content/cards directory not found at {contentCardsDir}");
    }

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
        StartingVigor20 = startingVigor20,
        InvokeMode = invokeMode,
        AltarMode = altarMode,
    };

    var report = BatchRunner.Run(config);
    string variantTags = GetVariantTags();
    Console.WriteLine(report.ToJson());
    Console.Error.WriteLine($"{variantTags}Done. P0 wins: {report.P0Wins}/{report.TotalGames} ({report.WinRateP0:P1}), avg turns: {report.AvgTurns:F1}, avg first death: {report.AvgTurnsFirstCreatureDeath:F1}t, combat turns deviating: {report.TotalDeviationTurns}/{report.TotalCombatTurns} ({report.AttackDeviationRate:P1})");

    string GetVariantTags()
        {
            var tags = new List<string>();
            if (startingVigor20) tags.Add("Vigor20");
            if (invokeMode) tags.Add("INVOKE");
            if (altarMode) tags.Add("ALTAR");
            return tags.Count > 0 ? $"[{string.Join("+", tags)}] " : "";
        }
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
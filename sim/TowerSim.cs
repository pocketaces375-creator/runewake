using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.Coop;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Engine.Tower;
using Runewake.Engine.World;

namespace Runewake.Sim;

/// <summary>
/// FABLE-020 — is a Tower floor as hard as it is meant to be?
///
///   dotnet run --project sim -- tower-sim [--root .] [--floor 1] [--games 10] [--raids 20]
///
/// 1. Validates the floor (every place reachable, 30-card decks of real cards,
///    one boss, keys wired).
/// 2. Every fight vs two kinds of player deck: the class STARTER decks, and
///    "TOP" decks (the 30 strongest cards a player of that class could own).
/// 3. The raid: 1–5 players with TOP decks against the shared pool.
///
/// The design target for floor 1 (Trikzos: "hard enough that we don't have to
/// worry about floor 2 for a long time … often requiring all the top cards to
/// beat this boss even once"): starters mostly lose in the wings; top decks win
/// the wings; the Colossus falls rarely even to five top decks, never to one.
/// </summary>
public static class TowerSim
{
    public static int Run(string[] args)
    {
        string root = "."; int floorNo = 1, games = 10, raids = 20;
        for (int i = 1; i < args.Length; i++)
            switch (args[i])
            {
                case "--root" when i + 1 < args.Length: root = args[++i]; break;
                case "--floor" when i + 1 < args.Length: floorNo = int.Parse(args[++i]); break;
                case "--games" when i + 1 < args.Length: games = int.Parse(args[++i]); break;
                case "--raids" when i + 1 < args.Length: raids = int.Parse(args[++i]); break;
            }

        var content = Path.Combine(root, "content");
        var cards = Directory.GetFiles(Path.Combine(content, "cards"), "*.json").SelectMany(CardLoader.LoadPack).ToList();
        CardRegistry.Clear(); CardRegistry.RegisterRange(cards);
        var artPath = Path.Combine(content, "artifacts", "launch_artifacts.json");
        if (File.Exists(artPath)) ArtifactLoader.LoadPack(artPath);

        var floor = TowerFloorDef.FromJson(File.ReadAllText(Path.Combine(content, "tower", $"floor_{floorNo:000}.json")));
        var errs = floor.Validate(id => CardRegistry.Get(id) != null);
        if (errs.Count > 0)
        {
            Console.WriteLine($"FLOOR {floorNo} INVALID:"); foreach (var e in errs) Console.WriteLine("  " + e);
            return 1;
        }
        Console.WriteLine($"Floor {floor.Floor} \"{floor.Title}\": {floor.Places.Count} places — valid.");

        using var starterDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "client", "content", "decks", "starter_decks.json")));
        var starters = starterDoc.RootElement.GetProperty("starters").EnumerateArray()
            .Select(s => (cls: s.GetProperty("class_id").GetString()!, deck: s.GetProperty("cards").EnumerateArray().Select(x => x.GetString()!).ToList()))
            .ToList();
        using var classDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "client", "content", "classes.json")));
        var classStrata = classDoc.RootElement.EnumerateArray().ToDictionary(c => c.GetProperty("id").GetString()!, c => c.GetProperty("strata").GetString()!);
        var tops = starters.Select(s => (s.cls, deck: TopDeck(cards, classStrata.GetValueOrDefault(s.cls, "VERDANT")))).ToList();

        var bot = new GreedyBot();
        int crashes = 0;
        Console.WriteLine($"\n{"place",-30} {"kind",-7} {"vigor",5} {"starter win",12} {"top-deck win",13}");
        foreach (var p in floor.Places.Where(p => p.Encounter != null && p.Kind != TowerPlaceKind.Boss))
        {
            double sw = WinRate(bot, starters, p.Encounter!, games, ref crashes);
            double tw = WinRate(bot, tops, p.Encounter!, games, ref crashes);
            Console.WriteLine($"{p.Name,-30} {p.Kind,-7} {p.Encounter!.EnemyVigor ?? 25,5} {sw,11:P0} {tw,12:P0}");
        }

        var boss = floor.Boss;
        Console.WriteLine($"\nRAID: {boss.Name} — shared pool {floor.Raid.SharedPool}, {floor.Raid.MaxRounds} rounds max, TOP decks");
        for (int n = 1; n <= floor.Raid.MaxPlayers; n++)
        {
            int won = 0; double poolLeft = 0; double rounds = 0;
            for (int r = 0; r < raids; r++)
            {
                try
                {
                    var cfg = new ExpeditionConfig
                    {
                        Kind = ExpeditionKind.Raid, WinRule = WinRule.SharedPool, Seed = StableHash.Of("raid", floorNo, n, r),
                        SharedPool = floor.Raid.PoolFor(n), MaxRounds = floor.Raid.MaxRounds, Encounter = boss.Encounter!,
                        EncounterClass = floor.Raid.BossClass ?? "", EncounterArtifacts = BatchRunner.GetArtifactIdsForClass(floor.Raid.BossClass ?? ""),
                        Seats = Enumerable.Range(0, n).Select(i =>
                        {
                            var t = tops[(r + i) % tops.Count];
                            return new SeatConfig { Seat = i, ClassId = t.cls, Deck = t.deck, Artifacts = BatchRunner.GetArtifactIdsForClass(t.cls) };
                        }).ToList(),
                    };
                    var x = new Expedition(cfg, (s, pi) => bot.ChooseAction(s, pi));
                    int guard = 0;
                    while (x.Outcome == ExpeditionOutcome.Running && guard++ < 5000)
                        foreach (var b in x.Boards)
                        {
                            int g2 = 0, round = x.Round;
                            while (x.Round == round && x.Outcome == ExpeditionOutcome.Running && b.Active && !b.EndedTurn && g2++ < 200)
                                x.Submit(b.Seat.Seat, round, bot.ChooseAction(b.State, 0) ?? new EndTurnAction { PlayerIndex = 0 }, out _);
                        }
                    if (x.Outcome == ExpeditionOutcome.Victory) won++;
                    poolLeft += x.PoolRemaining ?? 0; rounds += x.Round;
                }
                catch (Exception ex) { crashes++; Console.WriteLine($"CRASH raid n={n} #{r}: {ex.GetType().Name}: {ex.Message}"); }
            }
            Console.WriteLine($"  {n} player(s): win {100.0 * won / raids,5:F1}%   avg pool left {poolLeft / raids,5:F0}   avg rounds {rounds / raids,4:F1}");
        }
        Console.WriteLine(crashes == 0 ? "\nno crashes" : $"\n{crashes} CRASHES");
        return crashes == 0 ? 0 : 1;
    }

    /// <summary>The 30 strongest cards a player of a stratum could own (singleton): their stratum first, then the best of the rest.</summary>
    public static List<string> TopDeck(List<CardDef> all, string strata)
    {
        double V(CardDef c) => c.Type == CardType.CREATURE
            ? (c.Attack ?? 0) + (c.Vigor ?? 0) + 1.5 * c.Keywords.Count(k => k is "GUARD" or "VENOM" or "PIERCE" or "SWIFT" or "WARD" or "REACH") + 0.8 * (int)c.Rarity - 0.35 * c.Cost
            : 2.0 * c.Cost + 1.5 * (int)c.Rarity;
        var pool = EncounterForge.PlayablePool(all);
        var own = pool.Where(c => c.Strata.ToString() == strata).OrderByDescending(V).ThenBy(c => c.Id).Take(20).ToList();
        var rest = pool.Where(c => !own.Contains(c)).OrderByDescending(V).ThenBy(c => c.Id).Take(30 - own.Count);
        return own.Concat(rest).Select(c => c.Id).ToList();
    }

    private static double WinRate(GreedyBot bot, List<(string cls, List<string> deck)> decks, EncounterDef enc, int games, ref int crashes)
    {
        int won = 0, n = 0;
        for (int g = 0; g < games; g++)
        {
            var (cls, deck) = decks[g % decks.Count];
            try
            {
                var s = GameState.Initialize(new GameConfig
                {
                    Seed = StableHash.Of(enc.Id, g), Player0DeckIds = deck, Player1DeckIds = enc.Deck,
                    Player0Class = cls, Player0ArtifactIds = BatchRunner.GetArtifactIdsForClass(cls),
                    Player1Class = enc.Class ?? "", Player1ArtifactIds = BatchRunner.GetArtifactIdsForClass(enc.Class ?? ""),
                    OpeningRule = enc.OpeningRule, Player1StartingVigor = enc.EnemyVigor, Player1BonusAttunement = enc.EnemyBonusAttunement, BossRules = enc.BossRules,
                });
                int a = 0;
                while (!s.IsGameOver && a++ < 4000) { var act = bot.ChooseAction(s, s.CurrentPlayerIndex); if (act == null) break; s = DuelEngine.Apply(s, act); }
                if ((s.WinnerIndex ?? (s.Players[0].Vigor > s.Players[1].Vigor ? 0 : 1)) == 0) won++;
                n++;
            }
            catch (Exception ex) { crashes++; Console.WriteLine($"CRASH {enc.Id}: {ex.GetType().Name}: {ex.Message}"); }
        }
        return n == 0 ? 0 : (double)won / n;
    }
}

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Engine.World;

namespace Runewake.Sim;

/// <summary>
/// FABLE-020 — proof that the endless world works, without a phone.
///
///   dotnet run --project sim -- world-soak [--root .] [--pages 100] [--biome elvenwood]
///                                           [--class all|warrior|...] [--games 4]
///
/// Walks the road N pages deep (through as many areas as that takes). On every
/// page it:
///   1. explores like a player: clears reachable places one at a time until the
///      guardian falls, checking there is never a dead end and counting how many
///      open choices the player has at the widest point;
///   2. plays real bot duels — the class starter deck against the page's first
///      fight, an elite if there is one, and the guardian — with the generated
///      encounter's own Vigor/Attunement, and records wins, turns and crashes.
/// Exit code 0 only if every page was completable and no duel threw.
/// </summary>
public static class WorldSoak
{
    public static int Run(string[] args)
    {
        string root = ".";
        int pages = 100, games = 4;
        string biome = "elvenwood", cls = "all";
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--root" when i + 1 < args.Length: root = args[++i]; break;
                case "--pages" when i + 1 < args.Length: pages = int.Parse(args[++i]); break;
                case "--games" when i + 1 < args.Length: games = int.Parse(args[++i]); break;
                case "--biome" when i + 1 < args.Length: biome = args[++i]; break;
                case "--class" when i + 1 < args.Length: cls = args[++i]; break;
            }
        }

        var content = Path.Combine(root, "content");
        var cards = Directory.GetFiles(Path.Combine(content, "cards"), "*.json").SelectMany(CardLoader.LoadPack).ToList();
        CardRegistry.Clear();
        CardRegistry.RegisterRange(cards);
        var artPath = Path.Combine(content, "artifacts", "launch_artifacts.json");
        if (File.Exists(artPath)) ArtifactLoader.LoadPack(artPath);
        var atlas = AtlasDef.FromJson(File.ReadAllText(Path.Combine(content, "world", "atlas.json")));
        var gen = new WorldGenerator(atlas);
        var pool = EncounterForge.PlayablePool(cards);

        using var starterDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "client", "content", "decks", "starter_decks.json")));
        var starters = starterDoc.RootElement.GetProperty("starters").EnumerateArray()
            .Where(s => cls == "all" || s.GetProperty("class_id").GetString() == cls)
            .Select(s => (cls: s.GetProperty("class_id").GetString()!, deck: s.GetProperty("cards").EnumerateArray().Select(x => x.GetString()!).ToList()))
            .ToList();

        var bot = new GreedyBot();
        var progress = new WorldProgress(gen) { CrossroadsOpen = true };
        progress.OpenedAreas.Add(new AreaAddress(biome, 0));   // hidden biomes: as if its Gateway was found
        var addr = new PageAddress(biome, 0, 0);
        var rows = new List<string>();
        int failures = 0, duels = 0, crashes = 0, wins = 0;
        long totalBlips = 0; int maxFrontier = 0;
        var sw = Stopwatch.StartNew();

        for (int step = 0; step < pages; step++)
        {
            var page = gen.Page(addr);
            totalBlips += page.Blips.Count;

            // 1. Explore like a player: always take the reachable place nearest
            //    the entry side, never skip ahead, until the guardian falls.
            int frontierPeak = 0, guard = 0;
            while (!progress.Cleared.Contains(page.Guardian.Id) && guard++ < 1000)
            {
                var frontier = progress.Frontier(page).ToList();
                frontierPeak = Math.Max(frontierPeak, frontier.Count);
                if (frontier.Count == 0) { failures++; Console.WriteLine($"DEAD END on {page.Address}"); break; }
                var next = frontier.OrderBy(b => b.Column).ThenBy(b => b.Index).First();
                if (!progress.Visit(page, next)) { failures++; Console.WriteLine($"VISIT REFUSED {next.Id}"); break; }
                progress.Clear(page, next);
            }
            maxFrontier = Math.Max(maxFrontier, frontierPeak);

            // 2. Real duels on a sample of the page's fights.
            var sample = new List<Blip>();
            var firstFight = page.Blips.FirstOrDefault(b => b.IsEntry && EncounterForge.IsFight(b));
            if (firstFight != null) sample.Add(firstFight);
            var elite = page.Blips.FirstOrDefault(b => b.Kind == BlipKind.Elite);
            if (elite != null) sample.Add(elite);
            sample.Add(page.Guardian);
            int pageWins = 0, pageGames = 0; double turns = 0;
            foreach (var b in sample)
            {
                var enc = EncounterForge.For(atlas, page, b, pool);
                for (int gI = 0; gI < games; gI++)
                {
                    duels++; pageGames++;
                    var (sCls, sDeck) = starters[(int)(StableHash.Of(b.Id, gI) % (ulong)starters.Count)];
                    try
                    {
                        var (won, t) = Play(bot, sDeck, sCls, enc, StableHash.Of(b.Id, gI));
                        if (won) { wins++; pageWins++; }
                        turns += t;
                    }
                    catch (Exception ex)
                    {
                        crashes++;
                        Console.WriteLine($"CRASH {b.Id} game {gI}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            var kinds = page.Blips.GroupBy(b => b.Kind).ToDictionary(g => g.Key, g => g.Count());
            rows.Add($"{page.Address,-24} blips={page.Blips.Count,3} frontier_peak={frontierPeak,2} " +
                     $"elite={kinds.GetValueOrDefault(BlipKind.Elite),2} event={kinds.GetValueOrDefault(BlipKind.Event),2} " +
                     $"lore={kinds.GetValueOrDefault(BlipKind.Lore)} gate={kinds.GetValueOrDefault(BlipKind.Gateway)} " +
                     $"guardian={page.Guardian.Kind,-8} diff={page.Guardian.Difficulty:F2} " +
                     $"win={(pageGames == 0 ? 0 : 100.0 * pageWins / pageGames),5:F1}% turns={(pageGames == 0 ? 0 : turns / pageGames),4:F1}");
            addr = gen.NextPage(addr);
        }

        foreach (var r in rows) Console.WriteLine(r);
        Console.WriteLine($"\nWORLD SOAK: {pages} pages ({biome}, {cls} starter), {totalBlips} places, " +
                          $"{duels} duels, win {100.0 * wins / Math.Max(1, duels):F1}%, crashes {crashes}, dead ends {failures}, " +
                          $"widest choice {maxFrontier} open places, {sw.Elapsed.TotalSeconds:F1}s");
        return failures == 0 && crashes == 0 ? 0 : 1;
    }

    private static (bool won, int turns) Play(IGameBot bot, List<string> playerDeck, string cls, EncounterDef enc, ulong seed)
    {
        var state = GameState.Initialize(new GameConfig
        {
            Seed = seed,
            Player0DeckIds = new List<string>(playerDeck),
            Player1DeckIds = new List<string>(enc.Deck),
            Player0ArtifactIds = BatchRunner.GetArtifactIdsForClass(cls),
            Player0Class = cls,
            Player1ArtifactIds = BatchRunner.GetArtifactIdsForClass(enc.Class ?? ""),
            Player1Class = enc.Class ?? "",
            Player1StartingVigor = enc.EnemyVigor,
            Player1BonusAttunement = enc.EnemyBonusAttunement,
        });
        int actions = 0;
        while (!state.IsGameOver && actions++ < 4000)
        {
            var a = bot.ChooseAction(state, state.CurrentPlayerIndex);
            if (a is null) break;
            state = DuelEngine.Apply(state, a);
        }
        int winner = state.WinnerIndex ?? (state.Players[0].Vigor > state.Players[1].Vigor ? 0 : 1);
        return (winner == 0, state.TurnNumber);
    }
}

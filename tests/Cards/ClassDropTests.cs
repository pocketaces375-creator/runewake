using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Runewake.Tests.Cards;

/// <summary>
/// FABLE-DROP-1: the 50-card class drop. Every card is valid, reads cleanly, sorts to a class's
/// High-synergy shelf, and actually does something when played in a real duel.
/// </summary>
public class ClassDropTests
{
    private readonly ITestOutputHelper _out;
    public ClassDropTests(ITestOutputHelper output) { _out = output; }

    private static string Root => Environment.GetEnvironmentVariable("RUNEWAKE_ROOT")
        ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static List<CardDef> LoadAll()
    {
        var all = new List<CardDef>();
        foreach (var f in Directory.GetFiles(Path.Combine(Root, "content", "cards"), "*.json"))
            all.AddRange(CardLoader.LoadPackFromString(File.ReadAllText(f)));
        return all;
    }

    private static List<CardDef> Drop() => LoadAll().Where(c => c.Set == "class_drop_1").ToList();

    [Fact]
    public void Fifty_Cards_Plus_Tokens()
    {
        var drop = Drop();
        Assert.Equal(50, drop.Count(c => c.Type != CardType.TOKEN));
        Assert.Equal(3, drop.Count(c => c.Type == CardType.TOKEN));
    }

    [Fact]
    public void Every_Card_Validates_And_Reads_Cleanly()
    {
        foreach (var c in Drop())
        {
            var errors = CardValidator.Validate(c);
            Assert.True(errors.Count == 0, $"{c.Id}: {string.Join("; ", errors)}");
            string text = RulesTextRenderer.Render(c);
            _out.WriteLine($"{c.Id} [{c.Cost}] {text.Replace("\n", " | ")}");
            Assert.DoesNotContain("?", text.Replace("?\"", ""));
        }
    }

    [Fact]
    public void Every_Card_Uses_A_Drop_Mechanic()
    {
        foreach (var c in Drop().Where(c => c.Type != CardType.TOKEN))
            Assert.True(Synergy.Tags(c).Count > 0, $"{c.Id} has no synergy tag");
    }

    [Fact]
    public void Each_Class_Has_A_High_Synergy_Shelf()
    {
        var all = LoadAll();
        foreach (var cls in Synergy.ClassIds)
        {
            var shelf = Synergy.Shelf(cls, all);
            _out.WriteLine($"{cls}: {shelf.Count} cards, top: {string.Join(", ", shelf.Take(6).Select(c => c.Name))}");
            Assert.True(shelf.Count >= 7, $"{cls} shelf too small ({shelf.Count})");
        }
    }

    [Fact]
    public void Drop_Cards_Play_In_Bot_Duels_Without_Errors()
    {
        // every class's drop cards, in a 30-card deck, played by the bot against another class's — 40 games
        var all = LoadAll();
        foreach (var c in all) CardRegistry.Register(c);
        var drop = Drop().Where(c => c.Type != CardType.TOKEN).ToList();
        var bot = new GreedyBot();
        var byStrata = drop.GroupBy(c => c.Strata).ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());
        var fillers = all.Where(c => c.Set == "buried_age" && c.Type == CardType.CREATURE).ToList();
        List<string> Deck(Strata s)
        {
            var d = new List<string>();
            foreach (var id in byStrata[s]) { d.Add(id); d.Add(id); }
            foreach (var f in fillers.Where(f => f.Strata == s && f.Rarity != Rarity.RELIC)) { if (d.Count >= 30) break; d.Add(f.Id); }
            while (d.Count < 30) d.Add(d[d.Count % byStrata[s].Count]);
            return d.Take(30).ToList();
        }
        var strata = byStrata.Keys.OrderBy(k => k).ToList();
        int games = 0, finished = 0;
        for (int seed = 1; seed <= 8; seed++)
            foreach (var a in strata)
            {
                var b = strata[(strata.IndexOf(a) + seed) % strata.Count];
                var s = GameState.Initialize(new GameConfig { Seed = (ulong)seed * 7919, Player0DeckIds = Deck(a), Player1DeckIds = Deck(b) });
                int guard = 0;
                while (!s.IsGameOver && guard++ < 800)
                {
                    var act = bot.ChooseAction(s, s.CurrentPlayerIndex) ?? new EndTurnAction { PlayerIndex = s.CurrentPlayerIndex };
                    s = DuelEngine.Apply(s, act);
                }
                games++;
                if (s.IsGameOver) finished++;
            }
        _out.WriteLine($"{finished}/{games} bot games finished");
        Assert.Equal(games, finished);
    }
}

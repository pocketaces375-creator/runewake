using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;
using Xunit.Abstractions;

namespace Runewake.Tests.Cards;

/// <summary>
/// FABLE-SKILLS-1: every keyword a card carries is explained wherever the card is shown, and every
/// keyword actually does something on the card that carries it. Trikzos: "I understand echo and reach
/// as to be skills. If they are, they should be explained." Echo was printed on 17 cards that had
/// nothing for it to repeat.
/// </summary>
public class KeywordExplanationTests
{
    private readonly ITestOutputHelper _out;
    public KeywordExplanationTests(ITestOutputHelper output) { _out = output; }

    private static string Root => Environment.GetEnvironmentVariable("RUNEWAKE_ROOT")
        ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static List<CardDef> LoadAll()
    {
        var all = new List<CardDef>();
        foreach (var f in Directory.GetFiles(Path.Combine(Root, "content", "cards"), "*.json"))
            all.AddRange(CardLoader.LoadPackFromString(File.ReadAllText(f)));
        return all;
    }

    [Fact]
    public void Every_keyword_on_every_card_is_explained()
    {
        foreach (var c in LoadAll())
            foreach (var kw in c.Keywords)
                Assert.False(string.IsNullOrEmpty(RulesTextRenderer.KeywordReminder(kw, c.Type)), $"{c.Id}: no explanation for {kw}");
    }

    [Fact]
    public void Explanations_match_the_rules()
    {
        Assert.Contains("beside", RulesTextRenderer.KeywordReminder("REACH"));
        Assert.Contains("destroyed", RulesTextRenderer.KeywordReminder("VENOM"));
        Assert.Contains("twice", RulesTextRenderer.KeywordReminder("ECHO"));
        Assert.Contains("Can't attack", RulesTextRenderer.KeywordReminder("ROOTED"));
        Assert.Equal("Takes 2 less damage from every hit.", RulesTextRenderer.KeywordReminder("ARMOR:2"));
        Assert.Equal("25% chance to take no combat damage.", RulesTextRenderer.KeywordReminder("DODGE:25"));
        var lines = RulesTextRenderer.KeywordReminderLines(new CardDef { Id = "x", Name = "X", Keywords = new() { "REACH" } });
        Assert.Equal("Reach: Can attack the lane opposite it or either lane beside that one.", lines.Single());
    }

    [Fact]
    public void Echo_always_has_something_to_repeat()
    {
        foreach (var c in LoadAll().Where(c => c.Keywords.Contains("ECHO")))
        {
            _out.WriteLine($"{c.Id}: {RulesTextRenderer.Render(c).Replace("\n", " | ")}");
            Assert.True(c.Type == CardType.CREATURE || c.Type == CardType.TOKEN, $"{c.Id}: Echo only works on creatures");
            Assert.True(c.Abilities.Any(a => a.Trigger == Trigger.ON_SUMMON), $"{c.Id}: Echo with no 'when this enters play' effect does nothing");
        }
    }

    [Fact]
    public void Echo_entry_effect_really_happens_twice()
    {
        var all = LoadAll();
        foreach (var c in all) CardRegistry.Register(c);
        // Foam Runner: "When this enters play, you heal 1." — twice with Echo.
        var deck = Enumerable.Repeat("tid_c_foam_runner", 30).ToList();
        var s = GameState.Initialize(new GameConfig { Seed = 7, Player0DeckIds = deck, Player1DeckIds = deck });
        var me = s.Player(s.CurrentPlayerIndex);
        me.Vigor = 10;
        me.Attunement = Math.Max(me.Attunement, 1);
        var runner = me.Hand.First(h => h.CardDefId == "tid_c_foam_runner");
        s = DuelEngine.Apply(s, new PlayCardAction { PlayerIndex = s.CurrentPlayerIndex, CardInstanceId = runner.InstanceId, Cost = 1, LaneIndex = 0 });
        Assert.Equal(12, s.Player(s.CurrentPlayerIndex).Vigor);
    }
}

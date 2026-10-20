using System.Linq;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.State;

/// <summary>FABLE-075: the boon picked at the start of a campaign fight does exactly what its card says.</summary>
public class BattleBoonsTests
{
    private static GameState Fresh()
    {
        var s = new GameState(seed: 7);
        var p = s.Players[0];
        p.MaxVigor = 25; p.Vigor = 25; p.AttunementMax = 1; p.Attunement = 1;
        for (int i = 0; i < 10; i++) p.Deck.Add(new CardInstance(100 + i, "c" + i, 0) { Zone = Zone.Deck });
        for (int i = 0; i < 4; i++) p.Hand.Add(new CardInstance(200 + i, "h" + i, 0) { Zone = Zone.Hand });
        return s;
    }

    [Fact]
    public void Endurance_raises_vigor_and_its_ceiling()
    {
        var s = Fresh();
        int foe = s.Players[1].Vigor;
        Assert.True(BattleBoons.Apply(s, 0, "vigor"));
        Assert.Equal(25 + BattleBoons.VigorAmount, s.Players[0].Vigor);
        Assert.Equal(25 + BattleBoons.VigorAmount, s.Players[0].MaxVigor);
        Assert.Equal(foe, s.Players[1].Vigor);   // the foe is untouched
    }

    [Fact]
    public void Insight_draws_from_the_top_of_the_deck()
    {
        var s = Fresh();
        var top = s.Players[0].Deck.Take(BattleBoons.InsightCards).Select(c => c.InstanceId).ToList();
        Assert.True(BattleBoons.Apply(s, 0, "insight"));
        Assert.Equal(4 + BattleBoons.InsightCards, s.Players[0].Hand.Count);
        Assert.Equal(10 - BattleBoons.InsightCards, s.Players[0].Deck.Count);
        Assert.All(top, id => Assert.Contains(s.Players[0].Hand, c => c.InstanceId == id && c.Zone == Zone.Hand));
    }

    [Fact]
    public void Insight_never_overfills_the_hand()
    {
        var s = Fresh();
        s.Players[0].MaxHandSize = 5;
        BattleBoons.Apply(s, 0, "insight");
        Assert.Equal(5, s.Players[0].Hand.Count);
    }

    [Fact]
    public void Attunement_lifts_this_turn_and_every_turn_after()
    {
        var s = Fresh();
        Assert.True(BattleBoons.Apply(s, 0, "attune"));
        Assert.Equal(1 + BattleBoons.AttunementAmount, s.Players[0].Attunement);
        Assert.Equal(1 + BattleBoons.AttunementAmount, s.Players[0].AttunementMax);
    }

    [Fact]
    public void Unknown_boons_change_nothing()
    {
        var s = Fresh();
        Assert.False(BattleBoons.Apply(s, 0, "nope"));
        Assert.Equal(25, s.Players[0].Vigor);
        Assert.Equal(4, s.Players[0].Hand.Count);
        Assert.Equal(3, BattleBoons.All.Count);
    }
}

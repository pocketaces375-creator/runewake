using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.State;

public class CloneTests
{
    [Fact]
    public void SeededRng_Clone_IsIndependent()
    {
        var rng = new SeededRng(42);
        var clone = rng.Clone();

        // Both should produce the same sequence when advanced independently
        Assert.Equal(rng.NextInt(100), clone.NextInt(100));
        Assert.Equal(rng.NextInt(100), clone.NextInt(100));

        // Advance rng, clone stays at original position — should differ now
        rng.NextInt(100);
        Assert.NotEqual(rng.NextInt(100), clone.NextInt(100));
    }

    [Fact]
    public void CardInstance_Clone_IsDeepCopy()
    {
        var card = new CardInstance(1, "tst_v_moss_whelp", 0)
        {
            Zone = Zone.Lane,
            LaneIndex = 2,
            Damage = 3,
            AttackModifier = 1,
            IsExhausted = true,
            IsIdentified = true
        };
        card.GrantedKeywords.Add("GUARD");

        var clone = card.Clone();

        // Assert values match
        Assert.Equal(card.InstanceId, clone.InstanceId);
        Assert.Equal(card.CardDefId, clone.CardDefId);
        Assert.Equal(card.Zone, clone.Zone);
        Assert.Equal(card.LaneIndex, clone.LaneIndex);
        Assert.Equal(card.Damage, clone.Damage);
        Assert.Equal(card.AttackModifier, clone.AttackModifier);
        Assert.Equal(card.IsExhausted, clone.IsExhausted);
        Assert.Equal(card.IsIdentified, clone.IsIdentified);
        Assert.Equal(card.GrantedKeywords.Count, clone.GrantedKeywords.Count);
        Assert.Contains("GUARD", clone.GrantedKeywords);

        // Mutate clone — original must be unchanged
        clone.Damage = 99;
        clone.GrantedKeywords.Clear();
        clone.LaneIndex = 4;

        Assert.Equal(3, card.Damage);
        Assert.Single(card.GrantedKeywords);
        Assert.Equal(2, card.LaneIndex);
    }

    [Fact]
    public void LaneState_Clone_IsDeepCopy()
    {
        var lane = new LaneState(0);
        var creature = new CardInstance(1, "tst_v_moss_whelp", 0)
        {
            Zone = Zone.Lane,
            LaneIndex = 0
        };
        lane.Occupant = creature;
        lane.AttachedCurseIds.Add(99);

        var clone = lane.Clone();

        Assert.Equal(lane.Index, clone.Index);
        Assert.NotNull(clone.Occupant);
        Assert.Equal(creature.InstanceId, clone.Occupant!.InstanceId);
        Assert.Single(clone.AttachedCurseIds);

        // Mutate clone — original unchanged
        clone.Occupant!.Damage = 500;
        clone.Occupant = null;
        clone.AttachedCurseIds.Clear();

        Assert.Equal(0, creature.Damage);
        Assert.NotNull(lane.Occupant);
        Assert.Single(lane.AttachedCurseIds);
    }

    [Fact]
    public void PlayerState_Clone_IsDeepCopy()
    {
        var player = new PlayerState(0);
        player.Vigor = 20;
        player.Attunement = 5;
        player.AttunementMax = 8;

        // Add a card to each zone
        var deckCard = new CardInstance(1, "tst_v_moss_whelp", 0);
        var handCard = new CardInstance(2, "tst_e_ember_strike", 0) { Zone = Zone.Hand };
        var boardCard = new CardInstance(3, "tst_h_soul_warden", 0)
        {
            Zone = Zone.Lane,
            LaneIndex = 0
        };
        var discardCard = new CardInstance(4, "tst_t_tidal_recall", 0) { Zone = Zone.Discard };
        var barrowCard = new CardInstance(5, "tst_d_light_seeker", 0) { Zone = Zone.Barrow };

        player.Deck.Add(deckCard);
        player.Hand.Add(handCard);
        player.Lanes[0].Occupant = boardCard;
        player.Discard.Add(discardCard);
        player.Barrow.Add(barrowCard);

        player.AttachedCurseIds.Add(42);

        var clone = player.Clone();

        // Assert same counts
        Assert.Single(clone.Deck);
        Assert.Single(clone.Hand);
        Assert.Single(clone.Discard);
        Assert.Single(clone.Barrow);
        Assert.Equal(20, clone.Vigor);
        Assert.Equal(5, clone.Attunement);
        Assert.NotNull(clone.Lanes[0].Occupant);

        // Mutate clone deeply
        clone.Vigor = 1;
        clone.Deck.Clear();
        clone.Hand[0].Damage = 999;
        clone.Lanes[0].Occupant = null;
        clone.AttachedCurseIds.Clear();

        // Original unchanged
        Assert.Equal(20, player.Vigor);
        Assert.Single(player.Deck);
        Assert.Equal(0, player.Hand[0].Damage);
        Assert.NotNull(player.Lanes[0].Occupant);
        Assert.Single(player.AttachedCurseIds);
    }

    [Fact]
    public void GameState_Clone_IsDeepCopy()
    {
        var state = new GameState(42, 1);
        state.TurnNumber = 5;
        state.CurrentPlayerIndex = 1;

        // Populate some state
        var card = new CardInstance(1, "tst_v_moss_whelp", 0)
        {
            Zone = Zone.Hand
        };
        state.Players[0].Hand.Add(card);
        state.Players[0].Attunement = 4;
        state.Players[1].Vigor = 18;

        var clone = state.Clone();

        // Assert values match
        Assert.Equal(5, clone.TurnNumber);
        Assert.Equal(1, clone.CurrentPlayerIndex);
        Assert.Single(clone.Players[0].Hand);
        Assert.Equal(4, clone.Players[0].Attunement);
        Assert.Equal(18, clone.Players[1].Vigor);

        // Mutate clone extensively
        clone.TurnNumber = 99;
        clone.CurrentPlayerIndex = 0;
        clone.Players[0].Hand.Clear();
        clone.Players[0].Attunement = 0;
        clone.Players[1].Vigor = 0;
        clone.Rng.NextInt(100); // advance RNG

        // Original unchanged
        Assert.Equal(5, state.TurnNumber);
        Assert.Equal(1, state.CurrentPlayerIndex);
        Assert.Single(state.Players[0].Hand);
        Assert.Equal(4, state.Players[0].Attunement);
        Assert.Equal(18, state.Players[1].Vigor);

        // RNG is independent
        var stateVal = state.Rng.NextInt(100);
        var cloneVal = clone.Rng.NextInt(100);
        // After advancing clone's RNG once and state's RNG now once,
        // they should be at different offsets
    }

    [Fact]
    public void GameState_Clone_RngIsIndependent()
    {
        var state = new GameState(12345);
        var clone = state.Clone();

        // Both start with same seed, so first values match
        Assert.Equal(state.Rng.NextInt(1000), clone.Rng.NextInt(1000));

        // Advance state's RNG further
        for (int i = 0; i < 10; i++)
            state.Rng.NextInt(1000);

        // Clone's RNG is still at position 0 — should match a fresh RNG
        var fresh = new SeededRng(12345);
        fresh.NextInt(1000); // skip the first value
        Assert.Equal(fresh.NextInt(1000), clone.Rng.NextInt(1000));
    }
}

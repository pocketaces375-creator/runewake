using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.State;

/// <summary>FABLE-075: Shards are the one coin; dig charges are paid as shards.</summary>
public class OneCoinTests
{
    [Fact]
    public void Rune_dust_is_swept_into_shards_once()
    {
        var p = new ProgressionState { Shards = 100, RuneDust = 20 };
        Assert.Equal(20 * ProgressionState.ShardsPerRuneDust, p.MergeRuneDustIntoShards());
        Assert.Equal(100 + 20 * ProgressionState.ShardsPerRuneDust, p.Shards);
        Assert.Equal(0, p.RuneDust);
        Assert.Equal(0, p.MergeRuneDustIntoShards());            // nothing left to move
        Assert.Equal(100 + 20 * ProgressionState.ShardsPerRuneDust, p.Shards);
    }

    [Fact]
    public void Spending_shards_reports_the_shortfall()
    {
        var p = new ProgressionState { Shards = 30 };
        Assert.False(p.SpendShards(90, out int missing));
        Assert.Equal(60, missing);
        Assert.Equal(30, p.Shards);
        Assert.True(p.SpendShards(30, out missing));
        Assert.Equal(0, missing);
        Assert.Equal(0, p.Shards);
    }

    [Fact]
    public void A_win_pays_old_dig_charges_as_shards()
    {
        var enc = new EncounterDef { ShardReward = 80, DigChargeReward = 1 };
        Assert.Equal(80 + EncounterDef.ShardsPerDigCharge, enc.TotalShardReward);
    }

    [Fact]
    public void Daily_gifts_are_described_in_shards_only()
    {
        var r = new DailyReward(Shards: 10, Runes: 5);
        Assert.Equal(10 + 5 * ProgressionState.ShardsPerRuneDust, r.TotalShards);
        Assert.Equal($"{r.TotalShards} Shards", r.Describe());
        Assert.All(DailyRewards.LoginCalendar, d => Assert.Equal(0, d.DigCharges));
        Assert.All(DailyRewards.MatchTiers, t => Assert.Equal(0, t.Reward.DigCharges));
    }
}
